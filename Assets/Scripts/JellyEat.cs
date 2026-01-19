using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JellyEat : MonoBehaviour
{
    [SerializeField]
    private MeshRenderer playerMR;

    [SerializeField]
    private Color playerColor;

    private void Start()
    {
        playerMR = GetComponent<MeshRenderer>();
        playerColor = playerMR.material.color;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Edible"))
        {
            MeshRenderer mr = collision.gameObject.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material mat = mr.sharedMaterial;
                if (mat != null)
                {
                    
                }
            }
        }
    }
}
