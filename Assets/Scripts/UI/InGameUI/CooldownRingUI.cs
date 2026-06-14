using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 쿨타임 HUD(라디얼 링). PlayerMovement.Local(내 캐릭터)의 쿨타임을 매 프레임 읽어 채운다.
/// 한 컴포넌트로 대쉬/공격 둘 다 처리한다 — type만 바꿔 같은 프리팹을 재사용한다.
///   • Dash   : 모든 모드(Shift)
///   • Attack : Push 모드 전용(좌클릭, 최댓값은 DataManager.batCooldown)
///
/// 인스펙터 구성 권장:
///  - fillRing   : 진행 링 Image (Image Type = Filled, Fill Method = Radial 360). 도넛 스프라이트.
///  - 배경(트랙) 링 : fillRing 뒤에 같은 도넛 스프라이트를 어두운 색으로 깔아두기만 하면 됨(이 스크립트는 안 건드림).
///  - group      : (선택) 이 UI의 CanvasGroup. 플레이어가 없을 때 alpha=0으로 숨긴다.
///  - secondsText: (선택) 남은 초 표시.
/// </summary>
public class CooldownRingUI : MonoBehaviour
{
    public enum CooldownType { Dash, Attack }

    [Header("쿨타임 종류")]
    [Tooltip("Dash=모든 모드(Shift), Attack=Push 전용(좌클릭)")]
    [SerializeField] private CooldownType type = CooldownType.Dash;

    [Header("링")]
    [Tooltip("진행 링 Image (Image Type=Filled, Fill Method=Radial 360)")]
    [SerializeField] private Image fillRing;

    [Header("색")]
    [Tooltip("쿨다운 중 색")]
    [SerializeField] private Color cooldownColor = new Color(0.21f, 0.82f, 0.88f, 1f); // 시안
    [Tooltip("준비 완료 색")]
    [SerializeField] private Color readyColor = new Color(0.40f, 1.00f, 0.55f, 1f);    // 초록

    [Header("옵션")]
    [Tooltip("플레이어가 없을 때 숨길 CanvasGroup (비우면 같은 오브젝트에서 탐색)")]
    [SerializeField] private CanvasGroup group;
    [Tooltip("(선택) 남은 초 텍스트")]
    [SerializeField] private TMP_Text secondsText;
    [Tooltip("쿨타임이 다 차 준비된 순간 살짝 튕기는 연출")]
    [SerializeField] private bool pulseOnReady = true;

    private bool _wasReady = true;

    private void Reset()
    {
        fillRing = GetComponent<Image>();
        group = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
    }

    private void Update()
    {
        PlayerMovement p = PlayerMovement.Local;

        // 아직 내 캐릭터가 스폰되지 않았거나(로딩 등) 탈락으로 파괴된 경우 → 숨김
        if (p == null)
        {
            if (group != null) group.alpha = 0f;
            return;
        }
        if (group != null) group.alpha = 1f;

        // 종류에 맞는 쿨타임 소스 선택
        float ratio;       // 0=준비, 1=풀쿨
        bool ready;
        float remaining;   // 남은 초
        if (type == CooldownType.Attack)
        {
            ratio = p.AttackCooldownRatio;
            ready = p.AttackReady;
            remaining = p.attackCooldownTimer;
        }
        else
        {
            ratio = p.DashCooldownRatio;
            ready = p.DashReady;
            remaining = p.dashCooldownTimer;
        }

        // 비었다가 한 바퀴 차오르면 준비 완료 → fill = 1 - 쿨다운비율
        if (fillRing != null)
        {
            fillRing.fillAmount = 1f - ratio;
            fillRing.color = ready ? readyColor : cooldownColor;
        }

        if (secondsText != null)
            secondsText.text = ready ? "" : Mathf.Ceil(remaining).ToString();

        // 쿨다운 끝나는 순간(false→true)에만 펄스
        if (pulseOnReady && ready && !_wasReady)
        {
            transform.DOKill();
            transform.DOPunchScale(transform.localScale * 0.18f, 0.3f, 6, 0.7f).SetUpdate(true);
        }
        _wasReady = ready;
    }
}
