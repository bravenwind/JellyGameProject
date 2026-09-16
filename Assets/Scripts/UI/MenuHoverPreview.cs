using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MenuHoverPreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    #region Target UI

    [Header("Target UI")]
    [SerializeField] private Image characterImage;
    [SerializeField] private Text legacyText;

    #endregion

    #region Hover Data

    [Header("Hover Data")]
    [SerializeField] private Sprite hoverSprite;
    [TextArea] [SerializeField] private string hoverMessage;

    #endregion

    #region Revert On Exit

    [Header("Revert On Exit")]
    [SerializeField] private bool revertOnExit = true;

    #endregion

    #region No Hover State

    [Header("No Hover State")]
    [SerializeField] private bool hideImageWhenNoHover = true;
    [SerializeField] private float noHoverAlpha = 0f; 

    [SerializeField] private Image BG;
    [Range(0, 255)] [SerializeField] private int bgNoHoverAlpha = 40;
    [Range(0, 255)] [SerializeField] private int bgHoverAlpha = 200;

    //floating 을 지웠다. UI 추가(2a42fb0) 때 연결 칸만 만들고 코드에서 쓰지 않았다
    //(Main 씬에 남은 floating 키는 다음 저장 때 유니티가 버린다)
    [TextArea] [SerializeField] private string defaultMessage;
    [SerializeField] private ImagePreviewAni preview;

    #endregion

    void Awake()
    {
        if (string.IsNullOrEmpty(defaultMessage) && legacyText != null)
            defaultMessage = legacyText.text;

        if (hideImageWhenNoHover)
            SetImageAlpha(noHoverAlpha);

        SetBGAlpha(bgNoHoverAlpha);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (preview != null && hoverSprite != null)
            preview.Show(hoverSprite);

        if (legacyText != null && hoverMessage != null)
            legacyText.text = hoverMessage;

        SetBGAlpha(bgHoverAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!revertOnExit)
            return;

        if (preview != null)
            preview.Hide(true);

        if (legacyText != null)
            legacyText.text = defaultMessage;

        SetBGAlpha(bgNoHoverAlpha);
    }

    private void SetImageAlpha(float a)
    {
        if (characterImage == null)
            return;
        Color c = characterImage.color;
        c.a = a;
        characterImage.color = c;
    }
    private void SetBGAlpha(int a255)
    {
        if (BG == null)
            return;
        float a01 = Mathf.Clamp01(a255 / 255f);
        Color c = BG.color;
        c.a = a01;
        BG.color = c;
    }
}

