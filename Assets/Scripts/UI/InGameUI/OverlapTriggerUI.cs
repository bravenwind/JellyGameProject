using UnityEngine;
using System.Collections;
using System.Collections.Generic; // List 사용을 위해 필수

public class OverlapTriggerUI : MonoBehaviour
{
    [Header("설정")]
    public GameObject uiObject;
    public float checkRadius = 3.0f;
    public LayerMask targetLayer;

    [Header("젤리 소환")]
    [Tooltip("소환할 젤리 프리팹들을 여기에 넣어주세요.")]
    public List<GameObject> spawnJellies; // ★ 단일 GameObject에서 List로 변경
    public Transform jellySpawnTransform;
    public Rotator rotator;

    private bool isProcessing = false;

    void Start()
    {
        if (uiObject != null) uiObject.SetActive(false);

        // 시작하자마자 무한 스폰 루프 실행
        StartCoroutine(ContinuousSpawnLoop());
    }

    // 🔴 [수정됨] Update 대신 코루틴 하나로 무한 반복 처리
    IEnumerator ContinuousSpawnLoop()
    {
        while (true) // 무한 루프 (오브젝트가 파괴되면 자동으로 멈춤)
        {
            isProcessing = true;

            // 1. 소리 재생하고 길이 받아오기
            float waitTime = PlayFXAudio.Instance.PlayMachineSound();

            // 2. 소리 길이만큼 대기 & 회전 연출 (끝나면 SpawnRandomJelly 호출)
            yield return StartCoroutine(rotator.RotateRoutine(waitTime, SpawnRandomJelly));

            isProcessing = false;

            // 다음 실행까지 아주 짧은 대기 (프레임 드랍 방지)
            yield return null;
        }
    }

    // ★ [핵심] 리스트에서 랜덤으로 하나를 뽑아 소환하는 함수
    void SpawnRandomJelly()
    {
        // 안전장치: 리스트가 비어있으면 실행 안 함
        if (spawnJellies == null || spawnJellies.Count == 0)
        {
            Debug.LogWarning("[OverlapTriggerUI] 소환할 젤리 리스트가 비어있습니다!");
            return;
        }

        // 0부터 리스트의 개수 - 1 사이의 랜덤한 숫자(인덱스)를 뽑음
        int randomIndex = Random.Range(0, spawnJellies.Count);

        // 뽑힌 인덱스의 젤리 소환
        Instantiate(spawnJellies[randomIndex], jellySpawnTransform.position, Quaternion.identity);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}