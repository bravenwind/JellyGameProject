using UnityEngine;
using TMPro;
using System;
using System.Collections;

/// <summary>
/// 성장(젤리 흡수 / 배트 적중) 시 캐릭터 주위에 팝 하고 떴다가 사라지는 팝업 하나.
/// <b>팝업 프리팹에 부착해 쓴다</b> — 인스턴스 하나가 프리팹 하나다.
/// 꺼내고 넣는 일은 LevelUpFloaterPool이 하고, Play()가 끝나면 onComplete로 풀에 돌아간다.
///
/// ★ 글자의 생김새는 이 코드가 정하지 않는다 — 프리팹이 정한다
///   예전엔 EnsureText()가 TMP 컴포넌트를 <b>런타임에 AddComponent로 만들고</b>
///   글자·크기·색·외곽선까지 코드로 칠했다. 그래서
///     · 문구 하나 바꾸려면 스크립트를 고쳐야 했고
///     · 팝업이 한 종류밖에 될 수 없었다(코드가 한 벌의 값만 갖고 있으니까)
///     · [SerializeField]로 뚫어놓은 스타일 값들은 프리팹이 없으니 만질 자리도 없었다
///   지금은 프리팹마다 다른 문구·색·폰트를 넣고 풀이 그중 하나를 무작위로 고른다.
///   이 스크립트에 남은 건 "어디에 놓고 어떻게 움직일지"뿐이다.
/// </summary>
[DisallowMultipleComponent]
public class LevelUpFloater : MonoBehaviour
{
    [Tooltip("이 팝업의 글자. 비워두면 자식에서 찾는다.")]
    [SerializeField] private TextMeshPro tmp;

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

    [Header("등장 위치 (플레이어 기준)")]
    [Tooltip("플레이어 중심에서 이만큼 떨어진 곳에 무작위로 뜬다")]
    [SerializeField] private float spreadRadius = 1.1f;

    [Tooltip("플레이어 중심에서 위로 올린 높이")]
    [SerializeField] private float height = 1.6f;

    private Transform scaleRef;   // 텍스트 크기 상쇄 기준(플레이어 루트)
    private Camera cam;
    private Action<LevelUpFloater> onComplete;
    private Coroutine routine;
    private Color baseColor = Color.white;   // 프리팹이 정한 색. 알파만 이 스크립트가 건드린다.

    private void Awake()
    {
        //프리팹 루트에 TMP가 있는 게 보통이지만 자식에 두는 구성도 허용한다.
        if (tmp == null)
            tmp = GetComponentInChildren<TextMeshPro>(true);

        if (tmp != null)
            baseColor = tmp.color;

        gameObject.SetActive(false);   // 풀에서 꺼낼 때 활성화된다
    }

    /// <summary>풀에서 호출. scaleRef는 크기 상쇄 기준(플레이어 루트), onComplete는 반환 콜백.</summary>
    public void Play(Transform scaleRef, Action<LevelUpFloater> onComplete)
    {
        this.scaleRef = scaleRef;
        this.onComplete = onComplete;
        cam = Camera.main;

        gameObject.SetActive(true);
        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        // ★ 자리를 <b>한 번만</b> 정하고 그대로 둔다
        //   원 위의 임의 각도 × 임의 거리. 각도만 무작위로 뽑으면 전부 반지름 끝에
        //   붙어 고리 모양이 되므로 거리도 함께 흩뿌린다.
        //   (제곱근을 씌우면 원 안에 고르게 퍼지는데, 여기선 바깥쪽이 조금 더 잦은
        //    편이 캐릭터를 덜 가려서 그냥 선형으로 둔다)
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float radius = UnityEngine.Random.Range(spreadRadius * 0.35f, spreadRadius);
        Vector3 spot = new Vector3(Mathf.Cos(angle) * radius,
                                   height + Mathf.Sin(angle) * radius * 0.45f,
                                   0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (tmp == null)
                break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (cam == null)
                cam = Camera.main;

            // 부모(플레이어)가 커져도 글자 크기는 그대로여야 한다
            float parentScale = scaleRef != null
                ? Mathf.Max(scaleRef.localScale.x, 0.01f) : 1f;

            //위치는 고정. 부모 로컬 기준 원시 오프셋(스케일 곱 X) — NameTagBillboard와 같은 패턴.
            transform.localPosition = spot;

            // 빌보드: 카메라 정면
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - cam.transform.position);

            // ── 팝 하고 나타났다가 작아지며 사라진다 ──
            //   0 ~ 25%   : 0 → popScale   (튀어나옴)
            //   25 ~ 40%  : popScale → 1   (되튐)
            //   40 ~ 100% : 1 → endScale   (줄어들며 퇴장)
            float scale;
            if (t < 0.25f)
                scale = Mathf.Lerp(0f, popScale, EaseOutCubic(t / 0.25f));
            else if (t < 0.40f)
                scale = Mathf.Lerp(popScale, 1f, (t - 0.25f) / 0.15f);
            else
                scale = Mathf.Lerp(1f, endScale, EaseInCubic((t - 0.40f) / 0.60f));

            transform.localScale = Vector3.one / parentScale * scale;

            //알파는 끝에서만 뺀다. 크기가 줄어드는 게 주연이라 일찍 흐려지면 둘 다 약해진다.
            //색 자체는 프리팹이 정한 것을 그대로 쓴다.
            Color c = baseColor;
            c.a = t < 0.7f ? baseColor.a : Mathf.Lerp(baseColor.a, 0f, (t - 0.7f) / 0.3f);
            tmp.color = c;

            yield return null;
        }

        routine = null;
        gameObject.SetActive(false);
        onComplete?.Invoke(this);   // 풀에 반환
    }

    private static float EaseOutCubic(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
    private static float EaseInCubic(float x) => x * x * x;
}
