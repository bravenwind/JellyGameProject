using System;
using System.Collections.Generic;

namespace JellyNet
{
    public class RoomEntry
    {
        // 로컬은 "ip:port", 온라인은 방 이름 (호스트 닉네임)
        public string Id;

        public string Address;

        public string HostName;
        public GameModeType Mode;

        public int Current;
        public int Needed;

        public int AiCount;

        public bool IsFull { get { return Current >= Needed; } }
    }

    public struct RoomSetup
    {
        public string RoomName;

        public int LocalPort;
    }

    public interface INetSession
    {
        bool CreateRoom(RoomSetup options);

        bool JoinRoom(RoomEntry room);

        IEnumerable<RoomEntry> Rooms { get; }

        bool IsBrowseReady { get; }

        void StartBrowsing();

        void StopBrowsing();

        void StopAdvertising();

        bool IsLocal { get; }
    }
}
