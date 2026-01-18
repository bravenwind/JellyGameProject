using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Liquid))]
public class LiquidScriptEditor : Editor {

	SerializedProperty volumeMesh;
	SerializedProperty planePosition;
	SerializedProperty foamSettleSpeed;

	void OnEnable() {
		volumeMesh = serializedObject.FindProperty("volumeMesh");
		planePosition = serializedObject.FindProperty("planePosition");
		foamSettleSpeed = serializedObject.FindProperty("foamSettleSpeed");
	}

	public override void OnInspectorGUI() {
		serializedObject.Update();
		Liquid liquid = (Liquid)target;

		GUIStyle tinyStyle = EditorStyles.centeredGreyMiniLabel;
		GUIStyle prevTinyStyle = tinyStyle;
		tinyStyle.normal.textColor = new Color(0.2f, 0.2f, 0.2f);
		tinyStyle.wordWrap = true;

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.PrefixLabel("Volume Mesh");
		volumeMesh.objectReferenceValue = (Mesh)EditorGUILayout.ObjectField(volumeMesh.objectReferenceValue, typeof(Mesh), false);
		EditorGUILayout.EndHorizontal();
		planePosition.vector3Value = EditorGUILayout.Vector3Field("Plane Position", planePosition.vector3Value);
		foamSettleSpeed.floatValue = EditorGUILayout.FloatField("Foam Settle Speed", foamSettleSpeed.floatValue);

		if (liquid.gameObject.GetComponent<ReflectionProbe>() == null && !liquid.gameObject.GetComponent<MeshRenderer>().sharedMaterial.IsKeywordEnabled("USE_GRABPASS")) {
			EditorGUILayout.LabelField("Reflection Probe is not present. Add a reflection probe to use the ReflectionProbe refraction method in the liquid shader.", tinyStyle);
			if (GUILayout.Button("Add Reflection Probe")) {
				liquid.gameObject.AddComponent<ReflectionProbe>();
				ReflectionProbe probe = liquid.gameObject.GetComponent<ReflectionProbe>();
				probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
				probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.EveryFrame;
				probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
				probe.size = Vector3.one;
			}
		}

		serializedObject.ApplyModifiedProperties();
		tinyStyle = prevTinyStyle;
	}
}
