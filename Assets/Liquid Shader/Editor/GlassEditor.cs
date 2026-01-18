using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

public class GlassEditor : ShaderGUI {

	private static class Styles {
		public static GUIContent glassColorText = EditorGUIUtility.TrTextContent("Glass Color");
		public static GUIContent normalMapText = EditorGUIUtility.TrTextContent("Normal Map");
		public static GUIContent roughnessTexText = EditorGUIUtility.TrTextContent("Roughness Texture");
		public static GUIContent drawBackfaceText = EditorGUIUtility.TrTextContent("Draw Backface");

		public static string blurText = "Blur Amount";
		public static string fresnelPowerText = "Fresnel Power";
		public static string thicknessText = "Thickness";

		public static string generalSettingsText = "General Settings";
	}

	MaterialProperty _MainTex = null;
	MaterialProperty _NormalMap = null;
	MaterialProperty _RoughnessTex = null;
	MaterialProperty _GlassColor = null;
	MaterialProperty _Blur = null;
	MaterialProperty _FresnelPower = null;
	MaterialProperty _NormalMult = null;
	MaterialProperty _Thickness = null;
	MaterialProperty _DrawBackface = null;

	public void FindProperties(MaterialProperty[] props) {
		_MainTex = FindProperty("_MainTex", props);
		_NormalMap = FindProperty("_NormalMap", props);
		_RoughnessTex = FindProperty("_RoughnessTex", props);
		_GlassColor = FindProperty("_GlassColor", props);
		_Blur = FindProperty("_Blur", props);
		_FresnelPower = FindProperty("_FresnelPower", props);
		_NormalMult = FindProperty("_NormalMult", props);
		_Thickness = FindProperty("_Thickness", props);
		_DrawBackface = FindProperty("_DrawBackface", props);
	}

	public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties) {
		FindProperties(properties);
		Material material = materialEditor.target as Material;

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.LabelField(Styles.generalSettingsText, EditorStyles.boldLabel);
		materialEditor.TexturePropertySingleLine(Styles.glassColorText, _MainTex, _GlassColor);
		materialEditor.TexturePropertySingleLine(Styles.normalMapText, _NormalMap, _NormalMult);
		materialEditor.TexturePropertySingleLine(Styles.roughnessTexText, _RoughnessTex);
		_Blur.floatValue = EditorGUILayout.FloatField(Styles.blurText, _Blur.floatValue);
		_FresnelPower.floatValue = EditorGUILayout.FloatField(Styles.fresnelPowerText, _FresnelPower.floatValue);
		_Thickness.floatValue = EditorGUILayout.FloatField(Styles.thicknessText, _Thickness.floatValue);
		_DrawBackface.floatValue = EditorGUILayout.Toggle(Styles.drawBackfaceText, _DrawBackface.floatValue == 1.0f) ? 1.0f : 0.0f;

		EditorGUI.EndChangeCheck();

		material.SetShaderPassEnabled("ForwardBase", _DrawBackface.floatValue == 1.0f);
		material.renderQueue = 2999 - (int)_DrawBackface.floatValue;
	}
}
