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

        //지금 이 줄이 보여주고 있는 방. 버튼을 눌렀을 때 누구를 넘길지가 여기서 나온다
        private RoomEntry current;

        // ★ 예전엔 Setup 하나가 둘 다 했다
        //   목록은 초당 4번 갱신되는데, 그때마다 RemoveAllListeners 로 버튼을 비우고
        //   람다를 새로 만들어 다시 걸었다. 방이 8개면 초당 64개씩 클로저와 델리게이트가
        //   쓰레기로 쌓인다. 같은 파일에서 방 목록 List 는 재사용하려고 신경 썼는데
        //   정작 이쪽으로 그보다 훨씬 많이 새고 있었다.
        //
        //   버튼을 거는 일은 줄을 만들 때 한 번이면 되고, 갱신할 때마다 필요한 건
        //   글자를 바꾸는 것뿐이다. 그래서 둘로 나눈다.

        /// <summary>줄을 만든 직후 한 번. 눌렀을 때 무엇을 할지 건다.</summary>
        public void Bind(Action<RoomEntry> onJoin)
        {
            if (joinButton == null)
                return;

            joinButton.onClick.RemoveAllListeners();

            //current 를 캡처하지 않고 필드를 읽는다. 갱신 때마다 방이 바뀌어도
            //람다는 그대로 두고 필드만 갈아끼우면 된다
            joinButton.onClick.AddListener(() => onJoin?.Invoke(current));
        }

        /// <summary>갱신할 때마다. 글자와 버튼 활성만 바꾼다.</summary>
        //RoomInfo(UDP 비콘의 해석 결과)가 아니라 RoomEntry 를 받는다.
        //줄 하나가 보여주는 것은 어느 전송으로 찾은 방이든 똑같기 때문이다
        public void Show(RoomEntry room)
        {
            current = room;

            if (nameText != null)
                nameText.text = room.HostName;

            if (modeText != null)
                modeText.text = RoomConfig.ModeLabel(room.Mode);

            if (addressText != null)
                addressText.text = room.Address;

            if (countText != null)
            {
                string ai = room.AiCount > 0 ? $"   AI {room.AiCount}" : string.Empty;
                countText.text = $"{room.Current} / {room.Needed}명{ai}";
            }

            if (joinButton != null)
                joinButton.interactable = !room.IsFull;
        }
    }
}
