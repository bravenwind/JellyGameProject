using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    //네트워크 개체를 사람·봇 구분 없이 다루기 위한 질의 모음
    //같은 판단이 AbsorbMode와 PushMode에 따로 있다가 한쪽만 고쳐지는 일이 반복됐다
    public static class NetEntity
    {
        //판정을 내려도 되는 쪽인가. 오프라인은 혼자 다 굴린다
        private static bool IsHostNow
        {
            get { return NetManager.Instance != null && NetManager.Instance.IsHost; }
        }

        /// <summary>
        /// 이 몸의 움직임을 <b>다른 기계가</b> 책임지고 있는가. true면 여기서 물리를 켜면 안 된다.
        ///
        /// ★ 켜면 무슨 일이 나나
        ///   원격 사본은 NetTransform이 받은 좌표로 위치를 몰고 있다. 거기에 로컬 물리까지
        ///   붙이면 둘이 서로를 밀어 캐릭터가 화면마다 다르게 떨린다.
        ///
        /// ★ 씬에 손으로 놓은 것은 예외다
        ///   씬 젤리·소품은 OwnerId가 0이라 클라에서 IsSimulatedHere가 false인데,
        ///   <b>위치를 주고받지도 않는다.</b> 그대로 걸러내면 클라 화면에서만 공중에 굳는다.
        ///   (실제로 클라에서 젤리가 무너진 발판 위에 떠 있던 버그가 이거였다)
        ///   주고받는 게 없으니 소유권을 물을 이유도 없다 — 각자 자기 화면에서 떨어뜨린다.
        ///
        /// 같은 판단이 FallingTile과 ChocolateFluid에 따로 있었다. 한쪽만 고쳐지는 걸 막으려고 모았다.
        /// </summary>
        public static bool IsDrivenElsewhere(Component c)
        {
            if (c == null)
                return false;

            NetIdentity id = c.GetComponentInParent<NetIdentity>();

            if (id == null)
                return false;

            if (id.NetId >= NetConfig.SCENE_ID_BASE)
                return false;

            return !id.IsSimulatedHere;
        }

        // ═══════════════════════════════════════════════════════
        //  기준 크기 — 플레이어 프리팹이 유일한 출처
        // ═══════════════════════════════════════════════════════
        //
        // ★ 예전엔 DataManager.startingScale이라는 사본이 따로 있었다
        //   '2'라는 값이 프리팹 localScale과 인스펙터 양쪽에 적혀 있었고,
        //   같은 값이라는 보장이 어디에도 없었다. 프리팹만 키우면 스폰하자마자
        //   점수가 붙는데 에러가 안 나서 찾기도 어렵다.
        //
        //   기준 크기의 뜻은 두 곳에서 같다 — '캐릭터가 태어나는 크기'다.
        //     · 점수: 이 크기일 때 0점  (아래 ScoreFromScale)
        //     · 밀치기: 이 크기일 때 힘 1배 (PushMode)
        //   그러니 프리팹에서 한 번 읽어 쓰는 게 맞다.
        //
        //   prefabs[0]이 플레이어다(그 뒤가 봇, JELLY_PREFAB_START부터 젤리).
        private static float baselineScale = -1f;

        /// <summary>캐릭터가 태어나는 크기. 점수 0의 기준이자 밀치기 힘 1배의 기준.</summary>
        public static float BaselineScale
        {
            get
            {
                if (baselineScale > 0f)
                    return baselineScale;

                NetWorld world = NetWorld.Instance;

                if (world == null || world.prefabs == null || world.prefabs.Length == 0 || world.prefabs[0] == null)
                    return 1f;   //캐시하지 않는다 — 다음에 제대로 읽을 기회를 남긴다

                baselineScale = world.prefabs[0].transform.localScale.x;
                return baselineScale;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBaseline() => baselineScale = -1f;

        /// <summary>
        /// 크기를 점수로 바꾼다. 기준 크기(=태어나는 크기)일 때 0점이다.
        ///
        /// ★ 예전엔 DataManager에 있었다
        ///   그런데 이 계산은 기준 크기를 알아야 하고, 기준 크기의 출처는 플레이어
        ///   프리팹이다. 그래서 <b>설정 통이 네트워크를 참조하는</b> 거꾸로 된 의존이 생겼다.
        ///   DataManager는 숫자만 내주고, 규칙은 판정 창구인 여기가 갖는 게 맞다.
        /// </summary>
        public static int ScoreFromScale(float scale)
        {
            DataManager rules = DataManager.Instance;

            if (rules == null || rules.JellyScaleIncrease <= 0f)
                return 0;

            //기준 크기보다 얼마나 컸는지를 '젤리 몇 개분'으로 환산해 점수를 매긴다
            float grown = scale - BaselineScale;
            return Mathf.Max(0, Mathf.RoundToInt(grown * rules.ScorePerJelly / rules.JellyScaleIncrease));
        }

        public static bool IsJelly(NetIdentity id)
        {
            if (id == null)
                return false;

            if (id.IsBot)
                return false;

            return id.PrefabId >= NetConfig.JELLY_PREFAB_START;
        }

        /// <summary>
        /// 이 개체가 판에서 빠졌나. 사람·봇을 가리지 않는다.
        ///
        /// ★ 예전엔 여기 if (id.IsBot) 분기가 있었다
        ///   사람은 LanPlayerState, 봇은 AIPlayerMovement가 판 밖 여부를 들고 있어서다.
        ///   이제 둘 다 INetEntity를 구현하므로 물어보는 쪽은 그 차이를 몰라도 된다.
        ///   봇의 비대칭(두뇌가 들고 있는 것)은 LanBotState.IsOutOfPlay 한 줄에 갇혀 있다.
        /// </summary>
        public static bool IsOutOfPlay(NetIdentity id)
        {
            if (id == null)
                return true;

            INetEntity e = EntityOf(id);
            return e != null && e.IsOutOfPlay;
        }

        /// <summary>netId가 가리키는 참가자. 젤리·소품이면 null.</summary>
        public static INetEntity EntityOf(NetIdentity id)
        {
            if (id == null)
                return null;

            if (id.PlayerState != null)
                return id.PlayerState;

            return id.BotState;
        }

        // ★ 크기를 묻는 유일한 창구
        //   예전엔 네 곳이 각자 답을 만들었다.
        //     NetEntity.ScaleOf            → LanPlayerVisual.ScaleValue
        //     LanPlayerState.ScaleValue    → PlayerScaleController.CurrentScaleValue
        //     LanPlayerVisual.ScaleValue   → PlayerScaleController.CurrentScaleValue
        //     AIPlayerMovement.GetMyAuthorityScale → transform.localScale.x
        //   앞의 셋은 '논리적 크기'(연출이 끝난 목표값)이고 마지막 하나만
        //   '지금 화면에 보이는 크기'였다. 커지는 연출이 도는 0.3초 동안 봇만
        //   다른 답을 내놓아서, 흡수 가능 여부가 보는 쪽마다 갈렸다.
        //   이제 전부 PlayerScaleController를 거친다 — 없을 때만 transform으로 떨어진다.
        public static float ScaleOf(NetIdentity id)
        {
            //참가자면 그 개체가 자기 사정을 안다(봇은 호스트/클라에서 출처가 다르다).
            //젤리·소품은 INetEntity가 아니므로 transform으로 떨어진다
            INetEntity e = EntityOf(id);

            if (e != null)
                return e.ScaleValue;

            return id != null ? id.transform.localScale.x : 1f;
        }

        // ═══════════════════════════════════════════════════════
        //  점수 — 사람·봇, 두 모드가 모두 지나가는 유일한 관문
        // ═══════════════════════════════════════════════════════
        //
        // ★ 예전엔 모드마다 다른 길로 갔다
        //     밀치기 : PushMode → NetEntity.AddScore → 각 상태 컴포넌트 (방송 O)
        //     흡수   : LanPlayerState.HostRecomputeScore  (방송 O)
        //              LanBotState.HostSendScale 안의 한 줄  (방송 X)
        //   같은 '점수'인데 규칙이 세 곳에 흩어져 있었고, 그중 봇의 흡수 점수만
        //   방송을 안 해서 클라 순위표의 봇 점수가 영영 0이었다.
        //   이제 두 모드 다 여기를 지난다. 모드별 규칙(더할 것이냐 크기에서 뽑을 것이냐)은
        //   PushMode/AbsorbMode가 알고, '누구에게 어떻게 적는지'는 여기만 안다.
        public static void AddScore(NetIdentity id, int delta)
        {
            if (delta == 0)
                return;

            INetEntity e = EntityOf(id);

            if (e != null)
                e.HostAddScore(delta);
        }

        /// <summary>점수를 특정 값으로 맞춘다. 값이 바뀔 때만 방송이 나간다.</summary>
        public static void SetScore(NetIdentity id, int score)
        {
            if (id == null)
                return;

            LanPlayerState state = id.PlayerState;

            if (state != null)
            {
                state.HostSetScore(score);
                return;
            }

            LanBotState bot = id.BotState;

            if (bot != null)
                bot.HostSetScore(score);
        }

        /// <summary>
        /// 흡수 모드의 점수 규칙 — 점수는 '지금 크기'에서 나온다.
        /// 사람이든 봇이든 같은 공식을 쓰도록 여기 한 곳에만 둔다.
        /// </summary>
        public static void HostSetScoreFromScale(NetIdentity id)
        {
            if (id == null || DataManager.Instance == null)
                return;

            SetScore(id, ScoreFromScale(ScaleOf(id)));
        }

        // ═══════════════════════════════════════════════════════
        //  탈락 — 사람·봇 공통 관문 (호스트 전용)
        // ═══════════════════════════════════════════════════════
        //
        // ★ 예전엔 같은 사건이 두 벌로 구현돼 있었다
        //     사람 : LanGameFlow.HostConfirmEliminated
        //            → PushMode.HostAwardKillCredit + ps.HostSetFlag(Eliminated)
        //     봇   : AIPlayerMovement.OnEliminated
        //            → PushMode.HostAwardKillCredit + botSync.HostBroadcastEliminated
        //   '밀치기의 킬 점수 정산'이라는 모드 전용 규칙이 양쪽에 복사돼 있어서,
        //   한쪽만 고치면 사람과 봇의 탈락 처리가 조용히 갈라졌다.
        //   이제 두 갈래가 여기서 만난다.
        public static void HostEliminate(NetIdentity id)
        {
            //오프라인(에디터 단독 실행)에서는 나 혼자가 곧 호스트다.
            //이 허용이 없으면 접속 없이 테스트할 때 봇이 초콜릿에 빠져도 안 죽는다
            if (!NetManager.Offline && !IsHostNow)
                return;
            if (id == null || IsOutOfPlay(id))
                return;

            //★ 예전엔 이 검사가 사람 경로(LanGameFlow.ReportSelfEliminated/HostConfirmEliminated)
            //  에만 있었다. 봇은 AIPlayerMovement.ReportEliminated → 여기로 바로 들어와서
            //  카운트다운 중이나 게임이 끝난 뒤에 초콜릿에 빠지면 사람과 달리 탈락했다.
            //  관문이 관문이려면 관문에서 봐야 한다
            if (LanGameFlow.Instance != null && LanGameFlow.Instance.Phase != GamePhase.Playing)
                return;

            // ★ 마지막 한 명은 탈락시키지 않는다
            //   밀치기의 승리 조건은 '최후의 1인'인데, 판정이 0.5초 주기였던 탓에
            //   둘이 같은 창 안에서 떨어지면 2 → 0 이 되어 <b>생존자가 사라졌다</b>.
            //   그러면 우승자도 순위표도 비어 결과 화면에 아무도 안 나온다.
            //
            //   탈락은 한 번에 하나씩 이 관문을 지나므로, 여기서 "지금 이 개체를 빼면
            //   아무도 안 남는가"를 보면 동시 낙하도 순차로 갈라진다.
            //   먼저 들어온 쪽이 탈락하며 남은 한 명으로 게임이 끝나고,
            //   뒤이어 들어온 쪽은 위 Phase 검사(Playing이 아님)에 걸려 되돌아간다.
            if (LanScoreboard.CountAlive() <= 1)
            {
                LanGameFlow.Instance?.HostDeclareLastSurvivor(id);
                return;
            }

            //모드 전용 정산은 그 모드가 씬에 있을 때만 돈다.
            //탈락 표시보다 먼저 해야 피해자의 점수가 남아 있다
            if (PushMode.Instance != null)
                PushMode.Instance.HostAwardKillCredit(id.NetId);

            LanPlayerState state = id.PlayerState;

            if (state != null)
            {
                state.HostSetFlag(PlayerFlags.Eliminated, true);
                return;
            }

            LanBotState bot = id.BotState;

            if (bot != null)
                bot.HostEliminate();

            //탈락이 확정된 즉시 승패를 본다. 0.5초 주기를 기다리면 그 사이에
            //또 한 명이 떨어져 생존자가 0이 될 수 있다
            LanGameFlow.Instance?.HostCheckEndNow();
        }

        public static int ScoreOf(NetIdentity id)
        {
            INetEntity e = EntityOf(id);
            return e != null ? e.Score : 0;
        }

        //봇은 전부 호스트 소유라 OwnerId 비교만으로는 같은 편으로 오인된다
        public static bool IsSameSide(NetIdentity a, NetIdentity b)
        {
            if (a == null || b == null)
                return false;

            if (a == b)
                return true;

            if (a.IsBot || b.IsBot)
                return false;

            return a.OwnerId == b.OwnerId;
        }

        /// <summary>
        /// 사람 + 봇만 모은다. 호출부의 List를 재사용하므로 할당이 없다.
        ///
        /// ★ NetWorld.Objects를 순회하면 안 되는 이유
        ///   거기엔 젤리 30여 개와 씬에 배치된 소품(캔디 300개 등)까지 들어 있다.
        ///   캐릭터 몇 개를 찾으려고 수백 개를 훑고 매번 IsJelly로 걸러내게 된다.
        ///   EntityRegistry는 종류별로 이미 나뉘어 있다.
        /// </summary>
        public static void CollectCharacters(List<NetIdentity> into)
        {
            into.Clear();

            IReadOnlyList<INetEntity> entities = EntityRegistry.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                INetEntity e = entities[i];
                if (e != null && e.Identity != null)
                    into.Add(e.Identity);
            }
        }
    }
}
