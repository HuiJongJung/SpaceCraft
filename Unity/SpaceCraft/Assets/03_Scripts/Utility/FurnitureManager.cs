using System.Collections.Generic;
using UnityEngine;

public class FurnitureManager : MonoBehaviour
{
    [Header("Database")]
    public FurnitureDatabase furnitureDatabase;

    [Header("Inventory Data")] // To Load / Save JSON
    public List<FurnitureItemData> inventory = new List<FurnitureItemData>();
    
    // instanceId -> Item
    private Dictionary<string, FurnitureItemData> byInstanceId =
        new Dictionary<string, FurnitureItemData>();
    
    // roomID -> (instanceId -> Item)
    private Dictionary<int, Dictionary<string, FurnitureItemData>> byRoomId =
        new Dictionary<int, Dictionary<string, FurnitureItemData>>();


    // runtime: instanceId -> 씬 상의 Furniture 컴포넌트
    private Dictionary<string, Furniture> placedRuntimeMap = new Dictionary<string, Furniture>();

    [SerializeField] private int nextInstanceIndex = 1;

    private void Awake()
    {
        if (furnitureDatabase != null)
        {
            furnitureDatabase.Initialize();
        }
        else
        {
            Debug.LogError("FurnitureDatabase is not assigned on FurnitureManager.", this);
        }
        
        RebuildMapsFromInventory();
    }
    
    public void RebuildMapsFromInventory()
    {
        byInstanceId.Clear();
        byRoomId.Clear();

        for (int i = 0; i < inventory.Count; i++)
        {
            FurnitureItemData item = inventory[i];
            RegisterItemToMaps(item);
        }
    }
    
    private void RegisterItemToMaps(FurnitureItemData item)
    {
        byInstanceId[item.instanceId] = item;

        int roomKey = item.isPlaced ? item.roomID : -1;

        Dictionary<string, FurnitureItemData> roomDict;
        bool found = byRoomId.TryGetValue(roomKey, out roomDict);
        if (found == false)
        {
            roomDict = new Dictionary<string, FurnitureItemData>();
            byRoomId.Add(roomKey, roomDict);
        }

        roomDict[item.instanceId] = item;
    }

    // 인스턴스 ID 생성 규칙 예시: chair_office_basic#0001
    private string GenerateInstanceId(string furnitureId)
    {
        string suffix = nextInstanceIndex.ToString("D4");
        string instanceId = furnitureId + "#" + suffix;
        nextInstanceIndex = nextInstanceIndex + 1;
        return instanceId;
    }
    
    // Add Item From Detail (To My Furniture List)
    public FurnitureItemData AddItemFromDetail(
        string furnitureId,
        Vector3 sizeCentimeters,
        WallPlacementDirection wallDir,
        FunctionalClearanceCm clearance,
        bool isPrimaryFurniture,
        bool isPrivacyFurniture,
        PrivacyDirection privacyDir
    )
    {
        FurnitureItemData item = new FurnitureItemData();

        item.furnitureId = furnitureId;
        item.instanceId = GenerateInstanceId(furnitureId);
        
        // Reset Place Info
        item.isPlaced = false;
        item.roomID = -1;
        item.gridCell = new Vector2Int(0, 0);
        item.rotation = 0;
        
        // Assign Size & Option
        item.sizeCentimeters = sizeCentimeters;
        item.wallDir = wallDir;
        item.clearance = clearance;
        item.isPrimaryFurniture = isPrimaryFurniture;
        item.isPrivacyFurniture = isPrivacyFurniture;
        item.privacyDir = privacyDir;

        inventory.Add(item);
        RegisterItemToMaps(item);
        
        return item;
    }

    // Get By InstanceID
    public FurnitureItemData GetItemByInstanceId(string instanceId)
    {
        FurnitureItemData item;
        bool found = byInstanceId.TryGetValue(instanceId, out item);
        if (found == false)
        {
            return null;
        }
        return item;
    }
    
    public List<FurnitureItemData> GetItemsInRoom(int roomID)
    {
        Dictionary<string, FurnitureItemData> roomDict;
        bool found = byRoomId.TryGetValue(roomID, out roomDict);
        if (found == false)
        {
            return new List<FurnitureItemData>();
        }

        List<FurnitureItemData> result = new List<FurnitureItemData>();

        foreach (KeyValuePair<string, FurnitureItemData> pair in roomDict)
        {
            result.Add(pair.Value);
        }

        return result;
    }
    
    
    // Add Test Function
    public void AddInventoryItems(string furnitureId, Vector3 size, int count)
    {
        // DB check
        if (furnitureDatabase == null)
        {
            Debug.LogError("FurnitureDatabase is null.");
            return;
        }

        // def check
        FurnitureDefinition def = furnitureDatabase.GetById(furnitureId);
        if (def == null)
        {
            Debug.LogError("FurnitureDefinition not found: " + furnitureId);
            return;
        }
        
        for (int i = 0; i < count; i++)
        {
            FurnitureItemData item = new FurnitureItemData();
            item.instanceId = GenerateInstanceId(furnitureId);
            item.furnitureId = furnitureId;
            item.isPlaced = false;

            item.roomID = -1;
            item.gridCell = new Vector2Int(0, 0);
            item.rotation = 0;
            item.sizeCentimeters = size;

            item.isPrimaryFurniture = false;
            item.clearance = new FunctionalClearanceCm();
            item.isPrivacyFurniture = false;
            item.privacyDir = new PrivacyDirection();

            inventory.Add(item);
            RegisterItemToMaps(item);
        }

        // 여기서 인벤토리 UI 새로고침 호출해주면 됨
    }
    
    // 인벤토리 아이템 하나를 실제 씬에 배치하는 함수
    // worldPos는 그리드 스냅 결과, rotationDeg는 0/90/180/270
    public Furniture PlaceFromInventory(string instanceId, Vector3 worldPos, int rotationDeg, int roomID, Vector2Int gridCell)
    {
        int index = FindInventoryIndex(instanceId);
        if (index < 0)
        {
            Debug.LogError("Inventory item not found: " + instanceId);
            return null;
        }

        FurnitureItemData item = inventory[index];

        FurnitureDefinition def = furnitureDatabase.GetById(item.furnitureId);
        if (def == null)
        {
            Debug.LogError("FurnitureDefinition not found: " + item.furnitureId);
            return null;
        }

        Quaternion rot = Quaternion.Euler(0f, (float)rotationDeg, 0f);
        GameObject go = GameObject.Instantiate(def.prefab, worldPos, rot);
        Furniture furniture = go.GetComponent<Furniture>();

        // 기존 방에서 제거
        int oldRoomKey = item.isPlaced ? item.roomID : -1;

        Dictionary<string, FurnitureItemData> oldDict;
        bool foundOld = byRoomId.TryGetValue(oldRoomKey, out oldDict);
        if (foundOld)
        {
            oldDict.Remove(instanceId);
        }
        
        // 인벤토리 정보 갱신
        item.isPlaced = true;
        item.roomID = roomID;
        item.gridCell = gridCell;
        item.rotation = rotationDeg;
        inventory[index] = item;
        
        // 새 방에 등록
        Dictionary<string, FurnitureItemData> newDict;
        bool foundNew = byRoomId.TryGetValue(roomID, out newDict);
        if (foundNew == false)
        {
            newDict = new Dictionary<string, FurnitureItemData>();
            byRoomId.Add(roomID, newDict);
        }
        newDict[instanceId] = item;

        if (furniture != null)
        {
            ApplyInventoryToFurniture(item, furniture);
        }

        placedRuntimeMap[instanceId] = furniture;

        return furniture;
    }

    private int FindInventoryIndex(string instanceId)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].instanceId == instanceId)
            {
                return i;
            }
        }
        return -1;
    }

    // Inventory -> Furniture
    private void ApplyInventoryToFurniture(FurnitureItemData item, Furniture f)
    {
        f.instanceId = item.instanceId;
        f.furnitureId = item.furnitureId;
        f.roomID = item.roomID;
        f.gridCell = item.gridCell;
        f.rotation = item.rotation;
        f.sizeCentimeters = item.sizeCentimeters;
        f.isPrimaryFurniture = item.isPrimaryFurniture;
        f.clearance = item.clearance;
        f.isPrivacyFurniture = item.isPrivacyFurniture;
        // Set Size
        f.SetSize(f.sizeCentimeters.x, 
            f.sizeCentimeters.z, f.sizeCentimeters.y, true);
    }

    // 가구 삭제
    public void DeleteFurniture(string instanceId)
    {
        FurnitureItemData item = GetItemByInstanceId(instanceId);
        if (item == null)
        {
            return;
        }

        // roomMap에서 제거
        int roomKey = item.isPlaced ? item.roomID : -1;
        Dictionary<string, FurnitureItemData> roomDict;
        bool found = byRoomId.TryGetValue(roomKey, out roomDict);
        if (found)
        {
            roomDict.Remove(instanceId);
        }

        // instanceMap에서 제거
        byInstanceId.Remove(instanceId);

        // inventory 리스트에서 제거
        inventory.Remove(item);
        
        // 씬 오브젝트 제거
        Furniture placed;
        bool hasPlaced = placedRuntimeMap.TryGetValue(instanceId, out placed);
        if (hasPlaced && placed != null)
        {
            Destroy(placed.gameObject);
        }
        placedRuntimeMap.Remove(instanceId);
    }

    // 배치되지 않은 가구 반환
    public List<FurnitureItemData> GetUnplacedItems()
    {
        return GetItemsInRoom(-1);
    }
}
