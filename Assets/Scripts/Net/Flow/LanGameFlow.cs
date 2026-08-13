using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace JellyNet
{
    public class LanGameFlow : MonoBehaviour
    {
        public static LanGameFlow Instance { get; private set; }

        [Header("모드")]
        public GameModeType mode = GameModeType.Absorb;

        [Header("진행")]
        [Tooltip("흡수 모드 제한 시간(초). 밀치기는 시간 제한 없이 생존자로 끝난다.")]
        public float gameDuration = 180f;
        [Tooltip("시작 전 카운트다운(초)")]
        public float countdownSeconds = 3f;
        [Tooltip("이 인원이 모이면 호스트가 게임을 시작한다")]
        public int minPlayersToStart = 2;

        [Header("HUD")]
        [Tooltip("남은 시간 표시. 기존 GameModeManager가 쓰던 텍스트를 그대로 연결한다.")]
        public TextMeshProUGUI gameTimerText;

        [Header("카운트다운")]
        [Tooltip("화면 가운데에 3·2·1·시작!을 띄울 텍스트. 기존 GameModeManager의 것을 그대로 쓰면 된다.")]
        public TextMeshProUGUI centerCountdownText;
        public string gameStartLabel = "시작!";
        public string gameEndLabel = "게임 종료!";

        [Header("종료 연출")]
        [Tooltip("종료 몇 초 전부터 3·2·1을 셀지.")]
        public float endCountdownFrom = 3f;

        [Tooltip("게임 속도가 1 → 0.1로 느려지는 시간.")]
        public float slowDownDuration = 1.2f;

        [Tooltip("완전히 멈춘 뒤 결과 씬으로 넘어가기까지 기다리는 시간.")]
        public float freezeHold = 1f;

        [Tooltip("로딩 커튼 신호가 유실됐을 때 이만큼 기다렸다가 그냥 진행한다.")]
        public float countdownCurtainTimeout = 6f;

        [Header("게임오버 화면 (흡수당했을 때)")]
        [Tooltip("씬의 결과 패널. 기존 GameModeManager가 쓰던 것을 그대로 연결하면 된다.")]
        public GameObject gameResultPanel;
        public TextMeshProUGUI resultTitleText;

        [Tooltip("탈락했을 때만 나오는 '관전하기' 버튼.")]
        public GameObject spectateButton;

        [Tooltip("호스트와 연결이 끊겼을 때만 나오는 '메인으로 돌아가기' 버튼.")]
        public GameObject returnToMainButton;

        [Header("결과 씬")]
        [Tooltip("테스트 중에는 꺼둔다. 켜면 종료 후 결과 씬으로 넘어간다.")]
        public bool autoLoadResultScene = true;
        public string resultSceneAbsorb = "GameResult_AbsorbMode";
        public string resultScenePush = "GameResult_PushMode";

        public GamePhase Phase { get; private set; }
        public float Remaining { get; private set; }
        public int WinnerNetId { get; private set; }
        public int WinnerScore { get; private set; }

        private readonly NetWriter writer = new NetWriter();

        public static bool IsMode(GameModeType m)
        {
            return Instance == null || Instance.mode == m;
        }

        public static bool IsFrozen
        {
            get
            {
                if (Instance == null)
                    return false;
                NetManager net = NetManager.Instance;
                if (net == null || net.CurrentMode == NetManager.Mode.None)
                    return false;
                return Instance.Phase != GamePhase.Playing;
            }
        }

        public static bool IsPlaying(GameModeType m)
        {
            if (Instance == null)
                return true;
            return Instance.mode == m && Instance.Phase == GamePhase.Playing;
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
            Phase = GamePhase.Loading;

            hud.Bind(gameTimerText, centerCountdownText, gameResultPanel, resultTitleText,
                     spectateButton, returnToMainButton);

            hud.HideResultPanel();

            if (LanLobby.ChosenMode.HasValue && LanLobby.ChosenMode.Value != mode)
            {
                Debug.LogWarning("[LanGameFlow] 씬에 설정된 모드(" + mode
                    + ")와 로비에서 넘어온 모드(" + LanLobby.ChosenMode.Value
                    + ")가 다릅니다. 로비 값을 따릅니다. "
                    + "LanLobby의 씬 이름 설정을 확인해주세요.");
                mode = LanLobby.ChosenMode.Value;
            }

            GameState.CurrentGameMode = mode;

            if (LanRoomConfig.HasValue)
            {
                minPlayersToStart = LanRoomConfig.HumanCount;
                mode = LanRoomConfig.Mode;
                GameState.CurrentGameMode = mode;
            }
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
            net.OnClientMessage += HandleClientMessage;
            net.OnHostMessage += HandleHostMessage;
            net.OnDisconnected += ResetAll;
            net.OnConnectionLost += HandleConnectionLost;

            if (net.IsHost)
                HandleHostStarted();
        }

        public static string EliminationReason = "탈락했습니다!";

        public void ReportSelfEliminated(int netId, string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
                EliminationReason = reason;

            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;
            if (Phase != GamePhase.Playing)
                return;

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

            NetIdentity id = NetWorld.Instance.Find(netId);
            if (id == null)
                return;

            LanPlayerState ps = id.GetComponent<LanPlayerState>();
            if (ps == null || ps.IsOutOfPlay)
                return;

            if (PushMode.Instance != null)
                PushMode.Instance.HostReportEliminated(netId);

            ps.HostSetFlag(PlayerFlags.Eliminated, true);
        }

        private void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type != MsgType.EliminateRequest)
                return;

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
            net.OnClientMessage -= HandleClientMessage;
            net.OnHostMessage -= HandleHostMessage;
            net.OnDisconnected -= ResetAll;
            net.OnConnectionLost -= HandleConnectionLost;
        }

        private void ResetAll()
        {
            SetPhaseLocal(GamePhase.Loading);
            Remaining = gameDuration;
            WinnerNetId = 0;
            countdownRunning = false;
            endingStarted = false;
            Time.timeScale = 1f;
        }

        private void HandleHostStarted()
        {
            Remaining = gameDuration;
            GameState.CurrentGameMode = mode;

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
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;

            if (Phase == GamePhase.Playing)
            {
                Remaining -= Time.deltaTime;

                if (mode == GameModeType.Absorb && Remaining < 0f)
                    Remaining = 0f;
            }

            hud.UpdateTimer(mode, gameDuration, Remaining);

            if (Phase == GamePhase.Playing && mode == GameModeType.Absorb
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
                if (Phase == GamePhase.Loading)
                    return -1f;
                return Mathf.Max(0f, gameDuration - Remaining);
            }
        }

        private bool countdownRunning;

        private void TryStartCountdown()
        {
            if (countdownRunning)
                return;

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

            writer.Begin(MsgType.CountdownStart);
            writer.End();
            NetManager.Instance.Host.Broadcast(writer);

            NetManager.Instance.AddLog("인원 " + players + "명 — 카운트다운 시작");
            BeginCountdown();
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

        private void BeginCountdown()
        {
            if (countdownRunning)
                return;
            countdownRunning = true;
            StartCoroutine(CountdownRoutine());
        }

        private System.Collections.IEnumerator CountdownRoutine()
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

            countdownRunning = false;
        }

        private void CheckEndCondition()
        {
            if (mode == GameModeType.Absorb && Remaining <= 0f)
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

        private void HandleClientMessage(MsgType type, NetReader r)
        {
            switch (type)
            {
                case MsgType.GamePhaseChange:
                    {
                        GamePhase p = (GamePhase)r.ReadByte();
                        byte modeId = r.ReadByte();
                        Remaining = r.ReadFloat();

                        mode = (GameModeType)modeId;
                        GameState.CurrentGameMode = mode;
                        SetPhaseLocal(p);
                        break;
                    }

                case MsgType.CountdownStart:
                    BeginCountdown();
                    break;

                case MsgType.FinalStandings:
                    LanStandings.Read(r);
                    break;

                case MsgType.GameOver:
                    {
                        WinnerNetId = r.ReadInt();
                        WinnerScore = r.ReadInt();
                        OnGameOver();
                        break;
                    }
            }
        }

        private void SetPhaseLocal(GamePhase p)
        {
            if (Phase == p)
                return;
            Phase = p;

            GameState.Phase = p;

            PlayerMovement.InputLocked = (p != GamePhase.Playing);

            if (NetManager.Instance != null)
                NetManager.Instance.AddLog("게임 단계 → " + p
                    + (PlayerMovement.InputLocked ? "  (입력 잠금)" : "  (입력 해제)"));
        }

        //탈락은 판이 아직 도는 중이다. 나가는 버튼 없이 관전만 남긴다
        public void ShowLocalGameOver(string message)
        {
            hud.ShowGameOver(message, true, false);

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

        private static void StopWorldAnimations()
        {
            if (NetWorld.Instance == null)
                return;

            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity id = kv.Value;
                if (id == null)
                    continue;

                foreach (Animator anim in id.GetComponentsInChildren<Animator>(true))
                    anim.SetBool("IsMoving", false);
            }
        }

        public void OnClick_Spectate()
        {
            hud.HideResultPanel();

            if (LanSpectator.Instance != null)
                LanSpectator.Instance.Begin();
        }

        public void OnClick_ReturnToMain()
        {
            GoToMainMenu();
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

        private System.Collections.IEnumerator EndSequenceRoutine(bool withCountdown)
        {
            PlaySFXAudio.Instance?.StopWalking();

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

            if (autoLoadResultScene)
                GoToResultScene();
        }

        private void GoToResultScene()
        {
            string scene = (mode == GameModeType.Push) ? resultScenePush : resultSceneAbsorb;
            if (string.IsNullOrEmpty(scene))
                return;

            LanSceneFlow.ToResult(scene);
        }

        public void GoToMainMenu()
        {
            if (!LanSceneFlow.CanLeaveMatch)
            {
                if (NetManager.Instance != null)
                    NetManager.Instance.AddLog("경기 중에는 나갈 수 없습니다.");
                return;
            }

            LanSceneFlow.ToMain();
        }

        private void WritePhase(GamePhase p, float remaining)
        {
            writer.Begin(MsgType.GamePhaseChange);
            writer.WriteByte((byte)p);
            writer.WriteByte((byte)mode);
            writer.WriteFloat(remaining);
            writer.End();
        }

        private int CountPlayers()
        {
            if (NetWorld.Instance == null)
                return 0;
            int n = 0;
            foreach (var kv in NetWorld.Instance.Objects)
                if (kv.Value != null && kv.Value.PrefabId < NetConfig.JELLY_PREFAB_START)
                    n++;
            return n;
        }
    }
}
