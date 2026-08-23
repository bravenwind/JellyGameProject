// ============================================================
// NameTagBillboard.cs
// ============================================================
// 역할: 플레이어/AI봇 머리 위 이름표를 항상 카메라 정면으로 유지
//
// [사용 방법]
//   1. NetworkPlayer 프리팹 자식에 빈 GameObject 생성 → "NameTag"
//   2. 해당 GameObject에 TextMeshPro - Text (3D) 컴포넌트 추가
//   3. 이 스크립트(NameTagBillboard) 추가
//   4. nameText 슬롯에 TextMeshPro 연결
//   5. LanPlayerSetup / AIPlayerMovement의 nameTagBillboard 슬롯에 연결
//
// [주의]
//   - NameTag GameObject는 플레이어의 자식이므로 플레이어 Scale에 영향을 받음
//   - fixedWorldSize = true 시, 플레이어 크기와 관계없이 이름표 크기 고정
// ============================================================

using UnityEngine;
using TMPro;

public class NameTagBillboard : MonoBehaviour
{
    [Header("텍스트 컴포넌트")]
    public TextMeshPro nameText;

    [Header("위치 오프셋 (부모 기준 로컬)")]
    [Tooltip("플레이어 머리 위로 얼마나 띄울지. 스케일 1 기준.")]
    public float heightOffset = 2.5f;

    [Header("크기 고정")]
    [Tooltip("true면 부모 스케일이 커져도 이름표 크기를 항상 일정하게 유지")]
    public bool fixedWorldSize = true;

    [Header("이름표 색상")]
    public Color playerColor = Color.white;   // 로컬 플레이어
    public Color otherColor  = new Color(1f, 0.8f, 0.2f, 1f);  // 다른 플레이어 (노란색)
    public Color botColor    = new Color(0.8f, 0.8f, 0.8f, 1f); // AI봇 (회색)

    [Header("TopTransform 기준")]
    [Tooltip("비어있으면 기존 heightOffset 방식, 할당하면 TopTransform 위치 + heightOffset")]
    public Transform topTransform;

    private Camera _cam;
    private Transform _parentTf;

    private void Awake()
    {
        _cam      = Camera.main;
        _parentTf = transform.parent;
    }

    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        float parentScale = _parentTf != null ? Mathf.Max(_parentTf.localScale.x, 0.01f) : 1f;

        // ── 1. 위치: TopTransform 기준 또는 기존 heightOffset 방식 ──
        if (topTransform != null)
        {
            Vector3 worldPos = topTransform.position + Vector3.up * heightOffset;
            transform.position = worldPos;
        }
        else
        {
            transform.localPosition = new Vector3(0f, heightOffset, 0f);
        }

        // ── 2. 빌보드: 화면과 평행하게 세운다 ──
        //
        // ★ LookRotation(카메라 → 나)이 아니라 카메라 회전을 그대로 복사한다
        //   예전 방식은 이름표마다 '카메라가 있는 지점을 향하는' 서로 다른 회전을 만든다.
        //   이 게임처럼 위에서 내려다보는 카메라에서는 그 방향이 거의 수직이라
        //   LookRotation이 기준으로 삼는 up(월드 Y축)이 뭉개지고, 남은 roll이
        //   화면 안에서의 위치(중앙에서 얼마나 벗어났는지)에 따라 달라진다.
        //   → 플레이어가 화면 가장자리로 갈수록 이름표가 기울어 보였다.
        //
        //   카메라 회전을 그대로 쓰면 텍스트 평면이 항상 화면과 평행해져
        //   위치와 상관없이 언제나 가로로 반듯하게 보인다.
        //   (같은 씬의 BillboardEffect가 이미 쓰고 있는 방식이다)
        transform.rotation = _cam.transform.rotation;

        // ── 3. 크기 고정: 부모 스케일 상쇄 → 텍스트 크기는 항상 일정하게 ──
        if (fixedWorldSize && _parentTf != null)
        {
            transform.localScale = Vector3.one / parentScale;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부 API
    // ─────────────────────────────────────────────────────────

    /// <summary>이름표 텍스트 설정</summary>
    public void SetName(string displayName)
    {
        if (nameText != null)
            nameText.text = displayName;
    }

    /// <summary>이름표 색상 설정</summary>
    public void SetColor(Color color)
    {
        if (nameText != null)
            nameText.color = color;
    }

    /// <summary>로컬 플레이어 / 원격 플레이어 / 봇 구분에 따라 색상 자동 적용</summary>
    public void ApplyRoleColor(NameTagRole role)
    {
        Color c = role switch
        {
            NameTagRole.LocalPlayer => playerColor,
            NameTagRole.RemotePlayer => otherColor,
            NameTagRole.Bot          => botColor,
            _                        => Color.white
        };
        SetColor(c);
    }
}

public enum NameTagRole
{
    LocalPlayer,
    RemotePlayer,
    Bot
}
