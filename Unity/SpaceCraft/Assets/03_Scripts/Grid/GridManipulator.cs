using UnityEngine;

public static class GridManipulator 
{
    /// 배치가 확정된 영역을 그리드에서 '사용 불가(false)'로 처리하고,
    /// '점유됨(occupied)' 상태로 마킹합니다.
    public static void MarkGridAsOccupied(RoomPlacementGrid grid,
        Vector2Int totalOrigin, Vector2Int totalSize,
        Vector2Int bodyOrigin, Vector2Int bodySize)
    {
        // 1. 전체 영역 (여유 공간 포함) -> 배치 금지, 점유 표시(주황색)
        for (int dz = 0; dz < totalSize.y; dz++)
        {
            for (int dx = 0; dx < totalSize.x; dx++)
            {
                int gx = totalOrigin.x + dx;
                int gz = totalOrigin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    grid.placementMask[gx, gz] = false; // 다른 가구 배치 불가
                    if (grid.occupiedMask != null)
                        grid.occupiedMask[gx, gz] = true; // 시각화용 (주황색)
                }
            }
        }

        // 2. 본체 영역 (진짜 가구) -> 물리적 장애물 표시(빨간색)
        for (int dz = 0; dz < bodySize.y; dz++)
        {
            for (int dx = 0; dx < bodySize.x; dx++)
            {
                int gx = bodyOrigin.x + dx;
                int gz = bodyOrigin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    if (grid.physicalBodyMask != null)
                        grid.physicalBodyMask[gx, gz] = true; // 시각화 및 경로 탐색용 (빨간색)
                }
            }
        }
    }

    ///  점유된 그리드 영역을 다시 '배치 가능' 상태로 되돌립니다.
    public static void UnmarkGrid(RoomPlacementGrid grid,
         Vector2Int totalOrigin, Vector2Int totalSize,
         Vector2Int bodyOrigin, Vector2Int bodySize)
    {
        // 1. 전체 영역 복구
        for (int dz = 0; dz < totalSize.y; dz++)
        {
            for (int dx = 0; dx < totalSize.x; dx++)
            {
                int gx = totalOrigin.x + dx;
                int gz = totalOrigin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    grid.placementMask[gx, gz] = true; // 다시 배치 가능
                    if (grid.occupiedMask != null)
                        grid.occupiedMask[gx, gz] = false;
                }
            }
        }

        // 2. 본체 영역 복구
        for (int dz = 0; dz < bodySize.y; dz++)
        {
            for (int dx = 0; dx < bodySize.x; dx++)
            {
                int gx = bodyOrigin.x + dx;
                int gz = bodyOrigin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    if (grid.physicalBodyMask != null)
                        grid.physicalBodyMask[gx, gz] = false;
                }
            }
        }
    }
}
