using UnityEngine;
using JellyNet;

/// <summary>
/// 내 캐릭터가 자랐을 때 <b>내 기계에서만</b> 일어나야 하는 반응을 모은다 —
/// HUD 점수, 카메라 줌아웃, 점프력, 효과음. 사람 프리팹에만 붙는다.
///
/// ★ 이름이 PlayerBridge였다
///   BotBridge와 짝을 이루던 이름인데 그쪽이 사라지면서 'Bridge'가 무엇과
///   대비되는지 없어졌다. "무엇을 무엇에 잇는가"를 아무도 말해주지 않으니
///   <b>새 반응을 어디에 붙일지</b>가 매번 판단이 됐다.
///
///   실체는 아래 핸들러 넷이 전부 IsLocalOwner로 갈린다는 것 하나다. 그래서
///   게이트의 이름을 클래스 이름으로 올렸다. 이제 규칙이 이름에서 바로 나온다:
///     · 내 화면·내 전역 상태에만 영향을 주는 반응  → 여기
///     · 모든 기계에서 그 캐릭터 옆에 나야 하는 월드 연출
///       → 그 캐릭터의 컴포넌트가 방송 지점을 직접 듣는다 (LevelUpFloaterPool)
///
///   성장 팝업이 여기 없는 이유도 같은 문장으로 설명된다 — 팝업은 소유와 무관하고
///   봇에게도 떠야 하는데, 이 컴포넌트는 사람 프리팹에만 있다.
/// </summary>
public class LocalOwnerFeedback : MonoBehaviour
{
    private PlayerScaleController scaleController;
    private PlayerAbsorber absorber;
    private PlayerMovement movement;

    // [CBT-4/W6] 이 아바타가 로컬 소유인가. 이 컴포넌트는 로컬·원격 플레이어 프리팹 모두에 붙고
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
            //이 컴포넌트는 플레이어 프리팹에만 붙고, 그 루트엔 항상 NetIdentity가 있다.
            //없다면 배선 사고이므로 전역 상태를 건드리지 않는 쪽(false)이 안전하다
            return netId != null && netId.IsMine;
        }
    }

    private void Awake()
    {
        netId = GetComponentInParent<NetIdentity>();
        scaleController = GetComponentInChildren<PlayerScaleController>();
        absorber = GetComponentInChildren<PlayerAbsorber>();
        movement = GetComponentInChildren<PlayerMovement>();

        // ★ 팝업은 여기서 다루지 않는다
        //   예전엔 이 클래스가 LevelUpFloaterPool을 찾아 들고 Play()를 시켰다.
        //   그런데 이 클래스는 <b>사람 전용</b>이라 봇에는 팝업이 아예 없었고,
        //   BotBridge에 같은 코드를 복사하면 같은 일이 두 군데가 된다.
        //   지금은 풀이 스스로 PlayerScaleController.OnGrowStarted를 구독한다.
    }

    private void OnEnable()
    {
        if (scaleController != null)
        {
            scaleController.OnGrowStarted += HandleGrowStarted;
            scaleController.OnScaleThresholdUp += HandleScaleThresholdUp;
            scaleController.OnScaleSettled += HandleScaleSettled;
        }

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

        if (absorber != null)
            absorber.OnJellyScored -= HandleJellyScored;
    }

    // ── Scale ──

    private void HandleGrowStarted(bool playEffect)
    {
        if (!playEffect)
            return;

        //팝업은 LevelUpFloaterPool이 같은 이벤트를 직접 듣는다. 여기는 소리만 맡는다.

        // ★ 효과음은 내 캐릭터일 때만
        //   [W6] 가드가 다른 핸들러에는 다 있는데 여기만 빠져 있었다.
        //   이 컴포넌트는 원격 아바타에도 붙으므로, 남이 커질 때마다 내 스피커에서
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

    // ★ 색 구독을 걷어냈다
    //   HandleColorApplied가 하던 일은 GameState.CurrentDisplayColor에 색을 적는 것
    //   하나뿐이었는데, 그 값을 읽는 곳이 CurrentStatusUI(죽은 HUD)뿐이었다.
    //   HUD가 사라지면서 '아무도 안 읽는 값에 쓰기 위한 구독'만 남아 함께 지웠다.
    //   화면에 보이는 색은 PlayerColorVisual이 직접 칠하고, 원격 아바타는 색 스트림이
    //   담당한다 — 이 경유지는 처음부터 없어도 되는 자리였다.

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

        // ★ 팝업은 여기서 띄우지 않는다 — LevelUpFloaterPool이 같은 이벤트를 직접 듣는다
        //   팝업은 사람·봇 모두, 모든 기계에서 떠야 하는 월드 연출이다.
        //   반면 이 함수는 IsLocalOwner로 막혀 있어 <b>내 캐릭터에서만</b> 실행된다.
        //   같은 이벤트에 성격이 다른 두 반응을 한 함수에 묶으면 가드가 서로를 오염시킨다.
        //   소리는 내 것만 나야 하므로 여기 남는다.
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.PlayColorMixSound();
    }
}
