using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JellyClickReceiver : MonoBehaviour
{
    Renderer modelRenderer;
    float controlTime;

    RaycastHit hit; //New Variable
    Ray clickRay; //New Variable

    private void Start()
    {
        modelRenderer = GetComponent<Renderer>();
    }

    private void Update()
    {
        controlTime += Time.deltaTime;

        if (Input.GetMouseButtonDown(0))
        {
            clickRay = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(clickRay, out hit))
            {
                controlTime = 0;
                modelRenderer.material.SetVector("_ModelOrigin", transform.position);
                modelRenderer.material.SetVector("_ImpactOrigin", hit.point);
            }
        }

        modelRenderer.material.SetFloat("_ControlTime", controlTime);
    }
}
