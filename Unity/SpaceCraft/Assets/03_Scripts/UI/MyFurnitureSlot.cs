using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MyFurnitureSlot : MonoBehaviour, IPointerClickHandler
{
    public string instanceId;

    public Image img;

    private PlaceSceneUI placeSceneUI;
    
    [SerializeField] private Color placedColor = Color.green;
    [SerializeField] private Color unplacedColor = Color.white;

    public void Setup(PlaceSceneUI ui, string instanceId)
    {
        placeSceneUI = ui;
        this.instanceId = instanceId;

        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
        }
    }
    
    // Set Color of Slot
    public void SetColor(bool placed)
    {
        if (img == null) return;
        
        //Set Color
        if (placed) img.color = placedColor;
        else img.color = unplacedColor;
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (placeSceneUI == null)
        {
            Debug.LogWarning("[MyFurnitureSlot] placeSceneUI is null");
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // 좌클릭 → 배치 모드 진입용
            placeSceneUI.OnLeftClickFurnitureSlot(instanceId);
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // 우클릭 → 가구 정보창
            placeSceneUI.OnRightClickFurnitureSlot(instanceId);
        }
    }
}