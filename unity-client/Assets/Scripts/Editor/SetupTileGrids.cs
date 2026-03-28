// SetupTileGrids.cs — One-click setup: creates 4 TileGrid GameObjects in Game_ML,
// each wired to tile prefabs, camera, and UnitPrefabRegistry.
// Run once via Castle Defender → Setup → Setup TileGrids.

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using CastleDefender.Game;

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

                Debug.Log($"[SetupTileGrids] Lane {lane} ({laneNames[lane]}) ready.");
            }

            EditorUtility.SetDirty(parent);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SetupTileGrids] Done — 4 TileGrid GameObjects created. Save the scene (Ctrl+S).");
        }
    }
}
