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

## 적용 상태
- [x] F1  (2026-06-04 적용) — LoadingSceneController 기본 씬을 GameState.CurrentGameMode에서 파생
- [x] F2  (2026-06-04 적용) — NetworkManager 씬 결정을 GameState.CurrentGameMode 기준으로 통일
- [ ] F3  (대기)
- [ ] F4  (대기)
- [ ] F5  (대기)
- [ ] G1  (대기 / 버그 — 승인 시 즉시 수정 권장)
- [ ] G2  (대기 / 동기화 구성 확인 필요)
- [ ] G3  (대기)
- [ ] G4  (대기)
- [ ] G5  (대기)
- [ ] G6  (대기)
- [ ] G7  (대기)
- [ ] G8  (대기)

## 환경 메모
- 원격 컨테이너는 매 세션 새로 클론되므로 `~/.config/gsheet/credentials.json` 와 `gspread`가
  매번 없어진다. 2026-06-04 루틴에서는 사용자가 credentials를 업로드해주어 수동 설치
  (`pip install gspread google-auth cffi cryptography`) 후 시트 기록을 정상 수행함.
- 영구 자동화하려면 환경 SessionStart 훅/시작 스크립트에 위 설치 + credentials 주입을 넣어야 함.
