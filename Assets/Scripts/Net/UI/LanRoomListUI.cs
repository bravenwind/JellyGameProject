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
        [SerializeField] private Transform container;

        [Tooltip("한 줄 프리팹(LanRoomRow 보유).")]
        [SerializeField] private GameObject rowPrefab;

        [Tooltip("목록이 비었을 때 보여줄 안내.")]
        [SerializeField] private GameObject emptyHint;

        [SerializeField] private TMP_Text statusText;

        [Header("표시")]
        [Tooltip("초당 몇 번 갱신할지. 방 정보는 1초에 한 번 오므로 낮아도 된다.")]
        [SerializeField] private float refreshRate = 4f;

        // ★ 방이 있어도 '비었습니다'가 먼저 번쩍이던 문제
        //   방장은 beaconInterval(1초)마다 한 번씩 자기를 알린다. 목록 화면을 연 직후에는
        //   아직 그 신호가 한 번도 안 왔으므로 목록이 비어 있는 게 정상이다.
        //   그걸 곧바로 '없음'으로 단정해서, 실제로 방이 있는데도 안내가 떴다가
        //   1초 뒤 방이 나타나며 사라지는 깜빡임이 생겼다.
        //   비콘 주기보다 넉넉히 기다린 뒤에야 '없다'고 말한다.
        [Tooltip("이 시간 동안은 '방 없음' 안내를 띄우지 않는다. 방장 알림 주기(1초)보다 길어야 한다.")]
        [SerializeField] private float emptyHintDelay = 2.5f;

        private readonly List<LanRoomRow> rows = new List<LanRoomRow>();
        private float timer;
        private int lastCount = -1;
        private float searchElapsed;

        private void OnEnable()
        {
            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StartListening();
            lastCount = -1;
            searchElapsed = 0f;
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
            searchElapsed += Time.unscaledDeltaTime;

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
            //아직 찾는 중이면 '없다'고 말하지 않는다
            bool stillSearching = searchElapsed < emptyHintDelay;

            if (emptyHint != null)
                emptyHint.SetActive(count == 0 && !stillSearching);

            if (statusText == null)
                return;

            if (count > 0)
                statusText.text = count + "개의 방을 찾았습니다.";
            else if (stillSearching)
                statusText.text = "방을 찾는 중…";
            else
                statusText.text = "같은 와이파이의 방을 찾는 중…  방장과 같은 공유기에 연결되어 있어야 합니다.";
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
