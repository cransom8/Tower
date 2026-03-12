// MigrateBootstrapSingletons.cs — Moves singleton GameObjects from Login.unity
// into Bootstrap.unity so they initialise before the first real scene loads.
// Castle Defender → Setup → Migrate Singletons to Bootstrap

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace CastleDefender.Editor
{
    public static class MigrateBootstrapSingletons
    {
        const string LoginPath     = "Assets/Scenes/Login.unity";
        const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

        // Component type names that identify singleton GOs to migrate
        static readonly string[] SingletonTypes =
        {
            "NetworkManager",
            "AudioManager",
            "AuthManager",
            "CatalogLoader",
            "LoadoutManager",
            "SnapshotApplier",
            "LoadingScreen",
            "PostProcessController",
        };

        [MenuItem("Castle Defender/Setup/Migrate Singletons to Bootstrap")]
        static void Run()
        {
            // Open both scenes additively so we can move GOs between them
            var loginScene     = EditorSceneManager.OpenScene(LoginPath,     OpenSceneMode.Additive);
            var bootstrapScene = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Additive);

            if (!loginScene.IsValid())     { Debug.LogError("[MigrateSingletons] Could not open Login.unity");     return; }
            if (!bootstrapScene.IsValid()) { Debug.LogError("[MigrateSingletons] Could not open Bootstrap.unity"); return; }

            // Build a set of component types to look for
            var typeSet = new HashSet<string>(SingletonTypes);

            var moved = new List<string>();
            var roots = loginScene.GetRootGameObjects();

            foreach (var go in roots)
            {
                bool isSingleton = false;
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    if (typeSet.Contains(comp.GetType().Name))
                    {
                        isSingleton = true;
                        break;
                    }
                }

                if (!isSingleton) continue;

                // Already present in Bootstrap? Skip (avoid duplicates)
                bool alreadyInBootstrap = false;
                foreach (var bgo in bootstrapScene.GetRootGameObjects())
                {
                    if (bgo.name == go.name) { alreadyInBootstrap = true; break; }
                }
                if (alreadyInBootstrap)
                {
                    Debug.Log($"[MigrateSingletons] Skipped (already in Bootstrap): {go.name}");
                    continue;
                }

                SceneManager.MoveGameObjectToScene(go, bootstrapScene);
                moved.Add(go.name);
                Debug.Log($"[MigrateSingletons] Moved → Bootstrap: {go.name}");
            }

            if (moved.Count > 0)
            {
                EditorSceneManager.SaveScene(loginScene);
                EditorSceneManager.SaveScene(bootstrapScene);
                Debug.Log($"[MigrateSingletons] Done. Moved {moved.Count} GO(s): {string.Join(", ", moved)}");
            }
            else
            {
                Debug.Log("[MigrateSingletons] Nothing to move — all singletons already in Bootstrap or not found in Login.");
            }

            // Close additive scenes, return to whatever was open before
            EditorSceneManager.CloseScene(loginScene,     true);
            EditorSceneManager.CloseScene(bootstrapScene, true);

            EditorUtility.DisplayDialog("Migrate Singletons",
                moved.Count > 0
                    ? $"Moved {moved.Count} singleton(s) to Bootstrap.unity:\n\n• {string.Join("\n• ", moved)}"
                    : "Nothing to move — singletons already in Bootstrap or not found in Login.",
                "OK");
        }
    }
}
