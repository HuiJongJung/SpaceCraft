using Ookii.Dialogs;
using System.IO;
using TMPro;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FloorPlanUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject templatePanel;
    public GameObject previewPanel;

    [Header("UI")]
    public GameObject contentPanel;
    public GameObject noticeNullPlace;
    public GameObject templatePrefab;

    [Header("Preview")]
    public Image previewImage = null;

    [Header("SpaceData")]
    public FloorPlanInterpreter floorPlanInterpreter;
    public SpaceData spaceData;

    void Start()
    {
        LoadTemplates();
        ShowMain();
    }

    /* ========================== */
    /*         버튼 이벤트        */
    /* ========================== */

    public void ReturnBack()
    {
        if (mainPanel.activeSelf)
            LoadMainScene();
        else if (templatePanel.activeSelf)
            ShowMain();
    }

    public void ShowMain()
    {
        DeactiveAllPanels();
        mainPanel.SetActive(true); 
    }
    
    public void ShowTemplate()
    {
        DeactiveAllPanels();
        templatePanel.SetActive(true);  
    }
    
    public void ShowPreview()
    {
        DeactiveAllPanels();
        previewPanel.SetActive(true);
    }

    private void DeactiveAllPanels()
    {
        mainPanel.SetActive(false);
        templatePanel.SetActive(false);
        previewPanel.SetActive(false);
    }

    public void OnClickTemplateIcon(GameObject selected)
    {
        // 미리보기 변경
        // mask가 포함된 이미지 탐색 => 이미지 이름 통일하는 방식으로 변경 예정

        // space.json 파일 매핑
        // load space

        // 메인 화면으로 돌아가기
        ShowMain();
    }

    // Preview -> Place Scene
    public void OnClickPlace()
    {
        //Send space information to next scene
        if (previewImage.sprite == null)
        {
            // todo : 에러 창 띄우기
            return;
        }

        SceneManager.LoadScene("03_PlaceScene");
    }

    // go to  Main Scene
    public void LoadMainScene()
    {
        //Send space information to next scene
        SceneManager.LoadScene("01_MainScene");
    }

    // Upload FloorPlan
    /* ======== Comment ======== */
    // [ 지원 ]
    // EditorUtility에서 지원하는 기능은 Editor에서만 동작하고,
    // 빌드된 파일에서는 작동하지 않는 문제가 있어서 빌드에서도 동작하는 방식으로 "수정 예정"
    /* ======================== */
    public void ApplyOutput()
    {
        ApplyJson(Path.Combine(Application.persistentDataPath, "UserData", "space.json"));
        PreviewUpload();
    }

    public void OnClickUpload() {
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select floor image", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path)) {

            // Save the image file for extract floor plan
            string saveDir = Path.Combine(Application.persistentDataPath, "UserData");

            // Create save directory
            if (!Directory.Exists(saveDir))
                Directory.CreateDirectory(saveDir);

            string saveFilePath = Path.Combine(saveDir, "Input_FloorPlan" + Path.GetExtension(path));

            // Copy Image
            try
            {
                File.Copy(path, saveFilePath, true);
            }
            catch (IOException ex)
            {
                Debug.LogError($"Failed to copy file.: {ex.Message}");
                return;
            }

            //Communication With Server & Get Files
            floorPlanInterpreter.InterpretFloorPlan(saveFilePath);
        }
    }
    
    //Preveiw Upload
    private void PreviewUpload()
    {
        // Texture Preview
        Texture2D tex = LoadTexture(Path.Combine(Application.persistentDataPath, "UserData", "floorplan_mask.png"));

        Sprite img = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f
        );

        previewImage.sprite = img;
        previewImage.preserveAspect = true;
        previewImage.enabled = true;
    }

    private void ApplyJson(string jsonPath)
    {
        //Save room data
        if (File.Exists(jsonPath))
        {
            string jsonText = File.ReadAllText(jsonPath);
            spaceData.roomsJson = new TextAsset(jsonText);
            spaceData.LoadData();
            Debug.Log("space layout 생성 완료");
        }
        else
        {
            Debug.LogError("space.json 파일을 찾을 수 없습니다: " + jsonPath);
            return;
        }

    }

    Texture2D LoadTexture(string path) {
        var bytes = System.IO.File.ReadAllBytes(path);
        var tex = new Texture2D(2,2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);
        tex.name = System.IO.Path.GetFileName(path);
        return tex;
    }

    // Load templates from template directory
    void LoadTemplates()
    {
        string templateDir = Path.Combine(Application.streamingAssetsPath, "FloorPlan", "Template");

        if (!Directory.Exists(templateDir))
        {
            Debug.LogWarning($"Template 폴더를 찾을 수 없습니다: {templateDir}");
            return;
        }

        // 기존에 Content 밑에 있던 항목 제거
        foreach (Transform child in contentPanel.transform)
        {
            Destroy(child.gameObject);
        }

        // 내부 폴더 하나씩 순회하면서 템플릿 생성 (templatePrefab)
        string[] templateFolders = Directory.GetDirectories(templateDir);

        foreach (string folderPath in templateFolders)
        {
            string folderName = Path.GetFileName(folderPath);

            // 프리팹 인스턴스 생성 + 부모 transform 지정 (contentPanel)
            GameObject item = Instantiate(templatePrefab, contentPanel.transform);
            item.name = folderName;

            // Template 하위 Text를 폴더 명으로 변경
            TextMeshProUGUI label = item.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = folderName;
            }

            // Template 하위 Button의 이미지를
            // 폴더 내부에 "mask" 문자열이 포함되지 않은 이미지를 가져와서 이미지 변경
            string previewImagePath = FindPreviewImagePath(folderPath);
            if (!string.IsNullOrEmpty(previewImagePath))
            {
                Sprite sprite = LoadSpriteFromFile(previewImagePath);
                if (sprite != null)
                {
                    Button button = item.GetComponentInChildren<Button>(true);
                    if (button != null)
                    {
                        Image img = button.GetComponent<Image>();
                        if (img != null)
                        {
                            img.sprite = sprite;
                            img.preserveAspect = true;
                        }
                    }
                }
            }

            // todo : 버튼 클릭 시, OnClickTemplateIcon 함수 호출할 수 있게 지정
        }
    }

    private string FindPreviewImagePath(string folderPath)
    {
        string[] files = Directory.GetFiles(folderPath);
        foreach (string path in files)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string nameNoExt = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") &&
                !nameNoExt.Contains("mask"))
            {
                return path;
            }
        }
        return null;
    }

    private Sprite LoadSpriteFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"이미지 파일을 찾을 수 없습니다: {path}");
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            Debug.LogWarning($"이미지 로드 실패: {path}");
            return null;
        }

        tex.name = Path.GetFileNameWithoutExtension(path);
        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f
        );
    }
}