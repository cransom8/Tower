// BattlefieldSceneSetup.cs — Editor utility for the H-shaped battlefield scene.
// Menu: Castle Defender → Setup → Validate Battlefield Scene
//       Castle Defender → Setup → Frame Battlefield in Scene View
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using CastleDefender.Game;
using CastleDefender.Net;

namespace CastleDefender.Editor
{
    public static class BattlefieldSceneSetup
    {
        [MenuItem("Castle Defender/Setup/Validate Battlefield Scene")]
        static void ValidateScene()
        {
            int errors = 0, warnings = 0;

            // ── TileGrid ──────────────────────────────────────────────────────
            var tileGrid = Object.FindFirstObjectByType<TileGrid>();
            if (tileGrid == null)
            {
                Debug.LogError("[BattlefieldSetup] No TileGrid found in scene. Add a GameObject with TileGrid component.");
                errors++;
            }
            else
            {
                if (tileGrid.FloorPrefab  == null) { Debug.LogWarning("[BattlefieldSetup] TileGrid.FloorPrefab not assigned.");  warnings++; }
                if (tileGrid.WallPrefab   == null) { Debug.LogWarning("[BattlefieldSetup] TileGrid.WallPrefab not assigned.");   warnings++; }
                if (tileGrid.CastlePrefab == null) { Debug.LogWarning("[BattlefieldSetup] TileGrid.CastlePrefab not assigned."); warnings++; }
                if (tileGrid.Registry     == null) { Debug.LogWarning("[BattlefieldSetup] TileGrid.Registry not assigned.");     warnings++; }
                if (tileGrid.Cam          == null) { Debug.LogWarning("[BattlefieldSetup] TileGrid.Cam not assigned.");          warnings++; }
                if (tileGrid.TileMenuBehaviour     == null) { Debug.LogWarning("[BattlefieldSetup] TileGrid.TileMenu not assigned.");     warnings++; }
                else Debug.Log("[BattlefieldSetup] TileGrid OK.");
            }

            // ── LaneRenderer ─────────────────────────────────────────────────
            var laneRenderer = Object.FindFirstObjectByType<LaneRenderer>();
            if (laneRenderer == null)
            {
                Debug.LogError("[BattlefieldSetup] No LaneRenderer found in scene. Add a GameObject with LaneRenderer component.");
                errors++;
            }
            else
            {
                if (laneRenderer.Registry == null) { Debug.LogWarning("[BattlefieldSetup] LaneRenderer.Registry not assigned."); warnings++; }
                else Debug.Log("[BattlefieldSetup] LaneRenderer OK.");
            }

            // ── NetworkManager ────────────────────────────────────────────────
            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (nm == null) { Debug.LogWarning("[BattlefieldSetup] No NetworkManager found in scene."); warnings++; }
            else Debug.Log("[BattlefieldSetup] NetworkManager OK.");

            // ── SnapshotApplier ───────────────────────────────────────────────
            var sa = Object.FindFirstObjectByType<SnapshotApplier>();
            if (sa == null) { Debug.LogWarning("[BattlefieldSetup] No SnapshotApplier found in scene."); warnings++; }
            else Debug.Log("[BattlefieldSetup] SnapshotApplier OK.");

            // ── Summary ───────────────────────────────────────────────────────
            if (errors == 0 && warnings == 0)
                Debug.Log("[BattlefieldSetup] All checks passed.");
            else
                Debug.Log($"[BattlefieldSetup] Done — {errors} error(s), {warnings} warning(s). Check Console for details.");
        }

        [MenuItem("Castle Defender/Setup/Frame Battlefield in Scene View")]
        static void FrameBattlefield()
        {
            // Position the scene view camera to see the full H-shaped battlefield.
            // Battlefield spans roughly X: -30 to +30, Z: -15 to +15.
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
            {
                Debug.LogWarning("[BattlefieldSetup] No active Scene View found.");
                return;
            }

            sv.pivot = Vector3.zero;
            sv.rotation = Quaternion.Euler(60f, 0f, 0f);
            sv.size = 35f;
            sv.Repaint();
            Debug.Log("[BattlefieldSetup] Scene View framed on battlefield (pivot=0, angle=60°, size=35).");
        }
    }
}
#endif
