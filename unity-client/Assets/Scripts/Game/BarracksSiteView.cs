using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BarracksSiteView : MonoBehaviour
    {
        [Header("Identity")]
        public string barracksId = "center";
        public FortressPadAnchor.LaneColor laneColor = FortressPadAnchor.LaneColor.Any;
        public Transform focusTransform;

        [Header("Interaction")]
        public Collider interactionCollider;
        public bool autoSizeInteractionCollider = true;
        public Vector3 minimumColliderSize = new(2.2f, 2.4f, 2.2f);

        [Header("Optional Visual Overrides")]
        public GameObject ghostVisualRoot;
        public GameObject builtVisualRoot;
        public Renderer[] explicitRenderers;

        static readonly List<BarracksSiteView> s_activeSites = new();
        static readonly HashSet<string> s_missingHpBarLogs = new();
        static readonly HashSet<string> s_invalidConfigLogs = new();

        Transform _overlayRoot;
        Transform _labelRoot;
        TMP_Text _healthLabel;
        Transform _hpBarRoot;
        Transform _hpBarFill;
        Image _hpBarImage;
        Vector3 _hpBarFillBaseScale = Vector3.one;
        Vector3 _hpBarFillBaseLocalPosition = Vector3.zero;
        Renderer[] _cachedRenderers;
        Renderer[] _cachedGhostRenderers;
        Renderer[] _cachedBuiltRenderers;
        readonly Dictionary<Renderer, Color> _baseColorCache = new();
        readonly Dictionary<Renderer, Color> _emissionColorCache = new();
        MaterialPropertyBlock _propertyBlock;
        SnapshotBuildingVisualBridge _visualBridge;
        bool _selected;
        bool _subscribed;
        string _lastMissingSiteLogKey;

        public string BarracksId => NormalizeBarracksId(barracksId);
        public Transform FocusTransform => focusTransform != null ? focusTransform : transform;

        void OnEnable()
        {
            if (!s_activeSites.Contains(this))
                s_activeSites.Add(this);

            ValidateIdentity();
            EnsureInteractionCollider();
            SubscribeSnapshots();
            RefreshFromSnapshot();
        }

        void LateUpdate()
        {
            if (_overlayRoot == null || !_overlayRoot.gameObject.activeSelf || _labelRoot == null)
                return;

            FaceToCamera(_labelRoot);
        }

        void OnDisable()
        {
            s_activeSites.Remove(this);
            UnsubscribeSnapshots();
            if (_overlayRoot != null)
                _overlayRoot.gameObject.SetActive(false);
        }

        void OnValidate()
        {
            ValidateIdentity();
            EnsureInteractionCollider();
        }

        void OnDestroy()
        {
            if (_overlayRoot != null)
                Destroy(_overlayRoot.gameObject);
        }

        public bool MatchesLane(string slotColor, int laneIndex)
        {
            return FortressLaneResolver.MatchesLane(transform, laneColor, slotColor, laneIndex);
        }

        public bool MatchesBarracks(string value)
        {
            return string.Equals(BarracksId, NormalizeBarracksId(value), StringComparison.OrdinalIgnoreCase);
        }

        public Renderer[] GetPrimaryRenderers()
        {
            if (_cachedRenderers == null)
                _cachedRenderers = ResolveRenderers(explicitRenderers, gameObject);
            return _cachedRenderers;
        }

        public Renderer[] GetGhostRenderers()
        {
            if (_cachedGhostRenderers == null)
                _cachedGhostRenderers = ResolveRenderers(null, ghostVisualRoot);
            return _cachedGhostRenderers;
        }

        public Renderer[] GetBuiltRenderers()
        {
            if (_cachedBuiltRenderers == null)
                _cachedBuiltRenderers = ResolveRenderers(null, builtVisualRoot);
            return _cachedBuiltRenderers;
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected)
                return;

            _selected = selected;
            RefreshFromSnapshot();
        }

        public Vector3 GetFocusWorldPosition()
        {
            if (TryGetWorldBounds(out var bounds))
                return new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);

            return FocusTransform.position;
        }

        public Collider EnsureInteractionCollider()
        {
            if (interactionCollider == null)
                interactionCollider = GetComponent<Collider>();

            if (interactionCollider == null)
            {
                Debug.LogError(
                    $"[BarracksSiteView] Missing collider on authored barracks '{GetScenePath()}'. " +
                    "Add a collider to the real scene object; no runtime substitute will be created.",
                    this);
                return null;
            }

            if (autoSizeInteractionCollider && interactionCollider is BoxCollider box)
                ApplyBoundsToBoxCollider(box);

            interactionCollider.isTrigger = true;
            return interactionCollider;
        }

        public static BarracksSiteView FindSite(string barracksId, string slotColor, int laneIndex)
        {
            string normalizedId = NormalizeBarracksId(barracksId);
            for (int i = 0; i < s_activeSites.Count; i++)
            {
                var site = s_activeSites[i];
                if (site == null || !site.isActiveAndEnabled)
                    continue;

                if (!site.MatchesLane(slotColor, laneIndex) || !site.MatchesBarracks(normalizedId))
                    continue;

                return site;
            }

            return null;
        }

        public static int CollectSites(List<BarracksSiteView> results, string slotColor = null, int laneIndex = -1)
        {
            if (results == null)
                return 0;

            results.Clear();
            bool filterByLane = laneIndex >= 0 || !string.IsNullOrWhiteSpace(slotColor);

            for (int i = 0; i < s_activeSites.Count; i++)
            {
                var site = s_activeSites[i];
                if (site == null || !site.isActiveAndEnabled)
                    continue;

                if (filterByLane && !site.MatchesLane(slotColor, laneIndex))
                    continue;

                results.Add(site);
            }

            return results.Count;
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

        void RefreshFromSnapshot()
        {
            var lane = ResolveLane();
            if (lane == null)
            {
                ApplyVisualState(null);
                HideHud();
                return;
            }

            var site = ResolveSite(lane);
            if (site == null)
            {
                LogMissingSite(lane);
                ApplyVisualState(null);
                HideHud();
                return;
            }

            _lastMissingSiteLogKey = null;
            ApplyVisualState(site);
            UpdateHud(site);
        }

        MLLaneSnap ResolveLane()
        {
            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier == null)
                return null;

            return FortressLaneResolver.TryResolveSnapshotLane(
                snapshotApplier,
                transform,
                laneColor,
                out var lane,
                out _)
                ? lane
                : null;
        }

        MLBarracksSite ResolveSite(MLLaneSnap lane)
        {
            var sites = lane?.barracksSites;
            if (sites == null)
                return null;

            for (int i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                if (site != null && MatchesBarracks(site.barracksId))
                    return site;
            }

            return null;
        }

        void UpdateHud(MLBarracksSite site)
        {
            bool showHud = site != null
                && ((site.isBuilt)
                    || (!site.isBuilt && _selected));

            if (!showHud)
            {
                HideHud();
                return;
            }

            EnsureHud();
            if (_overlayRoot == null)
                return;

            _overlayRoot.gameObject.SetActive(true);

            Vector3 focus = FocusTransform.position;
            if (TryGetWorldBounds(out var bounds))
                focus.y = bounds.max.y + 0.8f;
            else
                focus.y += 2.8f;

            _overlayRoot.position = focus;
            _overlayRoot.rotation = Quaternion.identity;

            if (_labelRoot != null)
            {
                _labelRoot.localPosition = Vector3.zero;
                FaceToCamera(_labelRoot);
            }

            if (_healthLabel != null)
            {
                _healthLabel.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                if (site.isBuilt)
                {
                    _healthLabel.text = $"HP {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}";
                    _healthLabel.color = site.hp < site.maxHp
                        ? new Color(1f, 0.80f, 0.66f, 0.98f)
                        : new Color(0.84f, 0.96f, 0.88f, 0.98f);
                }
                else
                {
                    _healthLabel.text = site.canBuild
                        ? $"Buy Building {Mathf.Max(0, site.buildCost)}g"
                        : site.lockedReason ?? "Locked";
                    _healthLabel.color = new Color(0.94f, 0.90f, 0.66f, 0.98f);
                }
            }

            if (_hpBarRoot != null)
            {
                _hpBarRoot.gameObject.SetActive(site.isBuilt);
                if (site.isBuilt)
                {
                    _hpBarRoot.localPosition = new Vector3(0f, -0.12f, 0f);
                    float hp01 = Mathf.Clamp01(site.hp / Mathf.Max(1f, site.maxHp));
                    HpBarVisuals.ApplyFill(_hpBarFill, _hpBarImage, hp01);
                    if (_hpBarFill != null)
                    {
                        _hpBarFill.localScale = new Vector3(
                            _hpBarFillBaseScale.x * hp01,
                            _hpBarFillBaseScale.y,
                            _hpBarFillBaseScale.z);
                        _hpBarFill.localPosition = new Vector3(
                            0.5f * hp01,
                            _hpBarFillBaseLocalPosition.y,
                            _hpBarFillBaseLocalPosition.z);
                    }
                }
            }
        }

        void ApplyVisualState(MLBarracksSite site)
        {
            bool built = site != null && site.isBuilt;
            if (builtVisualRoot != null)
                builtVisualRoot.SetActive(built);
            if (ghostVisualRoot != null)
                ghostVisualRoot.SetActive(!built);

            _visualBridge ??= GetComponent<SnapshotBuildingVisualBridge>();
            if (_visualBridge != null)
                return;

            ApplyRendererVisualState(GetPrimaryRenderers(), built);
            if (builtVisualRoot != null)
                ApplyRendererVisualState(GetBuiltRenderers(), built);
            if (ghostVisualRoot != null)
                ApplyRendererVisualState(GetGhostRenderers(), !built);
        }

        void ApplyRendererVisualState(Renderer[] renderers, bool activeState)
        {
            if (renderers == null || renderers.Length == 0)
                return;

            _propertyBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_propertyBlock);
                Color baseColor = GetRendererBaseColor(renderer);
                Color emissionColor = GetRendererEmissionColor(renderer);
                Color tintColor = activeState
                    ? baseColor
                    : new Color(
                        Mathf.Lerp(baseColor.r, 0.60f, 0.55f),
                        Mathf.Lerp(baseColor.g, 0.68f, 0.55f),
                        Mathf.Lerp(baseColor.b, 0.78f, 0.55f),
                        Mathf.Clamp01(baseColor.a * 0.45f));
                Color tintEmission = activeState ? emissionColor : Color.black;

                var sharedMaterial = renderer.sharedMaterial;
                if (sharedMaterial != null)
                {
                    if (sharedMaterial.HasProperty("_BaseColor"))
                        _propertyBlock.SetColor("_BaseColor", tintColor);
                    if (sharedMaterial.HasProperty("_Color"))
                        _propertyBlock.SetColor("_Color", tintColor);
                    if (sharedMaterial.HasProperty("_EmissionColor"))
                        _propertyBlock.SetColor("_EmissionColor", tintEmission);
                }

                renderer.SetPropertyBlock(_propertyBlock);
                renderer.shadowCastingMode = activeState ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        Color GetRendererBaseColor(Renderer renderer)
        {
            if (renderer == null)
                return Color.white;

            if (_baseColorCache.TryGetValue(renderer, out var cached))
                return cached;

            Color resolved = Color.white;
            var sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial != null)
            {
                if (sharedMaterial.HasProperty("_BaseColor"))
                    resolved = sharedMaterial.GetColor("_BaseColor");
                else if (sharedMaterial.HasProperty("_Color"))
                    resolved = sharedMaterial.GetColor("_Color");
            }

            _baseColorCache[renderer] = resolved;
            return resolved;
        }

        Color GetRendererEmissionColor(Renderer renderer)
        {
            if (renderer == null)
                return Color.black;

            if (_emissionColorCache.TryGetValue(renderer, out var cached))
                return cached;

            Color resolved = Color.black;
            var sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial != null && sharedMaterial.HasProperty("_EmissionColor"))
                resolved = sharedMaterial.GetColor("_EmissionColor");

            _emissionColorCache[renderer] = resolved;
            return resolved;
        }

        void EnsureHud()
        {
            if (_overlayRoot != null)
                return;

            _overlayRoot = new GameObject($"BarracksHud_{name}").transform;
            _overlayRoot.SetParent(transform.parent, false);

            _labelRoot = new GameObject("Hud").transform;
            _labelRoot.SetParent(_overlayRoot, false);

            _healthLabel = CreateWorldLabel("Health", _labelRoot, 1.55f);
            EnsureHpBar();
        }

        void EnsureHpBar()
        {
            if (_hpBarRoot != null)
                return;

            var hpBarPrefab = ResolveHpBarPrefab();
            if (hpBarPrefab == null || _labelRoot == null)
                return;

            var bar = Instantiate(hpBarPrefab, _labelRoot);
            bar.name = "BarracksHpBar";
            _hpBarRoot = bar.transform;

            HpBarVisuals.EnsureStyled(_hpBarRoot);
            _hpBarFill = FindChildRecursive(_hpBarRoot, "Fill");
            _hpBarImage = _hpBarFill != null
                ? _hpBarFill.GetComponent<Image>()
                : _hpBarRoot.GetComponentInChildren<Image>(true);
            _hpBarFillBaseScale = _hpBarFill != null ? _hpBarFill.localScale : Vector3.one;
            _hpBarFillBaseLocalPosition = _hpBarFill != null ? _hpBarFill.localPosition : Vector3.zero;
        }

        void HideHud()
        {
            if (_overlayRoot != null)
                _overlayRoot.gameObject.SetActive(false);
        }

        public Bounds GetWorldBounds()
        {
            if (TryGetWorldBounds(out var bounds))
                return bounds;

            Vector3 focus = FocusTransform != null ? FocusTransform.position : transform.position;
            return new Bounds(focus, minimumColliderSize);
        }

        bool TryGetWorldBounds(out Bounds bounds)
        {
            _cachedRenderers ??= GetPrimaryRenderers();
            if (_cachedRenderers == null || _cachedRenderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bool hasBounds = false;
            Bounds combined = default;
            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                var renderer = _cachedRenderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            bounds = combined;
            return hasBounds;
        }

        static Renderer[] ResolveRenderers(Renderer[] explicitRenderers, GameObject renderRoot)
        {
            if (explicitRenderers != null && explicitRenderers.Length > 0)
            {
                var filtered = new List<Renderer>(explicitRenderers.Length);
                for (int i = 0; i < explicitRenderers.Length; i++)
                {
                    if (explicitRenderers[i] != null)
                        filtered.Add(explicitRenderers[i]);
                }

                if (filtered.Count > 0)
                    return filtered.ToArray();
            }

            if (renderRoot == null)
                return Array.Empty<Renderer>();

            return renderRoot.GetComponentsInChildren<Renderer>(true);
        }

        void ApplyBoundsToBoxCollider(BoxCollider box)
        {
            if (!TryGetWorldBounds(out var bounds))
            {
                Debug.LogError(
                    $"[BarracksSiteView] Could not size collider for '{GetScenePath()}' because no renderers were found.",
                    this);
                return;
            }

            Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
            Vector3 lossyScale = transform.lossyScale;
            Vector3 safeScale = new(
                Mathf.Max(0.001f, Mathf.Abs(lossyScale.x)),
                Mathf.Max(0.001f, Mathf.Abs(lossyScale.y)),
                Mathf.Max(0.001f, Mathf.Abs(lossyScale.z)));

            box.center = localCenter;
            box.size = new Vector3(
                Mathf.Max(minimumColliderSize.x, bounds.size.x / safeScale.x),
                Mathf.Max(minimumColliderSize.y, bounds.size.y / safeScale.y),
                Mathf.Max(minimumColliderSize.z, bounds.size.z / safeScale.z));
        }

        void LogMissingSite(MLLaneSnap lane)
        {
            if (!_selected)
                return;

            string key = $"{FortressLaneResolver.ResolveLaneKey(transform, laneColor)}::{BarracksId}";
            if (_lastMissingSiteLogKey == key)
                return;

            _lastMissingSiteLogKey = key;
            Debug.LogError(
                $"[BarracksSiteView] Missing barracks snapshot data for site '{BarracksId}' " +
                $"on lane '{lane?.slotColor ?? FortressLaneResolver.ResolveLaneKey(transform, laneColor)}' at '{GetScenePath()}'.",
                this);
        }

        static TMP_Text CreateWorldLabel(string name, Transform parent, float fontSize)
        {
            var go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            if (go.GetComponent<CastleDefender.FX.BillboardY>() == null)
                go.AddComponent<CastleDefender.FX.BillboardY>();

            var tmp = go.GetComponent<TextMeshPro>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.outlineWidth = 0.15f;
            tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            return tmp;
        }

        static void FaceToCamera(Transform target)
        {
            if (target == null)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 direction = target.position - cam.transform.position;
            if (direction.sqrMagnitude > 0.001f)
                target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        static GameObject ResolveHpBarPrefab()
        {
            var hpBarPrefab = GameplayPresentationRoot.ResolveHpBarPrefab();
            if (hpBarPrefab != null)
                return hpBarPrefab;

            if (s_missingHpBarLogs.Add("GameplayPresentationRoot.HpBarPrefab"))
            {
                Debug.LogError(
                    "[BarracksSiteView] Missing GameplayPresentationRoot.HpBarPrefab. " +
                    "Barracks HP bars will not render until the real scene reference is assigned.");
            }

            return null;
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return null;
            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        static string NormalizeBarracksId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToLowerInvariant() switch
            {
                "center" => "center",
                "left" => "left",
                "right" => "right",
                _ => value.Trim().ToLowerInvariant(),
            };
        }

        string GetScenePath()
        {
            var current = transform;
            var path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = $"{current.name}/{path}";
            }

            return path;
        }

        void ValidateIdentity()
        {
            string scenePath = GetScenePath();
            if (string.IsNullOrWhiteSpace(BarracksId))
            {
                string key = $"barracksId::{scenePath}";
                if (s_invalidConfigLogs.Add(key))
                {
                    Debug.LogError(
                        $"[BarracksSiteView] Missing barracksId on authored barracks '{scenePath}'. " +
                        "Assign left, center, or right; no fallback id will be inferred.",
                        this);
                }
            }

            if (laneColor == FortressPadAnchor.LaneColor.Any)
            {
                string key = $"laneColor::{scenePath}";
                if (s_invalidConfigLogs.Add(key))
                {
                    Debug.LogError(
                        $"[BarracksSiteView] Missing explicit laneColor on authored barracks '{scenePath}'. " +
                        "Catch-all matching is disabled.",
                        this);
                }
            }
        }
    }
}
