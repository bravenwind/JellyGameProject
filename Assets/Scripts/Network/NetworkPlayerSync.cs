// ============================================================
// NetworkPlayerSync.cs
// ============================================================
// 역할: 로컬 플레이어의 상태를 다른 클라이언트에 동기화
//       + 플레이어끼리 충돌 시 흡수 처리 (생존 모드 로직)
//
// [이 스크립트가 하는 일]
//   1. IPunObservable → 위치/색상/스케일을 매 프레임 전송
//   2. RPC → "나를 흡수해라" 명령을 네트워크로 전달
//   3. 점수/스케일 레벨을 PhotonNetwork.LocalPlayer.CustomProperties에 저장
//      → 모든 클라이언트가 다른 플레이어의 점수를 읽을 수 있게 됨
//
// [이 컴포넌트를 붙일 오브젝트]
//   NetworkPlayer 프리팹 (PlayerController, PlayerAbsorber 등 기존 컴포넌트 포함)
//   + PhotonView 컴포넌트 (필수!)
//   + PhotonTransformView 컴포넌트 (위치 자동 동기화용)
// ============================================================

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
    public PlayerController playerController;
    public PlayerAbsorber playerAbsorber;
    public PlayerColorVisual colorVisual;
    public PlayerScaleController scaleController;
    public Renderer jellyRenderer;

    [Header("이름표 UI (선택사항)")]
    public TMPro.TextMeshProUGUI nameTagText;

    [Header("봇 설정")]
    public bool isBot = false;

    // ─────────────────────────────────────────────────────────
    // 원격 플레이어 보간용 변수 (내 화면에서 다른 플레이어를 부드럽게 움직이기 위해)
    // ─────────────────────────────────────────────────────────
    private Vector3 _networkPosition;       // 받은 위치 값
    private Quaternion _networkRotation;    // 받은 회전 값
    private Color _networkColor;            // 받은 색상 값
    private int _networkScaleLevel = 1;     // 받은 스케일 레벨

    // 보간 속도 (값이 클수록 다른 플레이어가 더 빠르게 목표 위치로 이동)
    private const float LerpSpeed = 10f;

    // ─────────────────────────────────────────────────────────
    // 생존 모드: 플레이어 흡수 관련
    // ─────────────────────────────────────────────────────────
    [Header("생존 모드 설정")]
    [Tooltip("내가 흡수되었을 때 리스폰까지 걸리는 시간 (초)")]
    public float respawnDelay = 3f;

    private bool _isAbsorbed = false;   // 현재 흡수된 상태인지

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────
    private void Start()
    {
        // photonView.IsMine: "이 PhotonView의 주인이 나인가?"
        // true  = 로컬 플레이어 (내가 조작하는 캐릭터)
        // false = 원격 플레이어 (다른 클라이언트의 캐릭터, 내 화면에 표시만 됨)

        if (photonView.IsMine && !isBot)
        {
            // ── 로컬 플레이어 초기화 ──
            SetupLocalPlayer();
        }
        else if (!photonView.IsMine)
        {
            // ── 원격 플레이어/봇 초기화 ──
            SetupRemotePlayer();
        }

        // 이름표 설정
        if (nameTagText != null)
            nameTagText.text = photonView.Owner?.NickName ?? "???";
    }

    private void SetupLocalPlayer()
    {
        // 카메라가 이 플레이어를 따라오도록 설정
        TopDownCameraFollow cam = Camera.main?.GetComponent<TopDownCameraFollow>();
        if (cam != null) cam.target = transform;

        // MainCamera_Action 타겟 연결
        MainCamera_Action camAction = Camera.main?.GetComponent<MainCamera_Action>();
        if (camAction != null) camAction.SetTarget(transform);

        // GameModeManager에 내 참조 등록
        GameModeManager.Instance?.RegisterLocalPlayer(this);

        // UIFollowTarget들이 플레이어를 못 찾는 경우를 대비해 직접 연결
        foreach (var uiFollow in FindObjectsByType<UIFollowTarget>(FindObjectsSortMode.None))
        {
            uiFollow.SetTarget(transform);
        }

        Debug.Log($"[Network] 로컬 플레이어 초기화: {PhotonNetwork.NickName}");
    }

    private void SetupRemotePlayer()
    {
        // 원격 플레이어는 PlayerController(입력) 비활성화
        if (playerController != null) playerController.enabled = false;

        // 원격 플레이어는 직접 물리 연산 필요 없음
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Debug.Log($"[Network] 원격 플레이어 초기화: {photonView.Owner?.NickName}");
    }

    // ─────────────────────────────────────────────────────────
    // Update: 원격 플레이어 보간 처리
    // ─────────────────────────────────────────────────────────
    private void Update()
    {
        if (photonView.IsMine) return; // 내 플레이어는 건드리지 않음

        // Lerp: 현재 위치에서 받은 위치로 부드럽게 이동
        // 네트워크 지연으로 인한 튀는 현상을 완화
        transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * LerpSpeed);
        transform.rotation = Quaternion.Lerp(transform.rotation, _networkRotation, Time.deltaTime * LerpSpeed);

        // 색상 적용
        if (jellyRenderer != null)
        {
            jellyRenderer.material.SetColor("_BaseColor_01", _networkColor);
        }
    }

    // ─────────────────────────────────────────────────────────
    // IPunObservable 구현: 연속 데이터 동기화
    // OnPhotonSerializeView는 PhotonView가 데이터를 보내고/받을 때 자동으로 호출됨
    // ─────────────────────────────────────────────────────────
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // ── 내 플레이어의 데이터를 보냄 ──
            // stream.SendNext()로 보내는 순서와
            // stream.ReceiveNext()로 받는 순서가 반드시 일치해야 함!

            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(DataManager.Instance.playerCurrentScaleLevel);
            stream.SendNext((Vector4)DataManager.Instance.GetCurrentDisplayColor());
        }
        else
        {
            // ── 다른 플레이어의 데이터를 받음 ──
            _networkPosition = (Vector3)stream.ReceiveNext();
            _networkRotation = (Quaternion)stream.ReceiveNext();
            _networkScaleLevel = (int)stream.ReceiveNext();
            Vector4 colorVec = (Vector4)stream.ReceiveNext();
            _networkColor = new Color(colorVec.x, colorVec.y, colorVec.z, colorVec.w);

            // 스케일 레벨에 따라 오브젝트 크기 적용
            if (DataManager.Instance != null)
            {
                int levelIdx = Mathf.Clamp(_networkScaleLevel - 1, 0, DataManager.Instance.scalePerLevel.Length - 1);
                transform.localScale = Vector3.one * DataManager.Instance.scalePerLevel[levelIdx];
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // 점수 동기화: PhotonNetwork CustomProperties 사용
    // CustomProperties = 모든 클라이언트가 읽을 수 있는 딕셔너리
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 내 점수를 네트워크에 업데이트 (점수가 바뀔 때마다 호출)
    /// </summary>
    public void SyncScore(int newScore)
    {
        if (!photonView.IsMine) return;

        // Hashtable: key-value 쌍으로 데이터를 저장하는 딕셔너리
        // Photon에서는 이 방식으로 플레이어 속성을 공유
        Hashtable props = new Hashtable
        {
            { "Score", newScore },
            { "ScaleLevel", DataManager.Instance.playerCurrentScaleLevel }
        };

        // SetCustomProperties: 변경된 내용만 네트워크로 전송 (효율적)
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    // ─────────────────────────────────────────────────────────
    // 생존 모드: 플레이어 흡수 (RPC)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 이 플레이어에 충돌했을 때 호출 (다른 플레이어/AI 충돌 감지)
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine || _isAbsorbed) return;

        // 상대방도 NetworkPlayerSync를 가지고 있는지 확인
        NetworkPlayerSync other_player = other.GetComponentInParent<NetworkPlayerSync>();
        if (other_player != null)
        {
            int myLevel = DataManager.Instance.playerCurrentScaleLevel;
            int otherLevel = other_player._networkScaleLevel;

            // 상대방이 나보다 크면 내가 흡수됨
            if (otherLevel > myLevel)
            {
                // RPC: 모든 클라이언트에서 GetAbsorbed() 실행
                // RpcTarget.All = 나 포함 모든 클라이언트
                photonView.RPC(nameof(RPC_GetAbsorbed), RpcTarget.All, other_player.photonView.ViewID);
            }
        }

        // AI 봇(AIPlayerMovement)이 나보다 큰지 확인
        AIPlayerMovement aiBot = other.GetComponentInParent<AIPlayerMovement>();
        if (aiBot != null)
        {
            int myLevel = DataManager.Instance.playerCurrentScaleLevel;
            int botLevel = aiBot.CurrentScaleLevel;

            if (botLevel > myLevel)
            {
                // AI가 더 큼 → 내가 흡수됨
                photonView.RPC(nameof(RPC_GetAbsorbed), RpcTarget.All, -1);
            }
            else if (myLevel > botLevel)
            {
                // 내가 더 큼 → AI 흡수 (점수 획득 + 봇 파괴 요청)
                int bonus = aiBot.CurrentScaleLevel * 500;
                DataManager.Instance.currentScore += bonus;
                SyncScore(DataManager.Instance.currentScore);
                aiBot.photonView.RPC("RPC_BotAbsorbed", RpcTarget.All, photonView.ViewID);
            }
        }
    }

    /// <summary>
    /// [RPC] 모든 클라이언트에서 이 플레이어가 흡수되는 연출 재생
    /// </summary>
    [PunRPC]
    private void RPC_GetAbsorbed(int absorberViewID)
    {
        if (_isAbsorbed) return;
        _isAbsorbed = true;

        // 흡수한 플레이어 점수 증가 (흡수한 쪽 클라이언트에서만 처리)
        if (absorberViewID >= 0)
        {
            PhotonView absorberView = PhotonView.Find(absorberViewID);
            if (absorberView != null && absorberView.IsMine)
            {
                // 흡수한 플레이어에게 점수 보너스
                int bonus = DataManager.Instance.playerCurrentScaleLevel * 500;
                DataManager.Instance.currentScore += bonus;
                SyncScore(DataManager.Instance.currentScore);
            }
        }

        // 내 클라이언트에서: 오브젝트 숨기고 리스폰 대기
        if (photonView.IsMine)
        {
            gameObject.SetActive(false);
            Invoke(nameof(Respawn), respawnDelay);
        }
        else
        {
            // 다른 클라이언트에서: 시각적으로만 숨김
            gameObject.SetActive(false);
        }

        GameModeManager.Instance?.OnPlayerAbsorbed(this);
    }

    /// <summary>
    /// 리스폰: 스케일/색상 초기화 후 다시 활성화
    /// </summary>
    private void Respawn()
    {
        _isAbsorbed = false;

        // 스폰 포인트 중 랜덤 위치로 이동
        if (NetworkManager.Instance?.spawnPoints?.Length > 0)
        {
            int idx = Random.Range(0, NetworkManager.Instance.spawnPoints.Length);
            transform.position = NetworkManager.Instance.spawnPoints[idx].position;
        }

        // 색상/스케일 리셋
        scaleController?.ResetScale();
        colorVisual?.ResetColor();
        DataManager.Instance.currentScore = 0;

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
    // 외부에서 읽는 프로퍼티
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 이 플레이어의 현재 스케일 레벨 (로컬이면 DataManager, 원격이면 네트워크 값)
    /// </summary>
    public int ScaleLevel => photonView.IsMine
        ? DataManager.Instance.playerCurrentScaleLevel
        : _networkScaleLevel;

    /// <summary>
    /// 이 플레이어의 닉네임
    /// </summary>
    public string PlayerName => photonView.Owner?.NickName ?? "Bot";
}
