#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CastleDefender.Game;

namespace CastleDefender.Editor
{
    static class AuthorFortressPadAnchors
    {
        const string EnvironmentPrefabPath = "Assets/AddressableContent/Environment/GameEnvironment.prefab";

        sealed class MigrationStats
        {
            public int existingAnchors;
            public int createdAnchors;
            public int updatedAnchors;
            public int createdBridges;
            public int updatedBridges;
            public int missingTargets;
        }

        [MenuItem("Castle Defender/Debug/Author Fortress Pad Anchors In GameEnvironment")]
        static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[AuthorFortressPadAnchors] Failed to load '{EnvironmentPrefabPath}'.");
                return;
            }

            try
            {
                EnvironmentPrefabSafety.AssertValidCriticalEnvironmentRoot(root, EnvironmentPrefabPath);

                var runtimeSpecs = new List<FortressPadBindingCatalog.RuntimePadSpec>();
                FortressPadBindingCatalog.GetRuntimePadSpecs(runtimeSpecs);

                var existingByKey = new Dictionary<string, FortressPadAnchor>(StringComparer.OrdinalIgnoreCase);
                var existingAnchors = root.GetComponentsInChildren<FortressPadAnchor>(true);
                for (int i = 0; i < existingAnchors.Length; i++)
                {
                    var anchor = existingAnchors[i];
                    if (anchor == null)
                        continue;

                    string key = BuildAnchorKey(anchor.padId, anchor.laneColor);
                    if (!string.IsNullOrWhiteSpace(key) && !existingByKey.ContainsKey(key))
                        existingByKey.Add(key, anchor);
                }

                var objectsByName = BuildNameLookup(root.transform);
                var stats = new MigrationStats();

                for (int i = 0; i < runtimeSpecs.Count; i++)
                    ApplySpec(runtimeSpecs[i], existingByKey, objectsByName, stats);

                EnvironmentPrefabSafety.AssertValidCriticalEnvironmentRoot(root, "authored fortress migration");

                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (stats.missingTargets > 0)
                {
                    Debug.LogWarning(
                        $"[AuthorFortressPadAnchors] Saved authored fortress pad migration with missingTargets={stats.missingTargets}. " +
                        $"existing={stats.existingAnchors} created={stats.createdAnchors} updated={stats.updatedAnchors} " +
                        $"bridgeCreated={stats.createdBridges} bridgeUpdated={stats.updatedBridges}. " +
                        "Resolve the missing authored targets before treating the prefab as migration-complete.");
                }
                else
                {
                    Debug.Log(
                        $"[AuthorFortressPadAnchors] Authored all runtime fortress pad specs into '{EnvironmentPrefabPath}'. " +
                        $"existing={stats.existingAnchors} created={stats.createdAnchors} updated={stats.updatedAnchors} " +
                        $"bridgeCreated={stats.createdBridges} bridgeUpdated={stats.updatedBridges}.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void ApplySpec(
            FortressPadBindingCatalog.RuntimePadSpec spec,
            Dictionary<string, FortressPadAnchor> existingByKey,
            Dictionary<string, List<Transform>> objectsByName,
            MigrationStats stats)
        {
            string key = BuildAnchorKey(spec.PadId, spec.LaneColor);
            if (existingByKey.TryGetValue(key, out var existingAnchor) && existingAnchor != null)
            {
                stats.existingAnchors++;
                ConfigureAnchor(existingAnchor, spec);
                stats.updatedAnchors++;
                EnsureBridge(existingAnchor.gameObject, spec, ref stats.createdBridges, ref stats.updatedBridges);
                return;
            }

            Transform target = ResolveTargetTransform(spec, objectsByName);
            if (target == null)
            {
                stats.missingTargets++;
                Debug.LogWarning(
                    $"[AuthorFortressPadAnchors] Missing target transform for lane '{LaneColorToLaneKey(spec.LaneColor)}' " +
                    $"pad '{spec.PadId}' ({spec.BuildingType}).");
                return;
            }

            var conflictingAnchor = target.GetComponent<FortressPadAnchor>();
            if (conflictingAnchor != null && !string.IsNullOrWhiteSpace(conflictingAnchor.padId))
            {
                Debug.LogError(
                    $"[AuthorFortressPadAnchors] Refusing to overwrite existing FortressPadAnchor '{conflictingAnchor.padId}' " +
                    $"on '{BuildScenePath(target)}' with '{spec.PadId}'.");
                return;
            }

            var anchor = conflictingAnchor != null ? conflictingAnchor : Undo.AddComponent<FortressPadAnchor>(target.gameObject);
            ConfigureAnchor(anchor, spec);
            existingByKey[key] = anchor;
            stats.createdAnchors++;
            EnsureBridge(target.gameObject, spec, ref stats.createdBridges, ref stats.updatedBridges);
        }

        static void ConfigureAnchor(FortressPadAnchor anchor, FortressPadBindingCatalog.RuntimePadSpec spec)
        {
            anchor.padId = spec.PadId;
            anchor.padAliases = Array.Empty<string>();
            anchor.buildingType = spec.BuildingType;
            anchor.laneColor = spec.LaneColor;
            anchor.focusTransform = anchor.transform;
            anchor.labelTransform = anchor.transform;
            anchor.explicitRenderers = anchor.GetComponentsInChildren<Renderer>(true);
            anchor.ghostVisualRoot = null;
            anchor.builtVisualRoot = null;
            anchor.autoSizeInteractionCollider = true;
            anchor.minimumColliderSize = FortressPadBindingCatalog.ResolveMinimumColliderSize(spec.BuildingType);

            var collider = anchor.GetComponent<Collider>();
            if (collider == null)
                collider = Undo.AddComponent<BoxCollider>(anchor.gameObject);
            anchor.interactionCollider = collider;

            anchor.EnsureInteractionCollider();
            EditorUtility.SetDirty(anchor);
        }

        static void EnsureBridge(GameObject target, FortressPadBindingCatalog.RuntimePadSpec spec, ref int createdBridges, ref int updatedBridges)
        {
            var bridge = target.GetComponent<SnapshotBuildingVisualBridge>();
            if (bridge == null)
            {
                bridge = Undo.AddComponent<SnapshotBuildingVisualBridge>(target);
                createdBridges++;
            }
            else
            {
                updatedBridges++;
            }

            bridge.ConfigureForEditor(
                configuredCatalog: null,
                configuredBuildingTypeOverride: spec.BuildingType,
                configuredLegacyRenderers: target.GetComponentsInChildren<Renderer>(true));
            EditorUtility.SetDirty(bridge);
        }

        static Dictionary<string, List<Transform>> BuildNameLookup(Transform root)
        {
            var lookup = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            if (root == null)
                return lookup;

            var stack = new Stack<Transform>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null)
                    continue;

                if (!lookup.TryGetValue(current.name, out var bucket))
                {
                    bucket = new List<Transform>();
                    lookup[current.name] = bucket;
                }

                bucket.Add(current);

                for (int i = current.childCount - 1; i >= 0; i--)
                    stack.Push(current.GetChild(i));
            }

            foreach (var bucket in lookup.Values)
                bucket.Sort(CompareHierarchyOrder);

            return lookup;
        }

        static Transform ResolveTargetTransform(
            FortressPadBindingCatalog.RuntimePadSpec spec,
            Dictionary<string, List<Transform>> objectsByName)
        {
            for (int i = 0; i < spec.CandidateNames.Length; i++)
            {
                string candidateName = spec.CandidateNames[i];
                if (string.IsNullOrWhiteSpace(candidateName))
                    continue;

                if (!objectsByName.TryGetValue(candidateName, out var bucket) || bucket.Count <= 0)
                    continue;

                int index = Mathf.Clamp(spec.DuplicateIndex, 0, bucket.Count - 1);
                return bucket[index];
            }

            return null;
        }

        static int CompareHierarchyOrder(Transform a, Transform b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            string aKey = BuildHierarchyKey(a);
            string bKey = BuildHierarchyKey(b);
            int compare = string.CompareOrdinal(aKey, bKey);
            if (compare != 0)
                return compare;

            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        }

        static string BuildHierarchyKey(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var parts = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                parts.Push(current.GetSiblingIndex().ToString("D4"));
            return string.Join("/", parts);
        }

        static string BuildAnchorKey(string padId, FortressPadAnchor.LaneColor laneColor)
        {
            if (string.IsNullOrWhiteSpace(padId) || laneColor == FortressPadAnchor.LaneColor.Any)
                return string.Empty;

            return $"{LaneColorToLaneKey(laneColor)}::{padId.Trim()}";
        }

        static string BuildScenePath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var parts = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }

        static string LaneColorToLaneKey(FortressPadAnchor.LaneColor laneColor)
        {
            return laneColor switch
            {
                FortressPadAnchor.LaneColor.Red => "red",
                FortressPadAnchor.LaneColor.Gold => "yellow",
                FortressPadAnchor.LaneColor.Blue => "blue",
                FortressPadAnchor.LaneColor.Green => "green",
                _ => string.Empty,
            };
        }
    }
}
#endif
