using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;

public class Milk : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float respawnTime = 5.0f; // 다시 나타날 시간

    [SerializeField] private float speedSlowMultiplier = 0.5f;

    // 이 밀크가 현재 감속시킨 대상들. ViewID로 식별해 중복 적용/복원을 막고,
    // 비활성화(OnDisable) 시 남은 대상을 안전하게 복원하기 위해 컴포넌트 참조를 들고 있는다. (J3)
    private readonly Dictionary<int, SlowedEntity> _slowed = new Dictionary<int, SlowedEntity>();

    private struct SlowedEntity
    {
        public PlayerMovement player;
        public AIPlayerMovement ai;
        public bool isHuman;   // 로컬 사람 플레이어인지(SFX 대상 여부)
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerMesh")) return;

        // 소유자만 처리한다: 사람 플레이어는 본인 클라, 봇은 마스터 클라에서만 IsMine == true.
        // (PhotonView 하나로 사람/봇을 통일해 가린다 — 원격 사본을 건드리지 않는다.) (J2)
        PhotonView pv = other.GetComponentInParent<PhotonView>();
        if (pv == null || !pv.IsMine) return;

        // 같은 대상을 이 밀크가 이미 감속 중이면(겹친 트리거/재진입) 중복 적용하지 않는다. (J3)
        if (_slowed.ContainsKey(pv.ViewID)) return;

        PlayerMovement movement = other.GetComponentInParent<PlayerMovement>();
        AIPlayerMovement aiMovement = other.GetComponentInParent<AIPlayerMovement>();
        if (movement == null && aiMovement == null) return;

        if (movement != null) movement.moveSpeed *= speedSlowMultiplier;
        if (aiMovement != null) aiMovement.moveSpeed *= speedSlowMultiplier;

        // 봇은 AIPlayerMovement만 가지므로, PlayerMovement가 있으면 (로컬) 사람 플레이어다.
        bool isHuman = movement != null;
        _slowed[pv.ViewID] = new SlowedEntity { player = movement, ai = aiMovement, isHuman = isHuman };

        if (isHuman && PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.isSteppingMilk = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerMesh")) return;

        PhotonView pv = other.GetComponentInParent<PhotonView>();
        if (pv == null) return;

        // 이 밀크가 실제로 감속시킨 대상만 복원한다(추적 여부로 판단 → enter/exit 항상 대칭). (J2/J3)
        if (!_slowed.TryGetValue(pv.ViewID, out SlowedEntity entity)) return;
        _slowed.Remove(pv.ViewID);

        RestoreSpeed(entity);

        if (entity.isHuman && PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.isSteppingMilk = false;
    }

    // 밀크가 비활성화/파괴될 때, 아직 위에 있던 대상의 속도를 되돌려 영구 감속이 남지 않게 한다. (J3)
    private void OnDisable()
    {
        bool hadHuman = false;
        foreach (SlowedEntity entity in _slowed.Values)
        {
            RestoreSpeed(entity);
            hadHuman |= entity.isHuman;
        }
        _slowed.Clear();

        if (hadHuman && PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.isSteppingMilk = false;
    }

    // 진입 시 곱한 값을 정확히 되돌린다. speedSlowMultiplier가 0이면 나누기 불가이므로 가드.
    private void RestoreSpeed(SlowedEntity entity)
    {
        if (speedSlowMultiplier <= 0f) return;
        float restore = 1f / speedSlowMultiplier;
        if (entity.player != null) entity.player.moveSpeed *= restore;
        if (entity.ai != null) entity.ai.moveSpeed *= restore;
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
