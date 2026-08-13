# 코드 파악 계획 (7일 × 2~3시간)

목표: **읽어서 아는 것**이 아니라 **고치고 응용할 수 있는 것**.
그래서 매일 "읽기 → 예측 → 망가뜨리기 → 확인" 순서로 간다.

현재 규모: 145개 파일 / 21,131줄

---

## 이 계획의 규칙

**1. 폴더 순서로 읽지 않는다.** 실행 순서로 읽는다. 바이트가 소켓에 들어와서 화면에 젤리가 움직이기까지의 경로를 따라간다.

**2. 매일 반드시 한 번은 망가뜨린다.** 값을 바꾸거나 줄을 지우고 "무슨 일이 일어날까"를 **먼저 적은 뒤** 실행한다. 예측이 틀린 지점이 진짜 모르는 지점이다.

**3. 예측이 맞았으면 넘어간다.** 다 읽으려 하지 않는다. 아래 "안 읽어도 되는 것"을 참고.

**4. 테스트는 Multiplayer Play Mode로.** 호스트/클라 두 창을 띄워야 보이는 버그가 대부분이다.

### 매일 시작 전 5분

게임을 한 판 켜고 **F1**(`LanDiagnostics`)을 눌러 현재 상태를 본다. 어제 배운 게 이 화면 어디에 나타나는지 짚어본다.

### 안 읽어도 되는 것

- `UI/DoTween/*` — 화면 연출. 게임 로직과 무관
- `UI/Result/GameResultManager.cs`의 시상대 배치 수학
- `JellyMesh/SoftBody3D.cs` 내부 (Cloth 물리는 유니티가 함)
- `Map/AutoGridMapGenerator.cs` — 맵 생성. 필요할 때 보면 됨
- 남아 있는 Photon 흔적 (`photon` 브랜치용 폴백)

---

## 1일차 — 바이트가 흐르는 길

**읽을 것** `Net/Transport/` (1,141줄)

읽는 순서:

1. `NetProtocol.cs` (79) — 메시지 종류 전부. **여기가 이 프로젝트의 목차다**
2. `NetWriter.cs` (88) / `NetReader.cs` (82) — 값을 바이트로, 바이트를 값으로
3. `FramedConnection.cs` (204) — **오늘의 핵심**
4. `NetHost.cs` (206) / `NetClient.cs` (158)
5. `NetManager.cs` (230) — 위 전부를 유니티 생명주기에 붙이는 곳

### 붙잡아야 할 개념

TCP는 **메시지 경계가 없다.** `Send`를 세 번 해도 상대는 한 덩어리로 받을 수 있고, 반대로 한 번 보낸 게 쪼개져 올 수도 있다. 그래서 `[길이4][타입1][본문]` 틀을 직접 씌운다.

`FramedConnection.Poll`의 `offset` 루프가 그 일을 한다. **버퍼에 남은 조각을 앞으로 당기는 부분**을 손으로 그려볼 것.

### 과제

**(A) 새 메시지 하나를 끝까지 뚫기** — 이게 오늘의 전부다.

`Emote`(이모티콘) 메시지를 추가한다:

1. `NetProtocol.cs`에 `Emote = 28` 추가
2. 클라 → 호스트로 보내고, 호스트가 전원에게 방송
3. 받은 쪽이 콘솔에 `P3이 이모티콘 2번`을 찍게

`LanPlayerVisual.Send`와 `PushMode.HandleClientMessage`가 좋은 본보기다.

**(B) 망가뜨리기**

`FramedConnection.Poll`에서 버퍼를 당기는 부분(`Buffer.BlockCopy`)을 주석 처리하고 실행. **무슨 일이 일어날지 먼저 적을 것.**

---

## 2일차 — 무엇이, 어떻게 복제되는가

**읽을 것** `Net/Replication/` (1,276줄)

1. `NetIdentity.cs` (70) — 짧지만 **모든 판정의 뿌리**. `IsMine` / `IsMineOrOffline` / `IsSimulatedHere` 셋의 차이를 말로 설명할 수 있어야 한다
2. `NetWorld.cs` (812) — 오늘의 산. 한 번에 읽지 말고 **메시지 라우팅 switch부터** 볼 것
3. `NetTransform.cs` (151) — 위치 동기화
4. `NetScale.cs` (66) / `NetKnockback.cs` (37)
5. `NetSpawnPool.cs` (130) + `INetPoolable.cs`

### 붙잡아야 할 개념

**호스트 권한(host authority).** 클라는 "내가 이걸 했다"고 **요청**만 하고, 호스트가 검사해서 **결과를 방송**한다. 클라가 직접 결과를 만들면 서로 다른 세계가 된다.

`IsSimulatedHere`가 왜 `IsMine`과 다른지가 오늘의 함정이다 — 씬에 놓인 젤리는 주인이 없어서(`OwnerId == 0`) `IsMine`이 어디서도 참이 아니다.

### 과제

**(A)** `NetConfig.TRANSFORM_SEND_RATE`를 20 → 2로 낮추고 클라 화면을 본다. 그 다음 `NetTransform`의 보간을 꺼본다. **둘의 차이**를 설명할 수 있어야 한다.

**(B)** 스폰 메시지에 필드를 하나 더 싣는다 (예: 회전값). `NetWorld.HostSpawn`과 `SpawnLocal` 양쪽을 고쳐야 하고, **한쪽만 고치면 그 뒤의 모든 메시지가 깨진다.** 왜 그런지가 1일차 내용과 이어진다.

**(C)** `NetSim`을 켜서 지연 200ms·손실 5%를 준다. 어떤 기능이 먼저 이상해지는가?

---

## 3일차 — 판이 흘러가는 순서

**읽을 것** `Net/Flow/` (2,553줄 — 제일 큼)

1. `LanDiscovery.cs` (232) — UDP 브로드캐스트로 방 찾기. TCP와 대비해서 볼 것
2. `LanLobby.cs` (655) — 방 만들기/참가
3. `LanSceneFlow.cs` (97) — **씬 전환의 단일 창구.** 짧지만 중요
4. `LanGameFlow.cs` (690) — 단계(Phase) 관리, 카운트다운, 종료 판정
5. `LanFlowHud.cs` (144) / `LanStandings.cs` (140) — 위에서 갈라져 나온 것들
6. `LanScoreboard.cs` (98) — 순위 수집

### 붙잡아야 할 개념

**`GamePhase`가 게임 전체의 신호등이다.** 봇 정지, 입력 잠금, 타일 붕괴, 흡수 판정이 전부 이걸 본다. `LanGameFlow.IsFrozen` / `IsPlaying(mode)` / `IsMode(mode)` 세 개가 어디서 쓰이는지 grep으로 훑어볼 것.

`SceneReady` 핸드셰이크를 이해할 것 — 접속은 Main 씬에서 일어나는데 스폰은 게임 씬에서 해야 한다. 그 시차를 어떻게 메우는가?

### 과제

**(A)** 승리 조건을 바꾼다. 흡수 모드에서 "제한 시간"이 아니라 "누가 크기 5에 먼저 도달"로. `CheckEndCondition` 한 곳만 고치면 되는지 확인.

**(B)** 카운트다운을 3초 → 10초로 늘리고, **그 동안 무엇이 멈추고 무엇이 안 멈추는지** 목록을 만든다. 안 멈추는 게 있으면 그게 버그다.

---

## 4일차 — 규칙 (모드)

**읽을 것** `Net/Modes/` (1,381줄)

1. `NetGameMode.cs` (121) — **먼저 읽을 것.** 두 모드의 공통 골격
2. `NetEntity.cs` (136) — 엔티티 질문 모음 (젤리인가? 탈락했나? 크기는?)
3. `HostVerdict.cs` (78) — 호스트 검증 6단계
4. `AbsorbMode.cs` (674) / `PushMode.cs` (372)

### 붙잡아야 할 개념

`HostVerdict.Judge`의 6단계를 **순서대로 외울 것**. 호스트인가 → 판이 도는가 → 둘 다 존재하는가 → 요청자가 그 캐릭터의 주인인가 → 같은 편이 아닌가 → 둘 다 살아있는가.

이 중 하나라도 빠지면 치팅이 뚫린다. 특히 **네 번째**(소유권)가 왜 필요한지 생각해볼 것 — 없으면 남의 캐릭터로 공격 요청을 보낼 수 있다.

### 과제

**(A) 세 번째 모드의 뼈대를 만든다.** `TagMode : NetGameMode<TagMode>` — 술래잡기. 실제로 동작할 필요는 없고, **`NetGameMode`를 상속했을 때 무엇을 자동으로 얻고 무엇을 직접 채워야 하는지**만 확인하면 된다. 이게 4일차의 핵심이다.

**(B)** `HostVerdict.Judge`에서 소유권 검사를 지우고, 클라에서 남의 netId로 공격 요청을 보내본다. 무슨 일이 일어나는가?

---

## 5일차 — AI

**읽을 것** `AI/` (1,561) + `AI/FSM/` (686) + `EnemyAI/` (506)

1. `AI/FSM/AIBaseState.cs` — 20줄. 상태 기계의 틀
2. `AI/AIPlayerMovement.cs` (1,383) — **전부 읽지 말 것.** `ChangeState`, `EvaluateAndTransition`, `Update` 세 곳만
3. `AI/FSM/` 상태 4개 (Wander / Chase / Flee / PushSurvive)
4. `AI/AIDetector.cs` — 탐지 로직
5. `EnemyAI/JellyAgentAI.cs` (169) → `WanderingAI.cs` / `AIWaypointPatrol.cs`

### 붙잡아야 할 개념

**봇은 호스트에서만 생각하고, 결과만 방송된다.** `LanBotSync`가 크기·색을 절대값으로 보내는 이유 — 클라는 봇이 무슨 젤리를 먹었는지 모르니 재현할 수 없다. 사람 플레이어는 반대로 "무슨 일이 있었다"(GrowEvent)를 보낸다. **왜 다른가?**

`JellyAgentAI`는 상속을 왜 썼는지의 좋은 예다. 하위 클래스가 채우는 건 `OnBecameDriver`(첫 목적지)와 `DriveUpdate`(매 프레임) 둘뿐이다.

### 과제

**(A)** 상태를 하나 추가한다. `AIIdleState` — 5초간 제자리에서 두리번거림. `EvaluateAndTransition`에 진입 조건을 넣는다.

**(B)** `AIDetector`의 탐지 반경을 절반으로 줄이고 봇 행동을 관찰. 어느 상태가 제일 많이 나오는가?

---

## 6일차 — 게임플레이 오브젝트

**읽을 것** `Player/` (902) + `Player/PlayerFSM/` (651) + `Absorbing/` (605) + `Map/` (2,196)

1. `Player/PlayerFSM/PlayerMovement.cs` + 상태 6개 — 5일차 AI FSM과 **같은 패턴**이라 빨리 읽힌다
2. `Absorbing/JellyColliderAbsorb.cs` — 흡수 연출. 최근에 고친 곳이라 익숙할 것
3. `Player/PlayerBridge.cs` / `BotBridge.cs` — 이벤트 배선
4. `Map/TileCollapseManager.cs` (864) — 링 붕괴. **`GetSyncedElapsed`부터**
5. `Map/FallingTile.cs` (419) / `ChocolateFluid.cs`

### 붙잡아야 할 개념

**흡수 한 번에 몇 개의 시스템이 관여하는가.** 트리거 감지 → 연출 시작 → 호스트에 요청 → 검증 → 승인 방송 → 성장·색·점수 → 젤리 파괴. 이 사슬을 **종이에 그릴 것**. 각 단계가 어느 파일인지 적는다.

### 과제

**(A)** 흡수 연출 값(`absorbSpeed`, `endScaleRatio`, 이징 `t*t`)을 바꿔가며 느낌 비교. `t*t*t`로 바꾸면?

**(B)** 발판이 무너지는 순서를 바꾼다 (`TileCollapseManager`). 바깥 링부터가 아니라 안쪽부터.

**(C)** 새 지형 위험 요소를 하나 추가 — 밟으면 느려지는 타일.

---

## 7일차 — 이어 붙이기, 그리고 혼자 만들기

### 전반 (1시간) — 한 판을 끝까지 추적

로그를 켜고 **한 판을 처음부터 끝까지** 따라간다. 종이에 순서대로 적는다:

```
방 만들기 → 발견 → 접속 → Welcome → 씬 로드 → SceneReady
→ 스폰 → 카운트다운 → Playing → (젤리 흡수 / 배트 히트 / 타일 붕괴)
→ 종료 판정 → 순위 방송 → 결과 씬
```

각 화살표에서 **어떤 메시지가 오가는지** 적을 것. 못 적는 화살표가 있으면 그 날 내용을 다시 본다.

### 후반 (1~2시간) — 혼자 기능 하나 만들기

아무 도움 없이 처음부터 끝까지. 예시:

- **관전자 채팅** — 탈락한 사람끼리만 보이는 채팅
- **부활 아이템** — 맵에 떨어지고, 먹으면 한 번 부활
- **팀전 뼈대** — 2팀으로 나누고 같은 팀은 서로 흡수 불가

어느 걸 골라도 1~6일차 내용을 **전부** 써야 한다. 이걸 만들 수 있으면 계획은 성공한 것이다.

---

## 참고: 이 코드가 왜 이렇게 생겼는지

읽다가 "이건 왜 이렇게 해놨지?" 싶은 부분은 대부분 **Photon에서 소켓으로 옮기다 생긴 흉터**다. 대표적인 것들:

| 코드 | 사연 |
|---|---|
| `NetIdentity.IsSimulatedHere` | `IsMine`만 봤더니 씬에 놓인 젤리가 전부 얼어붙었다 |
| `HostVerdict` | 흡수·밀치기가 같은 검증을 각자 적어둬서 한쪽만 고쳐지곤 했다 |
| `LanBotSync`의 절대값 전송 | 클라는 봇이 뭘 먹었는지 몰라서 재현할 수 없다 |
| `FramedConnection`의 `SelectRead` | `TcpClient.Connected`는 상대가 조용히 끊은 걸 못 잡는다 |
| `NetSpawnPool`의 `spawnPosition` | `NavMeshAgent`는 켜지는 순간의 자리에서 NavMesh를 찾는다 |
| `TileCollapseManager`의 `standGap` | 점프 중에도 발판이 밟힌 걸로 판정됐다 |

`REVIEW_NOTES.md`에 구조 분석이 더 있다.

---

## 진행 체크

- [ ] 1일차 — Transport
- [ ] 2일차 — Replication
- [ ] 3일차 — Flow
- [ ] 4일차 — Modes
- [ ] 5일차 — AI
- [ ] 6일차 — 게임플레이 오브젝트
- [ ] 7일차 — 통합 + 직접 만들기
