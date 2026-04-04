using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FortressPadAnchor))]
    public sealed class FortressPadHealthView : MonoBehaviour
    {
        static readonly Color HealthyLabelColor = new(0.84f, 0.96f, 0.88f, 0.98f);
        static readonly Color WoundedLabelColor = new(1f, 0.80f, 0.66f, 0.98f);
        static readonly Color CriticalLabelColor = new(1f, 0.58f, 0.52f, 0.98f);
        static readonly Color ConstructionLabelColor = new(1f, 0.90f, 0.48f, 0.98f);
        static readonly System.Collections.Generic.HashSet<string> s_missingHpBarLogs = new();

        FortressPadAnchor _anchor;
        Transform _overlayRoot;
        Transform _labelRoot;
        TMP_Text _statusLabel;
        TMP_Text _healthLabel;
        Transform _hpBarRoot;
        Transform _hpBarFill;
        Image _hpBarImage;
        Vector3 _hpBarFillBaseScale = Vector3.one;
        Vector3 _hpBarFillBaseLocalPosition = Vector3.zero;
        bool _subscribed;

        void Awake()
        {
            _anchor ??= GetComponent<FortressPadAnchor>();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            _anchor ??= GetComponent<FortressPadAnchor>();
            UserPreferencesManager.PreferencesChanged += HandlePreferencesChanged;
            SubscribeSnapshots();
            RefreshFromSnapshot();
        }

        void LateUpdate()
        {
            if (_overlayRoot == null || !_overlayRoot.gameObject.activeSelf || _labelRoot == null)
                return;

            HpBarVisuals.ConfigureBuildingHud(_labelRoot);
            FaceToCamera(_labelRoot);
        }

        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            UserPreferencesManager.PreferencesChanged -= HandlePreferencesChanged;
            UnsubscribeSnapshots();
            HideHud();
        }

        void OnDestroy()
        {
            UserPreferencesManager.PreferencesChanged -= HandlePreferencesChanged;
            if (_overlayRoot != null)
                Destroy(_overlayRoot.gameObject);
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
            if (!UserPreferencesManager.ShowHealthBars)
            {
                HideHud();
                return;
            }

            if (_anchor == null)
            {
                HideHud();
                return;
            }

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier == null
                || !FortressLaneResolver.TryResolveSnapshotLane(
                    snapshotApplier,
                    transform,
                    _anchor.AnchorLaneColor,
                    out var lane,
                    out _)
                || lane == null)
            {
                HideHud();
                return;
            }

            if (TryResolveSharedDefenseGroupRoot(_anchor, out var groupRoot))
            {
                if (!IsSharedDefenseGroupLeader(groupRoot, _anchor))
                {
                    HideHud();
                    return;
                }

                if (!TryBuildSharedDefenseGroupPad(snapshotApplier, lane, groupRoot, out var groupPad, out var groupBounds))
                {
                    HideHud();
                    return;
                }

                EnsureHud();
                if (_overlayRoot == null)
                    return;

                _overlayRoot.gameObject.SetActive(true);
                UpdateHud(groupPad, groupBounds, groupBounds.center);
                return;
            }

            var pad = snapshotApplier.GetFortressPad(lane.laneIndex, _anchor.PadId);
            if (pad == null || (!pad.isBuilt && !pad.isConstructing))
            {
                HideHud();
                return;
            }

            EnsureHud();
            if (_overlayRoot == null)
                return;

            _overlayRoot.gameObject.SetActive(true);
            UpdateHud(
                pad,
                _anchor.GetWorldBounds(),
                _anchor.LabelTransform != null
                    ? _anchor.LabelTransform.position
                    : _anchor.FocusTransform.position);
        }

        void HandlePreferencesChanged(UserPreferencesData _)
        {
            RefreshFromSnapshot();
        }

        void UpdateHud(MLFortressPad pad, Bounds bounds, Vector3 focus)
        {
            bool constructing = pad != null && pad.isConstructing;
            float verticalOffset = Mathf.Clamp(bounds.size.y * 0.14f, 0.55f, 1.15f);
            focus.y = bounds.max.y + verticalOffset;
            _overlayRoot.position = focus;
            _overlayRoot.rotation = Quaternion.identity;

            if (_labelRoot != null)
            {
                _labelRoot.localPosition = Vector3.zero;
                HpBarVisuals.ConfigureBuildingHud(_labelRoot);
                FaceToCamera(_labelRoot);
            }

            float hp01 = pad.maxHp > 0f
                ? Mathf.Clamp01(pad.hp / pad.maxHp)
                : 1f;

            if (_statusLabel != null)
            {
                if (constructing)
                {
                    _statusLabel.gameObject.SetActive(true);
                    _statusLabel.transform.localPosition = new Vector3(0f, 0.42f, 0f);
                    _statusLabel.text = $"{ResolveConstructionVerb(pad.constructionKind)} {ResolveSecondsRemaining(pad.constructionTimerTicksRemaining)}s";
                    _statusLabel.color = ConstructionLabelColor;
                }
                else if (pad.isDestroyed)
                {
                    _statusLabel.gameObject.SetActive(true);
                    _statusLabel.transform.localPosition = new Vector3(0f, 0.42f, 0f);
                    _statusLabel.text = "Destroyed";
                    _statusLabel.color = CriticalLabelColor;
                }
                else
                {
                    _statusLabel.gameObject.SetActive(false);
                }
            }

            if (_healthLabel != null)
            {
                if (constructing)
                {
                    _healthLabel.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                    _healthLabel.text = string.IsNullOrWhiteSpace(pad.constructionTargetTierName)
                        ? pad.buildingName
                        : pad.constructionTargetTierName;
                    _healthLabel.color = HealthyLabelColor;
                }
                else
                {
                    int displayHp = Mathf.Max(0, Mathf.RoundToInt(pad.hp));
                    int displayMax = Mathf.Max(displayHp, Mathf.RoundToInt(pad.maxHp));
                    _healthLabel.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                    _healthLabel.text = $"HP {displayHp}/{displayMax}";
                    _healthLabel.color = ResolveHealthLabelColor(hp01);
                }
            }

            if (_hpBarRoot != null)
            {
                _hpBarRoot.gameObject.SetActive(!constructing);
                if (!constructing)
                {
                    _hpBarRoot.localPosition = new Vector3(0f, -0.10f, 0f);
                    _hpBarRoot.localScale = Vector3.one;
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

        void EnsureHud()
        {
            if (_overlayRoot != null)
                return;

            _overlayRoot = new GameObject($"FortressPadHud_{name}").transform;
            _overlayRoot.SetParent(transform.parent, false);

            _labelRoot = new GameObject("Hud").transform;
            _labelRoot.SetParent(_overlayRoot, false);
            HpBarVisuals.ConfigureBuildingHud(_labelRoot);

            _statusLabel = CreateWorldLabel("Status", _labelRoot, 1.15f);
            _healthLabel = CreateWorldLabel("Health", _labelRoot, 1.35f);
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
            bar.name = "FortressPadHpBar";
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
                    "[FortressPadHealthView] Missing GameplayPresentationRoot.HpBarPrefab. " +
                    "Fortress pad HP bars will not render until the real scene reference is assigned.");
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

        static Color ResolveHealthLabelColor(float hp01)
        {
            if (hp01 >= 0.7f)
                return HealthyLabelColor;
            if (hp01 >= 0.35f)
                return WoundedLabelColor;
            return CriticalLabelColor;
        }

        static int ResolveSecondsRemaining(int ticksRemaining)
        {
            float tickHz = SnapshotApplier.Instance != null
                ? Mathf.Max(1f, SnapshotApplier.Instance.GetTickHz())
                : 20f;
            return Mathf.Max(0, Mathf.CeilToInt(ticksRemaining / tickHz));
        }

        static string ResolveConstructionVerb(string constructionKind)
        {
            return string.Equals(constructionKind, "upgrade", System.StringComparison.OrdinalIgnoreCase)
                ? "Upgrading"
                : "Building";
        }

        static bool TryResolveSharedDefenseGroupRoot(FortressPadAnchor anchor, out Transform groupRoot)
        {
            groupRoot = null;
            if (anchor == null || !IsSharedDefenseBuildingType(anchor.BuildingType))
                return false;

            var current = anchor.transform.parent;
            while (current != null)
            {
                if (CountSharedDefenseAnchors(current) > 1)
                {
                    groupRoot = current;
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        static bool IsSharedDefenseGroupLeader(Transform groupRoot, FortressPadAnchor candidate)
        {
            if (groupRoot == null || candidate == null)
                return false;

            var anchors = groupRoot.GetComponentsInChildren<FortressPadAnchor>(true);
            Array.Sort(anchors, CompareAnchorsForSharedDefenseHud);

            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor == null || !IsSharedDefenseBuildingType(anchor.BuildingType))
                    continue;

                return ReferenceEquals(anchor, candidate);
            }

            return false;
        }

        static int CompareAnchorsForSharedDefenseHud(FortressPadAnchor left, FortressPadAnchor right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int nameComparison = string.Compare(left.name, right.name, StringComparison.Ordinal);
            if (nameComparison != 0)
                return nameComparison;

            int padComparison = string.Compare(left.PadId, right.PadId, StringComparison.Ordinal);
            if (padComparison != 0)
                return padComparison;

            return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
        }

        static int CountSharedDefenseAnchors(Transform root)
        {
            if (root == null)
                return 0;

            int count = 0;
            var anchors = root.GetComponentsInChildren<FortressPadAnchor>(true);
            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor != null && IsSharedDefenseBuildingType(anchor.BuildingType))
                    count += 1;
            }

            return count;
        }

        static bool TryBuildSharedDefenseGroupPad(
            SnapshotApplier snapshotApplier,
            MLLaneSnap lane,
            Transform groupRoot,
            out MLFortressPad groupPad,
            out Bounds groupBounds)
        {
            groupPad = null;
            groupBounds = default;
            if (snapshotApplier == null || lane == null || groupRoot == null)
                return false;

            var anchors = groupRoot.GetComponentsInChildren<FortressPadAnchor>(true);
            var visiblePads = new List<MLFortressPad>(anchors.Length);
            bool hasBounds = false;

            for (int i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor == null || !IsSharedDefenseBuildingType(anchor.BuildingType))
                    continue;

                var pad = snapshotApplier.GetFortressPad(lane.laneIndex, anchor.PadId);
                if (pad == null || (!pad.isBuilt && !pad.isConstructing))
                    continue;

                visiblePads.Add(pad);
                var bounds = anchor.GetWorldBounds();
                if (!hasBounds)
                {
                    groupBounds = bounds;
                    hasBounds = true;
                }
                else
                {
                    groupBounds.Encapsulate(bounds);
                }
            }

            if (visiblePads.Count <= 0 || !hasBounds)
                return false;

            var representative = visiblePads[0];
            float totalHp = 0f;
            float totalMaxHp = 0f;
            bool anyConstructing = false;
            bool anyDestroyed = false;
            bool anyUnderRepair = false;
            int highestTier = 1;
            int maxTimerRemaining = 0;
            int maxTimerTotal = 0;
            float maxConstructionProgress01 = 0f;
            string constructionKind = null;
            string constructionTargetTierName = null;
            string groupDisplayName = ResolveSharedDefenseGroupDisplayName(groupRoot, representative);

            for (int i = 0; i < visiblePads.Count; i++)
            {
                var pad = visiblePads[i];
                if (pad == null)
                    continue;

                totalHp += Mathf.Max(0f, pad.hp);
                totalMaxHp += Mathf.Max(0f, pad.maxHp);
                anyConstructing |= pad.isConstructing;
                anyDestroyed |= pad.isDestroyed;
                anyUnderRepair |= pad.isUnderRepair
                    || string.Equals(pad.lifecycleState, "under_repair", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pad.buildState, "under_repair", StringComparison.OrdinalIgnoreCase);
                highestTier = Mathf.Max(highestTier, Mathf.Max(1, pad.tier));
                maxTimerRemaining = Mathf.Max(maxTimerRemaining, pad.constructionTimerTicksRemaining);
                maxTimerTotal = Mathf.Max(maxTimerTotal, pad.constructionTimerTotalTicks);
                maxConstructionProgress01 = Mathf.Max(maxConstructionProgress01, pad.constructionProgress01);

                if (string.IsNullOrWhiteSpace(constructionKind) && !string.IsNullOrWhiteSpace(pad.constructionKind))
                    constructionKind = pad.constructionKind;
                if (string.IsNullOrWhiteSpace(constructionTargetTierName) && !string.IsNullOrWhiteSpace(pad.constructionTargetTierName))
                    constructionTargetTierName = pad.constructionTargetTierName;
            }

            groupPad = new MLFortressPad
            {
                padId = representative.padId,
                buildingType = representative.buildingType,
                buildingName = groupDisplayName,
                displayName = groupDisplayName,
                buildState = anyConstructing ? representative.buildState : (anyUnderRepair ? "under_repair" : "built"),
                lifecycleState = anyUnderRepair ? "under_repair" : representative.lifecycleState,
                tier = highestTier,
                maxTier = Mathf.Max(highestTier, representative.maxTier),
                isBuilt = true,
                isConstructing = anyConstructing,
                constructionKind = constructionKind,
                constructionTargetTier = Mathf.Max(1, representative.constructionTargetTier),
                constructionTargetTierName = !string.IsNullOrWhiteSpace(constructionTargetTierName)
                    ? constructionTargetTierName
                    : groupDisplayName,
                constructionTimerTicksRemaining = maxTimerRemaining,
                constructionTimerTotalTicks = maxTimerTotal,
                constructionProgress01 = Mathf.Clamp01(maxConstructionProgress01),
                isDestroyed = anyDestroyed && totalHp <= 0f,
                isUnderRepair = anyUnderRepair,
                hp = totalHp,
                maxHp = Mathf.Max(totalHp, totalMaxHp),
            };
            return true;
        }

        static string ResolveSharedDefenseGroupDisplayName(Transform groupRoot, MLFortressPad representative)
        {
            string rawName = groupRoot != null ? groupRoot.name : null;
            if (string.IsNullOrWhiteSpace(rawName))
                return representative?.displayName ?? representative?.buildingName ?? "Walls";

            string normalized = rawName.Replace('_', ' ').Trim();
            string[] lanePrefixes = { "Red ", "Blue ", "Green ", "Yellow ", "Gold " };
            for (int i = 0; i < lanePrefixes.Length; i++)
            {
                if (normalized.StartsWith(lanePrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(lanePrefixes[i].Length).Trim();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(normalized))
                return representative?.displayName ?? representative?.buildingName ?? "Walls";

            var words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                words[i] = word.Length <= 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
            }

            return string.Join(" ", words);
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
    }
}
