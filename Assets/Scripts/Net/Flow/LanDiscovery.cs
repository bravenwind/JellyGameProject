using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace JellyNet
{
    public class LanDiscovery : MonoBehaviour
    {
        public static LanDiscovery Instance { get; private set; }

        public const int DISCOVERY_PORT = 7778;

        private const string MAGIC = "JELLYPANG1";

        [Tooltip("방장이 몇 초마다 알릴지.")]
        public float beaconInterval = 1f;

        [Tooltip("이 시간 동안 소식이 없으면 목록에서 지운다.")]
        public float roomTimeout = 3.5f;

        public class RoomInfo
        {
            public string Ip;
            public int Port;
            public string HostName;
            public GameModeType Mode;
            public int Current;
            public int Needed;
            public int AiCount;
            public float LastSeen;

            public bool IsFull { get { return Current >= Needed; } }
            public string Address { get { return Ip + ":" + Port; } }
        }

        private readonly Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>();

        private UdpClient send;
        private UdpClient listen;
        private float beaconTimer;

        public IEnumerable<RoomInfo> Rooms { get { return rooms.Values; } }
        public int RoomCount { get { return rooms.Count; } }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            StopAll();
            if (Instance == this)
                Instance = null;
        }

        private void OnApplicationQuit() { StopAll(); }

        public void StopAll()
        {
            StopBeacon();
            StopListening();
        }

        public void StartBeacon()
        {
            StopBeacon();
            try
            {
                send = new UdpClient();
                send.EnableBroadcast = true;
                beaconTimer = 0f;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[탐색] 브로드캐스트를 열 수 없습니다: " + e.Message);
                send = null;
            }
        }

        public void StopBeacon()
        {
            if (send == null)
                return;
            try { send.Close(); } catch { }
            send = null;
        }

        private void SendBeacon()
        {
            NetManager net = NetManager.Instance;
            if (send == null || net == null || !net.IsHost)
                return;

            int current = net.Host != null ? net.Host.PeerCount + 1 : 1;

            string name = (LanRoomConfig.Nickname ?? "").Replace("|", "");
            if (string.IsNullOrEmpty(name))
                name = "방";

            string msg = string.Join("|", new string[]
            {
                MAGIC,
                net.port.ToString(),
                name,
                ((int)LanRoomConfig.Mode).ToString(),
                current.ToString(),
                LanRoomConfig.HumanCount.ToString(),
                LanRoomConfig.AiCount.ToString()
            });

            byte[] data = Encoding.UTF8.GetBytes(msg);

            try
            {
                send.Send(data, data.Length,
                    new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[탐색] 알림 실패: " + e.Message);
                StopBeacon();
            }
        }

        public void StartListening()
        {
            StopListening();
            rooms.Clear();

            try
            {
                listen = new UdpClient();

                listen.Client.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listen.ExclusiveAddressUse = false;

                listen.Client.Bind(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[탐색] 수신을 열 수 없습니다: " + e.Message);
                listen = null;
            }
        }

        public void StopListening()
        {
            if (listen == null)
                return;
            try { listen.Close(); } catch { }
            listen = null;
            rooms.Clear();
        }

        private void Poll()
        {
            if (listen == null)
                return;

            while (listen.Available > 0)
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;

                try { data = listen.Receive(ref from); }
                catch { break; }

                Parse(Encoding.UTF8.GetString(data), from.Address.ToString());
            }
        }

        private void Parse(string msg, string fromIp)
        {
            string[] p = msg.Split('|');
            if (p.Length < 7 || p[0] != MAGIC)
                return;

            RoomInfo r;
            if (!rooms.TryGetValue(fromIp, out r))
            {
                r = new RoomInfo { Ip = fromIp };
                rooms[fromIp] = r;
            }

            int.TryParse(p[1], out r.Port);
            r.HostName = p[2];

            int modeId;
            int.TryParse(p[3], out modeId);
            r.Mode = (GameModeType)modeId;

            int.TryParse(p[4], out r.Current);
            int.TryParse(p[5], out r.Needed);
            int.TryParse(p[6], out r.AiCount);

            r.LastSeen = Time.unscaledTime;
        }

        private readonly List<string> stale = new List<string>();

        private void Expire()
        {
            stale.Clear();
            foreach (var kv in rooms)
                if (Time.unscaledTime - kv.Value.LastSeen > roomTimeout)
                    stale.Add(kv.Key);

            for (int i = 0; i < stale.Count; i++) rooms.Remove(stale[i]);
        }

        private void Update()
        {
            if (send != null)
            {
                beaconTimer += Time.unscaledDeltaTime;
                if (beaconTimer >= beaconInterval)
                {
                    beaconTimer = 0f;
                    SendBeacon();
                }
            }

            Poll();
            Expire();
        }
    }
}
