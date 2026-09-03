# JellyGameProject — Claude 루틴 지침

## 프로젝트 개요
- Unity 6000.0.65f1, C# 멀티플레이어 .io 게임 (LAN 호스트-서버, 순수 C# TCP 소켓)
- 주요 시스템: 젤리 물리(SoftBody3D/Cloth), FSM 플레이어 컨트롤러, AI 봇, 네트워크 동기화
- Photon PUN2에서 이식 완료 — 잔재 없음
- 작업 브랜치: `IO-lan_socket`

### 알아둘 개념
- 프레이밍 `[len4][type1][body]`, 한 `MsgType`의 주인은 하나
- `OwnerId`는 정체가 아니라 **책임**이다. 호스트 1, 클라 2+, 씬 오브젝트 0
- `NetIdentity.IsSimulatedHere` ≠ `IsMine`. 씬 오브젝트는 `NetId >= SCENE_ID_BASE`
- `INetEntity`가 사람(`LanPlayerState`)과 봇(`LanBotState`)을 하나로 묶는다.
  판단은 `NetEntity` 정적 게이트웨이에 모은다 — 같은 판단이 두 모드에 흩어져
  한쪽만 고쳐지는 일이 반복됐다
- **크기의 출처가 사람과 봇이 다르다.** 사람은 패킷으로 오지 않아 각 기계가 성장 사건을 받아
  스스로 만든다. 봇은 호스트가 절대값을 방송하고 클라는 `FollowScale`로 따라간다.
  그래서 클라에서 봇의 `ScaleTo`를 돌리면 안 된다 → `NetEntity.DrivesScaleHere`
- 캐릭터는 트리거 콜라이더가 **둘**이다(루트 캡슐 + 자식 메시). 대표는 `PlayerMesh` 태그 쪽

## 일일 코드 리뷰 루틴

### 진행 조건
- IO 브랜치에서 진행
- 전날 대비 코드 수정이 없으면 진행하지 않음

### 작업 흐름
1. 게임 시퀀스 관련 스크립트를 살펴보고 구조적 개선점 탐색
2. 개선점을 정리해서 도출하고 기억해놓기 (직접 코드 수정 X)
3. 사용자 확인 후 적용 여부 결정 → 승인된 항목만 실제 작업
4. 버그 발견/제보 시 즉시 수정 가능

### 기록
- 무엇을 왜 고쳤는지는 **코드 주석**에 남긴다. 별도 이력 문서는 쓰지 않는다.
- 이유가 긴 수정(과거에 어떻게 틀렸는지)은 해당 함수 위에 `★` 주석으로 붙인다.

### 세팅은 세팅이 있는 곳에
**인스펙터·프리팹·씬에서 한 번 정하면 되는 것을 코드로 다시 정하지 않는다.**
출처가 둘이 되면 프리팹을 고쳐도 코드가 덮어써 "왜 안 바뀌지"가 되고,
코드만 고치면 프리팹을 보는 사람이 속는다.

- 레이어·태그·크기·색·정렬 순서 → 프리팹이나 씬에
- 런타임에 `GameObject`를 조립하는 대신 **프리팹을 만들어 `Instantiate`**
- 코드는 "어디에 놓을지·언제 켤지" 같은 **규칙**만 정한다

실제로 걸린 것들: `MinimapArrowManager`의 `SetLayerRecursively`(프리팹이 이미 그 레이어),
`OffScreenPlayerIndicator`의 캔버스·삼각형 런타임 생성, `FallingTile`의 감지 박스 상수.

### 도달하지 않는 코드는 지운다
읽다가 "이 분기 언제 도나?" 싶으면 **수치나 프리팹으로 확인하고**, 도달 불가면 지운다.
검증되지 않은 채 남아 있다가 나중에 조건이 바뀌면 아무도 모르게 되살아난다.
지울 때는 **왜 도달 불가인지**를 주석에 남긴다.

### 푸시
- 원격에서 작업한 게 로컬과 연동될 수 있도록 항상 푸시

## 검증 — 고치기 전에 재고, 고친 뒤에 확인한다

**추측으로 진단하지 않는다.** "아마 이래서 그럴 것"이라고 말하기 전에 수치를 뽑는다.
씬·프리팹·meta는 전부 텍스트라 파이썬으로 읽으면 실측이 된다.

작업 뒤에 도는 검사:

1. **컴파일.** 이게 최우선이고 다른 검사로 대체되지 않는다.
2. 씬·프리팹 YAML 파싱 (`yaml.safe_load_all`, Unity 태그는 치환하고 넣는다)
3. **끊긴 참조** — 씬 안에 없는 `fileID`를 가리키는 곳
4. **미해결 guid** — `Assets`+`Packages`+`Library/PackageCache`의 meta와 대조
5. **고아 직렬화 키** — 씬/프리팹의 키 ↔ 스크립트의 `[SerializeField]` 필드
6. **이벤트 구독/해제 짝** — `+=` 와 `-=` 개수와 대상이 맞는지

## 씬·프리팹을 텍스트로 고칠 때

컴포넌트를 지우면 **블록만 지우면 안 된다.** GameObject의 `m_Component` 목록에서도 빼야 하고,
그걸 가리키던 참조도 찾아야 한다.

**"안 쓰는 컴포넌트"를 판단할 때 `m_Target`만 보면 안 된다.**
프리팹 인스턴스는 버튼 배선을 `PrefabInstance`의 수정 항목 안 `objectReference`로 들고 있다.
실제로 이걸 놓쳐서 쓰이고 있던 `SceneLoader`를 지웠다가 되돌린 적이 있다.

손으로 쓴 `.meta`는 **에셋 파일과 같은 순간에** 넣는다. 유니티가 켜져 있는 채로 에셋만 먼저
들어가면 유니티가 자기 guid로 meta를 만들고, 나중에 들어온 meta가 그걸 덮어써 아티팩트 DB와
어긋난다.

## 반복해서 밟은 함정

같은 실수를 다시 하지 않기 위한 목록이다. 새로 걸리면 여기에 한 줄 추가한다.

- **`Instantiate(원본, 부모)`는 `worldPositionStays`가 true다.** 자식 `localScale`에
  `프리팹스케일 / 부모lossyScale`이 들어가서, 생성 시점의 부모 크기에 따라 크기가 달라진다.
  UI·캐릭터 자식으로 낳을 때는 세 번째 인자 `false`를 반드시 넘긴다.
  ("플레이어마다 인디케이터 크기가 다르다"의 원인이었고 `ComponentPool`에도 같은 게 있었다)
- **`component.enabled = false`는 직접 호출을 막지 못한다.** 유니티 콜백만 멈춘다.
  `GetComponentInChildren<T>(true)`로 찾아서 public 메서드를 부르면 그대로 실행된다.
  (`AIPlayerMovement`가 클라 봇의 `PlayerAbsorber`를 껐지만 `AbsorbColor`는 계속 불렸다)
- **연출을 크기 파이프라인에 붙이지 않는다.** 봇의 `ScaleTo`는 구동자에서만 돈다.
  "모든 화면에서 보여야 하는 것"은 **방송이 도착한 자리**에 붙인다.
- **C# `%`는 피제수의 부호를 따른다.** `GetInstanceID()`가 음수라 해시 범위가 깨졌다.
  `& 0x7FFFFFFF`로 막는다 (`Mathf.Abs`는 `int.MinValue`에서 터진다).
- **`NavMeshPath.corners`는 접근할 때마다 배열을 새로 만드는 프로퍼티다.**
- **`.material`은 접근하는 순간 사본을 만든다.** 아무도 파괴하지 않고 배칭도 깨진다.
- **`[RequireComponent]`는 그 컴포넌트가 없는 오브젝트에 스크립트를 못 붙이게 막는다.**
  `RectTransform`을 요구하면 평범한 `Transform` 오브젝트에는 추가 자체가 안 된다.

## 이 프로젝트에서 틀렸던 진단

*틀린 길을 다시 가지 않기 위해 남긴다.*

- `GrowKind.Jelly`는 **아무도 방송하지 않는다.** 젤리 흡수는 `EatJellyConfirm` →
  `AbsorbMode.OnEatConfirmed` → `PlayerAbsorber.AbsorbColor` 경로로 전파된다.
- 에디터의 "Internal error - unexpected guid mismatch"는 원인을 못 찾았다.
  확인해서 **아닌 것으로 밝혀진 것**: meta guid 충돌(0건), `SourceAssetDB` ↔ meta 불일치(0건),
  고아 meta(0건), 파일명 유니코드 정규화(전부 NFC), 대소문자 충돌(0건),
  LFS 포인터 잔존(0건), 패키지 manifest ↔ lock 불일치(0건).
  설치된 패키지 소스에 그 문자열이 없으므로 **유니티 네이티브 코드**가 내는 메시지다.
  다음 단서는 콘솔의 스택 트레이스와 `Library` 캐시 삭제 후 재현 여부.

## 참고 문서
- `STUDY_PLAN.md`: 코드 파악 일정과 남은 결정 사항
- `REVIEW_NOTES.md`: 게임 시퀀스 코드 구조 분석 결과
- `NETWORK_SPEC.md` / `LAN_SOCKET_MIGRATION.md`: 소켓 프로토콜과 이식 기록
