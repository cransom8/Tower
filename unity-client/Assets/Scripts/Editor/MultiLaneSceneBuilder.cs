// MultiLaneSceneBuilder.cs - Ensures Game_ML has the core multiplayer runtime objects.
//
// Menu: Castle Defender -> Setup -> Build 4-Lane Scene
//       Castle Defender -> Setup -> Apply to Game_ML Scene
//
// What it creates / wires:
//   * GameplayPresentationRoot - shared presentation host for combat visuals
//   * SnapshotApplier - singleton that feeds ML snapshots to presentation and UI systems
//   * NetworkManager - if absent (needed by SnapshotApplier.OnEnable)
//
// After running, also run "Wire Registry and Scene" to assign the UnitPrefabRegistry.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CastleDefender.Game;
using CastleDefender.Net;

namespace CastleDefender.Editor
{
    public static class MultiLaneSceneBuilder
    {
        [MenuItem("Castle Defender/Setup/Build 4-Lane Scene")]
        static void BuildCurrentScene()
        {
            BuildScene();
            Debug.Log("[MultiLane] Done. Run 'Wire Registry and Scene' next if prefabs are unassigned.");
        }

        [MenuItem("Castle Defender/Setup/Apply to Game_ML Scene")]
        static void ApplyToBothScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var sceneML = EditorSceneManager.OpenScene(EditorPaths.SCENE_ML, OpenSceneMode.Single);
            BuildScene();
            EditorSceneManager.SaveScene(sceneML);
            Debug.Log("[MultiLane] Game_ML built and saved. Run 'Wire Registry and Scene' to assign prefabs.");
        }

        static void BuildScene()
        {
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(EditorPaths.REGISTRY);
            if (registry == null)
            {
                Debug.LogWarning("[MultiLane] UnitPrefabRegistry not found at " + EditorPaths.REGISTRY +
                                 " - run 'Wire Registry and Scene' afterward.");
            }

            EnsureGameplayPresentationRoot(registry);
            EnsureSnapshotApplier();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        static void EnsureGameplayPresentationRoot(UnitPrefabRegistry registry)
        {
            var root = Object.FindFirstObjectByType<GameplayPresentationRoot>();
            if (root == null)
            {
                root = new GameObject("GameplayPresentation").AddComponent<GameplayPresentationRoot>();
                Debug.Log("[MultiLane] Created GameplayPresentationRoot.");
            }

            if (registry != null && root.Registry == null)
                root.Registry = registry;

            EditorUtility.SetDirty(root);
            Debug.Log("[MultiLane] GameplayPresentationRoot wired.");
        }

        static void EnsureSnapshotApplier()
        {
            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (nm == null)
            {
                nm = new GameObject("NetworkManager").AddComponent<NetworkManager>();
                Debug.Log("[MultiLane] Created NetworkManager.");
            }

            var sa = Object.FindFirstObjectByType<SnapshotApplier>();
            if (sa == null)
            {
                nm.gameObject.AddComponent<SnapshotApplier>();
                Debug.Log("[MultiLane] Added SnapshotApplier to NetworkManager.");
            }
        }
    }
}
#endif
