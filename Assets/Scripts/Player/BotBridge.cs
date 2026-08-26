using UnityEngine;

public class BotBridge : MonoBehaviour
{
    private PlayerScaleController scaleCtrl;
    private AIPlayerMovement bot;

    private void Awake()
    {
        scaleCtrl = GetComponentInChildren<PlayerScaleController>();
        bot = GetComponent<AIPlayerMovement>();
    }

    private void OnEnable()
    {
        if (scaleCtrl != null)
            scaleCtrl.OnPostScalePhysics += HandlePostScalePhysics;
    }

    private void OnDisable()
    {
        if (scaleCtrl != null)
            scaleCtrl.OnPostScalePhysics -= HandlePostScalePhysics;
    }

    //몸집이 바뀌면 NavMeshAgent를 새 크기에 맞춰 다시 맞춘다(캡슐 크기·회피 우선순위·재착지).
    //봇에만 필요한 일이라 여기 있다 — 사람은 CharacterController라 이 뒤처리가 없다
    private void HandlePostScalePhysics()
    {
        if (bot != null)
            bot.ChangeScale();
    }
}
