# LAN 소켓 마이그레이션 — 0단계 분석 결과

> 목표: Photon PUN2 → **같은 LAN 안에서 클라이언트 한 명이 서버+호스트를 겸하는 호스트 서버 방식**(C# TCP 소켓)
> 이 문서는 **조사·설계 결과만** 담는다(코드 수정 없음). 기준: IO 브랜치 현재 코드.

---

## 1. 규모 요약

| 항목 | 수량 |
|---|---|
| Photon 의존 스크립트 | **29개 파일** |
| `[PunRPC]` 메서드 | **약 26개** |
| `PhotonView` 붙은 프리팹 | **29개** (+ 씬 2개) |
| `PhotonTransformView` 프리팹 | 7개 |
| `PhotonAnimatorView` 프리팹 | 3개 |
| `IsMasterClient` 사용 | 44곳 |
| `IsMine` 사용 | 35곳 |
| `CustomProperties` 사용 | 35곳 |
| `RaiseEvent` 사용 | 9곳 (이벤트 코드 4종) |

> **가장 놓치기 쉬운 지점: 프리팹/씬.** 코드만 바꿔도 프리팹 29개와 씬 2개에 붙은 `PhotonView`·`PhotonTransformView`·`PhotonAnimatorView` 컴포넌트를 전부 걷어내고 자체 컴포넌트로 갈아야 한다. 수작업이 많으므로 에디터 스크립트로 일괄 처리하는 편이 낫다.

---

## 2. 파일별 영향도 (작업 등급)

### 등급 A — 사실상 전면 재작성 (네트워크 코어)

| 파일 | Photon 참조 | 담당 역할 |
|---|---|---|
| `Network/NetworkManager.cs` | 62 | 접속·룸 매칭·카운트다운·스폰 → **연결 관리로 전면 교체** |
| `Network/NetworkPlayerSync.cs` | 50 | 위치/색/스케일 동기화·흡수·밀치기 RPC |
| `Network/GameModeManager.cs` | 39 | 게임 시작/종료·타일 전파·결과 동기화 |
| `AI/AIPlayerMovement.cs` | 26 | 봇 넉백·탈락·애니 RPC (이동 로직 자체는 유지) |
| `Network/NetworkJellyManager.cs` | 19 | 젤리 스폰·흡수 판정 |
| `Network/AIPlayerSync.cs` | 19 | 봇 상태(스케일·점수·색) 룸 프로퍼티 |

### 등급 B — 부분 수정 (Photon 호출부만 교체)

`UI/Result/GameResultManager.cs`(10) · `UI/LoadingSceneController.cs`(9) · `Network/ScoreboardSnapshot.cs`(6) · `Network/LobbyController.cs`(5) · `Map/FallingTile.cs`(5) · `Network/NetworkNavMeshHelper.cs`(4) · `Map/Milk.cs`(4) · `Absorbing/JellyColliderAbsorb.cs`(4) · `UI/UIManagement/UIManager.cs`(3) · `Map/TileCollapseManager.cs`(3) · `Map/PuddingWiggle.cs`(3) · `Map/ChocolateFluid.cs`(3) · `EnemyAI/WanderingAI.cs`(3) · `EnemyAI/AIWaypointPatrol.cs`(3)

### 등급 C — 한두 줄 (IsMine/IsMasterClient 가드 정도)

`UI/MinimapArrowManager.cs` · `UI/InGameUI/JellySpawnMachine.cs` · `Network/AutoConnectForTest.cs` · `Map/RandomJellySpawner.cs` · `UI/OffScreenPlayerIndicator.cs` · `Player/PlayerFSM/PlayerJumpState.cs` · `PlayerDashState.cs` · `PlayerAttackState.cs` · `AI/AIDetector.cs`

---

## 3. Photon 기능 → 직접 구현 매핑

| Photon 기능 | 현재 사용처 | 소켓 버전에서 만들 것 |
|---|---|---|
| 접속·룸 매칭 | `NetworkManager` | `TcpListener`(호스트) / `TcpClient`(참가) + 세션 목록 |
| `PhotonView.ViewID` | 32곳 | 자체 `netId`(호스트가 발급) + `NetworkIdentity` 컴포넌트 |
| `PhotonNetwork.Instantiate` | 8곳 | `SpawnEntity` 메시지 + prefabId 레지스트리 |
| `InstantiateRoomObject` | 5곳 (봇·젤리) | 동일 + **소유자 = 호스트**로 표시 |
| `PhotonNetwork.Destroy` | 6곳 | `DespawnEntity` 메시지 (호스트만 발행) |
| `[PunRPC]` + `RpcTarget` | 26개 | 메시지 타입 + 수신자 라우팅(호스트만/전체/특정) |
| `CustomProperties` | 35곳 | `StateUpdate` 메시지 + **늦은 입장용 전체 스냅샷** |
| `IPunObservable`(색) | 9곳 | 색은 변경 시에만 전송(개선 겸) |
| `PhotonTransformView` | 프리팹 7개 | 위치 주기 전송 + **원격 보간 직접 구현** |
| `PhotonAnimatorView` | 프리팹 3개 | `AnimTrigger` 메시지 |
| `IsMasterClient` | 44곳 | `Net.IsHost` 플래그 |
| `IsMine` | 35곳 | `identity.OwnerId == Net.MyId` |
| `ServerTimestamp` | 5곳 | 호스트 기준 시각(`GameStartTime` 대체) |
| `PhotonNetwork.LoadLevel` | 6곳 | `SceneLoad` 메시지 |
| `RaiseEvent`(로비) | 9곳 | 접속 전 단계도 소켓으로 통일 |
| 마스터 자동 교체 | — | **없음** → 호스트 이탈 시 게임 종료 |

### 대체 대상 프로퍼티 키
`GM`(모드) · `GameStartTime` · `ResultSyncToken` · `PushSurvivorActors` · `Eliminated` · `Scale` · `Score` · `Color_R/G/B` · `Bot{id}_*`

### 대체 대상 RaiseEvent 코드
`EVENT_COUNTDOWN=11` · `EVENT_GAME_START=12` · `EVENT_PLAYER_COUNT=13` · `EVENT_BEGIN_CURTAIN=14`

---

## 4. 동기화 대상 — 빈도·신뢰성 요구

| 대상 | 빈도 | 신뢰성 | 방향 | 비고 |
|---|---|---|---|---|
| 플레이어·봇 위치/회전 | 매 틱(10~20Hz) | 유실 허용 | 소유자→호스트→전체 | **나중에 UDP 후보** |
| 애니메이션 트리거 | 사건 | 필요 | 소유자→전체 | 점프·대쉬·공격 |
| 스케일·점수 | 낮음(변할 때) | 필요 | 소유자→전체 | 판정 기준값 |
| 색상 | 거의 안 변함 | 필요 | 소유자→전체 | 스트림 낭비 개선 |
| 흡수/밀치기 판정 | 사건 | **필수** | 클라→호스트→전체 | 요청·검증·결과 |
| 넉백 적용 | 사건 | **필수** | 호스트→해당 소유자 | 위치는 스트림이 전파 |
| 타일 마모/붕괴 | 사건 | **필수** | 호스트→전체 | |
| 게임 시작/종료 | 사건 | **필수** | 호스트→전체 | 시작 시각 포함 |
| 늦은 입장 스냅샷 | 입장 시 1회 | **필수** | 호스트→신규 | Photon이 공짜로 해줬던 것 |

> **시작 전략: 전부 TCP 한 채널.** LAN에서는 유실이 드물어 충분하다. 게임이 돌아간 뒤 위치만 UDP로 분리.

---

## 5. 메시지 목록 초안 (프로토콜)

형식: `[길이 4바이트][타입 1바이트][페이로드...]`

**연결·세션**
- `JoinRequest(nickname)` — 클라→호스트
- `Welcome(myId, isHost, gameMode)` — 호스트→클라
- `PlayerJoined / PlayerLeft(playerId)`
- `FullSnapshot(엔티티·상태 전체)` — 늦은 입장용

**스폰·상태**
- `SpawnEntity(netId, prefabId, pos, ownerId)`
- `DespawnEntity(netId)`
- `TransformUpdate(netId, x, y, z, yaw)` — 고빈도
- `StateUpdate(netId, scale, score, r, g, b)`
- `AnimTrigger(netId, animId)`

**흡수 모드**
- `EatJellyRequest(jellyNetId, eaterNetId)` → `EatConfirm(eaterNetId, colorType)`
- `AbsorbRequest(targetNetId)` → `AbsorbResult(victimNetId, absorberNetId)`

**밀치기 모드**
- `BatHitRequest / DashHitRequest(targetNetId)`
- `ApplyKnockback(netId, dirX, dirZ, force)`
- `GrowReward(netId, amount)`
- `TileDarken(x, z)` / `TileCollapse(x, z)`

**게임 흐름**
- `PlayerCount(n)` / `Countdown(n)` / `SceneLoad(sceneName)`
- `GameStart(startTimeMs)`
- `Eliminated(netId)` / `Respawn(netId)`
- `GameEnd(survivorIds[])`

---

## 6. 재사용 가능한 자산 (다시 안 짜도 되는 것)

전송 방식과 무관하므로 **판정 로직은 그대로 가져간다.**

- 흡수 선착 판정: `_claimedJellies` 선점 가드
- 중복 방지(멱등): `_isAbsorbed`, `_absorbedBotIds`, `_tiles[x,z]==null`
- 결정론 재생: `GameStartTime` 기반 링 붕괴·초콜릿 흐름
- 권위 규율: "요청 → 호스트 검증 → 브로드캐스트" 패턴 전체
- 게임플레이 전반: AI FSM, 카메라, UI, 색 시스템, 물리

---

## 7. 리스크 · 주의사항

1. **프리팹/씬 작업량** — 프리팹 29개·씬 2개의 Photon 컴포넌트 제거 및 자체 컴포넌트 부착. 에디터 스크립트 일괄 처리 권장.
2. **`MonoBehaviourPunCallbacks` 상속 9곳** — 자체 베이스 클래스 또는 일반 `MonoBehaviour`로 전환 필요.
3. **경로 기반 스폰** — `PhotonNetwork.Instantiate("Prefabs/...")`를 쓰므로 자체 **prefabId ↔ 경로 레지스트리**가 필요.
4. **보간 부재 시 뚝뚝 끊김** — `PhotonTransformView`가 해줬던 보간을 직접 구현해야 체감 품질이 유지된다.
5. **늦은 입장 스냅샷 누락** — 구현 안 하면 중간 입장자가 빈 월드를 본다.
6. **호스트 이탈** — 자동 교체가 없으므로 "게임 종료" 처리부터 시작.
7. **스레드 정리** — 소켓 스레드를 `IsBackground=true` + 종료 처리. 안 하면 에디터가 굳고 포트가 점유된다.
8. **AP 격리** — 학교·카페 와이파이는 기기간 통신이 막히는 경우가 많다. 집 공유기나 휴대폰 핫스팟에서 테스트.

---

## 8. 다음 단계 (B단계 연결)

1. **1단계** — 콘솔 앱으로 TCP 에코 → 길이 프리픽스 프레이밍 → 다중 클라 접속 연습
2. **2단계** — Unity 통합 뼈대(수신 스레드 → 메인 스레드 큐), 호스트/참가 UI, 수동 IP 입력
3. **3단계** — netId 발급·스폰 복제·위치 보간·상태 스냅샷 구현
4. **4단계** — **흡수 모드만** 먼저 완성 → 밀치기 모드 확장
5. **5단계** — 2대 → N대 테스트, 호스트 이탈 처리

> 권장: `lan-socket` 브랜치에서 진행. 공통 수정은 `IO`에 하고 병합으로 전파.
