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

### [M2] (버그 / 중) **[2026-06-23 적용]** 결과 시상대가 *인스턴스*가 아닌 *공유 프리팹*의 batPivot을 비활성화 — 다음 매치로 누수 가능
- 위치: `GameResultManager.SpawnPodium:218-233`
- ※ 용어 정정: `batPivot`은 박쥐가 아니라 **방망이(배트 무기)**다. 규칙은 **밀치기 모드=배트 표시 /
  흡수 모드=배트 숨김**이며, 기존 `if (Absorb)` 조건 자체는 올바랐다. 문제는 *대상*이 인스턴스가
  아니라 공유 프리팹이었던 것뿐.
- 내용: Absorb 모드에서 시상대 젤리의 방망이(배트)를 숨기려고
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
- 적용(2026-06-23): SpawnPodium의 프리팹 변경 블록을 제거하고, `InstantiateDisplayOnly` 안에서
  **인스턴스 `go`** 에 대해 Strip *전에* `HideBat(go)`를 호출하도록 변경. `HideBat`은
  `go.GetComponent<PlayerMovement>()`/`AIPlayerMovement`의 `batPivot`을 null 가드 후 비활성화한다
  (인게임 `AIPlayerMovement`의 batPivot null 가드 관례와 동일). 흡수 모드에서만 호출되므로
  밀치기 모드는 배트 표시가 그대로 유지되고, 공유 프리팹 에셋은 더 이상 건드리지 않는다.
  ※ 인스턴스는 비활성 상태로 생성돼 Awake 미실행이지만 `batPivot`은 직렬화 참조라 유효하고,
    batPivot 자신의 activeSelf=false가 이후 `go.SetActive(true)` 후에도 숨김을 유지한다.
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

## 2026-06-25 루틴 — 06-24 M2 적용 검증 + 플레이어 FSM(상태머신) 신규 심층 리뷰 (N1~N6 도출)

마지막 코드 변경은 06-24(`5ab9656` = M2 적용)이고, 전날 대비 변경이 있으므로 루틴을 진행했다(IO=origin 동기).
이번엔 아직 한 번도 항목이 나오지 않은 **플레이어 FSM(상태머신)** — 인게임 조작의 매 프레임 시퀀스 —
를 골라 분석했다. 대상: `PlayerMovement`(FSM 컨텍스트) + `PlayerBaseState`/`Idle`/`Move`/`Dash`/
`Jump`/`Attack`/`Knockback` 7개 상태. 네트워크 정합성은 `NetworkPlayerSync`(원격 사본 처리·배트 히트
권위)까지 교차 확인했다.

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3). 즉시 고칠 명백한 동작 버그는
> 없었다. N1(로그 스파이크)이 가장 효과 크고 저위험이라 우선 적용을 권한다.

### 06-24 커밋(`5ab9656`) 검증 — M2 회귀 없음
- `GameResultManager.InstantiateDisplayOnly:235-252` 확인: 인스턴스는 `prefab.SetActive(false)` 후
  `Instantiate`로 **비활성 생성** → `HideBat(go)`(흡수 모드만, null 가드) → `StripNetworkingAndGameplay(go)`
  순서. 공유 프리팹(`playerJellyPrefab`/`botJellyPrefab`)은 더 이상 변형되지 않는다(블록 제거 확인).
- `batPivot.gameObject.SetActive(false)`는 *자식의 자기 활성 상태*라, 이후 라인 224의 `go.SetActive(true)`
  (부모 활성화) 뒤에도 배트는 숨김 유지된다 — 의도대로다. Push 모드는 `HideBat` 미호출이라 배트 표시 유지.
- 결론: **회귀 없음.** (인게임에서 "흡수 결과=배트 숨김 / 다음 매치 정상 / Push=배트 표시"만 눈으로 확인 권장.)

### 좋았던 점(설계 관찰)
- **원격 사본 입력 차단이 확실하다.** `NetworkPlayerSync.SetupRemotePlayer:175`에서 `playerController.enabled
  = false` → 원격 플레이어 사본의 `PlayerMovement.Update`가 안 돌아 로컬 Input을 읽지 않는다. FSM이 원격에서
  제멋대로 움직이는 사고가 구조적으로 막혀 있다(좋음).
- **배트 히트가 클라 감지 → 마스터 권위 구조다.** `PlayerAttackState.DetectBatHit`가 IsMine에서만 판정 후
  `RPC_RequestBatHitPlayer/Bot`를 MasterClient로 보내고, 마스터가 phase·봇 `IsOutOfPlay`(G6)를 재검증한 뒤
  넉백/성장을 권위 적용한다. 입력잠금(InputLocked)·쿨타임도 `CanDash()`/`CanAttack()`에 모여 있어 일관적.
- `ChangeState`에 `if (currentState == newState) return;` 자기전이 가드, 각 상태의 분기마다 `return`으로
  프레임 내 중복 전이 방지 — FSM 기본기는 갖춰져 있다.

### [N1] (성능·로그 / 중) FSM 상태 전환마다 `Debug.Log` — 빌드 로그 스파이크 (G3/K3가 못 덮은 경로)
- 위치: `PlayerMovement.ChangeState:166` (`Debug.Log($"[Player] 상태 변경 완료");`),
  `PlayerIdleState.Enter:13` (`Debug.Log("...")`, 게다가 mojibake).
- 내용: `ChangeState`는 **모든 상태 전환마다** 호출된다. Idle↔Move는 플레이어가 멈췄다/움직였다 할 때마다
  바뀌므로 조작 중 초당 수~수십 회 로그가 찍힌다(IdleState.Enter도 Idle 진입마다 추가). 문자열 보간까지
  매번 수행하고, 메시지에 *어느 상태로* 바뀌었는지도 없어 디버그 가치도 낮다. **이미 G3(FallingTile)·
  K3(ClearJudge)에서 "빌드 매 프레임 Debug.Log = 후반 프레임 스파이크"로 확정된 것과 같은 결의 문제**인데,
  플레이어 FSM 경로는 그 정리에서 누락됐다.
- 제안: 두 로그를 제거하거나 `#if UNITY_EDITOR`로 감싼다. 남길 거면 최소한 전이 대상을 찍게
  (`$"[Player] {currentState} → {newState}"`) 바꾸고 에디터 전용으로 가드.
- 학습: 상태머신의 전이 로그는 개발 중엔 유용하지만 **고빈도 경로(매 프레임/매 전이)에 무가드 로그를
  남기면 출시 빌드에서 GC·IO 비용이 누적**된다. "로그는 빌드에서 빠지게" 가드를 거는 게 이 프로젝트의
  확립된 관례(G3/K3)다 — FSM도 같은 관례를 따르면 된다.

### [N2] (아키텍처 / 중하) 액션 입력 폴링이 상태마다 복붙 — 입력 캐싱 일원화 안 됨 + `CanJump()` 부재
- 위치: `PlayerIdleState.Update:23-39`, `PlayerMoveState.Update:21-37` (동일한 공격/대쉬/점프 3분기 중복),
  대조: `PlayerMovement.Update:107-119`는 *이동축*(`inputH/inputV`)만 프레임당 1회 캐싱한다.
- 내용: 이동축은 한 곳에서 캐싱·InputLocked 처리하는데, **액션 입력(마우스/Shift/Space)은 각 상태가
  직접 `Input.GetMouseButtonDown`/`GetKeyDown`을 또 읽는다.** 같은 3분기가 Idle·Move에 그대로 복붙돼 있어
  새 상태(예: 공격 가능한 다른 상태)를 늘릴 때 누락되기 쉽다. 특히 **점프는 `CanDash()`/`CanAttack()`
  같은 헬퍼가 없어** 매 상태에서 `Input.GetKeyDown(Space) && player.isGrounded && !PlayerMovement.InputLocked`를
  손으로 조합한다 — 조건 하나만 빠뜨려도 한 상태에서만 점프가 깨지는 류의 버그가 난다(현재는 두 곳 다
  맞게 적혀 있지만 *구조적으로* 취약).
- 제안: (a) `PlayerMovement.Update`에서 액션 입력도 캐싱(예: `jumpPressed`/`dashPressed`/`attackPressed`를
  프레임당 1회, InputLocked면 false). (b) `CanJump()` 헬퍼 추가(`!InputLocked && isGrounded && 상태제약`).
  (c) 공통 전이 분기(공격→대쉬→점프 순)를 `PlayerBaseState`의 protected 헬퍼(예: `TryHandleActionInputs()`)로
  올려 Idle/Move가 호출만 하게. → 중복 제거 + 입력 정책 단일 출처.
- 학습: **"입력을 어디서 읽는가"는 한 곳으로 모으는 게 정석**이다(프레임당 1회, 잠금 처리도 그 한 곳).
  지금은 이동축만 모였고 액션은 흩어져 있어 *반쪽짜리 캐싱*이다. 분기 로직을 베이스/헬퍼로 올리면
  "상태 추가 시 입력 처리 누락" 부류의 버그를 구조적으로 차단한다(F2·G6·H6과 같은 '출처 일원화' 테마).

### [N3] (안정성 / 하) `PlayerMovement.OnFailAnimationFinished`의 `uiManager` null 미가드
- 위치: `PlayerMovement.OnFailAnimationFinished:220-223` (`uiManager.SetState(UIState.GameOver);`).
- 내용: 사망 애니메이션 이벤트에서 호출되는 콜백인데 `uiManager`(인스펙터 직렬화 필드)가 미할당이면 NRE.
  게임 종료 직전 시퀀스라 여기서 예외가 나면 GameOver UI 전환이 끊긴다(H3 'PushModeEndSequence 첫 줄 NRE로
  결과 전환 소프트락'과 같은 종료 시퀀스 NRE 테마). 실발생 확률은 낮지만(보통 할당돼 있음) 종료 경로라 가드 권장.
- 제안: `uiManager?.SetState(UIState.GameOver);` 또는 시작 시 null 검증 후 경고 로그. J7/G8/L2와 동일한
  '종료/콜백 경로 null 가드' 정리.

### [N4] (네트워크·아키텍처 / 하·관찰) 마스터 배트 히트 검증이 **사거리/각도 미재검증** — 공격자 로컬 판정 신뢰 (L1 테마)
- 위치: `NetworkPlayerSync.RPC_RequestBatHitPlayer:797-822`, `RPC_RequestBatHitBot:824-851`.
- 내용: 마스터는 `phase==Playing`과 봇 `IsOutOfPlay`만 재검증하고, **피격자가 실제 배트 사거리·아크 안에
  있었는지는 재확인하지 않는다.** 히트 판정(`OverlapSphere`+각도)은 전적으로 공격자 로컬(`DetectBatHit`)
  결과를 신뢰한다. 방향/세기는 마스터가 *현재 위치*로 다시 계산하므로 그 부분은 권위적이나, "맞았다" 자체는
  클라 권위다. → (1) 치팅 클라가 임의 ViewID로 RPC를 쏘면 무조건 넉백+성장 보상 (.io 친선전이라 우선순위 낮음),
  (2) 비악의 지연 상황에서도 공격자 화면 기준으론 맞았는데 실제론 빗나간 스윙이 보상될 수 있다.
  **L1(흡수 점수/성장 로컬 무검증, double-eat)과 정확히 같은 '권위 재검증 누락' 테마**다.
- 제안: 마스터에서 `attacker.position`↔`victim.position` 거리와 정면 각도를 `batRange*scale`/`batArcAngle`로
  한 번 더 검증(여유 마진 포함)한 뒤 적용. L1과 함께 "권위 측 거리 재검증"으로 묶어 처리하면 정책이 일관됨.
- 학습: **클라이언트가 보내는 "맞았다/먹었다"는 힌트일 뿐, 권위(마스터)가 자기 좌표로 재검증해야 한다.**
  지금은 *효과(방향·세기·성장량)*는 권위적인데 *판정(맞았는지)*만 클라 신뢰라 한 단계가 비어 있다.

### [N5] (코드품질 / 하·관찰) 인스펙터 `jumpForce` 필드가 `Start`에서 항상 `originalJumpForce`로 덮어써짐
- 위치: `PlayerMovement.Start:93` (`jumpForce = originalJumpForce;`), 필드 `jumpForce=7.5`/`originalJumpForce=10`.
- 내용: 동작은 **정상**이다 — `jumpForce`는 런타임에 `PlayerBridge:113-115`가 스케일 임계치를 넘으면
  `originalJumpForce + 증가량`, 아니면 `originalJumpForce`로 매번 다시 설정한다(스케일 커지면 더 높이 점프).
  다만 `Start`가 인스펙터의 `jumpForce`(7.5) 초기값을 즉시 `originalJumpForce`(10)로 덮으므로 **인스펙터에
  보이는 jumpForce 값은 항상 무의미**(누가 7.5로 바꿔도 게임엔 반영 안 됨). 읽는 사람이 "점프 힘 = 7.5"로
  오해한다(M3 'GetRankString이 firstPlaceText를 덮어써 인스펙터↔런타임 불일치'와 같은 표시 혼란 테마).
- 제안: `jumpForce`를 `[HideInInspector]`로 가리거나 인스펙터 기본값을 `originalJumpForce`와 맞춰
  (혹은 `jumpForce`를 런타임 전용 프로퍼티로) 표시 혼란 제거. 저위험·동작 불변.

### [N6] (코드품질·문서화 / 하) 플레이어 FSM 한글 주석 mojibake (M4와 동일 테마)
- 위치: `PlayerBaseState.cs:3`(`// ���� ����`), `PlayerIdleState.cs:4-6,13`,
  `PlayerMoveState.cs:4`, `PlayerJumpState.cs:5` 등 (상태 클래스 헤더 주석 다수).
- 내용: EUC-KR로 저장된 파일을 UTF-8로 디코딩한 흔적으로 한글 주석이 전부 깨져 의미 0. 로직 영향은 없으나
  학습/유지보수에서 주석 가치를 잃는다(`.editorconfig`/`.gitattributes`가 있는 프로젝트라 인코딩 일관성도 깨짐).
  **M4(NextSceneManager/ResultStarsUI mojibake)와 같은 문제** — 한 번에 UTF-8(BOM 없음) 재작성으로 묶어 처리 권장.

> ※ 우선순위: **N1(로그)** > N2(입력 아키텍처) > N3(null 가드) > N4(권위 재검증, L1과 묶음) > N5/N6(표시·주석).
>   N1·N3·N6은 동작 불변에 가까운 저위험 정리, N2는 리팩터링(회귀 테스트 권장), N4는 L1과 함께 설계 논의 대상.

---

## 2026-06-27 루틴 — AI 봇 FSM(상태머신)·탐지·네트워크 동기화 신규 심층 리뷰 (O1~O7 도출)

마지막 코드 변경은 06-24(`5ab9656`=M2), 06-25는 docs(N1~N6 도출)였고 이후 신규 커밋은 없다. 이 프로젝트의
확립된 패턴(06-12·06-18·06-23처럼 "신규 커밋 없어도 아직 안 본 시퀀스 영역을 골라 심층 분석")에 따라,
06-25에 본 **플레이어 FSM**의 자연스러운 짝인 **AI 봇 FSM(상태머신)** 을 골랐다. 대상:
`AIPlayerMovement`(FSM 컨텍스트 + 흡수/넉백/대쉬/공격) + `AIBaseState`/`AIWanderState`/`AIChaseState`/
`AIFleeState`/`AIPushSurviveState` 5개 상태 + `AIDetector`(탐지) + `EntityRegistry`(레지스트리) +
`AIPlayerSync`(네트워크 룸프롭 동기화). 네트워크 권위는 `NetworkPlayerSync`의 흡수/넉백 검증 RPC까지 교차 확인했다.

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3). 즉시 고칠 명백한 동작 버그는 없었다.
> O2(double-eat)는 L1·N4와 같은 '권위 재검증/경합' 테마라 묶어서 설계 결정이 필요해 보류했다.
> 효과·저위험 기준 우선순위는 **O1(탐지 캐시)** > O3(탐지 이원화) > O2(double-eat, L1묶음) > O5/O6/O7(정리).

### 좋았던 점(설계 관찰)
- **`EntityRegistry`의 스냅샷 패턴이 훌륭하다.** 내부는 `HashSet`(중복 방지·O(1) 추가/삭제), 외부 순회는
  dirty일 때만 새 `List`로 캐싱해 노출한다. 멀티플레이에서 한 프레임에 여러 엔티티가 파괴(`OnDisable`→
  `Unregister`)돼도, AI 탐지 `foreach`가 옛 스냅샷을 돌므로 `InvalidOperationException`(순회 중 수정)이
  구조적으로 안 난다. 변경 없으면 재사용해 매 프레임 GC도 없다. **"순회 안전 + GC 회피"를 동시에 푼 좋은 예.**
- **비마스터에서 봇 시뮬레이션을 확실히 끈다.** `Start`에서 비마스터는 `PlayerAbsorber`/`PlayerAbsorbingManager`
  비활성 + `Cloth` 제거 + `Agent.enabled=false` 후 리턴(line 174-189). 봇 권위는 마스터 단독이고 비마스터는
  스케일만 `IPunObservable`로 받아 Lerp(line 394) → 권위 충돌(지터/되감김)이 원천 차단된다(플레이어 FSM의
  `playerController.enabled=false` 원격 차단과 같은 결).
- **넉백 RPC가 소유자(마스터)에게만 간다.** 봇 넉백을 `RpcTarget.All`이 아닌 `aiBot.photonView.Owner`로 보내
  비마스터 로컬 이동과 수신 동기화 값의 충돌을 막는다(line 919, G2 적용과 일관). FSM 기본기(`ChangeState`
  자기전이 가드 line 279, `_isTransitioning` 재진입 가드 line 280, 각 분기 `return`)도 갖춰져 있다.

### [O1] (성능 / 중) `AIDetector` 캐시가 *null 결과를 캐싱하지 않아* 흔한 "대상 없음"에서 매 호출 전체 재스캔
- 위치: `AIDetector.FindThreat:27`, `FindPrey:37`, `FindNearestJelly:54`
  (`if (Time.time - _lastXScan < ScanCacheDuration && _cachedX != null)`).
- 내용: 캐시 적중 조건에 **`_cachedX != null`** 이 들어가 있다. 즉 직전 스캔 결과가 null(=주변에 위협/먹이가
  없음)이면 캐시가 무효 처리돼, 캐시 유효시간(0.1초) 안이라도 **호출할 때마다 `EntityRegistry.Players` +
  `Bots` 전체를 다시 순회**한다(각 대상마다 `CalcEdgeDistance` 거리 계산). 그런데 "주변에 나보다 큰 위협이
  없음 / 먹잇감이 없음"은 게임 대부분의 시간에서 **가장 흔한 상태**다. 게다가 `FindThreat`는 한 프레임에 여러
  곳에서 불린다 — `Update`의 긴급위협 체크(0.1초, line 435), `StateEvalLoop`(line 341), `EvaluateAndTransition`
  (line 364). 결과가 null이면 이 호출들이 전부 캐시를 못 타고 같은 프레임에 전체 스캔을 반복한다.
  비용은 (봇 수 × 엔티티 수)로 늘어 봇이 많을수록 커진다 — 정작 캐시가 가장 필요한 상황(봇 多)에서 안 먹힌다.
- 제안: 캐시 유효성 판정을 **시간만으로** 하고(`null`도 정상 결과로 캐싱), 별도 `bool _threatScanned`로
  "이 주기에 스캔했는가"를 표시하거나, 단순히 `_cachedX != null` 조건을 빼면 된다. 동작 불변(반환값 동일),
  null 구간에서 재스캔만 제거되는 순수 성능 개선.
- 학습: **캐시는 "값이 있을 때"가 아니라 "최근에 계산했을 때"를 기준으로 적중시켜야 한다.** `null`(없음)도
  엄연한 계산 결과인데, 그것만 캐시에서 빼면 "찾는 게 없을 때 가장 자주, 가장 비싸게 도는" 역설이 생긴다.
  G3/K3가 '빌드 매 프레임 로그'를 잡은 것과 같은 결의 '고빈도 경로 비용' 문제다(이번엔 로그가 아니라 스캔).

### [O2] (네트워크·아키텍처 / 중·확인필요) 봇 `OnTriggerEnter` 흡수의 동일프레임 *double-eat* 윈도우 + 플레이어 경로와의 규율 불일치
- 위치: `AIPlayerMovement.OnTriggerEnter:611-643` (봇이 플레이어/다른 봇을 직접 흡수).
- 내용: 이 트리거는 마스터에서만 돈다(line 613)지만, **흡수 성장(`ScaleCtrl.GrowByAbsorbing`)을 로컬에서
  즉시 적용**하고 피식자에겐 `RPC_GetAbsorbed`/`RPC_BotAbsorbed`를 `RpcTarget.All`로 보낸다. 그런데 이 RPC는
  **같은 프레임에 동기 실행되지 않고 큐잉**된다. 그래서:
  - 봇→봇: 가드 `!otherBot.IsBeingAbsorbed`(line 633)가 있으나, `IsBeingAbsorbed`는 `RPC_BotAbsorbed`에서
    세팅되는데 그게 큐잉이라 **같은 프레임에 두 큰 봇 A·B가 같은 작은 봇 C에 겹치면** 둘 다 가드를 통과해
    둘 다 성장한다(C는 한 번만 흡수돼도 A·B 둘 다 보상).
  - 봇→플레이어: 아예 `IsOutOfPlay`/중복 가드가 **없다**(line 619-629). 피식자 `RPC_GetAbsorbed`가
    `if(_isAbsorbed) return`로 막아도 그건 *피식자* 쪽 정합일 뿐, *흡수자(봇)* 들은 이미 로컬에서 성장한다.
  → **L1(젤리 흡수 로컬 무검증 double-eat)·N4(배트 히트 권위 재검증 누락)와 정확히 같은 '경합/권위 재검증'
    테마**다. .io 친선전이라 심각도는 낮지만 스케일/점수 인플레가 생긴다.
- 구조적 대조(중요): **플레이어의 흡수는 검증 RPC를 거친다** — `NetworkPlayerSync.RPC_RequestBotAbsorbValidation
  :493`, `RPC_RequestDashHitBot:713`은 마스터가 `aiBot.IsOutOfPlay`/`!IsBeingAbsorbed`를 재검증한 뒤 승인한다.
  반면 **봇 자신의 `OnTriggerEnter`는 이 중앙 검증 규율을 우회**해 직접 성장한다. 같은 "봇 흡수"인데 한쪽은
  검증 게이트를 통과하고 한쪽은 안 통과하는 *비대칭*이다.
- 제안: (a) 즉효 — 봇→봇은 마스터에서 RPC 전에 `otherBot.IsBeingAbsorbed = true`를 **동기 세팅**(setter가
  public, line 79). 봇→플레이어는 마스터 프레임 단위 "이미 claim된 피식자" HashSet으로 중복 보상 차단.
  (b) 정석 — 봇 OnTriggerEnter도 플레이어와 같은 마스터 검증 경로(또는 공용 헬퍼)로 모아 흡수 판정/보상을
  단일 규율로 통일. L1·N4와 함께 "권위 측 흡수/히트 재검증" 정책으로 묶어 처리 권장.
- 학습: **`RpcTarget.All`은 보낸 즉시 로컬에 반영되지 않는다(큐잉).** "상태 플래그를 RPC로 세팅하고 그 플래그로
  중복을 막는" 패턴은 *같은 프레임 다중 트리거*에 취약하다 — 막을 거면 권위 측에서 **동기적으로** 먼저 잠가야 한다.

### [O3] (아키텍처 / 중하) 먹이(추격 대상) 탐색 로직이 두 군데에 *이원화* — 규칙 분기 시 누락 위험
- 위치: `AIDetector.FindPrey/FindEntityByScaleComparison:35,75` ↔ `AIPushSurviveState.FindNearestTarget:158`.
- 내용: "나보다 작은 가장 가까운 대상"을 찾는 같은 목적의 로직이 **두 벌** 있다. `AIDetector`(흡수 모드 Chase가
  사용)는 `EntityRegistry`를 돌며 `IsScaleMatch`+`CalcEdgeDistance`(반지름 보정 *엣지* 거리)로 판정하고,
  `AIPushSurviveState.FindNearestTarget`(Push 모드)은 같은 레지스트리를 따로 돌며 `Vector3.Distance`(*중심* 거리)
  와 `IsOutOfPlay`/`IsBeingAbsorbed` 가드로 판정한다. 판정 기준(엣지 vs 중심 거리)·제외 조건(한쪽만 IsOutOfPlay)
  ·detectRadius 출처가 **미묘하게 다르다.** 둘 다 "포식자-피식자" 규칙을 손으로 구현해, 한쪽 규칙을 바꾸면
  다른 쪽이 조용히 어긋난다(예: K2에서 IsOutOfPlay 단일화를 Push 경로에 따로 보강해야 했던 것과 같은 류).
- 제안: 스케일 비교 기반 최근접 대상 탐색을 `AIDetector`(또는 공용 헬퍼) 한 곳으로 모으고, Push/흡수가
  파라미터(제외조건·거리기준)만 다르게 호출하게. → F2(모드 출처)·G6(탈락판정)·H6(점수집계)·N2(입력 폴링)와
  같은 **'판정 로직 단일 출처'** 테마.
- 학습: 같은 질문("누가 내 먹이냐")에 답이 두 군데 있으면, 그 둘은 **시간이 지나며 반드시 갈라진다.** 지금처럼
  "거의 같지만 미묘하게 다른" 복제가 가장 위험하다 — 버그가 한쪽에서만 재현돼 추적이 어렵다.

### [O4] (아키텍처 / 하·관찰) 상태 전이 주체가 *3원화* — 코루틴 + Update + 상태 내부
- 위치: ① `StateEvalLoop:291`(0.4초 주기 평가), ② `Update:431-437`(0.1초 긴급 위협 체크),
  ③ 각 상태 `Update` 내부의 `ai.EvaluateAndTransition()`(Chase line 44/52, Flee line 63 등).
- 내용: 플레이어 FSM은 전이가 사실상 *상태 Update*에서만 일어나는데, 봇은 전이를 거는 곳이 셋이다. 의도는
  타당하다 — 긴급 회피는 0.4초 주기로는 늦으니 Update에서 0.1초로 따로 본다. 다만 "지금 누가 내 상태를
  바꿀 수 있나"가 분산돼 추론·디버깅 난도가 올라간다. 동작 버그는 아니며(각 전이 호출 뒤 `return`으로 같은
  프레임 중복 전이는 막힘), **설계 관찰/학습 노트**로 남긴다.
- 학습: FSM에서 전이 트리거가 여러 주체로 흩어지면, "왜 이 상태로 갔지?"를 한 곳에서 못 읽는다. 가능하면
  전이 결정을 한 함수(`EvaluateAndTransition`)로 모으고, 주기/긴급은 *호출 빈도만* 다르게 두는 게 읽기 쉽다.

### [O5] (안정성 / 하) 탈락 봇의 `StateEvalLoop` 코루틴이 종료되지 않고 무한 공회전
- 위치: `ApplyEliminatedLocally:674`(`enabled = false`) ↔ `StateEvalLoop:291`(`while(true)`).
- 내용: `MonoBehaviour.enabled = false`는 `Update`만 멈출 뿐 **이미 도는 코루틴은 멈추지 않는다.** 탈락 봇은
  "파괴하지 않고 둥둥 유지"(line 647 주석)라 오브젝트가 살아 있어, `StateEvalLoop`이 씬이 끝날 때까지
  0.4초마다 깨어나 `if(!Agent.enabled) continue`(line 299)만 반복한다. 한 판에 탈락 봇이 쌓일수록
  죽은 코루틴이 누적된다(미미하지만 순수 낭비). ※ 흡수당한 봇은 `BotAbsorbedSequence`가 `PhotonNetwork.Destroy`
  하므로 해당 없음 — *탈락(OnEliminated) 경로만* 해당.
- 제안: `StateEvalLoop` 핸들을 필드로 잡아 `ApplyEliminatedLocally`에서 `StopCoroutine`, 또는 루프 선두에
  `if (IsEliminated) yield break;`. J7/G8/L2/N3와 같은 '정리/종료 경로' 위생.
- 학습: **`enabled=false` ≠ 코루틴 정지.** 코루틴은 오브젝트 비활성/파괴 또는 명시적 `StopCoroutine`으로만
  멈춘다. "컴포넌트를 껐으니 다 멈췄겠지"가 흔한 오해다.

### [O6] (안정성 / 하) `OnMasterClientSwitched`의 이벤트 *중복 구독* 가드(`-=`) 부재
- 위치: `OnMasterClientSwitched:751-752`(`ScaleCtrl.OnScaleValueChanged += OnBotScaleChanged;`),
  최초 구독은 `Start:191-192`.
- 내용: 현재 흐름상 새 마스터는 비마스터 시절 `Start`에서 구독을 안 했으니(line 188 early return) 보통 1회
  구독으로 끝난다. 다만 `+=` 앞에 `-=`(방어적 해제)가 없어, 향후 마스터 교체가 연쇄로 일어나거나 콜백 흐름이
  바뀌면 **중복 구독 → `SyncScale` 중복 호출(룸프롭 중복 쓰기)** 위험이 구조적으로 열려 있다. H1(정적 이벤트
  정리)·N3(콜백 가드)와 같은 '이벤트 수명 위생' 테마.
- 제안: 구독 직전에 `ScaleCtrl.OnScaleValueChanged -= OnBotScaleChanged;`를 한 줄 넣어 멱등 구독으로.
- 학습: **C# 이벤트 구독은 `+= ` 전에 항상 `-=`** 를 습관화하면 "두 번 등록돼 두 번 호출" 부류 버그를
  구조적으로 차단한다(특히 재진입 가능한 콜백 OnEnable/OnMasterClientSwitched에서).

### [O7] (성능·로그 / 하) `RPC_BotAbsorbed`의 무가드 `Debug.Log` — 프로젝트 로그 규약(N1/G3/K3) 미적용 경로
- 위치: `RPC_BotAbsorbed:692` (`Debug.Log(this.name + "/RPC_BotAbsorbed : AI 플레이어 흡수됨.");`).
- 내용: 흡수 1회당 모든 클라에서 1회라 빈도는 낮지만(매 프레임 아님), `#if UNITY_EDITOR` 가드가 없어
  **빌드에도 남는 디버그 로그**다. 이 프로젝트는 G3(FallingTile)·K3(ClearJudge)·N1(FSM)에서 "빌드 로그는
  에디터 전용으로 가드"를 관례로 확립했는데, 이 경로가 그 정리에서 빠졌다(저빈도라 우선순위는 가장 낮음).
- 제안: 제거하거나 `#if UNITY_EDITOR`로 감싼다. N1 적용 시 함께 묶어 처리하면 한 번에 정리된다.

> ※ 우선순위: **O1(탐지 캐시, 순수 성능)** > O3(탐지 이원화 단일화) > O2(double-eat, L1·N4와 묶음·확인필요)
>   > O5(코루틴 종료) ≈ O6(중복구독 가드) > O7(로그 가드). O1·O5·O6·O7은 동작 불변에 가까운 저위험 정리,
>   O3는 리팩터링(회귀 테스트 권장), O2는 L1·N4와 함께 흡수/히트 권위 정책으로 묶어 설계 논의 대상.

---

## 2026-06-30 루틴 — 게임 진입/매칭/룸 라이프사이클 신규 심층 리뷰 (연결→매칭→스폰→시작, P1~P5 도출)

마지막 코드 변경은 06-24(`5ab9656`=M2)였고 이후 신규 커밋은 docs뿐(06-25 N, 06-27 O)이라 06-30 기준 신규
커밋은 없다. 이 프로젝트의 확립된 패턴(06-12·06-18·06-23·06-27처럼 "신규 커밋 없어도 아직 전용 심층 리뷰가
없는 시퀀스 영역을 골라 분석")에 따라, 지금까지 다룬 **인게임 코어(타일·흡수 K/L)·액터 FSM(플레이어 N·봇 O)·
종료/결과(M)** 의 *반대편 끝* — **게임 진입 시퀀스(연결→로비/매칭→씬 로드→스폰→게임 시작)** 를 골랐다. 이 영역은
F(모드 선택)·H2(매칭 데드락)에서 단편적으로만 봤고 *라이프사이클 전체*로는 처음이다. 루틴 핵심 주제
"안정적인 네트워크 연동"과 가장 직접적으로 맞닿는다. 대상:
`NetworkManager`(연결/방/카운트다운/스폰), `GameModeManager.SpawnAndStartGame`(씬 진입 후 스폰·시작),
`LoadingSceneController`(씬 전환), `LobbyController`(매칭 UI). 네트워크 권위/씬 동기화 경로를 교차 확인했다.

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3). 즉시 고칠 명백한 동작 버그는 없었다.
> P1(재연결)은 단순 수정이 아니라 **PlayerTtl/ReconnectAndRejoin 같은 설계 결정**이 필요해 보류했고(확인필요).
> P2(스폰 결정성)는 씬 SpawnPoint 수를 점검한 결과 **현재 구성(10개=maxPlayers)에선 비활성인 잠재 결함**으로
> 확인됨 → 우선순위 하향. P3·P5는 동작 불변 정리, P4는 UI/네트워크 상태 단일화 테마다.
> 효과·저위험 우선순위는 **P1(재연결 룸 복귀)** > P3(필드/상태 분리) ≈ P4 ≈ P5 ≈ P2(잠재, 설정 변경 시 활성).

### 좋았던 점(설계 관찰)
- **PUN 메시지 큐 정지/desync를 두 겹으로 방어한다.** `AutomaticallySyncScene`은 씬 전환 중 메시지 큐를
  멈추는데, 이게 재개 안 되면 플레이어 Instantiate·타일 RPC가 게임 내내 버퍼링돼 "서로 안 보임/타일 desync/
  젤리 끝에 소환"이 난다. 이를 `NetworkManager.OnSceneLoaded`(게임 씬 진입 시, line 141-147)와
  `GameModeManager.SpawnAndStartGame`(스폰 직전, line 147-151) **두 진입점에서 모두** `IsMessageQueueRunning=true`로
  복구한다. 한 경로가 누락돼도 다른 쪽이 막는 방어적 이중화 — 네트워크 타이밍 버그의 정석 대응.
- **매칭 카운트다운의 마스터 승계 인수인계가 견고하다(H2 적용).** `OnMasterClientSwitched`가 Main 씬에서만
  반응하고(line 311), 인원이 최소 미달이면 방을 다시 열어 모집을 재개(line 313-321), 충족이면 새 마스터가
  카운트다운을 이어받는다(line 324). "마스터가 죽으면 매칭이 영구 대기"라는 흔한 P2P 매칭 데드락을 정확히 막음.
- **스폰 슬롯 인덱스를 ActorNumber가 아니라 PlayerList 정렬 인덱스로 잡는다(line 563-575).** 입·퇴장 반복으로
  ActorNumber가 비연속(1,3,5…)이 돼도 0-based 연속 인덱스를 모든 클라가 동일하게 계산 → 슬롯 충돌/범위 초과
  방지. 물리 SpawnPoint 영역에서는 이 덕에 완전 결정적이다(아래 P2는 *가상* 포인트 영역에만 해당).
- **결과 전환을 '모든 클라 각자 LoadLevel + 동기화 토큰 대기'로 처리(GameModeManager).** 마스터만 로드하게 하면
  마스터가 먼저 탈락 시 생존자가 결과 씬에 못 가는 데드락이 생기는데, 룸프롭 토큰(ServerTimestamp)이 도착할
  때까지 대기 후 각자 로드해 데드락·색상 누락을 동시에 푼다(line 454-490).

### [P1] (네트워크 / 중·확인필요) 재연결이 룸으로 복귀하지 않음 — 일시 끊김 후 마스터 서버에 고립
- 위치: `NetworkManager.OnDisconnected:391-401`(일시 끊김 → `ReconnectCoroutine`), `ReconnectCoroutine:410-418`
  (`ConnectUsingSettings`), `OnConnectedToMaster:199-208`(재진입 가드 `if (_wantsToJoin)`).
- 내용: `OnJoinedRoom`이 `_wantsToJoin=false`로 내린다(line 249). 그 뒤 **일시 끊김**(`ClientTimeout`/`ServerTimeout`/
  `Exception`)이 나면 `ReconnectCoroutine`이 `ConnectUsingSettings()`로 **마스터 서버까지만** 재연결하고,
  `OnConnectedToMaster`의 재입장 가드 `if (_wantsToJoin)`가 이제 false라 **방으로 다시 들어가는 호출이 아예 없다**
  (`JoinOrCreateRoom`도, `ReconnectAndRejoin`도 호출 안 됨). 결과: 게임 씬은 그대로 떠 있는데 플레이어는 룸에서
  떨어져 — 내 캐릭터가 더는 동기화되지 않고(남들 눈엔 정지), 나도 결과 씬으로 못 넘어가 **세션 내내 고립**된다.
  매칭 단계(OnJoinedRoom 이후 카운트다운 대기 중)에도 동일하게 발생한다(이미 룸 안이라 `_wantsToJoin`=false).
- 추가로, PUN 기본 `RoomOptions`에 `PlayerTtl`이 설정돼 있지 않다(=0) → 끊긴 액터가 서버에서 즉시 제거되므로,
  설령 `ReconnectAndRejoin()`을 넣어도 `PlayerTtl>0` 없이는 재입장 자체가 실패한다. 즉 **두 가지가 같이 빠져 있다.**
- 제안(설계 결정 필요): (a) "인게임 재접속"을 지원하려면 `JoinOrCreateRoom`의 `RoomOptions.PlayerTtl`을
  수 초~수십 초로 주고, 재연결 경로를 `PhotonNetwork.ReconnectAndRejoin()`으로 바꾼다(실패 시 폴백). (b) 지원
  안 할 거면 최소한 *정직하게* — 일시 끊김도 룸 복귀가 불가하면 `LoadMainViaLoading()`로 메인에 돌려보내 "고립
  상태로 멈춤"을 없앤다. 지금은 (a)도 (b)도 아니라 **조용히 방치**된다. (※ 비일시·재시도 소진 경로에서
  `_wantsToJoin`을 초기화하지 않는 잔여 위생 문제도 함께 정리 권장.)
- 학습: **재연결은 두 계층이다 — ① 마스터 서버 복귀 ② 룸 복귀.** `ConnectUsingSettings`는 ①만 한다. ②는
  `ReconnectAndRejoin`(+서버측 `PlayerTtl`)이나 명시적 재입장이 있어야 하고, 그게 없으면 "연결은 됐는데 게임엔
  못 돌아오는" 가장 헷갈리는 고립 상태가 된다.

### [P2] (아키텍처·네트워크 / 하·관찰·잠재) 스폰 슬롯의 *가상 포인트*가 클라이언트마다 무작위 → 물리 SpawnPoint 부족 시 겹침/봇-플레이어 충돌 (현재 구성에선 비활성, 확인 완료)
- 위치: `NetworkManager.PrepareSpawnSlots:482-527` + `TryGenerateVirtualSpawnPoint:529-548`(`Random.insideUnitCircle`),
  `SpawnLocalPlayer:581-598`(자기 슬롯에 본인 Instantiate), `SpawnBots:603-633`(`botStartIdx = PlayerList.Length`).
- 내용: 각 클라이언트는 **자기만의 `_spawnSlots`** 를 만든다. 물리 SpawnPoint(태그 탐색)는 씬 지오메트리라 모든
  클라가 동일 → 슬롯 `[0..물리수)`는 결정적이다. 그러나 부족분을 채우는 **가상 포인트는 `Random.insideUnitCircle`
  로 클라마다 다른 좌표**가 된다. 그런데 플레이어는 *자기 클라의* `_spawnSlots[자기 정렬 인덱스]`에 **본인을**
  Instantiate하고, 마스터는 *마스터 클라의* `_spawnSlots[PlayerList.Length..]`에 **봇을** Instantiate한다. 즉
  가상 영역에서 "슬롯 i"가 클라마다 다른 위치를 가리켜 **서로의 배치를 모른다.** 겹침 방지(`minSpawnDistance`
  `tooClose`)는 *한 클라의 `_spawnSlots` 내부에서만* 도므로 **클라 간 겹침은 못 막는다.** → 물리 SpawnPoint가
  (플레이어+봇) 총수보다 적을 때, 봇이 원격 플레이어 위에, 또는 두 플레이어가 충돌 거리 안에 스폰돼 **시작
  즉시 넉백/흡수**가 날 수 있다. `SpawnBots`가 `botStartIdx=PlayerList.Length`로 "플레이어가 0..N-1을 점유"를
  가정하는 것도 결정적 물리 영역에서만 참이고, 가상 영역에선 그 가정이 클라 간에 깨진다.
- 조건부: 물리 SpawnPoint 수 ≥ 총 액터면 전혀 발생 안 함. **확인 완료(2026-06-30)** — 두 게임 씬
  (`Game_io_AbsorbMode`/`Game_io_PushMode`) 모두 `m_TagString: SpawnPoint` 오브젝트가 **정확히 10개**이고
  `maxPlayersPerRoom`도 10이다. `needed = max(10, PlayerCount+botCount) = 10`이라 물리 10개로 슬롯이 다 차서
  `while(_spawnSlots.Count < needed)` 가상 보충 루프가 **현재 구성에선 한 번도 돌지 않는다.** 즉 P2는 지금은
  **비활성(잠재) 결함**이다 — 누군가 SpawnPoint를 10개 미만으로 줄이거나 `maxPlayersPerRoom`을 11+로 올리는
  순간 바로 활성화된다. (가상 포인트 생성 경로 자체가 현재 사실상 도달 불가 = 검증 안 된 채 잠들어 있다는 뜻이라
  오히려 위험 — 설정 한 줄 바뀌면 테스트 안 된 코드가 깨어난다.)
- 제안: 가상 포인트를 **권위(마스터)가 한 번 계산해 룸 프로퍼티로 공유**하거나, **공유 시드**(룸 이름/
  `GameStartTime` 등)로 `Random`을 초기화해 모든 클라가 동일한 가상 레이아웃을 재현하게 한다. → F2(모드 출처)·
  G6(탈락판정)·O3(먹이 탐색)와 같은 **'월드 상태는 단일 권위/공유 시드에서'** 테마.
- 학습: **"각 클라가 빈자리를 알아서 채운다"는 그 자리가 *크로스-클라 배치*에 안 쓰일 때만 안전하다.** 무작위를
  클라별로 돌리면 월드 상태가 비결정적이 된다 — 스폰 레이아웃 같은 공유 상태는 한 권위나 공유 시드에서 와야 한다.

### [P3] (아키텍처 / 하·관찰) 직렬화 설정 필드 `botCount`를 런타임 상태로 덮어씀 — N5/M3 테마
- 위치: 선언 `NetworkManager.botCount`(인스펙터 설정, 기본 2, line 45) ↔ `CountdownCoroutine:355`
  (`botCount = maxPlayersPerRoom - PhotonNetwork.CurrentRoom.PlayerCount;`).
- 내용: `botCount`는 "방에 유지할 봇 수"라는 *설정값*인데, 카운트다운 막판에 *런타임 계산값*으로 덮어쓴다. 첫
  매치 이후 인스펙터 원본값은 세션 동안 사라진다. 동작상 마스터에선 매 카운트다운마다 LoadLevel 직전에 다시
  계산하므로 OK지만 — (a) 비마스터에선 이 줄이 안 돌아 `botCount`가 기본 2로 남고 `PrepareSpawnSlots`의 `needed`
  계산에 섞인다(`needed=max(maxPlayers, PlayerCount+botCount)`이라 maxPlayers가 상한이라 무해할 뿐), (b) N5
  (`jumpForce`가 Start에서 덮어써짐)·M3(`firstPlaceText`를 조회 중 부수효과로 덮어씀)과 같은 **'직렬화 필드를
  가변 상태로 재사용'** 냄새다. 인스펙터 값이 의미를 잃고 모드/경로별로 다른 값이 새어들 여지를 남긴다.
- 제안: `botCount`는 읽기 전용 설정으로 두고, 실제 스폰 수는 별도 필드(예 `_botsToSpawn`)에 계산해 담는다.
- 학습: **설정(직렬화) ≠ 상태(런타임).** 한 변수에 둘을 겹치면 인스펙터 값이 거짓이 되고, "어디서 이 값이
  바뀌었지?"를 추적하기 어려워진다. 값을 덮어쓰는 대신 파생값을 새 필드에 담는 습관이 안전하다.

### [P4] (아키텍처·안정성 / 하·관찰) 매칭 UI(LobbyController)와 네트워크 상태(NetworkManager)가 분리돼 일시 끊김 시 어긋남
- 위치: `LobbyController.OnDisconnected:207-220`(어떤 끊김이든 매칭 UI를 입력 패널로 되돌림) ↔
  `NetworkManager.OnDisconnected:391-401`(일시 끊김이면 백그라운드 재연결 시도).
- 내용: 일시 끊김이 매칭 도중 나면, `LobbyController`는 *모든* 끊김을 "매칭 실패"로 보고 매칭 패널을 접어
  이름 입력 화면으로 되돌린다. 동시에 `NetworkManager`는 `ReconnectCoroutine`으로 조용히 재연결을 시도한다
  (그리고 P1대로 룸 복귀엔 실패). 두 상태머신이 "지금 매칭 중인가"에 대한 **단일 출처를 공유하지 않아**, 화면은
  '매칭 취소됨'처럼 보이는데 네트워크는 여전히 재연결 중인 **불일치**가 생긴다.
- 제안: 로비 UI를 `NetworkManager`의 매칭 상태(또는 명시적 상태 enum)에서 파생시키거나, `OnDisconnected`에서
  *재시도 없는 최종 끊김*에서만 UI를 되돌리고 일시 끊김(재연결 중)에는 "재연결 중…"을 유지한다. → F2/G6/H6/
  N2/O3와 같은 **'상태 단일 출처'** 테마. P1과 묶어 처리하면 매칭 단계 견고성이 한 번에 정리된다.
- 학습: UI 상태와 네트워크 상태가 각자 콜백으로 따로 갱신되면, 둘이 갈라지는 순간(특히 재시도 가능한 끊김)에
  사용자에게 모순된 화면이 보인다. 화면은 *네트워크 상태의 함수*로 두는 게 어긋남을 원천 차단한다.

### [P5] (코드품질·문서화 / 하·관찰) 인스펙터 `spawnPoints`는 사실상 디버그 표시용 — 매 씬 로드에서 null로 비워져 지정값이 안 쓰임 (N5 테마)
- 위치: 선언/툴팁 `NetworkManager.spawnPoints:34-35`("비워두면 'SpawnPoint' 태그로 자동 탐색") ↔
  `OnSceneLoaded:134`(`spawnPoints = null;`) ↔ `GetValidSpawnPoints:445-471`(인스펙터값 우선 → 없으면 태그 탐색,
  태그 결과를 `spawnPoints`에 되저장).
- 내용: `NetworkManager`는 `DontDestroyOnLoad`(Main에서 생성)라 게임 씬의 SpawnPoint Transform을 인스펙터에
  미리 꽂아둘 방법이 없고, 실제 기전은 게임 씬에서의 **태그 탐색**이다. `OnSceneLoaded`가 매 씬 로드 때
  `spawnPoints=null`로 비우는 건 *이전 씬의 파괴된 Transform 재사용 방지*라 **올바르다.** 다만 그 결과 인스펙터에
  값을 꽂아도 첫 게임 씬 진입 시 비워져 **실제로는 절대 안 쓰인다** — 툴팁("지정하면 그걸 쓴다")은 오해의 소지가
  있고, `spawnPoints`는 사실상 *런타임 디버그 표시용 캐시*다. N5(인스펙터 `jumpForce`가 항상 덮어써져 표시가
  혼란)와 같은 '인스펙터 필드가 거짓 신호' 테마.
- 제안: 동작은 정상이므로 코드 변경보다 **의도 명시**가 핵심 — 툴팁을 "런타임에 태그 탐색 결과로 채워짐(읽기
  전용 표시)"로 고치거나, 필드를 `[SerializeField] private`/디버그 전용으로 강등. (동작 불변)
- 학습: "비워두면 자동, 채우면 수동"이라고 적힌 인스펙터 필드가 실제론 항상 자동으로 덮어써지면, 그 필드는
  설정처럼 보이는 *표시값*이다. 보이는 것과 실제가 다른 필드는 미래의 자신/팀원을 헷갈리게 한다 — 라벨이 진실이게.

> ※ 우선순위: **P1(재연결 룸 복귀, 네트워크 견고성)** > P3(필드/상태 분리) ≈ P4(UI/네트워크 단일화) ≈
>   P5(인스펙터 라벨 정직화) ≈ P2(잠재 — 현재 비활성, SpawnPoint를 줄이거나 maxPlayers를 늘리면 활성).
>   P1은 PlayerTtl/ReconnectAndRejoin 같은 **설계 결정**이 필요해 도출만 함(확인필요). P3·P4·P5는 동작 불변에
>   가까운 저위험 정리. 즉시 고칠 명백한 동작 버그는 없었다(P2도 현재 구성에선 도달 불가로 확인됨).

---

## 2026-07-02 루틴 — 인게임 점수·성장 피드백 파이프라인 신규 심층 리뷰 (흡수/적중 → 스케일 → 점수 → 동기화 → 리더보드, Q1~Q7 도출)

06-30 이후 신규 코드 커밋은 없다(마지막 코드 변경은 06-24 M2, 이후는 docs만: 06-27 O, 06-30 P). 이 프로젝트의
확립된 패턴(신규 커밋이 없어도 아직 전용 심층 리뷰가 없는 시퀀스 영역을 골라 분석)을 따른다. 지금까지 진입(P)·
액터 FSM(N/O)·타일/흡수 코어(K/L)·종료/결과(M)를 다뤘는데, **정작 게임 *진행 중* 매 순간 도는 핵심 피드백
루프 — "젤리 흡수/배트 적중 → PlayerScaleController 성장 → 점수 환산 → 네트워크 동기화 → 리더보드 갱신" —**
은 조각(H4·H6·L1)으로만 봤지 *데이터 흐름 전체*로는 처음이다. 루틴 3대 주제 중 **아키텍처(단일 출처)와
안정적 네트워크 연동(SetCustomProperties 빈도)** 에 정면으로 닿는다. 대상:
`PlayerScaleController`(성장 상태머신/큐), `PlayerBridge`(성장→점수 브리지), `NetworkPlayerSync`(SyncScale/
SyncScore), `ScoreboardSnapshot`+`GameModeManager.UpdateLeaderboard`(수집·정렬·표시), `LevelUpFloater(+Pool)`·
`ScoreUI`/`LevelUI`/`CurrentStatusUI`(HUD). 성장 1회가 만드는 네트워크 쓰기 횟수와 순위 계산 경로를 교차 추적했다.

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3, 승인 후 적용). 게임을 즉시 깨는 크래시성
> 버그는 없었으나, **Q3(리더보드 '본인 행 항상 표시' 미구현)** 와 **Q4(중복 닉네임 자기 오식별)** 는 관찰 가능한
> 동작 결함이라 우선 권장. 효과·저위험 우선순위: **Q1(스케일/점수 동기화 이중·삼중 쓰기)** ≈ **Q3** ≈ **Q4**
> > Q2(데드 "Score" 키) ≈ Q5(스케일 폴백 불일치) > Q6·Q7(관찰/방어).

### 좋았던 점(설계 관찰)
- **순위의 단일 출처가 'scale' 하나로 통일돼 있다(H6의 결실).** 인게임 리더보드도, 결과 씬도 모두
  `ScoreboardSnapshot.Collect`로 수집하고 점수는 그때그때 `DataManager.ScoreFromScale(scale)`로 *파생*한다
  (GameModeManager.cs:664, DataManager.cs:101). 점수를 따로 저장·동기화하지 않으니 "표시 점수 ≠ 실제 순위"
  같은 이원화 사고가 원천 차단된다. 성장=scale 증가라는 게임 규칙과 데이터 모델이 정확히 일치한다.
- **성장이 상태머신 큐로 직렬화돼 경합에 안전하다.** `PlayerScaleController`가 모든 성장/축소를 `scaleQueue`에
  넣어 `ProcessScaleQueue`로 하나씩 처리(cs:141-155)하고, `_pendingScale`을 누적 목표로 분리해 애니메이션 중
  들어온 추가 흡수도 자연스럽게 이어붙인다. 젤리 성장은 한 프레임 배칭(`BatchedJellyGrow`, cs:54-59)까지 해
  연속 흡수 시 코루틴 폭발을 막는다(G1의 `_jellyBatchCoroutine=null` 방어도 유지됨).
- **원격 사본이 성장 로직을 절대 재실행하지 않도록 못 박아 뒀다.** `SetupRemotePlayer`가 원격에서 `PlayerAbsorber`
  /`PlayerAbsorbingManager`를 꺼(cs:179-182) 원격 클라이언트가 제 맘대로 GrowByJelly를 돌려 스케일이 갈라지는
  사고를 막고, 스케일은 오직 소유자 CustomProperties("Scale")를 권위로 삼아 Lerp로 따라간다(cs:204-206).
- **LevelUpFloater 풀이 부모 스케일을 상쇄해 항상 일정 크기로 뜬다.** `Vector3.one / parentScale * pop`
  (LevelUpFloater.cs:103)으로 젤리가 아무리 커져도 팝업 글자 크기가 고정되고, 동시 흡수 시 좌우 흩뿌림
  (spreadX)으로 겹침을 피한다. 프리팹 없으면 런타임 생성 폴백까지 있어 에셋 의존이 약하다.

### [Q1] (네트워크·성능 / 중) 성장 1회에 스케일/점수 CustomProperties가 2~3번 중복 기록됨 — 겹치는 Scale 데이터 반복 전송
- 위치: `PlayerScaleController.ScaleTo`가 완료 순간 **두 이벤트를 연달아** 던진다 —
  `OnScaleValueChanged`(cs:127) + `OnScaleCompleted`(cs:128).
  - `OnScaleValueChanged` → `NetworkPlayerSync.OnLocalScaleChanged`(cs:150-153) → `SyncScale()` →
    `SetCustomProperties({ "Scale" })`(cs:273-282).
  - `OnScaleCompleted` → `PlayerBridge.HandleScaleCompleted`(cs:109-127) → `SyncScore()` →
    `SetCustomProperties({ "Score", "Scale" })`(cs:260-270).
  즉 **같은 완료 시점에 SetCustomProperties가 2번** 호출되고, 두 번째가 첫 번째의 Scale을 이미 포함한다.
  여기에 젤리 1개를 먹으면 `PlayerAbsorber.OnJellyScored` → `HandleJellyScored`도 **예측 점수로 SyncScore를
  한 번 더** 호출한다(PlayerBridge.cs:162-163) → 젤리 1개당 대략 **SetCustomProperties 3회**(예측 SyncScore +
  완료 SyncScale + 완료 SyncScore)가 나갈 수 있다.
- 왜 문제인가(학습 포인트): `SetCustomProperties`는 로컬 대입이 아니라 **Photon 서버를 왕복해 방 전체에 브로드
  캐스트되는 이벤트**다. Photon은 초당 메시지/이벤트 예산이 있어서, 여러 명이 젤리를 빠르게 흡수하는 순간
  같은 값을 나르는 이 중복 쓰기가 예산을 잠식하고 다른 동기화(위치/색/타일 RPC)의 지연·유실로 번질 수 있다.
  또 세 쓰기가 서로 다른 프레임에 서버에 반영되면 리더보드가 잠깐 들쭉날쭉 보일 수도 있다.
- 제안: 성장→동기화 경로를 **하나로** 통일한다. `OnScaleValueChanged` 구독(SyncScale)을 없애고
  `OnScaleCompleted` 한 곳에서만 `{ "Scale" }`(점수는 파생이므로 Score 키 자체 제거 — Q2 참고)을 쓰거나,
  최소한 "직전 전송값과 동일하면 스킵"하는 가드를 둔다. `HandleJellyScored`의 예측 SyncScore는 **로컬 HUD 즉시
  갱신용**이면 `GameState.CurrentScore`만 로컬로 올리고 네트워크 쓰기는 완료 시점 1회로 미룬다.
- 참고: 리더보드는 어차피 30프레임마다(`GameModeManager.Update` cs:246-247) 다시 그리므로, 성장마다 즉시
  전파할 필요가 없다 — 완료 1회 쓰기로도 순위 정확도에 손해가 없다.

### [Q2] (아키텍처·데드데이터 / 하) "Score" 커스텀 프로퍼티는 *쓰기만* 하고 아무도 읽지 않음 — 네트워크 데드 데이터
- 위치: `NetworkPlayerSync.SyncScore`가 `{ "Score", newScore }`를 매번 기록(cs:266)하지만, 프로젝트 전체에서
  `"Score"` 키를 **읽는 곳이 단 한 곳도 없다**(`grep "Score"` 결과 쓰기 1곳뿐). 리더보드·순위·결과 모두 scale로
  파생(Q1의 '좋았던 점' 참조)하므로 이 값은 소비처가 없다.
- 왜 문제인가: 죽은 키라도 SetCustomProperties 페이로드에 실려 매번 브로드캐스트되고, "혹시 이게 진짜 점수
  출처인가?" 하고 다음 사람이 오해하게 만든다(구버전 잔재는 M1과 같은 테마). Q1을 고치면 자연히 함께 정리된다.
- 제안: `SyncScore`를 없애고 호출부를 `SyncScale`로 대체(점수는 로컬 `GameState.CurrentScore`만 갱신), 또는
  최소한 Hashtable에서 `"Score"` 키를 빼 Scale만 남긴다. **동작 불변, 순수 정리.**

### [Q3] (버그·UX / 중하) 리더보드 "상위 5위 밖이면 마지막 칸을 본인 행으로" 기능이 실제로 없음 — `localRank`/`localOutside`가 계산만 되고 버려짐
- 위치: `GameModeManager.UpdateLeaderboard`(cs:633-667). 주석(cs:643-644)은 "로컬 플레이어가 상위 displayCount
  밖에 있으면 마지막 칸을 본인 행으로 대체해 자신의 이름/순위가 항상 보이도록 한다"고 약속하고, 실제로
  `localRank`를 찾아(cs:645-653) `bool localOutside = localRank >= displayCount;`(cs:654)까지 계산한다.
  **그런데 이어지는 표시 루프(cs:656-666)는 `localOutside`/`localRank`를 전혀 참조하지 않고** 그냥 상위
  `displayCount`(≤5)만 그린다. 두 변수는 계산 후 폐기되는 **데드 변수**이고, 약속한 기능은 미구현이다.
- 증상: 봇 포함 6명 이상인 판에서 내가 6위 이하면 **리더보드에서 내 행이 아예 안 보인다.** 자기 순위를 확인
  못 하는 UX 공백(특히 봇 다수 흡수 모드에서 흔함).
- 제안(둘 중 택1): (a) `localOutside`일 때 마지막 칸(i==displayCount-1)을 상위행 대신 `entries[localRank]`로
  치환해 렌더 — 주석대로 구현. (b) 그 기능을 접기로 했다면 `localRank`/`localOutside`/주석을 삭제해 코드와
  의도를 일치시킨다. **어느 쪽이든 "코드=주석" 정합**이 목표(Q3은 동작 결함이라 (a) 권장).

### [Q4] (버그·일관성 / 중하) 본인 식별을 *닉네임 문자열*로 함 — 중복 닉네임 시 오식별 (H4의 확장, 이제 즉시 해결 가능)
- 위치: 자기 식별이 닉네임 비교로 되어 있는 곳이 **3군데** —
  `UpdateLeaderboard` 내 localRank 탐색(cs:648)·`isMe` 판정(cs:663), `GetLocalPlayerRank`(cs:407):
  모두 `entry.name == PhotonNetwork.NickName`.
- 왜 문제인가: 닉네임은 유일성이 보장되지 않는다(둘 다 "Player" 가능). 동명이인이 있으면 하이라이트(myEntry
  노란 배경)와 내 순위가 **엉뚱한 사람 행에** 붙는다. 이건 06-13에 H4로 이미 도출됐던 테마다.
- **지금이 고치기 딱 좋은 이유:** H6에서 `ScoreboardSnapshot.Entry`에 `actorNumber` 필드를 추가하며 주석에
  *"닉네임 중복에 안전한 비교용"*(ScoreboardSnapshot.cs:28)이라고 명시해뒀는데, **정작 이 세 호출부는 아직도
  name을 쓴다.** 인프라는 이미 있으니 `entry.actorNumber == PhotonNetwork.LocalPlayer.ActorNumber` 한 줄로
  교체하면 H4가 닫힌다(봇은 actorNumber=0이라 자연히 제외됨).
- 제안: 위 3곳을 actorNumber 비교로 교체. **H4를 이번 루틴에서 함께 완료 처리 가능.**

### [Q5] (일관성·견고성 / 하) 스케일 '기본값' 폴백이 리더마다 제각각(1f vs startingScale=2f) + 무검증 캐스트
- 위치: 같은 "Scale" 프로퍼티가 아직 없을 때의 폴백이 경로마다 다르다 —
  `NetworkPlayerSync.GetPlayerSyncedScale`은 **1f**(cs:333),
  `GetAuthorityScale`은 실제 `pv.transform.localScale.x`(cs:631),
  `ScoreboardSnapshot.Collect`엔 `GameModeManager`가 `startingScale`(=2f)을 넘김(GameModeManager.cs:367),
  `GameState.Reset`의 초기값은 **2f**(GameState.cs). 실제 스폰 스케일은 2f인데 `GetPlayerSyncedScale`만 1f로
  가정한다.
- 증상: 첫 `SyncScore(0)`(cs:142)이 서버에 반영되기 전 짧은 창에서, 봇의 위협/도주 판정이 상대 플레이어를
  실제(2f)보다 작은 **1f로 오판**(`AIPlayerMovement.cs:623`이 `GetPlayerSyncedScale` 사용)해 봇이 안 도망가거나
  잘못 달려들 수 있다. 스폰 직후 한두 프레임짜리 미세 결함이지만 폴백 상수 불일치가 근본 원인.
- 추가: `GetPlayerSyncedScale`(cs:332)·`GetAuthorityScale`(cs:629)의 `return (float)val;`은 **무검증 캐스트**다.
  `ScoreboardSnapshot.ReadFloat`(cs:118-122)는 `v is float` 확인 후 폴백하는 안전 패턴을 쓰는데 이 둘만 안 쓴다.
  "Scale"은 항상 float로 쓰이니 실무상 안전하지만, 타입이 어긋나면 InvalidCastException으로 매 프레임(Update의
  Lerp 경로) 터질 수 있어 방어가 낫다.
- 제안: 폴백 상수를 `DataManager.Instance.startingScale` 하나로 통일하고, 두 메서드도 `is float` 가드 패턴으로
  맞춘다. **동작 사실상 불변, 일관성/견고성만 상승.**

### [Q6] (일관성·관찰 / 하) `ResetScale`은 1f로 리셋하는데 스폰/초기값은 2f — 리셋 기준이 스폰 기준과 이원화
- 위치: `PlayerScaleController.ResetScale`이 `currentScaleValue=1f`·`_pendingScale=1f`(cs:169-170),
  `HandleScaleReset`도 `PlayerCurrentScale=1f`(PlayerBridge.cs:133). 반면 `Start`/`GameState.Reset`/
  `startingScale`은 모두 2f 기준. 리셋 경로만 1f로 떨어진다.
- 관찰: 리셋(사망/재시작 등) 후 다시 게임에 들어갈 때 기준 스케일이 스폰(2f)과 달라, "리셋 직후 첫 판정"에서
  시각 크기·흡수 우열·점수(ScoreFromScale은 startingScale=2f 기준이라 scale 1f면 음수→0으로 클램프) 계산이
  스폰 상태와 어긋날 수 있다. 설계 의도(리셋=최소 기본 1)일 가능성이 있어 **관찰**로 둔다 — 다만 리셋 기준을
  `startingScale`로 통일할지 결정이 필요하다.
- 제안: 리셋 목표를 `DataManager.Instance.startingScale`로 통일하거나, 1f가 의도라면 스폰 초기값도 1f로 맞춰
  **'기본 스케일'의 단일 출처**를 정한다. (N5/P3의 "설정≠상태" 테마와 결이 같음)

### [Q7] (견고성 / 하·방어) `LevelUI` 경험치 게이지가 (max-min) 0 나눗셈을 가드하지 않음
- 위치: `LevelUI.Refresh`의 `expImage.fillAmount = (current - min) / (max - min);`(LevelUI.cs:30). `DataManager`에서
  `minScale == maxScale`로 설정되면 0 나눗셈 → `fillAmount`가 NaN/Inf가 되어 게이지가 깨진다.
- 관찰: 정상 구성에선 max>min이라 발생하지 않는 **구성 실수 방어** 수준. 아주 경미.
- 제안: `float denom = Mathf.Max(max - min, 1e-4f);`로 나눈 뒤 `Mathf.Clamp01`. 한 줄 방어.

> 요약: 이 파이프라인의 **설계(단일 출처 scale, 상태머신 큐, 원격 재실행 차단)는 견고**하다. 개선 여지는
> 주로 **네트워크 쓰기 빈도(Q1)** 와 **정리·정합(Q2 데드키, Q3 미구현 기능, Q4 닉네임 식별, Q5/Q6 폴백 이원화)** 에
> 몰려 있다. 특히 **Q4는 H6이 깔아둔 actorNumber 덕에 이제 한 줄로 닫을 수 있고(H4 동시 종결)**, Q3는
> 사용자가 실제로 체감하는 UX 공백이라 우선 검토를 권한다.

---

## 2026-07-04 루틴 — 인게임 '시야/상황인지' 계층 신규 심층 리뷰 (카메라 팔로우·크기 연출 / 미니맵 화살표 / 오프스크린 인디케이터, R1~R9 도출)

07-02 이후 신규 코드 커밋은 없다(마지막 코드 변경은 06-24 M2, 이후 06-27 O·06-30 P·07-02 Q는 docs만).
IO 브랜치 HEAD는 여전히 `9aa33f0`. 이 프로젝트의 확립된 패턴(신규 커밋이 없어도 아직 전용 심층 리뷰가 없는
시퀀스 영역을 골라 분석)을 따른다. 지금까지 진입(P)·액터 FSM(N/O)·타일/흡수 코어(K/L)·종료/결과(M)·점수/성장
파이프라인(Q)을 다뤘는데, **플레이어가 매 프레임 '세상을 지각하는' 계층 — 메인 카메라 팔로우/크기 연출,
미니맵 화살표, 화면 밖 대상 삼각형 인디케이터 —** 는 아직 전용 리뷰가 없었다(관전 관련 언급은 06-20 Push
사망 커튼 *버그 수정*뿐, 카메라 스크립트 자체의 구조 리뷰는 처음). 루틴 3대 주제 중 **아키텍처(추적 대상의
단일 출처)·버그 차단(파괴/null 대상, 디버그 잔재)·네트워크 연동(원격 액터를 따라가는 카메라/인디케이터)** 에
정면으로 닿는다. 대상: `MainCamera_Action`(팔로우+ortho 크기 큐), `TopDownCameraFollow`, `JellyCamera`(연출),
`MinimapArrowManager`/`MinimapArrow`/`MinimapFollow`, `OffScreenPlayerIndicator`, 연결부(`NetworkPlayerSync.
SetupLocalPlayer`, `PlayerExternalEventLinker`, `GameTimer`).

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3, 승인 후 적용). 게임을 즉시 깨는 크래시성
> 결함은 관찰되지 않았으나(정상 씬 구성 전제), **관찰 가능 동작 결함으로는 R3(게임 중 P키 → 화면 연출 오발동)**
> 이 가장 눈에 띈다. 효과·저위험 우선순위: **R3(디버그 P키 잔존)** ≈ **R2(미니맵 카메라 무가드 NRE 체인)** ≈
> **R6(Camera.main 무가드 반복 접근)** > R1(추적 이원화)·R5(카메라 크기 라이터 4원화, 아키텍처) >
> R4·R8(방어/레거시 확인) > R7·R9(정합/미미).

### 좋았던 점(설계 관찰)
- **`OffScreenPlayerIndicator`는 이 그룹에서 가장 잘 짜인 코드다 — 그동안 쌓인 규율이 전부 반영돼 있다.**
  추적 대상을 `FindObjectsByType`가 아니라 **`EntityRegistry`(단일 출처, dirty-스냅샷, GC 무할당)** 로 조회하고
  (cs:108·127), 탈락/흡수 제외는 `IsOutOfPlay` 단일 헬퍼(G6)로(cs:114·131), 봇 색은 **sharedMaterial**로
  읽어(G4) 배칭을 안 깬다(cs:225-227). 인디케이터는 딕셔너리+큐로 **풀링**되고(cs:238·284), 카메라 뒤
  대상은 `WorldToScreenPoint`가 뒤집는 좌표 대신 카메라 right/up 축 투영으로 실제 방향을 다시 구한다(cs:165-170).
  런타임 부트스트랩(`RuntimeInitializeOnLoadMethod`)+`DontDestroyOnLoad` 싱글턴이라 씬 와이어링도 필요 없다.
  **다른 추적 코드(미니맵)가 따라야 할 기준점이다(R1의 근거).**
- **사망=관전을 '오브젝트를 살려둔 채 입력만 차단'으로 구현해 카메라 타겟이 뜨지 않는다.** `GameModeManager.
  GameOver`가 Push/엔딩 시퀀스 경로에서 로컬 플레이어를 파괴하지 않고 `playerController.enabled=false`만
  건다(cs:542·563). 그래서 `MainCamera_Action.target`(SetupLocalPlayer에서 1회 지정, NetworkPlayerSync.cs:127)이
  **가리키는 트랜스폼이 계속 살아 있어** 카메라가 '내 시체' 시점에서 자연스럽게 관전으로 이어진다. 즉 "사망 시
  카메라 타겟 null → 프리즈"는 이 설계에선 발생하지 않는다(별도 관전 카메라 핸드오프가 없어도 되는 이유).
- **카메라 크기 연출(이벤트 경로)이 큐로 직렬화돼 있다.** 흡수/축소로 들어온 크기 변화량을 `cameraSizeQueue`에
  넣고 `ProcessCameraQueue`가 하나씩, **코루틴 시작 시점의 실측 orthographicSize를 기준으로** 목표를 잡아
  순차 처리한다(MainCamera_Action.cs:100-117). 연속 성장 시 크기 튐을 줄이는 좋은 구조 — 다만 이 큐를 *우회하는*
  경로가 따로 있어 R5로 이어진다.

### [R2] (버그·NRE / 중) 미니맵 카메라 타겟 지정이 무가드 체인 — 태그/컴포넌트 부재 시 매 스캔 NRE로 화살표 갱신 전체 중단
- 위치: `MinimapArrowManager.ScanAndRefresh` 안, 로컬 플레이어를 처음 발견했을 때
  `GameObject.FindGameObjectWithTag("MinimapCamera").GetComponent<TopDownCameraFollow>().target = player.transform;`
  (cs:94). **세 단계가 전부 무가드**다 — (1) "MinimapCamera" 태그 오브젝트가 없으면 `FindGameObjectWithTag`가
  null, (2) 그 오브젝트에 `TopDownCameraFollow`가 없으면 `GetComponent`가 null, (3) 둘 중 하나라도 null이면 여기서
  NRE.
- 왜 문제인가(학습 포인트): 이 줄은 0.5초마다 도는 `ScanAndRefresh`(cs:78) 안에 있고, NRE가 나면 **그 뒤의 봇
  화살표 생성 루프(cs:101-111)까지 통째로 스킵**된다 → 태그를 안 붙였거나 씬을 바꾼 순간부터 미니맵이 영영
  안 채워진다. 게다가 같은 파일이 바로 아래에서는 `arrowPrefab`(cs:119)·`MinimapArrow` 컴포넌트(cs:141·147)를
  꼬박꼬박 null 가드하는데 **정작 이 카메라 체인만 안 한다** — 규율 불일치. `NetworkPlayerSync`가 같은 일을
  할 때는 `Camera.main?.GetComponent<TopDownCameraFollow>()`로 안전하게 처리(cs:123)하는 것과도 대비된다.
- 추가 구조 문제: 이 "미니맵 카메라를 로컬 플레이어에 붙인다"는 **한 번만 하면 되는 1회성 배선**인데, 매 0.5초
  스캔 루프의 `if (!_arrows.ContainsKey(tf))` 분기 안에 얹혀 있어(사실상 로컬 화살표를 처음 만들 때 한 번 실행)
  위치가 어색하고 의도가 흐릿하다.
- 제안: `var mc = GameObject.FindGameObjectWithTag("MinimapCamera"); var follow = mc ? mc.GetComponent<
  TopDownCameraFollow>() : null; if (follow != null) follow.target = player.transform;`로 가드하고, 가능하면
  이 배선을 스캔 루프 밖(로컬 플레이어 등록 시 1회)으로 빼낸다. **동작 불변, 크래시 경로 제거.**

### [R3] (버그·디버그 잔재 / 중) `JellyCamera.Update`에 P키 디버그 트리거가 프로덕션에 그대로 남음 — 게임 중 P 누르면 화면 왜곡 연출 오발동
- 위치: `JellyCamera.Update`의 `if (Input.GetKeyDown(KeyCode.P)) PlayDing();`(cs:57-60). `PlayDing`은 렌즈
  왜곡(Lens Distortion) 꿀렁임 + FOV 펀치 + 카메라 Z축 기우뚱 회전을 1.5초간 연출한다(cs:65-105).
- 왜 문제인가: 인게임에서 **아무 때나 P를 누르면 실제로 화면이 출렁인다.** 주석(cs:56)도 "P키를 누르면 효과
  확인 가능"이라 명시한 **개발용 테스트 코드**다. 특히 인접 키(O/L 등) 오입력이나 채팅/닉네임 입력이 없는
  구성에서 조작 중 우발적으로 발동할 수 있다. 이는 N1/G3/K3에서 정리해 온 "디버그 잔재를 빌드에 남기지 않는다"
  규약의 **입력(Input) 버전**이다.
- 제안: 최소 `#if UNITY_EDITOR`로 감싸거나(에디터에서만 테스트), 아예 제거하고 `[ContextMenu("Play Ding
  Effect")]`(cs:64, 이미 존재)로만 테스트한다. **런타임 동작에서 오발동 경로 제거.**

### [R6] (버그·NRE·성능 / 중) `MainCamera_Action`이 `Camera.main.orthographicSize`를 곳곳에서 무가드로 반복 접근
- 위치: `ProcessCameraQueue`(cs:109), `OnScaleChanged_Co` 2종(cs:127·131·153·157), `ChangeCameraSizeToLevel`
  (cs:137), `GameFailSizeChange`(cs:162·164)가 모두 `Camera.main.orthographicSize`를 **null 가드 없이** 읽고
  쓴다. 반면 같은 파일의 `SetOrthographicSizeDirect`(cs:52-54)는 `if (Camera.main != null)`로 가드한다 — 한
  클래스 안에서 규율이 갈린다.
- 왜 문제인가(학습 포인트): `Camera.main`은 단순 참조가 아니라 **매 호출 "MainCamera" 태그를 씬에서 스캔**하는
  비용 있는 API고, 메인 카메라가 비활성/파괴(씬 전환 순간)면 **null을 반환**한다. 크기 연출 코루틴은 여러 프레임
  살아 있으므로, 연출 도중 씬이 바뀌면 `Camera.main.orthographicSize`에서 매 프레임 NRE가 날 수 있다. 또 코루틴
  루프 안에서 `Camera.main`을 반복 조회(cs:127·153)하는 건 불필요한 태그 스캔 반복이다.
- 제안: `Start`/`Awake`에서 카메라 참조를 한 번 캐시(`_cam = GetComponent<Camera>()` 또는 `Camera.main`)하고,
  코루틴은 캐시된 `_cam`을 null 가드 뒤에 쓴다. **연출 품질 불변, 씬 전환 크래시 경로 제거 + 태그 스캔 절감.**

### [R1] (아키텍처·성능 / 중) 액터 추적이 이원화 — 미니맵은 `FindObjectsByType`, 인디케이터는 `EntityRegistry`
- 위치: `MinimapArrowManager.ScanAndRefresh`가 0.5초마다 `FindObjectsByType<NetworkPlayerSync>` +
  `FindObjectsByType<AIPlayerMovement>`로 씬 전체를 스캔한다(cs:81·101). 반면 `OffScreenPlayerIndicator`는
  같은 "씬의 모든 플레이어/봇" 질문을 **`EntityRegistry.Players`/`Bots`**(단일 출처, dirty-스냅샷)로 답한다
  (cs:108·127). 즉 동일한 데이터에 대해 **두 개의 서로 다른 조회 경로**가 공존한다.
- 왜 문제인가: `FindObjectsByType`는 씬 오브젝트를 순회하는 무거운 API고, 미니맵은 이를 0.5초마다 두 번
  돌린다. `EntityRegistry`(N/O 루틴에서 정착)는 Register/Unregister로 이미 최신 목록을 GC 없이 유지하므로,
  미니맵도 이걸 쓰면 스캔 비용이 사라지고 "액터 목록의 단일 출처"가 하나로 모인다. H6(점수 단일화)·O3(먹이
  탐색 단일화)와 같은 **단일 출처 통일** 테마.
- 제안: `MinimapArrowManager`의 두 `FindObjectsByType`를 `EntityRegistry.Players`/`Bots` 조회로 교체
  (추가/파괴 감지 로직은 그대로 유지 가능). **동작 불변, 스캔 비용 제거 + 단일 출처.** (다만 미니맵 화살표는
  탈락한 봇도 계속 표시하는 게 의도일 수 있으니 `IsOutOfPlay` 필터 적용 여부는 UX 결정으로 남긴다.)

### [R5] (아키텍처·경합 / 중) 카메라 `orthographicSize`를 쓰는 주체가 4원화 — 큐를 우회하는 경로들이 서로 값 경합
- 위치: 같은 `Camera.main.orthographicSize`를 네 경로가 각자 건드린다 —
  (a) `MainCamera_Action.ProcessCameraQueue`(큐 직렬화 코루틴, cs:100),
  (b) `MainCamera_Action.ChangeCameraSizeToLevel`(**큐를 거치지 않고** 별도 코루틴 직접 시작, cs:135-143),
  (c) `MainCamera_Action.SetOrthographicSizeDirect`(이벤트로 즉시 대입, cs:51-55),
  (d) `PlayerExternalEventLinker.ChangeCameraOrthoSize`(`Camera.main.orthographicSize` 직접 대입, cs:33).
- 왜 문제인가(학습 포인트): (a)의 큐가 크기를 Lerp하는 도중 (b)/(c)/(d)가 끼어들면 **두 코루틴/대입이 같은
  변수를 두고 싸운다.** 예: 성장으로 큐 연출이 도는 중 레벨 변경(b)이 겹치면, 두 `OnScaleChanged_Co`가 서로
  다른 목표로 동시에 Lerp를 써 카메라 크기가 튀거나 최종값이 비결정적이 된다. `ProcessCameraQueue`가 큐로
  직렬화를 애써 해놨는데 (b)가 그 큐를 우회하는 게 근본 원인이다. `this.targetSize`/`currentSize` 필드(cs:18-19)도
  코루틴 간 공유 상태로 쓰여 경합을 키운다(대부분 로컬 `targetSize`에 가려진 사실상 데드/혼란 필드).
- 제안: **모든 크기 변경을 단일 큐(또는 단일 "목표 크기" 상태 + 단일 러너 코루틴)로 통일**한다. `ChangeCameraSizeToLevel`도
  큐에 목표를 넣게 하고, 즉시 대입 경로(c/d)는 진행 중 코루틴을 `StopCoroutine`으로 정리한 뒤 값을 세팅한다.
  공유 필드 `currentSize`/`targetSize`는 코루틴 인자로 넘겨 상태 공유를 없앤다. **연출 결정성/일관성 상승.**

### [R4] (견고성·NRE / 하) `JellyCamera`가 `globalVolume`/`lensDistortion` null을 가드하지 않음
- 위치: `Start`의 `if (globalVolume.profile.TryGet(out lensDistortion))`(cs:48) — `globalVolume`이 인스펙터에서
  비어 있으면 여기서 NRE. 또 `TryGet`이 false여도(Volume에 Lens Distortion 미추가) `lensDistortion`은 null인데,
  이후 `PlayDing`이 `lensDistortion.intensity.value`(cs:73·86-97)를 무조건 만져 NRE.
- 왜 문제인가: 주석(cs:31-33)이 "건드리지 마세요/꼭 추가돼 있어야"라고 경고하는 건 역으로 **구성 실수 시
  런타임에서 조용히 죽는다**는 뜻이다. 연출 하나 빠지는 것과 NRE로 카메라 스크립트가 죽는 건 무게가 다르다.
- 제안: `Start`에서 `globalVolume == null`이면 조기 반환(경고 로그 1회), `TryGet` 실패 시 `lensDistortion`
  null을 기억해 `PlayDing`에서 렌즈 왜곡 시퀀스만 스킵(FOV/회전 연출은 유지). **정상 구성 동작 불변, 오구성 방어.**

### [R8] (관찰·레거시 / 하) `GameTimer.GameFail` 종료 경로가 전부 무가드 — 멀티 흐름 사용 여부 확인 필요 (K4 연장선)
- 위치: `GameTimer.GameFail`(cs:62-88)이 `playerController.enabled`·`softBody3D.DisableCloth()`·`mainCamera_Action.
  GameFailSizeChange()`·`resultStarsUI.SetStarIndex(0)`·`playerAnimController.SetTrigger`·`PlaySFXAudio.Instance.
  *`를 **모두 null 가드 없이** 호출하고, `Time.timeScale = 0f`로 시간을 멈춘다.
- 왜 문제인가: 이 경로는 `limitTime`(기본 300초) 카운트다운이 0이 되면 도는 **싱글플레이형 타임아웃 종료**로,
  멀티플레이 종료를 총괄하는 `GameModeManager`(위 '좋았던 점' 참조)와 **별개**다. K4에서 이미 `GameTimer.GameFail`의
  `timeScale=0` 후 무가드 호출로 인한 소프트프리즈를 레거시로 도출했었다. 참조 필드 중 하나라도 인스펙터
  미할당이면 `timeScale=0`이 걸린 채 NRE → **입력·시간 영구 정지(소프트프리즈)**.
- 제안: 먼저 이 컴포넌트가 현재 멀티 씬에서 실제로 활성/사용되는지 확인한다. 사용 안 하면 K4와 묶어 데드코드
  정리, 사용하면 각 참조 null 가드 + `timeScale` 복원 보장. **사용 여부는 설계 결정(사용자 확인) 대상.**

### [R7] (일관성·인코딩 / 하) `TopDownCameraFollow.cs`·`GameTimer.cs` 한글 주석 mojibake — M4/N6와 동일 테마
- 위치: `TopDownCameraFollow.cs`의 Header/Tooltip·본문 주석 전체(cs:5-34)와 `GameTimer.cs` 다수 주석(cs:21·27·33·64
  등)이 인코딩이 깨져(`����…`) 읽을 수 없다. 특히 `TopDownCameraFollow`는 인스펙터에 보이는 `[Header]`/`[Tooltip]`
  문자열까지 깨져 **에디터에서 필드 설명이 글자 깨짐으로 뜬다.**
- 제안: M4(NextSceneManager/ResultStarsUI)·N6(플레이어 FSM)와 **한 번에 묶어 UTF-8로 재작성.** 동작 무관, 가독성/에디터 UX만.

### [R9] (성능·정합 / 하·미미) `MinimapArrow.SetColor`가 `r.material.color`로 인스턴스 머티리얼 복제 (G3/G4/K1 테마)
- 위치: `MinimapArrow.SetColor`가 SpriteRenderer는 `.color` tint로 안전하게 처리하면서(cs:31-34), 일반 Renderer는
  `r.material.color = color`로 대입(cs:41)해 **화살표마다 머티리얼 인스턴스를 복제**한다. 주석(cs:36)도 "인스턴스
  머티리얼 자동 생성"이라 인정한다.
- 왜 문제인가: G3/G4/K1에서 반복 정리해 온 `.material`(인스턴스 복제)→`sharedMaterial`/`MaterialPropertyBlock`
  전환 규약의 미적용 경로다. 화살표 수가 적어(≤플레이어+봇) 영향은 미미하지만, **색을 tint하려는 의도**면
  MaterialPropertyBlock이 정석이고 배칭도 안 깬다.
- 제안: MeshRenderer 경로도 MaterialPropertyBlock(`_BaseColor`/`_Color` 자동 선택, K1과 동일 패턴)으로 tint.
  **표시 불변, 인스턴스 복제 제거.** (미니맵 화살표가 스프라이트 기반이면 이 경로 자체가 죽은 분기일 수 있어 확인 겸.)

> 요약: 이 '시야/상황인지' 계층은 **최신 코드(`OffScreenPlayerIndicator`)가 그동안 쌓은 규율(EntityRegistry
> 단일 출처·G6·G4·풀링)을 모범적으로 반영**한 반면, **오래된 코드(`MinimapArrowManager`·`MainCamera_Action`·
> `JellyCamera`)는 그 규율 이전 상태**로 남아 대비가 크다. 개선 여지는 **① 크래시 경로(R2 무가드 체인, R6
> Camera.main, R4 Volume) ② 디버그 잔재(R3 P키) ③ 단일 출처/경합(R1 추적 이원화, R5 크기 라이터 4원화)** 에
> 몰려 있고, 방향은 이미 코드베이스가 확립한 패턴(EntityRegistry·null 가드·큐 직렬화·sharedMaterial)으로 **낡은
> 쪽을 끌어올리는 것**으로 일관된다. 사망=관전 카메라 설계는 견고하니 손댈 필요 없다.

---

## 2026-07-07 루틴 — 네트워크 '상태 동기화' 계층 신규 심층 리뷰 (Transform·스케일·색상·흡수판정 직렬화 / RPC 권위 흐름, S1~S10 도출)

07-04 이후 신규 코드 커밋은 없다(마지막 코드 변경은 06-24 M2, 이후 O·P·Q·R은 docs만). IO 브랜치 HEAD는 여전히
`c126760`. 확립된 패턴(신규 커밋이 없어도 아직 전용 심층 리뷰가 없는 시퀀스 영역을 골라 분석)을 따른다. 지금까지
진입(P)·액터 FSM(N/O)·타일/흡수 코어(K/L)·종료/결과(M)·점수/성장(Q)·시야(R)를 다뤘는데, **이 모든 시퀀스를
클라이언트 사이로 실어 나르는 '상태 동기화 계층' 자체 — 플레이어/봇의 Transform·스케일·색상 직렬화, 흡수/공격
판정의 MasterClient(MC) 권위 흐름, 젤리 스폰/삭제 동기화 —** 는 전용 리뷰가 없었다(네트워크는 F1·G2·H2·J2·L1·
N4·O2·P1 등에서 *부분적으로* 닿았을 뿐, 직렬화·권위 판정 파이프라인을 통째로 본 적은 처음). 루틴 3대 주제 중
사용자가 명시한 **"안정적인 네트워크 연동"** 에 정면으로 닿는 회차다. 대상: `NetworkPlayerSync`(플레이어 동기화+
흡수/대쉬/배트 판정 RPC), `AIPlayerSync`(봇 룸 프로퍼티), `AIPlayerMovement`(봇 흡수/넉백 RPC+IPunObservable),
`NetworkJellyManager`(젤리 스폰/삭제), `NetworkNavMeshHelper`+`WanderingAI`(젤리 Transform 직렬화).

> 모두 **도출만** 했고 게임 코드는 수정하지 않았다(루틴 작업흐름 2·3, 승인 후 적용). 게임을 즉시 깨는 크래시성
> 결함은 관찰되지 않았으나, **관찰 가능 동작 결함으로는 S1(흡수 거부된 봇 영구 차단)·S2(원격 보간값으로 점수
> 계산)** 이 가장 눈에 띈다. 효과·저위험 우선순위: **S1(거부봇 영구차단, 버그)** ≈ **S2(점수/성장 권위값 오류)**
> > S3(리스폰 미배선, 데드/설계)·S4(봇 스케일 이중채널) > S5(색상 상시 스트리밍)·S6(위치 재검증 부재) >
> S7(흡수경로 이원화)·S8(RPC null가드) > S9·S10(마이그레이션 관찰·로그 잔재).

### 좋았던 점(설계 관찰)
- **흡수·공격 판정을 MasterClient(MC)로 모으는 '권위 서버' 패턴이 일관되게 자리잡았다.** 플레이어가 상대를
  트리거하면 `RPC_Request*Validation`을 **`RpcTarget.MasterClient`** 로만 보내고(NetworkPlayerSync.cs:345·357),
  MC가 크기를 비교해 통과 시에만 `RPC_Get*Absorbed`/`RPC_*AbsorbConfirmed`를 되쏜다(cs:456-500). 클라이언트가
  스스로 "내가 먹었다"를 선언하지 못하게 막는 정석 구조 — 경합/치팅 표면을 MC 한 곳으로 좁힌다.
- **크기 비교의 단일 출처가 `GetAuthorityScale` 헬퍼로 통일돼 있다.** `Owner.CustomProperties["Scale"]`(권위) →
  없으면 `transform.localScale.x`(폴백) 순으로 읽는다(cs:625-631). 보간 중인 로컬 스케일이 아니라 **동기화된
  권위값**으로 승패를 가리려는 의도가 분명하다(그래서 S2가 더 뼈아프다 — 정작 보상 계산만 이 헬퍼를 안 쓴다).
- **넉백을 '소유자에게만' 보내 비마스터 지터를 막는다.** 봇 위치는 MC가 `PhotonTransformView`로 권위 동기화하므로,
  넉백 RPC를 `RpcTarget.All`이 아니라 `aiBot.photonView.Owner`(=MC)에게만 보낸다(cs:739·919). 주석(cs:737·916)이
  "All로 보내면 비마스터에서 transform이 수신값과 충돌해 지터/되감김"이라 이유까지 남겼다 — 네트워크 권위 규율을
  잘 이해한 코드. (단 배트 대(對)플레이어 넉백은 victim의 Owner로 보내 대칭. cs:817)
- **`NetworkJellyManager`가 로컬 추적목록이 아닌 `EntityRegistry` 실개수 기준으로 부족분만 보충한다**(cs:130-133·
  103·118). 마스터 교체로 새 마스터의 `_spawnedJellies`가 비어도 씬의 실제 젤리를 세어 과다 생성을 막는다 —
  R1에서 칭찬한 EntityRegistry 단일 출처가 여기서도 마이그레이션 견고성으로 쓰인다.
- **`AIPlayerSync`가 `IPunInstantiateMagicCallback`으로 ViewID 타이밍 버그를 회피한다**(cs:19-22). `Start`에서
  ViewID를 읽으면 PUN 초기화 순서 때문에 0이 나와 봇 이름표가 "AI 봇 0"으로 뭉개지던 문제를, ViewID가 확정된
  시점 콜백으로 옮겨 정면 해결했다. 주석에 원인까지 기록(cs:15-18) — 좋은 학습 흔적.

### [S1] (네트워크·버그 / 중) `_absorbedBotIds` 사전 등록 — MC가 흡수를 거부해도 그 봇은 영구히 다시 못 먹음
- 위치: `NetworkPlayerSync.OnTriggerEnter`의 봇 분기(cs:350-358). 봇과 겹치면 **MC 검증 RPC를 보내기 *전에***
  `_absorbedBotIds.Add(botId)`로 먼저 등록하고(cs:355), 다음부터 같은 봇은 `_absorbedBotIds.Contains(botId)`에서
  조기 반환된다(cs:354).
- 왜 문제인가(학습 포인트): 이 HashSet의 의도는 "이미 먹은 봇을 중복 처리하지 않기"인데, **실제로 먹었는지(=MC가
  승인했는지) 확인하기 전에** 넣어버린다. `RPC_RequestBotAbsorbValidation`(cs:475-500)은 세 갈래다 — (a) 봇이 더
  크면 *내가* 흡수당함, (b) 내가 더 크면 봇 흡수 확정, (c) **크기가 같으면(`botScale > playerScale`도 `playerScale
  > botScale`도 아님) 아무 일도 안 일어남**. (c)에서는 흡수가 성립 안 했는데도 botId는 세트에 남는다. 그 뒤 내가
  성장해 그 봇보다 커져서 다시 부딪혀도 → `Contains`에서 막혀 **영원히 그 특정 봇을 못 먹는다.** 흡수 모드의 핵심
  루프(작은 상대를 먹어 성장)가 특정 대상에 대해 조용히 깨지는 상태다.
- 대비: 확정 경로 `RPC_BotAbsorbConfirmed`가 이미 `_absorbedBotIds.Add(botViewID)`를 한다(cs:509). 즉 **확정 시점에
  넣는 코드가 따로 있는데도** 트리거 시점에서 미리 넣어 중복이자 버그를 만든다.
- 제안: cs:355의 사전 `Add`를 제거하고, "요청 진행 중" 중복만 막으려면 별도의 *임시* pending 집합(성공/실패 응답
  수신 시 제거)을 쓰거나, 짧은 재시도 쿨다운으로 대체한다. 확정 등록은 `RPC_BotAbsorbConfirmed`(cs:509)에만 둔다.
  **정상 흡수 동작 불변, '거부된 봇 영구 차단' 버그 제거.** (동일 프레임 중복 트리거 폭주 방지는 pending으로 커버)

### [S2] (네트워크·버그 / 중) `RPC_GetAbsorbed`가 점수·성장을 원격 사본의 *보간 중* `transform.localScale.x`로 계산 — 권위 Scale 미사용
- 위치: `RPC_GetAbsorbed`의 흡수자 보상 계산(cs:538-545). 흡수자 스케일에 **피흡수자의 크기를 더해** 예측 점수를
  구하는데, 그 피흡수자 크기를 `transform.localScale.x`(cs:539·545)로 읽는다. 이 스크립트의 `transform`은 피흡수자
  본인이고, 흡수자 클라이언트에서 이 값은 **매 프레임 `Vector3.Lerp`로 권위값을 향해 보간되는 중**이다(Update, cs:206).
- 왜 문제인가(학습 포인트): 흡수 순간 피흡수자의 화면상 로컬 스케일은 네트워크 지연 탓에 **아직 권위 Scale에
  도달하지 못한 중간값**일 수 있다. 그래서 흡수자가 얻는 점수/성장이 실제보다 작거나(따라잡기 전) 커질 수 있고,
  클라이언트마다 보간 진척이 달라 **흡수자 본인 화면에서만 보상이 어긋난다.** 바로 위 '좋았던 점'에서 본 대로
  이 코드베이스는 승패 판정엔 `GetAuthorityScale`(권위 CustomProperties)을 쓰는데, **정작 보상 계산만 보간값을
  쓴다** — 규율 불일치.
- 대비: 쌍둥이 경로인 봇 흡수 `RPC_BotAbsorbConfirmed`는 MC가 넘겨준 **권위 `botScale` 인자**로 성장한다(cs:512-517).
  플레이어 흡수 경로만 권위값 대신 로컬 보간값을 쓴다.
- 제안: cs:539·545의 `transform.localScale.x`를 `GetAuthorityScale(photonView)`(피흡수자 권위 Scale)로 교체. 더
  근본적으로는 S1과 함께, **성장/점수 확정을 MC가 계산해 흡수자에게 인자로 넘기는 방식(BotAbsorbConfirmed 패턴)**
  으로 플레이어 경로도 통일하면 클라 간 결정성이 올라간다. **정상 시 값 거의 동일, 지연 상황에서 보상 정확도 상승.**

### [S3] (아키텍처·데드코드 / 중) 플레이어 리스폰 시스템 전체가 미배선 — `_isAbsorbed`가 한 번 켜지면 리셋 경로 없음
- 위치: `respawnDelay`(cs:53), `Respawn()`(cs:589-613), `RPC_OnRespawn`(cs:615-619)이 존재하지만 **`Respawn()`을
  호출하는 코드가 프로젝트 어디에도 없다**(전역 grep 확인 — 정의부와 내부 RPC 호출뿐). `AbsorbedSequence`(cs:554)는
  피흡수자를 축소→`SetActive(false)`로 끝내고 리스폰 코루틴을 걸지 않는다.
- 왜 문제인가(학습 포인트): 필드 이름(`respawnDelay=3f`)과 `Respawn`/`RPC_OnRespawn`은 "3초 뒤 부활" 기능을
  암시하지만 **실제로는 흡수 모드에 리스폰이 없다**(흡수당하면 그대로 관전/탈락). `Respawn()` 안에서만 하는 두 가지
  중요한 리셋 — `_isAbsorbed=false`(cs:591)와 `_absorbedBotIds.Clear()`(cs:592) — 이 **한 판 안에서 절대 실행되지
  않는다.** 즉 S1의 세트도, `Update`를 통째로 스킵시키는 `if (_isAbsorbed) return`(cs:202)도 되돌릴 길이 없다.
  이는 J4(Milk 리스폰 통째 소실)·M1(ResultDataCarrier 미사용)에서 본 **"절반만 구현된/미배선 기능이 코드에 남아
  의도를 흐린다"** 테마의 네트워크판이다.
- 제안: 두 갈래 중 설계 결정이 필요하다 — **(A) 흡수 모드에 리스폰을 실제로 넣을 것인가**(그렇다면
  `AbsorbedSequence` 끝에서 `StartCoroutine`으로 `respawnDelay` 후 `Respawn()` 호출 + MC 권위 확인), **(B) 리스폰
  없음이 의도라면** `respawnDelay`/`Respawn`/`RPC_OnRespawn`을 삭제해 "부활할 것 같은" 착시를 제거. 어느 쪽이든
  **사용자 확인 대상**(게임 규칙 결정). 지금 상태는 '있지만 안 도는' 회색지대라 유지보수 함정.

### [S4] (아키텍처·네트워크 / 중하) 봇 스케일이 이중 채널로 동기화 — Room 프로퍼티(권위) + IPunObservable 스트림(시각)
- 위치: 봇의 스케일이 **두 경로로 동시에** 네트워크를 탄다 — (1) `AIPlayerSync.SyncScale`이 `Room.CustomProperties
  ["{prefix}_Scale"]`에 기록(cs:65-73), (2) `AIPlayerMovement.OnPhotonSerializeView`가 매 직렬화 틱마다
  `transform.localScale.x`를 스트림 전송(cs:761-771). 소비도 갈린다 — 크기 *판정*은 (1)을 `GetSyncedScale`로 읽고
  (AIPlayerMovement.cs:107, NetworkPlayerSync.cs:636), 원격 *시각*은 (2)의 `_networkScale`로 Lerp한다(cs:394).
- 왜 문제인가(학습 포인트): 같은 물리량(봇 크기)에 **두 개의 출처**가 생겨, 순간적으로 판정용 값과 표시용 값이
  어긋날 수 있다(스트림은 매 틱, 룸 프로퍼티는 성장 콜백 때만 갱신 — 갱신 시점이 다름). 또 스케일은 성장 순간에만
  바뀌는데 **IPunObservable로 매 틱 흘려보내는 건 대역폭 낭비**다(Q1에서 정리한 "안 바뀌는 값 반복 전송" 테마).
  Q2에서 본 "Score는 쓰기만 하고 안 읽음"과 유사하게, 여기선 두 채널이 **부분적으로 서로를 중복**한다.
- 제안: 봇 스케일의 단일 출처를 **룸 프로퍼티(권위)** 로 정하고, 원격 시각 Lerp도 `GetSyncedScale`을 목표값으로
  쓰면 `OnPhotonSerializeView`의 스케일 전송을 없앨 수 있다(봇이 스케일 외에 스트림할 게 없다면 `IPunObservable`
  자체를 관측 목록에서 뺄 수 있어 틱 비용 0). 반대로 스트림을 단일 출처로 삼는다면 `GetSyncedScale`류 판정도
  스트림 캐시를 읽게 통일. **동작 불변, 채널 이중화 해소 + 대역폭 절감.** (S2·Q1과 묶어 "권위 스케일 파이프라인
  일원화"로 다루면 좋음)

### [S5] (네트워크·성능 / 중) 플레이어 색상 4-float를 매 직렬화 틱마다 스트리밍 — 게임 중 거의 안 변하는 값 + alpha는 무의미 전송
- 위치: `NetworkPlayerSync.OnPhotonSerializeView`가 색상 r·g·b·a **네 개 float**를 매 틱 `SendNext`한다(cs:237-244).
  수신 측은 a를 받아 놓고 **무조건 1로 덮어쓴다**(`_networkColor = new Color(r,g,b,1f)`, cs:251) — 즉 alpha는
  보내나 마나다.
- 왜 문제인가(학습 포인트): `OnPhotonSerializeView`는 `PhotonView`의 SendRate(기본 초당 10~20회)로 **관측 오브젝트
  전원에 대해 지속 호출**된다. 플레이어 색은 흡수 성장으로 색조가 바뀔 때 말고는 사실상 고정인데, 이를 매 틱
  전 클라이언트에 흘려보낸다. 게다가 이 스크립트에는 이미 **게임 종료 직전 색을 룸 프로퍼티에 저장하는 `SyncColor`**
  (cs:289-301)가 있어, "변할 때만 알리는" 이벤트/프로퍼티 경로의 선례가 코드 안에 있다.
- 제안: 색상을 **변화 시에만** 전파(예: `PlayerColorVisual`이 색 확정 때 RPC 1회, 또는 스케일처럼 CustomProperties)로
  바꾸고 `OnPhotonSerializeView`의 상시 스트림을 제거. 최소한 alpha 전송(cs:243·250)은 즉시 삭제 가능(수신부가
  버리므로 완전 무손실). **표시 불변, 상시 대역폭 4→0(또는 이벤트당 3) float로 절감.** Q1/Q2의 "네트워크 데드/중복
  트래픽 정리" 테마 연장.

### [S6] (네트워크 / 중·확인필요) 흡수·대쉬·배트 MC 검증이 위치·사거리 재확인 없이 크기만 비교 — 요청 클라의 트리거를 신뢰 (N4 확장)
- 위치: `RPC_RequestAbsorbValidation`(cs:456-472)·`RPC_RequestDashHitPlayer`(cs:667)·`RPC_RequestBatHitPlayer`
  (cs:797)가 MC에서 도는데, **크기(및 Phase)만 검증**하고 두 대상이 실제로 사거리 안에 있는지는 재확인하지 않는다.
  판정의 사실상 근거는 "요청 클라가 트리거/스윙에 겹쳤다고 주장"뿐이다.
- 왜 문제인가(학습 포인트): 위치 권위는 각 소유자의 `PhotonTransformView`에 있으므로, MC는 `victimPV.transform.
  position`을 이미 알고 있다(넉백 방향 계산에 쓴다, cs:688·809). 즉 **"공격자와 피격자 거리 < 사거리"를 MC에서 한 줄
  재검증할 재료가 이미 있는데** 안 한다. 지연·경합 상황에서 공격자 화면에선 닿았지만 권위 위치로는 빗나간 히트가
  승인될 수 있고, 이론상 조작된 ViewID로 원거리 흡수/넉백을 유도할 수도 있다. N4(마스터 배트 히트 사거리 미재검증)에서
  이미 도출한 테마를 흡수/대쉬까지 확장한 것 — 이 계층 전반의 공통 약점이다.
- 제안: 각 `RPC_Request*`에서 크기 검증에 더해 **MC가 아는 권위 위치로 거리 게이트**를 추가
  (`(attackerPos - victimPos).sqrMagnitude <= (range*scale)^2`). 필요 시 약간의 관용(lag 보정)을 상수로 둔다.
  **정상 히트 동작 불변, 위치 불일치·조작 히트 차단.** 스케일이 커서 사거리가 늘어나는 배트/대쉬는 `range*scale`을
  그대로 재사용하면 되어 비용이 거의 없다. (경쟁적 정합성이 목표가 아니라면 우선순위는 낮출 수 있음 → 확인 필요)

### [S7] (아키텍처·일관성 / 하) 흡수 판정 경로 이원화 — 플레이어발(‘요청→MC검증’ RPC) vs 봇발(MC 로컬 직접) (O3 테마)
- 위치: 흡수 성립 판정이 두 파일에 서로 다른 모양으로 있다 — 플레이어가 상대와 겹치면 `NetworkPlayerSync.
  OnTriggerEnter`가 **MC에 검증 RPC를 보내고**(cs:342-358), 봇이 상대와 겹치면 `AIPlayerMovement.OnTriggerEnter`가
  **그 자리(MC)에서 크기 비교 후 바로 `RPC_GetAbsorbed`/`RPC_BotAbsorbed`를 쏜다**(cs:611-643, MC 전용 가드 cs:613).
- 왜 문제인가: 봇은 애초에 MC 소유라 봇발 트리거는 이미 MC에서 도니 "직접 판정"이 기능적으로 틀린 건 아니다.
  하지만 **같은 규칙("더 크면 먹는다")이 두 곳에 복붙**돼 있어, 규칙이 바뀌면(예: 크기차 임계값 도입, 흡수 쿨다운)
  한쪽만 고쳐 드리프트가 날 위험이 있다. O3(먹이 탐색 이원화)·H6(점수 집계 이중구현)와 같은 **단일 출처화** 테마.
  실제로 S1·S2가 "플레이어 경로에만" 존재하는 것도 이 이원화의 부산물이다.
- 제안: 흡수 성립 판정(대상 탐색은 제외, "이 둘 중 누가 먹나 + 보상은 얼마"를 정하는 코어)을 **MC에서 도는 단일
  헬퍼**로 뽑아 플레이어/봇 양쪽이 호출하게 통일. 당장의 리스크는 낮으니 S1·S2 수정 시 함께 리팩터링하는 걸 권장.

### [S8] (안정성·NRE / 하) 흡수 보상 RPC들이 `DataManager.Instance`를 null 가드 없이 역참조 (G8/L2 테마)
- 위치: `RPC_BotAbsorbConfirmed`(cs:511-513)·`RPC_GetAbsorbed`(cs:537-541)가 `DataManager.Instance`를 받아
  `dm.absorbScalePercent`·`dm.maxScale`·`dm.ScoreFromScale`을 **null 확인 없이** 호출한다. 대쉬/배트 RPC들도
  `DataManager.Instance.PushScaleThreshold`(cs:679)·`.dashPushForce`(cs:693) 등을 무가드로 쓴다.
- 왜 문제인가: 이 RPC들은 **네트워크로 도착**하므로 로컬 코드보다 타이밍을 통제하기 어렵다 — 씬 전환 직후 stale RPC가
  도착하거나 `DataManager`가 아직/이미 없을 때 NRE가 나면, 흡수 판정 도중에 그 클라이언트만 조용히 예외로 중단된다.
  G8(FallingTile의 DataManager 무가드)·L2(JellyColliderAbsorb null 미가드)·H3(결과 시퀀스 NRE)에서 반복 정리해 온
  "DataManager 등 싱글턴은 RPC/코루틴 진입점에서 가드" 규약의 미적용 경로다.
- 제안: 각 RPC 진입부에서 `var dm = DataManager.Instance; if (dm == null) return;` 한 줄. **정상 동작 불변, 경계
  타이밍의 NRE 차단.** (S2 수정 시 같은 함수를 건드리므로 묶어서 처리하기 좋다)

### [S9] (네트워크·안정성 / 하·확인필요) 젤리(`WanderingAI`)에 호스트 마이그레이션 재초기화 콜백 부재 — 봇은 이어받는데 젤리는 안 이어받음
- 위치: `WanderingAI._isMine`은 `Start`에서 `NetworkNavMeshHelper.SetupOwnership`로 **한 번만** 계산되고(cs:34), 이
  클래스는 `MonoBehaviourPun`이라 **`OnMasterClientSwitched`를 구현하지 않는다**. 반면 봇 `AIPlayerMovement`는
  `OnMasterClientSwitched`에서 `InitAndRun`을 다시 돌려 새 마스터가 제어를 이어받는다(cs:739-755).
- 왜 문제인가(학습 포인트): 현재 `NetworkJellyManager`는 젤리를 **`PhotonNetwork.Instantiate`**(룸오브젝트 아님,
  cs:150·259)로 소환한다. PUN 기본 정리(CleanupCacheOnLeave)에서 이런 오브젝트는 **소유자(마스터) 이탈 시 파괴**되고,
  새 마스터의 `SpawnRoutine`(OnMasterClientSwitched, cs:306-322)이 부족분을 다시 채운다. 그래서 `_isMine` staleness가
  당장 프리즈를 일으키진 않지만, **마스터 교체 때마다 흡수 대상 젤리가 일시에 사라졌다가 서서히 재생성**되는 흐름이
  된다(흡수 모드 체감 저하). 만약 향후 젤리를 `InstantiateRoomObject`로 바꿔 마이그레이션에서 살아남게 하면, 그
  순간 이 미구현이 **"새 마스터의 젤리가 `_isMine=false`로 남아 영영 안 움직이는"** 실버그로 승격된다(P1·O6의
  '소유권 이전 후 재초기화 누락' 테마). 지금은 관찰·잠재.
- 제안: (즉시) 마스터 교체 시 젤리 소멸→재생성이 흡수 모드 UX에 문제면 젤리를 룸오브젝트로 전환 **+** `WanderingAI`에
  `OnMasterClientSwitched`(또는 `IPunOwnershipCallbacks`)로 `_isMine` 재평가 + agent 재활성 추가. (아니면) 현
  '파괴 후 재보충'이 의도임을 주석으로 명시. **확인 필요(설계 결정).**

### [S10] (성능·로그 / 하) `RPC_BotAbsorbed`의 무가드 `Debug.Log`가 아직 남음 — O7에서 도출, 미적용 확인
- 위치: `AIPlayerMovement.RPC_BotAbsorbed`(cs:692)의 `Debug.Log(this.name + "/RPC_BotAbsorbed : AI 플레이어
  흡수됨.")`. 흡수가 일어날 때마다 **모든 클라이언트에서** 문자열 결합과 함께 로그가 찍힌다.
- 왜 문제인가: N1/G3/K3/O7에서 반복 정리한 "빌드 로그 스파이크 방지(무조건 로그 금지)" 규약의 잔존 경로다. 07-02
  Q 루틴까지 코드 변경이 없었으므로 O7(06-27 도출) 이후 그대로다 — **이번 리뷰에서 미적용 상태 재확인.**
- 제안: 제거하거나 `[Conditional("UNITY_EDITOR")]` 로그 래퍼로 감싼다. O7·N1과 **한 번에** 로그 규약 일괄 정리
  권장. **동작 불변.**

> 요약: 이 '상태 동기화' 계층은 **권위 구조(MC 검증)·크기 판정 단일 헬퍼·넉백 소유자 타겟팅·EntityRegistry 기반
> 젤리 보충**처럼 네트워크 규율의 좋은 뼈대를 이미 갖췄다. 개선 여지는 뼈대가 아니라 **① 그 권위 규율을 일부
> 경로가 안 지키는 데**(S2 보상만 보간값, S6 위치 재검증 부재)와 **② 절반만 구현/이중화된 상태 경로**(S1 사전등록
> 버그, S3 리스폰 미배선, S4 봇 스케일 이중채널, S5 색상 상시 스트리밍)에 몰려 있다. 방향은 R까지와 동일하게
> **이미 코드베이스가 확립한 패턴(권위값 GetAuthorityScale·이벤트/프로퍼티 전파·단일 출처)** 으로 예외 경로를
> 끌어올리는 것으로 일관된다. S1·S2는 흡수 모드 코어 루프에 직접 닿으니 우선 검토를 권한다.

---

## 2026-07-09 루틴 — 젤리 소프트바디(Cloth) 물리 계층 신규 심층 리뷰 (SoftBody3D 생명주기 · 스케일 연동 · 재빌드 경합, T1~T7 도출)

이번 루틴은 지금까지 한 번도 깊게 안 본 **게임의 시그니처 시스템, 젤리 소프트바디 물리**를 대상으로 했다.
현행 젤리 출렁임은 Unity 내장 `Cloth`(천 시뮬레이션)를 `SkinnedMeshRenderer` 위에 얹어 구현하며,
핵심 스크립트는 `Assets/Scripts/JellyMesh/SoftBody3D.cs`(181줄) 하나다. 이 컴포넌트는 스스로 물리를
돌리지 않고 **Cloth의 파라미터/제약(coefficients)을 게임 상황에 맞춰 켜고·끄고·다시 만드는 "관리자"**
역할만 한다. 그래서 이 계층의 위험은 대부분 *물리 계산 자체*가 아니라 **생명주기(언제 끄고 언제 다시
만드느냐)와 다른 시스템(스케일 애니메이션·네트워크·결과 전환)과의 타이밍 경합**에 있다.

리뷰 범위: `SoftBody3D.cs` 전체 + 이를 부르는 5개 호출부
(`PlayerScaleController`(성장/축소 시 Disable→Rebuild), `AIPlayerMovement`/`NetworkPlayerSync`(원격·봇에서 RemoveCloth),
`GameTimer.GameFail`(사망 시 DisableCloth), `GameResultManager`(결과 씬에서 enabled=false)) + 레거시 `JellyMesh_Legacy/`.

### 좋았던 점(설계 관찰)
- **역할 분리가 명확하다.** SoftBody3D는 "천을 어떻게 다룰지"만 알고, "언제 성장하는지"는 PlayerScaleController가
  결정해 `DisableCloth()`/`RequestRebuildCloth()`만 호출한다. 스케일 로직과 물리 로직이 이벤트 경계로 나뉘어 있어
  읽기 쉽다. 특히 성장 애니메이션(`transform.localScale` Lerp)이 도는 동안 Cloth를 꺼서 천이 스케일과 싸우며
  찌그러지는 것을 막으려는 **의도**가 분명하다(주석에도 "스케일 동기화와 충돌하여 모델 찌그러짐 방지").
- **원격/봇 사본은 Cloth를 통째로 제거**(`RemoveCloth`)해 스케일·애니메이션 동기화만 신뢰하고, 그림자 유지를 위해
  `updateWhenOffscreen=true`로 돌려놓는 처리가 세심하다. 물리를 로컬에서만 돌리는 것은 네트워크 게임의 정석이다.
- **결과 씬 정리(GameResultManager)에서 Cloth를 `DestroyImmediate` 대신 `enabled=false`로만** 끄는 이유를
  주석으로 남겨뒀다("Cloth를 DestroyImmediate하면 SkinnedMeshRenderer 데이터가 오염됨"). 실제로 Unity에서 겪기 쉬운
  함정을 이미 학습해 회피한 흔적이라 좋다.
- **재빌드를 `SetActive(false)` 없이** 하도록 코루틴을 짜서 렌더러 깜빡임을 피한 점(주석: "렌더러는 끄지 않아 깜빡임 방지")도
  경험에서 나온 좋은 판단이다.

아래는 그 위에서 발견한 구조적 개선점이다. **T1이 이번 계층의 핵심(시그니처 연출이 첫 성장 이후 손상)** 이고,
T2·T3는 그와 얽힌 재빌드 타이밍 경합이다.

### [T1] (버그·정합 / 중·확인필요) Cloth 재빌드가 **에디터에서 손으로 칠한 softness 맵을 잃어버림** — 첫 성장 이후 시그니처 연출 손상

이 컴포넌트의 하이라이트 기능은 `useHybridSoftness`다. 툴팁에 적혀 있듯 *"에디터에서 칠한 값(0인 부분)은 유지하고,
나머지만 Softness로 제어"* 하는 것 — 즉 젤리의 어떤 부분(예: 얼굴·눈)은 딱딱하게 고정하고 몸통만 출렁이게 하는,
**손으로 칠한 제약 맵**이 핵심이다. 이 맵은 `_initialCoefficients`에 담긴다.

문제는 **이 맵을 캡처하는 시점이 두 곳인데, 두 번째가 잘못된 값을 담는다**는 것이다.

- `InitCloth()`(게임 시작): `_initialCoefficients = _cloth.coefficients;` (SoftBody3D.cs:75)
  → 여기서 `_cloth`는 **에디터에서 직렬화된**(손으로 칠한) Cloth 컴포넌트다. 올바른 값이 담긴다.
- `EnableAndRebuildCloth()`(스케일 변경마다): 기존 Cloth를 `Destroy`하고 `AddComponent<Cloth>()`로 **새 Cloth**를
  만든 직후 다시 `_initialCoefficients = _cloth.coefficients;` (SoftBody3D.cs:171-173)
  → 새로 붙인 Cloth의 coefficients는 **에디터에서 칠한 값이 아니라 Unity가 자동 생성한 기본값**이다.
  칠한 제약 맵은 Destroy된 원본 컴포넌트와 함께 사라졌다.

**결과:** 재빌드는 성장/축소가 끝날 때마다(`ScaleTo` 끝의 `RequestRebuildCloth`) 일어난다. 따라서 **첫 성장 직후부터**
`_initialCoefficients`가 기본값으로 바뀌고, `UpdateSoftness`의 하이브리드 분기
(`if (_initialCoefficients[i].maxDistance < softness) 유지 else softness`)가 "칠한 부분 유지"를 못 하게 된다.
딱딱해야 할 부분(눈·입 등)까지 균일하게 출렁이기 시작한다 — **게임을 상징하는 젤리 연출이 한 번 성장하면 무너지는** 셈.

- 근거: SoftBody3D.cs:75(원본 캡처) vs :171-173(새 Cloth 계수 재캡처 — 덮어씀).
- 학습 포인트: Unity에서 `Cloth`의 "칠한 제약(constraint painting)"은 **컴포넌트에 직렬화**된다. 그 컴포넌트를 Destroy하면
  칠한 값도 함께 사라지고, 새로 `AddComponent`한 Cloth는 메시로부터 기본 coefficients를 자동 생성할 뿐이다.
- 제안(미적용): `_initialCoefficients`를 **InitCloth에서 한 번만** 캡처하고 재빌드 시에는 재캡처하지 말 것
  (멤버로 보존 → 새 Cloth에 `_cloth.coefficients = _initialCoefficients`로 다시 적용). 단 재빌드된 Cloth의
  정점 순서/개수가 원본과 동일한지(동일 SkinnedMesh이므로 일반적으로 동일) **에디터 실측 확인 필요**.
  ※ 그런데 애초에 "성장할 때마다 Cloth를 통째로 재생성"하는 설계가 꼭 필요한지도 함께 재검토 대상(→ T2/T3).

### [T2] (버그·경합 / 중·확인필요) 큐로 연속된 스케일 변경 시 **재빌드 코루틴이 다음 애니메이션 도중 Cloth를 다시 켜** 찌그러짐 방지가 무너짐

`ScaleTo`는 **시작에 `DisableCloth()`, 끝에 `RequestRebuildCloth()`** 를 부른다(PlayerScaleController.cs:106, 130).
그런데 `RequestRebuildCloth`는 즉시 끝나지 않는 **fire-and-forget 코루틴**(`EnableAndRebuildCloth`, 2프레임 대기 후
새 Cloth 생성·enable)이다. 반면 `ProcessScaleQueue`(:147-155)는 한 `ScaleTo`가 끝나면 **곧바로 다음 `ScaleTo`를 꺼내
실행**한다. 이 둘의 타이밍이 어긋난다:

1. ScaleTo#1 끝 → `RequestRebuildCloth` → `EnableAndRebuildCloth` 시작: `_isRebuilding=true`, 기존 Cloth `Destroy`,
   `yield 2프레임` 대기 진입.
2. ProcessScaleQueue가 곧바로 ScaleTo#2를 실행 → ScaleTo#2가 `DisableCloth()`를 부르지만, 방금 Destroy된 `_cloth`는
   이미 **Unity의 (가짜)null**이라 `if (_cloth != null)` 가드에 걸려 **아무 것도 못 끈다**.
3. ~2프레임 뒤 EnableAndRebuildCloth가 깨어나 **새 Cloth를 AddComponent + enable** → 이때 ScaleTo#2는 한창
   `transform.localScale`을 Lerp하는 중 → **스케일 애니메이션 도중에 Cloth 시뮬레이션이 켜진다** → DisableCloth로
   막으려던 바로 그 "스케일과 천이 싸워 찌그러지는" 증상이 재현된다.

젤리를 **빠르게 연속 흡수**(성장 큐가 쌓이는 흔한 상황)할 때 발생한다. AIPlayerMovement/NetworkPlayerSync 주석이 말하는
"스케일 동기화와 충돌하여 모델 찌그러짐"과 정확히 같은 증상이, 로컬 큐에서도 재현되는 경로다.

- 근거: PlayerScaleController.cs:106/130 + :147-155 + SoftBody3D.cs:141-181.
- 제안(미적용): 재빌드를 **ScaleTo 끝마다가 아니라 스케일 큐가 완전히 빈 시점**(ProcessScaleQueue의 while 종료 직후)에
  한 번만 수행하도록 이동. 성장 도중에는 Cloth를 계속 꺼둔 채로 두고, 모든 연속 성장이 끝난 뒤 딱 한 번 rebuild.
  이러면 T1의 "성장마다 재캡처" 빈도도 크게 줄어 두 문제를 동시에 완화한다. **확인 필요**(연출 체감 실측).

### [T3] (안정성 / 중하) `DisableCloth`/`RemoveCloth`가 **진행 중인 rebuild 코루틴을 취소하지 않아** 꺼야 할 때 Cloth가 되살아남

T2의 근본 원인이자 별도로도 위험하다. `DisableCloth()`는 `_cloth.enabled=false`만, `RemoveCloth()`는 `Destroy`만 한다.
둘 다 **이미 돌고 있는 `EnableAndRebuildCloth` 코루틴을 멈추지 않는다.** 그래서 "이제 Cloth를 꺼야 하는" 이벤트
(사망·결과 전환 등)가 rebuild 코루틴의 2프레임 대기 사이에 끼면, 코루틴이 깨어나 **Cloth를 다시 만들고 enable**해버린다.

특히 `GameTimer.GameFail`은 `Time.timeScale = 0f` 직후 `softBody3D.DisableCloth()`를 부르는데(GameTimer.cs:64,75),
`EnableAndRebuildCloth`의 대기는 `WaitForSeconds`가 아니라 **`yield return null`(프레임 단위)** 이라 `timeScale=0`에도
계속 진행된다 → **사망 처리 직후 rebuild가 완료돼 Cloth가 다시 켜질 수 있다.**

- 근거: SoftBody3D.cs:118-122 / :124-139 vs :148-181; GameTimer.cs:75.
- 제안(미적용): `DisableCloth`/`RemoveCloth` 진입부에 `if (_isRebuilding) { StopCoroutine(...); _isRebuilding=false; }`
  추가(코루틴 핸들 보관 필요). "끄기"가 "다시 만들기"를 항상 이긴다는 불변식을 코드로 못박기.

### [T4] (코드품질·성능 / 하) `Update`가 매 프레임 Cloth 파라미터 5종 + `useGravity=true`를 무조건 재기록 → `ApplyClothSettings`의 `useGravity=false`가 죽은 대입 + 두 경로 모순

`Update()`(SoftBody3D.cs:45-61)는 인스펙터 값이 안 바뀌어도 **매 프레임** `damping/stretchingStiffness/bendingStiffness/
worldVelocityScale/worldAccelerationScale`를 Cloth에 대입한다(젤리가 여럿이면 자잘한 낭비). 더 큰 문제는 **일관성**이다:
`ApplyClothSettings`(:80-89)는 `useGravity=false`로 설정하는데, `Update`는 매 프레임 `useGravity=true`로 덮는다
→ ApplyClothSettings의 그 줄은 **사실상 죽은 대입**이고, 두 경로가 중력 정책을 두고 서로 모순된다(초기화는 off, 매 프레임 on).
S5(거의 안 변하는 값을 매 틱 갱신) 테마.

- 근거: SoftBody3D.cs:45-61 vs :80-89.
- 제안(미적용): 파라미터 대입을 값이 바뀔 때만(런타임 튜닝이 필요 없으면 InitCloth/Rebuild 시 1회, 에디터 튜닝은
  `OnValidate`)로 제한하고, `useGravity` 정책을 **한 곳으로 통일**(둘 중 진짜 의도를 확정). 동작 영향 없이 코드 정직화.

### [T5] (안정성·문서화 / 하·확인필요) `GameTimer.GameFail` 종료 경로 전부 무가드 + `timeScale=0` + 주석 mojibake — **K4/R8 재확인** (젤리 관점에서 T3와 결합)

`GameFail`(GameTimer.cs:63-90)은 `Time.timeScale=0f`를 먼저 설정한 뒤 `PlaySFXAudio.Instance`, `playerController`,
`softBody3D.DisableCloth()`, `playerAnimController`, `mainCamera_Action`, `resultStarsUI`를 **전부 null 가드 없이** 호출한다.
참조 하나라도 미할당이면 **timeScale이 0인 채 NRE로 멈춰 소프트프리즈**. 한글 주석은 전부 mojibake(깨진 인코딩)다.
이미 K4(06-18)·R8(07-04)로 도출됐고 "이 GameFail 경로가 멀티 흐름에서 실제 불리는지" 확인이 반복 미해결 상태다.
젤리 관점의 추가 발견: 여기서 `DisableCloth`만 부르고 rebuild가 없어 **T3 위험(대기 중 rebuild가 되살림)과 직접 결합**한다.

- 근거: GameTimer.cs:63-90.
- 제안(미적용): **사용 여부 먼저 확인** → 미사용(레거시 단일플레이 잔재)이면 K4/R8/T5 묶어 데드코드 정리,
  사용 중이면 `timeScale=0` 설정 *전* null 가드 + T3(진행 중 rebuild 취소) 적용 + 주석 UTF-8 재작성(M4/N6/R7 묶음).

### [T6] (데드코드 / 하·관찰) `JellyMesh_Legacy/` 4종 중 3종 **완전 미참조**, `JellyMesh.cs`만 테스트 씬 2곳에 잔존

현행 젤리는 `SoftBody3D`(Cloth) 단일 계통인데, 구버전 스프링/2D 젤리 계열이 통째로 남아 있다. GUID 역참조 조사 결과:
- `AddSpringJoint.cs`, `JellyLine2D.cs`, `JellyMeshver2.cs` → 코드·프리팹·씬·asset **어디에서도 참조 0**.
- `JellyMesh.cs` → `Assets/Scenes/Legacy/3DTestScene.unity`, `Assets/Scenes/Test/3DTestScene.unity` 2개 **테스트 씬**에서만 참조.

M1(ResultDataCarrier 미사용)·J4와 같은 "반쪽/구버전 잔재" 테마. 빌드 크기·검색 노이즈·신규 합류자 혼란 요인.

- 제안(미적용): 미참조 3종은 삭제 후보, `JellyMesh.cs`는 딸린 테스트 씬 정리 여부까지 **사용자 확인 후** 함께 결정.
  (씬이 빌드에 포함되는지 확인 필요하므로 관찰로 둠, 직접 삭제 X.)

### [T7] (안정성 / 하·관찰) `RequestRebuildCloth`가 비활성 오브젝트에서 호출되면 **재빌드를 조용히 버려** Cloth가 영구 소실 (S3 리스폰 미배선과 연결)

`RequestRebuildCloth`는 `if (!gameObject.activeInHierarchy) return;`(SoftBody3D.cs:143)로, `EnableAndRebuildCloth`도
대기 후 `if (!gameObject.activeInHierarchy) { _isRebuilding=false; yield break; }`(:164-168)로 조기 반환한다.
즉 **스케일 완료 직후 오브젝트가 잠깐 비활성**(사망/흡수 연출로 SetActive(false))되면 rebuild가 스킵되고, 이후 다시
활성화돼도 **Cloth를 재생성하는 재시도 경로가 없어** 젤리가 뻣뻣한(천 없는) 상태로 남는다. 다만 현재 플레이어는
**리스폰이 배선돼 있지 않으므로(S3)** "사망 후 재활성" 시나리오 자체가 없어 실무 영향은 낮다 — S3 리스폰을 넣는 순간
실버그로 승격될 잠재 결함이다.

- 근거: SoftBody3D.cs:143, 164-168.
- 제안(미적용): `OnEnable`에서 "Cloth가 없고 원격/봇이 아니면 rebuild 재시도" 가드를 두거나, **S3 리스폰 설계와 묶어**
  결정. 지금은 관찰로 기록.

> **이번 루틴 총평(학습용):** 젤리 물리 계층의 위험은 물리식이 아니라 **"Cloth를 언제 끄고 언제 다시 만드느냐"의 타이밍**에
> 몰려 있다. 핵심은 T1(재빌드가 칠한 제약 맵을 날림)과 T2·T3(재빌드 코루틴이 다음 애니메이션/사망 처리와 경합)이다.
> 세 문제 모두 뿌리가 같다 — **"성장할 때마다 Cloth를 통째로 Destroy→AddComponent로 재생성"** 하는 설계.
> Cloth를 새로 만드는 대신 **끄고(disable) → 스케일 애니메이션 → 다시 켜기(enable)** 로 바꾸면(원본 컴포넌트·칠한 제약이
> 그대로 보존되므로) T1이 근본 해소되고, "만들기"가 없어 T2·T3 경합도 사라진다. 재빌드가 정말 필요한 이유(스케일 후
> 천의 rest pose 갱신 등)가 있는지 **사용자에게 확인**한 뒤, 없다면 enable/disable 방식으로 단순화하는 것을 1순위로 권한다.
> T4~T7은 정직화·데드코드·엣지케이스로 우선순위는 낮다.

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
- [x] M2  (2026-06-23 적용) — 결과 시상대 배트 숨김을 공유 프리팹→인스턴스(go)로 변경. SpawnPodium의
        프리팹 변경 블록 제거 + InstantiateDisplayOnly에서 Strip 전 HideBat(go) 호출(흡수 모드만,
        null 가드). 규칙(밀치기=배트 표시/흡수=숨김) 유지, Resources 캐시 오염 제거.
- [ ] M3  (대기 — 2026-06-23 도출, GameResultManager.GetRankString 조회 중 직렬화 필드 firstPlaceText 부수효과 덮어쓰기)
- [ ] M4  (대기 — 2026-06-23 도출, NextSceneManager/ResultStarsUI 한글 주석 인코딩 깨짐 mojibake)
- [x] M2-검증 (2026-06-25 확인) — 06-24 커밋 5ab9656의 M2 적용 회귀 없음(인스턴스 HideBat·공유 프리팹 미변형).
- [ ] N1  (대기 — 2026-06-25 도출, FSM ChangeState/IdleState.Enter 매 전환 Debug.Log 빌드 로그 스파이크 — G3/K3 미적용 경로, **우선 권장**)
- [ ] N2  (대기 — 2026-06-25 도출, 액션 입력 폴링 상태마다 복붙 + CanJump() 부재 — 입력 캐싱/전이 헬퍼 일원화 리팩터링)
- [ ] N3  (대기 — 2026-06-25 도출, PlayerMovement.OnFailAnimationFinished uiManager null 미가드 — 종료 시퀀스 NRE 위험)
- [ ] N4  (대기 — 2026-06-25 도출, 마스터 배트 히트 사거리/각도 미재검증 — 공격자 로컬 판정 신뢰, L1과 묶음, **확인 필요**)
- [ ] N5  (대기 — 2026-06-25 도출, 인스펙터 jumpForce가 Start에서 originalJumpForce로 항상 덮어써짐 — 표시 혼란, 동작 정상)
- [ ] N6  (대기 — 2026-06-25 도출, 플레이어 FSM 한글 주석 mojibake — M4와 묶어 UTF-8 재작성)
- [ ] O1  (대기 — 2026-06-27 도출, AIDetector 캐시가 null 결과 미캐싱 → '대상 없음'에서 매 호출 전체 재스캔, **우선 권장**)
- [ ] O2  (대기 — 2026-06-27 도출, 봇 OnTriggerEnter 흡수 동일프레임 double-eat + 플레이어 검증RPC 경로와 규율 불일치 — L1·N4 묶음, **확인 필요**)
- [ ] O3  (대기 — 2026-06-27 도출, 먹이 탐색 로직 이원화 AIDetector↔AIPushSurviveState.FindNearestTarget — 판정 단일 출처화 리팩터링)
- [ ] O4  (대기 — 2026-06-27 도출, 상태 전이 주체 3원화 StateEvalLoop/Update긴급/상태Update — 동작 정상·설계 관찰)
- [ ] O5  (대기 — 2026-06-27 도출, 탈락 봇 StateEvalLoop 코루틴 미종료 무한 공회전 — enabled=false는 코루틴 안 멈춤)
- [ ] O6  (대기 — 2026-06-27 도출, OnMasterClientSwitched 이벤트 중복구독 가드(-=) 부재)
- [ ] O7  (대기 — 2026-06-27 도출, RPC_BotAbsorbed 무가드 Debug.Log — N1/G3/K3 로그 규약 미적용 경로, N1과 묶음)
- [ ] P1  (대기 — 2026-06-30 도출, 재연결이 룸 미복귀 → 일시 끊김 후 마스터 서버 고립. ConnectUsingSettings만 하고 ReconnectAndRejoin/재입장 없음 + PlayerTtl=0, **확인 필요·설계 결정**, 우선 권장)
- [ ] P2  (대기 — 2026-06-30 도출, 스폰 슬롯 가상 포인트가 클라별 Random → 물리 SpawnPoint 부족 시 클라 간 겹침/봇-플레이어 충돌. **확인 완료**: 두 씬 모두 SpawnPoint 10개=maxPlayers 10 → 현재 가상 폴백 비활성, **잠재** 결함. 권위/공유 시드 스폰 레이아웃 권장)
- [ ] P3  (대기 — 2026-06-30 도출, 직렬화 필드 botCount를 CountdownCoroutine에서 런타임 상태로 덮어씀 — N5/M3 '설정≠상태' 테마, 별도 필드로 분리 권장)
- [ ] P4  (대기 — 2026-06-30 도출, 매칭 UI(LobbyController)와 네트워크 상태(NetworkManager)가 단일 출처 없이 분리 → 일시 끊김 시 화면/네트워크 불일치, P1과 묶음)
- [ ] P5  (대기 — 2026-06-30 도출, 인스펙터 spawnPoints가 OnSceneLoaded에서 매 씬 null로 비워져 지정값 미사용 — N5 '거짓 인스펙터 필드' 테마, 툴팁/접근성 정직화·동작 불변)
- [ ] Q1  (대기 — 2026-07-02 도출, 성장 1회에 SetCustomProperties 2~3회 중복(SyncScale+SyncScore(완료)+SyncScore(예측)) — 겹치는 Scale 반복 전송, 완료 1회 쓰기로 통합/변화없으면 스킵 권장, **우선 권장**)
- [ ] Q2  (대기 — 2026-07-02 도출, "Score" 커스텀 프로퍼티 쓰기만 하고 아무도 안 읽음 — 데드 데이터(M1 테마), SyncScore→SyncScale 대체 또는 "Score" 키 제거, 동작 불변)
- [ ] Q3  (대기 — 2026-07-02 도출, 리더보드 '상위5 밖이면 본인 행 표시' 미구현 + localRank/localOutside 데드변수 — 5위 밖 플레이어가 자기 순위 못 봄, 주석대로 구현 or 데드코드 삭제, **우선 권장**)
- [ ] Q4  (대기 — 2026-07-02 도출, 본인 식별을 닉네임 문자열로(UpdateLeaderboard 648/663, GetLocalPlayerRank 407) — 중복 닉네임 오식별. **H4 확장**: ScoreboardSnapshot.Entry.actorNumber 이미 존재 → actorNumber 비교로 교체하면 H4 동시 종결, **우선 권장**)
- [ ] Q5  (대기 — 2026-07-02 도출, 스케일 폴백 상수 이원화 GetPlayerSyncedScale=1f vs startingScale=2f/실제 transform — 스폰 직후 봇 위협판정 오판 + (float)val 무검증 캐스트(ReadFloat 안전패턴 미적용). startingScale로 통일+is float 가드 권장)
- [ ] Q6  (대기 — 2026-07-02 도출, ResetScale/HandleScaleReset은 1f로, Start/GameState.Reset/startingScale은 2f — '기본 스케일' 출처 이원화, 관찰. 설계의도 확인 후 startingScale로 통일 결정 필요)
- [ ] Q7  (대기 — 2026-07-02 도출, LevelUI.Refresh (max-min) 0 나눗셈 미가드 → min==max 구성 시 NaN, Mathf.Max(denom,ε)+Clamp01 방어, 아주 경미)
- [ ] R1  (대기 — 2026-07-04 도출, 액터 추적 이원화: MinimapArrowManager는 0.5s마다 FindObjectsByType×2, OffScreenPlayerIndicator는 EntityRegistry 단일출처 — 미니맵도 EntityRegistry로 통일 권장. H6/O3 단일출처 테마)
- [ ] R2  (대기 — 2026-07-04 도출, MinimapArrowManager:94 미니맵 카메라 타겟 지정 무가드 체인(FindGameObjectWithTag→GetComponent→.target) → 태그/컴포넌트 부재 시 매 스캔 NRE로 봇 화살표까지 통째 스킵. null 가드+1회성 분리, **우선 권장**)
- [ ] R3  (대기 — 2026-07-04 도출, JellyCamera.Update:57 P키 디버그 트리거 프로덕션 잔존 → 게임 중 P 누르면 렌즈왜곡+FOV+회전 연출 오발동. #if UNITY_EDITOR 가드 or 제거, ContextMenu 이미 존재. N1/G3 디버그잔재 입력판, **우선 권장**)
- [ ] R4  (대기 — 2026-07-04 도출, JellyCamera.Start:48 globalVolume/lensDistortion null 미가드 → 미할당/Volume 미구성 시 Start·PlayDing NRE. null 가드로 연출만 스킵)
- [ ] R5  (대기 — 2026-07-04 도출, 카메라 orthographicSize 라이터 4원화: ProcessCameraQueue(큐)/ChangeCameraSizeToLevel(큐 우회)/SetOrthographicSizeDirect/PlayerExternalEventLinker.ChangeCameraOrthoSize — 겹치면 Lerp 경합·비결정. 단일 큐/목표상태로 통일 권장. 아키텍처)
- [ ] R6  (대기 — 2026-07-04 도출, MainCamera_Action이 Camera.main.orthographicSize를 곳곳 무가드 반복 접근(SetOrthographicSizeDirect만 가드) → 씬 전환 중 코루틴 NRE + 태그 스캔 반복. _cam 캐시+null 가드, **우선 권장**)
- [ ] R7  (대기 — 2026-07-04 도출, TopDownCameraFollow.cs/GameTimer.cs 한글 주석 mojibake(Header/Tooltip 포함 → 에디터 필드설명 깨짐). M4/N6와 묶어 UTF-8 재작성)
- [ ] R8  (대기 — 2026-07-04 도출, GameTimer.GameFail 종료경로 전부 무가드+timeScale=0 → 참조 미할당 시 소프트프리즈(K4 연장선). 멀티 씬 사용 여부 확인 후 데드코드 정리 or 널가드, **확인 필요**)
- [ ] R9  (대기 — 2026-07-04 도출, MinimapArrow.SetColor:41 r.material.color 인스턴스 복제(G3/G4/K1 테마) → MaterialPropertyBlock tint 권장. 화살표 소수라 미미, 죽은 분기 여부 확인 겸)
- [ ] S1  (대기 — 2026-07-07 도출, NetworkPlayerSync.OnTriggerEnter:355 _absorbedBotIds를 MC 검증 *전* 사전 등록 → 크기 동률 등으로 흡수 거부돼도 세트에 남아 그 봇을 영구히 다시 못 먹음. 사전 Add 제거, 확정 등록은 RPC_BotAbsorbConfirmed:509에만. **우선 권장**·버그)
- [ ] S2  (대기 — 2026-07-07 도출, RPC_GetAbsorbed:539·545가 점수/성장을 피흡수자 *보간 중* transform.localScale.x로 계산 → 권위 Scale 미사용, 클라별 보상 어긋남. GetAuthorityScale(photonView)로 교체(BotAbsorbConfirmed는 이미 권위값 사용). **우선 권장**·버그)
- [ ] S3  (대기 — 2026-07-07 도출, 플레이어 리스폰 시스템 전체 미배선 — respawnDelay/Respawn()/RPC_OnRespawn 존재하나 Respawn() 호출처 0. _isAbsorbed·_absorbedBotIds 리셋이 한 판 내 절대 실행 안 됨. 리스폰 넣을지/삭제할지 **설계 결정·사용자 확인**. J4/M1 반쪽구현 테마)
- [ ] S4  (대기 — 2026-07-07 도출, 봇 스케일 이중 채널 — AIPlayerSync 룸프로퍼티(판정용 GetSyncedScale) + AIPlayerMovement.OnPhotonSerializeView 매틱 스트림(시각 Lerp). 단일 출처(룸프로퍼티) 통일 시 스트림 제거 가능, 대역폭 절감. Q1/Q2 테마)
- [ ] S5  (대기 — 2026-07-07 도출, NetworkPlayerSync.OnPhotonSerializeView가 색상 4-float를 매 틱 스트리밍(거의 안 변함) + alpha는 수신부가 1로 덮어 무의미 전송. 변화 시 이벤트/프로퍼티 전파로 전환(SyncColor 선례 존재), 최소 alpha 즉시 삭제. Q1/Q2 트래픽 정리)
- [ ] S6  (대기 — 2026-07-07 도출, RPC_Request흡수/대쉬/배트 MC 검증이 크기만 보고 위치·사거리 재확인 없음 → 지연/경합 시 빗나간 히트 승인, 조작 ViewID 원거리 히트 여지. MC가 아는 권위 위치로 거리 게이트 추가. **N4 확장·확인 필요**)
- [ ] S7  (대기 — 2026-07-07 도출, 흡수 판정 이원화 — 플레이어발 '요청→MC검증' RPC vs 봇발 MC 로컬 직접(OnTriggerEnter). 규칙 복붙→드리프트 위험, S1·S2가 플레이어 경로에만 있는 원인. MC 단일 헬퍼로 통일 권장. O3/H6 테마)
- [ ] S8  (대기 — 2026-07-07 도출, RPC_BotAbsorbConfirmed:511/RPC_GetAbsorbed:537 등 흡수·대쉬·배트 RPC가 DataManager.Instance 무가드 역참조 → 씬 전환 경계 stale RPC 시 NRE. 진입부 null 가드 1줄. G8/L2/H3 테마, S2와 묶음)
- [ ] S9  (대기 — 2026-07-07 도출, WanderingAI._isMine이 Start 1회 계산 + OnMasterClientSwitched 미구현 → 봇은 이어받는데 젤리는 안 함. 현재 PhotonNetwork.Instantiate라 마스터 이탈 시 파괴+재보충(젤리 일시 증발). 룸오브젝트化 시 실버그로 승격. **확인 필요·설계 결정**. P1/O6 테마)
- [ ] S10 (대기 — 2026-07-07 도출, AIPlayerMovement.RPC_BotAbsorbed:692 무가드 Debug.Log 잔존 — O7(06-27) 이후 코드변경 없어 그대로. 제거 or [Conditional] 래퍼, O7/N1 로그규약 일괄 정리)
- [ ] T1  (대기 — 2026-07-09 도출, Cloth 재빌드(EnableAndRebuildCloth)가 새 Cloth의 기본 coefficients를 _initialCoefficients에 재캡처 → 에디터에서 칠한 softness 맵 소실. 첫 성장 이후 하이브리드 연출 손상. InitCloth 1회 캡처값을 보존·재적용, 정점순서 동일성 에디터 실측 **확인 필요**, **우선 권장**·시그니처)
- [ ] T2  (대기 — 2026-07-09 도출, 연속 스케일 큐에서 이전 ScaleTo의 RequestRebuildCloth 코루틴이 다음 ScaleTo Lerp 도중 Cloth를 재-enable → 찌그러짐 방지 무력화(연속 흡수 시). 재빌드를 ProcessScaleQueue 종료 시 1회로 이동 권장(T1 빈도도 완화). **확인 필요**)
- [ ] T3  (대기 — 2026-07-09 도출, DisableCloth/RemoveCloth가 진행 중 EnableAndRebuildCloth 코루틴 미취소 → 꺼야 할 때(GameFail 등, yield null은 timeScale=0에도 진행) Cloth가 되살아남. 진입부 StopCoroutine+_isRebuilding=false로 '끄기가 만들기를 이긴다' 불변식 확립. T2 근본)
- [ ] T4  (대기 — 2026-07-09 도출, Update가 매 프레임 Cloth 파라미터 5종+useGravity=true 무조건 재기록 → ApplyClothSettings의 useGravity=false 죽은 대입+중력정책 모순. 값 변경 시로 제한+정책 단일화, 동작 불변. S5 테마)
- [ ] T5  (대기 — 2026-07-09 도출, GameTimer.GameFail 종료경로 전부 무가드+timeScale=0 후 소프트프리즈+주석 mojibake — K4/R8 재확인, 젤리 관점 T3와 결합(DisableCloth만·rebuild 취소 없음). 사용 여부 확인 후 데드코드 정리 or 널가드+T3, **확인 필요**)
- [ ] T6  (대기 — 2026-07-09 도출, JellyMesh_Legacy 4종 중 AddSpringJoint/JellyLine2D/JellyMeshver2 GUID 참조 0, JellyMesh.cs만 3DTestScene(Legacy/Test) 2씬 잔존. 구버전 스프링/2D 젤리 데드코드(M1/J4 테마). 씬 빌드 포함 여부 확인 후 사용자 확인·정리)
- [ ] T7  (대기 — 2026-07-09 도출, RequestRebuildCloth/EnableAndRebuildCloth가 비활성 시 재빌드 조용히 버림+재시도 경로 없음 → 재활성 시 Cloth 영구 소실. 현재 리스폰 미배선(S3)이라 시나리오 부재·잠재. S3 리스폰과 묶어 OnEnable 재시도 결정, 관찰)

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
- 2026-06-25 루틴: SessionStart 훅이 자동 실행되진 않아 수동(`CLAUDE_CODE_REMOTE=true bash .claude/hooks/
  session-start.sh`)으로 credentials 주입 + gspread 설치. 이번엔 **시스템 cryptography(rust pyo3 바인딩)가
  깨져** `import cryptography.exceptions`에서 `pyo3_runtime.PanicException` 발생(google.auth.crypt가 ES256용
  `es` 모듈을 강제 import해 cryptography가 필수). `pip install --upgrade --force-reinstall cryptography`로
  정상 휠을 dist-packages에 덮어 해결(debian판 uninstall은 RECORD 없음으로 실패하나 pip 설치본이 shadow함).
  이후 `status` 정상(개발계획서 루틴 21, 트러블슈팅 79=M2). → 06-16/06-23의 `--only-binary cffi` 대신
  **cryptography 자체 force-reinstall**이 이번 컨테이너의 해법. 훅 pip 목록에 cryptography 강제 재설치를
  넣어두면 다음 세션도 자동화될 가능성. (※ SessionStart 훅이 자동 실행됐는지 불확실 — credentials.json이
  세션 시작 시 없었음. 훅 자동 실행 여부는 다음 루틴에서 재확인 필요.)
