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

    private FurnitureItemData currentItem;
    private int currentRoomID;
    private int currentRotDeg;

    private GameObject ghost;

    private Vector2Int currentOriginCell;
    private Vector2Int currentSizeCells;
    private Vector2Int currentPivotCell;
    private bool canPlaceHere;

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
    }

    public void CancelPlacement()
    {
        currentItem = null;
        if (ghost != null)
        {
            Destroy(ghost);
        }
        ghost = null;
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

        // 좌클릭 배치
        if (Input.GetMouseButtonDown(0))
        {
            if (canPlaceHere)
            {
                RoomPlacementGrid grid = FindGridByRoomId(currentRoomID);

                furnitureManager.PlaceItem(
                    currentItem.instanceId,
                    currentRoomID,
                    currentPivotCell,     // 최종 배치는 pivotCell
                    currentRotDeg
                );

                if (grid != null)
                {
                    // originCell 기준으로 점유 표시
                    for (int dz = 0; dz < currentSizeCells.y; dz++)
                    {
                        for (int dx = 0; dx < currentSizeCells.x; dx++)
                        {
                            int gx = currentOriginCell.x + dx;
                            int gz = currentOriginCell.y + dz;

                            if (grid.InBounds(gx, gz))
                            {
                                grid.placementMask[gx, gz] = false;
                                if (grid.occupiedMask != null)
                                {
                                    grid.occupiedMask[gx, gz] = true;
                                }
                            }
                        }
                    }

                    gridBuilder.BuildRuntimeGridVisuals(currentRoomID);
                }

                CancelPlacement();
            }
        }

        // 우클릭 취소
        if (Input.GetMouseButtonDown(1))
        {
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

        // 1) 마우스가 가리키는 셀을 "originCell(좌하단)"로 사용
        Vector2Int originCell = grid.WorldToGrid(worldPos);

        // 2) 이 origin에서 배치 가능한지 검사 + footprint 크기 얻기
        Vector2Int sizeInCells;
        bool ok = placer.CanPlaceBasic(item, currentRoomID, originCell, currentRotDeg, out sizeInCells);

        // 3) origin과 footprint로 pivotCell 계산
        Vector2Int pivotCell = ComputePivotCell(originCell, sizeInCells);

        // 4) 고스트 위치/회전 갱신 (pivotCell 중심에 둠)
        float y = worldPos.y;
        Vector3 centerWorld = grid.GridCenterToWorld(pivotCell.x, pivotCell.y, y);
        ghost.transform.position = centerWorld;
        ghost.transform.rotation = Quaternion.Euler(0f, currentRotDeg, 0f);

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
