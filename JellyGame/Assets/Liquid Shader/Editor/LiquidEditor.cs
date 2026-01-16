using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

public class LiquidEditor : ShaderGUI {

	public enum RefractionMethods {
		ReflectionProbe,
		GrabPass
	}

	private static class Styles {
		public static GUIContent liquidColorText = EditorGUIUtility.TrTextContent("Liquid Color");
		public static GUIContent normalMapText = EditorGUIUtility.TrTextContent("Normal Map");
		public static GUIContent wavesTexText = EditorGUIUtility.TrTextContent("Wave Map");
		public static GUIContent perlinNoiseText = EditorGUIUtility.TrTextContent("Perlin Noise");
		public static GUIContent bubbleTexText = EditorGUIUtility.TrTextContent("Bubble Texture");

		public static string foamColorText = "Foam Color";
		public static string surfaceColorText = "Surface Color";
		public static string refractionIndexText = "Refraction Index";
		public static string probeLodText = "Murkiness";
		public static string syrupText = "Syrup";
		public static string edgeThicknessText = "Edge Thickness";
		public static string fresnelPowerText = "Fresnel Power";
		public static string meniscusHeightText = "Meniscus Height";
		public static string meniscusCurveText = "Meniscus Curve";
		public static string foamAmountText = "Foam Amount";
		public static string bubbleScaleText = "Bubble Scale";
		public static string bubbleCountText = "Bubble Count";
		public static string refractionMethodText = "Refraction Method";

		public static string generalSettingsText = "General Settings";
		public static string surfaceSettingsText = "Surface Settings";

		public static readonly string[] refractionMethods = Enum.GetNames(typeof(RefractionMethods));
	}

	MaterialProperty _MainTex = null;
	MaterialProperty _NormalMap = null;
	MaterialProperty _WavesTex = null;
	MaterialProperty _PerlinNoise = null;
	MaterialProperty _BubbleTex = null;
	MaterialProperty _LiquidColor = null;
	MaterialProperty _TopColor = null;
	MaterialProperty _FoamColor = null;
	MaterialProperty _Refraction = null;
	MaterialProperty _ProbeLod = null;
	MaterialProperty _Syrup = null;
	MaterialProperty _EdgeThickness = null;
	MaterialProperty _FresnelPower = null;
	MaterialProperty _MeniscusHeight = null;
	MaterialProperty _MeniscusCurve = null;
	MaterialProperty _FoamAmount = null;
	MaterialProperty _BubbleScale = null;
	MaterialProperty _BubbleCount = null;
	MaterialProperty _UseGrabpass = null;

	public void FindProperties(MaterialProperty[] props) {
		_MainTex = FindProperty("_MainTex", props);
		_NormalMap = FindProperty("_NormalMap", props);
		_WavesTex = FindProperty("_WavesTex", props);
		_PerlinNoise = FindProperty("_PerlinNoise", props);
		_BubbleTex = FindProperty("_BubbleTex", props);
		_LiquidColor = FindProperty("_LiquidColor", props);
		_TopColor = FindProperty("_TopColor", props);
		_FoamColor = FindProperty("_FoamColor", props);
		_Refraction = FindProperty("_Refraction", props);
		_ProbeLod = FindProperty("_ProbeLod", props);
		_Syrup = FindProperty("_Syrup", props);
		_EdgeThickness = FindProperty("_EdgeThickness", props);
		_FresnelPower = FindProperty("_FresnelPower", props);
		_MeniscusHeight = FindProperty("_MeniscusHeight", props);
		_MeniscusCurve = FindProperty("_MeniscusCurve", props);
		_FoamAmount = FindProperty("_FoamAmount", props);
		_BubbleScale = FindProperty("_BubbleScale", props);
		_BubbleCount = FindProperty("_BubbleCount", props);
		_UseGrabpass = FindProperty("_UseGrabpass", props);
	}

	public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties) {
		FindProperties(properties);
		Material material = materialEditor.target as Material;

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.LabelField(Styles.generalSettingsText, EditorStyles.boldLabel);
		_UseGrabpass.floatValue = EditorGUILayout.Popup(Styles.refractionMethodText, (int)_UseGrabpass.floatValue, Styles.refractionMethods);
		materialEditor.TexturePropertySingleLine(Styles.liquidColorText, _MainTex, _LiquidColor);
		materialEditor.TexturePropertySingleLine(Styles.normalMapText, _NormalMap);
		materialEditor.TexturePropertySingleLine(Styles.wavesTexText, _WavesTex);
		materialEditor.TexturePropertySingleLine(Styles.perlinNoiseText, _PerlinNoise);
		materialEditor.TexturePropertySingleLine(Styles.bubbleTexText, _BubbleTex);
		_Refraction.floatValue = EditorGUILayout.FloatField(Styles.refractionIndexText, _Refraction.floatValue);
		_ProbeLod.floatValue = EditorGUILayout.FloatField(Styles.probeLodText, _ProbeLod.floatValue);
		_Syrup.floatValue = EditorGUILayout.FloatField(Styles.syrupText, _Syrup.floatValue);
		_FresnelPower.floatValue = EditorGUILayout.FloatField(Styles.fresnelPowerText, _FresnelPower.floatValue);
		_BubbleScale.floatValue = EditorGUILayout.FloatField(Styles.bubbleScaleText, _BubbleScale.floatValue);
		_BubbleCount.floatValue = EditorGUILayout.FloatField(Styles.bubbleCountText, _BubbleCount.floatValue);
		EditorGUILayout.Separator();

		EditorGUILayout.LabelField(Styles.surfaceSettingsText, EditorStyles.boldLabel);
		materialEditor.ColorProperty(_TopColor, Styles.surfaceColorText);
		materialEditor.ColorProperty(_FoamColor, Styles.foamColorText);
		_EdgeThickness.floatValue = EditorGUILayout.FloatField(Styles.edgeThicknessText, _EdgeThickness.floatValue);
		_MeniscusHeight.floatValue = EditorGUILayout.FloatField(Styles.meniscusHeightText, _MeniscusHeight.floatValue);
		_MeniscusCurve.floatValue = EditorGUILayout.FloatField(Styles.meniscusCurveText, _MeniscusCurve.floatValue);
		_FoamAmount.floatValue = EditorGUILayout.FloatField(Styles.foamAmountText, _FoamAmount.floatValue);
		EditorGUILayout.Separator();

		if (EditorGUI.EndChangeCheck()) {
			if (_UseGrabpass.floatValue < 1) {
				material.DisableKeyword("USE_GRABPASS");
				material.SetShaderPassEnabled("Always", false);
			} else {
				material.EnableKeyword("USE_GRABPASS");
				material.SetShaderPassEnabled("Always", true);
			}

		}
	}
}
