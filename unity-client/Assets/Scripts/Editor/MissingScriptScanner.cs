using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

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
    }
}
