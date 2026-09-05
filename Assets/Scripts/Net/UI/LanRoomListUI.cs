using System.Collections.Generic;
using UnityEngine;

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

        [Tooltip("아직 찾는 중일 때 보여줄 안내. TMP_Text 에 WaitingDots 를 붙여두면 점이 늘어난다.")]
        [SerializeField] private GameObject searchingHint;

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

        //매 갱신마다 새로 만들면 초당 4개씩 쓰레기가 쌓인다. 담을 그릇은 하나만 둔다
        private readonly List<RoomHandle> list = new List<RoomHandle>();

        private float timer;
        private int lastCount = -1;
        private float searchElapsed;

        private static INetSession Session
        {
            get { return NetManager.Instance != null ? NetManager.Instance.Session : null; }
        }

        private void OnEnable()
        {
            INetSession s = Session;
            if (s != null)
            {
                s.StartBrowsing();
                s.OnRoomListChanged += Refresh;
            }

            lastCount = -1;
            searchElapsed = 0f;
            Refresh();
        }

        private void OnDisable()
        {
            INetSession s = Session;
            if (s != null)
            {
                s.OnRoomListChanged -= Refresh;
                s.StopBrowsing();
            }

            ClearRows();
        }

        //바뀔 때마다 OnRoomListChanged로 곧바로 그리지만, 주기 갱신도 남겨둔다.
        //Refresh는 몇 번을 불러도 같은 결과라 겹쳐도 문제가 없고,
        //알림이 오지 않는 경우에도 목록이 굳지 않는다
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
            INetSession s = Session;
            if (s == null || container == null || rowPrefab == null)
                return;

            list.Clear();
            foreach (RoomHandle r in s.Rooms)
                list.Add(r);

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

            // ★ 빈 화면과 '아직 찾는 중'은 다르다
            //   예전엔 이 구간에 아무것도 안 띄웠다. 방이 없는 것도 아니고 찾는 것도
            //   아닌 그냥 빈 화면이라, 사용자에게는 목록이 멈춘 것처럼 보인다.
            //   방이 하나라도 잡히면 목록이 곧 답이므로 안내를 내린다.
            if (searchingHint != null)
                searchingHint.SetActive(count == 0 && stillSearching);
        }

        private void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null)
                    Destroy(rows[i].gameObject);
            rows.Clear();
            lastCount = -1;
        }

        private void OnPick(RoomHandle r)
        {
            if (LanLobby.Instance != null)
                LanLobby.Instance.JoinRoom(r);
        }
    }
}
