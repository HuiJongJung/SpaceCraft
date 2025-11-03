using System.Collections.Generic;
using UnityEngine;

public class RoomBuilder : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SpaceData data;   // SpaceData.Instance 보관소
    private SpaceLayout _layout;
    private GameObject _spaceRoot;

    [Header("Options")]
    [SerializeField] private bool addCollider = true;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    
    //Structure
    public struct Edge { public int A; public int B; }

    private const float EPS_SQR = 1e-12f;

    private void Start()
    {
        if (data == null) { data = SpaceData.Instance; }
        
        _layout = null;
        if (data != null && data._layout != null) { _layout = data._layout; }

        if (_layout == null)
        {
            Debug.LogError("[RoomBuilder] SpaceLayout is null.");
            return;
        }

        _spaceRoot = gameObject;
        
        
        //Build
        BuildFloors();
        BuildWalls();
    }
    
    #region BuildFloor
    //Build Floors
    public void BuildFloors()
    {
        if (_layout.floors == null || _layout.floors.Count == 0)
        {
            Debug.LogWarning("[RoomBuilder] No floors.");
            return;
        }

        for (int i = 0; i < _layout.floors.Count; i++)
        {
            FloorDef fd = _layout.floors[i];
            //0. Validate
            if (fd == null) { continue; }
            if (fd.vertices == null || fd.vertices.Count < 3) { continue; }
            if (fd.indices == null || fd.indices.Count < 3) { continue; }

            // 1. Ensure Winding (UpWard) (change order of indices)
            List<int> fixedTopIndices = new List<int>(_layout.floors[i].indices);
            
            List<Vector3> tempTopVerts = new List<Vector3>(fd.vertices.Count);
            for (int k = 0; k < fd.vertices.Count; k++)
            {
                Vec3 v = fd.vertices[k];
                tempTopVerts.Add(new Vector3(v.x, v.y, v.z));
            }
            //Ensure UpWard Winding
            EnsureWindingByNormal(tempTopVerts, fixedTopIndices, Vector3.up);
            
            // 2. Make Mesh
            Mesh m = BuildFloorMesh(fd.vertices, fixedTopIndices, fd.thickness);
            
            // 3. Make Object & Assign Mesh
            GameObject go = new GameObject("Floor_" + fd.id);
            go.transform.SetParent(_spaceRoot.transform, false);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = m;
            
            // 4. Assign Material
            if (floorMaterial != null)
            {
                mr.sharedMaterial = floorMaterial;
            }
            else
            {
                mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
            
            // 5. Add Collider
            if (addCollider)
            {
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = m;
            }
            
            // 6. Recenter Pivot
            RecenterPivot(mf, new Vector3(fd.vertices[0].x, fd.vertices[0].y, fd.vertices[0].z));
        }

        Debug.Log("[RoomBuilder] Floors built: " + _layout.floors.Count);
    }
    
    // Build Floor Mesh
    private Mesh BuildFloorMesh(List<Vec3> topVerts, List<int> topIndices, float thickness)
    {
        Mesh mesh = new Mesh();
        
        //0. Validate
        if (topVerts == null || topVerts.Count < 3) { return mesh; }
        if (topIndices == null || topIndices.Count < 3) { return mesh; }
        
        int topCount = topVerts.Count;

        // 1) Top Vertices
        List<Vector3> verts = new List<Vector3>(topCount * 2);
        for (int i = 0; i < topCount; i++)
        {
            Vec3 v = topVerts[i];
            verts.Add(new Vector3(v.x, v.y, v.z));
        }

        // 2) Compute Top Face Normal
        int i0 = topIndices[0];
        int i1 = topIndices[1];
        int i2 = topIndices[2];

        Vector3 v1 = verts[i0];
        Vector3 v2 = verts[i1];
        Vector3 v3 = verts[i2];

        Vector3 faceN = Vector3.Cross(v2 - v1, v3 - v2).normalized;
        if (faceN.sqrMagnitude <= EPS_SQR) { faceN = Vector3.up; }

        // 3) Bottom Vertices
        // translate by "Thickness" to "Bottom Normal Direction"
        Vector3 bottomMove = -faceN * thickness;
        for (int i = 0; i < topCount; i++)
        {
            Vector3 vTop = verts[i];
            verts.Add(vTop + bottomMove);
        }

        // 4) Compute Indices
        List<int> indices = new List<int>(topIndices.Count * 2);

        // 4-1) Top Indices
        for (int i = 0; i < topIndices.Count; i++)
        {
            indices.Add(topIndices[i]);
        }

        // 4-2) Bottom Indices (Reverse)
        for (int i = 0; i < topIndices.Count; i += 3)
        {
            i0 = topIndices[i + 0] + topCount;
            i1 = topIndices[i + 1] + topCount;
            i2 = topIndices[i + 2] + topCount;
            indices.Add(i2);
            indices.Add(i1);
            indices.Add(i0);
        }

        // 5) Copmute Normals / UVs (Top / Bottom Face)
        List<Vector3> normals = new List<Vector3>(verts.Count);
        List<Vector2> uvs = new List<Vector2>(verts.Count);

        //Top Face
        //Normal : faceN
        //uv : Project XZ
        for (int i = 0; i < topCount; i++)
        {
            normals.Add(faceN);
            Vector3 v = verts[i];
            uvs.Add(new Vector2(v.x, v.z));
        }
        //Bottom Face
        //Normal : -faceN
        //uv : Project XZ
        for (int i = 0; i < topCount; i++)
        {
            normals.Add(-faceN);
            Vector3 v = verts[topCount + i];
            uvs.Add(new Vector2(v.x, v.z));
        }
    
        // 6) Create Side Face
        
        //Compute topIndices 's borders
        //bottomPosition = verts[i + topCount]
        List<Edge> borders = FindBorderEdges(topIndices);
        for (int i = 0; i < borders.Count; i++)
        {
            int a = borders[i].A;
            int b = borders[i].B;
            
            // Get other Side's Position (aTop / bTop / aBot / bBot) - By Offset
            Vector3 aTopPos = verts[a];
            Vector3 bTopPos = verts[b];
            Vector3 aBotPos = verts[a + topCount];
            Vector3 bBotPos = verts[b + topCount];
            
            // Compute Side Normal (edgeDir X faceN)
            Vector3 edgeDir = (bTopPos - aTopPos).normalized;
            Vector3 sideN = Vector3.Cross(edgeDir, faceN).normalized;
            float edgeLen = Vector3.Distance(aTopPos, bTopPos);
            
            
            //Make Side Vertices (Hard Copy Vertices)
            int baseIndex = verts.Count;

            // Add Vertices / Normals / UVs / Indices
            //vertices
            verts.Add(aTopPos);  
            verts.Add(aBotPos);  
            verts.Add(bBotPos);  
            verts.Add(bTopPos);
            
            //normal
            normals.Add(sideN);
            normals.Add(sideN);
            normals.Add(sideN);
            normals.Add(sideN);
            
            //indices
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
            //uv
            uvs.Add(new Vector2(0.0f, 0.0f));
            uvs.Add(new Vector2(0.0f, thickness));
            uvs.Add(new Vector2(edgeLen, thickness));
            uvs.Add(new Vector2(edgeLen, 0.0f));
        }

        // 7) Make Mesh
        mesh.SetVertices(verts);
        mesh.SetTriangles(indices, 0);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();

        return mesh;
    }
    
    #endregion
    
    
    #region BuildWall
    // Build Walls
    public void BuildWalls()
    {
        if (_layout == null || _layout.walls == null || _layout.walls.Count == 0)
        {
            Debug.LogWarning("[RoomBuilder] No walls.");
            return;
        }
        
        for (int i = 0; i < _layout.walls.Count; i++)
        {
            WallDef wd = _layout.walls[i];
            if (wd == null) continue;
            if (wd.vertices == null || wd.vertices.Count < 4) continue;
            if (wd.indices == null || wd.indices.Count < 3) continue;
            
            // 1. Get Openings List of Wall
            List<OpeningDef> wallOps;
            if (!data.opsByWall.TryGetValue(wd.id, out wallOps))
            {
                wallOps = new List<OpeningDef>();
            }
            
            // 2. Make Mesh
            Mesh m = BuildWallMesh(wd.vertices, wd.indices, wd.thickness, wallOps);

            // 3. Make GameObject & Assign Mesh
            GameObject go = new GameObject("Wall_" + wd.id);
            go.transform.SetParent(_spaceRoot.transform, false);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = m;
            
            // 4. Assign Material
            if (wallMaterial != null)
            {
                mr.sharedMaterial = wallMaterial;
            }
            else
            {
                mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
            
            // 5. Assign Collider
            if (addCollider)
            {
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = m;
            }
            
            // 6. Recenter Pivot
            RecenterPivot(mf, new Vector3(wd.vertices[0].x, wd.vertices[0].y, wd.vertices[0].z));
        }

        Debug.Log("[RoomBuilder] Walls built: " + _layout.walls.Count);
    }
    
    // Build Wall Mesh
    private Mesh BuildWallMesh(List<Vec3> wallVerts, List<int> baseIndices, float thickness, List<OpeningDef> openings)
    {
        Mesh mesh = new Mesh();
        // 0. Validate
        if (wallVerts == null || wallVerts.Count < 4) return mesh;
        
        // 1. Read 4 vertices
        List<Vector3> w = new List<Vector3>(wallVerts.Count);
        for (int i = 0; i < wallVerts.Count; i++)
        {
            Vec3 v = wallVerts[i];
            w.Add(new Vector3(v.x, v.y, v.z));
        }
        
        // Assume - p0:BottomLeft, p1:BottomRight, p2:TopRight, p3:TopLeft
        // 2. Copmute Normal & Axes
        Vector3 p0 = w[0];
        Vector3 p1 = w[1];
        Vector3 p3 = w[3];
        
        // u : p0 -> p1
        Vector3 uDir = (p1 - p0).normalized;
        // v : p0 -> p3
        Vector3 vDir = (p3 - p0).normalized;
        Vector3 nDir = Vector3.Cross(uDir, vDir).normalized;
        
        float uLen = Vector3.Distance(p0, p1);
        float vLen = Vector3.Distance(p0, p3);

        // 3. Collect Cut Lines
        List<float> uCuts = new List<float>();
        List<float> vCuts = new List<float>();
        
        uCuts.Add(0.0f);
        uCuts.Add(uLen);
        vCuts.Add(0.0f);
        vCuts.Add(vLen);
        
        //Openings 어쩌구..
        List<Rect> opRects = new List<Rect>();
        for (int i = 0; i < openings.Count; i++)
        {
            OpeningDef od = openings[i];
            if (od == null) { continue; }
            
            Vector3 cW = new Vector3(od.position.x, od.position.y, od.position.z);
            Vector2 cUV = WorldToUV(cW, p0, uDir, vDir);
            
            float uMin = cUV.x - od.size.x * 0.5f;
            float uMax = cUV.x + od.size.x * 0.5f;
            float vMin = cUV.y - od.size.y * 0.5f;
            float vMax = cUV.y + od.size.y * 0.5f;

            if (uMax <= 0.0f || uMin >= uLen) { continue; }
            if (vMax <= 0.0f || vMin >= vLen) { continue; }

            if (uMin < 0.0f) { uMin = 0.0f; }
            if (vMin < 0.0f) { vMin = 0.0f; }
            if (uMax > uLen) { uMax = uLen; }
            if (vMax > vLen) { vMax = vLen; }

            Rect r = new Rect(uMin, vMin, uMax - uMin, vMax - vMin);
            if (r.width <= 0.0f || r.height <= 0.0f) { continue; }

            opRects.Add(r);
            uCuts.Add(uMin); 
            uCuts.Add(uMax);
            vCuts.Add(vMin); 
            vCuts.Add(vMax);
        }
        
        //Sort & DeDuplication
        uCuts.Sort();
        vCuts.Sort();
        DedupSorted(uCuts);
        DedupSorted(vCuts);

        // 4. Build Front Face (Skip Hole)
        List<Vector3> topVerts = new List<Vector3>();
        List<int> topIndices = new List<int>();
        
        int[,] vertIndex = new int[uCuts.Count, vCuts.Count];
        for (int ui = 0; ui < uCuts.Count; ui++)
        {
            for (int vi = 0; vi < vCuts.Count; vi++)
            {
                vertIndex[ui, vi] = -1;
            }
        }
        
        for (int ui = 0; ui + 1 < uCuts.Count; ui++)
        {
            for (int vi = 0; vi + 1 < vCuts.Count; vi++)
            {
                float u0 = uCuts[ui];
                float u1 = uCuts[ui + 1];
                float v0 = vCuts[vi];
                float v1 = vCuts[vi + 1];

                if (u1 - u0 <= 0.0f || v1 - v0 <= 0.0f) { continue; }
                
                
                Rect cell = new Rect(u0, v0, u1 - u0, v1 - v0);
                if (IntersectsAny(cell, opRects)) { continue; }

                int i00 = EnsureCorner(topVerts, vertIndex, ui, vi, u0, v0, p0, uDir, vDir);
                int i10 = EnsureCorner(topVerts, vertIndex, ui + 1, vi, u1, v0, p0, uDir, vDir);
                int i11 = EnsureCorner(topVerts, vertIndex, ui + 1, vi + 1, u1, v1, p0, uDir, vDir);
                int i01 = EnsureCorner(topVerts, vertIndex, ui, vi + 1, u0, v1, p0, uDir, vDir);
                
                // Ensure CCW
                topIndices.Add(i00); topIndices.Add(i10); topIndices.Add(i11);
                topIndices.Add(i00); topIndices.Add(i11); topIndices.Add(i01);
            }
        }
        
        // return if no verts or no indices
        if (topVerts.Count == 0 || topIndices.Count == 0)
        {
            Mesh empty = new Mesh();
            return empty;
        }

        // 5. Make Back Face(By Copy FrontFace)
        int topCount = topVerts.Count;
        List<Vector3> verts = new List<Vector3>(topCount * 2);
        // 5-1. Front Face Verts
        for (int i = 0; i < topCount; i++)
        {
            verts.Add(topVerts[i]);
        }
        // 5-2. Back Face Verts
        Vector3 backOffset = -nDir * thickness;
        for (int i = 0; i < topCount; i++)
        {
            verts.Add(topVerts[i] + backOffset);
        }
        
        // 6. Compute Indices
        // 6-1. Front Face
        List<int> indices = new List<int>(topIndices.Count * 2);
        for (int i = 0; i < topIndices.Count; i++)
        {
            indices.Add(topIndices[i]);
        }
        // 6-2. Back Face (Reverse)
        for (int i = 0; i < topIndices.Count; i += 3)
        {
            int a = topIndices[i + 0] + topCount;
            int b = topIndices[i + 1] + topCount;
            int c = topIndices[i + 2] + topCount;
            indices.Add(a);
            indices.Add(c);
            indices.Add(b);
        }

        // 7. Assign Front/Back Face's Normals, UVs
        List<Vector3> normals = new List<Vector3>(verts.Count);
        List<Vector2> uvs = new List<Vector2>(verts.Count);

        for (int i = 0; i < topCount; i++)
        {
            Vector2 uvTop = WorldToUV(verts[i], p0, uDir, vDir);
            normals.Add(nDir);
            uvs.Add(new Vector2(uvTop.x, uvTop.y));
        }
        for (int i = 0; i < topCount; i++)
        {
            Vector2 uvBot = WorldToUV(verts[i + topCount], p0, uDir, vDir);
            normals.Add(-nDir);
            uvs.Add(new Vector2(uvBot.x, uvBot.y));
        }

        // 8. Create Side Faces
        List<Edge> borders = FindBorderEdges(topIndices);
        HashSet<(int,int)> processed = new HashSet<(int,int)>();

        for (int i = 0; i < borders.Count; i++)
        {
            int a = borders[i].A;
            int b = borders[i].B;

            int ka = a;
            int kb = b;
            if (ka > kb)
            {
                int t = ka; ka = kb; kb = t;
            }
            bool added = processed.Add((ka, kb));
            if (!added) { continue; }

            Vector3 aTop = verts[a];
            Vector3 bTop = verts[b];
            Vector3 aBot = verts[a + topCount];
            Vector3 bBot = verts[b + topCount];

            Vector3 edgeDir = (bTop - aTop).normalized;
            Vector3 sideN = Vector3.Cross(edgeDir, nDir).normalized;
            float edgeLen = Vector3.Distance(aTop, bTop);

            int baseIndex = verts.Count;

            //Verts / Normals / UVs ( (0,0) ~ (edgeLen,thickness) ) 
            verts.Add(aTop);  normals.Add(sideN);  uvs.Add(new Vector2(0.0f, 0.0f));
            verts.Add(aBot);  normals.Add(sideN);  uvs.Add(new Vector2(0.0f, thickness));
            verts.Add(bBot);  normals.Add(sideN);  uvs.Add(new Vector2(edgeLen, thickness));
            verts.Add(bTop);  normals.Add(sideN);  uvs.Add(new Vector2(edgeLen, 0.0f));

            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);

            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }
        
        // 9. Make Mesh
        mesh.SetVertices(verts);
        mesh.SetTriangles(indices, 0);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();
        return mesh;
    }
    #endregion
    
    #region UtilFunction

    // Ensure Winding
    // By Calculate DotProduct between "n" and "wantNormal"
    // if result value < 0, then swap tris
    private void EnsureWindingByNormal(List<Vector3> verts, List<int> tris, Vector3 wantNormal)
    {
        if (tris == null || tris.Count < 3) { return; }
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector3 a = verts[tris[i + 0]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, wantNormal) < 0.0f)
            {
                int t = tris[i + 1];
                tris[i + 1] = tris[i + 2];
                tris[i + 2] = t;
            }
        }
    }
    
    // Find Border Edges
    // Input : Indices
    // OutPut : All Border Edges of the Face
    private List<Edge> FindBorderEdges(List<int> tris)
    {
        Dictionary<(int,int), int> count = new Dictionary<(int,int), int>();
        Dictionary<(int,int), Edge> firstDir = new Dictionary<(int,int), Edge>();
        
        //Tour All Indices & Count Edges
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            int i0 = tris[i + 0];
            int i1 = tris[i + 1];
            int i2 = tris[i + 2];
            
            AddEdgeCount(i0, i1, count, firstDir);
            AddEdgeCount(i1, i2, count, firstDir);
            AddEdgeCount(i2, i0, count, firstDir);
        }
        
        
        //Collect edge that appears "once" (Value == 1) => "Border Edge"
        List<Edge> borders = new List<Edge>();
        foreach (KeyValuePair<(int,int), int> kv in count)
        {
            if (kv.Value == 1)
            {
                Edge e = firstDir[kv.Key];
                borders.Add(e);
            }
        }
        return borders;
    }

    // Add Edge Count
    // Edge's Key : (min, max) - ValueTuple Type : Order Sensitive
    private void AddEdgeCount(int a, int b, Dictionary<(int,int), int> count, Dictionary<(int,int), Edge> firstDir)
    {
        if (a == b) return;

        int min, max;
        min = System.Math.Min(a, b);
        max = System.Math.Max(a, b);
        
        //Make Key (Min, Max) - Order Sensitive
        (int, int) key = (min, max);

        int c;
        // Key Not Exist -> Add Edge
        if (!count.TryGetValue(key, out c))
        {
            // Add Key & Direction
            count.Add(key, 1);
            Edge e = new Edge();
            e.A = a;
            e.B = b;
            firstDir.Add(key, e);
        }
        // Key Already Exist -> Only Increase Count
        else
        {
            c = c + 1;
            count[key] = c;
        }
    }
    
    // Recenter Pivot
    // Position / Collider
    public static void RecenterPivot(MeshFilter mf, Vector3 pivotLocal)
    {
        if (mf == null) return;
        Mesh mesh = mf.sharedMesh;
        if (mesh == null) return;

        Vector3[] v = mesh.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = v[i] - pivotLocal;
        }
        mesh.vertices = v;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        // 월드 위치 보정(시각적 동일 유지)
        Transform t = mf.transform;
        Vector3 worldOffset = t.TransformVector(pivotLocal);
        t.position = t.position + worldOffset;

        // 콜라이더 갱신
        MeshCollider mc = mf.GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = null;
        if (mc != null) mc.sharedMesh = mesh;
    }
    
    // World -> UV
    private static Vector2 WorldToUV(Vector3 wpos, Vector3 p0, Vector3 uDir, Vector3 vDir)
    {
        Vector3 rel = wpos - p0;
        float u = Vector3.Dot(rel, uDir);
        float v = Vector3.Dot(rel, vDir);
        return new Vector2(u, v);
    }
    
    // UV -> World
    private static Vector3 UVToWorldPos(float u, float v, Vector3 p0, Vector3 uDir, Vector3 vDir)
    {
        return p0 + uDir * u + vDir * v;
    }
    
    // Make Vertices
    // Do not Allow Duplication
    private static int EnsureCorner(
        List<Vector3> topVerts, int[,] vertIndex, int ui, int vi,
        float u, float v, Vector3 p0, Vector3 uDir, Vector3 vDir)
    {
        int idx = vertIndex[ui, vi];
        if (idx >= 0) return idx;

        Vector3 w = UVToWorldPos(u, v, p0, uDir, vDir);
        topVerts.Add(w);
        int newIdx = topVerts.Count - 1;
        vertIndex[ui, vi] = newIdx;
        return newIdx;
    }
    
    // DeDuplication
    private void DedupSorted(List<float> arr)
    {
        if (arr.Count <= 1) { return; }
        List<float> outL = new List<float>();
        float last = arr[0];
        outL.Add(last);
        for (int i = 1; i < arr.Count; i++)
        {
            if (Mathf.Abs(arr[i] - last) > EPS_SQR)
            {
                outL.Add(arr[i]);
                last = arr[i];
            }
        }
        arr.Clear();
        for (int i = 0; i < outL.Count; i++)
        {
            arr.Add(outL[i]);
        }
    }
    
    // Check Intersects between Cell and Openings
    // * if cell intersects with openings, return true
    private bool IntersectsAny(Rect cell, List<Rect> rects)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            if (RectOverlaps(cell, rects[i])) { return true; }
        }
        return false;
    }
    
    // Check Overlaps between Rects
    private bool RectOverlaps(Rect a, Rect b)
    {
        if (a.xMin >= b.xMax) { return false; }
        if (a.xMax <= b.xMin) { return false; }
        if (a.yMin >= b.yMax) { return false; }
        if (a.yMax <= b.yMin) { return false; }
        return true;
    }
    
    #endregion
}
