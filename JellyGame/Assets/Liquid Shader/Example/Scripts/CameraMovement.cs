using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CameraMovement : MonoBehaviour {

	public float sensitivity = 10f;

	private Camera cam;
	private GameObject heldObject;
	private Vector2 mouseMove;
	private float fov = 60;

	// Use this for initialization
	void Start () {
		cam = GetComponent<Camera>();
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
	}
	
	// Update is called once per frame
	void Update () {
		if (!Input.GetButton("Fire1")) {
			transform.Rotate(-Input.GetAxis("Mouse Y") * sensitivity, 0, 0);
			transform.parent.Rotate(0, Input.GetAxis("Mouse X") * sensitivity, 0);
		}

		RaycastHit hit;
		bool goal = false;
		if (Physics.Raycast(transform.position, transform.forward, out hit, 4)) {
			if (Input.GetKeyDown(KeyCode.E)) {
				if (heldObject == null) {
					if (hit.transform.tag == "Pickup") {
						heldObject = hit.transform.gameObject;
						heldObject.GetComponent<Rigidbody>().isKinematic = true;
						goal = true;
					}
				}
			}
		}

		if (heldObject != null && goal == false && Input.GetKeyDown(KeyCode.E)) {
			if (heldObject.GetComponent<Rigidbody>() != null)
				heldObject.GetComponent<Rigidbody>().isKinematic = false;
			Liquid liquid = heldObject.GetComponent<Liquid>();
			if (liquid != null) {
				ReflectionProbe probe = liquid.GetComponent<ReflectionProbe>();
				if (probe != null) {
					probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
					probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
					probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
				}
			}
			if (heldObject.transform.childCount > 0) {
				liquid = heldObject.transform.GetChild(0).GetComponent<Liquid>();
				if (liquid != null) {
					ReflectionProbe probe = liquid.GetComponent<ReflectionProbe>();
					if (probe != null) {
						probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
						probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
						probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
					}
				}
			}
			heldObject = null;
		}

		if (heldObject != null) {
			heldObject.transform.position = Vector3.Lerp(heldObject.transform.position, transform.position + transform.forward * 2, Time.deltaTime * (Vector3.Distance(heldObject.transform.position, transform.position + transform.forward * 2) + 1) * 10);

			if (Input.GetButton("Fire1")) {
				mouseMove = Vector2.Lerp(mouseMove, new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * sensitivity, Time.deltaTime * 10);
			} else {
				mouseMove = Vector2.Lerp(mouseMove, Vector2.zero, Time.deltaTime * 10);
			}
			heldObject.transform.Rotate(transform.forward, -mouseMove.x, Space.World);
			heldObject.transform.Rotate(transform.right, mouseMove.y, Space.World);

			Liquid liquid = heldObject.GetComponent<Liquid>();
			if (liquid != null) {
				ReflectionProbe probe = liquid.GetComponent<ReflectionProbe>();
				if (probe != null) {
					probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
					probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.EveryFrame;
					probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
				}
			}
			if (heldObject.transform.childCount > 0) {
				liquid = heldObject.transform.GetChild(0).GetComponent<Liquid>();
				if (liquid != null) {
					ReflectionProbe probe = liquid.GetComponent<ReflectionProbe>();
					if (probe != null) {
						probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
						probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.EveryFrame;
						probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
					}
				}
			}
			fov = 40;
		} else {
			fov = 60;
		}
		cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, Time.deltaTime * 5);
	}
}
