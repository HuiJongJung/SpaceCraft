using UnityEngine;

public static class PlacementValidator
{
    public static bool CheckAreaValid(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size)
    {
        for (int dz = 0; dz < size.y; dz++)
        {
            for (int dx = 0; dx < size.x; dx++)
            {
                int gx = origin.x + dx;
                int gz = origin.y + dz;

                if (!grid.InBounds(gx, gz) || !grid.placementMask[gx, gz])
                    return false;
            }
        }
        return true;
    }

    public static bool CheckSideTouchingWall(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size, int rot, string side)
    {
        string gridSide = PlacementCalculator.GetGridSideByRotation(rot, side);
        if (string.IsNullOrEmpty(gridSide)) return false;

        switch (gridSide)
        {
            case "bottom": 
                for (int dx = 0; dx < size.x; dx++)
                {
                    if (!IsWallOrVoid(grid, origin.x + dx, origin.y - 1))
                        return false;
                }
                break;

            case "top": 
                for (int dx = 0; dx < size.x; dx++)
                {
                    if (!IsWallOrVoid(grid, origin.x + dx, origin.y + size.y))
                        return false;
                }
                break;

            case "left": 
                for (int dz = 0; dz < size.y; dz++)
                {
                    if (!IsWallOrVoid(grid, origin.x - 1, origin.y + dz))
                        return false;
                }
                break;

            case "right": 
                for (int dz = 0; dz < size.y; dz++)
                {
                    if (!IsWallOrVoid(grid, origin.x + size.x, origin.y + dz))
                        return false;
                }
                break;
        }

        return true;
    }

    private static bool IsWallOrVoid(RoomPlacementGrid grid, int gx, int gz)
    {
        // 그리드 밖 -> 벽
        if (!grid.InBounds(gx, gz)) return true;

        // 2. 문 구역 -> 벽 아님 (허공)
        if (grid.doorMask != null && grid.doorMask[gx, gz]) return false;

        // 3. 다른 가구가 있는 곳 -> 벽 아님 
        if (grid.occupiedMask != null && grid.occupiedMask[gx, gz]) return false;

        // 4. 그 외에 배치 불가능한 곳 -> 벽
        if (!grid.placementMask[gx, gz]) return true;

        return false; // 빈 땅
    }
}
