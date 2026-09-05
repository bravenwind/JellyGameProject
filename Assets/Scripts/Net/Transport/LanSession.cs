using System;
using System.Collections.Generic;

namespace JellyNet
{
    /// <summary>
    /// UDP 비콘(LanDiscovery)과 TCP 접속(LanTransport)으로 INetSession 을 구현한다.
    ///
    /// 예전엔 이 일이 세 군데 흩어져 있었다 — 포트·IP 는 NetManager 의 필드였고,
    /// 비콘 켜고 끄기는 LanLobby 가 LanDiscovery 를 직접 불렀고, 방 목록은 UI가
    /// LanDiscovery.Instance 에서 직접 읽었다. 방을 만들고 찾는 일을 한 자리에 모으면
    /// 온라인 구현은 이 인터페이스만 다시 채우면 된다.
    /// </summary>
    public class LanSession : INetSession
    {
        private readonly LanTransport transport;

        //지금 쓰는 포트. 인스펙터의 기본값에서 출발해 방을 만들 때 갱신된다.
        //참가할 때 쓰는 주소는 고른 방(RoomHandle)에서 나오므로 따로 들고 있지 않는다
        private int port;

        public event Action OnRoomListChanged;
        public event Action<string> OnFailed;
        public event Action OnRoomReady;

        public bool IsLocal { get { return true; } }

        //UDP 를 듣기 시작하면 곧바로 준비된 것이다. 붙을 서버가 없다
        public bool IsBrowseReady { get { return LanDiscovery.Instance != null; } }

        public LanSession(LanTransport transport, int defaultPort)
        {
            this.transport = transport;
            port = defaultPort;

            //방이 닫히면 알리기도 멈춘다. 호출부가 따로 기억해야 하는 일로 두면
            //취소 경로 하나를 빠뜨렸을 때 없는 방이 목록에 계속 떠 있게 된다
            this.transport.OnDisconnected += StopAdvertising;

            //클라는 호스트의 환영 인사(= 내 번호 배정)를 받아야 방에 들어간 것이다.
            //호스트는 StartHost 가 성공한 순간이라 CreateRoom 에서 직접 알린다
            this.transport.OnWelcomed += RaiseRoomReady;
        }

        private void RaiseRoomReady()
        {
            OnRoomReady?.Invoke();
        }

        // ─────────────────────────────────────────────────────────
        //  방 만들기 · 참가
        // ─────────────────────────────────────────────────────────

        public bool CreateRoom(RoomOptions options)
        {
            if (options.LocalPort > 0)
                port = options.LocalPort;

            if (!transport.StartHost(port))
            {
                Fail(transport.LastError);
                return false;
            }

            //비콘은 호스트가 실제로 연 포트를 알린다. 예전엔 LanDiscovery 가
            //NetManager.Port 를 따로 읽었는데, 그러면 출처가 둘이 된다
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StartBeacon(port);

            //호스트는 남의 승인을 기다릴 게 없다. 포트가 열린 순간 방이다
            RaiseRoomReady();
            return true;
        }

        public bool JoinRoom(RoomHandle room)
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

        //Id 는 LAN 에서 "ip:port" 다. 마지막 ':' 로 자른다 — IPv6 는 콜론이 여러 개다
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

        // ─────────────────────────────────────────────────────────
        //  방 찾기
        // ─────────────────────────────────────────────────────────

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

        /// <summary>전송에 건 구독을 푼다. NetManager 가 죽을 때 부른다(건 자리에서 짝을 맞춘다).</summary>
        public void Unhook()
        {
            transport.OnDisconnected -= StopAdvertising;
        }

        public void StopAdvertising()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StopBeacon();
        }

        //목록은 초당 몇 번씩 읽히므로 매번 새 리스트를 만들지 않는다
        private readonly List<RoomHandle> handles = new List<RoomHandle>();

        public IEnumerable<RoomHandle> Rooms { get { return handles; } }

        /// <summary>
        /// 방 목록을 훑어 바뀌었으면 다시 짓고 알린다. NetManager 가 매 프레임 부른다.
        ///
        /// LanDiscovery 는 "바뀌었다"를 알려주지 않고 사전만 들고 있어서 여기서 훑는다.
        /// 매 프레임 도는 자리라 바뀌지 않았으면 아무것도 만들지 않는다 — 예전에
        /// 목록 UI가 초당 4번 새 List 를 만들던 것과 같은 실수를 여기서 되풀이하지 않는다.
        /// </summary>
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
                handles.Add(new RoomHandle
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

            OnRoomListChanged?.Invoke();
        }

        //화면에 보이는 값만 섞는다. LastSeen 처럼 매 프레임 변하는 건 넣지 않는다 —
        //넣으면 바뀐 게 없어도 계속 '바뀌었다'가 나간다.
        //문자열로 이어붙이면 매 프레임 쓰레기가 생기므로 정수로만 굴린다
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

        //방이 하나도 없을 때의 Signature 값과 겹치지 않도록 처음엔 일부러 다른 값을 둔다.
        //겹치면 목록이 빈 채로 시작할 때 첫 알림이 나가지 않는다
        private int signature = -1;

        private void Fail(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                reason = "알 수 없는 이유로 실패했습니다.";

            OnFailed?.Invoke(reason);
        }
    }
}
