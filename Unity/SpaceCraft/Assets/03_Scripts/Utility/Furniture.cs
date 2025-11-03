using UnityEngine;

[DisallowMultipleComponent]
public class Furniture : MonoBehaviour
{
    [Header("Read-only (cm)")]
    public Vector3 sizeCentimeters;

    [Header("Options")]
    public bool keepBottomOnScale = true;

    // width=X, depth=Z, height=Y (cm)
    // width = X, depth = Z, height = Y  (cm)
    public void SetSize(float width, float depth, float height)
    {
        const float CM_TO_M = 0.01f;
        const float MIN_AXIS_M = 0.01f; // 1cm: 너무 얇은 축 폭주 방지

        // ----- 스케일 전: 바닥 Y 기록(옵션) -----
        float beforeBottomY = 0f;
        if (keepBottomOnScale)
        {
            Renderer[] r0 = GetComponentsInChildren<Renderer>(true);
            if (r0 != null && r0.Length > 0)
            {
                Bounds b = r0[0].bounds;
                for (int i = 1; i < r0.Length; i++) b.Encapsulate(r0[i].bounds);
                beforeBottomY = b.min.y;
            }
        }

        // ----- 현재 크기(로컬 축 기준) 측정 -----
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            Debug.LogWarning("[Furniture] Renderer not found.", this);
            return;
        }
        Bounds worldB = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) worldB.Encapsulate(rends[i].bounds);

        Vector3 axisX = transform.right.normalized;
        Vector3 axisY = transform.up.normalized;
        Vector3 axisZ = transform.forward.normalized;

        Vector3 c = worldB.center;
        Vector3 e = worldB.extents;

        Vector3[] corner = new Vector3[8];
        corner[0] = c + new Vector3( e.x,  e.y,  e.z);
        corner[1] = c + new Vector3( e.x,  e.y, -e.z);
        corner[2] = c + new Vector3( e.x, -e.y,  e.z);
        corner[3] = c + new Vector3( e.x, -e.y, -e.z);
        corner[4] = c + new Vector3(-e.x,  e.y,  e.z);
        corner[5] = c + new Vector3(-e.x,  e.y, -e.z);
        corner[6] = c + new Vector3(-e.x, -e.y,  e.z);
        corner[7] = c + new Vector3(-e.x, -e.y, -e.z);

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

        for (int i = 0; i < 8; i++)
        {
            float dx = Vector3.Dot(corner[i], axisX);
            float dy = Vector3.Dot(corner[i], axisY);
            float dz = Vector3.Dot(corner[i], axisZ);
            if (dx < minX) minX = dx; if (dx > maxX) maxX = dx;
            if (dy < minY) minY = dy; if (dy > maxY) maxY = dy;
            if (dz < minZ) minZ = dz; if (dz > maxZ) maxZ = dz;
        }

        float curX = Mathf.Max(maxX - minX, MIN_AXIS_M); // m
        float curY = Mathf.Max(maxY - minY, MIN_AXIS_M); // m
        float curZ = Mathf.Max(maxZ - minZ, MIN_AXIS_M); // m

        // ----- 목표(m) -----
        float targetX = width  * CM_TO_M;
        float targetY = height * CM_TO_M;
        float targetZ = depth  * CM_TO_M;

        // ----- 스케일 적용 -----
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

        // ----- 스케일 후: 바닥 Y 복원(옵션) -----
        if (keepBottomOnScale)
        {
            Renderer[] r1 = GetComponentsInChildren<Renderer>(true);
            if (r1 != null && r1.Length > 0)
            {
                Bounds b1 = r1[0].bounds;
                for (int i = 1; i < r1.Length; i++) b1.Encapsulate(r1[i].bounds);
                float afterBottomY = b1.min.y;
                float deltaY = beforeBottomY - afterBottomY;
                if (Mathf.Abs(deltaY) > 1e-6f)
                {
                    transform.position += new Vector3(0f, deltaY, 0f);
                }
            }
        }

        // ----- 읽기전용 cm 값 즉시 갱신 -----
        sizeCentimeters = new Vector3(width, height, depth);
    }

    
    // --- Helpers: 로컬 공간 바운즈 계산 (회전 무관) ---
    private static void CalculateBoundsInLocalSpace(Transform root, out Bounds localBounds)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);

        bool hasAny = false;
        localBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < rends.Length; i++)
        {
            Bounds w = rends[i].bounds; // 월드 AABB
            EncapsulateWorldAABBIntoLocal(ref localBounds, root, w, ref hasAny);
        }

        if (!hasAny)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
        }
    }

    private static void EncapsulateWorldAABBIntoLocal(ref Bounds localBounds, Transform root, Bounds worldAABB, ref bool hasAny)
    {
        Vector3 min = worldAABB.min;
        Vector3 max = worldAABB.max;

        Vector3 c000 = root.InverseTransformPoint(new Vector3(min.x, min.y, min.z));
        Vector3 c001 = root.InverseTransformPoint(new Vector3(min.x, min.y, max.z));
        Vector3 c010 = root.InverseTransformPoint(new Vector3(min.x, max.y, min.z));
        Vector3 c011 = root.InverseTransformPoint(new Vector3(min.x, max.y, max.z));
        Vector3 c100 = root.InverseTransformPoint(new Vector3(max.x, min.y, min.z));
        Vector3 c101 = root.InverseTransformPoint(new Vector3(max.x, min.y, max.z));
        Vector3 c110 = root.InverseTransformPoint(new Vector3(max.x, max.y, min.z));
        Vector3 c111 = root.InverseTransformPoint(new Vector3(max.x, max.y, max.z));

        if (!hasAny)
        {
            localBounds = new Bounds(c000, Vector3.zero);
            hasAny = true;
        }

        localBounds.Encapsulate(c001);
        localBounds.Encapsulate(c010);
        localBounds.Encapsulate(c011);
        localBounds.Encapsulate(c100);
        localBounds.Encapsulate(c101);
        localBounds.Encapsulate(c110);
        localBounds.Encapsulate(c111);
    }
}
