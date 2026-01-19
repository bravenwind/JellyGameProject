using UnityEngine;
using System.Collections.Generic;

public class JellyDataManager : MonoBehaviour
{
    public static JellyDataManager Instance { get; private set; }

    [Header("CSV File")]
    public TextAsset jellyDataCsv;

    private JellyDAO dao;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            dao = new JellyDAO();
            if (jellyDataCsv != null)
            {
                dao.LoadData(jellyDataCsv);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public JellyData GetJelly(string name)
    {
        return dao.GetJellyData(name);
    }
}