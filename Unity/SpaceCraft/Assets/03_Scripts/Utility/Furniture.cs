using UnityEngine;

public class Furniture : MonoBehaviour
{
    [Header("Identity")]
    public string instanceId;
    public string furnitureId;
    
    [Header("Layout")]
    public int roomID = -1;
    public Vector2Int gridCell;
    public int rotation;
    
    [Header("Size (cm)")]
    public Vector3 sizeCentimeters;

    [Header("Options")]
    public bool isPrimaryFurniture = false;
    public FunctionalClearanceCm clearance;
    public bool isPrivacyFurniture = false;
    public PrivacyDirection privacyDir;
    
    [Header("SetSize Options")]
    public bool keepBottomOnScale = true;
    
    // width=X, depth=Z, height=Y (cm)
    public void SetSize(float width, float depth, float height, bool keepBot)
    {
        keepBottomOnScale = keepBot;
        
        const float CM_TO_M = 0.01f;
        const float MIN_AXIS_M = 0.01f; // 1cm: 너무 얇은 축 폭주 방지

        // 1) 스케일 전: 루트 축 기준 실제 치수(curX/Y/Z)와 월드 바닥(Y 최소) 구하기
        float curX;
        float curY;
        float curZ;
        float beforeBottomY;
        ComputeExtentsOnRootAxes(out curX, out curY, out curZ, out beforeBottomY);

        if (curX < MIN_AXIS_M) curX = MIN_AXIS_M;
        if (curY < MIN_AXIS_M) curY = MIN_AXIS_M;
        if (curZ < MIN_AXIS_M) curZ = MIN_AXIS_M;

        // 2) 목표 치수(미터)
        float targetX = width  * CM_TO_M;
        float targetY = height * CM_TO_M;
        float targetZ = depth  * CM_TO_M;

        // 3) 스케일 비율 계산 및 적용
        Vector3 baseScale = transform.localScale;
        Vector3 ratio = new Vector3(
            targetX / curX,
            targetY / curY,
            targetZ / curZ
        );
        transform.localScale = new Vector3(
            baseScale.x * ratio.x,
            baseScale.y * ratio.y,
            baseScale.z * ratio.z
        );

        // 4) 바닥 유지 옵션: 스케일 이후 다시 바닥 Y 계산 → 보정 이동
        if (keepBottomOnScale)
        {
            float afterBottomY = ComputeWorldBottomY();
            float deltaY = beforeBottomY - afterBottomY;
            if (Mathf.Abs(deltaY) > 1e-6f)
            {
                transform.position = transform.position + new Vector3(0f, deltaY, 0f);
            }
        }

        // 5) 읽기전용 cm 값 갱신
        sizeCentimeters = new Vector3(width, height, depth);
    }

    /// <summary>
    /// 모든 하위 Renderer의 localBounds 8코너를 “각 Renderer의 로컬→월드”로 변환한 뒤,
    /// 루트 축(transform.right/up/forward)에 투영하여 X/Y/Z 범위를 계산한다.
    /// 또한 월드 Y-최소값(바닥)을 함께 반환한다.
    /// </summary>
    private void ComputeExtentsOnRootAxes(out float sizeX, out float sizeY, out float sizeZ, out float bottomWorldY)
    {
        Transform root = transform;

        Vector3 rootRight = root.right.normalized;
        Vector3 rootUp    = root.up.normalized;
        Vector3 rootFwd   = root.forward.normalized;

        float minProjX = float.PositiveInfinity;
        float maxProjX = float.NegativeInfinity;
        float minProjY = float.PositiveInfinity;
        float maxProjY = float.NegativeInfinity;
        float minProjZ = float.PositiveInfinity;
        float maxProjZ = float.NegativeInfinity;

        float minWorldY = float.PositiveInfinity;

        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        int rendsCount = rends != null ? rends.Length : 0;
        if (rendsCount == 0)
        {
            // 렌더러가 없으면 1m 기준으로 안전값 반환
            sizeX = 1f;
            sizeY = 1f;
            sizeZ = 1f;
            bottomWorldY = root.position.y;
            return;
        }

        // 각 렌더러의 localBounds 코너 → 그 렌더러 Transform로 월드 변환
        for (int r = 0; r < rendsCount; r++)
        {
            Renderer rdr = rends[r];
            Bounds lb = rdr.localBounds; // 로컬 AABB
            Vector3[] corners = GetLocalBoundsCorners(lb);

            // 렌더러 로컬→월드
            Matrix4x4 l2w = rdr.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 worldP = l2w.MultiplyPoint3x4(corners[i]);

                // 루트 축에 투영
                float px = Vector3.Dot(worldP, rootRight);
                float py = Vector3.Dot(worldP, rootUp);
                float pz = Vector3.Dot(worldP, rootFwd);

                if (px < minProjX) minProjX = px;
                if (px > maxProjX) maxProjX = px;
                if (py < minProjY) minProjY = py;
                if (py > maxProjY) maxProjY = py;
                if (pz < minProjZ) minProjZ = pz;
                if (pz > maxProjZ) maxProjZ = pz;

                // 월드 바닥(Y)
                if (worldP.y < minWorldY) minWorldY = worldP.y;
            }
        }

        sizeX = maxProjX - minProjX;
        sizeY = maxProjY - minProjY;
        sizeZ = maxProjZ - minProjZ;
        bottomWorldY = minWorldY;
    }

    /// <summary>
    /// 현재 스케일/자세 상태에서 월드 바닥(Y-최소)만 다시 계산.
    /// </summary>
    private float ComputeWorldBottomY()
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        int rendsCount = rends != null ? rends.Length : 0;
        if (rendsCount == 0) return transform.position.y;

        float minWorldY = float.PositiveInfinity;

        for (int r = 0; r < rendsCount; r++)
        {
            Renderer rdr = rends[r];
            Bounds lb = rdr.localBounds;
            Vector3[] corners = GetLocalBoundsCorners(lb);
            Matrix4x4 l2w = rdr.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 worldP = l2w.MultiplyPoint3x4(corners[i]);
                if (worldP.y < minWorldY) minWorldY = worldP.y;
            }
        }
        return minWorldY;
    }

    /// <summary>
    /// Bounds(local)에서 8개 코너 좌표(로컬공간)를 반환.
    /// </summary>
    private static Vector3[] GetLocalBoundsCorners(Bounds localB)
    {
        Vector3 c = localB.center;
        Vector3 e = localB.extents;

        Vector3[] v = new Vector3[8];
        v[0] = new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
        v[1] = new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
        v[2] = new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
        v[3] = new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
        v[4] = new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
        v[5] = new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
        v[6] = new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
        v[7] = new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);
        return v;
    }
    
    // Debug
    [ContextMenu("Refresh Size From Children Renderers")]
    public void RefreshSizeFromChildrenRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning(name + " : Renderer가 없습니다.");
            return;
        }

        // 1) 자식 렌더러 전부 포함하는 Bounds 계산 (월드 기준)
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 sizeWorld = bounds.size;  // 유닛 단위 (1 = 1m 라고 보면 됨)
        float width = sizeWorld.x;
        float height = sizeWorld.y;
        float depth = sizeWorld.z;

        Debug.Log(name + " size (units)  W x H x D = "
                       + width + " x " + height + " x " + depth);
    }
}
