using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 인게임 순위표. GameModeManager.UpdateLeaderboard를 옮긴 것.
    ///
    /// ★ 원본과 달라진 점 — '나'를 이름으로 찾지 않는다
    ///   원본은 entry.name == PhotonNetwork.NickName으로 본인 행을 찾았다.
    ///   닉네임이 겹치면 남의 행이 내 것으로 하이라이트된다(원본 주석도 이걸 알고 있었다).
    ///   여기서는 NetIdentity.IsMine을 그대로 들고 다니므로 그 문제가 없다.
    /// </summary>
    public class LanLeaderboardUI : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("순위 행들이 들어갈 부모. 기존 GameModeManager의 것을 그대로 쓰면 된다.")]
        public Transform container;

        [Tooltip("한 줄 프리팹(LeaderboardEntry 보유).")]
        public GameObject entryPrefab;

        [Header("표시")]
        [Tooltip("몇 등까지 보여줄지.")]
        public int displayCount = 5;

        [Tooltip("초당 몇 번 갱신할지. 매 프레임 정렬할 이유가 없다.")]
        public float refreshRate = 4f;

        readonly List<LeaderboardEntry> _rows = new List<LeaderboardEntry>();
        ObjectPool<LeaderboardEntry> _pool;
        float _timer;

        void Start()
        {
            if (entryPrefab == null || container == null) return;

            LeaderboardEntry proto = entryPrefab.GetComponent<LeaderboardEntry>();
            if (proto == null)
            {
                Debug.LogWarning("[순위표] entryPrefab에 LeaderboardEntry가 없습니다.");
                return;
            }
            _pool = new ObjectPool<LeaderboardEntry>(proto, container, displayCount);
        }

        void Update()
        {
            if (_pool == null) return;

            _timer += Time.deltaTime;
            if (_timer < 1f / refreshRate) return;
            _timer = 0f;

            Refresh();
        }

        void Refresh()
        {
            List<LanScoreboard.Entry> entries = LanScoreboard.Collect();

            _pool.ReturnAll(_rows);
            _rows.Clear();

            int n = Mathf.Min(entries.Count, displayCount);

            // ★ 내가 순위권 밖이면 마지막 칸을 내 행으로 바꾼다.
            //   내 순위가 아예 안 보이면 순위표가 무슨 의미인지 알 수 없다.
            int myRank = -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].isLocal) { myRank = i; break; }

            bool meOutside = myRank >= n;

            for (int i = 0; i < n; i++)
            {
                bool lastSlot = (i == n - 1);
                int src = (meOutside && lastSlot) ? myRank : i;
                LanScoreboard.Entry e = entries[src];

                LeaderboardEntry row = _pool.Get();
                row.transform.SetAsLastSibling();
                _rows.Add(row);

                row.Setup(src + 1, e.name, e.score, e.isLocal, e.color);
            }
        }
    }
}
