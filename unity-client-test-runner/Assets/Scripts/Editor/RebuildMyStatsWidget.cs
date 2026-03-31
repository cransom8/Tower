using UnityEditor;
using UnityEngine;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class RebuildMyStatsWidget
    {
        const string MenuPath = "Castle Defender/UI/Rebuild My Stats Widget";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var hud = Object.FindFirstObjectByType<MobileMatchHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("[RebuildMyStatsWidget] No MobileMatchHud found in the active scene.");
                return;
            }

            hud.ForceRebuildNow();
            EditorUtility.SetDirty(hud.gameObject);
            Debug.Log("[RebuildMyStatsWidget] Rebuilt My Stats widget.");
        }
    }
}
