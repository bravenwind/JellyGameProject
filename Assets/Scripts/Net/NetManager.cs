using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// Unity 쪽 입구. 호스트/클라를 만들고, 매 프레임 Poll()을 돌린다.
    ///
    /// ★ 여기가 Photon의 PhotonNetwork 자리를 대신한다.
    ///   게임 코드는 이 클래스만 보면 되고, 소켓 세부는 아래 계층에 숨는다.
    /// </summary>
    public class NetManager : MonoBehaviour
    {
        public enum Mode { None, Host, Client }

        public static NetManager Instance { get; private set; }

        [Header("설정")]
        public int port = NetConfig.DefaultPort;
        public string joinIp = "127.0.0.1";
        public int maxLogLines = 200;

        public Mode CurrentMode { get; private set; }
        public NetHost Host { get; private set; }
        public NetClient Client { get; private set; }

        /// <summary>내 플레이어 번호. 호스트는 1, 참가자는 호스트가 발급한 값.</summary>
        public int MyId
        {
            get
            {
                if (CurrentMode == Mode.Host) return NetHost.HostId;
                if (CurrentMode == Mode.Client && Client != null) return Client.MyId;
                return 0;
            }
        }

        /// <summary>내가 호스트인가. Photon의 IsMasterClient에 해당 — 판정 권한의 기준.</summary>
        public bool IsHost { get { return CurrentMode == Mode.Host; } }

        readonly List<string> _log = new List<string>();
        public IReadOnlyList<string> LogLines { get { return _log; } }

        // ─────────────────────────────────────────────
        //  게임 계층(NetWorld 등)이 구독하는 이벤트
        //  네트워크 계층은 "무슨 일이 있었다"만 알리고, 게임 처리는 위에서 한다.
        // ─────────────────────────────────────────────
        public event Action OnHostStarted;
        public event Action<NetHost.Peer> OnPeerJoined;
        public event Action<NetHost.Peer> OnPeerLeft;
        public event Action<NetHost.Peer, MsgType, NetReader> OnHostMessage;
        public event Action<MsgType, NetReader> OnClientMessage;
        public event Action OnDisconnected;

        // ─────────────────────────────────────────────
        [Header("씬 전환")]
        [Tooltip("씬이 바뀌어도 연결을 유지한다. Main 씬에서 접속해 게임 씬으로 넘어가려면 켜야 한다.")]
        public bool persistAcrossScenes = true;

        void Awake()
        {
            // ★ 중복 방지가 여기서 특히 중요해졌다.
            //   DontDestroyOnLoad로 살아남은 NetManager가 있는데 게임 씬의 LanNet에도
            //   NetManager가 붙어 있으면 둘이 공존한다. 그러면 Instance가 나중 것으로
            //   바뀌면서 <b>이미 열려 있는 소켓을 아무도 안 들고 있게 된다</b>.
            //   먼저 있던 쪽(= 소켓을 들고 있는 쪽)을 살린다.
            //
            //   ★ gameObject가 아니라 컴포넌트만 지운다.
            //     게임 씬의 LanNet에는 NetWorld·AbsorbMode·LanGameFlow가 함께 붙어 있다.
            //     오브젝트째 지우면 그것들까지 사라져 게임이 통째로 죽는다.
            //     중복인 건 NetManager 하나뿐이므로 그것만 걷어내면 된다.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Net] NetManager가 둘입니다. '" + name + "' 쪽을 걷어냅니다. "
                    + "(살아남는 쪽: '" + Instance.name + "')");
                Destroy(this);
                return;
            }
            Instance = this;

            if (persistAcrossScenes)
            {
                // DontDestroyOnLoad는 루트 오브젝트에만 걸린다.
                // 자식으로 두면 조용히 무시되고, 씬이 바뀌는 순간 소켓째로 사라진다.
                if (transform.parent != null)
                {
                    Debug.LogWarning("[Net] NetManager('" + name + "')가 다른 오브젝트의 자식입니다. "
                        + "씬을 넘어 살아남으려면 루트에 있어야 해서 부모에서 떼어냅니다.");
                    transform.SetParent(null, true);
                }
                DontDestroyOnLoad(gameObject);
            }

            // ★ 매우 중요: 에디터/빌드 창이 포커스를 잃어도 Update가 계속 돌게 한다.
            //   이걸 안 켜면 두 인스턴스 테스트 때 뒤에 있는 창이 멈춰서
            //   "갑자기 연결이 끊긴 것처럼" 보인다(폴링이 멈추므로).
            Application.runInBackground = true;
        }

        void Update()
        {
            // 폴링: 스레드가 없으므로 여기서 네트워크를 돌린다
            if (Host != null) Host.Poll();
            if (Client != null) Client.Poll();
        }

        void OnApplicationQuit() { Shutdown(); }

        /// <summary>
        /// ★ Instance == this 검사를 빼야 한다.
        ///
        ///   예전 코드는 "대표(Instance)일 때만" 소켓을 닫았다. 그런데 대표가 아닌
        ///   NetManager도 소켓을 들고 있을 수 있다 — 씬을 넘나들며 대표가 바뀌면
        ///   <b>포트를 쥔 채 아무도 안 닫는 인스턴스</b>가 남는다.
        ///   그 상태로 방을 다시 열면 "포트 7777가 이미 사용 중"이 뜬다.
        ///   범인이 같은 프로세스 안에 있어서 프로그램을 껐다 켜야만 풀렸다.
        ///
        ///   자기가 연 것은 자기가 닫는다 — 대표든 아니든.
        /// </summary>
        void OnDestroy()
        {
            Shutdown();
            if (Instance == this) Instance = null;
        }

        // ─────────────────────────────────────────────
        //  시작 / 종료
        // ─────────────────────────────────────────────
        public void StartHost()
        {
            Shutdown();

            Host = new NetHost();
            Host.OnLog = AddLog;
            Host.OnPeerJoined = RaisePeerJoined;
            Host.OnPeerLeft = RaisePeerLeft;
            Host.OnMessage = RaiseHostMessage;

            if (!Host.Start(port))
            {
                Host = null;
                return;
            }

            CurrentMode = Mode.Host;
            AddLog("== 호스트 모드 ==  내 번호: P" + NetHost.HostId);

            foreach (string ip in NetUtil.GetLocalIPv4List())
                AddLog("  다른 기기에서 접속: " + ip + ":" + port);

            if (OnHostStarted != null) OnHostStarted();
        }

        public void JoinHost()
        {
            Shutdown();

            Client = new NetClient();
            Client.OnLog = AddLog;
            Client.OnMessage = RaiseClientMessage;

            if (!Client.Connect(joinIp, port))
            {
                Client = null;
                return;
            }

            CurrentMode = Mode.Client;
            AddLog("== 참가 모드 ==");
        }

        public void Shutdown()
        {
            bool wasConnected = (Host != null || Client != null);

            if (Host != null) { Host.Stop(); Host = null; }
            if (Client != null) { Client.Disconnect(); Client = null; }
            CurrentMode = Mode.None;

            if (wasConnected && OnDisconnected != null) OnDisconnected();
        }

        // 이벤트 중계 (메서드로 두면 델리게이트가 한 번만 만들어져 할당이 적다)
        void RaisePeerJoined(NetHost.Peer p) { if (OnPeerJoined != null) OnPeerJoined(p); }
        void RaisePeerLeft(NetHost.Peer p) { if (OnPeerLeft != null) OnPeerLeft(p); }
        void RaiseHostMessage(NetHost.Peer p, MsgType t, NetReader r) { if (OnHostMessage != null) OnHostMessage(p, t, r); }
        void RaiseClientMessage(MsgType t, NetReader r) { if (OnClientMessage != null) OnClientMessage(t, r); }

        // ─────────────────────────────────────────────
        //  테스트용 동작
        // ─────────────────────────────────────────────
        public void SendPing()
        {
            if (CurrentMode == Mode.Client) Client.SendPing();
            else AddLog("핑은 참가자 쪽에서 호스트로 보냅니다.");
        }

        public void SendChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (CurrentMode == Mode.Client)
            {
                Client.SendChat(text);
            }
            else if (CurrentMode == Mode.Host)
            {
                // 호스트는 자기 자신에게 보낼 소켓이 없으므로 바로 로그에 찍고 전원에게 중계
                AddLog("P" + NetHost.HostId + ": " + text);
                Host.SendChat(text);
            }
        }

        // ─────────────────────────────────────────────
        public void AddLog(string line)
        {
            _log.Add(line);
            if (_log.Count > maxLogLines) _log.RemoveAt(0);
            Debug.Log("[Net] " + line);
        }

        public void ClearLog() { _log.Clear(); }
    }
}
