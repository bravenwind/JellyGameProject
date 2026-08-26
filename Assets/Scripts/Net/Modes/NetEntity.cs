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
        //     LanPlayerState.ScaleValue    → PlayerScaleController.currentScaleValue
        //     LanPlayerVisual.ScaleValue   → PlayerScaleController.currentScaleValue
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

            SetScore(id, DataManager.Instance.ScoreFromScale(ScaleOf(id)));
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
