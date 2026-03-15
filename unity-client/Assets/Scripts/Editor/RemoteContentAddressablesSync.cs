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
        const int UnitGroupCount = 6;
        const int SkinGroupCount = 3;

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

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RemoteContentAddressablesSync] Synced={synced} warnings={warnings}");
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
            int bucket = GetBucketIndex(key, string.Equals(kindLabel, "skin", StringComparison.OrdinalIgnoreCase) ? SkinGroupCount : UnitGroupCount);
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
                if (group != null) return group;
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
                        CopySchemaSettings(_defaultGroup, group);
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
        }
    }
}
#endif
