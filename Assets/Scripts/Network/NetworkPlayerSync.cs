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
    // 첫 스트림 패킷 전에 Color(0,0,0,0) 기본값이 적용되어 원격 플레이어가 투명해지는 것을 방지
    private Color _networkColor = Color.white;

    private const float LerpSpeed = 10f;

    // ─────────────────────────────────────────────────────────
    // 생존 모드: 플레이어 흡수 관련
    // ─────────────────────────────────────────────────────────
    [Header("생존 모드 설정")]
    [Tooltip("내가 흡수되었을 때 리스폰까지 걸리는 시간 (초)")]
    public float respawnDelay = 3f;

    private bool _isAbsorbed = false;   // 현재 흡수된 상태인지
    private System.Collections.Generic.HashSet<int> _absorbedBotIds = new System.Collections.Generic.HashSet<int>();

    // ─────────────────────────────────────────────────────────
    // 레지스트리 등록
    // ─────────────────────────────────────────────────────────
    private void OnEnable() => EntityRegistry.Register(this);
    private void OnDisable() => EntityRegistry.Unregister(this);

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
        if (photonView.IsMine) return;

        // 스케일: CustomProperties에서 읽어 Lerp (권위적 소스)
        float targetScale = GetAuthorityScale(photonView);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * targetScale, Time.deltaTime * LerpSpeed);

        // 색상 적용
        if (jellyRenderer != null)
        {
            jellyRenderer.material.SetColor("_BaseColor_01", _networkColor);
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
            _networkColor = new Color(r, g, b, a);
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
        if (!photonView.IsMine) return;
        photonView.RPC(nameof(RPC_ChocolateElimination), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_ChocolateElimination()
    {
        // 물리 구조 전환 (모든 클라이언트에서 실행)
        CharacterController cc = GetComponent<CharacterController>();
        Rigidbody rb = GetComponent<Rigidbody>();

        if (cc != null) cc.enabled = false;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.linearDamping = 3f;
            rb.angularDamping = 3f;
        }

        // 소유자만: 입력 차단, 애니메이션 정지, 탈락 동기화, 게임오버
        if (photonView.IsMine)
        {
            if (playerController != null) playerController.enabled = false;

            Animator playerAnimator = GetComponent<Animator>();
            if (playerAnimator != null)
                playerAnimator.SetBool("isMoving", false);

            SyncEliminated();
            GameModeManager.Instance?.GameOver();
        }
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
}
