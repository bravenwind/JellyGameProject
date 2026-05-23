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

        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            float t = elapsed / warningDuration;
            float intensity = Mathf.Lerp(0.01f, 0.12f, t);
            Vector3 shake = new Vector3(
                Mathf.Sin(elapsed * 37f) * intensity,
                Mathf.Sin(elapsed * 53f) * intensity * 0.3f,
                Mathf.Sin(elapsed * 43f) * intensity
            );
            transform.localPosition = originalPos + shake;

            if (rend != null)
                rend.material.color = Color.Lerp(originalColor, new Color(1f, 0.3f, 0.2f), t * 0.6f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPos;

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;
        Destroy(obstacle);

        elapsed = 0f;
        while (elapsed < fallDuration)
        {
            float t = elapsed / fallDuration;
            t = t * t;
            transform.localPosition = originalPos + Vector3.down * (fallDistance * t);

            if (rend != null)
            {
                Color c = rend.material.color;
                c.a = 1f - t;
                rend.material.color = c;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        gameObject.SetActive(false);
    }
}
