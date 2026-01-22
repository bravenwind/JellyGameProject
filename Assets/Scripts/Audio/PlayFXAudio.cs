using UnityEngine;

public class PlayFXAudio : MonoBehaviour
{
    public static PlayFXAudio Instance;

    [Header("Audio Sources")]
    public AudioSource fxAudioSource;   // 효과음용 (버튼, 점프 등)
    public AudioSource walkAudioSource; // 걷기 전용 (반복 재생용)

    [Header("Buttons")]
    public AudioClip button1Audio;
    public AudioClip button3Audio;
    public AudioClip buttonClickAudio;

    [Header("Machine")]
    public AudioClip machineAudio;

    [Header("Color Mix")]
    public AudioClip colorMixAudio;
    public AudioClip colorMix2Audio;

    [Header("Actions")]
    public AudioClip scaleUpAudio;
    public AudioClip crashAudio;
    public AudioClip walkAudio;
    public AudioClip walk2Audio;
    public AudioClip jumpAudio;

    [Header("Game State")]
    public AudioClip missionCompleteAudio;
    public AudioClip failAudio;

    [Header("Camera")]
    public AudioClip zoomInAudio;
    public AudioClip zoomOutAudio;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("🔊 [Sound] PlayFXAudio 매니저 초기화 완료");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // -----------------------------------------------------------
    // [걷기 소리 제어 - 루프 방식]
    // -----------------------------------------------------------

    // 캐릭터가 움직이기 시작할 때 한 번만 호출하세요
    public void StartWalking()
    {
        // 이미 소리가 나고 있다면 다시 재생하지 않음 (중복 방지)
        if (walkAudioSource.isPlaying) return;

        // 걷는 소리 2개 중 하나 랜덤 선택
        int rand = Random.Range(0, 2);
        if (rand == 0)
        {
            walkAudioSource.clip = walkAudio;
            Debug.Log("🔊 [Sound] 걷기 루프 시작 (타입 1)");
        }
        else
        {
            walkAudioSource.clip = walk2Audio;
            Debug.Log("🔊 [Sound] 걷기 루프 시작 (타입 2)");
        }

        walkAudioSource.loop = true; // 반복 재생 켜기
        walkAudioSource.Play();      // 재생 시작
    }

    // 캐릭터가 멈출 때 호출하세요
    public void StopWalking()
    {
        if (walkAudioSource.isPlaying)
        {
            Debug.Log("🔊 [Sound] 걷기 루프 정지");
            walkAudioSource.Stop();
        }
    }

    // -----------------------------------------------------------
    // [기타 효과음 - OneShot 방식]
    // -----------------------------------------------------------

    public void PlayButton1Sound()
    {
        Debug.Log("🔊 [Sound] 버튼 1 소리");
        fxAudioSource.PlayOneShot(button1Audio);
    }

    public void PlayButton3Sound()
    {
        Debug.Log("🔊 [Sound] 버튼 3 소리");
        fxAudioSource.PlayOneShot(button3Audio);
    }

    public void PlayButtonClickSound()
    {
        Debug.Log("🔊 [Sound] 버튼 클릭 소리");
        fxAudioSource.PlayOneShot(buttonClickAudio);
    }

    public void PlayMachineSound()
    {
        Debug.Log("🔊 [Sound] 기계 소리");
        fxAudioSource.PlayOneShot(machineAudio);
    }

    public void PlayColorMixSound()
    {
        int rand = Random.Range(0, 2);
        if (rand == 0) fxAudioSource.PlayOneShot(colorMixAudio);
        else fxAudioSource.PlayOneShot(colorMix2Audio);

        Debug.Log("🔊 [Sound] 색 조합 소리");
    }

    public void PlayScaleUpSound()
    {
        Debug.Log("🔊 [Sound] 크기 증가 소리");
        fxAudioSource.PlayOneShot(scaleUpAudio);
    }

    public void PlayCrashSound()
    {
        Debug.Log("🔊 [Sound] 충돌 소리");
        fxAudioSource.PlayOneShot(crashAudio);
    }

    public void PlayJumpSound()
    {
        Debug.Log("🔊 [Sound] 점프 소리");
        fxAudioSource.PlayOneShot(jumpAudio);
    }

    public void PlayMissionCompleteSound()
    {
        Debug.Log("🔊 [Sound] 미션 성공 소리");
        fxAudioSource.PlayOneShot(missionCompleteAudio);
    }

    public void PlayFailSound()
    {
        Debug.Log("🔊 [Sound] 실패 소리");
        fxAudioSource.PlayOneShot(failAudio);
    }

    public void PlayZoomInSound()
    {
        Debug.Log("🔊 [Sound] 줌 인");
        fxAudioSource.PlayOneShot(zoomInAudio);
    }

    public void PlayZoomOutSound()
    {
        Debug.Log("🔊 [Sound] 줌 아웃");
        fxAudioSource.PlayOneShot(zoomOutAudio);
    }
}