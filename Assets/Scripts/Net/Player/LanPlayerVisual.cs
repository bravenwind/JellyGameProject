using UnityEngine;

namespace JellyNet
{
    public class LanPlayerVisual : MonoBehaviour
    {
        [Header("연결 (비우면 자동 탐색)")]
        public PlayerScaleController scaleController;
        public PlayerColorVisual colorVisual;
        public Animator animator;

        [Header("애니메이션 동기화")]
        public float animSendRate = 10f;
        public float crossFade = 0.1f;

        private NetIdentity id;
        private float animTimer;
        private int lastSentHash;
        private readonly NetWriter w = new NetWriter();

        private void Awake()
        {
            id = GetComponent<NetIdentity>();

            if (scaleController == null)
                scaleController = GetComponentInChildren<PlayerScaleController>(true);
            
            if (colorVisual == null)
                colorVisual = GetComponentInChildren<PlayerColorVisual>(true);

            if (animator == null)
            {
                PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
                if (pm != null)
                    animator = pm.jellyAnimator;

                if (animator == null)
                    animator = GetComponentInChildren<Animator>(true);
            }
        }

        public const byte ANIM_IS_MOVING = 0;
        public const byte ANIM_JUMP = 1;
        public const byte ANIM_DASH = 2;
        public const byte ANIM_ATTACK = 3;
        public const byte ANIM_HIT = 4;

        private static readonly string[] TriggerNames = { "", "Jump", "Dash", "Attack", "Hit" };

        private bool lastMoving;

        private void Update()
        {
            if (id == null || animator == null || !id.IsMine)
                return;

            animTimer += Time.deltaTime;
            if (animTimer < 1f / animSendRate)
                return;
            animTimer = 0f;

            bool moving = animator.GetBool("IsMoving");
            if (moving == lastMoving)
                return;
            lastMoving = moving;

            Send(ANIM_IS_MOVING, moving ? (byte)1 : (byte)0);
        }

        public static void ReportTrigger(Component from, byte animCode)
        {
            if (from == null)
                return;

            LanPlayerVisual v = from.GetComponentInParent<LanPlayerVisual>();
            if (v == null || v.id == null || !v.id.IsMine)
                return;

            v.Send(animCode, 0);
        }

        private void Send(byte kind, byte value)
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;

            w.Begin(MsgType.AnimState);
            w.WriteInt(id.NetId);
            w.WriteByte(kind);
            w.WriteByte(value);
            w.End();

            if (net.IsHost)
                net.Host.Broadcast(w);
            else
                net.Client.Send(w);
        }

        public void ApplyAnim(byte kind, byte value)
        {
            if (animator == null)
                return;

            if (kind == ANIM_IS_MOVING)
            {
                animator.SetBool("IsMoving", value != 0);
                return;
            }

            if (kind < TriggerNames.Length)
                animator.SetTrigger(TriggerNames[kind]);
        }

        public void ApplyGrow(GrowKind kind, float amount)
        {
            if (scaleController == null)
                return;

            LanBotSync bot = GetComponent<LanBotSync>();
            if (bot != null && !bot.IsDriver)
                return;

            switch (kind)
            {
                case GrowKind.Jelly: scaleController.GrowByJelly(); break;
                case GrowKind.Absorbing: scaleController.GrowByAbsorbing(amount); break;
                case GrowKind.BatHit: scaleController.GrowByBatHit(amount); break;
            }
        }

        public void ApplyJellyColor(JellyColorType type)
        {
            if (colorVisual == null)
                return;
            colorVisual.HandleJellyAbsorbed(type);
        }

        public float ScaleValue
        {
            get { return scaleController != null ? scaleController.currentScaleValue : 1f; }
        }

        public bool HasScaleController { get { return scaleController != null; } }

        private bool absorbedPlaying;

        public void PlayAbsorbed(Transform absorber)
        {
            if (absorbedPlaying)
                return;
            absorbedPlaying = true;
            StartCoroutine(AbsorbedRoutine(absorber));
        }

        private System.Collections.IEnumerator AbsorbedRoutine(Transform absorber)
        {
            PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
            if (pm != null)
                pm.enabled = false;

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            NetTransform nt = GetComponent<NetTransform>();
            if (nt != null)
                nt.enabled = false;

            Vector3 startScale = transform.localScale;
            float elapsed = 0f;
            const float DURATION = 0.8f;
            const float MOVE_SPEED = 12f;
            const float SNAP_DIST = 0.4f;

            while (elapsed < DURATION)
            {
                if (absorber != null)
                {
                    if (Vector3.Distance(transform.position, absorber.position) <= SNAP_DIST)
                        break;
                    transform.position = Vector3.MoveTowards(
                        transform.position, absorber.position, MOVE_SPEED * Time.deltaTime);
                }

                transform.localScale = Vector3.Lerp(startScale, Vector3.one * 0.05f, elapsed / DURATION);
                elapsed += Time.deltaTime;
                yield return null;
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
