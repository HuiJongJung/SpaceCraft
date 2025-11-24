using UnityEngine;
using UnityEditor;

public static class FitBoxColliderOnPrefabs
{
    [MenuItem("Tools/Colliders/Fit BoxCollider To Prefab Assets (Selection)")]
    private static void FitToPrefabs()
    {
        Object[] selection = Selection.objects;
        if (selection == null || selection.Length == 0)
        {
            Debug.LogWarning("선택된 에셋이 없습니다.");
            return;
        }

        int processedCount = 0;

        for (int i = 0; i < selection.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(selection[i]);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            if (prefabRoot == null)
            {
                continue;
            }

            Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                continue;
            }

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
            {
                bounds.Encapsulate(renderers[r].bounds);
            }

            BoxCollider box = prefabRoot.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = prefabRoot.AddComponent<BoxCollider>();
            }

            Transform t = prefabRoot.transform;

            // 월드 center → 로컬 center
            Vector3 localCenter = t.InverseTransformPoint(bounds.center);

            // 월드 사이즈 → 로컬 사이즈 (lossyScale 보정)
            Vector3 worldSize = bounds.size;
            Vector3 lossy = t.lossyScale;
            Vector3 localSize = Vector3.zero;

            if (lossy.x != 0f)
            {
                localSize.x = worldSize.x / lossy.x;
            }
            if (lossy.y != 0f)
            {
                localSize.y = worldSize.y / lossy.y;
            }
            if (lossy.z != 0f)
            {
                localSize.z = worldSize.z / lossy.z;
            }

            box.center = localCenter;
            box.size = localSize;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            processedCount = processedCount + 1;
        }

        Debug.Log("프리팹 BoxCollider 자동 맞춤 완료. 처리한 프리팹 수: " + processedCount);
    }
}
