using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        [Header("결과 씬")]
        [Tooltip("테스트 중에는 꺼둔다. 켜면 종료 후 결과 씬으로 넘어간다.")]
        public bool autoLoadResultScene = false;
        public string resultSceneAbsorb = "GameResult_AbsorbMode";
        public string resultScenePush = "GameResult_PushMode";
        public float resultSceneDelay = 3f;

        // ── 상태 (전원 공유) ──
        public GamePhase Phase { get; private set; }
        public float Remaining { get; private set; }
        public int WinnerNetId { get; private set; }
        public int WinnerScore { get; private set; }

        readonly NetWriter _w = new NetWriter();

        float _countdownLeft;
        float _survivorCheckTimer;
        float _resultTimer;
        bool _resultPending;

        // ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Phase = GamePhase.Loading;
        }

        void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null) { Debug.LogError("[LanGameFlow] NetManager가 없습니다."); return; }

            net.OnHostStarted += HandleHostStarted;
            net.OnPeerJoined += HandlePeerJoined;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ResetAll;
        }

        void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;
            net.OnHostStarted -= HandleHostStarted;
            net.OnPeerJoined -= HandlePeerJoined;
            net.OnClientMessage -= HandleClientMessage;
            net.OnDisconnected -= ResetAll;
        }

        void ResetAll()
        {
            SetPhaseLocal(GamePhase.Loading);
            Remaining = gameDuration;
            WinnerNetId = 0;
            _countdownLeft = 0f;
            _resultPending = false;
        }

        void HandleHostStarted()
        {
            Remaining = gameDuration;
            GameState.CurrentGameMode = mode;
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
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            // 표시용 타이머는 전원이 각자 돌린다(종료 판정은 하지 않는다)
            if (Phase == GamePhase.Playing && mode == GameModeType.Absorb)
                Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);

            if (_resultPending)
            {
                _resultTimer -= Time.deltaTime;
                if (_resultTimer <= 0f) { _resultPending = false; GoToResultScene(); }
            }

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
                    CheckEndCondition();
                    break;
            }
        }

        void TryStartCountdown()
        {
            int players = CountPlayers();

            if (_countdownLeft <= 0f)
            {
                if (players < minPlayersToStart) return;   // 아직 인원 부족
                _countdownLeft = countdownSeconds;
                NetManager.Instance.AddLog("인원 " + players + "명 — " + countdownSeconds + "초 후 시작");
                return;
            }

            _countdownLeft -= Time.deltaTime;
            if (_countdownLeft > 0f) return;

            // 시작!
            Remaining = gameDuration;
            HostSetPhase(GamePhase.Playing);
        }

        void CheckEndCondition()
        {
            if (mode == GameModeType.Absorb)
            {
                if (Remaining > 0f) return;
                HostEndGame(FindTopScorer());
                return;
            }

            // 밀치기 — 생존자 검사는 매 프레임 할 필요가 없다
            _survivorCheckTimer += Time.deltaTime;
            if (_survivorCheckTimer < 0.5f) return;
            _survivorCheckTimer = 0f;

            List<NetIdentity> alive = FindAlivePlayers();
            if (alive.Count > 1) return;

            HostEndGame(alive.Count == 1 ? alive[0] : null);
        }

        void HostEndGame(NetIdentity winner)
        {
            WinnerNetId = winner != null ? winner.NetId : 0;

            LanPlayerState ws = winner != null ? winner.GetComponent<LanPlayerState>() : null;
            WinnerScore = ws != null ? ws.Score : 0;

            _w.Begin(MsgType.GameOver);
            _w.WriteInt(WinnerNetId);
            _w.WriteInt(WinnerScore);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);

            HostSetPhase(GamePhase.GameOver);
            OnGameOver();
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

            if (NetManager.Instance != null)
                NetManager.Instance.AddLog("게임 단계 → " + p);
        }

        void OnGameOver()
        {
            NetIdentity w = (NetWorld.Instance != null && WinnerNetId != 0)
                ? NetWorld.Instance.Find(WinnerNetId) : null;

            string who = w != null ? ("P" + w.OwnerId) : "없음";
            NetManager.Instance.AddLog("게임 종료! 승자 " + who + " (점수 " + WinnerScore + ")");

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

            SceneManager.LoadScene(scene);
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
