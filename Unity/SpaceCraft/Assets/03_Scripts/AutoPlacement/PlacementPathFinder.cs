using UnityEngine;
using System.Collections.Generic;

public static class PlacementPathFinder 
{
    public static bool CheckPassageAvailability(RoomPlacementGrid grid, Vector2Int bodyOrigin, Vector2Int bodySize, int cachedTotalWalkable)
    {
        // 1. 문 위치 찾기
        List<Vector2Int> doors = GetDoorCells(grid);
        if (doors.Count == 0) return true;

        // 2. 가구 면적 계산
        int furnitureArea = bodySize.x * bodySize.y;

        // 3. 예상 도달 가능 면적 (캐싱된 값 - 가구 면적)
        int expectedReachable = cachedTotalWalkable - furnitureArea;

        // 4. 실제 도달 가능 면적 계산 (BFS)
        int actualReachable = CountReachableCellsBFS(grid, bodyOrigin, bodySize, doors);

        // 5. 판정
        return actualReachable >= expectedReachable - 5;
    }

    // BFS로 도달 가능한 셀 개수를 세는 함수
    private static int CountReachableCellsBFS(RoomPlacementGrid grid, Vector2Int hypoOrigin, Vector2Int hypoSize, List<Vector2Int> seeds)
    {
        int cols = grid.cols;
        int rows = grid.rows;
        bool[,] visited = new bool[cols, rows];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        // 시작점(문) 큐에 넣기
        foreach (var seed in seeds)
        {
            if (grid.InBounds(seed.x, seed.y) && !visited[seed.x, seed.y])
            {
                queue.Enqueue(seed);
                visited[seed.x, seed.y] = true;
            }
        }

        int count = 0;
        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            count++;

            for (int i = 0; i < 4; i++)
            {
                int nx = curr.x + dx[i];
                int ny = curr.y + dy[i];

                if (!grid.InBounds(nx, ny)) continue;
                if (visited[nx, ny]) continue;

                if (IsWalkable(grid, nx, ny))
                {
                    if (nx >= hypoOrigin.x && nx < hypoOrigin.x + hypoSize.x &&
                        ny >= hypoOrigin.y && ny < hypoOrigin.y + hypoSize.y)
                    {
                        continue; 
                    }

                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        return count;
    }

    private static bool IsWalkable(RoomPlacementGrid grid, int gx, int gz)
    {
        // 1. 문 위는 걸을 수 있음
        if (grid.doorMask != null && grid.doorMask[gx, gz]) return true;

        // 2. 가구 본체 위는 못 걸음
        if (grid.physicalBodyMask != null && grid.physicalBodyMask[gx, gz]) return false;

        // 3. 벽이나 허공은 못 걸음
        if (grid.placementMask[gx, gz]) return true; // 빈 땅
        if (grid.occupiedMask != null && grid.occupiedMask[gx, gz]) return true; // 여유 공간

        return false; // 그 외
    }

    public static int CountTotalWalkableCells(RoomPlacementGrid grid)
    {
        int count = 0;
        for (int z = 0; z < grid.rows; z++)
        {
            for (int x = 0; x < grid.cols; x++)
            {
                if (IsWalkable(grid, x, z)) count++;
            }
        }
        return count;
    }

    private static List<Vector2Int> GetDoorCells(RoomPlacementGrid grid)
    {
        List<Vector2Int> doors = new List<Vector2Int>();
        if (grid.doorMask == null) return doors;

        for (int z = 0; z < grid.rows; z++)
        {
            for (int x = 0; x < grid.cols; x++)
            {
                if (grid.doorMask[x, z])
                    doors.Add(new Vector2Int(x, z));
            }
        }
        return doors;
    }
}
