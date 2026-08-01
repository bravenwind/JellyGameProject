using UnityEngine;

public class PlayerBridge : MonoBehaviour
{
    private PlayerScaleController _scaleCtrl;
    private PlayerColorVisual _colorVisual;
    private PlayerAbsorber _absorber;
    private PlayerMovement _playerController;
    private LevelUpFloaterPool _levelUpPool;
    private NetworkPlayerSync _netSync;

    // [CBT-4/W6] 이 아바타가 로컬 소유인가. PlayerBridge는 로컬·원격 플레이어 프리팹 모두에 붙고
    // PlayerScaleController.Start는 소유 무관하게 OnScaleInit을 발화한다. 아래 핸들러들이 전역 정적
    // GameState(PlayerCurrentScale/DetectRadius/색 — 클라당 1개)를 쓰므로, 원격 플레이어 스폰마다
    // 로컬 HUD/감지반경/동기화 색이 남의 값으로 오염됐다. 소유자일 때만 전역 상태를 쓰도록 가드한다.
    // (netSync가 없으면 오프라인/레거시로 보고 허용)
    // [LAN 이식] 소유자 판정을 NetIdentity 기준으로.
    //
    //   원래는 NetworkPlayerSync.photonView.IsMine을 봤는데, 그 컴포넌트를 걷어낸 뒤로
    //   _netSync가 항상 null이 되어 <b>이 프로퍼티가 언제나 true</b>가 됐다.
    //   그 결과 원격 아바타가 커져도 내 카메라가 줌되고, 전역 GameState/HUD가
    //   남의 값으로 오염됐다. (W6 가드가 통째로 무력화돼 있었다)
    private JellyNet.NetIdentity _netId;
    private JellyNet.LanPlayerState _lanState;

    // [LAN 이식] 점수 전송 창구를 한 곳으로.
    //   NetworkPlayerSync.SyncScore를 부르던 자리가 두 군데 있었는데,
    //   그 컴포넌트가 없어지면서 하나는 컴파일 에러, 하나는 조용히 죽어 있었다.
    private void PushScore(int score)
    {
        if (_lanState != null) { _lanState.ReportOwnScore(score); return; }
        if (_netSync != null && _netSync.photonView != null && _netSync.photonView.IsMine)
            _netSync.SyncScore(score);
    }

    private bool IsLocalOwner
    {
        get
        {
            if (_netId != null) return _netId.IsMine;
            if (_netSync != null) return _netSync.photonView != null && _netSync.photonView.IsMine;
            return true;   // 오프라인/레거시
        }
    }

    private void Awake()
    {
        _netSync = GetComponent<NetworkPlayerSync>();
        _netId = GetComponentInParent<JellyNet.NetIdentity>();
        _lanState = GetComponentInParent<JellyNet.LanPlayerState>();
        _scaleCtrl = GetComponentInChildren<PlayerScaleController>();
        _colorVisual = GetComponentInChildren<PlayerColorVisual>();
        _absorber = GetComponentInChildren<PlayerAbsorber>();
        _playerController = GetComponentInChildren<PlayerMovement>();

        // 풀(컨테이너)이 프리팹에 있으면 사용, 없으면 동적 생성
        _levelUpPool = GetComponentInChildren<LevelUpFloaterPool>();
        if (_levelUpPool == null)
        {
            var go = new GameObject("LevelUpFloaterPool");
            go.transform.SetParent(transform, false);
            _levelUpPool = go.AddComponent<LevelUpFloaterPool>();
        }
    }

    private void OnEnable()
    {
        if (_scaleCtrl != null)
        {
            _scaleCtrl.OnScaleInit += HandleScaleInit;
            _scaleCtrl.OnGrowStarted += HandleGrowStarted;
            _scaleCtrl.OnShrinkStarted += HandleShrinkStarted;
            _scaleCtrl.OnScaleThresholdUp += HandleScaleThresholdUp;
            _scaleCtrl.OnScaleThresholdDown += HandleScaleThresholdDown;
            _scaleCtrl.OnScaleCompleted += HandleScaleCompleted;
            _scaleCtrl.OnScaleReset += HandleScaleReset;
        }

        if (_colorVisual != null)
        {
            _colorVisual.OnColorApplied += HandleColorApplied;
            _colorVisual.OnColorUIUpdate += HandleColorUIUpdate;
        }

        if (_absorber != null)
        {
            _absorber.OnJellyScored += HandleJellyScored;
        }
    }

    private void OnDisable()
    {
        if (_scaleCtrl != null)
        {
            _scaleCtrl.OnScaleInit -= HandleScaleInit;
            _scaleCtrl.OnGrowStarted -= HandleGrowStarted;
            _scaleCtrl.OnShrinkStarted -= HandleShrinkStarted;
            _scaleCtrl.OnScaleThresholdUp -= HandleScaleThresholdUp;
            _scaleCtrl.OnScaleThresholdDown -= HandleScaleThresholdDown;
            _scaleCtrl.OnScaleCompleted -= HandleScaleCompleted;
            _scaleCtrl.OnScaleReset -= HandleScaleReset;
        }

        if (_colorVisual != null)
        {
            _colorVisual.OnColorApplied -= HandleColorApplied;
            _colorVisual.OnColorUIUpdate -= HandleColorUIUpdate;
        }

        if (_absorber != null)
        {
            _absorber.OnJellyScored -= HandleJellyScored;
        }
    }

    // ── Scale ──

    private void HandleScaleInit(float scaleValue)
    {
        if (!IsLocalOwner) return; // [W6] 원격 아바타는 전역 GameState/HUD를 건드리지 않는다
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius;
        GameState.PlayerCurrentScale = scaleValue;
        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    private void HandleGrowStarted(bool playEffect)
    {
        if (playEffect)
        {
            if (PlaySFXAudio.Instance != null)
            {
                PlaySFXAudio.Instance.PlayScaleUpSound();
                if (GameState.CurrentGameMode == GameModeType.Absorb)
                    PlaySFXAudio.Instance.PlayColorMixSound();
            }
            if (_levelUpPool != null) _levelUpPool.Play();
        }
    }

    private void HandleShrinkStarted()
    {
        UIPoolManager.Instance?.SpawnUI(UIType.MilkScaleDecrease);
    }

    // [LAN 이식] IsLocalOwner 가드 추가.
    //   PlayerEvents.OnCameraScale*는 static이라, 원격 캐릭터가 커져도
    //   내 카메라가 함께 줌되는 문제가 있었다. HandleScaleCompleted에는 이미
    //   같은 가드가 있었는데(W6) 이 둘만 빠져 있었다.
    private void HandleScaleThresholdUp()
    {
        if (!IsLocalOwner) return;
        PlayerEvents.OnCameraScaleIncreased?.Invoke();
    }

    private void HandleScaleThresholdDown()
    {
        if (!IsLocalOwner) return;
        PlayerEvents.OnCameraScaleDecreased?.Invoke();
    }

    private void HandleScaleCompleted(float scaleValue)
    {
        if (!IsLocalOwner) return; // [W6] 원격 아바타의 성장 완료가 로컬 전역 상태를 오염시키지 않게
        if (_playerController != null)
        {
            _playerController.jumpForce = scaleValue >= DataManager.Instance.jumpScaleThreshold
                ? _playerController.originalJumpForce + DataManager.Instance.IncreaseJumpForceValue
                : _playerController.originalJumpForce;
        }
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius
            + (scaleValue - 1f) * DataManager.Instance.detectPlusRadiusPerLevel;
        GameState.PlayerCurrentScale = scaleValue;

        GameState.CurrentScore = DataManager.Instance.ScoreFromScale(scaleValue);
        PushScore(GameState.CurrentScore);

        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    private void HandleScaleReset()
    {
        if (!IsLocalOwner) return; // [W6]
        PlayerEvents.OnCameraOrthoSizeChanged?.Invoke(6.1f);
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius;
        GameState.PlayerCurrentScale = 1f;
        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    // ── Color ──

    private void HandleColorApplied(JellyColorType dominantType, RYBColor ryb, Color displayColor)
    {
        // [W6] 원격 아바타의 색은 색 스트림(_networkColor)이 담당한다. 여기서 전역
        // GameState.CurrentDisplayColor(로컬 플레이어 색·SyncColor 소스)를 덮으면 내 색이 남의 색으로 샌다.
        if (!IsLocalOwner) return;
        GameState.CurrentRYBColor = ryb;
        GameState.CurrentDisplayColor = displayColor;
        PlayerEvents.OnColorChanged?.Invoke(dominantType, ryb);
    }

    private void HandleColorUIUpdate()
    {
        PlayerEvents.OnColorUIUpdate?.Invoke();
    }

    // ── Absorb ──

    private void HandleJellyScored()
    {
        // [LAN 이식] NetworkPlayerSync를 걷어낸 뒤로 netSync가 항상 null이 되어
        //   여기서 무조건 return → 점수가 아무에게도 반영되지 않았다.
        if (!IsLocalOwner) return;

        var dm = DataManager.Instance;
        float predictedScale = _scaleCtrl != null
            ? _scaleCtrl.PendingScale
            : GameState.PlayerCurrentScale + dm.jellyScaleIncrease;
        GameState.CurrentScore = dm.ScoreFromScale(predictedScale);
        PushScore(GameState.CurrentScore);
        UIPoolManager.Instance?.SpawnUI(UIType.JellyEat);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayColorMixSound();
        if (_levelUpPool != null) _levelUpPool.Play();
        if (GameState.CurrentScore >= dm.targetScore)
            dm.missions[1].missionCleared = true;
    }
}
