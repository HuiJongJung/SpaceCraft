using System.Collections.Generic;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SpaceData spaceData;
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;
    [SerializeField] private PlaceCameraController cameraController;
    private SpaceLayout layout;

    
    // roomID -> runtime data
    // Index "-1" Room : List of Objects not belong to Room
    private Dictionary<int, RoomRuntimeData> roomsById =
        new Dictionary<int, RoomRuntimeData>();
    
    private Dictionary<int, Vector3> roomCenterWorldById =
        new Dictionary<int, Vector3>();

    // currentRoomID
    public int currentRoomID;

    public int GetRoomCount()
    {
        int count = 0;
        foreach (KeyValuePair<int, RoomRuntimeData> pair in roomsById)
        {
            if (pair.Key >= 0)
            {
                count++;
            }
        }
        return count;
    }

    // 디버그용 인스펙터 확인
    [SerializeField] private List<RoomRuntimeData> debugRooms =
        new List<RoomRuntimeData>();

    private void Awake()
    {
        if (spaceData == null)
        {
            spaceData = SpaceData.Instance;
        }

        if (cameraController == null)
        {
            cameraController = FindObjectOfType<PlaceCameraController>();
        }

        if (spaceData != null)
        {
            layout = spaceData._layout;
        }
        else
        {
            Debug.LogWarning("[RoomManager] SpaceData is null.");
        }
    }

    private void Start()
    {
        if (gridBuilder == null)
        {
            gridBuilder = FindObjectOfType<RoomPlacementGridBuilder>();
        }

        if (spaceData == null)
        {
            spaceData = SpaceData.Instance;
        }

        if (spaceData != null)
        {
            layout = spaceData._layout;
        }

        if (layout == null || layout.rooms == null)
        {
            Debug.LogError("[RoomManager] layout 또는 rooms 가 null 입니다.");
            return;
        }
        
        // Add All Room to roomsById (Default)
        for (int i = 0; i < layout.rooms.Count; i++)
        {
            RoomDef r = layout.rooms[i];
            if (r == null)
            {
                continue;
            }
            GetOrCreateRoomData(layout.rooms[i].roomID);
        }
        
        // Compute Room Centers
        BuildRoomCenters();
    }

    // 특정 방의 중심 좌표
    public Vector3 GetRoomCenterWorld(int roomID)
    {
        Vector3 center;
        bool found = roomCenterWorldById.TryGetValue(roomID, out center);
        if (!found)
        {
            Debug.LogWarning($"[RoomManager] GetRoomCenterWorld: roomID={roomID} 의 center 를 찾지 못했습니다. (default 0,0,0 반환)");
            return Vector3.zero;
        }
        return center;
    }

    // 현재 방의 중심 좌표
    public Vector3 GetCurrentRoomCenterWorld()
    {
        return GetRoomCenterWorld(currentRoomID);
    }

    public void BuildRoomCenters()
{
    roomCenterWorldById.Clear();

    if (layout == null || layout.rooms == null)
    {
        Debug.LogError("[RoomManager] BuildRoomCenters: layout 또는 rooms 가 null 입니다.");
        return;
    }

    for (int i = 0; i < layout.rooms.Count; i++)
    {
        RoomDef room = layout.rooms[i];
        if (room == null)
        {
            Debug.LogWarning($"[RoomManager] rooms[{i}] 가 null 입니다.");
            continue;
        }

        int roomID = room.roomID;

        // floorIDs 중 첫 번째 바닥 기준
        if (room.floorIDs == null || room.floorIDs.Count == 0)
        {
            continue;
        }

        int floorID = room.floorIDs[0];

        FloorDef fd = FindFloorById(floorID);
        if (fd == null)
        {
            Debug.LogWarning($"[RoomManager] roomID={roomID} 의 floorID={floorID} 를 찾지 못했습니다.");
            continue;
        }

        if (fd.vertices == null || fd.vertices.Count == 0)
        {
            Debug.LogWarning($"[RoomManager] floorID={floorID} 의 vertices 가 비어있습니다.");
            continue;
        }

        // 바닥 vertices의 XZ 바운딩 박스 중심 계산
        float minX = fd.vertices[0].x;
        float maxX = fd.vertices[0].x;
        float minZ = fd.vertices[0].z;
        float maxZ = fd.vertices[0].z;

        for (int v = 1; v < fd.vertices.Count; v++)
        {
            Vec3 vert = fd.vertices[v];

            if (vert.x < minX) minX = vert.x;
            if (vert.x > maxX) maxX = vert.x;

            if (vert.z < minZ) minZ = vert.z;
            if (vert.z > maxZ) maxZ = vert.z;
        }

        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        Vector3 center = new Vector3(centerX, 0f, centerZ);
        roomCenterWorldById[roomID] = center;
        
    }
}
    
    private void FocusCameraTopDownOnRoom(int roomID)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }
        Debug.Log("카메라 조정");
        // roomID 기준 방 중심 좌표 가져오기
        Vector3 center = GetRoomCenterWorld(roomID);

        // 위치: 방 중심 + Y=15
        Vector3 pos = center;
        pos.y = 15f;

        cam.transform.position = pos;

        // 회전: 수직 위에서 아래로 보는 각도
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // RoomRuntimeData
    private RoomRuntimeData GetOrCreateRoomData(int roomID)
    {
        RoomRuntimeData roomData;
        bool found = roomsById.TryGetValue(roomID, out roomData);
        if (!found)
        {
            roomData = new RoomRuntimeData(roomID);
            roomsById.Add(roomID, roomData);
            debugRooms.Add(roomData);
        }
        return roomData;
    }
    
    // Called in RoomBuilder & Furniture Place
    // Register RoomObject To "roomID"
    public void Register(RoomObject obj, int roomID)
    {
        if (obj == null)
        {
            return;
        }

        // RoomObject가 자기 속한 방 ID를 알고 있게 해두고 싶다면:
        if (!obj.roomIDs.Contains(roomID))
        {
            obj.roomIDs.Add(roomID);
        }

        RoomRuntimeData roomData = GetOrCreateRoomData(roomID);
        roomData.Add(obj);
    }
    
    // Remove Furniture From Room
    public void RemoveFurnitureFromRoom(GameObject go, int roomID)
    {
        if (go == null)
        {
            return;
        }

        RoomRuntimeData roomData;
        bool found = roomsById.TryGetValue(roomID, out roomData);
        if (found == false || roomData == null)
        {
            return;
        }

        // Remove Furniture
        roomData.furnitures.Remove(go);
    }
    
    // SetActive Only One Room
    public void SetActiveOnly(int roomID)
    {
        // 1) DeActive All Rooms
        foreach (KeyValuePair<int, RoomRuntimeData> pair in roomsById)
        {
            RoomRuntimeData roomData = pair.Value;
            if (roomData != null)
            {
                roomData.SetActive(false);
            }
        }

        // 2) Active Only One Room
        RoomRuntimeData target;
        bool found = roomsById.TryGetValue(roomID, out target);
        if (found && target != null)
        {
            target.SetActive(true);
        }

        // 3) Show Grid
        if (gridBuilder != null)
        {
            gridBuilder.ShowOnlyRoomGrid(roomID);
        }

        // 4) Set Camera Focus
        FocusCameraTopDownOnRoom(roomID);
        cameraController.ResetViewForRoom();
        
        // 5) Set currentRoomID
        currentRoomID = roomID;
    }

    // Show All Rooms
    public void SetAllRoomsActive(bool active)
    {
        foreach (KeyValuePair<int, RoomRuntimeData> pair in roomsById)
        {
            RoomRuntimeData roomData = pair.Value;
            if (roomData != null)
            {
                roomData.SetActive(active);
            }
        }

        if (gridBuilder != null)
        {
            if (active)
            {
                gridBuilder.ShowAllRoomGrids();
            }
            else
            {
                gridBuilder.HideAllRoomGrids();
            }
        }
    }

    // ===== Getters =====
    
    // RoomDef Getter
    public RoomDef GetRoomDefById(int roomID)
    {
        if (layout == null)
        {
            return null;
        }

        if (layout.rooms == null)
        {
            return null;
        }

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            RoomDef r = layout.rooms[i];
            if (r == null)
            {
                continue;
            }

            if (r.roomID == roomID)
            {
                return r;
            }
        }

        return null;
    }
    
    private FloorDef FindFloorById(int floorID)
    {
        if (layout == null || layout.floors == null)
        {
            return null;
        }

        for (int i = 0; i < layout.floors.Count; i++)
        {
            FloorDef f = layout.floors[i];
            if (f != null && f.id == floorID)
            {
                return f;
            }
        }
        return null;
    }
}
