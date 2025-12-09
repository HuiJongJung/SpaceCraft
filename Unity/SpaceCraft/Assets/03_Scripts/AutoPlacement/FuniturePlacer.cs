using NUnit.Framework.Internal;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using static UnityEngine.UI.GridLayoutGroup;

/// - RoomPlacementGridBuilder가 만든 방별 그리드를 이용해서
///   "가구를 어디에 놓을 수 있는지" 판단하고,
/// - 조건을 만족하는 위치를 찾으면 FurnitureManager를 통해 실제 배치까지 호출하는 스크립트.

public class FurniturePlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;
    [SerializeField] private FurnitureManager furnitureManager;
    public RoomManager roomManager;

    // 나중에 gridBuilder.cellSize를 직접 써도 되지만,
    // 필요하다면 여기서 override 할 수도 있게 빼둠.
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
#pragma warning disable CS0618 // 형식 또는 멤버는 사용되지 않습니다.
            roomManager = FindObjectOfType<RoomManager>();
#pragma warning restore CS0618 // 형식 또는 멤버는 사용되지 않습니다.

        // gridBuilder가 이미 Start에서 RebuildAll()을 돌렸다고 가정.
        // 필요한 경우 여기서도 RebuildAll()을 호출 가능.
    }
    
    
// 수동 배치용 배치 함수 
// originCell(본체 좌하단)을 기준으로
// "본체 + 여유공간(클리어런스)까지" 전부 placementMask 안에 들어가는지 검사
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

        // 1) 본체 footprint (셀 단위) 계산
        Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(
            item.sizeCentimeters,
            cellSize,
            rotationDeg
        );

        if (bodySize.x <= 0 || bodySize.y <= 0)
        {
            return false;
        }

        // 호출 쪽에서 ghost/프리뷰에 쓸 값은 "본체" 크기 그대로 넘겨줌
        sizeInCells = bodySize;

        // 2) 회전된 여유공간(클리어런스)을 셀 단위로 계산
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


    /// [필수조건용 기본 판정 함수]
    /// 주어진 방(roomID)의 그리드에서 originCell을 "시작 셀"로 했을 때,
    /// 해당 가구(FurnitureItemData)를 rotationDeg 방향으로 배치할 수 있는지 판정한다.
    ///
    /// - 가구의 sizeCentimeters + 회전을 고려해 셀 단위 footprint 계산
    /// - footprint에 포함되는 모든 셀에 대해:
    ///     · InBounds인지
    ///     · placementMask[gx, gz] == true 인지
    /// 를 확인한다.
    ///
    /// originCell은 "footprint의 좌측-하단 모서리를 가리키는 셀"이라고 가정.

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

        // 최소 1셀 이상은 가져야 함
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
                // ☆ 필요한 방향만 AND 조건으로 검사
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

        // 여기까지 통과하면: "기본적으로 이 위치에 둘 수 있다"
        return true;
    }

    /// [필수조건용 자동 배치]
    /// - 주어진 방(roomID)의 그리드를 하나 선택하고,
    /// - grid 전체를 스캔하면서,
    ///   CanPlaceBasic(...) == true 인 첫 위치를 찾아 자동 배치한다.
    ///
    /// 성공하면 true, 실패(놓을 데 없음)면 false.
    public bool TryAutoPlaceBasic(FurnitureItemData item, int roomID, bool updateVisuals = true)
    {
        RoomPlacementGrid grid = FindGridByRoomId(roomID);
        if (grid == null) return false;

        float cellSize = grid.cellSize > 0 ? grid.cellSize : cellSizeMeters;

        int[] rotations = { 0, 90, 180, 270 };
        PlacementCalculator.ShuffleArray(rotations);

        List<PlacementCandidate> candidates = new List<PlacementCandidate>();
        bool requireWall = PlacementCalculator.IsWallPlacementRequired(item);

        // 1. 후보 수집
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
                    // [버그 수정] 문제의 최적화 코드 삭제
                    // if (requireWall && ... !grid.wallZoneMask[gx, gz]) continue; 

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

                // 1.5m 이내면 벌점 부과 (값 조절 가능)
                if (minDoorDist < 15.0f)
                    doorPenalty = (15.0f - minDoorDist) * 1.0f;
            }

            return minCornerDist + doorPenalty;

        }).ToList();

        // [패자부활전] 상위권 먼저 시도 -> 실패 시 하위권 시도
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

        // --- 최종 배치 (기존 동일) ---
        var bestClearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, best.rotation);
        Vector2Int finalBodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, best.rotation);
        Vector2Int finalBodyOrigin = new Vector2Int(best.origin.x + bestClearance.left, best.origin.y + bestClearance.bottom);
        Vector2Int finalBodyPivot = PlacementCalculator.ComputePivotCell(finalBodyOrigin, finalBodySize);

        // 안전 검사
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
            // 여기서 updateVisuals = false로 줘서, 중간중간 화면 갱신을 안 하게 함 (성능 최적화)
            bool result = TryAutoPlaceBasic(item, roomID, updateVisuals: false);

            if (result) successCount++;
        }

        Debug.Log($"[AutoPlace] Batch Complete. Success: {successCount} / {unplacedItems.Count}");

        // 3. 모든 배치가 끝난 후, 화면을 딱 한 번만 갱신
        gridBuilder.BuildRuntimeGridVisuals(roomManager.currentRoomID);
    }

    /// 방 ID로 RoomPlacementGrid 찾기.
    /// gridBuilder.grids 리스트를 그대로 활용.

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

    /// 가구를 삭제하고, 차지하고 있던 그리드 영역을 다시 '사용 가능(초록색)'으로 복구합니다.
    public void UnplaceFurniture(string instanceId)
    {
        // 1. 데이터 조회 (이미 매니저에 의해 삭제 중일 수 있으므로 데이터만 참조)
        FurnitureItemData item = furnitureManager.GetItemByInstanceId(instanceId);

        if (item == null)
        {
            // 이미 삭제되었거나 데이터가 없으면 패스
            return;
        }

        // 2. 그리드 정보 가져오기
        RoomPlacementGrid grid = FindGridByRoomId(item.roomID);

        if (grid != null)
        {
            float cellSize = (grid.cellSize > 0) ? grid.cellSize : 0.1f;

            //  여유 공간을 포함한 전체 영역 역계산 

            // A. 가구 본체(Body) 크기 계산
            Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, item.rotation);

            // B. 가구 본체의 시작점(Origin) 계산
            //    (저장된 item.gridCell은 '본체 중심(Pivot)'이므로 역산해야 함)
            Vector2Int bodyOrigin = PlacementCalculator.ComputeOriginFromPivot(item.gridCell, bodySize);

            // C. 여유 공간(Clearance) 계산
            var clearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, item.rotation);

            // D. 전체 영역(Total)의 시작점 계산
            //    (본체 시작점에서 여유 공간만큼 왼쪽/아래로 더 이동)
            Vector2Int totalOrigin = new Vector2Int(
                bodyOrigin.x - clearance.left,
                bodyOrigin.y - clearance.bottom
            );

            // E. 전체 영역(Total) 크기 계산
            Vector2Int totalSize = new Vector2Int(
                clearance.left + bodySize.x + clearance.right,
                clearance.bottom + bodySize.y + clearance.top
            );

            // 3. 전체 영역에 대해 마스킹 해제 (빨강 -> 초록 복구)
            GridManipulator.UnmarkGrid(grid, totalOrigin, totalSize, bodyOrigin, bodySize);

            // 4. 화면 갱신
            gridBuilder.BuildRuntimeGridVisuals(item.roomID);
        }
        Debug.Log($"[FurniturePlacer] Restored grid for {instanceId} (Cleared Area: {item.furnitureId})");
    }
    
    // JSON Load 후 Item 배치에 사용
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

        // 2) pivot(=item.gridCell) -> 본체 origin
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
