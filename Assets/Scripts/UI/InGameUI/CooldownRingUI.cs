using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 쿨타임 HUD(라디얼 링). PlayerMovement.Local(내 캐릭터)의 쿨타임을 매 프레임 읽어 채운다.
/// 한 컴포넌트로 대쉬/공격 둘 다 처리한다 — type만 바꿔 같은 프리팹을 재사용한다.
///   • Dash   : 모든 모드(Shift)
///   • Attack : Push 모드 전용(좌클릭, 최댓값은 DataManager.BatCooldown)
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

    private bool wasReady = true;

    private void Reset()
    {
        fillRing = GetComponent<Image>();
        group = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (group == null)
            group = GetComponent<CanvasGroup>();
    }

    private void Update()
    {
        PlayerMovement p = PlayerMovement.Local;

        // 아직 내 캐릭터가 스폰되지 않았거나(로딩 등) 탈락으로 파괴된 경우 → 숨김
        if (p == null)
        {
            if (group != null)
                group.alpha = 0f;
            return;
        }
        if (group != null)
            group.alpha = 1f;

        // ★ 왜 이벤트(옵저버)가 아니라 매 프레임 읽는가
        //   쿨타임은 '사건'이 아니라 매 프레임 줄어드는 '연속값'이다. 값이 바뀔 때마다
        //   이벤트를 쏘면 초당 60번 발화하는데, 그건 폴링을 더 비싸게 다시 만든 것이다.
        //   크기·색처럼 가끔 바뀌는 값은 CurrentStatusUI처럼 이벤트가 맞고 여기는 폴링이 맞다.
        //
        //   대신 '무엇을 읽는가'는 창구 하나로 좁혔다 — 예전엔 UI가 종류별로
        //   비율·준비여부·남은시간 세 멤버를 각각 알고 있었다.
        PlayerMovement.Cooldown c = type == CooldownType.Attack
            ? p.AttackCooldownInfo
            : p.DashCooldownInfo;

        // 비었다가 한 바퀴 차오르면 준비 완료 → fill = 1 - 쿨다운비율
        if (fillRing != null)
        {
            fillRing.fillAmount = 1f - c.Ratio;
            fillRing.color = c.Ready ? readyColor : cooldownColor;
        }

        if (secondsText != null)
            secondsText.text = c.Ready ? "" : Mathf.Ceil(c.Remaining).ToString();

        // 쿨다운 끝나는 순간(false→true)에만 펄스
        if (pulseOnReady && c.Ready && !wasReady)
        {
            transform.DOKill();
            transform.DOPunchScale(transform.localScale * 0.18f, 0.3f, 6, 0.7f).SetUpdate(true);
        }
        wasReady = c.Ready;
    }
}
