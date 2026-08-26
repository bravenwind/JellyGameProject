using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 판에 참가한 개체 하나 — 사람이든 봇이든.
    ///
    /// ★ 왜 만들었나
    ///   순위표·AI 표적 선정·탈락 판정이 전부 "사람 목록 한 번, 봇 목록 한 번"
    ///   두 벌 루프로 돼 있었다. 두 벌이면 한쪽만 고쳐지는 일이 반드시 생긴다 —
    ///   실제로 봇 점수가 클라 순위표에서 0으로 보이던 버그가 그렇게 났다
    ///   (사람 쪽은 점수를 방송하는데 봇 쪽은 안 했다).
    ///
    ///   질문이 같으면 창구도 같아야 한다. 구현하는 쪽은 둘이다.
    ///     사람 → LanPlayerState
    ///     봇   → LanBotState
    ///
    /// ★ 여기에 없는 것
    ///   '어떻게 움직이는가'는 넣지 않았다. 사람은 입력, 봇은 NavMesh라
    ///   공통으로 물어볼 게 없다. 이 인터페이스는 **밖에서 물어보는 것**만 담는다.
    /// </summary>
    public interface INetEntity
    {
        /// <summary>네트워크 신원. 없으면 이 개체는 아직 스폰이 안 끝난 것이다.</summary>
        NetIdentity Identity { get; }

        int EntityId { get; }

        /// <summary>
        /// 이 개체의 위치. 거리·표적·발판 판정이 전부 이걸 본다.
        ///
        /// ★ 왜 인터페이스에 있나
        ///   MonoBehaviour의 transform을 쓰려면 구체 타입을 알아야 한다.
        ///   그것 하나 때문에 밖에서 사람 목록·봇 목록을 따로 돌고 있었다.
        /// </summary>
        Transform Transform { get; }

        /// <summary>이 개체를 책임지는 기계. 봇은 전부 호스트(1)다.</summary>
        int OwnerId { get; }

        bool IsBot { get; }

        /// <summary>순위표·이름표에 띄울 이름.</summary>
        string DisplayName { get; }

        /// <summary>판정에 쓰는 크기. 출처는 PlayerScaleController 하나다.</summary>
        float ScaleValue { get; }

        int Score { get; }

        /// <summary>지금 몸에 칠해진 색. 순위표 점 색깔에 쓴다.</summary>
        Color VisualColor { get; }

        /// <summary>탈락했거나 흡수당하는 중. "이 개체가 판에서 빠졌나"의 단일 출처.</summary>
        bool IsOutOfPlay { get; }

        /// <summary>호스트만. 점수를 더한다.</summary>
        void HostAddScore(int delta);
    }
}
