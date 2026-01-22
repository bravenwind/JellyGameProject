using UnityEngine;

public class ClickDebug : MonoBehaviour
{
    void Update()
    {
        // 마우스 왼쪽 버튼을 클릭했을 때 (0: 왼쪽, 1: 오른쪽, 2: 휠)
        if (Input.GetMouseButtonDown(0))
        {
            // 카메라에서 마우스 위치로 향하는 레이(광선) 생성
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // 레이를 발사하여 무언가에 부딪혔는지 확인
            // Mathf.Infinity는 거리 제한 없음
            if (Physics.Raycast(ray, out hit, Mathf.Infinity))
            {
                // 부딪힌 오브젝트의 이름을 콘솔에 출력
                Debug.Log("감지된 오브젝트: " + hit.collider.gameObject.name);

                // (선택사항) 씬 뷰에서 레이를 빨간색 선으로 표시 (디버그 용도)
                Debug.DrawRay(ray.origin, ray.direction * 100, Color.red, 1f);
            }
        }
    }
}