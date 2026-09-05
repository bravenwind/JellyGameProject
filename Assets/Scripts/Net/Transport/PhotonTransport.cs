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

        // ★ 5.1 에서 이름이 바뀌었다
        //   LoadBalancingClient 는 ExitGames.Client.Photon 에 남은 [Obsolete] 껍데기이고,
        //   본체는 Photon.Realtime.RealtimeClient 다. 같은 이유로 EventData 등이 들어 있던
        //   ExitGames.Client.Photon 네임스페이스도 Photon.Client 로 옮겨갔다.
        //   (뼈대를 세울 땐 SDK가 없어 4.x 시절 이름으로 적혀 있었다)
        //
        private RealtimeClient client;

        /// <summary>방 조작(만들기·참가·로비)은 PhotonSession 이 이 위에서 한다.</summary>
        public RealtimeClient Client { get { return client; } }

        /// <summary>마스터 서버까지 붙었는가. 방 조작은 이 뒤에야 할 수 있다.</summary>
        public bool IsOnMaster
        {
            get { return client != null && client.IsConnectedAndReady && !client.InRoom; }
        }

        /// <summary>접속이 실패한 이유. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// 릴레이에 붙는다. 이미 붙어 있으면 아무것도 하지 않는다.
        /// App ID 는 코드에 없다 — Resources/PhotonAppSettings.asset 에서 읽는다.
        /// </summary>
        public bool Connect()
        {
            if (client != null)
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

            client = new RealtimeClient();
            client.AddCallbackTarget(this);
            client.EventReceived += OnEventReceived;

            if (!client.ConnectUsingSettings(settings))
            {
                LastError = "온라인 서버에 연결하지 못했습니다. 인터넷 상태를 확인해주세요.";
                Teardown();
                return false;
            }

            Log("== 온라인 모드 ==  릴레이에 접속 중");
            return true;
        }

        public Action<string> OnLog;
        public Action<string> OnError;

        public event Action<int> OnPeerJoined;
        public event Action<int> OnPeerLeft;
        public event Action OnHostStarted;
        public event Action OnDisconnected;
        public event Action OnConnectionLost;

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
            get { return client != null && client.LocalPlayer != null ? client.LocalPlayer.ActorNumber : 0; }
        }

        public bool IsHost
        {
            get { return client != null && client.LocalPlayer != null && client.LocalPlayer.IsMasterClient; }
        }

        public bool IsConnected
        {
            get { return client != null && client.InRoom; }
        }

        public int PeerCount
        {
            get { return client != null && client.CurrentRoom != null ? client.CurrentRoom.PlayerCount - 1 : 0; }
        }

        //Photon 은 방의 IsOpen 이 이 뜻이다. 방을 닫으면 새 사람이 못 들어온다
        public bool AcceptingNewPeers
        {
            get { return client != null && client.CurrentRoom != null && client.CurrentRoom.IsOpen; }
            set
            {
                if (client != null && client.CurrentRoom != null && IsHost)
                    client.CurrentRoom.IsOpen = value;
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

        // ★ Photon 에는 "한 명 빼고"가 없다
        //   방에 있는 사람에서 그 한 명만 뺀 명단을 직접 만들어 넘겨야 한다.
        //   여기는 스폰 중계처럼 자주 도는 자리라 매번 배열을 새로 만들면 그대로
        //   쓰레기가 된다. 인원이 바뀔 때만 다시 짓고 평소엔 재사용한다.
        private int[] othersCache;
        private int othersCacheExcept = -1;
        private int othersCacheStamp = -1;

        //인원이 바뀔 때마다 올린다. 값 자체에 뜻은 없고 '달라졌다'만 본다
        private int rosterStamp;

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            if (client == null || client.CurrentRoom == null)
                return;

            if (othersCache == null || othersCacheExcept != exceptPeerId || othersCacheStamp != rosterStamp)
            {
                Dictionary<int, Player> players = client.CurrentRoom.Players;

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
            if (client == null || !client.InRoom)
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

            client.OpRaiseEvent(CodeOf(w), BodyOf(w), args, send);
        }

        // ═══════════════════════════════════════════════════════
        //  받기
        // ═══════════════════════════════════════════════════════

        //구독은 Connect 에서 걸고 Teardown 에서 푼다
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

        private readonly NetReader reader = new NetReader();

        private void Dispatch(MsgType type, int senderId, byte[] body)
        {
            //타입은 이벤트 코드로 왔다. 여기서 ReadMsgType() 을 부르면 안 된다(위 설명 참고)
            reader.Reset(body, 0, body.Length);

            if (IsHost)
            {
                Action<int, NetReader> hostRoute;
                if (hostRoutes.TryGetValue(type, out hostRoute))
                {
                    hostRoute(senderId, reader);
                    return;
                }
            }
            else
            {
                Action<NetReader> clientRoute;
                if (clientRoutes.TryGetValue(type, out clientRoute))
                {
                    clientRoute(reader);
                    return;
                }
            }

            Log("처리되지 않은 메시지: " + type);
        }

        // ═══════════════════════════════════════════════════════
        //  라우팅 — LanTransport 와 같다
        // ═══════════════════════════════════════════════════════
        //
        //TODO(사람): 표를 다루는 코드가 LanTransport 와 글자 그대로 같다.
        //           둘 다 살아난 뒤에 공통 부모(NetRouteTable 같은 것)로 빼는 걸 권한다.
        //           지금 미리 빼두지 않은 이유는, 한쪽만 있는 상태에서 추상화를 하면
        //           실제로 무엇이 같은지 확인하지 못한 채 모양만 맞추게 되기 때문이다.

        private readonly Dictionary<MsgType, Action<int, NetReader>> hostRoutes
            = new Dictionary<MsgType, Action<int, NetReader>>();

        private readonly Dictionary<MsgType, Action<NetReader>> clientRoutes
            = new Dictionary<MsgType, Action<NetReader>>();

        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            if (handler == null)
                return;

            if (hostRoutes.ContainsKey(type))
            {
                LogError("호스트 메시지 " + type + " 의 주인이 이미 있습니다.");
                return;
            }

            hostRoutes[type] = handler;
        }

        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            if (handler == null)
                return;

            if (clientRoutes.ContainsKey(type))
            {
                LogError("클라 메시지 " + type + " 의 주인이 이미 있습니다.");
                return;
            }

            clientRoutes[type] = handler;
        }

        public void UnrouteHost(MsgType type) { hostRoutes.Remove(type); }

        public void UnrouteClient(MsgType type) { clientRoutes.Remove(type); }

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
            if (client != null)
                client.Service();
        }

        // ★ 라우팅 표는 남긴다
        //   라우팅은 접속보다 먼저 걸리고(로비가 Start 에서 LoadGameScene 을 등록한다)
        //   판이 끝나도 살아남아야 한다. LanTransport 와 같은 이유다.
        public void Shutdown()
        {
            if (client == null)
                return;

            bool wasConnected = client.InRoom;

            //정상 종료다. 이 뒤에 따라올 OnDisconnected 콜백을 '연결 끊김'으로
            //오해하지 않도록 미리 표시해 둔다
            shuttingDown = true;

            if (client.InRoom)
                client.OpLeaveRoom(false);

            client.Disconnect();
            Teardown();

            if (wasConnected)
                OnDisconnected?.Invoke();
        }

        private bool shuttingDown;

        private void Teardown()
        {
            if (client == null)
                return;

            client.EventReceived -= OnEventReceived;
            client.RemoveCallbackTarget(this);
            client = null;

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
            OnPeerJoined?.Invoke(newPlayer.ActorNumber);
        }

        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            rosterStamp++;
            OnPeerLeft?.Invoke(otherPlayer.ActorNumber);
        }

        // ★ 마스터가 바뀌면 판을 끝낸다
        //   위에서 "ActorNumber 를 그대로 쓴다"고 한 것의 대가다. 새 마스터의
        //   ActorNumber 는 1이 아니므로, 그대로 두면 호스트가 봇을 자기 것으로
        //   알아보지 못한 채 게임이 조용히 망가진다. 아무도 에러를 못 보는 형태라
        //   최악이다. LAN 도 호스트가 나가면 판이 끝나므로 동작이 같아진다.
        public void OnMasterClientSwitched(Player newMasterClient)
        {
            LogError("방장이 나갔습니다. 게임을 종료합니다.");
            OnConnectionLost?.Invoke();
        }

        public void OnCreatedRoom()
        {
            //방을 만든 사람이 곧 마스터다. LAN 의 '포트를 열었다'와 같은 자리
            OnHostStarted?.Invoke();
        }

        // ★ 이름이 겹친다 — 명시적 구현으로 푼다
        //   INetTransport 의 OnDisconnected 는 우리가 쏘는 이벤트고,
        //   IConnectionCallbacks 의 OnDisconnected 는 Photon 이 부르는 메서드다.
        //   둘 다 그냥 public 으로 두면 같은 이름이라 컴파일이 안 된다.
        void IConnectionCallbacks.OnDisconnected(DisconnectCause cause)
        {
            //Shutdown 이 부른 끊김은 이미 알렸다. 두 번 쏘면 로비가 두 번 되돌아간다
            if (shuttingDown)
            {
                shuttingDown = false;
                return;
            }

            Log("연결이 끊어졌습니다 — " + cause);
            Teardown();
            OnConnectionLost?.Invoke();
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
        public void OnJoinedRoom() { }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnLeftRoom() { }

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
