using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 스폰 직후 "내 캐릭터인가 남의 캐릭터인가"에 따라 컴포넌트를 켜고 끈다.
    /// 원본 NetworkPlayerSync.SetupLocalPlayer / SetupRemotePlayer를 그대로 옮긴 것.
    ///
    /// ★ 이게 없으면 벌어지는 일 (실제로 겪음)
    ///   · 카메라가 내 캐릭터를 안 따라간다
    ///   · 원격 캐릭터도 내 입력에 반응한다
    ///   · 원격 캐릭터의 PlayerAbsorber가 켜져 있어 크기가 두 번 커진다
    ///   · 원격 캐릭터의 CharacterController가 물리를 돌려 받은 위치와 싸운다
    ///   · Cloth(SoftBody3D)가 스케일 동기화를 방해한다
    ///
    /// ★ 호출 시점이 중요하다
    ///   Awake/Start에서 하면 안 된다. 그때는 아직 NetIdentity.OwnerId가 0이라
    ///   IsMine이 항상 false다. NetWorld가 소유자를 확정한 뒤 Apply()를 부른다.
    /// </summary>
    [RequireComponent(typeof(NetIdentity))]
    public class LanPlayerSetup : MonoBehaviour
    {
        [Header("연결 (비우면 자동 탐색)")]
        public PlayerMovement playerController;
        public NameTagBillboard nameTagBillboard;

        NetIdentity _id;
        bool _applied;

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            if (playerController == null) playerController = GetComponentInChildren<PlayerMovement>(true);
            if (nameTagBillboard == null) nameTagBillboard = GetComponentInChildren<NameTagBillboard>(true);
        }

        /// <summary>NetWorld가 소유자를 확정한 직후 호출한다.</summary>
        public void Apply()
        {
            if (_applied) return;
            _applied = true;

            if (_id.IsMine) SetupLocal();
            else SetupRemote();

            ApplyBatVisibility();
        }

        // ─────────────────────────────────────────────
        void SetupLocal()
        {
            // ★ 카메라 추적 컴포넌트는 Camera.main에 안 붙어 있을 수도 있다(실제로 그랬다).
            //   그래서 씬 전체에서 찾는다.
            TopDownCameraFollow cam = FindFirstObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);
            if (cam != null) cam.target = transform;

            MainCamera_Action camAction = FindFirstObjectByType<MainCamera_Action>(FindObjectsInactive.Include);
            if (camAction != null) camAction.SetTarget(transform);

            if (cam == null && camAction == null)
                Debug.LogWarning("[LanSetup] 씬에 카메라 추적 컴포넌트가 없습니다 — 카메라가 안 따라갑니다.");

            // HUD(대쉬 쿨타임 등)가 내 캐릭터를 읽을 수 있게
            if (playerController != null) playerController.MarkAsLocal();

            // 화면에 붙어 다니는 UI들도 나를 따라오게
            foreach (UIFollowTarget f in FindObjectsByType<UIFollowTarget>(FindObjectsSortMode.None))
                f.SetTarget(transform);

            if (nameTagBillboard != null)
            {
                nameTagBillboard.SetName("나");
                nameTagBillboard.ApplyRoleColor(NameTagRole.LocalPlayer);
            }

            Debug.Log("[LanSetup] 로컬 플레이어 초기화 (net" + _id.NetId + ")");
        }

        void SetupRemote()
        {
            // ① 입력 차단 — 남의 캐릭터가 내 키보드에 반응하면 안 된다
            if (playerController != null) playerController.enabled = false;

            // ② 흡수 '감지'만 차단한다.
            //
            //   PlayerAbsorber는 OnTriggerEnter로 젤리를 스스로 집는다.
            //   원격에서 그게 돌면 남의 캐릭터가 내 화면에서 멋대로 먹으므로 끈다.
            //   (AbsorbColor는 메서드 직접 호출이라 컴포넌트가 꺼져 있어도 동작한다)
            PlayerAbsorber absorber = GetComponentInChildren<PlayerAbsorber>(true);
            if (absorber != null) absorber.enabled = false;

            // ★ PlayerAbsorbingManager는 절대 끄면 안 된다.
            //   OnDisable에서 OnJellyEaten 구독을 해제해버려, 호스트가 확정한 흡수 결과
            //   (색·성장)가 원격 화면에 아예 반영되지 않는다. 실제로 이것 때문에
            //   "다른 플레이어는 크기·색이 그대로"인 증상이 났다.
            //
            //   Photon판은 크기를 절대값으로 따로 동기화해서 이걸 꺼야 했지만,
            //   우리는 '사건을 전원이 재생'하는 방식이라 켜둔 채로 두는 게 맞다.

            // ③ 물리 차단 — 원격은 받은 위치를 그대로 쓴다. 물리가 돌면 서로 싸운다
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // ④ Cloth 제거 — 스케일·애니메이션 동기화를 방해한다(원본 주석 그대로)
            SoftBody3D softBody = GetComponentInChildren<SoftBody3D>(true);
            if (softBody != null) softBody.RemoveCloth();

            // ⑤ AudioListener 끄기 — 플레이어 프리팹에 달려 있어서 인원수만큼 늘어난다.
            //    "There are 2 audio listeners in the scene" 경고의 원인. 내 것만 남긴다.
            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = false;

            if (nameTagBillboard != null)
            {
                LanPlayerState ps = GetComponent<LanPlayerState>();
                string nm = (ps != null && !string.IsNullOrEmpty(ps.PlayerName)) ? ps.PlayerName : ("P" + _id.OwnerId);
                nameTagBillboard.SetName(nm);
                nameTagBillboard.ApplyRoleColor(NameTagRole.RemotePlayer);
            }

            Debug.Log("[LanSetup] 원격 플레이어 초기화 (net" + _id.NetId + ", 소유 P" + _id.OwnerId + ")");
        }

        /// <summary>배트는 밀치기 모드에서만 보인다(원본 ApplyBatModeVisibility).</summary>
        void ApplyBatVisibility()
        {
            if (playerController == null || playerController.batPivot == null) return;

            bool pushMode = LanGameFlow.IsMode(GameModeType.Push);
            playerController.batPivot.gameObject.SetActive(pushMode && !playerController.hideBatWhenIdle);
        }
    }
}
