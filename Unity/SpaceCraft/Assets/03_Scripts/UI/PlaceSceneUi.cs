using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class PlaceSceneUi : MonoBehaviour
{
    public GameObject mainPanel;
    public GameObject categoryPanel;
    public GameObject sizePanel;
    public GameObject detailPanel;
    public GameObject loadingPanel;

    public GameObject detailPanelReadOnly;
    public GameObject autoPlacePanel;
    
    
    void Start()
    {
        ShowLoading();
    }
    
    private void DeactiveAllPanels()
    {
        mainPanel.SetActive(false);
        categoryPanel.SetActive(false);
        sizePanel.SetActive(false);
        detailPanel.SetActive(false);
        loadingPanel.SetActive(false);
        detailPanelReadOnly.SetActive(false);
        autoPlacePanel.SetActive(false);
    }

    public void ShowMain()
    {
        DeactiveAllPanels();
        mainPanel.SetActive(true);
    }
    
    public void ShowCategory()
    {
        DeactiveAllPanels();
        categoryPanel.SetActive(true);
    }

    public void ShowSize()
    {
        DeactiveAllPanels();
        sizePanel.SetActive(true);
    }

    public void ShowDetail()
    {
        detailPanel.SetActive(true);
    }

    public void CloseDetail()
    {
        detailPanel.SetActive(false);
    }

    public void ShowDetailPanelReadOnly()
    {
        detailPanelReadOnly.SetActive(true);
    }

    public void CloseDetailPanelReadOnly()
    {
        detailPanelReadOnly.SetActive(false);
    }

    public void ToggleAutoPlacePanel()
    {
        if (autoPlacePanel.activeSelf) autoPlacePanel.SetActive(false);
        else autoPlacePanel.SetActive(true);
    }
    
    public void ShowLoading()
    {
        DeactiveAllPanels();
        loadingPanel.SetActive(true);

        StartCoroutine(DoLoading());
    }
    
    private IEnumerator DoLoading()
    {
        yield return new WaitForSeconds(1.0f);

        ShowMain();
    }
    
    //go to Floor Plan Scene
    public void OnClickFloorPlanButton()
    {
        SceneManager.LoadScene("02_FloorPlanScene");
    }
    
    //go to Simulation Scene
    public void OnClickSaveButton()
    {
        SceneManager.LoadScene("04_SimulationScene");
    }
    
}
