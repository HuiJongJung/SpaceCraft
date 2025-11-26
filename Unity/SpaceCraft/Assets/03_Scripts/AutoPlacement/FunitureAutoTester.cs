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
            roomManager = FindObjectOfType<RoomManager>();
        }

        if (roomManager == null)
        {
            Debug.Log("FurnitureAutoTester : RoomManager is null");
            gameObject.SetActive(false);
        }

        if (furnitureManager == null)
            furnitureManager = FindObjectOfType<FurnitureManager>();
    }
    void Update()
    {
        // Space 키 누르면 테스트
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 하나씩(AutoPlaceOneItem)이 아니라 전체(AutoPlaceAllUnplacedItems) 호출
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

        // 1. 매니저에게 "이 방에 있는 가구 다 줘" 라고 요청
        List<FurnitureItemData> itemsInRoom = furnitureManager.GetItemsInRoom(currentRoomID);

        // 2. 삭제할 ID만 따로 모음 (루프 돌면서 리스트를 건드리면 에러가 날 수 있어서)
        List<string> idsToRemove = new List<string>();

        foreach (var item in itemsInRoom)
        {
            // 이미 배치된(isPlaced == true) 가구만 삭제 대상
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

        // 3. 모아둔 ID로 하나씩 삭제 요청
        foreach (string id in idsToRemove)
        {
            // Placer에게 삭제 요청 -> 그리드 복구 -> 매니저 삭제 -> 화면 갱신
            placer.UnplaceFurniture(id);
        }

        Debug.Log($"총 {idsToRemove.Count}개의 가구를 삭제하고 그리드를 복구했습니다.");
    }
}
