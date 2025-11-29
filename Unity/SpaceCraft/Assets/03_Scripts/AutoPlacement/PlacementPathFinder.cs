using UnityEngine;
using System.Collections.Generic;

public static class PlacementPathFinder 
{
    /// 가구(body)를 해당 위치(origin)에 가상 배치했을 때, 문에서 시작된 길이 막히지 않는지 검사
    public static bool CheckPassageAvailability(RoomPlacementGrid grid, Vector2Int bodyOrigin, Vector2Int bodySize)
    {
        // 1. 문(Door) 위치 찾기 (시작점)
        List<Vector2Int> doors = GetDoorCells(grid);
        if (doors.Count == 0) return true; // 문이 없으면 검사 패스 (예외)

        // 2. 현재 상태에서 '사람이 갈 수 있는 빈 땅'의 총개수 계산
        int currentTotalWalkable = CountTotalWalkableCells(grid);

        // 3. 가구가 차지할 면적 계산 (겹치는 부분 제외하고 순수하게 줄어들 면적)
        // (정확한 계산을 위해 BFS 내부에서 체크하는 것이 좋지만, 약식으로 크기만 뺌)
        int furnitureArea = bodySize.x * bodySize.y;

        // 4. 예상되는 도달 가능 면적 (원래 빈 땅 - 가구 면적)
        int expectedReachable = currentTotalWalkable - furnitureArea;

        // 5. 실제 도달 가능 면적 계산 (BFS 탐색)
        int actualReachable = CountReachableCellsBFS(grid, bodyOrigin, bodySize, doors);

        // 6. 판정: 실제 갈 수 있는 땅이 예상보다 터무니없이 적다면 -> 길 막힘!
        // (오차 허용 범위: 2칸 정도)
        return actualReachable >= expectedReachable - 2;
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

                // 이동 가능한 땅인가? (장애물 체크)
                if (IsWalkable(grid, nx, ny))
                {
                    // [중요] 가상 장애물(지금 놓아볼 가구) 체크
                    if (nx >= hypoOrigin.x && nx < hypoOrigin.x + hypoSize.x &&
                        ny >= hypoOrigin.y && ny < hypoOrigin.y + hypoSize.y)
                    {
                        continue; // 가구 놓을 자리는 못 지나감
                    }

                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        return count;
    }

    // 해당 셀이 '사람이 걸어다닐 수 있는 곳'인지 판단
    private static bool IsWalkable(RoomPlacementGrid grid, int gx, int gz)
    {
        // 1. 문 위는 걸을 수 있음
        if (grid.doorMask != null && grid.doorMask[gx, gz]) return true;

        // 2. 물리적 가구 본체(Physical Body) 위는 못 걸음 (가장 중요!)
        if (grid.physicalBodyMask != null && grid.physicalBodyMask[gx, gz]) return false;

        // 3. 벽이나 허공은 못 걸음
        // (placementMask가 false여도 occupiedMask가 true면 여유공간이므로 걸을 수 있음)
        if (grid.placementMask[gx, gz]) return true; // 빈 땅
        if (grid.occupiedMask != null && grid.occupiedMask[gx, gz]) return true; // 여유 공간

        return false; // 그 외(진짜 벽)
    }

    private static int CountTotalWalkableCells(RoomPlacementGrid grid)
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
