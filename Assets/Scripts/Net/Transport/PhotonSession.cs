// ─────────────────────────────────────────────────────────────────────
//  Photon Realtime 세션(방 만들기·찾기·참가) — 아직 뼈대만 있다
// ─────────────────────────────────────────────────────────────────────
//
//  PhotonTransport.cs 의 머리말에 적은 TODO(사람) 목록이 이 파일에도 그대로 적용된다.
//  SDK가 없는 지금은 통째로 없는 파일과 같다.

#if JELLY_PHOTON

using System;
using System.Collections.Generic;
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
    public class PhotonSession : INetSession
    {
        private readonly PhotonTransport transport;

        public event Action OnRoomListChanged;
        public event Action<string> OnFailed;

        //TODO(사람): OnCreatedRoom / OnJoinedRoom 콜백에서 이걸 쏜다.
        //           로비는 그때까지 "연결 중..." 을 띄우고 기다린다
        public event Action OnRoomReady;

        //온라인이므로 로컬 전용 입력(포트)은 화면에서 감춰야 한다
        public bool IsLocal { get { return false; } }

        public PhotonSession(PhotonTransport transport)
        {
            this.transport = transport;
        }

        // ═══════════════════════════════════════════════════════
        //  방 만들기 · 참가
        // ═══════════════════════════════════════════════════════

        public bool CreateRoom(RoomOptions options)
        {
            //TODO(사람): OpCreateRoom 을 부른다. options.LocalPort 는 무시한다(포트가 없다).
            //
            //  방 속성(CustomRoomProperties)에 목록에 보여줄 값을 실어야 한다.
            //  LAN 은 비콘 문자열에 실어 보냈던 것들이다 —
            //      모드(LanRoomConfig.Mode), 총원(TotalPlayers), 봇 수(AiCount), 방 이름
            //  그리고 그중 목록에서 보여야 하는 키는 CustomRoomPropertiesForLobby 에
            //  넣어야 로비까지 따라온다. 안 넣으면 방에 들어가기 전엔 못 읽어서
            //  목록의 '3/4명' 칸이 비어 보인다.
            //
            //  ★ 반환값의 뜻이 LAN 과 다르다
            //    LAN 은 여기서 결과가 확정되지만 Photon 은 요청만 나간다.
            //    성공은 OnCreatedRoom/OnJoinedRoom 콜백으로 나중에 온다.
            //    지금 로비(LanLobby.OnClickGenerate)는 true 를 받으면 곧바로 대기 화면으로
            //    넘어가는데, 그 상태로 실패 콜백이 오면 OnFailed 가 대기 화면 위에 뜬다.
            //    이건 해결됐다 — INetSession.OnRoomReady 가 생겼고, 로비는 요청이
            //    나간 뒤 "연결 중..." 을 띄운 채 그 신호를 기다린다. 여기서는 방에
            //    실제로 들어간 콜백에서 OnRoomReady 를 쏘기만 하면 된다.
            throw new NotImplementedException("TODO(사람)");
        }

        public bool JoinRoom(RoomHandle room)
        {
            //TODO(사람): OpJoinRoom(room.Id) — LAN 과 달리 Id 는 방 이름이다.
            //           위 CreateRoom 과 같은 '나중에 오는 실패' 문제가 그대로 있다
            throw new NotImplementedException("TODO(사람)");
        }

        // ═══════════════════════════════════════════════════════
        //  방 찾기
        // ═══════════════════════════════════════════════════════

        private readonly List<RoomHandle> handles = new List<RoomHandle>();

        public IEnumerable<RoomHandle> Rooms { get { return handles; } }

        public void StartBrowsing()
        {
            //TODO(사람): OpJoinLobby. 목록은 OnRoomListUpdate 콜백으로 들어온다.
            //           LAN 처럼 우리가 훑을 필요가 없다
            throw new NotImplementedException("TODO(사람)");
        }

        public void StopBrowsing()
        {
            //TODO(사람): OpLeaveLobby 후 handles.Clear()
            throw new NotImplementedException("TODO(사람)");
        }

        public void StopAdvertising()
        {
            //TODO(사람): CurrentRoom.IsVisible = false (그리고 보통 IsOpen = false 도 같이).
            //           연결은 끊지 않는다 — 판이 시작됐을 뿐 방은 살아 있어야 한다
            throw new NotImplementedException("TODO(사람)");
        }

        /// <summary>Photon 이 준 방 목록을 RoomHandle 로 옮긴다.</summary>
        private void ApplyRoomList(List<RoomInfo> photonRooms)
        {
            //TODO(사람): Photon 의 RoomInfo 는 '사라진 방'을 RemovedFromList 로 알린다.
            //           그 방을 목록에서 빼지 않으면 없는 방이 계속 떠 있다.
            //
            //  옮기는 규칙:
            //      Id       = info.Name
            //      Address  = info.Name           (온라인은 보여줄 주소가 방 이름뿐이다)
            //      HostName = 방 속성의 방 이름
            //      Mode / Needed / AiCount = 방 속성에서
            //      Current  = info.PlayerCount
            handles.Clear();
            OnRoomListChanged?.Invoke();

            throw new NotImplementedException("TODO(사람)");
        }

        private void Fail(string reason)
        {
            OnFailed?.Invoke(string.IsNullOrEmpty(reason) ? "알 수 없는 이유로 실패했습니다." : reason);
        }
    }
}

#endif
