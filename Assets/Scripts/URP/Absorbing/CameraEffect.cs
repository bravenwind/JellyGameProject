using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // URP 쓴다고 가정

public class CameraEffect : MonoBehaviour
{
    public Volume globalVolume;
    private LensDistortion lensDistortion;

    void Start()
    {
        // 볼륨에서 렌즈 왜곡 컴포넌트 가져오기
        globalVolume.profile.TryGet(out lensDistortion);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            //PlayJellyEffect();
        }
    }

    public void PlayJellyEffect()
    {
        Debug.Log("딩~~~~~ (출렁)");

        // 기존 애니메이션이 있다면 꼬이지 않게 먼저 죽임 (안전장치)
        DOTween.Kill(lensDistortion.intensity);

        // 1단계: 쑤우우욱 (충격)
        // 시간: 0.3초 -> 0.4초 (약간 더 묵직하게)
        // 강도: -0.5 -> -0.7 (더 깊게 찌그러짐)
        DOTween.To(() => lensDistortion.intensity.value,
                   x => lensDistortion.intensity.value = x,
                   -0.7f, 0.4f)
                   .SetEase(Ease.OutQuad) // OutBack보다 OutQuad가 처음에 묵직하게 들어감
                   .OnComplete(() => {
                       // 2단계: 딩~~~~ (여운)
                       // 시간: 0.5초 -> 1.5초 (핵심! 시간이 길어야 출렁거림이 보임)
                       // Ease: OutElastic (고무줄 튕기기)
                       DOTween.To(() => lensDistortion.intensity.value,
                                  x => lensDistortion.intensity.value = x,
                                  0.1f, 1.5f)
                                  .SetEase(Ease.OutElastic);
                   });
    }
}