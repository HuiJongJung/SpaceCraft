using NUnit.Framework.Internal;
using System.Collections.Generic;
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
            roomManager = FindObjectOfType<RoomManager>();

        // gridBuilder가 이미 Start에서 RebuildAll()을 돌렸다고 가정.
        // 필요한 경우 여기서도 RebuildAll()을 호출 가능.
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
        sizeInCells = ComputeFootprintCells(item.sizeCentimeters, cellSize, rotationDeg);

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

        if (IsWallPlacementRequired(item))
        {
            bool needBack = item.wallDir.back;
            bool needFront = item.wallDir.front;
            bool needLeft = item.wallDir.left;
            bool needRight = item.wallDir.right;

            bool requiresWall = needBack || needFront || needLeft || needRight;

            if (requiresWall)
            {
                // ☆ 필요한 방향만 AND 조건으로 검사
                if (needBack && !CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "back"))
                    return false;

                if (needFront && !CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "front"))
                    return false;

                if (needLeft && !CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "left"))
                    return false;

                if (needRight && !CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "right"))
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
            Vector2Int bodySize = ComputeFootprintCells(item.sizeCentimeters, cellSize, rot);
            if (bodySize.x <= 0 || bodySize.y <= 0) continue;

            // 2. 회전된 여유 공간 계산 (Cell 단위)
            // (이미 추가하신 GetRotatedClearanceInCells 함수 사용)
            var clearance = GetRotatedClearanceInCells(item, cellSize, rot);

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
                    if (!CheckAreaValid(grid, totalOrigin, totalSize))
                        continue;

                    // B. 본체의 실제 위치(Origin) 계산
                    //    전체 시작점에서 왼쪽/아래 여백만큼 안으로 들어간 좌표
                    Vector2Int bodyOrigin = new Vector2Int(
                        totalOrigin.x + clearance.left,
                        totalOrigin.y + clearance.bottom
                    );

                    // C. 벽 배치 조건 검사 (가구 본체 기준 검사!)
                    int wallMatchCount = 0;
                    if (IsWallPlacementRequired(item))
                    {
                        bool backOk = !item.wallDir.back || CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "back");
                        bool frontOk = !item.wallDir.front || CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "front");
                        bool leftOk = !item.wallDir.left || CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "left");
                        bool rightOk = !item.wallDir.right || CheckSideTouchingWall(grid, bodyOrigin, bodySize, rot, "right");

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

        // --- 최종 배치 (좌표 보정) ---

        // 1. Pivot 계산을 위해 여유 공간/본체 크기 다시 계산
        var bestClearance = GetRotatedClearanceInCells(item, cellSize, best.rotation);
        Vector2Int finalBodySize = ComputeFootprintCells(item.sizeCentimeters, cellSize, best.rotation);

        // 2. 본체 시작점 재계산 (전체 Origin + 여백)
        Vector2Int finalBodyOrigin = new Vector2Int(
            best.origin.x + bestClearance.left,
            best.origin.y + bestClearance.bottom
        );

        // 3. 실제 가구 생성 (본체 중심점 기준)
        Vector2Int finalBodyPivot = ComputePivotCell(finalBodyOrigin, finalBodySize);
        furnitureManager.PlaceItem(item.instanceId, roomID, finalBodyPivot, best.rotation);

        // 4. 그리드 마스킹은 '전체 영역(Total Size)'을 덮어버림 (여유 공간 확보)
        MarkGridAsOccupied(grid, best.origin, best.sizeInCells);

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

    /// 가구의 sizeCentimeters와 cellSize, rotation에 따라
    /// "가로 몇 셀 x 세로(깊이) 몇 셀을 차지하는지" 계산.
    /// - sizeCentimeters.x = width (가로, X)
    /// - sizeCentimeters.z = depth (세로, Z)
    /// - rotation 0/180:  width=X, depth=Z
    /// - rotation 90/270: width=Z, depth=X (회전으로 뒤바뀜)

    private Vector2Int ComputeFootprintCells(Vector3 sizeCentimeters, float cellSizeMeter, int rotationDeg)
    {
        const float CM_TO_M = 0.01f;

        float widthCm = sizeCentimeters.x;
        float depthCm = sizeCentimeters.z;

        int normalizedRot = Mathf.Abs(rotationDeg) % 360;
        if (normalizedRot == 90 || normalizedRot == 270)
        {
            // 90/270일 때 폭/깊이 스왑
            float tmp = widthCm;
            widthCm = depthCm;
            depthCm = tmp;
        }

        float widthM = widthCm * CM_TO_M;
        float depthM = depthCm * CM_TO_M;

        if (cellSizeMeter <= 0f) cellSizeMeter = 0.1f; // fallback

        int wCells = Mathf.CeilToInt(widthM / cellSizeMeter);
        int dCells = Mathf.CeilToInt(depthM / cellSizeMeter);

        if (wCells < 1) wCells = 1;
        if (dCells < 1) dCells = 1;

        return new Vector2Int(wCells, dCells);
    }

    /// originCell(좌측-하단)과 footprint 셀 크기(sizeInCells)를 기준으로
    /// 가구의 "중심 셀" 을 계산
    private Vector2Int ComputePivotCell(Vector2Int originCell, Vector2Int sizeInCells)
    {
        int offsetX = (sizeInCells.x - 1) / 2;
        int offsetZ = (sizeInCells.y - 1) / 2;

        Vector2Int pivot = new Vector2Int(
            originCell.x + offsetX,
            originCell.y + offsetZ
        );

        return pivot;
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

    /// 배치가 확정된 영역을 그리드에서 '사용 불가(false)'로 처리하고,
    /// '점유됨(occupied)' 상태로 마킹합니다.
    public void MarkGridAsOccupied(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size)
    {
        for (int dz = 0; dz < size.y; dz++)
        {
            for (int dx = 0; dx < size.x; dx++)
            {
                int gx = origin.x + dx;
                int gz = origin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    // 1. 로직용: 더 이상 이곳에 배치할 수 없도록 false 처리
                    grid.placementMask[gx, gz] = false;

                    // 2. 시각화용: 빨간색으로 표시하기 위해 true 처리
                    // (Builder에서 초기화했겠지만 안전하게 null 체크)
                    if (grid.occupiedMask != null)
                    {
                        grid.occupiedMask[gx, gz] = true;
                    }
                }
            }
        }
    }

    /// 해당 면의 모든 셀이 벽(또는 허공)과 접촉하고 있는지 검사합니다.
    /// 단 한 칸이라도 빈 공간(통로)과 접해있다면 실패로 간주합니다.
    private bool CheckSideTouchingWall(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size, int rot, string side)
    {
        string gridSide = GetGridSideByRotation(rot, side);
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
    private bool IsWallOrVoid(RoomPlacementGrid grid, int gx, int gz)
    {
        // 1. 그리드 밖이면 -> 무조건 벽(Void)
        if (!grid.InBounds(gx, gz)) return true;

        // 2. 만약 문(Door) 구역이라면? -> 벽이 아님 (통로/허공 취급)
        // 벽 배치 로직에서 "문에는 붙지 마라"는 의미가 됩니다.
        if (grid.doorMask != null && grid.doorMask[gx, gz]) return false;

        // 3. 그 외에 배치가 불가능한 곳(진짜 벽, 기둥 등) -> 벽
        if (!grid.placementMask[gx, gz]) return true;

        return false; // 빈 땅
    }


    /// rotationDeg(0/90/180/270)과 가구 로컬 방향("front/back/left/right")을 받아서
    /// 그리드 기준 방향("top/bottom/left/right") 문자열로 변환.
    private string GetGridSideByRotation(int rotationDeg, string side)
    {
        // 회전 후 가구의 forward / right 벡터 계산
        Quaternion q = Quaternion.Euler(0f, rotationDeg, 0f);
        Vector3 fwd = q * Vector3.forward; // 가구의 '앞'
        Vector3 right = q * Vector3.right;   // 가구의 '오른쪽'

        // side에 따라 어떤 방향 벡터를 쓸지 결정
        Vector3 dir;
        switch (side)
        {
            case "front":
                dir = fwd;
                break;
            case "back":
                dir = -fwd;
                break;
            case "left":
                dir = -right;
                break;
            case "right":
                dir = right;
                break;
            default:
                return "";
        }

        string closest = ClosestDir(dir);
        Debug.Log($"[GetGridSideByRotation] rotationDeg={rotationDeg}, side={side}, dir={dir}, closestGridSide={closest}");

        return ClosestDir(dir);
    }

    /// 벡터 dir이 XZ 평면에서 어떤 방향(+Z / -Z / +X / -X)에 가장 가까운지 보고
    /// "top/bottom/left/right" 중 하나를 리턴.
    private string ClosestDir(Vector3 dir)
    {
        Vector3 d = new Vector3(dir.x, 0f, dir.z).normalized;
        if (d.sqrMagnitude < 1e-6f)
            return "";

        float dotF = Vector3.Dot(d, Vector3.forward); // +Z
        float dotB = Vector3.Dot(d, Vector3.back);    // -Z
        float dotL = Vector3.Dot(d, Vector3.left);    // -X
        float dotR = Vector3.Dot(d, Vector3.right);   // +X

        float maxDot = dotF;
        string best = "top"; // +Z

        if (dotB > maxDot) { maxDot = dotB; best = "bottom"; }
        if (dotL > maxDot) { maxDot = dotL; best = "left"; }
        if (dotR > maxDot) { maxDot = dotR; best = "right"; }

        return best;
    }

    private bool IsWallPlacementRequired(FurnitureItemData item)
    {
        // 4방향 중 하나라도 true면 벽 배치 가구
        return item.wallDir.back || item.wallDir.front || item.wallDir.left || item.wallDir.right;
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
            Vector2Int bodySize = ComputeFootprintCells(item.sizeCentimeters, cellSize, item.rotation);

            // B. 가구 본체의 시작점(Origin) 계산
            //    (저장된 item.gridCell은 '본체 중심(Pivot)'이므로 역산해야 함)
            Vector2Int bodyOrigin = ComputeOriginFromPivot(item.gridCell, bodySize);

            // C. 여유 공간(Clearance) 계산
            var clearance = GetRotatedClearanceInCells(item, cellSize, item.rotation);

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
            UnmarkGrid(grid, totalOrigin, totalSize);

            // 4. 화면 갱신
            gridBuilder.BuildRuntimeGridVisuals(item.roomID);
        }
        Debug.Log($"[FurniturePlacer] Restored grid for {instanceId} (Cleared Area: {item.furnitureId})");
    }

    ///  점유된 그리드 영역을 다시 '배치 가능' 상태로 되돌립니다.
    private void UnmarkGrid(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size)
    {
        for (int dz = 0; dz < size.y; dz++)
        {
            for (int dx = 0; dx < size.x; dx++)
            {
                int gx = origin.x + dx;
                int gz = origin.y + dz;

                if (grid.InBounds(gx, gz))
                {
                    // 로직용: 다시 배치 가능하도록 true로 복구
                    grid.placementMask[gx, gz] = true;

                    // 시각화용: 점유 상태 해제 (빨간색 끄기)
                    if (grid.occupiedMask != null)
                        grid.occupiedMask[gx, gz] = false;
                }
            }
        }
    }

    // 중심점(Pivot)과 크기를 알 때, 왼쪽-아래(Origin) 좌표를 역계산하는 함수
    private Vector2Int ComputeOriginFromPivot(Vector2Int pivot, Vector2Int size)
    {
        int offsetX = (size.x - 1) / 2;
        int offsetZ = (size.y - 1) / 2;

        return new Vector2Int(pivot.x - offsetX, pivot.y - offsetZ);
    }

    // 회전된 상태에서의 상하좌우 여유 공간(Cell 단위)을 계산
    // 반환 순서: (Bottom, Top, Left, Right)
    private (int bottom, int top, int left, int right) GetRotatedClearanceInCells(FurnitureItemData item, float cellSize, int rot)
    {
        // 1. cm -> cell 변환 (100cm = 1m)
        // 예: 60cm / 10cm(0.1m) = 6칸
        int cFront = Mathf.CeilToInt(item.clearance.front * 0.01f / cellSize);
        int cBack = Mathf.CeilToInt(item.clearance.back * 0.01f / cellSize);
        int cLeft = Mathf.CeilToInt(item.clearance.left * 0.01f / cellSize);
        int cRight = Mathf.CeilToInt(item.clearance.right * 0.01f / cellSize);

        // 2. 회전 변환 (Grid 기준: Bottom(-Z), Top(+Z), Left(-X), Right(+X))
        int normRot = (rot % 360 + 360) % 360;

        // 0도: Front=Top(+Z), Back=Bottom(-Z), Left=Left(-X), Right=Right(+X)
        if (normRot == 0) return (cBack, cFront, cLeft, cRight);

        // 90도: Front=Right, Back=Left, Left=Top, Right=Bottom
        if (normRot == 90) return (cRight, cLeft, cBack, cFront);

        // 180도: Front=Bottom, Back=Top, Left=Right, Right=Left
        if (normRot == 180) return (cFront, cBack, cRight, cLeft);

        // 270도: Front=Left, Back=Right, Left=Bottom, Right=Top
        if (normRot == 270) return (cLeft, cRight, cFront, cBack);

        return (0, 0, 0, 0);
    }

    // 전체 영역이 유효한지(범위 안, 마스크 True) 검사하는 함수
    private bool CheckAreaValid(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size)
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
}
