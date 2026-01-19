using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CameraEffect : MonoBehaviour
{
    public Volume globalVolume;
    private LensDistortion lensDistortion;

    [Header("Jelly Effect Settings")]
    [Tooltip("1단계: 충격 시 찌그러지는 강도 (음수 값 권장, 예: -0.7)")]
    public float hitIntensity = -0.7f;

    [Tooltip("1단계: 충격 강도까지 도달하는 시간 (짧을수록 강한 타격감)")]
    public float hitDuration = 0.4f;

    [Space(10)] // 인스펙터에서 줄 간격 띄우기
    [Tooltip("2단계: 원래대로 돌아오며 출렁거리는 시간 (길수록 젤리 느낌)")]
    public float recoveryDuration = 1.5f;

    [Tooltip("2단계: 돌아올 때의 애니메이션 타입 (OutElastic 추천)")]
    public Ease recoveryEase = Ease.OutElastic;

    void Start()
    {
        // 볼륨에서 렌즈 왜곡 컴포넌트 가져오기
        if (!globalVolume.profile.TryGet(out lensDistortion))
        {
            Debug.LogWarning("[CameraEffect] Global Volume에 Lens Distortion 컴포넌트가 없습니다!");
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            PlayJellyEffect();
        }
    }

    public void PlayJellyEffect()
    {
        if (lensDistortion == null) return;

        Debug.Log("딩~~~~~ (출렁)");

        // 기존에 실행 중이던 intensity 관련 트윈이 있다면 즉시 중단 (중복 실행 꼬임 방지)
        DOTween.Kill(lensDistortion.intensity);

        // --- 1단계: 쑤우우욱 (충격) ---
        // 지정된 hitIntensity와 hitDuration 사용
        DOTween.To(() => lensDistortion.intensity.value,
                   x => lensDistortion.intensity.value = x,
                   hitIntensity,
                   hitDuration)
                   .SetEase(Ease.OutQuad) // 들어갈 때는 묵직하게(OutQuad)
                   .OnComplete(() =>
                   {
                       // --- 2단계: 딩~~~~ (여운/복구) ---
                       // 다시 0(기본 상태)으로 돌아옴
                       // 지정된 recoveryDuration과 recoveryEase 사용
                       DOTween.To(() => lensDistortion.intensity.value,
                                  x => lensDistortion.intensity.value = x,
                                  0f, // 보통 왜곡이 없는 상태는 0입니다. (기존 0.1f -> 0f 수정)
                                  recoveryDuration)
                                  .SetEase(recoveryEase);
                   });
    }
}