#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDefender.Game;
using CastleDefender.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CastleDefender.Editor
{
    public static class UnitPortraitBakeTools
    {
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";
        const string PortraitRootFolder = "Assets/AddressableContent/UnitPortraits";

        [MenuItem("Castle Defender/Remote Content/Bake Portrait For Selected Unit Prefab")]
        static void BakePortraitForSelectedUnitPrefab()
        {
            var registry = LoadRegistry();
            if (registry == null)
                return;

            var selectedPrefab = Selection.activeObject as GameObject;
            if (selectedPrefab == null)
            {
                Debug.LogWarning("[UnitPortraitBake] Select a unit prefab asset first.");
                return;
            }

            string unitKey = FindUnitKeyForPrefab(registry, selectedPrefab);
            if (string.IsNullOrWhiteSpace(unitKey))
            {
                Debug.LogWarning($"[UnitPortraitBake] Could not resolve a registry unit key for selected prefab '{selectedPrefab.name}'.");
                return;
            }

            if (!TryBakePortrait(registry, unitKey, out var result))
                return;

            SyncPortraitAddressables();
            Debug.Log($"[UnitPortraitBake] Baked portrait unit_key='{result.unitKey}' portrait_key='{result.portraitKey}' source='{result.portraitSource}' path='{result.assetPath}' address='{result.address}'.");
        }

        [MenuItem("Castle Defender/Remote Content/Bake TT Portraits")]
        static void BakeAllTtPortraits()
        {
            var registry = LoadRegistry();
            if (registry == null)
                return;

            var keys = GetTtUnitKeys(registry);
            if (keys.Count == 0)
            {
                Debug.LogWarning("[UnitPortraitBake] No TT unit entries were found in the registry.");
                return;
            }

            int baked = 0;
            int failed = 0;
            var bakedResults = new List<PortraitBakeResult>();
            foreach (string unitKey in keys)
            {
                if (TryBakePortrait(registry, unitKey, out var result))
                {
                    baked++;
                    bakedResults.Add(result);
                }
                else
                {
                    failed++;
                }
            }

            SyncPortraitAddressables();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = bakedResults.Count == 0
                ? "<none>"
                : string.Join(", ", bakedResults.Select(r => $"{r.unitKey}->{r.address}"));
            Debug.Log($"[UnitPortraitBake] TT batch complete. baked={baked} failed={failed} portraits=[{summary}]");
        }

        [MenuItem("Castle Defender/Remote Content/Bake Giant Rat Portrait")]
        static void BakeGiantRatPortrait()
        {
            BakePortraitForUnitKeyOrThrow("giant_rat");
        }

        public static void BakePortraitForUnitKeyCli()
        {
            string[] args = Environment.GetCommandLineArgs();
            string unitKey = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-portraitUnitKey", StringComparison.OrdinalIgnoreCase))
                {
                    unitKey = args[i + 1];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(unitKey))
                throw new InvalidOperationException("[UnitPortraitBake] Missing -portraitUnitKey <unit_key> command line argument.");

            BakePortraitForUnitKeyOrThrow(unitKey.Trim());
        }

        static UnitPrefabRegistry LoadRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
                ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
            if (registry == null)
                Debug.LogError("[UnitPortraitBake] UnitPrefabRegistry not found.");
            else
                registry.Rebuild();
            return registry;
        }

        static void BakePortraitForUnitKeyOrThrow(string unitKey)
        {
            var registry = LoadRegistry();
            if (registry == null)
                throw new InvalidOperationException("[UnitPortraitBake] UnitPrefabRegistry not found.");

            if (!TryBakePortrait(registry, unitKey, out var result))
                throw new InvalidOperationException($"[UnitPortraitBake] Failed to bake portrait for '{unitKey}'.");

            SyncPortraitAddressables();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"[UnitPortraitBake] Portrait bake complete unit_key='{result.unitKey}' portrait_key='{result.portraitKey}' " +
                $"path='{result.assetPath}' address='{result.address}'.");
        }

        static List<string> GetTtUnitKeys(UnitPrefabRegistry registry)
        {
            var keys = new List<string>();
            if (registry?.entries == null)
                return keys;

            foreach (var entry in registry.entries)
            {
                string key = entry.key?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (!key.StartsWith("tt_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    keys.Add(key);
            }

            return keys;
        }

        static string FindUnitKeyForPrefab(UnitPrefabRegistry registry, GameObject prefab)
        {
            if (registry?.entries == null || prefab == null)
                return null;

            string selectedPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrWhiteSpace(selectedPath))
                return null;

            foreach (var entry in registry.entries)
            {
                if (entry.prefab == null || string.IsNullOrWhiteSpace(entry.key))
                    continue;

                string entryPath = AssetDatabase.GetAssetPath(entry.prefab);
                if (string.Equals(entryPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                    return entry.key.Trim();
            }

            return null;
        }

        static bool TryBakePortrait(UnitPrefabRegistry registry, string unitKey, out PortraitBakeResult result)
        {
            result = null;
            if (registry == null || string.IsNullOrWhiteSpace(unitKey))
                return false;

            if (!registry.TryGet(unitKey, out var entry) || entry.prefab == null)
            {
                Debug.LogWarning($"[UnitPortraitBake] Registry prefab missing for unit '{unitKey}'.");
                return false;
            }

            EnsurePortraitFolders();

            GameObject studioRoot = null;
            RenderTexture studioTexture = null;
            Texture2D bakedTexture = null;
            try
            {
                var portraitCam = RuntimePortraitStudio.Create("EditorPortraitBakeStudio", registry, out studioRoot, out studioTexture);
                if (portraitCam == null)
                {
                    Debug.LogError($"[UnitPortraitBake] Failed to create portrait studio for '{unitKey}'.");
                    return false;
                }

                bakedTexture = portraitCam.CaptureIconImmediate(unitKey);
                if (bakedTexture == null)
                {
                    Debug.LogError($"[UnitPortraitBake] Portrait capture returned null for '{unitKey}'.");
                    return false;
                }

                string assetPath = GetPortraitAssetPath(unitKey);
                WriteTextureAsset(bakedTexture, assetPath);
                ConfigureTextureImporter(assetPath);

                result = new PortraitBakeResult
                {
                    unitKey = unitKey,
                    prefabKey = unitKey,
                    portraitKey = unitKey,
                    portraitSource = "baked_asset",
                    portraitFallbackKey = null,
                    assetPath = assetPath,
                    address = $"portraits/{unitKey}",
                };

                Debug.Log($"[UnitPortraitBake] Captured unit_key='{result.unitKey}' prefab='{entry.prefab.name}' portrait_key='{result.portraitKey}' source='{result.portraitSource}' path='{result.assetPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnitPortraitBake] Failed to bake portrait for '{unitKey}': {ex.Message}");
                return false;
            }
            finally
            {
                if (bakedTexture != null)
                    Object.DestroyImmediate(bakedTexture);

                if (studioRoot != null)
                    Object.DestroyImmediate(studioRoot);

                if (studioTexture != null)
                {
                    studioTexture.Release();
                    Object.DestroyImmediate(studioTexture);
                }
            }
        }

        static void EnsurePortraitFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/AddressableContent"))
                AssetDatabase.CreateFolder("Assets", "AddressableContent");
            if (!AssetDatabase.IsValidFolder(PortraitRootFolder))
                AssetDatabase.CreateFolder("Assets/AddressableContent", "UnitPortraits");
        }

        static string GetPortraitAssetPath(string unitKey) => $"{PortraitRootFolder}/{unitKey}.png";

        static void WriteTextureAsset(Texture2D texture, string assetPath)
        {
            byte[] png = texture.EncodeToPNG();
            if (png == null || png.Length == 0)
                throw new InvalidOperationException($"PNG encoding returned no bytes for '{assetPath}'.");

            string absolutePath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllBytes(absolutePath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        static void ConfigureTextureImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        static void SyncPortraitAddressables()
        {
            SetupPortraitAddressables.MovePortraitsToAddressables();
        }

        sealed class PortraitBakeResult
        {
            public string unitKey;
            public string prefabKey;
            public string portraitKey;
            public string portraitSource;
            public string portraitFallbackKey;
            public string assetPath;
            public string address;
        }
    }
}
#endif
