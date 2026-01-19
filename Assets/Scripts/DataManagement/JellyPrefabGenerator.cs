#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using System.IO;

public class JellyPrefabGenerator : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("복제할 원본 젤리 프리팹 (템플릿)")]
    public GameObject baseJellyPrefab;

    [Tooltip("프리팹이 저장될 경로 (Assets/...)")]
    public string savePath = "Assets/Resources/Prefabs/GeneratedJellies";

    [Header("Shader Settings")]
    [Tooltip("이미션 컬러 프로퍼티 이름 (쉐이더에 따라 다름, 보통 _EmissionColor)")]
    public string emissionPropertyName = "_EmissionColor";

    // 인스펙터에서 우클릭 메뉴로 실행 가능하도록 설정
    [ContextMenu("Generate Prefabs (Play Mode Only)")]
    public void GeneratePrefabs()
    {
        // 데이터가 로드된 플레이 모드에서만 실행 가능
        if (!Application.isPlaying)
        {
            Debug.LogError("데이터 로드를 위해 플레이 모드(Play Mode)에서 실행해주세요!");
            return;
        }

        if (DataManager.Instance == null || DataManager.Instance.jellyColorSets == null)
        {
            Debug.LogError("DataManager 혹은 ColorSets가 초기화되지 않았습니다.");
            return;
        }

#if UNITY_EDITOR
        // 1. 저장 경로 폴더가 없으면 생성
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
            AssetDatabase.Refresh();
        }

        int count = 0;

        // 2. DataManager의 모든 ColorSet을 순회
        foreach (var colorSet in DataManager.Instance.jellyColorSets)
        {
            if (colorSet.colorMaterial == null)
            {
                Debug.LogWarning($"{colorSet.colorName}의 머티리얼이 없습니다. 스킵합니다.");
                continue;
            }

            // 3. 베이스 프리팹을 씬에 임시로 생성
            GameObject tempObj = Instantiate(baseJellyPrefab);
            tempObj.name = $"Jelly_{colorSet.colorName}";

            // 4. JellyObject 스크립트 값 설정
            JellyObject jellyScript = tempObj.GetComponent<JellyObject>();
            if (jellyScript == null) jellyScript = tempObj.AddComponent<JellyObject>();

            jellyScript.Initialize(colorSet.colorName, colorSet.colorType);

            // 5. 머티리얼 교체 및 Emission 설정
            Renderer rend = tempObj.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // 원본 머티리얼을 수정하면 프로젝트 전체에 영향이 가므로, 
                // 해당 프리팹 전용 머티리얼 인스턴스를 만들지, 원본을 쓸지 결정해야 합니다.
                // 여기서는 "ColorSet의 머티리얼을 적용하고, 그 머티리얼의 설정을 바꿈"으로 처리합니다.

                // 주의: 이렇게 하면 Asset에 있는 원본 머티리얼(.mat) 파일의 Emission 값이 변경됩니다.
                // 의도하신 바가 맞다면 그대로 진행합니다.
                Material mat = colorSet.colorMaterial;

                // Emission 활성화 (Standard Shader 기준)
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(emissionPropertyName, colorSet.normal); // ★ Normal 컬러를 Emission으로

                // 렌더러에 할당
                rend.sharedMaterial = mat;
            }

            // 6. 프리팹으로 저장 (PrefabUtility 사용)
            string localPath = $"{savePath}/{tempObj.name}.prefab";

            // 기존에 파일이 있으면 덮어쓰기 위해 Unique path 체크는 생략하거나 옵션 조정
            localPath = AssetDatabase.GenerateUniqueAssetPath(localPath);

            PrefabUtility.SaveAsPrefabAsset(tempObj, localPath);
            Debug.Log($"프리팹 저장 완료: {localPath}");

            // 7. 임시 오브젝트 삭제
            DestroyImmediate(tempObj);
            count++;
        }

        Debug.Log($"총 {count}개의 젤리 프리팹 생성이 완료되었습니다!");
        AssetDatabase.Refresh(); // 에디터 새로고침
#endif
    }
}