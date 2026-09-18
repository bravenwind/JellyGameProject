# 3. 사용한 기술

전체 128개 스크립트, 약 23,900줄로 이루어져 있습니다.

## 3-1 네트워크

로컬 멀티플레이는 외부 라이브러리 없이 C# TCP 소켓으로 직접 구현했습니다. 처음에는 메시지를 그냥 보내면 그대로 도착할 줄 알았는데, TCP는 스트림이라 메시지 경계가 없다는 걸 테스트하다가 알게 됐습니다. 두 패킷이 한 번에 붙어서 오거나 하나가 반씩 잘려서 오는 일이 실제로 생겼습니다. 그래서 모든 메시지 앞에 4바이트 길이를 붙이는 `[길이 4바이트][타입 1바이트][내용]` 형식을 정하고, 받는 쪽에서는 버퍼에 쌓아두었다가 길이만큼 다 모였을 때만 꺼내 쓰도록 했습니다.

```csharp
// FramedConnection.Poll — 받은 바이트를 쌓아두고 완성된 메시지만 꺼낸다
int offset = 0;

while (len - offset >= 4)
{
    int bodyLen = buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24);

    if (len - offset - 4 < bodyLen)
        break;                          // 아직 다 안 왔으면 다음 프레임에 다시 본다

    reader.Reset(buf, offset + 4, bodyLen);
    onMessage(reader);

    offset += 4 + bodyLen;
}

// 처리하고 남은 조각은 버퍼 앞으로 당겨서 다음 수신과 이어붙인다
if (offset > 0)
{
    int rest = len - offset;
    if (rest > 0)
        Buffer.BlockCopy(buf, offset, buf, 0, rest);
    len = rest;
}
```

메시지 종류는 `MsgType` 열거형으로 30개를 정의했고, 각 종류를 처리하는 함수를 등록해두면 그쪽으로 보내주는 방식으로 만들었습니다. 판정은 전부 호스트가 내리고 클라이언트는 요청만 보냅니다. 예를 들어 젤리를 먹으면 클라이언트가 `EatJellyRequest`를 보내고, 호스트가 승인하면 `EatJellyConfirm`을 모두에게 방송합니다. 이렇게 한 이유는 두 명이 같은 젤리에 동시에 닿았을 때 양쪽 화면에서 모두 먹은 것으로 처리되는 문제가 있었기 때문입니다.

온라인 멀티플레이는 나중에 Photon을 붙였습니다. 그런데 처음에 만든 구조에서는 상위 코드가 TCP 소켓 객체를 47곳에서 직접 호출하고 있었습니다. 그대로 두면 그 47곳을 전부 "지금 로컬인가 온라인인가"로 나눠야 했습니다. 그래서 메시지를 주고받는 통로를 인터페이스로 만들고, 소켓 구현과 Photon 구현이 각각 그걸 따르도록 바꿨습니다.

```csharp
public interface INetTransport
{
    int MyId { get; }
    bool IsHost { get; }

    void Broadcast(NetWriter w);            // 호스트 → 모든 클라
    void SendTo(int peerId, NetWriter w);   // 호스트 → 특정 클라
    void SendToHost(NetWriter w);           // 클라 → 호스트

    void RouteHost(MsgType type, Action<int, NetReader> handler);
    void RouteClient(MsgType type, Action<NetReader> handler);

    void Poll();
    void Shutdown();
}
```

방을 만들고 찾고 들어가는 일은 `INetSession`으로 따로 분리했습니다. 로컬은 UDP 브로드캐스트로 같은 공유기 안의 방을 찾고 TCP로 접속하지만, 온라인은 서버가 방 목록을 주고 릴레이를 거쳐 접속하기 때문에 방식이 완전히 다릅니다. 반면 접속이 끝난 뒤에 메시지를 주고받는 모양은 둘이 같습니다. 그래서 "접속하기 전"과 "접속한 뒤"를 나눴습니다.

이렇게 하고 나니 로컬과 온라인이 갈리는 지점이 어느 구현을 쓸지 고르는 한 줄로 줄었고, 게임 로직 쪽은 한 줄도 고치지 않았습니다.

위치 동기화는 좌표를 그대로 대입하면 다른 사람 캐릭터가 뚝뚝 끊겨 보여서 보간을 넣었습니다. 처음에는 `Lerp(현재, 목표, deltaTime * 10)`처럼 썼는데, 이렇게 하면 프레임이 높을수록 빨리 따라붙어서 60fps와 30fps에서 움직임이 달라 보였습니다. 지금은 프레임률과 무관하게 같은 속도로 수렴하도록 고쳤습니다.

```csharp
// 남은 거리가 시간에 대해 지수적으로 줄어들도록 — 프레임률이 달라도 결과가 같다
float t = 1f - Mathf.Exp(-LerpSpeed * Time.deltaTime);
transform.position = Vector3.Lerp(transform.position, targetPos, t);
```

## 3-2 객체 지향 설계

### 상속

흡수 모드와 밀치기 모드는 규칙은 다르지만 준비 과정이 같습니다. 둘 다 자기가 쓸 메시지를 등록하고, 판이 끝나면 해제하고, 접속이 끊기면 상태를 초기화해야 합니다. 처음에는 두 모드에 이 배선을 각각 적어뒀는데 한쪽에만 해제를 빠뜨려서 두 번째 판에서 이벤트가 두 번씩 들어오는 버그가 났습니다. 그래서 공통 부분을 부모 클래스로 올렸습니다.

```csharp
public abstract class NetGameMode<T> : MonoBehaviour where T : NetGameMode<T>
{
    public static T Instance { get; private set; }

    protected abstract GameModeType Mode { get; }

    protected static bool IsHost
    {
        get { return NetManager.Instance != null && NetManager.Instance.IsHost; }
    }

    protected bool IsPlaying
    {
        get { return LanGameFlow.IsPlaying(Mode); }
    }
}

public class AbsorbMode : NetGameMode<AbsorbMode> { ... }
public class PushMode   : NetGameMode<PushMode>   { ... }
```

부모를 제네릭으로 만든 이유는 `Instance` 때문입니다. 그냥 상속하면 `AbsorbMode.Instance`와 `PushMode.Instance`가 같은 변수를 공유해서 나중에 켜진 모드가 앞의 것을 덮어씁니다. 자식 타입을 타입 인자로 받으면 각자 별도의 `Instance`를 갖게 됩니다.

### 추상화

앞에서 만든 `INetTransport`가 여기에 해당합니다. 이 인터페이스에는 IP도 포트도 방 번호도 없습니다. 소켓 구현에는 IP가 있고 Photon 구현에는 없기 때문에, 둘 중 하나에만 있는 것은 인터페이스에 올리지 않았습니다.

방 목록도 같은 문제가 있었습니다. 원래 UI가 `LanDiscovery.RoomInfo`라는 타입을 그대로 받아서 화면에 뿌렸는데, 그 타입은 UDP 신호를 해석한 결과라 IP와 포트를 들고 있었습니다. 온라인에는 그 둘이 없어서 UI를 그대로 쓸 수 없었습니다. 그래서 화면이 실제로 필요한 것만 남긴 표를 따로 정의했습니다.

```csharp
public class RoomHandle
{
    // 전송이 이 방을 다시 찾는 데 쓰는 값.
    // 로컬은 "ip:port", 온라인은 방 이름. 바깥은 뜻을 해석하지 않고 그대로 돌려준다.
    public string Id;

    public string Address;      // 화면에 보여줄 주소
    public string HostName;
    public GameModeType Mode;
    public int Current;         // 지금 들어와 있는 사람 수
    public int Needed;          // 시작에 필요한 사람 수

    public bool IsFull { get { return Current >= Needed; } }
}
```

### 다형성

순위표, AI 표적 선정, 탈락 판정은 상대가 사람인지 봇인지 상관이 없습니다. 그런데 처음에는 사람 목록 한 번, 봇 목록 한 번 이렇게 두 벌로 돌고 있었습니다. 두 벌이면 한쪽만 고치는 일이 생기는데 실제로 그랬습니다. 사람 점수는 방송하는데 봇 점수는 방송하지 않아서, 클라이언트 화면에서만 봇 점수가 계속 0으로 보였습니다.

그래서 "밖에서 물어보는 것"만 모아 인터페이스로 만들었습니다.

```csharp
public interface INetEntity
{
    NetIdentity Identity { get; }
    Transform Transform { get; }
    int OwnerId { get; }
    bool IsBot { get; }
    string DisplayName { get; }
    float ScaleValue { get; }
    int Score { get; }
    Color VisualColor { get; }
    bool IsOutOfPlay { get; }       // 탈락했거나 흡수당하는 중
}

// 사람 → LanPlayerState, 봇 → LanBotState 가 각각 구현
```

`Transform`을 굳이 인터페이스에 넣은 이유가 있습니다. `MonoBehaviour`의 `transform`을 쓰려면 구체 타입을 알아야 해서, 그것 하나 때문에 밖에서 목록을 둘로 나눠 돌고 있었습니다.

반대로 "어떻게 움직이는가"는 넣지 않았습니다. 사람은 키보드 입력으로 움직이고 봇은 NavMesh로 움직여서 공통으로 물어볼 만한 게 없었습니다. 인터페이스는 밖에서 필요한 것만 담아야 한다는 걸 이때 알았습니다.

지금은 이렇게 한 벌로 돕니다.

```csharp
foreach (INetEntity e in EntityRegistry.Entities)
{
    if (e.IsOutOfPlay)
        continue;
    // 사람이든 봇이든 같은 코드로 처리
}
```

## 3-3 디자인 패턴

### 싱글톤 패턴

한 판에 하나만 있어야 하고 어디서든 접근해야 하는 것들에 썼습니다. 네트워크 관리자, 게임 진행 관리자, 타일 붕괴 관리자, 설정값 저장소 등입니다.

```csharp
public static NetWorld Instance { get; private set; }

private void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }
    Instance = this;
}

private void OnDestroy()
{
    if (Instance == this)
        Instance = null;
}
```

`OnDestroy`에서 자기 자신일 때만 `null`로 되돌리는 부분은 처음에 없었습니다. 씬을 옮길 때 예전 객체가 파괴되면서 새로 만들어진 객체의 `Instance`까지 지워버리는 일이 있었습니다.

싱글톤을 많이 쓰면 어디서든 접근할 수 있어서 편하지만 의존 관계가 잘 안 보이게 됩니다. 그래서 컴포넌트끼리 직접 연결할 수 있는 곳은 인스펙터에서 연결하고, 씬 전체에 하나뿐인 관리자에만 쓰도록 기준을 정했습니다.

### 옵저버 패턴

캐릭터 크기가 변할 때 반응해야 하는 곳이 여러 군데 있습니다. 화면 연출, 효과음, HUD 점수, 카메라 줌, 봇의 NavMesh 캡슐 크기 등입니다. 크기를 담당하는 클래스가 이들을 전부 알고 하나씩 호출하면, 나중에 반응할 것이 늘어날 때마다 그 클래스를 고쳐야 합니다.

```csharp
// PlayerScaleController — 크기의 단일 출처
public event Action<float> OnScaleSettled;      // 크기가 확정됐다
public event Action<bool>  OnGrowStarted;       // 자라기 시작했다
public event Action        OnScaleThresholdUp;  // 카메라 줌 문턱을 넘었다
public event Action        OnPostScalePhysics;  // 크기 반영이 끝났다
```

듣는 쪽은 각자 필요한 것만 구독합니다.

```csharp
// AIPlayerMovement — 몸집이 바뀌면 NavMeshAgent를 새 크기에 맞춘다
private void OnEnable()
{
    if (ScaleCtrl != null)
        ScaleCtrl.OnPostScalePhysics += UpdateScaleOnAgent;
}

private void OnDisable()
{
    if (ScaleCtrl != null)
        ScaleCtrl.OnPostScalePhysics -= UpdateScaleOnAgent;
}
```

이걸 쓰면서 배운 건 구독과 해제를 반드시 짝으로 맞춰야 한다는 점입니다. `OnEnable`에서 구독하고 `OnDisable`에서 해제하지 않으면, 오브젝트를 껐다 켤 때마다 구독이 쌓여서 이벤트 하나에 같은 함수가 여러 번 호출됩니다. 젤리를 한 번 먹었는데 효과음이 세 번 나는 버그가 이것 때문이었습니다.

한 가지 더 알게 된 건 이벤트를 아무 데나 쓰면 안 된다는 것입니다. 발행하는 쪽이 구독자를 몰라도 된다는 게 이벤트의 장점인데, 같은 오브젝트에 붙어 있고 수명도 같은 컴포넌트끼리 이벤트로 연결한 곳이 있었습니다. 그 경우에는 장점이 없고 흐름만 안 보여서 직접 호출로 바꿨습니다.

### 전략 패턴

로컬과 온라인은 접속 방식이 완전히 다르지만 게임 로직 입장에서는 "메시지를 보내고 받는다"로 똑같습니다. 그래서 두 구현을 같은 인터페이스로 만들어두고 실행 시점에 하나를 고르도록 했습니다.

```csharp
// NetManager — 로컬/온라인이 갈리는 곳은 여기 두 줄뿐이다
transport = online ? (INetTransport)photon : lan;
session   = online ? (INetSession)photonSession : lanSession;
```

게임 모드도 같은 방식입니다. `AbsorbMode`와 `PushMode`가 같은 부모를 상속하고, 선택한 모드에 따라 해당 씬의 것이 동작합니다.

## 3-4 AI

AI 관련 코드는 8개 파일 3,064줄입니다. NavMeshAgent를 쓰되 경로 계산에만 쓰고 가속과 회전은 직접 처리했습니다. Agent에 맡기면 플레이어와 움직임이 달라서 같이 있을 때 어색했기 때문입니다.

### FSM 사용

봇은 배회, 추격, 도주, 밀치기 생존 네 가지 상태를 가집니다. 플레이어도 대기, 이동, 점프, 대시, 공격, 넉백 여섯 가지 상태로 같은 구조를 씁니다. 처음에는 `Update` 안에서 `if`로 나눴는데 조건이 늘어날수록 서로 얽혀서, 도망 중인지 추격 중인지에 따라 같은 코드가 다르게 동작하는 상황이 생겼습니다.

```csharp
public abstract class AIBaseState
{
    protected AIPlayerMovement ai;

    public AIBaseState(AIPlayerMovement ai)
    {
        this.ai = ai;
    }

    public abstract void Enter();   // 상태에 들어올 때 1회
    public abstract void Update();  // 매 프레임
    public abstract void Exit();    // 나갈 때 1회
}
```

상태를 나누고 나서 각 상태가 자기 일만 신경 쓰면 되니까 훨씬 읽기 쉬워졌습니다.

한동안 문제가 됐던 건 상태들이 공통으로 하는 일이었습니다. 이 게임은 발판이 순서대로 무너지기 때문에, 봇이 목적지를 정하면 그 경로가 곧 무너질 칸을 지나는지 확인해야 합니다. 그 검사가 세 개 상태에 글자까지 똑같이 복사돼 있었고, 배회 상태에만 빠져 있었습니다. 그래서 봇이 평소에는 잘 피하다가 배회할 때만 무너지는 발판으로 걸어 들어가 떨어졌습니다.

지금은 부모 클래스에 올려두고 각 상태가 그걸 부릅니다.

```csharp
// AIBaseState — 경로가 위험 구간을 지나지 않을 때만 적용한다
protected bool TrySetSafePath(Vector3 destination) { ... }

// 끼임 감지도 세 상태가 똑같이 하던 일이라 함께 올렸다
protected void HandleStuck() { ... }
```

## 3-5 최적화와 메모리 관리

### 오브젝트 풀링

젤리는 초당 여러 번 생기고 사라집니다. 매번 `Instantiate`와 `Destroy`를 하면 그때마다 메모리를 새로 잡고 버리게 되어, 가비지 컬렉션이 돌면서 순간적으로 프레임이 떨어집니다. 그래서 미리 만들어두고 껐다 켜서 다시 쓰도록 했습니다.

풀링 자체는 유니티가 `UnityEngine.Pool.ObjectPool<T>`를 제공하고 있어서 직접 만들지 않고 그걸 감싸는 방식으로 썼습니다.

```csharp
public class ComponentPool<T> where T : Component
{
    private readonly ObjectPool<T> pool;

    public ComponentPool(T prefab, Transform parent = null, int prewarmCount = 0)
    {
        pool = new ObjectPool<T>(
            createFunc:      Create,
            actionOnGet:     item => item.gameObject.SetActive(true),
            actionOnRelease: item => item.gameObject.SetActive(false),
            actionOnDestroy: item => Object.Destroy(item.gameObject),
            collectionCheck: true,
            defaultCapacity: Mathf.Max(1, prewarmCount),
            maxSize:         MAX_SIZE);

        Prewarm(prewarmCount);
    }

    private T Create()
    {
        // 세 번째 인자 false가 중요하다. 빼먹으면 유니티가 월드 스케일을 유지하려고
        // 자식의 localScale에 (프리팹 크기 / 부모 크기)를 넣는다. 부모인 캐릭터는
        // 성장하면서 크기가 변하기 때문에, 생성 시점에 따라 인스턴스 크기가 달라진다.
        T instance = Object.Instantiate(prefab, parent, false);
        instance.gameObject.SetActive(false);
        return instance;
    }
}
```

주석에 적은 문제는 실제로 겪은 것입니다. 화면 밖 플레이어를 가리키는 삼각형 표시가 사람마다 크기가 다르게 나와서 한참 찾았는데, 원인이 `Instantiate`의 기본 동작이었습니다.

모든 것을 풀링하지는 않았습니다. 플레이어와 봇은 한 판에 몇 번 안 생기는데다 네트워크 상태를 들고 있어서, 재사용하면 이전 판의 상태가 남을 위험이 더 큽니다. 그래서 젤리처럼 자주 생기고 상태가 단순한 것만 풀링하도록 기준을 정했습니다.

```csharp
private bool IsPoolable(int prefabId)
{
    if (prefabId < NetConfig.JELLY_PREFAB_START)
        return false;

    GameObject prefab = prefabs[prefabId];

    if (prefab.GetComponentInChildren<PlayerMovement>(true) != null)
        return false;
    if (prefab.GetComponentInChildren<AIPlayerMovement>(true) != null)
        return false;

    return true;
}
```

### HashSet

씬에 있는 플레이어와 젤리 목록을 관리하는 레지스트리를 만들었습니다. 처음에는 필요할 때마다 `FindObjectsByType`으로 찾았는데, AI 탐지와 순위표와 화면 밖 표시가 각각 매 프레임 씬 전체를 훑고 있어서 부하가 컸습니다. 그래서 오브젝트가 켜질 때 스스로 등록하고 꺼질 때 빠지는 방식으로 바꿨습니다.

목록 자료구조로는 `HashSet`을 골랐습니다. 중복 등록이 자동으로 걸러지고 추가와 삭제가 `O(1)`이라 매 프레임 드나드는 상황에 맞았습니다.

그런데 문제가 하나 있었습니다. `HashSet`은 `foreach`로 도는 도중에 원소가 추가되거나 삭제되면 예외를 던집니다. 멀티플레이에서는 한 프레임에 여러 캐릭터가 동시에 탈락하는 일이 있고, 그러면 AI가 목록을 돌고 있는 중에 `Unregister`가 불려서 실제로 게임이 멈췄습니다.

그래서 저장은 `HashSet`으로 하되 밖으로는 복사본을 내보내도록 했습니다.

```csharp
private static readonly HashSet<INetEntity> entities = new HashSet<INetEntity>();
private static List<INetEntity> entitiesSnapshot = new List<INetEntity>();
private static bool entitiesDirty = true;

public static IReadOnlyList<INetEntity> Entities
{
    get
    {
        if (entitiesDirty)
        {
            entitiesSnapshot = new List<INetEntity>(entities);
            entitiesDirty = false;
        }
        return entitiesSnapshot;
    }
}

// 등록·해제는 플래그만 세운다. 복사본은 건드리지 않는다.
public static void Register(INetEntity e)
{
    if (entities.Add(e))
        entitiesDirty = true;
}
```

새 `List`를 만들어서 넘기기 때문에 순회 도중에 원소가 사라져도 이미 돌고 있던 `foreach`는 예전 복사본을 그대로 돕니다. 그리고 변경이 없으면 만들어둔 것을 다시 쓰므로, 아무도 들어오고 나가지 않는 동안에는 매 프레임 새로 할당하지 않습니다.
