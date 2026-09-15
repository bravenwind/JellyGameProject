using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetManager : MonoBehaviour
    {
        public enum Mode { None, Host, Client }

        #region 상태

        public static NetManager Instance { get; private set; }

        //전송이 갈려도 하나뿐인 것 둘 — 어떤 MsgType 을 누가 맡는가, 무슨 일이 일어났는가
        private NetRouteTable routes;

        /// <summary>
        /// 네트워크에서 일어난 일(입장·퇴장·끝남·실패)을 듣는 곳. 구독은 모두 여기에 건다.
        /// 로컬/온라인을 갈아끼워도 같은 객체라 구독이 끊기지 않는다.
        /// </summary>
        public NetEvents Events { get; private set; }

        //LAN 전용 진입점(StartHost/JoinHost)을 세션이 불러야 해서 구체 타입으로 들고 있다
        private SocketTransport localTransport;
        private LocalSession localSession;

#if PHOTON_REALTIME_5_OR_NEWER
        private PhotonTransport photonTransport;
        private PhotonSession photonSession;
#endif

        private INetTransport transport;
        /// <summary>방을 만들고 찾고 참가하는 통로. 로비·방 목록 UI는 이것만 본다.</summary>
        public INetSession Session { get; private set; }

        /// <summary>지금 온라인 전송을 쓰고 있는가. 화면의 로컬/온라인 선택이 정한다.</summary>
        public bool IsOnline { get; private set; }

        private readonly List<string> log = new List<string>();

        // ★ ConnectionLost 와 LastError 를 지웠다
        //   둘 다 localTransport 를 직접 읽어서, 온라인일 때는 언제나 false / null 이었다.
        //   PhotonTransport 에도 LastError 가 있고 값을 채우지만 이 길로는 나오지 못했다.
        //   인터페이스(INetTransport)에 없는 것을 밖에 내주려다 구체 타입 하나를
        //   골라잡은 결과다 — 전송을 갈아끼워도 같은 답이 나와야 한다는 규칙이 여기서 깨졌다.

        //   고치는 대신 지운 이유는 부르는 곳이 없기 때문이다. 접속이 끊긴 사실은
        //   OnConnectionLost 이벤트로, 실패 사유는 OnSessionFailed 로 이미 흘러간다.
        //   값을 물어보는 통로와 알려주는 통로가 둘 다 있으면 한쪽만 고쳐지게 된다.

        #endregion

        #region 설정

        [Header("설정")]
        //방을 만들 때·붙을 때의 기본값이다. 실제로 쓰는 값은 세션이 들고 있다 —
        //로비의 포트 입력이 방을 만들 때 덮어쓰고, 참가는 고른 방의 주소를 따른다.
        //인스펙터에 남겨두는 건 "아무것도 안 정했을 때의 출발점"이기 때문이다
        [SerializeField] private int port = NetConfig.DEFAULT_PORT;
        [SerializeField] private int maxLogLines = 200;

        //joinIp 를 걷어냈다. 붙을 주소는 언제나 목록에서 고른 방(RoomHandle)에서 나오고,
        //주소를 손으로 넣는 화면은 없다. 남겨두면 "인스펙터의 저 IP 는 뭐지"가 된다
        //(씬에 남은 joinIp 키는 다음 저장 때 유니티가 알아서 버린다)

        #endregion

        #region 씬 전환

        [Header("씬 전환")]
        [Tooltip("씬이 바뀌어도 연결을 유지한다. Main 씬에서 접속해 게임 씬으로 넘어가려면 켜야 한다.")]
        [SerializeField] private bool persistAcrossScenes = true;

        #endregion

        /// <summary>
        /// 로컬(LAN)과 온라인(Photon)을 갈아끼운다. 방을 만들거나 참가하기 <b>전에</b> 부른다.
        ///
        /// 이름이 UseOnline 이었을 땐 "온라인을 쓴다"로 읽혀서, false 를 넘기는 자리가
        /// 앞뒤가 안 맞아 보였다. 고르는 일이라는 걸 이름에 넣는다.
        ///
        /// ★ 전송을 판마다 새로 만들지 않는 이유
        ///   둘 다 미리 만들어 두고 가리키는 곳만 바꾼다. 접속 전에 걸어두는 라우팅
        ///   (로비가 Start 에서 등록하는 LoadGameScene 등)과 이벤트 구독이,
        ///   전송을 새로 만드는 순간 통째로 사라지기 때문이다.
        /// </summary>
        public void UseLocalOrOnline(bool online)
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
            transport = online ? photonTransport : localTransport;
            Session = online ? photonSession : localSession;
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

            //게시판도 전송보다 먼저 선다. 전송·세션은 만들어질 때 이걸 받아 대고 말한다
            Events = new NetEvents();

            localTransport = new SocketTransport(routes, Events);
            localTransport.OnLog = AddLog;
            localTransport.OnError = msg => Debug.LogError("[NetManager] " + msg);

            localSession = new LocalSession(localTransport, Events, port);

            transport = localTransport;
            Session = localSession;

#if PHOTON_REALTIME_5_OR_NEWER
            //만들어만 둔다. 실제 접속은 온라인으로 방을 만들거나 참가할 때 일어난다
            photonTransport = new PhotonTransport(routes, Events);
            photonTransport.OnLog = AddLog;
            photonTransport.OnError = msg => Debug.LogError("[NetManager] " + msg);

            photonSession = new PhotonSession(photonTransport, Events);
#endif
        }

        // ★ 중계층을 걷어냈다
        //   예전엔 여기 RelayFrom / StopRelayingFrom 두 쌍과 Raise* 7개가 있었다.
        //   전송·세션이 저마다 이벤트를 들고 있어서, 갈아끼워도 구독이 끊기지 않게
        //   NetManager 가 받아서 되쏘았다. 이제 모두가 NetEvents 하나에 대고 말하므로
        //   되쏠 것이 없다. 자세한 사정은 NetEvents 머리말에.

        private void Update()
        {
            // ★ 고르지 않은 전송도 돌린다
            //   Photon 은 Disconnect 를 던진 뒤에도 Service 를 계속 받아야 실제로
            //   끝난다. 활성 전송만 돌리면, 온라인을 껐다 로컬로 바꾼 순간
            //   Photon 이 Disconnecting 인 채 멈춘다. 쓰지 않는 전송의 Poll 은
            //   소켓도 클라이언트도 없어 사실상 아무 일도 하지 않는다.
            localTransport?.Poll();
#if PHOTON_REALTIME_5_OR_NEWER
            photonTransport?.Poll();
#endif

            //방 목록이 바뀌었는지 훑는다. 로비 화면에서만 의미가 있지만, 목록을 켜지 않았으면
            //훑을 것도 없어 비용이 사실상 0이다
            //
            //★ photonSession 에는 짝이 되는 줄이 없다 — 빠뜨린 게 아니다
            //  Poll 은 INetSession 에 없는 LocalSession 만의 메서드다. LAN 은 LanDiscovery 가
            //  UDP 비콘을 사전에 쌓아두기만 하고 "바뀌었다"를 알려주지 않아서 우리가 훑어야 한다.
            //  온라인은 릴레이가 OnRoomListUpdate 콜백으로 밀어준다(PhotonSession 338줄).
            //  밀어주는 쪽에 훑기를 붙이면 같은 목록을 두 번 만들게 된다.
            localSession?.Poll();
        }

        private void OnApplicationQuit() { CloseEverything(); }

        //판만 끝내는 Shutdown 과 달리 연결까지 끊는다
        private void CloseEverything()
        {
            Shutdown();

#if PHOTON_REALTIME_5_OR_NEWER
            photonTransport?.DisconnectFully();
#endif
        }

        private void OnDestroy()
        {
            CloseEverything();

            //전송·세션은 이 객체만 들고 있으니 같이 사라지지만, 구독은 건 자리에서 푼다.
            //중복 NetManager가 걷어내질 때(Awake의 Destroy(this)) 이쪽만 살아남는 경우를
            //생각하면 짝을 맞춰두는 편이 안전하다
            localSession?.UnsubscribeFromEvents();
#if PHOTON_REALTIME_5_OR_NEWER
            photonSession?.UnsubscribeFromEvents();
#endif

            if (Instance == this)
                Instance = null;
        }

        public void Shutdown()
        {
            //접으라는 요청을 먼저 올린다. 접속 중에 적어둔 일(온라인 방 만들기 등)은
            //이 자리에서 취소돼야 한다 — 전송이 접히는 걸 기다리면 이미 늦을 수 있다
            Events.RaiseShutdownRequested();
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

        // ─────────────────────────────────────────────────────────
        //  메시지 라우팅 — 표는 NetManager 가 들고 두 전송이 함께 쓴다
        // ─────────────────────────────────────────────────────────
        //
        //예전 주석은 "표가 전송 쪽에 있다"였다. 전송마다 표를 들고 있다가 등록이
        //LAN 표로만 가서 온라인이 통째로 안 된 뒤 NetRouteTable 로 뺐다.
        //그래서 등록도 전송을 거치지 않고 표에 직접 한다.

        /// <summary>클라가 호스트로 보낸 메시지 한 종류의 처리를 맡는다. 첫 인자는 보낸 사람의 번호다.</summary>
        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            routes.RouteHost(type, handler);
        }

        /// <summary>호스트가 클라로 보낸 메시지 한 종류의 처리를 맡는다.</summary>
        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            routes.RouteClient(type, handler);
        }

        //씬을 나갈 때 반드시 풀어야 한다. 안 그러면 파괴된 오브젝트의 메서드가 남아
        //다음 판에서 "주인이 이미 있습니다" 에러가 뜬다
        public void UnrouteHost(MsgType type)
        {
            routes.UnrouteHost(type);
        }

        public void UnrouteClient(MsgType type)
        {
            routes.UnrouteClient(type);
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
