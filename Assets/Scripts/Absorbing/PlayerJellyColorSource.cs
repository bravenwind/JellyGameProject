using UnityEngine;

public class PlayerJellyColorSource : JellyColorSource
{
    // ★ 여기서 색을 정하지 않는다
    //   흰색으로 시작해두기만 하고, 실제 색은 젤리를 먹을 때
    //   PlayerColorVisual이 RYB 누적치로 칠한다.
    //
    //   예전엔 DataManager.initialColorSet에서 머티리얼과 색을 가져오는 코드가
    //   주석으로 남아 있었다. 그 필드는 이미 없어진 지 오래다.
    protected override void Start()
    {
        base.Start();

        jellyColor = Color.white;
        rend.material.SetColor(EmissionId, jellyColor);
    }

    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
}
