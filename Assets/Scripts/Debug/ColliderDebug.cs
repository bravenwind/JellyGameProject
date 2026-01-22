using UnityEngine;

public class ColliderDebug : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log(collision.ToString());
    }
}
