using UnityEngine;

public static class PlacementValidator
{
    // 전체 영역이 유효한지(범위 안, 마스크 True) 검사하는 함수
    public static bool CheckAreaValid(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size)
    {
        for (int dz = 0; dz < size.y; dz++)
        {
            for (int dx = 0; dx < size.x; dx++)
            {
                int gx = origin.x + dx;
                int gz = origin.y + dz;

                // 그리드 밖이거나, 이미 점유/벽인 경우 실패
                if (!grid.InBounds(gx, gz) || !grid.placementMask[gx, gz])
                    return false;
            }
        }
        return true;
    }

    /// 해당 면의 모든 셀이 벽(또는 허공)과 접촉하고 있는지 검사합니다.
    /// 단 한 칸이라도 빈 공간(통로)과 접해있다면 실패로 간주합니다.
    public static bool CheckSideTouchingWall(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size, int rot, string side)
    {
        string gridSide = PlacementCalculator.GetGridSideByRotation(rot, side);
        if (string.IsNullOrEmpty(gridSide)) return false;

        switch (gridSide)
        {
            case "bottom": // 아랫면 검사 (Z - 1 위치 확인)
                for (int dx = 0; dx < size.x; dx++)
                {
                    // 하나라도 벽이 아니면(빈 공간이면) 즉시 탈락
                    if (!IsWallOrVoid(grid, origin.x + dx, origin.y - 1))
                        return false;
                }
                break;

            case "top": // 윗면 검사 (Z + size.y 위치 확인)
                for (int dx = 0; dx < size.x; dx++)
                {
                    if (!IsWallOrVoid(grid, origin.x + dx, origin.y + size.y))
                        return false;
                }
                break;

            case "left": // 왼쪽면 검사 (X - 1 위치 확인)
                for (int dz = 0; dz < size.y; dz++)
                {
                    if (!IsWallOrVoid(grid, origin.x - 1, origin.y + dz))
                        return false;
                }
                break;

            case "right": // 오른쪽면 검사 (X + size.x 위치 확인)
                for (int dz = 0; dz < size.y; dz++)
                {
                    if (!IsWallOrVoid(grid, origin.x + size.x, origin.y + dz))
                        return false;
                }
                break;
        }

        // 루프를 무사히 통과했다면, 모든 칸이 벽에 닿아있다는 뜻임
        return true;
    }

    /// 해당 좌표가 벽, 허공, 혹은 배치 불가능한 구역인지 확인합니다.
    private static bool IsWallOrVoid(RoomPlacementGrid grid, int gx, int gz)
    {
        // 1. 그리드 밖이면 -> 무조건 벽
        if (!grid.InBounds(gx, gz)) return true;

        // 2. 문(Door) 구역 -> 벽 아님 (허공)
        if (grid.doorMask != null && grid.doorMask[gx, gz]) return false;

        // 3. 다른 가구가 있는 곳(Occupied) -> 벽 아님 (장애물일 뿐)
        if (grid.occupiedMask != null && grid.occupiedMask[gx, gz]) return false;

        // 4. 그 외에 배치 불가능한 곳(진짜 벽, 기둥) -> 벽
        if (!grid.placementMask[gx, gz]) return true;

        return false; // 빈 땅
    }
}
