using UnityEngine;
using JellyNet;

public class PlayerBridge : MonoBehaviour
{
    private PlayerScaleController scaleController;
    private PlayerColorVisual colorVisual;
    private PlayerAbsorber absorber;
    private PlayerMovement movement;
    private LevelUpFloaterPool levelUpFloaterPool;

    // [CBT-4/W6] 이 아바타가 로컬 소유인가. PlayerBridge는 로컬·원격 플레이어 프리팹 모두에 붙고
    // PlayerScaleController.Start는 소유 무관하게 OnScaleSettled를 발화한다. 아래 핸들러들이 전역 정적
    // GameState(PlayerCurrentScale/색 — 클라당 1개)를 쓰므로, 원격 플레이어 스폰마다
    // 로컬 HUD/동기화 색이 남의 값으로 오염됐다. 소유자일 때만 전역 상태를 쓰도록 가드한다.
    // 소유자 판정은 NetIdentity 기준이다.
    //
    //   이 가드가 무력화되면 원격 아바타가 커져도 내 카메라가 줌되고,
    //   전역 GameState/HUD가 남의 값으로 오염된다.
    private NetIdentity netId;

    // ★ 점수는 호스트만 만든다
    //   예전엔 여기서도 ScoreFromScale을 계산해 LanPlayerState.Score에 직접 써넣었다.
    //   같은 계산이 상태 컴포넌트 안에도 있어서 두 갈래였는데, 그쪽에만 모드 가드가
    //   있었다. 그래서 밀치기 모드에서 크기가 변하면 밀치기로 쌓은 점수를
    //   크기 환산값이 통째로 덮어썼다.
    //   지금은 '점수를 어떻게 정할지'가 모드(AbsorbMode/PushMode)에만 있고
    //   적는 일은 NetEntity가 한다.
    //
    //   아래 GameState.CurrentScore는 내 화면 HUD용 예측치라 그대로 둔다.
    //   판정에 쓰이는 LanPlayerState.Score는 호스트의 방송으로만 채워진다.

    private bool IsLocalOwner
    {
        get
        {
            //PlayerBridge는 플레이어 프리팹에만 붙고, 그 루트엔 항상 NetIdentity가 있다.
            //없다면 배선 사고이므로 전역 상태를 건드리지 않는 쪽(false)이 안전하다
            return netId != null && netId.IsMine;
        }
    }

    private void Awake()
    {
        netId = GetComponentInParent<NetIdentity>();
        scaleController = GetComponentInChildren<PlayerScaleController>();
        colorVisual = GetComponentInChildren<PlayerColorVisual>();
        absorber = GetComponentInChildren<PlayerAbsorber>();
        movement = GetComponentInChildren<PlayerMovement>();

        // 풀(컨테이너)이 프리팹에 있으면 사용, 없으면 동적 생성
        levelUpFloaterPool = GetComponentInChildren<LevelUpFloaterPool>();
        if (levelUpFloaterPool == null)
        {
            var go = new GameObject("LevelUpFloaterPool");
            go.transform.SetParent(transform, false);
            levelUpFloaterPool = go.AddComponent<LevelUpFloaterPool>();
        }
    }

    private void OnEnable()
    {
        if (scaleController != null)
        {
            scaleController.OnGrowStarted += HandleGrowStarted;
            scaleController.OnScaleThresholdUp += HandleScaleThresholdUp;
            scaleController.OnScaleSettled += HandleScaleSettled;
        }

        if (colorVisual != null)
            colorVisual.OnColorApplied += HandleColorApplied;

        if (absorber != null)
            absorber.OnJellyScored += HandleJellyScored;
    }

    private void OnDisable()
    {
        if (scaleController != null)
        {
            scaleController.OnGrowStarted -= HandleGrowStarted;
            scaleController.OnScaleThresholdUp -= HandleScaleThresholdUp;
            scaleController.OnScaleSettled -= HandleScaleSettled;
        }

        if (colorVisual != null)
            colorVisual.OnColorApplied -= HandleColorApplied;

        if (absorber != null)
            absorber.OnJellyScored -= HandleJellyScored;
    }

    // ── Scale ──

    private void HandleGrowStarted(bool playEffect)
    {
        if (!playEffect)
            return;

        //'Level Up!' 팝업은 그 캐릭터 머리 위에 뜨는 월드 연출이라 남의 것도 보여준다
        if (levelUpFloaterPool != null)
            levelUpFloaterPool.Play();

        // ★ 효과음은 내 캐릭터일 때만
        //   [W6] 가드가 다른 핸들러에는 다 있는데 여기만 빠져 있었다.
        //   PlayerBridge는 원격 아바타에도 붙으므로, 남이 커질 때마다 내 스피커에서
        //   소리가 났다. 봇을 흡수하면 흡수자가 크게 자라(GrowByAbsorbing) 그 소리가
        //   양쪽 화면에서 겹쳐 들렸다.
        //   젤리는 GrowByJelly가 playEffect: false 로 묶어 보내서 티가 안 났을 뿐이다.
        if (!IsLocalOwner)
            return;

        if (PlaySFXAudio.Instance == null)
            return;

        PlaySFXAudio.Instance.PlayScaleUpSound();

        if (GameState.CurrentGameMode == GameModeType.Absorb)
            PlaySFXAudio.Instance.PlayColorMixSound();
    }

    // [LAN 이식] IsLocalOwner 가드 추가.
    //   GameState.OnCameraScaleIncreased는 static이라, 원격 캐릭터가 커져도
    //   내 카메라가 함께 줌되는 문제가 있었다. HandleScaleSettled에는 이미
    //   같은 가드가 있었는데(W6) 여기만 빠져 있었다.
    private void HandleScaleThresholdUp()
    {
        if (!IsLocalOwner)
            return;
        GameState.OnCameraScaleIncreased?.Invoke();
    }

    /// <summary>크기가 확정됐다 — 스폰 직후든 성장이 끝난 뒤든 같은 처리를 한다.</summary>
    private void HandleScaleSettled(float scaleValue)
    {
        if (!IsLocalOwner)
            return; // [W6] 원격 아바타가 로컬 전역 상태를 오염시키지 않게
        if (movement != null)
        {
            movement.JumpForce = scaleValue >= DataManager.Instance.JumpScaleThreshold
                ? movement.OriginalJumpForce + DataManager.Instance.IncreaseJumpForceValue
                : movement.OriginalJumpForce;
        }
        GameState.PlayerCurrentScale = scaleValue;

        GameState.CurrentScore = NetEntity.ScoreFromScale(scaleValue);
    }

    // ── Color ──

    private void HandleColorApplied(JellyColorType dominantType, RYBColor ryb, Color displayColor)
    {
        // [W6] 원격 아바타의 색은 색 스트림(networkColor)이 담당한다. 여기서 전역
        // GameState.CurrentDisplayColor(로컬 플레이어 색·SyncColor 소스)를 덮으면 내 색이 남의 색으로 샌다.
        if (!IsLocalOwner)
            return;
        GameState.CurrentDisplayColor = displayColor;
    }

    // ── Absorb ──

    private void HandleJellyScored()
    {
        //내 화면의 HUD만 갱신한다. 실제 점수는 호스트가 크기를 보고 따로 만든다
        if (!IsLocalOwner)
            return;

        var dm = DataManager.Instance;
        float predictedScale = scaleController != null
            ? scaleController.PendingScale
            : GameState.PlayerCurrentScale + dm.JellyScaleIncrease;
        
        GameState.CurrentScore = NetEntity.ScoreFromScale(predictedScale);

        if (UIPoolManager.Instance != null)
            UIPoolManager.Instance.SpawnUI(UIType.JellyEat);
        
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.PlayColorMixSound();
        
        if (levelUpFloaterPool != null)
            levelUpFloaterPool.Play();
    }
}
