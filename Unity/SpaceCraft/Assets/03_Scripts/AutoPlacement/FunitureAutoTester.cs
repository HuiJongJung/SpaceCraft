using System.Collections.Generic;
using UnityEngine;

public class FunitureAutoTester : MonoBehaviour
{
    public RoomManager roomManager;
    public FurniturePlacer placer;
    public FurnitureManager furnitureManager;

    void Start()
    {
        if (roomManager == null)
        {
            roomManager = FindFirstObjectByType<RoomManager>(FindObjectsInactive.Include);
        }

        if (roomManager == null)
        {
            Debug.Log("FurnitureAutoTester : RoomManager is null");
            gameObject.SetActive(false);
        }

        if (furnitureManager == null)
            furnitureManager = FindFirstObjectByType<FurnitureManager>(FindObjectsInactive.Include);
    }
    void Update()
    {
        // Space 키 누르면 테스트
        if (Input.GetKeyDown(KeyCode.Space))
        {
            placer.AutoPlaceAllUnplacedItems(roomManager.currentRoomID);
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            RemoveAllInCurrentRoom();
        }
    }

    private void RemoveAllInCurrentRoom()
    {
        int currentRoomID = roomManager.currentRoomID;
        List<FurnitureItemData> itemsInRoom = furnitureManager.GetItemsInRoom(currentRoomID);

        List<string> idsToRemove = new List<string>();

        foreach (var item in itemsInRoom)
        {
            if (item.isPlaced)
            {
                idsToRemove.Add(item.instanceId);
            }
        }

        if (idsToRemove.Count == 0)
        {
            Debug.Log("삭제할 가구가 없습니다.");
            return;
        }

        foreach (string id in idsToRemove)
        {
            placer.UnplaceFurniture(id);
        }

        Debug.Log($"총 {idsToRemove.Count}개의 가구를 삭제하고 그리드를 복구했습니다.");
    }
}
