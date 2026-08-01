# 이식 대상 추출 — `NetworkPlayerSync` (874줄) 분해

> 목적: Photon 배관을 걷어내고 **실제 게임 규칙만** 추려 새로 쓴다.
> 기준: `IO-lan_socket` 브랜치, 2026-08-01.

---

## 요약

```
874줄  =  이미 대체됨 (버림)   약 45%
          Photon 배관 (버림)   약 30%
          살릴 게임 규칙        약 25%   ← 새로 쓸 대상
```

---

## A. 이미 대체된 것 — **버린다**

| 원본 | 대체 | 상태 |
|---|---|---|
| `PhotonTransformView` (위치·회전) | `NetTransform` | ✅ |
| `SyncScale` / `"Scale"` 프로퍼티 | `NetScale` | ✅ |
| `RPC_ApplyKnockback` | `NetKnockback` | ✅ |
| `RPC_RequestBatHitPlayer/Bot` + 쿨다운 + 거리검증 | `PushMode.ResolveBatHit` | ✅ |
| `_lastBatHitAuthTime` | `PushMode._lastHitTime` | ✅ |
| `GetAuthorityScale` / `GetBotAuthorityScale` | `NetScale.Current` | ✅ |
| `WithinPlausibleRange` | 각 판정부에 내장 | ✅ |

## B. Photon 배관 — **버린다**

```
MonoBehaviourPun · IPunObservable · photonView · PhotonView.Find
PhotonNetwork.InRoom / IsMasterClient
CustomProperties (딕셔너리 동기화)
Photon.Realtime.Player
RpcTarget / [PunRPC]
```

## C. 살릴 게임 규칙 — **새로 쓴다**

### C-1. 플레이어 상태 (`LanPlayerState`)

| 항목 | 원본 | 비고 |
|---|---|---|
| **점수** | `"Score"` CustomProperty | 호스트 권위 |
| **탈락** | `"Eliminated"` CustomProperty, `ELIMINATED_KEY` | 호스트 권위 |
| **색** | `OnPhotonSerializeView` RGBA 연속 전송 | 자주 안 바뀜 → **이벤트로 충분** |
| **닉네임** | `photonView.Owner.NickName` | 접속 시 1회 |
| **IsOutOfPlay** | 탈락 ∨ 흡수됨 | 판정 단일 출처 |
| **IsAbsorbed** | `_isAbsorbed` | 흡수 연출 중 |

> **색을 연속 전송하던 것은 낭비였다.** 매 프레임 RGBA 4개를 보냈지만
> 실제로 색이 바뀌는 건 젤리를 먹었을 때뿐이다. 바뀔 때만 보내면 된다.

### C-2. 플레이어 ↔ 플레이어 흡수 (`AbsorbMode` 확장)

원본 `RPC_RequestAbsorbValidation`의 규칙:

```
① 게임 진행 중인가
② 몸이 닿았나        거리 ≤ (내 크기 + 상대 크기)
③ 흡수자가 더 큰가    absorberScale > victimScale
   → 통과하면 전원에게 GetAbsorbed 방송
```

이후 흐름: `AbsorbedSequence` (연출) → `Respawn` (respawnDelay 3초)

### C-3. 맵 이탈 탈락

`RPC_ChocolateElimination` → `ConvertToFloatingBody`
초콜릿(맵 밖)에 빠지면 조작 불가 + 떠다니는 몸으로 전환.

### C-4. 애니메이션 연출 (연출 전용)

```
RPC_PlayJump · RPC_PlayDash · RPC_PlayAttack · "IsMoving"
```

**게임 판정과 무관한 시각 효과.** 손실돼도 게임이 안 깨지므로 우선순위 낮음.

---

## 새로 추가할 메시지

| 타입 | 값 | 방향 | 내용 |
|---|---|---|---|
| `PlayerStateUpdate` | 24 | 호스트 → 전원 | `netId, score, flags, r, g, b` |
| `PlayerNameSet` | 25 | 호스트 → 전원 | `netId, name` |
| `AnimTrigger` | 26 | 전원 방송 | `netId, triggerId` (연출) |
| `AbsorbPlayerRequest` | 32 | 클라 → 호스트 | `victimNetId, absorberNetId` |
| `PlayerAbsorbed` | 33 | 호스트 → 전원 | `victimNetId, absorberNetId` |
| `PlayerRespawn` | 34 | 호스트 → 전원 | `netId, x, y, z` |

`flags` (1바이트 비트필드)

```
bit 0 : Eliminated  (탈락)
bit 1 : Absorbed    (흡수되어 리스폰 대기)
```

> 점수·탈락·색을 **한 메시지에 묶는** 이유: 셋 다 자주 안 바뀌고,
> 바뀔 때 대개 함께 바뀐다(흡수 → 점수↑ + 색 변화). 나눠 봐야 메시지만 늘어난다.

---

## 이식하지 않기로 한 것

| 항목 | 이유 |
|---|---|
| `EntityRegistry` 등록/해제 | `NetWorld.Objects`가 같은 역할 |
| `GetPlayerSyncedScale(Player)` | `NetScale.Current`로 직접 접근 |
| AI 봇 관련 분기 | 봇은 별도 단계에서 (호스트가 소유한 오브젝트로 통일) |
| `nameTagBillboard` 연결 | UI 단계에서 |
