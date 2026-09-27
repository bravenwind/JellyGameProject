#if PHOTON_REALTIME_5_OR_NEWER

using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;

namespace JellyNet
{
    public class PhotonSession : INetSession,
        IConnectionCallbacks, ILobbyCallbacks, IMatchmakingCallbacks
    {
        private readonly PhotonTransport transport;

        private Intent pending;
        private string pendingRoomName;

        private readonly NetEvents events;

        private bool hooked;

        private const string PROP_MODE = "m";
        private const string PROP_NEEDED = "n";
        private const string PROP_AI = "a";
        private const string PROP_HOST = "h";

        private static readonly object[] LOBBY_PROPS =
            { PROP_MODE, PROP_NEEDED, PROP_AI, PROP_HOST };

        private readonly List<RoomEntry> rooms = new List<RoomEntry>();

        private readonly Dictionary<string, RoomEntry> byName
            = new Dictionary<string, RoomEntry>();

        public PhotonSession(PhotonTransport transport, NetEvents events)
        {
            this.transport = transport;
            this.events = events;

            this.events.OnShutdownRequested += CancelPending;
        }

        public bool IsLocal { get { return false; } }

        public bool IsBrowseReady
        {
            get { return transport.Client != null && transport.Client.InLobby; }
        }

        private enum Intent { None, Create, Join, Browse }

        public void UnsubscribeFromEvents()
        {
            events.OnShutdownRequested -= CancelPending;
        }

        private void CancelPending()
        {
            pending = Intent.None;
            pendingRoomName = null;
        }

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

            if (!hooked && transport.Client != null)
            {
                transport.Client.AddCallbackTarget(this);
                hooked = true;
            }

            if (transport.IsOnMaster)
                RunPending();

            return true;
        }

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

        private bool NameTaken(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
                return false;

            string wanted = roomName.Trim();

            for (int i = 0; i < rooms.Count; i++)
            {
                RoomEntry r = rooms[i];
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

            return Begin(Intent.Create, options.RoomName);
        }

        private void DoCreate()
        {
            PhotonHashtable props = new PhotonHashtable
            {
                { PROP_MODE, (int)RoomConfig.Mode },
                { PROP_NEEDED, RoomConfig.HumanCount },
                { PROP_AI, RoomConfig.AiCount },
                { PROP_HOST, pendingRoomName }
            };

            Photon.Realtime.RoomOptions opts = new Photon.Realtime.RoomOptions
            {
                MaxPlayers = RoomConfig.HumanCount,
                CustomRoomProperties = props,
                CustomRoomPropertiesForLobby = LOBBY_PROPS,

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

            return Begin(Intent.Join, room.Id);
        }

        private void DoJoin()
        {
            transport.Client.OpJoinRoom(new EnterRoomArgs { RoomName = pendingRoomName });
        }

        public IEnumerable<RoomEntry> Rooms { get { return rooms; } }

        public void StartBrowsing()
        {
            Begin(Intent.Browse, null);
        }

        public void StopBrowsing()
        {
            if (transport.Client != null && transport.Client.IsConnected && transport.Client.InLobby)
                transport.Client.OpLeaveLobby();

            rooms.Clear();
            byName.Clear();
        }

        public void StopAdvertising()
        {
            if (transport.Client == null || transport.Client.CurrentRoom == null)
                return;

            if (!transport.Client.LocalPlayer.IsMasterClient)
                return;

            transport.Client.CurrentRoom.IsVisible = false;
            transport.Client.CurrentRoom.IsOpen = false;
        }

        private void ApplyRoomList(List<RoomInfo> photonRooms)
        {
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
                h.Address = info.Name;
                h.HostName = PropString(info, PROP_HOST, info.Name);
                h.Mode = (GameModeType)PropInt(info, PROP_MODE, (int)GameModeType.Absorb);
                h.Needed = PropInt(info, PROP_NEEDED, info.MaxPlayers);
                h.AiCount = PropInt(info, PROP_AI, 0);
                h.Current = info.PlayerCount;
            }

            rooms.Clear();
            foreach (RoomEntry h in byName.Values)
                rooms.Add(h);

            events.RaiseRoomListChanged();
        }

        private static int PropInt(RoomInfo info, string key, int fallback)
        {
            return info.CustomProperties.TryGetValue(key, out object v) && v is int n
                ? n : fallback;
        }

        private static string PropString(RoomInfo info, string key, string fallback)
        {
            return info.CustomProperties.TryGetValue(key, out object v) && v is string s && s.Length > 0
                ? s : fallback;
        }

        public void OnConnectedToMaster() { RunPending(); }

        public void OnRoomListUpdate(List<RoomInfo> roomList) { ApplyRoomList(roomList); }

        public void OnJoinedRoom() { }

        public void OnCreateRoomFailed(short returnCode, string message)
        {
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
            CancelPending();
            hooked = false;
            rooms.Clear();
            byName.Clear();
        }

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
