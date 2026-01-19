using UnityEngine;

public class WorldToScreenGUI : MonoBehaviour
{
    [Header("GUI Settings")]
    public string displayText = "Target"; // 표시할 텍스트
    public Vector2 size = new Vector2(100, 30); // 라벨의 크기
    public Vector2 offset = new Vector2(0, -50); // 위치 미세 조정 (오프셋)

    [Header("Optional")]
    public bool showOnlyVisible = true; // 카메라 앞쪽에 있을 때만 표시할지 여부

    // ⭐ 폰트 크기 설정을 위한 변수 추가
    [Range(10, 100)]
    public int fontSize = 20;

    private void OnGUI()
    {
        // 메인 카메라가 없으면 실행 중단
        if (Camera.main == null) return;

        // 1. 오브젝트의 3D 월드 좌표를 2D 화면 좌표로 변환
        Vector3 screenPos = Camera.main.WorldToScreenPoint(this.transform.position);

        // 2. 오브젝트가 카메라 뒤에 있는지 확인 (z < 0이면 카메라 뒤)
        if (showOnlyVisible && screenPos.z < 0)
            return;

        // 3. 좌표계 변환 (가장 중요한 부분)
        // WorldToScreenPoint: (0,0)이 화면 왼쪽 아래 (Bottom-Left)
        // GUI (OnGUI): (0,0)이 화면 왼쪽 위 (Top-Left)
        // 따라서 Y축을 뒤집어 줘야 함
        float guiY = Screen.height - screenPos.y;

        // 4. GUI를 그릴 Rect 계산 (화면 좌표 + 오프셋 - 크기의 절반을 빼서 중앙 정렬)
        Rect rect = new Rect(
            screenPos.x + offset.x - (size.x / 2),
            guiY + offset.y - (size.y / 2),
            size.x,
            size.y
        );

        // ⭐ 1. 새로운 스타일 생성 (기존 Box 스타일을 복사해서 만듦)
        GUIStyle customStyle = new GUIStyle(GUI.skin.box);

        // ⭐ 2. 폰트 크기 및 정렬 설정
        customStyle.fontSize = fontSize;
        customStyle.alignment = TextAnchor.MiddleCenter; // 텍스트를 정중앙에 정렬
        customStyle.normal.textColor = Color.white; // 글자 색상 (필요시 변경)

        // ⭐ 3. 스타일 적용하여 그리기 (3번째 인자로 style 전달)
        GUI.Box(rect, displayText, customStyle);
    }
}