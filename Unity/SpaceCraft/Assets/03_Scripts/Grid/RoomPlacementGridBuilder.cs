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

    [Header("Door Blocking")]
    [SerializeField] private float wallInset = 0.02f; // 벽면에서 살짝 안쪽(수치오차/겹침 방지)
    [SerializeField] private float slidingDoorDepth = 0.5f;

    [Header("Runtime Grid Visual")]
    [SerializeField] private bool showRuntimeGrid = false;
    public Material gridMaterial;
    public Material gridOccupiedMaterial;
    public Material gridClearanceMaterial;
    [SerializeField] private Transform gridRoot; // 그리드 오브젝트들을 담아둘 부모

    private Dictionary<int, MeshRenderer[,]> _tileCache = new Dictionary<int, MeshRenderer[,]>();
    private Dictionary<int, GameObject> roomGridRoots = new Dictionary<int, GameObject>();

    // 결과물: 방별 그리드
    public List<RoomPlacementGrid> grids = new List<RoomPlacementGrid>();

    //public RoomManager roomManager;

    void Start()
    {
        if (data == null) data = SpaceData.Instance;
        //if (roomManager == null)
        //{
        //    roomManager = FindObjectOfType<RoomManager>();
        //}
        RebuildAll();
    }

    public void RebuildAll()
    {
#if UNITY_EDITOR
        _debugSwingRects.Clear();
#endif

        grids.Clear();
        _tileCache.Clear();

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

            grid.occupiedMask = new bool[cols, rows];
            grid.physicalBodyMask = new bool[cols, rows];
            grid.doorMask = new bool[cols, rows];

            IdentifyWallZones(grid);
            grids.Add(grid);
        }

        ApplyDoorSwingZones(); // 문이 열리는 사각형 영역만 배치 불가 처리
        Debug.Log("[RoomPlacementGridBuilder] Built " + grids.Count + " room grids with door swing mask.");

        if (showRuntimeGrid)
        {
            BuildRuntimeGridVisuals();
        }
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
    // ★ 문 스윙/반대편(동일 크기) 배치 불가 영역 생성
    // 문 스윙 / 미닫이 영역 적용
    // - 여닫이: 스윙 방향 + (외곽문일 경우) 반대편 동일 크기 차단
    // - 미닫이: 각 방 안쪽으로 얇은 띠(slidingDoorDepth) 차단
    private void ApplyDoorSwingZones()
    {
        if (data == null || data._layout == null) return;
        if (data._layout.openings == null || data._layout.walls == null || data._layout.rooms == null) return;

#if UNITY_EDITOR
        _debugSwingRects.Clear();
#endif

        // 방별 폴리곤 + 무게중심 캐시
        Dictionary<int, List<Vector2>> roomPoly = new Dictionary<int, List<Vector2>>();
        Dictionary<int, Vector2> roomCentroid = new Dictionary<int, Vector2>();
        for (int i = 0; i < grids.Count; i++)
        {
            RoomPlacementGrid g = grids[i];
            RoomDef rm = data._layout.rooms.Find(r => r != null && r.roomID == g.roomID);
            if (rm == null) continue;

            List<Vector2> poly = ExtractRoomPolygonXZ(rm, data._layout);
            if (poly == null || poly.Count < 3) continue;
            roomPoly[g.roomID] = poly;
            roomCentroid[g.roomID] = ComputePolygonCentroid(poly);
        }

        // 모든 openings 순회
        for (int oi = 0; oi < data._layout.openings.Count; oi++)
        {
            OpeningDef od = data._layout.openings[oi];
            if (od == null || od.type == null) continue;

            string otype = od.type.ToLower();
            bool isHinged = (otype == "door");
            bool isSliding = (otype == "slidedoor");
            if (!isHinged && !isSliding) continue;   // 창문 등은 무시

            // 벽 찾기
            WallDef wd = data._layout.walls.Find(w => w != null && w.id == od.wallID);
            if (wd == null || wd.vertices == null || wd.vertices.Count < 4) continue;

            Vector3 p0 = new Vector3(wd.vertices[0].x, wd.vertices[0].y, wd.vertices[0].z);
            Vector3 p1 = new Vector3(wd.vertices[1].x, wd.vertices[1].y, wd.vertices[1].z);
            Vector3 p3 = new Vector3(wd.vertices[3].x, wd.vertices[3].y, wd.vertices[3].z);

            // 원래 벡터들
            Vector3 e1 = p1 - p0;
            Vector3 e3 = p3 - p0;
            float len1 = e1.magnitude;
            float len3 = e3.magnitude;

            // 둘 다 0이면 의미 없는 벽이니 스킵
            if (len1 <= 1e-6f && len3 <= 1e-6f) continue;

            // 1) 긴 쪽을 "벽 길이 방향"으로 사용
            Vector3 uDir;
            Vector3 thicknessCandidate;
            float uLen;
            if (len1 >= len3)
            {
                uDir = e1.normalized;
                thicknessCandidate = e3;
                uLen = len1;
            }
            else
            {
                uDir = e3.normalized;
                thicknessCandidate = e1;
                uLen = len3;
            }

            // 2) 두께 후보가 거의 평행이면 → 버리고 uDir에 수직인 2D 법선으로 tDir 생성
            Vector3 tDir;
            if (thicknessCandidate.sqrMagnitude < 1e-10f)
            {
                // 후보가 너무 짧으면 바로 수직 벡터 생성
                Vector3 n2D = new Vector3(-uDir.z, 0f, uDir.x);   // (x,z) 평면 직교
                if (n2D.sqrMagnitude < 1e-10f)
                    n2D = new Vector3(uDir.z, 0f, -uDir.x);
                tDir = n2D.normalized;
            }
            else
            {
                Vector3 tCandNorm = thicknessCandidate.normalized;
                float dot = Mathf.Abs(Vector3.Dot(tCandNorm, uDir));

                if (dot > 0.95f)
                {
                    // 거의 평행 → thickness는 믿지 말고, uDir에 수직인 방향을 직접 만든다
                    Vector3 n2D = new Vector3(-uDir.z, 0f, uDir.x);
                    if (n2D.sqrMagnitude < 1e-10f)
                        n2D = new Vector3(uDir.z, 0f, -uDir.x);
                    tDir = n2D.normalized;
                }
                else
                {
                    // 평행은 아니면, uDir 성분을 제거해서 "정확히 수직"으로 만든다
                    Vector3 tProjOnU = Vector3.Dot(thicknessCandidate, uDir) * uDir;
                    Vector3 tOrtho = thicknessCandidate - tProjOnU;
                    tDir = tOrtho.normalized;
                }
            }

            Vector3 centerW = new Vector3(od.center.x, od.center.y, od.center.z);
            Vector3 hingeW = new Vector3(od.hingePos.x, od.hingePos.y, od.hingePos.z);

            float doorWidth = Mathf.Max(od.width, 0.0f);
            if (doorWidth <= 0.0f) continue;

            // u 방향 범위 계산
            float uMinCommon, uMaxCommon;

            if (isHinged)
            {
                // 여닫이: hinge 기준으로 한쪽으로 폭 만큼
                float hingeU = Vector3.Dot(hingeW - p0, uDir);
                float signU = Vector3.Dot(centerW - hingeW, uDir) >= 0.0f ? 1.0f : -1.0f;

                uMinCommon = Mathf.Min(hingeU, hingeU + signU * doorWidth);
                uMaxCommon = Mathf.Max(hingeU, hingeU + signU * doorWidth);
            }
            else
            {
                // 미닫이: center 기준 양쪽으로 doorWidth/2
                float centerU = Vector3.Dot(centerW - p0, uDir);
                uMinCommon = centerU - doorWidth * 0.5f;
                uMaxCommon = centerU + doorWidth * 0.5f;
            }

            // t 방향 범위
            float tMinCommon = wallInset;
            float tMaxCommon = isHinged ? wallInset + doorWidth
                                        : wallInset + slidingDoorDepth;

            // 이 벽을 공유하는 방들(외곽 문 판정용)
            List<RoomPlacementGrid> roomsWithThisWall = new List<RoomPlacementGrid>();
            for (int gi = 0; gi < grids.Count; gi++)
            {
                RoomPlacementGrid g = grids[gi];
                RoomDef rm = data._layout.rooms.Find(r => r != null && r.roomID == g.roomID);
                if (rm != null && rm.wallIDs != null && rm.wallIDs.Contains(od.wallID))
                    roomsWithThisWall.Add(g);
            }
            bool isPerimeterDoor = (roomsWithThisWall.Count <= 1);

            // 각 방에 대해 처리
            for (int gi = 0; gi < roomsWithThisWall.Count; gi++)
            {
                RoomPlacementGrid g = roomsWithThisWall[gi];

                List<Vector2> poly;
                Vector2 centroid2D;
                if (!roomPoly.TryGetValue(g.roomID, out poly) || poly == null) continue;
                if (!roomCentroid.TryGetValue(g.roomID, out centroid2D)) continue;

                // 무게중심이 벽의 +t 쪽인지/-t 쪽인지
                Vector3 centroidW = new Vector3(centroid2D.x, 0f, centroid2D.y);
                bool roomIsPlus = (Vector3.Dot(centroidW - p0, tDir) >= 0f);

                // 이 방 쪽으로 향하는 방향
                Vector3 intoDirForThisRoom = roomIsPlus ? tDir : -tDir;

                float uMin = uMinCommon, uMax = uMaxCommon;
                float tMin = tMinCommon, tMax = tMaxCommon;

                if (isHinged)
                {
                    // ───────────── 여닫이문 처리 ─────────────
                    bool anyBlockedMain = false;

                    // 1) 현재 방 방향으로 차단 (스윙 + 접근 클리어런스)
                    for (int gz = 0; gz < g.rows; gz++)
                    {
                        for (int gx = 0; gx < g.cols; gx++)
                        {
                            if (!g.placementMask[gx, gz]) continue;

                            float cx = g.originXZ.x + (gx + 0.5f) * g.cellSize;
                            float cz = g.originXZ.y + (gz + 0.5f) * g.cellSize;

                            Vector3 cellW = new Vector3(cx, 0f, cz);
                            Vector3 rel = cellW - p0;
                            float u = Vector3.Dot(rel, uDir);
                            float t = Vector3.Dot(rel, intoDirForThisRoom);

                            if (u >= uMin && u <= uMax && t >= tMin && t <= tMax)
                            {
                                g.placementMask[gx, gz] = false;

                                if (g.doorMask != null)
                                    g.doorMask[gx, gz] = true;

                                anyBlockedMain = true;
                            }
                        }
                    }

#if UNITY_EDITOR
                    if (anyBlockedMain && (uMax - uMin) > 1e-4f && (tMax - tMin) > 1e-4f)
                    {
                        Vector3 c00 = p0 + uDir * uMin + intoDirForThisRoom * tMin;
                        Vector3 c10 = p0 + uDir * uMax + intoDirForThisRoom * tMin;
                        Vector3 c11 = p0 + uDir * uMax + intoDirForThisRoom * tMax;
                        Vector3 c01 = p0 + uDir * uMin + intoDirForThisRoom * tMax;
                        _debugSwingRects.Add(new Vector3[] { c00, c10, c11, c01, c00 });
                    }
#endif

                    // 2) 외곽 문이면: 같은 방에 반대방향도 동일 크기로 차단
                    if (isPerimeterDoor)
                    {
                        Vector3 intoOpp = -intoDirForThisRoom;
                        bool anyBlockedOpp = false;

                        for (int gz = 0; gz < g.rows; gz++)
                        {
                            for (int gx = 0; gx < g.cols; gx++)
                            {
                                if (!g.placementMask[gx, gz]) continue;

                                float cx = g.originXZ.x + (gx + 0.5f) * g.cellSize;
                                float cz = g.originXZ.y + (gz + 0.5f) * g.cellSize;

                                Vector3 cellW = new Vector3(cx, 0f, cz);
                                Vector3 rel = cellW - p0;
                                float u = Vector3.Dot(rel, uDir);
                                float t = Vector3.Dot(rel, intoOpp);

                                if (u >= uMinCommon && u <= uMaxCommon &&
                                    t >= tMinCommon && t <= tMaxCommon)
                                {
                                    g.placementMask[gx, gz] = false;

                                    if (g.doorMask != null)
                                        g.doorMask[gx, gz] = true;

                                    anyBlockedOpp = true;
                                }
                            }
                        }

#if UNITY_EDITOR
                        if (anyBlockedOpp && (uMaxCommon - uMinCommon) > 1e-4f && (tMaxCommon - tMinCommon) > 1e-4f)
                        {
                            Vector3 oc00 = p0 + uDir * uMinCommon + intoOpp * tMinCommon;
                            Vector3 oc10 = p0 + uDir * uMaxCommon + intoOpp * tMinCommon;
                            Vector3 oc11 = p0 + uDir * uMaxCommon + intoOpp * tMaxCommon;
                            Vector3 oc01 = p0 + uDir * uMinCommon + intoOpp * tMaxCommon;
                            _debugSwingRects.Add(new Vector3[] { oc00, oc10, oc11, oc01, oc00 });
                        }
#endif
                    }
                }
                else if (isSliding)
                {
                    // ───────────── 미닫이문 처리 ─────────────
                    bool anyBlockedSlide = false;

                    for (int gz = 0; gz < g.rows; gz++)
                    {
                        for (int gx = 0; gx < g.cols; gx++)
                        {
                            if (!g.placementMask[gx, gz]) continue;

                            float cx = g.originXZ.x + (gx + 0.5f) * g.cellSize;
                            float cz = g.originXZ.y + (gz + 0.5f) * g.cellSize;

                            Vector3 cellW = new Vector3(cx, 0f, cz);
                            Vector3 rel = cellW - p0;
                            float u = Vector3.Dot(rel, uDir);
                            float t = Vector3.Dot(rel, intoDirForThisRoom);

                            // 문 폭(uMin~uMax) + 방 안쪽으로 slidingDoorDepth 만큼만 차단
                            if (u >= uMin && u <= uMax && t >= tMin && t <= tMax)
                            {
                                g.placementMask[gx, gz] = false;

                                if (g.doorMask != null)
                                    g.doorMask[gx, gz] = true;

                                anyBlockedSlide = true;
                            }
                        }
                    }

#if UNITY_EDITOR
                    if (anyBlockedSlide && (uMax - uMin) > 1e-4f && (tMax - tMin) > 1e-4f)
                    {
                        Vector3 c00 = p0 + uDir * uMin + intoDirForThisRoom * tMin;
                        Vector3 c10 = p0 + uDir * uMax + intoDirForThisRoom * tMin;
                        Vector3 c11 = p0 + uDir * uMax + intoDirForThisRoom * tMax;
                        Vector3 c01 = p0 + uDir * uMin + intoDirForThisRoom * tMax;
                        _debugSwingRects.Add(new Vector3[] { c00, c10, c11, c01, c00 });
                    }
#endif
                }
            }
        }
    }


    // 폴리곤 무게중심(centroid)
    private Vector2 ComputePolygonCentroid(List<Vector2> poly)
    {
        int n = poly.Count;
        float A2 = 0f;   // 서명 면적*2
        float cx = 0f, cy = 0f;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i].x, yi = poly[i].y;
            float xj = poly[j].x, yj = poly[j].y;
            float cross = xj * yi - xi * yj;
            A2 += cross;
            cx += (xj + xi) * cross;
            cy += (yj + yi) * cross;
        }

        if (Mathf.Abs(A2) < 1e-6f)
        {
            // 퇴화 시 평균값으로 폴백
            float sx = 0f, sy = 0f;
            for (int i = 0; i < n; i++) { sx += poly[i].x; sy += poly[i].y; }
            return new Vector2(sx / n, sy / n);
        }

        float A = A2 * 0.5f;
        cx /= (6f * A);
        cy /= (6f * A);
        return new Vector2(cx, cy);
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
                    if (grid.physicalBodyMask != null && grid.physicalBodyMask[gx, gz])
                    {
                        Gizmos.color = new Color(1f, 0f, 0f, gizmoAlpha); // Red (Body)
                    }
                    else if (grid.occupiedMask != null && grid.occupiedMask[gx, gz])
                    {
                        Gizmos.color = new Color(1f, 0.64f, 0f, gizmoAlpha); // Orange (Clearance)
                    }
                    else if (grid.wallZoneMask != null && grid.wallZoneMask[gx, gz])
                    {
                        Gizmos.color = new Color(0f, 0f, 1f, gizmoAlpha); // Blue (Wall Zone - 디버깅용)
                    }
                    else if (grid.placementMask[gx, gz])
                    {
                        Gizmos.color = new Color(0f, 1f, 0f, gizmoAlpha); // Green (Empty)
                    }
                    else continue;

                    // 큐브 그리기 (기존 코드 유지)
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
    private void EnsureGridMaterial()
    {
        if (gridMaterial == null)
        {
            gridMaterial = new Material(Shader.Find("Unlit/Color"));
            gridMaterial.color = new Color(0f, 1f, 0f, 0.4f); // 연한 초록 투명
        }

        if (gridOccupiedMaterial == null)
        {
            gridOccupiedMaterial = new Material(Shader.Find("Unlit/Color"));
            gridOccupiedMaterial.color = new Color(1f, 0f, 0f); // 빨간색 반투명
        }

        if (gridClearanceMaterial == null)
        {
            gridClearanceMaterial = new Material(Shader.Find("Unlit/Color"));
            gridClearanceMaterial.color = new Color(1f, 0.64f, 0f, 0.4f); // 주황 (여유 공간)
        }
    }

    /// 그리드를 그립니다. 이미 생성되어 있다면 머테리얼만 갱신(Refresh)하고, 없다면 생성(Create)합니다.
    public void BuildRuntimeGridVisuals(int targetRoomID = -1)
    {
        EnsureGridMaterial();
        if (gridRoot == null) gridRoot = this.transform;

        foreach (RoomPlacementGrid grid in grids)
        {
            if (targetRoomID != -1 && grid.roomID != targetRoomID) continue;

            // 1. 캐시 확인: 이미 타일이 만들어져 있는가?
            if (_tileCache.ContainsKey(grid.roomID) && roomGridRoots.ContainsKey(grid.roomID))
            {
                // 있으면 -> 색깔만 갱신 (Refresh)
                RefreshGridMaterials(grid);
            }
            else
            {
                // 없으면 -> 오브젝트 생성 (Create)
                CreateGridObjects(grid);
            }
        }
    }

    // 타일 오브젝트를 처음 생성하는 함수
    private void CreateGridObjects(RoomPlacementGrid grid)
    {
        if (roomGridRoots.TryGetValue(grid.roomID, out GameObject oldRoot)) Destroy(oldRoot);

        GameObject roomRootGO = new GameObject($"Grid_Room_{grid.roomID}");
        roomRootGO.transform.SetParent(gridRoot, false);
        roomGridRoots[grid.roomID] = roomRootGO;

        // 캐시 배열 할당
        MeshRenderer[,] renderers = new MeshRenderer[grid.cols, grid.rows];

        for (int gz = 0; gz < grid.rows; gz++)
        {
            for (int gx = 0; gx < grid.cols; gx++)
            {
                // 안 그릴 곳(벽 등)은 패스
                if (!IsTileVisible(grid, gx, gz)) continue;

                Vector3 c = grid.GridCenterToWorld(gx, gz, 0f);
                c.y += 0.01f;

                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tile.name = $"Tile_{gx}_{gz}";
                tile.transform.SetParent(roomRootGO.transform, false);
                tile.transform.position = c;
                tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                float s = grid.cellSize * 0.98f;
                tile.transform.localScale = new Vector3(s, s, 1f);

                Destroy(tile.GetComponent<Collider>());

                // 렌더러 캐싱 & 초기 색상 설정
                MeshRenderer mr = tile.GetComponent<MeshRenderer>();
                renderers[gx, gz] = mr;

                mr.material = GetMaterialForGrid(grid, gx, gz);
            }
        }
        _tileCache[grid.roomID] = renderers;
    }

    // 이미 있는 타일의 색상만 바꾸는 함수 (최적화)
    private void RefreshGridMaterials(RoomPlacementGrid grid)
    {
        MeshRenderer[,] renderers = _tileCache[grid.roomID];

        for (int gz = 0; gz < grid.rows; gz++)
        {
            for (int gx = 0; gx < grid.cols; gx++)
            {
                MeshRenderer mr = renderers[gx, gz];
                if (mr == null) continue;

                // 현재 상태에 맞는 머테리얼 가져오기
                Material correctMat = GetMaterialForGrid(grid, gx, gz);

                // 다를 때만 교체
                if (correctMat != null && mr.sharedMaterial != correctMat)
                {
                    mr.material = correctMat;
                }
            }
        }
    }

    // 상태에 따른 머테리얼 결정 로직
    private Material GetMaterialForGrid(RoomPlacementGrid grid, int gx, int gz)
    {
        if (grid.physicalBodyMask != null && grid.physicalBodyMask[gx, gz])
            return gridOccupiedMaterial; // 빨강 (본체)

        if (grid.occupiedMask != null && grid.occupiedMask[gx, gz])
            return gridClearanceMaterial; // 주황 (여유 공간)

        // (디버그용) 벽 구역도 보고 싶으면 주석 해제
        // if (grid.wallZoneMask != null && grid.wallZoneMask[gx, gz]) return gridMaterial; 

        if (grid.placementMask[gx, gz])
            return gridMaterial; // 초록 (빈 땅)

        return null;
    }

    // 타일을 생성할지 말지 결정
    private bool IsTileVisible(RoomPlacementGrid grid, int gx, int gz)
    {
        return grid.placementMask[gx, gz] ||
               (grid.occupiedMask != null && grid.occupiedMask[gx, gz]);
    }

    public void ShowOnlyRoomGrid(int roomID)
    {
        foreach (var kvp in roomGridRoots)
        {
            bool active = (kvp.Key == roomID);
            kvp.Value.SetActive(active);
        }
    }
    public void SetRoomGridVisible(int roomID, bool visible)
    {
        GameObject root;
        if (roomGridRoots.TryGetValue(roomID, out root))
        {
            root.SetActive(visible);
        }
    }
    public void HideAllRoomGrids()
    {
        foreach (var kvp in roomGridRoots)
            kvp.Value.SetActive(false);
    }

    public void ShowAllRoomGrids()
    {
        foreach (var kvp in roomGridRoots)
            kvp.Value.SetActive(true);
    }
    private void IdentifyWallZones(RoomPlacementGrid grid)
    {
        int cols = grid.cols;
        int rows = grid.rows;
        grid.wallZoneMask = new bool[cols, rows];

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < cols; x++)
            {
                // 배치가 불가능한 곳(이미 벽이거나 문)은 벽 인접 여부를 따질 필요 없음
                if (!grid.placementMask[x, z]) continue;

                // 상하좌우 중 하나라도 '배치 불가(false)'가 있다면 -> 벽(또는 문) 경계면임
                bool isEdge = false;

                if (x - 1 < 0 || !grid.placementMask[x - 1, z]) isEdge = true;
                else if (x + 1 >= cols || !grid.placementMask[x + 1, z]) isEdge = true;
                else if (z - 1 < 0 || !grid.placementMask[x, z - 1]) isEdge = true;
                else if (z + 1 >= rows || !grid.placementMask[x, z + 1]) isEdge = true;

                if (isEdge)
                {
                    grid.wallZoneMask[x, z] = true;
                }
            }
        }
    }
    
    // Util GetGridByRoomId
    public RoomPlacementGrid GetGridByRoomId(int roomID)
    {
        if (grids == null) return null;

        for (int i = 0; i < grids.Count; i++)
        {
            RoomPlacementGrid g = grids[i];
            if (g != null && g.roomID == roomID)
            {
                return g;
            }
        }
        return null;
    }
}
