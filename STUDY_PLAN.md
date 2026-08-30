# 코드 파악 계획 — 8/25(화) ~ 9/1(월)

> 8/19~8/22 일정(전송·복제·흐름·모드·봇 前半)은 **완료**. 읽는 데 시간이 더 걸려
> 원래 8/23~8/26이던 내용을 5일로 다시 폈다.
>
> 8/28(맵)까지 끝냈다. 남은 **8/29 일정(UI·보조)을 8/31~9/1 이틀로 다시 폈다** —
> 하루에 2,188줄은 지금까지의 실제 속도와 안 맞았고, 마지막 '한 판 추적'은
> 시간이 얼마나 걸릴지 모르는 작업이라 하루를 통째로 비워두는 게 낫다.
>
> **개선만 하는 날은 두지 않는다.** 미뤄두면 왜 고치려 했는지를 잊는다.
> 밀린 개선 항목은 그 코드를 읽는 날에 하나씩 박아뒀다 — 읽다가 그 자리에서 고친다.

목표: **이 코드베이스를 완전히 이해한다.** 그것 하나뿐이다.

기능 만들기 과제는 전부 뺐다. 실제로 해보니 **읽는 것만으로도 시간이 다 간다** — 읽다 보면
"이건 왜 이렇지?"가 계속 나오고, 죽은 코드·중복·잔재가 튀어나와 그 자리에서 정리하게 된다.
그게 곧 이해의 과정이라 억지로 줄이지 않는다.

현재 규모: **125개 파일 / 20,892줄** (`Assets/Scripts`)

> 줄 수가 다시 늘었는데 **코드가 아니라 주석이 늘었다.** 읽으면서 "왜 이렇게 생겼는지"를
> 그 자리에 적어두는 게 이 계획의 목적이라 늘어나는 게 맞다.
> 죽은 코드는 계속 줄고 있다 — 8/28에만 `OnTriggerStay` · `PurgeDestroyedEntries` ·
> `DisableAIOnObject` · `NavDriverOf` · NavMeshAgent 부활 경로가 사라졌다.

---

## 읽는 방법 — 8/18에 해보고 정한 것

**1. 파일 하나를 통째로, 함수 단위로.** 위에서 아래로 훑지 말고 함수마다 "이게 왜 필요한가"를 묻는다.

**2. "왜 이렇게 생겼지?"가 나오면 그 자리에서 판다.** 오늘 나온 것들이 전부 그랬다 —
`countdown = -1`, `Vector2?`, `Invoke(nameof(...))`, `static`, `Bind` vs `FindObjectsByType`.
막히면 멈추고 물어본다. 넘어가면 다음 파일에서 또 막힌다.

**3. 고칠 게 보이면 그때 고친다.** 나중으로 미루면 안 한다 — 미루면 **왜 고치려 했는지를 잊는다.**
읽는 동안은 그 코드가 머릿속에 다 들어와 있어서 고치는 비용이 가장 싸다.
하루 뒤에 돌아오면 그 맥락을 처음부터 다시 쌓아야 한다.
그래서 이 계획엔 "개선하는 날"이 따로 없다.
오늘 하루에 나온 것: `ai+1` 잠복 버그, 모드 4중 저장, `CanLeaveMatch` 죽은 검사,
`LocalLoad` 상수화, `DOKill` 누락, Photon SDK 3.8MB.

**4. 코드만 믿지 말고 씬을 확인한다.** 인스펙터 연결이 비어 있으면 코드가 멀쩡해도 안 돈다.
오늘 `Game_io_PushMode`의 `spectateButton`/`returnToMainButton`이 비어 있는 걸 그렇게 찾았다.

**5. 확신이 안 서면 "아마"라고 말하지 말고 확인한다.**
`CountPlayers`가 봇을 센다고 생각했는데 씬을 열어보니 아니었다.

### 하루 시작 전 5분

게임을 한 판 켜고 **F1**(`LanDiagnostics`)로 현재 상태를 본다. 어제 읽은 게 이 화면 어디에 나타나는지 짚는다.

---

## 진행 상황

| | 범위 | 상태 |
|---|---|---|
| ✅ | `Net/Transport/` | 완료 |
| ✅ | `Net/Replication/` | `NetWorld` `NetIdentity` `NetTransform` `NetSpawnPool` `NetKnockback` |
| ✅ | `Net/Flow/` 전부 | 발견·로비·씬전환·흐름·HUD·순위·관전·스폰지점 |
| ✅ | `Net/Modes/` 전부 | `NetGameMode` `NetEntity` `HostJudgement` `PushMode` `AbsorbMode` |
| ✅ | `Net/Bot/` + `AI/FSM/` | 봇 생성·상태 + 상태 4종 |
| ✅ | `AI/` 본체 (8/25) | `AIPlayerMovement` `AIDetector` `JellyAgentAI` `WanderingAI` |
| ✅ | `Player/PlayerFSM/` (8/26) | `PlayerMovement` + 상태 6종. 봇/사람 중복 통합 완료 |
| ✅ | `Player/` `Absorbing/` `Color/` `DataManagement/` (8/27) | 크기·색·흡수 |
| ✅ | `Map/` (8/28) | `TileCollapseManager` `FallingTile` `ChocolateFluid` `Milk` `PuddingWiggle` |
| ⬜ | `UI/`, `Net/UI/`, `Net/Debug/` | 8/31 ~ 9/1 |

**남은 양: 약 2,870줄** (`UI/DoTween/` 1,090줄과 `SoftBody3D` 181줄 제외).
이틀이니 하루 1,400줄쯤인데, 9/1은 읽는 양을 줄이는 대신 **'한 판 추적'** 을 넣었다.

---

## 8/25 (화) — AI 본체 (1,609줄)

| 파일 | 줄 | |
|---|---|---|
| `AIPlayerMovement.cs` | 1,078 | **전부 읽지 말 것.** `Awake`→`InitAndRun`→`Update`→`StateEvalLoop` 순으로 |
| `AIDetector.cs` | 145 | 탐지. `EntityRegistry`를 어떻게 쓰는지 |
| `EnemyAI/JellyAgentAI.cs` | 144 | 젤리 AI 기반 클래스 |
| `EnemyAI/WanderingAI.cs` · `AIWaypointPatrol.cs` | 242 | 하위 클래스 둘 |

### 붙잡을 것

- **`JellyAgentAI`가 상속을 쓴 이유.** 하위가 채우는 건 `OnBecameDriver`(첫 목적지)와 `DriveUpdate`(매 프레임) 둘뿐
- **`NavMeshAgent.baseOffset`** — 에이전트는 자기 위치보다 **아래**에서 NavMesh를 찾는다
- **`IsDriver`** — 봇은 호스트에서만 생각한다. 그 판정이 `AIPlayerMovement`·`LanBotState`·`NetworkNavMeshHelper` 셋에 각각 있다

### 오늘 고칠 것

- `AIPlayerMovement`의 **전투 관련 메서드에 표시만 해둔다** (`CheckGroundBelow` `DetectBatHit` `TryAttack` `TryDash`).
  사람 쪽 짝을 아직 안 읽었으니 합치는 건 내일. **오늘은 어느 줄인지 적어만 둔다**
- 읽다 나온 죽은 코드·중복은 그 자리에서 정리

---

## 8/26 (수) — 플레이어 FSM + 봇/사람 통합 (642줄 + 작업)

| 범위 | 줄 | |
|---|---|---|
| `Player/PlayerFSM/` | 642 | `PlayerMovement` + 상태 6개. AI FSM과 같은 패턴이라 빨리 읽힌다 |

읽는 양이 적은 대신 **어제 표시해둔 중복을 오늘 합친다.** 양쪽을 다 읽은 오늘이 그 자리다.

### 오늘 고칠 것 — 봇/사람 중복 (약 130줄)

| | 봇 | 사람 | 방법 |
|---|---|---|---|
| 발밑 지면 검사 | `CheckGroundBelow` 18줄 | `PlayerMovement.ApplyGravity` 12줄 | 공용 정적 헬퍼 |
| 배트 명중 판정 | `DetectBatHit` 56줄 | `PlayerAttackState.DetectBatHitLan` 42줄 | `BatArcQuery` 정적 클래스 |
| 공격 발동 | `TryAttack` 16줄 | `PlayerAttackState.Enter` 27줄 | 쿨다운·트리거 공통부만 |
| 대시 | `TryDash` 19줄 | `PlayerDashState` 19줄 | 구조가 다르니 무리하지 말 것 |

> 이미 합친 것: 흡수 연출 · 배트 회전 · 넉백 곡선. 같은 패턴이니 그대로 따라가면 된다 —
> **공통은 한 곳에, 다른 부분만 훅으로.**

### 붙잡을 것

- **상태 객체가 캐릭터마다 새로 생기나, 공유되나** (`PlayerMovement.Start`에서 `new`)
- **`Enter`/`Update`/`Exit`의 책임 분담.** 연출 시작은 `Enter`, 정리는 `Exit`

---

## 8/27 (목) — 크기·색·흡수 (1,932줄)

| 범위 | 줄 | |
|---|---|---|
| `Player/` | 815 | `PlayerScaleController`(크기의 유일한 권한자), `PlayerBridge`, `BotBridge` |
| `Absorbing/` | 533 | `JellyColliderAbsorb`, `PlayerAbsorber`, `JellyObject` |
| `Color/` · `DataManagement/` | 584 | RYB 색 계산, 설정값. 흡수와 한 덩어리라 같이 본다 |

### 붙잡을 것

**흡수 한 번에 몇 개의 시스템이 관여하는가.** 종이에 그릴 것.

```
트리거 감지 → 연출 시작 → 호스트에 요청 → 검증 → 승인 방송 → 성장·색·점수 → 젤리 파괴
   (클라)      (클라)        (클라)      (호스트)   (호스트)      (전원)        (호스트)
```

**연출은 예측하고 보상은 예측하지 않는다.** 클라는 렌더러를 즉시 끄지만 성장·점수는 호스트 확정을 기다린다.
거부되면 `RestoreIfRejected`가 1.5초 뒤 되돌린다.

**`PlayerScaleController`가 크기의 유일한 권한자.** `_pendingScale`과 `QueueScaleChange`가 왜 필요한지
(연달아 먹으면 어떻게 되는지) 확인할 것.

> 크기 파이프라인 통일은 이미 끝냈다. 판정용 크기는 전부 `currentScaleValue`고
> `NetEntity.ScaleOf`가 유일한 외부 창구다. **그 구조가 실제로 그런지 눈으로 확인**한다.

### 오늘 고칠 것

- `PlayerAbsorbingManager`는 **합치지 않는다** (사람·봇 공용이라 그 자리가 맞다). 왜 그런지 확인하고 넘어갈 것
- `DataManager`에 아직 안 쓰는 설정값이 남아 있는지

---

## 8/28 (금) — 맵 (1,873줄)

| 파일 | 줄 | |
|---|---|---|
| `TileCollapseManager.cs` | 810 | **`GetSyncedElapsed`부터.** 링 붕괴 + 밟기 마모 |
| `FallingTile.cs` | 422 | 발판 하나의 생애 |
| `ChocolateFluid.cs` | 317 | 초콜릿 탈락 |
| `Milk.cs`, `JellyCrusher.cs` 등 | 나머지 | 장애물들 |

### 붙잡을 것

- **`GetSyncedElapsed`가 -1을 돌려주면 링이 한 번도 안 무너진다.** 에러가 안 나서 증상만으로는 원인을 못 찾는다
- **`_needsStepGrace`** — 첫 패스에 전 개체의 타일을 동시에 마모시키지 않으려는 장치
- **`standGap`** — 점프 중에도 발판이 밟힌 걸로 판정되던 문제

### 확인할 질문

- 붕괴 판정은 호스트만 하는데 왜 마모 카운트를 전 클라에 복제하나
- 사람 플레이어를 발판이 건드리지 않는 이유
- `ChocolateFluid`가 사람·플레이어봇·배회젤리 셋을 어떻게 가르나

---

## 8/31 (월) — 게임 중 화면에 뜨는 것들 (1,656줄)

판이 도는 동안 화면 위에 얹히는 것들이다. **공통점은 "상태를 읽어 그리기만 한다"** —
게임 로직을 바꾸지 않는다. 그 경계가 실제로 지켜지는지 보는 게 오늘의 목적이다.

| 파일 | 줄 | |
|---|---|---|
| `UI/OffScreenPlayerIndicator.cs` | 364 | 화면 밖 플레이어 화살표. 오늘 가장 큰 파일 |
| `UI/MinimapArrowManager.cs` · `MinimapArrow.cs` | 257 | 미니맵. 위와 같은 문제를 다르게 푼다 — **비교하며 읽을 것** |
| `UI/InGameUI/` | 285 | `CooldownRingUI` `CurrentStatusUI` `SoundSettings` + 버튼 3개 |
| `UI/UIManagement/` | 257 | `UIManager` `UIPoolManager` |
| `UI/EffectUI/LevelUpFloater.cs` · `LevelUpFloaterPool.cs` | 203 | 흡수할 때 뜨는 숫자 |
| `Net/Debug/LanDiagnostics.cs` | 163 | **F1 화면. 지금까지 읽은 게 전부 여기 나온다 — 제일 먼저 읽을 것** |
| `UI/NameTagBillboard.cs` | 127 | 머리 위 이름표 |

### 읽는 순서

`LanDiagnostics`부터. 이건 **지금까지 읽은 모든 시스템의 요약본**이라, 여기 뜨는 항목 하나하나가
어느 파일에서 오는지 짚어보면 8/19~8/28이 한 화면에 정리된다. 못 짚는 항목이 있으면 그 날 내용으로 돌아간다.

### 붙잡을 것

- **`OffScreenPlayerIndicator`와 `MinimapArrowManager`가 같은 일을 하나.**
  둘 다 "어디 있는지 화면 밖 표시"인데 따로 있다. 합칠 수 있는지, 아니면 다른 문제인지 판단할 것.
  (오늘까지 나온 중복은 전부 이 모양이었다 — 같은 질문에 답이 둘)
- **`UIPoolManager`가 무엇을 푸는가.** `LevelUpFloaterPool`과 역할이 겹치는지
- **`CurrentStatusUI.OnScaleChanged`의 `scale > 0f ? … : "-"`** — 스폰 전에 0이 뜨던 걸 막은 자리다.
  왜 0이 뜰 수 있는지 다시 확인할 것 (`GameState.playerCurrentScale` 초기값)
- **이름표·화살표가 탈락한 개체를 어떻게 지우나.** `IsOutOfPlay`를 보는지, 각자 판단하는지

### 오늘 고칠 것

- `LoadingSceneController`의 `GetComponent<Canvas>()` 3회 (`312`행 근처) — **내일 읽을 파일이니 표시만.**
  오늘은 어느 줄인지 적어만 둔다
- 읽다 나온 죽은 코드·중복은 그 자리에서 정리

---

## 9/1 (화) — 판의 끝 (1,206줄) + 전체 이어붙이기

한 판이 **끝나고 나서** 도는 것들이다. 순위표 → 종료 연출 → 커튼 → 결과 씬으로 이어지는
한 줄기라, 그 순서대로 읽으면 그대로 오후의 '한 판 추적'으로 넘어간다.

| 파일 | 줄 | |
|---|---|---|
| `Net/UI/` | 312 | `LanRoomListUI` `LanLeaderboardUI` + 행 2종. 방 목록은 얕게, 순위표는 결과 씬과 이어지니 제대로 |
| `UI/LoadingSceneController.cs` | 367 | 커튼 전환. **`static` 필드가 5개다 — 시간 잡을 것** |
| `UI/Result/GameResultManager.cs` | 527 | 결과 씬 |

> `UI/DoTween/`(1,090줄)과 `JellyMesh/SoftBody3D.cs`(181줄)는 **읽지 않는다.**
> 연출과 Cloth 물리라 게임 로직과 무관하다.
> `MenuHoverPreview` `SceneLoader` `NextSceneManager` `EnableTargetButton` `DisableSelfButton`(149줄)도
> 버튼 한두 줄짜리라 **파일 이름만 보고 넘긴다.**

### 붙잡을 것

- **`LoadingSceneController`의 `static` 다섯 개** — `NextSceneName` `IsPresenting` `IsTransitioning`
  `instance` `pendingDepartureIntro`. 씬을 넘어 살아남는 값이라 **한 판이 끝나고 다음 판에 남아 있으면**
  커튼이 안 열리거나 두 번 돈다. 누가 언제 지우는지 확인할 것
- **`DontDestroyOnLoad`로 다음 씬 위에 겹쳐 있다**(312행 주석). 그래서 캔버스 `sortingOrder`를
  손으로 올린다 — 그 코드가 왜 필요한지
- **`GameResultManager`가 캐릭터를 어떻게 다시 세우나.** `HideBat` `GroundToFloor` `FindJellyRenderer` —
  게임 씬의 프리팹을 결과 씬에 다시 쓰는 구조다. 무엇을 끄고 무엇을 살리는지
- **결과 씬은 `LanScoreboard.FinalStandings`를 읽는다**(159행). 소켓이 이미 닫혀 있어도 되는 이유

### 오늘 고칠 것

- **`MsgType.GameOver` 본문** — `WinnerNetId`·`WinnerScore`가 `AddLog` 한 줄(`LanGameFlow` 668행)에만 쓰인다.
  결과 화면은 `FinalStandings`를 쓰므로 같은 사실을 두 번 보내는 셈이다.
  `GameResultManager`를 읽는 오늘 본문을 비울지 정한다
- 어제 표시해둔 `LoadingSceneController`의 `GetComponent<Canvas>()` 3회

### 마무리 — 한 판을 끝까지 추적

로그를 켜고 한 판을 처음부터 끝까지 따라간다. 종이에 순서대로 적는다.

```
방 만들기 → 비콘 → 발견 → 접속 → Welcome → 씬 로드 → SceneReady
→ 스폰 → 스냅샷 → 카운트다운 → Playing
→ (젤리 흡수 / 배트 히트 / 타일 붕괴 / 낙하)
→ 탈락 → 관전 → 종료 판정 → 순위 방송 → 종료 연출 → 결과 씬
```

**각 화살표에서 어떤 메시지가 오가는지** 적을 것. 못 적는 화살표가 있으면 그 날 내용을 다시 본다.
이걸 막힘없이 적을 수 있으면 이 계획은 성공한 것이다.

---

## 이미 이해한 것 — 다시 안 봐도 되는 것들

### 전송 계층

- **TCP는 메시지 경계가 없다.** `[길이4][타입1][본문]`을 직접 씌운다. UDP는 경계가 있어 안 씌워도 된다
- **소켓 = OS가 관리하는 우편함.** 수신 버퍼에 쌓이고, `Available`로 확인 후 `Read`
- **꺼낸 바이트는 소켓이 안 갖고 있다** → `FramedConnection.buf`가 필요한 이유
- **넣은 바이트는 수정할 수 없다** → `NetWriter`가 조립대인 이유 (길이 백패칭)
- **`Poll(SelectRead) && Available == 0` = FIN.** 순서를 바꾸면 멀쩡한데 끊겼다고 오판한다
- **Nagle + 지연 ACK** → `NoDelay = true`

### 복제

- **`OwnerId`는 신원이 아니라 책임.** 호스트 1, 클라 2+, 씬 오브젝트 0
- **`SceneNetId`(에디터 저장) vs `NetId`(런타임 사본)**, `SCENE_ID_BASE = 1000000`
- **번호 공간을 나누면 판별이 공짜.** `netId >= SCENE_ID_BASE` 한 줄로 씬/런타임 구분
- **호스트는 자기 메시지를 못 받는다.** 방송할 때마다 "나에게도 직접"을 손으로 짝지어야 한다
- **`Broadcast` vs `BroadcastExcept`** — 받는 사람 중 이미 아는 사람이 있으면 뺀다.
  호스트는 peer가 아니라 애초에 뺄 필요가 없다
- **스냅샷은 존재 목록이 주 화물.** `netId`는 추측할 수 없어서 알려주지 않으면 없는 것과 같다
- **삭제도 정보다.** "지금 있는 것"만 보내면 없어진 건 전달되지 않는다

### C# / 유니티

- **페이크 널** — `UnityEngine.Object`는 `!= null`, 순수 C#은 `?.`
- **`static`은 클래스에 하나.** 씬을 넘어 살아남고, 아무도 안 지우면 영원히 남는다
- **클래스도 런타임에 타입 객체를 갖는다.** `static` 저장소와 메서드 주소표가 거기 있다
- **`static class` = 인스턴스 0개** (컴파일러가 강제) vs **싱글턴 = 1개** (내 코드가 강제)
- **프로퍼티 = 필드처럼 생긴 메서드.** `private set`으로 읽기/쓰기 권한을 나눈다
- **센티널 값** — `countdown = -1`, `OwnerId = 0`, `shownHumans = -1`
- **`Update`에서 트리거할 땐 "바뀌었을 때만"** 조건이 필수. 없으면 애니메이션이 매 프레임 리셋된다
- **UI·네트워크 시간은 `unscaled`.** `SetUpdate(true)`, `WaitForSecondsRealtime`, `unscaledDeltaTime`
- **연출을 새로 시작하기 전에 이전 것을 정리한다.** `DOKill()`, `StopCoroutine`

### 네트워크 일반

- **IP는 32비트 숫자 하나.** 점은 8비트마다 찍은 구분선
- **서브넷 마스크가 네트워크/호스트 경계를 정한다.** 경계는 점 위치와 무관
- **브로드캐스트 = 호스트 부분을 전부 1로** (`IP | ~마스크`). 대역이 박혀 있어야 OS가 옳은 랜카드를 고른다
- **랜카드마다 IP가 따로.** 그래서 `IPAddress.Any`로 받고, 보낼 땐 어댑터마다 따로 보낸다
- **`Bind`는 "이 주소로 받겠다"는 등록.** 보내기는 필요 없고 받기만 필요하다
- **비콘 = 주기적 단방향 존재 알림.** 떠남을 알리지 않고 침묵으로 판단한다(연성 상태)

---

## 이 코드가 왜 이렇게 생겼는지

읽다가 "왜 이렇게 해놨지?" 싶으면 대부분 사연이 있다.

| 코드 | 사연 |
|---|---|
| `NetIdentity.IsSimulatedHere` | `IsMine`만 봤더니 씬에 놓인 젤리가 전부 얼어붙었다 |
| `HostJudgement` | 흡수·밀치기가 같은 검증을 각자 적어둬서 한쪽만 고쳐지곤 했다 |
| `LanBotState`의 절대값 전송 | 클라는 봇이 뭘 먹었는지 몰라서 재현할 수 없다 |
| `FramedConnection`의 `SelectRead` | `Poll`을 먼저, `Available`을 나중에. 순서를 바꿨더니 멀쩡한데 "연결 끊김"이 떴다 |
| `NetSpawnPool`의 `spawnPosition` | `NavMeshAgent`는 켜지는 순간의 자리에서 NavMesh를 찾는다 |
| `TileCollapseManager`의 `standGap` | 점프 중에도 발판이 밟힌 걸로 판정됐다 |
| `SpawnForOwner`의 "이미 있으면 반환" | `SceneReadyLoop`가 0.4초마다 재시도한다. 없으면 캐릭터가 여러 개 생긴다 |
| `ResolveEat`에 **거리 검사가 없는** 것 | 젤리는 호스트 소유인데 흡수 연출은 먹는 클라에서만 돈다. 호스트가 재는 거리는 0이 아니라, 볼 수 없는 사건(접촉)을 근사한 값이었다 |
| `PushMode`에 **연타 제한이 없는** 것 | 클라 쿨타임은 '스윙 시작' 기준인데 요청은 '명중' 시점에 온다. 기준이 달라 정상 타격이 조용히 씹혔다 |
| `NetTransform`이 **송신 시각을 실어 보내는** 것 | 도착 시각으로 보간했더니 네트워크 지터가 그대로 속도 변화로 보였다 |
| `LanPlayerVisual.PlayAbsorbed`가 봇·사람 공용인 것 | 같은 20줄이 두 파일에 복사돼 있어 한쪽만 고치면 서로 다르게 빨려 들어갔다 |
| `NetScale`이 **없는** 것 | 크기를 셋이 동시에 써서 봇이 쑥 작아졌다 커졌다 했다 |
| `NetHost.AcceptingNewPeers` | 게임 시작 후 붙은 클라는 캐릭터가 없어 화면만 멈춰 있었다 |
| `LanGameFlow.endingStarted` | 정상 종료와 사고가 소켓 입장에선 똑같이 보인다. 진행 단계로 구분한다 |
| `LanFlowHud.StopCenterAnim` | `FlashRoutine`이 알파를 0으로 내린 채 살아 있으면 "게임 종료!"를 지워버린다 |
| `NetManager.Offline` | 판이 끝나고 커튼이 도는 동안 게임 씬은 살아 있는데 소켓은 닫혀 있다 |
| `ChocolateFluid`의 부력이 **스프링**인 것 | 켜짐/꺼짐이었을 때 수면 높이에 힘이 둘 다 0인 죽은 구간이 생겨, 빠진 캐릭터가 그 자리에 굳었다 |
| `SurfaceY`를 **콜라이더에서** 읽는 것 | `transform.position.y`로 잡았더니 실제 윗면과 0.25m 어긋나, 부력이 꺼지는 높이가 물 밖 허공이 됐다 |
| `ReleaseMargin = 1` | 물체가 쉬는 높이가 트리거 박스 윗면에서 5cm 아래라, 물결마다 경계를 들락날락한다. 딱 수면으로 자르면 매번 방출된다 |
| `GameTags.IsCharacterMainCollider` | 캐릭터는 트리거 콜라이더가 둘이라 `OnTrigger*`가 개체당 두 번 불린다. 초콜릿 힘이 두 배로 들어갔다 |
| `PhysicsFall`이 **조종 장치까지** 끄는 것 | "끄는 건 부르는 쪽이" 로 나눴더니 같은 코드가 세 벌로 갈라졌고 셋의 범위가 서로 달랐다 |
| `PhysicsFall.Begin(go, useGravity)` | 무조건 켜고 `ChocolateFluid`가 다음 줄에서 다시 껐다. 중력의 주인이 두 곳이었다 |
| `NetEntity.IsDrivenElsewhere`의 **씬 예외** | 씬 젤리는 `OwnerId 0`이라 클라에서 걸러졌는데 위치 복제도 없어서, 클라 화면에만 공중에 떠 있었다 |
| `ComputeRingInterval`이 `LastCollapsingRing + 1`로 나누는 것 | 첫 고리 앞의 예고 간격 자리가 없어서 바깥 링만 경고 없이 무너졌다 (담장 문제) |
| `FallingTile`의 감지 박스가 **아래로도** 파는 것 | 박스 바닥이 타일 두께 한가운데였다. 파묻힌 소품 45개가 발판이 사라져도 공중에 남았다 |
| `CreateFloatData`의 `& 0x7FFFFFFF` | `GetInstanceID`가 음수일 수 있고 C#의 `%`는 피제수 부호를 따른다. 값의 구간이 통째로 갈라졌다 |

`REVIEW_NOTES.md`에 구조 분석이 더 있다.

---

## 아직 안 정한 것 (읽다가 마주치면 결정)

> 완료된 항목은 지웠다. 무엇을 왜 고쳤는지는 아래 "이 코드가 왜 이렇게 생겼는지"와
> 각 파일의 주석에 남겨뒀다.

> 날짜가 잡힌 것(봇/사람 중복 통합 → 8/26, `MsgType.GameOver` 본문 → 8/29)은
> 여기 두지 않고 그 날 일정에 직접 박아뒀다. 이 목록은 **아직 날을 못 정한 것**만이다.

### 날을 못 정한 것

- 인스펙터에 남은 고아 필드 14개 클래스 — 코드에 없는 키라 유니티가 무시한다.
  씬을 한 번 저장하면 자동으로 사라지므로 손댈 필요는 없다
- `JellyObject` → `JellyColliderAbsorb` 합치기 — 프리팹 36개를 건드려야 한다. 가능은 하나 이득이 작다
- `Tile_x_z`의 렌더러가 콜라이더와 **같은 오브젝트**라 `FallingTile.shakeTransform` 분리가 무효다.
  타일 위에 선 캐릭터가 같이 흔들리지 않게 하려면 씬 타일 270개를 재구성해야 한다
- `ChocolateFluid.entrySpeedKeep`이 `linearVelocity`에만 걸린다. 각속도는 그대로라
  빠르게 회전하며 들어온 물체가 초콜릿 안에서 계속 돈다. 연출 취향 문제라 보류

### 하지 않기로 한 것

- ~~`PlayerAbsorbingManager` → `PlayerBridge` 합치기~~ **하면 안 된다.**
  `PlayerAbsorbingManager`는 사람·봇 프리팹에 **둘 다** 붙어 있는데
  `PlayerBridge`는 사람 전용이고 봇은 `BotBridge`를 쓴다.
  합치면 봇이 젤리를 먹어도 색·크기가 안 변한다. 지금이 오히려 올바른 배치다

### 완료 (기록용)

- `INetEntity` 도입 — `LanPlayerState`/`LanBotState`가 구현. `EntityRegistry.Entities`로
  한 벌 순회. `LanScoreboard.Collect`의 두 벌 루프와 `NetEntity`의 `if (id.IsBot)` 분기가 사라졌다
- `IsOutOfPlay` 비대칭 — 봇의 판정이 두뇌(`AIPlayerMovement`)에 있는 건 그대로지만,
  `LanBotState.IsOutOfPlay` 한 줄에 갇혀 밖에서는 `INetEntity.IsOutOfPlay` 하나로 보인다
- `StartHost`/`JoinHost` 실패 알림 — `bool` 반환 + `NetManager.LastError`. 로비가 대기 화면으로
  넘어가지 않고 경고를 띄운다
- `LanRoomListUI.emptyHint` 연결 / `statusText`는 불필요로 결정
- `Rotator.cs`, `ResultStarsUI.cs` 삭제 (프리팹 컴포넌트도 제거)
- `Assets/_Recovery/0.unity` 삭제 · CP949 스크립트 11개 UTF-8 변환 · Google Sheets 기록 제거
- 축소(scale down) 경로 전면 제거 — 게임에 줄어드는 경우가 없어졌다. Min/Max 클램프도 삭제
- 8/28 맵 정리: `PhysicsFall` 통합 · `NetEntity.IsDrivenElsewhere` 승격 ·
  `LanPlayerState.ReportFellOutOfPlay` 신설 · `ChocolateFluid.OnTriggerStay` 삭제 ·
  `IsCharacterProxy` → `IsCharacterMainCollider` 개명 후 `Milk`·`PuddingWiggle`까지 적용
