using UnityEngine;
using DG.Tweening;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class JellyCamera : MonoBehaviour
{
    [Header("젤리 카메라 연출 설정 (여기서 값을 조절하세요)")]

    [Tooltip("효과가 완전히 멈출 때까지 걸리는 시간(초 단위)입니다.\n" +
             "예: 1.5로 설정하면 1.5초 동안 출렁거립니다.")]
    public float duration = 1.5f;

    [Tooltip("화면이 처음 충격받을 때 얼마나 찌그러질지 정합니다.\n" +
             "마이너스(-) 값: 볼록 렌즈처럼 앞으로 튀어나옴\n" +
             "플러스(+) 값: 오목 렌즈처럼 뒤로 빨려 들어감")]
    public float strength = -0.7f;

    [Tooltip("정해진 시간 동안 몇 번이나 앞뒤로 진동할지 정합니다.\n" +
             "값이 클수록 더 방정맞게(?) 부르르 떨립니다. (권장: 5~10)")]
    public int vibrato = 5;

    [Tooltip("젤리의 쫀득한 정도(탄성)입니다.\n" +
             "1에 가까울수록 말랑한 물풍선처럼 띠용~ 하고 많이 튕기고,\n" +
             "0에 가까울수록 딱딱한 돌처럼 튕김이 적습니다.")]
    [Range(0f, 1f)] // 유니티 인스펙터에 0~1 사이 슬라이더가 생겨서 조절하기 쉬워집니다.
    public float elasticity = 1f;


    [Space(20)] // 인스펙터에서 줄 간격 띄우기
    [Header("필수 연결 항목 (건드리지 마세요)")]
    [Tooltip("씬에 있는 Global Volume 오브젝트를 여기에 끌어다 넣으세요.\n" +
             "주의: Volume 안에 Lens Distortion 효과가 꼭 추가되어 있어야 합니다!")]
    public Volume globalVolume;

    // --- 내부 변수 (인스펙터에 안 보임) ---
    private LensDistortion lensDistortion;
    private Camera cam;
    private float defaultFov;
    private Quaternion defaultRotation;

    void Start()
    {
        cam = GetComponent<Camera>();
        defaultFov = cam.fieldOfView;
        defaultRotation = cam.transform.localRotation;

        if (globalVolume.profile.TryGet(out lensDistortion))
            lensDistortion.intensity.overrideState = true;
    }

#if UNITY_EDITOR
    private void Update()
    {
        // [R3] P키 연출 테스트는 에디터 전용 — 빌드에서는 게임 중 P를 눌러도
        // 렌즈왜곡/FOV 연출이 오발동하지 않는다. (에디터 테스트는 ContextMenu로도 가능)
        if (Input.GetKeyDown(KeyCode.P))
            PlayDing();
    }
#endif

    // 인스펙터에서 스크립트 우클릭 -> Play Ding Effect 를 눌러서 테스트 가능
    [ContextMenu("Play Ding Effect")]
    public void PlayDing()
    {
        // 1. 혹시 실행 중인 효과가 있다면 중단하고 초기화 (안전장치)
        DOTween.Kill(cam);
        DOTween.Kill(lensDistortion);
        DOTween.Kill(cam.transform);

        cam.fieldOfView = defaultFov;
        lensDistortion.intensity.value = 0f;
        cam.transform.localRotation = defaultRotation;

        // ---------------- 애니메이션 시작 ----------------

        // 2. 화면이 살짝 당겨졌다가(FOV 펀치) 돌아오는 효과
        cam.DOFieldOfView(defaultFov - 5f, duration)
           .From(defaultFov)
           .SetEase(Ease.OutElastic);

        // 3. 렌즈가 꿀렁이는 효과 (Lens Distortion)
        Sequence seq = DOTween.Sequence();
        seq.Append(
            DOTween.To(() => lensDistortion.intensity.value,
                       x => lensDistortion.intensity.value = x,
                       strength, 0.15f).SetEase(Ease.OutQuad)
        );
        seq.Append(
            DOTween.To(() => lensDistortion.intensity.value,
                       x => lensDistortion.intensity.value = x,
                       0f, duration).SetEase(Ease.OutElastic, elasticity) // 설정한 elasticity 값 적용
        );

        seq.OnComplete(() => {
            lensDistortion.intensity.value = 0f;
        });

        // 4. 카메라가 양옆으로 기우뚱거리는 효과
        cam.transform.DOPunchRotation(new Vector3(0, 0, 3f), duration, vibrato, elasticity)
           .OnComplete(() => {
               cam.transform.localRotation = defaultRotation;
           });
    }
}