using UnityEngine;

/// <summary>
/// [Z1] 폐기 예정(no-op).
/// 과거에는 씬 로드 1초 뒤 FindAnyObjectByType&lt;LoadingBGSlideAni&gt;로 찾은 로딩 커튼의
/// 루트를 강제 Destroy했다. 그런데 이 씬(Canvas)에는 자체 LoadingBGSlideAni가 없어
/// 반드시 DontDestroyOnLoad로 넘어온 LoadingSceneController의 커튼이 잡혔고,
/// ExitRoutine(최소 표시시간 + 슬라이드 아웃 후 자체 정리)과 '이중 파괴 경쟁'이 붙어
/// 씬 로드가 빠르면 커튼이 연출 없이 1초 만에 하드컷될 수 있었다.
///
/// 로딩 커튼 정리는 LoadingSceneController.ExitRoutine 단일 출처로 통일한다.
/// 이 컴포넌트는 빌드 4개 씬(Game_io×2, GameResult×2)의 Canvas에 부착돼 있어
/// 씬 파일 수정 없이 코드만 무해화했다 — 에디터에서 해당 씬을 열 일이 있을 때
/// 컴포넌트를 제거한 뒤 이 파일을 삭제하면 된다.
/// </summary>
public class NextSceneManager : MonoBehaviour
{
}
