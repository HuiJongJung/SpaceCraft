using UnityEngine;

public class FunitureAutoTester : MonoBehaviour
{
    public RoomManager roomManager;
    public FurniturePlacer placer;
    public int testRoomID = 1;   // 방 하나 정해서 테스트

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
    }
    void Update()
    {
        // Space 키 누르면 테스트
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 하나씩(AutoPlaceOneItem)이 아니라 전체(AutoPlaceAllUnplacedItems) 호출
            placer.AutoPlaceAllUnplacedItems(roomManager.currentRoomID);
        }
    }
}
