using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CastleDefender.Net;
using CastleDefender.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class SnapshotBuildingVisualBridge : MonoBehaviour
    {
        struct GeneratedVisualInstance
        {
            public GameObject root;
            public TieredBuildingVisual tieredVisual;
            public BuildingLifecycleVisual lifecycleVisual;
        }

        struct CachedRendererState
        {
            public Material[] materials;
            public ShadowCastingMode shadowCastingMode;
            public bool receiveShadows;
        }

        readonly struct SharedWallLineVisualState
        {
            public readonly bool Visible;
            public readonly bool Built;
            public readonly bool Constructing;
            public readonly int CurrentTier;
            public readonly int ConstructionTargetTier;
            public readonly float ConstructionProgress01;
            public readonly bool UnderRepair;
            public readonly float Hp01;

            public SharedWallLineVisualState(
                bool visible,
                bool built,
                bool constructing,
                int currentTier,
                int constructionTargetTier,
                float constructionProgress01,
                bool underRepair,
                float hp01)
            {
                Visible = visible;
                Built = built;
                Constructing = constructing;
                CurrentTier = Mathf.Max(1, currentTier);
                ConstructionTargetTier = Mathf.Max(1, constructionTargetTier);
                ConstructionProgress01 = Mathf.Clamp01(constructionProgress01);
                UnderRepair = underRepair;
                Hp01 = Mathf.Clamp01(hp01);
            }
        }

        [Header("Catalog")]
        [SerializeField] BuildingVisualCatalog catalog;
        [SerializeField] string buildingTypeOverride;
        [SerializeField] Transform visualParent;

        [Header("Legacy Visuals")]
        [SerializeField] Renderer[] legacyRenderers = Array.Empty<Renderer>();
        [SerializeField] Transform[] supplementalVisualRoots = Array.Empty<Transform>();

        readonly Dictionary<Renderer, CachedRendererState> _generatedRendererStates = new();
        readonly List<GeneratedVisualInstance> _supplementalGeneratedVisuals = new();

        FortressPadAnchor _fortressPad;
        BarracksSiteView _barracksSiteView;
        TieredBuildingVisual _tieredVisual;
        BuildingLifecycleVisual _lifecycleVisual;
        GameObject _visualInstanceRoot;
        Renderer[] _generatedRenderers = Array.Empty<Renderer>();
        MaterialPropertyBlock _rendererPropertyBlock;
        SnapshotApplier _boundSnapshotApplier;
        bool _subscribed;
        string _lastVisualFailureSignature;
        string _resolvedVisualBuildingType;

        void Awake()
        {
            CacheComponents();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            CacheComponents();
            EnsureVisualInstance();
            SubscribeSnapshots();
            RefreshFromSnapshot();
        }

        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            UnsubscribeSnapshots();
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            if (_boundSnapshotApplier != SnapshotApplier.Instance)
            {
                SubscribeSnapshots();
                RefreshFromSnapshot();
            }
        }

        public void ConfigureForEditor(
            BuildingVisualCatalog configuredCatalog,
            string configuredBuildingTypeOverride,
            Renderer[] configuredLegacyRenderers,
            Transform[] configuredSupplementalVisualRoots = null)
        {
            catalog = configuredCatalog;
            buildingTypeOverride = configuredBuildingTypeOverride;
            visualParent = transform;
            legacyRenderers = configuredLegacyRenderers ?? Array.Empty<Renderer>();
            supplementalVisualRoots = configuredSupplementalVisualRoots ?? Array.Empty<Transform>();
        }

        public void ConfigureRuntime(Renderer[] configuredLegacyRenderers, Transform[] configuredSupplementalVisualRoots = null)
        {
            visualParent = transform;
            legacyRenderers = configuredLegacyRenderers ?? Array.Empty<Renderer>();
            this.supplementalVisualRoots = configuredSupplementalVisualRoots ?? this.supplementalVisualRoots ?? Array.Empty<Transform>();
            CacheComponents();
            EnsureVisualInstance();
            RefreshFromSnapshot();
        }

        void CacheComponents()
        {
            _fortressPad ??= GetComponent<FortressPadAnchor>();
            _barracksSiteView ??= GetComponent<BarracksSiteView>();
            if (visualParent == null)
                visualParent = transform;

            if (_fortressPad != null)
                legacyRenderers = _fortressPad.GetPrimaryRenderers();
            else if (_barracksSiteView != null)
                legacyRenderers = _barracksSiteView.GetPrimaryRenderers();
            else if (legacyRenderers == null || legacyRenderers.Length == 0)
                legacyRenderers = GetComponentsInChildren<Renderer>(true);
        }

        void SubscribeSnapshots()
        {
            var snapshotApplier = SnapshotApplier.Instance;
            if (_boundSnapshotApplier == snapshotApplier && _subscribed)
                return;

            if (_boundSnapshotApplier != null)
                _boundSnapshotApplier.OnMLSnapshotApplied -= HandleSnapshotApplied;

            _boundSnapshotApplier = snapshotApplier;
            _subscribed = false;
            if (snapshotApplier == null)
                return;

            snapshotApplier.OnMLSnapshotApplied += HandleSnapshotApplied;
            _subscribed = true;

            if (snapshotApplier.LatestML != null)
                RefreshFromSnapshot();
        }

        void UnsubscribeSnapshots()
        {
            if (_boundSnapshotApplier != null)
                _boundSnapshotApplier.OnMLSnapshotApplied -= HandleSnapshotApplied;

            _boundSnapshotApplier = null;
            _subscribed = false;
        }

        void HandleSnapshotApplied(MLSnapshot _)
        {
            RefreshFromSnapshot();
        }

        void EnsureVisualInstance(string requestedBuildingType = null)
        {
            string buildingType = !string.IsNullOrWhiteSpace(requestedBuildingType)
                ? requestedBuildingType.Trim()
                : ResolveEffectiveBuildingType();
            if (_tieredVisual != null
                && !string.IsNullOrWhiteSpace(_resolvedVisualBuildingType)
                && string.Equals(_resolvedVisualBuildingType, buildingType, StringComparison.OrdinalIgnoreCase))
                return;

            ClearGeneratedVisuals();
            if (string.IsNullOrWhiteSpace(buildingType))
            {
                ReportVisualFailure("missing_type", "Building visual bridge could not resolve a building type.");
                HideBrokenVisuals();
                return;
            }

            var activeCatalog = catalog != null ? catalog : BuildingVisualCatalog.LoadGenerated();
            if (activeCatalog == null)
            {
                ReportVisualFailure("missing_catalog", $"Building visual catalog is missing for buildingType='{buildingType}'.");
                HideBrokenVisuals();
                return;
            }

            if (!activeCatalog.TryGetByBuildingType(buildingType, out var entry) || entry == null)
            {
                ReportVisualFailure("missing_entry", $"Building visual catalog has no entry for buildingType='{buildingType}'.");
                HideBrokenVisuals();
                return;
            }

            if (entry.prefab == null)
            {
                ReportVisualFailure("missing_prefab", $"Building visual catalog entry for buildingType='{buildingType}' is missing its prefab.");
                HideBrokenVisuals();
                return;
            }

            var primaryInstance = CreateGeneratedVisualInstance(
                entry.prefab,
                visualParent != null ? visualParent : transform,
                buildingType);
            _visualInstanceRoot = primaryInstance.root;
            _tieredVisual = primaryInstance.tieredVisual;
            _lifecycleVisual = primaryInstance.lifecycleVisual;

            if (_tieredVisual == null)
            {
                ClearGeneratedVisuals();
                ReportVisualFailure("missing_tier_component", $"Generated visual prefab '{entry.prefab.name}' is missing TieredBuildingVisual.");
                HideBrokenVisuals();
                return;
            }

            var resolvedSupplementalRoots = ResolveSupplementalVisualRoots(buildingType);
            for (int i = 0; i < resolvedSupplementalRoots.Length; i++)
            {
                var supplementalRoot = resolvedSupplementalRoots[i];
                if (supplementalRoot == null)
                    continue;

                if (ReferenceEquals(supplementalRoot, transform) || ReferenceEquals(supplementalRoot, visualParent))
                    continue;

                var supplementalInstance = CreateGeneratedVisualInstance(entry.prefab, supplementalRoot, buildingType);
                if (supplementalInstance.root != null)
                    _supplementalGeneratedVisuals.Add(supplementalInstance);
            }

            _resolvedVisualBuildingType = buildingType;
            ClearVisualFailure();
        }

        void RefreshFromSnapshot()
        {
            bool built = false;
            int tier = 1;
            bool constructing = false;
            int constructionTargetTier = 1;
            float constructionProgress01 = 0f;
            bool destroyed = false;
            bool underRepair = false;
            float hp01 = 1f;
            bool hasSnapshot = SnapshotApplier.Instance?.LatestML?.lanes != null;
            string effectiveBuildingType = ResolveEffectiveBuildingType();
            bool showUnbuiltTurretShell = false;
            MLLaneSnap resolvedLane = null;

            if (!hasSnapshot && ShouldUseEditorPreviewWithoutAuthoritativeMatch())
            {
                ShowEditorPreviewVisuals();
                return;
            }

            if (_barracksSiteView != null)
            {
                var lane = ResolveLane(_barracksSiteView.laneColor, out bool laneConfigured);
                resolvedLane = lane;
                if (hasSnapshot && lane == null)
                {
                    if (!laneConfigured)
                    {
                        ShowLegacyOnly(false);
                        ClearVisualFailure();
                        return;
                    }

                    ReportVisualFailure(
                        "missing_lane",
                        $"Barracks site '{_barracksSiteView.BarracksId}' could not resolve lane '{_barracksSiteView.laneColor}'.");
                    HideBrokenVisuals();
                    return;
                }

                if (hasSnapshot && lane != null && lane.barracksSites == null)
                {
                    ClearVisualFailure();
                    return;
                }

                var site = ResolveBarracksSite(lane);
                if (hasSnapshot && site == null)
                {
                    ReportVisualFailure(
                        "missing_barracks_site",
                        $"Barracks site '{_barracksSiteView.BarracksId}' is missing from lane snapshot '{lane?.laneIndex.ToString() ?? "<none>"}'.");
                    HideBrokenVisuals();
                    return;
                }

                built = site != null && site.isBuilt;
                tier = built ? Mathf.Max(1, site.level) : 1;
                constructing = site != null && site.isConstructing;
                constructionTargetTier = site != null
                    ? Mathf.Max(1, site.constructionTargetLevel > 0 ? site.constructionTargetLevel : (built ? site.level : 1))
                    : 1;
                constructionProgress01 = site != null ? Mathf.Clamp01(site.constructionProgress01) : 0f;
                destroyed = site != null && site.isDestroyed;
                underRepair = site != null
                    && (site.isUnderRepair
                        || string.Equals(site.lifecycleState, "under_repair", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(site.buildState, "under_repair", StringComparison.OrdinalIgnoreCase));
                hp01 = site != null && site.maxHp > 0f ? Mathf.Clamp01(site.hp / site.maxHp) : 1f;
            }
            else if (_fortressPad != null)
            {
                var lane = ResolveLane(_fortressPad.AnchorLaneColor, out bool laneConfigured);
                resolvedLane = lane;
                if (hasSnapshot && lane == null)
                {
                    if (!laneConfigured)
                    {
                        ShowLegacyOnly(false);
                        ClearVisualFailure();
                        return;
                    }

                    ReportVisualFailure(
                        "missing_lane",
                        $"Fortress pad '{_fortressPad.PadId}' could not resolve lane '{_fortressPad.AnchorLaneColor}'.");
                    HideBrokenVisuals();
                    return;
                }

                if (hasSnapshot && lane != null && lane.fortressPads == null)
                {
                    ClearVisualFailure();
                    return;
                }

                var pad = ResolveFortressPad(lane);
                if (hasSnapshot && pad == null)
                {
                    ReportVisualFailure(
                        "missing_pad",
                        $"Fortress pad '{_fortressPad.PadId}' is missing from lane snapshot '{lane?.laneIndex.ToString() ?? "<none>"}'.");
                    HideBrokenVisuals();
                    return;
                }

                effectiveBuildingType = ResolveEffectiveBuildingType(lane, pad);
                if (string.Equals(effectiveBuildingType, "wall_tower", StringComparison.OrdinalIgnoreCase))
                {
                    var sharedWallLineState = ResolveSharedWallLineVisualState(lane);
                    showUnbuiltTurretShell = sharedWallLineState.Visible;
                    built = sharedWallLineState.Built;
                    tier = sharedWallLineState.CurrentTier;
                    constructing = sharedWallLineState.Constructing;
                    constructionTargetTier = sharedWallLineState.ConstructionTargetTier;
                    constructionProgress01 = sharedWallLineState.ConstructionProgress01;
                    destroyed = false;
                    underRepair = sharedWallLineState.UnderRepair;
                    hp01 = sharedWallLineState.Hp01;
                }
                else
                {
                    built = pad != null && pad.isBuilt;
                    tier = built ? Mathf.Max(1, pad.tier) : 1;
                    constructing = pad != null && pad.isConstructing;
                    constructionTargetTier = pad != null
                        ? Mathf.Max(1, pad.constructionTargetTier > 0 ? pad.constructionTargetTier : (built ? pad.tier : 1))
                        : 1;
                    constructionProgress01 = pad != null ? Mathf.Clamp01(pad.constructionProgress01) : 0f;
                    destroyed = pad != null && pad.isDestroyed;
                    underRepair = pad != null
                        && (pad.isUnderRepair
                            || string.Equals(pad.lifecycleState, "under_repair", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(pad.buildState, "under_repair", StringComparison.OrdinalIgnoreCase));
                    hp01 = pad != null && pad.maxHp > 0f ? Mathf.Clamp01(pad.hp / pad.maxHp) : 1f;
                }
            }

            EnsureVisualInstance(effectiveBuildingType);

            bool showWorldVisual = built || constructing || showUnbuiltTurretShell;
            if (_tieredVisual == null)
            {
                HideBrokenVisuals();
                return;
            }

            if (!RestoreGeneratedRendererStates())
            {
                ReportVisualFailure("missing_renderer_state", "Generated building visuals could not restore their renderer state.");
                HideBrokenVisuals();
                return;
            }

            if (!showWorldVisual)
            {
                SetGeneratedVisualsActive(false);
                HideGeneratedPresentation();
                SetLegacyRenderersVisible(false);
                SetInteractionEnabled(false);
                ClearVisualFailure();
                return;
            }

            int presentationTier = constructing
                ? Mathf.Max(1, constructionTargetTier)
                : Mathf.Max(1, tier);
            destroyed = destroyed && built;
            if (destroyed)
                constructing = false;

            bool useAuthoredBuiltVisuals = built
                && !constructing
                && !destroyed
                && ShouldUseAuthoredBuiltVisuals(effectiveBuildingType);
            bool showLegacyStructuralShell = ShouldShowLegacyStructuralShell(
                effectiveBuildingType,
                built,
                constructing,
                destroyed,
                showUnbuiltTurretShell);

            bool showConstructionStages = constructing && HasConstructionStagesFor(constructionTargetTier);
            bool showConstructionFx = constructing;
            bool showDamagedFx = built && !destroyed && (underRepair || (hp01 > 0f && hp01 <= 0.35f));
            SetGeneratedVisualsActive(true);
            ApplyGeneratedPresentation(
                presentationTier,
                useAuthoredBuiltVisuals,
                constructionProgress01,
                showConstructionFx,
                showConstructionStages,
                constructionTargetTier,
                showDamagedFx,
                destroyed);

            SetLegacyRenderersVisible(showLegacyStructuralShell);
            SetInteractionEnabled(true);
            ClearVisualFailure();
        }

        void ShowEditorPreviewVisuals()
        {
            if (_tieredVisual == null)
                EnsureVisualInstance();

            if (ShouldUseAuthoredBuiltVisuals())
            {
                SetGeneratedVisualsActive(false);
                HideGeneratedPresentation();

                SetLegacyRenderersVisible(true);
                SetInteractionEnabled(true);
                ClearVisualFailure();
                return;
            }

            if (_tieredVisual != null && RestoreGeneratedRendererStates())
            {
                SetGeneratedVisualsActive(true);
                ApplyGeneratedPreviewPresentation();
                SetLegacyRenderersVisible(false);
                SetInteractionEnabled(true);
                ClearVisualFailure();
                return;
            }

            ShowLegacyOnly(true);
            ClearVisualFailure();
        }

        void CacheGeneratedRendererStates(GameObject root)
        {
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            if (_generatedRenderers == null || _generatedRenderers.Length == 0)
            {
                _generatedRenderers = renderers;
            }
            else
            {
                var merged = new Renderer[_generatedRenderers.Length + renderers.Length];
                Array.Copy(_generatedRenderers, merged, _generatedRenderers.Length);
                Array.Copy(renderers, 0, merged, _generatedRenderers.Length, renderers.Length);
                _generatedRenderers = merged;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var materials = renderer.sharedMaterials;
                var clonedMaterials = materials != null && materials.Length > 0
                    ? (Material[])materials.Clone()
                    : Array.Empty<Material>();

                _generatedRendererStates[renderer] = new CachedRendererState
                {
                    materials = clonedMaterials,
                    shadowCastingMode = renderer.shadowCastingMode,
                    receiveShadows = renderer.receiveShadows,
                };
            }
        }

        void ResetGeneratedRendererStates()
        {
            _generatedRendererStates.Clear();
            _generatedRenderers = Array.Empty<Renderer>();
        }

        void ClearGeneratedVisuals()
        {
            HideGeneratedPresentation();

            if (_visualInstanceRoot != null)
                DestroyGeneratedRoot(_visualInstanceRoot);

            for (int i = 0; i < _supplementalGeneratedVisuals.Count; i++)
            {
                if (_supplementalGeneratedVisuals[i].root != null)
                    DestroyGeneratedRoot(_supplementalGeneratedVisuals[i].root);
            }

            _supplementalGeneratedVisuals.Clear();
            _visualInstanceRoot = null;
            _tieredVisual = null;
            _lifecycleVisual = null;
            _resolvedVisualBuildingType = null;
            ResetGeneratedRendererStates();
        }

        static void DestroyGeneratedRoot(GameObject root)
        {
            if (root == null)
                return;

            root.SetActive(false);
            if (Application.isPlaying)
                Destroy(root);
            else
                DestroyImmediate(root);
        }

        bool RestoreGeneratedRendererStates()
        {
            if (_generatedRenderers == null || _generatedRenderers.Length == 0)
                return false;

            _rendererPropertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < _generatedRenderers.Length; i++)
            {
                var renderer = _generatedRenderers[i];
                if (renderer == null)
                    continue;

                if (!_generatedRendererStates.TryGetValue(renderer, out var cachedState))
                    continue;

                renderer.sharedMaterials = cachedState.materials ?? Array.Empty<Material>();
                renderer.shadowCastingMode = cachedState.shadowCastingMode;
                renderer.receiveShadows = cachedState.receiveShadows;
                _rendererPropertyBlock.Clear();
                renderer.SetPropertyBlock(_rendererPropertyBlock);
            }

            return true;
        }

        void SetLegacyRenderersVisible(bool visible)
        {
            if (legacyRenderers == null)
                return;

            for (int i = 0; i < legacyRenderers.Length; i++)
            {
                var renderer = legacyRenderers[i];
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        void SetInteractionEnabled(bool enabled)
        {
            if (_barracksSiteView?.EnsureInteractionCollider() is Collider barracksCollider)
                barracksCollider.enabled = enabled;

            if (_fortressPad?.EnsureInteractionCollider() is Collider padCollider)
                padCollider.enabled = enabled;
        }

        void ShowLegacyOnly(bool enableInteraction)
        {
            SetGeneratedVisualsActive(false);
            HideGeneratedPresentation();

            SetLegacyRenderersVisible(true);
            SetInteractionEnabled(enableInteraction);
        }

        void ApplyTeamTint(GameObject root)
        {
            if (root == null)
                return;

            if (!TryResolveTeam(out var team))
            {
                ReportVisualFailure("missing_team", "Building visual bridge could not resolve a valid team color.");
                return;
            }

            foreach (var profile in root.GetComponentsInChildren<TeamColorMaterialProfile>(true))
            {
                if (profile != null)
                    profile.Apply(team);
            }
        }

        string ResolveBuildingType()
        {
            if (!string.IsNullOrWhiteSpace(buildingTypeOverride))
                return buildingTypeOverride.Trim();

            if (_fortressPad != null && !string.IsNullOrWhiteSpace(_fortressPad.BuildingType))
                return _fortressPad.BuildingType.Trim();

            if (_barracksSiteView != null)
                return "barracks";

            return string.Empty;
        }

        string ResolveEffectiveBuildingType(MLLaneSnap lane = null, MLFortressPad snapshotPad = null)
        {
            string buildingType = ResolveBuildingType();
            if (_fortressPad == null
                || !string.Equals(buildingType, "turret", StringComparison.OrdinalIgnoreCase))
                return buildingType;

            if (snapshotPad == null && lane != null)
                snapshotPad = ResolveFortressPad(lane);

            if (snapshotPad == null)
                return "wall_tower";

            return (snapshotPad.isBuilt || snapshotPad.isConstructing)
                ? "turret"
                : "wall_tower";
        }

        static SharedWallLineVisualState ResolveSharedWallLineVisualState(MLLaneSnap lane)
        {
            if (lane?.fortressPads == null)
                return default;

            bool visible = false;
            bool built = false;
            bool constructing = false;
            bool underRepair = false;
            bool hasHp = false;
            int highestBuiltTier = 0;
            int highestConstructionTargetTier = 0;
            float constructionProgress01 = 0f;
            float lowestHp01 = 1f;

            for (int padIndex = 0; padIndex < lane.fortressPads.Length; padIndex++)
            {
                var pad = lane.fortressPads[padIndex];
                if (pad == null
                    || !IsSharedWallLineStructuralVisualBuildingType(pad.buildingType))
                {
                    continue;
                }

                bool padBuilt = pad.isBuilt && !pad.isDestroyed;
                bool padConstructing = pad.isConstructing && !pad.isDestroyed;
                if (!padBuilt && !padConstructing)
                    continue;

                visible = true;
                if (padBuilt)
                {
                    built = true;
                    highestBuiltTier = Mathf.Max(highestBuiltTier, Mathf.Max(1, pad.tier));
                }

                if (padConstructing)
                {
                    constructing = true;
                    int padTargetTier = Mathf.Max(1, pad.constructionTargetTier > 0
                        ? pad.constructionTargetTier
                        : (padBuilt ? pad.tier : 1));
                    highestConstructionTargetTier = Mathf.Max(highestConstructionTargetTier, padTargetTier);
                    constructionProgress01 = Mathf.Max(constructionProgress01, Mathf.Clamp01(pad.constructionProgress01));
                }

                if (pad.isUnderRepair
                    || string.Equals(pad.lifecycleState, "under_repair", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pad.buildState, "under_repair", StringComparison.OrdinalIgnoreCase))
                {
                    underRepair = true;
                }

                if (pad.maxHp > 0f)
                {
                    hasHp = true;
                    lowestHp01 = Mathf.Min(lowestHp01, Mathf.Clamp01(pad.hp / pad.maxHp));
                }
            }

            if (!visible)
                return default;

            int currentTier = built ? Mathf.Max(1, highestBuiltTier) : 1;
            int targetTier = constructing
                ? Mathf.Max(currentTier, highestConstructionTargetTier)
                : currentTier;
            return new SharedWallLineVisualState(
                visible,
                built,
                constructing,
                currentTier,
                targetTier,
                constructionProgress01,
                underRepair,
                hasHp ? lowestHp01 : 1f);
        }

        static bool ShouldShowLegacyStructuralShell(
            string buildingType,
            bool built,
            bool constructing,
            bool destroyed,
            bool showUnbuiltTurretShell)
        {
            if (constructing || destroyed)
                return false;

            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wall":
                case "gate":
                    return built;
                default:
                    return false;
            }
        }

        bool ShouldUseAuthoredBuiltVisuals(string buildingType = null)
        {
            if (_fortressPad == null)
                return false;

            string resolvedBuildingType = !string.IsNullOrWhiteSpace(buildingType)
                ? buildingType.Trim()
                : !string.IsNullOrWhiteSpace(_resolvedVisualBuildingType)
                    ? _resolvedVisualBuildingType
                    : ResolveEffectiveBuildingType();
            switch ((resolvedBuildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "gate":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsSharedWallLineStructuralVisualBuildingType(string buildingType)
        {
            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wall":
                case "gate":
                    return true;
                default:
                    return false;
            }
        }

        Transform[] ResolveSupplementalVisualRoots(string buildingType = null)
        {
            var results = new List<Transform>();
            var seen = new HashSet<Transform>();

            void AppendRoot(Transform root)
            {
                if (root == null || !seen.Add(root))
                    return;

                results.Add(root);
            }

            if (supplementalVisualRoots != null)
            {
                for (int i = 0; i < supplementalVisualRoots.Length; i++)
                    AppendRoot(supplementalVisualRoots[i]);
            }

            if (!ShouldUseAuthoredBuiltVisuals(buildingType) || legacyRenderers == null || legacyRenderers.Length <= 0)
                return results.ToArray();

            Transform primaryRoot = visualParent != null ? visualParent : transform;
            Transform primaryParent = primaryRoot != null ? primaryRoot.parent : null;
            for (int i = 0; i < legacyRenderers.Length; i++)
            {
                var renderer = legacyRenderers[i];
                if (renderer == null)
                    continue;

                var candidate = renderer.transform;
                while (candidate != null && candidate.parent != null && candidate.parent != primaryParent)
                    candidate = candidate.parent;

                if (candidate == null || candidate == primaryRoot)
                    continue;

                AppendRoot(candidate);
            }

            return results.ToArray();
        }

        GeneratedVisualInstance CreateGeneratedVisualInstance(GameObject prefab, Transform parent, string buildingType)
        {
            if (prefab == null || parent == null)
                return default;

            var instance = Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var tieredVisual = instance.GetComponent<TieredBuildingVisual>();
            if (tieredVisual == null)
                tieredVisual = instance.GetComponentInChildren<TieredBuildingVisual>(true);

            var lifecycleVisual = instance.GetComponent<BuildingLifecycleVisual>();
            if (lifecycleVisual == null)
                lifecycleVisual = instance.GetComponentInChildren<BuildingLifecycleVisual>(true);
#if UNITY_EDITOR
            if (lifecycleVisual == null)
            {
                TryAttachEditorLifecycleVisualFallback(instance, buildingType);
                lifecycleVisual = instance.GetComponent<BuildingLifecycleVisual>();
                if (lifecycleVisual == null)
                    lifecycleVisual = instance.GetComponentInChildren<BuildingLifecycleVisual>(true);
            }
#endif

            ApplyTeamTint(instance);
            CacheGeneratedRendererStates(instance);

            return new GeneratedVisualInstance
            {
                root = instance,
                tieredVisual = tieredVisual,
                lifecycleVisual = lifecycleVisual,
            };
        }

        void SetGeneratedVisualsActive(bool visible)
        {
            if (_visualInstanceRoot != null && _visualInstanceRoot.activeSelf != visible)
                _visualInstanceRoot.SetActive(visible);

            for (int i = 0; i < _supplementalGeneratedVisuals.Count; i++)
            {
                var instance = _supplementalGeneratedVisuals[i];
                if (instance.root != null && instance.root.activeSelf != visible)
                    instance.root.SetActive(visible);
            }
        }

        void HideGeneratedPresentation()
        {
            _lifecycleVisual?.HideAllPresentation();

            for (int i = 0; i < _supplementalGeneratedVisuals.Count; i++)
                _supplementalGeneratedVisuals[i].lifecycleVisual?.HideAllPresentation();
        }

        bool HasConstructionStagesFor(int constructionTargetTier)
        {
            if (_lifecycleVisual != null && _lifecycleVisual.HasConstructionStagesFor(constructionTargetTier))
                return true;

            for (int i = 0; i < _supplementalGeneratedVisuals.Count; i++)
            {
                var lifecycleVisual = _supplementalGeneratedVisuals[i].lifecycleVisual;
                if (lifecycleVisual != null && lifecycleVisual.HasConstructionStagesFor(constructionTargetTier))
                    return true;
            }

            return false;
        }

        void ApplyGeneratedPresentation(
            int presentationTier,
            bool useAuthoredBuiltVisuals,
            float constructionProgress01,
            bool showConstructionFx,
            bool showConstructionStages,
            int constructionTargetTier,
            bool showDamagedFx,
            bool showDestroyedFx)
        {
            ApplyGeneratedPresentationToInstance(
                _tieredVisual,
                _lifecycleVisual,
                presentationTier,
                useAuthoredBuiltVisuals,
                constructionProgress01,
                showConstructionFx,
                showConstructionStages,
                constructionTargetTier,
                showDamagedFx,
                showDestroyedFx);

            for (int i = 0; i < _supplementalGeneratedVisuals.Count; i++)
            {
                var instance = _supplementalGeneratedVisuals[i];
                ApplyGeneratedPresentationToInstance(
                    instance.tieredVisual,
                    instance.lifecycleVisual,
                    presentationTier,
                    useAuthoredBuiltVisuals,
                    constructionProgress01,
                    showConstructionFx,
                    showConstructionStages,
                    constructionTargetTier,
                    showDamagedFx,
                    showDestroyedFx);
            }
        }

        void ApplyGeneratedPreviewPresentation()
        {
            ApplyGeneratedPreviewToInstance(_tieredVisual, _lifecycleVisual);

            for (int i = 0; i < _supplementalGeneratedVisuals.Count; i++)
            {
                var instance = _supplementalGeneratedVisuals[i];
                ApplyGeneratedPreviewToInstance(instance.tieredVisual, instance.lifecycleVisual);
            }
        }

        static void ApplyGeneratedPreviewToInstance(TieredBuildingVisual tieredVisual, BuildingLifecycleVisual lifecycleVisual)
        {
            if (tieredVisual == null)
                return;

            tieredVisual.gameObject.SetActive(true);
            tieredVisual.ApplyTier(Mathf.Max(1, tieredVisual.CurrentTier));
            lifecycleVisual?.HideAllPresentation();
        }

        static void ApplyGeneratedPresentationToInstance(
            TieredBuildingVisual tieredVisual,
            BuildingLifecycleVisual lifecycleVisual,
            int presentationTier,
            bool useAuthoredBuiltVisuals,
            float constructionProgress01,
            bool showConstructionFx,
            bool showConstructionStages,
            int constructionTargetTier,
            bool showDamagedFx,
            bool showDestroyedFx)
        {
            if (tieredVisual == null)
                return;

            tieredVisual.gameObject.SetActive(true);
            tieredVisual.ApplyTier(presentationTier);

            if (lifecycleVisual != null)
            {
                lifecycleVisual.ApplyState(
                    !showConstructionStages && !useAuthoredBuiltVisuals,
                    constructionProgress01,
                    showConstructionFx,
                    showConstructionStages,
                    constructionTargetTier,
                    showDamagedFx,
                    showDestroyedFx);
            }
            else if (useAuthoredBuiltVisuals)
            {
                tieredVisual.gameObject.SetActive(false);
            }
        }

        MLLaneSnap ResolveLane(FortressPadAnchor.LaneColor laneColor, out bool laneConfigured)
        {
            var snapshotApplier = SnapshotApplier.Instance;
            Transform laneSource = _barracksSiteView != null
                ? _barracksSiteView.transform
                : _fortressPad != null ? _fortressPad.transform : transform;

            if (FortressLaneResolver.TryResolveSnapshotLane(
                snapshotApplier,
                laneSource,
                laneColor,
                out var lane,
                out laneConfigured))
            {
                return lane;
            }

            laneConfigured = FortressLaneResolver.IsLaneConfiguredInCurrentMatch(snapshotApplier, laneSource, laneColor);
            return null;
        }

        MLFortressPad ResolveFortressPad(MLLaneSnap lane)
        {
            if (lane?.fortressPads == null || _fortressPad == null)
                return null;

            for (int i = 0; i < lane.fortressPads.Length; i++)
            {
                var pad = lane.fortressPads[i];
                if (pad != null && string.Equals(pad.padId, _fortressPad.PadId, StringComparison.OrdinalIgnoreCase))
                    return pad;
            }

            return null;
        }

        MLBarracksSite ResolveBarracksSite(MLLaneSnap lane)
        {
            if (lane?.barracksSites == null || _barracksSiteView == null)
                return null;

            for (int i = 0; i < lane.barracksSites.Length; i++)
            {
                var site = lane.barracksSites[i];
                if (site != null && string.Equals(site.barracksId, _barracksSiteView.BarracksId, StringComparison.OrdinalIgnoreCase))
                    return site;
            }

            return null;
        }

        bool TryResolveTeam(out BattleTeam team)
        {
            FortressPadAnchor.LaneColor laneColor = _barracksSiteView != null
                ? _barracksSiteView.laneColor
                : _fortressPad != null ? _fortressPad.AnchorLaneColor : FortressPadAnchor.LaneColor.Any;

            switch (laneColor)
            {
                case FortressPadAnchor.LaneColor.Red:
                    team = BattleTeam.Red;
                    return true;
                case FortressPadAnchor.LaneColor.Gold:
                    team = BattleTeam.Yellow;
                    return true;
                case FortressPadAnchor.LaneColor.Blue:
                    team = BattleTeam.Blue;
                    return true;
                case FortressPadAnchor.LaneColor.Green:
                    team = BattleTeam.Green;
                    return true;
                default:
                    team = default;
                    return false;
            }
        }

        void HideBrokenVisuals()
        {
            SetGeneratedVisualsActive(false);
            HideGeneratedPresentation();

            SetLegacyRenderersVisible(false);
            SetInteractionEnabled(true);
        }

        void ReportVisualFailure(string reason, string detail)
        {
            string signature = $"{reason}|{detail}";
            if (_lastVisualFailureSignature != signature)
            {
                _lastVisualFailureSignature = signature;
                Debug.LogError($"[SnapshotBuildingVisualBridge] {detail} {BuildFailureContext()}", this);
            }

            RuntimeFailureMarker.Mark(transform, "building_visual", detail);
        }

        void ClearVisualFailure()
        {
            _lastVisualFailureSignature = null;
            RuntimeFailureMarker.Clear(transform, "building_visual");
        }

        static bool ShouldUseEditorPreviewWithoutAuthoritativeMatch()
        {
#if UNITY_EDITOR
            if (!Application.isEditor)
                return false;

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier?.LatestML?.lanes != null)
                return false;

            if (snapshotApplier?.LatestMLMatchReady?.laneAssignments?.Length > 0)
                return false;

            if (snapshotApplier?.LatestMLMatchConfig?.battlefieldLayout != null)
                return false;

            var network = NetworkManager.Instance;
            if (network == null)
                return true;

            if (network.LastMLMatchReady?.laneAssignments?.Length > 0)
                return false;

            if (network.LastMLMatchConfig?.battlefieldLayout != null)
                return false;

            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        const string TtConstructionRoot = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/models/buildings/construction";
        const string TtExtrasRoot = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/models/extras";
        const string TtFxPrefabsRoot = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/FX/FX_prefabs";

        struct EditorConstructionSpec
        {
            public int targetTier;
            public string[] stageModelPaths;
        }

        void TryAttachEditorLifecycleVisualFallback(GameObject instance, string buildingType)
        {
            if (!Application.isEditor || instance == null || string.IsNullOrWhiteSpace(buildingType))
                return;
            if (_lifecycleVisual != null)
                return;

            var tieredVisual = _tieredVisual != null
                ? _tieredVisual
                : instance.GetComponent<TieredBuildingVisual>() ?? instance.GetComponentInChildren<TieredBuildingVisual>(true);
            if (tieredVisual == null)
                return;

            var lifecycleVisual = instance.GetComponent<BuildingLifecycleVisual>() ?? instance.AddComponent<BuildingLifecycleVisual>();
            lifecycleVisual.ConfigureForEditor(
                tieredVisual,
                BuildEditorConstructionVisualSets(instance.transform, buildingType),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TtExtrasRoot}/weapons/w_hammer.FBX"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TtFxPrefabsRoot}/FX_Building_burning_small.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TtFxPrefabsRoot}/FX_Building_burning.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TtFxPrefabsRoot}/FX_Building_Destroyed_mid.prefab"));
            _lifecycleVisual = lifecycleVisual;
        }

        static BuildingLifecycleVisual.ConstructionVisualSet[] BuildEditorConstructionVisualSets(Transform parent, string buildingType)
        {
            var specs = ResolveEditorConstructionSpecs(buildingType);
            if (parent == null || specs.Length <= 0)
                return Array.Empty<BuildingLifecycleVisual.ConstructionVisualSet>();

            var results = new List<BuildingLifecycleVisual.ConstructionVisualSet>(specs.Length);
            for (int specIndex = 0; specIndex < specs.Length; specIndex++)
            {
                var spec = specs[specIndex];
                if (spec.stageModelPaths == null || spec.stageModelPaths.Length <= 0)
                    continue;

                var groupRoot = new GameObject($"ConstructionTargetTier_{Mathf.Max(1, spec.targetTier)}");
                groupRoot.transform.SetParent(parent, false);
                groupRoot.SetActive(false);

                var stageRoots = new List<GameObject>(spec.stageModelPaths.Length);
                for (int stageIndex = 0; stageIndex < spec.stageModelPaths.Length; stageIndex++)
                {
                    string sourceModelPath = spec.stageModelPaths[stageIndex];
                    if (string.IsNullOrWhiteSpace(sourceModelPath))
                        continue;

                    var stageAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourceModelPath);
                    if (stageAsset == null)
                        continue;

                    var stageRoot = new GameObject($"ConstructionStage_{stageIndex + 1}");
                    stageRoot.transform.SetParent(groupRoot.transform, false);
                    stageRoot.SetActive(false);

                    var stageModel = Instantiate(stageAsset, stageRoot.transform, false);
                    stageModel.name = stageAsset.name;
                    stageRoots.Add(stageRoot);
                }

                if (stageRoots.Count <= 0)
                {
                    DestroyImmediate(groupRoot);
                    continue;
                }

                results.Add(new BuildingLifecycleVisual.ConstructionVisualSet
                {
                    targetTier = Mathf.Max(1, spec.targetTier),
                    stageRoots = stageRoots.ToArray(),
                });
            }

            return results.ToArray();
        }

        static EditorConstructionSpec[] ResolveEditorConstructionSpecs(string buildingType)
        {
            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "town_core":
                    return new[]
                    {
                        ConstructionStages(1, $"{TtConstructionRoot}/House_0.FBX", $"{TtConstructionRoot}/House_1.FBX"),
                        ConstructionStages(2, $"{TtConstructionRoot}/TownHall_0.FBX", $"{TtConstructionRoot}/TownHall_1.FBX"),
                        ConstructionStages(3, $"{TtConstructionRoot}/Keep_0.FBX", $"{TtConstructionRoot}/Keep_1.FBX"),
                        ConstructionStages(4, $"{TtConstructionRoot}/Castle_0.FBX", $"{TtConstructionRoot}/Castle_1.FBX"),
                    };
                case "barracks":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Barracks_0.FBX", $"{TtConstructionRoot}/Barracks_1.FBX");
                case "blacksmith":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Blacksmith_0.FBX", $"{TtConstructionRoot}/Blacksmith_1.FBX");
                case "temple":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Temple_0.FBX", $"{TtConstructionRoot}/Temple_1.FBX");
                case "wizard_tower":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/MageTower_0.FBX", $"{TtConstructionRoot}/MageTower_1.FBX");
                case "archery_tower":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Archery_0.FBX", $"{TtConstructionRoot}/Archery_1.FBX");
                case "stable":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Stables_0.FBX", $"{TtConstructionRoot}/Stables_1.FBX");
                case "workshop":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Workshop_0.FBX", $"{TtConstructionRoot}/Workshop_1.FBX");
                case "library":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Library_0.FBX", $"{TtConstructionRoot}/Library_1.FBX");
                case "market":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Market_0.FBX", $"{TtConstructionRoot}/Market_1.FBX");
                case "lumber_mill":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/LumberMill_0.FBX", $"{TtConstructionRoot}/LumberMill_1.FBX");
                case "wall":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Wall_A_wall_0.FBX", $"{TtConstructionRoot}/Wall_A_wall_1.FBX");
                case "gate":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Wall_A_gate_0.FBX", $"{TtConstructionRoot}/Wall_A_gate_1.FBX");
                case "wall_tower":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Wall_A_corner_0.FBX", $"{TtConstructionRoot}/Wall_A_corner_1.FBX");
                case "turret":
                case "tower_archer":
                    return RepeatConstructionStages(3, $"{TtConstructionRoot}/Tower_A_0.FBX", $"{TtConstructionRoot}/Tower_A_1.FBX");
                default:
                    return Array.Empty<EditorConstructionSpec>();
            }
        }

        static EditorConstructionSpec ConstructionStages(int targetTier, params string[] stageModelPaths)
        {
            return new EditorConstructionSpec
            {
                targetTier = Mathf.Max(1, targetTier),
                stageModelPaths = stageModelPaths ?? Array.Empty<string>(),
            };
        }

        static EditorConstructionSpec[] RepeatConstructionStages(int maxTier, params string[] stageModelPaths)
        {
            if (maxTier <= 0 || stageModelPaths == null || stageModelPaths.Length <= 0)
                return Array.Empty<EditorConstructionSpec>();

            var results = new EditorConstructionSpec[maxTier];
            for (int tier = 1; tier <= maxTier; tier++)
                results[tier - 1] = ConstructionStages(tier, stageModelPaths);
            return results;
        }
#endif

        string BuildFailureContext()
        {
            string buildingType = ResolveBuildingType();
            string lane = _barracksSiteView != null
                ? _barracksSiteView.laneColor.ToString()
                : _fortressPad != null ? _fortressPad.AnchorLaneColor.ToString() : "<unknown>";
            string anchorId = _barracksSiteView != null
                ? _barracksSiteView.BarracksId
                : _fortressPad != null ? _fortressPad.PadId : "<none>";
            return $"object='{name}' scene='{gameObject.scene.name}' buildingType='{buildingType}' lane='{lane}' anchorId='{anchorId}'.";
        }
    }
}
