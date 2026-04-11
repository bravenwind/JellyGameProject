//using System.Collections;
//using UnityEngine;
//using TMPro;

//[RequireComponent(typeof(TextMeshProUGUI))]
//public class JellyTyperTMP : MonoBehaviour
//{
//    [Header("Text Content")]
//    [TextArea(3, 10)]
//    public string textToType = "��~��~��~�ϰ� ��Ÿ���� �������";

//    [Header("Animation Timing")]
//    [Tooltip("�� ���ڰ� ��Ÿ���� �ð� ����")]
//    public float delayBetweenChars = 0.1f;
//    [Tooltip("�� ���ڰ� ������ Ŀ���� �� �ɸ��� �ð�")]
//    public float appearDuration = 0.5f;
//    [Tooltip("��� ��Ÿ�� �� ��� �ð�")]
//    public float holdDuration = 2.0f;
//    [Tooltip("�� ���ڰ� ������� �� �ɸ��� �ð�")]
//    public float disappearDuration = 0.4f;

//    [Header("Animation Feel (Curves)")]
//    [Tooltip("��Ÿ�� ���� ������ ��ȭ Ŀ�� (ź�� ������ ���� ���� ��¦ �÷��ּ���)")]
//    public AnimationCurve appearCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.7f, 1.2f), new Keyframe(1, 1));
//    [Tooltip("����� ���� ������ ��ȭ Ŀ��")]
//    public AnimationCurve disappearCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

//    private TMP_Text tmpText;
//    private Coroutine mainRoutine;

//    // �� ������ �ִϸ��̼� ���¸� �����ϱ� ���� �迭
//    private float[] charAnimationTimes;
//    private bool isAppearingPhase;

//    void Start()
//    {
//        tmpText = GetComponent<TMP_Text>();
//        tmpText.text = textToType;

//        // �߿�: TMP�� ������Ʈ���� �����ϵ��� �����ϰ� �ʱ�ȭ
//        tmpText.ForceMeshUpdate();

//        // ���� �迭 �ʱ�ȭ
//        charAnimationTimes = new float[tmpText.textInfo.characterCount];

//        // ���� �� ��� ���ڸ� �� ���̰�(������ 0) ����
//        InitializeTextVisibility(false);

//        PlayAnimation();
//    }

//    public void PlayAnimation()
//    {
//        if (mainRoutine != null) StopCoroutine(mainRoutine);
//        mainRoutine = StartCoroutine(TypewriterRoutine());
//    }

//    // �ʱ� ���� ���� (��� ����ų� ���̰�)
//    void InitializeTextVisibility(bool visible)
//    {
//        tmpText.ForceMeshUpdate();
//        TMP_TextInfo textInfo = tmpText.textInfo;
//        float initialScale = visible ? 1f : 0f;

//        for (int i = 0; i < textInfo.characterCount; i++)
//        //{
//            if (!textInfo.characterInfo[i].isVisible) continue;
//            ApplyScaleToCharacter(i, initialScale);
//        }
//        tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
//    }


//    IEnumerator TypewriterRoutine()
//    {
//        // 1. ���� ������
//        isAppearingPhase = true;
//        // �ð� �迭 �ʱ�ȭ (��� ���� �� ���·�)
//        for (int i = 0; i < charAnimationTimes.Length; i++) charAnimationTimes[i] = 0f;

//        int totalChars = tmpText.textInfo.characterCount;
//        float startTime = Time.time;

//        // ��� ������ �ִϸ��̼��� ���� ������ �ݺ�
//        while (true)
//        {
//            bool allFinished = true;
//            float elapsedTime = Time.time - startTime;

//            tmpText.ForceMeshUpdate(); // �߿�: �� ������ �޽� ������ ���� �غ�

//            for (int i = 0; i < totalChars; i++)
//            {
//                // �� ������ �ִϸ��̼� ���� �ð� ��� (������ ����)
//                float charStartTime = i * delayBetweenChars;

//                // ���� �� ���ڰ� ������ �ð��� �ƴϸ� �Ѿ
//                if (elapsedTime < charStartTime)
//                {
//                    allFinished = false;
//                    continue;
//                }

//                // �� ������ �ִϸ��̼� ���൵ ���
//                float currentDuration = elapsedTime - charStartTime;
//                float progress = Mathf.Clamp01(currentDuration / appearDuration);

//                charAnimationTimes[i] = progress;

//                if (progress < 1f) allFinished = false;

//                // Ŀ�긦 �����Ͽ� ���� ������ ���
//                float currentScale = appearCurve.Evaluate(progress);
//                ApplyScaleToCharacter(i, currentScale);
//            }

//            // ����� ���ؽ� �����͸� ������ ����
//            tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);

//            if (allFinished) break;
//            yield return null;
//        }

//        // 2. ��� ������
//        yield return new WaitForSeconds(holdDuration);

//        // 3. ���� ������
//        isAppearingPhase = false;
//        startTime = Time.time;

//        while (true)
//        {
//            bool allFinished = true;
//            float elapsedTime = Time.time - startTime;

//            tmpText.ForceMeshUpdate();

//            for (int i = 0; i < totalChars; i++)
//            {
//                float charStartTime = i * delayBetweenChars;

//                if (elapsedTime < charStartTime)
//                {
//                    allFinished = false;
//                    continue;
//                }

//                float currentDuration = elapsedTime - charStartTime;
//                float progress = Mathf.Clamp01(currentDuration / disappearDuration);

//                if (progress < 1f) allFinished = false;

//                // ������� Ŀ�� ����
//                float currentScale = disappearCurve.Evaluate(progress);
//                ApplyScaleToCharacter(i, currentScale);
//            }
//            tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);

//            if (allFinished) break;
//            yield return null;
//        }
//    }

//    // *** �ٽ� ���: ���� ������ �������� �����ϴ� �Լ� ***
//    void ApplyScaleToCharacter(int charIndex, float scale)
//    {
//        TMP_CharacterInfo charInfo = tmpText.textInfo.characterInfo[charIndex];

//        // �����̳� ������ �ʴ� ���ڴ� �ǳʶ�
//        if (!charInfo.isVisible) return;

//        int materialIndex = charInfo.materialReferenceIndex;
//        int vertexIndex = charInfo.vertexIndex;

//        // �ش� ������ 4�� ���ؽ� ���� ��������
//        Vector3[] sourceVertices = tmpText.textInfo.meshInfo[materialIndex].vertices;

//        // ������ �߽��� ��� (�밢�� ���ؽ��� �߰�)
//        Vector3 center = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

//        // �߽����� �������� ������ ����
//        Vector3[] destinationVertices = tmpText.textInfo.meshInfo[materialIndex].vertices;

//        destinationVertices[vertexIndex + 0] = center + (sourceVertices[vertexIndex + 0] - center) * scale;
//        destinationVertices[vertexIndex + 1] = center + (sourceVertices[vertexIndex + 1] - center) * scale;
//        destinationVertices[vertexIndex + 2] = center + (sourceVertices[vertexIndex + 2] - center) * scale;
//        destinationVertices[vertexIndex + 3] = center + (sourceVertices[vertexIndex + 3] - center) * scale;
//    }
//}