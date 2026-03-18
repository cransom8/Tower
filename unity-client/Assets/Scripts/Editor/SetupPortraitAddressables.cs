#if UNITY_EDITOR
// SetupPortraitAddressables.cs
// Moves all portrait textures out of Resources/UnitPortraits/ into an addressable
// "Remote Portraits" group so they are no longer bundled inside the player build.
//
// Run once via: Castle Defender > Remote Content > Move Portraits to Addressables
// Then rebuild addressables: Castle Defender > Remote Content > Build Addressables Content

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class SetupPortraitAddressables
    {
        const string SourceFolder    = "Assets/Resources/UnitPortraits";
        const string DestFolder      = "Assets/AddressableContent/UnitPortraits";
        const string GroupName       = "Remote Portraits";
        const string TemplateGroup   = "Remote Units 01"; // copy remote path settings from here
        static readonly (string key, string sourcePath)[] SupplementalPortraitSources =
        {
            ("giant_rat", "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Giant Rat/Textures/T_GiantRat_BaseColor.png"),
            ("fantasy_wolf", "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Fantasy Wolf/Textures/T_FantasyWolf_BaseColor.png"),
        };

        [MenuItem("Castle Defender/Remote Content/Move Portraits to Addressables")]
        public static void MovePortraitsToAddressables()
        {
            // ── Resolve Addressables settings via reflection (version-safe) ──────
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Addressables editor API unavailable.");
                return;
            }

            object settings = null;
            var getSettings = settingsDefaultType.GetMethod(
                "GetSettings", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool) }, null);
            if (getSettings != null)
                settings = getSettings.Invoke(null, new object[] { true });
            settings ??= settingsDefaultType
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (settings == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Could not load AddressableAssetSettings.");
                return;
            }

            var settingsType = settings.GetType();

            var findGroupMethod = settingsType.GetMethod(
                "FindGroup", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            var createOrMoveEntryMethod = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateOrMoveEntry" && m.GetParameters().Length >= 2);
            var createGroupMethod = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "CreateGroup") return false;
                    var p = m.GetParameters();
                    return p.Length >= 4
                        && p[0].ParameterType == typeof(string)
                        && p[1].ParameterType == typeof(bool);
                });

            if (createOrMoveEntryMethod == null || createGroupMethod == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Required Addressables API methods not found.");
                return;
            }

            var bundledSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            var contentUpdateSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");
            var getSchemaMethod = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema" && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            // GetSchema lives on the group, not settings — grab it lazily from a group instance below

            // ── Ensure destination folder ─────────────────────────────────────
            if (!AssetDatabase.IsValidFolder("Assets/AddressableContent"))
                AssetDatabase.CreateFolder("Assets", "AddressableContent");
            if (!AssetDatabase.IsValidFolder(DestFolder))
                AssetDatabase.CreateFolder("Assets/AddressableContent", "UnitPortraits");

            // ── Find or create Remote Portraits group ─────────────────────────
            var group = findGroupMethod.Invoke(settings, new object[] { GroupName });
            if (group == null)
            {
                var templateGroupObj = findGroupMethod.Invoke(settings, new object[] { TemplateGroup });

                try
                {
                    var createParams = createGroupMethod.GetParameters();
                    var args = new object[createParams.Length];
                    for (int i = 0; i < createParams.Length; i++)
                    {
                        var p = createParams[i];
                        if (p.ParameterType == typeof(string))        args[i] = GroupName;
                        else if (p.ParameterType == typeof(bool))     args[i] = false;
                        else if (p.ParameterType == typeof(Type[]))   args[i] = new[] { bundledSchemaType, contentUpdateSchemaType };
                        else if (p.HasDefaultValue)                   args[i] = p.DefaultValue;
                        else args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                    }
                    group = createGroupMethod.Invoke(settings, args);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SetupPortraitAddressables] Failed to create group: {ex.Message}");
                    return;
                }

                // Copy schema (including remote path IDs) from the template group
                if (group != null && templateGroupObj != null)
                    CopySchemaSettings(templateGroupObj, group, bundledSchemaType, contentUpdateSchemaType);
            }

            if (group == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Failed to resolve Remote Portraits group.");
                return;
            }

            // ── Move each portrait and register as addressable ────────────────
            int synced = 0, skipped = 0;
            var sourceGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { SourceFolder });
            foreach (var guid in sourceGuids)
            {
                string srcPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(srcPath))
                    continue;

                if (SyncPortraitAsset(srcPath, null))
                    synced++;
                else
                    skipped++;
            }

            foreach (var supplemental in SupplementalPortraitSources)
            {
                if (SyncPortraitAsset(supplemental.sourcePath, supplemental.key))
                    synced++;
                else
                    skipped++;
            }

            var existingDestGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { DestFolder });
            foreach (var guid in existingDestGuids)
            {
                string destPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(destPath))
                    continue;

                if (SyncPortraitAsset(destPath, null))
                    synced++;
                else
                    skipped++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SetupPortraitAddressables] Done. Synced={synced} skipped={skipped}. " +
                      $"Remember to run Build Addressables Content before the next player build.");

            bool SyncPortraitAsset(string srcPath, string explicitKey)
            {
                if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath))
                {
                    Debug.LogWarning($"[SetupPortraitAddressables] Portrait source is missing: {srcPath}");
                    return false;
                }

                string key = string.IsNullOrWhiteSpace(explicitKey)
                    ? Path.GetFileNameWithoutExtension(srcPath)
                    : explicitKey.Trim();
                string destPath = $"{DestFolder}/{key}.png";

                if (!string.Equals(srcPath, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(destPath))
                    {
                        string error = AssetDatabase.CopyAsset(srcPath, destPath) ? null : "AssetDatabase.CopyAsset returned false.";
                        if (!string.IsNullOrEmpty(error))
                        {
                            Debug.LogWarning($"[SetupPortraitAddressables] Could not copy portrait for '{key}' from '{srcPath}' to '{destPath}': {error}");
                            return false;
                        }
                    }
                }

                string portraitGuid = AssetDatabase.AssetPathToGUID(destPath);
                if (string.IsNullOrWhiteSpace(portraitGuid))
                {
                    Debug.LogWarning($"[SetupPortraitAddressables] No GUID found for portrait '{key}' at '{destPath}'.");
                    return false;
                }

                string address = $"portraits/{key}";
                var entryArgs = createOrMoveEntryMethod.GetParameters();
                object entry;
                if (entryArgs.Length >= 4)
                    entry = createOrMoveEntryMethod.Invoke(settings, new[] { portraitGuid, group, (object)false, false });
                else if (entryArgs.Length == 3)
                    entry = createOrMoveEntryMethod.Invoke(settings, new[] { portraitGuid, group, (object)false });
                else
                    entry = createOrMoveEntryMethod.Invoke(settings, new[] { portraitGuid, group });

                if (entry != null)
                    entry.GetType()
                        .GetProperty("address", BindingFlags.Public | BindingFlags.Instance)
                        ?.SetValue(entry, address);

                return true;
            }
        }

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type bundledType, Type contentUpdateType)
        {
            var getSchema = sourceGroup.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchema == null) return;

            foreach (var schemaType in new[] { bundledType, contentUpdateType })
            {
                if (schemaType == null) continue;
                var src = getSchema.Invoke(sourceGroup, new object[] { schemaType });
                var dst = getSchema.Invoke(targetGroup, new object[] { schemaType });
                if (src == null || dst == null) continue;

                foreach (var prop in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || !prop.CanWrite) continue;
                    if (string.Equals(prop.Name, "Group", StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    try { prop.SetValue(dst, prop.GetValue(src)); } catch { }
                }

                EditorUtility.SetDirty(dst as UnityEngine.Object);
                EditorUtility.SetDirty(targetGroup as UnityEngine.Object);
            }
        }
    }
}
#endif
