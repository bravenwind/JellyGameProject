using System.Collections;
using UnityEngine;
using System; // Action�� ����ϱ� ���� �ʿ�

public class Rotator : MonoBehaviour
{

    [Tooltip("ȸ�� �ӵ� �׷��� (�ν����Ϳ��� ����)")]
    // �⺻������ EaseInOut ��� �����մϴ�.
    public AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

#pragma warning disable 0414
    private Coroutine rotateCoroutine;
#pragma warning restore 0414

    /// <summary>
    /// �ܺ� ��ũ��Ʈ���� ȣ���� �Լ��Դϴ�.
    /// </summary>
    /// <param name="onComplete">ȸ���� ���� �� ������ �ݹ�(���� ����)</param>

    public IEnumerator RotateRoutine(float duration, Action onComplete)
    {
        float elapsed = 0f;

        // ���� Y���� ����
        float startY = transform.eulerAngles.z;
        // ��ǥ Y���� (���� + 360��)
        float targetY = startY + 360f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // 0���� 1 ������ �����(t) ���
            float t = elapsed / duration;

            // Ŀ�긦 ���� ����/����/��� ������ ������ (0 ~ 1)
            float curveValue = rotationCurve.Evaluate(t);

            // ���� ������ ��ǥ ���� ���̸� Ŀ�� ���� ���� ����
            float currentAngle = Mathf.Lerp(startY, targetY, curveValue);

            // ȸ�� ����
            Vector3 currentRot = transform.eulerAngles;
            currentRot.z = currentAngle;
            transform.eulerAngles = currentRot;

            yield return null;
        }

        // ���� ���� �� ��Ȯ�� ������ ����
        Vector3 finalRot = transform.eulerAngles;
        finalRot.z = targetY;
        transform.eulerAngles = finalRot;

        rotateCoroutine = null;

        // �Ϸ� �� ������ ������ �ִٸ� ����
        onComplete?.Invoke();
    }
}