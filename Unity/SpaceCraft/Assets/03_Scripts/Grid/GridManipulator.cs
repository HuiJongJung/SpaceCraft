using UnityEngine;

public static class GridManipulator 
{
    public static void MarkGridAsOccupied(RoomPlacementGrid grid,
        Vector2Int totalOrigin, Vector2Int totalSize,
        Vector2Int bodyOrigin, Vector2Int bodySize)
    {
        for (int dz = 0; dz < totalSize.y; dz++)
        {
            for (int dx = 0; dx < totalSize.x; dx++)
            {
                int gx = totalOrigin.x + dx;
                int gz = totalOrigin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    grid.placementMask[gx, gz] = false; 
                    if (grid.occupiedMask != null)
                        grid.occupiedMask[gx, gz] = true; 
                }
            }
        }

        for (int dz = 0; dz < bodySize.y; dz++)
        {
            for (int dx = 0; dx < bodySize.x; dx++)
            {
                int gx = bodyOrigin.x + dx;
                int gz = bodyOrigin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    if (grid.physicalBodyMask != null)
                        grid.physicalBodyMask[gx, gz] = true; 
                }
            }
        }
    }

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
                    grid.placementMask[gx, gz] = true; 
                    if (grid.occupiedMask != null)
                        grid.occupiedMask[gx, gz] = false;
                }
            }
        }

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
