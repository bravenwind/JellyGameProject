using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;

namespace JellyNet
{
    public class PhotonTransport : INetTransport,
        IConnectionCallbacks, IInRoomCallbacks, IMatchmakingCallbacks
    {
        public RealtimeClient Client { get; private set; }

        public string LastError { get; private set; }

        private readonly NetRouteTable routes;
        private readonly NetEvents events;

        private readonly NetReader reader = new NetReader();

        private int[] othersCache;
        private int othersCacheExcept = -1;
        private int othersCacheStamp = -1;
        private int rosterStamp;

        private bool shuttingDown;

        private bool leavingRoom;

        public PhotonTransport(NetRouteTable routes, NetEvents events)
        {
            this.routes = routes;
            this.events = events;
        }

        private static byte[] BodyOf(NetWriter w)
        {
            const int HEADER = 5;
            int bodyLen = w.Length - HEADER;

            byte[] body = new byte[bodyLen];
            Buffer.BlockCopy(w.Buffer, HEADER, body, 0, bodyLen);
            return body;
        }

        private static byte CodeOf(NetWriter w)
        {
            return w.Buffer[4];
        }

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

            return true;
        }

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

        public bool AcceptingNewPeers
        {
            get { return Client != null && Client.CurrentRoom != null && Client.CurrentRoom.IsOpen; }
            set
            {
                if (Client != null && Client.CurrentRoom != null && IsHost)
                    Client.CurrentRoom.IsOpen = value;
            }
        }

        public void Broadcast(NetWriter w)
        {
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
            if (Client == null || !Client.IsConnected || !Client.InRoom)
                return;

            RaiseEventArgs args = new RaiseEventArgs
            {
                Receivers = group,
                TargetActors = targets
            };

            SendOptions send = CodeOf(w) == (byte)MsgType.TransformUpdate
                ? SendOptions.SendUnreliable
                : SendOptions.SendReliable;

            Client.OpRaiseEvent(CodeOf(w), BodyOf(w), args, send);
        }

        private void OnEventReceived(EventData e)
        {
            // 200 이상은 Photon 내부에서 쓰는 이벤트
            if (e.Code >= 200)
                return;

            byte[] body = e.CustomData as byte[];
            if (body == null)
                return;

            Dispatch((MsgType)e.Code, e.Sender, body);
        }

        private void Dispatch(MsgType type, int senderId, byte[] body)
        {
            reader.Reset(body, 0, body.Length);

            if (IsHost)
                routes.DispatchHost(senderId, type, reader);
            else
                routes.DispatchClient(type, reader);
        }

        public bool PrefersBatchedUpdates { get { return true; } }

        public void Poll()
        {
            if (Client != null)
                Client.Service();
        }

        public void Shutdown()
        {
            if (Client != null && Client.IsConnected && Client.InRoom)
            {
                leavingRoom = true;
                Client.OpLeaveRoom(false);
            }

            events.RaiseDisconnected();
        }

        public void DisconnectFully()
        {
            if (Client == null)
                return;

            shuttingDown = true;
            leavingRoom = true;

            if (Client.IsConnected && Client.InRoom)
                Client.OpLeaveRoom(false);

            Client.Disconnect();
        }

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

        public void OnMasterClientSwitched(Player newMasterClient)
        {
            if (leavingRoom || Client == null || !Client.InRoom)
                return;

            events.RaiseConnectionLost();
        }

        public void OnCreatedRoom()
        {
            events.RaiseHostStarted();
        }

        void IConnectionCallbacks.OnDisconnected(DisconnectCause cause)
        {
            bool expected = shuttingDown;
            shuttingDown = false;

            ReleaseClient();

            if (!expected)
                events.RaiseConnectionLost();
        }

        public void OnConnected() { }
        public void OnConnectedToMaster() { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }
        public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps) { }
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreateRoomFailed(short returnCode, string message) { }

        public void OnJoinedRoom()
        {
            leavingRoom = false;

            events.OpenSession();
            events.RaiseRoomEntered();
        }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnLeftRoom() { leavingRoom = false; }
    }
}

