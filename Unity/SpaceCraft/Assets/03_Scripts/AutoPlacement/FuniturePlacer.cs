using NUnit.Framework.Internal;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using static UnityEngine.UI.GridLayoutGroup;

public class FurniturePlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;
    [SerializeField] private FurnitureManager furnitureManager;
    public RoomManager roomManager;

    [Header("Grid Settings")]
    [SerializeField] private float cellSizeMeters = 0.1f;   // 10cm
    private struct PlacementCandidate
    {
        public Vector2Int origin;      // 배치 위치 (좌측 하단)
        public int rotation;           // 회전 각도
        public Vector2Int sizeInCells; // 차지하는 공간 크기 (여유공간 포함)
    }

    private void Awake()
    {
        if (gridBuilder == null)
            Debug.LogError($"[FurniturePlacer] gridBuilder is not assigned!", this);

        if (furnitureManager == null)
            Debug.LogError($"[FurniturePlacer] furnitureManager is not assigned!", this);

        if(roomManager == null)
#pragma warning disable CS0618
            roomManager = FindObjectOfType<RoomManager>();
#pragma warning restore CS0618 
    }
    
    
// originCell을 기준으로
// 본체 + 여유공간 전부 placementMask 안에 들어가는지 검사
    public bool CanPlaceOnGrid(
        FurnitureItemData item,
        int roomID,
        Vector2Int originCell,
        int rotationDeg,
        out Vector2Int sizeInCells
    )
    {
        sizeInCells = Vector2Int.zero;

        RoomPlacementGrid grid = FindGridByRoomId(roomID);
        if (grid == null)
        {
            Debug.LogWarning("[FurniturePlacer] CanPlaceOnGrid: No grid for roomID=" + roomID);
            return false;
        }

        float cellSize = grid.cellSize;
        if (cellSize <= 0f)
        {
            cellSize = cellSizeMeters;
        }

        // 1) 본체 footprint 계산
        Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(
            item.sizeCentimeters,
            cellSize,
            rotationDeg
        );

        if (bodySize.x <= 0 || bodySize.y <= 0)
        {
            return false;
        }

        sizeInCells = bodySize;

        // 2) 회전된 여유공간을 셀 단위로 계산
        //    originCell 은 "본체 좌하단" 이라고 가정
        var clearance = PlacementCalculator.GetRotatedClearanceInCells(
            item,
            cellSize,
            rotationDeg
        );

        Vector2Int bodyOrigin = originCell;

        // 3) 전체 영역(Total = 본체 + 여유공간) 계산
        Vector2Int totalOrigin = new Vector2Int(
            bodyOrigin.x - clearance.left,
            bodyOrigin.y - clearance.bottom
        );

        Vector2Int totalSize = new Vector2Int(
            clearance.left + bodySize.x + clearance.right,
            clearance.bottom + bodySize.y + clearance.top
        );

        // 4) 전체 영역에 대해 InBounds + placementMask 검사
        for (int dz = 0; dz < totalSize.y; dz++)
        {
            for (int dx = 0; dx < totalSize.x; dx++)
            {
                int gx = totalOrigin.x + dx;
                int gz = totalOrigin.y + dz;

                if (!grid.InBounds(gx, gz))
                {
                    return false;
                }

                if (!grid.placementMask[gx, gz])
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool CanPlaceBasic(
        FurnitureItemData item,
        int roomID,
        Vector2Int originCell,
        int rotationDeg,
        out Vector2Int sizeInCells   // (widthCells, depthCells)
    )
    {
        sizeInCells = Vector2Int.zero;

        RoomPlacementGrid grid = FindGridByRoomId(roomID);
        if (grid == null)
        {
            Debug.LogWarning($"[FurniturePlacer] No grid found for roomID={roomID}");
            return false;
        }

        // grid의 cellSize를 우선 사용 (필요시 인스펙터 값과 동기화 가능)
        float cellSize = grid.cellSize;
        if (cellSize <= 0f) cellSize = cellSizeMeters;

        // 1) 가구의 footprint (셀 단위 W x D) 계산
        sizeInCells = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, rotationDeg);

        if (sizeInCells.x <= 0 || sizeInCells.y <= 0)
            return false;

        // 2) footprint에 포함되는 모든 셀에 대해 유효한지 검사
        for (int dz = 0; dz < sizeInCells.y; dz++)
        {
            for (int dx = 0; dx < sizeInCells.x; dx++)
            {
                int gx = originCell.x + dx;
                int gz = originCell.y + dz;

                // 범위 밖이거나, 배치 불가능(Mask=false)하면 실패
                if (!grid.InBounds(gx, gz) || !grid.placementMask[gx, gz])
                    return false;
            }
        }

        if (PlacementCalculator.IsWallPlacementRequired(item))
        {
            bool needBack = item.wallDir.back;
            bool needFront = item.wallDir.front;
            bool needLeft = item.wallDir.left;
            bool needRight = item.wallDir.right;

            bool requiresWall = needBack || needFront || needLeft || needRight;

            if (requiresWall)
            {
                if (needBack && !PlacementValidator.CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "back"))
                    return false;

                if (needFront && !PlacementValidator.CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "front"))
                    return false;

                if (needLeft && !PlacementValidator.CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "left"))
                    return false;

                if (needRight && !PlacementValidator.CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "right"))
                    return false;
            }
        }
        return true;
    }

    public bool TryAutoPlaceBasic(FurnitureItemData item, int roomID, bool updateVisuals = true)
    {
        RoomPlacementGrid grid = FindGridByRoomId(roomID);
        if (grid == null) return false;

        float cellSize = grid.cellSize > 0 ? grid.cellSize : cellSizeMeters;

        int[] rotations = { 0, 90, 180, 270 };
        PlacementCalculator.ShuffleArray(rotations);

        List<PlacementCandidate> candidates = new List<PlacementCandidate>();
        bool requireWall = PlacementCalculator.IsWallPlacementRequired(item);

        for (int rIndex = 0; rIndex < rotations.Length; rIndex++)
        {
            int rot = rotations[rIndex];
            Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, rot);
            if (bodySize.x <= 0 || bodySize.y <= 0) continue;
            var clearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, rot);
            Vector2Int totalSize = new Vector2Int(clearance.left + bodySize.x + clearance.right, clearance.bottom + bodySize.y + clearance.top);

            for (int gz = 0; gz < grid.rows; gz++)
            {
                for (int gx = 0; gx < grid.cols; gx++)
                {
                    Vector2Int totalOrigin = new Vector2Int(gx, gz);

                    if (!PlacementValidator.CheckAreaValid(grid, totalOrigin, totalSize)) continue;

                    Vector2Int bodyOrigin = new Vector2Int(totalOrigin.x + clearance.left, totalOrigin.y + clearance.bottom);

                    if (requireWall)
                    {
                        bool backOk = !item.wallDir.back || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "back");
                        bool frontOk = !item.wallDir.front || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "front");
                        bool leftOk = !item.wallDir.left || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "left");
                        bool rightOk = !item.wallDir.right || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "right");

                        if (!backOk || !frontOk || !leftOk || !rightOk) continue;
                    }

                    candidates.Add(new PlacementCandidate
                    {
                        origin = totalOrigin,
                        rotation = rot,
                        sizeInCells = totalSize
                    });
                }
            }
        }

        if (candidates.Count == 0) return false;

        List<Vector2Int> doorCells = new List<Vector2Int>();
        if (grid.doorMask != null)
        {
            for (int z = 0; z < grid.rows; z++)
                for (int x = 0; x < grid.cols; x++)
                    if (grid.doorMask[x, z]) doorCells.Add(new Vector2Int(x, z));
        }

        Vector2Int[] corners = {
            new Vector2Int(0, 0), new Vector2Int(grid.cols, 0),
            new Vector2Int(0, grid.rows), new Vector2Int(grid.cols, grid.rows)
        };

        var sortedCandidates = candidates.OrderBy(c =>
        {
            // 1. 구석 거리 (가까울수록 좋음)
            float minCornerDist = float.MaxValue;
            foreach (var corner in corners)
            {
                float d = Vector2Int.Distance(c.origin, corner);
                if (d < minCornerDist) minCornerDist = d;
            }

            // 2. 문 거리 (가까우면 벌점)
            float doorPenalty = 0f;
            if (doorCells.Count > 0)
            {
                float minDoorDist = float.MaxValue;
                foreach (var door in doorCells)
                {
                    float d = Vector2Int.Distance(c.origin, door);
                    if (d < minDoorDist) minDoorDist = d;
                }

                // 1.5m 이내면 벌점 부과
                if (minDoorDist < 15.0f)
                    doorPenalty = (15.0f - minDoorDist) * 1.0f;
            }

            return minCornerDist + doorPenalty;

        }).ToList();

        // 상위권 먼저 시도 -> 실패 시 하위권 시도
        int cutIndex = Mathf.Max(1, sortedCandidates.Count / 2);
        List<PlacementCandidate> topGroup = sortedCandidates.Take(cutIndex).ToList();
        List<PlacementCandidate> bottomGroup = sortedCandidates.Skip(cutIndex).ToList();

        PlacementCalculator.ShuffleList(topGroup);
        PlacementCalculator.ShuffleList(bottomGroup);

        List<PlacementCandidate> finalQueue = new List<PlacementCandidate>();
        finalQueue.AddRange(topGroup);
        finalQueue.AddRange(bottomGroup);

        PlacementCandidate best = finalQueue[0];
        bool foundValidPath = false;
        int cachedTotalWalkable = PlacementPathFinder.CountTotalWalkableCells(grid);

        foreach (var cand in finalQueue)
        {
            var clearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, cand.rotation);
            Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, cand.rotation);
            Vector2Int bodyOrigin = new Vector2Int(cand.origin.x + clearance.left, cand.origin.y + clearance.bottom);

            if (PlacementPathFinder.CheckPassageAvailability(grid, bodyOrigin, bodySize, cachedTotalWalkable))
            {
                best = cand;
                foundValidPath = true;
                break;
            }
        }

        if (!foundValidPath)
        {
            Debug.Log($"[AutoPlace] {item.furnitureId} 배치 실패: 통로 막힘");
            return false;
        }

        var bestClearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, best.rotation);
        Vector2Int finalBodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, best.rotation);
        Vector2Int finalBodyOrigin = new Vector2Int(best.origin.x + bestClearance.left, best.origin.y + bestClearance.bottom);
        Vector2Int finalBodyPivot = PlacementCalculator.ComputePivotCell(finalBodyOrigin, finalBodySize);

        if (!PlacementValidator.CheckAreaValid(grid, best.origin, best.sizeInCells)) return false;
        if (!PlacementValidator.CheckAreaValid(grid, finalBodyOrigin, finalBodySize)) return false;

        furnitureManager.PlaceItem(item.instanceId, roomID, finalBodyPivot, best.rotation);
        GridManipulator.MarkGridAsOccupied(grid, best.origin, best.sizeInCells, finalBodyOrigin, finalBodySize);
        if (updateVisuals) gridBuilder.BuildRuntimeGridVisuals(roomManager.currentRoomID);

        return true;
    }

    public void AutoPlaceAllUnplacedItems(int roomID)
    {
        // 1. 배치되지 않은 가구 목록 가져오기
        List<FurnitureItemData> unplacedItems = furnitureManager.GetUnplacedItemsInRoom(roomID);

        PlacementCalculator.ShuffleList(unplacedItems);

        var sortedItems = unplacedItems
            .OrderByDescending(item => PlacementCalculator.GetRequiredWallCount(item))
            .ToList();

        int successCount = 0;

        // 2. 루프 돌면서 하나씩 배치 시도
        foreach (var item in sortedItems)
        {
            bool result = TryAutoPlaceBasic(item, roomID, updateVisuals: false);

            if (result) successCount++;
        }

        Debug.Log($"[AutoPlace] Batch Complete. Success: {successCount} / {unplacedItems.Count}");

        // 3. 모든 배치가 끝난 후, 화면 한 번 갱신
        gridBuilder.BuildRuntimeGridVisuals(roomManager.currentRoomID);
    }

    private RoomPlacementGrid FindGridByRoomId(int roomID)
    {
        if (gridBuilder == null || gridBuilder.grids == null)
            return null;

        for (int i = 0; i < gridBuilder.grids.Count; i++)
        {
            if (gridBuilder.grids[i].roomID == roomID)
                return gridBuilder.grids[i];
        }
        return null;
    }
    public bool AutoPlaceOneItem(int roomID)
    {
        List<FurnitureItemData> unplaced = furnitureManager.GetUnplacedItemsInRoom(roomID);
        if (unplaced.Count == 0)
        {
            Debug.Log("No unplaced items!");
            return false;
        }

        FurnitureItemData item = unplaced[0];

        bool success = TryAutoPlaceBasic(item, roomID);
        return success;
    }

    public void UnplaceFurniture(string instanceId)
    {
        FurnitureItemData item = furnitureManager.GetItemByInstanceId(instanceId);

        if (item == null)
        {
            return;
        }

        RoomPlacementGrid grid = FindGridByRoomId(item.roomID);

        if (grid != null)
        {
            float cellSize = (grid.cellSize > 0) ? grid.cellSize : 0.1f;

            //  여유 공간을 포함한 전체 영역 역계산 

            // A. 가구 본체 크기 계산
            Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, item.rotation);

            // B. 가구 본체의 시작점 계산
            Vector2Int bodyOrigin = PlacementCalculator.ComputeOriginFromPivot(item.gridCell, bodySize);

            // C. 여유 공간 계산
            var clearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, item.rotation);

            // D. 전체 영역의 시작점 계산
            Vector2Int totalOrigin = new Vector2Int(
                bodyOrigin.x - clearance.left,
                bodyOrigin.y - clearance.bottom
            );

            // E. 전체 영역 크기 계산
            Vector2Int totalSize = new Vector2Int(
                clearance.left + bodySize.x + clearance.right,
                clearance.bottom + bodySize.y + clearance.top
            );

            // 3. 전체 영역에 대해 마스킹 해제 
            GridManipulator.UnmarkGrid(grid, totalOrigin, totalSize, bodyOrigin, bodySize);

            // 4. 화면 갱신
            gridBuilder.BuildRuntimeGridVisuals(item.roomID);
        }
        Debug.Log($"[FurniturePlacer] Restored grid for {instanceId} (Cleared Area: {item.furnitureId})");
    }
    
    public void RestoreGridForItem(FurnitureItemData item)
    {
        if (item == null)
        {
            return;
        }

        RoomPlacementGrid grid = FindGridByRoomId(item.roomID);
        if (grid == null)
        {
            return;
        }

        float cellSize = grid.cellSize;
        if (cellSize <= 0f)
        {
            cellSize = 0.1f;
        }

        // 1) 본체 크기
        Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(
            item.sizeCentimeters,
            cellSize,
            item.rotation
        );

        // 2) pivot-> 본체 origin
        Vector2Int bodyOrigin = PlacementCalculator.ComputeOriginFromPivot(
            item.gridCell,
            bodySize
        );

        // 3) 여유공간
        var clearance =
            PlacementCalculator.GetRotatedClearanceInCells(
                item,
                cellSize,
                item.rotation
            );

        // 4) 전체 영역
        Vector2Int totalOrigin = new Vector2Int(
            bodyOrigin.x - clearance.left,
            bodyOrigin.y - clearance.bottom
        );

        Vector2Int totalSize = new Vector2Int(
            clearance.left + bodySize.x + clearance.right,
            clearance.bottom + bodySize.y + clearance.top
        );

        // 5) 점유 마스킹
        GridManipulator.MarkGridAsOccupied(
            grid,
            totalOrigin,
            totalSize,
            bodyOrigin,
            bodySize
        );
    }
}
