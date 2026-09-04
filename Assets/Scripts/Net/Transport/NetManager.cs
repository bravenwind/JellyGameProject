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

        //LAN 전용 진입점(StartHost/JoinHost)이 필요해 구체 타입도 함께 들고 있다.
        //3단계에서 방 만들기·참가가 INetSession 으로 넘어가면 transport 하나만 남는다
        private LanTransport lan;
        private INetTransport transport;

        /// <summary>
        /// ★ 예전엔 필드에 저장했다
        ///   Shutdown 이 소켓을 닫고 CurrentMode 를 None 으로 되돌리는 순서에 의존하는
        ///   코드가 있었고(로비의 취소 처리는 OnDisconnected 안에서 다시 Offline 을 묻는다),
        ///   순서를 한 번 어긋나게 놓으면 Shutdown 이 재귀로 들어갔다.
        ///   전송의 상태에서 매번 유도하면 어긋날 순서 자체가 없어진다.
        /// </summary>
        public Mode CurrentMode
        {
            get
            {
                if (transport == null)
                    return Mode.None;
                if (transport.IsHost)
                    return Mode.Host;
                return transport.IsConnected ? Mode.Client : Mode.None;
            }
        }

        public int MyId { get { return transport != null ? transport.MyId : 0; } }

        public bool IsHost { get { return transport != null && transport.IsHost; } }

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
        ///   · 전송을 멈춘다  — 보낼 곳이 없다(이제 전송이 조용히 버리지만, 쓸데없이 쓰지 않는다)
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
        public event Action<int> OnPeerJoined;
        public event Action<int> OnPeerLeft;
        public event Action OnDisconnected;

        //호스트가 강제 종료 등으로 사라진 경우. 정상 종료(Shutdown)와 구분해야
        //"서버와 연결이 끊겼습니다"를 띄울지 조용히 나갈지 판단할 수 있다
        public event Action OnConnectionLost;

        public bool ConnectionLost { get { return lan != null && lan.ConnectionLost; } }

        /// <summary>StartHost/JoinHost가 실패한 이유. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public string LastError { get { return lan != null ? lan.LastError : null; } }

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

            //전송은 NetManager 와 수명이 같다. 판마다 새로 만들면 접속 전에 걸어둔
            //라우팅(로비의 LoadGameScene 등)과 이벤트 구독이 통째로 사라진다
            lan = new LanTransport();
            lan.OnLog = AddLog;
            lan.OnError = msg => Debug.LogError("[NetManager] " + msg);
            transport = lan;

            //바깥은 NetManager 의 이벤트만 구독한다. 전송을 갈아끼워도 구독이 끊기지 않는다
            transport.OnPeerJoined += RaisePeerJoined;
            transport.OnPeerLeft += RaisePeerLeft;
            transport.OnHostStarted += RaiseHostStarted;
            transport.OnDisconnected += RaiseDisconnected;
            transport.OnConnectionLost += RaiseConnectionLost;
        }

        private void Update()
        {
            transport?.Poll();
        }

        private void OnApplicationQuit() { Shutdown(); }

        private void OnDestroy()
        {
            Shutdown();

            //전송은 이 객체만 들고 있으니 같이 사라지지만, 구독은 건 자리에서 푼다.
            //중복 NetManager가 걷어내질 때(Awake의 Destroy(this)) 이쪽만 살아남는 경우를
            //생각하면 짝을 맞춰두는 편이 안전하다
            if (transport != null)
            {
                transport.OnPeerJoined -= RaisePeerJoined;
                transport.OnPeerLeft -= RaisePeerLeft;
                transport.OnHostStarted -= RaiseHostStarted;
                transport.OnDisconnected -= RaiseDisconnected;
                transport.OnConnectionLost -= RaiseConnectionLost;
            }

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
            return lan.StartHost(port);
        }

        /// <summary>방에 붙는다. 실패하면 false — IP 오타·방이 닫힘 등.</summary>
        public bool JoinHost()
        {
            return lan.JoinHost(joinIp, port);
        }

        public void Shutdown()
        {
            transport?.Shutdown();
        }

        // ─────────────────────────────────────────────────────────
        //  전송 위임 — 바깥은 NetManager.Instance 만 보고 말한다
        // ─────────────────────────────────────────────────────────
        //
        //호스트가 아닌데 Broadcast 를 부르는 등 상태에 맞지 않는 호출은 전송이 조용히 버린다.
        //예전엔 Host/Client 를 직접 만져 판이 끝난 뒤 커튼 구간에서 NullReference 가 났다

        public void Broadcast(NetWriter w)
        {
            transport?.Broadcast(w);
        }

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            transport?.BroadcastExcept(exceptPeerId, w);
        }

        public void SendTo(int peerId, NetWriter w)
        {
            transport?.SendTo(peerId, w);
        }

        public void SendToHost(NetWriter w)
        {
            transport?.SendToHost(w);
        }

        public int PeerCount { get { return transport != null ? transport.PeerCount : 0; } }

        public bool AcceptingNewPeers
        {
            get { return transport != null && transport.AcceptingNewPeers; }
            set { if (transport != null) transport.AcceptingNewPeers = value; }
        }

        private void RaisePeerJoined(int peerId)
        {
            OnPeerJoined?.Invoke(peerId);
        }

        private void RaisePeerLeft(int peerId)
        {
            OnPeerLeft?.Invoke(peerId);
        }

        private void RaiseHostStarted()
        {
            OnHostStarted?.Invoke();
        }

        private void RaiseDisconnected()
        {
            OnDisconnected?.Invoke();
        }

        private void RaiseConnectionLost()
        {
            OnConnectionLost?.Invoke();
        }

        // ─────────────────────────────────────────────────────────
        //  메시지 라우팅 — 전송이 표를 들고 있다
        // ─────────────────────────────────────────────────────────
        //
        //표가 전송 쪽에 있는 이유는 수명 때문이다. 라우팅은 접속보다 먼저 걸리고
        //(로비는 Start 에서 LoadGameScene 을 등록하고 한참 뒤에 참가한다) 판이 끝나도
        //살아남아야 한다. LanTransport 는 NetManager 와 수명이 같고 Shutdown 은
        //소켓만 닫으므로 그 조건을 만족한다.

        /// <summary>클라가 호스트로 보낸 메시지 한 종류의 처리를 맡는다. 첫 인자는 보낸 사람의 번호다.</summary>
        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            transport?.RouteHost(type, handler);
        }

        /// <summary>호스트가 클라로 보낸 메시지 한 종류의 처리를 맡는다.</summary>
        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            transport?.RouteClient(type, handler);
        }

        //씬을 나갈 때 반드시 풀어야 한다. 안 그러면 파괴된 오브젝트의 메서드가 남아
        //다음 판에서 "주인이 이미 있습니다" 에러가 뜬다
        public void UnrouteHost(MsgType type)
        {
            transport?.UnrouteHost(type);
        }

        public void UnrouteClient(MsgType type)
        {
            transport?.UnrouteClient(type);
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
