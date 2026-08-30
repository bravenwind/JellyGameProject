using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JellyNet;

public class ChocolateFluid : MonoBehaviour
{
    [Header("부력")]
    [Tooltip("완전히 잠겼을 때의 부력 가속도. 클수록 수면 높이를 단단히 지켜 물결에 덜 잠긴다.")]
    [SerializeField] private float buoyancyForce = 35f;

    [Tooltip("이만큼 잠기면 부력이 최대가 된다. 작을수록 수면에 딱 붙고, 크면 물렁하게 잠긴다.")]
    [SerializeField] private float fullSubmergeDepth = 1.2f;

    [Tooltip("★ 얼마나 잠겨 보이는지는 이 값이 정한다. 떠 있을 때 물체의 원점이 수면보다 이만큼 아래에 머문다.")]
    [SerializeField] private float restDepth = 0.3f;

    [Tooltip("콜라이더 윗면과 눈에 보이는 초콜릿 표면의 높이 차이. 보이는 면이 더 높으면 양수.")]
    [SerializeField] private float surfaceOffset = 0.25f;

    [Tooltip("초콜릿의 점성. 크면 걸쭉해서 안 움직이고, 작으면 물처럼 미끄러진다.")]
    [SerializeField] private float chocolateViscosity = 3f;

    [Header("흐름과 물결")]
    [Tooltip("수평(X, Z)으로 흐르는 힘")]
    [SerializeField] private float flowForce = 6f;

    [Tooltip("흐름 방향이 도는 속도(rad/s). 작을수록 한 방향으로 길게 흘러간다.")]
    [SerializeField] private float flowTurnSpeed = 0.15f;

    [Tooltip("Y축 출렁임 속도 (파도의 빠르기)")]
    [SerializeField] private float waveSpeed = 1.6f;

    [Tooltip("Y축 출렁임 강도 (위아래로 밀어주는 힘)")]
    [SerializeField] private float waveForce = 4f;

    [Tooltip("물결에 맞춰 기우뚱거리는 힘. 0이면 차렷 자세로 굳은 채 떠 있는다.")]
    [SerializeField] private float rockTorque = 1.5f;

    [Tooltip("수면에 부딪힐 때 남기는 속도 비율. 0에 가까울수록 첨벙 하고 바로 멈춘다.")]
    [Range(0f, 1f)]
    [SerializeField] private float entrySpeedKeep = 0.2f;

    [Header("수명 설정")]
    [Tooltip("초콜릿에 빠진 오브젝트가 자동 비활성화되기까지의 시간 (초). 0이면 비활성화 안 함")]
    [SerializeField] private float floatingLifetime = 5f;

    [Header("디버그")]
    [SerializeField] private bool debugLogTriggers = false;

    // ═══════════════════════════════════════════════════════════
    //  수면 높이 — transform.position.y가 아니다
    // ═══════════════════════════════════════════════════════════
    //
    // ★ 여기서 "그 자리에 멈춰 있다"가 나왔다
    //   예전엔 수면을 transform.position.y로 잡았다. 그런데 트리거 박스는
    //   중심 오프셋(-0.06)과 스케일(50)을 갖는다. 실제 초콜릿의 윗면은
    //   transform보다 0.25m <b>아래</b>에 있었다.
    //
    //   그래서 부력이 꺼지는 높이가 물 밖 허공이 되어, 물체는 트리거를
    //   빠져나온 허공에서 부력도 중력도 없는 상태로 <b>정지</b>했다.
    //   콜라이더에서 직접 읽으면 이런 어긋남이 생길 수 없다.
    //
    //   보이는 초콜릿 면(메시)과 콜라이더 윗면이 정확히 같지는 않으므로
    //   surfaceOffset으로 눈에 보이는 높이에 맞춘다. 이 값을 안 맞추면
    //   물리적으로는 떠 있는데 화면에서는 초콜릿에 파묻혀 보인다.
    private BoxCollider waterBox;

    private float SurfaceY => (waterBox != null ? waterBox.bounds.max.y : transform.position.y) + surfaceOffset;

    private void Awake()
    {
        waterBox = GetComponent<BoxCollider>();
    }

    private struct FloatData
    {
        public float phase;
        public float speedMul;
        public float forceMul;
        public Vector2 flowOffset;
    }

    /// <summary>
    /// 초콜릿에 들어온 몸과 그 몸의 출렁임 개성. <b>'초콜릿에 든 목록'이기도 하다.</b>
    ///
    /// ★ 예전엔 컬렉션이 둘이었다
    ///   HashSet processedBodies가 '누가 들어와 있나'를, 이 Dictionary가 '어떻게 출렁이나'를
    ///   따로 들고 있었다. 그런데 넣는 것도 빼는 것도 <b>항상 쌍으로</b> 일어나서
    ///   내용이 언제나 같았다 — 같은 목록을 두 자료구조로 관리하고 있었던 셈이다.
    ///   하나면 제거를 한 곳에서 하고, 짝이 어긋날 자리도 없어진다.
    /// </summary>
    private readonly Dictionary<Rigidbody, FloatData> floatData = new Dictionary<Rigidbody, FloatData>();

    /// <summary>
    /// 수면보다 이만큼 위로 올라와야 '초콜릿을 벗어났다'로 본다.
    ///
    /// ★ 이 여유가 없으면 떠 있는 물체가 매 물결마다 방출된다
    ///   물체는 restY(= 수면 − restDepth)에 머무는데, 그 높이가 하필 <b>트리거 박스
    ///   윗면에서 5cm 아래</b>다. 그래서 출렁일 때마다 경계를 들락날락하고
    ///   OnTriggerExit가 수시로 불린다. 딱 수면 높이로 자르면 그때마다 목록에서 빠져
    ///   부력을 잃고 허공에 굳는다.
    ///
    /// ★ 그래서 실제로는 거의 안 불린다 (수치로 확인함)
    ///   방출까지 필요한 상승: 박스 윗면 + 1.25 − (박스 윗면 − 0.05) = <b>1.30 m</b>
    ///   물결만으로 뜰 수 있는 최대: 부력 스프링(35 m/s²)과 물결(최대 8 m/s²)이
    ///   균형을 이루는 35h/1.2 = 8 → <b>0.27 m</b>
    ///
    ///   필요치의 5분의 1이다. 정상적으로 떠다니는 물체는 절대 이 선을 못 넘는다.
    ///   넘는 건 충돌뿐이다 — 무너진 타일이 30m 낙하해 처박히거나, 탈락한 몸이
    ///   떨어지며 소품을 걷어차는 경우. 그때 놓아주지 않으면 그 물체는 중력이 꺼진 채
    ///   초콜릿 밖 허공에 굳는다. OnTriggerExit는 그 안전망으로만 남아 있다.
    /// </summary>
    private const float ReleaseMargin = 1f;

    /// <summary>
    /// 흐름·물결에 쓸 시간. 판이 돌고 있으면 호스트가 맞춰주는 경과 시간을,
    /// 아직 안 시작했으면 로컬 시계를 쓴다 — 물결은 대기 중에도 움직여야 하기 때문이다.
    /// </summary>
    private static float SyncedTime
    {
        get
        {
            float elapsed = LanGameFlow.SyncedElapsed;
            return elapsed >= 0f ? elapsed : Time.time;
        }
    }

    // ★ 예전엔 흐름 방향을 구간마다 <b>난수로 새로 뽑았다.</b> 1초마다 방향이 뚝뚝 끊겨
    //   왼쪽으로 밀렸다 오른쪽으로 밀렸다 하며 제자리에서 진동만 했다.
    //   느리게 도는 각도로 바꾸면 결정적(같은 시간 = 같은 방향)이면서도 흐름이 이어져
    //   물체가 실제로 하류로 떠내려간다.
    private Vector3 FlowDirection()
    {
        float angle = SyncedTime * flowTurnSpeed;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private readonly List<Rigidbody> floatingSnapshot = new List<Rigidbody>();

    private void FixedUpdate()
    {
        if (floatData.Count == 0)
            return;

        //떠 있을 때 원점이 머무는 높이. 여기를 기준으로 위아래 양쪽에서 되돌린다
        float restY = SurfaceY - restDepth;
        float depthScale = Mathf.Max(0.01f, fullSubmergeDepth);
        Vector3 flow = FlowDirection();
        float t = SyncedTime;

        floatingSnapshot.Clear();
        floatingSnapshot.AddRange(floatData.Keys);

        for (int i = 0; i < floatingSnapshot.Count; i++)
        {
            Rigidbody rb = floatingSnapshot[i];

            // ★ 파괴된 항목은 여기서 지운다 (예전엔 OnTriggerStay가 5초마다 훑었다)
            //   유니티는 Destroy된 오브젝트를 '가짜 null'로 만든다 — ==는 null을 돌려주지만
            //   C# 참조는 살아 있어서 이 키로 Remove하면 정상적으로 찾아 지운다.
            //   스냅샷을 돌고 있으므로 원본을 건드려도 순회가 깨지지 않는다.
            if (rb == null)
            {
                floatData.Remove(rb);
                continue;
            }

            FloatData fd = floatData[rb];

            // ★ 목록에 있는 동안은 물리 설정을 지킨다
            //   진입 때 한 번만 설정하면 다른 경로가 되돌려 놓을 수 있다 —
            //   흡수가 거부된 젤리는 JellyColliderAbsorb.RestoreToEdible이 중력을 켜고
            //   (에이전트 없는 젤리) 클라에서는 kinematic으로 되돌린다. 그러면 초콜릿
            //   속에서 가라앉거나 굳는다. 목록에 있다는 게 곧 자격이므로 조건은 필요 없다.
            if (rb.isKinematic)
                rb.isKinematic = false;

            if (rb.useGravity)
                rb.useGravity = false;

            // ═════════════════════════════════════════════
            //  부력은 스위치가 아니라 스프링이다
            // ═════════════════════════════════════════════
            //
            // ★ 예전엔 "수면 아래면 위로 민다"는 <b>켜짐/꺼짐</b>이었다.
            //   중력은 이미 꺼둔 상태라, 물체가 수면에 닿는 순간 위로 미는 힘도
            //   아래로 당기는 힘도 <b>둘 다 0</b>이 되는 죽은 구간이 생겼다.
            //   거기 들어간 물체는 감쇠로 속도만 잃고 그대로 굳어버린다.
            //   "빠지면 그 자리에 Idle 애니메이션만 재생하며 멈춘다"의 정체다.
            //
            //   잠긴 깊이에 비례하는 힘으로 바꾸면 수면 위로 뜬 만큼은 다시
            //   끌어내려진다. 되돌리는 힘이 생겨야 비로소 물체가 '출렁'인다.
            float submerged = Mathf.Clamp((restY - rb.position.y) / depthScale, -1f, 1f);
            rb.AddForce(Vector3.up * (submerged * buoyancyForce), ForceMode.Acceleration);

            float waveY = Mathf.Sin(t * waveSpeed * fd.speedMul + fd.phase);

            rb.AddForce(new Vector3(
                (flow.x + fd.flowOffset.x) * flowForce,
                waveY * waveForce * fd.forceMul,
                (flow.z + fd.flowOffset.y) * flowForce
            ), ForceMode.Acceleration);

            //물결에 맞춰 기우뚱거린다. 이게 없으면 젤리가 차렷 자세로 굳은 채
            //수면 위를 미끄러져 '떠 있다'가 아니라 '멈춰 있다'로 보인다
            if (rockTorque > 0f)
                rb.AddTorque(new Vector3(Mathf.Cos(fd.phase), 0f, Mathf.Sin(fd.phase)) * (waveY * rockTorque), ForceMode.Acceleration);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // ★ 캐릭터는 트리거 콜라이더가 둘이다 — 대표 하나만 받는다
        //   안 걸면 탈락 신고와 ApplyFloatPhysics가 개체당 두 번 돈다.
        //   (GameTags.IsCharacterMainCollider 주석에 이 이중 호출이 만든 사고들을 적어뒀다)
        if (other.attachedRigidbody != null && IsCharacter(other.attachedRigidbody)
            && !GameTags.IsCharacterMainCollider(other))
            return;

        // ═════════════════════════════════════════════
        //  사람 플레이어 탈락
        // ═════════════════════════════════════════════
        //
        // ★ 이 갈래가 없으면 사람이 아래 Rigidbody 분기로 흘러
        //   '떠다니는 물체' 취급만 받고 죽지 않는다.
        //
        // ★ 신고와 몸 처리는 서로 다른 일이다
        //   신고 조건(IsMine·IsOutOfPlay·LanGameFlow null)은 LanPlayerState가 알고,
        //   "초콜릿에 들어온 몸을 어떻게 할지"는 여기가 안다. 사람이든 봇이든 소품이든
        //   똑같이 ApplyFloatPhysics 하나로 끝난다.
        //
        // ★ 예전엔 신고만 하고 return 했다
        //   물리 설정을 건너뛰어서 사람은 부력·점성을 하나도 못 받고 <b>중력만으로
        //   끝없이 떨어졌다.</b> 같은 초콜릿에서 봇은 둥둥 떠 있는데도.
        //
        // ★ 탈락 확정을 기다리지 않고 지금 물리로 넘긴다
        //   탈락은 호스트 왕복 뒤에 확정된다. 그 사이 CharacterController가 계속 캐릭터를
        //   몰기 때문에 <b>두께 0.11짜리 얇은 초콜릿 판을 그대로 통과</b>한다. 빠져나간 뒤엔
        //   트리거 밖이라 부력도 흐름도 못 받아 허공에 멈춰 선다.
        //   ApplyFloatPhysics가 진입 프레임에 물리로 바꿔 그 자리에서 제동을 건다.
        LanPlayerState lanPlayer = other.GetComponentInParent<LanPlayerState>();

        if (lanPlayer != null)
        {
            lanPlayer.ReportFellOutOfPlay("초콜릿에 빠졌습니다!");
            ApplyFloatPhysics(other.attachedRigidbody);
            return;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        AIPlayerMovement aiPlayer = rb.GetComponent<AIPlayerMovement>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();

        bool isEdible = other.CompareTag(GameTags.Edible) || rb.gameObject.CompareTag(GameTags.Edible);
        bool isCandy = other.CompareTag(GameTags.Sphere) || rb.gameObject.CompareTag(GameTags.Sphere);

        int bgLayer = GameLayers.BackGroundObject;
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);

        //바로 위에서 이미 찾아둔 것을 쓴다. 예전엔 같은 GetComponent를 두 번 더 돌렸다.
        //여기서 묻는 건 신원이 아니라 '스스로 움직이는 두뇌가 있나'라서 INetEntity가 아니다 —
        //배회 젤리(WanderingAI)도 포함해야 한다. 두뇌를 실제로 끄는 건 PhysicsFall이 한다.
        bool isAI = aiPlayer != null || wanderingAI != null;

#if UNITY_EDITOR
        // [X5] 트리거 로그는 에디터 전용 — 빌드에서는 플래그가 켜져 있어도 문자열 보간(GC)과
        // Debug.Log(스택트레이스 수집) 비용이 트리거마다 발생하지 않게 한다.
        if (debugLogTriggers)
        {
            string category = isEdible ? "Edible" : isAI ? "AI" : isBackgroundObject ? "BG" : isCandy ? "Candy" : "기타(무시)";
            Debug.Log($"[Chocolate] ENTER [{category}]: {other.name}의 부모 {rb.name} 진입 중!");
        }
#endif

        if (isEdible || isAI || isBackgroundObject || isCandy)
        {
            // 두뇌(WanderingAI·NavMeshAgent)를 끄는 것도 여기 안에서 PhysicsFall이 한다.
            // 예전엔 이 아래에 루트만 훑는 별도 코드가 있었다 — FallingTile은 자식까지
            // 훑고 있어서 같은 일을 하는 코드 두 벌의 범위가 서로 달랐다.
            ApplyFloatPhysics(rb);

            if (!isAI && floatingLifetime > 0f)
                StartCoroutine(DeactivateAfterDelay(rb.gameObject, floatingLifetime));
        }

        if (aiPlayer != null)
        {
            //탈락 판정 권한은 OnEliminated 안에서 IsDriver로 이미 본다.
            //여기서 또 보면 규칙이 두 곳에 생기고, botId가 없을 때의 폴백까지 달라진다
            aiPlayer.ReportEliminated();
        }
    }

    /// <summary>
    /// 초콜릿에 빠진 물체를 '뜨는 상태'로 바꾼다. 사람·봇·소품이 같은 설정을 쓴다.
    ///
    /// 물리로 넘기는 것 자체(조종 장치 끄기·kinematic 해제·깨우기)는 PhysicsFall이 하고,
    /// 여기서는 그 위에 <b>'액체 안'이라는 조건만 덧칠</b>한다.
    /// </summary>
    private void ApplyFloatPhysics(Rigidbody rb)
    {
        if (rb == null)
            return;

        // 원격 사본은 NetTransform이 위치를 몰고 있다. 여기서 물리까지 켜면 서로를 민다.
        // (씬에 손으로 놓은 것은 위치를 주고받지 않으므로 예외 — NetEntity 주석 참고)
        if (NetEntity.IsDrivenElsewhere(rb))
            return;

        //중력은 켜지 않는다 — 액체 안에서는 부력이 그 자리를 대신한다.
        //예전엔 Begin이 무조건 켜고 여기서 바로 다시 껐다.
        PhysicsFall.Begin(rb.gameObject, useGravity: false);

        rb.linearDamping = chocolateViscosity;
        rb.angularDamping = chocolateViscosity;

        //물에 부딪히면 속도를 크게 잃는다. 안 죽이면 23m/s로 들어와 4m나 잠수한 뒤
        //0.58m/s로 6초에 걸쳐 올라온다 — 그동안은 '가라앉아 안 보이는' 상태다
        rb.linearVelocity *= entrySpeedKeep;

        //목록에 넣으면서 출렁임 개성도 이때 정한다. 예전엔 첫 FixedUpdate에서 만들었는데,
        //컬렉션이 하나가 되면서 '들어왔지만 개성이 아직 없는' 중간 상태가 사라졌다.
        if (!floatData.ContainsKey(rb))
            floatData[rb] = CreateFloatData(rb);
    }

    /// <summary>
    /// 사람이나 봇인가. 초콜릿 물리를 받아야 하는 캐릭터인지 판단한다.
    ///
    /// ★ 두뇌(AIPlayerMovement)가 아니라 신원(INetEntity)에게 묻는다
    ///   "판에 참가한 개체인가"의 단일 출처는 INetEntity고, 구현체는 사람 LanPlayerState와
    ///   봇 LanBotState 둘뿐이다. 예전엔 봇 쪽을 AIPlayerMovement로 물었는데, 그건
    ///   <b>어떻게 움직이는가</b>를 담당하는 클래스지 신원이 아니다.
    ///   (배회 젤리는 WanderingAI만 있고 LanBotState가 없어 지금도 대상이 아니다)
    /// </summary>
    private static bool IsCharacter(Rigidbody rb)
    {
        return rb.GetComponent<INetEntity>() != null;
    }

    /// <summary>
    /// 이미 판에서 빠져 초콜릿에 잠긴 캐릭터인가. 이런 몸은 초콜릿에서 놓아줄 이유가 없다.
    ///
    /// ★ 예전엔 사람과 봇의 기준이 서로 달랐다
    ///   사람은 IsOutOfPlay(탈락 + 흡수당하는 중)를 봤는데 봇은 IsEliminated(탈락만)를 봤다.
    ///   그래서 흡수당하는 중인 봇만 OnTriggerExit의 복구 경로를 탔다.
    ///   LanBotState가 그 비대칭을 자기 안에 가둬 IsOutOfPlay 하나로 내주고 있었는데,
    ///   여기서 담장을 넘어 두뇌를 직접 들여다보는 바람에 그 정리가 무의미해져 있었다.
    /// </summary>
    private static bool IsSunkCharacter(Rigidbody rb)
    {
        INetEntity entity = rb.GetComponent<INetEntity>();
        return entity != null && entity.IsOutOfPlay;
    }

    private const float HashModulus = 1000f;

    /// <summary>
    /// 개체마다 다르지만 <b>항상 같은</b> 0~1 값. 곱하고 나머지만 남기는 싸구려 해시다.
    /// (타일 흔들림의 12.9898 / 78.233과 같은 부류)
    ///
    /// Random.Range를 쓰지 않는 이유는 안정성이다 — 저장하지 않아도 같은 오브젝트가
    /// 늘 같은 값을 받고, 초콜릿에서 나갔다 다시 들어와도 위상이 이어져 툭 튀지 않는다.
    /// </summary>
    private static float Hash01(int seed, float multiplier)
    {
        return ((seed * multiplier) % HashModulus) / HashModulus;
    }

    private static FloatData CreateFloatData(Rigidbody rb)
    {
        // ★ 부호 비트를 떼어낸다 (한 번 이걸로 절반의 물체가 거의 안 움직였다)
        //   GetInstanceID는 음수가 나올 수 있고, C#의 %는 <b>피제수의 부호를 따른다.</b>
        //   그래서 음수 ID면 해시가 -1~0이 되어 구간이 통째로 갈라졌다.
        //     speedMul  [0.7, 1.3) 이어야 하는데 → (0.1, 0.7]   (최대 7배 느림)
        //     forceMul  [1.2, 2.0) 이어야 하는데 → (0.4, 1.2]   (최대 3배 약함)
        //     flowOffset ±0.3 대칭이어야 하는데 → [-0.9, -0.3)  (한쪽으로만 쏠림)
        //   flowForce가 6이라 마지막 것은 흐름 방향 자체를 뒤집을 수 있는 크기다.
        //
        //   Mathf.Abs가 아니라 마스크인 건 int.MinValue 때문이다 —
        //   그 값은 부호를 뒤집어도 자기 자신(음수)이라 Abs로는 안 걸러진다.
        int seed = rb.GetInstanceID() & 0x7FFFFFFF;

        // 계수를 넷 다 다르게 쓰는 건 값끼리 연동되지 않게 하려는 것이다.
        // 같은 계수면 "빠르게 출렁이는 놈은 항상 세게도 출렁인다"가 되어 규칙이 눈에 보인다.
        return new FloatData
        {
            phase = Hash01(seed, 2.3f) * (Mathf.PI * 2f),
            speedMul = Mathf.Lerp(0.7f, 1.3f, Hash01(seed, 7.9f)),
            forceMul = Mathf.Lerp(1.2f, 2.0f, Hash01(seed, 3.1f)),
            flowOffset = new Vector2(
                Mathf.Lerp(-0.3f, 0.3f, Hash01(seed, 5.7f)),
                Mathf.Lerp(-0.3f, 0.3f, Hash01(seed, 11.3f)))
        };
    }

    /// <summary>
    /// 초콜릿에 빠진 소품을 일정 시간 뒤 치운다.
    ///
    /// ★ 네트워크가 아는 것과 모르는 것을 다르게 치운다
    ///   예전엔 둘 다 SetActive(false)였다. 그런데 이 코루틴은 <b>기계마다 따로</b> 돌고
    ///   시작 시각은 그 화면에서 초콜릿에 닿은 순간이라, 화면마다 사라지는 시각이 달랐다.
    ///   더 나쁜 건 NetWorld의 스폰 장부와 무관하게 꺼진다는 것이다 — 장부에는 살아 있는데
    ///   화면에서만 사라지고, 풀로 돌아가지 않아 다시 쓸 수도 없다.
    ///
    ///   그래서 NetIdentity가 있으면 <b>호스트만</b> HostDespawn을 부른다. 그러면
    ///   DespawnEntity가 전원에게 퍼져 동시에 사라지고, NetWorld가 풀 반납(스폰물)과
    ///   removedSceneIds 기록(씬 배치물, 늦게 들어온 클라도 없는 걸로 본다)까지 해준다.
    ///
    ///   NetIdentity가 없는 순수 장식(밀크·사탕 등)은 아무도 모르는 물건이라 그냥 끈다.
    /// </summary>
    private IEnumerator DeactivateAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (obj == null)
            yield break;

        Rigidbody rb = obj.GetComponent<Rigidbody>();

        if (rb != null)
        {
            floatData.Remove(rb);
        }

        NetIdentity id = obj.GetComponentInParent<NetIdentity>();

        if (id == null)
        {
            obj.SetActive(false);
            yield break;
        }

        //클라는 아무것도 하지 않는다. 호스트가 보내주는 DespawnEntity를 받아 따라간다.
        if (NetWorld.Instance != null && NetManager.Instance != null && NetManager.Instance.IsHost)
            NetWorld.Instance.HostDespawn(id.NetId);
    }

    private void OnDisable()
    {
        floatData.Clear();
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        // ★ 탈락해서 잠긴 캐릭터는 놓아주지 않는다
        //   다시 조종할 일도, 초콜릿 밖에서 쓸 일도 없다. 놓는 순간 부력과 흐름이 끊기는데
        //   중력은 진입 때 꺼둔 그대로라 <b>합력 0으로 허공에 굳는다.</b>
        //   감쇠·중력을 되돌려 주는 길도 있지만, 애초에 놓을 이유가 없으니 안 놓는 게 맞다.
        if (IsSunkCharacter(rb))
            return;

        // ★ '아래로 빠져나간 것'은 나간 게 아니라 통과한 것이다
        //   빠르게 떨어지면 트리거를 뚫고 지나간다. 그때 목록에서 빼버리면
        //   부력을 못 받아 영영 아래로 사라진다. 수면 위로 나갔을 때만 '탈출'로 본다.
        //
        //   기준은 transform이 아니라 콜라이더 윗면이다. transform을 쓰면 실제 윗면보다
        //   높아서 <b>모든 탈출이 통과로 오인</b>되고, 목록에서 아무도 빠지지 않았다.
        //
        //   딱 수면 높이로 자르면 안 된다. 떠 있는 물체는 물결을 타고 수면을 오르내리므로
        //   그때마다 목록에서 빠져 부력을 잃고 허공에 굳는다. 확실히 튀어나왔을 때만 놓는다.
        if (rb.position.y < SurfaceY + ReleaseMargin)
            return;

        floatData.Remove(rb);

        int bgLayer = GameLayers.BackGroundObject;
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);
        // [MAP-3] 캐릭터도 damping 복원 대상에 포함한다. 예전엔 Edible/배경만 복원해서, 탈락이
        // 확정되기 전에 부력에 튕겨 초콜릿을 빠져나간 몸에 chocolateViscosity가 영구 잔존했다
        // → 이후 그 몸이 다시 물리 낙하할 때 과감쇠로 궤적이 비정상적으로 느려진다.
        //
        // ★ 그 수정이 봇에만 적용돼 있었다
        //   조건이 AIPlayerMovement != null이라 <b>사람은 그대로 남았다.</b> 사람도 탈락이
        //   확정되기 전에 튕겨 나오면 똑같이 굳는데도. IsCharacter로 바꿔 둘을 함께 본다.
        //   (판에서 빠진 몸은 위 IsSunkCharacter에서 이미 돌아갔으므로 여기 오는 건 전부 살아 있다)
        if (other.CompareTag(GameTags.Edible) || isBackgroundObject || IsCharacter(rb))
        {
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            if (isBackgroundObject)
                rb.useGravity = true;
        }

        // ★ NavMeshAgent를 되살리는 경로가 여기 25줄쯤 더 있었는데 지웠다
        //   도달할 수 있는 대상이 사실상 없다:
        //     봇   — 초콜릿에 닿는 즉시 탈락한다. 위 IsSunkCharacter에서 이미 돌아간다.
        //     사람 — agent 자체가 없다.
        //     배회 젤리 — 유일하게 남는데, 초콜릿 위에는 NavMesh가 없어
        //                SamplePosition(10m)이 대개 실패한다.
        //
        //   주석에 적혀 있던 [LAN 이식] 사고들(원격에서 agent를 켜서 젤리가 떨림 등)은
        //   탈락 처리가 지금처럼 확실해지기 전 이야기다. 도달하지 않는 코드는 검증되지
        //   않은 채 남아 있다가, 나중에 조건이 바뀌면 아무도 모르게 되살아난다.
        //   되살릴 일이 생기면 그때 필요한 조건을 다시 세워 쓰는 편이 낫다.
    }

}