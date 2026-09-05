using TMPro;
using UnityEngine;

/// <summary>
/// "연결 중" → "연결 중." → "연결 중.." → "연결 중..." 을 반복한다.
/// 기다리는 화면이 멈춘 게 아니라는 것을 보여주는 용도.
///
/// 켜져 있는 동안만 돈다. 끄면 붙이던 점을 떼고 원래 문구로 돌려놓는다 —
/// 다른 코드가 이어서 그 자리에 글을 쓰는데 점이 남아 있으면 "연결 중...3" 이 된다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class WaitingDots : MonoBehaviour
{
    [Tooltip("점을 뺀 본문. 비워두면 켜질 때 라벨에 적혀 있던 글을 그대로 쓴다.")]
    [SerializeField] private string label = "";

    [Tooltip("점 하나가 늘어나는 간격(초).")]
    [SerializeField] private float interval = 0.4f;

    [Tooltip("최대 몇 개까지 찍을지.")]
    [SerializeField] private int maxDots = 3;

    private TMP_Text text;
    private float timer;
    private int dots;

    private void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    /// <summary>본문을 갈아끼운다. 켜져 있는 중에 불러도 된다.</summary>
    public void SetLabel(string value)
    {
        label = value;
        dots = 0;
        timer = 0f;
        Apply();
    }

    private void OnEnable()
    {
        //인스펙터를 비워뒀으면 라벨에 적혀 있는 글이 본문이다.
        //문구를 코드가 아니라 씬에서 정할 수 있게 하려는 것 — 세팅은 세팅이 있는 곳에
        if (string.IsNullOrEmpty(label) && text != null)
            label = text.text;

        dots = 0;
        timer = 0f;
        Apply();
    }

    private void OnDisable()
    {
        if (text != null)
            text.text = label;
    }

    private void Update()
    {
        //대기 화면은 Time.timeScale 이 0인 동안에도 돌아야 한다
        timer += Time.unscaledDeltaTime;
        if (timer < interval)
            return;

        timer -= interval;
        dots = (dots + 1) % (maxDots + 1);
        Apply();
    }

    private void Apply()
    {
        if (text != null)
            text.text = label + new string('.', dots);
    }
}
