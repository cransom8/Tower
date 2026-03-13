// PlayFromBootstrap.cs — Forces Play Mode to always start from the Bootstrap scene,
// regardless of which scene is currently open in the Editor.
//
// No configuration needed. Just works automatically whenever you hit Play.

using UnityEditor;
using UnityEditor.SceneManagement;

namespace CastleDefender.Editor
{
    [InitializeOnLoad]
    static class PlayFromBootstrap
    {
        const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

        static PlayFromBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // Save any unsaved changes in the current scene first.
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                // Tell Unity to start Play Mode from Bootstrap instead of the active scene.
                EditorSceneManager.playModeStartScene =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath);
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Clear the override so scene-specific tools still work normally.
                EditorSceneManager.playModeStartScene = null;
            }
        }
    }
}
