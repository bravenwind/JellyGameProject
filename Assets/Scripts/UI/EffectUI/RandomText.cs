using UnityEngine;
using UnityEngine.UI;

public class RandomText : MonoBehaviour
{
    public Sprite[] textSprites;
    public Image textImage;
    // public RectTransform textRectTransform; // SetNativeSize를 쓰면 굳이 이 변수는 필요 없습니다.

    private void Start()
    {
        if (textSprites.Length > 0)
        {
            int index = Random.Range(0, textSprites.Length);
            Sprite sprite = textSprites[index];

            // 1. 이미지 교체
            textImage.sprite = sprite;

            // 2. 이미지의 원본 크기(width, height)로 RectTransform 자동 조절
            textImage.SetNativeSize();

            textImage.rectTransform.localScale = Vector3.one * 0.75f;
        }
    }
}