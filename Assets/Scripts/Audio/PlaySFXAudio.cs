using UnityEngine;
using UnityEngine.SceneManagement;

public class PlaySFXAudio : MonoBehaviour
{
    public static PlaySFXAudio Instance;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource fxAudioSource;   // 효과음용 (버튼, 점프 등)
    [SerializeField] private AudioSource walkAudioSource; // 걷기 전용 (반복 재생용)

    [Header("Buttons")]
    public AudioClip button1Audio;
    public AudioClip buttonClickAudio;

    [Header("Color Mix")]
    public AudioClip colorMix2Audio;

    [Header("Actions")]
    public AudioClip scaleUpAudio;
    public AudioClip walkAudio;
    public AudioClip milkWalkAudio;
    public AudioClip jumpAudio;

    [Header("Game State")]
    public bool isSteppingMilk;
    private bool prevIsSteppingMilk;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(walkAudioSource.gameObject);
            DontDestroyOnLoad(gameObject);
        }
        else
            Destroy(gameObject);

        prevIsSteppingMilk = isSteppingMilk;
        walkAudioSource.clip = isSteppingMilk ? milkWalkAudio : walkAudio;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StopWalking();
    }

    private void Update()
    {
        if (isSteppingMilk != prevIsSteppingMilk)
        {
            prevIsSteppingMilk = isSteppingMilk;

            // 소리가 바뀌기 전에 캐릭터가 걷고 있었는지(재생 중이었는지) 확인
            bool isCurrentlyWalking = walkAudioSource.isPlaying;

            // 클립 교체
            walkAudioSource.clip = isSteppingMilk ? milkWalkAudio : walkAudio;

            // 만약 걷고 있던 중이었다면, 바뀐 소리로 즉시 이어서 재생
            if (isCurrentlyWalking)
                walkAudioSource.Play();
        }
    }

    // -----------------------------------------------------------
    // [걷기 소리 제어 - 루프 방식]
    // -----------------------------------------------------------

    // 캐릭터가 움직이기 시작할 때 한 번만 호출
    public void StartWalking()
    {
        // 시작할 때 현재 바닥 상태에 맞는 클립인지 한 번 더 확인
        walkAudioSource.clip = isSteppingMilk ? milkWalkAudio : walkAudio;

        walkAudioSource.loop = true;
        walkAudioSource.Play();
    }

    // 캐릭터가 멈출 때 호출
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

    public void PlayButtonClickSound()
    {
        Debug.Log("🔊 [Sound] 버튼 클릭 소리");
        fxAudioSource.PlayOneShot(buttonClickAudio);
    }

    public void PlayColorMixSound()
    {
        fxAudioSource.PlayOneShot(colorMix2Audio);

        Debug.Log("🔊 [Sound] 색 조합 소리");
    }

    public void PlayScaleUpSound()
    {
        Debug.Log("🔊 [Sound] 크기 증가 소리");
        fxAudioSource.PlayOneShot(scaleUpAudio);
    }

    public void PlayJumpSound()
    {
        Debug.Log("🔊 [Sound] 점프 소리");
        fxAudioSource.PlayOneShot(jumpAudio);
    }

}