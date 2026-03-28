// BattlefieldSceneSetup.cs - Editor utility for the H-shaped battlefield scene.
// Menu: Castle Defender -> Setup -> Validate Battlefield Scene
//       Castle Defender -> Setup -> Frame Battlefield in Scene View
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

            var presentationRoot = Object.FindFirstObjectByType<GameplayPresentationRoot>();
            if (presentationRoot == null)
            {
                Debug.LogError("[BattlefieldSetup] No GameplayPresentationRoot found in scene. Add a GameObject with GameplayPresentationRoot component.");
                errors++;
            }
            else
            {
                if (presentationRoot.Registry == null)
                {
                    Debug.LogWarning("[BattlefieldSetup] GameplayPresentationRoot.Registry not assigned.");
                    warnings++;
                }
                else
                {
                    Debug.Log("[BattlefieldSetup] GameplayPresentationRoot OK.");
                }
            }

            var fortressSelection = Object.FindFirstObjectByType<FortressSelectionController>(FindObjectsInactive.Include);
            if (fortressSelection == null)
            {
                Debug.LogWarning("[BattlefieldSetup] No FortressSelectionController found in scene.");
                warnings++;
            }
            else
            {
                Debug.Log("[BattlefieldSetup] FortressSelectionController OK.");
            }

            var fortressPads = Object.FindObjectsByType<FortressPadAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (fortressPads == null || fortressPads.Length == 0)
            {
                Debug.LogWarning("[BattlefieldSetup] No authored FortressPadAnchor objects found in scene.");
                warnings++;
            }
            else
            {
                Debug.Log($"[BattlefieldSetup] FortressPadAnchor count = {fortressPads.Length}.");
            }

            var barracksSites = Object.FindObjectsByType<BarracksSiteView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (barracksSites == null || barracksSites.Length == 0)
            {
                Debug.LogWarning("[BattlefieldSetup] No authored BarracksSiteView objects found in scene.");
                warnings++;
            }
            else
            {
                Debug.Log($"[BattlefieldSetup] BarracksSiteView count = {barracksSites.Length}.");
            }

            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogWarning("[BattlefieldSetup] No NetworkManager found in scene.");
                warnings++;
            }
            else
            {
                Debug.Log("[BattlefieldSetup] NetworkManager OK.");
            }

            var sa = Object.FindFirstObjectByType<SnapshotApplier>();
            if (sa == null)
            {
                Debug.LogWarning("[BattlefieldSetup] No SnapshotApplier found in scene.");
                warnings++;
            }
            else
            {
                Debug.Log("[BattlefieldSetup] SnapshotApplier OK.");
            }

            if (errors == 0 && warnings == 0)
                Debug.Log("[BattlefieldSetup] All checks passed.");
            else
                Debug.Log($"[BattlefieldSetup] Done - {errors} error(s), {warnings} warning(s). Check Console for details.");
        }

        [MenuItem("Castle Defender/Setup/Frame Battlefield in Scene View")]
        static void FrameBattlefield()
        {
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
            Debug.Log("[BattlefieldSetup] Scene View framed on battlefield (pivot=0, angle=60, size=35).");
        }
    }
}
#endif
