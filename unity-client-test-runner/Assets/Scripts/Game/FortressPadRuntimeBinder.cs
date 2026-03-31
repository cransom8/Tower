using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class FortressPadRuntimeBindingMarker : MonoBehaviour
    {
    }

    [DefaultExecutionOrder(-950)]
    public sealed class FortressPadRuntimeBinder : MonoBehaviour
    {
        sealed class LaneSpec
        {
            public string scenePrefix;
            public FortressPadAnchor.LaneColor laneColor;
        }

        sealed class RuntimePadSpec
        {
            public string padId;
            public string buildingType;
            public string displayName;
            public FortressPadAnchor.LaneColor laneColor;
            public string[] candidateNames;
            public int duplicateIndex;
        }

        static readonly LaneSpec[] LaneSpecs =
        {
            new() { scenePrefix = "Red", laneColor = FortressPadAnchor.LaneColor.Red },
            new() { scenePrefix = "Yellow", laneColor = FortressPadAnchor.LaneColor.Gold },
            new() { scenePrefix = "Blue", laneColor = FortressPadAnchor.LaneColor.Blue },
            new() { scenePrefix = "Green", laneColor = FortressPadAnchor.LaneColor.Green },
        };

        static readonly (string padId, string buildingType, string displayName, string objectSuffix)[] BranchPadBindings =
        {
            ("stable_pad", "stable", "Stable", "Stable"),
            ("workshop_pad", "workshop", "Workshop", "Workshop"),
            ("library_pad", "library", "Library", "Library"),
            ("market_pad", "market", "Market", "Market"),
            ("lumber_mill_pad", "lumber_mill", "Lumber Mill", "LumberMill"),
        };

        static readonly (string padId, string displayName, string objectSuffix, string alternateSuffix, string redOverrideSuffix, int duplicateIndex)[] WallPadBindings =
        {
            ("wall_front_left_01_pad", "Front Left Wall 01", "Wall_Front_Left_01_Lvl_1", null, null, 0),
            ("wall_front_left_02_pad", "Front Left Wall 02", "Wall_Front_Left_02_Lvl_1", null, null, 0),
            ("wall_front_left_03_pad", "Front Left Wall 03", "Wall_Front_Left_03_Lvl_1", null, null, 0),
            ("wall_front_left_04_pad", "Front Left Wall 04", "Wall_Front_Left_04_Lvl_1", null, null, 0),
            ("wall_front_left_06_pad", "Front Left Wall 06", "Wall_Front_Left_06_Lvl_1", null, null, 0),
            ("wall_front_left_07_pad", "Front Left Wall 07", "Wall_Front_Left_07_Lvl_1", null, null, 0),
            ("wall_front_right_01_pad", "Front Right Wall 01", "Wall_Front_Right_01_Lvl_1", null, null, 0),
            ("wall_front_right_02_pad", "Front Right Wall 02", "Wall_Front_Right_02_Lvl_1", null, null, 0),
            ("wall_back_left_01_pad", "Back Left Wall 01", "Wall_Back_Left_01_Lvl_1", null, null, 0),
            ("wall_back_left_02_pad", "Back Left Wall 02", "Wall_Back_Left_02_Lvl_1", null, null, 0),
            ("wall_back_left_04_pad", "Back Left Wall 04", "Wall_Back_Left_04_Lvl_1", null, null, 0),
            ("wall_back_left_05_pad", "Back Left Wall 05", "Wall_Back_Left_05_Lvl_1", null, null, 0),
            ("wall_back_right_01_pad", "Back Right Wall 01", "Wall_Back_Right_01_Lvl_1", null, null, 0),
            ("wall_back_right_02_pad", "Back Right Wall 02", "Wall_Back_Right_02_Lvl_1", null, null, 0),
            // Red lane still uses the older 01-06 names for this wall run in the environment prefab.
            ("wall_right_side_03_pad", "Right Side Wall 03", "Wall_Right_Side_03_Lvl_1", null, "Wall_Right_Side_01_Lvl_1", 0),
            ("wall_right_side_04_a_pad", "Right Side Wall 04A", "Wall_Right_Side_04_Lvl_1", null, "Wall_Right_Side_02_Lvl_1", 0),
            ("wall_right_side_04_b_pad", "Right Side Wall 04B", "Wall_Right_Side_04_Lvl_1", "Wall_Right_Side_04_Lvl_1 (1)", "Wall_Right_Side_03_Lvl_1", 1),
            ("wall_right_side_05_pad", "Right Side Wall 05", "Wall_Right_Side_05_Lvl_1", null, "Wall_Right_Side_04_Lvl_1", 0),
            ("wall_right_side_06_pad", "Right Side Wall 06", "Wall_Right_Side_06_Lvl_1", null, "Wall_Right_Side_05_Lvl_1", 0),
            ("wall_right_side_07_pad", "Right Side Wall 07", "Wall_Right_Side_07_Lvl_1", null, "Wall_Right_Side_06_Lvl_1", 0),
        };

        static readonly (string padId, string displayName, string objectSuffix)[] GatePadBindings =
        {
            ("gate_front_pad", "Front Gate", "Gate_Front"),
            ("gate_left_pad", "Left Gate", "Gate_Left"),
            ("gate_right_pad", "Right Gate", "Gate_Right"),
            ("gate_rear_pad", "Rear Gate", "Gate_Rear"),
        };

        static readonly (string padId, string displayName, string objectSuffix)[] TurretPadBindings =
        {
            ("turret_front_left_pad", "Front Left Tower", "Tower_Front_Left_Lvl_1"),
            ("turret_front_left_05_pad", "Front Left Tower 05", "Tower_Front_Left_05_Lvl_1"),
            ("turret_front_right_pad", "Front Right Tower", "Tower_Front_Right_Lvl_1"),
            ("turret_core_03_pad", "Inner Tower 03", "Tower_lvl_3"),
            ("turret_core_04_pad", "Inner Tower 04", "Tower_lvl_4"),
            ("turret_core_05_pad", "Inner Tower 05", "Tower_lvl_5"),
            ("turret_front_gate_left_pad", "Front Gate Tower Left", "Tower_Front_Gate_Left_lvl_1"),
            ("turret_front_gate_right_pad", "Front Gate Tower Right", "Tower_Front_Gate_Right_lvl_1"),
            ("turret_back_left_03_pad", "Back Left Tower 03", "Tower_Back_Left_03_Lvl_1"),
            ("turret_back_left_06_pad", "Back Left Tower 06", "Tower_Back_Left_06_Lvl_1"),
            ("turret_back_left_07_pad", "Back Left Tower 07", "Tower_Back_Left_07_Lvl_1"),
            ("turret_back_right_03_pad", "Back Right Tower 03", "Tower_Back_Right_03_Lvl_1"),
            ("turret_rear_gate_left_pad", "Rear Gate Tower Left", "Tower_Rear_Gate_Left_lvl_1"),
            ("turret_rear_gate_right_pad", "Rear Gate Tower Right", "Tower_Rear_Gate_Right_lvl_1"),
            ("turret_right_side_05_pad", "Right Side Tower 05", "Tower_Right_Side_05_Lvl_1"),
            ("turret_right_side_06_pad", "Right Side Tower 06", "Tower_Right_Side_06_Lvl_1"),
            ("turret_right_side_07_pad", "Right Side Tower 07", "Tower_Right_Side_07_Lvl_1"),
        };

        static FortressPadRuntimeBinder s_instance;
        static readonly HashSet<string> s_missingLogs = new(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<string, List<Transform>> _objectsByName = new(StringComparer.Ordinal);
        readonly List<RuntimePadSpec> _runtimeSpecs = new();

        float _nextProbeAt;
        bool _bindingsComplete;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            EnsureInstance();
        }

        static FortressPadRuntimeBinder EnsureInstance()
        {
            if (s_instance != null)
                return s_instance;

            var go = new GameObject(nameof(FortressPadRuntimeBinder));
            return go.AddComponent<FortressPadRuntimeBinder>();
        }

        void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            BuildRuntimeSpecs();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (s_instance == this)
                s_instance = null;
        }

        void Update()
        {
            if (_bindingsComplete || Time.unscaledTime < _nextProbeAt)
                return;

            _nextProbeAt = Time.unscaledTime + 1f;
            TryBindRuntimePads();
        }

        void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            _bindingsComplete = false;
            _nextProbeAt = 0f;
        }

        void BuildRuntimeSpecs()
        {
            _runtimeSpecs.Clear();

            for (int laneIndex = 0; laneIndex < LaneSpecs.Length; laneIndex++)
            {
                var lane = LaneSpecs[laneIndex];

                for (int i = 0; i < BranchPadBindings.Length; i++)
                {
                    var spec = BranchPadBindings[i];
                    _runtimeSpecs.Add(new RuntimePadSpec
                    {
                        padId = spec.padId,
                        buildingType = spec.buildingType,
                        displayName = spec.displayName,
                        laneColor = lane.laneColor,
                        candidateNames = new[] { $"{lane.scenePrefix}_{spec.objectSuffix}" },
                        duplicateIndex = 0,
                    });
                }

                for (int i = 0; i < WallPadBindings.Length; i++)
                {
                    var spec = WallPadBindings[i];
                    _runtimeSpecs.Add(new RuntimePadSpec
                    {
                        padId = spec.padId,
                        buildingType = "wall",
                        displayName = spec.displayName,
                        laneColor = lane.laneColor,
                        candidateNames = BuildWallCandidateNames(lane.scenePrefix, spec.objectSuffix, spec.alternateSuffix, spec.redOverrideSuffix),
                        duplicateIndex = !string.IsNullOrWhiteSpace(spec.alternateSuffix)
                            && !string.Equals(lane.scenePrefix, "Red", StringComparison.Ordinal)
                            ? 0
                            : spec.duplicateIndex,
                    });
                }

                for (int i = 0; i < GatePadBindings.Length; i++)
                {
                    var spec = GatePadBindings[i];
                    _runtimeSpecs.Add(new RuntimePadSpec
                    {
                        padId = spec.padId,
                        buildingType = "gate",
                        displayName = spec.displayName,
                        laneColor = lane.laneColor,
                        candidateNames = new[] { $"{lane.scenePrefix}_{spec.objectSuffix}" },
                        duplicateIndex = 0,
                    });
                }

                for (int i = 0; i < TurretPadBindings.Length; i++)
                {
                    var spec = TurretPadBindings[i];
                    _runtimeSpecs.Add(new RuntimePadSpec
                    {
                        padId = spec.padId,
                        buildingType = "turret",
                        displayName = spec.displayName,
                        laneColor = lane.laneColor,
                        candidateNames = new[] { $"{lane.scenePrefix}_{spec.objectSuffix}" },
                        duplicateIndex = 0,
                    });
                }
            }
        }

        static string[] BuildCandidateNames(string lanePrefix, string objectSuffix, string alternateSuffix)
        {
            if (!string.IsNullOrWhiteSpace(alternateSuffix))
            {
                if (string.Equals(lanePrefix, "Red", StringComparison.Ordinal))
                    return new[] { $"{lanePrefix}_{objectSuffix}" };

                return new[]
                {
                    $"{lanePrefix}_{alternateSuffix}",
                    $"{lanePrefix}_{objectSuffix}",
                };
            }

            return new[]
            {
                $"{lanePrefix}_{objectSuffix}",
            };
        }

        static string[] BuildWallCandidateNames(string lanePrefix, string objectSuffix, string alternateSuffix, string redOverrideSuffix)
        {
            if (string.Equals(lanePrefix, "Red", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(redOverrideSuffix))
            {
                return new[]
                {
                    $"{lanePrefix}_{redOverrideSuffix}",
                };
            }

            return BuildCandidateNames(lanePrefix, objectSuffix, alternateSuffix);
        }

        void TryBindRuntimePads()
        {
            RebuildNameLookup();
            if (!LooksLikeFortressEnvironmentLoaded())
                return;

            int boundCount = 0;
            for (int i = 0; i < _runtimeSpecs.Count; i++)
            {
                if (EnsureBinding(_runtimeSpecs[i]))
                    boundCount++;
            }

            _bindingsComplete = boundCount >= _runtimeSpecs.Count;
        }

        void RebuildNameLookup()
        {
            _objectsByName.Clear();

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded)
                    continue;

                if (!_objectsByName.TryGetValue(candidate.name, out var bucket))
                {
                    bucket = new List<Transform>();
                    _objectsByName[candidate.name] = bucket;
                }

                bucket.Add(candidate);
            }

            foreach (var bucket in _objectsByName.Values)
                bucket.Sort(CompareHierarchyOrder);
        }

        bool LooksLikeFortressEnvironmentLoaded()
        {
            return _objectsByName.ContainsKey("Yellow_Row")
                || _objectsByName.ContainsKey("Blue_Row")
                || _objectsByName.ContainsKey("Green_Row")
                || _objectsByName.ContainsKey("Red_Blacksmith");
        }

        bool EnsureBinding(RuntimePadSpec spec)
        {
            string laneKey = FortressLaneResolver.LaneColorToLaneKey(spec.laneColor);
            if (!string.IsNullOrWhiteSpace(laneKey)
                && FortressPadAnchor.FindAnchor(spec.padId, laneKey, -1) != null)
            {
                return true;
            }

            Transform target = ResolveTargetTransform(spec);
            if (target == null)
            {
                string key = $"{laneKey}:{spec.padId}";
                if (s_missingLogs.Add(key))
                {
                    Debug.LogWarning(
                        $"[FortressPadRuntimeBinder] Could not find scene object for lane '{laneKey}' pad '{spec.padId}' ({spec.buildingType}).");
                }
                return false;
            }

            _ = target.GetComponent<FortressPadRuntimeBindingMarker>() ?? target.gameObject.AddComponent<FortressPadRuntimeBindingMarker>();
            var anchor = target.GetComponent<FortressPadAnchor>() ?? target.gameObject.AddComponent<FortressPadAnchor>();
            if (!string.IsNullOrWhiteSpace(anchor.padId)
                && !string.Equals(anchor.padId, spec.padId, StringComparison.OrdinalIgnoreCase))
            {
                string conflictKey = $"{laneKey}:{spec.padId}:conflict";
                if (s_missingLogs.Add(conflictKey))
                {
                    Debug.LogWarning(
                        $"[FortressPadRuntimeBinder] Scene object '{target.name}' was already bound to pad '{anchor.padId}' and cannot also bind '{spec.padId}' for lane '{laneKey}'.");
                }
                return false;
            }

            anchor.padId = spec.padId;
            anchor.padAliases = Array.Empty<string>();
            anchor.buildingType = spec.buildingType;
            anchor.laneColor = spec.laneColor;
            anchor.focusTransform = target;
            anchor.labelTransform = target;
            anchor.explicitRenderers = target.GetComponentsInChildren<Renderer>(true);
            anchor.ghostVisualRoot = null;
            anchor.builtVisualRoot = null;
            anchor.autoSizeInteractionCollider = true;
            anchor.minimumColliderSize = ResolveMinimumColliderSize(spec.buildingType);

            var collider = target.GetComponent<Collider>();
            if (collider == null)
                collider = target.gameObject.AddComponent<BoxCollider>();
            anchor.interactionCollider = collider;
            anchor.FinalizeRuntimeBinding();

            var bridge = target.GetComponent<SnapshotBuildingVisualBridge>() ?? target.gameObject.AddComponent<SnapshotBuildingVisualBridge>();
            bridge.ConfigureRuntime(anchor.explicitRenderers);

            return true;
        }

        Transform ResolveTargetTransform(RuntimePadSpec spec)
        {
            for (int nameIndex = 0; nameIndex < spec.candidateNames.Length; nameIndex++)
            {
                string candidateName = spec.candidateNames[nameIndex];
                if (string.IsNullOrWhiteSpace(candidateName))
                    continue;
                if (!_objectsByName.TryGetValue(candidateName, out var bucket) || bucket.Count <= 0)
                    continue;

                int index = Mathf.Clamp(spec.duplicateIndex, 0, bucket.Count - 1);
                return bucket[index];
            }

            return null;
        }

        static Vector3 ResolveMinimumColliderSize(string buildingType)
        {
            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wall":
                    return new Vector3(2.8f, 2.8f, 1.8f);
                case "gate":
                    return new Vector3(3.6f, 3.2f, 2.2f);
                case "turret":
                    return new Vector3(2.8f, 4.0f, 2.8f);
                default:
                    return new Vector3(2.2f, 2.4f, 2.2f);
            }
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
    }
}
