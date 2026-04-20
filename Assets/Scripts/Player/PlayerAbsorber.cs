using System;
using UnityEngine;

public class PlayerAbsorber : MonoBehaviour
{
    public Action<JellyColorType> OnJellyEaten;
    public Action OnResetRequested;

    [Header("Detection Settings")]
    public Transform detectTransform;
    public float playerBaseHeight = 1.5f;

    public bool isBot = false;

    // ── 통합된 젤리 흡수 로직 (사람 & 봇 공통) ──
    private void OnTriggerEnter(Collider other)
    {
        // 부딪힌 대상이 맵에 깔린 젤리("Edible")일 때만 작동
        if (other.CompareTag("Edible"))
        {
            JellyColliderAbsorb jca = other.GetComponentInParent<JellyColliderAbsorb>();
            if (jca != null) jca.StartAbsorb(transform);

            // 젤리의 물리 반응 해제 (중복 흡수 방지 등)
            Rigidbody rb = other.GetComponentInParent<Rigidbody>();
            if (rb != null) rb.constraints = RigidbodyConstraints.None;
            other.isTrigger = true;
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        if (!isBot)
        {
            DataManager.Instance.currentScore += DataManager.Instance.scorePerJelly;
            GetComponent<NetworkPlayerSync>().SyncScore(DataManager.Instance.currentScore);
            UIPoolManager.Instance?.SpawnUI(UIType.JellyEat);
            if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayColorMixSound();
            if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
                DataManager.Instance.missions[1].missionCleared = true;
        }
        else
        {
            // 봇도 젤리를 먹으면 점수 누적 → 나중에 플레이어/봇이 이 봇을 흡수할 때 가져감
            AIPlayerMovement ai = GetComponentInParent<AIPlayerMovement>();
            if (ai != null) ai.currentScore += DataManager.Instance.scorePerJelly;
        }

        OnJellyEaten?.Invoke(type);
    }
}