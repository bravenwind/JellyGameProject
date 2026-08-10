using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
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

        private readonly List<LeaderboardEntry> rows = new List<LeaderboardEntry>();
        private ComponentPool<LeaderboardEntry> pool;
        private float timer;

        private void Start()
        {
            if (container == null || entryPrefab == null)
            {
                Debug.LogWarning("[순위표] LanLeaderboardUI의 Container / Entry Prefab이 비어 있습니다. "
                    + "기존 GameModeManager의 leaderboardContainer·leaderboardEntryPrefab을 연결해주세요.");
                return;
            }

            LeaderboardEntry proto = entryPrefab.GetComponent<LeaderboardEntry>();
            if (proto == null)
            {
                Debug.LogWarning("[순위표] entryPrefab에 LeaderboardEntry가 없습니다.");
                return;
            }
            pool = new ComponentPool<LeaderboardEntry>(proto, container, displayCount);
        }

        private void Update()
        {
            if (pool == null)
                return;

            timer += Time.deltaTime;
            if (timer < 1f / refreshRate)
                return;
            timer = 0f;

            Refresh();
        }

        private void Refresh()
        {
            List<LanScoreboard.Entry> entries = LanScoreboard.Collect();

            pool.ReturnAll(rows);
            rows.Clear();

            int n = Mathf.Min(entries.Count, displayCount);

            int myRank = -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].isLocal)
                {
                    myRank = i;
                    break;
                }

            bool meOutside = myRank >= n;

            for (int i = 0; i < n; i++)
            {
                bool lastSlot = (i == n - 1);
                int src = (meOutside && lastSlot) ? myRank : i;
                LanScoreboard.Entry e = entries[src];

                LeaderboardEntry row = pool.Get();
                row.transform.SetAsLastSibling();
                rows.Add(row);

                row.Setup(src + 1, e.name, e.score, e.isLocal, e.color);
            }
        }
    }
}
