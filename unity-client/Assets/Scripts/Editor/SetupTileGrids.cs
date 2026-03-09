// SetupTileGrids.cs — One-click setup: creates 4 TileGrid GameObjects in Game_ML,
// each wired to the correct TileMenuUI, tile prefabs, camera, and UnitPrefabRegistry.
// Run once via Castle Defender → Setup → Setup TileGrids.

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using CastleDefender.Game;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class SetupTileGrids
    {
        [MenuItem("Castle Defender/Setup/Setup TileGrids")]
        public static void Run()
        {
            Debug.Log("[SetupTileGrids] Starting...");

            // ── Find prefabs ───────────────────────────────────────────────────
            var floorPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/FloorTile.prefab");
            var wallPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/WallTile.prefab");
            var castlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/CastleTile.prefab");
            var registry     = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(
                "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/HD and URP Support Packs/UnitPrefabRegistry.asset");

            Debug.Log($"[SetupTileGrids] floor={floorPrefab != null} wall={wallPrefab != null} castle={castlePrefab != null} registry={registry != null}");

            if (floorPrefab == null || wallPrefab == null || castlePrefab == null)
            {
                Debug.LogError("[SetupTileGrids] Could not find tile prefabs in Assets/Prefabs/Tiles/");
                return;
            }

            // ── Find camera ────────────────────────────────────────────────────
            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            Debug.Log($"[SetupTileGrids] cam={cam?.name ?? "null"}");
            if (cam == null) { Debug.LogError("[SetupTileGrids] No camera found."); return; }

            // ── Find all TileMenuUI components in scene ────────────────────────
            // Resources.FindObjectsOfTypeAll finds inactive GOs too (editor-only).
            var allMenusRaw = Resources.FindObjectsOfTypeAll<TileMenuUI>();
            // Filter to only scene objects (exclude prefab assets)
            var menuList = new System.Collections.Generic.List<TileMenuUI>();
            foreach (var m in allMenusRaw)
                if (m.gameObject.scene.isLoaded) menuList.Add(m);
            var allMenus = menuList.ToArray();
            Debug.Log($"[SetupTileGrids] found {allMenus.Length} TileMenuUI(s) in loaded scene");
            if (allMenus.Length < 4)
            {
                Debug.LogError($"[SetupTileGrids] Need at least 4 TileMenuUI components in scene, found {allMenus.Length}");
                return;
            }
            System.Array.Sort(allMenus, (a, b) =>
                a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            // ── Remove any existing TileGrids parent ───────────────────────────
            var existing = GameObject.Find("TileGrids");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                Debug.Log("[SetupTileGrids] Removed existing TileGrids parent.");
            }

            // ── Create parent and 4 lane grids ─────────────────────────────────
            var parent = new GameObject("TileGrids");
            Undo.RegisterCreatedObjectUndo(parent, "Setup TileGrids");

            string[] laneNames = {
                "TileGrid_Lane0_Red",
                "TileGrid_Lane1_Gold",
                "TileGrid_Lane2_Blue",
                "TileGrid_Lane3_Green"
            };

            for (int lane = 0; lane < 4; lane++)
            {
                var go = new GameObject(laneNames[lane]);
                go.transform.SetParent(parent.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Setup TileGrid Lane " + lane);

                var tg = go.AddComponent<TileGrid>();
                tg.LaneIndex     = lane;
                tg.IsInteractive = true;
                tg.Cols          = 11;
                tg.Rows          = 28;
                tg.FloorPrefab   = floorPrefab;
                tg.WallPrefab    = wallPrefab;
                tg.CastlePrefab  = castlePrefab;
                tg.Registry      = registry;
                tg.Cam           = cam;
                tg.TileMenu      = allMenus[lane];

                Debug.Log($"[SetupTileGrids] Lane {lane} ({laneNames[lane]}) → TileMenuUI '{allMenus[lane].name}'");
            }

            EditorUtility.SetDirty(parent);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SetupTileGrids] Done — 4 TileGrid GameObjects created. Save the scene (Ctrl+S).");
        }
    }
}
