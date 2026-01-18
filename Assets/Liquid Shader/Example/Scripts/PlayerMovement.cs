using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour {

	public float moveSpeed = 10f;

	private CharacterController cc;

	// Use this for initialization
	void Start () {
		cc = GetComponent<CharacterController>();
	}
	
	// Update is called once per frame
	void Update () {
		cc.Move((transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal")).normalized * moveSpeed * Time.deltaTime);
		if (!cc.isGrounded) {
			cc.Move(Vector3.up * Time.deltaTime * Physics.gravity.y);
		}
	}
}
