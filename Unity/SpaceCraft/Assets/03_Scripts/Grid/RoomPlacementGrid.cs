using UnityEngine;

public class RoomPlacementGrid
{
    public int roomID;
    public string roomName;

    // 그리드 좌표계
    public Vector2 originXZ;   // (xmin, zmin) in meters
    public float cellSize;     // e.g., 0.1f
    public int cols;           // X 방향 셀 수
    public int rows;           // Z 방향 셀 수

    // 배치 후보 맵 (방 내부 True)
    public bool[,] placementMask;
    // 가구에 의해 점유된 상태인지 확인용
    public bool[,] occupiedMask;
    // 벽 인접 여부
    public bool[,] wallZoneMask;

    // 월드↔그리드 간단 변환 유틸
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int gx = Mathf.FloorToInt((worldPos.x - originXZ.x) / cellSize);
        int gz = Mathf.FloorToInt((worldPos.z - originXZ.y) / cellSize);
        return new Vector2Int(gx, gz);
    }

    public Vector3 GridCenterToWorld(int gx, int gz, float y = 0f)
    {
        float cx = originXZ.x + (gx + 0.5f) * cellSize;
        float cz = originXZ.y + (gz + 0.5f) * cellSize;
        return new Vector3(cx, y, cz);
    }

    public bool InBounds(int gx, int gz) => gx >= 0 && gz >= 0 && gx < cols && gz < rows;
}
