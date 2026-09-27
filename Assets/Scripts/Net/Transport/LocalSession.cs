using System;
using System.Collections.Generic;

namespace JellyNet
{
    public class LocalSession : INetSession
    {
        private readonly SocketTransport transport;
        private readonly NetEvents events;

        private int port;

        private readonly List<RoomEntry> handles = new List<RoomEntry>();

        private int signature = -1;

        public bool IsLocal { get { return true; } }

        public bool IsBrowseReady { get { return LanDiscovery.Instance != null; } }

        public LocalSession(SocketTransport transport, NetEvents events, int defaultPort)
        {
            this.transport = transport;
            this.events = events;
            port = defaultPort;

            this.events.OnDisconnected += StopAdvertising;
        }

        private bool NameTaken(string roomName)
        {
            if (LanDiscovery.Instance == null || string.IsNullOrEmpty(roomName))
                return false;

            string wanted = roomName.Trim();

            foreach (LanDiscovery.RoomInfo r in LanDiscovery.Instance.Rooms)
            {
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

            if (options.LocalPort > 0)
                port = options.LocalPort;

            if (!transport.StartHost(port))
            {
                Fail(transport.LastError);
                return false;
            }

            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StartBeacon(port);

            return true;
        }

        public bool JoinRoom(RoomEntry room)
        {
            string ip;
            int p;

            if (room == null || !TryParseId(room.Id, out ip, out p))
            {
                Fail("방 주소를 알아볼 수 없습니다. 목록을 새로 고친 뒤 다시 시도해주세요.");
                return false;
            }

            port = p;

            if (!transport.JoinHost(ip, port))
            {
                Fail(transport.LastError);
                return false;
            }

            return true;
        }

        private static bool TryParseId(string id, out string ip, out int p)
        {
            ip = null;
            p = 0;

            if (string.IsNullOrEmpty(id))
                return false;

            int cut = id.LastIndexOf(':');
            if (cut <= 0 || cut == id.Length - 1)
                return false;

            ip = id.Substring(0, cut);
            return int.TryParse(id.Substring(cut + 1), out p) && p > 0;
        }

        public void StartBrowsing()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StartListening();
        }

        public void StopBrowsing()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StopListening();

            handles.Clear();
            signature = -1;
        }

        public void UnsubscribeFromEvents()
        {
            events.OnDisconnected -= StopAdvertising;
        }

        public void StopAdvertising()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StopBeacon();
        }

        public IEnumerable<RoomEntry> Rooms { get { return handles; } }

        public void Poll()
        {
            LanDiscovery d = LanDiscovery.Instance;
            if (d == null)
                return;

            int h = Signature(d);
            if (h == signature)
                return;

            signature = h;

            handles.Clear();
            foreach (LanDiscovery.RoomInfo r in d.Rooms)
            {
                handles.Add(new RoomEntry
                {
                    Id = r.Ip + ":" + r.Port,
                    Address = r.Address,
                    HostName = r.HostName,
                    Mode = r.Mode,
                    Current = r.Current,
                    Needed = r.Needed,
                    AiCount = r.AiCount
                });
            }

            events.RaiseRoomListChanged();
        }

        private static int Signature(LanDiscovery d)
        {
            unchecked
            {
                int h = 17;
                foreach (LanDiscovery.RoomInfo r in d.Rooms)
                {
                    h = h * 31 + (r.Ip != null ? r.Ip.GetHashCode() : 0);
                    h = h * 31 + r.Port;
                    h = h * 31 + (r.HostName != null ? r.HostName.GetHashCode() : 0);
                    h = h * 31 + (int)r.Mode;
                    h = h * 31 + r.Current;
                    h = h * 31 + r.Needed;
                    h = h * 31 + r.AiCount;
                }
                return h;
            }
        }

        private void Fail(string reason)
        {
            events.RaiseSessionFailed(reason);
        }
    }
}
