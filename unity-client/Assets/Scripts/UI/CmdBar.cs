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

        [SerializeField] UnitPortraitCamera PortraitCam;
        [SerializeField] UnitPrefabRegistry PortraitRegistry;

        const float MissingLoadoutErrorDelaySeconds = 1.5f;
        static readonly string[] BarracksOrder = { "center", "left", "right" };
        static readonly Color CommandButtonActive = new(0.18f, 0.55f, 0.46f, 0.98f);
        static readonly Color CommandButtonInactive = new(0.14f, 0.20f, 0.27f, 0.98f);
        static readonly Color CommandButtonDisabled = new(0.18f, 0.18f, 0.20f, 0.72f);

        readonly Dictionary<string, Texture2D> _portraitCache = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _capturePending = new(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> _captureQueue = new();
        readonly List<RawImage> _buttonPortraits = new();
        readonly Dictionary<string, BarracksSectionView> _sections = new(StringComparer.OrdinalIgnoreCase);

        string[] _loadoutKeys = Array.Empty<string>();
        RectTransform _contentRoot;
        TMP_Text _emptyStateLabel;
        RenderTexture _runtimePortraitTexture;
        GameObject _runtimePortraitRoot;
        bool _isCapturingPortraits;
        bool _loadoutMissingLogged;
        string _lastActivitySignature;

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
            RefreshButtonPortraits();
            RefreshActivityPanel(false);
            RefreshActivityPortraits();
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
            _loadoutMissingLogged = true;
            Debug.LogError($"[CmdBar] No authoritative match loadout arrived for '{name}' in scene '{gameObject.scene.name}'.");
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

            var runtimeRoot = EnsureChildRect(transform, "BarracksActivityRoot");
            Stretch(runtimeRoot, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            var background = runtimeRoot.gameObject.GetComponent<Image>() ?? runtimeRoot.gameObject.AddComponent<Image>();
            background.color = new Color(0.08f, 0.12f, 0.16f, 0.92f);

            var title = EnsureText(runtimeRoot, "Title", "Barracks Activity", 20f, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.Left;
            Stretch(title.rectTransform, new Vector2(10f, -38f), new Vector2(-10f, -8f));
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);

            var viewport = EnsureChildRect(runtimeRoot, "Viewport");
            var viewportImage = viewport.gameObject.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            if (viewport.gameObject.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10f, 10f);
            viewport.offsetMax = new Vector2(-10f, -44f);

            _contentRoot = EnsureChildRect(viewport, "Content");
            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(1f, 1f);
            _contentRoot.pivot = new Vector2(0.5f, 1f);

            var layout = _contentRoot.gameObject.GetComponent<VerticalLayoutGroup>() ?? _contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var contentFitter = _contentRoot.gameObject.GetComponent<ContentSizeFitter>() ?? _contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = runtimeRoot.gameObject.GetComponent<ScrollRect>() ?? runtimeRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.content = _contentRoot;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            _emptyStateLabel = EnsureText(_contentRoot, "EmptyState", "Waiting for barracks activity...", 13f, FontStyles.Italic);
            _emptyStateLabel.alignment = TextAlignmentOptions.Left;
            _emptyStateLabel.color = new Color(0.78f, 0.84f, 0.92f, 0.78f);

            for (int i = 0; i < BarracksOrder.Length; i++) EnsureSection(BarracksOrder[i]);
        }

        BarracksSectionView EnsureSection(string barracksId)
        {
            string id = BarracksActivityUtility.NormalizeBarracksId(barracksId);
            if (_sections.TryGetValue(id, out var existing)) return existing;

            var root = EnsureChildRect(_contentRoot, $"{id}_section");
            var image = root.gameObject.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            image.color = new Color(0.10f, 0.16f, 0.22f, 0.90f);
            var layout = root.gameObject.GetComponent<VerticalLayoutGroup>() ?? root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var rootFitter = root.gameObject.GetComponent<ContentSizeFitter>() ?? root.gameObject.AddComponent<ContentSizeFitter>();
            rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = EnsureText(root, "Header", BarracksActivityUtility.GetBarracksHeader(id), 16f, FontStyles.Bold);
            header.alignment = TextAlignmentOptions.Left;

            var ordersLabel = EnsureText(root, "OrdersLabel", "Formation Orders", 11f, FontStyles.Normal);
            ordersLabel.alignment = TextAlignmentOptions.Left;
            ordersLabel.color = new Color(0.78f, 0.84f, 0.92f, 0.78f);

            var controls = EnsureChildRect(root, "Controls");
            var controlsLayout = controls.gameObject.GetComponent<HorizontalLayoutGroup>() ?? controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            controlsLayout.spacing = 6f;
            controlsLayout.childAlignment = TextAnchor.MiddleLeft;
            controlsLayout.childForceExpandWidth = true;
            controlsLayout.childForceExpandHeight = false;
            controlsLayout.childControlWidth = true;
            controlsLayout.childControlHeight = true;
            var controlsFitter = controls.gameObject.GetComponent<ContentSizeFitter>() ?? controls.gameObject.AddComponent<ContentSizeFitter>();
            controlsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var attackButton = EnsureCommandButton(controls, "AttackButton", "Attack");
            var defendButton = EnsureCommandButton(controls, "DefendButton", "Defend");
            var retreatButton = EnsureCommandButton(controls, "RetreatButton", "Retreat");

            var troopsLabel = EnsureText(root, "TroopsLabel", "Active Troops", 11f, FontStyles.Normal);
            troopsLabel.alignment = TextAlignmentOptions.Left;
            troopsLabel.color = new Color(0.78f, 0.84f, 0.92f, 0.78f);

            var rows = EnsureChildRect(root, "Rows");
            var rowsLayout = rows.gameObject.GetComponent<VerticalLayoutGroup>() ?? rows.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 4f;
            rowsLayout.childAlignment = TextAnchor.UpperLeft;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            var rowsFitter = rows.gameObject.GetComponent<ContentSizeFitter>() ?? rows.gameObject.AddComponent<ContentSizeFitter>();
            rowsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var section = new BarracksSectionView(id, root, header, ordersLabel, attackButton, defendButton, retreatButton, troopsLabel, rows);
            _sections[id] = section;
            return section;
        }

        void RefreshActivityPanel(bool force)
        {
            if (_contentRoot == null) return;

            var applier = SnapshotApplier.Instance;
            var snapshot = applier != null ? applier.LatestML : null;
            int ownerLaneIndex = applier != null ? applier.MyLaneIndex : -1;
            var ownerLane = FindLane(snapshot, ownerLaneIndex);
            var buckets = BarracksActivityUtility.CollectAllBarracksActivity(snapshot, ownerLaneIndex);
            bool anyRows = false;
            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var section = EnsureSection(bucket.BarracksId);
                section.Header.text = bucket.DisplayName;
                UpdateSectionControls(section, ownerLaneIndex, ownerLane);
                anyRows |= bucket.Rows.Count > 0;
            }

            string signature = BuildActivitySignature(snapshot, ownerLaneIndex, ownerLane, buckets);
            if (!force && string.Equals(signature, _lastActivitySignature, StringComparison.Ordinal))
            {
                if (_emptyStateLabel == null) return;
                if (snapshot == null || ownerLaneIndex < 0)
                {
                    _emptyStateLabel.text = "Waiting for match snapshot...";
                    _emptyStateLabel.gameObject.SetActive(true);
                }
                else if (!anyRows)
                {
                    _emptyStateLabel.text = "No active barracks deployments.";
                    _emptyStateLabel.gameObject.SetActive(true);
                }
                else
                {
                    _emptyStateLabel.gameObject.SetActive(false);
                }
                return;
            }

            _lastActivitySignature = signature;
            for (int i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var section = EnsureSection(bucket.BarracksId);
                SetSectionRows(section, bucket, ownerLaneIndex);
            }

            if (_emptyStateLabel == null) return;
            if (snapshot == null || ownerLaneIndex < 0)
            {
                _emptyStateLabel.text = "Waiting for match snapshot...";
                _emptyStateLabel.gameObject.SetActive(true);
            }
            else if (!anyRows)
            {
                _emptyStateLabel.text = "No active barracks deployments.";
                _emptyStateLabel.gameObject.SetActive(true);
            }
            else
            {
                _emptyStateLabel.gameObject.SetActive(false);
            }
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
            for (int i = 0; i < section.RowViews.Count; i++)
                if (section.RowViews[i]?.Root != null) Destroy(section.RowViews[i].Root.gameObject);
            section.RowViews.Clear();

            for (int i = 0; i < bucket.Rows.Count; i++)
                section.RowViews.Add(CreateActivityRow(section.RowsRoot, bucket.Rows[i], ownerLaneIndex, bucket.BarracksId));
        }

        void UpdateSectionControls(BarracksSectionView section, int ownerLaneIndex, MLLaneSnap ownerLane)
        {
            if (section == null) return;

            bool canIssueOrders = ownerLane != null && ownerLaneIndex >= 0 && !ownerLane.eliminated;
            string commandState = ownerLane != null && !string.IsNullOrWhiteSpace(ownerLane.commandState)
                ? ownerLane.commandState.Trim().ToUpperInvariant()
                : "ATTACK";
            var latestSnapshot = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.LatestML : null;
            float defendProgress = EstimateLaneHoldProgress(latestSnapshot, ownerLaneIndex, ownerLane);
            bool hasDefendWorldAnchor = TryEstimateLaneHoldWorldPosition(latestSnapshot, ownerLaneIndex, ownerLane, out var defendWorldAnchor);

            if (section.OrdersLabel != null)
                section.OrdersLabel.text = canIssueOrders ? "Lane Formation Orders" : "Formation Orders Unavailable";
            if (section.TroopsLabel != null)
                section.TroopsLabel.text = canIssueOrders ? $"{HumanizeLaneCommand(commandState)} Formation Troops" : "Active Troops";

            ConfigureCommandButton(section.AttackButton, "Attack", commandState == "ATTACK", canIssueOrders, ActionSender.SetLaneAttack);
            ConfigureCommandButton(
                section.DefendButton,
                "Defend",
                commandState == "DEFEND",
                canIssueOrders,
                () =>
                {
                    if (hasDefendWorldAnchor)
                    {
                        ActionSender.SetLaneDefendAt(defendWorldAnchor.x, defendWorldAnchor.y);
                        return;
                    }

                    ActionSender.SetLaneDefendProgress(defendProgress);
                });
            ConfigureCommandButton(section.RetreatButton, "Retreat", commandState == "RETREAT", canIssueOrders, () => ActionSender.SetLaneRetreatProgress(0f));
        }

        void ConfigureCommandButton(BarracksCommandButtonView buttonView, string label, bool isActive, bool interactable, Action onClick)
        {
            if (buttonView?.Button == null) return;

            if (buttonView.Label != null)
                buttonView.Label.text = label;

            buttonView.Button.onClick.RemoveAllListeners();
            if (interactable && onClick != null)
                buttonView.Button.onClick.AddListener(() => onClick());
            buttonView.Button.interactable = interactable;

            Color baseColor = interactable
                ? (isActive ? CommandButtonActive : CommandButtonInactive)
                : CommandButtonDisabled;
            var image = buttonView.Button.targetGraphic as Image;
            if (image != null)
                image.color = baseColor;

            var colors = buttonView.Button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = interactable ? Color.Lerp(baseColor, Color.white, 0.10f) : baseColor;
            colors.pressedColor = interactable ? Color.Lerp(baseColor, Color.black, 0.14f) : baseColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = CommandButtonDisabled;
            buttonView.Button.colors = colors;
        }

        BarracksActivityRowView CreateActivityRow(Transform parent, BarracksActivityRow row, int ownerLaneIndex, string barracksId)
        {
            var root = EnsureChildRect(parent, $"{row.StableKey}_row");
            var image = root.gameObject.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            var button = root.gameObject.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color(0.14f, 0.20f, 0.27f, 0.98f);
            colors.highlightedColor = new Color(0.20f, 0.29f, 0.38f, 1f);
            colors.pressedColor = new Color(0.10f, 0.16f, 0.22f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.targetGraphic = image;
            image.color = colors.normalColor;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => FortressSelectionController.OpenBarracksSite(ownerLaneIndex, barracksId));

            var layoutElement = root.gameObject.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = 48f;
            layoutElement.preferredHeight = 48f;

            var layout = root.gameObject.GetComponent<HorizontalLayoutGroup>() ?? root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 10, 6, 6);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var portraitFrame = EnsureChildRect(root, "PortraitFrame");
            var portraitFrameImage = portraitFrame.gameObject.GetComponent<Image>() ?? portraitFrame.gameObject.AddComponent<Image>();
            portraitFrameImage.color = new Color(0.08f, 0.12f, 0.18f, 1f);
            if (portraitFrame.gameObject.GetComponent<Mask>() == null) portraitFrame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portraitLayout = portraitFrame.gameObject.GetComponent<LayoutElement>() ?? portraitFrame.gameObject.AddComponent<LayoutElement>();
            portraitLayout.minWidth = 36f;
            portraitLayout.preferredWidth = 36f;
            portraitLayout.minHeight = 36f;
            portraitLayout.preferredHeight = 36f;

            var portrait = EnsureRawPortrait(portraitFrame);
            portrait.texture = null;
            portrait.color = new Color(1f, 1f, 1f, 0f);

            var textRoot = EnsureChildRect(root, "Text");
            var textLayout = textRoot.gameObject.GetComponent<LayoutElement>() ?? textRoot.gameObject.AddComponent<LayoutElement>();
            textLayout.flexibleWidth = 1f;
            textLayout.minWidth = 80f;
            var textGroup = textRoot.gameObject.GetComponent<VerticalLayoutGroup>() ?? textRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            textGroup.spacing = 0f;
            textGroup.childAlignment = TextAnchor.MiddleLeft;
            textGroup.childForceExpandWidth = true;
            textGroup.childForceExpandHeight = false;
            textGroup.childControlWidth = true;
            textGroup.childControlHeight = true;

            var name = EnsureText(textRoot, "Name", row.DisplayName, 14f, FontStyles.Bold);
            name.alignment = TextAlignmentOptions.Left;
            name.color = new Color(0.96f, 0.98f, 1f, 1f);
            var subtitle = EnsureText(textRoot, "Subtitle", row.IsHero ? "Hero in formation" : "Active in formation", 11f, FontStyles.Normal);
            subtitle.alignment = TextAlignmentOptions.Left;
            subtitle.color = new Color(0.78f, 0.84f, 0.92f, 0.78f);

            var count = EnsureText(root, "Count", $"x{Mathf.Max(0, row.Count)}", 16f, FontStyles.Bold);
            count.alignment = TextAlignmentOptions.Right;
            count.color = new Color(0.96f, 0.92f, 0.74f, 1f);
            var countLayout = count.gameObject.GetComponent<LayoutElement>() ?? count.gameObject.AddComponent<LayoutElement>();
            countLayout.minWidth = 44f;
            countLayout.preferredWidth = 56f;

            return new BarracksActivityRowView(root, portrait, row.UnitTypeKey);
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
                existing.fontSize = fontSize;
                existing.fontStyle = fontStyle;
                if (TMP_Settings.defaultFontAsset != null && existing.font == null) existing.font = TMP_Settings.defaultFontAsset;
                return existing;
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        static BarracksCommandButtonView EnsureCommandButton(Transform parent, string name, string label)
        {
            var existingButton = parent.Find(name)?.GetComponent<Button>();
            if (existingButton != null)
            {
                var existingLabel = existingButton.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (existingLabel != null)
                    existingLabel.text = label;
                return new BarracksCommandButtonView(existingButton, existingLabel);
            }

            var root = EnsureChildRect(parent, name);
            var image = root.gameObject.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            image.color = CommandButtonInactive;
            var button = root.gameObject.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = image;

            var layout = root.gameObject.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;
            layout.minWidth = 52f;
            layout.flexibleWidth = 1f;

            var text = EnsureText(root, "Label", label, 11f, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.96f, 0.98f, 1f, 1f);
            Stretch(text.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f));

            return new BarracksCommandButtonView(button, text);
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

            if (ownerLane?.formationAnchor != null && IsFinite(ownerLane.formationAnchor.x) && IsFinite(ownerLane.formationAnchor.y))
            {
                var anchor = new Vector2(ownerLane.formationAnchor.x, ownerLane.formationAnchor.y);
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
            TMP_Text header,
            TMP_Text ordersLabel,
            BarracksCommandButtonView attackButton,
            BarracksCommandButtonView defendButton,
            BarracksCommandButtonView retreatButton,
            TMP_Text troopsLabel,
            RectTransform rowsRoot)
        {
            BarracksId = barracksId;
            Root = root;
            Header = header;
            OrdersLabel = ordersLabel;
            AttackButton = attackButton;
            DefendButton = defendButton;
            RetreatButton = retreatButton;
            TroopsLabel = troopsLabel;
            RowsRoot = rowsRoot;
        }
        public string BarracksId { get; }
        public RectTransform Root { get; }
        public TMP_Text Header { get; }
        public TMP_Text OrdersLabel { get; }
        public BarracksCommandButtonView AttackButton { get; }
        public BarracksCommandButtonView DefendButton { get; }
        public BarracksCommandButtonView RetreatButton { get; }
        public TMP_Text TroopsLabel { get; }
        public RectTransform RowsRoot { get; }
        public List<BarracksActivityRowView> RowViews { get; } = new();
    }

    internal sealed class BarracksActivityRowView
    {
        public BarracksActivityRowView(RectTransform root, RawImage portrait, string portraitKey) { Root = root; Portrait = portrait; PortraitKey = portraitKey; }
        public RectTransform Root { get; }
        public RawImage Portrait { get; }
        public string PortraitKey { get; }
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
        public bool IsHero;
        public int Count;
        public int SortIndex;
    }

    internal static class BarracksActivityUtility
    {
        static readonly string[] BarracksOrder = { "center", "left", "right" };

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

        static MLBarracksSite FindBarracksSite(MLLaneSnap lane, string barracksId)
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
