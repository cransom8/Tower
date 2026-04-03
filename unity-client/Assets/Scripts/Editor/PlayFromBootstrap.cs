// PlayFromBootstrap.cs — Forces Play Mode to always start from the Bootstrap scene,
// regardless of which scene is currently open in the Editor.
//
// No configuration needed. Just works automatically whenever you hit Play.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CastleDefender.Editor
{
    [InitializeOnLoad]
    static class PlayFromBootstrap
    {
        const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
        const string GameMlPath = "Assets/Scenes/Game_ML.unity";

        static PlayFromBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (ClassicRpgUiScreenshotAutomation.IsCaptureBatchRunning)
                {
                    ClearBootstrapOverride();
                    EditorSceneManager.playModeStartScene = null;
                    return;
                }

                // Save any unsaved changes in the current scene first.
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                PersistBootstrapOverrideForActiveScene();

                // Tell Unity to start Play Mode from Bootstrap instead of the active scene.
                EditorSceneManager.playModeStartScene =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath);
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Clear the override so scene-specific tools still work normally.
                ClearBootstrapOverride();
                EditorSceneManager.playModeStartScene = null;
            }
        }

        static void PersistBootstrapOverrideForActiveScene()
        {
            ClearBootstrapOverride();

            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path != GameMlPath)
                return;

            PlayerPrefs.SetString(
                CastleDefender.Net.BootstrapManager.EditorSceneOverridePrefKey,
                activeScene.name);
            PlayerPrefs.Save();
        }

        static void ClearBootstrapOverride()
        {
            PlayerPrefs.DeleteKey(CastleDefender.Net.BootstrapManager.EditorSceneOverridePrefKey);
            PlayerPrefs.Save();
        }
    }
}
