using UnityEngine;

[RequireComponent(typeof(JellyColorSource))] // JellyColorSource가 반드시 있어야 함
public class JellyVisualController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("텍스처가 있는 리소스 폴더 경로 (예: Textures/Jelly/)")]
    public string textureResourcePath = "";

    private JellyColorSource colorSource;
    private Renderer targetRenderer;

    private void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        // 1. 컴포넌트 가져오기
        colorSource = GetComponent<JellyColorSource>();
        targetRenderer = GetComponentInChildren<Renderer>();

        if (targetRenderer == null)
        {
            Debug.LogError($"{gameObject.name}: 렌더러를 찾을 수 없습니다.");
            return;
        }

        // 2. 게임오브젝트 이름을 정리하여 키값 추출
        // "(Clone)"이 붙는 경우를 대비해 이름을 정리합니다.
        string keyName = gameObject.name.Replace("(Clone)", "").Trim();

        // 3. DataManager에서 데이터 가져오기 (이름 기반 검색)
        JellyData data = JellyDataManager.Instance.GetJelly(keyName);

        if (data != null)
        {
            ApplyVisuals(data);
        }
        else
        {
            Debug.LogWarning($"{keyName}에 해당하는 젤리 데이터를 찾을 수 없습니다.");
        }
    }

    private void ApplyVisuals(JellyData data)
    {
        // 4. JellyColorSource에 색상 적용
        if (colorSource != null)
        {
            colorSource.jellyColor = data.Color;

            // 만약 JellyColorSource가 자동으로 색을 적용하지 않는다면,
            // 여기서 직접 렌더러 색상도 변경해줍니다.
            // (사용하는 셰이더의 프로퍼티 이름에 따라 "_BaseColor" 또는 "_Color" 사용)
            targetRenderer.material.SetColor("_BaseColor", data.Color);
        }

        // 5. 텍스처 로드 및 적용
        // CSV에 있는 파일명을 사용하여 Resources 폴더에서 텍스처를 불러옵니다.
        Texture baseMap = LoadTexture(data.Base);
        Texture normalMap = LoadTexture(data.Normal);
        Texture maskMap = LoadTexture(data.Mask);

        // 6. 머티리얼에 텍스처 할당
        // URP Lit 셰이더 기준 프로퍼티 이름입니다. 커스텀 셰이더라면 이름을 확인해야 합니다.
        if (baseMap != null) targetRenderer.material.SetTexture("_BaseMap", baseMap);
        if (normalMap != null) targetRenderer.material.SetTexture("_BumpMap", normalMap);

        // MaskMap은 셰이더에 따라 이름이 다를 수 있습니다. (예: _MaskMap, _MetallicGlossMap 등)
        if (maskMap != null) targetRenderer.material.SetTexture("_MaskMap", maskMap);

        // 7. 스케일 적용 (필요한 경우)
        // transform.localScale = Vector3.one * data.ScaleLevel;

        Debug.Log($"[{data.Name}] 비주얼 데이터 적용 완료: 색상 {data.Color}, 텍스처 변경됨.");
    }

    // Resources 폴더에서 텍스처 로드 헬퍼 함수
    private Texture LoadTexture(string textureName)
    {
        if (string.IsNullOrEmpty(textureName)) return null;

        string fullPath = System.IO.Path.Combine(textureResourcePath, textureName);
        Texture tex = Resources.Load<Texture>(fullPath);

        if (tex == null)
        {
            Debug.LogWarning($"텍스처를 찾을 수 없습니다: {fullPath} (Resources 폴더 확인 필요)");
        }
        return tex;
    }
}