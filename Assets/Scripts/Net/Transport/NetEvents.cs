using System;

namespace JellyNet
{
    /// <summary>
    /// 네트워크에서 일어난 일을 알리는 게시판. 전송이 무엇이든 하나만 있다.
    /// 쏘는 쪽(전송·세션)도 듣는 쪽(로비·게임 흐름·NetWorld)도 이것 하나만 본다.
    ///
    /// ★ 왜 게시판인가 — 예전엔 같은 이벤트가 네 층에 선언돼 있었다
    ///   NetHost/NetClient → SocketTransport·PhotonTransport → NetManager → 구독자.
    ///   전송마다 OnPeerJoined 를 따로 들고, NetManager 가 그 둘을 다시 받아
    ///   자기 OnPeerJoined 로 되쏘는 중계(RelayFrom·Raise* 7개·StopRelayingFrom)를 했다.
    ///   중계가 필요했던 이유는 전송을 갈아끼우기 때문인데, 그건 NetRouteTable 이
    ///   이미 푼 문제다 — 갈아끼우는 것들이 갈아끼우지 않는 것 하나에 대고 말하면
    ///   중계할 게 없다. 라우팅 표와 같은 모양으로 맞춘다.
    ///
    ///   중계층이 있는 동안 실제로 새던 것:
    ///     · OnRoomListChanged 만 중계에서 빠져 방 목록 UI가 세션을 직접 구독했다.
    ///       로비 구독이 옛 세션에 남아 "연결 중..." 에 갇혔던 것과 같은 모양의 구멍이다.
    ///     · SocketTransport.OnWelcomed, PhotonTransport.OnShutdownRequested 처럼
    ///       세션이 짝 전송의 구체 타입에 손을 넣어 알아내는 옆길이 둘 생겼다.
    ///
    /// ★ '끝남'은 여기서 지킨다 — 세션 하나에 OnDisconnected 는 정확히 한 번
    ///   SocketTransport 는 세션이 섰었으면 언제나 OnDisconnected 를 쐈다. PhotonTransport 는
    ///   "아직 방 안이고 연결돼 있으면"만 쐈다. 그래서 온라인에서 연결이 먼저 끊긴 뒤
    ///   (OnConnectionLost) 나가기를 누르면 OnDisconnected 가 영영 안 나가,
    ///   NetWorld.ClearAll·게임 모드 ResetAll 이 돌지 않은 채 다음 방으로 넘어갔다.
    ///   전송마다 맞게 구현하길 기대하지 않고, 몇 번 불리든 여기서 한 번만 나가게 한다.
    /// </summary>
    public class NetEvents
    {
        #region 이벤트·콜백

        #region 방에 들어가고 사람이 오가는 일

        /// <summary>내가 방을 열었다(호스트가 됐다).</summary>
        public event Action OnHostStarted;

        /// <summary>
        /// 방에 실제로 들어갔다. 요청을 보낸 것과 다르다 — LAN 클라는 호스트의 환영 인사가,
        /// 온라인은 릴레이의 입장 확인이 와야 들어간 것이다.
        /// </summary>
        public event Action OnRoomEntered;

        /// <summary>다른 사람이 들어왔다. 인자는 그 사람의 번호.</summary>
        public event Action<int> OnPeerJoined;

        /// <summary>다른 사람이 나갔다. 인자는 그 사람의 번호.</summary>
        public event Action<int> OnPeerLeft;

        #endregion

        #region 끝나는 일

        /// <summary>판을 접으라는 요청이 나갔다. 아직 끝난 게 아니다 — 하려던 일을 취소할 때 쓴다.</summary>
        public event Action OnShutdownRequested;

        /// <summary>세션이 끝났다. 세션 하나에 정확히 한 번. 뒷정리는 여기에 건다.</summary>
        public event Action OnDisconnected;

        /// <summary>
        /// 상대가 먼저 사라졌다(호스트 강제 종료·연결 끊김). 세션 하나에 최대 한 번.
        /// 이것만으로 세션이 끝나지는 않는다 — 안내를 띄우는 자리이고, 뒷정리는 이어서 오는
        /// OnDisconnected 가 한다.
        /// </summary>
        public event Action OnConnectionLost;

        #endregion

        #region 방을 찾고 만드는 일

        /// <summary>방 목록에서 화면에 보이는 값이 하나라도 바뀌었다.</summary>
        public event Action OnRoomListChanged;

        /// <summary>방 만들기·참가가 실패했다. 인자는 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public event Action<string> OnSessionFailed;

        #endregion

        #region 상태

        #endregion

        #region 세션 괄호

        //열린 세션이 있는가. OnDisconnected 를 한 번만 내보내는 근거다
        private bool sessionOpen;

        //이번 세션에서 이미 '상대가 사라졌다'를 알렸는가
        private bool lostReported;

        #endregion

        #endregion

        /// <summary>세션이 섰다. 전송이 방을 열거나 방에 붙은 순간 부른다.</summary>
        public void OpenSession()
        {
            sessionOpen = true;
            lostReported = false;
        }

        public void RaiseHostStarted() { OnHostStarted?.Invoke(); }

        public void RaiseRoomEntered() { OnRoomEntered?.Invoke(); }

        public void RaisePeerJoined(int peerId) { OnPeerJoined?.Invoke(peerId); }

        public void RaisePeerLeft(int peerId) { OnPeerLeft?.Invoke(peerId); }

        public void RaiseShutdownRequested() { OnShutdownRequested?.Invoke(); }

        /// <summary>세션을 닫는다. 열린 세션이 없으면 아무것도 하지 않는다.</summary>
        public void RaiseDisconnected()
        {
            if (!sessionOpen)
                return;

            //알리기 전에 닫는다. 구독자 안에서 다시 Shutdown 이 불려도 두 번 나가지 않는다
            sessionOpen = false;
            OnDisconnected?.Invoke();
        }

        /// <summary>상대가 사라졌다. 열린 세션이 없거나 이미 알렸으면 아무것도 하지 않는다.</summary>
        public void RaiseConnectionLost()
        {
            if (!sessionOpen || lostReported)
                return;

            lostReported = true;
            OnConnectionLost?.Invoke();
        }

        public void RaiseRoomListChanged() { OnRoomListChanged?.Invoke(); }

        public void RaiseSessionFailed(string reason)
        {
            OnSessionFailed?.Invoke(string.IsNullOrEmpty(reason) ? "알 수 없는 이유로 실패했습니다." : reason);
        }
    }
}
