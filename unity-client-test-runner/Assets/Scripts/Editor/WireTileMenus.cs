// WireTileMenus.cs — Assigns TileMenuUI to TileMenuBehaviour on each TileGrid.
// Castle Defender → Setup → Wire Tile Menus
//
// Matches TileGrid.LaneIndex to the Nth TileMenuUI found in the Canvas,
// where N corresponds to lane order (0=first TileMenuUI, 1=second, etc.)

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CastleDefender.Game;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class WireTileMenus
    {
        [MenuItem("Castle Defender/Setup/Wire Tile Menus")]
        static void Run()
        {
            var tilegrids = Resources.FindObjectsOfTypeAll<TileGrid>();
            var tilemenus = Resources.FindObjectsOfTypeAll<TileMenuUI>();

            // Filter to scene objects only (exclude prefabs/assets)
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            tilegrids = System.Array.FindAll(tilegrids, t => t.gameObject.scene == activeScene);
            tilemenus = System.Array.FindAll(tilemenus, t => t.gameObject.scene == activeScene);

            if (tilemenus.Length == 0) { Debug.LogError("[WireTileMenus] No TileMenuUI found in scene."); return; }

            // Sort TileMenuUIs by sibling index so lane 0 = first in hierarchy
            System.Array.Sort(tilemenus, (a, b) =>
                a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            int wired = 0;
            foreach (var tg in tilegrids)
            {
                int lane = tg.LaneIndex;
                if (lane < 0 || lane >= tilemenus.Length)
                {
                    Debug.LogWarning($"[WireTileMenus] No TileMenuUI for LaneIndex {lane} on '{tg.name}'");
                    continue;
                }

                // Only wire the player-facing grids (named TileGrid_Lane*)
                // Skip the spectator Lane_* grids
                if (!tg.gameObject.name.StartsWith("TileGrid_")) continue;

                tg.TileMenuBehaviour = tilemenus[lane];
                EditorUtility.SetDirty(tg);
                Debug.Log($"[WireTileMenus] {tg.name} (lane {lane}) → {tilemenus[lane].gameObject.name}");
                wired++;
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            EditorUtility.DisplayDialog("Wire Tile Menus", $"Wired {wired} TileGrid(s) to TileMenuUI.", "OK");
        }
    }
}
