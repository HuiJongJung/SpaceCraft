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

    // ID -> roomID
    private Dictionary<int, int> floorToRoom =
        new Dictionary<int, int>();
    // wallID -> roomID
    private Dictionary<int, List<int>> wallToRooms =
        new Dictionary<int, List<int>>();
    // openingID -> roomID
    private Dictionary<int, List<int>> openingToRooms =
        new Dictionary<int, List<int>>();
    // Furniture의 roomID는 반드시 1개
    private Dictionary<string, int> furnitureToRoom =
        new Dictionary<string, int>();

    // GameObject -> 포함 roomID 리스트
    private Dictionary<GameObject, List<int>> objectToRooms =
        new Dictionary<GameObject, List<int>>();
    
    // 현재 "켜져 있어야 하는" 방 집합
    private HashSet<int> activeRooms =
        new HashSet<int>();

    // 디버그용 인스펙터 확인
    [SerializeField] private List<RoomRuntimeData> debugRooms =
        new List<RoomRuntimeData>();

    private bool mapsBuilt = false;

    private void Start()
    {
        EnsureRoomMaps();
        
        if (gridBuilder == null)
        {
            gridBuilder = FindObjectOfType<RoomPlacementGridBuilder>();
        }
    }
    
    // Camera Follow
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
    
    // Grid Visual
    private void SyncRoomGridVisibility()
    {
        if (gridBuilder == null)
        {
            return;
        }

        // 일단 전부 끄고
        gridBuilder.HideAllRoomGrids();

        // activeRooms 에 들어있는 roomID만 켠다
        foreach (int roomID in activeRooms)
        {
            gridBuilder.SetRoomGridVisible(roomID, true);
        }
    }

    public void EnsureRoomMaps()
    {
        if (mapsBuilt)
        {
            return;
        }

        if (spaceData == null)
        {
            spaceData = SpaceData.Instance;
        }

        if (spaceData == null || spaceData._layout == null)
        {
            Debug.LogWarning("[RoomManager] SpaceData or layout is null.");
            return;
        }

        layout = spaceData._layout;
        BuildRoomMaps();
        mapsBuilt = true;

        // 처음에는 모든 방 켜진 상태로 시작
        activeRooms.Clear();
        foreach (KeyValuePair<int, RoomRuntimeData> pair in roomsById)
        {
            activeRooms.Add(pair.Key);
        }
        UpdateAllObjectActives();
    }

    private void BuildRoomMaps()
    {
        if (layout == null)
        {
            Debug.LogWarning("[RoomManager] layout is null.");
            return;
        }

        floorToRoom.Clear();
        wallToRooms.Clear();
        openingToRooms.Clear();
        furnitureToRoom.Clear();
        roomsById.Clear();
        objectToRooms.Clear();
        debugRooms.Clear();

        // 1. RoomDef 기반 floorID/wallID -> roomID 리스트
        if (layout.rooms != null)
        {
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                RoomDef room = layout.rooms[i];
                if (room == null)
                {
                    continue;
                }

                int roomID = room.roomID;

                if (room.floorIDs != null)
                {
                    for (int f = 0; f < room.floorIDs.Count; f++)
                    {
                        int floorID = room.floorIDs[f];
                        if (!floorToRoom.ContainsKey(floorID))
                        {
                            floorToRoom.Add(floorID, roomID);
                        }
                    }
                }

                if (room.wallIDs != null)
                {
                    for (int w = 0; w < room.wallIDs.Count; w++)
                    {
                        int wallID = room.wallIDs[w];

                        List<int> list;
                        if (!wallToRooms.TryGetValue(wallID, out list))
                        {
                            list = new List<int>();
                            wallToRooms.Add(wallID, list);
                        }

                        if (!list.Contains(roomID))
                        {
                            list.Add(roomID);
                        }
                    }
                }
            }
        }

        // 2. OpeningDef 기반 openingID -> roomID 리스트 (wallID -> roomIDs 이용)
        if (layout.openings != null)
        {
            for (int i = 0; i < layout.openings.Count; i++)
            {
                OpeningDef od = layout.openings[i];
                if (od == null)
                {
                    continue;
                }

                List<int> roomsOfWall;
                if (!wallToRooms.TryGetValue(od.wallID, out roomsOfWall))
                {
                    continue;
                }

                List<int> list;
                if (!openingToRooms.TryGetValue(od.id, out list))
                {
                    list = new List<int>();
                    openingToRooms.Add(od.id, list);
                }

                for (int r = 0; r < roomsOfWall.Count; r++)
                {
                    int roomID = roomsOfWall[r];
                    if (!list.Contains(roomID))
                    {
                        list.Add(roomID);
                    }
                }
            }
        }

        // 3. FurnitureItemData 기반 instanceId -> roomID
        if (layout.furnitures != null)
        {
            for (int i = 0; i < layout.furnitures.Count; i++)
            {
                FurnitureItemData fd = layout.furnitures[i];
                if (fd == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(fd.instanceId))
                {
                    if (!furnitureToRoom.ContainsKey(fd.instanceId))
                    {
                        furnitureToRoom.Add(fd.instanceId, fd.roomID);
                    }
                }
            }
        }
    }

    private RoomRuntimeData GetOrCreateRoomData(int roomID)
    {
        RoomRuntimeData roomData;
        if (!roomsById.TryGetValue(roomID, out roomData))
        {
            roomData = new RoomRuntimeData(roomID);
            roomsById.Add(roomID, roomData);
            debugRooms.Add(roomData);
        }
        return roomData;
    }
    
    // Register roomID to RoomObject
    public void Register(RoomObject obj, int roomID)
    {
        if (obj == null)
        {
            return;
        }

        // Update RoomObject roomIDs 
        if (!obj.roomIDs.Contains(roomID))
        {
            obj.roomIDs.Add(roomID);
        }

        RoomRuntimeData roomData = GetOrCreateRoomData(roomID);
        roomData.Add(obj);

        GameObject go = obj.gameObject;
        List<int> list;
        if (!objectToRooms.TryGetValue(go, out list))
        {
            list = new List<int>();
            objectToRooms.Add(go, list);
        }

        if (!list.Contains(roomID))
        {
            list.Add(roomID);
        }
    }

    // ===== 활성화 갱신 공통 함수 =====
    private void UpdateAllObjectActives()
    {
        foreach (KeyValuePair<GameObject, List<int>> pair in objectToRooms)
        {
            GameObject go = pair.Key;
            List<int> rooms = pair.Value;

            bool visible = false;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (activeRooms.Contains(rooms[i]))
                {
                    visible = true;
                    break;
                }
            }

            if (go != null)
            {
                go.SetActive(visible);
            }

            SyncRoomGridVisibility();
        }
    }

    // ===== 방 활성화 / 비활성 =====
    public void SetActiveOnly(int roomID)
    {
        activeRooms.Clear();
        activeRooms.Add(roomID);
        UpdateAllObjectActives();
        FocusCameraOnRoom(roomID);
    }

    public void SetRoomActive(int roomID, bool active)
    {
        if (active)
        {
            activeRooms.Add(roomID);
        }
        else
        {
            if (activeRooms.Contains(roomID))
            {
                activeRooms.Remove(roomID);
            }
        }

        UpdateAllObjectActives();
    }

    public void SetAllRoomsActive(bool active)
    {
        activeRooms.Clear();

        if (active)
        {
            foreach (KeyValuePair<int, RoomRuntimeData> pair in roomsById)
            {
                activeRooms.Add(pair.Key);
            }
        }

        UpdateAllObjectActives();
    }

    #region Getters
    public List<GameObject> GetWallsOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        if (roomsById.TryGetValue(roomID, out roomData))
        {
            return roomData.walls;
        }
        return null;
    }

    public List<GameObject> GetFloorsOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        if (roomsById.TryGetValue(roomID, out roomData))
        {
            return roomData.floors;
        }
        return null;
    }

    public List<GameObject> GetOpeningsOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        if (roomsById.TryGetValue(roomID, out roomData))
        {
            return roomData.openings;
        }
        return null;
    }

    public List<Furniture> GetFurnituresOfRoom(int roomID)
    {
        RoomRuntimeData roomData;
        if (roomsById.TryGetValue(roomID, out roomData))
        {
            return roomData.furnitures;
        }
        return null;
    }
    #endregion
}
