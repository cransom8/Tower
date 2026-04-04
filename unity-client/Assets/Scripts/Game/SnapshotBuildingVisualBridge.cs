using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class SnapshotBuildingVisualBridge : MonoBehaviour
    {
        struct CachedRendererState
        {
            public Material[] materials;
            public ShadowCastingMode shadowCastingMode;
            public bool receiveShadows;
        }

        [Header("Catalog")]
        [SerializeField] BuildingVisualCatalog catalog;
        [SerializeField] string buildingTypeOverride;
        [SerializeField] Transform visualParent;

        [Header("Legacy Visuals")]
        [SerializeField] Renderer[] legacyRenderers = Array.Empty<Renderer>();

        readonly Dictionary<Renderer, CachedRendererState> _generatedRendererStates = new();

        FortressPadAnchor _fortressPad;
        BarracksSiteView _barracksSiteView;
        TieredBuildingVisual _tieredVisual;
        BuildingLifecycleVisual _lifecycleVisual;
        Renderer[] _generatedRenderers = Array.Empty<Renderer>();
        MaterialPropertyBlock _rendererPropertyBlock;
        SnapshotApplier _boundSnapshotApplier;
        bool _subscribed;
        string _lastVisualFailureSignature;

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
            Renderer[] configuredLegacyRenderers)
        {
            catalog = configuredCatalog;
            buildingTypeOverride = configuredBuildingTypeOverride;
            visualParent = transform;
            legacyRenderers = configuredLegacyRenderers ?? Array.Empty<Renderer>();
        }

        public void ConfigureRuntime(Renderer[] configuredLegacyRenderers)
        {
            visualParent = transform;
            legacyRenderers = configuredLegacyRenderers ?? Array.Empty<Renderer>();
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
            if (legacyRenderers == null || legacyRenderers.Length == 0)
                legacyRenderers = GetComponents<Renderer>();
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

        void EnsureVisualInstance()
        {
            if (_tieredVisual != null)
                return;

            string buildingType = ResolveBuildingType();
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

            var instance = Instantiate(entry.prefab, visualParent != null ? visualParent : transform);
            instance.name = entry.prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            _tieredVisual = instance.GetComponent<TieredBuildingVisual>();

            if (_tieredVisual == null)
                _tieredVisual = instance.GetComponentInChildren<TieredBuildingVisual>(true);
            _lifecycleVisual = instance.GetComponent<BuildingLifecycleVisual>();
            if (_lifecycleVisual == null)
                _lifecycleVisual = instance.GetComponentInChildren<BuildingLifecycleVisual>(true);

            if (_tieredVisual == null)
            {
                Destroy(instance);
                ReportVisualFailure("missing_tier_component", $"Generated visual prefab '{entry.prefab.name}' is missing TieredBuildingVisual.");
                HideBrokenVisuals();
                return;
            }

            ApplyTeamTint(instance);
            CacheGeneratedRendererStates(instance);
            ClearVisualFailure();
        }

        void RefreshFromSnapshot()
        {
            bool built = false;
            int tier = 1;
            bool constructing = false;
            float constructionProgress01 = 0f;
            bool destroyed = false;
            float hp01 = 1f;
            bool hasSnapshot = SnapshotApplier.Instance?.LatestML?.lanes != null;

            if (_barracksSiteView != null)
            {
                var lane = ResolveLane(_barracksSiteView.laneColor, out bool laneConfigured);
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
                constructionProgress01 = site != null ? Mathf.Clamp01(site.constructionProgress01) : 0f;
                destroyed = site != null && site.isDestroyed;
                hp01 = site != null && site.maxHp > 0f ? Mathf.Clamp01(site.hp / site.maxHp) : 1f;
            }
            else if (_fortressPad != null)
            {
                var lane = ResolveLane(_fortressPad.AnchorLaneColor, out bool laneConfigured);
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

                built = pad != null && pad.isBuilt;
                tier = built ? Mathf.Max(1, pad.tier) : 1;
                constructing = pad != null && pad.isConstructing;
                constructionProgress01 = pad != null ? Mathf.Clamp01(pad.constructionProgress01) : 0f;
                destroyed = pad != null && pad.isDestroyed;
                hp01 = pad != null && pad.maxHp > 0f ? Mathf.Clamp01(pad.hp / pad.maxHp) : 1f;
            }

            if (_tieredVisual == null)
                EnsureVisualInstance();

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

            bool showWorldVisual = built || constructing;
            if (!showWorldVisual)
            {
                _tieredVisual.gameObject.SetActive(false);
                _lifecycleVisual?.HideAllPresentation();
                SetLegacyRenderersVisible(false);
                SetInteractionEnabled(false);
                ClearVisualFailure();
                return;
            }

            _tieredVisual.gameObject.SetActive(true);
            _tieredVisual.ApplyTier(tier);
            destroyed = destroyed && built;
            if (destroyed)
                constructing = false;

            if (_lifecycleVisual != null)
            {
                bool showConstructionStages = constructing && !built;
                bool showConstructionFx = constructing;
                bool showDamagedFx = built && !destroyed && hp01 > 0f && hp01 <= 0.35f;
                _lifecycleVisual.ApplyState(
                    !showConstructionStages,
                    constructionProgress01,
                    showConstructionFx,
                    showConstructionStages,
                    showDamagedFx,
                    destroyed);
            }

            SetLegacyRenderersVisible(false);
            SetInteractionEnabled(true);
            ClearVisualFailure();
        }

        void CacheGeneratedRendererStates(GameObject root)
        {
            if (root == null)
                return;

            _generatedRenderers = root.GetComponentsInChildren<Renderer>(true);
            _generatedRendererStates.Clear();

            for (int i = 0; i < _generatedRenderers.Length; i++)
            {
                var renderer = _generatedRenderers[i];
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
            if (_tieredVisual != null)
                _tieredVisual.gameObject.SetActive(false);
            _lifecycleVisual?.HideAllPresentation();

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
            if (_tieredVisual != null)
                _tieredVisual.gameObject.SetActive(false);
            _lifecycleVisual?.HideAllPresentation();

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
