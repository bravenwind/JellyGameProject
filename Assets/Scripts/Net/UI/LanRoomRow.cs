using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JellyNet
{
        public class LanRoomRow : MonoBehaviour
        {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text modeText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text addressText;
        [SerializeField] private Button joinButton;

        //RoomInfo(UDP 비콘의 해석 결과)가 아니라 RoomHandle 을 받는다.
        //줄 하나가 보여주는 것은 어느 전송으로 찾은 방이든 똑같기 때문이다
        public void Setup(RoomHandle room, Action<RoomHandle> onJoin)
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

            RoomHandle captured = room;
            joinButton.onClick.AddListener(() => onJoin?.Invoke(captured));
        }
    }
}
