using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class RoomObject : MonoBehaviour
{
    public List<int> roomIDs = new List<int>(); // roomID
    public RoomObjectType type;      // Floor/Wall/Openings
    public int logicalID;            // FloorID / WallID / OpeningID
}