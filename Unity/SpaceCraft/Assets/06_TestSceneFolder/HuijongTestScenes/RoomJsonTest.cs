// RoomsOpeningsSmokeTest.cs
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class RoomJsonTest : MonoBehaviour
{
    private SpaceLayout rooms;

    void Start()
    {
        rooms = SpaceData.Instance._layout;
        TestJson();
    }

    [ContextMenu("Test Parse (Rooms & Openings)")]
    public void TestJson()
    {
        if (rooms == null)
        {
            Debug.LogError("[SmokeTest] SpaceLayout is null");
            return;
        }

        int roomCount = 0;
        if (rooms.rooms != null)
        {
            roomCount = rooms.rooms.Count;
        }
        Debug.Log("[SmokeTest] ceilingHeight=" + rooms.ceilingHeight + ", roomCount=" + roomCount);

        if (rooms.rooms == null || rooms.rooms.Count == 0)
        {
            Debug.Log("[SmokeTest] No Rooms");
            return;
        }

        for (int i = 0; i < rooms.rooms.Count; i++)
        {
            RoomDef r = rooms.rooms[i];

            string floors = JoinIntList(r.floorIDs);
            string walls  = JoinIntList(r.wallIDs);

            Debug.Log("[Room " + i + "] id=" + r.roomID + ", name=" + r.name +
                      ", floorIDs=[" + floors + "], wallIDs=[" + walls + "]");

            if (r.floorIDs == null || r.floorIDs.Count == 0)
            {
                Debug.LogWarning("[Room " + r.roomID + "] floorIDs is empty");
            }
            if (r.wallIDs == null || r.wallIDs.Count == 0)
            {
                Debug.LogWarning("[Room " + r.roomID + "] wallIDs is empty");
            }
        }
    }

    private string JoinIntList(List<int> list)
    {
        if (list == null || list.Count == 0)
        {
            return "";
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }
            sb.Append(list[i]);
        }
        return sb.ToString();
    }
}