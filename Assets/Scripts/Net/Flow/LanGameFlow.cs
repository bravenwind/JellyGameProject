using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace JellyNet
{
    public class LanGameFlow : MonoBehaviour
    {
        public static LanGameFlow Instance { get; private set; }

        //모드를 묻는 유일한 창구. 씬 안 어디서든 같은 답이 나온다.
        //출처는 로비(LanRoomConfig.Mode)뿐이라 씬 인스펙터에는 모드 설정이 없다
        public static GameModeType Mode
        {
            get { return GameState.CurrentGameMode; }
        }

        private static void ApplyMode(GameModeType m)
        {
            GameState.CurrentGameMode = m;
        }

        [Header("진행")]
        [Tooltip("흡수 모드 제한 시간(초). 밀치기는 시간 제한 없이 생존자로 끝난다.")]
        [SerializeField] private float gameDuration = 180f;
        public float GameDuration { get { return gameDuration; } }
        [Tooltip("시작 전 카운트다운(초)")]
        [SerializeField] private float countdownSeconds = 3f;
        // ★ 인스펙터에 내보내지 않는다 — Awake에서 LanRoomConfig.HumanCount로 무조건 덮어쓴다
        //   "모드와 인원은 로비에서만 온다"는 규칙이 있는데 인스펙터 칸이 남아 있으면
        //   거기서 고칠 수 있다고 오해하게 된다
        private int minPlayersToStart = 2;
        public int MinPlayersToStart { get { return minPlayersToStart; } set { minPlayersToStart = value; } }

        [Header("HUD")]
        [Tooltip("남은 시간 표시.")]
        [SerializeField] private TextMeshProUGUI gameTimerText;

        [Header("카운트다운")]
        [Tooltip("화면 가운데에 3·2·1·시작!을 띄울 텍스트.")]
        [SerializeField] private TextMeshProUGUI centerCountdownText;
        [SerializeField] private string gameStartLabel = "시작!";
        [SerializeField] private string gameEndLabel = "게임 종료!";

        [Header("종료 연출")]
        [Tooltip("종료 몇 초 전부터 3·2·1을 셀지.")]
        [SerializeField] private float endCountdownFrom = 3f;

        [Tooltip("게임 속도가 1 → 0.1로 느려지는 시간.")]
        [SerializeField] private float slowDownDuration = 1.2f;

        [Tooltip("완전히 멈춘 뒤 결과 씬으로 넘어가기까지 기다리는 시간.")]
        [SerializeField] private float freezeHold = 1f;

        [Tooltip("로딩 커튼 신호가 유실됐을 때 이만큼 기다렸다가 그냥 진행한다.")]
        [SerializeField] private float countdownCurtainTimeout = 6f;

        [Header("게임오버 화면 (흡수당했을 때)")]
        [Tooltip("씬의 결과 패널.")]
        [SerializeField] private GameObject gameResultPanel;
        [SerializeField] private TextMeshProUGUI resultTitleText;

        [Tooltip("탈락했을 때만 나오는 '관전하기' 버튼.")]
        [SerializeField] private GameObject spectateButton;

        [Tooltip("호스트와 연결이 끊겼을 때만 나오는 '메인으로 돌아가기' 버튼.")]
        [SerializeField] private GameObject returnToMainButton;

        [Header("결과 씬")]
        [SerializeField] private string resultSceneAbsorb = "GameResult_AbsorbMode";
        [SerializeField] private string resultScenePush = "GameResult_PushMode";

        /// <summary>지금 모드에 맞는 결과 씬 이름. 모드 분기를 밖에서 다시 쓰지 않게 여기서 준다.</summary>
        public string ResultSceneName
        {
            get { return Mode == GameModeType.Push ? resultScenePush : resultSceneAbsorb; }
        }

        public GamePhase Phase { get; private set; }
        public float Remaining { get; private set; }
        public int WinnerNetId { get; private set; }
        public int WinnerScore { get; private set; }

        private readonly NetWriter writer = new NetWriter();

        public static bool IsMode(GameModeType m)
        {
            return Instance == null || Mode == m;
        }

        public static bool IsFrozen
        {
            get
            {
                if (Instance == null)
                    return false;
                if (NetManager.Offline)
                    return false;
                return Instance.Phase != GamePhase.Playing;
            }
        }

        public static bool IsPlaying(GameModeType m)
        {
            if (Instance == null)
                return true;
            return Mode == m && Instance.Phase == GamePhase.Playing;
        }

        private float survivorCheckTimer;

        private readonly LanFlowHud hud = new LanFlowHud();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // ★ 판을 여는 쪽이 내 화면 상태를 초기화한다
            //   예전엔 DataManager.Awake가 이걸 불렀다. 설정 통이 전역 상태를 리셋하는 것도
            //   어색하지만, 더 나쁜 건 <b>순서를 보장할 수 없다</b>는 점이었다.
            //   DataManager.Awake가 이 Awake보다 뒤에 돌면 아래에서 세운 Phase를
            //   ResetValues가 None으로 되돌려버린다. 같은 함수 안에 두면 그 창이 닫힌다.
            GameState.ResetValues();

            Phase = GamePhase.Loading;

            hud.Bind(gameTimerText, centerCountdownText, gameResultPanel, resultTitleText,
                     spectateButton, returnToMainButton);

            hud.HideResultPanel();

            //모드와 인원은 로비에서만 온다
            minPlayersToStart = LanRoomConfig.HumanCount;
            ApplyMode(LanRoomConfig.Mode);
        }

        private void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
            {
                Debug.LogError("[LanGameFlow] NetManager가 없습니다.");
                return;
            }

            net.OnHostStarted += HandleHostStarted;
            net.OnPeerJoined += HandlePeerJoined;
            net.OnDisconnected += ResetAll;
            net.OnConnectionLost += HandleConnectionLost;

            RegisterRoutes(net);

            if (net.IsHost)
                HandleHostStarted();
        }

        public static string EliminationReason = "탈락했습니다!";

        public void ReportSelfEliminated(int netId, string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
                EliminationReason = reason;

            if (NetManager.Offline)
                return;
            if (Phase != GamePhase.Playing)
                return;

            NetManager net = NetManager.Instance;

            if (net.IsHost)
            {
                HostConfirmEliminated(netId);
                return;
            }

            writer.Begin(MsgType.EliminateRequest);
            writer.WriteInt(netId);
            writer.End();
            net.Client.Send(writer);
        }

        public void HostConfirmEliminated(int netId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null)
                return;
            if (Phase != GamePhase.Playing)
                return;

            //사람이든 봇이든 탈락은 NetEntity 한 관문을 지난다.
            //예전엔 여기서 직접 킬 크레딧을 정산하고 플래그를 세웠는데,
            //봇 쪽(AIPlayerMovement.OnEliminated)에 같은 코드가 한 벌 더 있었다
            NetEntity.HostEliminate(NetWorld.Instance.Find(netId));
        }

        private void RegisterRoutes(NetManager net)
        {
            net.RouteHost(MsgType.EliminateRequest, HandleEliminateRequest);

            net.RouteClient(MsgType.GamePhaseChange, r =>
            {
                GamePhase p = (GamePhase)r.ReadByte();
                byte modeId = r.ReadByte();
                Remaining = r.ReadFloat();

                //모드는 호스트가 정한다. 클라는 받은 값을 그대로 따른다
                ApplyMode((GameModeType)modeId);
                SetPhaseLocal(p);
            });

            net.RouteClient(MsgType.FinalStandings, r => LanStandings.Read(r));

            net.RouteClient(MsgType.GameOver, r =>
            {
                WinnerNetId = r.ReadInt();
                WinnerScore = r.ReadInt();
                OnGameOver();
            });
        }

        private void UnregisterRoutes(NetManager net)
        {
            net.UnrouteHost(MsgType.EliminateRequest);
            net.UnrouteClient(MsgType.GamePhaseChange);
            net.UnrouteClient(MsgType.FinalStandings);
            net.UnrouteClient(MsgType.GameOver);
        }

        private void HandleEliminateRequest(NetHost.Peer from, NetReader r)
        {
            int netId = r.ReadInt();

            NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
            if (id == null || id.OwnerId != from.Id)
                return;

            HostConfirmEliminated(netId);
        }

        private void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;
            net.OnHostStarted -= HandleHostStarted;
            net.OnPeerJoined -= HandlePeerJoined;
            net.OnDisconnected -= ResetAll;
            net.OnConnectionLost -= HandleConnectionLost;

            UnregisterRoutes(net);
        }

        private void ResetAll()
        {
            SetPhaseLocal(GamePhase.Loading);
            Remaining = gameDuration;
            WinnerNetId = 0;

            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
            }

            endingStarted = false;

            // ★ 여기서 Time.timeScale을 되돌리면 안 된다
            //   ResetAll은 OnDisconnected로 들어온다. 그런데 결과 씬으로 넘어가는 길이
            //   LanSceneFlow.ToResult → Disconnect() → Begin() 순서라,
            //   <b>커튼을 띄우기 전에</b> 이 함수가 먼저 돌아 종료 연출의 정지를 풀어버렸다.
            //   그래서 멈춰 있던 화면이 커튼 밖에서 정상 속도로 튀는 게 그대로 보였다.
            //   해제는 커튼이 화면을 다 덮은 뒤 LoadingSceneController가 한다.
            //   (연결이 끊겨 로비로 돌아가는 경우도 LanSceneFlow.Begin의 폴백이 처리한다)
        }

        private void HandleHostStarted()
        {
            Remaining = gameDuration;

            if (LanSpawnPoints.Instance != null)
            {
                LanSpawnPoints.Instance.Prepare();
                LanSpawnPoints.Instance.ResetAssignment();
            }

            SetPhaseLocal(GamePhase.Loading);
        }

        private void HandlePeerJoined(NetHost.Peer peer)
        {
            WritePhase(Phase, Remaining);
            NetManager.Instance.Host.SendTo(peer, writer);
        }

        private void Update()
        {
            if (NetManager.Offline)
                return;

            NetManager net = NetManager.Instance;

            if (Phase == GamePhase.Playing)
            {
                Remaining -= Time.deltaTime;

                if (Mode == GameModeType.Absorb && Remaining < 0f)
                    Remaining = 0f;
            }

            hud.UpdateTimer(Mode, gameDuration, Remaining);

            if (Phase == GamePhase.Playing && Mode == GameModeType.Absorb
                && Remaining <= endCountdownFrom && !endingStarted)
            {
                BeginEndSequence(true);
            }

            if (net.IsHost)
                HostTick();
        }

        private void HostTick()
        {
            switch (Phase)
            {
                case GamePhase.Loading:
                    TryStartCountdown();
                    break;

                case GamePhase.Playing:
                    ResyncClock();
                    CheckEndCondition();
                    break;
            }
        }

        private float resyncTimer;

        private void ResyncClock()
        {
            resyncTimer += Time.deltaTime;
            if (resyncTimer < 2f)
                return;
            resyncTimer = 0f;

            WritePhase(Phase, Remaining);
            NetManager.Instance.Host.Broadcast(writer);
        }

        public float Elapsed
        {
            get
            {
                if (Phase == GamePhase.Loading || Phase == GamePhase.Countdown)
                    return -1f;
                return Mathf.Max(0f, gameDuration - Remaining);
            }
        }

        /// <summary>
        /// 모든 기계에서 같아야 하는 판 경과 시간. 아직 판이 안 시작했으면 -1.
        ///
        /// ★ 왜 여기 있나
        ///   각자의 Time.time을 쓰면 기계마다 답이 달라 링 붕괴 시점이나 초콜릿 흐름
        ///   방향이 화면마다 엇갈린다. 호스트가 맞춰주는 이 값을 봐야 한다.
        ///
        ///   그런데 <c>Instance != null ? Instance.Elapsed : ...</c> 라는 같은 껍데기가
        ///   TileCollapseManager와 ChocolateFluid에 <b>따로</b> 있었다. 폴백만 서로 달랐고,
        ///   그래서 '아직 안 셌다'의 의미가 두 곳에서 갈렸다. 껍데기를 여기 하나로 모은다.
        ///   폴백을 어떻게 쓸지는 -1을 받은 쪽이 정한다.
        /// </summary>
        public static float SyncedElapsed
        {
            get { return Instance != null ? Instance.Elapsed : -1f; }
        }

        private Coroutine countdownRoutine;

        private void TryStartCountdown()
        {
            int players = CountPlayers();
            if (players < minPlayersToStart)
            {
                ReportStall("인원 " + players + "/" + minPlayersToStart);
                return;
            }

            if (LoadingSceneController.IsPresenting)
            {
                ReportStall("로딩 커튼이 아직 떠 있음");
                return;
            }

            NetManager.Instance.AddLog("인원 " + players + "명 — 카운트다운 시작");

            //단계를 바꾸면 방송까지 함께 나간다. 예전엔 MsgType.CountdownStart를 따로
            //쏘고 단계는 Loading에 둔 채 countdownRunning 플래그로 표시했다
            HostSetPhase(GamePhase.Countdown);
        }

        private float stallLogTimer;
        private string lastStallReason;

        private void ReportStall(string reason)
        {
            stallLogTimer -= Time.deltaTime;
            if (stallLogTimer > 0f && reason == lastStallReason)
                return;

            stallLogTimer = 3f;
            lastStallReason = reason;
            Debug.Log("[게임흐름] 시작 대기 중 — " + reason);
        }

        //호스트는 HostSetPhase로, 클라는 GamePhaseChange 수신으로 — 둘 다 SetPhaseLocal을 거쳐
        //여기로 들어온다. SetPhaseLocal은 단계가 실제로 바뀔 때만 부르지만,
        //ResyncClock이 같은 단계를 2초마다 다시 방송하므로 핸들로 한 번 더 막는다
        private void BeginCountdown()
        {
            if (countdownRoutine != null)
                return;
            countdownRoutine = StartCoroutine(CountdownRoutine());
        }

        private IEnumerator CountdownRoutine()
        {
            PlayerMovement.InputLocked = true;

            float guard = 0f;
            while (LoadingSceneController.IsPresenting && guard < countdownCurtainTimeout)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }

            hud.ShowCenter(true);

            for (int n = Mathf.RoundToInt(countdownSeconds); n >= 1; n--)
            {
                //내 로딩 커튼이 늦게 걷히는 동안 호스트가 이미 Playing으로 넘어갔다면
                //남은 숫자를 마저 세는 건 거짓말이다 — 곧바로 "시작!"으로 건너뛴다.
                //(호스트는 이 루틴 안에서 스스로 Playing을 만들므로 여기 걸리지 않는다)
                if (Phase != GamePhase.Countdown)
                    break;

                hud.Pop(n.ToString());
                yield return new WaitForSecondsRealtime(1f);
            }

            hud.Pop(gameStartLabel);

            if (NetManager.Instance != null && NetManager.Instance.IsHost)
            {
                Remaining = gameDuration;
                HostSetPhase(GamePhase.Playing);
            }

            yield return new WaitForSecondsRealtime(0.7f);
            hud.ShowCenter(false);

            countdownRoutine = null;
        }

        /// <summary>
        /// 탈락이 확정된 직후 호출한다. 0.5초 주기를 기다리지 않고 즉시 승패를 본다.
        /// 이렇게 해야 남은 한 명이 그 사이에 또 떨어지는 일이 없다.
        /// </summary>
        public void HostCheckEndNow()
        {
            NetManager net = NetManager.Instance;

            if (net == null || !net.IsHost)
                return;
            if (Phase != GamePhase.Playing)
                return;

            List<LanScoreboard.Entry> alive = LanScoreboard.Collect();

            if (alive.Count > 1)
                return;

            HostEndGame(alive.Count == 1 ? FindById(alive[0].netId) : null);
        }

        /// <summary>
        /// 마지막 한 명이 탈락하려는 순간 호출된다. 탈락시키지 않고 그대로 우승 처리한다.
        ///
        /// ★ 왜 살려두나
        ///   승리 조건이 '최후의 1인'이라 생존자가 0이 되면 우승자도 순위표도 없어진다.
        ///   같은 발판에서 둘이 함께 떨어질 때 실제로 그렇게 됐다.
        ///   마지막 한 명은 떨어지는 중이라도 이긴 것으로 본다.
        /// </summary>
        public void HostDeclareLastSurvivor(NetIdentity lastFaller)
        {
            NetManager net = NetManager.Instance;

            if (net == null || !net.IsHost)
                return;
            if (Phase != GamePhase.Playing)
                return;

            HostEndGame(lastFaller);
        }

        private void CheckEndCondition()
        {
            if (Mode == GameModeType.Absorb && Remaining <= 0f)
            {
                HostEndGame(null);
                return;
            }

            survivorCheckTimer += Time.deltaTime;
            if (survivorCheckTimer < 0.5f)
                return;
            survivorCheckTimer = 0f;

            if (gameDuration - Remaining < 3f)
                return;

            List<LanScoreboard.Entry> alive = LanScoreboard.Collect();
            if (alive.Count > 1)
                return;

            HostEndGame(alive.Count == 1 ? FindById(alive[0].netId) : null);
        }

        private NetIdentity FindById(int netId)
        {
            return NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
        }

        private void HostEndGame(NetIdentity winner)
        {
            LanStandings.Result final = LanStandings.Build(winner);

            WinnerNetId = final.WinnerNetId;
            WinnerScore = final.WinnerScore;

            LanStandings.Write(writer, final.Entries, final.WinnerName);
            NetManager.Instance.Host.Broadcast(writer);

            LanScoreboard.SetFinal(final.Entries, final.WinnerName);

            writer.Begin(MsgType.GameOver);
            writer.WriteInt(WinnerNetId);
            writer.WriteInt(WinnerScore);
            writer.End();
            NetManager.Instance.Host.Broadcast(writer);

            HostSetPhase(GamePhase.GameOver);
            OnGameOver();
        }

        private void HostSetPhase(GamePhase p)
        {
            SetPhaseLocal(p);

            WritePhase(p, Remaining);
            NetManager.Instance.Host.Broadcast(writer);
        }

        private void SetPhaseLocal(GamePhase p)
        {
            if (Phase == p)
                return;
            Phase = p;

            GameState.Phase = p;

            PlayerMovement.InputLocked = (p != GamePhase.Playing);

            //호스트든 클라든 이 한 곳에서 카운트다운이 시작된다
            if (p == GamePhase.Countdown)
                BeginCountdown();

            if (NetManager.Instance != null)
                NetManager.Instance.AddLog("게임 단계 → " + p
                    + (PlayerMovement.InputLocked ? "  (입력 잠금)" : "  (입력 해제)"));
        }

        //탈락은 판이 아직 도는 중이다. 관전할 수도, 나갈 수도 있게 둘 다 띄운다
        //
        // ★ 버튼 위치는 코드가 정하지 않는다
        //   연결이 끊겼을 때는 관전 버튼이 꺼져 하나만 남는데, 그때 남은 버튼의 x를
        //   손으로 가운데로 옮기면 <b>되돌리는 코드까지</b> 필요해진다.
        //   두 버튼을 감싼 컨테이너의 HorizontalLayoutGroup이 알아서 정렬하므로
        //   여기서는 켤지 말지만 정한다.
        public void ShowLocalGameOver(string message)
        {
            hud.ShowGameOver(message, true, true);

            if (PlayerMovement.Local != null)
                PlayerMovement.Local.enabled = false;

            if (NetManager.Instance != null)
                NetManager.Instance.AddLog("게임오버: " + message.Replace("\n", " "));
        }

        public const string DISCONNECT_MESSAGE = "서버와 연결이 끊겼습니다.";

        private void HandleConnectionLost()
        {
            //판이 이미 끝났으면 호스트가 결과 씬으로 넘어가며 소켓을 닫은 것이다.
            //정상 종료와 사고가 소켓 입장에서는 똑같이 보이므로 진행 단계로 구분한다.
            if (Phase == GamePhase.GameOver || endingStarted)
                return;

            SetPhaseLocal(GamePhase.GameOver);

            if (LanSpectator.Instance != null)
                LanSpectator.Instance.Stop();

            //호스트가 사라지면 봇의 위치·애니메이션이 마지막 값에 그대로 멈춘다
            //걷는 자세로 얼어붙지 않게 여기서 직접 내려준다
            StopWorldAnimations();

            PlayerMovement.InputLocked = true;

            if (PlaySFXAudio.Instance != null)
                PlaySFXAudio.Instance.StopWalking();

            hud.ShowCenter(false);
            hud.ShowGameOver(DISCONNECT_MESSAGE, false, true);
        }

        //"IsMoving"을 가진 건 사람·봇뿐이다. Objects를 돌면 씬 사탕 300개까지
        //GetComponentsInChildren<Animator>로 훑게 된다
        private static readonly List<NetIdentity> stopBuffer = new List<NetIdentity>();

        private static void StopWorldAnimations()
        {
            NetEntity.CollectCharacters(stopBuffer);

            for (int i = 0; i < stopBuffer.Count; i++)
            {
                NetIdentity id = stopBuffer[i];
                if (id == null)
                    continue;

                foreach (Animator anim in id.GetComponentsInChildren<Animator>(true))
                    anim.SetBool(AnimParams.IsMoving, false);
            }
        }

        public void OnClick_Spectate()
        {
            hud.HideResultPanel();

            if (LanSpectator.Instance != null)
                LanSpectator.Instance.Begin();
        }

        //인스펙터 OnClick에 연결되는 이름이다. 씬에서 다시 연결해야 하므로 함부로 바꾸지 말 것
        public void OnClick_ReturnToMain()
        {
            LanSceneFlow.ToMain();
        }

        private void OnGameOver()
        {
            NetIdentity w = (NetWorld.Instance != null && WinnerNetId != 0)
                ? NetWorld.Instance.Find(WinnerNetId) : null;

            string who = w != null ? ("P" + w.OwnerId) : "없음";
            if (NetManager.Instance != null)
                NetManager.Instance.AddLog("게임 종료! 승자 " + who + " (점수 " + WinnerScore + ")");

            PlayerMovement.InputLocked = true;

            BeginEndSequence(false);
        }

        private bool endingStarted;

        private void BeginEndSequence(bool withCountdown)
        {
            if (endingStarted)
                return;
            endingStarted = true;
            StartCoroutine(EndSequenceRoutine(withCountdown));
        }

        private IEnumerator EndSequenceRoutine(bool withCountdown)
        {
            if (PlaySFXAudio.Instance != null)
                PlaySFXAudio.Instance.StopWalking();

            if (withCountdown)
            {
                hud.ShowCenter(true);

                int shown = -1;
                while (Remaining > 0f && Phase == GamePhase.Playing)
                {
                    int n = Mathf.CeilToInt(Remaining);

                    if (n != shown && n >= 1)
                    {
                        shown = n;
                        hud.FlashCountdown(this, n.ToString());
                    }

                    yield return null;
                }

                if (NetManager.Instance != null && NetManager.Instance.IsHost
                    && Phase == GamePhase.Playing)
                {
                    Remaining = 0f;
                    HostEndGame(null);
                }
            }

            hud.ShowEndLabel(gameEndLabel);

            float elapsed = 0f;
            while (elapsed < slowDownDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                Time.timeScale = Mathf.Lerp(1f, 0.1f, elapsed / slowDownDuration);
                yield return null;
            }

            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(freezeHold);

            GoToResultScene();
        }

        private void GoToResultScene()
        {
            string scene = ResultSceneName;
            if (string.IsNullOrEmpty(scene))
                return;

            LanSceneFlow.ToResult(scene);
        }

        private void WritePhase(GamePhase p, float remaining)
        {
            writer.Begin(MsgType.GamePhaseChange);
            writer.WriteByte((byte)p);
            writer.WriteByte((byte)Mode);
            writer.WriteFloat(remaining);
            writer.End();
        }

        //사람 수. 봇은 세지 않는다(LanPlayerState는 사람 프리팹에만 붙어 있다)
        private int CountPlayers()
        {
            return EntityRegistry.Players.Count;
        }
    }
}
