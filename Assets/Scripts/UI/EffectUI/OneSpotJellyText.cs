using System.Collections;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class OneSpotJellyText : MonoBehaviour
{
    [Header("Settings")]
    public string fullText = "대~박~사~건"; // 출력할 전체 문장

    [Header("Timing")]
    [Tooltip("글자가 떠 있는 시간")]
    public float displayDuration = 0.3f;
    [Tooltip("글자 바뀔 때 간격 (0이면 바로 바뀜)")]
    public float interval = 0.05f;

    [Header("Jelly Animation")]
    [Tooltip("커질 때 (등장) 속도")]
    public float popSpeed = 10f;
    [Tooltip("작아질 때 (퇴장) 속도")]
    public float shrinkSpeed = 15f;

    // N자 곡선: 0에서 시작 -> 1.3(띠용) -> 1.0(안착)
    public AnimationCurve popCurve = new AnimationCurve(
        new Keyframe(0, 0),
        new Keyframe(0.7f, 1.3f),
        new Keyframe(1, 1)
    );

    private TextMeshProUGUI tmpText;
    private char[] charArray; // 글자를 저장할 배열

    void Start()
    {
        tmpText = GetComponent<TextMeshProUGUI>();

        // 1. 문자열을 문자 배열로 변환하여 저장
        charArray = fullText.ToCharArray();

        // 시작 시 텍스트 비우기
        tmpText.text = "";

        // 애니메이션 시작
        StartCoroutine(ShowTextOneByOne());
    }

    IEnumerator ShowTextOneByOne()
    {
        // 배열에 저장된 글자를 하나씩 순회
        foreach (char letter in charArray)
        {
            // 공백은 애니메이션 없이 그냥 시간만 보냄
            if (char.IsWhiteSpace(letter))
            {
                tmpText.text = "";
                yield return new WaitForSeconds(displayDuration);
                continue;
            }

            // 2. 글자 교체
            tmpText.text = letter.ToString();

            // 3. 등장 애니메이션 (Pop In)
            yield return StartCoroutine(AnimateScale(true));

            // 보여주는 시간 대기
            yield return new WaitForSeconds(displayDuration);

            // 4. 퇴장 애니메이션 (Shrink Out)
            yield return StartCoroutine(AnimateScale(false));

            // 다음 글자 나오기 전 잠깐 대기
            if (interval > 0) yield return new WaitForSeconds(interval);
        }

        // 끝난 후 처리 (원하면 마지막 글자 유지하거나 비우기)
        tmpText.text = "";
    }

    // 스케일 조절 코루틴 (등장/퇴장 공용)
    IEnumerator AnimateScale(bool isAppearing)
    {
        float t = 0;

        while (t < 1f)
        {
            float speed = isAppearing ? popSpeed : shrinkSpeed;
            t += Time.deltaTime * speed;
            float progress = Mathf.Clamp01(t);

            float scaleValue;

            if (isAppearing)
            {
                // 등장: Curve 사용 (띠~용 효과)
                scaleValue = popCurve.Evaluate(progress);
            }
            else
            {
                // 퇴장: 1에서 0으로 줄어듦
                scaleValue = Mathf.Lerp(1f, 0f, progress);
            }

            // 전체 오브젝트의 스케일을 조절 (같은 자리 효과)
            transform.localScale = Vector3.one * scaleValue;

            yield return null;
        }
    }
}