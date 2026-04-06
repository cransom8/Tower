#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class SetupRemoteSceneAddressables
    {
        const string GroupName = "Remote Scenes";
        const string TemplateGroup = "Remote Units 01";
        const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        const string RemoteBuildPathProfileId = "165fb4a3ad8d19e4aa002d6fc764a7ce";
        const string RemoteLoadPathProfileId = "247226ff3fd294f46b8dfca266320b8c";

        static readonly (string address, string path)[] SceneEntries =
        {
            ("Login", "Assets/Scenes/Login.unity"),
            ("Lobby", "Assets/Scenes/Lobby.unity"),
            ("Loadout", "Assets/Scenes/Loadout.unity"),
            ("Game_ML", "Assets/Scenes/Game_ML.unity"),
            ("PostGame", "Assets/Scenes/PostGame.unity"),
        };

        [MenuItem("Castle Defender/Remote Content/Setup Remote Scenes Addressables")]
        public static void SyncRemoteScenes()
        {
            object settings = ResolveSettings();
            if (settings == null)
            {
                Debug.LogError("[SetupRemoteSceneAddressables] Could not load AddressableAssetSettings.");
                return;
            }

            var settingsType = settings.GetType();
            var findGroupMethod = settingsType.GetMethod("FindGroup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
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
                Debug.LogError("[SetupRemoteSceneAddressables] Required Addressables editor APIs were not found.");
                return;
            }

            var bundledSchemaType = Type.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            var contentUpdateSchemaType = Type.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");

            object group = EnsureGroup(settings, findGroupMethod, createGroupMethod, bundledSchemaType, contentUpdateSchemaType);
            if (group == null)
            {
                Debug.LogError("[SetupRemoteSceneAddressables] Failed to resolve the remote scenes group.");
                return;
            }

            int pruned = ClearManagedEntries(group);
            int synced = 0;
            int missing = 0;
            foreach (var scene in SceneEntries)
            {
                string guid = AssetDatabase.AssetPathToGUID(scene.path);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    Debug.LogWarning($"[SetupRemoteSceneAddressables] Scene asset is missing: {scene.path}");
                    missing++;
                    continue;
                }

                object entry = CreateOrMoveEntry(settings, createOrMoveEntryMethod, guid, group);
                if (entry == null)
                {
                    Debug.LogWarning($"[SetupRemoteSceneAddressables] Failed to create Addressables entry for '{scene.address}'.");
                    missing++;
                    continue;
                }

                entry.GetType().GetProperty("address", BindingFlags.Public | BindingFlags.Instance)?.SetValue(entry, scene.address);
                synced++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EnsureBootstrapOnlyBuildScenes();
            Debug.Log($"[SetupRemoteSceneAddressables] Synced={synced} missing={missing} pruned={pruned} group='{GroupName}'.");
        }

        static void EnsureBootstrapOnlyBuildScenes()
        {
            var existingScenes = EditorBuildSettings.scenes?.ToList() ?? new System.Collections.Generic.List<EditorBuildSettingsScene>();
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(existingScenes.Count + 1);
            bool changed = false;
            bool hasBootstrap = false;

            for (int i = 0; i < existingScenes.Count; i++)
            {
                var scene = existingScenes[i];
                if (scene == null)
                    continue;

                bool enabled = scene.enabled;
                if (string.Equals(scene.path, BootstrapScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    hasBootstrap = true;
                    if (!enabled)
                    {
                        enabled = true;
                        changed = true;
                    }

                    scenes.Add(new EditorBuildSettingsScene(scene.path, enabled));
                    continue;
                }

                bool isManagedRemoteScene = SceneEntries.Any(entry =>
                    string.Equals(entry.path, scene.path, StringComparison.OrdinalIgnoreCase));
                bool assetMissing = !string.IsNullOrWhiteSpace(scene.path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) == null;
                if ((isManagedRemoteScene || assetMissing) && enabled)
                {
                    enabled = false;
                    changed = true;
                }

                scenes.Add(new EditorBuildSettingsScene(scene.path, enabled));
            }

            if (!hasBootstrap)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(BootstrapScenePath, true));
                changed = true;
            }

            if (!changed)
                return;

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[SetupRemoteSceneAddressables] Updated EditorBuildSettings so Bootstrap remains the only enabled local scene.");
        }

        static object ResolveSettings()
        {
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
                return null;

            var getSettings = settingsDefaultType.GetMethod(
                "GetSettings",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            if (getSettings != null)
                return getSettings.Invoke(null, new object[] { true });

            return settingsDefaultType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        static object EnsureGroup(
            object settings,
            MethodInfo findGroupMethod,
            MethodInfo createGroupMethod,
            Type bundledSchemaType,
            Type contentUpdateSchemaType)
        {
            object group = findGroupMethod.Invoke(settings, new object[] { GroupName });
            object templateGroup = findGroupMethod.Invoke(settings, new object[] { TemplateGroup });

            if (group == null)
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
                        args[i] = parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
                }

                group = createGroupMethod.Invoke(settings, args);
            }

            if (group != null && templateGroup != null)
                CopySchemaSettings(templateGroup, group, bundledSchemaType, contentUpdateSchemaType);

            ForceRemotePaths(group, bundledSchemaType, contentUpdateSchemaType);
            return group;
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

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type bundledSchemaType, Type contentUpdateSchemaType)
        {
            if (sourceGroup == null || targetGroup == null)
                return;

            var getSchemaMethod = sourceGroup.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchemaMethod == null)
                return;

            CopySchemaSettings(sourceGroup, targetGroup, bundledSchemaType, getSchemaMethod);
            CopySchemaSettings(sourceGroup, targetGroup, contentUpdateSchemaType, getSchemaMethod);
        }

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type schemaType, MethodInfo getSchemaMethod)
        {
            if (schemaType == null)
                return;

            object sourceSchema = getSchemaMethod.Invoke(sourceGroup, new object[] { schemaType });
            object targetSchema = getSchemaMethod.Invoke(targetGroup, new object[] { schemaType });
            if (sourceSchema == null || targetSchema == null)
                return;

            foreach (var property in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
                    continue;
                if (string.Equals(property.Name, "Group", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    property.SetValue(targetSchema, property.GetValue(sourceSchema));
                }
                catch
                {
                    // Ignore schema properties Unity will not let us copy.
                }
            }

            EditorUtility.SetDirty(targetSchema as UnityEngine.Object);
            EditorUtility.SetDirty(targetGroup as UnityEngine.Object);
        }

        static void ForceRemotePaths(object group, Type bundledSchemaType, Type contentUpdateSchemaType)
        {
            if (group == null || bundledSchemaType == null)
                return;

            var getSchemaMethod = group.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchemaMethod == null)
                return;

            var bundledSchema = getSchemaMethod.Invoke(group, new object[] { bundledSchemaType }) as UnityEngine.Object;
            if (bundledSchema == null)
                return;

            var serialized = new SerializedObject(bundledSchema);
            serialized.FindProperty("m_Name")?.SetValueIfPresent($"{GroupName}_BundledAssetGroupSchema");
            serialized.FindProperty("m_BuildPath.m_Id")?.SetValueIfPresent(RemoteBuildPathProfileId);
            serialized.FindProperty("m_LoadPath.m_Id")?.SetValueIfPresent(RemoteLoadPathProfileId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bundledSchema);
            EditorUtility.SetDirty(group as UnityEngine.Object);
            NormalizeSchemaName(group, getSchemaMethod, contentUpdateSchemaType, $"{GroupName}_ContentUpdateGroupSchema");
        }

        static void NormalizeSchemaName(object group, MethodInfo getSchemaMethod, Type schemaType, string expectedName)
        {
            if (group == null || getSchemaMethod == null || schemaType == null || string.IsNullOrWhiteSpace(expectedName))
                return;

            var schema = getSchemaMethod.Invoke(group, new object[] { schemaType }) as UnityEngine.Object;
            if (schema == null)
                return;

            var serialized = new SerializedObject(schema);
            serialized.FindProperty("m_Name")?.SetValueIfPresent(expectedName);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(schema);
        }

        static void SetValueIfPresent(this SerializedProperty property, string value)
        {
            if (property != null)
                property.stringValue = value;
        }
    }
}
#endif
