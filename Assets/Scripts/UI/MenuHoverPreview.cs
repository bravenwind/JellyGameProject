using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MenuHoverPreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Target UI")]
    public Image characterImage;
    public Text legacyText;

    [Header("Hover Data")]
    public Sprite hoverSprite;
    [TextArea] public string hoverMessage;

    [Header("Revert On Exit")]
    public bool revertOnExit = true;

    [Header("No Hover State")]
    public bool hideImageWhenNoHover = true;
    public float noHoverAlpha = 0f; 
    public float hoverAlpha = 1f; 

    public FloatingUI floating;
    [TextArea] public string defaultMessage;
    public ImagePreviewAni preview;

    void Awake()
    {
        if (string.IsNullOrEmpty(defaultMessage) && legacyText != null)
            defaultMessage = legacyText.text;

        if (hideImageWhenNoHover)
            SetImageAlpha(noHoverAlpha);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (preview != null && hoverSprite != null)
            preview.Show(hoverSprite);

        if (legacyText != null && hoverMessage != null)
            legacyText.text = hoverMessage;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!revertOnExit) return;

        if (preview != null)
            preview.Hide(true);

        if (legacyText != null)
            legacyText.text = defaultMessage;
    }

    private void SetImageAlpha(float a)
    {
        if (characterImage == null) return;
        Color c = characterImage.color;
        c.a = a;
        characterImage.color = c;
    }
}

