using System;
using UnityEngine;

public class PlayerAbsorber : MonoBehaviour
{
    public Action<JellyColorType> OnJellyEaten;
    public Action OnJellyScored;

    private void OnTriggerEnter(Collider other)
    {
        if (GameState.CurrentGameMode == GameModeType.Push) return;

        // 시작 카운트다운(Phase=None) 동안과 게임 종료 후엔 젤리 흡수도 막는다. 카운트다운에는
        // 입력 잠금·봇 정지·타일 붕괴 정지·플레이어간 흡수(Phase!=Playing) 차단이 모두 걸려 있는데,
        // 젤리 흡수만 빠져 있어 배회하던 젤리가 정지한 플레이어에 닿으면 '시작!' 전에 성장했다.
        if (GameState.Phase != GamePhase.Playing) return;

        if (other.CompareTag("Edible"))
        {
            JellyColliderAbsorb jca = other.GetComponentInParent<JellyColliderAbsorb>();
            if (jca != null) jca.StartAbsorb(transform);

            Rigidbody rb = other.GetComponentInParent<Rigidbody>();
            if (rb != null) rb.constraints = RigidbodyConstraints.None;
            other.isTrigger = true;
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        OnJellyScored?.Invoke();
        OnJellyEaten?.Invoke(type);
    }
}
