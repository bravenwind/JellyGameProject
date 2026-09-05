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
        //방을 만들 때·붙을 때의 기본값이다. 실제로 쓰는 값은 세션이 들고 있다 —
        //로비의 포트 입력이 방을 만들 때 덮어쓰고, 참가는 고른 방의 주소를 따른다.
        //인스펙터에 남겨두는 건 "아무것도 안 정했을 때의 출발점"이기 때문이다
        [SerializeField] private int port = NetConfig.DEFAULT_PORT;
        [SerializeField] private int maxLogLines = 200;

        //joinIp 를 걷어냈다. 붙을 주소는 언제나 목록에서 고른 방(RoomHandle)에서 나오고,
        //주소를 손으로 넣는 화면은 없다. 남겨두면 "인스펙터의 저 IP 는 뭐지"가 된다
        //(씬에 남은 joinIp 키는 다음 저장 때 유니티가 알아서 버린다)

        //LAN 전용 진입점(StartHost/JoinHost)이 필요해 구체 타입도 함께 들고 있다.
        //전송이 갈려도 하나뿐인 것 — 어떤 MsgType 을 누가 맡는가
        private NetRouteTable routes;

        private LanTransport lan;
        private LanSession lanSession;

#if PHOTON_REALTIME_5_OR_NEWER
        private PhotonTransport photon;
        private PhotonSession photonSession;
#endif

        private INetTransport transport;
        private INetSession session;

        /// <summary>방을 만들고 찾고 참가하는 통로. 로비·방 목록 UI는 이것만 본다.</summary>
        public INetSession Session { get { return session; } }

        // ★ 세션 이벤트는 NetManager 가 중계한다 — 전송 이벤트와 같은 이유다
        //   로비는 Start 에서 한 번 구독하는데, 그때 세션은 아직 LAN 이다.
        //   온라인을 고르면 session 이 바뀌지만 구독은 옛 세션에 남아,
        //   방에 들어가도 OnRoomReady 가 오지 않아 "연결 중..." 에서 멈췄다.
        //   실패(OnFailed)도 마찬가지로 화면에 닿지 못했다.
        public event Action OnRoomReady;
        public event Action<string> OnSessionFailed;

        private void HookSession(INetSession s)
        {
            s.OnRoomReady += RaiseRoomReady;
            s.OnFailed += RaiseSessionFailed;
        }

        private void UnhookSession(INetSession s)
        {
            if (s == null)
                return;

            s.OnRoomReady -= RaiseRoomReady;
            s.OnFailed -= RaiseSessionFailed;
        }

        //고르지 않은 세션은 아무것도 쏘지 않으므로 둘 다 걸어둬도 된다
        private void RaiseRoomReady() { OnRoomReady?.Invoke(); }

        private void RaiseSessionFailed(string reason) { OnSessionFailed?.Invoke(reason); }

        /// <summary>지금 온라인 전송을 쓰고 있는가. 화면의 로컬/온라인 선택이 정한다.</summary>
        public bool IsOnline { get; private set; }

        /// <summary>
        /// 로컬(LAN)과 온라인(Photon)을 갈아끼운다. 방을 만들거나 참가하기 <b>전에</b> 부른다.
        ///
        /// ★ 전송을 판마다 새로 만들지 않는 이유
        ///   둘 다 미리 만들어 두고 가리키는 곳만 바꾼다. 접속 전에 걸어두는 라우팅
        ///   (로비가 Start 에서 등록하는 LoadGameScene 등)과 이벤트 구독이,
        ///   전송을 새로 만드는 순간 통째로 사라지기 때문이다.
        /// </summary>
        public void UseOnline(bool online)
        {
            if (IsOnline == online)
                return;

            //판이 도는 중에 바꾸면 라우팅은 새 전송을 보는데 데이터는 옛 전송으로 온다
            if (transport != null && transport.IsConnected)
            {
                Debug.LogError("[Net] 접속 중에는 로컬/온라인을 바꿀 수 없습니다.");
                return;
            }

#if PHOTON_REALTIME_5_OR_NEWER
            IsOnline = online;
            transport = online ? (INetTransport)photon : lan;
            session = online ? (INetSession)photonSession : lanSession;
#else
            if (online)
            {
                //Photon Realtime SDK 가 없으면 온라인 코드가 아예 컴파일되지 않았다.
                //조용히 LAN 으로 돌리면 "온라인을 골랐는데 랜으로 붙는다"가 되므로 말한다
                Debug.LogError("[Net] 온라인 전송이 이 빌드에 없습니다. "
                    + "Photon Realtime SDK 가 설치돼 있는지 확인해주세요.");
                return;
            }
#endif
        }

        /// <summary>
        /// 지금 호스트인가 클라인가 아무것도 아닌가. 전송 상태에서 매번 유도한다.
        ///
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

        /// <summary>위치 갱신을 묶어 보내야 하는 전송인가. NetWorld 가 본다.</summary>
        public bool PrefersBatchedUpdates
        {
            get { return transport != null && transport.PrefersBatchedUpdates; }
        }

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
            //표는 전송보다 위에 있다. 무엇으로 실어 나르든 "이 타입은 누가 맡는가"는
            //같은 답이고, 등록은 접속보다 한참 먼저 일어난다
            routes = new NetRouteTable();
            routes.OnLog = AddLog;
            routes.OnError = msg => Debug.LogError("[NetManager] " + msg);

            lan = new LanTransport(routes);
            lan.OnLog = AddLog;
            lan.OnError = msg => Debug.LogError("[NetManager] " + msg);

            lanSession = new LanSession(lan, port);

            transport = lan;
            session = lanSession;

            HookSession(lanSession);

#if PHOTON_REALTIME_5_OR_NEWER
            //만들어만 둔다. 실제 접속은 온라인으로 방을 만들거나 참가할 때 일어난다
            photon = new PhotonTransport(routes);
            photon.OnLog = AddLog;
            photon.OnError = msg => Debug.LogError("[NetManager] " + msg);

            photonSession = new PhotonSession(photon);

            Hook(photon);
            HookSession(photonSession);
#endif
            Hook(lan);
        }

        //바깥은 NetManager 의 이벤트만 구독한다. 전송을 갈아끼워도 구독이 끊기지 않는다.
        //쓰지 않는 전송은 아무것도 쏘지 않으므로 둘 다 걸어둬도 된다
        private void Hook(INetTransport t)
        {
            t.OnPeerJoined += RaisePeerJoined;
            t.OnPeerLeft += RaisePeerLeft;
            t.OnHostStarted += RaiseHostStarted;
            t.OnDisconnected += RaiseDisconnected;
            t.OnConnectionLost += RaiseConnectionLost;
        }

        private void Unhook(INetTransport t)
        {
            if (t == null)
                return;

            t.OnPeerJoined -= RaisePeerJoined;
            t.OnPeerLeft -= RaisePeerLeft;
            t.OnHostStarted -= RaiseHostStarted;
            t.OnDisconnected -= RaiseDisconnected;
            t.OnConnectionLost -= RaiseConnectionLost;
        }

        private void Update()
        {
            // ★ 고르지 않은 전송도 돌린다
            //   Photon 은 Disconnect 를 던진 뒤에도 Service 를 계속 받아야 실제로
            //   끝난다. 활성 전송만 돌리면, 온라인을 껐다 로컬로 바꾼 순간
            //   Photon 이 Disconnecting 인 채 멈춘다. 쓰지 않는 전송의 Poll 은
            //   소켓도 클라이언트도 없어 사실상 아무 일도 하지 않는다.
            lan?.Poll();
#if PHOTON_REALTIME_5_OR_NEWER
            photon?.Poll();
#endif

            //방 목록이 바뀌었는지 훑는다. 로비 화면에서만 의미가 있지만, 목록을 켜지 않았으면
            //훑을 것도 없어 비용이 사실상 0이다
            lanSession?.Poll();
        }

        private void OnApplicationQuit() { CloseEverything(); }

        //판만 끝내는 Shutdown 과 달리 연결까지 끊는다
        private void CloseEverything()
        {
            Shutdown();

#if PHOTON_REALTIME_5_OR_NEWER
            photon?.DisconnectFully();
#endif
        }

        private void OnDestroy()
        {
            CloseEverything();

            //전송은 이 객체만 들고 있으니 같이 사라지지만, 구독은 건 자리에서 푼다.
            //중복 NetManager가 걷어내질 때(Awake의 Destroy(this)) 이쪽만 살아남는 경우를
            //생각하면 짝을 맞춰두는 편이 안전하다
            lanSession?.Unhook();
            UnhookSession(lanSession);
#if PHOTON_REALTIME_5_OR_NEWER
            photonSession?.Unhook();
            UnhookSession(photonSession);
#endif

            Unhook(lan);
#if PHOTON_REALTIME_5_OR_NEWER
            Unhook(photon);
#endif

            if (Instance == this)
                Instance = null;
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
