using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class CommitSceneEnvironmentInstances
    {
        [MenuItem("Castle Defender/Remote Content/Commit Scene Environment Instances")]
        static void Commit()
        {
            try
            {
                ApplyIfPresent("GameEnvironment");
                ApplyIfPresent("GameEnvironmentOptional");

                AssetDatabase.SaveAssets();
                var scene = EditorSceneManager.GetActiveScene();
                if (scene.IsValid())
                    EditorSceneManager.SaveScene(scene);

                Debug.Log("[CommitSceneEnvironmentInstances] Applied GameEnvironment and GameEnvironmentOptional back to their prefab assets.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw;
            }
        }

        static void ApplyIfPresent(string name)
        {
            var sceneObject = GameObject.Find(name);
            if (sceneObject == null)
            {
                Debug.LogWarning($"[CommitSceneEnvironmentInstances] '{name}' was not found in the active scene.");
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(sceneObject))
                throw new InvalidOperationException($"'{name}' is not a prefab instance, so it cannot be applied safely.");

            if (string.Equals(name, "GameEnvironment", StringComparison.Ordinal))
            {
                EnvironmentPrefabSafety.AssertValidCriticalEnvironmentRoot(
                    sceneObject,
                    "scene instance 'GameEnvironment'");
            }

            PrefabUtility.ApplyPrefabInstance(sceneObject, InteractionMode.AutomatedAction);
        }
    }
}
