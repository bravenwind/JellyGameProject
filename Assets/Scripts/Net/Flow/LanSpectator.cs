using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace JellyNet
{
    //탈락 후 남은 참가자를 따라다니는 관전 카메라
    //대상이 도중에 죽어도 알아서 넘기지 않는다. 화살표를 눌렀을 때만 옮긴다
    public class LanSpectator : MonoBehaviour
    {
        public static LanSpectator Instance { get; private set; }

        [Header("UI")]
        [Tooltip("← 이름 → 묶음. 관전 중에만 켜진다.")]
        [SerializeField] private GameObject bar;

        [Tooltip("지금 보고 있는 참가자 이름.")]
        [SerializeField] private TextMeshProUGUI targetNameText;

        [Header("표시")]
        [SerializeField] private string deadSuffix = " (탈락)";
        [SerializeField] private string noTargetLabel = "남은 참가자 없음";

        public bool IsSpectating { get; private set; }

        private int killerNetId;
        private int currentNetId;
        private string currentName = "";

        private TopDownCameraFollow topDown;
        private MainCamera_Action camAction;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (bar != null)
                bar.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        //나를 쓰러뜨린 상대. 관전을 시작할 때 첫 화면이 된다
        public static void ReportKiller(int netId)
        {
            if (Instance != null)
                Instance.killerNetId = netId;
        }

        public void Begin()
        {
            IsSpectating = true;

            if (bar != null)
                bar.SetActive(true);

            List<LanScoreboard.Entry> alive = LanScoreboard.Collect();

            int index = IndexOf(alive, killerNetId);

            //죽인 상대가 이미 탈락했거나 낙사처럼 가해자가 없는 경우
            if (index < 0)
                index = alive.Count > 0 ? 0 : -1;

            if (index < 0)
            {
                currentNetId = 0;
                currentName = "";
                Refresh(false);
                return;
            }

            Focus(alive[index]);
        }

        public void Stop()
        {
            IsSpectating = false;

            if (bar != null)
                bar.SetActive(false);
        }

        public void OnClick_Prev()
        {
            Step(-1);
        }

        public void OnClick_Next()
        {
            Step(1);
        }

        private void Step(int dir)
        {
            if (!IsSpectating)
                return;

            //살아 있는 사람만 후보다. 지금 보던 대상이 죽었으면 목록에 없다
            List<LanScoreboard.Entry> alive = LanScoreboard.Collect();

            if (alive.Count == 0)
                return;

            int index = IndexOf(alive, currentNetId);

            if (index < 0)
                index = dir > 0 ? 0 : alive.Count - 1;
            else
                index = ((index + dir) % alive.Count + alive.Count) % alive.Count;

            Focus(alive[index]);
        }

        private void Focus(LanScoreboard.Entry entry)
        {
            currentNetId = entry.netId;
            currentName = entry.name;

            NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(currentNetId) : null;

            if (id != null)
                PointCameraAt(id.transform);

            Refresh(true);
        }

        private void PointCameraAt(Transform target)
        {
            if (topDown == null)
                topDown = FindFirstObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);

            if (camAction == null)
                camAction = FindFirstObjectByType<MainCamera_Action>(FindObjectsInactive.Include);

            if (topDown != null)
                topDown.Target = target;

            if (camAction != null)
                camAction.SetTarget(target);
        }

        private void Refresh(bool hasTarget)
        {
            if (targetNameText == null)
                return;

            if (!hasTarget)
            {
                targetNameText.text = noTargetLabel;
                return;
            }

            targetNameText.text = currentName + (IsCurrentAlive() ? "" : deadSuffix);
        }

        //관전 중에 대상이 탈락하면 이름 뒤에 표시만 바꾼다. 카메라는 그대로 둔다
        private float refreshTimer;

        private void Update()
        {
            if (!IsSpectating || currentNetId == 0)
                return;

            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < 0.5f)
                return;
            refreshTimer = 0f;

            Refresh(true);
        }

        private bool IsCurrentAlive()
        {
            NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(currentNetId) : null;

            return id != null && !NetEntity.IsOutOfPlay(id);
        }

        private static int IndexOf(List<LanScoreboard.Entry> list, int netId)
        {
            if (netId == 0)
                return -1;

            for (int i = 0; i < list.Count; i++)
                if (list[i].netId == netId)
                    return i;

            return -1;
        }
    }
}
