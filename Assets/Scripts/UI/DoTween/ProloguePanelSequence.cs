using UnityEngine;
using DG.Tweening;

public class ProloguePanelSequence : MonoBehaviour
{
    [System.Serializable]
    public class Step
    {
        public RectTransform image;      // 오른쪽 밖에 놓인 이미지
        public RectTransform centerPos;  // 중앙 목표 위치(빈 오브젝트 RectTransform)
    }

    [Header("Panel")]
    [SerializeField] private GameObject prologuePanel;

    [Header("Steps (Image1~3)")]
    [SerializeField] private Step[] steps = new Step[3];

    [Header("Tween")]
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private Ease slideEase = Ease.OutCubic;
    [SerializeField] private bool ignoreTimeScale = true;

    [Header("Finish")]
    [SerializeField] private bool closePanelOnFinish = true;

    public SceneLoader sceneLoader;

    private int _index = -1;
    private bool _playing = false;
    private Tween _tween;

    private void Awake()
    {
        if (prologuePanel != null)
            prologuePanel.SetActive(false);
    }

    private void Update()
    {
        if (!_playing) return;

        bool any =
            Input.anyKeyDown ||
            Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2);

        if (!any) return;

        Next();
    }

    public void StartPrologue()
    {
        if (prologuePanel == null) return;

        PlaySFXAudio.Instance.PlayButton1Sound();
        prologuePanel.SetActive(true);

        _tween?.Kill();
        _index = -1;
        _playing = true;

        Next();
    }

    private void Next()
    {
        if (_tween != null && _tween.IsActive() && _tween.IsPlaying())
            return;

        _index++;

        if (_index >= steps.Length)
        {
            Finish();
            return;
        }

        var s = steps[_index];
        if (s == null || s.image == null || s.centerPos == null)
        {
            PlaySFXAudio.Instance.PlayButton1Sound();
            Next();
            return;
        }

        Vector2 target = s.centerPos.anchoredPosition;
        _tween = s.image.DOAnchorPos(target, slideDuration)
                        .SetEase(slideEase)
                        .SetUpdate(ignoreTimeScale);
    }

    private void Finish()
    {
        //_playing = false;
        //_tween?.Kill();
        //_tween = null;

        //if (closePanelOnFinish && prologuePanel != null)
        //    prologuePanel.SetActive(false);

        sceneLoader.LoadGame();
    }
}
