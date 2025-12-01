using UnityEngine;
using UnityEngine.SceneManagement;

public class MainSceneUI : MonoBehaviour
{

    public void OnClickFloorPlanButton()
    {
        //Send space information to next scene
        
        SceneManager.LoadScene("02_FloorPlanScene");
    }

    public void OnClickLoadSpaceButton()
    {
        //string path = UnityEditor.EditorUtility.OpenFilePanel("Select floor image", "", "png,jpg,jpeg");
        
        SceneManager.LoadScene("03_PlaceScene");
    }
}
