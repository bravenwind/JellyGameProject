using UnityEngine;
using System.Collections;
using Photon.Pun;

public class Milk : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float respawnTime = 5.0f; // 다시 나타날 시간

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerMesh")) return;

        // 멀티플레이어: 로컬(혹은 MasterClient 권한) 오브젝트만 처리
        NetworkPlayerSync nps = other.GetComponentInParent<NetworkPlayerSync>();
        if (nps != null && !nps.photonView.IsMine) return;

        PlayerScaleController sc = other.GetComponentInParent<PlayerScaleController>();
        if (sc == null) return;

        // 봇이 아닐 때만 오디오 처리
        if (!sc.isBot && PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.isSteppingMilk = true;

        // 봇은 BotScaleLevel, 플레이어는 DataManager로 현재 레벨 확인
        int currentLevel = sc.isBot ? sc.BotScaleLevel : DataManager.Instance.playerCurrentScaleLevel;

        if (currentLevel != 1)
        {
            sc.QueueScaleChange(sc.DecreaseScale(DataManager.Instance.scaleDecreaseTime));

            // 봇이 아닐 때만 카메라 크기 변경
            if (!sc.isBot)
            {
                MainCamera_Action camAction = Camera.main?.GetComponent<MainCamera_Action>();
                camAction?.ScaleDecreased();
            }

            StartCoroutine(RespawnRoutine());
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerMesh")) return;

        PlayerScaleController sc = other.GetComponentInParent<PlayerScaleController>();
        if (sc != null && !sc.isBot && PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.isSteppingMilk = false;
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