// CleanupTileMenuUI.cs — Fixes the ghost dark-panel bug:
//   1. Removes the outer Image background from all TileMenuUI GOs (only PanelTileMenu needs it)
//   2. Deletes orphaned TileMenuUI/BarracksPanel/GameOverUI sets beyond the first 4 of each
// Run via: Castle Defender → Setup → Cleanup TileMenuUI
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using CastleDefender.UI;

public static class CleanupTileMenuUI
{
    [MenuItem("Castle Defender/Setup/Cleanup TileMenuUI Ghost Panel")]
    public static void Run()
    {
        var allMenus = Object.FindObjectsByType<TileMenuUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[CleanupTileMenuUI] Found {allMenus.Length} TileMenuUI instances.");

        // Sort by sibling index so we keep the first 4 (wired to TileGrids)
        System.Array.Sort(allMenus, (a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

        for (int i = 0; i < allMenus.Length; i++)
        {
            var menu = allMenus[i];
            bool isOrphan = i >= 4;

            if (isOrphan)
            {
                // Delete orphaned set: TileMenuUI + sibling BarracksPanel + GameOverUI
                // They appear in groups of 3 in sibling order
                var parent = menu.transform.parent;
                int sib = menu.transform.GetSiblingIndex();

                // Collect the 3 siblings at this position (TileMenuUI, BarracksPanel, GameOverUI)
                var toDelete = new System.Collections.Generic.List<GameObject>();
                for (int s = sib; s < sib + 3 && s < parent.childCount; s++)
                {
                    var child = parent.GetChild(s).gameObject;
                    // Only delete if it's one of the expected types
                    if (child.GetComponent<TileMenuUI>() != null ||
                        child.GetComponent<BarracksPanel>() != null ||
                        child.GetComponent<GameOverUI>() != null)
                    {
                        toDelete.Add(child);
                    }
                }
                foreach (var go in toDelete)
                {
                    Debug.Log($"[CleanupTileMenuUI] Deleting orphan: {go.name} (sibling {go.transform.GetSiblingIndex()})");
                    Undo.DestroyObjectImmediate(go);
                }
            }
            else
            {
                // Remove or zero-alpha the outer Image on kept TileMenuUI GOs
                var img = menu.GetComponent<Image>();
                if (img != null)
                {
                    Undo.RecordObject(img, "Zero outer TileMenuUI Image");
                    var c = img.color;
                    c.a = 0f;
                    img.color = c;
                    img.raycastTarget = false; // no longer needed as hitbox
                    Debug.Log($"[CleanupTileMenuUI] Zeroed outer Image alpha on {menu.name} (sibling {menu.transform.GetSiblingIndex()})");
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[CleanupTileMenuUI] Done. Save the scene (Ctrl+S).");
    }
}
