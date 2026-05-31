using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;

[System.Serializable]
public class JellySpawnData
{
    public GameObject jellyPrefab;

    [Tooltip("이 젤리가 나올 확률(가중치)입니다. 숫자가 높을수록 자주 나옵니다.")]
    [Min(0)]
    public float spawnWeight = 10f;
}

public class JellySpawnMachine : MonoBehaviour
{
    [Header("설정")]
    public float checkRadius = 3.0f;
    public LayerMask targetLayer;

    [Header("젤리 소환 설정")]
    [Tooltip("소환할 젤리 프리팹과 각각의 확률(가중치)을 설정해주세요.\n" +
             "멀티플레이 시 jellyPrefab 이름이 Resources 폴더 경로와 일치해야 합니다.")]
    public List<JellySpawnData> spawnJellies;
    public Transform jellySpawnTransform;
    public Rotator rotator;

    void Start()
    {
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            gameObject.SetActive(false);
            return;
        }
        StartCoroutine(ContinuousSpawnLoop());
    }

    IEnumerator ContinuousSpawnLoop()
    {
        while (true)
        {
            // 사운드 + 회전 연출은 모든 클라이언트에서 재생
            float waitTime = PlaySFXAudio.Instance.PlayMachineSound();
            yield return StartCoroutine(rotator.RotateRoutine(waitTime, SpawnRandomJelly));

            float remainingTime = Mathf.Max(0, DataManager.Instance.spawnCoolTime - waitTime);
            yield return new WaitForSeconds(remainingTime);
        }
    }

    void SpawnRandomJelly()
    {
        if (spawnJellies == null || spawnJellies.Count == 0)
        {
            Debug.LogWarning("[JellySpawnMachine] 소환할 젤리 리스트가 비어있습니다!");
            return;
        }

        // 가중치 기반 젤리 선택
        JellySpawnData selected = PickWeighted();
        if (selected == null) return;

        // 멀티플레이: MasterClient만 실제 소환, 나머지는 연출만
        if (NetworkJellyManager.Instance != null)
        {
            if (PhotonNetwork.IsMasterClient)
                NetworkJellyManager.Instance.SpawnJellyAt(selected.jellyPrefab.name, jellySpawnTransform.position);
            return;
        }

        // 싱글플레이 fallback
        Instantiate(selected.jellyPrefab, jellySpawnTransform.position, Quaternion.identity);
    }

    JellySpawnData PickWeighted()
    {
        float totalWeight = 0;
        foreach (var item in spawnJellies) totalWeight += item.spawnWeight;

        float randomValue = Random.Range(0, totalWeight);
        float current = 0;
        foreach (var item in spawnJellies)
        {
            current += item.spawnWeight;
            if (randomValue <= current) return item;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}
