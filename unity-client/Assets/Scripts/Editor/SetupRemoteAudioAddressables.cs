#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class SetupRemoteAudioAddressables
    {
        const string GroupName = "Remote Audio";
        const string TemplateGroup = "Remote Units 01";
        const string RemoteBuildPathProfileId = "165fb4a3ad8d19e4aa002d6fc764a7ce";
        const string RemoteLoadPathProfileId = "247226ff3fd294f46b8dfca266320b8c";
        const string SharedMusicLoopAddress = "audio/music/winters-gloom-loop";

        static readonly (string address, string path)[] AudioEntries =
        {
            (SharedMusicLoopAddress, "Assets/Audio/Generated/Winters_Gloom_Wars_Loom_2026-04-02T062148.mp3"),
        };

        [MenuItem("Castle Defender/Remote Content/Setup Remote Audio Addressables")]
        public static void SyncRemoteAudio()
        {
            object settings = ResolveSettings();
            if (settings == null)
            {
                Debug.LogError("[SetupRemoteAudioAddressables] Could not load AddressableAssetSettings.");
                return;
            }

            var settingsType = settings.GetType();
            MethodInfo findGroupMethod = settingsType.GetMethod("FindGroup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            MethodInfo createOrMoveEntryMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "CreateOrMoveEntry" && method.GetParameters().Length >= 2);
            MethodInfo createGroupMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "CreateGroup")
                        return false;

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length >= 4
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(bool);
                });

            if (findGroupMethod == null || createOrMoveEntryMethod == null || createGroupMethod == null)
            {
                Debug.LogError("[SetupRemoteAudioAddressables] Required Addressables editor APIs were not found.");
                return;
            }

            Type bundledSchemaType = Type.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            Type contentUpdateSchemaType = Type.GetType("UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");
            object group = EnsureGroup(settings, findGroupMethod, createGroupMethod, bundledSchemaType, contentUpdateSchemaType);
            if (group == null)
            {
                Debug.LogError("[SetupRemoteAudioAddressables] Failed to resolve the remote audio group.");
                return;
            }

            int synced = 0;
            int missing = 0;
            foreach ((string address, string path) in AudioEntries)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    Debug.LogWarning($"[SetupRemoteAudioAddressables] Audio asset is missing: {path}");
                    missing++;
                    continue;
                }

                object entry = CreateOrMoveEntry(settings, createOrMoveEntryMethod, guid, group);
                if (entry == null)
                {
                    Debug.LogWarning($"[SetupRemoteAudioAddressables] Failed to create Addressables entry for '{address}'.");
                    missing++;
                    continue;
                }

                entry.GetType().GetProperty("address", BindingFlags.Public | BindingFlags.Instance)?.SetValue(entry, address);
                synced++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SetupRemoteAudioAddressables] Synced={synced} missing={missing} group='{GroupName}'.");
        }

        static object ResolveSettings()
        {
            Type settingsDefaultType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
                return null;

            MethodInfo getSettings = settingsDefaultType.GetMethod(
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
                ParameterInfo[] parameters = createGroupMethod.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo parameter = parameters[i];
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

            ForceRemotePaths(group, bundledSchemaType);
            return group;
        }

        static object CreateOrMoveEntry(object settings, MethodInfo createOrMoveEntryMethod, string guid, object group)
        {
            ParameterInfo[] parameters = createOrMoveEntryMethod.GetParameters();
            if (parameters.Length >= 4)
                return createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false, false });
            if (parameters.Length == 3)
                return createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false });

            return createOrMoveEntryMethod.Invoke(settings, new[] { guid, group });
        }

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type bundledSchemaType, Type contentUpdateSchemaType)
        {
            if (sourceGroup == null || targetGroup == null)
                return;

            MethodInfo getSchemaMethod = sourceGroup.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "GetSchema"
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(Type));
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

            foreach (PropertyInfo property in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
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

        static void ForceRemotePaths(object group, Type bundledSchemaType)
        {
            if (group == null || bundledSchemaType == null)
                return;

            MethodInfo getSchemaMethod = group.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "GetSchema"
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchemaMethod == null)
                return;

            var bundledSchema = getSchemaMethod.Invoke(group, new object[] { bundledSchemaType }) as UnityEngine.Object;
            if (bundledSchema == null)
                return;

            var serialized = new SerializedObject(bundledSchema);
            serialized.FindProperty("m_BuildPath.m_Id")?.SetValueIfPresent(RemoteBuildPathProfileId);
            serialized.FindProperty("m_LoadPath.m_Id")?.SetValueIfPresent(RemoteLoadPathProfileId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bundledSchema);
            EditorUtility.SetDirty(group as UnityEngine.Object);
        }

        static void SetValueIfPresent(this SerializedProperty property, string value)
        {
            if (property != null)
                property.stringValue = value;
        }
    }
}
#endif
