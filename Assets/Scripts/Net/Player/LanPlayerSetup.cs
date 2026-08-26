using UnityEngine;

namespace JellyNet
{
    [RequireComponent(typeof(NetIdentity))]
    public class LanPlayerSetup : MonoBehaviour
    {
        [Header("연결 (비우면 자동 탐색)")]
        [SerializeField] private PlayerMovement playerController;
        [SerializeField] private NameTagBillboard nameTagBillboard;

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
                cam.Target = transform;

            MainCamera_Action camAction = FindFirstObjectByType<MainCamera_Action>(FindObjectsInactive.Include);
            if (camAction != null)
                camAction.SetTarget(transform);

            if (cam == null && camAction == null)
                Debug.LogWarning("[LanSetup] 씬에 카메라 추적 컴포넌트가 없습니다 — 카메라가 안 따라갑니다.");

            if (playerController != null)
                playerController.MarkAsLocal();

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
                //Apply()는 스폰 메시지를 처리하는 '도중에' 동기적으로 불린다. 이름은
                //PlayerNameSet이라는 별도 메시지로 그 뒤에 오므로 여기서 PlayerName을
                //읽어봐야 항상 Awake가 넣은 ""다. 그래서 임시 이름을 짓지 않고 비운다.
                //  비우지 않으면 프리팹에 박힌 플레이스홀더 텍스트가 그대로 노출된다.
                //진짜 닉네임은 LanPlayerState.SetName이 도착하는 대로 채운다
                nameTagBillboard.SetName("");
                nameTagBillboard.ApplyRoleColor(NameTagRole.RemotePlayer);
            }

            Debug.Log("[LanSetup] 원격 플레이어 초기화 (net" + id.NetId + ", 소유 P" + id.OwnerId + ")");
        }

        private void ApplyBatVisibility()
        {
            if (playerController == null || playerController.BatPivot == null)
                return;

            bool pushMode = LanGameFlow.IsMode(GameModeType.Push);
            playerController.BatPivot.gameObject.SetActive(pushMode && !playerController.HideBatWhenIdle);
        }
    }
}
