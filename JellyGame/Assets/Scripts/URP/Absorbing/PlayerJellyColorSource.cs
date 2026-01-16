using UnityEngine;

public class PlayerJellyColorSource : JellyColorSource
{
    protected override void Start()
    {
        base.Start();
        rend.material = DataManager.Instance.initialColorSet.colorMaterial;
        jellyColor = DataManager.Instance.initialColorSet.normal;
        rend.material.SetColor("_Emission", jellyColor);
    }
}
