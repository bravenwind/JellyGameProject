using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
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
    [SerializeField] private bool debugLogTriggers = true;

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

    // OnTriggerEnter에서 이미 물리 설정 완료된 Rigidbody 캐싱 (Stay에서 중복 GetComponent 방지)
    private readonly HashSet<Rigidbody> processedBodies = new HashSet<Rigidbody>();

    private struct FloatData
    {
        public float phase;
        public float speedMul;
        public float forceMul;
        public Vector2 flowOffset;
    }
    private readonly Dictionary<Rigidbody, FloatData> floatData = new Dictionary<Rigidbody, FloatData>();

    private const float PurgeInterval = 5f;

    /// <summary>수면보다 이만큼 위로 올라와야 '초콜릿을 벗어났다'로 본다. 물결 출렁임에 놓치지 않기 위한 여유.</summary>
    private const float ReleaseMargin = 1f;

    private float lastPurgeTime;

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

    private void OnTriggerStay(Collider other)
    {
        if (Time.time - lastPurgeTime >= PurgeInterval)
        {
            PurgeDestroyedEntries();
            lastPurgeTime = Time.time;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        // ★ 매 프레임 다시 확인한다 — 한 번 설정하고 끝내면 안 된다
        //   사람은 초콜릿에 닿은 <b>뒤에</b> 탈락이 확정되고, 그때 PhysicsFall이
        //   useGravity를 다시 켠다. 진입 시점에만 설정하면 그 한 번에 덮여
        //   중력이 되살아나고 그대로 무한히 떨어진다.
        //   여기서 계속 눌러주면 어느 순서로 와도 결국 물에 뜬다.
        if (rb.useGravity || rb.isKinematic)
        {
            bool isEdible = other.CompareTag(GameTags.Edible);
            int bgLayer = GameLayers.BackGroundObject;
            bool isBackgroundObject = (bgLayer >= 0) &&
                (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);

            //캐릭터는 트리거 콜라이더가 둘이라 대표 하나만 받는다(GameTags.IsCharacterProxy 주석 참고).
            //소품·젤리는 콜라이더가 하나뿐이라 그대로 통과한다
            bool isCharacter = IsCharacter(rb);

            if (isCharacter && !GameTags.IsCharacterProxy(other))
                return;

            if (isEdible || isBackgroundObject || isCharacter)
                ApplyFloatPhysics(rb);
        }

        //힘은 여기서 주지 않는다 — 아래 FixedUpdate가 '초콜릿에 든 목록'을 보고 준다.
        //이유는 FixedUpdate 주석 참고
    }

    // ─────────────────────────────────────────────────────────
    //  부력·흐름 — 트리거 체류가 아니라 '목록'을 기준으로
    // ─────────────────────────────────────────────────────────
    //
    // ★ 예전엔 OnTriggerStay에서 힘을 줬다. 그런데 거의 안 불렸다
    //   초콜릿 트리거는 <b>두께 0.11짜리 얇은 판</b>이다. 빠지는 물체는 한두 프레임 만에
    //   판을 통과해 아래로 빠져나가고, 그 뒤로는 Stay가 오지 않는다.
    //   진입 순간 중력이 꺼져 있으니 감쇠로 속도만 죽고 <b>그 자리에 멈춰 선다.</b>
    //   "빠지긴 하는데 둥둥 떠다니지 않는다"의 정체였다.
    //
    //   그래서 '지금 트리거 안에 있는가'가 아니라 '초콜릿에 들어온 적이 있는가'로
    //   기준을 바꿨다. 목록(processedBodies)에 든 동안은 계속 힘을 준다.
    //   부력은 수면 아래일 때만 걸리므로, 통과해 내려간 물체도 수면까지 밀려 올라와
    //   거기서 출렁인다.
    //순회 중 목록이 바뀌어도(파괴·탈출) 터지지 않게 스냅샷을 재사용한다
    private readonly List<Rigidbody> floatingSnapshot = new List<Rigidbody>();

    private void FixedUpdate()
    {
        if (processedBodies.Count == 0)
            return;

        //떠 있을 때 원점이 머무는 높이. 여기를 기준으로 위아래 양쪽에서 되돌린다
        float restY = SurfaceY - restDepth;
        float depthScale = Mathf.Max(0.01f, fullSubmergeDepth);
        Vector3 flow = FlowDirection();
        float t = SyncedTime;

        floatingSnapshot.Clear();
        floatingSnapshot.AddRange(processedBodies);

        for (int i = 0; i < floatingSnapshot.Count; i++)
        {
            Rigidbody rb = floatingSnapshot[i];

            if (rb == null)
                continue;

            if (!floatData.TryGetValue(rb, out FloatData fd))
            {
                fd = CreateFloatData(rb);
                floatData[rb] = fd;
            }

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
        //   (GameTags.IsCharacterProxy 주석에 이 이중 호출이 만든 사고들을 적어뒀다)
        if (other.attachedRigidbody != null && IsCharacter(other.attachedRigidbody)
            && !GameTags.IsCharacterProxy(other))
            return;

        // ═════════════════════════════════════════════
        //  사람 플레이어 탈락
        // ═════════════════════════════════════════════
        //
        // ★ 이 갈래가 없으면 사람이 아래 Rigidbody 분기로 흘러
        //   '떠다니는 물체' 취급만 받고 죽지 않는다.
        LanPlayerState lanPlayer = other.GetComponentInParent<LanPlayerState>();

        if (lanPlayer != null)
        {
            // 신고는 본인만. 남의 캐릭터가 내 화면에서 스쳤다고 죽이면 안 된다.
            if (lanPlayer.IsMine && !lanPlayer.IsOutOfPlay && LanGameFlow.Instance != null)
            {
                LanGameFlow.Instance.ReportSelfEliminated(
                    lanPlayer.EntityId, "초콜릿에 빠졌습니다!");
            }

            // ★ 예전엔 여기서 return 했다
            //   신고만 하고 물리 설정을 건너뛰어서, 사람은 부력·점성을 하나도 못 받고
            //   <b>중력만으로 끝없이 떨어졌다.</b> 같은 초콜릿에서 봇은 둥둥 떠 있는데도.
            //
            // ★ 탈락 확정을 기다리면 늦는다
            //   초콜릿 트리거는 두께 0.11짜리 얇은 판이다. 호스트 왕복을 기다리는 동안
            //   CharacterController가 캐릭터를 그대로 몰아 <b>판을 통과해 버린다.</b>
            //   빠져나가면 트리거 밖이라 아무 힘도 못 받고 허공에 멈춰 선다.
            //   봇은 진입 순간 물리로 바뀌어 그 자리에서 제동이 걸린다 — 사람도 같게 한다.
            //   (소유자가 아니면 NetTransform이 위치를 몰고 있으므로 물리를 켜지 않는다)
            if (lanPlayer.IsMine)
            {
                lanPlayer.BeginPhysicsFallNow();
                ApplyFloatPhysics(other.attachedRigidbody);
            }

            return;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        AIPlayerMovement aiPlayer = rb.GetComponent<AIPlayerMovement>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();
        NavMeshAgent navMeshAgent = rb.GetComponent<NavMeshAgent>();

        bool isEdible = other.CompareTag(GameTags.Edible) || rb.gameObject.CompareTag(GameTags.Edible);
        bool isCandy = other.CompareTag(GameTags.Sphere) || rb.gameObject.CompareTag(GameTags.Sphere);

        int bgLayer = GameLayers.BackGroundObject;
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);

        //바로 위에서 이미 찾아둔 것을 쓴다. 예전엔 같은 GetComponent를 두 번 더 돌렸다.
        //여기서 묻는 건 신원이 아니라 '스스로 움직이는 두뇌가 있나'라서 INetEntity가 아니다 —
        //배회 젤리(WanderingAI)도 포함해야 하고, 아래에서 그 두뇌를 실제로 끄기 때문이다.
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
            ApplyFloatPhysics(rb);

            if (!isAI && floatingLifetime > 0f)
                StartCoroutine(DeactivateAfterDelay(rb.gameObject, floatingLifetime));
        }

        if (wanderingAI != null)
            wanderingAI.enabled = false;
        if (navMeshAgent != null)
            navMeshAgent.enabled = false;

        if (aiPlayer != null)
        {
            //탈락 판정 권한은 OnEliminated 안에서 IsDriver로 이미 본다.
            //여기서 또 보면 규칙이 두 곳에 생기고, botId가 없을 때의 폴백까지 달라진다
            aiPlayer.ReportEliminated();
        }
    }

    /// <summary>초콜릿에 빠진 물체를 '뜨는 상태'로 바꾼다. 사람·봇·소품이 같은 설정을 쓴다.</summary>
    private void ApplyFloatPhysics(Rigidbody rb)
    {
        if (rb == null)
            return;

        rb.isKinematic = false;
        rb.useGravity = false;               //부력이 대신한다. 중력을 켜두면 계속 가라앉는다
        rb.linearDamping = chocolateViscosity;
        rb.angularDamping = chocolateViscosity;

        //물에 부딪히면 속도를 크게 잃는다. 안 죽이면 23m/s로 들어와 4m나 잠수한 뒤
        //0.58m/s로 6초에 걸쳐 올라온다 — 그동안은 '가라앉아 안 보이는' 상태다
        rb.linearVelocity *= entrySpeedKeep;

        processedBodies.Add(rb);
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

    private static FloatData CreateFloatData(Rigidbody rb)
    {
        int id = rb.GetInstanceID();
        return new FloatData
        {
            phase = (id * 2.3f) % (Mathf.PI * 2f),
            speedMul = 0.7f + ((id * 7.9f) % 1000f) / 1000f * 0.6f,
            forceMul = 1.2f + ((id * 3.1f) % 1000f) / 1000f * 0.8f,
            flowOffset = new Vector2(
                ((id * 5.7f) % 1000f) / 1000f * 0.6f - 0.3f,
                ((id * 11.3f) % 1000f) / 1000f * 0.6f - 0.3f)
        };
    }

    private IEnumerator DeactivateAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null)
        {
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                processedBodies.Remove(rb);
                floatData.Remove(rb);
            }
            obj.SetActive(false);
        }
    }

    private void PurgeDestroyedEntries()
    {
        processedBodies.RemoveWhere(rb => rb == null);

        List<Rigidbody> staleKeys = null;
        foreach (var kvp in floatData)
        {
            if (kvp.Key == null)
            {
                staleKeys ??= new List<Rigidbody>();
                staleKeys.Add(kvp.Key);
            }
        }
        if (staleKeys != null)
            foreach (var key in staleKeys)
                floatData.Remove(key);
    }

    private void OnDisable()
    {
        processedBodies.Clear();
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

        processedBodies.Remove(rb);
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

        NavMeshAgent navMeshAgent = rb.GetComponent<NavMeshAgent>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();

        // ═════════════════════════════════════════════
        //  [LAN 이식] NavMeshAgent를 되살릴 자격
        // ═════════════════════════════════════════════
        //
        // ★ 문제 두 가지가 여기서 나왔다
        //   ① 원격에서도 agent를 켜버렸다 → 호스트가 보내주는 위치와 agent의
        //      자체 이동이 서로를 밀어 젤리가 떨린다. 원격은 agent가 꺼져 있어야 한다.
        //   ② SamplePosition이 실패했는데도 켰다 → "Failed to create agent because
        //      it is not close enough to the NavMesh"가 프레임마다 쏟아진다.
        //      NavMesh 위로 못 옮겼으면 켜지 않는 게 맞다(다음 Exit 때 다시 시도).
        bool drives = NavDriverOf(rb);

        if (navMeshAgent != null && drives)
        {
            NavMeshHit hit;
            //agent 자신의 타입 + 걸어다닐 수 있는 영역만. int 마스크 오버로드는
            //타입 0(PlayerJelly) 기준이라 젤리를 그 자리에 놓으면 다시 NavMesh 밖이 된다
            bool onMesh = NavMesh.SamplePosition(
                rb.transform.position, out hit, 10f, NavMeshUtil.WalkableFilter(navMeshAgent));

            if (onMesh)
            {
                rb.transform.position = hit.position;
                rb.useGravity = false;
                navMeshAgent.enabled = true;
            }
        }

        // AI 스크립트는 원격에서도 켠다 — 이동은 안 하고 걷는 애니메이션만 맞춘다.
        if (wanderingAI != null)
            wanderingAI.enabled = true;
    }

    /// <summary>이 기계가 그 오브젝트의 NavMeshAgent를 굴리는가(= 호스트이거나 오프라인).</summary>
    private static bool NavDriverOf(Rigidbody rb)
    {
        NetIdentity id = rb.GetComponentInParent<NetIdentity>();
        if (id != null)
            return id.IsSimulatedHere;

        var net = NetManager.Instance;
        return NetManager.Offline || net.IsHost;
    }
}