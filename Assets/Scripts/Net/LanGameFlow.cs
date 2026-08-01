using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;   // DOScale은 확장 메서드라 네임스페이스를 열어야 쓸 수 있다

namespace JellyNet
{
    /// <summary>
    /// 게임 진행 흐름(대기 → 진행 → 종료 → 결과). GameModeManager의 흐름부를 대신한다.
    ///
    /// ★ 원본이 겪던 문제가 여기서는 구조적으로 사라진다
    ///   Photon판은 각 클라가 자기 타이머로 종료를 판정해서, RPC 지연·프레임 히칭 때문에
    ///   "한쪽은 이미 결과 화면, 다른 쪽은 아직 진행 중"이 되곤 했다.
    ///   (그래서 GameStartTime 룸 프로퍼티로 서버 클럭을 억지로 맞추는 코드가 있었다)
    ///
    ///   여기서는 <b>종료를 선언하는 주체가 호스트 하나뿐이다.</b>
    ///   클라의 타이머는 화면 표시용일 뿐이고, 실제 종료는 GameOver 메시지가 도착할 때다.
    ///   시계가 조금 어긋나도 "끝나는 순간"은 전원이 같다.
    ///
    /// ★ GameState는 그대로 재사용한다
    ///   기존 UI가 GameState.OnPhaseChanged를 구독하고 있으므로,
    ///   여기서 GameState.Phase만 바꿔주면 UI는 손대지 않아도 반응한다.
    /// </summary>
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
        public TMPro.TextMeshProUGUI gameTimerText;

        [Header("카운트다운")]
        [Tooltip("화면 가운데에 3·2·1·시작!을 띄울 텍스트. 기존 GameModeManager의 것을 그대로 쓰면 된다.")]
        public TMPro.TextMeshProUGUI centerCountdownText;
        public string gameStartLabel = "시작!";

        [Tooltip("로딩 커튼 신호가 유실됐을 때 이만큼 기다렸다가 그냥 진행한다.")]
        public float countdownCurtainTimeout = 6f;

        [Header("게임오버 화면 (흡수당했을 때)")]
        [Tooltip("씬의 결과 패널. 기존 GameModeManager가 쓰던 것을 그대로 연결하면 된다.")]
        public GameObject gameResultPanel;
        public TMPro.TextMeshProUGUI resultTitleText;

        [Header("결과 씬")]
        [Tooltip("테스트 중에는 꺼둔다. 켜면 종료 후 결과 씬으로 넘어간다.")]
        // ★ 기본을 켬으로 바꿨다.
        //   꺼져 있으면 "승자!" 패널만 뜨고 결과 씬으로 영영 안 넘어간다.
        //   테스트 편의로 꺼뒀던 값이 그대로 남아 있었다.
        public bool autoLoadResultScene = true;
        public string resultSceneAbsorb = "GameResult_AbsorbMode";
        public string resultScenePush = "GameResult_PushMode";
        public float resultSceneDelay = 3f;

        // ── 상태 (전원 공유) ──
        public GamePhase Phase { get; private set; }
        public float Remaining { get; private set; }
        public int WinnerNetId { get; private set; }
        public int WinnerScore { get; private set; }

        readonly NetWriter _w = new NetWriter();

        // ─────────────────────────────────────────────
        //  다른 모드 스크립트가 "지금 내가 동작해도 되나"를 묻는 창구
        // ─────────────────────────────────────────────

        /// <summary>이 모드가 지금 씬의 모드인가. 흐름 관리자가 없으면 제한하지 않는다.</summary>
        public static bool IsMode(GameModeType m)
        {
            return Instance == null || Instance.mode == m;
        }

        /// <summary>이 모드이면서 게임이 진행 중인가. 판정·보상은 전부 이걸 통과해야 한다.</summary>
        /// <summary>
        /// 지금 모든 것이 멈춰 있어야 하는가(대기·카운트다운·종료).
        ///
        /// 젤리·봇처럼 스스로 움직이는 것들이 이 값을 본다.
        /// 플레이어는 PlayerMovement.InputLocked가 따로 막는다.
        /// 접속이 없으면(단독 테스트) 멈추지 않는다 — 안 그러면 아무것도 안 움직인다.
        /// </summary>
        public static bool IsFrozen
        {
            get
            {
                if (Instance == null) return false;
                NetManager net = NetManager.Instance;
                if (net == null || net.CurrentMode == NetManager.Mode.None) return false;
                return Instance.Phase != GamePhase.Playing;
            }
        }

        public static bool IsPlaying(GameModeType m)
        {
            if (Instance == null) return true;      // 단독 테스트(NetTest 씬)에서는 제한 없음
            return Instance.mode == m && Instance.Phase == GamePhase.Playing;
        }

        float _survivorCheckTimer;
        float _resultTimer;
        bool _resultPending;

        // ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Phase = GamePhase.Loading;

            // ★ 씬과 모드가 어긋났는지 확인한다.
            //   AbsorbMode·PushMode·타일 붕괴·봇 FSM이 전부 이 모드로 갈라진다.
            //   어긋나면 "아무 일도 안 일어나는" 형태로 조용히 망가지므로,
            //   차라리 시끄럽게 알리고 로비가 정한 값을 따른다.
            //   씬을 직접 열어 테스트할 때는 로비 값이 없다(null) — 그때는 씬 설정을 쓴다.
            if (LanLobby.ChosenMode.HasValue && LanLobby.ChosenMode.Value != mode)
            {
                Debug.LogWarning("[LanGameFlow] 씬에 설정된 모드(" + mode
                    + ")와 로비에서 넘어온 모드(" + LanLobby.ChosenMode.Value
                    + ")가 다릅니다. 로비 값을 따릅니다. "
                    + "LanLobby의 씬 이름 설정을 확인해주세요.");
                mode = LanLobby.ChosenMode.Value;
            }

            GameState.CurrentGameMode = mode;

            // ★ 시작 인원도 로비를 따른다.
            //   로비에서 이미 인원을 다 모아 왔으므로, 게임 씬에서 또 기다리면
            //   같은 조건을 두 번 검사하게 되고 한쪽만 어긋나도 게임이 시작되지 않는다.
            if (LanRoomConfig.HasValue)
            {
                minPlayersToStart = LanRoomConfig.HumanCount;
                mode = LanRoomConfig.Mode;
                GameState.CurrentGameMode = mode;
            }
        }

        void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null) { Debug.LogError("[LanGameFlow] NetManager가 없습니다."); return; }

            net.OnHostStarted += HandleHostStarted;
            net.OnPeerJoined += HandlePeerJoined;
            net.OnClientMessage += HandleClientMessage;
            net.OnHostMessage += HandleHostMessage;
            net.OnDisconnected += ResetAll;

            // ★ 로비에서 이미 호스트를 켜고 넘어온 경우를 따라잡는다.
            //   OnHostStarted는 Main 씬에서 이미 지나갔다 — 여기서 다시 오지 않는다.
            //   이게 없으면 스폰 슬롯이 준비되지 않고 단계도 Loading으로 안 잡혀
            //   게임이 영영 시작되지 않는다.
            if (net.IsHost) HandleHostStarted();
        }

        // ═════════════════════════════════════════════
        //  탈락 (초콜릿 · 낙사)
        // ═════════════════════════════════════════════
        //
        // ★ 왜 클라가 직접 죽지 않고 호스트에게 묻는가
        //   초콜릿 경계는 아슬아슬하다. 각자 자기 화면에서 판정하면 위치 보간 오차
        //   때문에 "내 화면에선 빠졌는데 상대 화면에선 안 빠진" 상태가 생긴다.
        //   그러면 죽은 사람이 남의 화면에선 계속 돌아다닌다.
        //
        //   그렇다고 호스트가 혼자 판정하게 하면, 원격 위치는 InterpDelay만큼
        //   과거라 본인 화면보다 늦게 죽는다(억울한 죽음/살아남음).
        //   그래서 원본과 같은 절충을 쓴다 — <b>본인이 신고하고 호스트가 확정한다.</b>

        /// <summary>내 캐릭터가 초콜릿에 빠졌다. 호스트에 알린다.</summary>
        public void ReportSelfEliminated(int netId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;
            if (Phase != GamePhase.Playing) return;

            if (net.IsHost) { HostConfirmEliminated(netId); return; }

            _w.Begin(MsgType.EliminateRequest);
            _w.WriteInt(netId);
            _w.End();
            net.Client.Send(_w);
        }

        /// <summary>호스트: 탈락을 확정하고 전원에게 알린다.</summary>
        public void HostConfirmEliminated(int netId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null) return;
            if (Phase != GamePhase.Playing) return;

            NetIdentity id = NetWorld.Instance.Find(netId);
            if (id == null) return;

            LanPlayerState ps = id.GetComponent<LanPlayerState>();
            if (ps == null || ps.IsOutOfPlay) return;

            ps.HostSetFlag(PlayerFlags.Eliminated, true);   // 방송까지 여기서 일어난다
        }

        void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type != MsgType.EliminateRequest) return;

            int netId = r.ReadInt();

            // 남을 죽이려는 요청은 무시한다
            NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
            if (id == null || id.OwnerId != from.Id) return;

            HostConfirmEliminated(netId);
        }

        void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;
            net.OnHostStarted -= HandleHostStarted;
            net.OnPeerJoined -= HandlePeerJoined;
            net.OnClientMessage -= HandleClientMessage;
            net.OnHostMessage -= HandleHostMessage;
            net.OnDisconnected -= ResetAll;
        }

        void ResetAll()
        {
            SetPhaseLocal(GamePhase.Loading);
            Remaining = gameDuration;
            WinnerNetId = 0;
            _countdownRunning = false;
            _resultPending = false;
        }

        void HandleHostStarted()
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

        /// <summary>늦게 들어온 사람에게 현재 진행 상황을 알려준다.</summary>
        void HandlePeerJoined(NetHost.Peer peer)
        {
            WritePhase(Phase, Remaining);
            NetManager.Instance.Host.SendTo(peer, _w);
        }

        // ═════════════════════════════════════════════
        void Update()
        {
            // ★ 결과 씬 예약은 연결 상태와 무관하게 처리한다.
            //
            //   예전엔 아래 '연결 없으면 return'보다 뒤에 있었다. 그런데 게임이 끝나는
            //   순간에 호스트가 나가거나 연결이 끊기면, 이 타이머가 영영 안 돌아
            //   <b>게임오버 화면에 갇힌다.</b> 결과는 이미 스냅샷으로 들고 있으므로
            //   연결이 없어도 결과 씬으로 가는 데는 아무 문제가 없다.
            if (_resultPending)
            {
                _resultTimer -= Time.deltaTime;
                if (_resultTimer <= 0f) { _resultPending = false; GoToResultScene(); }
            }

            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            // 표시용 타이머는 전원이 각자 돌린다(종료 판정은 하지 않는다)
            if (Phase == GamePhase.Playing && mode == GameModeType.Absorb)
                Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);

            UpdateTimerUI();

            if (net.IsHost) HostTick();
        }

        // ═════════════════════════════════════════════
        //  호스트: 시작·종료를 결정한다
        // ═════════════════════════════════════════════
        void HostTick()
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

        float _resyncTimer;

        /// <summary>
        /// 호스트가 남은 시간을 주기적으로 다시 알린다.
        ///
        /// ★ 왜 필요한가
        ///   Remaining은 전원이 각자 Time.deltaTime으로 깎는다. 표시용 타이머라면
        ///   몇십 ms 어긋나도 상관없지만, <b>링 붕괴가 이 시각을 기준으로 터진다.</b>
        ///   로딩 끊김·프레임 정체가 한 번만 있어도 클라마다 다른 순간에 링이 무너져
        ///   "내 화면에선 아직 땅이 있는데 저쪽에선 떨어졌다"가 된다.
        ///   메시지 하나가 12바이트라 2초에 한 번이면 비용은 무시할 수준이다.
        /// </summary>
        void ResyncClock()
        {
            _resyncTimer += Time.deltaTime;
            if (_resyncTimer < 2f) return;
            _resyncTimer = 0f;

            WritePhase(Phase, Remaining);
            NetManager.Instance.Host.Broadcast(_w);
        }

        /// <summary>
        /// 게임 시작 후 흐른 시간. 링 붕괴가 이 값으로 어느 링까지 무너뜨릴지 정한다.
        /// 시작 전이면 -1.
        /// </summary>
        public float Elapsed
        {
            get
            {
                if (Phase == GamePhase.Loading) return -1f;
                return Mathf.Max(0f, gameDuration - Remaining);
            }
        }

        // ═════════════════════════════════════════════
        //  3 · 2 · 1 · 시작!
        // ═════════════════════════════════════════════
        //
        // ★ 왜 호스트가 '카운트다운 시작'을 따로 방송하는가
        //   각자 씬에 도착한 순간부터 세면 도착 시각이 달라 숫자가 어긋난다.
        //   호스트가 "지금부터 세라"를 한 번 쏘면 전원이 같은 박자로 센다.
        //   실제 Playing 전환은 호스트가 자기 카운트다운 끝에 다시 알린다
        //   — 숫자는 연출이고, 게임이 언제 시작됐는지는 호스트만 정한다.
        //
        // ★ 로딩 커튼이 걷힐 때까지 기다린다
        //   커튼에 가려진 채로 3·2·1이 지나가면 아무도 못 본다.
        //   기다리는 동안에도 단계는 Loading이라 조작·봇은 계속 멈춰 있다.

        bool _countdownRunning;

        void TryStartCountdown()
        {
            if (_countdownRunning) return;

            // ★ CountPlayers는 스폰된 '사람 캐릭터' 수다.
            //   즉 전원이 게임 씬에 도착해 SceneReady까지 끝났다는 뜻이기도 하다.
            int players = CountPlayers();
            if (players < minPlayersToStart) return;

            // 커튼이 걷히기 전에는 시작 신호를 보내지 않는다
            if (LoadingSceneController.IsPresenting) return;

            _w.Begin(MsgType.CountdownStart);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);

            NetManager.Instance.AddLog("인원 " + players + "명 — 카운트다운 시작");
            BeginCountdown();
        }

        void BeginCountdown()
        {
            if (_countdownRunning) return;
            _countdownRunning = true;
            StartCoroutine(CountdownRoutine());
        }

        System.Collections.IEnumerator CountdownRoutine()
        {
            PlayerMovement.InputLocked = true;

            // 커튼이 완전히 걷힐 때까지(신호가 유실돼도 timeout 후 진행)
            float guard = 0f;
            while (LoadingSceneController.IsPresenting && guard < countdownCurtainTimeout)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }

            if (centerCountdownText != null) centerCountdownText.gameObject.SetActive(true);

            for (int n = Mathf.RoundToInt(countdownSeconds); n >= 1; n--)
            {
                if (centerCountdownText != null)
                {
                    centerCountdownText.text = n.ToString();
                    PopCenterText();
                }
                yield return new WaitForSecondsRealtime(1f);
            }

            if (centerCountdownText != null)
            {
                centerCountdownText.text = gameStartLabel;
                PopCenterText();
            }

            // 실제 시작 선언은 호스트만 한다. 참가자는 GamePhaseChange를 받아 따라간다.
            if (NetManager.Instance != null && NetManager.Instance.IsHost)
            {
                Remaining = gameDuration;
                HostSetPhase(GamePhase.Playing);
            }

            yield return new WaitForSecondsRealtime(0.7f);
            if (centerCountdownText != null) centerCountdownText.gameObject.SetActive(false);

            _countdownRunning = false;
        }

        void PopCenterText()
        {
            if (centerCountdownText == null) return;
            centerCountdownText.rectTransform.localScale = Vector3.one * 1.6f;
            centerCountdownText.rectTransform
                .DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        void CheckEndCondition()
        {
            // ── ① 시간 종료 (흡수 모드) ──
            if (mode == GameModeType.Absorb && Remaining <= 0f)
            {
                HostEndGame(null);
                return;
            }

            // ── ② 마지막 생존자 (LMS) ──
            //
            // ★ 흡수 모드에도 적용한다.
            //   원래는 밀치기 모드에만 있었는데, 흡수 모드도 남은 사람이 하나면
            //   더 겨룰 상대가 없어 남은 시간을 빈 맵에서 보내게 된다.
            //
            // ★ 봇도 생존자로 센다.
            //   봇만 남았는데 게임이 끝나버리면, 사람이 다 죽은 판이
            //   승자 없이 종료된다. 봇도 참가자다.
            _survivorCheckTimer += Time.deltaTime;
            if (_survivorCheckTimer < 0.5f) return;
            _survivorCheckTimer = 0f;

            // 시작 직후엔 스폰·상태 전파가 안 끝나 생존자가 과소 집계된다(조기 종료 방지)
            if (gameDuration - Remaining < 3f) return;

            List<LanScoreboard.Entry> alive = LanScoreboard.Collect();
            if (alive.Count > 1) return;

            HostEndGame(alive.Count == 1 ? FindById(alive[0].netId) : null);
        }

        NetIdentity FindById(int netId)
        {
            return NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
        }

        void HostEndGame(NetIdentity winner)
        {
            // ★ 순위를 '지금' 떠야 한다.
            //   조금만 늦어도 흡수 연출이 끝나며 오브젝트가 사라지거나
            //   탈락 플래그가 더 붙어서, 끝난 판과 다른 순위가 나온다.
            List<LanScoreboard.Entry> standings = LanScoreboard.Collect();

            // 승자를 따로 안 줬으면 순위 1등이 승자다(흡수 모드의 시간 종료).
            if (winner == null && standings.Count > 0)
                winner = FindById(standings[0].netId);

            WinnerNetId = winner != null ? winner.NetId : 0;

            LanPlayerState ws = winner != null ? winner.GetComponent<LanPlayerState>() : null;
            WinnerScore = ws != null ? ws.Score : 0;
            if (ws == null && standings.Count > 0) WinnerScore = standings[0].score;

            string winnerName = standings.Count > 0 ? standings[0].name : "";

            BroadcastStandings(standings, winnerName);
            LanScoreboard.SetFinal(standings, winnerName);   // 호스트 자신도 보관

            _w.Begin(MsgType.GameOver);
            _w.WriteInt(WinnerNetId);
            _w.WriteInt(WinnerScore);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);

            HostSetPhase(GamePhase.GameOver);
            OnGameOver();
        }

        // ═════════════════════════════════════════════
        //  최종 순위 방송
        // ═════════════════════════════════════════════
        //
        // ★ 왜 결과 씬에서 다시 계산하지 않는가
        //   씬을 넘기면 플레이어·봇 오브젝트가 전부 파괴된다. 즉 결과 씬에는
        //   계산할 재료가 없다. 원본이 룸 프로퍼티에 값을 미리 뿌려둔 것도 같은 이유였다.
        //   여기서는 끝나는 순간 한 번만 보내고, 각자 static에 담아 씬을 넘어간다.
        //   덕분에 결과 씬에서는 소켓이 필요 없다 — 전환 중 끊겨도 결과는 보인다.
        const int MaxStandings = 20;

        void BroadcastStandings(List<LanScoreboard.Entry> list, string winnerName)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost) return;

            int n = Mathf.Min(list.Count, MaxStandings);

            _w.Begin(MsgType.FinalStandings);
            _w.WriteString(winnerName);
            _w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                LanScoreboard.Entry e = list[i];
                _w.WriteString(e.name);
                _w.WriteByte(e.isBot ? (byte)1 : (byte)0);
                _w.WriteInt(e.netId);
                _w.WriteInt(e.ownerId);
                _w.WriteFloat(e.scale);
                _w.WriteInt(e.score);
                _w.WriteFloat(e.color.r);
                _w.WriteFloat(e.color.g);
                _w.WriteFloat(e.color.b);
            }
            _w.End();
            net.Host.Broadcast(_w);
        }

        void ReadStandings(NetReader r)
        {
            string winnerName = r.ReadString();
            int n = r.ReadByte();

            List<LanScoreboard.Entry> list = new List<LanScoreboard.Entry>(n);
            for (int i = 0; i < n; i++)
            {
                LanScoreboard.Entry e;
                e.name = r.ReadString();
                e.isBot = r.ReadByte() != 0;
                e.netId = r.ReadInt();
                e.ownerId = r.ReadInt();
                e.scale = r.ReadFloat();
                e.score = r.ReadInt();
                float cr = r.ReadFloat();
                float cg = r.ReadFloat();
                float cb = r.ReadFloat();
                e.color = new Color(cr, cg, cb, 1f);

                // '내 것'은 받는 쪽에서 판단한다 — 보내는 쪽 기준으로 오면 전부 호스트 것이 된다
                e.isLocal = !e.isBot && e.ownerId == NetManager.Instance.MyId;

                list.Add(e);
            }

            LanScoreboard.SetFinal(list, winnerName);
        }

        void HostSetPhase(GamePhase p)
        {
            SetPhaseLocal(p);

            WritePhase(p, Remaining);
            NetManager.Instance.Host.Broadcast(_w);
        }

        // ═════════════════════════════════════════════
        //  클라이언트: 통보받아 따라간다
        // ═════════════════════════════════════════════
        void HandleClientMessage(MsgType type, NetReader r)
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
                    ReadStandings(r);
                    break;

                // LoadGameScene은 Main 씬에서 받아야 하므로 LanLobby가 처리한다.

                case MsgType.GameOver:
                    {
                        WinnerNetId = r.ReadInt();
                        WinnerScore = r.ReadInt();
                        OnGameOver();
                        break;
                    }
            }
        }

        // ═════════════════════════════════════════════
        void SetPhaseLocal(GamePhase p)
        {
            if (Phase == p) return;
            Phase = p;

            // 기존 UI가 이 이벤트를 구독하고 있다 — 여기만 바꿔주면 UI는 그대로 동작
            GameState.Phase = p;

            // ★ 입력 잠금 — 원본 StartCountdownRoutine의 PlayerMovement.InputLocked를 옮긴 것.
            //   대기·카운트다운·종료 중에는 움직일 수 없고, 진행 중에만 풀린다.
            PlayerMovement.InputLocked = (p != GamePhase.Playing);

            if (NetManager.Instance != null)
                NetManager.Instance.AddLog("게임 단계 → " + p
                    + (PlayerMovement.InputLocked ? "  (입력 잠금)" : "  (입력 해제)"));
        }

        /// <summary>
        /// 남은 시간 표시. 원본 GameModeManager.UpdateGameTimerUI를 옮긴 것.
        /// 밀치기 모드는 시간 제한이 없어 경과 시간을 센다.
        /// </summary>
        void UpdateTimerUI()
        {
            if (gameTimerText == null) return;

            float t = (mode == GameModeType.Push) ? (gameDuration - Remaining) : Remaining;
            if (t < 0f) t = 0f;

            int min = Mathf.FloorToInt(t / 60f);
            int sec = Mathf.FloorToInt(t % 60f);
            gameTimerText.text = min.ToString("00") + ":" + sec.ToString("00");
        }

        /// <summary>
        /// 나 혼자만 보는 게임오버 화면(흡수당함·낙하 등).
        /// 게임 자체는 계속 진행된다 — 원본과 같은 '관전 전환'이다.
        /// </summary>
        public void ShowLocalGameOver(string message)
        {
            if (gameResultPanel != null) gameResultPanel.SetActive(true);
            if (resultTitleText != null) resultTitleText.text = message;

            // 입력 차단 (관전)
            if (PlayerMovement.Local != null) PlayerMovement.Local.enabled = false;

            if (NetManager.Instance != null) NetManager.Instance.AddLog("게임오버: " + message.Replace("\n", " "));
        }

        void OnGameOver()
        {
            NetIdentity w = (NetWorld.Instance != null && WinnerNetId != 0)
                ? NetWorld.Instance.Find(WinnerNetId) : null;

            string who = w != null ? ("P" + w.OwnerId) : "없음";
            NetManager.Instance.AddLog("게임 종료! 승자 " + who + " (점수 " + WinnerScore + ")");

            // 전원에게 최종 결과 창을 띄운다(관전 중이던 사람 포함)
            if (gameResultPanel != null) gameResultPanel.SetActive(true);
            if (resultTitleText != null)
            {
                string winner = LanScoreboard.WinnerName;
                resultTitleText.text = string.IsNullOrEmpty(winner)
                    ? "게임 종료!"
                    : ("승자: " + winner);
            }

            PlayerMovement.InputLocked = true;

            if (autoLoadResultScene)
            {
                _resultPending = true;
                _resultTimer = resultSceneDelay;
            }
        }

        void GoToResultScene()
        {
            string scene = (mode == GameModeType.Push) ? resultScenePush : resultSceneAbsorb;
            if (string.IsNullOrEmpty(scene)) return;

            LanSceneFlow.ToResult(scene);
        }

        /// <summary>"메인으로" 버튼에서 부른다. 게임 도중이든 종료 후든 안전하다.</summary>
        public void GoToMainMenu()
        {
            // ★ 예약된 결과 씬 전환을 취소한다.
            //   게임이 끝나면 resultSceneDelay(3초) 뒤에 결과 씬으로 가도록 예약된다.
            //   그 사이에 "메인으로"를 누르면, Main에 도착한 뒤에 예약이 터져
            //   <b>결과 씬으로 다시 끌려간다.</b>
            _resultPending = false;

            LanSceneFlow.ToMain();
        }

        void WritePhase(GamePhase p, float remaining)
        {
            _w.Begin(MsgType.GamePhaseChange);
            _w.WriteByte((byte)p);
            _w.WriteByte((byte)mode);
            _w.WriteFloat(remaining);
            _w.End();
        }

        // ─────────────────────────────────────────────
        //  집계
        // ─────────────────────────────────────────────
        int CountPlayers()
        {
            if (NetWorld.Instance == null) return 0;
            int n = 0;
            foreach (var kv in NetWorld.Instance.Objects)
                if (kv.Value != null && kv.Value.PrefabId < NetConfig.JellyPrefabStart) n++;
            return n;
        }

        List<NetIdentity> FindAlivePlayers()
        {
            List<NetIdentity> list = new List<NetIdentity>();
            if (NetWorld.Instance == null) return list;

            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity id = kv.Value;
                if (id == null || id.PrefabId >= NetConfig.JellyPrefabStart) continue;

                LanPlayerState ps = id.GetComponent<LanPlayerState>();
                if (ps != null && ps.IsEliminated) continue;   // 흡수는 부활하므로 탈락만 센다

                list.Add(id);
            }
            return list;
        }

        NetIdentity FindTopScorer()
        {
            if (NetWorld.Instance == null) return null;

            NetIdentity best = null;
            int bestScore = int.MinValue;

            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity id = kv.Value;
                if (id == null || id.PrefabId >= NetConfig.JellyPrefabStart) continue;

                LanPlayerState ps = id.GetComponent<LanPlayerState>();
                int s = ps != null ? ps.Score : 0;
                if (s > bestScore) { bestScore = s; best = id; }
            }
            return best;
        }
    }
}
