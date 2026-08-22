using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using JellyNet;

public class Milk : MonoBehaviour
{
    [Header("Settings")]
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

        // 소유자만 처리한다 — 사람은 본인 클라에서, 봇은 호스트에서만 IsMine == true.
        //   원격 사본에서도 감속을 걸면 같은 효과가 두 번 들어간다.
        NetIdentity id = other.GetComponentInParent<NetIdentity>();
        if (id == null || !id.IsMine) return;

        // 같은 대상을 이 밀크가 이미 감속 중이면(겹친 트리거/재진입) 중복 적용하지 않는다. (J3)
        if (_slowed.ContainsKey(id.NetId)) return;

        PlayerMovement movement = other.GetComponentInParent<PlayerMovement>();
        AIPlayerMovement aiMovement = other.GetComponentInParent<AIPlayerMovement>();
        if (movement == null && aiMovement == null) return;

        if (movement != null) movement.moveSpeed *= speedSlowMultiplier;
        // 봇은 moveSpeed만 바꾸면 Agent.speed(실제 이동 속도)에 즉시 반영되지 않으므로
        // ApplySpeedMultiplier로 Agent.speed까지 함께 곱한다. (밀크 이탈 후 슬로우 잔존 방지)
        if (aiMovement != null) aiMovement.ApplySpeedMultiplier(speedSlowMultiplier);

        // 봇은 AIPlayerMovement만 가지므로, PlayerMovement가 있으면 (로컬) 사람 플레이어다.
        bool isHuman = movement != null;
        _slowed[id.NetId] = new SlowedEntity { player = movement, ai = aiMovement, isHuman = isHuman };

        if (isHuman && PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.isSteppingMilk = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerMesh")) return;

        NetIdentity id = other.GetComponentInParent<NetIdentity>();
        if (id == null) return;

        // 이 밀크가 실제로 감속시킨 대상만 복원한다(추적 여부로 판단 → enter/exit 항상 대칭). (J2/J3)
        if (!_slowed.TryGetValue(id.NetId, out SlowedEntity entity)) return;
        _slowed.Remove(id.NetId);

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
        // 봇은 진입 때와 대칭으로 Agent.speed까지 함께 복원해야 슬로우가 즉시 풀린다.
        if (entity.ai != null) entity.ai.ApplySpeedMultiplier(restore);
    }

    // "밟으면 사라졌다 재생성" 기능은 트리거 코드가 소실돼 사체만 남아 있어 지웠다.
    // 되살릴 경우 로컬 처리만으로는 클라 간 상태가 갈라지므로
    // 호스트 판정 + 방송 구조로 새로 설계할 것.
}
