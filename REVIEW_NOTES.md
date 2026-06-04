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

## 적용 상태
- [x] F1  (2026-06-04 적용) — LoadingSceneController 기본 씬을 GameState.CurrentGameMode에서 파생
- [x] F2  (2026-06-04 적용) — NetworkManager 씬 결정을 GameState.CurrentGameMode 기준으로 통일
- [ ] F3  (대기)
- [ ] F4  (대기)
- [ ] F5  (대기)

## 환경 메모
- 원격 컨테이너는 매 세션 새로 클론되므로 `~/.config/gsheet/credentials.json` 와 `gspread`가
  매번 없어진다. 2026-06-04 루틴에서는 사용자가 credentials를 업로드해주어 수동 설치
  (`pip install gspread google-auth cffi cryptography`) 후 시트 기록을 정상 수행함.
- 영구 자동화하려면 환경 SessionStart 훅/시작 스크립트에 위 설치 + credentials 주입을 넣어야 함.
