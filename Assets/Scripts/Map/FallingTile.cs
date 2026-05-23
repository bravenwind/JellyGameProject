using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class FallingTile : MonoBehaviour
{
    public void StartFall(float warningDuration, float fallDuration, float fallDistance, float delay)
    {
        StartCoroutine(FallRoutine(warningDuration, fallDuration, fallDistance, delay));
    }

    private IEnumerator FallRoutine(float warningDuration, float fallDuration,
        float fallDistance, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        Vector3 originalPos = transform.localPosition;
        Renderer rend = GetComponentInChildren<Renderer>();
        Color originalColor = Color.white;
        if (rend != null)
            originalColor = rend.material.color;

        NavMeshObstacle obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.shape = NavMeshObstacleShape.Box;
        BoxCollider box = GetComponentInChildren<BoxCollider>();
        if (box != null)
        {
            obstacle.size = box.size;
            obstacle.center = box.center;
        }

        // 위상을 타일 위치 기반으로 다르게 해서 옆 타일과 흔들림이 똑같이 보이지 않게 함
        float phase = (originalPos.x * 12.9898f + originalPos.z * 78.233f) % (Mathf.PI * 2f);

        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            float t = elapsed / warningDuration;
            // 시작부터 눈에 보이는 강도로, 시간이 갈수록 더 격하게
            float intensity = Mathf.Lerp(0.08f, 0.35f, t);
            Vector3 shake = new Vector3(
                Mathf.Sin(elapsed * 37f + phase) * intensity,
                Mathf.Abs(Mathf.Sin(elapsed * 53f + phase)) * intensity * 0.25f,
                Mathf.Sin(elapsed * 43f + phase) * intensity
            );
            transform.localPosition = originalPos + shake;

            if (rend != null)
                rend.material.color = Color.Lerp(originalColor, new Color(1f, 0.25f, 0.15f), t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPos;

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;
        Destroy(obstacle);

        // 실제로 Y축으로 떨어지는 단계 — 가속도 곡선
        elapsed = 0f;
        while (elapsed < fallDuration)
        {
            float t = elapsed / fallDuration;
            float fallY = fallDistance * (t * t);
            transform.localPosition = originalPos + Vector3.down * fallY;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 충분히 내려간 뒤에 비활성화
        transform.localPosition = originalPos + Vector3.down * fallDistance;
        gameObject.SetActive(false);
    }
}
