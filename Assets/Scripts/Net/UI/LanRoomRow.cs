using System;
using JellyNet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LanRoomRow : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private TMP_Text addressText;
    [SerializeField] private Button joinButton;

    public void Setup(LanDiscovery.RoomInfo room, Action<LanDiscovery.RoomInfo> onJoin)
    {
        if (nameText != null)
            nameText.text = room.HostName;

        if (modeText != null)
            modeText.text = LanLobby.Label(room.Mode);

        if (addressText != null)
            addressText.text = room.Address;

        if (countText != null)
        {
            string ai = room.AiCount > 0 ? $"   AI {room.AiCount}" : string.Empty;
            countText.text = $"{room.Current} / {room.Needed}명{ai}";
        }

        if (joinButton == null)
            return;

        joinButton.onClick.RemoveAllListeners();
        joinButton.interactable = !room.IsFull;

        LanDiscovery.RoomInfo captured = room;
        joinButton.onClick.AddListener(() => onJoin?.Invoke(captured));
    }
}
