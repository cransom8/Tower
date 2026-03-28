using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    public static class MissingScriptScanner
    {
        [MenuItem("Castle Defender/Debug/Scan Active Scene For Missing Scripts")]
        public static void ScanActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("[MissingScriptScanner] No active loaded scene.");
                return;
            }

            int totalMissing = 0;
            foreach (var root in scene.GetRootGameObjects())
                totalMissing += ScanRecursive(root, root.name);

            if (totalMissing == 0)
                Debug.Log($"[MissingScriptScanner] No missing scripts found in active scene '{scene.name}'.");
            else
                Debug.LogWarning($"[MissingScriptScanner] Found {totalMissing} missing script reference(s) in active scene '{scene.name}'.");
        }

        [MenuItem("Castle Defender/Debug/Scan Prefabs For Missing Scripts")]
        public static void ScanPrefabs()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            int prefabCount = 0;
            int totalMissing = 0;

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                prefabCount++;
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    int missing = ScanRecursive(root, Path.GetFileNameWithoutExtension(path));
                    if (missing > 0)
                        Debug.LogWarning($"[MissingScriptScanner] Prefab '{path}' has {missing} missing script reference(s).");
                    totalMissing += missing;
                }
                finally
                {
                    if (root != null)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (totalMissing == 0)
                Debug.Log($"[MissingScriptScanner] No missing scripts found across {prefabCount} prefab(s).");
            else
                Debug.LogWarning($"[MissingScriptScanner] Found {totalMissing} missing script reference(s) across {prefabCount} prefab(s).");
        }

        [MenuItem("Castle Defender/Debug/Reload Open Prefab Stage")]
        public static void ReloadOpenPrefabStage()
        {
            object prefabStage = GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                Debug.LogWarning("[MissingScriptScanner] No prefab stage is currently open.");
                return;
            }

            string assetPath = GetPrefabStageAssetPath(prefabStage);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                Debug.LogError("[MissingScriptScanner] Could not determine the asset path for the open prefab stage.");
                return;
            }

            AssetDatabase.SaveAssets();

            if (!TryGoToMainStage())
            {
                Debug.LogError($"[MissingScriptScanner] Could not close the current prefab stage for '{assetPath}'.");
                return;
            }

            EditorApplication.delayCall += () =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null)
                {
                    Debug.LogError($"[MissingScriptScanner] Could not reload prefab stage because '{assetPath}' could not be loaded.");
                    return;
                }

                AssetDatabase.OpenAsset(asset);
                Debug.Log($"[MissingScriptScanner] Reloaded prefab stage '{assetPath}' from disk.");
            };
        }

        static int ScanRecursive(GameObject go, string path)
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0)
                Debug.LogWarning($"[MissingScriptScanner] {path} has {missing} missing script reference(s).", go);

            int total = missing;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i).gameObject;
                total += ScanRecursive(child, $"{path}/{child.name}");
            }

            return total;
        }

        [MenuItem("Castle Defender/Debug/Remove Missing Scripts From Active Scene")]
        public static void RemoveMissingScriptsFromActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("[MissingScriptScanner] No active loaded scene.");
                return;
            }

            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                removed += RemoveRecursive(root);

            if (removed == 0)
            {
                Debug.Log($"[MissingScriptScanner] No missing scripts removed from active scene '{scene.name}'.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.LogWarning($"[MissingScriptScanner] Removed {removed} missing script component(s) from active scene '{scene.name}'.");
        }

        static int RemoveRecursive(GameObject go)
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            for (int i = 0; i < go.transform.childCount; i++)
                removed += RemoveRecursive(go.transform.GetChild(i).gameObject);
            return removed;
        }

        static object GetCurrentPrefabStage()
        {
            Type utilityType = FindEditorType(
                "UnityEditor.SceneManagement.PrefabStageUtility",
                "UnityEditor.Experimental.SceneManagement.PrefabStageUtility");
            if (utilityType == null)
                return null;

            MethodInfo getCurrentMethod = utilityType.GetMethod(
                "GetCurrentPrefabStage",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return getCurrentMethod?.Invoke(null, null);
        }

        static string GetPrefabStageAssetPath(object prefabStage)
        {
            if (prefabStage == null)
                return string.Empty;

            Type stageType = prefabStage.GetType();
            PropertyInfo assetPathProperty =
                stageType.GetProperty("assetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? stageType.GetProperty("prefabAssetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return assetPathProperty?.GetValue(prefabStage) as string ?? string.Empty;
        }

        static bool TryGoToMainStage()
        {
            Type stageUtilityType = FindEditorType(
                "UnityEditor.SceneManagement.StageUtility",
                "UnityEditor.Experimental.SceneManagement.StageUtility",
                "UnityEditor.StageUtility");
            if (stageUtilityType == null)
                return false;

            MethodInfo goToMainStageMethod = stageUtilityType.GetMethod(
                "GoToMainStage",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (goToMainStageMethod == null)
                return false;

            goToMainStageMethod.Invoke(null, null);
            return true;
        }

        static Type FindEditorType(params string[] typeNames)
        {
            foreach (string typeName in typeNames)
            {
                Type resolved = Type.GetType(typeName);
                if (resolved != null)
                    return resolved;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (string typeName in typeNames)
            {
                foreach (Assembly assembly in assemblies)
                {
                    Type resolved = assembly.GetType(typeName);
                    if (resolved != null)
                        return resolved;
                }
            }

            return null;
        }
    }
}
