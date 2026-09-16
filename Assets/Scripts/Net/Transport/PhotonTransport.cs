// ─────────────────────────────────────────────────────────────────────
//  Photon Realtime 전송
// ─────────────────────────────────────────────────────────────────────
//
//  ★ 가드가 왜 PHOTON_REALTIME_5_OR_NEWER 인가 — 손으로 켜는 심볼을 두 번 버렸다
//    ① PHOTON_UNITY_NETWORKING — PUN2 의 심볼이다. PUN2 를 걷어낼 때 ProjectSettings 의
//       정의 심볼은 지워지지 않아 <b>SDK 도 없는 채로 켜져 있었고</b> 컴파일이 깨졌다.
//    ② JELLY_PHOTON — 그래서 우리 이름을 만들어 손으로 넣었다. 그런데 정의 심볼은
//       플랫폼마다 따로고 유니티가 메모리에 들고 있어서, 파일에는 있는데 컴파일에는
//       안 들어가는 상태가 됐다(Bee 의 Assembly-CSharp.rsp 로 확인).
//       사람이 열일곱 줄을 맞춰야 하는 스위치는 언젠가 어긋난다.
//
//    PHOTON_REALTIME_5_OR_NEWER 는 Realtime SDK 가 [InitializeOnLoad] 로 <b>스스로</b>
//    모든 플랫폼에 박는다(PhotonUtilitiesUnity.ApplyDefinesRealtimeV5).
//    "Realtime 5 가 설치돼 있다"는 우리가 필요한 조건 그 자체이고, 켜고 끄는 일이
//    SDK 를 넣고 빼는 일과 하나로 묶인다 — 사람이 맞춰야 할 것이 없다.
//
//  ★ 설정은 어디에 있나
//    App ID·지역·앱 버전은 Resources/PhotonAppSettings.asset 에 있다.
//    코드에는 없다 — PhotonAppSettingsAsset.Load() 로 읽는다.
//
//  Realtime 5.1.19 기준으로 맞춰져 있다. 5.x 에서 이름이 바뀐 것들:
//    ExitGames.Client.Photon → Photon.Client / LoadBalancingClient → RealtimeClient
//    RaiseEventOptions → RaiseEventArgs(구조체)

#if PHOTON_REALTIME_5_OR_NEWER

using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;

namespace JellyNet
{
    /// <summary>
    /// Photon Realtime 릴레이로 INetTransport 를 구현한다.
    ///
    /// LAN 과 다른 점은 셋뿐이다.
    ///   · 프레이밍을 안 한다  — 아래 '메시지 모양' 참고
    ///   · 호스트가 소켓의 주인이 아니라 방의 마스터 클라이언트다
    ///   · 번호가 우리 것이 아니라 Photon 의 ActorNumber 다
    /// 그 위의 라우팅·핸들러는 LanTransport 와 글자 그대로 같다.
    /// </summary>
    public class PhotonTransport : INetTransport,
        IConnectionCallbacks, IInRoomCallbacks, IMatchmakingCallbacks
    {
        #region 상태

        // ★ 5.1 에서 이름이 바뀌었다
        //   LoadBalancingClient 는 ExitGames.Client.Photon 에 남은 [Obsolete] 껍데기이고,
        //   본체는 Photon.Realtime.RealtimeClient 다. 같은 이유로 EventData 등이 들어 있던
        //   ExitGames.Client.Photon 네임스페이스도 Photon.Client 로 옮겨갔다.
        //   (뼈대를 세울 땐 SDK가 없어 4.x 시절 이름으로 적혀 있었다)
        //
        /// <summary>방 조작(만들기·참가·로비)은 PhotonSession 이 이 위에서 한다.</summary>
        public RealtimeClient Client { get; private set; }

        /// <summary>접속이 실패한 이유. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public string LastError { get; private set; }

        public Action<string> OnLog;
        public Action<string> OnError;

        #endregion

        #region 보내기

        // ★ Photon 에는 "한 명 빼고"가 없다
        //   방에 있는 사람에서 그 한 명만 뺀 명단을 직접 만들어 넘겨야 한다.
        //   여기는 스폰 중계처럼 자주 도는 자리라 매번 배열을 새로 만들면 그대로
        //   쓰레기가 된다. 인원이 바뀔 때만 다시 짓고 평소엔 재사용한다.
        private int[] othersCache;
        private int othersCacheExcept = -1;
        private int othersCacheStamp = -1;

        //인원이 바뀔 때마다 올린다. 값 자체에 뜻은 없고 '달라졌다'만 본다
        private int rosterStamp;

        #endregion

        #region 받기

        // ★ 리더는 하나로 충분하다 — 조건이 셋이다
        //   ① 이벤트 하나가 메시지 하나다. RaiseEvent 로 보낸 byte[] 는 경계째로 도착하고,
        //      묶어 보낸 TransformUpdate 도 이벤트 하나 안에서 끝까지 읽힌다.
        //   ② 받기는 한 줄로 돈다. Service 는 NetManager.Update 에서만 부르고, Dispatch 가
        //      이벤트마다 Reset 한 뒤 핸들러가 다 읽고 돌아와야 다음 이벤트를 꺼낸다.
        //   ③ 핸들러가 리더를 들고 나가지 않는다. 코루틴·나중에 도는 람다·필드에 넘기면
        //      그땐 이미 다음 메시지를 가리킨다 — 지금 라우트 핸들러 중엔 그런 곳이 없다.
        //   UseByteArraySlicePoolForEvents 를 켜면 CustomData 가 byte[] 가 아니라
        //   ByteArraySlice 로 와서 OnEventReceived 의 'as byte[]' 가 null 이 되고, 메시지가
        //   조용히 버려진다. 기본값은 꺼짐이고 우리도 켜지 않는다.
        private readonly NetReader reader = new NetReader();

        #endregion

        #region 라우팅 — LanTransport 와 같은 표를 쓴다

        private readonly NetRouteTable routes;

        //일어난 일을 알리는 게시판. NetManager 가 하나 만들어 모두에게 꽂아준다
        private readonly NetEvents events;

        #endregion

        #region 수명

        private bool shuttingDown;

        #endregion

        #region Photon 콜백

        //내가 방을 떠나는 중인가. 이 동안 오는 방 이벤트는 뒷정리일 뿐이다
        private bool leavingRoom;

        #endregion

        // ═══════════════════════════════════════════════════════
        //  메시지 모양 — [len4][type1][body] ↔ RaiseEvent(byte, byte[])
        // ═══════════════════════════════════════════════════════
        //
        //  LAN 은 TCP 라 경계가 없어서 우리가 직접 길이를 붙였다.
        //      NetWriter.Buffer = [len4][type1][body...]
        //      NetWriter.Length = 4 + 1 + body 길이
        //  Photon 은 이벤트 하나가 곧 메시지 하나라 경계를 릴레이가 지킨다.
        //  그래서 [len4] 는 빼고 보낸다 — 넣으면 4바이트를 매 메시지 낭비한다.
        //
        //  보낼 때:
        //      eventCode = w.Buffer[4]                  ← type1 이 그대로 이벤트 코드가 된다
        //      content   = w.Buffer[5 .. w.Length-1]    ← body 만
        //
        //  받을 때:
        //      type = (MsgType)eventData.Code
        //      reader.Reset(body, 0, body.Length)       ← body 의 맨 앞부터
        //
        //  ★ 여기서 반드시 주의할 것
        //    LAN 경로는 NetHost.HandleMessage 가 r.ReadMsgType() 으로 타입을 읽어
        //    리더를 한 바이트 밀어놓은 뒤 핸들러에 넘긴다. Photon 은 타입이 이벤트 코드로
        //    따로 오므로 ReadMsgType() 을 부르면 안 된다 — 부르면 body 첫 바이트를
        //    타입으로 먹고 그 뒤가 전부 한 칸씩 밀린다. 예외도 안 나고 값만 이상해진다.
        //    아래 Dispatch 는 그래서 코드로 타입을 정하고 리더는 body 앞에 세운다.

        //TODO(사람): w.Buffer 를 그대로 넘기면 Photon 이 배열 전체를 직렬화한다.
        //           길이에 맞춰 잘라야 한다. 매 메시지 새 배열을 만들면 쓰레기가 쌓이므로
        //           크기별 풀이나 ArraySegment 지원 여부를 SDK 문서에서 확인할 것.

        public PhotonTransport(NetRouteTable routes, NetEvents events)
        {
            this.routes = routes;
            this.events = events;
        }

        private static byte[] BodyOf(NetWriter w)
        {
            const int HEADER = 5;               // len4 + type1
            int bodyLen = w.Length - HEADER;

            byte[] body = new byte[bodyLen];
            Buffer.BlockCopy(w.Buffer, HEADER, body, 0, bodyLen);
            return body;
        }

        private static byte CodeOf(NetWriter w)
        {
            return w.Buffer[4];
        }

        // ═══════════════════════════════════════════════════════
        //  상태
        // ═══════════════════════════════════════════════════════

/// <summary>
        /// 마스터 서버까지 붙었는가. 방 만들기·참가·로비는 이 뒤에야 할 수 있다.
        ///
        /// ★ IsConnectedAndReady 만으로는 모자란다
        ///   Photon 은 NameServer → MasterServer → GameServer 순으로 옮겨 다닌다.
        ///   IsConnectedAndReady 는 "지금 연산을 보낼 수 있는 상태인가"만 보기 때문에
        ///   <b>NameServer 에 붙어 있어도 참</b>이다. 그런데 방 만들기(OpCreateRoom)는
        ///   MasterServer 에서만 받는다. 그 사이에 버튼을 누르면
        ///   "Operation CreateGame (227) not allowed on current server NameServer" 가 뜬다.
        ///   어느 서버에 붙어 있는지를 직접 본다.
        /// </summary>
        public bool IsOnMaster
        {
            get
            {
                return Client != null
                    && Client.IsConnectedAndReady
                    && Client.Server == ServerConnection.MasterServer
                    && !Client.InRoom;
            }
        }

        /// <summary>
        /// 릴레이에 붙는다. 이미 붙어 있으면 아무것도 하지 않는다.
        /// App ID 는 코드에 없다 — Resources/PhotonAppSettings.asset 에서 읽는다.
        /// </summary>
        public bool Connect()
        {
            if (Client != null)
                return true;

            PhotonAppSettingsAsset asset = PhotonAppSettingsAsset.Load();
            if (asset == null || string.IsNullOrEmpty(asset.AppIdRealtime))
            {
                LastError = "온라인 설정이 없습니다. Resources/PhotonAppSettings 의 App ID 를 확인해주세요.";
                return false;
            }

            AppSettings settings = new AppSettings
            {
                AppIdRealtime = asset.AppIdRealtime,
                AppVersion = asset.AppVersion,
                FixedRegion = asset.FixedRegion
            };

            Client = new RealtimeClient();
            Client.AddCallbackTarget(this);
            Client.EventReceived += OnEventReceived;

            if (!Client.ConnectUsingSettings(settings))
            {
                LastError = "온라인 서버에 연결하지 못했습니다. 인터넷 상태를 확인해주세요.";
                ReleaseClient();
                return false;
            }

            Log("== 온라인 모드 ==  릴레이에 접속 중");
            return true;
        }

// ★ ActorNumber 를 그대로 쓴다. 번역표는 두지 않는다
        //   이 게임에서 OwnerId 는 "책임"이고 호스트는 언제나 1이다(NetHost.HOST_ID).
        //   봇이 전부 호스트 소유라, 호스트 번호가 1이 아니면 호스트가 봇을 자기 것으로
        //   알아보지 못해 아무도 봇을 굴리지 않는다. 에러 하나 없이 게임만 이상해진다.
        //
        //   그런데 Photon 도 <b>새 방의 첫 참가자는 항상 ActorNumber 1</b> 이고 그 사람이
        //   곧 마스터다. 즉 방을 만든 순간 이미 "마스터 = 1"이 성립한다. 번역표가 필요한
        //   경우는 마스터가 도중에 바뀔 때 하나뿐인데, 그건 아래에서 판을 끝내는 쪽으로
        //   막는다. 안 일어날 일을 위해 계층을 하나 더 두면, 나중에 번호가 안 맞을 때
        //   의심할 곳만 늘어난다.
        //
        //   대신 OnMasterClientSwitched 를 반드시 잡아야 한다 — 아래를 볼 것.
        public int MyId
        {
            get { return Client != null && Client.LocalPlayer != null ? Client.LocalPlayer.ActorNumber : 0; }
        }

        public bool IsHost
        {
            get { return Client != null && Client.LocalPlayer != null && Client.LocalPlayer.IsMasterClient; }
        }

        public bool IsConnected
        {
            get { return Client != null && Client.InRoom; }
        }

        public int PeerCount
        {
            get { return Client != null && Client.CurrentRoom != null ? Client.CurrentRoom.PlayerCount - 1 : 0; }
        }

        //Photon 은 방의 IsOpen 이 이 뜻이다. 방을 닫으면 새 사람이 못 들어온다
        public bool AcceptingNewPeers
        {
            get { return Client != null && Client.CurrentRoom != null && Client.CurrentRoom.IsOpen; }
            set
            {
                if (Client != null && Client.CurrentRoom != null && IsHost)
                    Client.CurrentRoom.IsOpen = value;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  보내기
        // ═══════════════════════════════════════════════════════

        //호스트가 아닌데 Broadcast 를 부르는 건 호출부의 실수지만, LAN 과 마찬가지로
        //조용히 버린다. 판이 끝나 방을 나간 뒤에도 커튼 구간에서 게임 씬의 Update 가
        //계속 도는 구간이 판마다 반드시 지나가기 때문이다

        public void Broadcast(NetWriter w)
        {
            //자기 자신에게는 보내지 않는다. 호스트는 이미 로컬에서 처리했다 — LAN 과 같다
            Raise(w, ReceiverGroup.Others, null);
        }

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            if (Client == null || Client.CurrentRoom == null)
                return;

            if (othersCache == null || othersCacheExcept != exceptPeerId || othersCacheStamp != rosterStamp)
            {
                Dictionary<int, Player> players = Client.CurrentRoom.Players;

                int count = 0;
                foreach (int id in players.Keys)
                    if (id != exceptPeerId && id != MyId)
                        count++;

                othersCache = new int[count];

                int i = 0;
                foreach (int id in players.Keys)
                    if (id != exceptPeerId && id != MyId)
                        othersCache[i++] = id;

                othersCacheExcept = exceptPeerId;
                othersCacheStamp = rosterStamp;
            }

            //보낼 곳이 없으면 보내지 않는다. 빈 TargetActors 는 '전체'로 해석될 수 있다
            if (othersCache.Length == 0)
                return;

            Raise(w, ReceiverGroup.Others, othersCache);
        }

        public void SendTo(int peerId, NetWriter w)
        {
            Raise(w, ReceiverGroup.Others, new int[] { peerId });
        }

        public void SendToHost(NetWriter w)
        {
            Raise(w, ReceiverGroup.MasterClient, null);
        }

        private void Raise(NetWriter w, ReceiverGroup group, int[] targets)
        {
            //IsConnected 를 함께 보는 이유는 Shutdown 의 주석에 적어두었다 —
            //소켓이 죽어도 state 는 한동안 Joined 로 남는다
            if (Client == null || !Client.IsConnected || !Client.InRoom)
                return;

// ★ 5.1 에서 RaiseEventOptions 는 RaiseEventArgs 가 됐다(클래스 → 구조체)
            RaiseEventArgs args = new RaiseEventArgs
            {
                Receivers = group,
                TargetActors = targets
            };

            // ★ 어떤 메시지를 놓쳐도 되는가
            //   LAN 은 전부 TCP 라 고민할 필요가 없었지만, 릴레이에서는 이게
            //   대역폭과 지연을 좌우한다.
            //
            //   위치 갱신은 20Hz로 계속 덮어쓰는 값이라 하나 놓쳐도 다음 것이
            //   50ms 뒤에 온다. 게다가 엔트리마다 '잰 순간'(SendTime)을 들고 다녀서
            //   순서가 바뀌어도 받는 쪽이 타임라인을 바로 세운다 — 재전송을 기다리면
            //   오히려 그 뒤의 최신 위치까지 같이 밀린다.
            //
            //   나머지는 전부 사건이다. 스폰·탈락·점수는 놓치면 그걸로 끝이라 신뢰 전송.
            SendOptions send = CodeOf(w) == (byte)MsgType.TransformUpdate
                ? SendOptions.SendUnreliable
                : SendOptions.SendReliable;

            Client.OpRaiseEvent(CodeOf(w), BodyOf(w), args, send);
        }

        // ═══════════════════════════════════════════════════════
        //  받기
        // ═══════════════════════════════════════════════════════

        //구독은 Connect 에서 걸고 ReleaseClient 에서 푼다
        private void OnEventReceived(EventData e)
        {
            //Photon 내부 이벤트(코드 200 이상)는 우리 것이 아니다
            if (e.Code >= 200)
                return;

            byte[] body = e.CustomData as byte[];
            if (body == null)
                return;

            Dispatch((MsgType)e.Code, e.Sender, body);
        }

        private void Dispatch(MsgType type, int senderId, byte[] body)
        {
            //타입은 이벤트 코드로 왔다. 여기서 ReadMsgType() 을 부르면 안 된다(위 설명 참고)
            reader.Reset(body, 0, body.Length);

            if (IsHost)
                routes.DispatchHost(senderId, type, reader);
            else
                routes.DispatchClient(type, reader);
        }

        //라우팅 등록(RouteHost 등)은 여기 없다 — 지운 이유는 INetTransport 에 적었다.
        //받은 이벤트를 표에 넘기는 일(Dispatch)만 전송이 한다

        // ═══════════════════════════════════════════════════════
        //  수명
        // ═══════════════════════════════════════════════════════

        // ★ 릴레이는 메시지 수가 곧 비용이자 한도다
        //   Photon 은 방 하나에 초당 500 메시지. 봇 9 + 클라 3 이면 묶지 않았을 때
        //   호스트만으로 초당 300개를 넘긴다. 묶으면 엔티티 수와 무관하게 20개다.
        public bool PrefersBatchedUpdates { get { return true; } }

        //Photon 은 우리가 직접 돌려야 한다. 끊김은 콜백으로 오므로 여기서 볼 게 없다
        public void Poll()
        {
            if (Client != null)
                Client.Service();
        }

        // ★ 라우팅 표는 남긴다
        //   라우팅은 접속보다 먼저 걸리고(로비가 Start 에서 LoadGameScene 을 등록한다)
        //   판이 끝나도 살아남아야 한다. LanTransport 와 같은 이유다.
        // ★ 판을 끝내는 것은 '방을 나가는 것'이지 '연결을 끊는 것'이 아니다
        //   예전엔 여기서 Disconnect 까지 하고 Client 를 곧바로 null 로 만들었다.
        //   Disconnect 는 <b>끊겠다는 요청</b>일 뿐이고 실제 종료는 Service 를 계속
        //   돌려야 콜백으로 온다. Client 를 버리면 Poll 이 멈춰 그 호출이 사라지고,
        //   Photon 이 Disconnecting 인 채 5초 뒤에 경고를 뱉는다.
        //   ("DispatchIncomingCommands() wasn't called for > 5 seconds")
        //   게다가 그 상태에서 방을 다시 만들면 죽지 않은 옛 연결 위에 새 연결이 얹힌다.
        //
        //   릴레이에서 로비로 돌아가는 것은 '방에서 나가는 것'이다. 마스터 서버에는
        //   붙어 있는 편이 맞다 — 방 목록도 거기서 오고, 다시 방을 만들 때 접속
        //   과정을 처음부터 되풀이하지 않아도 된다.
        public void Shutdown()
        {
            //'접으라는 요청'은 NetManager.Shutdown 이 게시판에 먼저 올린다. 세션은 거기서 듣는다

            // ★ InRoom 만으로는 모자란다 — IsConnected 도 본다
            //   InRoom 은 state == Joined 인지만 보는데, 그 상태는 소켓이 죽어도
            //   곧바로 바뀌지 않는다. Service 가 몇 초 안 돌면(에디터가 멈췄거나
            //   콜백 안에서 연산을 부르다 꼬이면) 서버는 이미 우리를 떨궜는데
            //   state 는 Joined 로 굳어 있다. 그때 OpLeaveRoom 을 보내면
            //   "Operation LeaveRoom (254) can't be sent because peer is not
            //   connected" 가 찍힌다.
            if (Client != null && Client.IsConnected && Client.InRoom)
            {
                leavingRoom = true;
                Client.OpLeaveRoom(false);
            }

            // ★ 방을 나갈 수 없어도 세션은 닫는다
            //   예전엔 위 조건이 거짓이면 그대로 return 해서 OnDisconnected 가 안 나갔다.
            //   연결이 먼저 끊긴 경우(ReleaseClient 로 Client 가 이미 null)가 정확히 그 경우라,
            //   OnConnectionLost 뒤에 나가기를 눌러도 뒷정리(NetWorld.ClearAll 등)가 돌지 않았다.
            //   몇 번 불려도 한 번만 나가게 하는 일은 게시판이 한다.
            events.RaiseDisconnected();
        }

        /// <summary>
        /// 연결까지 정말로 끊는다. 앱을 닫거나 NetManager 가 사라질 때만.
        /// 끊김 콜백이 올 때까지 Poll 은 계속 돌아야 한다 — NetManager 가 두 전송을
        /// 모두 돌리는 이유가 이것이다.
        /// </summary>
        public void DisconnectFully()
        {
            if (Client == null)
                return;

            shuttingDown = true;
            leavingRoom = true;

            //위 Shutdown 과 같은 이유로 IsConnected 를 함께 본다.
            //이미 끊긴 상태면 방을 나갈 것도 없고, Disconnect 는 그냥 부르면 된다
            if (Client.IsConnected && Client.InRoom)
                Client.OpLeaveRoom(false);

            Client.Disconnect();
        }

        /// <summary>
        /// 이미 끝났거나 시작하지 못한 연결의 RealtimeClient 를 놓는다.
        /// 서버에는 아무것도 보내지 않는다 — 우리 구독을 풀고, 참조를 버리고, 명단 캐시를 비운다.
        ///
        /// ★ 이름이 Teardown 이었다
        ///   '허문다'로 읽혀서 연결을 끊는 함수처럼 보였다. 실제로 끊는 건 DisconnectFully 가
        ///   서버에 요청하는 일이고, 이 함수는 그 끊김이 확인된 뒤(OnDisconnected 콜백)나
        ///   접속을 시작조차 못 했을 때 클라이언트 객체를 버리는 뒷정리만 한다.
        /// </summary>
        private void ReleaseClient()
        {
            if (Client == null)
                return;

            Client.EventReceived -= OnEventReceived;
            Client.RemoveCallbackTarget(this);
            Client = null;

            othersCache = null;
            othersCacheExcept = -1;
            othersCacheStamp = -1;
        }

        // ═══════════════════════════════════════════════════════
        //  Photon 콜백
        // ═══════════════════════════════════════════════════════

        public void OnPlayerEnteredRoom(Player newPlayer)
        {
            rosterStamp++;
            events.RaisePeerJoined(newPlayer.ActorNumber);
        }

        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            rosterStamp++;
            events.RaisePeerLeft(otherPlayer.ActorNumber);
        }

        // ★ 마스터가 바뀌면 판을 끝낸다
        //   위에서 "ActorNumber 를 그대로 쓴다"고 한 것의 대가다. 새 마스터의
        //   ActorNumber 는 1이 아니므로, 그대로 두면 호스트가 봇을 자기 것으로
        //   알아보지 못한 채 게임이 조용히 망가진다. 아무도 에러를 못 보는 형태라
        //   최악이다. LAN 도 호스트가 나가면 판이 끝나므로 동작이 같아진다.
        public void OnMasterClientSwitched(Player newMasterClient)
        {
            // ★ 판이 끝날 때는 모두가 방을 나간다 — 그때의 교체는 사고가 아니다
            //   결과 씬으로 넘어가며 우리도 OpLeaveRoom 을 부르는데, 그 직후 호스트가
            //   나가면서 마스터 교체가 도착한다. 그걸 '방장이 나갔다'로 읽으면
            //   씬 전환 한복판에서 판을 다시 GameOver 로 되돌려, 전환이 끝나지 못하고
            //   화면이 멈췄다. 매 판 끝날 때마다 반드시 일어나는 일이었다.
            //
            //   내가 나가는 중이 아닐 때의 교체만 사고다 — 그때는 정말로 방장이
            //   판 도중에 사라진 것이다.
            if (leavingRoom || Client == null || !Client.InRoom)
                return;

            LogError("방장이 나갔습니다. 게임을 종료합니다.");
            events.RaiseConnectionLost();
        }

        public void OnCreatedRoom()
        {
            //방을 만든 사람이 곧 마스터다. LAN 의 '포트를 열었다'와 같은 자리
            events.RaiseHostStarted();
        }

        // ★ 이름이 겹친다 — 명시적 구현으로 푼다
        //   INetTransport 의 OnDisconnected 는 우리가 쏘는 이벤트고,
        //   IConnectionCallbacks 의 OnDisconnected 는 Photon 이 부르는 메서드다.
        //   둘 다 그냥 public 으로 두면 같은 이름이라 컴파일이 안 된다.
        void IConnectionCallbacks.OnDisconnected(DisconnectCause cause)
        {
            //우리가 시킨 끊김인가. 정리는 어느 쪽이든 여기서 한다 —
            //Disconnect 가 실제로 끝나는 시점이 여기이기 때문이다
            bool expected = shuttingDown;
            shuttingDown = false;

            Log("연결이 끊어졌습니다 — " + cause);
            ReleaseClient();

            //시키지 않은 끊김만 위로 알린다. 시킨 끊김은 부른 쪽이 이미 알고 있다
            if (!expected)
                events.RaiseConnectionLost();
        }

        //우리가 듣지 않는 콜백들. 인터페이스라 비워둘 수는 없다
        public void OnConnected() { }
        public void OnConnectedToMaster() { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }
        public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps) { }
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        //방에 들어왔으니 '나가는 중'은 끝났다
        // ★ 방에 들어간 사실은 전송이 알린다 — LAN 과 자리를 맞춘다
        //   예전엔 PhotonSession 이 이 콜백에서 OnRoomEntered 를 쐈고, LAN 은 전송의 환영
        //   인사가 세션을 거쳐 올라갔다. 같은 사건을 한쪽은 세션이, 한쪽은 전송이 쐈다.
        //   '방 안에 있는가'는 연결의 상태라 전송의 몫이다.
        //   만든 사람에게는 OnCreatedRoom 다음에 이것도 온다 — 그래서 여기 한 곳에만 건다.
        public void OnJoinedRoom()
        {
            //방에 들어왔으니 '나가는 중'은 끝났다
            leavingRoom = false;

            events.OpenSession();
            events.RaiseRoomEntered();
        }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnLeftRoom() { leavingRoom = false; }

        private void Log(string msg) { OnLog?.Invoke(msg); }

        private void LogError(string msg)
        {
            if (OnError != null)
                OnError(msg);
            else
                OnLog?.Invoke("[오류] " + msg);
        }
    }
}

#endif
