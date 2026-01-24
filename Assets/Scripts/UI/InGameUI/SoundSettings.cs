using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class SoundSettings : MonoBehaviour
{
    [Header("설정")]
    public AudioMixer audioMixer;
    public string parameter_BGM = "BGMVolume";
    public string parameter_SFX = "SFXVolume";

    [Header("UI 컴포넌트")]
    public Slider volumeSlider_BGM;
    public Slider volumeSlider_SFX;
    public TMP_Text volumeText_BGM;
    public TMP_Text volumeText_SFX;

    [Header("볼륨 범위 (dB)")]
    public float minVolume_BGM = -40f; // -20은 너무 클 수 있어 보통 -40~-60 추천
    public float maxVolume_BGM = 0f;   // 5는 소리가 깨질 수 있어 0 추천

    public float minVolume_SFX = -40f;
    public float maxVolume_SFX = 0f;

    [Header("참조")]
    public UIManager uiManager;

    private void Start()
    {
        // 1. BGM 슬라이더 초기화
        if (volumeSlider_BGM != null)
        {
            if (audioMixer.GetFloat(parameter_BGM, out float currentBGMDb))
            {
                // -80dB 이하라면 음소거로 간주하여 슬라이더 0
                volumeSlider_BGM.value = (currentBGMDb <= -80f)
                    ? 0
                    : Mathf.InverseLerp(minVolume_BGM, maxVolume_BGM, currentBGMDb);
            }
            // 초기 텍스트 갱신
            UpdateVolumeText(volumeText_BGM, volumeSlider_BGM.value);
        }

        // 2. SFX 슬라이더 초기화 (이전 코드의 버그 수정: 독립적으로 실행되게 변경)
        if (volumeSlider_SFX != null)
        {
            if (audioMixer.GetFloat(parameter_SFX, out float currentSFXDb))
            {
                // [버그 수정] 이전 코드에서 currentBGMDb를 체크하던 오류 수정 -> currentSFXDb 체크
                volumeSlider_SFX.value = (currentSFXDb <= -80f)
                    ? 0
                    : Mathf.InverseLerp(minVolume_SFX, maxVolume_SFX, currentSFXDb);
            }
            // 초기 텍스트 갱신
            UpdateVolumeText(volumeText_SFX, volumeSlider_SFX.value);
        }
    }

    // [중요 변경] BGM 전용 함수
    public void SetBGMVolume(float value)
    {
        float targetVolume = (value <= 0.001f) ? -80f : Mathf.Lerp(minVolume_BGM, maxVolume_BGM, value);

        audioMixer.SetFloat(parameter_BGM, targetVolume);
        UpdateVolumeText(volumeText_BGM, value);
    }

    // [중요 변경] SFX 전용 함수
    public void SetSFXVolume(float value)
    {
        float targetVolume = (value <= 0.001f) ? -80f : Mathf.Lerp(minVolume_SFX, maxVolume_SFX, value);

        audioMixer.SetFloat(parameter_SFX, targetVolume);
        UpdateVolumeText(volumeText_SFX, value);
    }

    // 텍스트 업데이트용 헬퍼 함수
    private void UpdateVolumeText(TMP_Text textComponent, float value)
    {
        if (textComponent != null)
        {
            textComponent.text = ((int)(value * 100)).ToString();
        }
    }

    public void OnSettingsBtnClicked()
    {
        // 싱글톤 인스턴스 null 체크 권장
        if (PlaySFXAudio.Instance != null)
        {
            PlaySFXAudio.Instance.PlayButtonClickSound();
        }

        uiManager.SetState(UIState.Settings);
    }
}