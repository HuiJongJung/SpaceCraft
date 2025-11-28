using UnityEngine;
using System.Collections.Generic;

public class SpaceData : MonoBehaviour {
    public static SpaceData Instance { get; private set; }
    public SpaceLayout _layout { get; private set; }
    //WallID - Openings
    public Dictionary<int, List<OpeningDef>> opsByWall { get; private set; }
    
    [Header("Test Jsons")]
    public TextAsset roomsJson;

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        //Load JSON
        LoadData();
    }

    //Load Data
    public void LoadData()
    {
        if (roomsJson != null)
        {
            _layout = SpaceJsonLoader.LoadSpaceLayout(roomsJson);
            CreateOpeningsByWallMap();
        }
    }
    
    // Create Wall-Openings Dictionary (Called at the time of LoadData)
    public void CreateOpeningsByWallMap()
    {
        // 1. Make opsByWall Dictionary : Mapping "wallID" - "openings"
        opsByWall = new Dictionary<int, List<OpeningDef>>();
        
        if (_layout.openings != null)
        {
            for (int i = 0; i < _layout.openings.Count; i++)
            {
                OpeningDef od = _layout.openings[i];
                if (od == null) continue;
                List<OpeningDef> lst;
                // If nothing has a wallID as a key, Create new List
                if (!opsByWall.TryGetValue(od.wallID, out lst))
                {
                    lst = new List<OpeningDef>();
                    opsByWall.Add(od.wallID, lst);
                }
                //else Add od to List
                lst.Add(od);
            }
        }
    }
}
