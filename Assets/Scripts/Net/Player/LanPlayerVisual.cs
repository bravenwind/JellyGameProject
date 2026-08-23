using UnityEngine;
using System.Collections;

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

        private NetIdentity id;
        private float animTimer;
        private readonly NetWriter w = new NetWriter();

        //배트 스윙은 휘두른 본인만 batPivot을 돌린다. NetTransform은 루트만 동기화하므로
        //원격 화면에서는 캐릭터만 움직이고 배트는 가만히 있었다. 여기서 같은 연출을 재생한다
        private Transform batPivot;
        private bool hideBatWhenIdle;
        private Coroutine batSwing;

        private void Awake()
        {
            id = GetComponent<NetIdentity>();

            if (scaleController == null)
                scaleController = GetComponentInChildren<PlayerScaleController>(true);

            if (colorVisual == null)
                colorVisual = GetComponentInChildren<PlayerColorVisual>(true);

            ResolveBat();

            if (animator == null)
            {
                PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
                if (pm != null)
                    animator = pm.animator;

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

        /// <summary>내 캐릭터의 애니메이션 트리거를 다른 화면에도 재생시킨다.</summary>
        //예전에는 static ReportTrigger(Component from, ...)이라 호출될 때마다
        //GetComponentInParent로 계층을 거슬러 올라갔다. 공격·대시·점프·피격은 자주
        //터지는데다 Component를 받으니 아무거나 넘겨도 컴파일이 통과했다.
        //이제 호출자가 Awake에서 한 번만 찾아두고 그 참조로 부른다
        public void SendTrigger(byte animCode)
        {
            if (id == null || !id.IsMine)
                return;

            Send(animCode, 0);
        }

        private void Send(byte kind, byte value)
        {
            NetManager net = NetManager.Instance;
            if (NetManager.Offline)
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

            if (kind == ANIM_ATTACK)
                PlayBatSwing();
        }

        private void ResolveBat()
        {
            PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
            if (pm != null)
            {
                batPivot = pm.batPivot;
                hideBatWhenIdle = pm.hideBatWhenIdle;
                return;
            }

            AIPlayerMovement bot = GetComponentInChildren<AIPlayerMovement>(true);
            if (bot != null)
            {
                batPivot = bot.batPivot;
                hideBatWhenIdle = bot.hideBatWhenIdle;
            }
        }

        //휘두른 쪽의 AttackSwingRoutine과 같은 궤적. 판정은 없고 보이는 것만 한다
        // ═══════════════════════════════════════════════════════
        //  배트 스윙 회전 — 사람·봇·원격 공용
        // ═══════════════════════════════════════════════════════
        //
        // ★ 예전엔 같은 회전이 세 곳에 있었다
        //     PlayerAttackState.Update      로컬 사람
        //     AIPlayerMovement.AttackSwing  호스트의 봇
        //     여기 BatSwingRoutine          원격 화면
        //   셋 다 batArcAngle의 절반씩 좌우로 Slerp하는 같은 코드였고,
        //   ResolveBat이 PlayerMovement든 AIPlayerMovement든 batPivot을 찾아주므로
        //   애초에 하나면 됐다. 연출을 바꾸려면 세 곳을 같이 고쳐야 했다.
        public void PlayBatSwing()
        {
            if (batPivot == null || DataManager.Instance == null)
                return;

            if (batSwing != null)
                StopCoroutine(batSwing);

            batSwing = StartCoroutine(BatSwingRoutine());
        }

        private IEnumerator BatSwingRoutine()
        {
            DataManager dm = DataManager.Instance;

            float halfArc = dm.batArcAngle * 0.5f;
            Quaternion from = Quaternion.Euler(0f, -halfArc, 0f);
            Quaternion to = Quaternion.Euler(0f, halfArc, 0f);

            batPivot.gameObject.SetActive(true);
            batPivot.localRotation = from;

            float elapsed = 0f;

            while (elapsed < dm.batSwingDuration)
            {
                elapsed += Time.deltaTime;
                batPivot.localRotation = Quaternion.Slerp(from, to, elapsed / dm.batSwingDuration);
                yield return null;
            }

            batPivot.localRotation = Quaternion.identity;

            if (hideBatWhenIdle)
                batPivot.gameObject.SetActive(false);

            batSwing = null;
        }

        public void ApplyGrow(GrowKind kind, float amount)
        {
            if (scaleController == null)
                return;

            LanBotState bot = GetComponent<LanBotState>();
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

        // ═══════════════════════════════════════════════════════
        //  흡수당하는 연출 — 사람·봇 공용
        // ═══════════════════════════════════════════════════════
        //
        // ★ 예전엔 두 벌이었다
        //   사람은 LanPlayerVisual.AbsorbedRoutine, 봇은 AIPlayerMovement.LanAbsorbedSequence.
        //   가운데 20줄(0.8초 동안 흡수자에게 끌려가며 0.05배로 줄기)이 상수까지 똑같은데
        //   파일 두 곳에 복사돼 있었다. 연출을 손보려면 두 곳을 같이 고쳐야 했고,
        //   한쪽만 고치면 사람과 봇이 다르게 빨려 들어간다.
        //
        //   다른 건 앞뒤뿐이다 — 무엇을 멈추고, 끝나고 무엇을 하느냐.
        //   LanPlayerVisual은 두 프리팹에 다 붙어 있으므로 여기가 합칠 자리다.
        private const float ABSORBED_DURATION = 0.8f;
        private const float ABSORBED_PULL_SPEED = 12f;
        private const float ABSORBED_SNAP_DIST = 0.4f;
        private const float ABSORBED_END_SCALE = 0.05f;

        public void PlayAbsorbed(Transform absorber)
        {
            if (absorbedPlaying)
                return;
            absorbedPlaying = true;

            //봇의 '판 밖' 판정은 AIPlayerMovement가 들고 있다. 연출을 시작하는 순간
            //표시해줘야 리더보드·AI 표적 선정에서 즉시 빠진다
            if (id != null && id.Bot != null)
                id.Bot.IsBeingAbsorbed = true;

            StartCoroutine(AbsorbedRoutine(absorber));
        }

        private IEnumerator AbsorbedRoutine(Transform absorber)
        {
            StopDriving();

            Vector3 startScale = transform.localScale;
            float elapsed = 0f;

            while (elapsed < ABSORBED_DURATION)
            {
                if (absorber != null)
                {
                    if (Vector3.Distance(transform.position, absorber.position) <= ABSORBED_SNAP_DIST)
                        break;
                    transform.position = Vector3.MoveTowards(
                        transform.position, absorber.position, ABSORBED_PULL_SPEED * Time.deltaTime);
                }

                transform.localScale = Vector3.Lerp(
                    startScale, Vector3.one * ABSORBED_END_SCALE, elapsed / ABSORBED_DURATION);
                elapsed += Time.deltaTime;
                yield return null;
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                r.enabled = false;

            Dispose();
        }

        /// <summary>이 개체를 움직이던 것을 전부 멈춘다. 사람과 봇은 운전자가 다르다.</summary>
        private void StopDriving()
        {
            AIPlayerMovement bot = id != null ? id.Bot : null;

            if (bot != null)
            {
                bot.StopForAbsorb();
                return;
            }

            PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
            if (pm != null)
                pm.enabled = false;

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            //연출로 옮긴 좌표가 네트워크로 나가면 다른 화면에서도 끌려간다 — 그건 호스트가 정할 일
            NetTransform nt = GetComponent<NetTransform>();
            if (nt != null)
                nt.enabled = false;
        }

        /// <summary>연출이 끝난 뒤 개체를 치운다. 봇은 호스트가 디스폰까지 해야 한다.</summary>
        private void Dispose()
        {
            LanBotState botState = id != null ? id.BotState : null;

            if (botState != null)
            {
                //호스트가 아니면 아무 일도 안 한다. 클라는 DespawnEntity를 받아 치운다
                botState.HostDespawnAfterAbsorbed();
                return;
            }

            gameObject.SetActive(false);
        }
    }
}
