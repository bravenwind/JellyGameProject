using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace JellyNet
{
    //진행 상태를 화면에 그리는 일만 한다. 게임 규칙은 모른다
    //인스펙터 참조는 씬 연결을 유지하려고 LanGameFlow에 남겨두고 여기로 넘겨받는다
    public class LanFlowHud
    {
        private TextMeshProUGUI timerText;
        private TextMeshProUGUI centerText;
        private GameObject resultPanel;
        private TextMeshProUGUI resultTitle;
        private GameObject spectateButton;
        private GameObject returnToMainButton;

        public void Bind(TextMeshProUGUI timer, TextMeshProUGUI center,
                         GameObject panel, TextMeshProUGUI title,
                         GameObject spectate, GameObject returnToMain)
        {
            timerText = timer;
            centerText = center;
            resultPanel = panel;
            resultTitle = title;
            spectateButton = spectate;
            returnToMainButton = returnToMain;
        }

        public void UpdateTimer(GameModeType mode, float gameDuration, float remaining)
        {
            if (timerText == null)
                return;

            //밀치기는 제한 시간이 없으니 경과 시간을 센다
            float t = mode == GameModeType.Push ? gameDuration - remaining : remaining;

            if (t < 0f)
                t = 0f;

            int total = Mathf.CeilToInt(t);
            timerText.text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        public void ShowCenter(bool on)
        {
            if (centerText != null)
                centerText.gameObject.SetActive(on);
        }

        public void Pop(string text)
        {
            if (centerText == null)
                return;

            centerText.text = text;
            centerText.rectTransform.localScale = Vector3.one * 1.6f;
            centerText.rectTransform
                .DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public void ShowEndLabel(string text)
        {
            if (centerText == null)
                return;

            centerText.gameObject.SetActive(true);
            centerText.text = text;
            centerText.transform.localScale = Vector3.one * 1.5f;

            Color c = centerText.color;
            c.a = 1f;
            centerText.color = c;
        }

        public void FlashCountdown(MonoBehaviour owner, string text)
        {
            if (centerText == null || owner == null)
                return;

            centerText.text = text;
            owner.StartCoroutine(FlashRoutine());
        }

        private const float FLASH_DURATION = 0.9f;

        private IEnumerator FlashRoutine()
        {
            Vector3 from = Vector3.one * 0.5f;
            Vector3 to = Vector3.one * 2.5f;

            Color col = centerText.color;
            col.a = 1f;

            float t = 0f;

            while (t < FLASH_DURATION)
            {
                t += Time.unscaledDeltaTime;
                float k = t / FLASH_DURATION;

                centerText.transform.localScale = Vector3.Lerp(from, to, k);
                col.a = Mathf.Lerp(1f, 0f, k);
                centerText.color = col;

                yield return null;
            }
        }

        //탈락은 관전만, 연결 끊김은 메인 복귀만 열어준다
        //LAN은 이미 약속하고 모인 자리라 판이 도는 중에 나가는 길을 두지 않는다
        public void ShowGameOver(string message, bool canSpectate, bool canReturnToMain)
        {
            HideCooldownRings();

            if (resultPanel != null)
                resultPanel.SetActive(true);

            if (resultTitle != null)
                resultTitle.text = message;

            if (spectateButton != null)
                spectateButton.SetActive(canSpectate);

            if (returnToMainButton != null)
                returnToMainButton.SetActive(canReturnToMain);
        }

        //내 캐릭터가 없어진 뒤로는 읽을 쿨타임도 없다. 관전 UI와 자리도 겹친다
        private static void HideCooldownRings()
        {
            foreach (CooldownRingUI ring in
                     Object.FindObjectsByType<CooldownRingUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                ring.gameObject.SetActive(false);
        }

        public void HideResultPanel()
        {
            if (resultPanel != null)
                resultPanel.SetActive(false);
        }
    }
}
