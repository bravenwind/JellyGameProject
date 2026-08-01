using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 플레이어의 게임 상태(점수·탈락·색·이름). NetworkPlayerSync에서 규칙만 추린 것.
    ///
    /// ★ 권위는 호스트에 있다.
    ///   클라는 스스로 점수를 올리거나 탈락을 해제하지 않는다. 통보받아 반영만 한다.
    ///
    /// ★ 왜 셋을 한 메시지로 묶는가
    ///   점수·탈락·색은 모두 '자주 안 바뀌고, 바뀔 때 대개 함께' 바뀐다.
    ///   (젤리를 먹으면 점수↑ + 색 변화, 흡수당하면 탈락 + 점수 정산)
    ///   따로 보내면 메시지 수만 늘고 얻는 게 없다.
    ///
    /// ★ 원본과 달라진 점
    ///   Photon판은 색을 OnPhotonSerializeView로 <b>매 프레임</b> 보냈다.
    ///   하지만 색이 바뀌는 건 젤리를 먹었을 때뿐이라 그건 낭비였다.
    ///   여기서는 <b>바뀔 때만</b> 보낸다.
    /// </summary>
    public class LanPlayerState : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("색을 칠할 렌더러. 비워두면 자식에서 찾는다.")]
        public Renderer targetRenderer;

        [Tooltip("색이 바뀔 때 부드럽게 전환하는 속도. 0이면 즉시.")]
        public float colorLerpSpeed = 6f;

        // ── 상태 (읽기 전용 공개) ──
        public int Score { get; private set; }
        public Color DisplayColor { get; private set; }
        public string PlayerName { get; private set; }
        public PlayerFlags Flags { get; private set; }

        public bool IsEliminated { get { return (Flags & PlayerFlags.Eliminated) != 0; } }
        public bool IsAbsorbed { get { return (Flags & PlayerFlags.Absorbed) != 0; } }

        /// <summary>탈락했거나 흡수되어 판 밖인 상태. 판정의 단일 출처.</summary>
        public bool IsOutOfPlay { get { return Flags != PlayerFlags.None; } }

        NetIdentity _id;
        Color _shownColor;

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

            DisplayColor = Color.white;
            _shownColor = Color.white;
            PlayerName = "";
        }

        void Update()
        {
            if (targetRenderer == null) return;
            if (_shownColor == DisplayColor) return;

            if (colorLerpSpeed <= 0f) _shownColor = DisplayColor;
            else
            {
                float t = 1f - Mathf.Exp(-colorLerpSpeed * Time.deltaTime);
                _shownColor = Color.Lerp(_shownColor, DisplayColor, t);
            }
            targetRenderer.material.color = _shownColor;
        }

        // ═════════════════════════════════════════════
        //  호스트 전용 — 상태를 바꾸고 전원에게 알린다
        // ═════════════════════════════════════════════
        public void HostAddScore(int delta)
        {
            if (!IsHost()) return;
            Score += delta;
            HostBroadcast();
        }

        public void HostSetColor(Color c)
        {
            if (!IsHost()) return;
            DisplayColor = c;
            HostBroadcast();
        }

        public void HostSetFlag(PlayerFlags flag, bool on)
        {
            if (!IsHost()) return;

            PlayerFlags next = on ? (Flags | flag) : (Flags & ~flag);
            if (next == Flags) return;      // 안 바뀌면 안 보낸다

            Flags = next;
            HostBroadcast();
        }

        /// <summary>흡수 보상을 한 번에 — 점수·색을 함께 바꾸고 메시지는 하나만 보낸다.</summary>
        public void HostReward(int scoreDelta, Color newColor)
        {
            if (!IsHost()) return;
            Score += scoreDelta;
            DisplayColor = newColor;
            HostBroadcast();
        }

        public void HostSetName(string name)
        {
            if (!IsHost() || _id == null) return;
            PlayerName = name ?? "";
            if (NetWorld.Instance != null) NetWorld.Instance.BroadcastPlayerName(_id.NetId, PlayerName);
        }

        void HostBroadcast()
        {
            if (_id == null || NetWorld.Instance == null) return;
            NetWorld.Instance.BroadcastPlayerState(_id.NetId, Score, (byte)Flags, DisplayColor);
        }

        static bool IsHost()
        {
            return NetManager.Instance != null && NetManager.Instance.IsHost;
        }

        // ═════════════════════════════════════════════
        //  수신 적용 (NetWorld가 호출)
        // ═════════════════════════════════════════════
        public void ApplyState(int score, byte flags, Color color)
        {
            Score = score;
            Flags = (PlayerFlags)flags;
            DisplayColor = color;
        }

        public void ApplyName(string name)
        {
            PlayerName = name ?? "";
            gameObject.name = "Player_" + PlayerName + "_net" + (_id != null ? _id.NetId : 0);
        }

        /// <summary>스폰 직후 색을 즉시 맞춘다(전환 연출 없이).</summary>
        public void SnapColorNow()
        {
            _shownColor = DisplayColor;
            if (targetRenderer != null) targetRenderer.material.color = _shownColor;
        }
    }
}
