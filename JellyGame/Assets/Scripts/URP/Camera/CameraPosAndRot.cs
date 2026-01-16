using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class CameraPosAndRot : MonoBehaviour
{
    public Slider[] posSliders;
    public Slider[] rotSliders;
    public TMP_Text persp_ortho_btn;

    public Camera cam;

    public MainCamera_Action action;

    public Vector3 originalOffset;

    private void Awake()
    {
        originalOffset = action.offset;
    }

    // Update is called once per frame
    void Update()
    {
        action.offset = new Vector3(originalOffset.x + posSliders[0].value, originalOffset.y + posSliders[1].value, originalOffset.z + posSliders[2].value);

        //cam.transform.rotation = action.originalRot * Quaternion.Euler(new Vector3(rotSliders[0].value, rotSliders[1].value, rotSliders[2].value));
    }

    public void ChangeProjection()
    {
        if (cam.orthographic) 
        {
            cam.orthographic = false;
        }
        else
        {
            cam.orthographic = true;
        }

        if (cam.orthographic) 
        {
            persp_ortho_btn.text = "Now : Orthographic";
        }
        else
        {
            persp_ortho_btn.text = "Now : Perspective";
        }
    }
}
