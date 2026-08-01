# LAN 소켓 마이그레이션 — 0단계 분석 결과

> 목표: Photon PUN2 → **같은 LAN 안에서 클라이언트 한 명이 서버+호스트를 겸하는 호스트 서버 방식**(C# TCP 소켓)
> 이 문서는 **조사·설계 결과만** 담는다(코드 수정 없음). 기준: IO 브랜치 현재 코드.

---

## 진행 현황 (2026-08-01 기준, `IO-lan_socket` 브랜치)

| 단계 | 상태 | 결과물 |
|---|---|---|
| 0 — 분석 | ✅ | 이 문서 |
| 1 — 소켓 기초 | ✅ | `SocketPractice/` (에코·프레이밍·다중 클라·부하 테스트) |
| 2 — Unity 네트워크 계층 | ✅ | `Assets/Scripts/Net/` 프레이밍·폴링·호스트/클라 |
| 3 — 오브젝트 복제·위치 동기화 | ✅ | `NetIdentity` `NetWorld` `NetTransform` (보간 3모드) |
| 4 — 흡수 모드 | 🟡 **핵심 루프만** | `AbsorbMode` `NetScale` — 4-1·4-2 완료, **4-3(색·점수)·4-4(게임 흐름) 미완** |
| 5 — 밀치기 모드 | 🟡 **판정·넉백만** | `PushMode` `NetKnockback` — 5-1·5-2 완료, **5-3(타일)·5-4(종료) 미완** |
| 6 — 안정화·실기기 | ⬜ | 6-2 UDP 자동 탐색 / 6-3 실기기 2대 미완 |
| 7 — 최적화 | ⬜ | 아래 추가 항목 참고 |

**측정된 성능(1단계):** 8클라 / 20Hz / 10초 → 호스트 출력 1287 msg/s, 46.5 KB/s(앱 기준),
RTT min·avg·p95 = 0.04 / 0.13 / 0.22 ms, 손실 0. **10명 LAN은 여유롭게 처리 가능.**

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
7. **스레드 문제** — 스레드 방식을 쓸 경우 `IsBackground=true` + 종료 처리 필수(안 하면 에디터가 굳고 포트가 점유됨). → **Unity에서는 폴링 방식을 채택해 이 문제를 회피한다(8장 2단계 참조).**
8. **AP 격리** — 학교·카페 와이파이는 기기간 통신이 막히는 경우가 많다. 집 공유기나 휴대폰 핫스팟에서 테스트.

---

## 8. 진행 계획

> **브랜치 전략**
> `IO` = 공통 베이스(에셋·게임로직·공통 버그수정, 이 문서)
> `IO-Photon` = 기존 Photon 버전 유지·개선
> `IO-lan_socket` = 소켓 구현 진행
> 공통 수정은 `IO`에 하고 두 형제 브랜치로 **병합해 전파**한다(형제끼리 병합 금지).

---

### ✅ 0단계 — 분석·설계 (완료)

Photon 의존부 전수조사, 대체 매핑, 메시지 목록 확정. → 이 문서 1~7장.

### ✅ 1단계 — 콘솔 소켓 프로토타입 (완료)

`SocketPractice/` (IO-lan_socket 브랜치). TCP 에코 → 길이 프리픽스 프레이밍 →
다중 접속 + 호스트 판정 → 부하 테스트까지 구현·검증.

**검증된 성능** (로컬 127.0.0.1, 8명 · 20Hz · 10초 · 24바이트 패킷):

| 지표 | 측정값 | 판정 |
|---|---|---|
| Hz 달성률 | 100% (실제 20.0Hz) | 주기 제어 정확 |
| 호스트 송신량 | 1,287 msg/s | 여유 |
| 호스트 업로드 | 46.5 KB/s | 와이파이에 부담 없음 |
| RTT 평균 / 상위95% | 0.13 / 0.22 ms | 매우 안정 |
| 응답 손실 | 0개 | — |
| 브로드캐스트 증폭 | ×8.0 | 이론값과 일치 |

> **결론: 10명 규모 LAN 게임에 소켓 계층 성능은 충분하다.**
> 따라서 **최적화는 뒤로 미루고 기능 구현에 집중**한다.

---

### 2단계 — Unity 통합 뼈대

목표: **게임 로직 없이 "연결만 되는" 상태.**

| 항목 | 내용 |
|---|---|
| 2-1 | `Assets/Scripts/Net/` 생성, 소켓 코드 이식 |
| 2-2 | **폴링 구조** — `Update()`에서 `listener.Pending()` / `conn.Available` 확인 |
| 2-3 | **바이너리 프로토콜** — `[길이4][타입1][데이터…]`, `BinaryWriter/Reader` |
| 2-4 | **누적 버퍼** — 부분 수신을 프레임 간에 이어붙이기 |
| 2-5 | 테스트 씬 — 호스트/참가 버튼, IP 입력칸, 접속자 목록 |

**왜 스레드가 아니라 폴링인가**
Unity는 `Update()`라는 주기 루프가 이미 있어 블로킹을 피할 수 있다. 모든 처리가
메인 스레드에서 일어나므로 `lock`·경쟁 상태가 사라지고 `transform`·`Instantiate`를
바로 호출할 수 있다. 대가는 **부분 수신을 직접 모아야 한다는 것**(2-4).

**왜 문자열이 아니라 바이너리인가**
문자열은 메시지마다 `string`을 새로 만들어 **GC 압박**을 준다. 초당 1,300개 규모에서는
무시할 수 없다(1단계 측정에서 RTT 최대 4.1ms가 튄 구간이 GC 의심). `byte[]` 재사용이
성능·GC 양쪽에 유리하고, 타입을 숫자 1바이트로 구분하면 파싱도 빨라진다.

> **검증 기준:** 두 인스턴스(빌드 + 에디터)가 접속되고 핑 메시지가 왕복하며,
> 접속·퇴장이 목록에 반영된다.

---

### 3단계 — Photon 기능 대체 (인프라)

| 항목 | 만들 것 | 대체 대상 |
|---|---|---|
| 3-1 | `netId` 발급 + `NetworkIdentity` 컴포넌트 | `PhotonView` |
| 3-2 | 스폰 복제(`SpawnEntity`/`DespawnEntity`) + prefabId 레지스트리 | `Instantiate` / `InstantiateRoomObject` |
| 3-3 | 위치 동기화 + **보간** | `PhotonTransformView` |
| 3-4 | 상태 스냅샷 + **늦은 입장 스냅샷** | `CustomProperties` |
| 3-5 | `IsHost` / `IsMine` 판정 헬퍼 | `IsMasterClient` / `IsMine` |

⚠️ **3-3의 보간을 빠뜨리면 안 된다.** 로컬 RTT는 0.13ms라 보간 없이도 멀쩡해 보이지만,
실제 와이파이(5~30ms)에서는 원격 캐릭터가 뚝뚝 끊긴다. **로컬 테스트로는 절대
발견되지 않는 항목**이다.

> **검증 기준:** 두 인스턴스에서 서로의 캐릭터가 부드럽게 움직이고,
> 중간에 접속한 클라도 기존 오브젝트를 모두 본다.

---

### 4단계 — 흡수 모드 이식

**한 모드만 끝까지 완성한다. 밀치기는 그다음.**

| 항목 | 내용 |
|---|---|
| 4-1 | 젤리 스폰 — 호스트 권위 | ✅ `AbsorbMode.HostSpawnTick` |
| 4-2 | 흡수 요청 → 호스트 판정 → 결과 브로드캐스트 | ✅ `AbsorbMode.ResolveEat` |
| 4-3 | 성장·점수·색 반영 | 🟡 **성장만 완료**(`NetScale`). 점수·색 미완 |
| 4-4 | 게임 시작/종료 흐름, 결과 씬 전환 | ⬜ 미완 |

> **검증 기준:** 흡수 모드 한 판을 처음부터 결과 화면까지 완주.

#### 4-2 구현 시 바뀐 점 — `_claimedJellies`가 필요 없어졌다

계획서에는 Photon판의 선착 가드 집합을 "그대로 재사용"한다고 적었으나, **소켓판에서는 불필요했다.**

`PhotonNetwork.Destroy`는 즉시 반영되지 않아 별도 집합으로 선점을 표시해야 했지만,
`NetWorld.HostDespawn`은 **같은 프레임에 딕셔너리에서 즉시 제거**된다.
따라서 판정 순간 젤리를 먼저 제거하면, 뒤이은 요청은 `Find()`에서 `null`을 받고 자동 탈락한다.

```
검증 → prefabId 저장 → HostDespawn(젤리)  ← 이 시점부터 후발 요청 자동 탈락
     → 크기 증가(StateUpdate 방송) → EatJellyConfirm 방송
```

**단일 스레드 + 동기 제거**라는 구조가 경쟁 상태를 원천 차단한 사례.

---

### 5단계 — 밀치기 모드 확장

| 항목 | 내용 |
|---|---|
| 5-1 | 배트/대쉬 히트 판정 (호스트 검증) | ✅ `PushMode.ResolveBatHit` |
| 5-2 | 넉백 — **피격자 소유자에게만** 전송 | ✅ `PushMode.SendKnockback` + `NetKnockback` |
| 5-3 | 타일 마모·붕괴 + `GameStartTime` 기반 결정론 재생 | ⬜ 미완 |
| 5-4 | 생존자 판정 · 종료 처리 | ⬜ 미완 |

#### 호스트가 재검증하는 항목 (5-1)

클라의 "때렸다"는 주장을 그대로 믿지 않는다. Photon판의 U2/N4·CBT-1·CBT-2 가드를 그대로 옮겼다.

| 검사 | 막는 것 |
|---|---|
| 소유권 | 남의 캐릭터를 내세운 요청 |
| 같은 편 / 대상 종류 | 자기 자신·아군·젤리 타격 |
| **쿨다운(호스트 기준)** | 요청 연사로 무한 성장 |
| **거리 재검증** | 맵 반대편 상대를 밀어내기 |

클라 쪽 쿨다운(`_localCooldown`)은 UX용일 뿐이며, **권위는 호스트의 `_lastHitTime`에 있다.**

#### 5-2 설계 근거 — 왜 전원 방송이 아닌가

피격자는 이미 자기 위치를 20Hz로 보내고 있으므로, **소유자만 밀려나면 결과가
`TransformUpdate`를 타고 자동 전파**된다. 전원에게 넉백을 보내면

1. 각자 자기 화면에서 피격자를 밀어냄(계산 중복)
2. 그 위에 소유자가 보낸 진짜 위치가 덮어씀(충돌 → 떨림)

기술명세서의 "RpcTarget 선택 근거"(`victimPV.Owner` 사용)와 동일한 판단이다.

---

### 6단계 — 안정화 · 실기기 검증

| 항목 | 내용 |
|---|---|
| 6-1 | **호스트 이탈 처리** — 자동 교체가 없으므로 "게임 종료"부터 |
| 6-2 | **UDP 브로드캐스트 자동 탐색** — IP 입력 없이 방 목록 표시 |
| 6-3 | **실기기 2대 LAN 테스트** — 방화벽·실제 지연 확인 |
| 6-4 | 지연 시뮬레이션(Clumsy 등)으로 보간 품질 검증 |

> 로컬(127.0.0.1)에서는 **방화벽과 실제 지연이 절대 드러나지 않는다.**
> 완성 직전 한 번은 실기기 2대 검증이 필요하다.

---

### 7단계 (선택) — 최적화

- 위치만 UDP로 분리 (신뢰성 채널 / 실시간 채널 분리)
- 전송률 조절, 관심 영역(가까운 대상에게만 전송)
- 델타 압축(변한 값만 전송)

**3단계 이후 추가된 항목**

- **위치 메시지에 tick(송신 시각) 싣기.** 현재 `NetTransform`은 스냅샷 시각으로 *도착 시각*을
  쓰므로, 지터가 그대로 재생 속도의 흔들림이 된다. 송신 틱을 기준으로 바꾸면 간격이 정확히
  고정되어 **스냅샷 보간이 정확하면서 동시에 부드러워진다.** 대가는 메시지당 2~4바이트와
  양쪽 시계 오프셋 추정 로직.
- **적응형 `InterpDelay`.** 지터를 실측해 재생 지연을 자동 조절(안정적이면 줄이고, 불안정하면 늘림).
  현재는 100ms 고정. 하한은 `전송 간격 + 지터`이며, TCP에서는 재전송(HOL blocking) 때문에
  `+ RTT`까지 필요할 수 있다.
- **클라 예측(prediction).** 현재 흡수는 요청→응답을 기다리므로 왕복 지연만큼 반응이 늦다.
  로컬에서는 안 보이지만 실제 무선에서는 체감된다.

> 1단계 측정 결과 **10명 규모에서는 불필요.** 인원을 크게 늘릴 때만 고려한다.
