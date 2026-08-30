using System;
using UnityEngine;

/// <summary>
/// 젤리를 먹는 쪽. 사람·봇 프리팹 <b>둘 다</b>의 루트에 붙는다.
///
/// ★ PlayerAbsorbingManager를 여기로 흡수했다
///   그 클래스가 하던 일은 OnJellyEaten을 받아 색과 크기에 넘기는 <b>배선</b>뿐이었다.
///   구독자가 이 하나로 고정이고, 같은 프리팹의 <b>같은 오브젝트</b>에 붙어 수명도 같았다.
///   이벤트는 '발행자가 구독자를 몰라도 되게' 하려고 쓰는 건데 그 이점이 없는 자리였다.
///
/// ★ OnJellyScored는 남긴다 — 이쪽은 진짜 이벤트다
///   색·크기는 사람과 봇이 똑같이 처리하지만, HUD·효과음·팝업은 <b>사람만</b> 한다.
///   그 처리는 사람 전용인 PlayerBridge에 있고 봇에는 구독자가 없는 게 정상이다.
///   "누가 들을지 모른다"가 성립하므로 느슨하게 두는 게 맞다.
/// </summary>
public class PlayerAbsorber : MonoBehaviour
{
    /// <summary>젤리를 먹었다 — 사람 화면의 HUD·효과음·팝업용. 봇은 구독자가 없다.</summary>
    public Action OnJellyScored;

    private PlayerColorVisual colorVisual;
    private PlayerScaleController scaleController;

    private void Awake()
    {
        colorVisual = GetComponentInChildren<PlayerColorVisual>();
        scaleController = GetComponentInChildren<PlayerScaleController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (GameState.CurrentGameMode == GameModeType.Push)
            return;

        // 시작 카운트다운(Phase=None) 동안과 게임 종료 후엔 젤리 흡수도 막는다. 카운트다운에는
        // 입력 잠금·봇 정지·타일 붕괴 정지·플레이어간 흡수(Phase!=Playing) 차단이 모두 걸려 있는데,
        // 젤리 흡수만 빠져 있어 배회하던 젤리가 정지한 플레이어에 닿으면 '시작!' 전에 성장했다.
        if (GameState.Phase != GamePhase.Playing)
            return;

        if (!other.CompareTag(GameTags.Edible))
            return;

        JellyColliderAbsorb jca = other.GetComponentInParent<JellyColliderAbsorb>();

        if (jca != null)
            jca.StartAbsorb(transform);

        Rigidbody rb = other.GetComponentInParent<Rigidbody>();

        if (rb != null)
            rb.constraints = RigidbodyConstraints.None;

        other.isTrigger = true;
    }

    /// <summary>
    /// 호스트가 승인한 흡수를 반영한다. 보상은 이 경로로만 들어온다.
    ///
    /// 연출은 클라가 미리 보여주지만 색·크기·점수는 호스트 확정을 기다린다 —
    /// 그래야 두 명이 같은 젤리를 동시에 먹어도 한 명만 성장한다.
    /// </summary>
    public void AbsorbColor(JellyColorType type)
    {
        OnJellyScored?.Invoke();

        //색과 크기는 사람·봇이 같다. 소유권을 볼 필요도 없다 —
        //호스트가 '이 개체가 먹었다'고 확정해 보낸 것이기 때문이다
        if (colorVisual != null)
            colorVisual.HandleJellyAbsorbed(type);

        if (scaleController != null)
            scaleController.GrowByJelly();
    }
}
