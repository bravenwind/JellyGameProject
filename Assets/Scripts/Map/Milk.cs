using UnityEngine;

public class Milk : MonoBehaviour
{
    [SerializeField]
    private float milkScaleDecreaseTime = 5.0f;

    [SerializeField]
    private float scaleDecreaseTimer = 0.0f;

    [SerializeField]
    private PlayerColorAbsorb playerColorAbsorb;

    [SerializeField]
    private MainCamera_Action mainCamera_Action;

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            scaleDecreaseTimer += Time.deltaTime;

            if (scaleDecreaseTimer >= milkScaleDecreaseTime)
            {
                // 크기 및 카메라 감소 실행
                StartCoroutine(playerColorAbsorb.DecreaseScale(DataManager.Instance.scaleIncreaseTime));
                if (mainCamera_Action != null) mainCamera_Action.ScaleDecreased();

                // 🔥 핵심 수정: 실행 후 타이머를 0으로 초기화해야 중복 실행을 막을 수 있음!
                scaleDecreaseTimer = 0.0f;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            scaleDecreaseTimer = 0.0f;
        }
    }
}
