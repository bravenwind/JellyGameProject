# B-4 흡수 모드 — 씬 구성 가이드

목표: **호스트가 젤리를 뿌리고, 두 플레이어가 먹으며 커지는 것**. 같은 젤리는 **한 명만** 먹는다.

---

## 1. 기존 플레이어 프리팹에 `NetScale` 추가

`NetPlayer` 프리팹 선택 → **Add Component → NetScale**

값은 기본값 그대로 두면 됩니다.

```
Lerp Speed  8      (커질 때 따라가는 속도)
Min Scale   0.5
Max Scale   6
```

## 2. 젤리 프리팹 만들기

**Hierarchy 우클릭 → 3D Object → Sphere** → 이름 `NetJelly`

| 컴포넌트 | 필요 여부 |
|---|---|
| `NetIdentity` | **필수** |
| `NetScale` | 권장 (젤리도 크기 정보를 가짐) |
| `NetTransform` | **불필요** — 젤리는 안 움직임 |
| `TestPlayerController` | **불필요** |

**Scale을 `(0.6, 0.6, 0.6)`** 정도로 줄이세요 (`jellyRadius = 0.3`에 맞춤).

색이 구분되면 좋으니 **2~3종류**를 만드시면 좋습니다 — `NetJelly_Red`, `NetJelly_Blue` 처럼 색만 다르게. 프리팹으로 저장 후 씬에서 삭제.

## 3. `NetWorld`의 Prefabs 배열 확장

```
Size: 3
Element 0: NetPlayer      ← 반드시 0번 (플레이어)
Element 1: NetJelly_Red   ← 1번부터 젤리
Element 2: NetJelly_Blue
```

⚠️ **0번은 항상 플레이어**여야 합니다. `NetConfig.JellyPrefabStart = 1`이 그 경계예요. 젤리는 뒤에 계속 덧붙이면 됩니다.

## 4. `NetTest` 오브젝트에 `AbsorbMode` 추가

**Add Component → AbsorbMode**

```
Spawn Jelly      ✓
Spawn Interval   1.5      (초당 0.66개)
Max Jelly Count  30
Spawn Range X/Z  18 / 18
Player Radius    0.5
Jelly Radius     0.3
Grow Per Jelly   0.12
```

---

## 5. 실행 & 확인

1. 한쪽 **[호스트 시작]** → 잠시 후 젤리가 하나씩 생김
2. 다른 쪽 **[참가]**
3. WASD로 젤리에 다가가면 **사라지면서 내 캡슐이 커짐**

### 확인 항목

| 항목 | 기대 결과 |
|---|---|
| 젤리 복제 | 양쪽 화면에 **같은 위치·같은 개수** |
| 흡수 | 먹으면 **양쪽 모두에서** 사라짐 |
| 크기 동기화 | 내가 커지면 **상대 화면에서도** 커짐 |
| 늦은 입장 | 나중에 들어와도 **기존 젤리 + 커진 크기**가 다 보임 |
| **이중 흡수 방지** | 아래 참고 |

### ★ 이중 흡수 테스트 (B-4의 핵심)

**두 캡슐을 같은 젤리에 동시에 밀어 넣어보세요.**

```
기대: 한 명만 커진다.  UI의 "크기" 값으로 확인
버그였다면: 둘 다 커진다
```

로그에도 한 줄만 찍혀야 합니다:

```
P1 흡수! (젤리 1)     ← 한 번만
```

원리는 `AbsorbMode.ResolveEat`에 있습니다 — **판정 즉시 젤리를 제거**하므로 두 번째 요청은 `Find()`에서 `null`을 받고 탈락합니다.

---

## 권위 흐름 정리

```
[클라 P2]  젤리에 닿음 → EatJellyRequest(젤리netId, 내netId) 전송
                              ↓
[호스트]   ① 젤리 존재? ② 내 캐릭터 맞나? ③ 거리 되나?
           → 통과하면 젤리 즉시 제거
           → 크기 증가 후 StateUpdate 방송
           → EatJellyConfirm 방송
                              ↓
[전원]     젤리 사라짐 + 그 플레이어 커짐
```

**클라는 스스로 아무것도 지우거나 키우지 않습니다.** 요청만 하고 결과를 통보받아요.

---

## 자주 겪는 문제

| 증상 | 원인 |
|---|---|
| 젤리가 안 생김 | `Prefabs` 배열이 1개뿐 (젤리 미등록) 또는 `Spawn Jelly` 꺼짐 |
| 젤리에 닿아도 안 먹힘 | `Player Radius` / `Jelly Radius`가 실제 크기와 안 맞음 |
| 내 화면에서만 젤리가 사라짐 | 클라가 직접 지우고 있음 — 권위 위반, 코드 확인 |
| 크기가 상대 화면에 반영 안 됨 | 프리팹에 `NetScale` 누락 (콘솔에 경고가 뜸) |
| 커질수록 젤리를 못 먹음 | 정상 — 반경이 커져 더 쉬워집니다. 반대면 부호 확인 |
