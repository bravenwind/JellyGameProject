using System;
using System.Collections.Generic;

namespace JellyNet
{
    /// <summary>
    /// 방 하나를 가리키는 표. 어떤 전송으로 찾았든 화면은 이것만 본다.
    ///
    /// ★ 예전엔 LanDiscovery.RoomInfo 가 그대로 UI까지 올라갔다
    ///   그 타입은 UDP 비콘의 해석 결과라 Ip·Port 를 품고 있었고, 방 목록 UI가
    ///   그걸 읽어 LanLobby.JoinRoom(ip, port) 를 불렀다. 온라인에는 IP도 포트도 없다.
    /// </summary>
    public class RoomHandle
    {
        /// <summary>
        /// 전송이 이 방을 다시 찾는 데 쓰는 값. LAN 은 "ip:port", 온라인은 방 이름.
        /// 바깥은 그대로 돌려주기만 하고 뜻을 해석하지 않는다.
        /// </summary>
        public string Id;

        /// <summary>화면에 보여줄 주소. LAN 은 "192.168.0.5:7777".</summary>
        public string Address;

        public string HostName;
        public GameModeType Mode;

        /// <summary>지금 들어와 있는 사람 수(호스트 포함).</summary>
        public int Current;

        /// <summary>판이 시작되려면 차야 하는 사람 수.</summary>
        public int Needed;

        public int AiCount;

        public bool IsFull { get { return Current >= Needed; } }
    }

    /// <summary>방을 만들 때 넘기는 값. 전송마다 쓰는 것이 다르므로 안 쓰는 항목은 무시한다.</summary>
    public struct RoomOptions
    {
        /// <summary>방 제목. 목록에 뜨는 이름이다.</summary>
        public string RoomName;

        /// <summary>
        /// 로컬(LAN) 전용 — 열 TCP 포트. 온라인 구현은 무시한다.
        /// 판의 모드·인원은 LanRoomConfig 가 들고 있으므로 여기 넣지 않는다.
        /// </summary>
        public int LocalPort;
    }

    /// <summary>
    /// 방을 만들고 찾고 참가하는 일. 연결이 서기 "전"을 책임진다.
    ///
    /// 연결이 선 다음의 주고받기는 INetTransport 의 몫이다. 둘을 나눈 이유는
    /// 갈리는 방식이 다르기 때문이다 — LAN 은 UDP 비콘으로 방을 찾고 TCP 로 붙지만,
    /// 온라인은 로비 서버가 목록을 주고 릴레이로 붙는다. 반면 붙은 뒤의 메시지 모양은 같다.
    /// </summary>
    public interface INetSession
    {
        /// <summary>
        /// 방을 연다. 요청이 성립하지 못하면 false 이고, 사유는 OnFailed 로 나간다.
        /// (LAN 은 여기서 결과가 확정되지만, 온라인은 요청만 나가므로 반환값을
        ///  "성공"이 아니라 "요청이 성립했는가"로 읽어야 한다)
        /// </summary>
        bool CreateRoom(RoomOptions options);

        /// <summary>목록에서 고른 방에 붙는다. 실패 사유는 OnFailed 로 나간다.</summary>
        bool JoinRoom(RoomHandle room);

        /// <summary>지금까지 찾은 방들. StartBrowsing 을 부르기 전에는 비어 있다.</summary>
        IEnumerable<RoomHandle> Rooms { get; }

        /// <summary>방 찾기를 시작한다(방 목록 화면을 열 때).</summary>
        void StartBrowsing();

        /// <summary>방 찾기를 멈춘다. 목록도 비운다.</summary>
        void StopBrowsing();

        /// <summary>
        /// 내 방을 목록에 그만 띄운다. 판이 시작돼 더는 사람을 받지 않을 때 부른다.
        /// 연결은 그대로 유지된다 — 방을 닫는 것과 판을 끝내는 것은 다른 일이다.
        /// (LAN 은 UDP 비콘을 끄고, 온라인이라면 방을 목록에서 감춘다)
        /// </summary>
        void StopAdvertising();

        /// <summary>방 목록에서 화면에 보이는 값이 하나라도 바뀌었다.</summary>
        event Action OnRoomListChanged;

        /// <summary>방 만들기·참가가 실패했다. 인자는 화면에 그대로 띄울 수 있는 문장이다.</summary>
        event Action<string> OnFailed;

        /// <summary>
        /// 같은 기계·같은 랜에서만 도는 세션인가. 화면이 로컬 전용 입력(포트 등)을
        /// 보여줄지 정하는 데 쓴다.
        /// </summary>
        bool IsLocal { get; }
    }
}
