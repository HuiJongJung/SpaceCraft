using UnityEngine;

public class FurniturePlacementController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera cam;
    [SerializeField] private FurniturePlacer placer;
    [SerializeField] private FurnitureManager furnitureManager;
    [SerializeField] private RoomPlacementGridBuilder gridBuilder;

    [Header("Raycast")]
    [SerializeField] private LayerMask floorMask;

    [Header("Ghost Visual")]
    [SerializeField] private Color placeableColor = new Color(0f, 1f, 0f, 0.8f);
    [SerializeField] private Color blockedColor   = new Color(1f, 0f, 0f, 0.8f);
    
    // Preview
    private bool hasPreviewArea;
    private Vector2Int previewTotalOrigin;
    private Vector2Int previewTotalSize;
    private Vector2Int previewBodyOrigin;
    private Vector2Int previewBodySize;
    

    private FurnitureItemData currentItem;
    private int currentRoomID;
    private int currentRotDeg;

    private GameObject ghost;

    private Vector2Int currentOriginCell;
    private Vector2Int currentSizeCells;
    private Vector2Int currentPivotCell;
    private bool canPlaceHere;

    private bool isPlacing = false;
    
    // Clear Preview
    // not hasPreview -> return
    // has Preview -> UnmarkGrid
    private void ClearPreviewMask()
    {
        if (!hasPreviewArea)
        {
            return;
        }

        RoomPlacementGrid grid = FindGridByRoomId(currentRoomID);
        if (grid == null)
        {
            return;
        }

        GridManipulator.UnmarkGrid(grid, previewTotalOrigin, previewTotalSize, previewBodyOrigin, previewBodySize);
        hasPreviewArea = false;
    }

    // Called When Placement Start
    public void BeginPlacement(FurnitureItemData item, int roomID)
    {
        currentItem = item;
        currentRoomID = roomID;
        currentRotDeg = 0;

        FurnitureDefinition def = furnitureManager.GetDB().GetById(item.furnitureId);
        if (def == null)
        {
            Debug.LogError("BeginPlacement: FurnitureDefinition not found: " + item.furnitureId);
            return;
        }

        ghost = Instantiate(def.prefab);
        Furniture ghostFur = ghost.GetComponent<Furniture>();
        if (ghostFur != null)
        {
            ghostFur.SetSize(item.sizeCentimeters.x,item.sizeCentimeters.z,item.sizeCentimeters.y,true);
        }
        DisableColliders(ghost);
        SetGhostMaterial(ghost, placeableColor);

        isPlacing = true;
    }
    
    public void BeginRepositionExisting(FurnitureItemData item)
    {
        int roomID = item.roomID;

        RoomPlacementGrid grid = FindGridByRoomId(roomID);
        if (grid == null)
        {
            Debug.LogWarning("[BeginRepositionExisting] No grid for roomID=" + roomID);
            return;
        }
        
        // 1. UnplaceFurniture
        // Destroy Object & Update Grid
        furnitureManager.UnplaceItem(item.instanceId);

        // 2. BeginPlacement    
        // Assign Rotation
        BeginPlacement(item, roomID);
        currentRotDeg = item.rotation;
    }

    public void CancelPlacement()
    {
        currentItem = null;
        if (ghost != null)
        {
            Destroy(ghost);
        }
        ghost = null;
        isPlacing = false;
    }

    private void Update()
    {
        if (currentItem == null || ghost == null)
        {
            return;
        }

        // 회전
        if (Input.GetKeyDown(KeyCode.Q))
        {
            currentRotDeg = (currentRotDeg + 270) % 360;
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            currentRotDeg = (currentRotDeg + 90) % 360;
        }

        // 바닥 레이캐스트
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100f, floorMask))
        {
            UpdateGhostOnWorld(currentItem, hit.point);
        }

        // Left Click -> Place Item
        if (Input.GetMouseButtonDown(0))
        {
            if (canPlaceHere)
            {
                RoomPlacementGrid grid = FindGridByRoomId(currentRoomID);
                if (grid == null) return;

                float cellSize = (grid.cellSize > 0) ? grid.cellSize : 0.1f;

                // 1. 여유 공간 및 본체 영역 계산 (Calculator 사용)
                // (수동 배치는 마우스 위치가 Total Origin인지 Body Origin인지 정책에 따라 다름)
                // 현재 코드 흐름상 마우스 위치(currentOriginCell)를 '본체 위치'로 가정하고 배치합니다.

                // 1-1. body Size
                Vector2Int bodySize = currentSizeCells;
                Vector2Int bodyOrigin = currentOriginCell;

                // 1-2. Compute Clearance
                var clearance = PlacementCalculator.GetRotatedClearanceInCells(currentItem, cellSize, currentRotDeg);

                // 1-3. Compute Total Origin & Size
                Vector2Int totalOrigin = new Vector2Int(
                    bodyOrigin.x - clearance.left,
                    bodyOrigin.y - clearance.bottom
                );
                Vector2Int totalSize = new Vector2Int(
                    clearance.left + bodySize.x + clearance.right,
                    clearance.bottom + bodySize.y + clearance.top
                );

                // 2. Place Furniture
                furnitureManager.PlaceItem(
                    currentItem.instanceId,
                    currentRoomID,
                    currentPivotCell,
                    currentRotDeg
                );
                
                // 3. Update Grid Data
                GridManipulator.MarkGridAsOccupied(grid, totalOrigin, totalSize, bodyOrigin, bodySize);
                
                hasPreviewArea = false;

                // 4. Update Grid Visual
                gridBuilder.BuildRuntimeGridVisuals(currentRoomID);
                
                // 5. Cancel Place Mode
                CancelPlacement();
            }
        }

        // Right Click -> Cancel Place Mode
        if (Input.GetMouseButtonDown(1))
        {
            // 1. RollBack Preview Mask
            ClearPreviewMask();
            gridBuilder.BuildRuntimeGridVisuals(currentRoomID);
            // 2. Cancel Place Mode
            CancelPlacement();
        }
    }

    private void UpdateGhostOnWorld(FurnitureItemData item, Vector3 worldPos)
    {
        RoomPlacementGrid grid = FindGridByRoomId(currentRoomID);
        if (grid == null)
        {
            return;
        }
        
        float cellSize = grid.cellSize;
        if (cellSize <= 0f)
        {
            cellSize = 0.1f;
        }

        // 1) 이전 프리뷰 영역 되돌리기
        ClearPreviewMask();

        // 2) 마우스가 가리키는 셀을 "originCell(좌하단)"로 사용
        Vector2Int originCell = grid.WorldToGrid(worldPos);

        // 3) 이 origin에서 배치 가능한지 검사 + footprint 크기 얻기
        Vector2Int sizeInCells;
        bool ok = placer.CanPlaceOnGrid(item, currentRoomID, originCell, currentRotDeg, out sizeInCells);

        // 4) origin과 footprint로 pivotCell 계산
        Vector2Int pivotCell = ComputePivotCell(originCell, sizeInCells);

        // 5) 고스트 위치/회전 갱신 (pivotCell 중심에 둠)
        float y = worldPos.y;
        Vector3 centerWorld = grid.GridCenterToWorld(pivotCell.x, pivotCell.y, y);
        ghost.transform.position = centerWorld;
        ghost.transform.rotation = Quaternion.Euler(0f, currentRotDeg, 0f);
        
        // 6) 프리뷰용으로 "가짜 마스킹" (배치 가능할 때만)
        if (ok)
        {
            // 본체 영역
            Vector2Int bodySize = sizeInCells;
            Vector2Int bodyOrigin = originCell;

            // 여유 공간
            var (clearBottom, clearTop, clearLeft, clearRight) =
                PlacementCalculator.GetRotatedClearanceInCells(item, cellSize, currentRotDeg);
            // ↑ 타입 이름은 네 프로젝트에 맞게 (int left/right...) 있는 struct

            // 전체 영역(Total) 계산
            Vector2Int totalOrigin = new Vector2Int(
                bodyOrigin.x - clearLeft,
                bodyOrigin.y - clearBottom
            );

            Vector2Int totalSize = new Vector2Int(
                clearLeft + bodySize.x + clearRight,
                clearBottom + bodySize.y + clearTop
            );

            // Mask Preview Area
            GridManipulator.MarkGridAsOccupied(grid, totalOrigin, totalSize, bodyOrigin, bodySize);

            // Save to Rollback
            hasPreviewArea = true;
            previewTotalOrigin = totalOrigin;
            previewTotalSize = totalSize;
            previewBodyOrigin = bodyOrigin;
            previewBodySize = bodySize;
        }

        // 7) 그리드 비주얼 갱신 (새로운 마스크 상태 반영)
        gridBuilder.BuildRuntimeGridVisuals(currentRoomID);
        
        
        // 상태 저장
        canPlaceHere = ok;
        currentOriginCell = originCell;
        currentSizeCells = sizeInCells;
        currentPivotCell = pivotCell;

        UpdateGhostColor(ok);
    }

    private RoomPlacementGrid FindGridByRoomId(int roomID)
    {
        if (gridBuilder == null || gridBuilder.grids == null)
        {
            return null;
        }

        for (int i = 0; i < gridBuilder.grids.Count; i++)
        {
            if (gridBuilder.grids[i].roomID == roomID)
            {
                return gridBuilder.grids[i];
            }
        }
        return null;
    }

    // origin(좌하단) → pivot(중심셀) 변환
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

    private void DisableColliders(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Collider[] cols = root.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            cols[i].enabled = false;
        }
    }

    private void SetGhostMaterial(GameObject root, Color baseColor)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];

            Material newMat = new Material(r.sharedMaterial);
            newMat.color = baseColor;
            r.material = newMat;
        }
    }

    private void UpdateGhostColor(bool canPlace)
    {
        if (ghost == null)
        {
            return;
        }

        Color targetColor = canPlace ? placeableColor : blockedColor;

        Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            Material mat = r.material;
            Color c = mat.color;

            c.r = targetColor.r;
            c.g = targetColor.g;
            c.b = targetColor.b;
            c.a = targetColor.a;

            mat.color = c;
        }
    }
}
