using UnityEngine;

public class PlayerAbsorbField : MonoBehaviour
{
    public float detectRadius = 6f;
    public LayerMask jellyLayer;
    public Transform detectTransform;

    private void Start()
    {
        detectRadius = DataManager.Instance.detectRadius;
    }

    public void Update()
    {
        Collider[] hits = Physics.OverlapSphere(
           detectTransform.position,
           detectRadius,
           jellyLayer
       );

        foreach (Collider hit in hits)
        {
            JellyColliderAbsorb jelly = hit.GetComponent<JellyColliderAbsorb>();
            if (jelly != null)
            {
                //Transform target = transform;
                //target.position = transform.position + ;
                Debug.Log("타겟 설정됨");
                jelly.StartAbsorb(transform);
            }
        }
    }

    // 범위 시각화 (디버그용)
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        if (DataManager.Instance != null)
        {
            Gizmos.DrawWireSphere(detectTransform.position, DataManager.Instance.detectRadius);
        }
        else
        {
            Gizmos.DrawWireSphere(detectTransform.position, detectRadius);
        }
    }
}
