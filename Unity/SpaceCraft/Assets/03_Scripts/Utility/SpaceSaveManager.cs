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
        string dir = Path.Combine(Application.persistentDataPath, "SaveData", safeName);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string filePath = Path.Combine(dir, "space.json");

        string json = JsonUtility.ToJson(spaceData._layout, true);
        File.WriteAllText(filePath, json, Encoding.UTF8);

        // 덮어쓰기는 일단 허용 (나중에 체크 로직 넣어도 됨)
        Debug.Log("[SpaceSaveManager] Saved layout to: " + filePath);


        // 미리보기 이미지 저장
        MakePreviewImage(Path.Combine(dir, "preview.png"));
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

    #region Generate Preview Image
    // 미리보기 이미지를 촬영하여 png로 저장
    private void MakePreviewImage(string filePath)
    {
        // 저장 폴더 없으면 생성
        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 캡처 해상도 (필요하면 조절)
        int width = 512;
        int height = 512;

        // 임시 카메라 생성
        GameObject camObj = new GameObject("PreviewCamera_TopView");
        Camera cam = camObj.AddComponent<Camera>();

        // 월드 (0,0)를 중심으로 위에서 아래로 내려보는 Top View 설정
        float camHeight = 10.0f;  // 높이 (필요에 맞게 조절)
        float orthoSize = 5.0f;   // 보이는 범위 (절반 길이, 필요에 맞게 조절)

        cam.transform.position = new Vector3(0f, camHeight, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // -Y 방향으로 내려다 보도록

        cam.orthographic = false;
        cam.orthographicSize = orthoSize;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);

        RenderTexture prevRT = RenderTexture.active;
        RenderTexture prevCamRT = cam.targetTexture;

        try
        {
            cam.targetTexture = rt;
            RenderTexture.active = rt;
            cam.Render();

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            byte[] pngData = tex.EncodeToPNG();
            if (pngData != null)
            {
                File.WriteAllBytes(filePath, pngData);
                Debug.Log($"Preview image saved: {filePath}");
            }

            Object.Destroy(tex);
        }
        finally
        {
            cam.targetTexture = prevCamRT;
            RenderTexture.active = prevRT;

            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(camObj);
        }
    }

    #endregion
}
