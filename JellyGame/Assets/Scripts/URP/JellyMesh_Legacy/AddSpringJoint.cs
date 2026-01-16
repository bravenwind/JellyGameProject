using UnityEngine;

public class AddSpringJoint : MonoBehaviour
{
    void Start()
    {
        Rigidbody[] rigidbodys = GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < rigidbodys.Length; i++)
        {
            if (i == 0)
            {
                continue;
            }
            SpringJoint joint = gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = rigidbodys[i];
            joint.spring = 100.0f;
        }
    }
}
