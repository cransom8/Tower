using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CastleDefender.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    public static class PromoteGameEnvironmentEditPreview
    {
        const string ScenePath = "Assets/Scenes/Game_ML.unity";
        const string MapRootName = "Map";
        const string PreviewRootName = "GameEnvironment_EditPreview";
        const string CriticalPrefabPath = "Assets/AddressableContent/Environment/GameEnvironment.prefab";
        const string OptionalPrefabPath = "Assets/AddressableContent/Environment/GameEnvironmentOptional.prefab";
        const string CriticalGroupName = "Remote Environment";
        const string OptionalGroupName = "Remote Environment Dressing";
        const string CriticalAddress = RemoteContentManager.GameMlEnvironmentAddress;
        const string OptionalAddress = RemoteContentManager.GameMlEnvironmentDressingAddress;
        const string RuntimeCriticalRootName = RemoteContentManager.GameMlEnvironmentRootName;
        const string RuntimeOptionalRootName = RemoteContentManager.GameMlEnvironmentDressingRootName;

        [MenuItem("Castle Defender/Remote Content/Promote GameEnvironment_EditPreview")]
        static void Promote()
        {
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                var mapRoot = GameObject.Find(MapRootName);
                if (mapRoot == null)
                    throw new InvalidOperationException($"Could not find '{MapRootName}' in '{ScenePath}'.");

                var previewRoot = mapRoot.transform.Find(PreviewRootName);
                if (previewRoot == null)
                    throw new InvalidOperationException(
                        $"Could not find '{PreviewRootName}' under '{MapRootName}'.");

                var audit = AuditExternalReferences(scene, previewRoot.gameObject);
                if (audit.Count > 0)
                {
                    Debug.LogWarning(
                        $"[PromoteGameEnvironmentEditPreview] Found {audit.Count} external scene reference(s) into '{PreviewRootName}'. " +
                        "The preview will be promoted into the critical prefab, but the scene instance will be kept so those bindings stay intact.");
                    foreach (string line in audit)
                        Debug.LogWarning($"[PromoteGameEnvironmentEditPreview] Ref: {line}");
                }

                int removedOptionalCount = RemoveLegacyOptionalBranches(previewRoot);
                Debug.Log($"[PromoteGameEnvironmentEditPreview] Removed {removedOptionalCount} legacy optional branch(es) from the preview root.");

                EnvironmentPrefabSafety.AssertValidCriticalEnvironmentRoot(
                    previewRoot.gameObject,
                    $"preview root '{PreviewRootName}'");

                PrefabUtility.ApplyPrefabInstance(previewRoot.gameObject, InteractionMode.AutomatedAction);
                RenameCriticalPrefabRoot();
                RemoveLegacyOptionalBranchesFromCriticalPrefab();

                EnsureAddressablesEntry(CriticalPrefabPath, CriticalAddress, CriticalGroupName);
                EnsureAddressablesEntry(OptionalPrefabPath, OptionalAddress, OptionalGroupName);
                EnsureSceneLoaders(mapRoot);

                if (audit.Count == 0)
                {
                    UnityEngine.Object.DestroyImmediate(previewRoot.gameObject);
                    Debug.Log("[PromoteGameEnvironmentEditPreview] Removed the in-scene preview root so remote loading is authoritative at runtime.");
                }
                else
                {
                    Debug.LogWarning(
                        "[PromoteGameEnvironmentEditPreview] Kept the in-scene preview root because serialized scene references still point into it.");
                }

                RemoveLegacyOptionalObjectsInScene();

                EditorUtility.SetDirty(mapRoot);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[PromoteGameEnvironmentEditPreview] Promotion complete.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw;
            }
        }

        static List<string> AuditExternalReferences(Scene scene, GameObject targetRoot)
        {
            var findings = new List<string>();
            var targetSet = new HashSet<UnityEngine.Object>();
            foreach (Transform t in targetRoot.GetComponentsInChildren<Transform>(true))
            {
                targetSet.Add(t.gameObject);
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c != null)
                        targetSet.Add(c);
                }
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                        continue;

                    if (component.gameObject == targetRoot || component.transform.IsChildOf(targetRoot.transform))
                        continue;

                    using var serialized = new SerializedObject(component);
                    var iterator = serialized.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                            continue;

                        UnityEngine.Object referenced = iterator.objectReferenceValue;
                        if (referenced == null || !targetSet.Contains(referenced))
                            continue;

                        findings.Add(
                            $"{GetHierarchyPath(component.transform)} [{component.GetType().Name}] -> {iterator.propertyPath} -> {GetObjectPath(referenced)}");
                    }
                }
            }

            return findings;
        }

        static int RemoveLegacyOptionalBranches(Transform root)
        {
            var toRemove = root
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t != root && string.Equals(t.name, "GameEnvironmentOptional", StringComparison.Ordinal))
                .OrderByDescending(t => GetDepth(t))
                .ToList();

            foreach (Transform item in toRemove)
                UnityEngine.Object.DestroyImmediate(item.gameObject);

            return toRemove.Count;
        }

        static void RenameCriticalPrefabRoot()
        {
            using var editScope = new PrefabUtility.EditPrefabContentsScope(CriticalPrefabPath);
            if (!string.Equals(editScope.prefabContentsRoot.name, PreviewRootName, StringComparison.Ordinal))
                editScope.prefabContentsRoot.name = PreviewRootName;
        }

        static void RemoveLegacyOptionalBranchesFromCriticalPrefab()
        {
            using var editScope = new PrefabUtility.EditPrefabContentsScope(CriticalPrefabPath);
            var matches = editScope.prefabContentsRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t != editScope.prefabContentsRoot.transform &&
                            string.Equals(t.name, "GameEnvironmentOptional", StringComparison.Ordinal))
                .OrderByDescending(t => GetDepth(t))
                .ToList();

            foreach (Transform match in matches)
                UnityEngine.Object.DestroyImmediate(match.gameObject);
        }

        static void EnsureSceneLoaders(GameObject mapRoot)
        {
            var criticalLoader = mapRoot.GetComponent<EnvironmentLoader>();
            if (criticalLoader == null)
                criticalLoader = Undo.AddComponent<EnvironmentLoader>(mapRoot);

            criticalLoader.environmentAddress = CriticalAddress;
            criticalLoader.instantiateParent = mapRoot.transform;
            criticalLoader.instantiatedRootName = RuntimeCriticalRootName;
            criticalLoader.failureTitle = "Required map environment failed to load.";
            criticalLoader.readinessTimeoutSeconds = 12f;
            EditorUtility.SetDirty(criticalLoader);

            var optionalLoader = mapRoot.GetComponent<OptionalEnvironmentLoader>();
            if (optionalLoader == null)
                optionalLoader = Undo.AddComponent<OptionalEnvironmentLoader>(mapRoot);

            optionalLoader.optionalEnvironmentAddress = OptionalAddress;
            optionalLoader.instantiateParent = mapRoot.transform;
            optionalLoader.instantiatedRootName = RuntimeOptionalRootName;
            optionalLoader.instantiatedRootScale = RemoteContentManager.GameMlEnvironmentDressingScale;
            optionalLoader.requiredRootName = RuntimeCriticalRootName;
            optionalLoader.waitForCriticalTimeoutSeconds = 15f;
            optionalLoader.loadStartDelaySeconds = 0.25f;
            optionalLoader.logWarnings = true;
            EditorUtility.SetDirty(optionalLoader);
        }

        static void RemoveLegacyOptionalObjectsInScene()
        {
            foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!obj.scene.IsValid() || !obj.scene.isLoaded)
                    continue;

                if (!string.Equals(obj.name, "GameEnvironmentOptional", StringComparison.Ordinal))
                    continue;

                if (PrefabUtility.IsPartOfPrefabAsset(obj))
                    continue;

                var parent = obj.transform.parent;
                bool underRuntimeCritical = parent != null && parent.name == RuntimeCriticalRootName;
                bool underPreview = parent != null && parent.name == PreviewRootName;
                bool nestedLegacy = obj.transform.parent != null;
                if (underRuntimeCritical || underPreview || nestedLegacy)
                    UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        static void EnsureAddressablesEntry(string assetPath, string address, string groupName)
        {
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
                throw new InvalidOperationException("Addressables editor API unavailable.");

            var settingsProperty = settingsDefaultType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            var settings = settingsProperty?.GetValue(null);
            if (settings == null)
                throw new InvalidOperationException("Could not load AddressableAssetSettings.");

            var settingsType = settings.GetType();
            var findGroup = settingsType.GetMethod("FindGroup", new[] { typeof(string) });
            var createOrMoveEntry = settingsType.GetMethod(
                "CreateOrMoveEntry",
                new[] { typeof(string), Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetGroup, Unity.Addressables.Editor"), typeof(bool), typeof(bool) });
            if (findGroup == null || createOrMoveEntry == null)
                throw new InvalidOperationException("Required Addressables API methods not found.");

            var group = findGroup.Invoke(settings, new object[] { groupName });
            if (group == null)
                throw new InvalidOperationException($"Could not find Addressables group '{groupName}'.");

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
                throw new InvalidOperationException($"No GUID found for '{assetPath}'.");

            var entry = createOrMoveEntry.Invoke(settings, new[] { guid, group, true, true });
            if (entry == null)
                throw new InvalidOperationException($"Failed to create Addressables entry for '{assetPath}'.");

            var entryType = entry.GetType();
            entryType.GetProperty("address", BindingFlags.Public | BindingFlags.Instance)?.SetValue(entry, address);
            EditorUtility.SetDirty((UnityEngine.Object)entry);

            EnsureGroupUsesRemoteBuildAndLoadPaths(group);
            EditorUtility.SetDirty((UnityEngine.Object)group);
            EditorUtility.SetDirty((UnityEngine.Object)settings);
        }

        static void EnsureGroupUsesRemoteBuildAndLoadPaths(object group)
        {
            var bundledSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            if (bundledSchemaType == null)
                return;

            var getSchema = group.GetType().GetMethod("GetSchema", new[] { typeof(Type) });
            if (getSchema == null)
                return;

            var bundledSchema = getSchema.Invoke(group, new object[] { bundledSchemaType }) as UnityEngine.Object;
            if (bundledSchema == null)
                return;

            var serialized = new SerializedObject(bundledSchema);
            var buildPath = serialized.FindProperty("m_BuildPath.m_Id");
            var loadPath = serialized.FindProperty("m_LoadPath.m_Id");
            if (buildPath != null)
                buildPath.stringValue = "RemoteBuildPath";
            if (loadPath != null)
                loadPath.stringValue = "RemoteLoadPath";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bundledSchema);
        }

        static int GetDepth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        static string GetHierarchyPath(Transform transform)
        {
            var parts = new Stack<string>();
            while (transform != null)
            {
                parts.Push(transform.name);
                transform = transform.parent;
            }

            return string.Join("/", parts);
        }

        static string GetObjectPath(UnityEngine.Object obj)
        {
            if (obj is Component component)
                return GetHierarchyPath(component.transform) + $" [{component.GetType().Name}]";
            if (obj is GameObject gameObject)
                return GetHierarchyPath(gameObject.transform);
            return obj.name;
        }
    }
}
