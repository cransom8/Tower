// SetupBootstrapScene.cs - One-click Bootstrap scene wiring.
// Castle Defender -> Setup -> Setup Bootstrap Scene
//
// What it does:
//   1. Opens Bootstrap.unity
//   2. Creates a "Bootstrap" GameObject with BootstrapManager attached
//   3. Saves the scene
//   4. Inserts Bootstrap at build index 0 (Login becomes index 1, etc.)

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    public static class SetupBootstrapScene
    {
        const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("Castle Defender/Setup/Setup Bootstrap Scene")]
        static void Run()
        {
            var scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[SetupBootstrap] Could not open Bootstrap.unity");
                return;
            }

            var existing = Object.FindFirstObjectByType<CastleDefender.Net.BootstrapManager>();
            if (existing == null)
            {
                var go = new GameObject("Bootstrap");
                go.AddComponent<CastleDefender.Net.BootstrapManager>();
                Debug.Log("[SetupBootstrap] Created Bootstrap GameObject with BootstrapManager.");
            }
            else
            {
                Debug.Log("[SetupBootstrap] BootstrapManager already exists - skipping creation.");
            }

            EditorSceneManager.SaveScene(scene);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == BootstrapScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(BootstrapScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[SetupBootstrap] Bootstrap inserted at build index 0.");
            EditorUtility.DisplayDialog(
                "Setup Bootstrap Scene",
                "Done!\n\n- BootstrapManager added to Bootstrap.unity\n- Bootstrap set to build index 0\n\nNext: open Login.unity and move NetworkManager, AudioManager, AuthManager, CatalogLoader, SnapshotApplier, LoadingScreen, and PostProcessController GameObjects into Bootstrap.unity.",
                "OK");
        }
    }
}
