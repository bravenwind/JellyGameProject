using System.Collections.Generic;
using UnityEngine;

public class MultiRandomMaterialApplier : MonoBehaviour
{
    [Header("Material Settings")]
    [Tooltip("여기에 등록된 머터리얼들이 각 파츠마다 랜덤하게 발라집니다.")]
    public List<Material> materialList;

    private Renderer[] allRenderers;

    void Start()
    {
        allRenderers = GetComponentsInChildren<Renderer>();

        if (materialList == null || materialList.Count == 0 || allRenderers.Length == 0) return;

        ApplyIndividualRandomMaterials();
    }

    public void ApplyIndividualRandomMaterials()
    {
        // 모든 렌더러를 하나씩 순회합니다.
        foreach (Renderer rend in allRenderers)
        {
            // ★ 핵심 변경점: 각각의 렌더러마다 주사위를 새로 굴립니다.
            int randomIndex = Random.Range(0, materialList.Count);

            // 뽑힌 랜덤 머터리얼을 해당 파츠에 적용합니다.
            rend.sharedMaterial = materialList[randomIndex];
        }

        Debug.Log($"[개별 랜덤 적용] {allRenderers.Length}개의 파츠에 각각 랜덤한 색상이 적용되었습니다!");
    }
}