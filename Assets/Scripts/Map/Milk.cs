using UnityEngine;
using System.Collections;

public class Milk : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float respawnTime = 5.0f; // 다시 나타날 시간

    [Header("References")]
    [SerializeField] private PlayerColorAbsorb playerColorAbsorb;
    [SerializeField] private MainCamera_Action mainCamera_Action;

    // 초기화 시 컴포넌트 자동 할당 (없을 경우)
    private void Awake()
    {
        if (playerColorAbsorb == null)
            playerColorAbsorb = FindFirstObjectByType<PlayerColorAbsorb>();

        if (mainCamera_Action == null)
            mainCamera_Action = FindFirstObjectByType<MainCamera_Action>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // 플레이어와 충돌했는지 확인
        if (other.CompareTag("PlayerMesh"))
        {
            // 플레이어 스케일 레벨이 1이 아닐 때만 실행
            if (DataManager.Instance.playerCurrentScaleLevel != 1)
            {
                ApplyEffectAndHide();
            }
        }
    }

    private void ApplyEffectAndHide()
    {
        // 1. 효과 실행 (스케일 감소 및 카메라 연출)
        if (playerColorAbsorb != null)
        {
            StartCoroutine(playerColorAbsorb.DecreaseScale(DataManager.Instance.scaleIncreaseTime));
        }

        if (mainCamera_Action != null)
        {
            mainCamera_Action.ScaleDecreased();
        }

        // 2. 우유 오브젝트 숨기기 및 리스폰 코루틴 시작
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // 렌더러와 컬라이더만 꺼서 오브젝트는 '살아있는' 상태로 유지 (그래야 코루틴이 돌아감)
        SetAppearance(false);

        // 5초 대기 (Time.scale의 영향을 받지 않으려면 WaitForSecondsRealtime 사용 가능)
        yield return new WaitForSeconds(respawnTime);

        // 다시 나타나기
        SetAppearance(true);
    }

    // 오브젝트의 외형과 충돌체만 끄고 켜는 함수
    private void SetAppearance(bool active)
    {
        //// MeshRenderer 또는 SpriteRenderer가 있을 경우
        //if (TryGetComponent<Renderer>(out var renderer)) renderer.enabled = active;

        // Collider 끄기
        if (TryGetComponent<Collider>(out var collider)) collider.enabled = active;

        // 만약 자식 오브젝트들이 있다면 자식들도 끄고 켜기
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(active);
        }
    }
}