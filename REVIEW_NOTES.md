# 게임 시퀀스 코드 구조 분석 (REVIEW_NOTES)

> 작성일: 2026-08-01 · 대상 브랜치: `claude/fervent-galileo-g2j2lu`
> 분석 범위: **게임 시퀀스(흐름) 관련 스크립트** — 씬 전환, 게임 시작/클리어/실패, UI 상태, 데이터 매니저, 오디오 싱글턴
> 성격: **구조 개선점 도출용 메모**입니다. 실제 코드는 아직 수정하지 않았습니다. (버그 항목만 사용자 승인 후 적용)

---

## 0. 게임 시퀀스 전체 흐름 (내가 파악한 그림)

학습 목적이시니, 먼저 코드가 어떻게 이어지는지 한눈에 정리했습니다.

```
[타이틀] SceneLoader.LoadGame() / ProloguePanelSequence
   → (프롤로그 슬라이드 연출: DOTween)
   → SceneManager.LoadScene("Game")
        │
        ├─ DataManager.Awake()   : 이번 판 규칙(색/목표레벨) 랜덤 결정, 데이터 초기화
        ├─ UIManager.Start()     : 페이드 인 + 초기 UIState(InGame) 설정
        ├─ StageTitleUI.Start()  : "스테이지 타이틀" 페이드 연출
        └─ GameTimer.Start()     : 제한시간 카운트 시작
              │
              ├─ (시간 초과) GameTimer.GameFail()  → 실패 연출 + timeScale=0
              └─ (저울 밟고 조건 충족) ClearJudge.JudgeClear() → ClearSequence() → UIState.GameOver
```

**핵심 관찰:** "게임이 끝났다"를 판단하고 처리하는 주체가 `GameTimer`(실패)와 `ClearJudge`(성공) **두 곳으로 나뉘어** 있고, 둘 사이에 서로를 알려주는 연결이 없습니다. 이게 아래 여러 개선점의 뿌리입니다.

---

## A. 아키텍처 (Architecture)

### A-1. 중앙 게임 상태 관리자(GameManager)의 부재 — ★★★ 최우선
- **현상:** 게임 종료 처리가 `ClearJudge.JudgeClear/ClearSequence`, `GameTimer.GameFail`, `UIManager.OnEnterState`에 흩어져 있습니다. 세 곳 모두 각자 `Time.timeScale`을 만지고, `playerController.enabled = false`를 하고, 사운드를 재생합니다.
- **왜 문제인가:** "게임 종료 시 해야 할 일"이 한 곳에 없어서, 나중에 종료 처리 하나(예: 자동저장, 통계 전송)를 추가하려면 세 파일을 다 고쳐야 합니다. 빠뜨리면 성공에는 되는데 실패에는 안 되는 식의 버그가 생깁니다.
- **개선 방향:** `GameFlowManager`(또는 `GameStateController`)를 만들어 `Playing / Cleared / Failed / Paused` 상태를 **단일 소유(single source of truth)**로 두고, `ClearJudge`·`GameTimer`는 "성공/실패 신호만 보내는" 역할로 축소. 종료 시 공통 처리는 `GameFlowManager.EndGame(result)` 한 군데에 모읍니다.

### A-2. 싱글턴 생명주기 패턴이 스크립트마다 다름 — ★★★
- **현상:**
  - `PlaySFXAudio`: `Awake`에서 null 체크 + `DontDestroyOnLoad` (정석적인 영속 싱글턴)
  - `DataManager`: `Awake`에서 null 체크는 하지만 `DontDestroyOnLoad` 없음 → 씬마다 새로 생성/재랜덤화
  - `UIManager`: `Awake`에서 **null 체크 없이** `Instance = this;` (중복 시 나중 것이 무조건 덮어씀, 이전 것 파괴 안 함)
  - `Memory` (`DontDestoryOnLoad/Memory.cs`): `Start`에서 `DontDestroyOnLoad`만 호출, **싱글턴 가드 자체가 없음**
- **왜 문제인가:** 패턴이 제각각이면 "이 매니저는 씬 넘어가도 살아있나?"를 매번 코드를 열어봐야 압니다. 특히 `Memory`는 가드가 없어서 Game 씬을 재진입할 때마다 `DontDestroyOnLoad` 객체가 **중복 누적**됩니다(항목 B-3).
- **개선 방향:** 영속/비영속 싱글턴 정책을 문서로 확정하고, 공통 베이스(예: `Singleton<T>`)로 통일. `Awake`에서 처리(Start는 순서 보장이 약함).

### A-3. 매니저 간 인스펙터 직접 참조로 인한 강결합(tight coupling) — ★★
- **현상:** `ClearJudge`가 `resultStarsUI, gameTimer, uiManager, uIPoolManager, playerController`를 전부 인스펙터 참조로 들고 있고, `GameTimer`도 `resultStarsUI, uiManager, softBody3D, playerController, playerAnimController, mainCamera_Action`를 직접 물고 있습니다.
- **왜 문제인가:** 참조가 하나라도 인스펙터에서 빠지면(프리팹 교체·씬 복제 시 흔함) 런타임 `NullReferenceException`. 또 A-1의 원인이기도 합니다(서로를 직접 조작).
- **개선 방향:** C# `event`/`UnityEvent` 또는 가벼운 이벤트 버스(`GameEvents.OnGameCleared`, `OnGameFailed`)로 "알림"과 "반응"을 분리. UI는 이벤트를 구독만 하도록.

### A-4. 씬 이름·상태 문자열이 코드 곳곳에 하드코딩(매직 스트링) — ★★
- **현상:** `"Game"`, `"Main"`, `"TileScene"`, `"LevelDesign"`(주석), `"FadeIn"/"FadeOut"` 등이 여러 파일에 문자열로 흩어져 있음 (`SceneLoader`, `UIManager`, `SceneChanger`).
- **왜 문제인가:** 오타가 나도 컴파일 에러가 안 나고 **런타임에 조용히 실패**합니다(씬이 안 넘어감). 씬 이름을 바꾸면 전수 검색이 필요.
- **개선 방향:** `static class SceneNames { public const string Game = "Game"; ... }` 상수 클래스로 모으기. 문자열 대신 참조 한 곳.

### A-5. 입력 처리 분산 + 레거시/신규 Input System 혼용 — ★★
- **현상:** `ProloguePanelSequence`, `ClearJudge`, `UIManager`, `StageTitleUI` 등이 각자 `Input.GetKeyDown / GetMouseButtonDown / anyKeyDown`(레거시 Input Manager)을 폴링합니다. 그런데 프로젝트에는 신규 Input System 에셋(`InputSystem_Actions.inputactions`)도 존재합니다.
- **왜 문제인가:** Project Settings의 Active Input Handling이 "Input System Package(New)" 단독이면 레거시 `Input.xxx`가 **런타임 예외/무반응**이 됩니다. 입력 로직이 흩어져 있어 나중에 리매핑·모바일 터치 대응도 힘듭니다.
- **개선 방향:** 입력 방식을 하나로 확정. 신규 Input System으로 갈 거면 입력을 `InputReader` 한 곳에 모아 이벤트로 뿌리기. 당장은 최소한 Active Input Handling = "Both"인지 확인.

### A-6. 페이드(Fade) 연출 로직 3중 중복 — ★★
- **현상:** 같은 알파 보간 페이드가 `ClearJudge.FadeRoutine`, `UIManager.Fade/SceneFade`, 주석 처리된 `SceneChanger.FadeIn/FadeOut` 세 곳에 각각 구현돼 있습니다.
- **왜 문제인가:** 페이드 시간·Raycast 처리 규칙을 바꾸면 세 곳을 따로 고쳐야 하고, 이미 미묘하게 동작이 다릅니다(unscaled 사용 여부 등).
- **개선 방향:** `FadeUtility.Fade(CanvasGroup, from, to, duration)` 공용 코루틴(또는 DOTween `DOFade`)으로 단일화.

### A-7. 페이드 방향을 문자열 `"FadeIn"/"FadeOut"`로 제어 — ★
- **현상:** `UIManager.Fade(cg, "FadeIn", ...)`. `StageTitleUI` 주석에는 "FadeOut이 사실 0→1이라 헷갈린다"고 스스로 적어 두셨습니다(의미가 반전돼 있음).
- **왜 문제인가:** 오타(`"Fadein"`)가 컴파일 에러 없이 조용히 아무 동작도 안 하게 만들고, 의미가 반전돼 읽는 사람이 헷갈립니다.
- **개선 방향:** `enum FadeDirection { In, Out }` 또는 `bool fadeIn` 파라미터로 교체.

### A-8. 사실상 죽은 코드(dead code) 정리 필요 — ★
- **현상:** `Memory` 클래스의 유일한 소비자였던 `SceneChanger.cs`가 **파일 전체 주석 처리**되어 있어, `Memory.enableFadeIn`을 쓰는 코드가 없습니다. `DataManager`에도 대량의 주석 블록(구 CSV 로딩 로직)이 남아 있습니다.
- **왜 문제인가:** 죽은 코드가 남으면 "이게 아직 쓰이나?"를 매번 확인해야 하고, 리팩터링 판단을 흐립니다.
- **개선 방향:** 사용 안 하는 `SceneChanger`, `Memory`, 주석 로직은 커밋 이력으로만 남기고 삭제(git이 기록하니 안전).

---

## B. 버그 가능성 차단 (Bug Prevention)

### B-1. `DataManager.GetJellyEffect` — null 체크 전에 역참조 → NRE ★★★ (실제 버그)
- **위치:** `Assets/Scripts/DataManagement/DataManager.cs:162-167`
```csharp
var data = jellyEffects.Find(x => x.type == type);
Debug.Log(data.rgbChange);              // ← data가 null이면 여기서 터짐
return data != null ? data.rgbChange : Vector3Int.zero;   // 이 방어는 도달 못함
```
- **문제:** `Find`가 못 찾으면 `data == null`인데, 바로 다음 줄 `Debug.Log(data.rgbChange)`가 null을 역참조해 `NullReferenceException`을 던집니다. 정작 아래 `data != null` 삼항 방어는 실행되지도 못합니다.
- **재현:** `jellyEffects` 리스트에 없는 `JellyColorType`으로 호출 시 즉시 예외.
- **수정(제안):** `Debug.Log` 줄을 null 체크 안으로 옮기거나 제거.

### B-2. 성공(Clear)과 실패(Fail) 동시 발생에 대한 가드 부재 — ★★★
- **위치:** `ClearJudge.isCleared` vs `GameTimer.isGameEnded` (서로 독립)
- **문제:** 제한시간이 0이 되는 그 프레임에 저울 조건도 충족되면, `GameFail()`(timeScale=0, 실패 연출)과 `JudgeClear()`(클리어 연출)이 **둘 다** 실행될 수 있습니다. 결과: timeScale 충돌, 실패·성공 UI 동시 표시, 이중 사운드.
- **개선 방향:** A-1의 `GameFlowManager`가 "이미 종료됨" 하나의 플래그를 소유하고, 성공/실패 진입을 그 한 곳에서 원자적으로 막기.

### B-3. `Memory`가 씬 재진입마다 중복 누적 — ★★
- **위치:** `Assets/Scripts/DontDestoryOnLoad/Memory.cs:6-9`
- **문제:** `Start`에서 `DontDestroyOnLoad`만 하고 싱글턴 가드가 없어, `Memory`가 있는 씬으로 되돌아올 때마다 파괴되지 않는 `Memory` 객체가 하나씩 늘어납니다(메모리 누수·중복 로직 위험). 현재는 소비자가 없어 잠재적이지만, 되살리면 바로 문제.
- **개선 방향:** 싱글턴 가드 추가 후 재사용하거나, 안 쓸 거면 삭제(A-8).

### B-4. 싱글턴 널 참조 위험 — 방어 없이 `Instance` 접근 — ★★
- **위치 예:** `SceneLoader.cs:8` `PlaySFXAudio.Instance.PlayButton1Sound();`, `ClearJudge.cs:88` `DataManager.Instance.currentColor = ...` 등 다수.
- **문제:** 해당 씬에 매니저 오브젝트가 없거나 아직 `Awake` 전이면 `Instance == null` → NRE. 특히 씬을 **단독 실행(개발 중 흔함)**하면 바로 터집니다.
- **개선 방향:** 최소한 핵심 진입점에 `if (PlaySFXAudio.Instance != null)` 방어, 또는 부트스트랩 씬에서 매니저 생성을 보장하는 초기화 순서 확립.

### B-5. 출시 빌드에 노출된 치트/디버그 키 — ★★
- **위치:** `ClearJudge.cs:86-90` `Input.GetKeyDown(KeyCode.O)` → 현재색/스케일을 목표값으로 즉시 세팅(사실상 즉시 클리어). 유사하게 `UIManager` 등에도 개발용 입력이 섞여 있음.
- **문제:** 릴리스 빌드에서도 'O' 키로 즉시 클리어가 됩니다.
- **개선 방향:** `#if UNITY_EDITOR ... #endif`로 감싸거나 `[SerializeField] bool debugMode` 게이트 뒤로. (게임플레이 동작이 바뀌므로 **승인 후 적용** 권장)

### B-6. `rangeRules` 고정 인덱스 접근(0~5) — IndexOutOfRange 위험 — ★
- **위치:** `DataManager.DetermineCurrentColor` (`rangeRules[0]`~`[5]`), `Awake`의 `rangeRules[index]`
- **문제:** 인스펙터에서 `rangeRules`가 6개 미만이면 `IndexOutOfRangeException`. 색 규칙이 인덱스 순서에 암묵적으로 종속돼 있어(0=Red 가정) 순서만 바꿔도 판정이 깨집니다.
- **개선 방향:** `Dictionary<JellyColorType, ColorRangeRule>`로 타입 키 접근, 시작 시 개수 검증 로그.

### B-7. `Time.timeScale` 전역 상태의 취약성 — ★★
- **문제:** `UIManager.OnEnterState`(Settings/Pause/GameOver=0, InGame=1), `GameTimer.GameFail`(0), `GameTimer.Start`(1) 등 여러 곳이 전역 `Time.timeScale`을 직접 씁니다. GameOver 후 `timeScale=0`이 남은 상태에서 다음 게임/씬으로 넘어가면 **화면이 멈춘 채 시작**될 수 있습니다.
- **개선 방향:** timeScale을 만지는 지점을 A-1의 상태 관리자로 일원화하고, 씬 로드 직후 항상 `1f`로 리셋 보장.

---

## C. 안정적인 네트워크 연동 (Network)

### C-1. 현재 네트워크 계층이 전혀 없음 — 지금은 정상, 대비만 제안 — 참고
- **현상:** 전체 스크립트에 `UnityWebRequest / System.Net / Firebase / Netcode` 등 네트워크 코드가 **하나도 없습니다.** 즉 지금 리뷰할 "네트워크 버그"는 없습니다.
- **향후(점수/리더보드/저장 서버 붙일 때) 미리 정할 원칙 — 학습용 체크리스트:**
  1. **비동기·논블로킹:** `UnityWebRequest`는 코루틴 또는 `async/await`(Awaitable)로. 절대 메인스레드 블로킹 금지.
  2. **타임아웃·재시도:** 요청마다 `timeout` 설정 + 지수 백오프 재시도(예: 2s→4s→8s). 이 리뷰 루틴의 git 재시도 정책과 같은 개념.
  3. **취소 처리:** 씬 전환/게임 종료 시 진행 중 요청을 취소(`CancellationToken`)해 파괴된 오브젝트 콜백 NRE 방지 — 위 B-4의 네트워크 버전입니다.
  4. **DTO 분리:** 서버 JSON ↔ 게임 데이터는 별도 DTO로 매핑(이미 `JellyDataDTO/DAO` 패턴이 있으니 그 방식 확장).
  5. **실패 UI 상태:** `UIState`에 `NetworkError/Loading`를 추가해 로딩·오류를 상태로 관리(현 UIManager 구조와 잘 맞음).

---

## D. 기타 안정성 / 유지보수

### D-1. `NextSceneManager`의 매직 넘버와 주석 불일치 — ★
- **위치:** `NextSceneManager.cs:13` `Destroy(loadingAni.transform.root.gameObject, 1.0f);`
- **문제:** 주석은 "약 0.4초 뒤"라고 적혀 있는데 실제 코드는 `1.0f`. 이런 주석-코드 불일치는 나중에 오해를 부릅니다. 또 `FindAnyObjectByType`는 비용이 있으니 Start 1회면 괜찮지만 습관적으로 남발하면 위험.
- **개선 방향:** 지연 시간을 `[SerializeField]` 상수로 빼고 주석 동기화.

### D-2. `ClearSequence`가 진행되는 동안 참조 객체 파괴 위험 — ★
- **위치:** `ClearJudge.ClearSequence` (uiManager, resultStarsUI, uIPoolManager 등 순차 접근)
- **문제:** 긴 코루틴 중간에 `uiManager.SetState(GameOver)`로 timeScale=0이 되고 여러 UI를 켜고 끕니다. 만약 시퀀스 도중 씬이 바뀌거나 참조 오브젝트가 꺼지면 NRE. 확률은 낮지만 A-3(강결합)과 결합하면 커집니다.
- **개선 방향:** 시퀀스 시작 시 필수 참조 null 검증, 이벤트 기반으로 전환(A-3).

---

## 요약 표

| # | 주제 | 항목 | 심각도 | 성격 |
|---|------|------|--------|------|
| A-1 | 아키텍처 | 중앙 GameManager 부재 | ★★★ | 구조(승인 필요) |
| A-2 | 아키텍처 | 싱글턴 패턴 불일치 | ★★★ | 구조 |
| A-3 | 아키텍처 | 매니저 강결합 | ★★ | 구조 |
| A-4 | 아키텍처 | 씬 이름 매직 스트링 | ★★ | 구조 |
| A-5 | 아키텍처 | 입력 처리 분산/혼용 | ★★ | 구조 |
| A-6 | 아키텍처 | 페이드 로직 3중 중복 | ★★ | 구조 |
| A-7 | 아키텍처 | 문자열로 페이드 방향 제어 | ★ | 구조 |
| A-8 | 아키텍처 | 죽은 코드 정리 | ★ | 구조 |
| B-1 | 버그 | GetJellyEffect null 역참조(NRE) | ★★★ | **버그(즉시수정 후보)** |
| B-2 | 버그 | 성공/실패 동시발생 가드 없음 | ★★★ | 버그/구조 |
| B-3 | 버그 | Memory 중복 누적 | ★★ | 버그 |
| B-4 | 버그 | 싱글턴 널 참조 위험 | ★★ | 버그 |
| B-5 | 버그 | 출시 빌드 치트키 'O' | ★★ | 버그(승인 필요) |
| B-6 | 버그 | rangeRules 고정 인덱스 | ★ | 버그 |
| B-7 | 버그 | timeScale 전역 취약성 | ★★ | 버그/구조 |
| C-1 | 네트워크 | 네트워크 계층 부재(대비 제안) | 참고 | 설계 가이드 |
| D-1 | 안정성 | 매직넘버/주석 불일치 | ★ | 유지보수 |
| D-2 | 안정성 | 코루틴 중 참조 파괴 위험 | ★ | 유지보수 |

**총 18개 항목** (A 8, B 7, C 1, D 2).

---

## 다음 단계 (사용자 승인 대기)
- **즉시 수정 후보(저위험):** `B-1`(null 역참조)은 동작 안전하게 고칠 수 있습니다. 승인 주시면 이 항목만 먼저 반영하겠습니다.
- **게임플레이 영향(승인 필요):** `B-5`(치트키), `B-2`(종료 일원화)는 동작이 바뀌므로 확인 후 진행.
- **구조 리팩터링:** `A-1`을 먼저 하면 `A-3 / B-2 / B-7`이 자연히 함께 풀립니다. 순서 추천: **A-1 → A-2 → A-4 → 나머지.**
