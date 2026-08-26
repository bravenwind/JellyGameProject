using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class LanLeaderboardUI : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("순위 행들이 들어갈 부모.")]
        [SerializeField] private Transform container;

        [Tooltip("한 줄 프리팹(LanLeaderboardRow 보유).")]
        [SerializeField] private GameObject entryPrefab;

        [Header("표시")]
        [Tooltip("몇 등까지 보여줄지.")]
        [SerializeField] private int displayCount = 5;

        [Tooltip("초당 몇 번 갱신할지. 매 프레임 정렬할 이유가 없다.")]
        [SerializeField] private float refreshRate = 4f;

        private readonly List<LanLeaderboardRow> rows = new List<LanLeaderboardRow>();
        private ComponentPool<LanLeaderboardRow> pool;
        private float timer;

        private void Start()
        {
            if (container == null || entryPrefab == null)
            {
                Debug.LogWarning("[순위표] LanLeaderboardUI의 Container / Entry Prefab이 비어 있습니다.");
                return;
            }

            LanLeaderboardRow proto = entryPrefab.GetComponent<LanLeaderboardRow>();
            if (proto == null)
            {
                Debug.LogWarning("[순위표] entryPrefab에 LanLeaderboardRow가 없습니다.");
                return;
            }
            pool = new ComponentPool<LanLeaderboardRow>(proto, container, displayCount);
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

                LanLeaderboardRow row = pool.Get();
                row.transform.SetAsLastSibling();
                rows.Add(row);

                row.Setup(src + 1, e.name, e.score, e.isLocal, e.color);
            }
        }
    }
}
