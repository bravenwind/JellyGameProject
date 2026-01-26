using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 🔥 [추가] 젤리 프리팹과 가중치를 함께 담을 클래스
[System.Serializable]
public class JellySpawnData
{
    public GameObject jellyPrefab;

    [Tooltip("이 젤리가 나올 확률(가중치)입니다. 숫자가 높을수록 자주 나옵니다.")]
    [Min(0)]
    public float spawnWeight = 10f;
}

public class OverlapTriggerUI : MonoBehaviour
{
    [Header("설정")]
    public GameObject uiObject;
    public float checkRadius = 3.0f;
    public LayerMask targetLayer;

    [Header("젤리 소환 설정")]
    [Tooltip("소환할 젤리 프리팹과 각각의 확률(가중치)을 설정해주세요.")]
    // 🔥 [수정] GameObject 리스트에서 JellySpawnData 리스트로 변경
    public List<JellySpawnData> spawnJellies;
    public Transform jellySpawnTransform;
    public Rotator rotator;

    private bool isProcessing = false;

    void Start()
    {
        if (uiObject != null) uiObject.SetActive(false);

        // 시작하자마자 무한 스폰 루프 실행
        StartCoroutine(ContinuousSpawnLoop());
    }

    IEnumerator ContinuousSpawnLoop()
    {
        while (true)
        {
            isProcessing = true;

            // 1. 소리 재생하고 길이 받아오기
            float waitTime = PlaySFXAudio.Instance.PlayMachineSound();

            // 2. 소리 길이만큼 대기 & 회전 연출 후 젤리 소환
            yield return StartCoroutine(rotator.RotateRoutine(waitTime, SpawnRandomJelly));

            isProcessing = false;

            // 3. 주기 맞추기
            float remainingTime = Mathf.Max(0, DataManager.Instance.spawnCoolTime - waitTime);

            yield return new WaitForSeconds(remainingTime);
        }
    }

    // 🔥 [수정] 가중치 기반 랜덤 스폰 로직 적용
    void SpawnRandomJelly()
    {
        if (spawnJellies == null || spawnJellies.Count == 0)
        {
            Debug.LogWarning("[OverlapTriggerUI] 소환할 젤리 리스트가 비어있습니다!");
            return;
        }

        // 1. 모든 젤리의 가중치 총합 계산
        float totalWeight = 0;
        foreach (var item in spawnJellies)
        {
            totalWeight += item.spawnWeight;
        }

        // 2. 0부터 가중치 총합 사이에서 랜덤한 값 하나 뽑기
        float randomValue = Random.Range(0, totalWeight);

        // 3. 랜덤 값에 해당하는 젤리 찾기
        float currentWeight = 0;
        foreach (var item in spawnJellies)
        {
            currentWeight += item.spawnWeight;

            // 랜덤 값이 현재 가중치 누적값보다 작거나 같으면 해당 젤리 당첨!
            if (randomValue <= currentWeight)
            {
                Instantiate(item.jellyPrefab, jellySpawnTransform.position, Quaternion.identity);
                return;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}