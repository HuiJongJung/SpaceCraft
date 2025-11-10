using System.Collections.Generic;
using UnityEngine;

public class RoomPlacementGridBuilder : MonoBehaviour
{
#if UNITY_EDITOR
    private List<Vector3[]> _debugSwingRects = new List<Vector3[]>();
#endif

    [Header("References")]
    [SerializeField] private SpaceData data;   // 비워두면 자동으로 SpaceData.Instance 사용

    [Header("Grid Options")]
    [SerializeField, Min(0.01f)] private float cellSize = 0.1f; // 10cm

    [Header("Debug View")]
    public bool drawGizmos = true;
    [Range(0f, 1f)] public float gizmoAlpha = 0.35f;
    //public Color[] roomColors; // 방마다 다른 색(비워두면 자동 생성)

    [Header("Door Swing Options")]
    [SerializeField] private float wallInset = 0.02f; // 벽면에서 살짝 안쪽(수치오차/겹침 방지)

    // 결과물: 방별 그리드
    public List<RoomPlacementGrid> grids = new List<RoomPlacementGrid>();

    void Start()
    {
        if (data == null) data = SpaceData.Instance;
        RebuildAll();
    }

    public void RebuildAll()
    {
#if UNITY_EDITOR
        _debugSwingRects.Clear();
#endif

        grids.Clear();

        if (data == null || data._layout == null || data._layout.rooms == null)
        {
            Debug.LogWarning("[RoomPlacementGridBuilder] No layout/rooms.");
            return;
        }

        foreach (RoomDef room in data._layout.rooms)
        {
            // 1) 방 바닥 폴리곤(XZ) 추출
            List<Vector2> poly = ExtractRoomPolygonXZ(room, data._layout);
            if (poly == null || poly.Count < 3)
            {
                Debug.LogWarning("[Grid] Room " + room.roomID + " has invalid polygon.");
                continue;
            }

            // 2) 바운딩 박스
            float minX, maxX, minZ, maxZ;
            Bounds2D(poly, out minX, out maxX, out minZ, out maxZ);

            int cols = Mathf.CeilToInt((maxX - minX) / cellSize);
            int rows = Mathf.CeilToInt((maxZ - minZ) / cellSize);
            if (cols <= 0 || rows <= 0) continue;

            // 3) 그리드 생성 + 내부 판정 (Point-In-Polygon)
            RoomPlacementGrid grid = new RoomPlacementGrid
            {
                roomID = room.roomID,
                roomName = room.name,
                originXZ = new Vector2(minX, minZ),
                cellSize = cellSize,
                cols = cols,
                rows = rows,
                placementMask = new bool[cols, rows]
            };

            for (int gz = 0; gz < rows; gz++)
            {
                float cz = minZ + (gz + 0.5f) * cellSize;
                for (int gx = 0; gx < cols; gx++)
                {
                    float cx = minX + (gx + 0.5f) * cellSize;
                    bool inside = PointInPolygon(cx, cz, poly);
                    // 경계는 안쪽으로 취급(안전)
                    grid.placementMask[gx, gz] = inside;
                }
            }

            grids.Add(grid);
        }

        ApplyDoorSwingZones(); // 문이 열리는 사각형 영역만 배치 불가 처리
        Debug.Log("[RoomPlacementGridBuilder] Built " + grids.Count + " room grids with door swing mask.");
    }

    // ===== 폴리곤 추출 =====
    // 가정: FloorDef.vertices가 상단면 윤곽을 이룸 (JSON 예시처럼 순서가 외곽 경로)
    // 안전하게 처리하기 위해 indices에서 경계 에지를 찾아 루프를 구성
    private List<Vector2> ExtractRoomPolygonXZ(RoomDef room, SpaceLayout layout)
    {
        if (room.floorIDs == null || room.floorIDs.Count == 0) return null;

        // 단일 플로어만 있다고 가정(현재 JSON 구조와 동일). 다수일 경우 합집합 필요.
        FloorDef fd = layout.floors.Find(f => f != null && f.id == room.floorIDs[0]);
        if (fd == null || fd.vertices == null || fd.vertices.Count < 3 || fd.indices == null || fd.indices.Count < 3)
            return null;

        // 1) 경계 에지 수집 (무향)
        Dictionary<(int, int), (int a, int b, int cnt)> edgeCount =
            new Dictionary<(int, int), (int a, int b, int cnt)>();
        for (int i = 0; i + 2 < fd.indices.Count; i += 3)
        {
            int i0 = fd.indices[i + 0];
            int i1 = fd.indices[i + 1];
            int i2 = fd.indices[i + 2];
            AddUndirectedEdge(i0, i1, edgeCount);
            AddUndirectedEdge(i1, i2, edgeCount);
            AddUndirectedEdge(i2, i0, edgeCount);
        }

        // 2) 경계(한 번만 등장한) 에지들로 루프 재구성
        Dictionary<int, List<int>> boundary = new Dictionary<int, List<int>>(); // from -> list of to
        foreach ((int a, int b, int cnt) rec in edgeCount.Values)
        {
            if (rec.cnt == 1)
            {
                List<int> listA;
                if (!boundary.TryGetValue(rec.a, out listA)) { listA = new List<int>(); boundary[rec.a] = listA; }
                listA.Add(rec.b);

                List<int> listB;
                if (!boundary.TryGetValue(rec.b, out listB)) { listB = new List<int>(); boundary[rec.b] = listB; }
                listB.Add(rec.a);
            }
        }

        // 3) 가장 긴 루프를 외곽으로 간주 (홀이 없다는 가정에 부합)
        List<int> loop = TraceLongestLoop(boundary);
        if (loop == null || loop.Count < 3) return null;

        // 4) 2D(XZ)로 투영
        List<Vector2> poly = new List<Vector2>(loop.Count);
        for (int k = 0; k < loop.Count; k++)
        {
            Vec3 v = fd.vertices[loop[k]];
            poly.Add(new Vector2(v.x, v.z));
        }
        return poly;
    }

    private void AddUndirectedEdge(int a, int b, Dictionary<(int, int), (int a, int b, int cnt)> map)
    {
        if (a == b) return;
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        (int, int) key = (min, max);

        (int a, int b, int cnt) rec;
        if (!map.TryGetValue(key, out rec)) rec = (a, b, 0);
        rec.cnt += 1;
        // a,b는 최초 방향 보존(대략적인 루프 복원에 도움)
        map[key] = rec;
    }

    private List<int> TraceLongestLoop(Dictionary<int, List<int>> graph)
    {
        List<int> best = new List<int>();
        HashSet<(int from, int cur)> visited = new HashSet<(int from, int cur)>();

        foreach (int start in graph.Keys)
        {
            DFS(start, -1, start, new List<int>(), visited, graph, ref best, 0);
        }
        return best;
    }

    private void DFS(int cur, int parent, int start, List<int> path,
                     HashSet<(int, int)> visited, Dictionary<int, List<int>> g,
                     ref List<int> best, int depth)
    {
        path.Add(cur);
        List<int> nbrs;
        if (g.TryGetValue(cur, out nbrs))
        {
            for (int i = 0; i < nbrs.Count; i++)
            {
                int nxt = nbrs[i];
                if (nxt == parent) continue;
                (int, int) e = (cur, nxt);
                if (visited.Contains(e)) continue;

                if (nxt == start && path.Count >= 3)
                {
                    if (path.Count > best.Count) best = new List<int>(path);
                }
                else
                {
                    visited.Add(e);
                    DFS(nxt, cur, start, path, visited, g, ref best, depth + 1);
                    visited.Remove(e);
                }
            }
        }
        path.RemoveAt(path.Count - 1);
    }

    // ===== 기초 유틸 =====
    private void Bounds2D(List<Vector2> poly, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = poly[0].x;
        maxX = poly[0].x;
        minZ = poly[0].y;
        maxZ = poly[0].y;

        for (int i = 1; i < poly.Count; i++)
        {
            Vector2 p = poly[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minZ) minZ = p.y;
            if (p.y > maxZ) maxZ = p.y;
        }
    }

    // 홀짝 규칙 PIP (시계/반시계/오목 모두 OK)
    private bool PointInPolygon(float x, float z, List<Vector2> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i].x, zi = poly[i].y;
            float xj = poly[j].x, zj = poly[j].y;

            bool intersect = ((zi > z) != (zj > z)) &&
                             (x < (xj - xi) * (z - zi) / ((zj - zi) + Mathf.Epsilon) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    // 문 스윙 영역(문이 열리는 쪽 한쪽만) 적용
    private void ApplyDoorSwingZones()
    {
        if (data == null || data._layout == null) return;
        if (data._layout.openings == null || data._layout.walls == null || data._layout.rooms == null) return;

#if UNITY_EDITOR
        // 디버그 와이어 누적 방지
        _debugSwingRects.Clear();
#endif

        // 방별 폴리곤 캐시(방 내부 판정용)
        Dictionary<int, List<Vector2>> roomPoly = new Dictionary<int, List<Vector2>>();
        for (int i = 0; i < grids.Count; i++)
        {
            RoomPlacementGrid g = grids[i];
            RoomDef rm = data._layout.rooms.Find(r => r != null && r.roomID == g.roomID);
            if (rm == null) continue;
            List<Vector2> poly = ExtractRoomPolygonXZ(rm, data._layout);
            if (poly != null) roomPoly[g.roomID] = poly;
        }

        // 모든 개구부 중 문만 처리
        for (int oi = 0; oi < data._layout.openings.Count; oi++)
        {
            OpeningDef od = data._layout.openings[oi];
            if (od == null || od.type == null) continue;
            string typ = od.type.ToLower();
            if (typ != "door") continue;

            // 벽 찾기
            WallDef wd = data._layout.walls.Find(w => w != null && w.id == od.wallID);
            if (wd == null || wd.vertices == null || wd.vertices.Count < 4) continue;

            // 벽 좌표계(u: 긴 변, t: 두께)
            Vector3 p0 = new Vector3(wd.vertices[0].x, wd.vertices[0].y, wd.vertices[0].z);
            Vector3 p1 = new Vector3(wd.vertices[1].x, wd.vertices[1].y, wd.vertices[1].z);
            Vector3 p3 = new Vector3(wd.vertices[3].x, wd.vertices[3].y, wd.vertices[3].z);

            Vector3 e1 = p1 - p0; float len1 = e1.magnitude;
            Vector3 e3 = p3 - p0; float len3 = e3.magnitude;
            if (len1 <= 1e-6f || len3 <= 1e-6f) continue;

            Vector3 uDir, tDir; float uLen, tLen;
            if (len1 >= len3) { uDir = e1 / len1; tDir = e3 / len3; uLen = len1; tLen = len3; }
            else { uDir = e3 / len3; tDir = e1 / len1; uLen = len3; tLen = len1; }

            // 도어 중심/힌지
            Vector3 centerW = new Vector3(od.center.x, od.center.y, od.center.z);
            Vector3 hingeW = new Vector3(od.hingePos.x, od.hingePos.y, od.hingePos.z);

            // 힌지 u좌표, 열림 u부호 (문패널의 u방향)
            float hingeU = Vector3.Dot(hingeW - p0, uDir);
            float signU = Vector3.Dot(centerW - hingeW, uDir) >= 0.0f ? 1.0f : -1.0f;
            float doorWidth = Mathf.Max(od.width, 0.0f);

            // 힌지↔센터의 t 성분 부호로 "여는 t 방향" 확정
            float signT = Vector3.Dot(centerW - hingeW, tDir);
            Vector3 intoDirBase = (signT >= 0f) ? tDir : -tDir;

            // 이 벽을 포함하는 방들 중 "intoDirBase 방향에 실제로 위치하는" 방만 처리
            for (int gi = 0; gi < grids.Count; gi++)
            {
                RoomPlacementGrid g = grids[gi];
                RoomDef rm = data._layout.rooms.Find(r => r != null && r.roomID == g.roomID);
                if (rm == null || rm.wallIDs == null) continue;
                if (!rm.wallIDs.Contains(od.wallID)) continue;

                List<Vector2> poly;
                if (!roomPoly.TryGetValue(g.roomID, out poly) || poly == null) continue;

                Vector3 probe = centerW + intoDirBase * 0.12f; // 12cm 정도, cellSize(0.1m)보다 살짝 큼
                bool inThisRoom = PointInPolygon(probe.x, probe.z, poly);
                if (!inThisRoom) continue; // 이 방이 여는 방향이 아님 → 스킵

                Vector3 intoDir = intoDirBase;

                // 문 스윙 정사각형(u, t) 범위
                float uMin = Mathf.Min(hingeU, hingeU + signU * doorWidth);
                float uMax = Mathf.Max(hingeU, hingeU + signU * doorWidth);
                float tMin = wallInset;
                float tMax = wallInset + doorWidth;

#if UNITY_EDITOR
                // 디버그 와이어(문-방 1쌍당 1회)
                Vector3 c00 = p0 + uDir * uMin + intoDir * tMin;
                Vector3 c10 = p0 + uDir * uMax + intoDir * tMin;
                Vector3 c11 = p0 + uDir * uMax + intoDir * tMax;
                Vector3 c01 = p0 + uDir * uMin + intoDir * tMax;
                _debugSwingRects.Add(new Vector3[] { c00, c10, c11, c01, c00 });
#endif

                // 셀 래스터라이즈: 스윙 영역 내부면 배치 불가
                for (int gz = 0; gz < g.rows; gz++)
                {
                    for (int gx = 0; gx < g.cols; gx++)
                    {
                        if (!g.placementMask[gx, gz]) continue; // 이미 불가/방밖이면 스킵

                        float cx = g.originXZ.x + (gx + 0.5f) * g.cellSize;
                        float cz = g.originXZ.y + (gz + 0.5f) * g.cellSize;

                        Vector3 cellW = new Vector3(cx, 0.0f, cz);
                        Vector3 rel = cellW - p0;
                        float u = Vector3.Dot(rel, uDir);
                        float t = Vector3.Dot(rel, intoDir); // 방 내부를 +t

                        if (u >= uMin && u <= uMax && t >= tMin && t <= tMax)
                        {
                            g.placementMask[gx, gz] = false; // 스윙 영역 = 배치 불가
                        }
                    }
                }
            }
        }
    }

    // ===== 디버그 시각화 =====
    void OnDrawGizmos()
    {
        // ✅ 플레이 중일 때만 디버그 시각화
        if (!Application.isPlaying) return;
        if (!drawGizmos || grids == null) return;

        float y = transform.position.y + 0.01f;

        for (int r = 0; r < grids.Count; r++)
        {
            RoomPlacementGrid grid = grids[r];

            // ✅ 녹색 고정 색상 (투명도는 gizmoAlpha 사용)
            Color fill = new Color(0f, 1f, 0f, gizmoAlpha);
            Gizmos.color = fill;

            // 너무 많은 셀을 그려 성능이 떨어지지 않게 간단한 가드
            int maxDraw = Mathf.Min(grid.cols * grid.rows, 25000);
            int drawn = 0;

            for (int gz = 0; gz < grid.rows; gz++)
            {
                for (int gx = 0; gx < grid.cols; gx++)
                {
                    if (!grid.placementMask[gx, gz]) continue;

                    Vector3 c = grid.GridCenterToWorld(gx, gz, y);
                    Vector3 size = new Vector3(grid.cellSize * 0.98f, 0.002f, grid.cellSize * 0.98f);
                    Gizmos.DrawCube(c, size);

                    drawn++;
                    if (drawn >= maxDraw) break;
                }
                if (drawn >= maxDraw) break;
            }
        }

#if UNITY_EDITOR
        Gizmos.color = Color.yellow;
        for (int i = 0; i < _debugSwingRects.Count; i++)
        {
            Vector3[] r = _debugSwingRects[i];
            for (int k = 0; k + 1 < r.Length; k++)
            {
                Gizmos.DrawLine(r[k], r[k + 1]);
            }
        }
#endif
    }
}
