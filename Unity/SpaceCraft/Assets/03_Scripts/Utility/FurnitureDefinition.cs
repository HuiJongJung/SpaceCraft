using UnityEngine;

[System.Serializable]
public struct WallPlacementDirection
{
    public bool front;
    public bool back;
    public bool left;
    public bool right;
}

[System.Serializable]
public struct FunctionalClearanceCm
{
    public int front;
    public int back;
    public int left;
    public int right;
}

[System.Serializable]
public struct PrivacyDirection
{
    public bool front;
    public bool back;
    public bool left;
    public bool right;
}

[System.Serializable]
public struct FurnitureSizeTemplate
{
    public Vector3 sizeCentimeters;   // X=width, Y=height, Z=depth
}

[CreateAssetMenu(menuName = "SpaceCraft/Furniture Definition")]
public class FurnitureDefinition : ScriptableObject
{
    public string id;                        // Furniture Type ID ex) "Bed1"
    public string name;                      // Furniture Name ex) "침대"
    public Sprite sprite;                    // Sprite Image
    public GameObject prefab;                // Furniture Prefab
    public FurnitureSizeTemplate[] sizeTemplates; // Template List
}