using System.Collections.Generic;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SpaceData spaceData;
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;

    private SpaceLayout layout;

    // roomID -> runtime data
    private Dictionary<int, RoomRuntimeData> roomsById =
        new Dictionary<int, RoomRuntimeData>();

    // 디버그용 인스펙터 확인
    [SerializeField] private List<RoomRuntimeData> debugRooms =
        new List<RoomRuntimeData>();

    private void Awake()
    {
        if (spaceData == null)
        {
            spaceData = SpaceData.Instance;
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
    }

    // ===== Camera Focus =====
    private void FocusCameraOnRoom(int roomID)
    {
        if (layout == null)
        {
            return;
        }

        // 1) roomID에 해당하는 RoomDef 찾기
        RoomDef room = null;
        if (layout.rooms != null)
        {
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                RoomDef r = layout.rooms[i];
                if (r != null && r.roomID == roomID)
                {
                    room = r;
                    break;
                }
            }
        }

        if (room == null)
        {
            return;
        }

        // 2) 방이 가지고 있는 floor 중 첫 번째 기준으로 중심 계산
        if (room.floorIDs == null || room.floorIDs.Count == 0)
        {
            return;
        }

        int floorID = room.floorIDs[0];

        FloorDef fd = null;
        if (layout.floors != null)
        {
            for (int i = 0; i < layout.floors.Count; i++)
            {
                FloorDef f = layout.floors[i];
                if (f != null && f.id == floorID)
                {
                    fd = f;
                    break;
                }
            }
        }

        if (fd == null || fd.vertices == null || fd.vertices.Count == 0)
        {
            return;
        }

        // 3) 바닥 vertices의 XZ 바운딩 박스 중심 계산
        float minX = fd.vertices[0].x;
        float maxX = fd.vertices[0].x;
        float minZ = fd.vertices[0].z;
        float maxZ = fd.vertices[0].z;

        for (int i = 1; i < fd.vertices.Count; i++)
        {
            Vec3 v = fd.vertices[i];
            if (v.x < minX) minX = v.x;
            if (v.x > maxX) maxX = v.x;
            if (v.z < minZ) minZ = v.z;
            if (v.z > maxZ) maxZ = v.z;
        }

        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        // 4) 메인 카메라 가져와서 XZ만 이동
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 pos = cam.transform.position;
        pos.x = centerX;
        pos.z = centerZ;
        cam.transform.position = pos;
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

        // 4) Camera Focus
        FocusCameraOnRoom(roomID);
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

    public List<GameObject> GetWallsOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        bool found = roomsById.TryGetValue(roomID, out roomData);
        if (found && roomData != null)
        {
            return roomData.walls;
        }
        return null;
    }

    public List<GameObject> GetFloorsOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        bool found = roomsById.TryGetValue(roomID, out roomData);
        if (found && roomData != null)
        {
            return roomData.floors;
        }
        return null;
    }

    public List<GameObject> GetOpeningsOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        bool found = roomsById.TryGetValue(roomID, out roomData);
        if (found && roomData != null)
        {
            return roomData.openings;
        }
        return null;
    }

    public List<GameObject> GetFurnituresOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        bool found = roomsById.TryGetValue(roomID, out roomData);
        if (found && roomData != null)
        {
            return roomData.furnitures;
        }
        return null;
    }
}
