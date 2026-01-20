using UnityEngine;
using UnityEngine.UI;

public class RandomText : MonoBehaviour
{
    public Sprite[] textSprites;
    public Image textImage;
    public RectTransform textRectTransform;

    private void Start()
    {
        int index = Random.Range(0, textSprites.Length);
        Sprite sprite = textSprites[index];
        textImage.sprite = sprite;
    }
}
