using UnityEngine;

public class PlayerAbsorbField : MonoBehaviour
{
    public float detectRadius = 6f;
    public LayerMask jellyLayer;
    public Transform detectTransform;

    private void Start()
    {
        detectRadius = GameState.DetectRadius;
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
                Debug.Log("Ÿ�� ������");
                jelly.StartAbsorb(transform);
            }
        }
    }

    // ���� �ð�ȭ (����׿�)
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        if (DataManager.Instance != null)
        {
            Gizmos.DrawWireSphere(detectTransform.position, GameState.DetectRadius);
        }
        else
        {
            Gizmos.DrawWireSphere(detectTransform.position, detectRadius);
        }
    }
}
