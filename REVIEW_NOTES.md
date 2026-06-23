# REVIEW_NOTES.md — 게임 시퀀스 코드 구조 분석

> ⚠️ 이전 세션의 REVIEW_NOTES.md(17개 항목)는 원격 컨테이너가 매번 새로 클론되며
> 커밋되지 않아 유실되었습니다. 이 파일은 2026-06-04 루틴에서 새로 작성한 것입니다.
> (앞으로는 이 파일을 반드시 커밋/푸시해야 다음 세션에서 누적됩니다.)

## 2026-06-04 루틴 — 어제(06-03) "게임 모드 선택(Push/Absorb)" 추가에 대한 리뷰

리뷰 대상 시퀀스: 로비(LobbyController) → 매칭/카운트다운(NetworkManager) →
로딩(LoadingSceneController) → 인게임(GameModeManager) → 결과(GameResultManager)

어제 변경 핵심(커밋 303059e):
- 시작 버튼 클릭 → 모드 선택 패널(Push/Absorb) → 각 버튼이 `SelectedGameMode` 지정 후 매칭 시작
- `gameSceneName` → `gameAbsorbModeSceneName` / `gamePushModeSceneName` 두 씬으로 분기
- 카운트다운 종료 시 `LoadingSceneController.NextSceneName`을 모드에 맞춰 지정

---

### [F1] (네트워크·버그 / 중) 마스터 교체 시 잘못된 게임 씬 로드 가능
- 위치: `LoadingSceneController.cs:45-47`, `OnMasterClientSwitched` / `NetworkManager.cs:311`
- 내용: 마스터만 `NextSceneName`을 Push/Absorb 씬으로 지정한다. 비마스터는 `NextSceneName==null`
  → 기본값 `gameAbsorbModeSceneName`으로 `_targetScene`이 캡처됨.
  평소엔 비마스터가 LoadLevel을 호출하지 않으므로(게임씬 전환은 마스터만) 무해하지만,
  **로딩 중 마스터가 끊겨 `OnMasterClientSwitched`로 새 마스터가 된 비마스터는
  `_targetScene`(=Absorb 기본값)으로 LoadLevel** → Push 게임인데 Absorb 씬 로드.
- 제안: 기본값을 하드코딩 absorb 대신 `GameState.CurrentGameMode`(룸 권위값)에서 파생.

### [F2] (아키텍처 / 중) 모드 출처 이원화 — `SelectedGameMode` vs `GameState.CurrentGameMode`
- 위치: `NetworkManager.cs:311` (씬 결정에 로컬 정적 `SelectedGameMode` 사용)
  vs `OnJoinedRoom`이 룸 프로퍼티 `GM`을 권위 출처로 `GameState.CurrentGameMode`에 반영(234-242).
- 내용: `SelectedGameMode`는 "내가 누르려던 모드"(로컬 의도), `GameState.CurrentGameMode`는
  "실제 입장한 방의 모드"(룸 권위). `expectedCustomRoomProperties` 덕에 대개 일치하지만,
  씬 결정처럼 **모든 클라가 동일해야 하는 값은 룸 권위 출처 하나로 통일**하는 것이 안전.
- 제안: 311행을 `GameState.CurrentGameMode == GameModeType.Absorb ? ...`로 변경. F1과 함께 해결됨.

### [F3] (버그·일관성 / 하) `nicknameMaxLength` 직렬화 필드 미사용
- 위치: `LobbyController.cs:16, 233`
- 내용: 인스펙터로 조정 가능한 `nicknameMaxLength=10` 필드를 만들어 놓고, 길이 검사는
  하드코딩 `if (playerNickname.Length > 10)`. 인스펙터에서 값을 바꿔도 동작이 안 바뀜.
- 제안: `> nicknameMaxLength`로 교체(의도된 인터페이스 사용).

### [F4] (안정성 / 하) `buttonSelectionPanel` null 가드 누락
- 위치: `LobbyController.cs:242`
- 내용: 같은 클래스의 다른 UI 참조(`warningText?.`, `matchingPanel != null` 등)는 모두
  null 안전 처리하는데 여기만 `buttonSelectionPanel.SetActive(true)` 직접 호출 →
  인스펙터 미할당 시 NRE로 시작 흐름 자체가 멈춤.
- 제안: `if (buttonSelectionPanel != null)` 또는 `?.SetActive(true)`.

### [F5] (UX·시퀀스 / 하) 모드 선택 패널에서 뒤로가기/취소 경로 없음
- 위치: `LobbyController.OnStartButtonClicked` → `buttonSelectionPanel` 노출 이후
- 내용: 모드 패널이 뜬 뒤 `startButton.interactable=false`로 고정. 닉네임을 다시 고치거나
  모드 선택을 취소할 방법이 없음(매칭을 시작해야만 진행). 학습/설계 관점 관찰 항목.
- 제안: 모드 패널에 "뒤로" 버튼 → 패널 닫고 `startButton.interactable=true` 복원.

---

## 2026-06-09 루틴 — 06-06~06-07 변경분 리뷰 (인디케이터/이펙트/NavMesh 복구)

마지막 루틴(06-04) 이후 06-06, 06-07에 들어온 미리뷰 변경을 분석.
대상 커밋군: 오프스크린 인디케이터 추가/수정, 젤리·AI흡수 SFX 분리, 젤리별 LevelUpFloater
풀 구조화, 무너진 타일 위 유령 NavMesh AI 복구, 리더보드 색 연동, Push 결과 버그 수정.

리뷰 파일: `OffScreenPlayerIndicator.cs`, `LevelUpFloater.cs` / `LevelUpFloaterPool.cs`,
`FallingTile.cs`, `AIPlayerMovement.cs`, `PlayerScaleController.cs`, `PlayerBridge.cs`

### [G1] (버그 / 중) ResetScale 후 "젤리로 성장"이 영구히 멈출 수 있음
- 위치: `PlayerScaleController.cs:157-168 (ResetScale)`, `47-59 (GrowByJelly/BatchedJellyGrow)`
- 원인: `GrowByJelly()`는 같은 프레임의 여러 젤리를 한 번에 묶으려고 `_jellyBatchCoroutine`에
  코루틴을 저장하고, 그 코루틴(`BatchedJellyGrow`)이 **첫 `yield return null` 이후** 자기 자신을
  `null`로 비운다. 그런데 `ResetScale()`은 `StopAllCoroutines()`로 이 코루틴을 중도에 죽이면서
  `_jellyBatchCoroutine` 필드는 비우지 않는다. → 리셋 시점에 배치 코루틴이 첫 yield에서 대기
  중이었다면, 필드에 **죽은 코루틴 핸들이 그대로 남는다.**
  이후 `GrowByJelly()`는 `if (_jellyBatchCoroutine == null)` 가드가 false가 되어
  **다시는 BatchedJellyGrow를 시작하지 않는다.** `_pendingScale`만 커지고 실제 스케일은 변화 X.
- 영향: 라운드 리셋/리스폰 후 젤리를 먹어도 안 커지는 잠복 버그(타이밍 의존이라 재현이 들쭉날쭉).
- 제안: `ResetScale()`에서 `StopAllCoroutines()` 직후 `_jellyBatchCoroutine = null;` 추가.
  (학습 포인트: 코루틴 핸들을 상태 플래그로 쓸 때는 *중단 경로 전부*에서 핸들을 초기화해야 한다.
  `StopAllCoroutines`는 코루틴 본문의 정리 코드를 실행하지 않고 즉시 끊는다.)

### [G2] (네트워크 / 중·확인필요) 봇 넉백이 전 클라에서 transform를 직접 이동
- 위치: `AIPlayerMovement.cs:863 (RpcTarget.All)`, `924-975 (RPC_ApplyKnockback/KnockbackRoutine)`
- 내용: **플레이어** 넉백은 `RPC_ApplyKnockback`을 `otherPlayer.photonView.Owner`에게만 보내
  소유자만 자기 위치를 움직인다(852-853). 반면 **봇** 넉백은 `RpcTarget.All`로 보내(863)
  모든 클라이언트가 `KnockbackRoutine`에서 `transform.position += ...`을 직접 실행한다.
  봇 위치는 마스터 권위로 네트워크 동기화(PhotonTransformView 등)되는데, 비마스터가 동시에
  로컬에서 좌표를 밀면 **수신 동기화 값과 충돌해 지터/되감김**이 생길 수 있다.
- 제안: 봇도 위치 권위자(마스터)만 KnockbackRoutine을 돌리고, 시각효과(애니/이펙트)만 All로 분리.
  먼저 봇 프리팹의 위치 동기화 컴포넌트 구성을 확인할 것(이게 PhotonTransformView면 충돌 확정).

### [G3] (성능·안정 / 중) FallingTile 디버그가 빌드에서도 동작 + 머티리얼 인스턴스 복제
- 위치: `FallingTile.cs:9 (drawOverlapGizmo=true 기본값)`, `184-191 (Debug.Log)`, `99-102/117-118`
- 내용: (a) `drawOverlapGizmo` 기본 true라서 타일이 붕괴할 때마다 `Debug.Log`가 출력된다.
  맵 전체가 무너지는 후반엔 수십~수백 회 로그 → 모바일/빌드에서 프레임 스파이크 + 로그 오버헤드.
  (b) `rend.material.color`(101,118행)는 첫 접근 시 공유 머티리얼을 **인스턴스 복제**한다.
  붕괴 타일마다 머티리얼 사본이 생겨 드로우콜 배칭이 깨지고 메모리가 늘어난다.
- 제안: (a) 기본값 false 또는 로그를 `#if UNITY_EDITOR`로 감싸기. (b) 색 변경은
  `MaterialPropertyBlock`으로 처리하면 인스턴스 복제 없이 배칭 유지.

### [G4] (성능 / 하) 인디케이터 색 조회가 봇 머티리얼을 인스턴스화
- 위치: `OffScreenPlayerIndicator.cs:220-226 (GetColor)`
- 내용: `botRenderer.material.HasProperty(...)`/`.GetColor(...)`의 `.material` 접근은 봇마다
  머티리얼 사본을 만든다(읽기만 하는데 복제됨). LateUpdate에서 매 봇 호출.
- 제안: 읽기 전용이므로 `sharedMaterial`로 조회. (학습: Unity에서 `.material`=인스턴스 생성,
  `.sharedMaterial`=공유 원본. 값을 *읽기*만 할 땐 항상 shared.)

### [G5] (아키텍처 / 하) 매직 스트링 산재
- 위치: 룸 프로퍼티 키 `"Eliminated"`(OffScreenPlayerIndicator:234, AIPlayerMovement 등),
  셰이더 `"_FresnelColor"`(:223), 애니 파라미터 `"IsMoving"/"Dash"/"Attack"`(AIPlayerMovement 다수)
- 내용: 같은 문자열이 여러 파일에 하드코딩 → 오타 시 무음 실패, 리네이밍 시 누락 위험.
- 제안: `static class NetKeys { public const string Eliminated = "Eliminated"; }` 식 상수 모음,
  애니 파라미터는 `Animator.StringToHash` 캐시. (학습: 문자열 키는 컴파일러가 안 잡아준다.)

### [G6] (아키텍처 / 하) "탈락" 판정 출처가 이원화
- 위치: `OffScreenPlayerIndicator.IsPlayerEliminated`(228-236)는 `IsAbsorbed` **또는**
  owner 룸 프로퍼티 `"Eliminated"`를 본다. 봇은 `IsEliminated`/`IsBeingAbsorbed` 별도 플래그.
- 내용: 같은 "이 엔티티가 게임에서 빠졌나?"를 곳마다 다른 조합으로 판단 → 한쪽만 갱신되면
  인디케이터/리더보드/결과창이 서로 다르게 보일 수 있음(F2와 같은 '권위 출처 단일화' 주제).
- 제안: `bool IsOutOfPlay`를 NetworkPlayerSync/AIPlayerMovement에 단일 헬퍼로 노출하고 모두 그걸 사용.

### [G7] (안정 / 하) NavCarve 오브젝트가 한 판 동안 무한 누적
- 위치: `FallingTile.CarveNavMesh:253-265`
- 내용: 타일이 무너질 때마다 루트에 `NavCarve_*` (영구 carving NavMeshObstacle) 생성.
  구멍 유지를 위한 의도된 설계지만, 한 판에서 수백 칸이 무너지면 그만큼 오브젝트/Obstacle가
  쌓여 NavMesh 재계산·메모리 부담. 씬 전환 전엔 정리되지 않음.
- 제안: 생성된 carve 오브젝트를 `TileCollapseManager`가 리스트로 들고 라운드 종료 시 일괄 정리.
  (지금 당장 버그는 아니므로 우선순위 낮음 — 관찰 항목.)

### [G8] (안정 / 하) FallingTile에서 DataManager.Instance 널 가드 없음
- 위치: `FallingTile.cs:172 (DataManager.Instance.objectLayerMask)`
- 내용: 다른 곳(AIPlayerMovement.DetectBatHit 등)은 `var dm = DataManager.Instance; if (dm==null) return;`로
  방어하는데 여기만 직접 접근. 씬 초기화/파괴 타이밍에 NRE 가능.
- 제안: 동일 패턴의 널 가드 추가.

---

## 2026-06-12 루틴 — 게임 시퀀스 심층 리뷰 (신규 커밋 없음 / 미리뷰 영역 분석)

06-09 이후 새 커밋이 없어, 아직 깊게 리뷰하지 않았던 시퀀스 핵심부를 분석.
리뷰 파일: `GameModeManager.cs`, `GameState.cs`, `GameResultManager.cs`,
`NetworkManager.cs`(매칭 흐름), `LoadingSceneController.cs`

### [H1] (버그 / 중) GameState.ResetValues()가 정적 이벤트 구독을 전부 끊어버림
- 위치: `GameState.cs:96-109 (ResetValues)`, 호출처 `GameModeManager.cs:167 (StartGameInternal)`
- 내용: 씬 UI(`LevelUI`/`ScoreUI`/`CurrentStatusUI`)는 **OnEnable(씬 활성화 시점)**에서
  `GameState.OnScaleChanged += ...`로 구독한다. 그런데 게임 시작 RPC(`RPC_StartGame` →
  `StartGameInternal` → `GameState.ResetValues()`)가 그 **뒤에** 실행되며
  `OnScaleChanged = null` 등으로 이벤트를 통째로 비운다. → 씬 로드 때 구독한 UI가
  게임 시작 순간 전부 **무음으로 구독 해제**되어 이후 스케일/점수 변화가 UI에 반영되지 않을 수 있다.
  (Unity 라이프사이클: 모든 OnEnable은 모든 Start보다 먼저 → 구독이 항상 먼저, 와이프가 항상 나중)
- 제안: `ResetValues()`에서 이벤트 null 대입 4줄 제거. 이벤트 정리는 도메인 리로드 대비용인
  `Reset()`(SubsystemRegistration)에만 남긴다. 구독 해제는 각 구독자의 OnDisable 책임.
- 학습 포인트: **static event는 씬을 넘어 살아남는 전역 상태**다. "값 리셋" 함수가 구독까지
  끊으면, 구독자는 끊긴 사실을 알 길이 없다(이벤트는 silent fail). 구독의 수명 관리는
  구독자 자신(OnEnable/OnDisable 짝)에게 맡기는 것이 원칙.

### [H2] (네트워크 / 중) 매칭 카운트다운 중 마스터 이탈 시 매칭 데드락
- 위치: `NetworkManager.cs:288-334 (CheckAndStartCountdown/CountdownCoroutine)`
- 내용: 카운트다운 코루틴은 **마스터에서만** 돈다. 매칭 버퍼(6초)+카운트다운(3초) 동안
  마스터가 이탈하면 코루틴이 같이 죽는다. 새 마스터는 `_isCountingDown=false`지만
  `CheckAndStartCountdown()`은 `OnJoinedRoom`/`OnPlayerEnteredRoom`에서만 호출되므로
  **아무도 카운트다운을 다시 시작하지 않는다** → 남은 인원은 매칭 화면에 영구 대기.
  심지어 `IsOpen=false`가 이미 설정된 뒤라면 새 인원 입장으로 트리거될 수도 없다.
- 제안: `OnMasterClientSwitched` 오버라이드 추가 → 내가 새 마스터면 `IsOpen` 복구 여부를
  판단하고 `CheckAndStartCountdown()` 재호출. (LoadingSceneController는 같은 문제를
  이미 `OnMasterClientSwitched`로 처리하고 있음 — 같은 패턴을 매칭 단계에도 적용)
- 학습 포인트: "마스터만 수행하는 일"에는 항상 **마스터 승계 시나리오**를 짝으로 설계해야 한다.
  PUN은 마스터를 자동 승계해주지만, 죽은 코루틴/타이머는 아무도 이어받지 않는다.

### [H3] (버그 / 중) PushModeEndSequence 첫 줄 NRE 시 결과 씬 전환 소프트락
- 위치: `GameModeManager.cs:738 (PushModeEndSequence)`
- 내용: `PlaySFXAudio.Instance.StopWalking();` — 같은 파일의 Absorb 경로(229행)는
  `PlaySFXAudio.Instance?.StopWalking()`으로 널 가드하는데 여기만 직접 호출.
  Instance가 null이면 코루틴이 첫 줄에서 NRE로 죽고, **그 뒤의 슬로우모션·결과 씬 전환이
  전부 실행되지 않는다.** 이미 `_gameRunning=false`가 된 뒤라 `RPC_PushModeGameEnd`도
  재발화하지 않음 → 해당 클라이언트는 게임 화면에 영구 정지(소프트락).
- 제안: `?.` 널 가드로 통일. (G8과 같은 주제지만, 여기는 실패 시 비용이 '씬 전환 중단'이라 더 큼)
- 학습 포인트: 코루틴 안의 예외는 **그 지점에서 코루틴을 통째로 끝낸다.** 씬 전환처럼
  반드시 도달해야 하는 코드 앞에는 죽을 수 있는 호출을 두지 않거나 방어해야 한다.

### [H4] (버그 / 중하) 순위·본인 판정을 닉네임 문자열로 비교 — 중복 닉네임 시 오동작
- 위치: `GameModeManager.cs:379, 638, 653 (GetLocalPlayerRank/UpdateLeaderboard)`
- 내용: `entries[i].name == PhotonNetwork.NickName`으로 '나'를 식별한다. 닉네임은
  사용자가 자유 입력하므로 **두 플레이어가 같은 닉네임이면** 순위/하이라이트가 먼저
  발견된 쪽으로 잘못 표시된다. 봇 색 조회는 이미 ActorNumber를 쓰고 있어(314행) 불일치.
- 제안: 엔트리 튜플에 `actorNumber`(봇은 -viewID 등)를 포함시키고 비교는 항상 번호로.
- 학습 포인트: **표시용 이름(display name)과 식별자(identity)를 분리**하라. 네트워크
  게임에서 유일성이 보장되는 건 서버가 부여한 ActorNumber뿐이다.

### [H5] (아키텍처·데드코드 / 하) GameOver()의 두 번째 Push 분기는 도달 불가능
- 위치: `GameModeManager.cs:508-519 (1차 Push 분기)` vs `543-567 (2차 Push 분기)`
- 내용: 메서드 초입의 1차 Push 분기가 항상 먼저 return하므로 2차 Push 분기(생존 시간
  표시 버전)는 **죽은 코드**다. 두 분기의 UI 문구도 달라("관전 중..." vs "n분 n초 생존")
  어느 쪽이 의도인지 코드만으로 알 수 없게 만든다. 덤: `GameWin()`(397행)이
  `RESULT_SCENE_NAME_ABSORB`를 하드코딩 — 현재는 Absorb 전용 경로라 무해하지만,
  이름과 달리 모드 의존적이라는 사실이 드러나지 않는다.
- 제안: 2차 분기 삭제(원하는 문구는 1차 분기로 통합). GameWin은 모드에 따라 씬을 고르거나
  이름을 `LoadAbsorbResult`처럼 정직하게.
- 학습 포인트: 죽은 코드는 "언젠가 쓸지도"가 아니라 **읽는 사람에게 거짓 정보**다.
  리팩터링 시 분기 순서가 바뀌며 갑자기 살아나는 사고도 일으킨다(git이 기억하니 지워도 된다).

### [H6] (아키텍처 / 하) 점수 집계 로직이 리더보드/결과 씬에 이중 구현
- 위치: `GameModeManager.GetSortedScores()`(299-373) vs `GameResultManager.GatherTopEntries()`(142-187)
- 내용: "플레이어+봇의 (이름, 스케일, 색, 탈락여부) 수집·정렬"이 두 곳에 따로 구현돼 있고,
  **탈락 필터 기준도 다르다**(리더보드: EntityRegistry의 IsEliminated / 결과 씬: 룸 프로퍼티
  정리 + PUSH_SURVIVOR_ACTORS). 한쪽만 수정하면 인게임 순위와 최종 결과가 어긋난다.
- 제안: `ScoreboardSnapshot` 같은 정적 헬퍼로 수집 로직을 한 곳에 모으고, 양쪽은
  필터 옵션만 다르게 호출. (G6 '권위 출처 단일화'와 같은 주제의 상위 과제)

---

## 2026-06-13 루틴 — 어제(06-12) 사용자 직접 수정분(커밋 fec8329) 검증

지난 루틴(06-12) 작업 이후, 사용자가 06-12 21:43에 `GameModeManager.cs`를 직접 수정
(`fec8329`)했다. IO 브랜치 기준 그 외 새 커밋은 없어, 이 변경 1건을 검증 대상으로 삼았다.

### 사용자 변경 내역과 판정 — H3·H5를 직접 적용함 (회귀 없음)
- `PushModeEndSequence` 첫 줄 `PlaySFXAudio.Instance.StopWalking()` → `?.StopWalking()`
  : **H3(결과 씬 전환 소프트락 방지) 정확히 적용.**
- `GameOver()`에서 두 번째 Push 분기(`탈락!\n{n}분 {n}초 생존` 버전) 블록 삭제
  : 이 블록은 첫 번째 Push 분기(469–481, `관전 중...`)가 항상 먼저 `return`하므로 **도달
    불가능한 죽은 코드**였다(= **H5**). 살아있는 첫 분기는 그대로 유지돼
    "관전 전환·권위 시뮬레이션 유지" 동작은 보존된다 → **last-survivor 판정/결과 씬 전환
    회귀 없음.** 삭제된 블록의 지역변수(survived/min/sec)는 블록 내부 한정이라 잔여 참조 없음.
- 결론: 두 변경 모두 안전. 06-12 루틴이 도출한 H3·H5를 사용자가 손수 반영한 것.

### 검증 과정에서 함께 확인한 인접 흐름 (모두 정상)
- Push `timeScale=0`(694) 후 결과 씬 진입 → 동결 우려 있었으나 `NetworkManager.OnSceneLoaded`가
  씬 로드 시 `timeScale=1`로 중앙 복구(코드 257–259 주석 일치). 정상.
- 봇이 최후 생존자인 경우 → 결과 씬은 `Bot####_Name` 룸 프로퍼티 잔존으로 봇 엔트리를 만들고,
  사람은 빈 survivorActors로 모두 제외되어 시상대에 봇만 표시됨. 의도대로 동작.

### [I1] (네트워크·결과 / 하·관찰) 결과 씬의 봇 생존 판정이 룸 프로퍼티 정리 순서에 의존
- 위치: `ScoreboardSnapshot.cs:86 (IsBotEliminated)`, `GameResultManager.GatherTopEntries:147-167`
- 내용: 결과 씬에서는 봇 오브젝트가 이미 파괴돼 `IsBotEliminated(viewId)`가 항상 false다.
  그래서 봇의 시상대 표시 여부가 전적으로 `Bot####_Name` 룸 프로퍼티가 정리됐는지에 달려 있고,
  탈락 봇의 프로퍼티 정리는 비동기다(코드 주석 83–85도 "잠깐 남아 있을 수 있음"으로 인정).
  → 결과 씬 진입 시점에 정리가 안 끝나 있으면 **죽은 봇이 시상대에 오르거나**, 반대로 생존 봇
  프로퍼티가 먼저 사라지면 **최후 생존 봇이 누락**될 수 있는 잠재 레이스.
- 영향: 사람 생존자는 마스터 권위 `PUSH_SURVIVOR_ACTORS`로 보호되지만 **봇은 동급 보호가 없다.**
  동시·근접 타이밍 탈락에서만 드러나므로 평소엔 무해(관찰 항목 우선순위 하).
- 제안: 사람처럼 봇도 마스터가 "생존 봇 viewId" 권위 목록을 룸 프로퍼티에 함께 기록하고,
  결과 씬은 그 목록만 신뢰. (H4 'identity는 번호로'·G6 '권위 출처 단일화'의 연장선)
- 학습 포인트: "오브젝트가 살아있을 때의 판정"과 "오브젝트가 사라진 뒤의 판정"을 한 함수가
  겸하면 후자는 결국 *프로퍼티 정리 순서*라는 암묵적 타이밍에 기댄다. 결과처럼 한 번만 읽는
  스냅샷은 마스터가 만든 **명시적 생존자 목록**을 신뢰하는 편이 안전하다.

---

## 2026-06-16 루틴 — 06-13~06-14 신규 커밋(시작 카운트다운/봇 정지/Milk·젤리 스폰) 리뷰

마지막 루틴(06-13) 이후 06-13 저녁~06-14에 들어온 미리뷰 커밋군을 분석.
대상 커밋: `2ad3279`→`1073166` (로딩 씬 전환, 대쉬 HUD→CooldownRingUI 범용화,
3-2-1 시작 카운트다운, 카운트다운 중 AI 봇 Idle 정지, Milk 슬로우 존 전환,
젤리 스폰 NavMesh 샘플 파라미터 변경).

리뷰 파일: `GameModeManager.cs`(카운트다운), `AIPlayerMovement.cs`/`AIFleeState`/
`AIPushSurviveState`(봇 정지·속도), `PlayerMovement.cs`/`PlayerIdleState`/`PlayerMoveState`
(InputLocked), `Milk.cs`, `NetworkJellyManager.cs`, `LoadingCenterImageAni.cs`.

### 좋았던 점(설계 관찰)
- 카운트다운 동안 `_gameRunning=false` & `Phase!=Playing` 유지로 타일 붕괴·타이머·흡수·공격을
  기존 가드 그대로 막고, **입력만 `InputLocked`로 별도 차단**(Idle 애니는 유지) — 책임 분리 깔끔.
- 봇 정지에 `GameState.Phase`가 아니라 **전용 플래그 `CountdownActive`**를 쓴 점(주석에 이유 명시:
  Phase는 '로컬 플레이어 사망'에 오염되어 Absorb에서 마스터 사망 시 전 봇 정지 버그). 정확한 판단.
- `GameStartTime`(붕괴 타이밍 기준)을 카운트다운 종료 후 '실제 시작' 시점에 기록 → 카운트다운만큼
  붕괴가 밀리도록 한 점 일관적. `TileCollapseManager.Update`도 `IsGameRunning` 가드라 충돌 없음.

### [J1] (버그 / 높음·확인필요) 젤리 스폰 NavMesh 샘플 반경(3f)이 수직 오프셋(5f)보다 작아 스폰 실패 가능
- 위치: `NetworkJellyManager.cs:202-205 (TryGetNavMeshSpawnPosition)`
- 내용: 후보 위치 `candidate = (x, baseY + 5f, z)`를 만든 뒤 `NavMesh.SamplePosition(candidate, 3f)`로
  스냅한다. `baseY`는 navmesh 기준 Y(origin 근방 샘플값). **평평한 바닥에서 후보점은 navmesh보다
  5만큼 위**이고, SamplePosition은 후보점에서 반경 3 이내의 navmesh만 찾으므로 5 > 3 → **30회 시도
  전부 실패** → `TryGetNavMeshSpawnPosition`이 false 반환 → 젤리가 거의/전혀 스폰되지 않을 수 있다.
  (이전 값 20f는 5f 수직 갭을 충분히 덮었음. 이번 커밋에서 20f→3f로 줄이며 깨진 것으로 보임.)
- 영향: Absorb 모드 핵심 자원(젤리)이 안 깔리면 게임 자체가 성립 안 됨. 맵 navmesh 기복에 따라
  일부만 잡힐 수도 있어 '가끔 적게 스폰'으로도 나타날 수 있음.
- 제안(택1): (a) 수직 오프셋을 줄이기(`baseY + 1f` 등), (b) 반경을 다시 키우기(`>= 6f`),
  (c) 후보점에서 아래로 Raycast/높이 보정 후 SamplePosition. **맵에서 실제 스폰 수 확인 필요**.
- 학습: `NavMesh.SamplePosition(point, maxDistance)`의 maxDistance는 3D 유클리드 거리다. 공중에
  띄운 후보를 바닥 navmesh에 스냅하려면 maxDistance ≥ 수직 높이여야 한다.

### [J2] (버그 / 중) Milk OnTriggerExit에 IsMine 가드 누락 — Enter/Exit 비대칭으로 원격 사본 속도 증식
- 위치: `Milk.cs:18 (Enter 가드)` vs `38-56 (Exit, 가드 없음)`
- 내용: `OnTriggerEnter`는 `if (nps != null && !nps.photonView.IsMine) return;`로 원격 플레이어를
  건너뛰지만(속도 ×0.5 안 함), `OnTriggerExit`는 그 가드 없이 **누구에게나 ×2 복원**을 적용한다.
  → 원격 클라가 보는 플레이어 사본은 enter는 스킵·exit는 ×2 → 밀크를 나갈 때마다 `moveSpeed`가
  **짝 없는 ×2로 누적**. 봇은 `nps==null`이라 모든 클라가 enter/exit를 처리해 마스터 외 사본까지 변형.
- 영향: 원격 사본 `moveSpeed`가 실제 이동(동기화 위치)에 직접 쓰이지 않으면 무해하지만, 로컬 예측·
  애니·이펙트가 참조하면 점점 빨라지는 값으로 드러남. 명백한 논리 비대칭.
- 제안: Exit도 Enter와 동일한 IsMine(봇은 master 권위) 가드를 적용.

### [J3] (버그 / 중) Milk가 공유 `moveSpeed`를 곱셈으로 파괴적 변형 — 불균형 시 영구 손상
- 위치: `Milk.cs:23-31 (×0.5)`, `48-56 (×2)`
- 내용: 직렬화 필드 `moveSpeed`를 직접 곱/나눈다. enter와 exit가 항상 짝지어 실행되면 0.5↔2는
  2의 거듭제곱이라 정확히 복원되지만, **짝이 깨지는 경로가 많다**: (1) 밀크 위에서 사망/흡수로
  PlayerMesh 콜라이더 파괴 시 OnTriggerExit가 보장되지 않음 → 리스폰 후에도 ×0.5 영구 잔존,
  (2) 겹친 밀크 동시 진입, (3) 씬 전환 중 콜라이더 토글. 어느 경우든 base 속도가 영구 오염되고
  복구 수단이 없다(원본 미저장).
- 제안: 공유 `moveSpeed`를 직접 건드리지 말고 **baseSpeed + 상태(밀크 위 bool/카운트)**로 매 프레임
  계산하거나(`effectiveSpeed = base * (onMilk?0.5:1)`), 진입 시 원본을 저장해 이탈 시 그 값으로 복원.
- 학습: 외부 효과가 '원본을 모르는 채' 상태값을 가감/곱제하면, 효과 적용/해제가 한 번이라도 어긋나면
  되돌릴 기준이 사라진다. 가역 효과는 *기준값 + 토글*로 모델링한다(G1·H1 '상태 복원' 주제의 변형).

### [J4] (설계·데드코드 / 하·확인) Milk의 스케일 감소 + 5초 리스폰 기능이 통째로 사라짐
- 위치: `Milk.cs` 전체 — `RespawnRoutine`/`respawnTime`/`SetAppearance`/DataManager 스케일 호출이
  더 이상 호출되지 않는 데드 코드. 기존 'milk=스케일 감소 후 리스폰'이 'milk=슬로우 존'으로 교체됨.
- 내용: 의도된 게임플레이 리디자인이면 데드 멤버를 정리하는 게 맞고, 의도치 않은 누락이면 스케일
  감소 효과가 사라진 회귀다. **설계 의도 확인 필요**.

### [J5] (안정성 / 중) 카운트다운 중단 시 `PlayerMovement.InputLocked`/`CountdownActive` 미복구 → 입력 영구 잠금 위험
- 위치: `GameModeManager.StartCountdownRoutine:184-228`, `PlayerMovement.cs:31-34 (ResetInputLocked)`
- 내용: 카운트다운(약 3.7초) 진행 중 GameModeManager가 파괴되거나 씬이 리로드되면 코루틴이 중도에
  끊긴다. `CountdownActive`는 `ResetStatics()`(Awake)에서 리셋되지만, `PlayerMovement.InputLocked`는
  **`SubsystemRegistration`(도메인 리로드)에서만** 리셋된다. 빌드에서 씬 리로드는 보통 도메인 리로드를
  동반하지 않으므로, 중단된 채 다음 씬으로 넘어가면 **로컬 입력이 영구 잠금**될 수 있다.
- 제안: `ResetStatics()`에 `PlayerMovement.InputLocked = false;`도 함께 넣어 카운트다운 전용 두 플래그를
  같은 지점에서 짝으로 해제. 또는 코루틴 종료를 try/finally·OnDisable로 보장.
- 학습: 코루틴이 켠 **전역 플래그는 모든 중단 경로(정상 종료/StopAllCoroutines/오브젝트 파괴)에서
  반드시 해제**해야 한다(G1과 동일 교훈, 이번엔 입력 잠금이라 비용이 큼).

### [J6] (견고성 / 하·관찰) `StartGameInternal`이 비멱등 — RPC 재수신 시 진행 중 게임 리셋
- 위치: `GameModeManager.cs:169-181`, `RPC_StartGame:158-162`
- 내용: `StartGameInternal`은 매 호출마다 `GameState.ResetValues()` + `_gameRunning=false` +
  `CountdownActive=true`를 다시 실행한다. 만약 `RPC_StartGame`이 어떤 이유로 두 번 도착하면 진행 중
  게임의 스코어/스케일이 초기화되고, `if(!_countdownRunning)` 가드 때문에 코루틴은 재시작되지 않아
  `_gameRunning`이 영구 false로 남는 소프트락이 가능. 현재는 `_spawned` + `RpcTarget.All`(비버퍼)로
  1회만 발화돼 실질 위험은 낮음(관찰 항목).
- 제안: 이미 시작/카운트다운 중이면 조기 return하는 멱등 가드(`if (_countdownRunning || _gameRunning) return;`).

### [J7] (견고성 / 하) `LoadingCenterMultiAni`의 부모 컴포넌트 조회 null 미가드
- 위치: `LoadingCenterImageAni.cs:73 (phase2Duration = GetComponentInParent<LoadingBGSlideAni>().holdSeconds)`
- 내용: 부모에 `LoadingBGSlideAni`가 없으면 NRE로 로딩 연출이 죽는다. 같은 파일 다른 참조는 null 가드가
  있는데 이 한 줄만 직접 접근. 제안: 변수에 받아 null 체크 후 기본값 유지.

---

## 2026-06-18 루틴 — 신규 커밋 없음 / 인게임 코어(타일 붕괴·흡수·타이머) 신규 심층 리뷰

06-17 루틴(G3·G4·G6·G7 적용) 이후 IO 브랜치에 새 커밋이 없어, 지금까지 깊게 보지 않았던
**인게임 진행 코어**를 새 리뷰 대상으로 삼았다. 시퀀스 상류(로비→매칭→로딩→결과)와 시작
카운트다운은 이미 다뤘으므로, 이번엔 라운드 도중 매 프레임 도는 핵심부를 본다.
리뷰 파일: `TileCollapseManager.cs`(붕괴/마모), `JellyColliderAbsorb.cs`/`PlayerAbsorber.cs`/
`PlayerAbsorbingManager.cs`(흡수 체인), `GameTimer.cs`/`ClearJudge.cs`(레거시 단판 진행부).

### [K1] (성능 / 중) Push 모드 타일 어둡게(DarkenStepTile)가 타일마다 머티리얼 인스턴스 복제 — G3가 못 덮은 경로
- 위치: `TileCollapseManager.cs:289 (_tileOriginalColors[tileKey] = rend.material.color)`, `294 (rend.material.color = ...)`
- 내용: G3(06-17)는 `FallingTile`의 색 변경을 `MaterialPropertyBlock`으로 바꿔 붕괴 타일별
  머티리얼 인스턴스 복제/배칭 깨짐을 제거했다. 그런데 **Push 모드에서 밟을 때마다 타일을 점점
  어둡게 하는 경로**는 여전히 `rend.material`을 쓴다. `.material`은 읽기(289)·쓰기(294) 양쪽 모두
  공유 머티리얼을 **인스턴스 복제**하므로, 밟힌 Push 타일 수만큼 머티리얼 사본이 생겨 드로우콜
  배칭이 깨지고 메모리가 늘어난다. Push 맵은 거의 모든 칸이 마모되므로 G3와 동일한 비용이
  고스란히 남아 있는 셈(G3가 FallingTile만 손대고 이 매니저 경로는 누락).
- 제안: G3와 동일하게 처리 — 원본 색은 `sharedMaterial`로 읽어 캐시하고, 색 적용은
  `MaterialPropertyBlock(rend.SetPropertyBlock)`으로. (FallingTile.cs:16/104의 패턴 그대로 재사용)
- 학습: `.material`은 *읽기만 해도* 인스턴스를 만든다. 같은 머티리얼을 공유하는 다수 오브젝트의
  색을 개별 변경할 때는 `MaterialPropertyBlock`이 배칭을 유지하는 정석이다(G3·G4와 같은 주제).

### [K2] (아키텍처·일관성 / 중하) G6 '탈락판정 단일화'가 Push 경로 두 곳을 누락 — raw "Eliminated" 매직스트링 직접 조회 잔존
- 위치: `TileCollapseManager.cs:207-209 (UpdateStepCollapse)`, `AIPushSurviveState.cs:167-169 (FindNearestTarget)`
- 내용: G6(06-17)는 사람/봇 탈락 판정을 `NetworkPlayerSync.IsOutOfPlay`/`AIPlayerMovement.IsOutOfPlay`
  단일 헬퍼로 통일했다고 기록돼 있으나, 위 두 곳은 여전히
  `player.photonView.Owner.CustomProperties.TryGetValue("Eliminated", out ...)`로 **룸 프로퍼티를
  직접·문자열로** 읽는다. 두 루프 모두 `EntityRegistry.Players`(= `IReadOnlyList<NetworkPlayerSync>`)를
  순회하므로 `player.IsOutOfPlay` 한 줄로 대체 가능하다.
  - 단순 정합성 문제만이 아니다: `IsOutOfPlay`는 `_isAbsorbed || owner "Eliminated"`라 **방금
    흡수됐지만 룸 프로퍼티 전파가 아직 안 끝난 순간**도 잡는다. 현재 raw 조회는 이 순간을 놓쳐,
    이미 흡수된 플레이어를 Push 밟기 마모 대상/봇 추격 대상으로 한 틱 더 본다(미세 오동작).
  - 또한 `"Eliminated"` 문자열이 코드 전반에 6+곳 하드코딩(G5 미해결). `NetworkPlayerSync.ELIMINATED_KEY`
    상수는 이미 있으나 대부분 호출부가 안 쓴다.
- 제안: 위 두 루프의 raw 조회를 `if (player == null || player.IsOutOfPlay) continue;`로 교체.
  (G6의 적용 범위를 Push 스텝붕괴/봇 타겟팅까지 확장 — G5 매직스트링 제거도 일부 진전)
- 학습: '단일 출처로 통일' 리팩터링은 *모든 호출부를 빠짐없이* 옮겨야 의미가 있다. 한두 곳이라도
  옛 경로(raw 조회)가 남으면 "단일화했다"는 기록과 실제가 어긋나고, 미묘한 타이밍 차로만 드러나는
  불일치가 잠복한다.

### [K3] (성능·로그 / 하·관찰·레거시) ClearJudge.Update가 매 프레임 Debug.Log + 클리어 로직은 주석처리됨
- 위치: `ClearJudge.cs:93 (Debug.Log("저울 눌림"))`, `98-115 (JudgeClear 통째 주석)`, `118-155 (ClearSequence 미호출)`
- 내용: 저울이 눌린 상태로 머무는 동안 `Debug.Log("저울 눌림")`이 매 프레임 출력된다(빌드 로그
  스파이크 — G3a와 동일 주제). 다만 정작 `JudgeClear()`가 통째 주석 처리돼 `ClearSequence`도
  호출되지 않으므로 이 컴포넌트의 클리어 연출은 **현재 비활성(레거시)** 로 보인다.
- 제안: 활성 씬에서 쓰이지 않으면 컴포넌트/씬 참조 제거, 쓰인다면 로그를 `#if UNITY_EDITOR`로 감싸기.
  **먼저 현재 게임 씬에 ClearJudge가 붙어 있는지 확인 필요**(네트워크 진행은 GameModeManager 전담이라
  단판 클리어 판정과 무관해 보임).

### [K4] (안정성 / 하·관찰·레거시) GameTimer.GameFail이 timeScale=0 설정 후 널가드 없는 호출들로 중단될 위험
- 위치: `GameTimer.cs:68 (Time.timeScale=0f)` 직후 `70 StopWalking()`, `72 playerController`, `75 softBody3D`,
  `78/83 playerAnimController`, `80 mainCamera_Action`, `84 PlayFailSound()` — 모두 직접 접근(널가드 없음)
- 내용: `GameFail()`은 먼저 `Time.timeScale=0`으로 게임을 얼린 뒤, 인스펙터 참조들을 줄줄이 직접
  호출한다. 그중 하나라도 null이면(예: `PlaySFXAudio.Instance`) 그 지점에서 메서드가 NRE로 끊겨
  **timeScale=0인 채 결과 연출/복구가 실행되지 않는 소프트프리즈**가 된다(H3·G8과 동일 주제).
  단, 이 타이머는 `Time.timeScale=0`을 전역으로 거는 **단판(싱글) 진행용**으로 보이며, 네트워크
  시퀀스는 `GameModeManager`가 전담하므로 현재 사용 여부 확인이 선행돼야 한다.
- 제안: 사용 중이면 H3 패턴대로 `?.`/널가드로 통일하고 timeScale 설정을 안전 호출 뒤로. 미사용이면 정리.

---

## 2026-06-20 루틴 — 06-19 K1/K2 적용 커밋 검증 + 흡수 체인 신규 리뷰

지난 루틴(06-18)에서 도출한 K1/K2가 06-19 커밋 `532d3da`로 적용됐다. IO 브랜치 기준
그 외 새 커밋은 없어, (1) 이 적용 1건을 코드 대조로 검증하고, (2) 06-18 리뷰 파일로
이름만 올랐던 **흡수 체인**(JellyColliderAbsorb/PlayerAbsorber/PlayerAbsorbingManager)을
아직 도출 항목이 없던 새 영역으로 골라 심층 분석했다.

### 06-19 커밋(532d3da) 검증 — K1·K2 모두 회귀 없음
- **K1** `TileCollapseManager.DarkenStepTile`(282-309): `rend.material.color` 읽기/쓰기를
  `sharedMaterial` 읽기 + `MaterialPropertyBlock` 쓰기로 전환. `_BaseColor`(URP)/`_Color`(빌트인)를
  `HasProperty`로 자동 선택, 둘 다 없으면 조기 return. `_mpb`는 `GetPropertyBlock`로 렌더러 현재
  블록을 읽어 색만 덮어 적용 → **정석 MPB 패턴**(G3와 동일). `_tileOriginalColors`는 이 메서드
  외에 복원 경로가 없고(어두워진 타일은 `CollapseStepTile`로 교체) `.material` 잔존 경로 0건 확인.
  → 밟힌 Push 타일마다 머티리얼 사본이 생기던 배칭 깨짐 제거. **깨끗하게 적용됨.**
- **K2** `TileCollapseManager.UpdateStepCollapse:212` / `AIPushSurviveState.FindNearestTarget:166`:
  raw `Owner.CustomProperties["Eliminated"]` 직접 조회 → `player.IsOutOfPlay`로 교체.
  `NetworkPlayerSync.IsOutOfPlay`(66-77)는 `_isAbsorbed || (owner "Eliminated")`이며 photonView/Owner
  null 가드가 견고. 교체는 기존 raw 체크와 동등하면서 **흡수 직후(_isAbsorbed) 한 틱을 추가로 포착**해
  더 정확하다(미세 오동작 제거). 회귀 없음.
- **tools/update_sheets.py** `safe_append`: `append_row`가 개발계획서 행을 덮어쓰던 사고(06-18 행 유실)
  방지용으로 현재 값 개수 +1 행을 직접 계산해 `update`로 기록. 컬럼 폭(plan 11/bug 8)이 26 이내라
  `end_col = chr('A'+n-1)` 단일 문자 계산도 안전. 타당.
- 결론: 06-18 루틴이 도출한 K1·K2를 정확히 반영. 적용 상태표 K1·K2를 [x]로 정합화.

### 흡수 체인 구조 관찰 — 설계 의도 확인 (모두 정상)
- 원격 플레이어는 `NetworkPlayerSync.SetupRemotePlayer:179-182`에서 `PlayerAbsorber`/
  `PlayerAbsorbingManager`를 `enabled=false`로 끈다. 비활성 MonoBehaviour는 `OnTriggerEnter`를
  받지 않으므로 **젤리 흡수는 로컬(IsMine) 플레이어만 수행**한다 → 원격 사본이 GrowByJelly로
  스케일을 이중 적용하던 충돌은 이미 차단됨(주석 178도 동일 이유 명시). 설계 일관적.
- 젤리 **파괴**는 `NetworkJellyManager.RequestDestroyJelly`→`RPC_DestroyJelly`(RpcTarget.MasterClient,
  마스터만 `PhotonNetwork.Destroy`)로 **마스터 권위**. 두 번째 파괴 요청은 `PhotonView.Find`가
  null이라 무해. 파괴 경로는 견고.

### [L1] (네트워크·아키텍처 / 하·관찰·확인필요) 젤리 흡수 *점수/성장*은 로컬 무검증 — 경합 시 중복 흡수(double-eat) 가능
- 위치: `JellyColliderAbsorb.OnAbsorbed:114-135`(로컬 성장 호출 + RequestDestroyJelly) vs
  플레이어-플레이어 흡수 `NetworkPlayerSync.RPC_RequestAbsorbValidation:457-470`(마스터가 스케일
  비교로 흡수 정당성 검증)
- 내용: 젤리 *파괴*는 마스터 권위지만, "누가 먹어서 성장하는가"는 각 클라가 로컬에서 즉시 처리하고
  **마스터 검증이 없다.** 같은 젤리가 두 플레이어에 근접한 경합 상황에서 양쪽 클라가 각자 자기
  로컬 플레이어로 흡수를 완료하면, 젤리는 마스터에서 1회만 파괴되지만 **점수/스케일은 양쪽에 적용**
  (double-eat)될 수 있다. 플레이어 간 흡수는 권위 스케일 비교(RPC_RequestAbsorbValidation)로
  한쪽만 인정하는 것과 대조적인 비대칭.
- 영향: 젤리는 분산 스폰이라 경합이 드물어 평소 무해. 군집 스폰/좁은 구역에서만 드러날 수 있는
  점수 불일치(관찰 우선순위 하).
- **확인 필요**: 비소유 클라에서 젤리 PhotonView의 `OnTriggerEnter`/`StartAbsorb`가 실제로 트리거되어
  로컬 흡수를 완료하는지(동기화 위치 vs 로컬 rb 이동 충돌 여부). 소유자 클라에서만 실질 흡수가
  완료되는 구조라면 위험은 더 낮다.
- 학습: '파괴(소멸)의 권위'와 '효과(점수·성장)의 권위'는 별개다. 소멸만 마스터가 쥐고 효과는 각자
  로컬에서 처리하면, 한 자원을 둘이 동시에 소비하는 경합에서 효과가 중복 적용된다. (H4 identity·
  G6 권위 단일화의 연장 — '한 번만 일어나야 하는 효과'는 권위자가 1회만 승인해야 한다.)

### [L2] (안정성 / 하) JellyColliderAbsorb null 미가드 — NRE 시 흡수된 젤리가 파괴/숨김되지 않음(H3·G8 테마)
- 위치: `JellyColliderAbsorb.Awake:32,42`(jellyRenderer), `OnAbsorbed:120`(GetComponent<JellyObject>())
- 내용: (a) `jellyRenderer = GetComponentInChildren<Renderer>()` 직후 `jellyRenderer.gameObject.tag="Edible"`
  를 null 가드 없이 접근 — 렌더러 없는 프리팹 구성이면 Awake가 NRE. (b) `OnAbsorbed`의
  `GetComponent<JellyObject>().jellyType`도 null 미가드. **(b)가 터지면 그 아래 `RequestDestroyJelly`(129)가
  실행되지 않아**, 흡수는 완료(absorbing=false) 처리됐는데 젤리 오브젝트는 렌더러 끄기/파괴가 안 돼
  화면에 '먹힌 젤리'가 남는 소프트 누수가 된다(코루틴/메서드 중단이 정리 코드를 건너뛰는 H3·G8과 동일 주제).
- 제안: JellyObject를 변수로 받아 null 분기, jellyRenderer null 가드 추가. 프리팹 구성이 항상
  일관되면 실질 위험은 낮으므로 우선순위 하(관찰).

---

## 2026-06-20 버그 제보 수정 (사용자 제보 — 흡수 모드 3건, 즉시 수정)

사용자가 흡수 모드에서 3가지 문제를 제보했고, 코드 분석으로 원인을 특정해 즉시 수정했다.

### [BUG-A] 봇이 밀크로 느려진 뒤 원래 속도로 안 돌아옴 — **확실**
- 위치: `Milk.cs:40-41,88-89` ↔ `AIPlayerMovement` 이동(`Update:418 Agent.velocity = wishDir * Agent.speed`),
  FSM 상태 Enter들(`AIChaseState:22`, `AIFleeState:24`, `AIWanderState:28` 등 `Agent.speed = ai.moveSpeed`)
- 원인: 봇의 **실제 이동 속도는 `Agent.speed`**인데, 이 값은 **FSM 상태 Enter에서만** `moveSpeed`로부터
  복사된다. Milk는 `moveSpeed` *필드*만 곱/나눴으므로, 밀크에서 나와 `moveSpeed`가 복원돼도
  `Agent.speed`는 다음 상태 전환이 일어나기 전까지 슬로우 값으로 남는다 → "느려진 뒤 안 돌아옴".
  (반대로 진입 직후에도 상태 전환 전엔 슬로우가 즉시 안 먹는 비결정적 동작도 동반)
- 수정: `AIPlayerMovement.ApplySpeedMultiplier(m)` 추가 — `moveSpeed`와 `Agent.speed`를 **같은 비율로
  함께** 곱한다(상태별 계수 0.9 등 보존). Milk 진입/복원이 봇에 대해 이 헬퍼를 호출하도록 변경.
  사람 플레이어는 `moveSpeed`를 매 프레임 직접 읽으므로(PlayerMovement:197) 기존 로직 유지.
- 학습: "설정값(base) 필드"와 "엔진이 실제로 쓰는 캐시값(Agent.speed)"이 분리돼 있고 갱신 시점이
  이벤트(상태 전환)에 묶이면, base만 바꾸는 외부 효과는 캐시에 반영되지 않는다. 둘을 같은 연산으로
  함께 갱신해야 한다(G1/H1 '상태 복원'·J3 '가역 효과' 주제의 변형).

### [BUG-B] 젤리(WanderingAI)가 땅에 박혀서 소환됨 + (후속) 박힌 채 못 움직임 — 높음
- 위치: `NetworkJellyManager.SpawnRandomJelly:150`/`SpawnJellyAt:259`, 젤리 프리팹 NavMeshAgent
  `m_BaseOffset:1.84`(+`m_IsKinematic:1`, 커스텀 agentType), `WanderingAI.Update:59`
- 1차 수정(오판·되돌림): `ApplyAgentBaseOffset`로 스폰 직후 `transform.position = pos + up*baseOffset`
  를 직접 대입했다. **이게 오히려 회귀를 만들었다** — 활성 NavMeshAgent의 transform을 Warp이 아니라
  직접 대입하면 agent 내부 위치와 어긋나 NavMesh 밖으로 떨어지고, 그 젤리는 '바닥에 박힌 채 전혀
  안 움직이는' 상태가 된다(WanderingAI는 `isOnNavMesh==false`면 이동을 멈춤). 사용자가 "여전히 몇몇은
  박혀서 못 움직인다"고 제보.
- 정정 원인: (a) 활성 agent는 위치를 **Warp으로** 옮겨야 NavMesh에 안착한다(봇 초기화도 동일 패턴 —
  AIPlayerMovement:243·261은 *비활성 상태로 위치 잡고→enable→Warp*). (b) baseOffset은 agent가
  **자동으로** 적용하므로 수동으로 더하면 안 된다(봇도 `hit.position`만 쓰고 offset을 더하지 않음).
  (c) 스폰뿐 아니라 **게임 중 발판이 붕괴(carve)돼 발 밑 NavMesh가 사라지면** 젤리가 NavMesh 밖으로
  떨어지는데, WanderingAI에 복구 경로가 없어 영영 멈춘다(이게 "여전히"의 본질 — 1차 수정과 무관한
  기존 버그).
- 최종 수정:
  - `PlaceJellyOnNavMesh(jelly, navMeshPos)` — 스폰 직후 agent가 활성이면 `agent.Warp(navMeshPos)`.
    Warp이 유효 NavMesh 지점에 안착시키고 이후 agent가 baseOffset만큼 들어올린다(직접 대입·수동 offset
    제거). 두 스폰 경로(SpawnRandomJelly/SpawnJellyAt)에 적용.
  - `WanderingAI.Update`: `if(!agent.isOnNavMesh) return;`을 **자가 복구**로 교체 — 근처(5f) NavMesh로
    Warp 복귀. 붕괴/안착 실패로 NavMesh를 벗어난 젤리가 박힌 채 멈추지 않게 한다(봇의 허공 복구와 동형).
- 학습: NavMeshAgent를 옮길 땐 **항상 Warp**(transform.position 직접 대입 금지). baseOffset은 agent가
  관리하므로 수동 보정 금지. 그리고 "한 번 NavMesh를 벗어나면 멈추는" 엔티티는 반드시 복귀 경로를
  함께 둬야 한다(붕괴 맵에선 carve로 언제든 발 밑이 사라질 수 있다).

### [BUG-C] 봇이 발판 없는 곳(허공) 위에 떠 있음 — 중(흡수 모드 한정)
- 위치: `AIPlayerMovement.StateEvalLoop:297` 가드, `CheckGroundBelow` 호출부 `:392-393`(Push 전용)
- 원인: (1) 발 밑 지면이 없으면 낙하시키는 `CheckGroundBelow()`가 **Push 모드에서만** 호출된다.
  (2) 흡수 모드엔 허공탈출(`IsOverVoid`→안전 타일 Warp)이 있지만, 그 앞의 가드
  `if (!Agent.enabled || !Agent.isOnNavMesh) continue;`가 **NavMesh 밖 봇을 먼저 걸러내** 복구를 건너뛴다.
  → 흡수 모드에서 봇이 넉백/타일 붕괴로 NavMesh 밖 허공에 박제되면 낙하도 복구도 안 돼 **영구 floating**.
- 수정: 가드를 둘로 분리. `!Agent.enabled`(정상 추락 중 — CheckGroundBelow가 Agent를 끔)면 그대로 두고,
  `Agent 켜짐 + !isOnNavMesh`(허공 박제)면 흡수 모드에서 가장 가까운 안전 타일로 `Agent.Warp` 복구.
  Push 모드는 자체 낙하 로직이 있으므로 비-Push로 게이팅(추락 봇을 되살리지 않음).
- 안전성 근거: 추락 봇은 `CheckGroundBelow`/`AwakePhysicsOnTile`이 `Agent.enabled=false`로 꺼두므로,
  'Agent 켜짐 + NavMesh 밖'은 추락이 아닌 박제 상태로 깔끔히 구분된다.
- 학습: "마스터만/특정 모드만 하는 처리"는 짝이 되는 시나리오(다른 모드·승계)를 함께 설계해야 한다
  (H2와 같은 주제). 낙하 회복이 Push에만 있으면 흡수 모드의 같은 상황은 무방비로 남는다.

> ※ BUG-A/B/C는 사용자 제보로 즉시 수정(루틴 작업흐름 4). BUG-B·C는 NavMesh 런타임 동작이라
>   실제 인게임에서 박힘/floating이 사라지는지 **맵 확인 권장**. 회복 동작(BUG-C)이 게임 느낌과
>   안 맞으면 '낙하'로 바꾸는 대안도 가능(CheckGroundBelow를 흡수 모드에도 호출).

---

## 2026-06-20 검증 — 시작 카운트다운(3-2-1-시작) 두 모드 정상 동작 확인 (사용자 요청)

사용자 요청으로 카운트다운 시스템이 흡수/밀치기 두 모드에서 제대로 동작하는지 코드로 검증했다.

### 결론: 카운트다운 자체는 두 모드에서 동일·정상 동작
- 진입 경로가 **모드 무관 공통**: `SpawnAndStartGame`(Start/OnJoinedRoom) → 마스터가
  `RPC_StartGame`(RpcTarget.All) → 전 클라 `StartGameInternal` → `StartCountdownRoutine`.
  모드는 `RestoreGameModeFromRoom`이 룸 권위값에서 복원(호스트/게스트 일치).
- `StartCountdownRoutine`: n=3→2→1 각 `WaitForSecondsRealtime(1f)`(timeScale 무관 — Push 종료
  timeScale=0와 충돌 없음) → "시작!" 표시와 **동시에** `_gameRunning=true`/`Phase=Playing`/
  `CountdownActive=false`/`InputLocked=false`를 한 묶음으로 전환 → 마스터가 `GameStartTime`(실제
  시작 시점)을 기록 → 0.7s 후 텍스트 숨김. UI 참조는 모두 `centerCountdownText != null` 가드.
- **카운트다운 동안 게임플레이 정지가 두 모드 모두에 올바로 적용됨**(코드 대조):
  - 로컬 입력: `PlayerMovement.InputLocked=true` (Idle 애니는 유지)
  - 봇: `GameModeManager.CountdownActive` → AIPlayerMovement가 velocity 0 + ResetPath (Phase가
    아니라 전용 플래그라 'Absorb에서 마스터 사망 시 전 봇 정지' 버그 없음)
  - 타일 붕괴: `TileCollapseManager.Update`가 `IsGameRunning(_gameRunning)` 가드 → Push 스텝마모·
    Absorb 링붕괴 둘 다 정지. 붕괴 타이밍 기준 `GameStartTime`도 카운트다운 후 시점이라 일관.
  - 게임 타이머: `Update`가 `!_gameRunning`이면 return (Push 카운트업/Absorb 카운트다운 둘 다)
  - 플레이어↔플레이어 흡수/공격: `NetworkPlayerSync`의 `Phase != Playing` 가드 8곳 → 카운트다운
    (Phase=None) 중 전부 차단
  - J5 누수 방지: `Awake`/`OnDestroy`에서 `CountdownActive`·`InputLocked` 해제(씬 전환 잠금 방지)
- Push 모드는 젤리를 스폰하지 않으므로(NetworkJellyManager.Start) 아래 누락과 무관 — **완전 정상.**

### [BUG-D] (흡수 모드 한정 / 하) 카운트다운 중 '젤리 흡수'만 정지에서 누락 → 시작 전 성장 — **수정함**
- 위치: `PlayerAbsorber.OnTriggerEnter:16` (모드만 체크, 진행 상태 미체크),
  `WanderingAI`(카운트다운 미인지로 배회), `NetworkJellyManager.Start`(스폰 루틴이 Start에서 시작)
- 원인: 흡수 모드에선 젤리가 씬 시작(Start)에 스폰돼 카운트다운 중에도 배회한다(WanderingAI는
  `CountdownActive`/`_gameRunning`을 보지 않음). 그런데 젤리 흡수 진입점 `PlayerAbsorber.OnTriggerEnter`는
  `GameModeType.Push`만 거를 뿐 카운트다운/Phase를 보지 않아, 배회 젤리가 **정지(InputLocked)한
  플레이어**에 닿으면 '시작!' 전에 흡수→성장한다. 카운트다운의 '다 같이 대기 후 시작' 취지에 어긋나는
  유일한 빈틈(입력·봇·타일·P2P흡수는 모두 차단되는데 젤리 흡수만 샜다).
- 수정: `OnTriggerEnter`에 `if (GameState.Phase != GamePhase.Playing) return;` 추가 — 플레이어간
  흡수와 **동일 기준**(Phase=Playing일 때만)으로 게이팅. 카운트다운(None)·게임 종료 후 모두 차단.
- 학습: "게임을 멈춘다"는 정지는 *모든 득점/상태변경 경로*에 빠짐없이 적용돼야 한다. 한 경로(젤리
  흡수)만 다른 기준(모드)으로 가드하면 정지 의미가 새어 나간다(G6/K2 '단일 기준' 주제의 변형).

> ※ 추가 점검 권장(코드로는 확인 불가): 두 게임 씬의 GameModeManager 인스펙터에 `centerCountdownText`가
>   각각 연결돼 있는지. 미연결이면 숫자 표시만 안 될 뿐 게임 시작 로직은 정상 진행된다(널 가드 있음).

---

## 2026-06-20 버그 제보 수정 (2차 — Push 발판 미붕괴 / 사망 시 로딩 회색)

### [BUG-E] (Push / 중) 가끔 밟아도 발판이 안 떨어짐 — 빠른 이동 시 타일 샘플 누락
- 위치: `TileCollapseManager.UpdateStepCollapse:200`(STEP_PROCESS_INTERVAL=0.15s 샘플), `TryStepAt:223`
- 원인: 스텝 마모는 마스터가 **0.15초마다 엔티티의 '현재 위치 한 점'만** 샘플링하고, 타일이 바뀌는
  순간에만 1회 마모한다. 대쉬 등으로 빠르게 이동하면 한 샘플 간격(0.15s) 동안 **여러 칸을 건너뛰어**,
  그 중간 칸들은 한 번도 마모되지 않는다 → 분명 밟고 지나갔는데 마모 카운트가 안 올라 안 떨어진다.
  ("가끔씩"="빠르게/대쉬로 지날 때". 이산 샘플링의 전형적 빈틈.)
- 수정: `WearTilePath(lastTile→현재)` 추가 — 마지막 밟은 칸에서 현재 칸까지 **그리드 라인을 훑어**
  건너뛴 칸도 각각 마모. 시작 칸은 이미 마모됐으니 제외, 인접 1칸은 기존과 동일, 비정상적으로 먼
  이동(맨해튼 거리 > 8 = 리스폰/Warp 추정)은 라인 생략(텔레포트가 칸 한 줄을 깎지 않게).
- 학습: 연속 이동을 '주기적 점 샘플'로 처리하면 샘플 사이를 건너뛴다. 경로 의존 효과(밟기 마모)는
  마지막 샘플과 현재 샘플 사이를 **스윕**해야 빠른 이동에서도 누락이 없다.

### [BUG-F] (흡수 / 중) 사망(관전) 상태로 결과 전환 시 로딩 화면 대신 회색 배경
- 위치: `GameModeManager.GameOver` Absorb 사망 분기(:570~), `GameEndingSequenceRoutine`(Update에서 시작),
  `GameWin:416`(여기서만 `LoadingSceneController.NextSceneName` 지정), `LoadingSceneController.Awake:66`
- 원인: 흡수 모드 종료는 `Update`의 타이머가 `GameEndingSequenceRoutine→GameWin`을 부르는데, 이는
  `_gameRunning=true`(생존) 클라에서만 돈다. **사망한 클라는 GameOver에서 `_gameRunning=false`라
  Update가 일찍 return → GameWin을 실행하지 못하고**, 따라서 `LoadingSceneController.NextSceneName`
  /`AllClientsLoad`를 설정하지 못한다. 그 상태로 `AutomaticallySyncScene`(생존자/마스터의 LoadLevel)에
  끌려 로딩 씬에 들어오면, `NextSceneName=null` → Awake가 **게임 씬으로 폴백**(`toGamePanel`/조작팁
  활성) → 결과용 로딩 패널(`toMainOrResultPanel`)이 안 떠 잘못된/빈 화면(회색)이 된다.
  (Push는 `RPC_PushModeGameEnd`(RpcTarget.All)로 사망자도 종료 시퀀스를 실행해 이 문제가 없다.)
- 수정: Absorb 사망(관전 전환) 시점(GameOver:570 분기)에 `NextSceneName=RESULT_SCENE_NAME_ABSORB`,
  `AllClientsLoad=true`를 **미리 지정**. 이후 씬 동기화로 로딩 씬에 들어와도 결과 타겟·결과 패널로
  올바르게 진입하고 결과 씬까지 따라간다.
- 학습: "특정 조건(생존)에서만 도는 코드"가 전역 전환 설정을 책임지면, 그 조건을 못 만족하는 경로
  (사망)는 설정 누락으로 깨진다. 모드/생존 여부와 무관하게 모두 거치는 전환은 그 설정도 모든 경로에서
  보장돼야 한다(H2 '마스터 승계'·BUG-C 'Push 전용 처리'와 같은 주제).

> ※ BUG-E는 빠른 이동 재현이 필요(맵에서 대쉬로 가로지르며 확인). BUG-F는 흡수 모드에서 일찍 흡수당해
>   관전하다 타임오버를 맞는 시나리오로 확인. 둘 다 네트워크/타이밍 의존이라 인게임 검증 권장.

---

## 2026-06-20 버그 제보 수정 (3차 — Push 사망 관전 시 결과 로딩 회색)

> 사용자 추가 제보: "푸쉬모드인데 죽고 관전 중 결과로 넘어갈 때 로딩 UI가 잠깐 떴다가 사라지고
> 유니티 기본 빈 배경(회색)이 나온다." → BUG-F(흡수, NextSceneName 미설정)와는 다른 Push 경로.

### [BUG-G] (Push·시퀀스 / 중) 사망 관전 → 결과 전환 시 로딩 커튼이 결과 씬보다 먼저 사라져 회색 공백
- 위치: `LoadingSceneController.Update`(로드 트리거가 `_elapsed >= minDisplayTime`), `LoadingCenterMultiAni`
  (OnEnable에서 고정 타임라인 fade-in→hold(holdSeconds)→**fade-out**), `LoadResultSceneAfterSync`+
  `AllClientsLoad`/`AutomaticallySyncScene`
- 원인: 결과 씬 **로드를 `minDisplayTime`(2s)이 지나서야 *시작***하는데, 로딩 커튼(center)은 그와
  무관하게 **고정 타임라인으로 fade-out**한다. Push 사망(비마스터) 클라는 `AllClientsLoad`+씬 동기화
  상호작용으로 결과 씬 로드가 더 지연돼, **커튼이 fade-out으로 사라진 뒤에도 결과 씬이 아직 로드되지
  않아** 그 사이 유니티 기본 빈 배경(회색)이 보였다. 결과 씬 데이터(룸 프로퍼티)·카메라는 정상이라
  '영구 빈 화면'이 아니라 '로드 전 공백'이 핵심.
  (※ Push 사망자는 BUG-F와 달리 `RPC_PushModeGameEnd`(All)로 NextSceneName은 정상 설정됨 — 별개 원인.)
- 수정: `LoadingSceneController`가 **결과/메인(게임 퇴장) 전환은 로딩 씬 진입 즉시 타겟을 로드**하도록
  변경(`_enteringGame ? minDisplayTime : 0f`). 커튼(DontDestroyOnLoad)은 그대로 `minDisplayTime`+타겟
  로드 완료까지 유지되므로(ExitRoutine 조건 불변), 결과 씬이 커튼 뒤에서 미리 로드·렌더된다 → 커튼이
  슬라이드/페이드로 사라질 때 회색이 아닌 **이미 준비된 결과 씬**이 드러난다. 게임 입장(카운트다운)
  전환은 기존대로 minDisplayTime 뒤 로드(일찍 로드하면 3-2-1이 커튼 뒤에서 시작돼 일부를 놓침).
- 학습: 로딩 '커튼'은 다음 씬이 준비될 때까지 화면을 덮는 게 목적이다. 커튼의 사라짐(애니)이 다음 씬의
  '준비 완료'와 분리돼 고정 타이머로 돌면, 로드가 늦은 경로(사망 비마스터)에서 커튼만 먼저 걷혀 빈
  화면이 샌다. 다음 씬을 **커튼 뒤에서 미리 로드**해 두면 커튼이 무엇을 하든 드러나는 건 준비된 씬이다.

> ※ 네트워크/씬 타이밍 의존이라 인게임 검증 권장(흡수·Push 양쪽에서 사망 관전→결과 확인).
>   추가로 로딩 패널 `holdSeconds`가 `minDisplayTime`보다 충분히 길어야 커튼이 너무 일찍 옅어지지
>   않는다(씬 인스펙터 값 점검 권장).

---

## 2026-06-23 루틴 — 신규 커밋 없음 / 게임 종료→결과 전환 시퀀스 신규 심층 리뷰 (M1~M4 도출)

마지막 코드 변경은 06-20(`4f2fbeb`)이고 그 이후 신규 커밋이 없다(전 브랜치 확인, IO=origin 동기).
06-12·06-18 루틴과 동일하게 "신규 커밋 없을 때는 미리뷰 영역을 새로 심층 분석" 방식으로 진행했다.
이번엔 시퀀스의 **맨 끝 — 게임 종료 → 결과 씬 데이터 전달/표시** 구간에서 아직 한 번도 항목이
도출되지 않은 스크립트(`GameResultManager`/`ScoreboardSnapshot`은 H3~I1로 일부만 다룸,
`ResultDataCarrier`·`NextSceneManager`·`ResultStarsUI`·`LeaderboardEntry`는 미리뷰)를 골라 분석했다.

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3). 즉시 수정할 명백한 버그는
> 없었다. M2는 잠재 버그 성격이라 우선순위를 높였지만, 표시 로직 회귀 우려가 있어 승인 후 적용을 권한다.

### [M2] (버그가능성·아키텍처 / 중) 결과 시상대가 *인스턴스*가 아닌 *공유 프리팹*의 batPivot을 비활성화 — 다음 매치로 누수 가능
- 위치: `GameResultManager.SpawnPodium:218-233`
- 내용: Absorb 모드에서 시상대 젤리의 박쥐(공격 비주얼)를 숨기려고
  ```csharp
  PlayerMovement playerMovement = prefab.GetComponent<PlayerMovement>();
  if (playerMovement != null) playerMovement.batPivot.gameObject.SetActive(false);
  ```
  를 호출하는데, 대상이 인스턴스(`go`)가 아니라 **`prefab`**(= `playerJellyPrefab`/`botJellyPrefab`,
  `Resources.Load` 또는 인스펙터로 잡은 **공유 에셋**)이다. `Instantiate` *전에* 프리팹 자식의
  활성 상태를 끄므로 그 뒤 만들어지는 인스턴스엔 반영되지만, **변경이 프리팹 에셋(메모리 캐시)에
  남는다.** `Resources.Load`가 돌려주는 건 캐시된 단일 인스턴스라, 결과 씬 이후 **재시작 → 다음 게임
  씬에서 같은 프리팹을 `PhotonNetwork.Instantiate`로 다시 찍으면 박쥐가 꺼진 채 생성**될 수 있다
  (에디터 플레이 모드에선 에셋을 dirty 처리할 수도 있음). 같은 결과 씬 안에서는 "모두 꺼짐"이 의도와
  같아 증상이 안 보이지만, **세션을 넘겨 한 번 더 흡수 매치를 하면** 박쥐 없는 플레이어가 나올 수 있다.
- 부가: 루프(`for i`) 안에서 매 항목마다 같은 프리팹에 반복 호출 — 중복.
- 제안: 끄는 대상을 `go`(인스턴스)로 바꾼다. 단 `StripNetworkingAndGameplay`가 인스턴스의
  `PlayerMovement`/`AIPlayerMovement`를 이미 `DestroyImmediate`하므로 그 컴포넌트의 `batPivot`
  참조로는 못 찾는다 → **Strip 호출 *전에* go에서 batPivot를 캐시**해 끄거나, 인스턴스 계층에서
  이름/경로(예: `go.transform.Find(".../BatPivot")`)로 찾아 비활성화한다. 공유 에셋은 절대 건드리지 않는다.
- 학습: **"프리팹(템플릿)"과 "인스턴스(사본)"는 다른 객체다.** 표시용으로 잠깐 바꿀 상태는 반드시
  Instantiate 뒤의 *사본*에 적용해야 한다. 템플릿을 바꾸면 그 변경이 캐시에 눌어붙어, 의도치 않은
  다음 사용처(다음 매치·다른 씬)로 새어 나간다. (M2는 G3/G4/K1 'sharedMaterial 오염'과 같은 결의
  문제 — 공유 자원에 쓰면 안 되는 곳에 썼다. 머티리얼이 아니라 프리팹 계층판.)

### [M1] (아키텍처·데드코드 / 하) `ResultDataCarrier`/`RankingData` 전체 미사용 — 결과 데이터 전달 경로의 구버전 잔재
- 위치: `Assets/Scripts/Result/ResultDataCarrier.cs`(static `TopRankings`), `struct RankingData`
- 내용: 결과 씬은 실제로 **룸/플레이어 커스텀 프로퍼티**를 `ScoreboardSnapshot.Collect`로 읽어
  Top3를 구성한다(H6 단일화). `ResultDataCarrier.TopRankings`/`RankingData`는 코드 전체에서
  **쓰지도 읽지도 않는다**(grep 0건). 결과 데이터를 정적 리스트로 씬 간 넘기려던 *옛 설계*의 잔재로,
  현재는 네트워크 권위(룸 프로퍼티) 방식으로 대체됐다. 남아 있으면 "결과 데이터가 어디서 오나"를
  읽는 사람이 두 경로(죽은 캐리어 vs 실제 룸프롭)로 오해한다.
- 제안: `ResultDataCarrier.cs` 삭제(또는 실제로 쓸 계획이면 ScoreboardSnapshot로 일원화). H6에서
  '수집 로직 단일화'를 했듯, **데이터 전달 경로도 하나만 남긴다.**
- 학습: 설계가 A(정적 캐리어)→B(네트워크 권위)로 옮겨갔으면 A는 지워야 한다. 죽은 경로를 남기면
  다음 사람이 그게 살아 있는 줄 알고 거기에 코드를 얹다가 "왜 결과에 안 뜨지?"로 시간을 버린다.
  (H5 '도달 불가능 분기 삭제'·K3/K4 '레거시 사용 여부 확인'과 같은 주제 — 죽은 코드는 빚이다.)

### [M3] (코드품질 / 하) `GameResultManager.GetRankString`이 조회 중에 직렬화 필드 `firstPlaceText`를 영구 덮어씀(부수효과)
- 위치: `GameResultManager.GetRankString:472-486`
  ```csharp
  if (GameState.CurrentGameMode == GameModeType.Push) firstPlaceText = "우승!";
  ```
- 내용: 순위 문자열을 *돌려주기만* 해야 할 함수가 호출 때마다 인스펙터 직렬화 필드 `firstPlaceText`를
  "우승!"으로 **변경**한다. 멱등(항상 같은 값)이고 결과 씬 인스턴스는 곧 파괴되므로 실해는 낮지만,
  '조회(getter) 함수가 상태를 바꾸는' 안티패턴이라 디버깅 시 값의 출처를 흐린다(인스펙터엔 "1위"인데
  런타임엔 "우승!"). 모드 판정도 매 순위 출력마다 반복된다.
- 제안: 필드를 건드리지 말고 로컬로 처리 —
  `string first = (GameState.CurrentGameMode == GameModeType.Push) ? "우승!" : firstPlaceText;`
  후 그 로컬을 반환. (필요하면 Push 분기를 Start에서 한 번만 결정)
- 학습: 이름이 `Get…`/조회인 함수는 **읽기만** 해야 한다. 부수효과를 숨기면 "왜 이 값이 바뀌었지"를
  추적할 때 의외의 곳을 의심하게 된다.

### [M4] (코드품질·문서화 / 하) `NextSceneManager.cs`·`ResultStarsUI.cs` 한글 주석 인코딩 깨짐(mojibake)
- 위치: `NextSceneManager.cs:7,12`(`"1. �Ѿ�� �ε� UI�� ã���ϴ�."` 등), `ResultStarsUI.cs:8,9,11,14,16,23,28,40,48`
- 내용: 한글 주석이 전부 깨져(EUC-KR로 저장된 파일을 UTF-8로 디코딩한 흔적) 의미 파악 불가.
  로직엔 영향 없지만 학습/유지보수에서 주석이 0 가치가 된다(`.editorconfig`/`.gitattributes`가 있는
  프로젝트라 인코딩 일관성도 깨진 상태).
- 부가 관찰(같은 파일): `NextSceneManager`는 `FindAnyObjectByType<LoadingBGSlideAni>()`를 찾아
  `transform.root`를 **하드코딩 1.0s 뒤** Destroy한다. 로딩 애니 길이가 바뀌면 이 매직넘버가
  어긋날 수 있음(J7 'LoadingCenterMultiAni 부모 조회'와 같은 로딩 정리 타이밍 주제). 우선순위 하·관찰.
- 제안: 두 파일을 UTF-8(BOM 없음)로 주석 재작성. 빌드/동작 변화 없음 → 저위험 정리.

> ※ M1·M3·M4는 동작 변화가 거의 없는 정리이고, M2가 유일하게 실사용 동작(결과 박쥐 표시)을 건드린다.
>   M2 적용 시 흡수 모드 결과 씬에서 박쥐가 여전히 숨겨지는지, Push 모드(박쥐 표시 유지)와 다음 매치
>   시작 시 박쥐가 정상인지 **인게임 확인 권장**.
> ※ ResultDataCarrier(M1)·ResultStarsUI/ClearJudge/GameTimer 묶음은 K3/K4에서 '레거시 사용 여부 확인'으로
>   남긴 단일 스테이지(클리어 별점) 경로와 연결된다 — 그 경로의 존폐를 정할 때 M1도 함께 정리하면 좋다.

---

## 적용 상태
- [x] F1  (2026-06-04 적용) — LoadingSceneController 기본 씬을 GameState.CurrentGameMode에서 파생
- [x] F2  (2026-06-04 적용) — NetworkManager 씬 결정을 GameState.CurrentGameMode 기준으로 통일
- [x] F3  (**사용자** 직접 적용, 2026-06-13 확인) — OnStartButtonClicked 길이 검사가
        하드코딩 10 대신 `nicknameMaxLength` 필드 사용(LobbyController.cs:230).
- [x] F4  (**사용자** 직접 적용, 2026-06-13 확인, 커밋 5f08a78) — buttonSelectionPanel.SetActive
        호출 2곳을 `?.SetActive(true)` null-조건 가드로 변경(LobbyController.cs:238, 380).
- [x] F5  (**사용자** 직접 적용, 2026-06-13 확인) — OnCancelMatchingClicked(322-388)가 매칭
        취소 시 모드 선택 화면 복귀 + 입력 패널 복원 + startButton.interactable=true 복원
        → 취소/뒤로가기 경로 제공으로 F5 의도 충족.
- [x] G1  (2026-06-09 적용) — ResetScale에서 _jellyBatchCoroutine = null 초기화
- [x] G2  (2026-06-09 적용) — 봇 넉백 RPC를 RpcTarget.All → 봇 소유자(마스터)로 변경 (3곳)
        ※ 봇 프리팹에 PhotonTransformView 존재 확인 → 비마스터 로컬 이동/동기화 충돌 제거
- [x] G3  (2026-06-17 적용) — FallingTile: drawOverlapGizmo 기본값 false + Debug.Log를 #if UNITY_EDITOR로
        가드(빌드 로그 스파이크 제거), 경고 색 변경을 .material→MaterialPropertyBlock(+읽기 sharedMaterial)으로
        전환해 붕괴 타일별 머티리얼 인스턴스 복제/배칭 깨짐 제거(FallingTile.cs).
- [x] G4  (2026-06-17 적용) — OffScreenPlayerIndicator.GetColor 봇 색 조회를 .material→sharedMaterial로
        변경(읽기 전용 인스턴스 복제 제거). 리더보드 GameModeManager.GetBotColor와 동일 패턴으로 정합.
- [ ] G5  (대기)
- [x] G6  (2026-06-17 적용) — 탈락/흡수 판정을 IsOutOfPlay 단일 헬퍼로 통일:
        AIPlayerMovement.IsOutOfPlay(=IsEliminated||IsBeingAbsorbed), NetworkPlayerSync.IsOutOfPlay
        (=IsAbsorbed || owner "Eliminated" 룸프롭, ELIMINATED_KEY 상수화). 인디케이터(사람/봇)·FallingTile·
        NetworkPlayerSync 봇 콤보 호출부가 모두 이 값을 사용. ※ 결과 씬 ScoreboardSnapshot은 오브젝트
        파괴 후 룸프롭 생존자 목록(I1/H6) 기반이라 의도적으로 별개 유지.
- [x] G7  (2026-06-17 적용) — TileCollapseManager가 NavCarve_* 오브젝트를 _carveObjects 목록으로 소유,
        RegisterCarveObject/ClearCarveObjects 추가 + OnDestroy에서 명시적 정리. FallingTile.CarveNavMesh가
        생성 즉시 매니저에 등록(FallingTile.cs, TileCollapseManager.cs).
- [ ] G8  (대기)
- [x] H1  (2026-06-12 적용, 커밋 fbcd419) — GameState.ResetValues에서 이벤트 null 대입 제거,
        정리는 Reset()(SubsystemRegistration)에만 유지. 코드 확인됨(GameState.cs:96-110).
- [x] H2  (2026-06-12 적용, 커밋 fbcd419) — NetworkManager.OnMasterClientSwitched 추가 →
        새 마스터가 CheckAndStartCountdown 재호출(NetworkManager.cs:305-324). 코드 확인됨.
- [x] H3  (2026-06-12 적용, **사용자** 커밋 fec8329) — PushModeEndSequence StopWalking에 ?. 가드.
- [ ] H4  (대기 — 닉네임 문자열 식별. UpdateLeaderboard:573 등 여전히 NickName 비교)
- [x] H5  (2026-06-12 적용, **사용자** 커밋 fec8329) — GameOver 두 번째(도달 불가능) Push 분기 삭제.
- [x] H6  (2026-06-12 적용, 커밋 fbcd419) — ScoreboardSnapshot.cs로 점수 집계 단일화. 코드 확인됨.
- [ ] I1  (대기 — 2026-06-13 도출, 결과 씬 봇 생존 판정의 프로퍼티 정리 레이스)
- [x] J1  (2026-06-16 적용) — 젤리 스폰 후보 오프셋 baseY+5f → +1f(반경 3f 안에 들도록). SamplePosition
        maxDistance(3D 거리) < 수직 오프셋이면 평지에서 스폰 전부 실패하던 문제 해소(NetworkJellyManager.cs:202).
- [x] J2  (2026-06-16 적용) — Milk를 PhotonView 소유권(사람=본인/봇=마스터) 기준으로 통일, Exit도 추적된
        대상만 대칭 복원 → 원격 사본 moveSpeed 비대칭 증식 제거(Milk.cs).
- [x] J3  (2026-06-16 적용) — Milk가 밀크별 _slowed 딕셔너리로 중복 적용 방지 + OnDisable에서 잔여 대상
        복원 → moveSpeed 파괴적 누적/영구 손상 제거(Milk.cs). ※ moveSpeed는 곱셈 합성 유지(겹친 밀크 정합).
- [ ] J4  (대기 — 2026-06-16 도출, Milk 스케일 감소/리스폰 제거 — **설계 의도 확인**, 미승인)
- [x] J5  (2026-06-16 적용) — GameModeManager Awake/OnDestroy에서 CountdownActive·PlayerMovement.InputLocked
        해제 → 카운트다운 도중 씬 전환 시 입력 영구 잠금 방지(GameModeManager.cs:86, 89).
- [ ] J6  (대기 — 2026-06-16 도출, StartGameInternal 비멱등)
- [ ] J7  (대기 — 2026-06-16 도출, LoadingCenterMultiAni 부모 조회 null 미가드)
- [x] K1  (2026-06-18 적용) — TileCollapseManager.DarkenStepTile을 sharedMaterial 읽기 + MaterialPropertyBlock
        쓰기로 전환(셰이더 프로퍼티 _BaseColor/_Color 자동 선택). Push 타일 어둡게 처리가 타일마다
        머티리얼 인스턴스를 복제하던 배칭 깨짐/메모리 증가 제거(FallingTile G3와 동일 패턴, 누락 경로 보강).
- [x] K2  (2026-06-18 적용) — Push 스텝붕괴(TileCollapseManager.UpdateStepCollapse:212)/봇 타겟팅
        (AIPushSurviveState.FindNearestTarget:166)의 raw "Eliminated" 룸프롭 직접 조회를 player.IsOutOfPlay로
        교체. G6 탈락판정 단일화 범위를 두 누락 경로까지 확장(흡수 직후 _isAbsorbed도 함께 판정 → 더 정확).
- [ ] K3  (대기 — 2026-06-18 도출, ClearJudge 매 프레임 Debug.Log + 클리어 로직 주석처리(레거시) — 사용 여부 확인)
- [ ] K4  (대기 — 2026-06-18 도출, GameTimer.GameFail timeScale=0 후 널가드 없는 호출 소프트프리즈(레거시) — 사용 여부 확인)
- [ ] L1  (대기 — 2026-06-20 도출, 젤리 흡수 점수/성장 로컬 무검증 — 경합 시 중복 흡수 double-eat, **확인 필요**)
- [ ] L2  (대기 — 2026-06-20 도출, JellyColliderAbsorb null 미가드 — NRE 시 흡수 젤리 미파괴 소프트 누수)
- [ ] M1  (대기 — 2026-06-23 도출, ResultDataCarrier/RankingData 전체 미사용 데드코드 — 결과 전달 구버전 잔재)
- [ ] M2  (대기 — 2026-06-23 도출, GameResultManager.SpawnPodium이 인스턴스 아닌 공유 프리팹 batPivot 비활성화 — 다음 매치 누수 가능, **잠재 버그**)
- [ ] M3  (대기 — 2026-06-23 도출, GameResultManager.GetRankString 조회 중 직렬화 필드 firstPlaceText 부수효과 덮어쓰기)
- [ ] M4  (대기 — 2026-06-23 도출, NextSceneManager/ResultStarsUI 한글 주석 인코딩 깨짐 mojibake)

> ※ 위 H1·H2·H6은 06-12 fix 커밋(fbcd419)에서 적용됐으나 당시 이 표가 갱신되지 않아
> 06-13 루틴에서 코드 대조 후 정합화함. H3·H5는 사용자가 직접 적용한 것을 06-13 루틴이 확인.

## 환경 메모
- 원격 컨테이너는 매 세션 새로 클론되므로 `~/.config/gsheet/credentials.json` 와 `gspread`가
  매번 없어진다. 2026-06-04 루틴에서는 사용자가 credentials를 업로드해주어 수동 설치
  (`pip install gspread google-auth cffi cryptography`) 후 시트 기록을 정상 수행함.
- 영구 자동화하려면 환경 SessionStart 훅/시작 스크립트에 위 설치 + credentials 주입을 넣어야 함.
- 2026-06-12 루틴: credentials/gspread 부재로 시트 기록 보류. H1~H6 승인 시 plan/bug 기록과
  함께 일괄 반영 필요(기록 대기 항목: "2026-06-12 코드리뷰 — H1~H6 도출").
- 2026-06-13 루틴: SessionStart 훅은 실행됨(CLAUDE_CODE_REMOTE=true)이나 **환경 변수
  `GSHEET_CREDENTIALS_JSON`이 미등록(빈 값)** 이라 credentials.json이 주입되지 않음 → 시트 기록
  또 보류. **사용자 조치 필요**: claude.ai/code 환경 설정에서 서비스 계정 JSON 전체를
  `GSHEET_CREDENTIALS_JSON`으로 1회 등록하면 이후 자동화됨(훅이 두 경로에 써줌).
  추가로 cffi 네이티브(_cffi_backend) 빌드 의존성 때문에 gspread 임포트가 실패할 수 있어,
  훅의 pip 설치 목록에 cffi/cryptography가 포함돼 있으나 네이티브 빌드 환경이 없으면 실패 가능.
  시트 기록 대기 항목(누적): "2026-06-12 H1~H6 도출/적용", "2026-06-13 H3·H5 사용자 적용 검증 + I1 도출",
  "2026-06-13 F3·F4·F5 사용자 직접 적용 확인/완료 처리",
  "2026-06-13 feat 로딩 씬 모드별 조작 팁 패널 추가(LoadingSceneController) — 사용자 요청".
- 2026-06-16 루틴: **환경변수 `GSHEET_CREDENTIALS_JSON`이 드디어 등록됨**(len≈2348). 다만 컨테이너에
  gspread 미설치 + cffi 네이티브(`_cffi_backend`) 부재로 임포트 실패 → `pip install --only-binary :all:
  --force-reinstall cffi cryptography`로 cffi 바이너리 휠 설치 후 정상화(cryptography는 debian판이라
  uninstall 실패하나 cffi 백엔드만 채워지면 import 됨). 이후 `update_sheets.py status` 정상 동작 확인.
  → SessionStart 훅의 pip 목록에 `--only-binary :all:` 옵션을 cffi에 적용하면 매 세션 자동화 가능.
- 2026-06-17 루틴: `tools/update_sheets.py`가 **현재 시트 컬럼과 불일치**해 데이터 오정렬을 유발함.
  실제 시트 규약(헤더 기준): 트러블슈팅 = `카테고리|심각도|날짜|이슈명|원인|해결방법|관련파일`,
  개발계획서 = `카테고리|작업명|세부내용|우선순위|난이도|예상(일)|상태|시작 날짜|종료 날짜|메모`(종료 날짜 컬럼
  추가됨). 구 스크립트는 bug 인자순서가 `날짜 심각도 카테고리…`였고 plan은 종료 날짜 컬럼이 없어 메모가
  한 칸 밀렸음. → 스크립트 인자순서/플랜 10필드로 수정 완료. (이번에 잘못 들어간 #67~70, plan #22는 API로 교정함)
- 2026-06-23 루틴: 새 컨테이너에 gspread/credentials 둘 다 없었으나, env `GSHEET_CREDENTIALS_JSON`(len=2347)
  존재 → `credentials.json` 주입 + `pip install --user gspread google-auth` 후 cryptography pyo3 panic 발생,
  06-16 메모대로 `pip install --user --only-binary :all: --force-reinstall cffi cryptography`로 정상화.
  `update_sheets.py status` 정상(개발계획서 루틴 27/26완료, 트러블슈팅 78). M1~M4 도출 기록 plan으로 반영.
