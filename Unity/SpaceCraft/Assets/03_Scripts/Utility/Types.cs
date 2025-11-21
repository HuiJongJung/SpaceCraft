using System;

// Vec3: x,y,z in world units (meters)
[Serializable]
public struct Vec3 { public float x; public float y; public float z; }

// Vec2: x=width, y=height (meters)
[Serializable]
public struct Vec2 { public float x; public float y; }

public enum OpeningType
{
    None = 0,
    Door,
    SlideDoor,
    Window
}