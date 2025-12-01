using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;


public class SpaceSaveManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SpaceData spaceData;
    [SerializeField] private FurnitureManager furnitureManager;

    void Start()
    {
        if (spaceData == null)
        {
            spaceData = SpaceData.Instance;
        }

        if (furnitureManager == null)
        {
            furnitureManager = FindFirstObjectByType<FurnitureManager>(FindObjectsInactive.Include);
        }
    }

    // 1) 현재 레이아웃 + 가구 상태를 합쳐서 JSON 문자열로 만들어서 저장
    public void SaveFileToJSON(string rawFileName)
    {
        if (spaceData == null || spaceData._layout == null)
        {
            Debug.LogError("[SpaceSaveManager] SpaceData 또는 Layout 이 없습니다.");
            return;
        }

        if (furnitureManager != null)
        {
            // 현재 인벤토리 -> layout.furnitures 에 반영
            List<FurnitureItemData> items = furnitureManager.GetAllItems();
            spaceData._layout.furnitures = new List<FurnitureItemData>(items);
        }

        string safeName = MakeSafeFileName(rawFileName);

        if (string.IsNullOrEmpty(safeName))
        {
            // 이름이 비어 있으면 날짜 기반 기본 이름
            safeName = "Space_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        // 저장 폴더 (맘에 안 들면 폴더 이름만 바꾸면 됨)
        string dir = Path.Combine(Application.persistentDataPath, "SaveData");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string filePath = Path.Combine(dir, safeName + ".json");

        string json = JsonUtility.ToJson(spaceData._layout, true);
        File.WriteAllText(filePath, json, Encoding.UTF8);

        // 덮어쓰기는 일단 허용 (나중에 체크 로직 넣어도 됨)
        Debug.Log("[SpaceSaveManager] Saved layout to: " + filePath);
    }
    
    private string MakeSafeFileName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string invalidChars = new string(Path.GetInvalidFileNameChars());
        string result = raw;

        for (int i = 0; i < invalidChars.Length; i++)
        {
            string ch = invalidChars[i].ToString();
            if (result.Contains(ch))
            {
                result = result.Replace(ch, "_");
            }
        }

        result = result.Trim();
        return result;
    }
}
