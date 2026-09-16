using System;

namespace JellyNet
{
    /// <summary>
    /// 메시지를 주고받는 통로. LAN 소켓이든 온라인 릴레이든 상위 코드는 이것만 본다.
    ///
    /// ★ 왜 필요한가
    ///   예전엔 상위 코드가 NetManager.Host / NetManager.Client 를 직접 만졌다(47곳).
    ///   그 둘은 TCP 소켓 구현 그 자체라, 온라인 모드를 붙이려면 47곳을 전부
    ///   "지금 LAN인가 온라인인가"로 분기시켜야 했다. 통로를 인터페이스로 세우면
    ///   갈리는 지점은 구현체를 고르는 한 곳뿐이다.
    ///
    /// 여기 없는 것 — 방을 만들고 찾고 참가하는 일은 INetSession 의 몫이다.
    /// 전송은 "이미 연결된 뒤"만 책임진다.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>내 번호. 호스트는 1, 클라는 호스트가 배정한 2 이상. 연결 전엔 0.</summary>
        int MyId { get; }

        bool IsHost { get; }

        /// <summary>
        /// 세션이 서 있는가. "소켓이 살아 있는가"가 아니라 "호스트를 열었거나 참가한 상태인가"다.
        /// 호스트가 죽어 연결이 끊긴 뒤에도 Shutdown 전까지는 참이다 — 그 구간을
        /// "연결 없음"으로 보면 접속 끊김 안내를 띄울 곳이 사라진다.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>호스트에게만 의미가 있다. 클라에서는 0.</summary>
        int PeerCount { get; }

        /// <summary>
        /// 새 접속을 받을지. 한 판의 인원은 로비에서 확정되므로 게임 씬에 들어가면 문을 닫는다.
        /// 클라에서는 아무 뜻이 없다(설정해도 무시된다).
        /// </summary>
        bool AcceptingNewPeers { get; set; }

        /// <summary>호스트 → 모든 클라.</summary>
        void Broadcast(NetWriter w);

        /// <summary>호스트 → 한 명만 빼고 모든 클라. 보낸 사람에게 되돌려주지 않을 때 쓴다.</summary>
        void BroadcastExcept(int exceptPeerId, NetWriter w);

        /// <summary>호스트 → 그 번호의 클라 하나. 없는 번호면 조용히 버린다.</summary>
        void SendTo(int peerId, NetWriter w);

        /// <summary>클라 → 호스트.</summary>
        void SendToHost(NetWriter w);

        // ★ RouteHost·RouteClient·UnrouteHost·UnrouteClient 를 지웠다
        //   라우팅 표가 전송마다 있던 시절, NetManager 가 transport.RouteHost 로 넘기던 통로다.
        //   표를 NetRouteTable 로 빼고(44d253f) NetManager 가 표에 직접 걸게 되면서(1bec093)
        //   이 인터페이스로 들어오는 호출이 하나도 남지 않았다. 구현 둘은 표로 다시 넘기기만 했다.
        //   라우팅은 전송이 무엇이든 같은 답이라 전송의 일이 아니다 — NetManager.RouteHost 를 쓴다.

        //이벤트는 여기 없다. 전송은 생성자로 받은 NetEvents 에 대고 알린다 —
        //왜 인터페이스마다 이벤트를 두지 않는지는 NetEvents 머리말에 적었다.

        /// <summary>
        /// 위치 갱신을 한 메시지로 묶어 보내야 하는 전송인가.
        /// 릴레이는 방당 초당 메시지 수에 한도가 있고, 소켓은 그렇지 않다.
        /// NetWorld 가 이 값을 보고 프레임 끝에 몰아 보낼지 즉시 보낼지 정한다.
        /// </summary>
        bool PrefersBatchedUpdates { get; }

        /// <summary>매 프레임 호출. 받은 것을 읽어 라우팅하고 접속/퇴장을 처리한다.</summary>
        void Poll();

        /// <summary>세션을 닫는다. 라우팅 표와 구독은 살아남는다 — 다음 판에 다시 쓴다.</summary>
        void Shutdown();
    }
}
