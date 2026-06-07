using UnityEngine;

public class PlayerBridge : MonoBehaviour
{
    private PlayerScaleController _scaleCtrl;
    private PlayerColorVisual _colorVisual;
    private PlayerAbsorber _absorber;
    private PlayerMovement _playerController;
    private LevelUpFloaterPool _levelUpPool;

    private void Awake()
    {
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

    private void HandleScaleThresholdUp() => PlayerEvents.OnCameraScaleIncreased?.Invoke();
    private void HandleScaleThresholdDown() => PlayerEvents.OnCameraScaleDecreased?.Invoke();

    private void HandleScaleCompleted(float scaleValue)
    {
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
        var netSync = GetComponent<NetworkPlayerSync>();
        if (netSync != null && netSync.photonView.IsMine)
            netSync.SyncScore(GameState.CurrentScore);

        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    private void HandleScaleReset()
    {
        PlayerEvents.OnCameraOrthoSizeChanged?.Invoke(6.1f);
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius;
        GameState.PlayerCurrentScale = 1f;
        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    // ── Color ──

    private void HandleColorApplied(JellyColorType dominantType, RYBColor ryb, Color displayColor)
    {
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
        var netSync = GetComponent<NetworkPlayerSync>();
        if (netSync == null || !netSync.photonView.IsMine) return;

        var dm = DataManager.Instance;
        float predictedScale = _scaleCtrl != null
            ? _scaleCtrl.PendingScale
            : GameState.PlayerCurrentScale + dm.jellyScaleIncrease;
        GameState.CurrentScore = dm.ScoreFromScale(predictedScale);
        netSync.SyncScore(GameState.CurrentScore);
        UIPoolManager.Instance?.SpawnUI(UIType.JellyEat);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayColorMixSound();
        if (_levelUpPool != null) _levelUpPool.Play();
        if (GameState.CurrentScore >= dm.targetScore)
            dm.missions[1].missionCleared = true;
    }
}
