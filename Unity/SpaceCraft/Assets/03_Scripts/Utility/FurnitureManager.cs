using System.Collections.Generic;
using UnityEngine;

public class FurnitureManager : MonoBehaviour
{
    [Header("Database")]
    public FurnitureDatabase furnitureDatabase;

    [Header("Inventory Data")] 
    // JSON Load/Save
    public List<FurnitureItemData> inventory = new List<FurnitureItemData>();

    // 1) Data : instanceId -> FurnitureItemData
    private Dictionary<string, FurnitureItemData> byInstanceId =
        new Dictionary<string, FurnitureItemData>();

    // 2) RoomToInstances : roomID -> instanceIDs
    private Dictionary<int, HashSet<string>> roomToInstances =
        new Dictionary<int, HashSet<string>>();

    // 3) placedRuntimeMap : instanceID -> Furniture (Scene Object)
    private Dictionary<string, Furniture> placedRuntimeMap =
        new Dictionary<string, Furniture>();

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
        roomToInstances.Clear();
        placedRuntimeMap.Clear(); // 런타임 오브젝트 맵은 다시 배치할 때만 채움

        for (int i = 0; i < inventory.Count; i++)
        {
            FurnitureItemData item = inventory[i];
            if (item == null)
            {
                continue;
            }
            RegisterItemToMaps(item);
        }
        
        // JSON 상에서 실제로 배치된 가구가 있다면 재배치 해야하긴 함
    }
    
    // Register To Map (room->instanceId)
    private void RegisterItemToMaps(FurnitureItemData item)
    {
        if (item == null)
        {
            return;
        }

        byInstanceId[item.instanceId] = item;

        int roomKey = item.roomID; // 항상 실제 roomID

        HashSet<string> set;
        bool found = roomToInstances.TryGetValue(roomKey, out set);
        if (found == false)
        {
            set = new HashSet<string>();
            roomToInstances.Add(roomKey, set);
        }

        set.Add(item.instanceId);
    }

    // InstanceID Generator
    // ex) Desk#0001
    private string GenerateInstanceId(string furnitureId)
    {
        string suffix = nextInstanceIndex.ToString("D4");
        string instanceId = furnitureId + "#" + suffix;
        nextInstanceIndex = nextInstanceIndex + 1;
        return instanceId;
    }
    
    // Add Item To Inventory
    public FurnitureItemData AddItemToRoomInventory(
        int roomID,
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

        item.isPlaced = false;
        item.roomID = roomID;
        item.gridCell = new Vector2Int(0, 0);
        item.rotation = 0;

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

    // 해당 방의 모든 가구 (배치 + 미배치)
    public List<FurnitureItemData> GetItemsInRoom(int roomID)
    {
        HashSet<string> set;
        bool found = roomToInstances.TryGetValue(roomID, out set);
        if (found == false)
        {
            return new List<FurnitureItemData>();
        }

        List<FurnitureItemData> result = new List<FurnitureItemData>();

        foreach (string id in set)
        {
            FurnitureItemData item;
            if (byInstanceId.TryGetValue(id, out item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    // ***Function For Auto Place***
    // Get "Unplaced Items" In Room
    public List<FurnitureItemData> GetUnplacedItemsInRoom(int roomID)
    {
        List<FurnitureItemData> all = GetItemsInRoom(roomID);
        List<FurnitureItemData> result = new List<FurnitureItemData>();

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].isPlaced == false)
            {
                result.Add(all[i]);
            }
        }

        return result;
    }

    // Place Item To Room
    // worldPos -> Grid Snap, rotationDeg -> 0/90/180/270
    public Furniture PlaceItem(
        string instanceId,
        int roomID,
        Vector2Int gridCell,
        Vector3 worldPos,
        int rotationDeg
    )
    {
        int index = FindInventoryIndex(instanceId);
        if (index < 0)
        {
            Debug.LogError("Inventory item not found: " + instanceId);
            return null;
        }

        FurnitureItemData item = inventory[index];

        if (item == null)
        {
            Debug.LogError("Inventory item is null: " + instanceId);
            return null;
        }

        // different roomID -> Error (SafeGuard)
        if (item.roomID != roomID)
        {
            Debug.LogWarning("PlaceItem: item roomID mismatch. item:" +
                             item.roomID + " arg:" + roomID);
            item.roomID = roomID;
        }
    
        // No Furniture in DB
        FurnitureDefinition def = furnitureDatabase.GetById(item.furnitureId);
        if (def == null)
        {
            Debug.LogError("FurnitureDefinition not found: " + item.furnitureId);
            return null;
        }

        Quaternion rot = Quaternion.Euler(0f, (float)rotationDeg, 0f);
        GameObject go = Instantiate(def.prefab, worldPos, rot);
        Furniture furniture = go.GetComponent<Furniture>();

        // Renew Data
        item.isPlaced = true;
        item.roomID = roomID;
        item.gridCell = gridCell;
        item.rotation = rotationDeg;

        inventory[index] = item;
        byInstanceId[item.instanceId] = item;

        if (furniture != null)
        {
            ApplyInventoryToFurniture(item, furniture);
        }

        placedRuntimeMap[instanceId] = furniture;

        return furniture;
    }
    
    // Unplace Item
    public void UnplaceItem(string instanceId)
    {
        FurnitureItemData item = GetItemByInstanceId(instanceId);
        if (item == null)
        {
            return;
        }

        // Destroy Furniture Object
        Furniture placed;
        bool hasPlaced = placedRuntimeMap.TryGetValue(instanceId, out placed);
        if (hasPlaced && placed != null)
        {
            Destroy(placed.gameObject);
        }
        placedRuntimeMap.Remove(instanceId);

        // renew Data (keep roomID)
        item.isPlaced = false;
        item.gridCell = new Vector2Int(0, 0);
        item.rotation = 0;

        // Apply to inventory list
        int index = FindInventoryIndex(instanceId);
        if (index >= 0)
        {
            inventory[index] = item;
        }
        byInstanceId[instanceId] = item;
    }

    // Delete Furniture
    public void DeleteFurnitureItem(string instanceId)
    {
        FurnitureItemData item = GetItemByInstanceId(instanceId);
        if (item == null)
        {
            return;
        }

        // 1. Unplace Object
        UnplaceItem(instanceId);

        // 2. remove at roomToInstances
        HashSet<string> set;
        bool found = roomToInstances.TryGetValue(item.roomID, out set);
        if (found)
        {
            set.Remove(instanceId);
            if (set.Count == 0)
            {
                roomToInstances.Remove(item.roomID);
            }
        }

        // 3. remove at byInstanceId
        byInstanceId.Remove(instanceId);

        // 4. remove at inventory
        inventory.Remove(item);
    }

    private int FindInventoryIndex(string instanceId)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i] != null && inventory[i].instanceId == instanceId)
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

        // Set Size (width=X, depth=Z, height=Y)
        f.SetSize(
            f.sizeCentimeters.x,
            f.sizeCentimeters.z,
            f.sizeCentimeters.y,
            true
        );
    }
}
