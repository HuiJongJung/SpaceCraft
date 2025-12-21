#if UNITY_EDITOR
using System.Drawing;
using System.IO;
using UnityEditor;
using UnityEngine;

public class PreviewGenerator : EditorWindow
{
    private int     textureSize = 512;
    private Vector3 cameraDir = new Vector3(0f, 0.5f, 1f);  // from the model to the camera.
    private float   boundsMargin = 1.2f;                    // to pad the model bounds.

    private FurnitureDatabase targetDB;

    #region Custom Tool
    [MenuItem("Tools/Furniture Preview Generator")]
    private static void Window()
    {
        var window = GetWindow<PreviewGenerator>("Furniture Preview Generator");
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Generator Setting", EditorStyles.boldLabel);
        EditorGUILayout.Space();


        // 1) Target Furniture Database
        targetDB = (FurnitureDatabase)EditorGUILayout.ObjectField(
            "Furniture Database",
            targetDB,
            typeof(FurnitureDatabase),
            false);

        // 2) Preview Texture Resolution.
        textureSize = EditorGUILayout.IntPopup(
            "Texture Size",
            textureSize,
            new[] { "256", "512", "1024" },
            new[] { 256, 512, 1024 });

        // 3) Preview image padding factor
        boundsMargin = EditorGUILayout.Slider(
            "Bounds Margin",
            boundsMargin, 
            1.05f, 
            2.0f);

        // 4) Camera Direction (from the model to the camera)
        cameraDir = EditorGUILayout.Vector3Field("Camera Direction", cameraDir);


        if (GUILayout.Button("Generate Previews"))
        {
            if (targetDB == null)
            {
                EditorUtility.DisplayDialog("Error", "Furniture Database is not assigned.", "OK");
                return;
            }

            // Generate previews
            GeneratePreviews(targetDB);
        }
    }
    #endregion

    #region Render & Save
    /// Renders the given camera into a RenderTexture and returns the result as a Texture2D.
    private Texture2D RenderToTexture(Camera cam, int width, int height)
    {
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;

        RenderTexture prevRT = RenderTexture.active;
        RenderTexture prevCamRT = cam.targetTexture;

        try
        {
            cam.targetTexture = rt;
            RenderTexture.active = rt;
            cam.Render();

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            return tex;
        }
        finally
        {
            cam.targetTexture = prevCamRT;
            RenderTexture.active = prevRT;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    private void SaveTexture(Texture2D tex, string assetPath)
    {
        /* =============================== */
        // Save Texture2D in png file.
        /* =============================== */
        byte[] pngData = tex.EncodeToPNG();
        File.WriteAllBytes(assetPath, pngData);
    }

    private void ImportAsSprite(string assetPath)
    {
        /* =============================== */
        // Import png file into Sprite.
        /* =============================== */
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
    #endregion

    #region Create Environment

    // Create Camera
    private Camera CreateCamera(Transform parent)
    {
        GameObject camObj = new GameObject("Camera");
        camObj.transform.SetParent(parent, false);

        UnityEngine.Color backColor;
        ColorUtility.TryParseHtmlString("#D6D8D6", out backColor);

        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        //cam.backgroundColor = backgroundColor;
        cam.orthographic = true;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        cam.backgroundColor = backColor;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        return cam;
    }

    // Create Directional Light
    private Light CreateLight(Transform parent)
    {
        GameObject lightObj = new GameObject("Light");
        lightObj.transform.SetParent(parent, false);
        lightObj.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;

        return light;
    }

    // Create a prefab instance to be rendered as a preview image.
    private GameObject InstantiatePrefab(GameObject prefab, Transform parent)
    {
        // Use PrefabUtility so the connection to the original prefab is preserved.
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance != null)
        {
            instance.transform.SetParent(parent, false);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
        return instance;
    }
    #endregion

    #region Settings
    /// Configures the camera so that the given bounds fully fit within the view.
    /// - Camera position is offset along cameraDir (default: z-, y+ direction).
    /// - The camera looks at the center of the model.
    /// - orthographicSize is set to the maximum extent of the bounds multiplied by the margin.
    private void SetupCameraForBounds(Camera cam, Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        // Normalize camera direction.
        Vector3 dir = cameraDir.normalized;
        if (dir == Vector3.zero)
        {
            dir = new Vector3(0f, 0.5f, -1f).normalized;
        }

        // Approximate radius of the bounds.
        float radius = extents.magnitude;

        // Place the camera at roughly radius * 2 units away from the center along dir.
        float distance = radius * 2.0f;
        cam.transform.position = center + dir * distance;
        cam.transform.LookAt(center);

        // Set the orthographic camera's vertical half size.
        float maxExtent = Mathf.Max(extents.x, extents.y, extents.z);
        cam.orthographicSize = maxExtent * boundsMargin;

        // Adjust clipping planes.
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = distance + radius * 2.0f;
    }
    #endregion

    #region Generate Preview Images

    private bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        /* =============================== */
        // Compute the combined bounds using all child renderers.
        /* =============================== */
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            return false;
        }

        bounds = new Bounds(renderers[0].bounds.center, renderers[0].bounds.size);
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return true;
    }

    private void GeneratePreviews(FurnitureDatabase db)
    {
        /* =============================== */
        // Generate preview images for every FurnitureDefinition in the given FurnitureDatabase.
        /* =============================== */
        if (db.definitions == null || db.definitions.Length == 0)
        {
            EditorUtility.DisplayDialog("Info",
                "No FurnitureDefinition entries are defined in the database.",
                "OK");
            return;
        }

        string dirPath = "Assets/09_ScriptableObject/FurnitureDefinition/00_Previews";
        if (!Directory.Exists(dirPath))
        { 
            Directory.CreateDirectory(dirPath);
        }

        // Create a temporary root, camera, and light used during preview rendering.
        GameObject root = new GameObject("PreviewRoot");
        root.hideFlags = HideFlags.HideAndDontSave;

        Camera cam = CreateCamera(root.transform);
        Light light = CreateLight(root.transform);

        try
        {
            int count = 0;

            for (int i = 0; i < db.definitions.Length; i++)
            {
                var def = db.definitions[i];
                if (def == null)
                    continue;

                if (def.prefab == null)
                {
                    Debug.LogWarning($"[Preview Generator] Prefab for '{{def.id}}' is missing. Skipping.");
                    continue;
                }

                // Instantiate prefab to render the preview images.
                GameObject instance = InstantiatePrefab(def.prefab, root.transform);

                if (instance == null)
                {
                    Debug.LogWarning($"[Preview Generator] Failed to instantiate prefab for '{{def.id}}'.");
                    continue;
                }

                // Calculate bounds.
                if (!TryGetRendererBounds(instance, out Bounds bounds))
                {
                    Debug.LogWarning($"[Preview Generator] No Renderer found on '{{def.id}}'. Skipping.");
                    DestroyImmediate(instance);
                    continue;
                }

                // Configure camera position and size based on bounds.
                SetupCameraForBounds(cam, bounds);

                // Render and save as PNG.
                string fileName = $"{def.id}_preview.png";
                string assetPath = Path.Combine(dirPath, fileName).Replace("\\", "/");

                Texture2D tex = RenderToTexture(cam, textureSize, textureSize);
                SaveTexture(tex, assetPath);

                // Import the PNG as a Sprite.
                ImportAsSprite(assetPath);

                // Load the Sprite and assign it to the ScriptableObject.
                Sprite spriteAsset = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (spriteAsset != null)
                {
                    def.sprite = spriteAsset;
                    EditorUtility.SetDirty(def);
                }

                DestroyImmediate(tex);
                DestroyImmediate(instance);

                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            //EditorUtility.DisplayDialog("Complete",
            //    $"Preview generation finished.\\nGenerated {{count}} preview images in total.",
            //    "OK");
        }
        finally
        {
            if (root != null)
                DestroyImmediate(root);
        }
    }

    #endregion

}
#endif