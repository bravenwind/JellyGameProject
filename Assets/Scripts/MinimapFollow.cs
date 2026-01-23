using UnityEngine;

public class MinimapFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float height = 30f;
    [SerializeField] bool rotateWithTarget = false;

    void LateUpdate()
    {
        if (!target) return;

        Vector3 pos = target.position;
        pos.y += height;
        transform.position = pos;

        if (rotateWithTarget)
        {
            transform.rotation = Quaternion.Euler(90f, target.eulerAngles.y, 0f);
        }
        else
        {
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}
