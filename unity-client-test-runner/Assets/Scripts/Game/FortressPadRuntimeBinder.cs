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

        static readonly (string side, int count)[] WallPadBindings =
        {
            ("Front", 8),
            ("Left", 8),
            ("Right", 8),
            ("Rear", 8),
        };

        static readonly string[] GatePadBindings =
        {
            "Front",
            "Left",
            "Right",
            "Rear",
        };

        static readonly (string side, int count)[] TowerPadBindings =
        {
            ("Front", 6),
            ("Left", 4),
            ("Right", 4),
            ("Rear", 6),
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
                    var section = WallPadBindings[i];
                    for (int index = 1; index <= section.count; index++)
                    {
                        _runtimeSpecs.Add(new RuntimePadSpec
                        {
                            padId = $"wall_{section.side.ToLowerInvariant()}_{index}_pad",
                            buildingType = "wall",
                            displayName = $"{section.side} Wall {index}",
                            laneColor = lane.laneColor,
                            candidateNames = new[] { $"{lane.scenePrefix}_Wall_{section.side}_{index}" },
                            duplicateIndex = 0,
                        });
                    }
                }

                for (int i = 0; i < GatePadBindings.Length; i++)
                {
                    _runtimeSpecs.Add(new RuntimePadSpec
                    {
                        padId = $"gate_{GatePadBindings[i].ToLowerInvariant()}_pad",
                        buildingType = "gate",
                        displayName = $"{GatePadBindings[i]} Gate",
                        laneColor = lane.laneColor,
                        candidateNames = new[] { $"{lane.scenePrefix}_Gate_{GatePadBindings[i]}" },
                        duplicateIndex = 0,
                    });
                }

                for (int i = 0; i < TowerPadBindings.Length; i++)
                {
                    var section = TowerPadBindings[i];
                    for (int index = 1; index <= section.count; index++)
                    {
                        _runtimeSpecs.Add(new RuntimePadSpec
                        {
                            padId = $"tower_{section.side.ToLowerInvariant()}_{index}_pad",
                            buildingType = "turret",
                            displayName = $"{section.side} Tower {index}",
                            laneColor = lane.laneColor,
                            candidateNames = new[] { $"{lane.scenePrefix}_Tower_{section.side}_{index}" },
                            duplicateIndex = 0,
                        });
                    }
                }
            }
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
