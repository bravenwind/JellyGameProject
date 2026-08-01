using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 크기·색·애니메이션을 기존 게임 시스템에 연결한다.
    ///
    /// ★ 설계 원칙 — 절대값을 맞추지 않고 '사건'을 전달한다
    ///   크기는 PlayerScaleController가, 색은 PlayerColorVisual이 이미 관리하고 있다.
    ///   여기에 네트워크가 절대값을 억지로 밀어넣으면 두 시스템이 싸운다(연출도 깨진다).
    ///
    ///   그래서 호스트는 "젤리를 먹었다 / 흡수했다 / 맞았다"만 알리고,
    ///   각 클라가 <b>기존 함수를 그대로 호출</b>한다. 같은 입력 → 같은 결과.
    ///
    ///     GrowByJelly()  ·  GrowByAbsorbing(v)  ·  GrowByBatHit(g)
    ///     HandleJellyAbsorbed(JellyColorType)
    ///
    /// ★ 애니메이션은 상태 해시를 그대로 보낸다
    ///   SetTrigger는 값이 남지 않아 폴링할 수 없다. 대신 Animator의 '현재 상태'를
    ///   20Hz로 보내고 원격에서 CrossFade하면 점프·대쉬·공격·이동이 한 번에 커버된다.
    /// </summary>
    public class LanPlayerVisual : MonoBehaviour
    {
        [Header("연결 (비우면 자동 탐색)")]
        public PlayerScaleController scaleController;
        public PlayerColorVisual colorVisual;
        public Animator animator;

        [Header("애니메이션 동기화")]
        public float animSendRate = 10f;
        public float crossFade = 0.1f;

        NetIdentity _id;
        float _animTimer;
        int _lastSentHash;
        readonly NetWriter _w = new NetWriter();

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            if (scaleController == null) scaleController = GetComponentInChildren<PlayerScaleController>(true);
            if (colorVisual == null) colorVisual = GetComponentInChildren<PlayerColorVisual>(true);

            if (animator == null)
            {
                PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
                if (pm != null) animator = pm.jellyAnimator;
                if (animator == null) animator = GetComponentInChildren<Animator>(true);
            }
        }

        // ═════════════════════════════════════════════
        //  애니메이션 — 파라미터를 직접 동기화한다
        // ═════════════════════════════════════════════
        //
        // ★ 왜 '상태 해시'가 아니라 '파라미터'인가
        //   처음엔 Animator의 현재 상태 해시를 보내려 했지만, Idle↔Move를 Bool 하나로
        //   블렌딩하는 컨트롤러에서는 상태가 바뀌지 않아 아무것도 전송되지 않는다.
        //   기존 FSM이 쓰는 파라미터를 그대로 보내는 편이 확실하다.
        //
        //     IsMoving (Bool)  — 매 프레임 폴링해서 바뀔 때만 전송
        //     Jump/Dash/Attack/Hit (Trigger) — 값이 남지 않으므로 FSM이 직접 알린다

        public const byte AnimIsMoving = 0;
        public const byte AnimJump = 1;
        public const byte AnimDash = 2;
        public const byte AnimAttack = 3;
        public const byte AnimHit = 4;

        static readonly string[] TriggerNames = { "", "Jump", "Dash", "Attack", "Hit" };

        bool _lastMoving;

        void Update()
        {
            if (_id == null || animator == null || !_id.IsMine) return;

            _animTimer += Time.deltaTime;
            if (_animTimer < 1f / animSendRate) return;
            _animTimer = 0f;

            bool moving = animator.GetBool("IsMoving");
            if (moving == _lastMoving) return;      // 바뀔 때만 보낸다
            _lastMoving = moving;

            Send(AnimIsMoving, moving ? (byte)1 : (byte)0);
        }

        /// <summary>
        /// FSM이 트리거를 쏠 때 같이 불러준다(PlayerJumpState 등).
        /// 소유자가 아니면 아무 일도 하지 않으므로 어디서 불러도 안전하다.
        /// </summary>
        public static void ReportTrigger(Component from, byte animCode)
        {
            if (from == null) return;

            LanPlayerVisual v = from.GetComponentInParent<LanPlayerVisual>();
            if (v == null || v._id == null || !v._id.IsMine) return;

            v.Send(animCode, 0);
        }

        void Send(byte kind, byte value)
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            _w.Begin(MsgType.AnimState);
            _w.WriteInt(_id.NetId);
            _w.WriteByte(kind);
            _w.WriteByte(value);
            _w.End();

            if (net.IsHost) net.Host.Broadcast(_w);
            else net.Client.Send(_w);
        }

        /// <summary>원격에서 애니메이션 정보를 받았을 때.</summary>
        public void ApplyAnim(byte kind, byte value)
        {
            if (animator == null) return;

            if (kind == AnimIsMoving)
            {
                animator.SetBool("IsMoving", value != 0);
                return;
            }

            if (kind < TriggerNames.Length)
                animator.SetTrigger(TriggerNames[kind]);
        }

        // ═════════════════════════════════════════════
        //  성장 — 기존 함수를 그대로 부른다
        // ═════════════════════════════════════════════
        public void ApplyGrow(GrowKind kind, float amount)
        {
            if (scaleController == null) return;

            // ★ AI 봇은 예외 — 크기를 '사건'이 아니라 '절대값'으로 받는다.
            //
            //   봇은 호스트에서만 젤리를 먹는다(원격에서는 PlayerAbsorber가 꺼져 있다).
            //   그래서 클라는 봇이 왜 커졌는지 알 방법이 없고, LanBotSync가 보내주는
            //   실제 크기를 따라가는 수밖에 없다.
            //   그런데 여기서 GrowByAbsorbing까지 부르면 PlayerScaleController와
            //   LanBotSync가 같은 transform.localScale을 서로 다른 목표로 당겨
            //   봇이 부풀었다 쪼그라들었다 떨린다. 구동자가 아니면 손대지 않는다.
            LanBotSync bot = GetComponent<LanBotSync>();
            if (bot != null && !bot.IsDriver) return;

            switch (kind)
            {
                case GrowKind.Jelly: scaleController.GrowByJelly(); break;
                case GrowKind.Absorbing: scaleController.GrowByAbsorbing(amount); break;
                case GrowKind.BatHit: scaleController.GrowByBatHit(amount); break;
            }
        }

        // ═════════════════════════════════════════════
        //  색 — 기존 함수를 그대로 부른다
        // ═════════════════════════════════════════════
        public void ApplyJellyColor(JellyColorType type)
        {
            if (colorVisual == null) return;
            colorVisual.HandleJellyAbsorbed(type);
        }

        /// <summary>현재 크기값(권위 판정에 쓰인다). 없으면 1.</summary>
        public float ScaleValue
        {
            get { return scaleController != null ? scaleController.currentScaleValue : 1f; }
        }

        /// <summary>실제 게임의 크기 관리자가 붙어 있는가(테스트용 캡슐에는 없다).</summary>
        public bool HasScaleController { get { return scaleController != null; } }

        // ═════════════════════════════════════════════
        //  흡수당했을 때 — 원본 AbsorbedSequence를 옮긴 것
        // ═════════════════════════════════════════════
        bool _absorbedPlaying;

        /// <summary>
        /// 흡수당한 연출: 흡수자에게 빨려 들어가며 작아지다가 사라진다.
        /// 전원이 각자 재생하므로 모든 화면에서 같이 없어진다.
        /// </summary>
        public void PlayAbsorbed(Transform absorber)
        {
            if (_absorbedPlaying) return;
            _absorbedPlaying = true;
            StartCoroutine(AbsorbedRoutine(absorber));
        }

        System.Collections.IEnumerator AbsorbedRoutine(Transform absorber)
        {
            // 조작·물리 차단
            PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
            if (pm != null) pm.enabled = false;

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            NetTransform nt = GetComponent<NetTransform>();
            if (nt != null) nt.enabled = false;      // 연출 중엔 위치 동기화를 멈춘다

            Vector3 startScale = transform.localScale;
            float elapsed = 0f;
            const float duration = 0.8f;
            const float moveSpeed = 12f;
            const float snapDist = 0.4f;

            while (elapsed < duration)
            {
                if (absorber != null)
                {
                    if (Vector3.Distance(transform.position, absorber.position) <= snapDist) break;
                    transform.position = Vector3.MoveTowards(
                        transform.position, absorber.position, moveSpeed * Time.deltaTime);
                }

                transform.localScale = Vector3.Lerp(startScale, Vector3.one * 0.05f, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
