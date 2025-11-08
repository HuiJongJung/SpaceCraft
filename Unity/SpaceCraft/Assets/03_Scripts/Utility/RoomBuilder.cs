using System;
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

    [SerializeField] private GameObject doorPrefab;
    [SerializeField] private GameObject slideDoorPrefab;
    [SerializeField] private GameObject windowPrefab;
    
    //Structure
    public struct Edge { public int A; public int B; }

    [Header("Constants")]
    private const float EPS_SQR = 1e-12f;
    private const float DEFAULT_WINDOWY = 1.0f;
    private const float DEFAULT_WINDOWHEIGHT = 0.8f;
    private const float DEFAULT_DOORHEIGHT = 2.1f;
    private const float DEFAULT_CEILINGHEIGHT = 2.6f;
    
    private const float OPENING_ANGLE = 90.0f;
    private const float DEFAULT_DOORDEPTH = 0.2f;
    private const float DEFAULT_WINDOWDEPTH = 0.2f;

    private void Start()
    {
        if (data == null)
        {
            data = SpaceData.Instance;
        }

        _layout = null;
        if (data != null && data._layout != null)
        {
            _layout = data._layout;
        }

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

        // 1. Top Vertices
        List<Vector3> verts = new List<Vector3>(topCount * 2);
        for (int i = 0; i < topCount; i++)
        {
            Vec3 v = topVerts[i];
            verts.Add(new Vector3(v.x, v.y, v.z));
        }

        // 2. Compute Top Face Normal
        int i0 = topIndices[0];
        int i1 = topIndices[1];
        int i2 = topIndices[2];

        Vector3 v1 = verts[i0];
        Vector3 v2 = verts[i1];
        Vector3 v3 = verts[i2];

        Vector3 faceN = Vector3.Cross(v2 - v1, v3 - v2).normalized;
        if (faceN.sqrMagnitude <= EPS_SQR) { faceN = Vector3.up; }

        // 3. Bottom Vertices
        // translate by "Thickness" to "Bottom Normal Direction"
        Vector3 bottomMove = -faceN * thickness;
        for (int i = 0; i < topCount; i++)
        {
            Vector3 vTop = verts[i];
            verts.Add(vTop + bottomMove);
        }

        // 4. Compute Indices
        List<int> indices = new List<int>(topIndices.Count * 2);

        // 4-1. Top Indices
        for (int i = 0; i < topIndices.Count; i++)
        {
            indices.Add(topIndices[i]);
        }

        // 4-2. Bottom Indices (Reverse)
        for (int i = 0; i < topIndices.Count; i += 3)
        {
            i0 = topIndices[i + 0] + topCount;
            i1 = topIndices[i + 1] + topCount;
            i2 = topIndices[i + 2] + topCount;
            indices.Add(i2);
            indices.Add(i1);
            indices.Add(i0);
        }

        // 5. Copmute Normals / UVs (Top / Bottom Face)
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
    
        // 6. Create Side Face
        
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
            
            //uv
            uvs.Add(new Vector2(0.0f, 0.0f));
            uvs.Add(new Vector2(0.0f, thickness));
            uvs.Add(new Vector2(edgeLen, thickness));
            uvs.Add(new Vector2(edgeLen, 0.0f));
            
            //indices
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }

        // 7. Make Mesh
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
        // 0. Validate
        if (_layout == null || _layout.walls == null || _layout.walls.Count == 0)
        {
            Debug.LogWarning("[RoomBuilder] No walls.");
            return;
        }
        
        //Loop for all Walls
        for (int i = 0; i < _layout.walls.Count; i++)
        {
            WallDef wd = _layout.walls[i];
            if (wd == null) continue;
            if (wd.vertices == null || wd.vertices.Count < 4) continue;
            if (wd.indices == null || wd.indices.Count < 3) continue;
            
            // 1. Get Openings List of Each Wall
            List<OpeningDef> wallOps;
            if (!data.opsByWall.TryGetValue(wd.id, out wallOps))
            {
                wallOps = new List<OpeningDef>();
            }
            
            // 2. Make Mesh
            float height = _layout.ceilingHeight;
            if (_layout.ceilingHeight <= EPS_SQR)
            {
                height = DEFAULT_CEILINGHEIGHT;
            }
            
            // 3. Make GameObject & Assign Mesh
            GameObject go = new GameObject("Wall_" + wd.id);
            go.transform.SetParent(_spaceRoot.transform, false);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            
            Mesh m = BuildWallMeshWithOpenings(wd.vertices, wd.indices, height, wallOps);
            
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
    
    // Build Wall Mesh & Openings
    private Mesh BuildWallMeshWithOpenings(List<Vec3> bottomVerts, List<int> baseIndices, float height, List<OpeningDef> openings)
    {
        Mesh mesh = new Mesh();
        
        // 0. Validate
        if (bottomVerts == null || bottomVerts.Count < 4) { return mesh; }
        if (baseIndices == null || baseIndices.Count < 3) { return mesh; }

        // 1. Load 4 Bottom Vertices
        List<Vector3> w = new List<Vector3>(bottomVerts.Count);
        for (int i = 0; i < bottomVerts.Count; i++)
        {
            Vec3 v = bottomVerts[i];
            w.Add(new Vector3(v.x, v.y, v.z));
        }

        // 2. Compute Axes / Len
        // u: Long Side(Actual Wall, including)
        // t: Short Side
        // v: Normal of Bottom Face
        Vector3 p0 = w[0];
        Vector3 p1 = w[1];
        Vector3 p3 = w[3];

        Vector3 e1 = p1 - p0;
        Vector3 e3 = p3 - p0;

        float len1 = e1.magnitude;
        float len3 = e3.magnitude;
        
        //Edge Guard : if less than EPS_SQR, return empty mesh
        if (len1 <= EPS_SQR || len3 <= EPS_SQR)
        {
            return mesh;
        }

        Vector3 uDir; // uDir : Wall Dir
        Vector3 tDir; // tDir
        Vector3 vDir = Vector3.up; // vDir : Normal of Bottom Face
        float uLen;
        float tLen;
        float vLen = height;
        
        //Normalize uDir / tDir
        if (len1 >= len3)
        {
            uDir = e1 / len1;
            tDir = e3 / len3;
            uLen = len1;
            tLen = len3;
        }
        else
        {
            uDir = e3 / len3;
            tDir = e1 / len1;
            uLen = len3;
            tLen = len1;
        }

        // 3. nCand (u×v) : Front Face Normal Candidate
        // copmare with tDir to make nFrontOut
        Vector3 nCand = Vector3.Cross(uDir, vDir);
        if (nCand.sqrMagnitude <= EPS_SQR) nCand = tDir;
        else nCand = nCand.normalized;

        Vector3 nFrontOut;
        // if result of dot(nCand,tDir) > 0 then, use "-nCand"
        // nCand Should be "opposite direction of tDir"
        if (Vector3.Dot(nCand, tDir) > 0.0f)
        {
            nFrontOut = -nCand;
        }
        else
        {
            nFrontOut = nCand;
        }

        // ** Force tDir to -nFrontOut
        tDir = -nFrontOut;

        // 4. Collect Cutlines of Openings & Place Opening Prefabs
        
        // FrontFace: uv Plane
        // Origin : p0
        List<float> uCuts = new List<float>();
        List<float> vCuts = new List<float>();
        uCuts.Add(0.0f); uCuts.Add(uLen);
        vCuts.Add(0.0f); vCuts.Add(vLen);

        List<Rect> opRects = new List<Rect>();
        
        // 4-1. Assign windowCenterY
        // invalid windowY input -> Use Default Value
        float windowCenterY;
        if (_layout.windowY > 0.0f) windowCenterY = _layout.windowY;
        else windowCenterY = DEFAULT_WINDOWY;
        
        // loop for all Openings (included in wall)
        for (int i = 0; i < openings.Count; i++)
        {
            OpeningDef od = openings[i];
            if (od == null) { continue; }
            
            Vector3 cW = new Vector3(od.center.x, od.center.y, od.center.z);
            Vector3 rel = cW - p0;
            
            // 4-2. Compute uCenter & vCenter
            
            // Project Center to uvPlane (only extract "uCenter")
            float uCenter = Vector3.Dot(rel, uDir);
            float vCenter;
            OpeningType opType;
            float halfH;
            
            // Assign Type & Prefab

            // Validate
            if (od.type == null) continue;

            string t = od.type.ToLower();
            // door
            if (t == "door")
            {
                opType = OpeningType.Door;
                vCenter = _layout.doorHeight * 0.5f;
                halfH   = _layout.doorHeight * 0.5f;
            }
            // slide door
            else if (t == "slidedoor")
            {
                opType = OpeningType.SlideDoor;
                vCenter = _layout.doorHeight * 0.5f;
                halfH   = _layout.doorHeight * 0.5f;
            }
            // window
            else if (t == "window")
            {
                opType = OpeningType.Window;
                vCenter = windowCenterY;
                halfH   = _layout.windowHeight * 0.5f;
            }
            // exception
            else
            {
                continue;
            }
            
            // 4-4. Clipping & Clamping uMin, uMax, vMin, vMax
            float uMin = uCenter - od.width * 0.5f;
            float uMax = uCenter + od.width * 0.5f;
            float vMin = vCenter - halfH;
            float vMax = vCenter + halfH;
            
            // Clipping
            if (uMax <= 0.0f || uMin >= uLen) { continue; }
            if (vMax <= 0.0f || vMin >= vLen) { continue; }
            // Clamping
            if (uMin < 0.0f) { uMin = 0.0f; }
            if (uMax > uLen) { uMax = uLen; }
            if (vMin < 0.0f) { vMin = 0.0f; }
            if (vMax > vLen) { vMax = vLen; }

            float du = uMax - uMin;
            float dv = vMax - vMin;
            if (du <= 0.0f || dv <= 0.0f) { continue; }
            
            // 4-5. Add opRects & uCuts/ vCuts
            Rect r = new Rect(uMin, vMin, du, dv);
            opRects.Add(r);
            uCuts.Add(uMin); uCuts.Add(uMax);
            vCuts.Add(vMin); vCuts.Add(vMax);
            
            // 4-6. Place Opening Prefab
            
            // Get Width, Height, Depth
            float prefabWidth = od.width;
            float prefabHeight = DEFAULT_DOORHEIGHT;
            float prefabDepth = DEFAULT_DOORDEPTH;
            
            if (opType == OpeningType.Window)
            {
                prefabHeight = DEFAULT_WINDOWHEIGHT;
                prefabDepth = DEFAULT_WINDOWDEPTH;
            }

            // Compute Center Position Project On Wall (y = height/2)
            
            float uCenterUsed = 0.5f * (uMin + uMax);
            float vCenterUsed = 0.5f * (vMin + vMax);
            
            Vector3 centerWorld = UVToWorldPos(uCenterUsed, vCenterUsed, p0, uDir, vDir);
            // mid : Wall Depth Center Position
            Vector3 mid = p0 + tDir * (tLen * 0.5f);
            // Project to "mid" Plane
            centerWorld = mid + Vector3.ProjectOnPlane(centerWorld - mid, tDir);
            Quaternion rot = Quaternion.LookRotation(tDir, Vector3.up);
            GameObject openingObj = null;
            
            // Door
            if (opType == OpeningType.Door)
            {
                // Compute Hinge World (apply y pos, project to mid Plane)
                Vector3 hingeWorld = new Vector3(od.hingePos.x, od.hingePos.y, od.hingePos.z);
                hingeWorld.y = vCenter;
                hingeWorld = mid + Vector3.ProjectOnPlane(hingeWorld - mid, tDir);

                // Create Hinge Object
                GameObject hingeGO = new GameObject("Hinge_Door_" + od.id);
                hingeGO.transform.SetParent(_spaceRoot.transform, false);
                hingeGO.transform.position = hingeWorld;
                hingeGO.transform.rotation = rot;

                // Create Door Prefab
                float uHalf = od.width * 0.5f;
                if (doorPrefab == null)
                {
                    continue;
                }
                openingObj = Instantiate(doorPrefab, hingeGO.transform);

                // Compare Hinge Object's Right and Hinge->Center
                // Dot Result < 0, then signU = -1f
                float dot = Vector3.Dot(centerWorld - hingeWorld, hingeGO.transform.right);
                float signU = 1f;
                if (dot < 0f)
                {
                    signU = -1f;
                }
                // Move OpeningObj
                openingObj.transform.localPosition = new Vector3(signU * uHalf, 0f, 0f);
                // if signU = -1f, then Flip Object ( Y Axis, 180f )
                if (signU < 0f)
                {
                    openingObj.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);
                }
                else
                {
                    openingObj.transform.localRotation = Quaternion.identity;
                }

                // Door Visual Open (CW/CCW)
                if (od.isCW)
                {
                    hingeGO.transform.localRotation *= Quaternion.AngleAxis(OPENING_ANGLE, Vector3.up);
                }
                else
                {
                    hingeGO.transform.localRotation *= Quaternion.AngleAxis(-OPENING_ANGLE, Vector3.up);
                }
            }
            // Window
            else if (opType == OpeningType.Window)
            {
                // Apply centerY
                centerWorld.y = vCenter;
                // Window Depth = Wall Depth
                prefabDepth = tLen;
                
                
                // Create Window Object
                if (windowPrefab == null)
                {
                    continue;
                }
                openingObj = Instantiate(windowPrefab,centerWorld, rot, _spaceRoot.transform);
            }
            // SlideDoor
            else if (opType == OpeningType.SlideDoor)
            {
                // panel : One door
                float panelWidth = od.width * 0.5f;
                float panelHeight = _layout.doorHeight;
                float panelDepth = tLen * 0.5f;

                // Create Anchor Object (Apply pos, rot)
                GameObject anchorGO = new GameObject("Anchor_SlideDoor_" + od.id);
                anchorGO.transform.SetParent(_spaceRoot.transform, false);
                anchorGO.transform.position = centerWorld;
                anchorGO.transform.rotation = rot;
                
                if (slideDoorPrefab == null)
                {
                    continue;
                }
                // Create PanelA / Panel B
                // Move Each Panel about u Axis (+- panelWidth * 0.5f)
                // Move Each Panel about t Axis (+- tLen * 0.25f)
                float uHalfPanel = panelWidth * 0.5f;
                float tQuarter = 0.25f * tLen;

                // PanelA : u = -uHalfPanel, t = +tQuarter
                GameObject panelA = Instantiate(slideDoorPrefab, anchorGO.transform);
                panelA.name = "SlideDoor_A_" + od.id;
                panelA.transform.localPosition = new Vector3(-uHalfPanel, 0.0f, tQuarter);
                panelA.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up);
                
                // PanelB : u = +uHalfPanel, t = -tQuarter
                GameObject panelB = Instantiate(slideDoorPrefab, anchorGO.transform);
                panelB.name = "SlideDoor_B_" + od.id;
                panelB.transform.localPosition = new Vector3(uHalfPanel, 0.0f, -tQuarter);
                
                float slideOffsetU = 1.75f * uHalfPanel;
                Vector3 aPos = panelA.transform.localPosition;
                aPos.x += slideOffsetU;
                panelA.transform.localPosition = aPos;
                
                // Apply Size to Each Panel (width, depth, height)
                Furniture fa = panelA.GetComponent<Furniture>();
                if (fa != null)
                {
                    fa.SetSize(panelWidth * 100.0f, panelDepth * 100.0f, panelHeight * 100.0f, false);
                }
                Furniture fb = panelB.GetComponent<Furniture>();
                if (fb != null)
                {
                    fb.SetSize(panelWidth * 100.0f, panelDepth * 100.0f, panelHeight * 100.0f, false);
                }
            }
            
            // All - Apply Size (width, depth, height)
            if (openingObj != null)
            {
                Furniture f = openingObj.GetComponent<Furniture>();
                if (f != null)
                {
                    f.keepBottomOnScale = false;
                    f.SetSize(prefabWidth * 100, prefabDepth * 100, prefabHeight * 100, false);
                }   
            }
        }
        
        // 4-6. Sort & Deduplication
        uCuts.Sort();
        vCuts.Sort();
        DedupSorted(uCuts);
        DedupSorted(vCuts);

        // 5. Create Front Face(uv Plane)'s indices
        // Make Indices of front face
        // with Deduplication
        List<Vector3> frontVerts = new List<Vector3>();
        List<int> frontIndices = new List<int>();
        
        // 5-1. vertex's index of intersection of cutLine
        // For Deduplicaiton
        int[,] vertIndex = new int[uCuts.Count, vCuts.Count];
        for (int ui = 0; ui < uCuts.Count; ui++)
        {
            for (int vi = 0; vi < vCuts.Count; vi++)
            {
                vertIndex[ui, vi] = -1;
            }
        }
        
        // loop for (u,v)
        for (int ui = 0; ui + 1 < uCuts.Count; ui++)
        {
            for (int vi = 0; vi + 1 < vCuts.Count; vi++)
            {
                float u0 = uCuts[ui];
                float u1 = uCuts[ui + 1];
                float v0 = vCuts[vi];
                float v1 = vCuts[vi + 1];
                
                if (u1 - u0 <= EPS_SQR || v1 - v0 <= EPS_SQR) { continue; }

                Rect cell = new Rect(u0, v0, u1 - u0, v1 - v0);
                // if hole cell, Skip
                if (IntersectsAny(cell, opRects)) { continue; }
                
                // else -> Make 4 Vertices ( Reuse Duplicated Vertex )
                int i00 = GetOrCreateCornerVertex(frontVerts, vertIndex, ui,     vi,     u0, v0, p0, uDir, vDir);
                int i10 = GetOrCreateCornerVertex(frontVerts, vertIndex, ui + 1, vi,     u1, v0, p0, uDir, vDir);
                int i11 = GetOrCreateCornerVertex(frontVerts, vertIndex, ui + 1, vi + 1, u1, v1, p0, uDir, vDir);
                int i01 = GetOrCreateCornerVertex(frontVerts, vertIndex, ui,     vi + 1, u0, v1, p0, uDir, vDir);
                // Indices
                frontIndices.Add(i00); frontIndices.Add(i10); frontIndices.Add(i11);
                frontIndices.Add(i00); frontIndices.Add(i11); frontIndices.Add(i01);
            }
        }
        // 5-3. Validate for frontVerts & frotIndices
        if (frontVerts.Count == 0 || frontIndices.Count == 0)
        {
            return mesh;
        }
        // 5-4. Ensure Winding of Front Face with "nFrontOut"
        EnsureWindingByNormal(frontVerts, frontIndices, nFrontOut);

        // 6. Assign Front/Back Face's Vertices
        // Front Face : Just Add
        // Back Face : Copy Front Vertices and Parallel Movement with (-nFrontOut * tLen)
        int fCount = frontVerts.Count;
        
        // 6-1. add front vertices
        List<Vector3> verts = new List<Vector3>(fCount * 2);
        for (int i = 0; i < fCount; i++)
        {
            verts.Add(frontVerts[i]);
        }
        // 6-2. add back vertices (with Parallel Movement)
        Vector3 backOffset = -nFrontOut * tLen;
        for (int i = 0; i < fCount; i++)
        {
            verts.Add(frontVerts[i] + backOffset);
        }

        // 7. Assign Indices
        // Front Face : Same
        // Back Face : Reverse
        List<int> indices = new List<int>(frontIndices.Count * 2);
        // Front
        for (int i = 0; i < frontIndices.Count; i++)
        {
            indices.Add(frontIndices[i]);
        }
        // Back
        for (int i = 0; i < frontIndices.Count; i += 3)
        {
            int a = frontIndices[i + 0] + fCount;
            int b = frontIndices[i + 1] + fCount;
            int c = frontIndices[i + 2] + fCount;
            indices.Add(a);
            indices.Add(c); // Reverse
            indices.Add(b);
        }

        // 8. Assign Normals/UVs (Front/Back)
        List<Vector3> normals = new List<Vector3>(verts.Count);
        List<Vector2> uvs = new List<Vector2>(verts.Count);
        
        // 8-1. Front Face's normals / uvs
        for (int i = 0; i < fCount; i++)
        {
            Vector2 uvTop = WorldToUV(verts[i], p0, uDir, vDir);
            normals.Add(nFrontOut);
            uvs.Add(new Vector2(uvTop.x, uvTop.y));
        }
        // 8-2. Back Face's normals / uvs
        for (int i = 0; i < fCount; i++)
        {
            Vector2 uvBot = WorldToUV(verts[i + fCount], p0 + backOffset, uDir, vDir);
            normals.Add(-nFrontOut);
            uvs.Add(new Vector2(uvBot.x, uvBot.y));
        }

        // 9. Side Face
        // 세로 UV는 vLen(=height)
        // Ensure nOut
        List<Edge> borders = FindBorderEdges(frontIndices);
        for (int i = 0; i < borders.Count; i++)
        {
            int a = borders[i].A;
            int b = borders[i].B;
            
            
            // Front Vertices
            Vector3 aFront = verts[a];
            Vector3 bFront = verts[b];
            // Back Vertices
            Vector3 aBack = verts[a + fCount];
            Vector3 bBack = verts[b + fCount];
            
            //edgeDir
            Vector3 edgeDir = (bFront - aFront).normalized;
            Vector3 sideN = Vector3.Cross(edgeDir, nFrontOut).normalized;
            float edgeLen = Vector3.Distance(aFront, bFront);

            int baseIndex = verts.Count;
            
            // Copmute Normal of Triangle
            // if Dot(triN, sideN) < 0.0f -> then flip sideN & indices
            Vector3 triN = Vector3.Cross(aBack - aFront, bBack - aFront).normalized;
            bool flip = Vector3.Dot(triN, sideN) < 0.0f;
            
            // flipl sideN
            if (flip)
            {
                sideN = -sideN;
            }
            
            // Add Vertices / Normals / UVs / Indices
            
            // vertices
            verts.Add(aFront);
            verts.Add(aBack);
            verts.Add(bBack);
            verts.Add(bFront);
            // normals
            normals.Add(sideN); 
            normals.Add(sideN);
            normals.Add(sideN);
            normals.Add(sideN);
            // uvs
            uvs.Add(new Vector2(0.0f, 0.0f));
            uvs.Add(new Vector2(0.0f, vLen));
            uvs.Add(new Vector2(edgeLen, vLen));
            uvs.Add(new Vector2(edgeLen, 0.0f));

            if (!flip)
            {
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
            }
            // flip indices
            else
            {
                // reverse winding
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 2); indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 3); indices.Add(baseIndex + 2);
            }
        }

        // 10. Make Mesh
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
    // Edge's Key : (min, max) - ValueTuple Type : Order Insensitive
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
    
    // Make Vertices & Indices
    // Deduplication
    private static int GetOrCreateCornerVertex(
        List<Vector3> topVerts, int[,] vertIndex, int ui, int vi,
        float u, float v, Vector3 p0, Vector3 uDir, Vector3 vDir)
    {
        int idx = vertIndex[ui, vi];
        // if vertex already made, just return idx of vertex
        if (idx >= 0) return idx;
        
        //Make World Position Vertex with (u,v)
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
