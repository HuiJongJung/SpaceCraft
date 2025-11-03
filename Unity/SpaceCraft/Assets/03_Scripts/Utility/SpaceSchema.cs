using System;
using System.Collections.Generic;
using UnityEngine;

// SpaceLayout:
// - ceilingHeight: global ceiling height (m)
// - floors: list of FloorDef (global)
// - walls: list of WallDef  (global)
// - openings: list of OpeningDef (global, each references a wallID)
// - rooms: list of RoomRef (IDs only, no embedded geometry)
[Serializable]
public class SpaceLayout
{
    public float ceilingHeight = 2.6f;
    public float doorHeight = 2.1f;
    public float windowHeight = 0.8f;
    public float windowY = 1.0f;
    public List<FloorDef> floors;
    public List<WallDef> walls;
    public List<OpeningDef> openings;
    public List<RoomDef> rooms;
}

// FloorDef:
// - id: unique floor id
// - thickness: floor slab thickness (m)
// - vertices: mesh vertices (world Vec3)
// - indices: triangle indices
[Serializable]
public class FloorDef
{
    public int id;
    public float thickness;
    public List<Vec3> vertices;
    public List<int> indices;
}

// WallDef:
// - id: unique wall id
// - thickness: wall thickness (m) and opening depth
// - vertices: mesh vertices (world Vec3)
// - indices: triangle indices
[Serializable]
public class WallDef
{
    public int id;
    public List<Vec3> vertices;
    public List<int> indices;
}

// OpeningDef:
// - type: "door" | "window"
// - wallID: referenced wall id
// - position: opening center (world Vec3)
// - size: x=width, y=height (m)
// - hinge: "left" | "right" | null (doors)
// - inward: true if swings toward interior (doors)
[Serializable]
public class OpeningDef
{
    public string type;
    public int wallID;
    public Vec3 center;
    public float width;
    public string hinge;
    public bool inward;
}

// RoomRef:
// - roomID: stable string id (e.g., "1")
// - name: display name
// - floorIDs: list of floor ids in this room
// - wallIDs: list of wall ids forming this room
[Serializable]
public class RoomDef
{
    public int roomID;
    public string name;
    public List<int> floorIDs;
    public List<int> wallIDs;
}
