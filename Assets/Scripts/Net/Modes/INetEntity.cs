using UnityEngine;

namespace JellyNet
{
    public interface INetEntity
    {
        NetIdentity Identity { get; }

        int EntityId { get; }

        Transform Transform { get; }

        // 봇은 전부 호스트(1)
        int OwnerId { get; }

        bool IsBot { get; }

        string DisplayName { get; }

        float ScaleValue { get; }

        int Score { get; }

        Color VisualColor { get; }

        bool IsOutOfPlay { get; }

        void HostAddScore(int delta);
    }
}
