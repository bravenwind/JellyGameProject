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
        [SerializeField] private float beaconInterval = 1f;

        [Tooltip("이 시간 동안 소식이 없으면 목록에서 지움. ")]
        [SerializeField] private float roomTimeout = 3.5f;

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

        //비콘을 보낼 곳들. 랜카드마다 대역이 달라서 한 곳만으로는 부족하다 (NetUtil 참고)
        private readonly List<IPEndPoint> targets = new List<IPEndPoint>();

        //와이파이가 나중에 붙거나 VPN이 켜질 수 있어 주기적으로 다시 훑는다
        private const float TARGETS_TTL = 10f;
        private float targetsAge;

        private UdpClient send;
        private UdpClient listen;
        private float beaconTimer;

        public IEnumerable<RoomInfo> Rooms { get { return rooms.Values; } }
        private void Awake()
        {
            if (Instance != null && Instance != this) 
            { 
                Destroy(this); 
                return; 
            }

            Instance = this;
        }

        private void Update()
        {
            if (send != null)
            {
                targetsAge += Time.unscaledDeltaTime;
                if (targetsAge >= TARGETS_TTL)
                    RefreshTargets();

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

        private void OnDestroy()
        {
            StopAll();
            if (Instance == this)
                Instance = null;
        }

        private void OnApplicationQuit()
        { 
            StopAll();
        }

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
                RefreshTargets();
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

            try
            { 
                send.Close();
            }
            catch 
            {

            }

            send = null;
            targets.Clear();
        }

        private void RefreshTargets()
        {
            targets.Clear();
            targets.AddRange(NetUtil.GetBroadcastTargets(DISCOVERY_PORT));
            targetsAge = 0f;

            if (targets.Count == 0)
                Debug.LogWarning("[탐색] 알림을 보낼 네트워크 어댑터를 찾지 못했습니다. "
                                 + "랜선이나 와이파이가 연결되어 있는지 확인해주세요.");
        }

        private void SendBeacon()
        {
            NetManager net = NetManager.Instance;
            if (send == null || net == null || !net.IsHost)
                return;

            //여기 오는 건 호스트뿐이다(위에서 걸렀다). PeerCount 에 자기 자신을 더한다
            int current = net.PeerCount + 1;

            string name = (LanRoomConfig.Nickname ?? "").Replace("|", "");
            if (string.IsNullOrEmpty(name))
                name = "방";

            string msg = string.Join("|", new string[]
            {
                MAGIC,
                net.Port.ToString(),
                name,
                ((int)LanRoomConfig.Mode).ToString(),
                current.ToString(),
                LanRoomConfig.HumanCount.ToString(),
                LanRoomConfig.AiCount.ToString()
            });

            byte[] data = Encoding.UTF8.GetBytes(msg);

            int sent = 0;
            string lastError = null;

            for (int i = 0; i < targets.Count; i++)
            {
                try
                {
                    send.Send(data, data.Length, targets[i]);
                    sent++;
                }
                catch (Exception e)
                {
                    lastError = e.Message;
                }
            }

            if (sent == 0)
            {
                Debug.LogWarning("[탐색] 모든 어댑터로 알림을 보내지 못했습니다: " + lastError);
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

            try
            { 
                listen.Close();
            } 
            catch 
            {
            
            }

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

                try
                { 
                    data = listen.Receive(ref from);
                }
                catch 
                {
                    break;
                }

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

            for (int i = 0; i < stale.Count; i++)
                rooms.Remove(stale[i]);
        }
    }
}
