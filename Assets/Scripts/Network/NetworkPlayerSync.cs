// ============================================================
// NetworkPlayerSync.cs
// ============================================================
// 역할: 플레이어 네트워크 동기화 + 흡수 판정
//
// 동기화 분담:
//   - 위치/회전 → PhotonTransformView
//   - 애니메이션 → PhotonAnimatorView
//   - 스케일/점수 → CustomProperties (State Sync)
//   - 색상 → IPunObservable 스트림
//   - 흡수 판정 → RPC (MasterClient 검증)
// ============================================================

using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon; // Hashtable (커스텀 프로퍼티) 사용을 위해

[RequireComponent(typeof(PhotonView))]
public class NetworkPlayerSync : MonoBehaviourPun, IPunObservable
// MonoBehaviourPun = PhotonView를 자동으로 캐싱해주는 MonoBehaviour
// IPunObservable = OnPhotonSerializeView() 구현 강제 → 연속 데이터 동기화
{
    // ─────────────────────────────────────────────────────────
    // 참조 (기존 플레이어 컴포넌트들)
    // ─────────────────────────────────────────────────────────
    [Header("기존 플레이어 컴포넌트 연결")]
    public PlayerMovement playerController;
    public PlayerAbsorber playerAbsorber;
    public PlayerColorVisual colorVisual;
    public PlayerScaleController scaleController;
    public Renderer jellyRenderer;

    [Header("이름표")]
    public NameTagBillboard nameTagBillboard;

    // ─────────────────────────────────────────────────────────
    // 원격 플레이어 보간용 변수
    // ─────────────────────────────────────────────────────────
    private Color _networkColor = Color.white;
    public Color DisplayColor => photonView.IsMine ? GameState.CurrentDisplayColor : _networkColor;

    private const float LerpSpeed = 10f;
    private const float ColorDarkenFactor = 0.85f;
    private const float BaseColor02Lightness = 0.6f;

    // ─────────────────────────────────────────────────────────
    // 생존 모드: 플레이어 흡수 관련
    // ─────────────────────────────────────────────────────────
    [Header("생존 모드 설정")]
    [Tooltip("내가 흡수되었을 때 리스폰까지 걸리는 시간 (초)")]
    public float respawnDelay = 3f;

    private bool _isAbsorbed = false;   // 현재 흡수된 상태인지
    private System.Collections.Generic.HashSet<int> _absorbedBotIds = new System.Collections.Generic.HashSet<int>();

    public bool IsAbsorbed => _isAbsorbed;

    // ─────────────────────────────────────────────────────────
    // 레지스트리 등록
    // ─────────────────────────────────────────────────────────
    private void OnEnable() => EntityRegistry.Register(this);
    private void OnDisable() => EntityRegistry.Unregister(this);

    private void Awake()
    {
        if (jellyRenderer == null)
            jellyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        if (jellyRenderer == null)
            jellyRenderer = GetComponentInChildren<Renderer>();
    }

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────
    private void Start()
    {
        if (photonView.IsMine)
        {
            SetupLocalPlayer();
        }
        else
        {
            SetupRemotePlayer();
        }

        // 배트는 Push 모드에서만 활성화 (로컬/원격 공통)
        ApplyBatModeVisibility();

        // 이름표 설정
        if (nameTagBillboard != null)
        {
            string displayName = photonView.Owner?.NickName ?? "???";
            nameTagBillboard.SetName(displayName);

            NameTagRole role = (photonView.IsMine) ? NameTagRole.LocalPlayer : NameTagRole.RemotePlayer;
            nameTagBillboard.ApplyRoleColor(role);
        }
    }

    private void SetupLocalPlayer()
    {
        TopDownCameraFollow cam = Camera.main?.GetComponent<TopDownCameraFollow>();
        if (cam != null) cam.target = transform;

        MainCamera_Action camAction = Camera.main?.GetComponent<MainCamera_Action>();
        if (camAction != null) camAction.SetTarget(transform);

        GameModeManager.Instance?.RegisterLocalPlayer(this);

        // HUD(대쉬 쿨타임 UI 등)가 내 캐릭터의 쿨타임을 읽을 수 있게 로컬 플레이어로 표시
        if (playerController != null) playerController.MarkAsLocal();

        // 이전 게임의 stale 탈락 플래그 제거 (리더보드 누락 / 라스트맨 조기 종료 방지)
        ClearEliminatedFlag();

        foreach (var uiFollow in FindObjectsByType<UIFollowTarget>(FindObjectsSortMode.None))
        {
            uiFollow.SetTarget(transform);
        }

        SyncScore(0);

        if (scaleController != null)
            scaleController.OnScaleValueChanged += OnLocalScaleChanged;

        Debug.Log($"[Network] 로컬 플레이어 초기화: {PhotonNetwork.NickName}");
    }

    private void OnLocalScaleChanged(float newScale)
    {
        SyncScale();
    }

    /// <summary>
    /// 배트(빠따)는 Push 모드에서만 활성화한다. 그 외 모드에서는 완전히 숨긴다.
    /// Push 모드에서도 hideBatWhenIdle이면 평상시 숨겨두고 공격 스윙 때만 표시한다.
    /// </summary>
    private void ApplyBatModeVisibility()
    {
        if (playerController == null || playerController.batPivot == null) return;
        bool pushMode = GameState.CurrentGameMode == GameModeType.Push;
        playerController.batPivot.gameObject.SetActive(pushMode && !playerController.hideBatWhenIdle);
    }

    private void OnDestroy()
    {
        if (scaleController != null)
            scaleController.OnScaleValueChanged -= OnLocalScaleChanged;
    }

    private void SetupRemotePlayer()
    {
        // 원격 플레이어는 PlayerController(입력) 비활성화
        if (playerController != null) playerController.enabled = false;

        // 원격 플레이어는 로컬 흡수 처리 비활성화
        // (PlayerAbsorber가 켜져 있으면 이 클라이언트에서도 GrowByJelly()가 발동해 스케일 충돌 발생)
        PlayerAbsorber absorber = GetComponentInChildren<PlayerAbsorber>();
        if (absorber != null) absorber.enabled = false;
        PlayerAbsorbingManager absorbMgr = GetComponentInChildren<PlayerAbsorbingManager>();
        if (absorbMgr != null) absorbMgr.enabled = false;

        // 원격 플레이어는 직접 물리 연산 필요 없음
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // 원격 플레이어는 Cloth 완전 제거 (스케일/애니메이션 동기화 보장 + updateWhenOffscreen으로 그림자 유지)
        SoftBody3D softBody = GetComponentInChildren<SoftBody3D>();
        if (softBody != null) softBody.RemoveCloth();

        Debug.Log($"[Network] 원격 플레이어 초기화: {photonView.Owner?.NickName}");
    }

    // ─────────────────────────────────────────────────────────
    // Update: 원격 플레이어 보간 처리
    // ─────────────────────────────────────────────────────────
    private void Update()
    {
        if (photonView.IsMine)
            return;
        if (_isAbsorbed) return;

        // 스케일: CustomProperties에서 읽어 Lerp (권위적 소스)
        float targetScale = GetAuthorityScale(photonView);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * targetScale, Time.deltaTime * LerpSpeed);

        if (jellyRenderer != null)
        {
            if (!jellyRenderer.enabled)
                jellyRenderer.enabled = true;

            Color displayColor = _networkColor;
            displayColor.a = 1f;

            Color baseColor = new Color(
                displayColor.r * ColorDarkenFactor,
                displayColor.g * ColorDarkenFactor,
                displayColor.b * ColorDarkenFactor, 1f);
            Color baseColor02 = Color.Lerp(baseColor, Color.white, BaseColor02Lightness);

            jellyRenderer.material.SetColor("_BaseColor_01", baseColor);
            jellyRenderer.material.SetColor("_BaseColor_02", baseColor02);
            jellyRenderer.material.SetColor("_FresnelColor", displayColor);
        }
    }

    // ─────────────────────────────────────────────────────────
    // IPunObservable: 색상만 스트림 전송
    // (위치/회전 = PhotonTransformView, 스케일 = CustomProperties)
    // (애니메이션 = PhotonAnimatorView)
    // ─────────────────────────────────────────────────────────
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.InRoom) return;

        if (stream.IsWriting)
        {
            Color myColor = GameState.CurrentDisplayColor;
            stream.SendNext(myColor.r);
            stream.SendNext(myColor.g);
            stream.SendNext(myColor.b);
            stream.SendNext(myColor.a);
        }
        else
        {
            float r = (float)stream.ReceiveNext();
            float g = (float)stream.ReceiveNext();
            float b = (float)stream.ReceiveNext();
            float a = (float)stream.ReceiveNext();
            _networkColor = new Color(r, g, b, 1f);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 점수 동기화: PhotonNetwork CustomProperties 사용
    // CustomProperties = 모든 클라이언트가 읽을 수 있는 딕셔너리
    // ─────────────────────────────────────────────────────────

    public void SyncScore(int newScore)
    {
        if (!photonView.IsMine) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "Score", newScore },
            { "Scale", scaleController != null ? scaleController.currentScaleValue : GameState.PlayerCurrentScale }
        };

        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public void SyncScale()
    {
        if (!photonView.IsMine) return;

        float scaleValue = scaleController != null ? scaleController.currentScaleValue : GameState.PlayerCurrentScale;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "Scale", scaleValue }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    /// <summary>
    /// 현재 디스플레이 색상을 룸 프로퍼티에 저장. 씬 전환 후에도 색상 복원 가능.
    /// 게임 종료 직전에 호출.
    /// </summary>
    public void SyncColor()
    {
        if (!photonView.IsMine) return;

        Color c = GameState.CurrentDisplayColor;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "Color_R", c.r },
            { "Color_G", c.g },
            { "Color_B", c.b }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public void SyncEliminated()
    {
        if (!photonView.IsMine) return;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "Eliminated", true }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    /// <summary>
    /// 탈락 상태 플래그를 해제(false)한다. 게임 스폰 / 리스폰 시 호출해
    /// 이전 게임의 stale "Eliminated"=true가 남아 리더보드에서 빠지거나
    /// 생존자 카운트에서 누락(라스트 맨 스탠딩 조기 종료)되는 것을 막는다.
    /// </summary>
    public void ClearEliminatedFlag()
    {
        if (!photonView.IsMine) return;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "Eliminated", false }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public static float GetPlayerSyncedScale(Player player)
    {
        if (player?.CustomProperties != null &&
            player.CustomProperties.TryGetValue("Scale", out object val))
            return (float)val;
        return 1f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine || _isAbsorbed) return;
        if (GameState.Phase != GamePhase.Playing) return;
        if (GameState.CurrentGameMode != GameModeType.Absorb) return;

        NetworkPlayerSync otherPlayer = other.GetComponentInParent<NetworkPlayerSync>();
        if (otherPlayer != null && otherPlayer != this)
        {
            photonView.RPC(nameof(RPC_RequestAbsorbValidation), RpcTarget.MasterClient,
                otherPlayer.photonView.ViewID);
            return;
        }

        AIPlayerMovement aiBot = other.GetComponentInParent<AIPlayerMovement>();
        if (aiBot != null)
        {
            int botId = aiBot.photonView.ViewID;
            if (_absorbedBotIds.Contains(botId)) return;
            _absorbedBotIds.Add(botId);

            photonView.RPC(nameof(RPC_RequestBotAbsorbValidation), RpcTarget.MasterClient, botId);
        }
    }

    // NetworkPlayerSync.cs 내부 혹은 플레이어 메인 스크립트에 추가
    public void SyncChocolateElimination()
    {
        if (!photonView.IsMine || _isAbsorbed) return;
        if (GameState.Phase != GamePhase.Playing) return;
        photonView.RPC(nameof(RPC_ChocolateElimination), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_ChocolateElimination()
    {
        // 모든 클라이언트: 입력/이동 FSM 정지
        if (playerController != null) playerController.enabled = false;

        // 이동 애니메이션 정지 (실제 모델 Animator + 올바른 파라미터명 "IsMoving").
        // FSM을 비활성화하면 현재 상태(Move)의 Exit()가 호출되지 않아 IsMoving이 true로
        // 남아 계속 걷는 애니메이션이 재생되므로 여기서 수동으로 꺼준다.
        Animator jellyAnim = playerController != null ? playerController.jellyAnimator : null;
        if (jellyAnim == null) jellyAnim = GetComponentInChildren<Animator>();
        if (jellyAnim != null) jellyAnim.SetBool("IsMoving", false);

        if (photonView.IsMine)
        {
            // 걷기 루프 사운드 정지 (걷기 사운드는 소유자 로컬에서만 재생되므로 소유자에서만 정지).
            // FSM Exit()가 호출되지 않아 StopWalking()이 누락되면 발소리가 계속 재생된다.
            PlaySFXAudio.Instance?.StopWalking();

            // 소유자만 물리 바디로 전환 → 초콜릿 강의 부력/유동으로 둥둥 떠다님.
            // PhotonTransformView가 이 위치를 원격 클라이언트로 전파한다.
            ConvertToFloatingBody();
            SyncEliminated();
            GameModeManager.Instance?.GameOver();
        }
        else
        {
            // 원격: CC만 끄고 위치는 네트워크 동기화에 맡김 (로컬 물리 중복 방지)
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }
    }

    /// <summary>
    /// CharacterController를 CapsuleCollider + Rigidbody로 전환.
    /// 초콜릿 강(ChocolateFluid.OnTriggerStay)의 부력/유동 힘을 받아 둥둥 떠다니게 한다.
    /// CC는 트리거 힘에 반응하지 않으므로 반드시 Rigidbody가 필요하다.
    /// </summary>
    private void ConvertToFloatingBody()
    {
        if (GetComponent<Rigidbody>() != null) return;

        CharacterController cc = GetComponent<CharacterController>();
        float radius = 0.5f, height = 2f;
        Vector3 center = Vector3.zero;
        if (cc != null)
        {
            radius = cc.radius;
            height = cc.height;
            center = cc.center;
            cc.enabled = false;
        }

        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.radius = radius;
        capsule.height = height;
        capsule.center = center;

        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.linearDamping = 3f;
        rb.angularDamping = 3f;
    }

    // ─────────────────────────────────────────────────────────
    // 점프 애니메이션 동기화 RPC
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 다른 클라이언트에서 이 플레이어의 점프 애니메이션을 재생
    /// PlayerJumpState.Enter()에서 RpcTarget.Others로 호출됨
    /// </summary>
    [PunRPC]
    public void RPC_PlayJump()
    {
        // IsMine이 아닌 쪽(원격 플레이어)에서 수신하므로 바로 재생
        if (playerController != null && playerController.jellyAnimator != null)
        {
            playerController.jellyAnimator.SetTrigger("Jump");
        }
    }

    // ─────────────────────────────────────────────────────────
    // MasterClient 검증 RPC
    // ─────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_RequestAbsorbValidation(int absorberViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (GameState.Phase != GamePhase.Playing) return;

        PhotonView absorberPV = PhotonView.Find(absorberViewID);
        if (absorberPV == null) return;

        float victimScale = GetAuthorityScale(photonView);
        float absorberScale = GetAuthorityScale(absorberPV);

        if (absorberScale > victimScale)
        {
            photonView.RPC(nameof(RPC_GetAbsorbed), RpcTarget.All, absorberViewID);
        }
    }

    [PunRPC]
    private void RPC_RequestBotAbsorbValidation(int botViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (GameState.Phase != GamePhase.Playing) return;

        PhotonView botPV = PhotonView.Find(botViewID);
        if (botPV == null) return;

        AIPlayerMovement aiBot = botPV.GetComponent<AIPlayerMovement>();
        if (aiBot == null) return;

        float playerScale = GetAuthorityScale(photonView);
        float botScale = GetBotAuthorityScale(aiBot);

        if (botScale > playerScale)
        {
            photonView.RPC(nameof(RPC_GetAbsorbed), RpcTarget.All, -1);
        }
        else if (playerScale > botScale && !aiBot.IsBeingAbsorbed)
        {
            int bonus = aiBot.CurrentScore;
            photonView.RPC(nameof(RPC_BotAbsorbConfirmed), photonView.Owner,
                bonus, botScale, botViewID);
            aiBot.photonView.RPC("RPC_BotAbsorbed", RpcTarget.All, photonView.ViewID);
        }
    }

    /// <summary>
    /// [플레이어 전용] MC가 봇 흡수를 승인한 후 점수/스케일 보상 수신
    /// </summary>
    [PunRPC]
    private void RPC_BotAbsorbConfirmed(int bonusScore, float botScale, int botViewID)
    {
        if (!photonView.IsMine) return;
        _absorbedBotIds.Add(botViewID);

        var dm = DataManager.Instance;
        float predictedScale = scaleController != null
            ? Mathf.Min(scaleController.currentScaleValue + botScale * dm.absorbScalePercent, dm.maxScale)
            : GameState.PlayerCurrentScale;
        GameState.CurrentScore = dm.ScoreFromScale(predictedScale);
        SyncScore(GameState.CurrentScore);
        scaleController?.GrowByAbsorbing(botScale);
    }

    /// <summary>
    /// [RPC] 모든 클라이언트에서 이 플레이어가 흡수되는 연출 재생
    /// </summary>
    [PunRPC]
    private void RPC_GetAbsorbed(int absorberViewID)
    {
        if (_isAbsorbed) return;
        _isAbsorbed = true;

        if (absorberViewID >= 0)
        {
            PhotonView absorberView = PhotonView.Find(absorberViewID);
            if (absorberView != null && absorberView.IsMine)
            {
                var absorberScale = absorberView.GetComponent<PlayerScaleController>();
                if (absorberScale != null)
                {
                    var dm = DataManager.Instance;
                    float predictedScale = Mathf.Min(
                        absorberScale.currentScaleValue + transform.localScale.x * dm.absorbScalePercent,
                        dm.maxScale);
                    GameState.CurrentScore = dm.ScoreFromScale(predictedScale);
                    SyncScore(GameState.CurrentScore);
                }

                absorberView.GetComponent<PlayerScaleController>()?.GrowByAbsorbing(transform.localScale.x);
            }
        }

        SyncEliminated();
        GameModeManager.Instance?.OnPlayerAbsorbed(this);
        StartCoroutine(AbsorbedSequence(absorberViewID));
    }

    private IEnumerator AbsorbedSequence(int absorberViewID)
    {
        if (playerController != null) playerController.enabled = false;
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        PhotonView absorberView = absorberViewID >= 0 ? PhotonView.Find(absorberViewID) : null;
        Transform absorberTf = absorberView?.transform;

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        const float duration = 0.8f;
        const float moveSpeed = 12f;
        const float snapDist = 0.4f;

        while (elapsed < duration)
        {
            if (absorberTf != null)
            {
                if (Vector3.Distance(transform.position, absorberTf.position) <= snapDist) break;
                transform.position = Vector3.MoveTowards(transform.position, absorberTf.position, moveSpeed * Time.deltaTime);
            }
            transform.localScale = Vector3.Lerp(startScale, Vector3.one * 0.05f, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 리스폰: 스케일/색상 초기화 후 다시 활성화
    /// </summary>
    private void Respawn()
    {
        _isAbsorbed = false;
        _absorbedBotIds.Clear();

        // 리스폰 시 탈락 플래그 해제 (리더보드에서 다시 보이도록 + 생존자 카운트 복구)
        ClearEliminatedFlag();

        // 스폰 포인트 중 랜덤 위치로 이동
        if (NetworkManager.Instance?.spawnPoints?.Length > 0)
        {
            int idx = Random.Range(0, NetworkManager.Instance.spawnPoints.Length);
            transform.position = NetworkManager.Instance.spawnPoints[idx].position;
        }

        // 색상/스케일 리셋
        scaleController?.ResetScale();
        colorVisual?.ResetColor();
        GameState.CurrentScore = 0;

        gameObject.SetActive(true);

        // 리스폰 알림 RPC
        photonView.RPC(nameof(RPC_OnRespawn), RpcTarget.Others);
    }

    [PunRPC]
    private void RPC_OnRespawn()
    {
        gameObject.SetActive(true);
    }

    // ─────────────────────────────────────────────────────────
    // State Sync 기반 크기 판정 유틸리티
    // ─────────────────────────────────────────────────────────

    private static float GetAuthorityScale(PhotonView pv)
    {
        if (pv.Owner?.CustomProperties != null &&
            pv.Owner.CustomProperties.TryGetValue("Scale", out object val))
            return (float)val;
        return pv.transform.localScale.x;
    }

    private static float GetBotAuthorityScale(AIPlayerMovement bot)
    {
        AIPlayerSync sync = bot.GetComponent<AIPlayerSync>();
        if (sync != null) return sync.GetSyncedScale();
        return bot.transform.localScale.x;
    }

    // ─────────────────────────────────────────────────────────
    // 외부에서 읽는 프로퍼티
    // ─────────────────────────────────────────────────────────

    public float ScaleValue
    {
        get
        {
            if (photonView.IsMine)
                return scaleController != null ? scaleController.currentScaleValue : 1f;
            return GetAuthorityScale(photonView);
        }
    }

    public string PlayerName => photonView.Owner?.NickName ?? "Bot";

    // ─────────────────────────────────────────────────────────
    // 대쉬 시스템
    // ─────────────────────────────────────────────────────────

    [PunRPC]
    public void RPC_PlayDash()
    {
        if (playerController != null && playerController.jellyAnimator != null)
            playerController.jellyAnimator.SetTrigger("Dash");
    }

    [PunRPC]
    public void RPC_RequestDashHitPlayer(int victimViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (GameState.Phase != GamePhase.Playing) return;

        PhotonView victimPV = PhotonView.Find(victimViewID);
        if (victimPV == null) return;

        float dasherScale = GetAuthorityScale(photonView);
        float victimScale = GetAuthorityScale(victimPV);
        float diff = dasherScale - victimScale;
        float threshold = DataManager.Instance.PushScaleThreshold;

        // Push 모드에서는 크기 차가 커도 흡수하지 않고 항상 밀치기만.
        if (diff > threshold && GameState.CurrentGameMode == GameModeType.Absorb)
        {
            victimPV.RPC(nameof(RPC_GetAbsorbed), RpcTarget.All, photonView.ViewID);
        }
        else
        {
            Vector3 pushDir = (victimPV.transform.position - transform.position).normalized;
            pushDir.y = 0f;
            if (pushDir.sqrMagnitude < 0.01f) pushDir = transform.forward;
            pushDir.Normalize();

            float pushForce = DataManager.Instance.dashPushForce;

            victimPV.RPC(nameof(RPC_ApplyKnockback), victimPV.Owner,
                pushDir.x, pushDir.z, pushForce);

            photonView.RPC(nameof(RPC_ApplyKnockback), photonView.Owner,
                -pushDir.x, -pushDir.z, pushForce * 0.5f);
        }
    }

    [PunRPC]
    public void RPC_RequestDashHitBot(int botViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (GameState.Phase != GamePhase.Playing) return;

        PhotonView botPV = PhotonView.Find(botViewID);
        if (botPV == null) return;

        AIPlayerMovement aiBot = botPV.GetComponent<AIPlayerMovement>();
        if (aiBot == null || aiBot.IsEliminated || aiBot.IsBeingAbsorbed) return;

        float dasherScale = GetAuthorityScale(photonView);
        float botScale = GetBotAuthorityScale(aiBot);
        float diff = dasherScale - botScale;
        float threshold = DataManager.Instance.PushScaleThreshold;

        // Push 모드에서는 크기 차가 커도 흡수하지 않고 항상 밀치기만.
        if (diff > threshold && GameState.CurrentGameMode == GameModeType.Absorb)
        {
            int bonus = aiBot.CurrentScore;
            photonView.RPC(nameof(RPC_BotAbsorbConfirmed), photonView.Owner,
                bonus, botScale, botViewID);
            aiBot.photonView.RPC("RPC_BotAbsorbed", RpcTarget.All, photonView.ViewID);
        }
        else
        {
            Vector3 pushDir = (botPV.transform.position - transform.position).normalized;
            pushDir.y = 0f;
            if (pushDir.sqrMagnitude < 0.01f) pushDir = transform.forward;
            pushDir.Normalize();

            float pushForce = DataManager.Instance.dashPushForce;

            // 봇 위치는 소유자(마스터)가 PhotonTransformView로 권위 동기화하므로, 넉백도
            // 소유자에게만 보내 transform을 움직이게 한다(All로 보내면 비마스터에서 지터 발생).
            aiBot.photonView.RPC(nameof(AIPlayerMovement.RPC_ApplyKnockback), aiBot.photonView.Owner,
                pushDir.x, pushDir.z, pushForce);

            photonView.RPC(nameof(RPC_ApplyKnockback), photonView.Owner,
                -pushDir.x, -pushDir.z, pushForce * 0.5f);
        }
    }

    [PunRPC]
    public void RPC_ApplyKnockback(float dirX, float dirZ, float force)
    {
        if (playerController == null) return;
        Vector3 dir = new Vector3(dirX, 0f, dirZ).normalized;
        playerController.ApplyKnockback(dir, force);
    }

    // ─────────────────────────────────────────────────────────
    // 방망이 공격 시스템 (밀치기 모드 전용)
    // ─────────────────────────────────────────────────────────

    [PunRPC]
    public void RPC_PlayAttack()
    {
        if (playerController != null)
        {
            if (playerController.jellyAnimator != null)
                playerController.jellyAnimator.SetTrigger("Attack");

            if (playerController.batPivot != null)
                StartCoroutine(RemoteBatSwing());
        }
    }

    private IEnumerator RemoteBatSwing()
    {
        var dm = DataManager.Instance;
        if (dm == null) yield break;

        Transform bat = playerController.batPivot;
        bat.gameObject.SetActive(true);

        float half = dm.batArcAngle * 0.5f;
        Quaternion start = Quaternion.Euler(0f, -half, 0f);
        Quaternion end = Quaternion.Euler(0f, half, 0f);

        float t = 0f;
        while (t < dm.batSwingDuration)
        {
            t += Time.deltaTime;
            bat.localRotation = Quaternion.Slerp(start, end, t / dm.batSwingDuration);
            yield return null;
        }

        bat.localRotation = Quaternion.identity;
        if (playerController.hideBatWhenIdle)
            bat.gameObject.SetActive(false);
    }

    [PunRPC]
    public void RPC_RequestBatHitPlayer(int victimViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (GameState.Phase != GamePhase.Playing) return;

        PhotonView victimPV = PhotonView.Find(victimViewID);
        if (victimPV == null) return;

        float attackerScale = GetAuthorityScale(photonView);
        float victimScale = GetAuthorityScale(victimPV);

        Vector3 pushDir = (victimPV.transform.position - transform.position).normalized;
        pushDir.y = 0f;
        if (pushDir.sqrMagnitude < 0.01f) pushDir = transform.forward;
        pushDir.Normalize();

        var dm = DataManager.Instance;
        float pushForce = dm.batPushForce * (attackerScale / dm.startingScale);

        victimPV.RPC(nameof(RPC_ApplyKnockback), victimPV.Owner,
            pushDir.x, pushDir.z, pushForce);

        float growth = dm.batHitGrowth / Mathf.Max(attackerScale, 1f);
        photonView.RPC(nameof(RPC_BatGrowReward), photonView.Owner, growth);
    }

    [PunRPC]
    public void RPC_RequestBatHitBot(int botViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (GameState.Phase != GamePhase.Playing) return;

        PhotonView botPV = PhotonView.Find(botViewID);
        if (botPV == null) return;

        AIPlayerMovement aiBot = botPV.GetComponent<AIPlayerMovement>();
        if (aiBot == null || aiBot.IsEliminated || aiBot.IsBeingAbsorbed) return;

        float attackerScale = GetAuthorityScale(photonView);

        Vector3 pushDir = (botPV.transform.position - transform.position).normalized;
        pushDir.y = 0f;
        if (pushDir.sqrMagnitude < 0.01f) pushDir = transform.forward;
        pushDir.Normalize();

        var dm = DataManager.Instance;
        float pushForce = dm.batPushForce * (attackerScale / dm.startingScale);

        aiBot.photonView.RPC(nameof(AIPlayerMovement.RPC_ApplyKnockback), RpcTarget.All,
            pushDir.x, pushDir.z, pushForce);

        float growth = dm.batHitGrowth / Mathf.Max(attackerScale, 1f);
        photonView.RPC(nameof(RPC_BatGrowReward), photonView.Owner, growth);
    }

    [PunRPC]
    private void RPC_BatGrowReward(float growth)
    {
        if (!photonView.IsMine) return;
        scaleController?.GrowByBatHit(growth);
        SyncScale();
    }
}
