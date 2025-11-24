using System.Collections.Generic;
using UnityEngine;

/// - RoomPlacementGridBuilder가 만든 방별 그리드를 이용해서
///   "가구를 어디에 놓을 수 있는지" 판단하고,
/// - 조건을 만족하는 위치를 찾으면 FurnitureManager를 통해 실제 배치까지 호출하는 스크립트.

public class FurniturePlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;
    [SerializeField] private FurnitureManager furnitureManager;

    // 나중에 gridBuilder.cellSize를 직접 써도 되지만,
    // 필요하다면 여기서 override 할 수도 있게 빼둠.
    [Header("Grid Settings")]
    [SerializeField] private float cellSizeMeters = 0.1f;   // 10cm

    private void Awake()
    {
        if (gridBuilder == null)
            Debug.LogError($"[FurniturePlacer] gridBuilder is not assigned!", this);

        if (furnitureManager == null)
            Debug.LogError($"[FurniturePlacer] furnitureManager is not assigned!", this);

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
            // 각 방향별로 조건이 만족되었는지 체크
            // (옵션이 꺼져있으면 true, 켜져있으면 해당 면이 벽 구역(WallZone)과 닿아야 true)

            bool backOk = !item.wallDir.back || CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "back");
            bool frontOk = !item.wallDir.front || CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "front");
            bool leftOk = !item.wallDir.left || CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "left");
            bool rightOk = !item.wallDir.right || CheckSideTouchingWall(grid, originCell, sizeInCells, rotationDeg, "right");

            // 하나라도 조건을 만족하지 못하면 실패
            if (!backOk || !frontOk || !leftOk || !rightOk)
                return false;
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

        float cellSize = grid.cellSize;
        if (cellSize <= 0f) cellSize = cellSizeMeters;

        int[] rotations = { 0, 90, 180, 270 };

        for (int rIndex = 0; rIndex < rotations.Length; rIndex++)
        {
            int rot = rotations[rIndex];
            Vector2Int sizeInCells = ComputeFootprintCells(item.sizeCentimeters, cellSize, rot);
            if (sizeInCells.x <= 0 || sizeInCells.y <= 0) continue;

            for (int gz = 0; gz < grid.rows; gz++)
            {
                for (int gx = 0; gx < grid.cols; gx++)
                {
                    Vector2Int origin = new Vector2Int(gx, gz);
                    Vector2Int dummySize;

                    if (!CanPlaceBasic(item, roomID, origin, rot, out dummySize))
                        continue;

                    // --- 배치 성공 ---
                    Vector3 worldPos = ComputeWorldPositionForFootprint(grid, origin, sizeInCells);

                    furnitureManager.PlaceFromInventory(
                        item.instanceId,
                        worldPos,
                        rot,
                        roomID,
                        origin
                    );

                    // 그리드 마스킹 (데이터 갱신)
                    MarkGridAsOccupied(grid, origin, sizeInCells);

                    // 플래그가 true일 때만 화면 갱신 (일괄 배치 시 false로 끄기 위함)
                    if (updateVisuals)
                    {
                        gridBuilder.BuildRuntimeGridVisuals();
                    }

                    return true;
                }
            }
        }
        return false;
    }

    public void AutoPlaceAllUnplacedItems(int roomID)
    {
        // 1. 배치되지 않은 가구 목록 가져오기
        List<FurnitureItemData> unplacedItems = furnitureManager.GetUnplacedItems();

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
        gridBuilder.BuildRuntimeGridVisuals();
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
    /// 가구의 "중심" 월드 좌표를 계산.
    ///
    /// - 눈으로 보기 좋게, footprint 중앙 셀의 center를 사용.
    private Vector3 _debugLastPos;
    private Vector3 _debugLastSize;
    private bool _hasDebugInfo = false;
    private Vector3 ComputeWorldPositionForFootprint(RoomPlacementGrid grid,
                                                Vector2Int originCell,
                                                Vector2Int sizeInCells)
    {
        // footprint의 월드 크기(폭/깊이)
        float wWorld = sizeInCells.x * grid.cellSize;   // width in meters
        float dWorld = sizeInCells.y * grid.cellSize;   // depth in meters

        // footprint의 "왼쪽-아래 모서리" 기준 world 좌표
        Vector3 cornerWorld = grid.GridCenterToWorld(originCell.x, originCell.y, 0f);
        cornerWorld.x -= grid.cellSize / 2f;
        cornerWorld.z -= grid.cellSize / 2f;

        // footprint 중심 좌표 = cornerWorld + (폭/2, 깊이/2)
        Vector3 centerWorld = cornerWorld + new Vector3(wWorld / 2f, 0f, dWorld / 2f);

        _debugLastPos = centerWorld;
        _debugLastSize = new Vector3(wWorld, 1.0f, dWorld); // 높이는 대충 1m
        _hasDebugInfo = true;

        return centerWorld;
    }

    public bool AutoPlaceOneItem(int roomID)
    {
        List<FurnitureItemData> unplaced = furnitureManager.GetUnplacedItems();
        if (unplaced.Count == 0)
        {
            Debug.Log("No unplaced items!");
            return false;
        }

        FurnitureItemData item = unplaced[0];

        bool success = TryAutoPlaceBasic(item, roomID);
        return success;
    }

    private void OnDrawGizmos()
    {
        if (_hasDebugInfo)
        {
            Gizmos.color = Color.red;
            // 코드가 생각하는 "가구가 있어야 할 자리"를 와이어 큐브로 그림
            Gizmos.DrawWireCube(_debugLastPos, _debugLastSize);

            // 중심점 표시
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_debugLastPos, 0.1f);
        }
    }

    /// 배치가 확정된 영역을 그리드에서 '사용 불가(false)'로 처리하고,
    /// '점유됨(occupied)' 상태로 마킹합니다.
    private void MarkGridAsOccupied(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size)
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
    /// <summary>
    /// 가구의 특정 면(side)이 현재 회전각도에서 그리드의 벽 영역(wallZoneMask)과 접촉하고 있는지 검사
    /// side: "back", "front", "left", "right"
    /// </summary>
    private bool CheckSideTouchingWall(RoomPlacementGrid grid, Vector2Int origin, Vector2Int size, int rot, string side)
    {
        // 1. 현재 회전각도에서 '가구의 side'가 '그리드의 어느 방향'을 향하는지 매핑
        // 그리드 기준: Bottom(Z=0쪽), Top(Z=Max쪽), Left(X=0쪽), Right(X=Max쪽)

        string gridSide = "";
        int normRot = (rot % 360 + 360) % 360; // 0~360 정규화

        if (side == "back")
        {
            if (normRot == 0) gridSide = "bottom";       // 0도일 때 뒤쪽은 아래(-Z)
            else if (normRot == 90) gridSide = "left";   // 90도 돌면 뒤쪽은 왼쪽(-X)
            else if (normRot == 180) gridSide = "top";   // 180도 돌면 뒤쪽은 위(+Z)
            else if (normRot == 270) gridSide = "right"; // 270도 돌면 뒤쪽은 오른쪽(+X)
        }
        else if (side == "front")
        {
            if (normRot == 0) gridSide = "top";
            else if (normRot == 90) gridSide = "right";
            else if (normRot == 180) gridSide = "bottom";
            else if (normRot == 270) gridSide = "left";
        }
        else if (side == "left")
        {
            if (normRot == 0) gridSide = "left";
            else if (normRot == 90) gridSide = "top";
            else if (normRot == 180) gridSide = "right";
            else if (normRot == 270) gridSide = "bottom";
        }
        else if (side == "right")
        {
            if (normRot == 0) gridSide = "right";
            else if (normRot == 90) gridSide = "bottom";
            else if (normRot == 180) gridSide = "left";
            else if (normRot == 270) gridSide = "top";
        }

        // 2. 매핑된 그리드 면의 셀들 중 하나라도 WallZone인지 검사

        if (gridSide == "bottom") // 아랫면(Z=0 라인) 검사
        {
            for (int dx = 0; dx < size.x; dx++)
                if (IsWallZone(grid, origin.x + dx, origin.y)) return true;
        }
        else if (gridSide == "top") // 윗면(Z=Max 라인) 검사
        {
            for (int dx = 0; dx < size.x; dx++)
                if (IsWallZone(grid, origin.x + dx, origin.y + size.y - 1)) return true;
        }
        else if (gridSide == "left") // 왼쪽면(X=0 라인) 검사
        {
            for (int dz = 0; dz < size.y; dz++)
                if (IsWallZone(grid, origin.x, origin.y + dz)) return true;
        }
        else if (gridSide == "right") // 오른쪽면(X=Max 라인) 검사
        {
            for (int dz = 0; dz < size.y; dz++)
                if (IsWallZone(grid, origin.x + size.x - 1, origin.y + dz)) return true;
        }

        return false;
    }

    private bool IsWallZone(RoomPlacementGrid grid, int gx, int gz)
    {
        if (!grid.InBounds(gx, gz)) return false;
        // wallZoneMask가 생성되어 있고 true라면 벽 인접 구역임
        return (grid.wallZoneMask != null && grid.wallZoneMask[gx, gz]);
    }

    private bool IsWallPlacementRequired(FurnitureItemData item)
    {
        // 4방향 중 하나라도 true면 벽 배치 가구
        return item.wallDir.back || item.wallDir.front || item.wallDir.left || item.wallDir.right;
    }

}
