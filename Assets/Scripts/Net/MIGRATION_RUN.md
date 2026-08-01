# 실제 씬 이식 — 실행 순서

> 아래를 **순서대로** 따라가면 `Game_io_AbsorbMode` 씬이 LAN(소켓) 구성으로 바뀝니다.
> 각 단계마다 콘솔 로그를 확인하세요.

---

## 0. 커밋

프리팹과 씬을 건드리므로 **반드시 먼저 커밋**하세요.

---

## 1. 프리팹 변환 (재실행)

```
Tools ▸ LAN 이식 ▸ ① 프리팹 변환 미리보기
Tools ▸ LAN 이식 ▸ ② 프리팹 변환 실행
Tools ▸ LAN 이식 ▸ 현황 조사
```

**이번엔 5개가 다 풀립니다.** `[RequireComponent(typeof(PhotonView))]` 3줄을 없앴거든요.

변환기가 이번엔 이것도 함께 처리합니다:

| 제거 | 추가 |
|---|---|
| `PhotonView` | `NetIdentity` + `NetScale` |
| `PhotonTransformView` | `NetTransform` + `NetKnockback` |
| **`NetworkPlayerSync`** | **`LanPlayerState`** |
| **`AIPlayerSync`** | — |
| **`AIPlayerMovement`** | — (봇은 이후 단계) |

> Photon 스크립트 **파일 자체는 지우지 않습니다.** 서로 참조가 얽혀 있어
> 지우면 컴파일이 연쇄로 무너집니다. 프리팹·씬에서만 걷어내면 실행되지 않아요.

**기대 결과**

```
Photon만  : 0개
Net만     : 29개  ← 변환 완료
둘 다     : 0개
```

## 2. 씬 구성

`Scenes/Game_io_AbsorbMode.unity` 를 연 뒤:

```
Tools ▸ LAN 이식 ▸ ④ 현재 씬을 LAN 구성으로
```

하는 일:

1. Photon 매니저 오브젝트 **비활성화** (삭제 X — 되돌리기 쉽게)
   `NetworkManager` `GameModeManager` `NetworkJellyManager` `LobbyController` 등
2. **`LanNet`** 오브젝트 생성 + 컴포넌트 6종 부착
   `NetManager` `NetWorld` `AbsorbMode` `PushMode` `LanGameFlow` `NetTestUI`
3. `NetWorld.prefabs` 자동 채움
   `[0]` NetworkPlayer(플레이어) / `[1]`~ 젤리들

**끝나면 Ctrl+S로 저장하세요.**

## 3. 인스펙터 확인

`LanNet` 선택 후:

| 컴포넌트 | 확인할 것 |
|---|---|
| `NetWorld` | Prefabs `[0]`이 **NetworkPlayer**인지 (젤리가 0번이면 안 됨) |
| `AbsorbMode` | `Spawn Jelly` 체크, `Max Jelly Count` 30 정도 |
| `LanGameFlow` | `Mode` = Absorb, `Min Players To Start` = 2, **`Auto Load Result Scene` 해제** |
| `PushMode` | 흡수 모드 테스트 중엔 체크 해제해도 됨 |

> `Auto Load Result Scene`은 **처음엔 꺼두세요.** 켜면 게임 종료 후 결과 씬으로
> 넘어가 버려서 로그를 확인할 시간이 없습니다.

## 4. 실행

Multiplayer Play Mode로 2명:

```
① 메인 에디터 → [호스트 시작]     → 내 캐릭터 스폰
② Player 2   → [참가] 127.0.0.1  → 2명 되면 3초 카운트다운 → 게임 시작
③ 젤리 먹기 / 서로 흡수 / 스페이스로 밀치기
```

### 확인 항목

| 항목 | 기대 |
|---|---|
| 캐릭터 복제 | 양쪽에 2명 |
| 젤리 스폰 | 같은 위치·개수 |
| 흡수 | 먹으면 크기·점수 증가, 양쪽 반영 |
| **플레이어 흡수** | 큰 쪽이 작은 쪽을 먹으면 3초 뒤 부활 |
| **게임 흐름** | 2명 모이면 자동 시작, 180초 뒤 종료 + 승자 로그 |
| 늦은 입장 | 나중에 들어와도 진행 중 상태가 보임 |

---

## 되돌리는 법

| 상황 | 방법 |
|---|---|
| 씬만 되돌리기 | 저장 안 했으면 씬 다시 열기 / 저장했으면 `git checkout -- Scenes/...` |
| 프리팹까지 | `git checkout -- Assets/Prefabs Assets/Resources` |
| 전부 | 이 작업 전 커밋으로 `git reset --hard` |

---

## 예상되는 문제

| 증상 | 원인 · 조치 |
|---|---|
| 캐릭터가 안 보임 | `NetWorld.Prefabs[0]`이 플레이어가 아님 |
| `MissingReferenceException` | 기존 UI가 사라진 `NetworkPlayerSync`를 참조 중 → 해당 UI 컴포넌트 임시 비활성화 |
| 젤리가 안 나옴 | `Prefabs` 배열에 젤리가 안 들어감 → 프리팹 변환을 먼저 |
| 게임이 시작 안 됨 | `Min Players To Start`보다 인원이 적음. 1로 낮춰 단독 테스트 |
| 콘솔에 Photon 에러 | 비활성화 안 된 Photon 매니저가 남음 → Hierarchy에서 직접 끄기 |
| 카메라가 캐릭터를 안 따라감 | 기존 카메라 스크립트가 `NetworkPlayerSync`를 찾고 있음 → 이후 단계 |

---

## 이번 이식으로 **하지 않은 것**

| 항목 | 이유 |
|---|---|
| AI 봇 | `AIPlayerMovement`가 Photon 배관과 얽혀 있음. 호스트 소유 오브젝트로 재구현 예정 |
| 타일 붕괴 (5-3) | 결정론 재생 방식이라 별도 설계 필요 |
| 색·젤리 종류별 보상 | `RYBColorSystem` 연결이 남음 |
| 점수판 UI | `LanPlayerState.Score`를 읽어 붙이면 됨 |
| 로비·방 목록 | 6-2 UDP 자동 탐색과 함께 |
