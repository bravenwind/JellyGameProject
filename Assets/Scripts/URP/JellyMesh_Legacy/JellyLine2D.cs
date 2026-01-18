using UnityEngine;

public class JellyLine2D : MonoBehaviour
{
    public Transform[] bones;
    LineRenderer lr;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
    }

    void LateUpdate()
    {
        for (int i = 0; i < bones.Length; i++)
            lr.SetPosition(i, bones[i].position);
    }
}
