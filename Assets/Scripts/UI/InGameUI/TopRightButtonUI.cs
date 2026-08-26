﻿using UnityEngine;

public class TopRightButtonUI : MonoBehaviour
{
    //예전엔 UIManager를 인스펙터로 받았다. 싱글턴이 이미 있는데 배선을 하나 더
    //두면 그것만 비어도 버튼이 조용히 아무 일도 안 한다
    public void OnSettingsBtnClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.SetState(UIState.Settings);

        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.PlayButtonClickSound();
    }
}
