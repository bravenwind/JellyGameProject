using UnityEngine;
using DG.Tweening;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // URP 필수

public class JellyCamera : MonoBehaviour
{
    [Header("Settings")]
    public float duration = 1.5f;   // 딩~~ 하는 시간 (길수록 여운이 김)
    public float strength = -0.7f;  // 찌그러지는 강도 (음수면 뚱뚱, 양수면 홀쭉)
    public int vibrato = 5;         // 떨림 횟수 (클수록 많이 띠요옹~ 거림)
    public float elasticity = 1f;   // 탄성 (0~1, 1이면 고무줄, 0이면 딱딱)

    [Header("References")]
    public Volume globalVolume;
    private LensDistortion lensDistortion;
    private Camera cam;
    private float defaultFov;
    private Quaternion defaultRotation; // Start에서 저장해야 함

    void Start()
    {
        cam = GetComponent<Camera>();
        defaultFov = cam.fieldOfView;
        defaultRotation = cam.transform.localRotation; // 원래 회전값 저장 (추가)

        if (globalVolume.profile.TryGet(out lensDistortion))
        {
            lensDistortion.intensity.overrideState = true;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            PlayDing();
        }
    }

    // 테스트용: 인스펙터 우클릭 -> Play Ding Effect 선택
    [ContextMenu("Play Ding Effect")]
    public void PlayDing()
    {
        // 1. 기존 트윈 모두 즉시 중단 (이전 움직임 킬)
        DOTween.Kill(cam);
        DOTween.Kill(lensDistortion);
        DOTween.Kill(cam.transform);

        // 2. ★안전장치: 시작 전에 무조건 '원래 상태'로 강제 복구
        // 이걸 안 하면 연타했을 때 화면이 뒤틀린 채로 시작함
        cam.fieldOfView = defaultFov;
        lensDistortion.intensity.value = 0f;
        cam.transform.localRotation = defaultRotation;

        // ---------------- 애니메이션 시작 ----------------

        // 3. 카메라 FOV 펀치
        cam.DOFieldOfView(defaultFov - 5f, duration)
           .From(defaultFov) // From을 명시하면 더 안정적
           .SetEase(Ease.OutElastic);

        // 4. 렌즈 왜곡 (꿀렁임)
        Sequence seq = DOTween.Sequence();
        seq.Append(
            DOTween.To(() => lensDistortion.intensity.value,
                       x => lensDistortion.intensity.value = x,
                       strength, 0.15f).SetEase(Ease.OutQuad)
        );
        seq.Append(
            DOTween.To(() => lensDistortion.intensity.value,
                       x => lensDistortion.intensity.value = x,
                       0.1f, duration).SetEase(Ease.OutElastic, 1.2f) // ★수정: 0.1f가 아니라 0f여야 깔끔하게 돌아옴
        );

        // 시퀀스가 끝났을 때도 확실하게 0으로 고정 (부동소수점 오차 방지)
        seq.OnComplete(() => {
            lensDistortion.intensity.value = 0f;
        });

        // 5. 카메라 회전
        cam.transform.DOPunchRotation(new Vector3(0, 0, 3f), duration, vibrato, elasticity)
           .OnComplete(() => {
               // 끝나면 회전도 확실하게 원위치
               cam.transform.localRotation = defaultRotation;
           });
    }
}