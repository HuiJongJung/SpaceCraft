using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainSceneUI : MonoBehaviour
{
    [Header("Main Panel UI")]
    public GameObject mainPanel;

    [Header("Load Panel UI")]
    public GameObject loadPanel;
    public GameObject contentPanel;
    public GameObject spacePrefab;

    [Header("Space Data")]
    public SpaceData spaceData;

    public void Start()
    {
        ActiveMainPanel();
        LoadSpaceData();
    }

    public void DeactiveAllPanel()
    {
        mainPanel.SetActive(false);
        loadPanel.SetActive(false);
    }

    public void ActiveMainPanel()
    {
        DeactiveAllPanel();
        mainPanel.SetActive(true);
    }

    public void ActiveLoadPanel()
    {
        DeactiveAllPanel();
        loadPanel.SetActive(true);
    }

    public void OnClickFloorPlanButton()
    {
        //Send space information to next scene
        
        SceneManager.LoadScene("02_FloorPlanScene");
    }

    public void OnClickLoadSpaceButton(string spaceDirPath)
    {
        // SpaceData에 JSON 입력 후 씬 전환
        TextAsset jsonText = Resources.Load<TextAsset>(Path.Combine(spaceDirPath, "space.json"));

        if (jsonText == null)
        {
            Debug.LogError("Can't find JSON file.");
            return;
        }

        spaceData.roomsJson = jsonText;
        SceneManager.LoadScene("03_PlaceScene");
    }

    // SaveData 폴더에서 저장된 공간 정보 불러오기
    public void LoadSpaceData()
    {
        string targetDir = Path.Combine(Application.persistentDataPath, "SaveData");

        if (!Directory.Exists(targetDir))
        {
            Debug.LogWarning($"SaveData Directory is not exist. : {targetDir}");
            return;
        }

        // Dummy Content 제거
        foreach (Transform child in contentPanel.transform)
        {
            Destroy(child.gameObject);
        }

        // 폴더 정보 바탕으로 data 생성
        string[] saveFolders = Directory.GetDirectories(targetDir);

        foreach (string folderPath in saveFolders)
        {
            string folderName = Path.GetFileName(folderPath);

            // 인스턴스 생성 + 부모 transform 지정
            GameObject item = Instantiate(spacePrefab, contentPanel.transform);
            item.name = folderName;

            // Text를 폴더 명으로 변경
            TextMeshProUGUI label = item.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = folderName;
            }

            // Button 이미지를 미리보기 이미지로 변경
            Button button = item.GetComponentInChildren<Button>(true);
            string previewImagePath = (Path.Combine(folderPath, "preview.png"));

            if (File.Exists(previewImagePath))
            {
                byte[] imageData = File.ReadAllBytes(previewImagePath);

                Texture2D texture = new Texture2D(2, 2);

                if (texture.LoadImage(imageData))
                {
                    Rect rect = new Rect(0, 0, texture.width, texture.height);
                    Vector2 pivot = new Vector2(0.5f, 0.5f);
                    Sprite newSprite = Sprite.Create(texture, rect, pivot);

                    button.image.sprite = newSprite;
                    button.image.preserveAspect = true;
                }
            }

            button.onClick.AddListener(() => { OnClickLoadSpaceButton(folderPath); });
        }
    }
}
