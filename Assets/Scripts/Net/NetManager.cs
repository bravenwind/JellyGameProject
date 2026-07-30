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

        readonly List<string> _log = new List<string>();
        public IReadOnlyList<string> LogLines { get { return _log; } }

        // ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

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
        void OnDestroy() { if (Instance == this) Shutdown(); }

        // ─────────────────────────────────────────────
        //  시작 / 종료
        // ─────────────────────────────────────────────
        public void StartHost()
        {
            Shutdown();

            Host = new NetHost();
            Host.OnLog = AddLog;

            if (!Host.Start(port))
            {
                Host = null;
                return;
            }

            CurrentMode = Mode.Host;
            AddLog("== 호스트 모드 ==  내 번호: P" + NetHost.HostId);

            foreach (string ip in NetUtil.GetLocalIPv4List())
                AddLog("  다른 기기에서 접속: " + ip + ":" + port);
        }

        public void JoinHost()
        {
            Shutdown();

            Client = new NetClient();
            Client.OnLog = AddLog;

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
            if (Host != null) { Host.Stop(); Host = null; }
            if (Client != null) { Client.Disconnect(); Client = null; }
            CurrentMode = Mode.None;
        }

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
