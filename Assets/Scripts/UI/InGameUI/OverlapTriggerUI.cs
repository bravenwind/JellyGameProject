using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class OverlapTriggerUI : MonoBehaviour
{
    [Header("설정")]
    public GameObject uiObject;
    public float checkRadius = 3.0f;
    public LayerMask targetLayer;

    [Header("젤리 소환")]
    [Tooltip("소환할 젤리 프리팹들을 여기에 넣어주세요.")]
    public List<GameObject> spawnJellies;
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

            // 1. 소리 재생하고 길이 받아오기 (예: 2초 걸린다고 가정)
            float waitTime = PlaySFXAudio.Instance.PlayMachineSound();

            // 2. 소리 길이만큼 대기 & 회전 연출 후 젤리 소환
            yield return StartCoroutine(rotator.RotateRoutine(waitTime, SpawnRandomJelly));

            isProcessing = false;

            // ★ 3. 10초 주기 맞추기 (10초 - 연출에 걸린 시간)
            // 연출이 2초 걸렸다면, 8초만 더 기다려서 정확히 10초 주기를 맞춥니다.
            float remainingTime = Mathf.Max(0, DataManager.Instance.spawnCoolTime - waitTime);

            yield return new WaitForSeconds(remainingTime);
        }
    }

    void SpawnRandomJelly()
    {
        if (spawnJellies == null || spawnJellies.Count == 0)
        {
            Debug.LogWarning("[OverlapTriggerUI] 소환할 젤리 리스트가 비어있습니다!");
            return;
        }

        int randomIndex = Random.Range(0, spawnJellies.Count);
        Instantiate(spawnJellies[randomIndex], jellySpawnTransform.position, Quaternion.identity);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}