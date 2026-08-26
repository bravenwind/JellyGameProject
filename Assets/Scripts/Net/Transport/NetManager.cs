using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetManager : MonoBehaviour
    {
        public enum Mode { None, Host, Client }

        public static NetManager Instance { get; private set; }

        [Header("설정")]
        [SerializeField] private int port = NetConfig.DEFAULT_PORT;
        public int Port { get { return port; } set { port = value; } }
        [SerializeField] private string joinIp = "127.0.0.1";
        public string JoinIp { get { return joinIp; } set { joinIp = value; } }
        [SerializeField] private int maxLogLines = 200;

        public Mode CurrentMode { get; private set; }
        public NetHost Host { get; private set; }
        public NetClient Client { get; private set; }

        public int MyId
        {
            get
            {
                if (CurrentMode == Mode.Host)
                    return NetHost.HOST_ID;
                if (CurrentMode == Mode.Client && Client != null)
                    return Client.MyId;
                return 0;
            }
        }

        public bool IsHost { get { return CurrentMode == Mode.Host; } }

        /// <summary>
        /// 네트워크가 없는 상태. 호스트도 클라도 아니다.
        ///
        /// 세 가지 경우에 참이 된다.
        ///   ① 아직 방을 만들거나 접속하지 않음 (메인·로비 화면)
        ///   ② Shutdown() 이후 — 판이 끝나 소켓을 닫았지만 커튼 애니메이션 동안
        ///      게임 씬은 아직 살아서 Update가 돈다. 판마다 반드시 지나가는 구간이다
        ///   ③ StartHost/JoinHost가 실패함 (포트 충돌, 접속 실패)
        ///
        /// 이 값을 보는 코드는 두 부류다.
        ///   · 전송을 멈춘다  — Host/Client가 null이라 그냥 두면 NullReference
        ///   · 로컬로 처리한다 — 아무도 시뮬레이션을 안 돌리면 전부 얼어붙는다
        /// </summary>
        public static bool Offline
        {
            get
            {
                NetManager net = Instance;
                return net == null || net.CurrentMode == Mode.None;
            }
        }

        private readonly List<string> log = new List<string>();
        public event Action OnHostStarted;
        public event Action<NetHost.Peer> OnPeerJoined;
        public event Action<NetHost.Peer> OnPeerLeft;
        public event Action OnDisconnected;

        //호스트가 강제 종료 등으로 사라진 경우. 정상 종료(Shutdown)와 구분해야
        //"서버와 연결이 끊겼습니다"를 띄울지 조용히 나갈지 판단할 수 있다
        public event Action OnConnectionLost;

        public bool ConnectionLost { get; private set; }

        /// <summary>StartHost/JoinHost가 실패한 이유. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public string LastError { get; private set; }

        [Header("씬 전환")]
        [Tooltip("씬이 바뀌어도 연결을 유지한다. Main 씬에서 접속해 게임 씬으로 넘어가려면 켜야 한다.")]
        [SerializeField] private bool persistAcrossScenes = true;

        private void Awake()
        {
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
                if (transform.parent != null)
                {
                    Debug.LogWarning("[Net] NetManager('" + name + "')가 다른 오브젝트의 자식입니다. "
                        + "씬을 넘어 살아남으려면 루트에 있어야 해서 부모에서 떼어냅니다.");
                    transform.SetParent(null, true);
                }
                DontDestroyOnLoad(gameObject);
            }

            Application.runInBackground = true;
        }

        private void Update()
        {
            if (Host != null)
                Host.Poll();

            if (Client == null)
                return;

            Client.Poll();

            if (ConnectionLost || Client.Connected)
                return;

            ConnectionLost = true;
            AddLog("호스트와의 연결이 끊어졌습니다.");

            OnConnectionLost?.Invoke();
        }

        private void OnApplicationQuit() { Shutdown(); }

        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 방을 연다. 실패하면 false — 호출부가 반드시 확인해야 한다.
        ///
        /// ★ 예전엔 void였다
        ///   포트가 이미 쓰이는 중이면 조용히 return했고, 로비는 그걸 모른 채
        ///   대기 화면으로 넘어가 영원히 "다른 참가자를 기다리는 중"을 띄웠다.
        ///   실패를 반환값으로 표현할 수 없으면 호출부는 실패를 무시하게 된다.
        /// </summary>
        public bool StartHost()
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
                LastError = "포트 " + port + " 를 열 수 없습니다. 다른 게임이 켜져 있는지 확인해주세요.";
                return false;
            }

            CurrentMode = Mode.Host;
            AddLog("== 호스트 모드 ==  내 번호: P" + NetHost.HOST_ID);

            foreach (string ip in NetUtil.GetLocalIPv4List())
                AddLog("  다른 기기에서 접속: " + ip + ":" + port);

            OnHostStarted?.Invoke();
            return true;
        }

        /// <summary>방에 붙는다. 실패하면 false — IP 오타·방이 닫힘 등.</summary>
        public bool JoinHost()
        {
            Shutdown();

            Client = new NetClient();
            Client.OnLog = AddLog;
            Client.OnMessage = RaiseClientMessage;

            if (!Client.Connect(joinIp, port))
            {
                Client = null;
                LastError = joinIp + ":" + port + " 에 접속하지 못했습니다. 주소를 확인해주세요.";
                return false;
            }

            CurrentMode = Mode.Client;
            AddLog("== 참가 모드 ==");
            return true;
        }

        public void Shutdown()
        {
            bool wasConnected = (Host != null || Client != null);

            ConnectionLost = false;

            if (Host != null)
            {
                Host.Stop();
                Host = null;
            }
            if (Client != null)
            {
                Client.Disconnect();
                Client = null;
            }
            CurrentMode = Mode.None;

            if (wasConnected)
                OnDisconnected?.Invoke();
        }

        private void RaisePeerJoined(NetHost.Peer peer)
        {
            OnPeerJoined?.Invoke(peer);
        }

        private void RaisePeerLeft(NetHost.Peer peer)
        {
            OnPeerLeft?.Invoke(peer);
        }

        // ─────────────────────────────────────────────────────────
        //  메시지 라우팅 테이블
        // ─────────────────────────────────────────────────────────
        //
        // ★ 왜 이벤트 브로드캐스트로는 부족한가
        //   OnHostMessage/OnClientMessage는 멀티캐스트라 구독자 전원이 같은 메시지를
        //   순서대로 받는다. 문제가 셋 있었다.
        //
        //     1. MsgType 하나를 추가하면 NetWorld·AbsorbMode·LanGameFlow 중
        //        어디 switch에 넣을지 매번 골라야 하고, 아무 데도 안 넣어도 조용하다.
        //     2. 두 구독자가 같은 타입을 읽으면 NetReader를 공유하므로 두 번째는
        //        위치가 밀린 채 쓰레기를 읽는다. 예외도 안 난다.
        //     3. 어떤 타입을 누가 담당하는지 코드 어디에도 안 적혀 있다.
        //
        //   타입당 주인을 하나로 못 박으면 셋 다 사라진다. 중복 등록은 그 자리에서
        //   에러로 잡히고, 주인 없는 타입은 로그에 남는다.
        private readonly Dictionary<MsgType, Action<NetHost.Peer, NetReader>> hostRoutes
            = new Dictionary<MsgType, Action<NetHost.Peer, NetReader>>();

        private readonly Dictionary<MsgType, Action<NetReader>> clientRoutes
            = new Dictionary<MsgType, Action<NetReader>>();

        /// <summary>클라가 호스트로 보낸 메시지 한 종류의 처리를 맡는다.</summary>
        public void RouteHost(MsgType type, Action<NetHost.Peer, NetReader> handler)
        {
            if (handler == null)
                return;

            if (hostRoutes.ContainsKey(type))
            {
                Debug.LogError("[NetManager] 호스트 메시지 " + type + " 의 주인이 이미 있습니다. "
                    + "한 타입은 한 곳에서만 처리해야 합니다.");
                return;
            }

            hostRoutes[type] = handler;
        }

        /// <summary>호스트가 클라로 보낸 메시지 한 종류의 처리를 맡는다.</summary>
        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            if (handler == null)
                return;

            if (clientRoutes.ContainsKey(type))
            {
                Debug.LogError("[NetManager] 클라 메시지 " + type + " 의 주인이 이미 있습니다. "
                    + "한 타입은 한 곳에서만 처리해야 합니다.");
                return;
            }

            clientRoutes[type] = handler;
        }

        //씬을 나갈 때 반드시 풀어야 한다. 안 그러면 파괴된 오브젝트의 메서드가 남아
        //다음 판에서 "주인이 이미 있습니다" 에러가 뜬다
        public void UnrouteHost(MsgType type)
        {
            hostRoutes.Remove(type);
        }

        public void UnrouteClient(MsgType type)
        {
            clientRoutes.Remove(type);
        }

        private void RaiseHostMessage(NetHost.Peer peer, MsgType type, NetReader reader)
        {
            Action<NetHost.Peer, NetReader> route;
            if (hostRoutes.TryGetValue(type, out route))
            {
                route(peer, reader);
                return;
            }

            AddLog("처리되지 않은 호스트 메시지: " + type);
        }

        private void RaiseClientMessage(MsgType type, NetReader reader)
        {
            Action<NetReader> route;
            if (clientRoutes.TryGetValue(type, out route))
            {
                route(reader);
                return;
            }

            AddLog("처리되지 않은 클라 메시지: " + type);
        }

        public void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > maxLogLines)
                log.RemoveAt(0);
            Debug.Log("[Net] " + line);
        }

    }
}
