#if UNITY_EDITOR
// SetupPortraitAddressables.cs
// Syncs unit portraits into the "Remote Portraits" Addressables group so they are
// no longer bundled inside the player build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CastleDefender.Game;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class SetupPortraitAddressables
    {
        const string SourceFolder = "Assets/Resources/UnitPortraits";
        const string DestFolder = "Assets/AddressableContent/UnitPortraits";
        const string GroupName = "Remote Portraits";
        const string TemplateGroup = "Remote Units 01";
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";

        static readonly (string key, string sourcePath)[] SupplementalPortraitSources =
        {
            ("fantasy_wolf", "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Fantasy Wolf/Textures/T_FantasyWolf_BaseColor.png"),
        };

        [MenuItem("Castle Defender/Remote Content/Move Portraits to Addressables")]
        public static void MovePortraitsToAddressables()
        {
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Addressables editor API unavailable.");
                return;
            }

            object settings = ResolveSettings(settingsDefaultType);
            if (settings == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Could not load AddressableAssetSettings.");
                return;
            }

            var settingsType = settings.GetType();
            var findGroupMethod = settingsType.GetMethod(
                "FindGroup",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            var createOrMoveEntryMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateOrMoveEntry" && m.GetParameters().Length >= 2);
            var createGroupMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "CreateGroup")
                        return false;

                    var parameters = m.GetParameters();
                    return parameters.Length >= 4
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(bool);
                });

            if (findGroupMethod == null || createOrMoveEntryMethod == null || createGroupMethod == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Required Addressables API methods not found.");
                return;
            }

            var bundledSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            var contentUpdateSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");

            EnsureFolder("Assets/AddressableContent");
            EnsureFolder(DestFolder);

            object group = EnsureGroup(
                settings,
                findGroupMethod,
                createGroupMethod,
                bundledSchemaType,
                contentUpdateSchemaType);
            if (group == null)
            {
                Debug.LogError("[SetupPortraitAddressables] Failed to resolve Remote Portraits group.");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
                ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
            var sourcePortraitsByKey = BuildTextureLookup(SourceFolder);
            var existingPortraitsByKey = BuildTextureLookup(DestFolder);
            var supplementalPortraitsByKey = SupplementalPortraitSources.ToDictionary(
                entry => entry.key,
                entry => entry.sourcePath,
                StringComparer.OrdinalIgnoreCase);
            var desiredPortraitKeys = BuildDesiredPortraitKeys(registry);
            if (desiredPortraitKeys.Count == 0)
            {
                foreach (string key in sourcePortraitsByKey.Keys)
                    desiredPortraitKeys.Add(key);
                foreach (string key in existingPortraitsByKey.Keys)
                    desiredPortraitKeys.Add(key);

                Debug.LogWarning(
                    "[SetupPortraitAddressables] UnitPrefabRegistry was unavailable or empty. " +
                    "Falling back to broad portrait sync without registry-based pruning.");
            }

            var portraitPlan = BuildPortraitPlan(
                desiredPortraitKeys,
                sourcePortraitsByKey,
                existingPortraitsByKey,
                supplementalPortraitsByKey);

            int pruned = ClearManagedEntries(group);
            int synced = 0;
            int missing = Math.Max(0, desiredPortraitKeys.Count - portraitPlan.Count);
            for (int i = 0; i < portraitPlan.Count; i++)
            {
                if (EnsurePortraitAssetInAddressables(portraitPlan[i], settings, group, createOrMoveEntryMethod))
                    synced++;
                else
                    missing++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"[SetupPortraitAddressables] Done. Synced={synced} missing={missing} pruned={pruned} requested={desiredPortraitKeys.Count}. " +
                "Remember to run Build Addressables Content before the next player build.");
        }

        static object ResolveSettings(Type settingsDefaultType)
        {
            object settings = null;
            var getSettings = settingsDefaultType.GetMethod(
                "GetSettings",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            if (getSettings != null)
                settings = getSettings.Invoke(null, new object[] { true });

            settings ??= settingsDefaultType
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            return settings;
        }

        static object EnsureGroup(
            object settings,
            MethodInfo findGroupMethod,
            MethodInfo createGroupMethod,
            Type bundledSchemaType,
            Type contentUpdateSchemaType)
        {
            object group = findGroupMethod.Invoke(settings, new object[] { GroupName });
            if (group != null)
                return group;

            object templateGroup = findGroupMethod.Invoke(settings, new object[] { TemplateGroup });
            try
            {
                var parameters = createGroupMethod.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    if (parameter.ParameterType == typeof(string))
                        args[i] = GroupName;
                    else if (parameter.ParameterType == typeof(bool))
                        args[i] = false;
                    else if (parameter.ParameterType == typeof(Type[]))
                        args[i] = new[] { bundledSchemaType, contentUpdateSchemaType };
                    else if (parameter.HasDefaultValue)
                        args[i] = parameter.DefaultValue;
                    else
                        args[i] = parameter.ParameterType.IsValueType
                            ? Activator.CreateInstance(parameter.ParameterType)
                            : null;
                }

                group = createGroupMethod.Invoke(settings, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SetupPortraitAddressables] Failed to create group: {ex.Message}");
                return null;
            }

            if (group != null && templateGroup != null)
                CopySchemaSettings(templateGroup, group, bundledSchemaType, contentUpdateSchemaType);
            return group;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static HashSet<string> BuildDesiredPortraitKeys(UnitPrefabRegistry registry)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (registry != null)
            {
                foreach (var entry in registry.entries ?? Array.Empty<UnitPrefabRegistry.Entry>())
                {
                    if (!string.IsNullOrWhiteSpace(entry.key))
                        keys.Add(entry.key.Trim());
                }

                foreach (var skinEntry in registry.skinEntries ?? Array.Empty<UnitPrefabRegistry.SkinEntry>())
                {
                    if (!string.IsNullOrWhiteSpace(skinEntry.skinKey))
                        keys.Add(skinEntry.skinKey.Trim());
                    if (!string.IsNullOrWhiteSpace(skinEntry.unitType))
                        keys.Add(skinEntry.unitType.Trim());
                }
            }

            foreach (var supplemental in SupplementalPortraitSources)
            {
                if (!string.IsNullOrWhiteSpace(supplemental.key))
                    keys.Add(supplemental.key.Trim());
            }

            return keys;
        }

        static Dictionary<string, string> BuildTextureLookup(string folderPath)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return lookup;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                string key = Path.GetFileNameWithoutExtension(assetPath)?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                    lookup[key] = assetPath;
            }

            return lookup;
        }

        static List<PortraitPlanEntry> BuildPortraitPlan(
            HashSet<string> desiredPortraitKeys,
            Dictionary<string, string> sourcePortraitsByKey,
            Dictionary<string, string> existingPortraitsByKey,
            Dictionary<string, string> supplementalPortraitsByKey)
        {
            var plan = new List<PortraitPlanEntry>(desiredPortraitKeys.Count);
            foreach (string key in desiredPortraitKeys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string existingDestPath = existingPortraitsByKey.TryGetValue(key, out string existingPath)
                    ? existingPath
                    : null;
                string sourcePath = existingDestPath;
                if (string.IsNullOrWhiteSpace(sourcePath)
                    && supplementalPortraitsByKey.TryGetValue(key, out string supplementalPath))
                {
                    sourcePath = supplementalPath;
                }

                if (string.IsNullOrWhiteSpace(sourcePath)
                    && sourcePortraitsByKey.TryGetValue(key, out string sourceLookupPath))
                {
                    sourcePath = sourceLookupPath;
                }

                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[SetupPortraitAddressables] Portrait source is missing for '{key}'.");
                    continue;
                }

                string destPath = string.IsNullOrWhiteSpace(existingDestPath)
                    ? $"{DestFolder}/{key}.png"
                    : existingDestPath;
                if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(destPath))
                {
                    string error = AssetDatabase.CopyAsset(sourcePath, destPath)
                        ? null
                        : "AssetDatabase.CopyAsset returned false.";
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning(
                            $"[SetupPortraitAddressables] Could not copy portrait for '{key}' from '{sourcePath}' to '{destPath}': {error}");
                        continue;
                    }
                }

                plan.Add(new PortraitPlanEntry(key, destPath));
            }

            return plan;
        }

        static bool EnsurePortraitAssetInAddressables(
            PortraitPlanEntry portrait,
            object settings,
            object group,
            MethodInfo createOrMoveEntryMethod)
        {
            string portraitGuid = AssetDatabase.AssetPathToGUID(portrait.AssetPath);
            if (string.IsNullOrWhiteSpace(portraitGuid))
            {
                Debug.LogWarning(
                    $"[SetupPortraitAddressables] No GUID found for portrait '{portrait.Key}' at '{portrait.AssetPath}'.");
                return false;
            }

            string address = $"portraits/{portrait.Key}";
            object entry = CreateOrMoveEntry(settings, createOrMoveEntryMethod, portraitGuid, group);
            if (entry == null)
            {
                Debug.LogWarning($"[SetupPortraitAddressables] Failed to create Addressables entry for '{address}'.");
                return false;
            }

            entry.GetType()
                .GetProperty("address", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(entry, address);
            return true;
        }

        static object CreateOrMoveEntry(object settings, MethodInfo createOrMoveEntryMethod, string guid, object group)
        {
            var parameters = createOrMoveEntryMethod.GetParameters();
            if (parameters.Length >= 4)
                return createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false, false });
            if (parameters.Length == 3)
                return createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false });

            return createOrMoveEntryMethod.Invoke(settings, new[] { guid, group });
        }

        static int ClearManagedEntries(object group)
        {
            if (group is not UnityEngine.Object groupObject)
                return 0;

            var serialized = new SerializedObject(groupObject);
            var entries = serialized.FindProperty("m_SerializeEntries");
            if (entries == null || entries.arraySize == 0)
                return 0;

            int removed = entries.arraySize;
            entries.ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(groupObject);
            return removed;
        }

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type bundledType, Type contentUpdateType)
        {
            var getSchema = sourceGroup.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchema == null)
                return;

            foreach (var schemaType in new[] { bundledType, contentUpdateType })
            {
                if (schemaType == null)
                    continue;

                var sourceSchema = getSchema.Invoke(sourceGroup, new object[] { schemaType });
                var targetSchema = getSchema.Invoke(targetGroup, new object[] { schemaType });
                if (sourceSchema == null || targetSchema == null)
                    continue;

                foreach (var property in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || !property.CanWrite)
                        continue;
                    if (string.Equals(property.Name, "Group", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (property.GetIndexParameters().Length != 0)
                        continue;

                    try
                    {
                        property.SetValue(targetSchema, property.GetValue(sourceSchema));
                    }
                    catch
                    {
                        // Ignore non-copyable schema properties.
                    }
                }

                EditorUtility.SetDirty(targetSchema as UnityEngine.Object);
                EditorUtility.SetDirty(targetGroup as UnityEngine.Object);
            }
        }

        readonly struct PortraitPlanEntry
        {
            public PortraitPlanEntry(string key, string assetPath)
            {
                Key = key;
                AssetPath = assetPath;
            }

            public string Key { get; }
            public string AssetPath { get; }
        }
    }
}
#endif
