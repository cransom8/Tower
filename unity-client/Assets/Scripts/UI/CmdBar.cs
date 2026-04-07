using System;
using System.Collections;
using System.Collections.Generic;
using CastleDefender.Game;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    public class CmdBar : MonoBehaviour
    {
        public Button[] UnitButtons;
        public Button[] AutoToggleButtons;
        public TMP_Text[] AutoToggleTxts;
        public TMP_Text[] QueueCountLabels;
        public Image QueueDrainBar;
        public Button ReturnToLaneButton;

        public Color ColorWallOn = new(0.2f, 0.7f, 0.6f, 1f);
        public Color ColorAutoOn = new(0.2f, 0.7f, 0.6f, 1f);
        public Color ColorAutoOff = new(0.14f, 0.12f, 0.10f, 0.92f);
        public Color ColorPhaseOff = new(0.35f, 0.35f, 0.35f, 0.50f);
        public Color ColorReturnLane = new(0.10f, 0.58f, 0.52f, 0.98f);

        [Range(0.10f, 0.35f)] public float WidthPercentOfScreen = 0.16f;
        public float MinExpandedWidth = 152f;
        public float MaxExpandedWidth = 228f;
        public float CollapsedWidth = 20f;
        public float CollapseToggleWidth = 26f;
        public float CollapseToggleHeight = 116f;
        public float CollapseToggleInset = 6f;
        public float CollapseAnimDuration = 0.18f;
        [Range(0.08f, 0.30f)] public float MobileWidthPercentOfSafeArea = 0.13f;
        public float MobileMinExpandedWidth = 118f;
        public float MobileMaxExpandedWidth = 180f;
        public float MobileCollapsedWidth = 24f;
        public float MobileCollapseToggleWidth = 38f;
        public float MobileCollapseToggleHeight = 132f;
        public float MobileCollapseToggleInset = 4f;
        public bool RespectSafeArea = true;
        public float SmallScreenThreshold = 1100f;
        public float MobileMinButtonHeight = 80f;
        public float DesktopMinButtonHeight = 128f;
        public float MobileVerticalSpacing = 4f;
        public float DesktopVerticalSpacing = 12f;
        public int MobileTopBottomPadding = 4;
        public int DesktopTopBottomPadding = 10;
        [Range(0.60f, 1.00f)] public float MobileVerticalUsagePercent = 0.84f;
        [Range(0.60f, 1.00f)] public float DesktopVerticalUsagePercent = 0.98f;
        [Header("Lane Command Cluster")]
        [Range(0.18f, 0.40f)] public float ClusterWidthPercentOfScreen = 0.31f;
        [Range(0.28f, 0.60f)] public float MobileClusterWidthPercentOfSafeArea = 0.46f;
        public float ClusterMinWidth = 408f;
        public float ClusterMaxWidth = 620f;
        public float MobileClusterMinWidth = 330f;
        public float MobileClusterMaxWidth = 500f;
        public float ClusterHeight = 184f;
        public float MobileClusterHeight = 160f;
        public float ClusterLeftInset = 6f;
        public float ClusterTopInset = 12f;
        public float ClusterPadding = 8f;

        [SerializeField] UnitPortraitCamera PortraitCam;
        [SerializeField] UnitPrefabRegistry PortraitRegistry;

        const float MissingLoadoutErrorDelaySeconds = 1.5f;
        const float ActivityDownTickFlashSeconds = 0.42f;
        static readonly string[] BarracksOrder = { "left", "center", "right" };
        static readonly Color CommandButtonActive = new(0.18f, 0.55f, 0.46f, 0.98f);
        static readonly Color CommandButtonInactive = new(0.14f, 0.20f, 0.27f, 0.98f);
        static readonly Color CommandButtonDisabled = new(0.18f, 0.18f, 0.20f, 0.72f);
        static readonly Color ActivityDownTickFlashColor = new(0.96f, 0.20f, 0.14f, 0.72f);
        static readonly Color ActivityDownTickTextColor = new(1.00f, 0.76f, 0.72f, 1.00f);
        static readonly Color InactiveActivityIconColor = new(0.84f, 0.88f, 0.95f, 0.70f);
        static readonly Color InactiveActivityFallbackColor = new(0.82f, 0.86f, 0.92f, 0.76f);
        static readonly BarracksActivityIconKind[] ActivityIconOrder =
        {
            BarracksActivityIconKind.Shield,
            BarracksActivityIconKind.Sword,
            BarracksActivityIconKind.Spear,
            BarracksActivityIconKind.Archer,
            BarracksActivityIconKind.Priest,
            BarracksActivityIconKind.Mage,
        };

        readonly Dictionary<string, Texture2D> _portraitCache = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _capturePending = new(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> _captureQueue = new();
        readonly List<RawImage> _buttonPortraits = new();
        readonly Dictionary<string, BarracksSectionView> _sections = new(StringComparer.OrdinalIgnoreCase);

        string[] _loadoutKeys = Array.Empty<string>();
        RectTransform _runtimeRoot;
        RectTransform _runtimeHeaderRoot;
        RectTransform _runtimeViewport;
        RectTransform _contentRoot;
        Image _runtimeBackground;
        TMP_Text _emptyStateLabel;
        TMP_Text _titleLabel;
        Button _collapseButton;
        TMP_Text _collapseButtonLabel;
        CanvasGroup _contentCanvasGroup;
        RenderTexture _runtimePortraitTexture;
        GameObject _runtimePortraitRoot;
        Coroutine _collapseAnimation;
        bool _isCapturingPortraits;
        bool _loadoutMissingLogged;
        bool _panelCollapsed;
        string _lastActivitySignature;
        Rect _lastSafeArea = new(-1f, -1f, -1f, -1f);

        void Start()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnMLMatchConfig += HandleMatchConfig;
            HideLegacyWidgets();
            EnsurePortraitSlots();
            BuildRuntimePanel();

            var cachedLoadout = NetworkManager.Instance?.LastMatchLoadout;
            if (cachedLoadout != null && cachedLoadout.Length > 0) ApplyMatchLoadout(cachedLoadout);
            else StartCoroutine(RequireAuthoritativeLoadout());

            RefreshActivityPanel(true);
        }

        void OnDestroy()
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnMLMatchConfig -= HandleMatchConfig;
            if (_runtimePortraitRoot != null) Destroy(_runtimePortraitRoot);
            if (_runtimePortraitTexture != null)
            {
                _runtimePortraitTexture.Release();
                Destroy(_runtimePortraitTexture);
            }
            foreach (var texture in _portraitCache.Values) if (texture != null) Destroy(texture);
            _portraitCache.Clear();
        }

        void Update()
        {
            if (ApplyRuntimeSafeAreaLayout())
                UpdateRuntimePanelSizing(immediate: true);

            RefreshButtonPortraits();
            RefreshActivityPanel(false);
            RefreshActivityRowEffects();
            RefreshActivityPortraits();
        }

        void OnRectTransformDimensionsChange()
        {
            UpdateRuntimePanelSizing(immediate: true);
            RefreshResponsiveSectionLayout();
        }

        void HandleMatchConfig(MLMatchConfig config)
        {
            if (config?.loadout == null || config.loadout.Length == 0) return;
            ApplyMatchLoadout(config.loadout);
        }

        IEnumerator RequireAuthoritativeLoadout()
        {
            yield return new WaitForSeconds(MissingLoadoutErrorDelaySeconds);
            if (_loadoutKeys.Length > 0 || _loadoutMissingLogged) yield break;

            if (ShouldUseEditorPreviewWithoutAuthoritativeMatch())
                yield break;

            _loadoutMissingLogged = true;
            Debug.LogError($"[CmdBar] No authoritative match loadout arrived for '{name}' in scene '{gameObject.scene.name}'.");
        }

        static bool ShouldUseEditorPreviewWithoutAuthoritativeMatch()
        {
#if UNITY_EDITOR
            if (!Application.isEditor)
                return false;

            if (SnapshotApplier.Instance?.LatestMLMatchConfig?.loadout?.Length > 0)
                return false;

            if (SnapshotApplier.Instance?.LatestMLMatchReady?.laneAssignments?.Length > 0)
                return false;

            var network = NetworkManager.Instance;
            if (network == null)
                return true;

            if (network.LastMatchLoadout?.Length > 0)
                return false;

            if (network.LastMLMatchReady?.laneAssignments?.Length > 0)
                return false;

            if (network.LastMLMatchConfig?.loadout?.Length > 0)
                return false;

            return true;
#else
            return false;
#endif
        }

        void ApplyMatchLoadout(LoadoutEntry[] loadout)
        {
            int count = Mathf.Min(loadout != null ? loadout.Length : 0, UnitButtons != null ? UnitButtons.Length : 0);
            _loadoutKeys = new string[count];
            _loadoutMissingLogged = false;
            for (int i = 0; i < count; i++)
            {
                var entry = loadout[i];
                _loadoutKeys[i] = entry != null ? entry.key : null;
                SetHiddenButtonLabel(i, entry != null ? entry.name : string.Empty);
            }
            ResetPortraitState();
            RefreshButtonPortraits();
        }

        void HideLegacyWidgets()
        {
            var rootLayout = GetComponent<VerticalLayoutGroup>();
            if (rootLayout != null) rootLayout.enabled = false;
            var fitter = GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            var layoutGroup = GetComponent<LayoutGroup>();
            if (layoutGroup != null) layoutGroup.enabled = false;
            var layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null) layoutElement.ignoreLayout = true;
            var rootImage = GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.color = new Color(rootImage.color.r, rootImage.color.g, rootImage.color.b, 0f);
                rootImage.raycastTarget = false;
            }

            if (UnitButtons != null) for (int i = 0; i < UnitButtons.Length; i++) if (UnitButtons[i] != null) { UnitButtons[i].interactable = false; UnitButtons[i].gameObject.SetActive(false); }
            if (AutoToggleButtons != null) for (int i = 0; i < AutoToggleButtons.Length; i++) if (AutoToggleButtons[i] != null) AutoToggleButtons[i].gameObject.SetActive(false);
            if (AutoToggleTxts != null) for (int i = 0; i < AutoToggleTxts.Length; i++) if (AutoToggleTxts[i] != null) AutoToggleTxts[i].gameObject.SetActive(false);
            if (QueueCountLabels != null) for (int i = 0; i < QueueCountLabels.Length; i++) if (QueueCountLabels[i] != null) QueueCountLabels[i].gameObject.SetActive(false);
            if (QueueDrainBar != null) QueueDrainBar.gameObject.SetActive(false);
            if (ReturnToLaneButton != null) ReturnToLaneButton.gameObject.SetActive(false);
        }

        void BuildRuntimePanel()
        {
            if (_contentRoot != null) return;

            ApplyClusterRuntimeTuning();
            RectTransform runtimeHost = ResolveCanvasRect();
            if (runtimeHost == null)
                runtimeHost = transform as RectTransform;

            RectTransform existingRoot = transform.Find("LaneCommandClusterRoot") as RectTransform;
            if (existingRoot == null && runtimeHost != null && runtimeHost != transform)
                existingRoot = runtimeHost.Find("LaneCommandClusterRoot") as RectTransform;

            _runtimeRoot = existingRoot ?? EnsureChildRect(runtimeHost != null ? runtimeHost : transform, "LaneCommandClusterRoot");
            if (runtimeHost != null && _runtimeRoot.parent != runtimeHost)
                _runtimeRoot.SetParent(runtimeHost, false);
            _runtimeRoot.SetAsLastSibling();
            _runtimeRoot.anchorMin = new Vector2(0f, 1f);
            _runtimeRoot.anchorMax = new Vector2(0f, 1f);
            _runtimeRoot.pivot = new Vector2(0f, 1f);
            _runtimeRoot.anchoredPosition = new Vector2(ClusterLeftInset, -ClusterTopInset);
            _runtimeRoot.sizeDelta = new Vector2(ClusterMinWidth, ClusterHeight);
            _runtimeBackground = _runtimeRoot.gameObject.GetComponent<Image>() ?? _runtimeRoot.gameObject.AddComponent<Image>();
            _runtimeBackground.color = Color.clear;
            _runtimeBackground.raycastTarget = false;
            _runtimeBackground.enabled = false;
            ClearPanelChrome(_runtimeRoot.gameObject);

            _contentRoot = EnsureChildRect(_runtimeRoot, "Content");
            Stretch(
                _contentRoot,
                new Vector2(ClusterPadding, ClusterPadding),
                new Vector2(-ClusterPadding, -ClusterPadding));

            var layout = _contentRoot.gameObject.GetComponent<HorizontalLayoutGroup>() ?? _contentRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = IsCompactPanel() ? 8f : 12f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            _emptyStateLabel = null;

            for (int i = 0; i < BarracksOrder.Length; i++) EnsureSection(BarracksOrder[i]);
            EnsureDefaultActivityRows();
            UpdateRuntimePanelSizing(immediate: true);
            RefreshResponsiveSectionLayout();
        }

        void ApplyClusterRuntimeTuning()
        {
            ClusterWidthPercentOfScreen = 0.31f;
            MobileClusterWidthPercentOfSafeArea = 0.46f;
            ClusterMinWidth = 408f;
            ClusterMaxWidth = 620f;
            MobileClusterMinWidth = 330f;
            MobileClusterMaxWidth = 500f;
            ClusterHeight = 184f;
            MobileClusterHeight = 160f;
            ClusterLeftInset = 6f;
            ClusterTopInset = 12f;
            ClusterPadding = 8f;
        }

        void EnsureRuntimeRootHost()
        {
            if (_runtimeRoot == null)
                return;

            RectTransform runtimeHost = ResolveCanvasRect();
            if (runtimeHost == null || _runtimeRoot.parent == runtimeHost)
                return;

            _runtimeRoot.SetParent(runtimeHost, false);
            _runtimeRoot.SetAsLastSibling();
            _runtimeRoot.anchorMin = new Vector2(0f, 1f);
            _runtimeRoot.anchorMax = new Vector2(0f, 1f);
            _runtimeRoot.pivot = new Vector2(0f, 1f);
        }

        BarracksSectionView EnsureSection(string barracksId)
        {
            string id = BarracksActivityUtility.NormalizeBarracksId(barracksId);
            if (_sections.TryGetValue(id, out var existing)) return existing;

            var root = EnsureChildRect(_contentRoot, $"{id}_section");
            var image = root.gameObject.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            image.color = ResolveSectionFillColor(id, true);
            image.raycastTarget = false;
            ApplyPanelChrome(root.gameObject, ResolveSectionAccentColor(id, 0.72f), ResolveSectionAccentColor(id));

            var layout = root.gameObject.GetComponent<VerticalLayoutGroup>() ?? root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 12, 8);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var rootLayout = root.gameObject.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            rootLayout.flexibleWidth = 1f;
            rootLayout.flexibleHeight = 1f;
            rootLayout.minHeight = 0f;
            rootLayout.preferredHeight = 0f;
            rootLayout.minWidth = 0f;
            rootLayout.preferredWidth = 0f;

            var header = EnsureText(root, "Header", GetResponsiveBarracksHeader(id), 13f, FontStyles.Bold);
            ClassicRpgUiRuntime.ApplyTextStyle(header, ClassicRpgTextStyle.SectionHeader, TextAlignmentOptions.Center, ResolveSectionAccentColor(id), allowWrap: false);
            header.enableAutoSizing = false;
            ConfigureSectionTextLayout(header, 18f);

            var controls = EnsureChildRect(root, "Controls");
            var controlsLayout = controls.gameObject.GetComponent<HorizontalLayoutGroup>() ?? controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            controlsLayout.spacing = 8f;
            controlsLayout.childAlignment = TextAnchor.MiddleCenter;
            controlsLayout.childForceExpandWidth = true;
            controlsLayout.childForceExpandHeight = false;
            controlsLayout.childControlWidth = true;
            controlsLayout.childControlHeight = true;

            bool compactControls = IsCompactPanel();
            float commandButtonHeight = GetCommandButtonHeight();
            var attackButton = EnsureCommandButton(controls, "AttackButton", "Attack", compactControls, commandButtonHeight);
            var defendButton = EnsureCommandButton(controls, "DefendButton", "Defend", compactControls, commandButtonHeight);
            var retreatButton = EnsureCommandButton(controls, "RetreatButton", "Retreat", compactControls, commandButtonHeight);

            var rows = EnsureChildRect(root, "Rows");
            var legacyRowsLayout = rows.gameObject.GetComponent<HorizontalLayoutGroup>();
            if (legacyRowsLayout != null)
                legacyRowsLayout.enabled = false;
            var rowsLayout = rows.gameObject.GetComponent<GridLayoutGroup>() ?? rows.gameObject.AddComponent<GridLayoutGroup>();
            rowsLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rowsLayout.constraintCount = 3;
            rowsLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            rowsLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.padding = new RectOffset(0, 0, 0, 0);
            rowsLayout.spacing = new Vector2(GetCommandColumnSpacing(compactControls), GetUnitGridVerticalSpacing(compactControls));
            float initialUnitCardSize = GetUnitCardHeight(compactControls);
            rowsLayout.cellSize = new Vector2(initialUnitCardSize, initialUnitCardSize);
            var rowsLayoutElement = rows.gameObject.GetComponent<LayoutElement>() ?? rows.gameObject.AddComponent<LayoutElement>();
            rowsLayoutElement.minHeight = GetUnitGridHeight();
            rowsLayoutElement.preferredHeight = GetUnitGridHeight();
            rowsLayoutElement.flexibleHeight = 0f;

            var section = new BarracksSectionView(id, root, image, header, controls, attackButton, defendButton, retreatButton, rows);
            _sections[id] = section;
            ApplyResponsiveSectionLayout(section);
            return section;
        }

        void RefreshActivityPanel(bool force)
        {
            if (_contentRoot == null) return;

            var applier = SnapshotApplier.Instance;
            var snapshot = applier != null ? applier.LatestML : null;
            int ownerLaneIndex = applier != null ? applier.MyLaneIndex : -1;
            var ownerLane = FindLane(snapshot, ownerLaneIndex);
            bool hasValidSnapshot = snapshot != null && ownerLaneIndex >= 0 && ownerLane != null;

            if (!hasValidSnapshot)
            {
                EnsureDefaultActivityRows();
                foreach (var section in _sections.Values)
                    UpdateSectionControls(section, -1, null);
                return;
            }

            var buckets = BarracksActivityUtility.CollectAllBarracksActivity(snapshot, ownerLaneIndex);
            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var section = EnsureSection(bucket.BarracksId);
                section.Header.text = GetResponsiveBarracksHeader(bucket.BarracksId);
                UpdateSectionControls(section, ownerLaneIndex, ownerLane);
            }

            string signature = BuildActivitySignature(snapshot, ownerLaneIndex, ownerLane, buckets);
            if (!force && string.Equals(signature, _lastActivitySignature, StringComparison.Ordinal))
            {
                if (_emptyStateLabel != null)
                    _emptyStateLabel.gameObject.SetActive(false);
                return;
            }

            _lastActivitySignature = signature;
            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var section = EnsureSection(bucket.BarracksId);
                SetSectionRows(section, bucket, ownerLaneIndex);
                ApplyResponsiveSectionLayout(section);
            }

            if (_emptyStateLabel != null)
                _emptyStateLabel.gameObject.SetActive(false);
        }

        string BuildActivitySignature(MLSnapshot snapshot, int ownerLaneIndex, MLLaneSnap ownerLane, List<BarracksActivityBucket> buckets)
        {
            if (snapshot == null || ownerLaneIndex < 0) return "no-snapshot";
            var parts = new List<string>(20) { ownerLaneIndex.ToString() };
            parts.Add(ownerLane != null ? ownerLane.commandState ?? string.Empty : string.Empty);
            parts.Add(ownerLane != null ? ownerLane.commandAnchorProgress.ToString("F3") : string.Empty);
            parts.Add(ownerLane != null && ownerLane.eliminated ? "eliminated" : "active");
            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                parts.Add(bucket.BarracksId);
                var site = BarracksActivityUtility.FindBarracksSite(ownerLane, bucket.BarracksId);
                parts.Add(site != null ? site.commandState ?? string.Empty : string.Empty);
                for (int j = 0; j < bucket.Rows.Count; j++)
                {
                    parts.Add(bucket.Rows[j].StableKey);
                    parts.Add(bucket.Rows[j].Count.ToString());
                }
            }
            return string.Join("|", parts);
        }

        void SetSectionRows(BarracksSectionView section, BarracksActivityBucket bucket, int ownerLaneIndex)
        {
            var displayRows = BuildDisplayRows(bucket);
            for (int i = 0; i < displayRows.Count; i++)
            {
                var row = displayRows[i];
                var view = EnsureActivityRowView(section, row.IconKind, i);
                UpdateActivityRow(view, row, ownerLaneIndex, bucket.BarracksId);
            }
        }

        void EnsureDefaultActivityRows()
        {
            foreach (var section in _sections.Values)
            {
                if (section == null || section.RowsRoot == null || section.RowsRoot.childCount > 0)
                    continue;

                var bucket = new BarracksActivityBucket(
                    section.BarracksId,
                    BarracksActivityUtility.GetBarracksHeader(section.BarracksId));
                SetSectionRows(section, bucket, -1);
                ApplyResponsiveSectionLayout(section);
            }
        }

        List<BarracksActivityDisplayRow> BuildDisplayRows(BarracksActivityBucket bucket)
        {
            var groupedCounts = new Dictionary<BarracksActivityIconKind, int>();
            if (bucket?.Rows != null)
            {
                for (int i = 0; i < bucket.Rows.Count; i++)
                {
                    var row = bucket.Rows[i];
                    if (row == null || row.Count <= 0)
                        continue;

                    var iconKind = ResolveActivityIconKind(row);
                    groupedCounts.TryGetValue(iconKind, out int currentCount);
                    groupedCounts[iconKind] = currentCount + Mathf.Max(0, row.Count);
                }
            }

            var displayRows = new List<BarracksActivityDisplayRow>(ActivityIconOrder.Length);
            for (int i = 0; i < ActivityIconOrder.Length; i++)
            {
                var iconKind = ActivityIconOrder[i];
                groupedCounts.TryGetValue(iconKind, out int count);

                displayRows.Add(new BarracksActivityDisplayRow(iconKind, count, i));
            }

            return displayRows;
        }

        void UpdateSectionControls(BarracksSectionView section, int ownerLaneIndex, MLLaneSnap ownerLane)
        {
            if (section == null) return;

            var site = BarracksActivityUtility.FindBarracksSite(ownerLane, section.BarracksId);
            bool canIssueOrders = ownerLane != null && ownerLaneIndex >= 0 && !ownerLane.eliminated && site != null && site.isBuilt;
            string commandState = site != null && !string.IsNullOrWhiteSpace(site.commandState)
                ? site.commandState.Trim().ToUpperInvariant()
                : ownerLane != null && !string.IsNullOrWhiteSpace(ownerLane.commandState)
                    ? ownerLane.commandState.Trim().ToUpperInvariant()
                    : "DEFEND";

            if (section.RootImage != null)
                section.RootImage.color = ResolveSectionFillColor(section.BarracksId, canIssueOrders);
            if (section.Header != null)
                section.Header.color = canIssueOrders ? ResolveSectionAccentColor(section.BarracksId) : ResolveSectionAccentColor(section.BarracksId, 0.42f);

            ConfigureCommandButton(section.AttackButton, "Attack", commandState == "ATTACK", canIssueOrders, () => ActionSender.SetBarracksAttack(section.BarracksId));
            ConfigureCommandButton(
                section.DefendButton,
                "Defend",
                commandState == "DEFEND",
                canIssueOrders,
                () => ActionSender.SetBarracksDefend(section.BarracksId));
            ConfigureCommandButton(section.RetreatButton, "Retreat", commandState == "RETREAT", canIssueOrders, () => ActionSender.SetBarracksRetreat(section.BarracksId));
        }

        void ConfigureCommandButton(BarracksCommandButtonView buttonView, string label, bool isActive, bool interactable, Action onClick)
        {
            if (buttonView?.Button == null) return;

            Sprite commandSprite = ResolveCommandButtonSprite(label);
            bool iconOnlyButton = commandSprite != null;
            buttonView.Button.transition = Selectable.Transition.None;
            if (buttonView.Label != null)
            {
                buttonView.Label.text = label;
                buttonView.Label.color = interactable
                    ? (isActive ? Color.white : ClassicRpgUiRuntime.BrightText)
                    : new Color(0.76f, 0.80f, 0.88f, 0.82f);
            }
            ApplyCommandButtonArtwork(buttonView.Button, buttonView.Label, label);

            buttonView.Button.onClick.RemoveAllListeners();
            if (interactable && onClick != null)
                buttonView.Button.onClick.AddListener(() => onClick());
            buttonView.Button.interactable = interactable;

            Color backgroundColor = iconOnlyButton
                ? Color.clear
                : ResolveCommandButtonColor(label, isActive, interactable);
            Color iconColor = iconOnlyButton
                ? ResolveCommandSpriteTint(label, isActive, interactable)
                : new Color(1f, 1f, 1f, 0f);
            var image = buttonView.Button.targetGraphic as Image;
            if (image != null)
                image.color = backgroundColor;

            var iconImage = buttonView.Button.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImage != null)
            {
                float iconSize = GetStanceIconSize();
                var iconRect = iconImage.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = Vector2.zero;
                iconImage.transform.SetAsLastSibling();
                iconImage.sprite = commandSprite;
                iconImage.color = iconColor;
                iconImage.preserveAspect = true;
                iconImage.gameObject.SetActive(commandSprite != null);

                var iconShadow = iconImage.GetComponent<Shadow>();
                if (iconShadow != null)
                {
                    iconShadow.effectColor = interactable
                        ? new Color(0f, 0f, 0f, isActive ? 0.52f : 0.34f)
                        : new Color(0f, 0f, 0f, 0.24f);
                    iconShadow.effectDistance = isActive ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                }

                var iconOutline = iconImage.GetComponent<Outline>();
                if (iconOutline != null)
                {
                    iconOutline.effectColor = interactable
                        ? new Color(1f, 0.96f, 0.90f, isActive ? 0.30f : 0.16f)
                        : new Color(1f, 1f, 1f, 0.08f);
                    iconOutline.effectDistance = isActive ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                }
            }

            var shadow = buttonView.Button.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.effectColor = iconOnlyButton
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color(0f, 0f, 0f, 0.28f);
                shadow.effectDistance = iconOnlyButton ? Vector2.zero : new Vector2(1f, -1f);
            }

            var outline = buttonView.Button.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = iconOnlyButton
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color(0f, 0f, 0f, 0.22f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            var colors = buttonView.Button.colors;
            colors.normalColor = backgroundColor;
            colors.highlightedColor = backgroundColor;
            colors.pressedColor = backgroundColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = iconOnlyButton
                ? backgroundColor
                : ResolveCommandButtonColor(label, isActive: false, interactable: false);
            buttonView.Button.colors = colors;
        }

        BarracksActivityRowView EnsureActivityRowView(BarracksSectionView section, BarracksActivityIconKind iconKind, int siblingIndex)
        {
            if (section == null)
                return null;

            for (int i = 0; i < section.RowViews.Count; i++)
            {
                var existing = section.RowViews[i];
                if (existing != null && existing.IconKind == iconKind)
                {
                    existing.Root.SetSiblingIndex(siblingIndex);
                    return existing;
                }
            }

            var created = CreateActivityRow(section.RowsRoot, iconKind);
            ApplyResponsiveActivityRowLayout(created, IsCompactPanel());
            created.Root.SetSiblingIndex(siblingIndex);
            section.RowViews.Add(created);
            return created;
        }

        BarracksActivityRowView CreateActivityRow(Transform parent, BarracksActivityIconKind iconKind)
        {
            var root = EnsureChildRect(parent, $"{iconKind}_row");
            var image = root.gameObject.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            var button = root.gameObject.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            image.color = Color.clear;
            image.raycastTarget = true;

            var layoutElement = root.gameObject.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            float cardSize = GetUnitCardHeight();
            layoutElement.minHeight = cardSize;
            layoutElement.preferredHeight = cardSize;
            layoutElement.minWidth = 0f;
            layoutElement.preferredWidth = 0f;
            layoutElement.flexibleWidth = 0f;

            var slot = EnsureChildRect(root, "Slot");
            slot.anchorMin = new Vector2(0.5f, 0.5f);
            slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = Vector2.zero;
            slot.sizeDelta = new Vector2(cardSize, cardSize);
            var slotLayout = slot.gameObject.GetComponent<LayoutElement>() ?? slot.gameObject.AddComponent<LayoutElement>();
            slotLayout.ignoreLayout = true;

            var icon = EnsureChildRect(slot, "Icon");
            Stretch(icon, Vector2.zero, Vector2.zero);
            var iconImage = icon.gameObject.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.sprite = ResolveActivityIconSprite(iconKind);
            iconImage.color = iconImage.sprite != null
                ? InactiveActivityIconColor
                : new Color(1f, 1f, 1f, 0f);
            var iconShadow = icon.gameObject.GetComponent<Shadow>() ?? icon.gameObject.AddComponent<Shadow>();
            iconShadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
            iconShadow.effectDistance = new Vector2(1f, -1f);
            var iconOutline = icon.gameObject.GetComponent<Outline>() ?? icon.gameObject.AddComponent<Outline>();
            iconOutline.effectColor = new Color(1f, 1f, 1f, 0.12f);
            iconOutline.effectDistance = new Vector2(1f, -1f);

            var fallback = EnsureText(slot, "FallbackLabel", BuildActivityIconFallbackLabel(iconKind), IsCompactPanel() ? 8.5f : 9.5f, FontStyles.Bold);
            fallback.alignment = TextAlignmentOptions.Center;
            fallback.color = InactiveActivityFallbackColor;
            fallback.raycastTarget = false;
            fallback.rectTransform.anchorMin = Vector2.zero;
            fallback.rectTransform.anchorMax = Vector2.one;
            fallback.rectTransform.offsetMin = new Vector2(1f, 1f);
            fallback.rectTransform.offsetMax = new Vector2(-1f, -1f);
            fallback.gameObject.SetActive(iconImage.sprite == null);

            var count = EnsureText(slot, "Count", GetActivityCountLabel(0), IsCompactPanel() ? 8.5f : 9.5f, FontStyles.Bold);
            count.alignment = TextAlignmentOptions.BottomRight;
            count.color = Color.white;
            count.enableAutoSizing = false;
            count.fontSizeMin = count.fontSize;
            count.fontSizeMax = count.fontSize;
            count.outlineWidth = 0.2f;
            count.outlineColor = new Color(0f, 0f, 0f, 0.95f);
            count.raycastTarget = false;
            count.rectTransform.anchorMin = new Vector2(1f, 0f);
            count.rectTransform.anchorMax = new Vector2(1f, 0f);
            count.rectTransform.pivot = new Vector2(1f, 0f);
            count.rectTransform.sizeDelta = new Vector2(20f, 12f);
            count.rectTransform.anchoredPosition = new Vector2(-1f, 0f);
            count.gameObject.SetActive(false);

            return new BarracksActivityRowView(iconKind, root, image, button, slot, iconImage, fallback, count, null, null);
        }

        void UpdateActivityRow(BarracksActivityRowView view, BarracksActivityDisplayRow row, int ownerLaneIndex, string barracksId)
        {
            if (view == null || row == null)
                return;

            bool interactable = ownerLaneIndex >= 0;
            if (view.Button != null)
            {
                view.Button.onClick.RemoveAllListeners();
                if (interactable)
                    view.Button.onClick.AddListener(() => FortressSelectionController.OpenBarracksSite(ownerLaneIndex, barracksId));
                view.Button.interactable = interactable;
            }

            if (row.Count < view.CurrentCount)
                view.DownTickFlashUntil = Mathf.Max(view.DownTickFlashUntil, Time.unscaledTime + ActivityDownTickFlashSeconds);

            view.CurrentCount = Mathf.Max(0, row.Count);
            if (view.CountLabel != null)
                view.CountLabel.text = GetActivityCountLabel(view.CurrentCount);

            ApplyActivityRowVisualState(view, Time.unscaledTime);
        }

        void RefreshActivityRowEffects()
        {
            float now = Time.unscaledTime;
            foreach (var section in _sections.Values)
            {
                if (section == null)
                    continue;

                for (int i = 0; i < section.RowViews.Count; i++)
                    ApplyActivityRowVisualState(section.RowViews[i], now);
            }
        }

        void ApplyActivityRowVisualState(BarracksActivityRowView view, float now)
        {
            if (view == null)
                return;

            bool hasUnits = view.CurrentCount > 0;
            bool flashing = view.DownTickFlashUntil > now;
            float flashStrength = 0f;
            if (flashing)
            {
                float elapsed = 1f - Mathf.Clamp01((view.DownTickFlashUntil - now) / ActivityDownTickFlashSeconds);
                flashStrength = Mathf.Sin(elapsed * Mathf.PI);
            }

            if (view.RootImage != null)
                view.RootImage.color = Color.Lerp(Color.clear, ActivityDownTickFlashColor, flashStrength);

            Color baseIconColor = hasUnits ? ResolveActivityIconTint(view.IconKind) : InactiveActivityIconColor;
            Color baseFallbackColor = hasUnits
                ? new Color(0.95f, 0.90f, 0.72f, 0.96f)
                : InactiveActivityFallbackColor;
            if (view.IconImage != null)
            {
                view.IconImage.color = view.IconImage.sprite != null
                    ? Color.Lerp(baseIconColor, ActivityDownTickTextColor, flashStrength * 0.70f)
                    : new Color(1f, 1f, 1f, 0f);
            }

            if (view.FallbackLabel != null)
            {
                bool showFallback = view.IconImage == null || view.IconImage.sprite == null;
                view.FallbackLabel.gameObject.SetActive(showFallback);
                view.FallbackLabel.color = Color.Lerp(baseFallbackColor, ActivityDownTickTextColor, flashStrength);
            }

            if (view.CountLabel != null)
            {
                bool showCount = hasUnits || flashStrength > 0.01f;
                view.CountLabel.gameObject.SetActive(showCount);
                view.CountLabel.color = Color.Lerp(Color.white, ActivityDownTickTextColor, flashStrength);
                view.CountLabel.outlineColor = Color.Lerp(new Color(0f, 0f, 0f, 0.95f), new Color(0.35f, 0f, 0f, 0.98f), flashStrength);
            }
        }

        void RefreshActivityPortraits()
        {
            foreach (var section in _sections.Values)
            {
                for (int i = 0; i < section.RowViews.Count; i++)
                {
                    var view = section.RowViews[i];
                    if (view == null || view.Portrait == null || string.IsNullOrWhiteSpace(view.PortraitKey)) continue;
                    if (_portraitCache.TryGetValue(view.PortraitKey, out var portrait) && portrait != null)
                    {
                        view.Portrait.texture = portrait;
                        view.Portrait.color = Color.white;
                    }
                    else
                    {
                        view.Portrait.texture = null;
                        view.Portrait.color = new Color(1f, 1f, 1f, 0f);
                        StartPortraitCapture(view.PortraitKey);
                    }
                }
            }
        }

        void SetHiddenButtonLabel(int index, string text)
        {
            if (UnitButtons == null || index < 0 || index >= UnitButtons.Length || UnitButtons[index] == null) return;
            var label = UnitButtons[index].transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null) label.text = text;
        }

        void ResetPortraitState()
        {
            _portraitCache.Clear();
            _capturePending.Clear();
            _captureQueue.Clear();
            if (_buttonPortraits.Count == 0) EnsurePortraitSlots();
            for (int i = 0; i < _buttonPortraits.Count; i++)
                if (_buttonPortraits[i] != null) { _buttonPortraits[i].texture = null; _buttonPortraits[i].color = new Color(1f, 1f, 1f, 0f); }
        }

        void EnsurePortraitSlots()
        {
            _buttonPortraits.Clear();
            if (UnitButtons == null) return;
            for (int i = 0; i < UnitButtons.Length; i++) _buttonPortraits.Add(EnsurePortraitImage(UnitButtons[i]));
        }

        RawImage EnsurePortraitImage(Button button)
        {
            if (button == null) return null;
            var existing = button.transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>();
            if (existing != null) return existing;

            var frame = EnsureChildRect(button.transform, "PortraitFrame");
            var frameImage = frame.gameObject.GetComponent<Image>() ?? frame.gameObject.AddComponent<Image>();
            frameImage.color = new Color(0.08f, 0.12f, 0.20f, 0.96f);
            if (frame.gameObject.GetComponent<Mask>() == null) frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var reference = button.transform.Find("IconBg") as RectTransform ?? button.transform.Find("Icon") as RectTransform;
            if (reference != null)
            {
                frame.anchorMin = reference.anchorMin;
                frame.anchorMax = reference.anchorMax;
                frame.anchoredPosition = reference.anchoredPosition;
                frame.sizeDelta = reference.sizeDelta;
                frame.offsetMin = reference.offsetMin + new Vector2(2f, 2f);
                frame.offsetMax = reference.offsetMax + new Vector2(-2f, -2f);
            }
            else
            {
                frame.anchorMin = new Vector2(0.10f, 0.24f);
                frame.anchorMax = new Vector2(0.90f, 0.72f);
                frame.offsetMin = Vector2.zero;
                frame.offsetMax = Vector2.zero;
            }
            return EnsureRawPortrait(frame);
        }

        RawImage EnsureRawPortrait(Transform parent)
        {
            var existing = parent.Find("Portrait")?.GetComponent<RawImage>();
            if (existing != null) return existing;

            var rect = EnsureChildRect(parent, "Portrait");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(-2f, -2f);
            var portrait = rect.gameObject.GetComponent<RawImage>() ?? rect.gameObject.AddComponent<RawImage>();
            portrait.color = new Color(1f, 1f, 1f, 0f);
            portrait.raycastTarget = false;
            var fitter = rect.gameObject.GetComponent<AspectRatioFitter>() ?? rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1f;
            return portrait;
        }

        void RefreshButtonPortraits()
        {
            if (_buttonPortraits.Count == 0) EnsurePortraitSlots();
            for (int i = 0; i < _buttonPortraits.Count; i++)
            {
                var portrait = _buttonPortraits[i];
                if (portrait == null) continue;
                string key = i < _loadoutKeys.Length ? _loadoutKeys[i] : null;
                if (string.IsNullOrWhiteSpace(key))
                {
                    portrait.texture = null;
                    portrait.color = new Color(1f, 1f, 1f, 0f);
                    continue;
                }

                if (_portraitCache.TryGetValue(key, out var cached) && cached != null)
                {
                    portrait.texture = cached;
                    portrait.color = Color.white;
                }
                else
                {
                    portrait.texture = null;
                    portrait.color = new Color(1f, 1f, 1f, 0f);
                    StartPortraitCapture(key);
                }
            }
        }

        void StartPortraitCapture(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _capturePending.Contains(key)) return;
            var portraitCam = EnsurePortraitCamera();
            if (portraitCam == null) return;
            _capturePending.Add(key);
            _captureQueue.Enqueue(key);
            if (!_isCapturingPortraits) StartCoroutine(ProcessPortraitQueue(portraitCam));
        }

        IEnumerator ProcessPortraitQueue(UnitPortraitCamera portraitCamera)
        {
            _isCapturingPortraits = true;
            while (_captureQueue.Count > 0)
            {
                var key = _captureQueue.Dequeue();
                yield return EnsureRemotePortraitPrefabReady(key);

                bool finished = false;
                Texture2D captured = null;
                portraitCamera.StartIconCapture(key, texture => { captured = texture; finished = true; });
                while (!finished) yield return null;

                _capturePending.Remove(key);
                if (captured != null) _portraitCache[key] = captured;
            }
            _isCapturingPortraits = false;
        }

        IEnumerator EnsureRemotePortraitPrefabReady(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) yield break;
            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null) yield break;
            if (remoteContent.TryGetLoadedPrefabForUnit(key, out var loadedPrefab) && loadedPrefab != null) yield break;

            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry != null && registry.TryGet(key, out var entry) && entry.prefab != null) yield break;
            Debug.LogWarning($"[CmdBar] Portrait source for unit '{key}' is unavailable. Portrait capture will stay blank until the prefab can be resolved.");
        }

        UnitPortraitCamera EnsurePortraitCamera()
        {
            if (PortraitCam != null && PortraitCam.Registry != null) return PortraitCam;
            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry == null) return null;
            if (PortraitCam != null)
            {
                PortraitCam.Registry = registry;
                return PortraitCam;
            }
            if (_runtimePortraitRoot == null)
                PortraitCam = RuntimePortraitStudio.Create("CmdBarPortraitStudio", registry, out _runtimePortraitRoot, out _runtimePortraitTexture);
            PortraitCam.Registry = registry;
            PortraitCam.transform.position = new Vector3(0f, 0f, 50f);
            return PortraitCam;
        }

        static RectTransform EnsureChildRect(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        static TMP_Text EnsureText(Transform parent, string name, string value, float fontSize, FontStyles fontStyle)
        {
            var existing = parent.Find(name)?.GetComponent<TextMeshProUGUI>();
            if (existing != null)
            {
                existing.text = value;
                ApplyTextDefaults(existing, fontSize, fontStyle);
                if (TMP_Settings.defaultFontAsset != null && existing.font == null) existing.font = TMP_Settings.defaultFontAsset;
                return existing;
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.color = Color.white;
            ApplyTextDefaults(text, fontSize, fontStyle);
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        static void ApplyTextDefaults(TMP_Text text, float fontSize, FontStyles fontStyle)
        {
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.extraPadding = true;
        }

        static BarracksCommandButtonView EnsureCommandButton(Transform parent, string name, string label, bool compact, float buttonHeight)
        {
            var existingButton = parent.Find(name)?.GetComponent<Button>();
            if (existingButton != null)
            {
                var existingLayout = existingButton.GetComponent<LayoutElement>();
                if (existingLayout != null)
                {
                    existingLayout.minHeight = buttonHeight;
                    existingLayout.preferredHeight = buttonHeight;
                }

                var existingLabel = existingButton.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (existingLabel != null)
                {
                    existingLabel.text = label;
                    existingLabel.fontSize = compact ? 10f : 11f;
                    existingLabel.enableAutoSizing = false;
                    existingLabel.alignment = TextAlignmentOptions.Center;
                    ClassicRpgUiRuntime.ApplyTextStyle(existingLabel, ClassicRpgTextStyle.ButtonLabel, TextAlignmentOptions.Center, ClassicRpgUiRuntime.BrightText, allowWrap: false);
                    Stretch(existingLabel.rectTransform, new Vector2(4f, -2f), new Vector2(-4f, 2f));
                }
                ApplyCommandButtonArtwork(existingButton, existingLabel, label);
                return new BarracksCommandButtonView(existingButton, existingLabel);
            }

            var root = EnsureChildRect(parent, name);
            var image = root.gameObject.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            var button = root.gameObject.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            var shadow = root.gameObject.GetComponent<Shadow>() ?? root.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.28f);
            shadow.effectDistance = new Vector2(1f, -1f);
            var outline = root.gameObject.GetComponent<Outline>() ?? root.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.22f);
            outline.effectDistance = new Vector2(1f, -1f);

            var layout = root.gameObject.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = buttonHeight;
            layout.preferredHeight = buttonHeight;
            layout.minWidth = 0f;
            layout.preferredWidth = 0f;
            layout.flexibleWidth = 1f;

            var text = EnsureText(root, "Label", label, compact ? 11f : 12f, FontStyles.Bold);
            ClassicRpgUiRuntime.ApplyTextStyle(text, ClassicRpgTextStyle.ButtonLabel, TextAlignmentOptions.Center, ClassicRpgUiRuntime.BrightText, allowWrap: false);
            text.enableAutoSizing = false;
            text.fontSize = compact ? 10f : 11f;
            text.fontSizeMin = compact ? 10f : 11f;
            text.fontSizeMax = compact ? 10f : 11f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(text.rectTransform, new Vector2(4f, -2f), new Vector2(-4f, 2f));
            ApplyCommandButtonArtwork(button, text, label);

            return new BarracksCommandButtonView(button, text);
        }

        static void ConfigureSectionTextLayout(TMP_Text text, float preferredHeight)
        {
            if (text == null)
                return;

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, preferredHeight);

            var layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minWidth = 0f;
            layout.preferredWidth = 0f;
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
        }

        void RefreshResponsiveSectionLayout()
        {
            foreach (var section in _sections.Values)
            {
                if (section?.Header != null)
                    section.Header.text = GetResponsiveBarracksHeader(section.BarracksId);
                ApplyResponsiveSectionLayout(section);
            }
        }

        void ApplyResponsiveSectionLayout(BarracksSectionView section)
        {
            if (section?.Root == null)
                return;

            bool compact = IsCompactPanel();

            var rootLayout = section.Root.GetComponent<VerticalLayoutGroup>();
            if (rootLayout != null)
            {
                rootLayout.padding = compact ? new RectOffset(6, 6, 10, 6) : new RectOffset(8, 8, 12, 8);
                rootLayout.spacing = compact ? 5f : 7f;
            }

            if (section.Header != null)
            {
                section.Header.fontSize = compact ? 11f : 12f;
                section.Header.fontSizeMin = compact ? 11f : 12f;
                section.Header.fontSizeMax = compact ? 11f : 12f;
                var headerLayout = section.Header.GetComponent<LayoutElement>();
                if (headerLayout != null)
                {
                    headerLayout.minHeight = compact ? 16f : 18f;
                    headerLayout.preferredHeight = compact ? 16f : 18f;
                }
            }

            if (section.ControlsRoot != null)
            {
                var controlsLayout = section.ControlsRoot.GetComponent<HorizontalLayoutGroup>();
                if (controlsLayout != null)
                    controlsLayout.spacing = GetCommandColumnSpacing(compact);
            }

            float commandButtonHeight = GetCommandButtonHeight();
            ApplyResponsiveCommandButtonLayout(section.AttackButton, compact, commandButtonHeight);
            ApplyResponsiveCommandButtonLayout(section.DefendButton, compact, commandButtonHeight);
            ApplyResponsiveCommandButtonLayout(section.RetreatButton, compact, commandButtonHeight);

            var legacyRowsLayout = section.RowsRoot != null ? section.RowsRoot.GetComponent<HorizontalLayoutGroup>() : null;
            if (legacyRowsLayout != null)
                legacyRowsLayout.enabled = false;
            var rowsLayout = section.RowsRoot != null ? section.RowsRoot.GetComponent<GridLayoutGroup>() : null;
            if (rowsLayout != null)
            {
                float horizontalSpacing = GetCommandColumnSpacing(compact);
                float verticalSpacing = GetUnitGridVerticalSpacing(compact);
                rowsLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                rowsLayout.constraintCount = 3;
                rowsLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
                rowsLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
                rowsLayout.childAlignment = TextAnchor.UpperCenter;
                rowsLayout.padding = new RectOffset(0, 0, 0, 0);
                rowsLayout.spacing = new Vector2(horizontalSpacing, verticalSpacing);
                rowsLayout.cellSize = GetUnitGridCellSize(section, compact);
            }
            var rowsLayoutElement = section.RowsRoot != null ? section.RowsRoot.GetComponent<LayoutElement>() : null;
            if (rowsLayoutElement != null)
            {
                rowsLayoutElement.minHeight = GetUnitGridHeight(compact);
                rowsLayoutElement.preferredHeight = GetUnitGridHeight(compact);
                rowsLayoutElement.flexibleHeight = 0f;
            }

            for (int i = 0; i < section.RowViews.Count; i++)
                ApplyResponsiveActivityRowLayout(section.RowViews[i], compact);
        }

        static void ApplyResponsiveCommandButtonLayout(BarracksCommandButtonView buttonView, bool compact, float buttonHeight)
        {
            if (buttonView?.Button == null)
                return;

            var layout = buttonView.Button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minHeight = buttonHeight;
                layout.preferredHeight = buttonHeight;
            }

            if (buttonView.Label == null)
                return;

            buttonView.Label.fontSize = compact ? 10f : 11f;
            buttonView.Label.fontSizeMin = compact ? 10f : 11f;
            buttonView.Label.fontSizeMax = compact ? 10f : 11f;
            buttonView.Label.enableAutoSizing = false;
            buttonView.Label.alignment = TextAlignmentOptions.Center;
            Stretch(buttonView.Label.rectTransform, new Vector2(4f, -2f), new Vector2(-4f, 2f));
        }

        bool IsCompactPanel()
        {
            RectTransform canvasRect = ResolveCanvasRect();
            float width = canvasRect != null && canvasRect.rect.width > 0f ? canvasRect.rect.width : Screen.width;
            float height = canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : Screen.height;
            return width <= SmallScreenThreshold || height <= 820f;
        }

        void ApplyResponsiveActivityRowLayout(BarracksActivityRowView rowView, bool compact)
        {
            if (rowView?.Root == null)
                return;

            float cardSize = GetUnitCardHeight();
            var layout = rowView.Root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minHeight = cardSize;
                layout.preferredHeight = cardSize;
                layout.minWidth = 0f;
                layout.preferredWidth = 0f;
                layout.flexibleWidth = 0f;
            }

            if (rowView.SlotRoot != null)
            {
                rowView.SlotRoot.anchorMin = new Vector2(0.5f, 0.5f);
                rowView.SlotRoot.anchorMax = new Vector2(0.5f, 0.5f);
                rowView.SlotRoot.pivot = new Vector2(0.5f, 0.5f);
                rowView.SlotRoot.anchoredPosition = Vector2.zero;
                rowView.SlotRoot.sizeDelta = new Vector2(cardSize, cardSize);
            }

            if (rowView.FallbackLabel != null)
            {
                float fallbackSize = compact ? 8.5f : 9.5f;
                rowView.FallbackLabel.fontSize = fallbackSize;
                rowView.FallbackLabel.fontSizeMin = fallbackSize;
                rowView.FallbackLabel.fontSizeMax = fallbackSize;
            }

            if (rowView.CountLabel != null)
            {
                float countSize = compact ? 8.5f : 9.5f;
                rowView.CountLabel.fontSize = countSize;
                rowView.CountLabel.fontSizeMin = countSize;
                rowView.CountLabel.fontSizeMax = countSize;
                rowView.CountLabel.rectTransform.sizeDelta = compact ? new Vector2(18f, 10f) : new Vector2(20f, 12f);
                rowView.CountLabel.rectTransform.anchoredPosition = compact ? new Vector2(-1f, 0f) : new Vector2(-1.5f, 0f);
            }
        }

        float GetCommandButtonHeight() => GetCommandButtonHeight(IsCompactPanel());

        float GetCommandButtonHeight(bool compact) => compact ? 51f : 55f;

        float GetStanceIconSize() => GetStanceIconSize(IsCompactPanel());

        float GetStanceIconSize(bool compact)
        {
            float buttonHeight = GetCommandButtonHeight(compact);
            return Mathf.Round(buttonHeight * (compact ? 0.76f : 0.80f));
        }

        float GetUnitCardHeight() => GetUnitCardHeight(IsCompactPanel());

        float GetUnitCardHeight(bool compact) => Mathf.Round(GetStanceIconSize(compact) * 0.80f);

        float GetCommandColumnSpacing(bool compact) => compact ? 5f : 8f;

        float GetUnitGridVerticalSpacing() => GetUnitGridVerticalSpacing(IsCompactPanel());

        float GetUnitGridVerticalSpacing(bool compact) => compact ? 4f : 5f;

        float GetUnitGridHeight() => GetUnitGridHeight(IsCompactPanel());

        float GetUnitGridHeight(bool compact) => (GetUnitCardHeight(compact) * 2f) + GetUnitGridVerticalSpacing(compact);

        Vector2 GetUnitGridCellSize(BarracksSectionView section, bool compact)
        {
            float cardSize = GetUnitCardHeight(compact);
            float horizontalSpacing = GetCommandColumnSpacing(compact);
            float availableWidth = 0f;

            if (section?.ControlsRoot != null && section.ControlsRoot.rect.width > 1f)
                availableWidth = section.ControlsRoot.rect.width;
            else if (section?.RowsRoot != null && section.RowsRoot.rect.width > 1f)
                availableWidth = section.RowsRoot.rect.width;
            else if (section?.Root != null && section.Root.rect.width > 1f)
            {
                availableWidth = section.Root.rect.width;
                var rootLayout = section.Root.GetComponent<VerticalLayoutGroup>();
                if (rootLayout != null)
                    availableWidth -= rootLayout.padding.left + rootLayout.padding.right;
            }

            float minimumWidth = (cardSize * 3f) + (horizontalSpacing * 2f);
            availableWidth = Mathf.Max(availableWidth, minimumWidth);
            float columnWidth = Mathf.Max(cardSize, Mathf.Floor((availableWidth - (horizontalSpacing * 2f)) / 3f));
            return new Vector2(columnWidth, cardSize);
        }

        float GetPortraitSize() => IsCompactPanel() ? 14f : 16f;

        static BarracksActivityIconKind ResolveActivityIconKind(BarracksActivityRow row)
        {
            if (row != null && row.IsHero)
                return BarracksActivityIconKind.Hero;

            string summary = string.Join(
                " ",
                row?.Role ?? string.Empty,
                row?.RoleLabel ?? string.Empty,
                row?.ProductionBuildingType ?? string.Empty,
                row?.DisplayName ?? string.Empty,
                row?.UnitTypeKey ?? string.Empty,
                row?.StableKey ?? string.Empty).ToLowerInvariant();

            if (summary.Contains("mage")
                || summary.Contains("wizard")
                || summary.Contains("sorcer")
                || summary.Contains("warlock")
                || summary.Contains("arcane")
                || summary.Contains("wizard_tower")
                || summary.Contains("spell"))
            {
                return BarracksActivityIconKind.Mage;
            }

            if (summary.Contains("priest")
                || summary.Contains("cleric")
                || summary.Contains("bishop")
                || summary.Contains("monk")
                || summary.Contains("healer")
                || summary.Contains("temple")
                || summary.Contains("support"))
            {
                return BarracksActivityIconKind.Priest;
            }

            if (summary.Contains("archer")
                || summary.Contains("crossbow")
                || summary.Contains("bow")
                || summary.Contains("ranged")
                || summary.Contains("archery"))
            {
                return BarracksActivityIconKind.Archer;
            }

            if (summary.Contains("spear")
                || summary.Contains("pike")
                || summary.Contains("pikeman")
                || summary.Contains("pikemen")
                || summary.Contains("polearm")
                || summary.Contains("halberd")
                || summary.Contains("lance")
                || summary.Contains("lancer")
                || summary.Contains("phalanx"))
            {
                return BarracksActivityIconKind.Spear;
            }

            if (summary.Contains("shield")
                || summary.Contains("guardian")
                || summary.Contains("paladin")
                || summary.Contains("defender"))
            {
                return BarracksActivityIconKind.Shield;
            }

            return BarracksActivityIconKind.Sword;
        }

        static string BuildActivityIconFallbackLabel(BarracksActivityIconKind iconKind) => iconKind switch
        {
            BarracksActivityIconKind.Shield => "SH",
            BarracksActivityIconKind.Sword => "SWD",
            BarracksActivityIconKind.Spear => "SPR",
            BarracksActivityIconKind.Archer => "ARC",
            BarracksActivityIconKind.Priest => "PRI",
            BarracksActivityIconKind.Mage => "MAG",
            BarracksActivityIconKind.Hero => "HR",
            _ => "TRP",
        };

        static Color ResolveActivityCardColor(BarracksActivityIconKind iconKind) => iconKind switch
        {
            BarracksActivityIconKind.Shield => new Color(0.12f, 0.21f, 0.28f, 0.98f),
            BarracksActivityIconKind.Sword => new Color(0.25f, 0.17f, 0.12f, 0.98f),
            BarracksActivityIconKind.Spear => new Color(0.28f, 0.18f, 0.08f, 0.98f),
            BarracksActivityIconKind.Archer => new Color(0.15f, 0.24f, 0.17f, 0.98f),
            BarracksActivityIconKind.Priest => new Color(0.16f, 0.24f, 0.22f, 0.98f),
            BarracksActivityIconKind.Mage => new Color(0.18f, 0.16f, 0.30f, 0.98f),
            BarracksActivityIconKind.Hero => new Color(0.33f, 0.23f, 0.08f, 0.98f),
            _ => new Color(0.14f, 0.20f, 0.27f, 0.98f),
        };

        static Color ResolveActivityIconTint(BarracksActivityIconKind iconKind) => iconKind switch
        {
            BarracksActivityIconKind.Shield => new Color(0.78f, 0.88f, 0.98f, 0.98f),
            BarracksActivityIconKind.Sword => new Color(0.98f, 0.88f, 0.74f, 0.98f),
            BarracksActivityIconKind.Spear => new Color(1.00f, 0.90f, 0.66f, 0.98f),
            BarracksActivityIconKind.Archer => new Color(0.76f, 0.92f, 0.72f, 0.98f),
            BarracksActivityIconKind.Priest => new Color(0.86f, 0.96f, 0.86f, 0.98f),
            BarracksActivityIconKind.Mage => new Color(0.88f, 0.78f, 0.98f, 0.98f),
            BarracksActivityIconKind.Hero => new Color(0.98f, 0.84f, 0.42f, 0.98f),
            _ => new Color(0.92f, 0.95f, 0.98f, 0.98f),
        };

        static Color ResolveSectionAccentColor(string barracksId, float alpha = 0.98f)
        {
            var color = BarracksActivityUtility.NormalizeBarracksId(barracksId) switch
            {
                "left" => new Color(0.69f, 0.38f, 0.92f, alpha),
                "center" => new Color(0.32f, 0.60f, 0.96f, alpha),
                "right" => new Color(0.26f, 0.84f, 0.76f, alpha),
                _ => ClassicRpgUiRuntime.WarmGold,
            };
            color.a = alpha;
            return color;
        }

        static Color ResolveSectionFillColor(string barracksId, bool canIssueOrders)
        {
            Color accent = ResolveSectionAccentColor(barracksId, canIssueOrders ? 0.22f : 0.10f);
            Color fill = Color.Lerp(ClassicRpgUiRuntime.DeepBluePanel, accent, canIssueOrders ? 0.28f : 0.18f);
            fill.a = canIssueOrders ? 0.95f : 0.84f;
            return fill;
        }

        static Color ResolveCommandButtonColor(string commandLabel, bool isActive, bool interactable)
        {
            if (!interactable)
                return new Color(0.16f, 0.18f, 0.22f, 0.72f);

            return commandLabel switch
            {
                "Attack" => isActive ? new Color(0.78f, 0.46f, 0.16f, 0.98f) : new Color(0.31f, 0.18f, 0.12f, 0.96f),
                "Defend" => isActive ? new Color(0.70f, 0.63f, 0.28f, 0.98f) : new Color(0.26f, 0.24f, 0.16f, 0.96f),
                "Retreat" => isActive ? new Color(0.24f, 0.47f, 0.70f, 0.98f) : new Color(0.14f, 0.21f, 0.31f, 0.96f),
                _ => isActive ? CommandButtonActive : CommandButtonInactive,
            };
        }

        static Sprite ResolveCommandButtonSprite(string commandLabel)
        {
            var theme = ClassicRpgUiRuntime.Theme;
            if (theme == null)
                return null;

            return commandLabel switch
            {
                "Attack" => theme.CommandAttackIcon,
                "Defend" => theme.CommandDefendIcon,
                "Retreat" => theme.CommandRetreatIcon,
                _ => null,
            };
        }

        static Color ResolveCommandSpriteTint(string commandLabel, bool isActive, bool interactable)
        {
            if (!interactable)
            {
                return commandLabel switch
                {
                    "Attack" => new Color(0.94f, 0.68f, 0.52f, 0.86f),
                    "Defend" => new Color(0.96f, 0.84f, 0.52f, 0.86f),
                    "Retreat" => new Color(0.56f, 0.86f, 0.98f, 0.86f),
                    _ => new Color(1f, 1f, 1f, 0.86f),
                };
            }

            if (!isActive)
            {
                return commandLabel switch
                {
                    "Attack" => new Color(0.98f, 0.74f, 0.56f, 0.96f),
                    "Defend" => new Color(1.00f, 0.88f, 0.56f, 0.96f),
                    "Retreat" => new Color(0.62f, 0.90f, 1.00f, 0.96f),
                    _ => new Color(1f, 1f, 1f, 0.96f),
                };
            }

            return commandLabel switch
            {
                "Attack" => new Color(1.00f, 0.72f, 0.46f, 1.00f),
                "Defend" => new Color(1.00f, 0.90f, 0.50f, 1.00f),
                "Retreat" => new Color(0.58f, 0.90f, 1.00f, 1.00f),
                _ => Color.white,
            };
        }

        static Color ResolveCommandButtonBackplateColor(string commandLabel, bool isActive, bool interactable)
        {
            if (!interactable)
                return new Color(0.10f, 0.12f, 0.15f, 0.20f);

            return commandLabel switch
            {
                "Attack" => isActive ? new Color(0.42f, 0.20f, 0.08f, 0.56f) : new Color(0.16f, 0.10f, 0.08f, 0.26f),
                "Defend" => isActive ? new Color(0.42f, 0.34f, 0.10f, 0.56f) : new Color(0.18f, 0.16f, 0.10f, 0.26f),
                "Retreat" => isActive ? new Color(0.12f, 0.30f, 0.44f, 0.56f) : new Color(0.10f, 0.16f, 0.22f, 0.26f),
                _ => isActive ? new Color(0.20f, 0.32f, 0.38f, 0.56f) : new Color(0.12f, 0.18f, 0.24f, 0.26f),
            };
        }

        static Image EnsureCommandButtonIcon(Button button)
        {
            if (button == null)
                return null;

            var icon = EnsureChildRect(button.transform, "Icon");
            Stretch(icon, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            icon.SetAsLastSibling();

            var layout = icon.gameObject.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var image = icon.gameObject.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;

            var shadow = icon.gameObject.GetComponent<Shadow>() ?? icon.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
            shadow.effectDistance = new Vector2(1f, -1f);

            var outline = icon.gameObject.GetComponent<Outline>() ?? icon.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.86f, 0.54f, 0.16f);
            outline.effectDistance = new Vector2(1f, -1f);
            return image;
        }

        static void ApplyCommandButtonArtwork(Button button, TMP_Text label, string commandLabel)
        {
            if (button == null)
                return;

            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            Sprite sprite = ResolveCommandButtonSprite(commandLabel);
            Image iconImage = EnsureCommandButtonIcon(button);
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                button.targetGraphic = image;
            }

            if (iconImage != null)
            {
                iconImage.sprite = sprite;
                iconImage.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                iconImage.gameObject.SetActive(sprite != null);
            }

            if (label != null)
            {
                bool showLabel = sprite == null;
                label.gameObject.SetActive(showLabel);
                label.raycastTarget = false;
            }
        }

        static void ApplyPanelChrome(GameObject target, Color outlineColor, Color accentColor)
        {
            if (target == null)
                return;

            var shadow = target.GetComponent<Shadow>() ?? target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.34f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var outline = target.GetComponent<Outline>() ?? target.AddComponent<Outline>();
            outline.effectColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.90f);
            outline.effectDistance = new Vector2(1f, -1f);

            var accent = EnsureChildRect(target.transform, "Accent");
            accent.transform.SetAsFirstSibling();
            accent.anchorMin = new Vector2(0f, 1f);
            accent.anchorMax = new Vector2(1f, 1f);
            accent.pivot = new Vector2(0.5f, 1f);
            accent.sizeDelta = new Vector2(0f, 3f);
            accent.anchoredPosition = Vector2.zero;
            var accentLayout = accent.gameObject.GetComponent<LayoutElement>() ?? accent.gameObject.AddComponent<LayoutElement>();
            accentLayout.ignoreLayout = true;
            var accentImage = accent.gameObject.GetComponent<Image>() ?? accent.gameObject.AddComponent<Image>();
            accentImage.color = accentColor;
            accentImage.raycastTarget = false;
        }

        static void ClearPanelChrome(GameObject target)
        {
            if (target == null)
                return;

            var shadow = target.GetComponent<Shadow>();
            if (shadow != null)
                shadow.enabled = false;

            var outline = target.GetComponent<Outline>();
            if (outline != null)
                outline.enabled = false;

            var accent = target.transform.Find("Accent");
            if (accent != null)
                accent.gameObject.SetActive(false);
        }

        static Sprite ResolveActivityIconSprite(BarracksActivityIconKind iconKind)
        {
            var theme = ClassicRpgUiRuntime.Theme;
            if (theme == null)
                return null;

            return iconKind switch
            {
                BarracksActivityIconKind.Shield => theme.ActivityShieldIcon,
                BarracksActivityIconKind.Sword => theme.ActivitySwordIcon,
                BarracksActivityIconKind.Spear => theme.ActivitySpearIcon,
                BarracksActivityIconKind.Archer => theme.ActivityArcherIcon,
                BarracksActivityIconKind.Priest => theme.ActivityPriestIcon,
                BarracksActivityIconKind.Mage => theme.ActivityMageIcon,
                BarracksActivityIconKind.Hero => theme.ActivityHeroIcon,
                _ => null,
            };
        }

        void TogglePanelCollapsed()
        {
            SetPanelCollapsed(!_panelCollapsed, immediate: false);
        }

        void SetPanelCollapsed(bool collapsed, bool immediate)
        {
            _panelCollapsed = collapsed;
            if (_collapseAnimation != null)
                StopCoroutine(_collapseAnimation);

            float targetWidth = GetTargetPanelWidth();
            if (immediate || !isActiveAndEnabled)
            {
                ApplyPanelWidth(targetWidth);
                ApplyPanelVisualState();
                return;
            }

            _collapseAnimation = StartCoroutine(AnimatePanelWidth(targetWidth));
        }

        IEnumerator AnimatePanelWidth(float targetWidth)
        {
            float startWidth = _runtimeRoot != null ? _runtimeRoot.rect.width : targetWidth;
            float duration = Mathf.Max(0.01f, CollapseAnimDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                ApplyPanelWidth(Mathf.Lerp(startWidth, targetWidth, t));
                yield return null;
            }

            ApplyPanelWidth(targetWidth);
            ApplyPanelVisualState();
            _collapseAnimation = null;
        }

        void UpdateRuntimePanelSizing(bool immediate)
        {
            if (_runtimeRoot == null)
                return;

            ApplyClusterRuntimeTuning();
            EnsureRuntimeRootHost();
            ApplyPanelWidth(ResolveExpandedWidth());
            ApplyPanelHeight(ResolvePanelHeight());
            ApplyPanelVisualState();
            RefreshResponsiveSectionLayout();
        }

        float GetTargetPanelWidth()
        {
            return ResolveExpandedWidth();
        }

        float ResolveExpandedWidth()
        {
            RectTransform canvasRect = ResolveCanvasRect();
            float canvasWidth = canvasRect != null && canvasRect.rect.width > 0f
                ? canvasRect.rect.width
                : Mathf.Max(1f, Screen.width);
            bool compact = IsCompactPanel();

            float safeAreaWidth = canvasWidth;
            if (RespectSafeArea && Screen.width > 0f)
            {
                float safeAreaPixels = Screen.safeArea.width > 0f ? Screen.safeArea.width : Screen.width;
                safeAreaWidth = safeAreaPixels * (canvasWidth / Screen.width);
            }

            float width = compact
                ? safeAreaWidth * MobileClusterWidthPercentOfSafeArea
                : canvasWidth * ClusterWidthPercentOfScreen;
            float min = compact ? MobileClusterMinWidth : ClusterMinWidth;
            float max = compact ? MobileClusterMaxWidth : ClusterMaxWidth;
            return Mathf.Clamp(width, min, max);
        }

        float ResolveCollapsedWidth()
        {
            return ResolveExpandedWidth();
        }

        void ApplyPanelWidth(float width)
        {
            if (_runtimeRoot != null)
                _runtimeRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, width));
        }

        void ApplyPanelHeight(float height)
        {
            if (_runtimeRoot != null)
                _runtimeRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, height));
        }

        float ResolvePanelHeight() => IsCompactPanel() ? MobileClusterHeight : ClusterHeight;

        bool ApplyRuntimeSafeAreaLayout(bool force = false)
        {
            if (_runtimeRoot == null)
                return false;

            EnsureRuntimeRootHost();
            Rect safeArea = Screen.safeArea;
            if (!force && safeArea == _lastSafeArea)
                return false;

            _lastSafeArea = safeArea;

            float leftInset = 0f;
            float topInset = 0f;
            float bottomInset = 0f;

            if (RespectSafeArea && IsCompactPanel() && Screen.width > 0 && Screen.height > 0)
            {
                RectTransform canvasRect = ResolveCanvasRect();
                float canvasWidth = canvasRect != null && canvasRect.rect.width > 0f
                    ? canvasRect.rect.width
                    : Screen.width;
                float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f
                    ? canvasRect.rect.height
                    : Screen.height;
                float widthScale = canvasWidth / Screen.width;
                float heightScale = canvasHeight / Screen.height;

                leftInset = safeArea.xMin * widthScale;
                topInset = (Screen.height - safeArea.yMax) * heightScale;
                bottomInset = safeArea.yMin * heightScale;
            }

            _runtimeRoot.anchoredPosition = new Vector2(leftInset + ClusterLeftInset, -(topInset + ClusterTopInset));

            return true;
        }

        void ApplyPanelVisualState()
        {
            if (_runtimeBackground != null)
            {
                _runtimeBackground.color = Color.clear;
                _runtimeBackground.enabled = false;
            }
        }

        bool IsSmallScreen() => Screen.width <= SmallScreenThreshold;

        float GetCollapseToggleWidth() => IsSmallScreen() ? MobileCollapseToggleWidth : CollapseToggleWidth;

        float GetCollapseToggleHeight() => IsSmallScreen() ? MobileCollapseToggleHeight : CollapseToggleHeight;

        float GetCollapseToggleInset() => IsSmallScreen() ? MobileCollapseToggleInset : CollapseToggleInset;

        RectTransform ResolveCanvasRect()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return transform as RectTransform;

            var rootCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return rootCanvas.GetComponent<RectTransform>() ?? canvas.GetComponent<RectTransform>() ?? (transform as RectTransform);
        }

        static Button EnsureButton(Transform parent, string name)
        {
            var existing = parent.Find(name)?.GetComponent<Button>();
            if (existing != null)
                return existing;

            var rect = EnsureChildRect(parent, name);
            var image = rect.gameObject.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            var button = rect.gameObject.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        static MLLaneSnap FindLane(MLSnapshot snapshot, int laneIndex)
        {
            if (snapshot?.lanes == null || laneIndex < 0)
                return null;

            for (int i = 0; i < snapshot.lanes.Length; i++)
            {
                var lane = snapshot.lanes[i];
                if (lane != null && lane.laneIndex == laneIndex)
                    return lane;
            }

            return null;
        }

        static string HumanizeLaneCommand(string commandState) => string.Equals(commandState, "RETREAT", StringComparison.OrdinalIgnoreCase)
            ? "Retreat"
            : string.Equals(commandState, "DEFEND", StringComparison.OrdinalIgnoreCase)
                ? "Defend"
                : "Attack";

        string GetResponsivePanelTitle() => IsCompactPanel() ? "Barracks" : "Barracks Activity";

        string GetResponsiveBarracksHeader(string barracksId)
        {
            return BarracksActivityUtility.NormalizeBarracksId(barracksId) switch
            {
                "left" => "LEFT",
                "center" => "CENTER",
                "right" => "RIGHT",
                _ => "LANE",
            };
        }

        string GetResponsiveOrdersLabel(bool canIssueOrders)
        {
            if (IsCompactPanel())
                return "Orders";

            return canIssueOrders ? "Barracks Orders" : "Orders Unavailable";
        }

        string GetResponsiveTroopsLabel(string commandState, bool canIssueOrders)
        {
            if (IsCompactPanel())
                return "Troops";

            return canIssueOrders ? $"{HumanizeLaneCommand(commandState)} Troops" : "Active Troops";
        }

        string GetActivityCountLabel(int count)
        {
            int safeCount = Mathf.Max(0, count);
            return $"x{safeCount}";
        }

        static float EstimateLaneHoldProgress(MLSnapshot snapshot, int ownerLaneIndex, MLLaneSnap ownerLane)
        {
            if (snapshot?.lanes == null || ownerLaneIndex < 0)
                return ownerLane != null ? Mathf.Clamp01(ownerLane.commandAnchorProgress) : 0f;

            var progressSamples = new List<float>(16);
            for (int laneIndex = 0; laneIndex < snapshot.lanes.Length; laneIndex++)
            {
                var lane = snapshot.lanes[laneIndex];
                if (lane?.units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.units.Length; unitIndex++)
                {
                    var unit = lane.units[unitIndex];
                    if (unit == null || unit.ownerLaneIndex != ownerLaneIndex)
                        continue;
                    if (!string.Equals(unit.spawnSourceType, "barracks_roster", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(unit.spawnSourceType, "barracks_hero", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    progressSamples.Add(Mathf.Clamp01(unit.pathIdx));
                }
            }

            if (progressSamples.Count > 0)
            {
                progressSamples.Sort();
                int middleIndex = progressSamples.Count / 2;
                if (progressSamples.Count % 2 == 1)
                    return progressSamples[middleIndex];

                return (progressSamples[middleIndex - 1] + progressSamples[middleIndex]) * 0.5f;
            }
            if (ownerLane != null)
                return Mathf.Clamp01(ownerLane.commandAnchorProgress);
            return 0f;
        }

        static bool TryEstimateLaneHoldWorldPosition(MLSnapshot snapshot, int ownerLaneIndex, MLLaneSnap ownerLane, out Vector2 worldPosition)
        {
            worldPosition = Vector2.zero;
            if (snapshot?.lanes == null || ownerLaneIndex < 0)
                return false;

            var assignedUnitIds = BuildAssignedUnitIdSet(ownerLane);
            var positionSamples = new List<MLUnit>(16);
            for (int laneIndex = 0; laneIndex < snapshot.lanes.Length; laneIndex++)
            {
                var lane = snapshot.lanes[laneIndex];
                if (lane?.units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.units.Length; unitIndex++)
                {
                    var unit = lane.units[unitIndex];
                    if (unit == null || unit.ownerLaneIndex != ownerLaneIndex)
                        continue;
                    if (!string.Equals(unit.spawnSourceType, "barracks_roster", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(unit.spawnSourceType, "barracks_hero", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (assignedUnitIds != null && assignedUnitIds.Count > 0)
                    {
                        string unitId = !string.IsNullOrWhiteSpace(unit.id) ? unit.id : unit.unitId;
                        if (string.IsNullOrWhiteSpace(unitId) || !assignedUnitIds.Contains(unitId))
                            continue;
                    }
                    if (!IsFinite(unit.gridX) || !IsFinite(unit.gridY))
                        continue;

                    positionSamples.Add(unit);
                }
            }

            if (positionSamples.Count <= 0)
                return false;

            if (TryGetLaneTacticalAnchor(ownerLane, out var anchor))
            {
                positionSamples.Sort((left, right) =>
                {
                    float leftDistance = (new Vector2(left.gridX, left.gridY) - anchor).sqrMagnitude;
                    float rightDistance = (new Vector2(right.gridX, right.gridY) - anchor).sqrMagnitude;
                    return leftDistance.CompareTo(rightDistance);
                });

                int clusterCount = Mathf.Clamp(Mathf.CeilToInt(positionSamples.Count * 0.4f), 1, positionSamples.Count);
                var centroid = Vector2.zero;
                for (int i = 0; i < clusterCount; i++)
                    centroid += new Vector2(positionSamples[i].gridX, positionSamples[i].gridY);

                worldPosition = centroid / clusterCount;
                return true;
            }

            float sumX = 0f;
            float sumY = 0f;
            for (int i = 0; i < positionSamples.Count; i++)
            {
                sumX += positionSamples[i].gridX;
                sumY += positionSamples[i].gridY;
            }
            worldPosition = new Vector2(sumX / positionSamples.Count, sumY / positionSamples.Count);
            return true;
        }

        static bool TryGetLaneTacticalAnchor(MLLaneSnap lane, out Vector2 anchor)
        {
            anchor = Vector2.zero;
            if (lane == null)
                return false;

            MLGridPos preferredAnchor = string.Equals(lane.commandState, "RETREAT", StringComparison.OrdinalIgnoreCase)
                ? lane.insideGateAnchor
                : string.Equals(lane.commandState, "ATTACK", StringComparison.OrdinalIgnoreCase)
                    ? lane.enemyCoreAnchor
                    : lane.outsideGateAnchor;
            if (preferredAnchor != null && IsFinite(preferredAnchor.x) && IsFinite(preferredAnchor.y))
            {
                anchor = new Vector2(preferredAnchor.x, preferredAnchor.y);
                return true;
            }

            if (lane.commandAnchor != null && IsFinite(lane.commandAnchor.x) && IsFinite(lane.commandAnchor.y))
            {
                anchor = new Vector2(lane.commandAnchor.x, lane.commandAnchor.y);
                return true;
            }

            return false;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static HashSet<string> BuildAssignedUnitIdSet(MLLaneSnap lane)
        {
            if (lane?.assignedUnits == null || lane.assignedUnits.Length <= 0)
                return null;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lane.assignedUnits.Length; i++)
            {
                var id = lane.assignedUnits[i];
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
            return ids;
        }
    }

    internal sealed class BarracksCommandButtonView
    {
        public BarracksCommandButtonView(Button button, TMP_Text label) { Button = button; Label = label; }
        public Button Button { get; }
        public TMP_Text Label { get; }
    }

    internal sealed class BarracksSectionView
    {
        public BarracksSectionView(
            string barracksId,
            RectTransform root,
            Image rootImage,
            TMP_Text header,
            RectTransform controlsRoot,
            BarracksCommandButtonView attackButton,
            BarracksCommandButtonView defendButton,
            BarracksCommandButtonView retreatButton,
            RectTransform rowsRoot)
        {
            BarracksId = barracksId;
            Root = root;
            RootImage = rootImage;
            Header = header;
            ControlsRoot = controlsRoot;
            AttackButton = attackButton;
            DefendButton = defendButton;
            RetreatButton = retreatButton;
            RowsRoot = rowsRoot;
        }
        public string BarracksId { get; }
        public RectTransform Root { get; }
        public Image RootImage { get; }
        public TMP_Text Header { get; }
        public RectTransform ControlsRoot { get; }
        public BarracksCommandButtonView AttackButton { get; }
        public BarracksCommandButtonView DefendButton { get; }
        public BarracksCommandButtonView RetreatButton { get; }
        public RectTransform RowsRoot { get; }
        public List<BarracksActivityRowView> RowViews { get; } = new();
    }

    internal sealed class BarracksActivityRowView
    {
        public BarracksActivityRowView(
            BarracksActivityIconKind iconKind,
            RectTransform root,
            Image rootImage,
            Button button,
            RectTransform slotRoot,
            Image iconImage,
            TMP_Text fallbackLabel,
            TMP_Text countLabel,
            RawImage portrait,
            string portraitKey)
        {
            IconKind = iconKind;
            Root = root;
            RootImage = rootImage;
            Button = button;
            SlotRoot = slotRoot;
            IconImage = iconImage;
            FallbackLabel = fallbackLabel;
            CountLabel = countLabel;
            Portrait = portrait;
            PortraitKey = portraitKey;
        }

        public BarracksActivityIconKind IconKind { get; }
        public RectTransform Root { get; }
        public Image RootImage { get; }
        public Button Button { get; }
        public RectTransform SlotRoot { get; }
        public Image IconImage { get; }
        public TMP_Text FallbackLabel { get; }
        public TMP_Text CountLabel { get; }
        public RawImage Portrait { get; }
        public string PortraitKey { get; }
        public int CurrentCount { get; set; }
        public float DownTickFlashUntil { get; set; }
    }

    internal sealed class BarracksActivityBucket
    {
        public BarracksActivityBucket(string barracksId, string displayName) { BarracksId = barracksId; DisplayName = displayName; }
        public string BarracksId { get; }
        public string DisplayName { get; }
        public List<BarracksActivityRow> Rows { get; } = new();
    }

    internal sealed class BarracksActivityRow
    {
        public string StableKey;
        public string DisplayName;
        public string UnitTypeKey;
        public string Role;
        public string RoleLabel;
        public string ProductionBuildingType;
        public bool IsHero;
        public int Count;
        public int SortIndex;
    }

    internal enum BarracksActivityIconKind
    {
        Shield,
        Sword,
        Spear,
        Archer,
        Priest,
        Mage,
        Hero,
    }

    internal sealed class BarracksActivityDisplayRow
    {
        public BarracksActivityDisplayRow(BarracksActivityIconKind iconKind, int count, int sortIndex)
        {
            IconKind = iconKind;
            Count = count;
            SortIndex = sortIndex;
        }

        public BarracksActivityIconKind IconKind { get; }
        public int Count { get; }
        public int SortIndex { get; }
    }

    internal static class BarracksActivityUtility
    {
        static readonly string[] BarracksOrder = { "left", "center", "right" };

        public static string NormalizeBarracksId(string barracksId) => string.IsNullOrWhiteSpace(barracksId) ? string.Empty : barracksId.Trim().ToLowerInvariant();

        public static string GetBarracksHeader(string barracksId) => NormalizeBarracksId(barracksId) switch
        {
            "center" => "Center Barracks",
            "left" => "Left Barracks",
            "right" => "Right Barracks",
            _ => "Barracks",
        };

        public static List<BarracksActivityBucket> CollectAllBarracksActivity(MLSnapshot snapshot, int ownerLaneIndex)
        {
            var buckets = new List<BarracksActivityBucket>(BarracksOrder.Length);
            for (int i = 0; i < BarracksOrder.Length; i++) buckets.Add(CollectBarracksActivityBucket(snapshot, ownerLaneIndex, BarracksOrder[i]));
            return buckets;
        }

        public static BarracksActivityBucket CollectBarracksActivityBucket(MLSnapshot snapshot, int ownerLaneIndex, string barracksId)
        {
            string id = NormalizeBarracksId(barracksId);
            var bucket = new BarracksActivityBucket(id, GetBarracksHeader(id));
            if (snapshot == null || ownerLaneIndex < 0 || snapshot.lanes == null) return bucket;
            var ownerLane = FindLane(snapshot, ownerLaneIndex);
            if (ownerLane == null) return bucket;
            var site = FindBarracksSite(ownerLane, id);
            var rowsByKey = new Dictionary<string, BarracksActivityRow>(StringComparer.OrdinalIgnoreCase);
            var assignedUnitIds = BuildAssignedUnitIdSet(ownerLane);

            for (int i = 0; i < snapshot.lanes.Length; i++)
            {
                var lane = snapshot.lanes[i];
                if (lane == null) continue;
                CollectUnits(lane.units, ownerLaneIndex, id, site, ownerLane, assignedUnitIds, rowsByKey);
            }

            var rows = new List<BarracksActivityRow>(rowsByKey.Values);
            rows.Sort((a, b) =>
            {
                int sortCompare = a.SortIndex.CompareTo(b.SortIndex);
                if (sortCompare != 0) return sortCompare;
                int nameCompare = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                return nameCompare != 0 ? nameCompare : string.Compare(a.StableKey, b.StableKey, StringComparison.OrdinalIgnoreCase);
            });
            bucket.Rows.AddRange(rows);
            return bucket;
        }

        public static int CountActiveUnitsForBarracks(MLSnapshot snapshot, int ownerLaneIndex, string barracksId)
        {
            var bucket = CollectBarracksActivityBucket(snapshot, ownerLaneIndex, barracksId);
            int total = 0;
            for (int i = 0; i < bucket.Rows.Count; i++) total += Mathf.Max(0, bucket.Rows[i].Count);
            return total;
        }

        public static int CountActiveUnitsForRosterEntry(MLSnapshot snapshot, int ownerLaneIndex, string barracksId, MLBarracksRosterEntry entry)
        {
            if (entry == null) return 0;
            var bucket = CollectBarracksActivityBucket(snapshot, ownerLaneIndex, barracksId);
            string stableKey = $"roster:{entry.rosterKey}";
            for (int i = 0; i < bucket.Rows.Count; i++) if (string.Equals(bucket.Rows[i].StableKey, stableKey, StringComparison.OrdinalIgnoreCase)) return bucket.Rows[i].Count;
            return 0;
        }

        public static string BuildActivityLead(MLSnapshot snapshot, int ownerLaneIndex, string barracksId, string emptyText)
        {
            var bucket = CollectBarracksActivityBucket(snapshot, ownerLaneIndex, barracksId);
            if (bucket.Rows.Count == 0) return emptyText;
            var parts = new List<string>(bucket.Rows.Count);
            for (int i = 0; i < bucket.Rows.Count; i++) parts.Add($"{bucket.Rows[i].DisplayName} x{bucket.Rows[i].Count}");
            return string.Join("   |   ", parts);
        }

        static void CollectUnits(MLUnit[] units, int ownerLaneIndex, string barracksId, MLBarracksSite site, MLLaneSnap ownerLane, HashSet<string> assignedUnitIds, Dictionary<string, BarracksActivityRow> rowsByKey)
        {
            if (units == null) return;
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                int unitOwnerLaneIndex = unit != null && unit.ownerLaneIndex >= 0
                    ? unit.ownerLaneIndex
                    : unit != null ? unit.sourceLaneIndex : -1;
                if (unit == null || unitOwnerLaneIndex != ownerLaneIndex) continue;
                if (!string.Equals(unit.spawnSourceType, "barracks_roster", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(unit.spawnSourceType, "barracks_hero", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (assignedUnitIds != null && assignedUnitIds.Count > 0)
                {
                    string unitId = !string.IsNullOrWhiteSpace(unit.id) ? unit.id : unit.unitId;
                    if (string.IsNullOrWhiteSpace(unitId) || !assignedUnitIds.Contains(unitId))
                        continue;
                }
                if (!string.Equals(NormalizeBarracksId(ResolveSourceBarracksKey(unit)), barracksId, StringComparison.OrdinalIgnoreCase)) continue;
                var row = ResolveRow(unit, site, ownerLane);
                if (row == null) continue;
                if (rowsByKey.TryGetValue(row.StableKey, out var existing)) existing.Count += 1;
                else { row.Count = 1; rowsByKey[row.StableKey] = row; }
            }
        }

        static HashSet<string> BuildAssignedUnitIdSet(MLLaneSnap lane)
        {
            if (lane?.assignedUnits == null || lane.assignedUnits.Length <= 0)
                return null;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lane.assignedUnits.Length; i++)
            {
                var id = lane.assignedUnits[i];
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
            return ids;
        }

        static BarracksActivityRow ResolveRow(MLUnit unit, MLBarracksSite site, MLLaneSnap ownerLane)
        {
            if (unit == null) return null;
            if (unit.isHero && !string.IsNullOrWhiteSpace(unit.heroKey))
            {
                var hero = FindHero(ownerLane, unit.heroKey);
                return new BarracksActivityRow
                {
                    StableKey = $"hero:{unit.heroKey}",
                    DisplayName = hero != null ? hero.displayName : Humanize(unit.heroKey),
                    UnitTypeKey = ResolveCatalogUnitKey(
                        unit.archetypeKey,
                        unit.presentationKey,
                        unit.catalogUnitKey,
                        !string.IsNullOrWhiteSpace(unit.type) ? unit.type : (hero != null ? hero.unitTypeKey : null),
                        unit.skinKey),
                    Role = hero != null ? hero.role : null,
                    RoleLabel = hero != null ? hero.roleLabel : null,
                    ProductionBuildingType = hero != null ? hero.summonSourceBuildingType : null,
                    IsHero = true,
                    SortIndex = hero != null ? hero.sortIndex : int.MaxValue - 1,
                };
            }

            var roster = FindRosterEntry(site, unit);
            return new BarracksActivityRow
            {
                StableKey = roster != null && !string.IsNullOrWhiteSpace(roster.rosterKey) ? $"roster:{roster.rosterKey}" : $"unit:{unit.type}",
                DisplayName = roster != null
                    ? roster.displayName
                    : FortUnitIdentityCatalog.ResolveDisplayName(unit.archetypeKey, unit.presentationKey, unit.catalogUnitKey, unit.skinKey, Humanize(unit.type)),
                UnitTypeKey = ResolveCatalogUnitKey(
                    unit.archetypeKey,
                    unit.presentationKey,
                    unit.catalogUnitKey,
                    !string.IsNullOrWhiteSpace(unit.type) ? unit.type : (roster != null ? roster.unitTypeKey : null),
                    unit.skinKey),
                Role = roster != null ? roster.role : null,
                RoleLabel = roster != null ? roster.roleLabel : null,
                ProductionBuildingType = roster != null ? roster.productionBuildingType : null,
                IsHero = false,
                SortIndex = roster != null ? roster.sortIndex : int.MaxValue,
            };
        }

        static MLLaneSnap FindLane(MLSnapshot snapshot, int laneIndex)
        {
            if (snapshot?.lanes == null) return null;
            for (int i = 0; i < snapshot.lanes.Length; i++) if (snapshot.lanes[i] != null && snapshot.lanes[i].laneIndex == laneIndex) return snapshot.lanes[i];
            return null;
        }

        internal static MLBarracksSite FindBarracksSite(MLLaneSnap lane, string barracksId)
        {
            var sites = lane != null ? lane.barracksSites : null;
            if (sites == null) return null;
            for (int i = 0; i < sites.Length; i++) if (sites[i] != null && string.Equals(NormalizeBarracksId(sites[i].barracksId), barracksId, StringComparison.OrdinalIgnoreCase)) return sites[i];
            return null;
        }

        static MLHeroRosterEntry FindHero(MLLaneSnap lane, string heroKey)
        {
            var heroes = lane != null ? lane.heroRoster : null;
            if (heroes == null || string.IsNullOrWhiteSpace(heroKey)) return null;
            for (int i = 0; i < heroes.Length; i++) if (heroes[i] != null && string.Equals(heroes[i].heroKey, heroKey, StringComparison.OrdinalIgnoreCase)) return heroes[i];
            return null;
        }

        static MLBarracksRosterEntry FindRosterEntry(MLBarracksSite site, MLUnit unit)
        {
            var roster = site != null ? site.roster : null;
            if (roster == null || unit == null) return null;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(unit.archetypeKey) && string.Equals(entry.archetypeKey, unit.archetypeKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
                if (!string.IsNullOrWhiteSpace(unit.catalogUnitKey) && string.Equals(entry.catalogUnitKey, unit.catalogUnitKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
                if (!string.IsNullOrWhiteSpace(unit.type) && string.Equals(entry.unitTypeKey, unit.type, StringComparison.OrdinalIgnoreCase))
                    return entry;
                if (!string.IsNullOrWhiteSpace(unit.skinKey) && string.Equals(entry.skinKey, unit.skinKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        static string ResolveSourceBarracksKey(MLUnit unit)
        {
            if (!string.IsNullOrWhiteSpace(unit?.sourceBarracksKey)) return unit.sourceBarracksKey;
            return unit?.sourceBarracksId;
        }

        static string ResolveCatalogUnitKey(string archetypeKey, string presentationKey, string catalogUnitKey, string unitTypeKey, string skinKey)
        {
            if (!string.IsNullOrWhiteSpace(catalogUnitKey))
                return catalogUnitKey.Trim();

            return FortUnitIdentityCatalog.ResolveCatalogUnitKey(archetypeKey, presentationKey, unitTypeKey, skinKey);
        }

        static string Humanize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unknown Unit";
            string normalized = raw.Trim().Replace("_", " ").Replace("-", " ");
            var parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Length <= 1 ? parts[i].ToUpperInvariant() : char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant();
            return string.Join(" ", parts);
        }
    }
}
