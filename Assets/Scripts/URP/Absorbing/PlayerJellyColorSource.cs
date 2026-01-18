using UnityEngine;
using DG.Tweening;

public class PlayerJellyColorSource : JellyColorSource
{
    protected override void Start()
    {
        base.Start();
        rend.material = DataManager.Instance.initialColorSet.colorMaterial;
        jellyColor = DataManager.Instance.initialColorSet.normal;
        rend.material.SetColor("_Emission", jellyColor);
    }

    //private void Update()
    //{
    //    if (Input.GetKeyDown(KeyCode.P))
    //    {
    //        // 카메라가 0.5초 동안, 강도 1로, 마구 흔들립니다 (지진/충돌 효과)
    //        //Camera.main.transform.DOShakePosition(1.0f, 1f);

    //        //회전까지 같이 흔들면 더 어지러운 느낌(폭발/ 혼란)
    //        Camera.main.transform.DOShakeRotation(0.5f, 30f);

    //        //Camera.main.DOFieldOfView(100f, 0.3f).OnComplete(() =>
    //        //{
    //        //    // 다 빨려 들어가면 다시 원래대로(60) 복구
    //        //    Camera.main.DOFieldOfView(60f, 0.1f);
    //        //});
    //    }
    //}
}
