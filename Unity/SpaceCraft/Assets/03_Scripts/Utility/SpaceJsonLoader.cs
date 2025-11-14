using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;


public static class SpaceJsonLoader
{
    //Load Room Information
    public static SpaceLayout LoadSpaceLayout(TextAsset json)
    {
        if (json == null) { Debug.LogError("rooms.json TextAsset is null"); return null; }
        return JsonConvert.DeserializeObject<SpaceLayout>(json.text);
    }
    
    //Load Furniture Information
    //LoadFurnitures
}