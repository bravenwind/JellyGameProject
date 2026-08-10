using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JellyNet
{
    public class LanRoomListUI : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("방 항목들이 들어갈 부모(보통 Scroll View의 Content).")]
        public Transform container;

        [Tooltip("한 줄 프리팹(LanRoomRow 보유).")]
        public GameObject rowPrefab;

        [Tooltip("목록이 비었을 때 보여줄 안내.")]
        public GameObject emptyHint;

        public TMP_Text statusText;

        [Header("표시")]
        [Tooltip("초당 몇 번 갱신할지. 방 정보는 1초에 한 번 오므로 낮아도 된다.")]
        public float refreshRate = 4f;

        private readonly List<LanRoomRow> rows = new List<LanRoomRow>();
        private float timer;
        private int lastCount = -1;

        private void OnEnable()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StartListening();
            lastCount = -1;
            Refresh();
        }

        private void OnDisable()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StopListening();
            ClearRows();
        }

        private void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer < 1f / refreshRate)
                return;
            timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            LanDiscovery d = LanDiscovery.Instance;
            if (d == null || container == null || rowPrefab == null)
                return;

            List<LanDiscovery.RoomInfo> list = new List<LanDiscovery.RoomInfo>(d.Rooms);

            if (list.Count == lastCount && rows.Count == list.Count)
            {
                for (int i = 0; i < list.Count; i++) rows[i].Setup(list[i], OnPick);
                UpdateHint(list.Count);
                return;
            }

            ClearRows();

            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = Instantiate(rowPrefab, container);
                LanRoomRow row = go.GetComponent<LanRoomRow>();
                if (row == null)
                {
                    Destroy(go);
                    continue;
                }

                row.Setup(list[i], OnPick);
                rows.Add(row);
            }

            lastCount = list.Count;
            UpdateHint(list.Count);
        }

        private void UpdateHint(int count)
        {
            if (emptyHint != null)
                emptyHint.SetActive(count == 0);

            if (statusText != null)
            {
                statusText.text = count > 0
                    ? (count + "개의 방을 찾았습니다.")
                    : "같은 와이파이의 방을 찾는 중…  안 보이면 아래에 주소를 직접 입력하세요.";
            }
        }

        private void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null)
                    Destroy(rows[i].gameObject);
            rows.Clear();
            lastCount = -1;
        }

        private void OnPick(LanDiscovery.RoomInfo r)
        {
            if (LanLobby.Instance != null)
                LanLobby.Instance.JoinRoom(r.Ip, r.Port);
        }
    }
}
