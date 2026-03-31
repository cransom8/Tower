using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CastleDefender.Game;

namespace CastleDefender.Editor
{
    public static class MissingScriptScanner
    {
        const string PrimaryEnvironmentPrefabPath = "Assets/AddressableContent/Environment/GameEnvironment.prefab";

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

        [MenuItem("Castle Defender/Debug/Repair Barracks Legacy Components In Active Scene")]
        public static void RepairBarracksLegacyComponentsInActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("[MissingScriptScanner] No active loaded scene.");
                return;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(PrimaryEnvironmentPrefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"[MissingScriptScanner] Could not load '{PrimaryEnvironmentPrefabPath}'.");
                return;
            }

            int repaired = 0;
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    repaired += RepairBarracksRecursive(root.transform, prefabRoot.transform);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            if (repaired == 0)
            {
                Debug.Log($"[MissingScriptScanner] No barracks legacy components needed repair in active scene '{scene.name}'.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.LogWarning($"[MissingScriptScanner] Repaired {repaired} barracks legacy component issue(s) in active scene '{scene.name}'.");
        }

        static int RemoveRecursive(GameObject go)
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            for (int i = 0; i < go.transform.childCount; i++)
                removed += RemoveRecursive(go.transform.GetChild(i).gameObject);
            return removed;
        }

        static int RepairBarracksRecursive(Transform sceneTransform, Transform prefabRoot)
        {
            int repaired = 0;
            if (sceneTransform.GetComponent<BarracksSiteView>() != null)
                repaired += RepairBarracksObject(sceneTransform, prefabRoot);

            for (int i = 0; i < sceneTransform.childCount; i++)
                repaired += RepairBarracksRecursive(sceneTransform.GetChild(i), prefabRoot);

            return repaired;
        }

        static int RepairBarracksObject(Transform sceneTransform, Transform prefabRoot)
        {
            if (sceneTransform == null || prefabRoot == null)
                return 0;

            int repaired = 0;
            repaired += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(sceneTransform.gameObject);

            string relativePath = GetPathUnderRoot(sceneTransform, prefabRoot.name);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                Debug.LogWarning($"[MissingScriptScanner] Could not determine prefab path for barracks '{sceneTransform.name}'.", sceneTransform.gameObject);
                return repaired;
            }

            var sourceTransform = prefabRoot.Find(relativePath);
            if (sourceTransform == null)
            {
                Debug.LogWarning(
                    $"[MissingScriptScanner] Could not find source barracks '{relativePath}' in '{PrimaryEnvironmentPrefabPath}'.",
                    sceneTransform.gameObject);
                return repaired;
            }

            var targetPath = sceneTransform.GetComponent<BarracksLanePath>();
            if (targetPath == null)
            {
                targetPath = Undo.AddComponent<BarracksLanePath>(sceneTransform.gameObject);
                repaired++;
            }

            var targetSpawner = sceneTransform.GetComponent<BarracksAutoSpawner>();
            if (targetSpawner == null)
            {
                targetSpawner = Undo.AddComponent<BarracksAutoSpawner>(sceneTransform.gameObject);
                repaired++;
            }

            var sourcePath = sourceTransform.GetComponent<BarracksLanePath>();
            if (sourcePath != null)
                EditorUtility.CopySerializedManagedFieldsOnly(sourcePath, targetPath);

            targetPath.spawnPoint = FindDirectChild(sceneTransform, "SpawnPoint");
            targetPath.markerTransforms = CollectNamedChildren(sceneTransform, "Marker_");

            var sourceSpawner = sourceTransform.GetComponent<BarracksAutoSpawner>();
            if (sourceSpawner != null)
                EditorUtility.CopySerializedManagedFieldsOnly(sourceSpawner, targetSpawner);
            else
                targetSpawner.team = ResolveTeam(sceneTransform.GetComponent<BarracksSiteView>());

            targetSpawner.barracksPath = targetPath;

            EditorUtility.SetDirty(targetPath);
            EditorUtility.SetDirty(targetSpawner);
            EditorUtility.SetDirty(sceneTransform.gameObject);
            return repaired;
        }

        static string GetPathUnderRoot(Transform current, string rootName)
        {
            if (current == null || string.IsNullOrWhiteSpace(rootName))
                return string.Empty;

            var segments = new List<string>();
            var cursor = current;
            while (cursor != null)
            {
                if (string.Equals(cursor.name, rootName, StringComparison.Ordinal))
                {
                    segments.Reverse();
                    return string.Join("/", segments);
                }

                segments.Add(cursor.name);
                cursor = cursor.parent;
            }

            return string.Empty;
        }

        static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        static List<Transform> CollectNamedChildren(Transform parent, string prefix)
        {
            var results = new List<Transform>();
            if (parent == null || string.IsNullOrWhiteSpace(prefix))
                return results;

            for (int markerIndex = 1; markerIndex <= 16; markerIndex++)
            {
                var marker = FindDirectChild(parent, $"{prefix}{markerIndex}");
                if (marker == null)
                    break;

                results.Add(marker);
            }

            return results;
        }

        static BattleTeam ResolveTeam(BarracksSiteView barracksSiteView)
        {
            if (barracksSiteView == null)
                return BattleTeam.Red;

            return barracksSiteView.laneColor switch
            {
                FortressPadAnchor.LaneColor.Red => BattleTeam.Red,
                FortressPadAnchor.LaneColor.Gold => BattleTeam.Yellow,
                FortressPadAnchor.LaneColor.Blue => BattleTeam.Blue,
                FortressPadAnchor.LaneColor.Green => BattleTeam.Green,
                _ => BattleTeam.Red,
            };
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
