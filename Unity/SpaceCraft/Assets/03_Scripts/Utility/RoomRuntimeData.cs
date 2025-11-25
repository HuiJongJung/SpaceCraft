using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class RoomRuntimeData
{
    public int roomID;

    public List<GameObject> floors = new List<GameObject>();
    public List<GameObject> walls = new List<GameObject>();
    public List<GameObject> openings = new List<GameObject>();
    public List<Furniture> furnitures = new List<Furniture>();

    public RoomRuntimeData(int roomID)
    {
        this.roomID = roomID;
    }

    public void Add(RoomObject obj)
    {
        if (obj.type == RoomObjectType.Floor)
        {
            floors.Add(obj.gameObject);
        }
        else if (obj.type == RoomObjectType.Wall)
        {
            walls.Add(obj.gameObject);
        }
        else if (obj.type == RoomObjectType.Opening)
        {
            openings.Add(obj.gameObject);
        }
        else if (obj.type == RoomObjectType.Furniture)
        {
            furnitures.Add(obj.GetComponent<Furniture>());
        }
    }

    public void SetActive(bool active)
    {
        SetListActive(floors, active);
        SetListActive(walls, active);
        SetListActive(openings, active);
        SetFurnitureActive(furnitures, active);
    }

    // Floor / Wall / Openings Active
    private void SetListActive(List<GameObject> list, bool active)
    {
        for (int i = 0; i < list.Count; i++)
        {
            GameObject go = list[i];
            if (go != null)
            {
                go.SetActive(active);
            }
        }
    }
    
    // Furniture Active
    private void SetFurnitureActive(List<Furniture> list, bool active)
    {
        for (int i = 0; i < list.Count; i++)
        {
            GameObject go = list[i].gameObject;
            if (go != null)
            {
                go.SetActive(active);
            }
        }
    }
}

