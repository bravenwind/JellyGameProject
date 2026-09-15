// ─────────────────────────────────────────────────────────────────────
//  Photon Realtime 세션 — 방 만들기·찾기·참가
// ─────────────────────────────────────────────────────────────────────
//
//  Photon Realtime SDK 가 있어야 컴파일에 들어온다. 가드 이야기는 PhotonTransport.cs 머리말 참고.

#if PHOTON_REALTIME_5_OR_NEWER

using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;

namespace JellyNet
{
    /// <summary>
    /// Photon 로비로 INetSession 을 구현한다.
    ///
    /// LAN 과 갈리는 지점이 여기다. LAN 은 UDP 비콘을 뿌려 같은 랜의 방을 찾지만,
    /// Photon 은 로비에 붙으면 서버가 방 목록을 밀어준다. IP도 포트도 없다 —
    /// 그래서 RoomHandle.Id 가 "ip:port" 가 아니라 방 이름이 된다.
    /// </summary>
    public class PhotonSession : INetSession,
        IConnectionCallbacks, ILobbyCallbacks, IMatchmakingCallbacks
    {
        private readonly PhotonTransport transport;


        //온라인이므로 로컬 전용 입력(포트)은 화면에서 감춰야 한다
        public bool IsLocal { get { return false; } }

        //로비에 들어가야 목록이 오기 시작한다. 그 전엔 '방이 없다'가 아니라 '아직'이다
        public bool IsBrowseReady
        {
            get { return transport.Client != null && transport.Client.InLobby; }
        }

        // ★ 방 조작은 '마스터 서버에 붙은 뒤'에만 할 수 있다
        //   LAN 은 소켓을 여는 것이 곧 방을 여는 것이라 한 걸음이었다. Photon 은
        //   접속 → 마스터 서버 도착 → 그제서야 방 만들기/참가/로비 순이다.
        //   그래서 하려던 일을 적어두고, 도착 콜백에서 꺼내 실행한다.
        private enum Intent { None, Create, Join, Browse }

        private Intent pending;
        private string pendingRoomName;

        //방 속성 키. 길수록 매 방마다 그만큼 더 오간다 — 로비 목록은 방 수만큼 곱해진다
        private const string PROP_MODE = "m";
        private const string PROP_NEEDED = "n";
        private const string PROP_AI = "a";
        private const string PROP_HOST = "h";

        //목록에 보여야 하는 키는 이 배열에 있어야 로비까지 따라온다.
        //안 넣으면 방에 들어가기 전엔 못 읽어서 목록의 '3/4명' 칸이 비어 보인다
        private static readonly object[] LOBBY_PROPS =
            { PROP_MODE, PROP_NEEDED, PROP_AI, PROP_HOST };

        public PhotonSession(PhotonTransport transport, NetEvents events)
        {
            this.transport = transport;
            this.events = events;

            //취소·판 종료로 판을 접으면 적어둔 일도 없던 일이 된다
            this.events.OnShutdownRequested += CancelPending;
        }

        private readonly NetEvents events;

        /// <summary>생성자에서 게시판에 걸어둔 구독을 푼다. NetManager 가 죽을 때 부른다.</summary>
        public void UnsubscribeFromEvents()
        {
            events.OnShutdownRequested -= CancelPending;
        }

        private void CancelPending()
        {
            pending = Intent.None;
            pendingRoomName = null;
        }

        /// <summary>
        /// 릴레이에 붙어 있게 만들고, 하려던 일을 적어둔다.
        /// 이미 마스터 서버에 있으면 그 자리에서 실행한다.
        /// </summary>
        private bool Begin(Intent intent, string roomName)
        {
            pending = intent;
            pendingRoomName = roomName;

            if (!transport.Connect())
            {
                pending = Intent.None;
                Fail(transport.LastError);
                return false;
            }

            //Connect 가 방금 만든 클라이언트에 우리 콜백을 건다. 두 번 걸지 않는다
            if (!hooked && transport.Client != null)
            {
                transport.Client.AddCallbackTarget(this);
                hooked = true;
            }

            if (transport.IsOnMaster)
                RunPending();

            return true;
        }

        private bool hooked;

        private void RunPending()
        {
            Intent intent = pending;
            pending = Intent.None;

            switch (intent)
            {
                case Intent.Create: DoCreate(); break;
                case Intent.Join: DoJoin(); break;
                case Intent.Browse: transport.Client.OpJoinLobby(null); break;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  방 만들기 · 참가
        // ═══════════════════════════════════════════════════════

        // ★ 반환값의 뜻이 LAN 과 다르다
        //   LAN 은 여기서 결과가 확정되지만 Photon 은 요청만 나간다. 성공은
        //   OnCreatedRoom/OnJoinedRoom 콜백으로 몇백 ms 뒤에 온다. 그래서 true 는
        //   "됐다"가 아니라 "보냈다"는 뜻이고, 로비는 그동안 "연결 중..." 을 띄운 채
        //   INetSession.OnRoomEntered 를 기다린다.
        // ★ 서버에 물어보기 전에 우리가 먼저 거른다
        //   서버도 같은 이름을 거절하지만, 그 대답은 몇백 ms 뒤 콜백으로 온다.
        //   그때는 이미 "연결 중" 화면이 떠 있고, 실패 처리가 Photon 이 메시지를
        //   풀고 있는 도중에 끼어들게 된다. 방 목록은 이미 우리 손에 있으니
        //   버튼을 누른 그 자리에서 답할 수 있다 — LocalSession 과 같은 방식이다.
        //
        //   목록에 없는 방과 겹칠 수는 있다(목록이 도착하기 전, 또는 시작해서 감춰진 방).
        //   그건 서버가 잡아주고 OnCreateRoomFailed 가 받는다. 여기 검사는 그 경로를
        //   드물게 만들 뿐 없애지는 않는다.
        private bool NameTaken(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
                return false;

            string wanted = roomName.Trim();

            for (int i = 0; i < handles.Count; i++)
            {
                RoomEntry r = handles[i];
                if (r == null || string.IsNullOrEmpty(r.HostName))
                    continue;

                if (string.Equals(r.HostName.Trim(), wanted,
                                  StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public bool CreateRoom(RoomSetup options)
        {
            if (NameTaken(options.RoomName))
            {
                Fail("같은 이름의 방이 이미 있습니다. 닉네임을 바꿔주세요.");
                return false;
            }

            //Photon 에는 포트가 없다. options.LocalPort 는 여기서 버린다
            return Begin(Intent.Create, options.RoomName);
        }

        private void DoCreate()
        {
            //LAN 은 이 값들을 UDP 비콘 문자열에 실어 보냈다. 여기서는 방 속성이 그 자리다
            PhotonHashtable props = new PhotonHashtable
            {
                { PROP_MODE, (int)LanRoomConfig.Mode },
                { PROP_NEEDED, LanRoomConfig.HumanCount },
                { PROP_AI, LanRoomConfig.AiCount },
                { PROP_HOST, pendingRoomName }
            };

            Photon.Realtime.RoomOptions opts = new Photon.Realtime.RoomOptions
            {
                //봇은 방에 들어오지 않는다. 자리를 차지하는 건 사람뿐이다
                MaxPlayers = LanRoomConfig.HumanCount,
                CustomRoomProperties = props,
                CustomRoomPropertiesForLobby = LOBBY_PROPS,

                //방을 만든 사람이 나가면 방이 그대로 사라져야 한다.
                //남겨두면 아무도 없는 방이 목록에 떠 있게 된다
                EmptyRoomTtl = 0,
                PlayerTtl = 0
            };

            transport.Client.OpCreateRoom(new EnterRoomArgs
            {
                RoomName = pendingRoomName,
                RoomOptions = opts,
                Lobby = TypedLobby.Default
            });
        }

        public bool JoinRoom(RoomEntry room)
        {
            if (room == null || string.IsNullOrEmpty(room.Id))
            {
                Fail("방을 알아볼 수 없습니다. 목록을 새로 고친 뒤 다시 시도해주세요.");
                return false;
            }

            //LAN 은 Id 가 "ip:port" 였지만 온라인은 방 이름 그 자체다
            return Begin(Intent.Join, room.Id);
        }

        private void DoJoin()
        {
            transport.Client.OpJoinRoom(new EnterRoomArgs { RoomName = pendingRoomName });
        }

        // ═══════════════════════════════════════════════════════
        //  방 찾기
        // ═══════════════════════════════════════════════════════

        //목록은 초당 몇 번씩 읽히므로 매번 새 리스트를 만들지 않는다
        private readonly List<RoomEntry> handles = new List<RoomEntry>();

        public IEnumerable<RoomEntry> Rooms { get { return handles; } }

        //방 이름 → 자리. 서버가 '바뀐 것만' 보내므로 우리가 표를 들고 있어야 한다
        private readonly Dictionary<string, RoomEntry> byName
            = new Dictionary<string, RoomEntry>();

        public void StartBrowsing()
        {
            //목록은 OnRoomListUpdate 콜백으로 들어온다. LAN 처럼 우리가 훑을 필요가 없다
            Begin(Intent.Browse, null);
        }

        public void StopBrowsing()
        {
            //InLobby 도 state 만 보는 값이라 소켓이 죽은 뒤에도 한동안 참으로 남는다.
            //그때 OpLeaveLobby 를 보내면 "peer is not connected" 가 찍힌다
            if (transport.Client != null && transport.Client.IsConnected && transport.Client.InLobby)
                transport.Client.OpLeaveLobby();

            handles.Clear();
            byName.Clear();
        }

        // ★ 연결은 끊지 않는다
        //   판이 시작됐을 뿐 방은 살아 있어야 한다. 목록에서만 내린다
        public void StopAdvertising()
        {
            if (transport.Client == null || transport.Client.CurrentRoom == null)
                return;

            if (!transport.Client.LocalPlayer.IsMasterClient)
                return;

            transport.Client.CurrentRoom.IsVisible = false;
            transport.Client.CurrentRoom.IsOpen = false;
        }

        /// <summary>Photon 이 준 방 목록을 RoomHandle 로 옮긴다.</summary>
        private void ApplyRoomList(List<RoomInfo> photonRooms)
        {
            // ★ 서버는 '바뀐 방'만 보낸다
            //   전체 목록이 아니다. 그대로 갈아끼우면 가만히 있는 방이 사라진다.
            //   그리고 사라진 방은 RemovedFromList 로 오는데, 그걸 빼지 않으면
            //   없는 방이 목록에 계속 떠 있는다.
            for (int i = 0; i < photonRooms.Count; i++)
            {
                RoomInfo info = photonRooms[i];

                if (info.RemovedFromList)
                {
                    byName.Remove(info.Name);
                    continue;
                }

                RoomEntry h;
                if (!byName.TryGetValue(info.Name, out h))
                {
                    h = new RoomEntry();
                    byName[info.Name] = h;
                }

                h.Id = info.Name;
                h.Address = info.Name;      //온라인은 보여줄 주소가 방 이름뿐이다
                h.HostName = PropString(info, PROP_HOST, info.Name);
                h.Mode = (GameModeType)PropInt(info, PROP_MODE, (int)GameModeType.Absorb);
                h.Needed = PropInt(info, PROP_NEEDED, info.MaxPlayers);
                h.AiCount = PropInt(info, PROP_AI, 0);
                h.Current = info.PlayerCount;
            }

            handles.Clear();
            foreach (RoomEntry h in byName.Values)
                handles.Add(h);

            events.RaiseRoomListChanged();
        }

        private static int PropInt(RoomInfo info, string key, int fallback)
        {
            object v;
            if (info.CustomProperties != null && info.CustomProperties.TryGetValue(key, out v) && v is int)
                return (int)v;

            return fallback;
        }

        private static string PropString(RoomInfo info, string key, string fallback)
        {
            object v;
            if (info.CustomProperties != null && info.CustomProperties.TryGetValue(key, out v))
            {
                string t = v as string;
                if (!string.IsNullOrEmpty(t))
                    return t;
            }

            return fallback;
        }

        // ═══════════════════════════════════════════════════════
        //  Photon 콜백
        // ═══════════════════════════════════════════════════════

        //붙었다. 적어둔 일을 이제 실행한다
        public void OnConnectedToMaster() { RunPending(); }

        public void OnRoomListUpdate(List<RoomInfo> roomList) { ApplyRoomList(roomList); }

        //방에 들어간 사실은 PhotonTransport.OnJoinedRoom 이 알린다. LAN 과 같이 전송의 몫이다
        public void OnJoinedRoom() { }

        public void OnCreateRoomFailed(short returnCode, string message)
        {
            //제일 흔한 실패다. Photon 은 방 이름이 방을 가리키는 열쇠라 겹칠 수 없다
            if (returnCode == ErrorCode.GameIdAlreadyExists)
                Fail("같은 이름의 방이 이미 있습니다. 닉네임을 바꿔주세요.");
            else
                Fail("방을 만들지 못했습니다 — " + message);
        }

        public void OnJoinRoomFailed(short returnCode, string message)
        {
            if (returnCode == ErrorCode.GameDoesNotExist)
                Fail("그 방은 이미 사라졌습니다. 목록을 새로 고쳐주세요.");
            else if (returnCode == ErrorCode.GameFull)
                Fail("방이 가득 찼습니다.");
            else if (returnCode == ErrorCode.GameClosed)
                Fail("이미 시작한 방입니다.");
            else
                Fail("방에 들어가지 못했습니다 — " + message);
        }

        public void OnDisconnected(DisconnectCause cause)
        {
            //붙는 도중에 끊겼다면 하려던 일도 없던 일이 된다.
            //남겨두면 다음에 붙었을 때 아무도 시키지 않은 방이 만들어진다
            CancelPending();
            hooked = false;
            handles.Clear();
            byName.Clear();
        }

        //우리가 듣지 않는 콜백들. 인터페이스라 비워둘 수는 없다
        public void OnConnected() { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }
        public void OnJoinedLobby() { }
        public void OnLeftLobby() { }
        public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnLeftRoom() { }

        private void Fail(string reason)
        {
            events.RaiseSessionFailed(reason);
        }
    }
}

#endif
