// MultiLaneSceneBuilder.cs — Ensures Game_ML and Game_Survival scenes have the correct
// GameObjects for the 4-lane H-shaped battlefield.
//
// Menu: Castle Defender → Setup → Build 4-Lane Scene
//       Castle Defender → Setup → Apply to Both Game Scenes
//
// "Build 4-Lane Scene" operates on the currently open scene.
// "Apply to Both Game Scenes" saves current scene, then opens Game_ML + Game_Survival
// in sequence, runs the build on each, and saves them.
//
// What it creates / wires:
//   • TileGrid        — single instance; handles the player's viewed branch interactively
//   • LaneRenderer    — single instance; renders units for all 4 branches simultaneously
//   • SnapshotApplier — singleton that feeds ML snapshots to both TileGrid and LaneRenderer
//   • NetworkManager  — if absent (needed by SnapshotApplier.OnEnable)
//
// After running, also run "Wire Registry and Scene" to assign tile prefabs + UnitPrefabRegistry.

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CastleDefender.Game;
using CastleDefender.Net;

namespace CastleDefender.Editor
{
    public static class MultiLaneSceneBuilder
    {
        // All paths from EditorPaths.cs — do not redeclare here.

        // ── Menu items ────────────────────────────────────────────────────────

        [MenuItem("Castle Defender/Setup/Build 4-Lane Scene")]
        static void BuildCurrentScene()
        {
            BuildScene();
            Debug.Log("[MultiLane] Done. Run 'Wire Registry and Scene' next if prefabs are unassigned.");
        }

        [MenuItem("Castle Defender/Setup/Apply to Both Game Scenes")]
        static void ApplyToBothScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var sceneML = EditorSceneManager.OpenScene(EditorPaths.SCENE_ML, OpenSceneMode.Single);
            BuildScene();
            EditorSceneManager.SaveScene(sceneML);
            Debug.Log("[MultiLane] Game_ML built and saved.");

            var sceneSurv = EditorSceneManager.OpenScene(EditorPaths.SCENE_SURVIVAL, OpenSceneMode.Single);
            BuildScene();
            EditorSceneManager.SaveScene(sceneSurv);
            Debug.Log("[MultiLane] Game_Survival built and saved.");

            Debug.Log("[MultiLane] Both scenes ready. Run 'Wire Registry and Scene' on each to assign prefabs.");
        }

        // ── Core builder ──────────────────────────────────────────────────────

        static void BuildScene()
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(EditorPaths.REGISTRY);
            if (registry == null)
                Debug.LogWarning("[MultiLane] UnitPrefabRegistry not found at " + EditorPaths.REGISTRY +
                                 " — run 'Wire Registry and Scene' afterward.");

            EnsureTileGrid(registry);
            EnsureLaneRenderer(registry);
            EnsureSnapshotApplier();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ── TileGrid (4 instances, one per lane) ──────────────────────────────

        static readonly string[] LaneNames = { "TileGrid_Lane0_Red", "TileGrid_Lane1_Gold", "TileGrid_Lane2_Blue", "TileGrid_Lane3_Green" };

        static void EnsureTileGrid(UnitPrefabRegistry registry)
        {
            var floorPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPaths.TILE_FLOOR);
            var wallPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPaths.TILE_WALL);
            var castlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPaths.TILE_CASTLE);
            var cam          = Camera.main ?? Object.FindFirstObjectByType<Camera>();

            // Build a lookup of existing TileGrids by LaneIndex so we don't duplicate
            var existing = new System.Collections.Generic.Dictionary<int, TileGrid>();
            foreach (var tg in Object.FindObjectsByType<TileGrid>(FindObjectsSortMode.None))
                existing[tg.LaneIndex] = tg;

            for (int lane = 0; lane < 4; lane++)
            {
                TileGrid tileGrid;
                if (existing.TryGetValue(lane, out tileGrid))
                {
                    Debug.Log($"[MultiLane] Found existing TileGrid for lane {lane}.");
                }
                else
                {
                    tileGrid = new GameObject(LaneNames[lane]).AddComponent<TileGrid>();
                    tileGrid.LaneIndex = lane;
                    Debug.Log($"[MultiLane] Created {LaneNames[lane]}.");
                }

                // IsInteractive defaults false; TileGrid.Update() auto-sets it at runtime
                tileGrid.IsInteractive = false;

                if (registry != null)    tileGrid.Registry     = registry;
                if (floorPrefab  != null) tileGrid.FloorPrefab  = floorPrefab;
                if (wallPrefab   != null) tileGrid.WallPrefab   = wallPrefab;
                if (castlePrefab != null) tileGrid.CastlePrefab = castlePrefab;
                if (cam != null && tileGrid.Cam == null) tileGrid.Cam = cam;

                EditorUtility.SetDirty(tileGrid);
            }

            Debug.Log("[MultiLane] 4 TileGrids wired (lanes 0-3).");
        }

        // ── LaneRenderer ──────────────────────────────────────────────────────

        static void EnsureLaneRenderer(UnitPrefabRegistry registry)
        {
            var lr = Object.FindFirstObjectByType<LaneRenderer>();
            if (lr == null)
            {
                lr = new GameObject("LaneRenderer").AddComponent<LaneRenderer>();
                Debug.Log("[MultiLane] Created LaneRenderer.");
            }

            if (registry != null && lr.Registry == null)
                lr.Registry = registry;

            EditorUtility.SetDirty(lr);
            Debug.Log("[MultiLane] LaneRenderer wired.");
        }

        // ── SnapshotApplier + NetworkManager ──────────────────────────────────

        static void EnsureSnapshotApplier()
        {
            // NetworkManager must exist for SnapshotApplier.OnEnable to find it
            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (nm == null)
            {
                nm = new GameObject("NetworkManager").AddComponent<NetworkManager>();
                Debug.Log("[MultiLane] Created NetworkManager.");
            }

            var sa = Object.FindFirstObjectByType<SnapshotApplier>();
            if (sa == null)
            {
                nm.gameObject.AddComponent<SnapshotApplier>();
                Debug.Log("[MultiLane] Added SnapshotApplier to NetworkManager.");
            }
        }
    }
}
#endif
