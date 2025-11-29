using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class PlaceSceneUI : MonoBehaviour
{
    [Header("External Systems")]
    [SerializeField] private SpaceData spaceData;
    [SerializeField] private FurnitureManager furnitureManager;
    [SerializeField] private RoomManager roomManager;
    [SerializeField] private FurniturePlacementController placementController;
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;
    
    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject categoryPanel;
    public GameObject sizePanel;
    public GameObject detailPanel;
    public GameObject loadingPanel;
    public GameObject detailPanelReadOnly;
    public GameObject autoPlacePanel;
    public GameObject simulationPanel;

    [Header("Main UI (My Furniture List)")]
    public Button returnButton;
    public Transform myFurnitureListRoot;
    public GameObject furnitureSlotPrefab;
    public Button prevButton;
    public Button nextButton;
    public Button roomNameButton;
    public Button saveButton;
    public Button categoryButton;
    public Button autoPlaceButton;
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI indexText;

    [Header("AutoPlace UI")]
    public Button closeAutoPlaceButton;
    public Button startAutoPlaceButton;
    
    [Header("Detail Panel (ReadOnly)")]
    public Image roFurnitureImage;
    public TextMeshProUGUI roNameText;
    
    public TMP_InputField roWidthText;
    public TMP_InputField roDepthText;
    public TMP_InputField roHeightText;
    
    public Toggle roWallFrontToggle;
    public Toggle roWallBackToggle;
    public Toggle roWallLeftToggle;
    public Toggle roWallRightToggle;

    public TMP_InputField roClearanceFrontText;
    public TMP_InputField roClearanceBackText;
    public TMP_InputField roClearanceLeftText;
    public TMP_InputField roClearanceRightText;

    public Toggle roPrimaryToggle;
    
    public Toggle roPrivacyToggle;
    public Toggle roPrivacyFrontToggle;
    public Toggle roPrivacyBackToggle;
    public Toggle roPrivacyLeftToggle;
    public Toggle roPrivacyRightToggle;

    public Button closeDetailPanelReadOnlyButton;
    public Button deleteFurnitureButton;

    [Header("Category UI")] 
    public Button returnToMainButton;
    public Transform categoryListRoot;   
    public GameObject categorySlotPrefab;

    [Header("Size UI")] 
    public Button returnToCategoryButton;
    public Transform sizeListRoot;
    public GameObject sizeItemPrefab;


    [Header("Detail Panel (Editable)")] 
    public Image detailFurnitureImage;
    public TextMeshProUGUI detailNameText;
    
    public TMP_InputField widthInput;
    public TMP_InputField depthInput;
    public TMP_InputField heightInput;
    
    public Toggle wallFrontToggle;
    public Toggle wallBackToggle;
    public Toggle wallLeftToggle;
    public Toggle wallRightToggle;
    
    public TMP_InputField clearanceFrontInput;
    public TMP_InputField clearanceBackInput;
    public TMP_InputField clearanceLeftInput;
    public TMP_InputField clearanceRightInput;
    
    public Toggle primaryToggle;
    
    public Toggle privacyToggle;
    public Toggle privacyFrontToggle;
    public Toggle privacyBackToggle;
    public Toggle privacyLeftToggle;
    public Toggle privacyRightToggle;

    public Button closeDetailButton;
    public Button addFurnitureButton;

    [Header("Simulation Panel")] 
    public Button returnToPlaceButton;
    
    // 현재 선택 상태
    private string currentReadOnlyInstanceId;
    private FurnitureDefinition currentDefinition;
    private bool hasCurrentDefinition = false;

    private FurnitureSizeTemplate currentTemplate;
    private bool hasCurrentTemplate = false;
    
    // Slot Manage
    private Dictionary<string, MyFurnitureSlot> slotMap =
        new Dictionary<string, MyFurnitureSlot>();
    
    // Placce Mode Manage
    private bool isPlacementMode = false;
    
    void Start()
    {
        ShowLoading();
        
        if (spaceData == null)
        {
            spaceData = SpaceData.Instance;
        }

        if (roomManager == null)
        {
            roomManager = FindObjectOfType<RoomManager>();
        }

        if (gridBuilder == null)
        {
            gridBuilder = FindObjectOfType<RoomPlacementGridBuilder>();
        }

        SetButtonListeners();
        
        if (spaceData == null || spaceData._layout == null || spaceData._layout.rooms == null)
        {
            Debug.LogWarning("[RoomSelectUI] SpaceLayout.rooms 가 비어있음.");
            return;
        }

        if (roomManager.GetRoomCount() == 0)
        {
            Debug.LogWarning("[RoomSelectUI] 방이 0개임.");
            return;
        }

        // 처음 방으로 초기화
        roomManager.SetActiveOnly(0);
        UpdateRoomView();
    }

    public void SetButtonListeners()
    { 
        // Return Button
        returnButton.onClick.RemoveAllListeners();
        returnButton.onClick.AddListener(OnClickReturnButton);
        
        // Save Button
        saveButton.onClick.RemoveAllListeners();
        saveButton.onClick.AddListener(OnClickSaveButton);
        
        // Prev Button & next Button
        prevButton.onClick.RemoveAllListeners();
        prevButton.onClick.AddListener(OnClickPrevRoom);
        
        nextButton.onClick.RemoveAllListeners();
        nextButton.onClick.AddListener(OnClickNextRoom);
        
        // Add Function
        addFurnitureButton.onClick.RemoveAllListeners();
        addFurnitureButton.onClick.AddListener(OnClickAddFurnitureButton);
        
        // Close Detail Panel Read Only 
        closeDetailPanelReadOnlyButton.onClick.RemoveAllListeners();
        closeDetailPanelReadOnlyButton.onClick.AddListener(OnClickCloseDetailPanelReadOnly);
        
        // Delete Function
        deleteFurnitureButton.onClick.RemoveAllListeners();
        deleteFurnitureButton.onClick.AddListener(OnClickDeleteFurnitureButton);
        
        // roomName Button
        roomNameButton.onClick.RemoveAllListeners();
        roomNameButton.onClick.AddListener(OnClickRoomNameButton);
        
        // category Button
        categoryButton.onClick.RemoveAllListeners();
        categoryButton.onClick.AddListener(OnClickCategoryButton);
        
        // AutoPlace Button
        autoPlaceButton.onClick.RemoveAllListeners();
        autoPlaceButton.onClick.AddListener(OnClickAutoPlaceButton);
        
        // Close Auto Place Button
        closeAutoPlaceButton.onClick.RemoveAllListeners();
        closeAutoPlaceButton.onClick.AddListener(OnClickCloseAutoPlaceButton);
        
        // Start Auto Place Button
        startAutoPlaceButton.onClick.RemoveAllListeners();
        startAutoPlaceButton.onClick.AddListener(OnClickStartAutoPlaceButton);

        // Return To Main Button 
        returnToMainButton.onClick.RemoveAllListeners();
        returnToMainButton.onClick.AddListener(ShowMain);
        
        // Return To Category Button
        returnToCategoryButton.onClick.RemoveAllListeners();
        returnToCategoryButton.onClick.AddListener(ShowCategory);
        
        // close Detail Button
        closeDetailButton.onClick.RemoveAllListeners();
        closeDetailButton.onClick.AddListener(OnClickCloseDetailPanel);
        
        // Return To Place Button
        returnToPlaceButton.onClick.RemoveAllListeners();
        returnToPlaceButton.onClick.AddListener(OnClickReturnToPlaceButton);
    }
    
    public void SetSlotColor(string instanceId, bool isPlaced)
    {
        MyFurnitureSlot slot;
        bool found = slotMap.TryGetValue(instanceId, out slot);
        if (!found || slot == null)
        {
            return;
        }

        slot.SetColor(isPlaced);
    }
    
    #region PanelControl
    private void DeactiveAllPanels()
    {
        mainPanel.SetActive(false);
        categoryPanel.SetActive(false);
        sizePanel.SetActive(false);
        detailPanel.SetActive(false);
        loadingPanel.SetActive(false);
        detailPanelReadOnly.SetActive(false);
        autoPlacePanel.SetActive(false);
        simulationPanel.SetActive(false);
    }

    public void ShowMain()
    {
        DeactiveAllPanels();
        mainPanel.SetActive(true);
        
        // Refresh ToolBar
        RefreshFurnitureList();
    }
    
    public void ShowCategory()
    {
        DeactiveAllPanels();
        categoryPanel.SetActive(true);
        
        BuildCategoryList();
    }

    public void ShowSize()
    {
        DeactiveAllPanels();
        sizePanel.SetActive(true);
    }

    public void SetDetailPanel(bool isActive)
    {
        detailPanel.SetActive(isActive);
    }

    public void SetDetailPanelReadOnly(bool isActive)
    {
        detailPanelReadOnly.SetActive(isActive);
    }

    public void SetAutoPlacePanel(bool isActive)
    {
        autoPlacePanel.SetActive(isActive);
    }
    
    public void SetSimulationPanel(bool isActive)
    {
        simulationPanel.SetActive(isActive);
    }
    
    #endregion
    
    #region CategoryPanelMethod
    // Make Category List
    private void BuildCategoryList()
    {
        // Validate
        if (furnitureManager.GetDB() == null)
        {
            return;
        }
        
        // Reset Children
        ClearChildren(categoryListRoot);

        if (furnitureManager.GetDB().definitions == null)
        {
            return;
        }
        
        // Load All Definitions in DB
        for (int i = 0; i < furnitureManager.GetDB().definitions.Length; i++)
        {
            
            FurnitureDefinition def = furnitureManager.GetDB().definitions[i];
            if (def == null)
            {
                continue;
            }

            GameObject itemGo = Instantiate(categorySlotPrefab, categoryListRoot);
            
            // Button Event
            Button button = itemGo.GetComponent<Button>();
            FurnitureDefinition capturedDef = def;
            if (button != null)
            {
                button.onClick.AddListener(delegate { OnClickCategorySlot(capturedDef); });
            }

            // Apply UI
            Image image = itemGo.GetComponentInChildren<Image>();
            if (image != null && def.sprite != null)
            {
                image.sprite = def.sprite;
            }

            TextMeshProUGUI text = itemGo.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = def.name;
            }
        }
    }
    // CategorySlot OnClick Method
    private void OnClickCategorySlot(FurnitureDefinition def)
    {
        currentDefinition = def;
        hasCurrentDefinition = true;
        hasCurrentTemplate = false;

        // Go To Size Panel
        BuildSizeList();
        ShowSize();
    }
    
    #endregion
    
    #region SizePanelMethod
    
    //Make Size List
    private void BuildSizeList()
    {
        // Validate
        if (hasCurrentDefinition == false || currentDefinition == null)
        {
            return;
        }
        
        // Reset Children
        ClearChildren(sizeListRoot);
        
        bool hasTemplates = currentDefinition.sizeTemplates != null && currentDefinition.sizeTemplates.Length > 0;
    
        
        if (hasTemplates)
        {
            for (int i = 0; i < currentDefinition.sizeTemplates.Length; i++)
            {
                FurnitureSizeTemplate template = currentDefinition.sizeTemplates[i];
                GameObject itemGo = Instantiate(sizeItemPrefab, sizeListRoot);

                Button button = itemGo.GetComponent<Button>();
                FurnitureSizeTemplate capturedTemplate = template;
                if (button != null)
                {
                    button.onClick.AddListener(delegate { OnClickSizeSlot(capturedTemplate); });
                }

                TextMeshProUGUI text = itemGo.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null)
                {
                    Vector3 size = template.sizeCentimeters;
                    text.text = size.x + "x" + size.z + "x" + size.y;
                }
            }
        }
        // Case : No Templates
        // Default Template : 100x100x100
        else
        {
            FurnitureSizeTemplate defaultTemplate = new FurnitureSizeTemplate();
            defaultTemplate.sizeCentimeters = new Vector3(100f, 100f, 100f);

            GameObject itemGo = Instantiate(sizeItemPrefab, sizeListRoot);

            Button button = itemGo.GetComponent<Button>();
            FurnitureSizeTemplate capturedTemplate = defaultTemplate;
            if (button != null)
            {
                button.onClick.AddListener(delegate { OnClickSizeSlot(capturedTemplate); });
            }

            TextMeshProUGUI text = itemGo.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = "100x100x100";
            }
        }
    }

    // SizeSlot OnClick Method
    private void OnClickSizeSlot(FurnitureSizeTemplate template)
    {
        currentTemplate = template;
        hasCurrentTemplate = true;
        
        // Set Name & Sprite
        if (currentDefinition != null)
        {
            if (detailNameText != null)
            {   
                detailNameText.text = "이름 : " + currentDefinition.name;
            }

            if (detailFurnitureImage != null)
            {
                detailFurnitureImage.sprite = currentDefinition.sprite;
            }
        }
            
        // Apply Size to Detail Panel
        Vector3 size = template.sizeCentimeters;
        widthInput.text = size.x.ToString();
        depthInput.text = size.z.ToString();
        heightInput.text = size.y.ToString();

        // Reset Options
        wallFrontToggle.isOn = false;    
        wallBackToggle.isOn = true; // Default Option : WallBack
        wallLeftToggle.isOn = false;
        wallRightToggle.isOn = false;
        
        clearanceFrontInput.text = "0";
        clearanceBackInput.text = "0";
        clearanceLeftInput.text = "0";
        clearanceRightInput.text = "0";

        primaryToggle.isOn = false;
        
        privacyToggle.isOn = false;
        
        privacyFrontToggle.isOn = false;
        privacyBackToggle.isOn = false;
        privacyLeftToggle.isOn = false;
        privacyRightToggle.isOn = false;

        SetDetailPanel(true);
    }
    #endregion
    
    #region DetailPanelMethod

    public void OnClickCloseDetailPanel()
    {
        SetDetailPanel(false);
    }
    
    public void OnClickAddFurnitureButton()
    {
        if (furnitureManager == null)
        {
            Debug.LogError("FurnitureManager is not assigned.");
            return;
        }

        if (hasCurrentDefinition == false || currentDefinition == null)
        {
            Debug.LogWarning("No FurnitureDefinition selected.");
            return;
        }

        // Size Parsing
        float width;
        float depth;
        float height;

        bool okWidth = float.TryParse(widthInput.text, out width);
        bool okDepth = float.TryParse(depthInput.text, out depth);
        bool okHeight = float.TryParse(heightInput.text, out height);

        if (okWidth == false || okDepth == false || okHeight == false)
        {
            Debug.LogWarning("Size parse failed.");
            return;
        }
        Vector3 sizeCentimeters = new Vector3(width, height, depth);
        
        // WallPlacement Parsing
        WallPlacementDirection wallDir = new WallPlacementDirection();
        wallDir.front = wallFrontToggle.isOn;
        wallDir.back  = wallBackToggle.isOn;
        wallDir.left  = wallLeftToggle.isOn;
        wallDir.right = wallRightToggle.isOn;

        
        // clearance Parsing
        FunctionalClearanceCm clearance = new FunctionalClearanceCm();
        clearance.front = ParseIntOrZero(clearanceFrontInput.text);
        clearance.back = ParseIntOrZero(clearanceBackInput.text);
        clearance.left = ParseIntOrZero(clearanceLeftInput.text);
        clearance.right = ParseIntOrZero(clearanceRightInput.text);
        
        // Primary
        bool isPrimaryFurniture = primaryToggle.isOn;
        
        // Privacy & directions
        bool isPrivacyFurniture = privacyToggle.isOn;
        
        PrivacyDirection privacyDir = new PrivacyDirection();
        privacyDir.front = privacyFrontToggle.isOn;
        privacyDir.back  = privacyBackToggle.isOn;
        privacyDir.left  = privacyLeftToggle.isOn;
        privacyDir.right = privacyRightToggle.isOn;

        // Add Furniture
        furnitureManager.AddItemToRoomInventory(
            roomManager.currentRoomID,
            currentDefinition.id,
            sizeCentimeters,
            wallDir,
            clearance,
            isPrimaryFurniture,
            isPrivacyFurniture,
            privacyDir
        );

        // Refresh
        RefreshFurnitureList();

        // Go To Main
        ShowMain();
    }
    
    #endregion
    
    #region MainPanelMethod
    
    // Prev Button
    public void OnClickPrevRoom()
    {
        int roomCnt = roomManager.GetRoomCount();
        if (roomCnt==0)
        {
            return;
        }

        int curRoomID = roomManager.currentRoomID;
        
        curRoomID = curRoomID - 1;
        if (curRoomID < 0)
        {
            curRoomID = roomCnt - 1;
        }

        roomManager.currentRoomID = curRoomID;
        
        // Refresh List & Room
        RefreshFurnitureList();
        UpdateRoomView();
    }

    // Next Button
    public void OnClickNextRoom()
    {
        int roomCnt = roomManager.GetRoomCount();
        if (roomCnt == 0)
        {
            return;
        }
        
        int curRoomID = roomManager.currentRoomID;

        curRoomID = curRoomID + 1;
        if (curRoomID >= roomManager.GetRoomCount())
        {
            curRoomID = 0;
        }
        
        roomManager.currentRoomID = curRoomID;
        
        // Refresh List & Room
        RefreshFurnitureList();
        UpdateRoomView();
    }
    
    // Save Button
    public void OnClickSaveButton()
    {
        // Show All Rooms
        roomManager.SetAllRoomsActive(true);
        
        // Hide All Grids
        gridBuilder.HideAllRoomGrids();
        
        // Save Datas
        
        // Spawn Player
        
        // Show Simulation UI
        DeactiveAllPanels();
        SetSimulationPanel(true);
    }
    
    // LeftClick -> Place Mode
    public void OnLeftClickFurnitureSlot(string instanceId)
    {
        // 0. No Furniture -> return
        FurnitureItemData item = furnitureManager.GetItemByInstanceId(instanceId);
        if (item == null)
        {
            Debug.LogWarning("OnLeftClickFurnitureSlot: item not found: " + instanceId);
            return;
        }
        
        // 1. if isPlaced -> RePosition
        if (item.isPlaced)
        {
            // 이미 배치된 가구 → 원래 위치에서 재배치 모드
            placementController.BeginRepositionExisting(item);
        }
        // 2. else -> Begin Placement
        else
        {
            placementController.BeginPlacement(item, roomManager.currentRoomID);
        }
    }
    
    // RightClick -> Show Detail Panel (RO)
    public void OnRightClickFurnitureSlot(string instanceId)
    {
        FurnitureItemData item = furnitureManager.GetItemByInstanceId(instanceId);
        if (item == null)
        {
            return;
        }

        currentReadOnlyInstanceId = instanceId;
        ApplyReadOnlyDetail(item);
        SetDetailPanelReadOnly(true);
    }

    public void OnClickCloseDetailPanelReadOnly()
    {
        SetDetailPanelReadOnly(false);
    }
    
    public void OnClickDeleteFurnitureButton()
    {
        if (string.IsNullOrEmpty(currentReadOnlyInstanceId))
        {
            return;
        }

        if (furnitureManager == null)
        {
            return;
        }

        // Delete ( inventory + roomMap + object )
        furnitureManager.DeleteFurnitureItem(currentReadOnlyInstanceId);

        // Refresh
        RefreshFurnitureList();

        // Close ReadOnly Panel
        SetDetailPanelReadOnly(false);

        // Reset current ID
        currentReadOnlyInstanceId = null;
    }

    // OnClick Room Name -> Rename room
    public void OnClickRoomNameButton()
    {
        // Rename Room
        Debug.Log("Set Room Name");
    }

    public void OnClickCategoryButton()
    {
        ShowCategory();
    }

    public void OnClickAutoPlaceButton()
    {
        SetAutoPlacePanel(true);
    }

    public void OnClickCloseAutoPlaceButton()
    {
        SetAutoPlacePanel(false);
    }

    public void OnClickStartAutoPlaceButton()
    {
        Debug.Log("Start Auto Place");
        // *** 추가 예정 *** 
    }
    
    // Refresh Furniture List
    private void RefreshFurnitureList()
    {
        if (myFurnitureListRoot == null)
        {
            return;
        }

        ClearChildren(myFurnitureListRoot);

        if (furnitureManager == null)
        {
            return;
        }
        
        slotMap.Clear();
        
        // Get Room Item Lists
        List<FurnitureItemData> list = furnitureManager.GetItemsInRoom(roomManager.currentRoomID);
        for (int i = 0; i < list.Count; i++)
        {
            FurnitureItemData item = list[i];

            GameObject itemGo = Instantiate(furnitureSlotPrefab, myFurnitureListRoot);
            
            // Setup (Assign PlaceSceneUI & instanceID)
            MyFurnitureSlot slot = itemGo.GetComponent<MyFurnitureSlot>();
            if (slot != null)
            {
                slot.Setup(this, item.instanceId);
                
                if (!slotMap.ContainsKey(item.instanceId))
                {
                    slotMap.Add(item.instanceId, slot);
                }
            }

            // Image / Text
            Image image = itemGo.GetComponentInChildren<Image>();
            if (image != null && furnitureManager.GetDB() != null)
            {
                FurnitureDefinition def = furnitureManager.GetDB().GetById(item.furnitureId);
                if (def != null && def.sprite != null)
                {
                    slot.img = image;
                    image.sprite = def.sprite;
                    
                    // Set Color
                    slot.SetColor(item.isPlaced);
                }
            }
            
            TextMeshProUGUI text = itemGo.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = furnitureManager.GetDB().GetById(item.furnitureId).name;
            }
        }
    }

    private void ApplyReadOnlyDetail(FurnitureItemData item)
    {
        string displayName = "이름 : " + item.furnitureId + "\nID : " + item.instanceId;

        if (furnitureManager.GetDB() != null)
        {
            FurnitureDefinition def = furnitureManager.GetDB().GetById(item.furnitureId);
            if (def != null)
            {
                displayName = "이름 : " + def.name + "\nID : " + item.instanceId;
                if (def.sprite != null)
                {
                    roFurnitureImage.sprite = def.sprite;
                }
            }
        }

        roNameText.text = displayName;
        
        roWidthText.text = item.sizeCentimeters.x.ToString();
        roDepthText.text = item.sizeCentimeters.z.ToString();
        roHeightText.text = item.sizeCentimeters.y.ToString();
        
        roWallFrontToggle.isOn = item.wallDir.front;
        roWallBackToggle.isOn  = item.wallDir.back;
        roWallLeftToggle.isOn  = item.wallDir.left;
        roWallRightToggle.isOn = item.wallDir.right;
        
        roClearanceFrontText.text = item.clearance.front.ToString();
        roClearanceBackText.text  = item.clearance.back.ToString();
        roClearanceLeftText.text  = item.clearance.left.ToString();
        roClearanceRightText.text = item.clearance.right.ToString();
        
        roPrimaryToggle.isOn = item.isPrimaryFurniture;
        roPrivacyToggle.isOn = item.isPrivacyFurniture;
        
        roPrivacyFrontToggle.isOn = item.privacyDir.front;
        roPrivacyBackToggle.isOn  = item.privacyDir.back;
        roPrivacyLeftToggle.isOn  = item.privacyDir.left;
        roPrivacyRightToggle.isOn = item.privacyDir.right;
    }

    // room Name / room Index / RoomManager Update
    private void UpdateRoomView()
    {
        int roomCnt = roomManager.GetRoomCount();
        if (roomCnt == 0)
        {
            return;
        }
        
        int curRoomID = roomManager.currentRoomID;
        RoomDef curRoom = roomManager.GetRoomDefById(curRoomID);

        if (curRoom == null)
        {
            Debug.LogWarning(curRoomID+" Room does not exist.");
            return;
        }

        // RoomName
        if (roomNameText != null)
        {
            // RoomDef.name 이 null -> roomID 표시
            if (!string.IsNullOrEmpty(curRoom.name))
            {
                roomNameText.text = curRoom.name;
            }
            else
            {
                roomNameText.text = "Room " + curRoom.roomID.ToString();
            }
        }

        // "현재 / 전체" 표시
        if (indexText != null)
        {
            int humanIndex = roomManager.currentRoomID + 1;
            int total = roomCnt;
            indexText.text = humanIndex.ToString() + " / " + total.ToString();
        }

        // 방 활성화 반영
        if (roomManager != null)
        {
            roomManager.SetActiveOnly(curRoom.roomID);
        }
    }
    
    // Place Mode -> Deactive All Buttons
    public void SetPlacementMode(bool isPlacing)
    {
        isPlacementMode = isPlacing;

        // isPlacing -> interact false
        bool interact = !isPlacing;

        // 상단 네비게이션 버튼 잠금
        if (returnButton != null) returnButton.interactable = interact;
        if (roomNameButton != null) roomNameButton.interactable = interact;
        if (prevButton != null) prevButton.interactable = interact;
        if (nextButton != null) nextButton.interactable = interact;
        if (saveButton != null) saveButton.interactable = interact;
        if (autoPlaceButton != null)  autoPlaceButton.interactable  = interact;
        if (categoryButton != null) categoryButton.interactable = interact;

        // 하단 가구 슬롯 버튼 잠금
        foreach (KeyValuePair<string, MyFurnitureSlot> kvp in slotMap)
        {
            MyFurnitureSlot slot = kvp.Value;
            if (slot != null)
            {
                slot.SetInteractable(interact);
            }
        }
    }

    public bool IsPlacementMode()
    {
        return isPlacementMode;
    }
    
    
    #endregion
    
    #region Simulation Panel

    public void OnClickReturnToPlaceButton()
    {
        // Active index 0 room
        roomManager.currentRoomID = 0;
        UpdateRoomView();
        
        // Show Main Panel
        ShowMain();
    }
    
    #endregion

    #region Others
    
    // Loading
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
    public void OnClickReturnButton()
    {
        
        SceneManager.LoadScene("02_FloorPlanScene");
    }
    
    
    private void ClearChildren(Transform root)
    {
        int count = root.childCount;
        for (int i = count - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            Destroy(child.gameObject);
        }
    }
    
    private int ParseIntOrZero(string text)
    {
        int value;
        bool ok = int.TryParse(text, out value);
        if (ok)
        {
            return value;
        }
        return 0;
    }
    
    #endregion
}
