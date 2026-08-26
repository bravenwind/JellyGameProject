# 코드 파악 계획 — 8/25(화) ~ 8/29(토)

> 8/19~8/22 일정(전송·복제·흐름·모드·봇 前半)은 **완료**. 읽는 데 시간이 더 걸려
> 원래 8/23~8/26이던 내용을 5일로 다시 폈다.
>
> **개선만 하는 날은 두지 않는다.** 미뤄두면 왜 고치려 했는지를 잊는다.
> 밀린 개선 항목은 그 코드를 읽는 날에 하나씩 박아뒀다 — 읽다가 그 자리에서 고친다.

목표: **이 코드베이스를 완전히 이해한다.** 그것 하나뿐이다.

기능 만들기 과제는 전부 뺐다. 실제로 해보니 **읽는 것만으로도 시간이 다 간다** — 읽다 보면
"이건 왜 이렇지?"가 계속 나오고, 죽은 코드·중복·잔재가 튀어나와 그 자리에서 정리하게 된다.
그게 곧 이해의 과정이라 억지로 줄이지 않는다.

현재 규모: **123개 파일 / 18,615줄** (`Assets/Scripts`)
읽으면서 죽은 코드·중복을 걷어낸 결과 시작 시점(137개 / 19,355줄)보다 줄었다.

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
| ⬜ | `AI/` 본체, `Player/`, `Absorbing/`, `Map/`, `UI/` | 아래 일정 |

**남은 양: 약 8,240줄.** 5일이니 하루 1,650줄쯤이다. 네트워크와 무관한 코드라 앞의 절반보다
빨리 읽히고, 8/26은 읽는 양을 줄이는 대신 밀린 통합 작업을 넣었다.

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

## 8/29 (토) — UI·보조 (2,188줄, 훑기) + 전체 이어붙이기

| 범위 | 줄 | 깊이 |
|---|---|---|
| `Net/UI/` | 295 | 방 목록·순위표. 얕게 |
| `Net/Debug/LanDiagnostics.cs` | 163 | F1 화면. 지금까지 읽은 게 다 나온다 |
| `UI/LoadingSceneController.cs` | 349 | 커튼 전환. **얽혀 있으니 시간 잡을 것** |
| `UI/Result/GameResultManager.cs` | 515 | 결과 씬 |
| `UI/InGameUI/`, `UI/UIManagement/` | 866 | 훑기 |

> `UI/DoTween/`(1,090줄)과 `JellyMesh/SoftBody3D.cs`(181줄)는 **읽지 않는다.**
> 연출과 Cloth 물리라 게임 로직과 무관하다.

### 오늘 고칠 것

- **`MsgType.GameOver` 본문** — `WinnerNetId`·`WinnerScore`가 `AddLog` 한 줄에만 쓰인다.
  결과 화면은 `FinalStandings`를 쓰므로 같은 사실을 두 번 보내는 셈이다.
  `GameResultManager`를 읽는 오늘 본문을 비울지 정한다

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

`REVIEW_NOTES.md`에 구조 분석이 더 있다.

---

## 아직 안 정한 것 (읽다가 마주치면 결정)

> 완료된 항목은 지웠다. 무엇을 왜 고쳤는지는 아래 "이 코드가 왜 이렇게 생겼는지"와
> 각 파일의 주석에 남겨뒀다.

> 날짜가 잡힌 것(봇/사람 중복 통합 → 8/26, `MsgType.GameOver` 본문 → 8/29)은
> 여기 두지 않고 그 날 일정에 직접 박아뒀다. 이 목록은 **아직 날을 못 정한 것**만이다.

### 날을 못 정한 것

- `Assets/_Recovery/0.unity` (2.9MB) — 유니티 크래시 복구 임시 씬. 빌드에 안 들어간다. 지울지
- 인스펙터에 남은 고아 필드 14개 클래스 — 코드에 없는 키라 유니티가 무시한다.
  씬을 한 번 저장하면 자동으로 사라지므로 손댈 필요는 없다
- CP949로 저장된 스크립트 10개 — 도구로 편집하면 한글 주석이 깨진다.
  건드릴 일이 생기면 UTF-8로 먼저 변환할 것

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
