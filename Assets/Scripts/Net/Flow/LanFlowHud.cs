using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace JellyNet
{
    public class LanFlowHud
    {
        private TextMeshProUGUI timerText;
        private TextMeshProUGUI centerText;
        private GameObject resultPanel;
        private TextMeshProUGUI resultTitle;
        private GameObject spectateButton;
        private GameObject returnToMainButton;

        private Coroutine flashRoutine;
        private MonoBehaviour flashOwner;

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

        private void StopCenterAnim()
        {
            if (centerText != null)
                centerText.rectTransform.DOKill();

            if (flashRoutine != null && flashOwner != null)
                flashOwner.StopCoroutine(flashRoutine);

            flashRoutine = null;
            flashOwner = null;
        }

        public void Pop(string text, float fromScale = 1.6f, float toScale = 1f)
        {
            if (centerText == null)
                return;

            StopCenterAnim();

            centerText.text = text;
            centerText.rectTransform.localScale = Vector3.one * fromScale;
            centerText.rectTransform
                .DOScale(Vector3.one * toScale, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public void ShowEndLabel(string text)
        {
            if (centerText == null)
                return;

            StopCenterAnim();
            centerText.gameObject.SetActive(true);

            Color c = centerText.color;
            c.a = 1f;
            centerText.color = c;

            Pop(text, 2.4f, 1.5f);
        }

        public void FlashCountdown(MonoBehaviour owner, string text)
        {
            if (centerText == null || owner == null)
                return;

            StopCenterAnim();

            centerText.text = text;
            flashOwner = owner;
            flashRoutine = owner.StartCoroutine(FlashRoutine());
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

            flashRoutine = null;
            flashOwner = null;
        }

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
