#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CastleDefender.Game;
using CastleDefender.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CastleDefender.Editor
{
    public static class SetupRemoteEnvironmentAddressables
    {
        const string ScenePath = "Assets/Scenes/Game_ML.unity";
        const string MapRootName = "Map";
        const string EnvironmentFolder = "Assets/AddressableContent/Environment";
        const string EnvironmentPrefabPath = EnvironmentFolder + "/GameEnvironment.prefab";
        const string GroupName = "Remote Environment";
        const string TemplateGroup = "Remote Units 01";
        const string ReportRelativePath = "projects/Game_ML Environment Extraction.md";
        const string RemoteBuildPathProfileId = "165fb4a3ad8d19e4aa002d6fc764a7ce";
        const string RemoteLoadPathProfileId = "247226ff3fd294f46b8dfca266320b8c";
        const string FloorTilePrefabPath = EditorPaths.TILE_FLOOR;
        const string WallTilePrefabPath = EditorPaths.TILE_WALL;
        const string CastleTilePrefabPath = EditorPaths.TILE_CASTLE;
        const string FloorTileAddress = "tiles/floor";
        const string WallTileAddress = "tiles/wall";
        const string CastleTileAddress = "tiles/castle";
        static readonly string[] LegacyToonyTinyPeopleBannerPrefixes =
        {
            "TT_Banner_Pole_",
            "TT_Flag_",
        };

        static readonly HashSet<Type> AllowedVisualComponentTypes = new()
        {
            typeof(Transform),
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(SkinnedMeshRenderer),
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(TrailRenderer),
            typeof(LineRenderer),
            typeof(Animator),
            typeof(LODGroup),
            typeof(SpriteRenderer),
            typeof(Light),
            typeof(UniversalAdditionalLightData),
            typeof(ReflectionProbe),
            typeof(Projector),
            typeof(FlareLayer),
        };

        static readonly HashSet<string> AllowedVisualMonoBehaviourTypes = new(StringComparer.Ordinal)
        {
            "WinterChristmasVillage.ChristmasLights",
        };

        static readonly HashSet<string> ExplicitExtractableRoots = new(StringComparer.OrdinalIgnoreCase)
        {
            // Audited in Game_ML as pure environment dressing from the Winter pack.
            "WinterDecor",
        };

        static readonly string[] KeepLocalNameFragments =
        {
            "waypoint",
            "spawn",
            "trigger",
            "zone",
            "tile",
            "grid",
            "path",
            "loader",
        };

        static readonly string[] EmbeddedEnvironmentRootNames =
        {
            "GameEnvironment",
            "GameEnvironmentOptional",
            RemoteContentManager.GameMlEnvironmentRootName,
            RemoteContentManager.GameMlEnvironmentDressingRootName,
        };

        public static void ExtractGameMlEnvironment()
        {
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                var mapRoot = GameObject.Find(MapRootName);
                if (mapRoot == null)
                {
                    Debug.LogError($"[SetupRemoteEnvironmentAddressables] Could not find '{MapRootName}' in {ScenePath}.");
                    return;
                }

                AssertNoLiveEnvironmentInstances(mapRoot.transform);

                var candidates = new List<Transform>();
                var keptLocal = new List<BranchDecision>();
                foreach (Transform child in mapRoot.transform)
                {
                    var decision = EvaluateBranch(child);
                    if (decision.CanExtract)
                        candidates.Add(child);
                    else
                        keptLocal.Add(decision);
                }

                if (candidates.Count == 0)
                {
                    Debug.LogWarning("[SetupRemoteEnvironmentAddressables] No extractable environment branches were found. The scene may already be extracted.");
                    return;
                }

                EnsureFolder("Assets/AddressableContent");
                EnsureFolder(EnvironmentFolder);

                var prefabRoot = BuildPrefabRootWithExistingContent();
                try
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        RemoveChildIfPresent(prefabRoot.transform, candidates[i].name);
                        var clone = UnityEngine.Object.Instantiate(candidates[i].gameObject, prefabRoot.transform);
                        clone.name = candidates[i].name;
                        StripColliders(clone.transform);
                    }

                    StripLegacyToonyTinyPeopleBannerMeshes(prefabRoot.transform);

                    var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, EnvironmentPrefabPath);
                    if (prefab == null)
                    {
                        Debug.LogError($"[SetupRemoteEnvironmentAddressables] Failed to save prefab at '{EnvironmentPrefabPath}'.");
                        return;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(prefabRoot);
                }

                if (!EnsureAddressablesEntry(EnvironmentPrefabPath, RemoteContentManager.GameMlEnvironmentAddress))
                    return;

                EnsureEnvironmentLoader(mapRoot);

                var removedNames = new List<string>(candidates.Count);
                for (int i = 0; i < candidates.Count; i++)
                {
                    removedNames.Add(candidates[i].name);
                    UnityEngine.Object.DestroyImmediate(candidates[i].gameObject);
                }

                SafeSetDirty(mapRoot);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                WriteReport(removedNames, keptLocal);
                Debug.Log(
                    $"[SetupRemoteEnvironmentAddressables] Extracted {removedNames.Count} branches to '{EnvironmentPrefabPath}' " +
                    $"and registered address '{RemoteContentManager.GameMlEnvironmentAddress}'.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        [MenuItem("Castle Defender/Remote Content/Sync Winter Tile Prefabs To Addressables")]
        static void SyncWinterTilePrefabsToAddressables()
        {
            try
            {
                var tileEntries = new (string assetPath, string address)[]
                {
                    (FloorTilePrefabPath, FloorTileAddress),
                    (WallTilePrefabPath, WallTileAddress),
                    (CastleTilePrefabPath, CastleTileAddress),
                };

                int synced = 0;
                foreach (var tileEntry in tileEntries)
                {
                    if (!File.Exists(tileEntry.assetPath))
                    {
                        Debug.LogWarning($"[SetupRemoteEnvironmentAddressables] Tile prefab missing: {tileEntry.assetPath}");
                        continue;
                    }

                    if (EnsureAddressablesEntry(tileEntry.assetPath, tileEntry.address))
                        synced++;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[SetupRemoteEnvironmentAddressables] Synced {synced}/{tileEntries.Length} winter tile prefabs to '{GroupName}'.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        [MenuItem("Castle Defender/Remote Content/Remove Embedded Game_ML Environment Roots")]
        public static void RemoveEmbeddedGameMlEnvironmentRootsMenu()
        {
            RemoveEmbeddedGameMlEnvironmentRoots(logResult: true);
        }

        public static int RemoveEmbeddedGameMlEnvironmentRoots(bool logResult = false)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var mapRoot = GameObject.Find(MapRootName);
            if (mapRoot == null)
            {
                if (logResult)
                    Debug.LogWarning($"[SetupRemoteEnvironmentAddressables] Could not find '{MapRootName}' in {ScenePath}.");

                return 0;
            }

            int removed = 0;
            for (int i = 0; i < EmbeddedEnvironmentRootNames.Length; i++)
            {
                string rootName = EmbeddedEnvironmentRootNames[i];
                while (true)
                {
                    var child = mapRoot.transform.Find(rootName);
                    if (child == null)
                        break;

                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    removed++;
                }
            }

            EnsureEnvironmentLoader(mapRoot);

            if (removed > 0)
            {
                SafeSetDirty(mapRoot);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (logResult)
                Debug.Log($"[SetupRemoteEnvironmentAddressables] Removed {removed} embedded environment root(s) from '{ScenePath}'.");

            return removed;
        }

        [MenuItem("Castle Defender/Remote Content/Sanitize GameEnvironment Prefab")]
        public static void SanitizeGameEnvironmentPrefabMenu()
        {
            SanitizeGameEnvironmentPrefab(logResult: true);
        }

        public static int SanitizeGameEnvironmentPrefab(bool logResult = false)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPrefabPath) == null)
            {
                if (logResult)
                    Debug.LogWarning($"[SetupRemoteEnvironmentAddressables] Prefab '{EnvironmentPrefabPath}' was not found.");

                return 0;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            if (prefabRoot == null)
            {
                if (logResult)
                    Debug.LogWarning($"[SetupRemoteEnvironmentAddressables] Prefab '{EnvironmentPrefabPath}' was not found.");

                return 0;
            }

            try
            {
                int removedBannerRenderers = StripLegacyToonyTinyPeopleBannerMeshes(prefabRoot.transform);
                int consolidatedDefensePads = ConsolidateDuplicateDefensePadAnchors(prefabRoot.transform);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, EnvironmentPrefabPath);
                AssetDatabase.SaveAssets();

                if (logResult)
                {
                    Debug.Log(
                        $"[SetupRemoteEnvironmentAddressables] Sanitized '{EnvironmentPrefabPath}'. " +
                        $"removedBannerRenderers={removedBannerRenderers} consolidatedDuplicateDefensePads={consolidatedDefensePads}.");
                }

                return removedBannerRenderers + consolidatedDefensePads;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static int ConsolidateDuplicateDefensePadAnchors(Transform root)
        {
            if (root == null)
                return 0;

            var groups = new Dictionary<string, List<FortressPadAnchor>>(StringComparer.OrdinalIgnoreCase);
            var anchors = root.GetComponentsInChildren<FortressPadAnchor>(true);
            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor == null
                    || anchor.AnchorLaneColor == FortressPadAnchor.LaneColor.Any
                    || string.IsNullOrWhiteSpace(anchor.PadId)
                    || !IsSharedDefenseBuildingType(anchor.BuildingType))
                {
                    continue;
                }

                string key = $"{(int)anchor.AnchorLaneColor}:{anchor.PadId.Trim()}";
                if (!groups.TryGetValue(key, out var bucket))
                {
                    bucket = new List<FortressPadAnchor>();
                    groups.Add(key, bucket);
                }

                bucket.Add(anchor);
            }

            int consolidatedCount = 0;
            foreach (var entry in groups)
            {
                var bucket = entry.Value;
                if (bucket == null || bucket.Count <= 1)
                    continue;

                bucket.Sort(CompareDuplicateDefenseAnchors);
                var primary = bucket[0];
                var mergedRenderers = new List<Renderer>(32);
                var supplementalRoots = new Transform[Mathf.Max(0, bucket.Count - 1)];
                AppendUniqueRenderers(mergedRenderers, primary.GetPrimaryRenderers());

                for (int i = 1; i < bucket.Count; i++)
                {
                    AppendUniqueRenderers(mergedRenderers, bucket[i].GetPrimaryRenderers());
                    supplementalRoots[i - 1] = bucket[i] != null ? bucket[i].transform : null;
                }

                if (mergedRenderers.Count > 0)
                    primary.explicitRenderers = mergedRenderers.ToArray();

                primary.autoSizeInteractionCollider = true;
                EditorUtility.SetDirty(primary);

                var primaryBridge = primary.GetComponent<SnapshotBuildingVisualBridge>();
                if (primaryBridge != null)
                {
                    primaryBridge.ConfigureForEditor(
                        configuredCatalog: null,
                        configuredBuildingTypeOverride: primary.BuildingType,
                        configuredLegacyRenderers: primary.explicitRenderers,
                        configuredSupplementalVisualRoots: supplementalRoots);
                    EditorUtility.SetDirty(primaryBridge);
                }

                for (int i = 1; i < bucket.Count; i++)
                {
                    var secondary = bucket[i];
                    if (secondary == null)
                        continue;

                    var secondaryBridge = secondary.GetComponent<SnapshotBuildingVisualBridge>();
                    if (secondaryBridge != null)
                        UnityEngine.Object.DestroyImmediate(secondaryBridge);

                    var secondaryHud = secondary.GetComponent<FortressPadHealthView>();
                    if (secondaryHud != null)
                        UnityEngine.Object.DestroyImmediate(secondaryHud);

                    var secondaryCollider = secondary.GetComponent<Collider>();
                    if (secondaryCollider != null)
                        UnityEngine.Object.DestroyImmediate(secondaryCollider);

                    UnityEngine.Object.DestroyImmediate(secondary);
                    consolidatedCount += 1;
                }
            }

            return consolidatedCount;
        }

        static void AppendUniqueRenderers(List<Renderer> results, Renderer[] renderers)
        {
            if (results == null || renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer != null && !results.Contains(renderer))
                    results.Add(renderer);
            }
        }

        static int CompareDuplicateDefenseAnchors(FortressPadAnchor left, FortressPadAnchor right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int suffixCompare = GetDuplicateNamePriority(left.name).CompareTo(GetDuplicateNamePriority(right.name));
            if (suffixCompare != 0)
                return suffixCompare;

            return string.CompareOrdinal(BuildTransformPath(left.transform), BuildTransformPath(right.transform));
        }

        static int GetDuplicateNamePriority(string name)
        {
            return string.IsNullOrWhiteSpace(name) || name.IndexOf(" (", StringComparison.Ordinal) < 0 ? 0 : 1;
        }

        static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var parts = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }

        static bool IsSharedDefenseBuildingType(string buildingType)
        {
            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wall":
                case "gate":
                case "turret":
                    return true;
                default:
                    return false;
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static BranchDecision EvaluateBranch(Transform root)
        {
            var decision = new BranchDecision
            {
                Name = root.name,
                Path = BuildPath(root),
                Reason = "Eligible visual branch.",
            };

            if (ExplicitExtractableRoots.Contains(root.name))
            {
                decision.CanExtract = true;
                decision.Reason = "Explicit extractable visual branch.";
                return decision;
            }

            for (int i = 0; i < KeepLocalNameFragments.Length; i++)
            {
                if (root.name.IndexOf(KeepLocalNameFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    decision.Reason = $"Name matched keep-local fragment '{KeepLocalNameFragments[i]}'.";
                    return decision;
                }
            }

            bool hasVisuals = false;
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(node.tag, "Untagged", StringComparison.OrdinalIgnoreCase))
                {
                    decision.Reason = $"Tagged object '{node.name}' found ({node.tag}).";
                    return decision;
                }

                var components = node.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (component == null)
                    {
                        decision.Reason = $"Missing script on '{node.name}'.";
                        return decision;
                    }

                    if (component is Transform)
                        continue;

                    if (component is Renderer || component is ParticleSystem || component is Light)
                        hasVisuals = true;

                    if (component is Collider collider)
                    {
                        if (collider.isTrigger)
                        {
                            decision.Reason = $"Trigger collider found on '{node.name}'.";
                            return decision;
                        }

                        continue;
                    }

                    if (component is MonoBehaviour)
                    {
                        if (AllowedVisualMonoBehaviourTypes.Contains(component.GetType().FullName ?? component.GetType().Name))
                            continue;

                        decision.Reason = $"Custom MonoBehaviour '{component.GetType().Name}' found on '{node.name}'.";
                        return decision;
                    }

                    if (!AllowedVisualComponentTypes.Contains(component.GetType()))
                    {
                        decision.Reason = $"Unsupported component '{component.GetType().Name}' found on '{node.name}'.";
                        return decision;
                    }
                }
            }

            if (!hasVisuals)
            {
                decision.Reason = "Branch does not contain visual components.";
                return decision;
            }

            decision.CanExtract = true;
            return decision;
        }

        static void StripColliders(Transform root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                UnityEngine.Object.DestroyImmediate(colliders[i], true);
        }

        static GameObject BuildPrefabRootWithExistingContent()
        {
            var prefabRoot = new GameObject("GameEnvironment");
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPrefabPath);
            if (existingPrefab == null)
                return prefabRoot;

            var existingInstance = UnityEngine.Object.Instantiate(existingPrefab);
            try
            {
                foreach (Transform child in existingInstance.transform)
                {
                    var clone = UnityEngine.Object.Instantiate(child.gameObject, prefabRoot.transform);
                    clone.name = child.name;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(existingInstance);
            }

            return prefabRoot;
        }

        static void RemoveChildIfPresent(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
                return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (!string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                    continue;

                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        static int StripLegacyToonyTinyPeopleBannerMeshes(Transform root)
        {
            if (root == null)
                return 0;

            var removals = root.GetComponentsInChildren<Transform>(true)
                .Where(ShouldRemoveLegacyBannerNode)
                .OrderByDescending(GetHierarchyDepth)
                .ToArray();

            for (int i = 0; i < removals.Length; i++)
                UnityEngine.Object.DestroyImmediate(removals[i].gameObject);

            return removals.Length;
        }

        static bool ShouldRemoveLegacyBannerNode(Transform transform)
        {
            if (transform == null)
                return false;

            for (int i = 0; i < LegacyToonyTinyPeopleBannerPrefixes.Length; i++)
            {
                if (transform.name.StartsWith(LegacyToonyTinyPeopleBannerPrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static int GetHierarchyDepth(Transform transform)
        {
            int depth = 0;
            var current = transform;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        static void EnsureEnvironmentLoader(GameObject mapRoot)
        {
            var loader = mapRoot.GetComponent<EnvironmentLoader>();
            if (loader == null)
                loader = Undo.AddComponent<EnvironmentLoader>(mapRoot);

            loader.environmentAddress = RemoteContentManager.GameMlEnvironmentAddress;
            loader.instantiateParent = mapRoot.transform;
            loader.instantiatedRootName = RemoteContentManager.GameMlEnvironmentRootName;
            loader.failureTitle = "Required map environment failed to load.";
            loader.readinessTimeoutSeconds = 12f;
            loader.enabled = true;
            SafeSetDirty(loader);

            var optionalLoader = mapRoot.GetComponent<OptionalEnvironmentLoader>();
            if (optionalLoader == null)
                optionalLoader = Undo.AddComponent<OptionalEnvironmentLoader>(mapRoot);

            optionalLoader.optionalEnvironmentAddress = RemoteContentManager.GameMlEnvironmentDressingAddress;
            optionalLoader.instantiateParent = mapRoot.transform;
            optionalLoader.instantiatedRootName = RemoteContentManager.GameMlEnvironmentDressingRootName;
            optionalLoader.instantiatedRootScale = RemoteContentManager.GameMlEnvironmentDressingScale;
            optionalLoader.requiredRootName = RemoteContentManager.GameMlEnvironmentRootName;
            optionalLoader.waitForCriticalTimeoutSeconds = 15f;
            optionalLoader.loadStartDelaySeconds = 0.25f;
            optionalLoader.logWarnings = true;
            optionalLoader.enabled = true;
            SafeSetDirty(optionalLoader);
        }

        static void AssertNoLiveEnvironmentInstances(Transform mapRoot)
        {
            if (mapRoot == null)
                throw new InvalidOperationException("[SetupRemoteEnvironmentAddressables] Map root is required before extraction.");

            for (int i = 0; i < EmbeddedEnvironmentRootNames.Length; i++)
            {
                string rootName = EmbeddedEnvironmentRootNames[i];
                if (mapRoot.Find(rootName) == null)
                    continue;

                throw new InvalidOperationException(
                    $"[SetupRemoteEnvironmentAddressables] Refusing to extract because '{MapRootName}/{rootName}' already exists in '{ScenePath}'. " +
                    "This scene is already using the authored remote environment. " +
                    "Use 'Castle Defender/Remote Content/Commit Scene Environment Instances' to persist live map edits instead of re-extracting legacy scene branches.");
            }
        }

        static bool EnsureAddressablesEntry(string assetPath, string address)
        {
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
            {
                Debug.LogError("[SetupRemoteEnvironmentAddressables] Addressables editor API unavailable.");
                return false;
            }

            object settings = null;
            var getSettings = settingsDefaultType.GetMethod(
                "GetSettings", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (getSettings != null)
                settings = getSettings.Invoke(null, new object[] { true });
            settings ??= settingsDefaultType
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (settings == null)
            {
                Debug.LogError("[SetupRemoteEnvironmentAddressables] Could not load AddressableAssetSettings.");
                return false;
            }

            var settingsType = settings.GetType();
            var findGroupMethod = settingsType.GetMethod(
                "FindGroup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            var createOrMoveEntryMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateOrMoveEntry" && m.GetParameters().Length >= 2);
            var createGroupMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "CreateGroup") return false;
                    var parameters = m.GetParameters();
                    return parameters.Length >= 4
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(bool);
                });

            if (findGroupMethod == null || createOrMoveEntryMethod == null || createGroupMethod == null)
            {
                Debug.LogError("[SetupRemoteEnvironmentAddressables] Required Addressables API methods not found.");
                return false;
            }

            var bundledSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            var contentUpdateSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");

            var group = findGroupMethod.Invoke(settings, new object[] { GroupName });
            if (group == null)
            {
                var templateGroup = findGroupMethod.Invoke(settings, new object[] { TemplateGroup });
                var parameters = createGroupMethod.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    if (parameter.ParameterType == typeof(string))
                        args[i] = GroupName;
                    else if (parameter.ParameterType == typeof(bool))
                        args[i] = false;
                    else if (parameter.ParameterType == typeof(Type[]))
                        args[i] = new[] { bundledSchemaType, contentUpdateSchemaType };
                    else if (parameter.HasDefaultValue)
                        args[i] = parameter.DefaultValue;
                    else
                        args[i] = parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
                }

                group = createGroupMethod.Invoke(settings, args);
                if (group != null && templateGroup != null)
                    CopySchemaSettings(templateGroup, group, bundledSchemaType, contentUpdateSchemaType);
            }

            if (group == null)
            {
                Debug.LogError("[SetupRemoteEnvironmentAddressables] Failed to resolve Remote Environment group.");
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogError($"[SetupRemoteEnvironmentAddressables] Could not resolve GUID for '{assetPath}'.");
                return false;
            }

            var entryParameters = createOrMoveEntryMethod.GetParameters();
            object entry;
            if (entryParameters.Length >= 4)
                entry = createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false, false });
            else if (entryParameters.Length == 3)
                entry = createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false });
            else
                entry = createOrMoveEntryMethod.Invoke(settings, new[] { guid, group });

            if (entry == null)
            {
                Debug.LogError("[SetupRemoteEnvironmentAddressables] Failed to create Addressables entry for GameEnvironment.");
                return false;
            }

            entry.GetType()
                .GetProperty("address", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(entry, address);

            ForceRemoteBundledSchema(group, bundledSchemaType);
            SafeSetDirty(entry);
            SafeSetDirty(group);
            return true;
        }

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type bundledType, Type contentUpdateType)
        {
            var getSchema = sourceGroup.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchema == null)
                return;

            foreach (var schemaType in new[] { bundledType, contentUpdateType })
            {
                if (schemaType == null)
                    continue;

                var sourceSchema = getSchema.Invoke(sourceGroup, new object[] { schemaType });
                var targetSchema = getSchema.Invoke(targetGroup, new object[] { schemaType });
                if (sourceSchema == null || targetSchema == null)
                    continue;

                foreach (var property in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || !property.CanWrite) continue;
                    if (string.Equals(property.Name, "Group", StringComparison.OrdinalIgnoreCase)) continue;
                    if (property.GetIndexParameters().Length != 0) continue;

                    try
                    {
                        property.SetValue(targetSchema, property.GetValue(sourceSchema));
                    }
                    catch
                    {
                        // Ignore non-copyable schema properties.
                    }
                }

                SafeSetDirty(targetSchema);
            }

            SafeSetDirty(targetGroup);
        }

        static void ForceRemoteBundledSchema(object group, Type bundledSchemaType)
        {
            if (group == null || bundledSchemaType == null)
                return;

            var getSchema = group.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchema == null)
                return;

            var bundledSchema = getSchema.Invoke(group, new object[] { bundledSchemaType }) as UnityEngine.Object;
            if (bundledSchema == null)
                return;

            var serialized = new SerializedObject(bundledSchema);
            serialized.FindProperty("m_Name")?.SetValueIfPresent("Remote Environment_BundledAssetGroupSchema");
            serialized.FindProperty("m_BuildPath.m_Id")?.SetValueIfPresent(RemoteBuildPathProfileId);
            serialized.FindProperty("m_LoadPath.m_Id")?.SetValueIfPresent(RemoteLoadPathProfileId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SafeSetDirty(bundledSchema);
        }

        static void WriteReport(List<string> removedNames, List<BranchDecision> keptLocal)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string reportPath = Path.Combine(projectRoot, ReportRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? projectRoot);

            var builder = new StringBuilder();
            builder.AppendLine("# Game_ML Environment Extraction");
            builder.AppendLine();
            builder.AppendLine($"Scene: `{ScenePath}`");
            builder.AppendLine($"Prefab: `{EnvironmentPrefabPath}`");
            builder.AppendLine($"Address: `{RemoteContentManager.GameMlEnvironmentAddress}`");
            builder.AppendLine($"Generated: `{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
            builder.AppendLine();
            builder.AppendLine("## Extracted Branches");
            builder.AppendLine();

            if (removedNames.Count == 0)
            {
                builder.AppendLine("No branches were extracted.");
            }
            else
            {
                for (int i = 0; i < removedNames.Count; i++)
                    builder.AppendLine($"- `{removedNames[i]}`");
            }

            builder.AppendLine();
            builder.AppendLine("## Kept Local");
            builder.AppendLine();

            if (keptLocal.Count == 0)
            {
                builder.AppendLine("No branches were kept local.");
            }
            else
            {
                for (int i = 0; i < keptLocal.Count; i++)
                    builder.AppendLine($"- `{keptLocal[i].Path}`: {keptLocal[i].Reason}");
            }

            builder.AppendLine();
            builder.AppendLine("## Manual Verification");
            builder.AppendLine();
            builder.AppendLine("- Confirm gameplay still finds all waypoints, spawn points, zones, and triggers.");
            builder.AppendLine("- Confirm the runtime-instantiated environment lights and baked lighting still look correct.");
            builder.AppendLine("- Rebuild Addressables before testing the new environment gate.");

            File.WriteAllText(reportPath, builder.ToString());
        }

        static string BuildPath(Transform transform)
        {
            var segments = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        static void SafeSetDirty(object value)
        {
            if (value is UnityEngine.Object unityObject && unityObject != null)
                EditorUtility.SetDirty(unityObject);
        }

        static void SetValueIfPresent(this SerializedProperty property, string value)
        {
            if (property != null)
                property.stringValue = value;
        }

        sealed class BranchDecision
        {
            public string Name;
            public string Path;
            public bool CanExtract;
            public string Reason;
        }
    }
}
#endif
