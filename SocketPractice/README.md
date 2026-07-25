# B-1단계 실습: 콘솔로 TCP 소켓 익히기

> Unity 없이 콘솔에서 TCP를 직접 다뤄보는 단계.
> 목표는 "게임 만들기"가 아니라 **소켓의 원리와 함정을 몸으로 익히기**.
> ⚠️ 이 폴더는 `Assets/` **밖에** 있어야 한다. Unity가 콘솔용 `Main`을 컴파일하면 에러가 난다.

---

## 실행 방법

터미널(또는 명령 프롬프트) **2개**를 띄운다. 하나는 호스트, 하나는 클라.

```bash
cd SocketPractice

# 터미널 A (호스트)
dotnet run -- step1-host

# 터미널 B (클라)
dotnet run -- step1-client 127.0.0.1
```

`dotnet` 명령이 없다면 → [.NET SDK](https://dotnet.microsoft.com/download) 설치, 또는 **Visual Studio에서 이 폴더의 `SocketPractice.csproj`를 열어** 실행(프로젝트 속성 → 디버그 → 명령줄 인수에 `step1-host` 입력).

SDK 버전이 다르면 `SocketPractice.csproj`의 `<TargetFramework>net8.0</TargetFramework>`를 설치된 버전(`net6.0` 등)으로 바꾸면 된다.

---

## 1단계 — 프레이밍 없는 에코 (뭉침 현상 확인)

```bash
dotnet run -- step1-host
dotnet run -- step1-client 127.0.0.1
```

클라에서 아무 말이나 쳐보고, 그다음 **`/burst`** 를 입력한다.

**관찰 포인트:** `/burst`는 `HELLO` `WORLD` `BYE`를 **3번 따로** 보낸다. 그런데 호스트 콘솔에는 이렇게 뜬다:

```
[호스트] 받음(13바이트): "HELLOWORLDBYE"
```

**3번 보냈는데 1번으로 받았다.** TCP가 "메시지"가 아니라 "바이트 흐름"이기 때문. 반대로 큰 메시지는 쪼개져서 여러 번에 걸쳐 오기도 한다. 이것이 프레이밍이 필요한 이유다.

## 2단계 — 길이 프리픽스 적용 (문제 해결)

```bash
dotnet run -- step2-host
dotnet run -- step2-client 127.0.0.1
```

똑같이 `/burst`를 입력한다. 이번엔:

```
[호스트] 메시지 #1: "HELLO"
[호스트] 메시지 #2: "WORLD"
[호스트] 메시지 #3: "BYE"
```

**핵심 코드는 `MessageIO.cs`** — 특히 `ReadExactly()`. "요청한 바이트를 다 채울 때까지 반복해서 읽는다"는 이 한 가지가 소켓 프로그래밍의 절반이다.

## 3단계 — 다중 접속 + 호스트 판정 (게임 구조 축소판)

```bash
dotnet run -- step3-host
dotnet run -- step3-client 127.0.0.1     # 이 클라를 2~3개 띄운다
```

클라에서:

- 아무 말 입력 → 전원에게 채팅 브로드캐스트
- `/eat jelly_7` → "이 젤리 먹었어요" 라고 **호스트에 요청**

**관찰 포인트:** 클라 2개에서 **같은 젤리**를 `/eat jelly_7` 해본다.

```
  <- RESULT jelly_7 -> 승자 P1     (먼저 요청한 쪽)
  <- DENY jelly_7 (이미 먹힌 젤리)  (늦은 쪽)
```

클라가 스스로 판정하지 않고 **호스트가 선착 1명만 인정**한다. 이게 우리 게임의 `_claimedJellies` 선점 가드와 똑같은 구조다.

---

## 우리 게임과의 대응 관계

| 실습에서 만든 것 | 젤리팡 아일랜드에서의 정체 |
|---|---|
| `TcpListener` 로 접속 받기 | Photon 룸 생성 / 입장 |
| 클라별 수신 스레드 | Photon이 감춰줬던 부분 |
| `WELCOME P1` (ID 발급) | `ActorNumber` / `netId` |
| `EAT` 요청 → 호스트 판정 | `RPC_RequestEatJelly` → 마스터 검증 |
| `RESULT` 브로드캐스트 | `RPC_ConfirmEat`, `RpcTarget.All` |
| `DENY` (요청자에게만) | 특정 Owner 대상 RPC |
| `_claimed` HashSet + lock | `_claimedJellies` 선점 가드 |

---

## 실습 과제 (권장)

1. **메시지 타입 추가** — 지금은 문자열 `"EAT ..."`로 구분한다. 본문 첫 바이트에 숫자 타입(1=CHAT, 2=EAT, 3=RESULT)을 넣도록 바꿔보기. 실제 게임 프로토콜 형태다.
2. **위치 전송 흉내내기** — 클라가 0.1초마다 `x,y,z` 좌표를 보내고 호스트가 전원에게 뿌리기. 초당 몇 개가 오가는지 세어보기.
3. **호스트 이탈 실험** — 호스트를 Ctrl+C로 끄면 클라가 어떻게 되는지 확인. (Photon의 자동 마스터 교체가 없다는 걸 체감)
4. **다른 기기에서 접속** — 같은 와이파이의 다른 PC/노트북에서 `dotnet run -- step3-client 192.168.0.x` 로 접속해보기.

---

## 자주 만나는 문제

| 증상 | 원인 / 해결 |
|---|---|
| `SocketException: 사용 중인 주소` | 이전 실행이 안 죽었다. 프로세스 종료 또는 포트 번호 변경 |
| 다른 기기에서 접속 안 됨 | ① 윈도우 방화벽 허용(첫 실행 시 팝업에서 '허용') ② 호스트 IP 확인(`ipconfig`) |
| 같은 와이파이인데도 안 됨 | 공용 와이파이의 **AP 격리**. 집 공유기나 휴대폰 핫스팟으로 테스트 |
| 콘솔 한글이 깨짐 | `chcp 65001` 실행 후 재시도 |
| 클라만 켜면 즉시 종료 | 호스트를 먼저 실행해야 한다 |

---

## 다음 단계

2단계(Unity 통합): 이 소켓 코드를 Unity에 넣고 **수신 스레드 → 메인 스레드 큐** 패턴으로 연결한다.
Unity의 Transform은 메인 스레드에서만 만질 수 있기 때문에, 여기서 배운 구조를 그대로 쓰되 큐를 하나 끼우는 것이 핵심이다.
