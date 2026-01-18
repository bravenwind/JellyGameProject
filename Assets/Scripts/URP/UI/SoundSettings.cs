using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class SoundSettings : MonoBehaviour
{
    [Header("설정")]
    public AudioMixer audioMixer;
    public string parameterName = "BGMVolume";
    public Slider volumeSlider;
    public TMP_Text volumeText;

    [Header("볼륨 범위 (dB)")]
    public float minVolume = -20f;
    public float maxVolume = 5f;

    [Header("참조")]
    public UIManager uiManager;

    private void Start()
    {
        if (volumeSlider != null)
        {
            float currentDb;
            bool result = audioMixer.GetFloat(parameterName, out currentDb);

            if (result)
            {
                if (currentDb <= -80f)
                {
                    volumeSlider.value = 0;
                }
                else
                {
                    volumeSlider.value = Mathf.InverseLerp(minVolume, maxVolume, currentDb);
                }
            }

            // [수정] 시작할 때 현재 슬라이더 값을 넘겨주며 초기화
            SetVolume(volumeSlider.value);
        }
    }

    // [수정] float value를 매개변수로 받도록 변경!
    public void SetVolume(float value)
    {
        float targetVolume;

        if (value <= 0.001f)
        {
            targetVolume = -80f;
        }
        else
        {
            targetVolume = Mathf.Lerp(minVolume, maxVolume, value);
        }
        
        audioMixer.SetFloat(parameterName, targetVolume);

        volumeText.text = ((int)(value * 100)).ToString();
    }

    public void OnSettingsBtnClicked()
    {
        uiManager.SetState(UIState.InGame);
    }
}