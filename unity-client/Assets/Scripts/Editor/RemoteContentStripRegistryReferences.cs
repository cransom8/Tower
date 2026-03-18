#if UNITY_EDITOR
using CastleDefender.Game;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteContentStripRegistryReferences
    {
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";

        [MenuItem("Castle Defender/Remote Content/Strip Local Prefab References From Registry")]
        static void StripLocalPrefabReferences()
        {
            StripRegistryReferences(saveAssets: true);
        }

        public static bool StripRegistryReferences(bool saveAssets)
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
                ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
            if (registry == null)
            {
                Debug.LogError("[RemoteContentStripRegistryReferences] UnitPrefabRegistry not found.");
                return false;
            }

            int unitRefsCleared = 0;
            int skinRefsCleared = 0;

            if (registry.entries != null)
            {
                for (int i = 0; i < registry.entries.Length; i++)
                {
                    var entry = registry.entries[i];
                    if (entry.prefab == null) continue;
                    entry.prefab = null;
                    registry.entries[i] = entry;
                    unitRefsCleared++;
                }
            }

            if (registry.skinEntries != null)
            {
                for (int i = 0; i < registry.skinEntries.Length; i++)
                {
                    var entry = registry.skinEntries[i];
                    if (entry.prefab == null) continue;
                    entry.prefab = null;
                    registry.skinEntries[i] = entry;
                    skinRefsCleared++;
                }
            }

            registry.fallbackPrefab = null;
            registry.Rebuild();
            EditorUtility.SetDirty(registry);
            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[RemoteContentStripRegistryReferences] Cleared {unitRefsCleared} unit refs and {skinRefsCleared} skin refs from {AssetDatabase.GetAssetPath(registry)}.");
            return true;
        }
    }
}
#endif
