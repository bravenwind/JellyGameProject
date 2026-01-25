using System.Collections.Generic;
using UnityEngine;

public class ResultStarsUI : MonoBehaviour
{
    [Header("Top Images")]
    [SerializeField] private GameObject failTopImage;   // 0일 때 켜짐
    [SerializeField] private GameObject clearTopImage;  // 1~3일 때 켜짐

    [Header("Main Stars (0~3)")]
    [SerializeField] private List<GameObject> stars = new List<GameObject>(); // 0,1,2 인덱스 = 별 1~3

    [Header("Mini Stars (0~3)")]
    [SerializeField] private List<GameObject> miniStars = new List<GameObject>(); // 0,1,2 인덱스 = 미니별 1~3

    public int _starIndex = 0;

    private void OnEnable()
    {
        Apply(_starIndex);
    }

    // 외부에서 결과 세팅
    public void SetStarIndex(int starIndex)
    {
        _starIndex = Mathf.Clamp(starIndex, 0, 3);

        // 이미 활성화 상태면 즉시 반영
        if (isActiveAndEnabled)
            Apply(_starIndex);
    }

    private void Apply(int starIndex)
    {
        bool isClear = starIndex >= 1;

        if (failTopImage != null) failTopImage.SetActive(!isClear);
        if (clearTopImage != null) clearTopImage.SetActive(isClear);

        // 메인 별 활성화
        for (int i = 0; i < stars.Count; i++)
        {
            var go = stars[i];
            if (go == null) continue;
            go.SetActive(i < starIndex);
        }

        // 미니 별 활성화
        for (int i = 0; i < miniStars.Count; i++)
        {
            var go = miniStars[i];
            if (go == null) continue;
            go.SetActive(i < starIndex);
        }
    }
}
