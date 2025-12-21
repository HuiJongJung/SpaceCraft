using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
#endif

public class FontChanger : MonoBehaviour
{
    [Header("Target Fonts")]
    public TMP_FontAsset targetTMPFont; // TextMeshPro / TextMeshProUGUI 에 사용할 폰트
    public Font targetLegacyFont;       // UnityEngine.UI.Text 에 사용할 폰트

    [ContextMenu("Apply Font To All Texts In Scene (Include Prefabs)")]
    public void ApplyFontsInScene()
    {
        if (targetTMPFont == null && targetLegacyFont == null)
        {
            Debug.LogWarning("No target fonts assigned.");
            return;
        }

#if UNITY_EDITOR
        // 변경된 프리팹 루트 모음 (중복 Apply 방지)
        HashSet<GameObject> modifiedPrefabRoots = new HashSet<GameObject>();
#endif

        /* =============================== */
        /*     TextMeshProUGUI (UI용)      */
        /* =============================== */
        var tmpUIs = FindObjectsOfType<TextMeshProUGUI>(true); // 비활성 포함
        foreach (var t in tmpUIs)
        {
            if (targetTMPFont != null && t.font != targetTMPFont)
            {
                t.font = targetTMPFont;

#if UNITY_EDITOR
                // 프리팹 인스턴스라면, 해당 프리팹 루트를 기록
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if (root != null)
                    modifiedPrefabRoots.Add(root);
#endif
            }
        }

        /* =============================== */
        /*     TextMeshPro (3D 텍스트)     */
        /* =============================== */
        var tmps = FindObjectsOfType<TextMeshPro>(true);
        foreach (var t in tmps)
        {
            if (targetTMPFont != null && t.font != targetTMPFont)
            {
                t.font = targetTMPFont;

#if UNITY_EDITOR
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if (root != null)
                    modifiedPrefabRoots.Add(root);
#endif
            }
        }

        /* =============================== */
        /*    Legacy UI Text (UGUI Text)   */
        /* =============================== */
        var legacyTexts = FindObjectsOfType<Text>(true);
        foreach (var t in legacyTexts)
        {
            if (targetLegacyFont != null && t.font != targetLegacyFont)
            {
                t.font = targetLegacyFont;

#if UNITY_EDITOR
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if (root != null)
                    modifiedPrefabRoots.Add(root);
#endif
            }
        }

#if UNITY_EDITOR
        /* =============================== */
        /*      프리팹 에셋에 Apply        */
        /* =============================== */
        foreach (var root in modifiedPrefabRoots)
        {
            // 변경 사항을 프리팹에 기록 후 Apply
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction);
        }

        Debug.Log($"Font apply finished. Modified prefab count: {modifiedPrefabRoots.Count}");
#else
        Debug.Log("Font apply finished. (Prefab apply is editor only)");
#endif
    }
}
