using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MyFurnitureSlot : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    public string instanceId;

    private PlaceSceneUI placeSceneUI;

    public void Setup(PlaceSceneUI ui, string instanceId)
    {
        placeSceneUI = ui;
        this.instanceId = instanceId;

        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnButtonClick);
        }
    }

    private void OnButtonClick()
    {
        if (placeSceneUI != null)
        {
            placeSceneUI.OnClickFurnitureSlot(instanceId);
        }
    }
    
    // Begin Drag
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (placeSceneUI != null)
        {
            //placeSceneUI.OnBeginDragFurnitureSlot(instanceId, eventData.position);
        }
    }
    
    // On Drag
    public void OnDrag(PointerEventData eventData)
    {
        if (placeSceneUI != null)
        {
            //placeSceneUI.OnDragFurnitureSlot(instanceId, eventData.position);
        }
    }
    
    // End Drag
    public void OnEndDrag(PointerEventData eventData)
    {
        if (placeSceneUI != null)
        {
            //placeSceneUI.OnEndDragFurnitureSlot(instanceId, eventData.position);
        }
    }
}