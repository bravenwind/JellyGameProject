using System.Collections;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class JellyTyperTMP : MonoBehaviour
{
    [Header("Text Content")]
    [TextArea(3, 10)]
    public string textToType = "미~끄~덩~하게 나타났다 사라져요";

    [Header("Animation Timing")]
    [Tooltip("각 글자가 나타나는 시간 간격")]
    public float delayBetweenChars = 0.1f;
    [Tooltip("한 글자가 완전히 커지는 데 걸리는 시간")]
    public float appearDuration = 0.5f;
    [Tooltip("모두 나타난 후 대기 시간")]
    public float holdDuration = 2.0f;
    [Tooltip("한 글자가 사라지는 데 걸리는 시간")]
    public float disappearDuration = 0.4f;

    [Header("Animation Feel (Curves)")]
    [Tooltip("나타날 때의 스케일 변화 커브 (탄성 느낌을 위해 끝을 살짝 올려주세요)")]
    public AnimationCurve appearCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.7f, 1.2f), new Keyframe(1, 1));
    [Tooltip("사라질 때의 스케일 변화 커브")]
    public AnimationCurve disappearCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    private TMP_Text tmpText;
    private Coroutine mainRoutine;

    // 각 글자의 애니메이션 상태를 추적하기 위한 배열
    private float[] charAnimationTimes;
    private bool isAppearingPhase = true;

    void Start()
    {
        tmpText = GetComponent<TMP_Text>();
        tmpText.text = textToType;

        // 중요: TMP가 지오메트리를 생성하도록 강제하고 초기화
        tmpText.ForceMeshUpdate();

        // 상태 배열 초기화
        charAnimationTimes = new float[tmpText.textInfo.characterCount];

        // 시작 시 모든 글자를 안 보이게(스케일 0) 설정
        InitializeTextVisibility(false);

        PlayAnimation();
    }

    public void PlayAnimation()
    {
        if (mainRoutine != null) StopCoroutine(mainRoutine);
        mainRoutine = StartCoroutine(TypewriterRoutine());
    }

    // 초기 상태 설정 (모두 숨기거나 보이게)
    void InitializeTextVisibility(bool visible)
    {
        tmpText.ForceMeshUpdate();
        TMP_TextInfo textInfo = tmpText.textInfo;
        float initialScale = visible ? 1f : 0f;

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            if (!textInfo.characterInfo[i].isVisible) continue;
            ApplyScaleToCharacter(i, initialScale);
        }
        tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }


    IEnumerator TypewriterRoutine()
    {
        // 1. 등장 페이즈
        isAppearingPhase = true;
        // 시간 배열 초기화 (모두 시작 전 상태로)
        for (int i = 0; i < charAnimationTimes.Length; i++) charAnimationTimes[i] = 0f;

        int totalChars = tmpText.textInfo.characterCount;
        float startTime = Time.time;

        // 모든 글자의 애니메이션이 끝날 때까지 반복
        while (true)
        {
            bool allFinished = true;
            float elapsedTime = Time.time - startTime;

            tmpText.ForceMeshUpdate(); // 중요: 매 프레임 메쉬 데이터 갱신 준비

            for (int i = 0; i < totalChars; i++)
            {
                // 각 글자의 애니메이션 시작 시간 계산 (순차적 지연)
                float charStartTime = i * delayBetweenChars;

                // 아직 이 글자가 시작할 시간이 아니면 넘어감
                if (elapsedTime < charStartTime)
                {
                    allFinished = false;
                    continue;
                }

                // 이 글자의 애니메이션 진행도 계산
                float currentDuration = elapsedTime - charStartTime;
                float progress = Mathf.Clamp01(currentDuration / appearDuration);

                charAnimationTimes[i] = progress;

                if (progress < 1f) allFinished = false;

                // 커브를 적용하여 현재 스케일 계산
                float currentScale = appearCurve.Evaluate(progress);
                ApplyScaleToCharacter(i, currentScale);
            }

            // 변경된 버텍스 데이터를 실제로 적용
            tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);

            if (allFinished) break;
            yield return null;
        }

        // 2. 대기 페이즈
        yield return new WaitForSeconds(holdDuration);

        // 3. 퇴장 페이즈
        isAppearingPhase = false;
        startTime = Time.time;

        while (true)
        {
            bool allFinished = true;
            float elapsedTime = Time.time - startTime;

            tmpText.ForceMeshUpdate();

            for (int i = 0; i < totalChars; i++)
            {
                float charStartTime = i * delayBetweenChars;

                if (elapsedTime < charStartTime)
                {
                    allFinished = false;
                    continue;
                }

                float currentDuration = elapsedTime - charStartTime;
                float progress = Mathf.Clamp01(currentDuration / disappearDuration);

                if (progress < 1f) allFinished = false;

                // 사라지는 커브 적용
                float currentScale = disappearCurve.Evaluate(progress);
                ApplyScaleToCharacter(i, currentScale);
            }
            tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);

            if (allFinished) break;
            yield return null;
        }
    }

    // *** 핵심 기능: 개별 글자의 스케일을 조절하는 함수 ***
    void ApplyScaleToCharacter(int charIndex, float scale)
    {
        TMP_CharacterInfo charInfo = tmpText.textInfo.characterInfo[charIndex];

        // 공백이나 보이지 않는 문자는 건너뜀
        if (!charInfo.isVisible) return;

        int materialIndex = charInfo.materialReferenceIndex;
        int vertexIndex = charInfo.vertexIndex;

        // 해당 글자의 4개 버텍스 원본 가져오기
        Vector3[] sourceVertices = tmpText.textInfo.meshInfo[materialIndex].vertices;

        // 글자의 중심점 계산 (대각선 버텍스의 중간)
        Vector3 center = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

        // 중심점을 기준으로 스케일 적용
        Vector3[] destinationVertices = tmpText.textInfo.meshInfo[materialIndex].vertices;

        destinationVertices[vertexIndex + 0] = center + (sourceVertices[vertexIndex + 0] - center) * scale;
        destinationVertices[vertexIndex + 1] = center + (sourceVertices[vertexIndex + 1] - center) * scale;
        destinationVertices[vertexIndex + 2] = center + (sourceVertices[vertexIndex + 2] - center) * scale;
        destinationVertices[vertexIndex + 3] = center + (sourceVertices[vertexIndex + 3] - center) * scale;
    }
}