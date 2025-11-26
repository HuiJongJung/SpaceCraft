using System.Collections.Generic;
using UnityEngine;

public class FurnitureManager : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private FurnitureDatabase furnitureDatabase;

    [Header("Data (Saved / Loaded)")]
    [SerializeField] private List<FurnitureItemData> inventory =
        new List<FurnitureItemData>();   // JSON, Inspector 에 보이는 진짜 데이터

    // instanceId -> Data
    private Dictionary<string, FurnitureItemData> byInstanceId =
        new Dictionary<string, FurnitureItemData>();

    // instanceId -> Runtime Furniture (씬 오브젝트)
    private Dictionary<string, Furniture> placedRuntimeMap =
        new Dictionary<string, Furniture>();

    [Header("Refs")]
    [SerializeField] private RoomManager roomManager;

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

        if (roomManager == null)
        {
            roomManager = FindObjectOfType<RoomManager>();
        }

        RebuildMapsFromInventory();
    }

    // DB Getter
    public FurnitureDatabase GetDB()
    {
        return furnitureDatabase;
    }

    // Rebuild Maps From Inventory
    // Use When "Load JSON"
    public void RebuildMapsFromInventory()
    {
        byInstanceId.Clear();
        placedRuntimeMap.Clear();

        for (int i = 0; i < inventory.Count; i++)
        {
            FurnitureItemData item = inventory[i];

            // No instanceId -> Generate
            if (string.IsNullOrEmpty(item.instanceId))
            {
                item.instanceId = GenerateInstanceId(item.furnitureId);
                inventory[i] = item;
            }

            if (string.IsNullOrEmpty(item.instanceId) == false)
            {
                if (byInstanceId.ContainsKey(item.instanceId))
                {
                    Debug.LogWarning("Duplicate instanceId in inventory: " + item.instanceId);
                }
                else
                {
                    byInstanceId[item.instanceId] = item;
                }
            }
        }
    }

    // Generate Rule : Desk#0001
    private string GenerateInstanceId(string furnitureId)
    {
        string suffix = nextInstanceIndex.ToString("D4");
        string instanceId = furnitureId + "#" + suffix;
        nextInstanceIndex = nextInstanceIndex + 1;
        return instanceId;
    }

    // Add Furniture to "Room" (UnPlaced)
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
        byInstanceId[item.instanceId] = item;

        return item;
    }
    
    
    // Find FurnitureItemData
    // Using InstanceId
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

    // Get All Furnitures in "Room"
    public List<FurnitureItemData> GetItemsInRoom(int roomID)
    {
        List<FurnitureItemData> result = new List<FurnitureItemData>();

        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].roomID == roomID)
            {
                result.Add(inventory[i]);
            }
        }
        return result;
    }

    // Get All "Unplaced" Furnitures in Room
    // For "Auto Place"
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
    
    // Place Furniture
    public Furniture PlaceItem(
        string instanceId,
        int roomID,
        Vector2Int gridCell,
        Vector3 worldPos,
        int rotationDeg
    )
    {
        // Find index in inventory
        int index = FindInventoryIndex(instanceId);
        if (index < 0)
        {
            Debug.LogError("Inventory item not found: " + instanceId);
            return null;
        }

        FurnitureItemData item = inventory[index];

        // Safety
        if (item.roomID != roomID)
        {
            Debug.LogWarning("PlaceItem: item roomID mismatch. item:" +
                             item.roomID + " arg:" + roomID);
            item.roomID = roomID;
        }
        
        // Get FurnitureDefinition By Id
        FurnitureDefinition def = furnitureDatabase.GetById(item.furnitureId);
        if (def == null)
        {
            Debug.LogError("FurnitureDefinition not found: " + item.furnitureId);
            return null;
        }
        
        // Instantiate Prefab
        Quaternion rot = Quaternion.Euler(0f, rotationDeg, 0f);
        GameObject go = Instantiate(def.prefab, worldPos, rot);
        Furniture furniture = go.GetComponent<Furniture>();

        // Renew Data
        item.isPlaced = true;
        item.roomID = roomID;
        item.gridCell = gridCell;
        item.rotation = rotationDeg;

        inventory[index] = item;
        byInstanceId[item.instanceId] = item;

        // Apply Data to Furniture 
        if (furniture != null)
        {
            ApplyInventoryToFurniture(item, furniture);
        }

        // Create RoomObject & Register
        RoomObject ro = go.GetComponent<RoomObject>();
        if (ro == null)
        {
            ro = go.AddComponent<RoomObject>();
            ro.type = RoomObjectType.Furniture;
            ro.roomIDs.Add(roomID);
            roomManager.Register(ro, roomID);
            
            // Register to RuntimeMap
            placedRuntimeMap[instanceId] = furniture;
        }

        return furniture;
    }

    // UnPlace
    public void UnplaceItem(string instanceId)
    {
        FurnitureItemData item = GetItemByInstanceId(instanceId);
        if (item == null)
        {
            return;
        }

        // Destroy GameObject in Scene
        Furniture placed;
        bool hasPlaced = placedRuntimeMap.TryGetValue(instanceId, out placed);
        if (hasPlaced && placed != null)
        {
            // Remove At roomManager's furnitures list
            if (roomManager != null)
            {
                roomManager.RemoveFurnitureFromRoom(placed.gameObject, item.roomID);
            }

            // 🔹 그 다음 실제 GameObject 제거
            GameObject go = placed.gameObject;
            if (go != null)
            {
                GameObject.Destroy(go);
            }
        }
        placedRuntimeMap.Remove(instanceId);

        // Renew Data
        item.isPlaced = false;
        item.gridCell = new Vector2Int(0, 0);
        item.rotation = 0;

        // Apply in Inventory Lists
        int index = FindInventoryIndex(instanceId);
        if (index >= 0)
        {
            inventory[index] = item;
        }
        byInstanceId[instanceId] = item;
    }

    // Delete
    public void DeleteFurnitureItem(string instanceId)
    {
        FurnitureItemData item = GetItemByInstanceId(instanceId);
        if (item == null)
        {
            return;
        }

        // Unplace
        UnplaceItem(instanceId);

        // Remove at byInstanceId
        byInstanceId.Remove(instanceId);

        // Remove at inventory
        inventory.Remove(item);
        
        //
    }

    // Util

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
        f.privacyDir = item.privacyDir;

        // Set Size (width=X, depth=Z, height=Y)
        f.SetSize(
            f.sizeCentimeters.x,
            f.sizeCentimeters.z,
            f.sizeCentimeters.y,
            true
        );
    }
    
    // Get Inventory
    public List<FurnitureItemData> GetAllItems()
    {
        return inventory;
    }
}
