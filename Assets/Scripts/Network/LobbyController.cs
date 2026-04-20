using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyController : MonoBehaviour
{
    public TMP_InputField nameInputField;
    public Button startButton;

    public ProloguePanelSequence prologue;

    private void Start()
    {
        // 버튼을 누르면 NetworkManager의 StartConnect 호출
        startButton.onClick.AddListener(OnStartButtonClicked);
    }

    private void OnStartButtonClicked()
    {
        string playerName = nameInputField != null ? nameInputField.text : "Jelly";
        // NetworkManager는 싱글톤이므로 바로 접근 가능

        prologue.StartPrologue(playerName);

        // 여러 번 눌리는 것(연타) 방지
        startButton.interactable = false;
    }
}