using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class FloorPlanUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject templatePanel;
    public GameObject previewPanel;

    [Header("Preview")]
    public Image previewImage;

    void Start()
    {
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
    
    // Upload FloorPlan
    public void OnClickUpload() {
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select floor image", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path)) {
            Texture2D tex = LoadTexture(path);
            
            Sprite img = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f
            );

            previewImage.sprite = img;
            previewImage.preserveAspect = true;
            previewImage.enabled = true;
            
            //Communication With Server & Get Files
            
            ShowPreview();
        }
    }
    
    //Load Template
    public void OnClickTemplateIcon()
    {
        //Load Saved Template Files
        
        ShowPreview();
    }
    
    // Preview -> Place Scene
    public void OnClickPlace()
    {
        //Send space information to next scene
        
        SceneManager.LoadScene("03_PlaceScene");
    }
    
    // go to  Main Scene
    public void LoadMainScene()
    {
        //Send space information to next scene
        
        SceneManager.LoadScene("01_MainScene");
    }

    Texture2D LoadTexture(string path) {
        var bytes = System.IO.File.ReadAllBytes(path);
        var tex = new Texture2D(2,2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);
        tex.name = System.IO.Path.GetFileName(path);
        return tex;
    }
}