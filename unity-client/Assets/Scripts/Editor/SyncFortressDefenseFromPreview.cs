#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    static class SyncFortressDefenseFromPreview
    {
        const string MenuPath = "Castle Defender/Environment/Sync Fortress Defense From GameEnvironment 1";
        const string SourcePrefabPath = "Assets/AddressableContent/Environment/GameEnvironment 1.prefab";

        static readonly string[] TeamNames = { "Red", "Green", "Blue", "Yellow" };
        static readonly string[] WallSides = { "FRONT", "LEFT", "RIGHT", "REAR" };

        [MenuItem(MenuPath)]
        static void Run()
        {
            var sourceRoot = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            if (sourceRoot == null)
            {
                Debug.LogError($"[SyncFortressDefenseFromPreview] Failed to load source prefab '{SourcePrefabPath}'.");
                return;
            }

            var destinationRoot = PrefabUtility.LoadPrefabContents(EditorPaths.GAME_ENVIRONMENT_PREFAB);
            if (destinationRoot == null)
            {
                PrefabUtility.UnloadPrefabContents(sourceRoot);
                Debug.LogError($"[SyncFortressDefenseFromPreview] Failed to load destination prefab '{EditorPaths.GAME_ENVIRONMENT_PREFAB}'.");
                return;
            }

            try
            {
                int cloned = 0;
                int removed = 0;

                for (int teamIndex = 0; teamIndex < TeamNames.Length; teamIndex++)
                {
                    string team = TeamNames[teamIndex];
                    for (int sideIndex = 0; sideIndex < WallSides.Length; sideIndex++)
                    {
                        string side = WallSides[sideIndex];
                        Transform sourceParent = FindWallParent(sourceRoot.transform, team, side);
                        Transform destinationParent = FindWallParent(destinationRoot.transform, team, side);
                        if (sourceParent == null || destinationParent == null)
                        {
                            Debug.LogWarning(
                                $"[SyncFortressDefenseFromPreview] Skipped {team} {side} because one of the wall parents was missing.");
                            continue;
                        }

                        SyncParent(sourceParent, destinationParent, ref cloned, ref removed);
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(destinationRoot, EditorPaths.GAME_ENVIRONMENT_PREFAB);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[SyncFortressDefenseFromPreview] Synced defense hierarchy from '{SourcePrefabPath}' into '{EditorPaths.GAME_ENVIRONMENT_PREFAB}'. " +
                    $"cloned={cloned} removed={removed}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(destinationRoot);
                PrefabUtility.UnloadPrefabContents(sourceRoot);
            }
        }

        static void SyncParent(
            Transform sourceParent,
            Transform destinationParent,
            ref int cloned,
            ref int removed)
        {
            destinationParent.localPosition = sourceParent.localPosition;
            destinationParent.localRotation = sourceParent.localRotation;
            destinationParent.localScale = sourceParent.localScale;
            destinationParent.gameObject.SetActive(sourceParent.gameObject.activeSelf);

            var destinationChildren = GetDirectChildren(destinationParent);
            for (int index = destinationChildren.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(destinationChildren[index].gameObject);
                removed++;
            }

            var sourceChildren = GetDirectChildren(sourceParent);
            for (int index = 0; index < sourceChildren.Count; index++)
            {
                Transform sourceChild = sourceChildren[index];
                Transform destinationChild = UnityEngine.Object.Instantiate(sourceChild.gameObject, destinationParent).transform;
                destinationChild.name = sourceChild.name;
                destinationChild.localPosition = sourceChild.localPosition;
                destinationChild.localRotation = sourceChild.localRotation;
                destinationChild.localScale = sourceChild.localScale;
                destinationChild.gameObject.SetActive(sourceChild.gameObject.activeSelf);
                destinationChild.SetSiblingIndex(index);
                cloned++;
            }
        }

        static List<Transform> GetDirectChildren(Transform parent)
        {
            return parent == null
                ? new List<Transform>()
                : Enumerable.Range(0, parent.childCount)
                    .Select(parent.GetChild)
                    .Where(child => child != null)
                    .ToList();
        }

        static Transform FindWallParent(Transform root, string team, string side)
        {
            if (root == null)
                return null;

            return root.Find($"{team} Fort/Walls/{team.ToUpperInvariant()}_{side}_WALL");
        }
    }
}
#endif
