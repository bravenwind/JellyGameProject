# 코드 파악 계획 — 8/19(수) ~ 8/26(수)

목표: **이 코드베이스를 완전히 이해한다.** 그것 하나뿐이다.

기능 만들기 과제는 전부 뺐다. 실제로 해보니 **읽는 것만으로도 시간이 다 간다** — 읽다 보면
"이건 왜 이렇지?"가 계속 나오고, 죽은 코드·중복·잔재가 튀어나와 그 자리에서 정리하게 된다.
그게 곧 이해의 과정이라 억지로 줄이지 않는다.

현재 규모: **137개 파일 / 19,355줄** (`Assets/Scripts`)

---

## 읽는 방법 — 8/18에 해보고 정한 것

**1. 파일 하나를 통째로, 함수 단위로.** 위에서 아래로 훑지 말고 함수마다 "이게 왜 필요한가"를 묻는다.

**2. "왜 이렇게 생겼지?"가 나오면 그 자리에서 판다.** 오늘 나온 것들이 전부 그랬다 —
`countdown = -1`, `Vector2?`, `Invoke(nameof(...))`, `static`, `Bind` vs `FindObjectsByType`.
막히면 멈추고 물어본다. 넘어가면 다음 파일에서 또 막힌다.

**3. 고칠 게 보이면 그때 고친다.** 나중으로 미루면 안 한다.
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
| ✅ | `Net/Transport/` (1,051줄) | 완료 |
| ✅ | `Net/Replication/NetWorld.cs` (759줄) | 완료 — 함수 전부 |
| ✅ | `Net/Flow/` 6개 (1,700줄쯤) | `LanDiscovery` `LanLobby` `LanSceneFlow` `LanGameFlow` `LanFlowHud` `LanRoomConfig` |
| ⬜ | 나머지 | 아래 일정 |

**남은 양: 약 16,800줄.** 8일이니 하루 2,000줄 남짓인데, 뒤로 갈수록 네트워크와 무관한 코드라 속도가 붙는다.

---

## 8/19 (수) — 복제 계층 마무리

**읽을 것 (386줄)**

| 파일 | 줄 | 왜 읽나 |
|---|---|---|
| `NetIdentity.cs` | 66 | 짧지만 **모든 판정의 뿌리**. 세 프로퍼티를 말로 설명할 수 있어야 한다 |
| `NetTransform.cs` | 151 | 위치 동기화. 보간 3종(`None`/`Lerp`/`Snapshot`)의 차이 |
| `NetSpawnPool.cs` | 132 | 풀링. `poolOfObject` 역인덱스, `spawnPosition`이 왜 필요했나 |
| `NetKnockback.cs` | 37 | 짧다 |

### 붙잡을 것

- **`IsMine` / `IsMineOrOffline` / `IsSimulatedHere`** — 셋이 갈리는 지점. 봇은 왜 호스트에서 `IsMine`이 참인가
- **보간(interpolation)** — 20Hz로 오는 좌표를 60fps 화면으로 펴는 방법. `InterpDelay`가 왜 필요한가
- **`NetTransform.CurrentMode`가 `static`인 것** — 오브젝트마다가 아니라 게임 전체 설정

### 확인할 질문

- `SendIfDue`의 `sendTimer -= interval`이 왜 `= 0`이 아닌가 (로비 비콘은 `= 0`이었다)
- 풀에 넣을 수 있는 것과 없는 것의 기준 (`IsPoolable`)
- 씬 배치 오브젝트를 `Pool.Release`하면 왜 안 되나

---

## 8/20 (목) — 흐름 마무리 + 사람 캐릭터

**읽을 것 (1,319줄)**

**Flow 남은 것 (565줄)**

| 파일 | 줄 | |
|---|---|---|
| `LanScoreboard.cs` | 98 | 순위 수집. `Collect()`가 살아있는 사람만 모으는 방식 |
| `LanStandings.cs` | 140 | 최종 순위 계산·전송. `LanGameFlow.HostEndGame`이 부르던 것 |
| `LanSpectator.cs` | 201 | 관전. 대상 목록·전환 규칙 |
| `LanSpawnPoints.cs` | 126 | 스폰 지점 배분 |

**Player (619줄)**

| 파일 | 줄 | |
|---|---|---|
| `LanPlayerSetup.cs` | 104 | 내 것/남의 것 초기화 갈림 |
| `LanPlayerState.cs` | 251 | 점수·이름·플래그 |
| `LanPlayerVisual.cs` | 264 | 크기·색·애니메이션 |

### 붙잡을 것

- **`PlayerFlags`가 `[Flags]` 비트인 이유** — `Eliminated`와 `Absorbed`를 따로 두는 까닭
- **`GrowKind`가 셋인 이유** — 젤리/흡수/배트가 왜 다른 성장인가
- **관전 대상이 죽으면 자동 전환하지 않는 규칙**

### 확인할 질문

- 초콜릿 접촉 탈락에서 사람·플레이어봇·배회젤리 셋을 어떻게 가르나 (`ChocolateFluid.OnTriggerEnter`)
- `LanPlayerState`의 점수가 0.25초마다 갱신되는 까닭

---

## 8/21 (금) — 규칙 (모드)

**읽을 것 (1,264줄)**

읽는 순서를 반드시 지킬 것.

| 순서 | 파일 | 줄 |
|---|---|---|
| 1 | `NetGameMode.cs` | 117 | 두 모드의 공통 골격. `<T>`가 하는 일 |
| 2 | `NetEntity.cs` | 135 | 엔티티 질문 모음 (젤리인가? 탈락했나? 크기는?) |
| 3 | `HostVerdict.cs` | 78 | 호스트 검증 6단계 |
| 4 | `PushMode.cs` | 327 | 더 단순한 쪽부터 |
| 5 | `AbsorbMode.cs` | 607 | 오늘의 산 |

### 붙잡을 것

- **`HostVerdict.Judge`의 6단계를 순서대로 외운다.**
  호스트인가 → 판이 도는가 → 둘 다 존재하는가 → 요청자가 그 캐릭터의 주인인가 → 같은 편이 아닌가 → 둘 다 살아있는가
- **네 번째(소유권)가 왜 필요한가.** `from.Id`는 소켓에서 나와 위조 불가, 메시지 속 `netId`는 위조 가능
- **감지는 정확한 곳에서, 판정은 권한 있는 곳에서.** 호스트는 물리를 다시 안 돌리고 거리만 잰다

### 확인할 질문

- `ResolveEat`만 `HostVerdict`를 안 쓰는 이유. 넓힐 수 있는가
- `eatChaseTolerance`가 실제 접촉 거리의 6배인 까닭
- `RestoreIfRejected`가 "살아있으면 거부된 것"으로 판단하는 방식

---

## 8/22 (토) — 봇과 AI ①

**읽을 것 (약 1,050줄)**

| 파일 | 줄 | |
|---|---|---|
| `LanBotSpawner.cs` | 181 | 봇 생성. `botPrefabId` 자동 탐색 |
| `LanBotState.cs` | 190 | 봇 상태 + 동기화 (`LanPlayerState`와 짝) |
| `AI/FSM/AIBaseState.cs` | ~20 | 상태 기계의 틀 |
| `AI/FSM/` 상태 4개 | ~660 | Wander / Chase / Flee / PushSurvive |

### 붙잡을 것

**봇은 호스트에서만 생각하고 결과만 방송된다.** 그런데 전송 방식이 사람과 다르다.

| | 보내는 것 | 왜 |
|---|---|---|
| 사람 | **"무슨 일이 있었다"** (`GrowEvent`) | 클라도 원인을 알아 연출을 재현할 수 있다 |
| 봇 | **"지금 크기는 이만큼"** (절대값) | 클라는 봇이 뭘 먹었는지 모른다 |

이 차이가 실제 버그를 만들었다 — `LanBotState`가 절대값을 5Hz로 쓰고 `NetScale`도 같은 값을 lerp로 써서
봇 크기가 쑥 작아졌다 커졌다 했다. `NetScale`을 지워서 해결했다.
**어느 쪽을 지웠어야 했는지, 왜 그쪽인지** 스스로 논증해볼 것.

### ★ 크기 파이프라인 조사 (8/24와 이어짐)

**크기 하나에 진실이 셋이다.** 이름이 다른데 값의 출처가 다르다.

| 어디 | 무엇을 읽나 |
|---|---|
| `LanPlayerState.ScaleValue` | `PlayerScaleController.currentScaleValue` (게임 논리값) |
| `AIPlayerMovement.GetMyAuthorityScale()` | `transform.localScale.x` (실제 트랜스폼) |
| `NetEntity.ScaleOf()` | `LanPlayerVisual.ScaleValue`, 없으면 **1f** |

그리고 `LanBotState`는 **보낼 때와 받을 때가 다르다.**

```csharp
float CurrentScale                                    // 호스트가 보낼 때
{
    if (scaleCtrl != null) return scaleCtrl.currentScaleValue;   // 논리값
    return transform.localScale.x;
}

private void FollowScale()                            // 클라가 받을 때
{
    transform.localScale = Vector3.Lerp(...);         // 트랜스폼에만 적용
}
```

**클라에서는 `scaleCtrl.currentScaleValue`가 갱신되지 않는다.** 그러면 클라의
`NetEntity.ScaleOf(봇)`은 무슨 값을 돌려주나? 흡수·배트 사거리 판정이 그걸 쓰는데?

확인할 것:

1. 봇 프리팹에 `PlayerScaleController`와 `LanPlayerVisual`이 둘 다 붙어 있나
2. `NetEntity.ScaleOf`가 봇에 대해 `1f`로 빠지는 경로가 실제로 도는가
3. 호스트와 클라의 봇 크기 판정이 갈라지는가 (갈라지면 판정 불일치)
4. 이름을 `ScaleValue`로 통일할 수 있나 — **값이 같아진 다음에** 통일할 것.
   지금 통일하면 다른 값을 같은 이름으로 부르게 되어 더 위험하다

> `NetScale`을 지웠던 그 문제와 같은 계열이다. **크기에 권위가 여럿이면 반드시 갈라진다.**

### 확인할 질문

- FSM의 상태 전환을 누가 결정하나 (`EvaluateAndTransition`)
- 상태 객체가 봇마다 새로 생기나, 공유되나

---

## 8/23 (일) — AI ②

**읽을 것 (약 1,700줄)**

| 파일 | 줄 | |
|---|---|---|
| `AIPlayerMovement.cs` | 1,103 | **전부 읽지 말 것.** `ChangeState`, `EvaluateAndTransition`, `Update`, `OnEliminated` |
| `AIDetector.cs` | ~200 | 탐지 로직 |
| `EnemyAI/JellyAgentAI.cs` | 134 | 젤리 AI 기반 클래스 |
| `EnemyAI/WanderingAI.cs`, `AIWaypointPatrol.cs` | ~270 | 하위 클래스 둘 |

### 붙잡을 것

- **`JellyAgentAI`가 상속을 쓴 이유.** 하위가 채우는 건 `OnBecameDriver`(첫 목적지)와 `DriveUpdate`(매 프레임) 둘뿐
- **`NavMeshAgent.baseOffset`** — 에이전트는 자기 위치보다 **아래**에서 NavMesh를 찾는다. 스폰 위치가 어긋나면 "not close enough" 경고

### 확인할 질문

- 봇을 굴리지 않는 쪽에서 `NavMeshAgent`만 끄고 AI 스크립트는 살려두는 이유
- 사람 플레이어와 봇이 같은 `PlayerAbsorber`를 쓰는가

---

## 8/24 (월) — 플레이어와 흡수

**읽을 것 (약 2,100줄)**

| 범위 | 줄 | |
|---|---|---|
| `Player/PlayerFSM/` | 645 | `PlayerMovement` + 상태 6개. AI FSM과 **같은 패턴**이라 빨리 읽힌다 |
| `Player/` | 884 | `PlayerScaleController`(크기의 유일한 권한자), `PlayerBridge`, `BotBridge` |
| `Absorbing/` | 581 | `JellyColliderAbsorb`, `PlayerAbsorber`, `JellyObject` |

### 붙잡을 것

**흡수 한 번에 몇 개의 시스템이 관여하는가.** 종이에 그릴 것.

```
트리거 감지 → 연출 시작 → 호스트에 요청 → 검증 → 승인 방송 → 성장·색·점수 → 젤리 파괴
   (클라)      (클라)        (클라)      (호스트)   (호스트)      (전원)        (호스트)
```

각 단계가 어느 파일인지 적는다.

**연출은 예측하고 보상은 예측하지 않는다.** 클라는 렌더러를 즉시 끄지만 성장·점수는 호스트 확정을 기다린다.
거부되면 `RestoreIfRejected`가 1.5초 뒤 되돌린다.

**`PlayerScaleController`가 크기의 유일한 권한자.** `_pendingScale`과 `QueueScaleChange`가 왜 필요한지
(연달아 먹으면 어떻게 되는지) 확인할 것.

**8/22의 크기 파이프라인 조사를 여기서 마무리한다.** `currentScaleValue`(논리값)와
`transform.localScale`(실제 크기)의 관계를 확정하고, 봇·사람이 같은 규칙을 쓰는지 본다.
같아진 뒤에 `GetMyAuthorityScale` → `ScaleValue`로 이름을 통일한다.

---

## 8/25 (화) — 맵

**읽을 것 (2,129줄)**

| 파일 | 줄 | |
|---|---|---|
| `TileCollapseManager.cs` | ~840 | **`GetSyncedElapsed`부터.** 링 붕괴 + 밟기 마모 |
| `FallingTile.cs` | ~410 | 발판 하나의 생애 |
| `ChocolateFluid.cs` | ~200 | 초콜릿 탈락 |
| `Milk.cs`, `JellyCrusher.cs` 등 | 나머지 | 장애물들 |

### 붙잡을 것

- **`GetSyncedElapsed`가 -1을 돌려주면 링이 한 번도 안 무너진다.** 에러가 안 나서 증상만으로는 원인을 못 찾는다
- **`_needsStepGrace`** — 첫 패스에 전 개체의 타일을 동시에 마모시키지 않으려는 장치
- **`standGap`** — 점프 중에도 발판이 밟힌 걸로 판정되던 문제

### 확인할 질문

- 붕괴 판정은 호스트만 하는데 왜 마모 카운트를 전 클라에 복제하나
- 사람 플레이어를 발판이 건드리지 않는 이유

---

## 8/26 (수) — UI·보조 + 전체 이어붙이기

**읽을 것 (약 2,500줄, 훑는 수준)**

| 범위 | 줄 | 깊이 |
|---|---|---|
| `Net/UI/` | 295 | 방 목록·순위표. 얕게 |
| `Net/Debug/LanDiagnostics.cs` | 163 | F1 화면. 지금까지 읽은 게 다 나온다 |
| `UI/LoadingSceneController.cs` | ~390 | 커튼 전환. **얽혀 있으니 시간 잡을 것** |
| `UI/Result/GameResultManager.cs` | ~470 | 결과 씬 |
| `Color/`, `DataManagement/` | 584 | 색 계산·설정값 |
| `UI/InGameUI/`, `UI/UIManagement/` | 866 | 훑기 |

> `UI/DoTween/` (1,090줄)과 `JellyMesh/SoftBody3D.cs` (181줄)는 **읽지 않는다.**
> 연출과 Cloth 물리라 게임 로직과 무관하다. 필요할 때 보면 된다.

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
| `HostVerdict` | 흡수·밀치기가 같은 검증을 각자 적어둬서 한쪽만 고쳐지곤 했다 |
| `LanBotState`의 절대값 전송 | 클라는 봇이 뭘 먹었는지 몰라서 재현할 수 없다 |
| `FramedConnection`의 `SelectRead` | `Poll`을 먼저, `Available`을 나중에. 순서를 바꿨더니 멀쩡한데 "연결 끊김"이 떴다 |
| `NetSpawnPool`의 `spawnPosition` | `NavMeshAgent`는 켜지는 순간의 자리에서 NavMesh를 찾는다 |
| `TileCollapseManager`의 `standGap` | 점프 중에도 발판이 밟힌 걸로 판정됐다 |
| `SpawnForOwner`의 "이미 있으면 반환" | `SceneReadyLoop`가 0.4초마다 재시도한다. 없으면 캐릭터가 여러 개 생긴다 |
| `AbsorbMode.eatChaseTolerance` (5) | 흡수 연출 0.5초 동안 도망쳐서, 접촉 거리로 재면 정상 플레이가 전부 거부됐다 |
| `NetScale`이 **없는** 것 | 크기를 셋이 동시에 써서 봇이 쑥 작아졌다 커졌다 했다 |
| `NetHost.AcceptingNewPeers` | 게임 시작 후 붙은 클라는 캐릭터가 없어 화면만 멈춰 있었다 |
| `LanGameFlow.endingStarted` | 정상 종료와 사고가 소켓 입장에선 똑같이 보인다. 진행 단계로 구분한다 |
| `LanFlowHud.StopCenterAnim` | `FlashRoutine`이 알파를 0으로 내린 채 살아 있으면 "게임 종료!"를 지워버린다 |
| `NetManager.Offline` | 판이 끝나고 커튼이 도는 동안 게임 씬은 살아 있는데 소켓은 닫혀 있다 |

`REVIEW_NOTES.md`에 구조 분석이 더 있다.

---

## 아직 안 정한 것 (읽다가 마주치면 결정)

- `Game_io_PushMode` 씬의 `spectateButton` / `returnToMainButton` **미연결** — 밀치기 판에서 관전·퇴장 버튼이 안 뜬다
- `LanLobby.statusText` **미연결** — 유효성 메시지가 화면에 하나도 안 나온다
- `LanRoomListUI.emptyHint` / `statusText` **미연결** — 방이 없을 때 안내가 없다
- `StartHost()` / `JoinHost()`가 실패를 안 알린다 — 포트 충돌·IP 오타 시 영원히 "기다리는 중"
- 클라가 로비에서 인원수를 못 본다 (`"접속됨"`만 표시)
- `RandomJellySpawner`의 `[ContextMenu]` 2개 (에디터 맵 작업 도구)
- **`NetManager`·`LanDiscovery`가 게임 씬에도 붙어 있다** — 로비에서 `DontDestroyOnLoad`로 넘어오므로
  게임 씬 것은 중복 가드에 걸려 파괴되고 경고 로그가 뜬다. 로비를 항상 거친다면 뺄 수 있다
- `INetEntity` 도입 — `LanPlayerState`/`AIPlayerMovement`가 공통 인터페이스를 구현하면
  `LanScoreboard.Collect`의 사람용·봇용 두 벌 루프가 하나로 합쳐진다 (8/21 `NetEntity` 읽은 뒤)
- `PlayerAbsorbingManager`(39줄)를 `PlayerBridge`로 합치기 — `PlayerAbsorber`의 이벤트 둘을
  서로 다른 컴포넌트가 하나씩 나눠 듣고 있다 (8/24)
- ~~크기 파이프라인 통일~~ **완료** — 판정용 크기는 전부 `PlayerScaleController.currentScaleValue`로
  모았다. `AIPlayerMovement.GetMyAuthorityScale`이 `transform.localScale.x`(보이는 크기)를
  돌려주던 게 원인이었고, `AIPushSurviveState`·`PlayerAttackState`가 직접 읽던 곳도 정리했다.
  `NetEntity.ScaleOf`가 유일한 외부 창구이고 `PlayerMovement.AuthorityScale`이 사람 쪽 대응물이다
- ~~점수 경로 통일~~ **완료** — `PlayerBridge.PushScore`/`LanPlayerState.ReportOwnScore`를 없앴다.
  `LanPlayerState.Score`는 이제 호스트만 만든다(`HostRecomputeScore` + `HostAddScore`).
  `GameState.CurrentScore`는 내 화면 HUD용 예측치로 남는다
- ~~`NetIdentity` 컴포넌트 캐시~~ **완료** — `id.PlayerState` / `.Visual` / `.BotState` / `.Bot`.
  코드 30줄의 `GetComponent`가 사라졌다
- ~~`GamePhase.Countdown` 도입~~ **완료** — `countdownRunning` 플래그와 `MsgType.CountdownStart`가
  단계 하나로 합쳐졌다
- **★ 봇/사람 중복 통합 — 남은 것 (내일)**
  `AIPlayerMovement` 1070줄 중 약 150줄이 아직 사람 쪽 FSM의 사본이다.
  오늘 흡수 연출·배트 회전·넉백 곡선 셋은 합쳤다. 남은 것을 쉬운 순서로:

  | | 봇 | 사람 | 합칠 자리 |
  |---|---|---|---|
  | 발밑 지면 검사 | `CheckGroundBelow` 18줄 | `PlayerMovement.ApplyGravity` 12줄 | 공용 정적 헬퍼 |
  | 배트 명중 판정 | `DetectBatHit` 56줄 | `PlayerAttackState.DetectBatHitLan` 42줄 | `BatArcQuery` 정적 클래스 |
  | 공격 발동 | `TryAttack` 16줄 | `PlayerAttackState.Enter` 27줄 | 쿨다운·트리거 공통부만 |
  | 대시 | `TryDash` 19줄 | `PlayerDashState` 19줄 | FSM 구조가 달라 어려움 |
  | 탈락 처리 | `ApplyEliminatedLocally` 20줄 | `LanPlayerState.OnBecameOutOfPlay` 24줄 | 아래 `IsOutOfPlay` 건과 같은 뿌리 |

  위 둘(지면 검사·명중 판정)은 오늘 한 것과 같은 패턴이라 위험이 낮다.
  아래 둘(대시·탈락)은 구조를 먼저 정해야 한다

- **`IsOutOfPlay`의 위치가 사람과 봇이 다르다** — 사람은 `LanPlayerState`(네트워크 상태),
  봇은 `AIPlayerMovement`(이동 컨트롤러)에 있다. `LanBotState`가 `LanPlayerState`의 짝인데
  정작 판정만 다른 데 있어서 `NetEntity.IsOutOfPlay`가 그 비대칭을 `if (id.IsBot)`로 흡수한다.
  단순 이동으로는 안 된다 — 사람은 `Flags`(네트워크로 오는 비트)가 출처인데 봇은
  `IsEliminated || IsBeingAbsorbed`(로컬 bool 둘)다. **봇도 `PlayerFlags`를 쓰게 해서
  `LanBotState`가 들고, `AIPlayerMovement`는 읽기만** 하면 `NetEntity`의 분기도 없앨 수 있다.
  크기 파이프라인 통일과 같은 성격이라 8/24에 묶어서 (8/24)
- ~~메시지 라우팅 테이블 이관~~ **완료** — `NetManager.RouteHost`/`RouteClient`로
  호스트 8종·클라 21종 전부 이관. `OnHostMessage`/`OnClientMessage` 이벤트와
  `NetGameMode.Handle*Message`는 삭제했다. 이제 `MsgType`마다 주인이 정확히 하나이고,
  주인 없는 타입은 로그에 남으며, 중복 등록은 `LogError`로 즉시 잡힌다
- `MenuUI.cs` — 어느 씬에도 붙어 있지 않다. 파일째 지울지
- `MsgType.GameOver`가 `WinnerNetId`·`WinnerScore`를 싣는데, 받아서 하는 일이
  `AddLog` 한 줄뿐이다. 결과 화면은 `LanScoreboard.FinalStandings`를 쓴다 —
  메시지 본문을 비워도 되는지 8/20(`LanStandings`)에서 확인
