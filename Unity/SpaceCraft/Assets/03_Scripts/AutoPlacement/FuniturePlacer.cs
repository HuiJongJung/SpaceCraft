using NUnit.Framework.Internal;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

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
    // 조건 무시 후 그리드 위에 배치 가능한지만 판단
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
            Debug.LogWarning("[FurniturePlacer] No grid found for roomID=" + roomID);
            return false;
        }
    
        float cellSize = grid.cellSize;
        if (cellSize <= 0f)
        {
            cellSize = cellSizeMeters;
        }
    
        // footprint 계산
        sizeInCells = PlacementCalculator.ComputeFootprintCells(
            item.sizeCentimeters,
            cellSize,
            rotationDeg
        );
    
        if (sizeInCells.x <= 0 || sizeInCells.y <= 0)
        {
            return false;
        }
    
        // 그리드 범위 + placementMask 검사
        for (int dz = 0; dz < sizeInCells.y; dz++)
        {
            for (int dx = 0; dx < sizeInCells.x; dx++)
            {
                int gx = originCell.x + dx;
                int gz = originCell.y + dz;
    
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
        int[] rotations = { 0, 90, 180, 270 }; // 필요시 셔플 추가

        List<PlacementCandidate> candidates = new List<PlacementCandidate>();

        for (int rIndex = 0; rIndex < rotations.Length; rIndex++)
        {
            int rot = rotations[rIndex];

            // 1. 가구 본체 크기 계산
            Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, rot);
            if (bodySize.x <= 0 || bodySize.y <= 0) continue;

            // 2. 회전된 여유 공간 계산 (Cell 단위)
            // (이미 추가하신 GetRotatedClearanceInCells 함수 사용)
            var clearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, rot);

            // 3. 전체 필요 영역(Total Footprint) 계산
            //    (왼쪽 여백 + 본체 + 오른쪽 여백, 아래 여백 + 본체 + 위쪽 여백)
            Vector2Int totalSize = new Vector2Int(
                clearance.left + bodySize.x + clearance.right,
                clearance.bottom + bodySize.y + clearance.top
            );

            // 4. 전체 영역 기준으로 탐색
            for (int gz = 0; gz < grid.rows; gz++)
            {
                for (int gx = 0; gx < grid.cols; gx++)
                {
                    Vector2Int totalOrigin = new Vector2Int(gx, gz);

                    // A. 전체 영역(가구+여유공간)이 빈 땅인지 검사
                    // (이미 추가하신 CheckAreaValid 함수 사용)
                    if (!PlacementValidator.CheckAreaValid(grid, totalOrigin, totalSize))
                        continue;

                    // B. 본체의 실제 위치(Origin) 계산
                    //    전체 시작점에서 왼쪽/아래 여백만큼 안으로 들어간 좌표
                    Vector2Int bodyOrigin = new Vector2Int(
                        totalOrigin.x + clearance.left,
                        totalOrigin.y + clearance.bottom
                    );

                    // C. 벽 배치 조건 검사 (가구 본체 기준 검사!)
                    int wallMatchCount = 0;
                    if (PlacementCalculator.IsWallPlacementRequired(item))
                    {
                        bool backOk = !item.wallDir.back || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "back");
                        bool frontOk = !item.wallDir.front || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "front");
                        bool leftOk = !item.wallDir.left || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "left");
                        bool rightOk = !item.wallDir.right || PlacementValidator.CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "right");

                        if (!backOk || !frontOk || !leftOk || !rightOk) continue;

                        // 점수 계산 (많이 붙을수록 좋음)
                        if (item.wallDir.back && backOk) wallMatchCount++;
                        if (item.wallDir.front && frontOk) wallMatchCount++;
                        if (item.wallDir.left && leftOk) wallMatchCount++;
                        if (item.wallDir.right && rightOk) wallMatchCount++;
                    }

                    // 후보 등록 (위치는 '전체 영역' 기준, 사이즈도 '전체 사이즈' 저장)
                    candidates.Add(new PlacementCandidate
                    {
                        origin = totalOrigin,
                        rotation = rot,
                        sizeInCells = totalSize
                        // (나중에 wallMatchCount도 구조체에 넣어서 정렬 가능)
                    });
                }
            }
        }

        // 후보가 없으면 실패
        if (candidates.Count == 0) return false;

        // 최적 후보 선정 (지금은 첫 번째, 나중엔 정렬)
        PlacementCandidate best = candidates[0];
        bool foundValidPath = false;

        foreach (var cand in candidates)
        {
            // 검사를 위해 본체 위치 재계산
            var clearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, cand.rotation);
            Vector2Int bodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, cand.rotation);
            Vector2Int bodyOrigin = new Vector2Int(cand.origin.x + clearance.left, cand.origin.y + clearance.bottom);

            //  통로 확보 검사 
            // 이 자리에 놨을 때 문에서 다른 빈칸으로 갈 수 있는지?
            if (PlacementPathFinder.CheckPassageAvailability(grid, bodyOrigin, bodySize))
            {
                best = cand;
                foundValidPath = true;
                break; // 통과했으면 확정
            }
        }

        if (!foundValidPath)
        {
            Debug.Log($"[AutoPlace] {item.furnitureId} 배치 실패: 놓을 자리는 있는데 길을 막습니다.");
            return false;
        }

        // --- 최종 배치 (좌표 보정) ---

        // 1. Pivot 계산을 위해 여유 공간/본체 크기 다시 계산
        var bestClearance = PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, best.rotation);
        Vector2Int finalBodySize = PlacementCalculator.ComputeFootprintCells(item.sizeCentimeters, cellSize, best.rotation);

        // 2. 본체 시작점 재계산 (전체 Origin + 여백)
        Vector2Int finalBodyOrigin = new Vector2Int(
            best.origin.x + bestClearance.left,
            best.origin.y + bestClearance.bottom
        );

        // 3. 실제 가구 생성 (본체 중심점 기준)
        Vector2Int finalBodyPivot = PlacementCalculator.ComputePivotCell(finalBodyOrigin, finalBodySize);
        furnitureManager.PlaceItem(item.instanceId, roomID, finalBodyPivot, best.rotation);

        // 4. 그리드 마스킹은 '전체 영역(Total Size)'을 덮어버림 (여유 공간 확보)
        GridManipulator.MarkGridAsOccupied(grid, best.origin, best.sizeInCells, finalBodyOrigin, finalBodySize);

        if (updateVisuals) gridBuilder.BuildRuntimeGridVisuals(roomManager.currentRoomID);

        return true;
    }

    public void AutoPlaceAllUnplacedItems(int roomID)
    {
        // 1. 배치되지 않은 가구 목록 가져오기
        List<FurnitureItemData> unplacedItems = furnitureManager.GetUnplacedItemsInRoom(roomID);

        // (옵션) 가구를 크기 순으로 정렬해서 큰 것부터 배치하면 성공률이 높음
        // unplacedItems.Sort((a, b) => (b.sizeCentimeters.x * b.sizeCentimeters.z).CompareTo(a.sizeCentimeters.x * a.sizeCentimeters.z));

        int successCount = 0;

        // 2. 루프 돌면서 하나씩 배치 시도
        foreach (var item in unplacedItems)
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
}
