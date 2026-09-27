using System;

namespace JellyNet
{
    public class NetEvents
    {
        public event Action OnHostStarted;

        public event Action OnRoomEntered;

        public event Action<int> OnPeerJoined;
        public event Action<int> OnPeerLeft;

        public event Action OnShutdownRequested;

        public event Action OnDisconnected;

        public event Action OnConnectionLost;

        public event Action OnRoomListChanged;

        public event Action<string> OnSessionFailed;

        private bool sessionOpen;
        private bool lostReported;

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

        public void RaiseDisconnected()
        {
            if (!sessionOpen)
                return;

            sessionOpen = false;
            OnDisconnected?.Invoke();
        }

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
