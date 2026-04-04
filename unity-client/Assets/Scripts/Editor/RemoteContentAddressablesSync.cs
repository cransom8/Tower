#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CastleDefender.Game;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteContentAddressablesSync
    {
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";
        const string RemoteUnitsGroupPrefix = "Remote Units";
        const string RemoteSkinsGroupPrefix = "Remote Skins";
        const string RemoteSkinsSharedGroupName = "Remote Skins Shared";
        const string RemoteBuildPathProfileId = "165fb4a3ad8d19e4aa002d6fc764a7ce";
        const string RemoteLoadPathProfileId = "247226ff3fd294f46b8dfca266320b8c";
        const int DefaultUnitGroupCount = 6;
        const int UnitGroupCount = 10;
        const int SkinGroupCount = 3;
        const string SharedLabel = "shared";
        const string SharedSkinLabel = "skin-shared";
        static readonly string[] SharedSkinDependencyRoots =
        {
            "Assets/ExplosiveLLC/",
            "Assets/Materials/TT/",
            "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/models/",
        };
        static readonly Dictionary<string, int> UnitGroupOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            // Slimming pass 1: split the heaviest legacy bucket (Remote Units 03)
            ["cyclops"] = 7,
            ["demon_lord"] = 7,
            ["ogre"] = 7,
            ["troll"] = 8,
            ["werewolf"] = 8,
            // Slimming pass 2: decompose Remote Units 05 into three smaller buckets
            ["mountain_dragon"] = 9,
            ["evil_watcher"] = 9,
            ["wyvern"] = 10,
            ["fantasy_wolf"] = 10,
        };

        [MenuItem("Castle Defender/Remote Content/Sync Registry To Addressables")]
        static void SyncRegistryToAddressables()
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
                ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
            if (registry == null)
            {
                Debug.LogError("[RemoteContentAddressablesSync] UnitPrefabRegistry not found.");
                return;
            }

            var api = AddressablesSyncApi.TryCreate();
            if (!api.IsAvailable)
            {
                Debug.LogError("[RemoteContentAddressablesSync] Addressables API unavailable. Make sure Addressables is installed and initialized.");
                return;
            }

            api.EnsureSplitGroups();
            api.ClearLegacyEntries();

            int synced = 0;
            int warnings = 0;
            int sharedSynced = 0;

            if (registry.entries != null)
            {
                foreach (var entry in registry.entries)
                {
                    if (TrySyncEntry(api, "unit", entry.key, entry.prefab, $"units/{entry.key}"))
                        synced++;
                    else
                        warnings++;
                }
            }

            if (registry.skinEntries != null)
            {
                foreach (var entry in registry.skinEntries)
                {
                    if (TrySyncEntry(api, "skin", entry.skinKey, entry.prefab, $"skins/{entry.skinKey}"))
                        synced++;
                    else
                        warnings++;
                }
            }

            sharedSynced = SyncSharedSkinDependencies(api, registry);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RemoteContentAddressablesSync] Synced={synced} shared={sharedSynced} warnings={warnings}");
        }

        [MenuItem("Castle Defender/Remote Content/Sync Shared Skin Dependencies To Addressables")]
        static void SyncSharedSkinDependenciesToAddressables()
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
                ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
            if (registry == null)
            {
                Debug.LogError("[RemoteContentAddressablesSync] UnitPrefabRegistry not found.");
                return;
            }

            var api = AddressablesSyncApi.TryCreate();
            if (!api.IsAvailable)
            {
                Debug.LogError("[RemoteContentAddressablesSync] Addressables API unavailable. Make sure Addressables is installed and initialized.");
                return;
            }

            int sharedSynced = SyncSharedSkinDependencies(api, registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RemoteContentAddressablesSync] Shared skin dependencies synced={sharedSynced}");
        }

        static bool TrySyncEntry(AddressablesSyncApi api, string kindLabel, string key, GameObject prefab, string address)
        {
            if (prefab == null || string.IsNullOrWhiteSpace(key)) return false;

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(guid)) return false;

            try
            {
                string groupName = GetGroupName(kindLabel, key);
                var entry = api.CreateOrMoveEntry(guid, groupName);
                if (entry == null)
                {
                    Debug.LogWarning($"[RemoteContentAddressablesSync] Failed to create Addressables entry for '{key}'.");
                    return false;
                }

                var entryType = entry.GetType();
                entryType.GetProperty("address", BindingFlags.Public | BindingFlags.Instance)?.SetValue(entry, address);

                api.RegisterLabel(kindLabel);
                api.RegisterLabel(key);
                api.SetEntryLabel(entry, kindLabel);
                api.SetEntryLabel(entry, key);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteContentAddressablesSync] Failed to sync '{key}': {ex.Message}");
                return false;
            }
        }

        static string GetGroupName(string kindLabel, string key)
        {
            int bucket;
            if (string.Equals(kindLabel, "skin", StringComparison.OrdinalIgnoreCase))
            {
                bucket = GetBucketIndex(key, SkinGroupCount);
            }
            else if (UnitGroupOverrides.TryGetValue(key ?? string.Empty, out int explicitGroupNumber))
            {
                bucket = Math.Clamp(explicitGroupNumber, 1, UnitGroupCount) - 1;
            }
            else
            {
                bucket = GetBucketIndex(key, DefaultUnitGroupCount);
            }

            string prefix = string.Equals(kindLabel, "skin", StringComparison.OrdinalIgnoreCase)
                ? RemoteSkinsGroupPrefix
                : RemoteUnitsGroupPrefix;
            return $"{prefix} {bucket + 1:D2}";
        }

        static int GetBucketIndex(string value, int bucketCount)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? string.Empty)
                {
                    hash ^= c;
                    hash *= 16777619;
                }

                return (int)(hash % Math.Max(1, bucketCount));
            }
        }

        static int SyncSharedSkinDependencies(AddressablesSyncApi api, UnitPrefabRegistry registry)
        {
            if (api == null || !api.IsAvailable || registry == null)
                return 0;

            var dependencyUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var skinPrefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (registry.skinEntries == null)
                return 0;

            foreach (var entry in registry.skinEntries)
            {
                if (entry.prefab == null || string.IsNullOrWhiteSpace(entry.skinKey))
                    continue;

                string skinPrefabPath = AssetDatabase.GetAssetPath(entry.prefab);
                if (string.IsNullOrWhiteSpace(skinPrefabPath))
                    continue;

                skinPrefabPaths.Add(skinPrefabPath);

                var perPrefabDependencies = AssetDatabase.GetDependencies(skinPrefabPath, true)
                    .Where(IsSharedSkinDependencyCandidate)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (string dependencyPath in perPrefabDependencies)
                {
                    dependencyUsageCounts.TryGetValue(dependencyPath, out int count);
                    dependencyUsageCounts[dependencyPath] = count + 1;
                }
            }

            var sharedDependencyPaths = dependencyUsageCounts
                .Where(pair => pair.Value > 1 && !skinPrefabPaths.Contains(pair.Key))
                .Select(pair => pair.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            api.ClearManagedEntries(RemoteSkinsSharedGroupName);
            api.RegisterLabel(SharedLabel);
            api.RegisterLabel(SharedSkinLabel);

            int synced = 0;
            for (int i = 0; i < sharedDependencyPaths.Length; i++)
            {
                string dependencyPath = sharedDependencyPaths[i];
                string guid = AssetDatabase.AssetPathToGUID(dependencyPath);
                if (string.IsNullOrWhiteSpace(guid))
                    continue;

                var entry = api.CreateOrMoveEntry(guid, RemoteSkinsSharedGroupName);
                if (entry == null)
                    continue;

                string address = BuildSharedDependencyAddress(dependencyPath);
                entry.GetType().GetProperty("address", BindingFlags.Public | BindingFlags.Instance)?.SetValue(entry, address);

                api.SetEntryLabel(entry, SharedLabel);
                api.SetEntryLabel(entry, SharedSkinLabel);
                synced++;
            }

            return synced;
        }

        static bool IsSharedSkinDependencyCandidate(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = 0; i < SharedSkinDependencyRoots.Length; i++)
            {
                if (assetPath.StartsWith(SharedSkinDependencyRoots[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static string BuildSharedDependencyAddress(string assetPath)
        {
            string withoutExtension = System.IO.Path.ChangeExtension(assetPath, null)?.Replace('\\', '/')
                ?? assetPath.Replace('\\', '/');
            const string assetsPrefix = "Assets/";
            if (withoutExtension.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
                withoutExtension = withoutExtension.Substring(assetsPrefix.Length);

            return $"skins/shared/{withoutExtension}";
        }

        sealed class AddressablesSyncApi
        {
            readonly object _settings;
            readonly object _defaultGroup;
            readonly MethodInfo _createOrMoveEntryMethod;
            readonly MethodInfo _settingsSetLabelMethod;
            readonly MethodInfo _entrySetLabelMethod;
            readonly MethodInfo _findGroupMethod;
            readonly MethodInfo _createGroupMethod;
            readonly MethodInfo _getSchemaMethod;
            readonly Type _bundledSchemaType;
            readonly Type _contentUpdateSchemaType;

            AddressablesSyncApi(
                object settings,
                object defaultGroup,
                MethodInfo createOrMoveEntryMethod,
                MethodInfo settingsSetLabelMethod,
                MethodInfo entrySetLabelMethod,
                MethodInfo findGroupMethod,
                MethodInfo createGroupMethod,
                MethodInfo getSchemaMethod,
                Type bundledSchemaType,
                Type contentUpdateSchemaType)
            {
                _settings = settings;
                _defaultGroup = defaultGroup;
                _createOrMoveEntryMethod = createOrMoveEntryMethod;
                _settingsSetLabelMethod = settingsSetLabelMethod;
                _entrySetLabelMethod = entrySetLabelMethod;
                _findGroupMethod = findGroupMethod;
                _createGroupMethod = createGroupMethod;
                _getSchemaMethod = getSchemaMethod;
                _bundledSchemaType = bundledSchemaType;
                _contentUpdateSchemaType = contentUpdateSchemaType;
            }

            public bool IsAvailable => _settings != null && _defaultGroup != null && _createOrMoveEntryMethod != null;

            public static AddressablesSyncApi TryCreate()
            {
                var settingsDefaultType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                object settings = null;
                var getSettingsMethod = settingsDefaultType?.GetMethod("GetSettings", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                if (getSettingsMethod != null)
                    settings = getSettingsMethod.Invoke(null, new object[] { true });

                if (settings == null)
                {
                    var settingsProp = settingsDefaultType?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
                    settings = settingsProp?.GetValue(null);
                }

                var settingsType = settings?.GetType();
                var group = settingsType?.GetProperty("DefaultGroup", BindingFlags.Public | BindingFlags.Instance)?.GetValue(settings);
                var createOrMoveEntry = settingsType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "CreateOrMoveEntry" && m.GetParameters().Length >= 2);
                var settingsSetLabel = settingsType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "SetLabel" && m.GetParameters().Length >= 3 && m.GetParameters()[0].ParameterType == typeof(string));
                var findGroup = settingsType?.GetMethod("FindGroup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                var createGroup = settingsType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "CreateGroup") return false;
                        var parameters = m.GetParameters();
                        return parameters.Length >= 5
                            && parameters[0].ParameterType == typeof(string)
                            && parameters[1].ParameterType == typeof(bool)
                            && parameters[2].ParameterType == typeof(bool)
                            && parameters[3].ParameterType == typeof(bool);
                    });

                MethodInfo entrySetLabel = null;
                MethodInfo getSchema = null;
                if (group != null)
                {
                    var entryType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetEntry, Unity.Addressables.Editor");
                    entrySetLabel = entryType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "SetLabel" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(string));
                    getSchema = group.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetSchema" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
                }

                var bundledSchemaType = Type.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
                var contentUpdateSchemaType = Type.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");

                return new AddressablesSyncApi(
                    settings,
                    group,
                    createOrMoveEntry,
                    settingsSetLabel,
                    entrySetLabel,
                    findGroup,
                    createGroup,
                    getSchema,
                    bundledSchemaType,
                    contentUpdateSchemaType);
            }

            public void EnsureSplitGroups()
            {
                for (int i = 0; i < UnitGroupCount; i++)
                    EnsureGroup($"{RemoteUnitsGroupPrefix} {i + 1:D2}");
                for (int i = 0; i < SkinGroupCount; i++)
                    EnsureGroup($"{RemoteSkinsGroupPrefix} {i + 1:D2}");
                EnsureGroup(RemoteSkinsSharedGroupName);
            }

            public void ClearLegacyEntries()
            {
                if (!(_defaultGroup is UnityEngine.Object groupObject)) return;

                var serialized = new SerializedObject(groupObject);
                var entries = serialized.FindProperty("m_SerializeEntries");
                if (entries == null || entries.arraySize == 0) return;

                entries.ClearArray();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(groupObject);
            }

            public void ClearManagedEntries(string groupName)
            {
                var group = EnsureGroup(groupName);
                if (!(group is UnityEngine.Object groupObject))
                    return;

                var serialized = new SerializedObject(groupObject);
                var entries = serialized.FindProperty("m_SerializeEntries");
                if (entries == null || entries.arraySize == 0)
                    return;

                entries.ClearArray();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(groupObject);
            }

            public object CreateOrMoveEntry(string guid, string groupName)
            {
                var group = EnsureGroup(groupName);
                if (group == null) return null;

                var parameters = _createOrMoveEntryMethod.GetParameters();
                if (parameters.Length >= 4)
                    return _createOrMoveEntryMethod.Invoke(_settings, new[] { guid, group, false, false });
                if (parameters.Length == 3)
                    return _createOrMoveEntryMethod.Invoke(_settings, new[] { guid, group, false });
                return _createOrMoveEntryMethod.Invoke(_settings, new[] { guid, group });
            }

            public void RegisterLabel(string label)
            {
                if (_settingsSetLabelMethod == null || string.IsNullOrWhiteSpace(label)) return;

                var parameters = _settingsSetLabelMethod.GetParameters();
                if (parameters.Length >= 4)
                    _settingsSetLabelMethod.Invoke(_settings, new object[] { label, true, true, false });
                else if (parameters.Length == 3)
                    _settingsSetLabelMethod.Invoke(_settings, new object[] { label, true, true });
            }

            public void SetEntryLabel(object entry, string label)
            {
                if (_entrySetLabelMethod == null || entry == null || string.IsNullOrWhiteSpace(label)) return;

                var parameters = _entrySetLabelMethod.GetParameters();
                if (parameters.Length >= 4)
                    _entrySetLabelMethod.Invoke(entry, new object[] { label, true, true, false });
                else if (parameters.Length == 3)
                    _entrySetLabelMethod.Invoke(entry, new object[] { label, true, true });
                else if (parameters.Length == 2)
                    _entrySetLabelMethod.Invoke(entry, new object[] { label, true });
            }

            object EnsureGroup(string groupName)
            {
                if (string.IsNullOrWhiteSpace(groupName)) return _defaultGroup;

                var group = FindGroup(groupName);
                if (group != null)
                {
                    ForceRemoteBundledSchema(group, groupName);
                    return group;
                }
                if (_createGroupMethod == null) return _defaultGroup;

                try
                {
                    var parameters = _createGroupMethod.GetParameters();
                    object[] args = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var p = parameters[i];
                        if (p.ParameterType == typeof(string))
                        {
                            args[i] = groupName;
                        }
                        else if (p.ParameterType == typeof(bool))
                        {
                            args[i] = false;
                        }
                        else if (p.ParameterType == typeof(List<>).MakeGenericType(Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetGroupSchema, Unity.Addressables.Editor") ?? typeof(object)))
                        {
                            args[i] = null;
                        }
                        else if (p.ParameterType == typeof(Type[]))
                        {
                            args[i] = new[] { _bundledSchemaType, _contentUpdateSchemaType };
                        }
                        else if (typeof(IList).IsAssignableFrom(p.ParameterType))
                        {
                            args[i] = null;
                        }
                        else if (p.HasDefaultValue)
                        {
                            args[i] = p.DefaultValue;
                        }
                        else
                        {
                            args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                        }
                    }

                    group = _createGroupMethod.Invoke(_settings, args);
                    if (group != null)
                    {
                        CopySchemaSettings(_defaultGroup, group);
                        ForceRemoteBundledSchema(group, groupName);
                    }
                    return group ?? _defaultGroup;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemoteContentAddressablesSync] Failed to create group '{groupName}': {ex.Message}");
                    return _defaultGroup;
                }
            }

            object FindGroup(string groupName)
            {
                return _findGroupMethod?.Invoke(_settings, new object[] { groupName });
            }

            void CopySchemaSettings(object sourceGroup, object targetGroup)
            {
                if (sourceGroup == null || targetGroup == null || _getSchemaMethod == null) return;

                CopySchemaSettings(sourceGroup, targetGroup, _bundledSchemaType);
                CopySchemaSettings(sourceGroup, targetGroup, _contentUpdateSchemaType);
            }

            void CopySchemaSettings(object sourceGroup, object targetGroup, Type schemaType)
            {
                if (schemaType == null) return;

                var sourceSchema = _getSchemaMethod.Invoke(sourceGroup, new object[] { schemaType });
                var targetSchema = _getSchemaMethod.Invoke(targetGroup, new object[] { schemaType });
                if (sourceSchema == null || targetSchema == null) return;

                foreach (var property in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || !property.CanWrite) continue;
                    if (string.Equals(property.Name, "Group", StringComparison.OrdinalIgnoreCase)) continue;
                    if (property.GetIndexParameters().Length != 0) continue;

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

            void ForceRemoteBundledSchema(object group, string groupName)
            {
                if (group == null
                    || _bundledSchemaType == null
                    || _getSchemaMethod == null
                    || string.IsNullOrWhiteSpace(groupName))
                {
                    return;
                }

                bool isRemoteGroup =
                    groupName.StartsWith(RemoteUnitsGroupPrefix, StringComparison.OrdinalIgnoreCase)
                    || groupName.StartsWith(RemoteSkinsGroupPrefix, StringComparison.OrdinalIgnoreCase);
                if (!isRemoteGroup)
                    return;

                var bundledSchema = _getSchemaMethod.Invoke(group, new object[] { _bundledSchemaType }) as UnityEngine.Object;
                if (bundledSchema == null)
                    return;

                var serialized = new SerializedObject(bundledSchema);
                serialized.FindProperty("m_Name")?.SetValueIfPresent($"{groupName}_BundledAssetGroupSchema");
                serialized.FindProperty("m_BuildPath.m_Id")?.SetValueIfPresent(RemoteBuildPathProfileId);
                serialized.FindProperty("m_LoadPath.m_Id")?.SetValueIfPresent(RemoteLoadPathProfileId);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bundledSchema);
                NormalizeSchemaName(group, _contentUpdateSchemaType, $"{groupName}_ContentUpdateGroupSchema");
                EditorUtility.SetDirty(group as UnityEngine.Object);
            }

            void NormalizeSchemaName(object group, Type schemaType, string expectedName)
            {
                if (group == null || schemaType == null || _getSchemaMethod == null || string.IsNullOrWhiteSpace(expectedName))
                    return;

                var schema = _getSchemaMethod.Invoke(group, new object[] { schemaType }) as UnityEngine.Object;
                if (schema == null)
                    return;

                var serialized = new SerializedObject(schema);
                serialized.FindProperty("m_Name")?.SetValueIfPresent(expectedName);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(schema);
            }
        }

        static void SetValueIfPresent(this SerializedProperty property, string value)
        {
            if (property != null)
                property.stringValue = value;
        }
    }
}
#endif
