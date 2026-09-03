using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// 성장(젤리 흡수 / 봇 흡수 / 배트 적중) 시 캐릭터 주위에 팝 하고 떴다가 사라지는 팝업 하나.
/// <b>팝업 프리팹에 부착해 쓴다</b> — 인스턴스 하나가 프리팹 하나다.
/// 꺼내고 넣는 일은 LevelUpFloaterPool이 하고, Play()가 끝나면 onComplete로 풀에 돌아간다.
///
/// ★ 생김새는 이 코드가 정하지 않는다 — 프리팹이 정한다
///   예전엔 TMP 컴포넌트를 <b>런타임에 AddComponent로 만들고</b> 글자·크기·색·외곽선까지
///   코드로 칠했다. 그래서 팝업이 한 종류밖에 될 수 없었고, 문구를 바꾸려면 스크립트를
///   고쳐야 했다. 지금 이 스크립트에 남은 건 "어디에 놓고 어떻게 움직일지"뿐이다.
///
/// ★ 팝업은 월드 스프라이트다 — UI(Graphic) 갈래는 지웠다
///   한때 Image·TextMeshPro도 받으려고 Graphic을 함께 찾았다. 그런데 팝업 프리팹
///   셋(jellyEatPopup_Grow/Squishy/Yum)은 전부 SpriteRenderer 하나만 들고 있어
///   그 갈래는 한 번도 돌지 않았다. UI 팝업은 캔버스가 있어야 하는데 이 컴포넌트는
///   캐릭터의 자식으로 붙으므로 애초에 성립하지도 않았다.
///
/// ★ 크기도 프리팹의 값을 <b>배율로</b> 쓴다
///   예전엔 매 프레임 localScale에 1을 통째로 써넣어서, 프리팹이 정해둔 크기를 덮어썼다.
///   이미지 팝업은 원본이 수백 픽셀이라 그렇게 하면 화면을 다 덮는다.
///   지금은 Awake에서 프리팹의 스케일을 기억해두고 거기에 애니메이션 배율만 곱한다.
/// </summary>
[DisallowMultipleComponent]
public class LevelUpFloater : MonoBehaviour
{
    // ★ 예전엔 위로 떠오르며 페이드아웃했다
    //   지금은 <b>팝 하고 나타났다가 작아지며 사라진다.</b> 위로 흘러가지 않으니
    //   시선이 따라갈 필요가 없고, 여러 개가 동시에 떠도 서로 밀려나지 않는다.
    [Header("애니메이션")]
    [Tooltip("한 번 뜨고 사라지기까지의 시간")]
    [SerializeField] private float duration = 0.55f;

    [Tooltip("등장할 때 부풀어 오르는 최대 배율")]
    [SerializeField] private float popScale = 1.3f;

    [Tooltip("사라질 때 줄어드는 최소 배율. 0이면 완전히 사라진다.")]
    [SerializeField] private float endScale = 0.15f;

    [Header("등장 위치 (캐릭터 기준)")]
    [Tooltip("캐릭터 중심에서 이만큼 떨어진 곳에 무작위로 뜬다")]
    [SerializeField] private float spreadRadius = 1.1f;

    [Tooltip("캐릭터 중심에서 위로 올린 높이")]
    [SerializeField] private float height = 1.6f;

    private Transform scaleRef;      // 크기 상쇄 기준(캐릭터 루트)
    private Camera cam;
    private Action<LevelUpFloater> onComplete;
    private Coroutine routine;

    private SpriteRenderer sprite;   // 색·알파를 여기에 쓴다
    private Color baseColor = Color.white;
    private Vector3 baseScale = Vector3.one;

    private void Awake()
    {
        baseScale = transform.localScale;

        sprite = GetComponentInChildren<SpriteRenderer>(true);
        if (sprite != null)
            baseColor = sprite.color;
    }

    // ★ 켜고 끄는 건 풀이 한다 — 여기서는 건드리지 않는다
    //   예전엔 Awake와 애니메이션 끝에서 각각 SetActive를 불렀다. 지금은
    //   ComponentPool이 Get에서 켜고 Return에서 끄므로 출처가 둘이 된다.
    //   둘이 되면 "왜 꺼진 채로 나오지" 같은 걸 두 곳에서 찾게 된다.

    /// <summary>풀에서 호출. scaleRef는 크기 상쇄 기준(캐릭터 루트), onComplete는 반환 콜백.</summary>
    public void Play(Transform scaleRef, Action<LevelUpFloater> onComplete)
    {
        this.scaleRef = scaleRef;
        this.onComplete = onComplete;
        cam = Camera.main;

        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        // ★ 자리를 <b>한 번만</b> 정하고 그대로 둔다
        //   원 위의 임의 각도 × 임의 거리. 각도만 무작위로 뽑으면 전부 반지름 끝에
        //   붙어 고리 모양이 되므로 거리도 함께 흩뿌린다.
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float radius = UnityEngine.Random.Range(spreadRadius * 0.35f, spreadRadius);
        Vector3 spot = new Vector3(Mathf.Cos(angle) * radius,
                                   height + Mathf.Sin(angle) * radius * 0.45f,
                                   0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (cam == null)
                cam = Camera.main;

            // 캐릭터가 커져도 팝업 크기는 그대로여야 한다
            float parentScale = scaleRef != null
                ? Mathf.Max(scaleRef.localScale.x, 0.01f) : 1f;

            //위치는 고정. 부모 로컬 기준 원시 오프셋(스케일 곱 X) — NameTagBillboard와 같은 패턴.
            transform.localPosition = spot;

            // ★ 빌보드는 '카메라를 바라보기'가 아니라 '화면과 나란히 세우기'다
            //   예전엔 LookRotation(카메라 → 나)이었다. 그러면 팝업마다 카메라가 있는
            //   <b>한 점</b>을 향하는 서로 다른 회전이 나오고, 그 회전은 화면 중앙에서
            //   벗어날수록 화면 평면에서 더 많이 기운다. 이 게임의 메인 카메라는
            //   직교(orthographic size 25)라 더 나쁘다 — 직교에서는 시선이 전부
            //   평행한데 팝업만 한 점을 향해 돌아가므로, 기운 만큼 그대로
            //   찌그러져 보인다. 화면 가장자리로 갈수록 심해진다.
            //
            //   카메라 회전을 그대로 복사하면 모든 팝업이 근평면과 나란해져
            //   화면 어디에 있든 똑같이 반듯하게 보인다.
            //   NameTagBillboard가 같은 증상("가장자리로 갈수록 이름표가 기울어
            //   보였다")을 겪고 이미 이 방식으로 고쳤다 — 여기만 옛 방식이 남아 있었다.
            if (cam != null)
                transform.rotation = cam.transform.rotation;

            float k;
            if (t < 0.25f)
                k = Mathf.Lerp(0f, popScale, EaseOutCubic(t / 0.25f));
            else if (t < 0.40f)
                k = Mathf.Lerp(popScale, 1f, (t - 0.25f) / 0.15f);
            else
                k = Mathf.Lerp(1f, endScale, EaseInCubic((t - 0.40f) / 0.60f));

            transform.localScale = baseScale * (k / parentScale);

            //알파는 끝에서만 뺀다. 크기가 줄어드는 게 주연이라 일찍 흐려지면 둘 다 약해진다.
            //색 자체는 프리팹이 정한 것을 그대로 쓴다.
            float alpha = t < 0.7f ? baseColor.a
                                   : Mathf.Lerp(baseColor.a, 0f, (t - 0.7f) / 0.3f);
            ApplyAlpha(alpha);

            yield return null;
        }

        routine = null;
        onComplete?.Invoke(this);   // 풀에 반환 — 끄는 건 풀이 한다
    }

    private void ApplyAlpha(float alpha)
    {
        Color c = baseColor;
        c.a = alpha;

        if (sprite != null)
            sprite.color = c;
    }

    private static float EaseOutCubic(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
    private static float EaseInCubic(float x) => x * x * x;
}
