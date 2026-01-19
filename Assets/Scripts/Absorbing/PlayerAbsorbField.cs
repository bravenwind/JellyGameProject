using UnityEngine;

public class PlayerAbsorbField : MonoBehaviour
{
    public float absorbRadius = 6f;
    public LayerMask jellyLayer;
    public Transform meshTransform;

    public void OnAbsorbBtnClicked()
    {
        Collider[] hits = Physics.OverlapSphere(
           meshTransform.position,
           absorbRadius,
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
                jelly.StartAbsorb(meshTransform);
            }
        }
    }

    // 범위 시각화 (디버그용)
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(meshTransform.position, absorbRadius);
    }
}
