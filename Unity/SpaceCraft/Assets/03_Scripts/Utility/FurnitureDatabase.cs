using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "SpaceCraft/Furniture Database")]
public class FurnitureDatabase : ScriptableObject
{
    public FurnitureDefinition[] definitions;

    private Dictionary<string, FurnitureDefinition> map;

    public void Initialize()
    {
        if (map != null) return;

        map = new Dictionary<string, FurnitureDefinition>();
        for (int i = 0; i < definitions.Length; i++)
        {
            FurnitureDefinition def = definitions[i];
            if (def == null || string.IsNullOrEmpty(def.id)) continue;
            if (map.ContainsKey(def.id)) continue;
            map.Add(def.id, def);
        }
    }

    public FurnitureDefinition GetById(string id)
    {
        if (map == null) Initialize();
        FurnitureDefinition def;
        bool found = map.TryGetValue(id, out def);
        return found ? def : null;
    }
}