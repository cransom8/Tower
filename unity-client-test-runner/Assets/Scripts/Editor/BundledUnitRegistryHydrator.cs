#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CastleDefender.Game;

namespace CastleDefender.Editor
{
    public static class BundledUnitRegistryHydrator
    {
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";
        const string GeneratedPrefabRoot = "Assets/Prefabs/Units/HFC";

        [MenuItem("Castle Defender/Build/Hydrate Bundled Unit Registry")]
        public static void HydrateBundledRegistryMenu()
        {
            var result = HydrateBundledRegistry(logWarnings: true);
            if (!result.RegistryFound)
            {
                Debug.LogError("[BundledUnitRegistryHydrator] Unit prefab registry not found.");
                return;
            }

            Debug.Log(
                $"[BundledUnitRegistryHydrator] baseRefsFilled={result.BaseRefsFilled}, " +
                $"baseRefsMissing={result.BaseRefsMissing}, fallbackAssigned={result.FallbackAssigned}.");
        }

        public static HydrationResult HydrateBundledRegistry(bool logWarnings)
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
                ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);

            if (registry == null)
                return default;

            var result = new HydrationResult { RegistryFound = true };
            bool changed = false;

            if (registry.entries != null)
            {
                for (int i = 0; i < registry.entries.Length; i++)
                {
                    var entry = registry.entries[i];
                    if (!string.IsNullOrWhiteSpace(entry.key) && entry.prefab == null)
                    {
                        string prefabPath = $"{GeneratedPrefabRoot}/Unit_{entry.key.Trim()}.prefab";
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        if (prefab != null)
                        {
                            entry.prefab = prefab;
                            registry.entries[i] = entry;
                            result.BaseRefsFilled++;
                            changed = true;
                        }
                        else
                        {
                            result.BaseRefsMissing++;
                            if (logWarnings)
                                Debug.LogWarning($"[BundledUnitRegistryHydrator] Missing bundled prefab for '{entry.key}' at {prefabPath}");
                        }
                    }
                }
            }

            if (registry.fallbackPrefab == null)
            {
                for (int i = 0; registry.entries != null && i < registry.entries.Length; i++)
                {
                    if (registry.entries[i].prefab == null)
                        continue;

                    registry.fallbackPrefab = registry.entries[i].prefab;
                    result.FallbackAssigned = true;
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                registry.Rebuild();
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
            }

            return result;
        }

        public struct HydrationResult
        {
            public bool RegistryFound;
            public int BaseRefsFilled;
            public int BaseRefsMissing;
            public bool FallbackAssigned;
        }
    }
}
#endif
