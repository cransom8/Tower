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
        const string LockedBuildingHologramResourcePath = "LockedBuildingHologram";

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

        static Material s_lockedBuildingHologramMaterial;
        static bool s_attemptedLockedBuildingMaterialLoad;

        readonly Dictionary<Renderer, CachedRendererState> _generatedRendererStates = new();

        FortressPadAnchor _fortressPad;
        BarracksSiteView _barracksSiteView;
        TieredBuildingVisual _tieredVisual;
        Renderer[] _generatedRenderers = Array.Empty<Renderer>();
        MaterialPropertyBlock _hologramPropertyBlock;
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
            if (_subscribed)
                return;

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier == null)
                return;

            snapshotApplier.OnMLSnapshotApplied += HandleSnapshotApplied;
            _subscribed = true;
        }

        void UnsubscribeSnapshots()
        {
            if (!_subscribed)
                return;

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null)
                snapshotApplier.OnMLSnapshotApplied -= HandleSnapshotApplied;
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
            }

            if (_tieredVisual == null)
                EnsureVisualInstance();

            if (_tieredVisual == null)
            {
                HideBrokenVisuals();
                return;
            }

            _tieredVisual.gameObject.SetActive(true);

            if (!RestoreGeneratedRendererStates())
            {
                ReportVisualFailure("missing_renderer_state", "Generated building visuals could not restore their renderer state.");
                HideBrokenVisuals();
                return;
            }

            _tieredVisual.ApplyTier(tier);

            if (!built)
            {
                if (!ApplyLockedHologram())
                {
                    HideBrokenVisuals();
                    return;
                }
            }

            SetLegacyRenderersVisible(false);
            SetInteractionEnabled(true);
            ClearVisualFailure();
        }

        bool ApplyLockedHologram()
        {
            Material lockedMaterial = ResolveLockedBuildingHologramMaterial();
            if (lockedMaterial == null)
            {
                ReportVisualFailure(
                    "missing_locked_material",
                    $"Locked building hologram material '{LockedBuildingHologramResourcePath}' could not be loaded or its shader is unsupported.");
                return false;
            }

            if (_generatedRenderers == null || _generatedRenderers.Length == 0)
            {
                ReportVisualFailure("missing_renderers", "Generated building visual has no renderers available for locked hologram rendering.");
                return false;
            }

            ResolveLockedPalette(out Color baseColor, out Color edgeColor, out Color scanColor);
            _hologramPropertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < _generatedRenderers.Length; i++)
            {
                var renderer = _generatedRenderers[i];
                if (renderer == null)
                    continue;

                if (!_generatedRendererStates.TryGetValue(renderer, out var cachedState)
                    || cachedState.materials == null
                    || cachedState.materials.Length == 0)
                {
                    continue;
                }

                var replacement = new Material[cachedState.materials.Length];
                for (int materialIndex = 0; materialIndex < replacement.Length; materialIndex++)
                    replacement[materialIndex] = lockedMaterial;

                renderer.sharedMaterials = replacement;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                renderer.GetPropertyBlock(_hologramPropertyBlock);
                _hologramPropertyBlock.Clear();
                _hologramPropertyBlock.SetColor("_BaseColor", baseColor);
                _hologramPropertyBlock.SetColor("_EdgeColor", edgeColor);
                _hologramPropertyBlock.SetColor("_ScanColor", scanColor);
                renderer.SetPropertyBlock(_hologramPropertyBlock);
            }

            return true;
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

            _hologramPropertyBlock ??= new MaterialPropertyBlock();

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
                _hologramPropertyBlock.Clear();
                renderer.SetPropertyBlock(_hologramPropertyBlock);
            }

            return true;
        }

        static Material ResolveLockedBuildingHologramMaterial()
        {
            if (!s_attemptedLockedBuildingMaterialLoad)
            {
                s_attemptedLockedBuildingMaterialLoad = true;
                s_lockedBuildingHologramMaterial = Resources.Load<Material>(LockedBuildingHologramResourcePath);
            }

            if (s_lockedBuildingHologramMaterial == null)
                return null;

            return s_lockedBuildingHologramMaterial.shader != null && s_lockedBuildingHologramMaterial.shader.isSupported
                ? s_lockedBuildingHologramMaterial
                : null;
        }

        void ResolveLockedPalette(out Color baseColor, out Color edgeColor, out Color scanColor)
        {
            BattleTeam team = BattleTeam.Blue;
            if (!TryResolveTeam(out team))
                team = BattleTeam.Blue;

            switch (team)
            {
                case BattleTeam.Red:
                    baseColor = new Color(1f, 0.30f, 0.40f, 0.16f);
                    edgeColor = new Color(1f, 0.76f, 0.82f, 0.90f);
                    break;
                case BattleTeam.Yellow:
                    baseColor = new Color(1f, 0.80f, 0.18f, 0.16f);
                    edgeColor = new Color(1f, 0.95f, 0.72f, 0.90f);
                    break;
                case BattleTeam.Green:
                    baseColor = new Color(0.25f, 1f, 0.68f, 0.16f);
                    edgeColor = new Color(0.78f, 1f, 0.90f, 0.90f);
                    break;
                case BattleTeam.Blue:
                default:
                    baseColor = new Color(0.16f, 0.78f, 1f, 0.16f);
                    edgeColor = new Color(0.78f, 0.96f, 1f, 0.92f);
                    break;
            }

            scanColor = Color.Lerp(baseColor, Color.white, 0.55f);
            scanColor.a = 0.65f;
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
