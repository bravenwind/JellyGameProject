using UnityEngine;

namespace JellyNet
{
    [RequireComponent(typeof(NetIdentity))]
    public class LanPlayerSetup : MonoBehaviour
    {
        [Header("연결 (비우면 자동 탐색)")]
        public PlayerMovement playerController;
        public NameTagBillboard nameTagBillboard;

        private NetIdentity id;
        private bool applied;

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            if (playerController == null)
                playerController = GetComponentInChildren<PlayerMovement>(true);
            if (nameTagBillboard == null)
                nameTagBillboard = GetComponentInChildren<NameTagBillboard>(true);
        }

        public void Apply()
        {
            if (applied)
                return;
            applied = true;

            if (id.IsMine)
                SetupLocal();
            else
                SetupRemote();

            ApplyBatVisibility();
        }

        private void SetupLocal()
        {
            TopDownCameraFollow cam = FindFirstObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);
            if (cam != null)
                cam.target = transform;

            MainCamera_Action camAction = FindFirstObjectByType<MainCamera_Action>(FindObjectsInactive.Include);
            if (camAction != null)
                camAction.SetTarget(transform);

            if (cam == null && camAction == null)
                Debug.LogWarning("[LanSetup] 씬에 카메라 추적 컴포넌트가 없습니다 — 카메라가 안 따라갑니다.");

            if (playerController != null)
                playerController.MarkAsLocal();

            foreach (UIFollowTarget f in FindObjectsByType<UIFollowTarget>(FindObjectsSortMode.None))
                f.SetTarget(transform);

            if (nameTagBillboard != null)
            {
                nameTagBillboard.SetName("나");
                nameTagBillboard.ApplyRoleColor(NameTagRole.LocalPlayer);
            }

            Debug.Log("[LanSetup] 로컬 플레이어 초기화 (net" + id.NetId + ")");
        }

        private void SetupRemote()
        {
            if (playerController != null)
                playerController.enabled = false;

            PlayerAbsorber absorber = GetComponentInChildren<PlayerAbsorber>(true);
            if (absorber != null)
                absorber.enabled = false;

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            SoftBody3D softBody = GetComponentInChildren<SoftBody3D>(true);
            if (softBody != null)
                softBody.RemoveCloth();

            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null)
                listener.enabled = false;

            if (nameTagBillboard != null)
            {
                LanPlayerState ps = GetComponent<LanPlayerState>();
                string nm = (ps != null && !string.IsNullOrEmpty(ps.PlayerName)) ? ps.PlayerName : ("P" + id.OwnerId);
                nameTagBillboard.SetName(nm);
                nameTagBillboard.ApplyRoleColor(NameTagRole.RemotePlayer);
            }

            Debug.Log("[LanSetup] 원격 플레이어 초기화 (net" + id.NetId + ", 소유 P" + id.OwnerId + ")");
        }

        private void ApplyBatVisibility()
        {
            if (playerController == null || playerController.batPivot == null)
                return;

            bool pushMode = LanGameFlow.IsMode(GameModeType.Push);
            playerController.batPivot.gameObject.SetActive(pushMode && !playerController.hideBatWhenIdle);
        }
    }
}
