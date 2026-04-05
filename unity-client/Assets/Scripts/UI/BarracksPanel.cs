using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CastleDefender.Game;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class BarracksPanel : MonoBehaviour
    {
        const string RuntimePanelRootName = "RuntimeBuildingOverviewPanel";
        const string LegacyRuntimePanelRootName = "RuntimeBarracksPanel";
        const int FocusedBarracksTargetVisibleCards = 4;
        const float FocusedBarracksCardSpacing = 12f;
        const float FocusedBarracksMinCardWidth = 228f;
        const float FocusedBarracksMaxCardWidth = 252f;
        const float FocusedBarracksMinCardHeight = 392f;
        const float FocusedBarracksMaxCardHeight = 432f;
        const float FocusedBarracksRailFooterHeight = 28f;
        const float FocusedBarracksRailFooterGap = 8f;
        const float FocusedBarracksScrollbarHeight = 14f;
        const float FocusedBarracksRailButtonWidth = 34f;
        const float PanelBaseWidth = 1220f;
        const float PanelBaseHeight = 860f;
        const float PanelViewportWidthRatio = 0.94f;
        const float PanelViewportHeightRatio = 0.95f;
        const float MinimumPanelFontSize = 12f;
        const string MilitiaRosterKey = "militia";
        static readonly Color ObsidianRootColor = new(0.04f, 0.04f, 0.05f, 0.99f);
        static readonly Color ObsidianSurfaceColor = new(0.07f, 0.07f, 0.09f, 0.98f);
        static readonly Color ObsidianElevatedColor = new(0.12f, 0.12f, 0.14f, 0.98f);
        static readonly Color GunmetalColor = new(0.20f, 0.22f, 0.26f, 0.98f);
        static readonly Color GunmetalSoftColor = new(0.15f, 0.16f, 0.19f, 0.96f);
        static readonly Color SilverAccentColor = new(0.78f, 0.81f, 0.86f, 0.98f);
        static readonly Color SilverTextColor = new(0.89f, 0.91f, 0.94f, 0.98f);
        static readonly Color MutedSilverTextColor = new(0.74f, 0.77f, 0.82f, 0.96f);
        static readonly Color GoldAccentColor = new(0.95f, 0.79f, 0.42f, 0.98f);
        static readonly Color GoldSurfaceColor = new(0.33f, 0.24f, 0.10f, 0.98f);
        static readonly Color GoldSurfaceBrightColor = new(0.44f, 0.33f, 0.14f, 0.98f);
        static readonly Color GoldTextColor = new(0.98f, 0.92f, 0.76f, 1f);
        static readonly Color DisabledSurfaceColor = new(0.22f, 0.22f, 0.24f, 0.88f);
        static readonly string[] TownCorePreferredBuildingOrder =
        {
            "blacksmith",
            "archery_tower",
            "market",
            "lumber_mill",
            "stable",
            "wizard_tower",
            "temple",
            "library",
            "workshop",
            "wall",
        };

        public GameObject PanelBarracks;
        public TMP_Text TxtTitle;
        public TMP_Text TxtBenefits;
        public TMP_Text TxtCost;
        public TMP_Text TxtAffordance;
        public Button BtnConfirm;
        public Button BtnCancel;

        RectTransform _contentRoot;
        ScrollRect _scrollRect;
        Scrollbar _verticalScrollbar;
        float _runtimeContentNormalizedPosition = 1f;
        bool _usingMinimalFocusedHeader;
        string _lastContentSignature;
        int _lastHeaderTick = -1;
        bool _initialized;
        bool _networkHooksRegistered;
        string _statusMessage;
        string _pendingBarracksBuildId;
        string _pendingBarracksSellKey;
        Coroutine _scaleCoroutine;
        string _focusedBarracksId;
        float _focusedBarracksRailNormalizedPosition;
        string _focusedPadId;
        float _focusedPadUntil;
        readonly HashSet<string> _missingBarracksSiteLogs = new();
        readonly HashSet<string> _missingPortraitLogs = new();
        readonly HashSet<string> _missingUnitCardStatLogs = new();
        float _lastViewportWidth = -1f;
        float _lastViewportHeight = -1f;
        RectTransform _progressionViewerHost;
        LoadoutPhaseManager _progressionViewer;
        string _guidedUnlockPadId;
        string _guidedUnlockUnitKey;
        string _guidedUnlockUnitName;
        string _guidedUnlockBuildingType;
        string _guidedUnlockBuildingName;
        string _guidedUnlockBarracksId;
        int _guidedUnlockRequiredTier;

        enum GuidedPadAction
        {
            None,
            Build,
            Upgrade,
            Explain,
        }

        sealed class PanelRowPillData
        {
            public string Text;
            public Color BackgroundColor;
            public Color TextColor;
        }

        sealed class PanelRowStatData
        {
            public string Label;
            public string Value;
            public Color BackgroundColor;
            public Color LabelColor;
            public Color ValueColor;
        }

        sealed class PanelRowActionData
        {
            public string ObjectName;
            public string Label;
            public UnityEngine.Events.UnityAction Action;
            public bool Interactable;
            public bool Highlighted;
        }

        sealed class PanelRowTemplateData
        {
            public string ObjectName;
            public string Eyebrow;
            public string Title;
            public string StatusText;
            public string Description;
            public Color BackgroundColor;
            public Color AccentColor;
            public Color StatusColor;
            public bool Highlighted;
            public float MinHeight;
            public readonly List<PanelRowPillData> Pills = new();
            public readonly List<PanelRowStatData> Stats = new();
            public PanelRowActionData PrimaryAction;
            public PanelRowActionData SecondaryAction;
        }

        void Start()
        {
            EnsureInitialized();
            HideImmediate();
        }

        void OnDestroy()
        {
            DestroyProgressionViewer();
            UnregisterNetworkCallbacks();
        }

        void Update()
        {
            if (PanelBarracks == null || !PanelBarracks.activeSelf)
                return;

            if (_progressionViewer != null && string.IsNullOrWhiteSpace(_focusedBarracksId) && string.IsNullOrWhiteSpace(_focusedPadId))
                return;

            RefreshHeader();
            RefreshContentIfNeeded();
        }

        public void Show()
        {
            DestroyProgressionViewer();
            ClearGuidedUnlockContext();
            _focusedBarracksId = null;
            _focusedPadId = null;
            _focusedPadUntil = 0f;
            _statusMessage = null;
            Debug.Log("[BuildingOverviewTrace][ClientPanel] mode='overview'");
            ShowInternal();
        }

        void ShowInternal()
        {
            EnsureInitialized();
            _usingMinimalFocusedHeader = UseMinimalFocusedHeader();
            EnsureRuntimePanelChrome();
            EnsureRuntimeContentRoot(forceReconfigure: true);
            RefreshHeader(force: true);
            OpenPanel();
            Canvas.ForceUpdateCanvases();
            RefreshContent(force: true);
            ResetRuntimeContentScrollPosition();
        }

        public void ShowForPad(string padId)
        {
            DestroyProgressionViewer();
            ClearGuidedUnlockContext();
            _focusedBarracksId = null;
            if (!string.IsNullOrWhiteSpace(padId))
            {
                _focusedPadId = padId;
                _focusedPadUntil = Time.unscaledTime + 3f;
                Debug.Log(
                    $"[BarracksTrace][ClientPanel] mode='pad' padId='{_focusedPadId}'");
                int laneIndex = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
                var pad = laneIndex >= 0 ? SnapshotApplier.Instance?.GetFortressPad(laneIndex, padId) : null;
                if (pad != null)
                    _statusMessage = $"{pad.buildingName}: {HumanizeBuildState(pad.buildState)}";
            }

            ShowInternal();
        }

        public void ShowForBarracks(string barracksId)
        {
            DestroyProgressionViewer();
            ClearGuidedUnlockContext();
            if (!string.IsNullOrWhiteSpace(barracksId))
            {
                _focusedBarracksId = NormalizeBarracksId(barracksId);
                _focusedBarracksRailNormalizedPosition = 0f;
                _focusedPadId = null;
                _focusedPadUntil = 0f;
                Debug.Log(
                    $"[BarracksTrace][ClientPanel] mode='barracks' barracksId='{_focusedBarracksId}'");

                int laneIndex = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
                var site = laneIndex >= 0 ? SnapshotApplier.Instance?.GetBarracksSite(laneIndex, _focusedBarracksId) : null;
                _statusMessage = site == null
                    ? $"{HumanizeBarracksId(_focusedBarracksId)} selection failed: barracks snapshot data is missing."
                    : null;
            }

            ShowInternal();
        }

        public void Show(int _currentLevel, float _gold, float _income) => Show();

        public bool IsShowingFocusedBarracks(string barracksId)
        {
            return PanelBarracks != null
                && PanelBarracks.activeInHierarchy
                && IsBarracksFocused(barracksId);
        }

        public bool IsShowingFocusedPad(string padId)
        {
            return PanelBarracks != null
                && PanelBarracks.activeInHierarchy
                && !string.IsNullOrWhiteSpace(padId)
                && string.Equals(_focusedPadId, padId, System.StringComparison.OrdinalIgnoreCase);
        }

        public Button GetFocusedBarracksHeaderActionButton()
        {
            return BtnConfirm != null && BtnConfirm.gameObject.activeInHierarchy
                ? BtnConfirm
                : null;
        }

        public Button GetFocusedBarracksUnitBuyButton(string rosterKey, int quantity = 1)
        {
            if (PanelBarracks == null || !PanelBarracks.activeInHierarchy || string.IsNullOrWhiteSpace(rosterKey))
                return null;

            string actionKey = quantity >= 10
                ? "BuyTen"
                : "Buy";
            string buttonName = BuildFocusedBarracksActionObjectName(actionKey, rosterKey);
            return FindNamedButton(PanelBarracks.transform, buttonName);
        }

        public RectTransform GetFocusedBarracksUnitCard(string rosterKey)
        {
            if (PanelBarracks == null || !PanelBarracks.activeInHierarchy || string.IsNullOrWhiteSpace(rosterKey))
                return null;

            string cardName = BuildFocusedBarracksCardObjectName(rosterKey);
            var target = PanelBarracks.transform.Find(cardName);
            if (target != null)
                return target as RectTransform;

            return FindNamedTransform(PanelBarracks.transform, cardName) as RectTransform;
        }

        public Button GetTownCoreBarracksPrimaryActionButton(string barracksId)
        {
            if (PanelBarracks == null || !PanelBarracks.activeInHierarchy || string.IsNullOrWhiteSpace(barracksId))
                return null;

            return FindNamedButton(PanelBarracks.transform, BuildTownCoreBarracksActionObjectName("Primary", barracksId));
        }

        public Button GetTownCorePadPrimaryActionButton(string padId)
        {
            if (PanelBarracks == null || !PanelBarracks.activeInHierarchy || string.IsNullOrWhiteSpace(padId))
                return null;

            return FindNamedButton(PanelBarracks.transform, BuildTownCorePadActionObjectName("Primary", padId));
        }

        public Button GetFocusedMarketBuyButton(string unitKey)
        {
            if (PanelBarracks == null || !PanelBarracks.activeInHierarchy || string.IsNullOrWhiteSpace(unitKey))
                return null;

            return FindNamedButton(PanelBarracks.transform, BuildFocusedMarketActionObjectName("Buy", unitKey));
        }

        public void ShowProgression()
        {
            ClearGuidedUnlockContext();
            _focusedBarracksId = null;
            _focusedPadId = null;
            _focusedPadUntil = 0f;
            _statusMessage = null;
            ShowProgressionViewer();
        }

        public void ToggleProgression()
        {
            Debug.Log("TECH toggle fired");
            EnsureInitialized();
            EnsureRuntimePanelChrome();
            if (PanelBarracks == null)
            {
                Debug.LogError("Tech panel is NULL");
                return;
            }

            if (IsShowingProgression)
            {
                Hide();
                return;
            }

            ShowProgression();
        }

        public bool IsShowingProgression =>
            PanelBarracks != null
            && PanelBarracks.activeSelf
            && _progressionViewerHost != null
            && _progressionViewerHost.gameObject.activeInHierarchy;

        public void Hide()
        {
            DestroyProgressionViewer();
            if (PanelBarracks == null)
                return;

            if (isActiveAndEnabled)
            {
                if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
                _scaleCoroutine = StartCoroutine(ScaleOut(PanelBarracks.transform, 0.15f));
            }
            else
            {
                PanelBarracks.SetActive(false);
            }
        }

        void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            EnsureRuntimePanelChrome();
            RegisterNetworkCallbacks();

            if (BtnCancel != null)
            {
                BtnCancel.onClick.RemoveListener(Hide);
                BtnCancel.onClick.AddListener(Hide);
                var cancelLabel = BtnCancel.GetComponentInChildren<TMP_Text>(true);
                if (cancelLabel != null) cancelLabel.text = "Close";
            }

            if (BtnConfirm != null)
                BtnConfirm.gameObject.SetActive(false);

            EnsureRuntimeContentRoot();
        }

        void EnsureRuntimePanelChrome()
        {
            if (PanelBarracks == null)
                PanelBarracks = FindOrCreateRuntimePanelRoot();
            if (PanelBarracks == null)
                return;

            PanelBarracks.transform.SetAsLastSibling();
            if (PanelBarracks.TryGetComponent<RectTransform>(out var panelRect))
                ApplyResponsivePanelRootLayout(panelRect);
            var panelImage = PanelBarracks.GetComponent<Image>();
            if (panelImage == null)
                panelImage = PanelBarracks.AddComponent<Image>();
            panelImage.sprite = null;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false;
            panelImage.color = ObsidianRootColor;
            EnsureRuntimePanelSurface();

            TxtTitle = EnsureRuntimeLabel(
                TxtTitle,
                "RuntimeTitle",
                new Vector2(0.05f, 0.92f),
                new Vector2(0.54f, 0.978f),
                24f,
                FontStyles.Bold,
                GoldTextColor,
                TextAlignmentOptions.Left);
            TxtTitle.textWrappingMode = TextWrappingModes.NoWrap;
            TxtTitle.overflowMode = TextOverflowModes.Ellipsis;

            TxtBenefits = EnsureRuntimeLabel(
                TxtBenefits,
                "RuntimeBenefits",
                new Vector2(0.05f, 0.872f),
                new Vector2(0.95f, 0.913f),
                15f,
                FontStyles.Normal,
                SilverTextColor,
                TextAlignmentOptions.Left);
            TxtBenefits.textWrappingMode = TextWrappingModes.NoWrap;
            TxtBenefits.overflowMode = TextOverflowModes.Ellipsis;

            TxtCost = EnsureRuntimeLabel(
                TxtCost,
                "RuntimeRosterSummary",
                new Vector2(0.05f, 0.825f),
                new Vector2(0.95f, 0.863f),
                14f,
                FontStyles.Bold,
                GoldTextColor,
                TextAlignmentOptions.Left);
            TxtCost.textWrappingMode = TextWrappingModes.NoWrap;
            TxtCost.overflowMode = TextOverflowModes.Ellipsis;

            TxtAffordance = EnsureRuntimeLabel(
                TxtAffordance,
                "RuntimeAffordance",
                new Vector2(0.05f, 0.782f),
                new Vector2(0.95f, 0.82f),
                13f,
                FontStyles.Normal,
                MutedSilverTextColor,
                TextAlignmentOptions.Left);
            TxtAffordance.textWrappingMode = TextWrappingModes.NoWrap;
            TxtAffordance.overflowMode = TextOverflowModes.Ellipsis;

            BtnCancel = EnsureRuntimeButton(
                BtnCancel,
                "RuntimeCloseButton",
                "Close",
                new Vector2(0.82f, 0.918f),
                new Vector2(0.95f, 0.976f));

            BtnConfirm = EnsureRuntimeButton(
                BtnConfirm,
                "RuntimeConfirmButton",
                string.Empty,
                new Vector2(0.56f, 0.918f),
                new Vector2(0.80f, 0.976f));
            if (BtnConfirm != null)
            {
                var confirmImage = BtnConfirm.GetComponent<Image>();
                if (confirmImage != null)
                    confirmImage.color = GoldSurfaceColor;
            }

            SetHeaderSummaryVisible(!_usingMinimalFocusedHeader);
            ApplyResponsiveChromeLayout();
            EnsureRuntimeChromeOverlayOrder();
        }

        void EnsureRuntimePanelSurface()
        {
            if (PanelBarracks == null)
                return;

            var shadow = ClassicRpgUiRuntime.EnsureChildImage(PanelBarracks.transform, "RuntimePanelShadow");
            if (shadow != null)
            {
                shadow.transform.SetAsFirstSibling();
                shadow.raycastTarget = false;
                ClassicRpgUiRuntime.ApplyPanel(
                    shadow,
                    ClassicRpgPanelSkin.Shadow,
                    false,
                    new Color(1f, 1f, 1f, 0.22f));
                shadow.rectTransform.anchorMin = Vector2.zero;
                shadow.rectTransform.anchorMax = Vector2.one;
                shadow.rectTransform.offsetMin = new Vector2(-24f, -26f);
                shadow.rectTransform.offsetMax = new Vector2(24f, 20f);
            }

            var surface = ClassicRpgUiRuntime.EnsureChildImage(PanelBarracks.transform, "RuntimePanelSurface");
            if (surface != null)
            {
                surface.transform.SetSiblingIndex(shadow != null ? 1 : 0);
                surface.raycastTarget = false;
                surface.sprite = null;
                surface.type = Image.Type.Simple;
                surface.preserveAspect = false;
                surface.color = ObsidianSurfaceColor;
                surface.rectTransform.anchorMin = Vector2.zero;
                surface.rectTransform.anchorMax = Vector2.one;
                surface.rectTransform.offsetMin = new Vector2(18f, 18f);
                surface.rectTransform.offsetMax = new Vector2(-18f, -18f);
            }

            var frame = ClassicRpgUiRuntime.EnsureChildImage(PanelBarracks.transform, "RuntimePanelFrame");
            if (frame != null)
            {
                frame.transform.SetAsLastSibling();
                frame.raycastTarget = false;
                ClassicRpgUiRuntime.ApplyPanel(
                    frame,
                    ClassicRpgPanelSkin.Frame,
                    true,
                    new Color(0.86f, 0.86f, 0.88f, 0.42f));
                frame.type = Image.Type.Sliced;
                frame.fillCenter = false;
                frame.rectTransform.anchorMin = Vector2.zero;
                frame.rectTransform.anchorMax = Vector2.one;
                frame.rectTransform.offsetMin = Vector2.zero;
                frame.rectTransform.offsetMax = Vector2.zero;
            }
        }

        void EnsureRuntimeChromeOverlayOrder()
        {
            if (BtnConfirm != null && BtnConfirm.transform.parent == PanelBarracks.transform)
                BtnConfirm.transform.SetAsLastSibling();

            if (BtnCancel != null && BtnCancel.transform.parent == PanelBarracks.transform)
                BtnCancel.transform.SetAsLastSibling();
        }

        GameObject FindOrCreateRuntimePanelRoot()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return null;

            var existing = canvas.transform.Find(RuntimePanelRootName) ?? canvas.transform.Find(LegacyRuntimePanelRootName);
            GameObject root;
            if (existing != null)
            {
                root = existing.gameObject;
            }
            else
            {
                root = new GameObject(RuntimePanelRootName, typeof(RectTransform), typeof(Image));
                root.transform.SetParent(canvas.transform, false);
            }

            var rect = root.GetComponent<RectTransform>();
            root.name = RuntimePanelRootName;
            ApplyResponsivePanelRootLayout(rect);

            return root;
        }

        TMP_Text EnsureRuntimeLabel(
            TMP_Text existing,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            FontStyles fontStyle,
            Color color,
            TextAlignmentOptions alignment)
        {
            if (PanelBarracks == null)
                return existing;

            var target = existing != null ? existing.transform : PanelBarracks.transform.Find(name);
            TextMeshProUGUI label;
            if (target != null)
            {
                label = target.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(PanelBarracks.transform, false);
                label = go.GetComponent<TextMeshProUGUI>();
            }

            var rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = Mathf.Max(MinimumPanelFontSize, fontSize);
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        Button EnsureRuntimeButton(
            Button existing,
            string name,
            string labelText,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            if (PanelBarracks == null)
                return existing;

            var target = existing != null ? existing.transform : PanelBarracks.transform.Find(name);
            Button button;
            if (target != null)
            {
                button = target.GetComponent<Button>();
            }
            else
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(PanelBarracks.transform, false);
                button = go.GetComponent<Button>();
            }

            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var label = EnsureButtonLabel(button, labelText);
            ClassicRpgUiRuntime.ApplyButton(
                button,
                string.Equals(name, "RuntimeCloseButton", System.StringComparison.Ordinal)
                    ? ClassicRpgButtonSkin.MiniBrown
                    : ClassicRpgButtonSkin.MiniGold,
                label,
                labelText);
            return button;
        }

        TextMeshProUGUI EnsureButtonLabel(Button button, string labelText)
        {
            if (button == null)
                return null;

            var labelTransform = button.transform.Find("Label");
            TextMeshProUGUI label;
            if (labelTransform != null && labelTransform.TryGetComponent<TextMeshProUGUI>(out var existingLabel))
            {
                label = existingLabel;
            }
            else
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(button.transform, false);
                label = labelGo.GetComponent<TextMeshProUGUI>();
            }

            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.SetAsLastSibling();

            label.gameObject.SetActive(true);
            label.enabled = true;
            label.raycastTarget = false;
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 16f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = Color.white;
            if (!string.IsNullOrEmpty(labelText) || string.IsNullOrEmpty(label.text))
                label.text = labelText;
            return label;
        }

        void HideHeaderActionButton()
        {
            if (BtnConfirm == null)
                return;

            BtnConfirm.onClick.RemoveAllListeners();
            BtnConfirm.gameObject.SetActive(false);
        }

        void ConfigureHeaderActionButton(string label, UnityEngine.Events.UnityAction action, bool interactable, Color backgroundColor)
        {
            if (BtnConfirm == null)
                return;

            BtnConfirm.onClick.RemoveAllListeners();
            BtnConfirm.gameObject.SetActive(true);
            BtnConfirm.interactable = interactable;

            var labelText = EnsureButtonLabel(BtnConfirm, label);
            ClassicRpgUiRuntime.ApplyButton(
                BtnConfirm,
                interactable ? ClassicRpgButtonSkin.MiniGold : ClassicRpgButtonSkin.MiniBrown,
                labelText,
                label);

            if (action != null)
                BtnConfirm.onClick.AddListener(action);
        }

        void RegisterNetworkCallbacks()
        {
            if (_networkHooksRegistered || NetworkManager.Instance == null)
                return;

            NetworkManager.Instance.OnActionApplied += HandleActionApplied;
            NetworkManager.Instance.OnErrorMsg += HandleErrorMessage;
            _networkHooksRegistered = true;
        }

        void UnregisterNetworkCallbacks()
        {
            if (!_networkHooksRegistered || NetworkManager.Instance == null)
                return;

            NetworkManager.Instance.OnActionApplied -= HandleActionApplied;
            NetworkManager.Instance.OnErrorMsg -= HandleErrorMessage;
            _networkHooksRegistered = false;
        }

        void HandleActionApplied(ActionAppliedPayload payload)
        {
            if (payload == null || PanelBarracks == null || !PanelBarracks.activeSelf)
                return;

            switch (payload.type)
            {
                case "build_on_pad":
                case "upgrade_building":
                case "purchase_building_upgrade":
                case "repair_all_buildings":
                case "upgrade_barracks":
                case "build_barracks_site":
                case "upgrade_barracks_site":
                case "buy_barracks_unit":
                case "sell_barracks_unit":
                    if (string.Equals(payload.type, "sell_barracks_unit", System.StringComparison.OrdinalIgnoreCase))
                        ClearPendingBarracksSell();
                    _statusMessage = $"Applied: {HumanizeAction(payload.type)}";
                    RefreshHeader(force: true);
                    RefreshContent(force: true);
                    break;
            }
        }

        void HandleErrorMessage(ErrorPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.message) || PanelBarracks == null || !PanelBarracks.activeSelf)
                return;

            _pendingBarracksBuildId = null;
            ClearPendingBarracksSell();
            _statusMessage = payload.message;
            RefreshHeader(force: true);
        }

        void HideImmediate()
        {
            DestroyProgressionViewer();
            ClearPendingBarracksSell();
            if (PanelBarracks != null)
            {
                PanelBarracks.transform.localScale = Vector3.zero;
                PanelBarracks.SetActive(false);
            }
        }

        void ShowProgressionViewer()
        {
            EnsureInitialized();
            EnsureRuntimePanelChrome();
            EnsureProgressionViewerHost();
            SetPanelChromeVisible(false);
            OpenPanel();
            _progressionViewerHost?.gameObject.SetActive(true);

            if (_progressionViewer == null && _progressionViewerHost != null)
            {
                _progressionViewer = LoadoutPhaseManager.AttachEmbeddedViewer(
                    _progressionViewerHost,
                    requestedRaceId: RaceProgressionCatalog.DefaultRaceId,
                    onClose: Hide);
            }
        }

        void EnsureProgressionViewerHost()
        {
            if (PanelBarracks == null)
                return;

            if (_progressionViewerHost != null)
            {
                _progressionViewerHost.SetAsLastSibling();
                return;
            }

            var existing = PanelBarracks.transform.Find("RuntimeProgressionViewerHost");
            GameObject host;
            if (existing != null)
            {
                host = existing.gameObject;
            }
            else
            {
                host = new GameObject("RuntimeProgressionViewerHost", typeof(RectTransform));
                host.transform.SetParent(PanelBarracks.transform, false);
            }

            _progressionViewerHost = host.GetComponent<RectTransform>();
            _progressionViewerHost.anchorMin = Vector2.zero;
            _progressionViewerHost.anchorMax = Vector2.one;
            _progressionViewerHost.offsetMin = Vector2.zero;
            _progressionViewerHost.offsetMax = Vector2.zero;
            _progressionViewerHost.SetAsLastSibling();
        }

        void DestroyProgressionViewer()
        {
            if (_progressionViewer != null)
            {
                Destroy(_progressionViewer.gameObject);
                _progressionViewer = null;
            }

            if (_progressionViewerHost != null)
            {
                Destroy(_progressionViewerHost.gameObject);
                _progressionViewerHost = null;
            }

            SetPanelChromeVisible(true);
        }

        void SetPanelChromeVisible(bool visible)
        {
            if (TxtTitle != null) TxtTitle.gameObject.SetActive(visible);
            if (TxtBenefits != null) TxtBenefits.gameObject.SetActive(visible);
            if (TxtCost != null) TxtCost.gameObject.SetActive(visible);
            if (TxtAffordance != null) TxtAffordance.gameObject.SetActive(visible);
            if (BtnConfirm != null) BtnConfirm.gameObject.SetActive(visible);
            if (BtnCancel != null) BtnCancel.gameObject.SetActive(visible);

            if (_scrollRect != null && _scrollRect.gameObject != null)
                _scrollRect.gameObject.SetActive(visible);
        }

        void SetHeaderSummaryVisible(bool visible)
        {
            if (TxtTitle != null) TxtTitle.gameObject.SetActive(visible);
            if (TxtBenefits != null) TxtBenefits.gameObject.SetActive(visible);
            if (TxtCost != null) TxtCost.gameObject.SetActive(visible);
            if (TxtAffordance != null) TxtAffordance.gameObject.SetActive(visible);
        }

        bool UseMinimalFocusedHeader(MLLaneSnap lane = null)
        {
            lane ??= SnapshotApplier.Instance?.MyLane;
            return GetFocusedPad(lane) != null || GetFocusedBarracksSite(lane) != null;
        }

        void OpenPanel()
        {
            if (PanelBarracks == null)
                return;

            PanelBarracks.transform.SetAsLastSibling();
            PanelBarracks.SetActive(true);
            PanelBarracks.transform.localScale = Vector3.zero;
            if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
            _scaleCoroutine = StartCoroutine(ScaleIn(PanelBarracks.transform, 0.2f));
        }

        void EnsureRuntimeContentRoot(bool forceReconfigure = false)
        {
            if (PanelBarracks == null)
                return;

            if (_contentRoot != null && _scrollRect != null && !forceReconfigure)
                return;

            var existing = PanelBarracks.transform.Find("RuntimeScrollView");
            GameObject scrollGo;
            if (existing != null)
            {
                scrollGo = existing.gameObject;
            }
            else
            {
                scrollGo = new GameObject("RuntimeScrollView");
                scrollGo.transform.SetParent(PanelBarracks.transform, false);
            }

            var scrollRt = GetOrAddComponent<RectTransform>(scrollGo);
            var scrollImage = GetOrAddComponent<Image>(scrollGo);
            _scrollRect = GetOrAddComponent<ScrollRect>(scrollGo);
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(
                GetPanelSidePadding() + GetFrameContentSideGutter(),
                GetPanelBottomPadding() + GetFrameContentBottomGutter());
            scrollRt.offsetMax = new Vector2(
                -(GetPanelSidePadding() + GetFrameContentSideGutter()),
                -(GetContentTopInset() + GetFrameContentTopGutter()));

            scrollImage.sprite = null;
            scrollImage.type = Image.Type.Simple;
            scrollImage.color = new Color(0.05f, 0.05f, 0.06f, 0.76f);
            scrollImage.raycastTarget = true;

            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 24f;
            _scrollRect.horizontalScrollbar = null;
            _scrollRect.verticalScrollbar = null;

            var viewportTransform = scrollGo.transform.Find("Viewport");
            GameObject viewportGo;
            if (viewportTransform != null)
            {
                viewportGo = viewportTransform.gameObject;
            }
            else
            {
                viewportGo = new GameObject("Viewport");
                viewportGo.transform.SetParent(scrollGo.transform, false);
            }

            var viewportRt = GetOrAddComponent<RectTransform>(viewportGo);
            var viewportImage = GetOrAddComponent<Image>(viewportGo);
            var viewportMask = GetOrAddComponent<Mask>(viewportGo);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            float viewportInset = GetContentViewportInset();
            float scrollbarGap = GetRuntimeVerticalScrollbarGap();
            float scrollbarWidth = GetRuntimeVerticalScrollbarWidth();
            viewportRt.offsetMin = new Vector2(viewportInset, viewportInset);
            viewportRt.offsetMax = new Vector2(-(viewportInset + scrollbarGap + scrollbarWidth), -viewportInset);
            viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
            viewportImage.raycastTarget = true;
            viewportMask.showMaskGraphic = false;

            var contentTransform = viewportGo.transform.Find("Content");
            GameObject contentGo;
            if (contentTransform != null)
            {
                contentGo = contentTransform.gameObject;
            }
            else
            {
                contentGo = new GameObject("Content");
                contentGo.transform.SetParent(viewportGo.transform, false);
            }

            _contentRoot = GetOrAddComponent<RectTransform>(contentGo);
            var layout = GetOrAddComponent<VerticalLayoutGroup>(contentGo);
            var fitter = GetOrAddComponent<ContentSizeFitter>(contentGo);
            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(1f, 1f);
            _contentRoot.pivot = new Vector2(0.5f, 1f);
            _contentRoot.offsetMin = Vector2.zero;
            _contentRoot.offsetMax = Vector2.zero;

            layout.spacing = GetContentStackSpacing();
            layout.padding = new RectOffset(0, 0, 0, GetContentBottomGutter());
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.enabled = false;

            var scrollbarTransform = scrollGo.transform.Find("VerticalScrollbar");
            GameObject scrollbarGo;
            if (scrollbarTransform != null)
            {
                scrollbarGo = scrollbarTransform.gameObject;
            }
            else
            {
                scrollbarGo = new GameObject("VerticalScrollbar");
                scrollbarGo.transform.SetParent(scrollGo.transform, false);
            }

            var scrollbarRt = GetOrAddComponent<RectTransform>(scrollbarGo);
            var scrollbarTrack = GetOrAddComponent<Image>(scrollbarGo);
            var scrollbar = GetOrAddComponent<Scrollbar>(scrollbarGo);
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 0.5f);
            scrollbarRt.anchoredPosition = new Vector2(-viewportInset, 0f);
            scrollbarRt.sizeDelta = new Vector2(scrollbarWidth, 0f);
            scrollbarTrack.sprite = null;
            scrollbarTrack.type = Image.Type.Simple;
            scrollbarTrack.color = GunmetalSoftColor;

            var slidingAreaTransform = scrollbarGo.transform.Find("SlidingArea");
            GameObject slidingAreaGo;
            if (slidingAreaTransform != null)
            {
                slidingAreaGo = slidingAreaTransform.gameObject;
            }
            else
            {
                slidingAreaGo = new GameObject("SlidingArea");
                slidingAreaGo.transform.SetParent(scrollbarGo.transform, false);
            }

            var slidingAreaRt = GetOrAddComponent<RectTransform>(slidingAreaGo);
            slidingAreaRt.anchorMin = Vector2.zero;
            slidingAreaRt.anchorMax = Vector2.one;
            slidingAreaRt.offsetMin = new Vector2(2f, 4f);
            slidingAreaRt.offsetMax = new Vector2(-2f, -4f);

            var handleTransform = slidingAreaGo.transform.Find("Handle");
            GameObject handleGo;
            if (handleTransform != null)
            {
                handleGo = handleTransform.gameObject;
            }
            else
            {
                handleGo = new GameObject("Handle");
                handleGo.transform.SetParent(slidingAreaGo.transform, false);
            }

            var handleRt = GetOrAddComponent<RectTransform>(handleGo);
            var handleImage = GetOrAddComponent<Image>(handleGo);
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            handleImage.color = GoldAccentColor;
            ClassicRpgUiRuntime.ApplyPanel(
                handleImage,
                ClassicRpgPanelSkin.Frame,
                true,
                handleImage.color);

            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImage;
            scrollbar.numberOfSteps = 0;

            _scrollRect.viewport = viewportRt;
            _scrollRect.content = _contentRoot;
            _scrollRect.verticalScrollbar = scrollbar;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scrollRect.verticalScrollbarSpacing = 0f;
            _scrollRect.onValueChanged.RemoveListener(HandleRuntimeScrollValueChanged);
            _scrollRect.onValueChanged.AddListener(HandleRuntimeScrollValueChanged);

            _verticalScrollbar = scrollbar;
            EnsureRuntimePanelSurface();
            EnsureRuntimeChromeOverlayOrder();
        }

        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (target == null)
                return null;

            return target.TryGetComponent<T>(out var component)
                ? component
                : target.AddComponent<T>();
        }

        void ApplyResponsivePanelRootLayout(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            float width = PanelBaseWidth;
            float height = PanelBaseHeight;
            Vector2 anchoredPosition = new Vector2(0f, -6f);

            if (TryGetCanvasRect(out var canvasRect))
            {
                GetSafeAreaInsetsUnits(canvasRect, out float leftInset, out float rightInset, out float topInset, out float bottomInset);
                float safeWidth = Mathf.Max(0f, canvasRect.rect.width - leftInset - rightInset);
                float safeHeight = Mathf.Max(0f, canvasRect.rect.height - topInset - bottomInset);
                if (safeWidth > 0f)
                    width = Mathf.Min(PanelBaseWidth, safeWidth * PanelViewportWidthRatio);
                if (safeHeight > 0f)
                    height = Mathf.Min(PanelBaseHeight, safeHeight * PanelViewportHeightRatio);

                anchoredPosition = new Vector2(
                    (leftInset - rightInset) * 0.5f,
                    ((bottomInset - topInset) * 0.5f) - 6f);
            }

            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = anchoredPosition;
        }

        void ApplyResponsiveChromeLayout()
        {
            if (PanelBarracks == null)
                return;

            float sidePadding = GetPanelSidePadding();
            float topPadding = GetPanelTopPadding();
            float titleHeight = GetHeaderTitleHeight();
            float infoHeight = GetHeaderInfoRowHeight();
            float infoGap = GetHeaderInfoGap();
            float buttonHeight = GetHeaderButtonHeight();
            float closeWidth = GetHeaderCloseButtonWidth();
            float confirmWidth = GetHeaderConfirmButtonWidth();
            float buttonGap = GetHeaderButtonGap();
            float reservedRight = sidePadding + closeWidth + buttonGap + confirmWidth + buttonGap;

            ConfigureHeaderLabel(TxtTitle, GetHeaderTitleFontSize());
            ConfigureHeaderLabel(TxtBenefits, GetHeaderInfoFontSize());
            ConfigureHeaderLabel(TxtCost, GetHeaderCostFontSize());
            ConfigureHeaderLabel(TxtAffordance, GetHeaderHintFontSize());

            if (TxtTitle != null)
                SetTopStretchRect(TxtTitle.rectTransform, topPadding, titleHeight, sidePadding, reservedRight);

            float nextTop = topPadding + titleHeight + infoGap;
            if (TxtBenefits != null)
                SetTopStretchRect(TxtBenefits.rectTransform, nextTop, infoHeight, sidePadding, sidePadding);
            nextTop += infoHeight + infoGap;
            if (TxtCost != null)
                SetTopStretchRect(TxtCost.rectTransform, nextTop, infoHeight, sidePadding, sidePadding);
            nextTop += infoHeight + infoGap;
            if (TxtAffordance != null)
                SetTopStretchRect(TxtAffordance.rectTransform, nextTop, infoHeight, sidePadding, sidePadding);

            if (BtnCancel != null)
            {
                SetBottomCenterRect(
                    BtnCancel.GetComponent<RectTransform>(),
                    GetFooterCloseButtonBottomInset(),
                    closeWidth,
                    buttonHeight);
                ConfigureHeaderButtonLabel(BtnCancel);
            }

            if (BtnConfirm != null)
            {
                SetTopRightRect(
                    BtnConfirm.GetComponent<RectTransform>(),
                    topPadding,
                    sidePadding + closeWidth + buttonGap,
                    confirmWidth,
                    buttonHeight);
                ConfigureHeaderButtonLabel(BtnConfirm);
            }
        }

        void ConfigureHeaderLabel(TMP_Text label, float fontSize)
        {
            if (label == null)
                return;

            label.fontSize = Mathf.Max(MinimumPanelFontSize, fontSize);
            label.alignment = TextAlignmentOptions.Left;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        void ConfigureHeaderButtonLabel(Button button)
        {
            var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null)
                return;

            label.fontSize = Mathf.Max(MinimumPanelFontSize, GetHeaderButtonFontSize());
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        static void SetTopStretchRect(RectTransform rect, float top, float height, float left, float right)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static void SetTopRightRect(RectTransform rect, float top, float right, float width, float height)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(-right, -top);
        }

        static void SetBottomCenterRect(RectTransform rect, float bottom, float width, float height)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, bottom);
        }

        bool TryGetCanvasRect(out RectTransform canvasRect)
        {
            canvasRect = null;
            var canvas = PanelBarracks != null
                ? PanelBarracks.GetComponentInParent<Canvas>()
                : GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return false;

            canvasRect = canvas.transform as RectTransform;
            return canvasRect != null;
        }

        static void GetSafeAreaInsetsUnits(RectTransform canvasRect, out float leftInset, out float rightInset, out float topInset, out float bottomInset)
        {
            leftInset = 0f;
            rightInset = 0f;
            topInset = 0f;
            bottomInset = 0f;

            if (canvasRect == null || Screen.width <= 0f || Screen.height <= 0f)
                return;

            var safeArea = Screen.safeArea;
            float widthScale = canvasRect.rect.width / Screen.width;
            float heightScale = canvasRect.rect.height / Screen.height;
            leftInset = safeArea.xMin * widthScale;
            rightInset = (Screen.width - safeArea.xMax) * widthScale;
            topInset = (Screen.height - safeArea.yMax) * heightScale;
            bottomInset = safeArea.yMin * heightScale;
        }

        float GetPanelHeight()
        {
            if (PanelBarracks != null && PanelBarracks.TryGetComponent<RectTransform>(out var rect))
            {
                float panelHeight = rect.rect.height;
                if (panelHeight > 0f)
                    return panelHeight;
            }

            if (TryGetCanvasRect(out var canvasRect))
                return canvasRect.rect.height;

            return PanelBaseHeight;
        }

        bool IsCompactPanelLayout() => GetPanelHeight() <= 660f;
        bool IsTightPanelLayout() => GetPanelHeight() <= 560f;
        float GetPanelSidePadding() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 34f : 40f) : 50f;
        float GetPanelTopPadding() => IsCompactPanelLayout() ? 14f : 18f;
        float GetPanelBottomPadding() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 24f : 28f) : 36f;
        float GetContentViewportInset() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 10f : 12f) : 16f;
        float GetRuntimeVerticalScrollbarWidth() => IsCompactPanelLayout() ? 12f : 16f;
        float GetRuntimeVerticalScrollbarGap() => IsCompactPanelLayout() ? 4f : 6f;
        int GetContentBottomGutter() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 18 : 22) : 28;
        float GetContentStackSpacing() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 6f : 8f) : 10f;
        float GetHeaderTitleFontSize() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 18f : 19f) : 24f;
        float GetHeaderInfoFontSize() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 12f : 13f) : 15f;
        float GetHeaderCostFontSize() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 11.5f : 12.5f) : 14f;
        float GetHeaderHintFontSize() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 10.5f : 11.5f) : 13f;
        float GetHeaderButtonFontSize() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 13f : 14f) : 16f;
        float GetHeaderTitleHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 22f : 24f) : 28f;
        float GetHeaderInfoRowHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 14f : 15f) : 18f;
        float GetHeaderInfoGap() => IsTightPanelLayout() ? 2f : (IsCompactPanelLayout() ? 3f : 4f);
        float GetHeaderButtonHeight() => IsCompactPanelLayout() ? 32f : 34f;
        float GetHeaderCloseButtonWidth() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 82f : 88f) : 104f;
        float GetHeaderConfirmButtonWidth() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 124f : 132f) : 164f;
        float GetHeaderButtonGap() => IsCompactPanelLayout() ? 8f : 10f;
        float GetFooterCloseButtonBottomInset() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 10f : 12f) : 16f;
        float GetHeaderToContentGap() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 10f : 12f) : 18f;
        float GetFrameContentSideGutter() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 20f : 26f) : 34f;
        float GetFrameContentBottomGutter() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 12f : 16f) : 22f;
        float GetFrameContentTopGutter() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 20f : 24f) : 32f;

        float GetContentTopInset()
        {
            if (_usingMinimalFocusedHeader)
                return GetPanelTopPadding() + GetFrameContentTopGutter();

            float infoHeight = GetHeaderInfoRowHeight();
            return
                GetPanelTopPadding() +
                GetHeaderTitleHeight() +
                GetHeaderInfoGap() +
                infoHeight +
                GetHeaderInfoGap() +
                infoHeight +
                GetHeaderInfoGap() +
                infoHeight +
                GetHeaderToContentGap() +
                GetFrameContentTopGutter();
        }

        void RefreshHeader(bool force = false)
        {
            var snapshotApplier = SnapshotApplier.Instance;
            var snap = snapshotApplier?.LatestML;
            var lane = snapshotApplier?.MyLane;
            _usingMinimalFocusedHeader = UseMinimalFocusedHeader(lane);
            SetHeaderSummaryVisible(!_usingMinimalFocusedHeader);
            if (!force && snap != null && snap.tick == _lastHeaderTick)
                return;

            if (lane == null)
            {
                if (TxtTitle != null) TxtTitle.text = "Building Overview";
                if (TxtBenefits != null) TxtBenefits.text = "Waiting for lane snapshot...";
                if (TxtCost != null) TxtCost.text = string.Empty;
                if (TxtAffordance != null) TxtAffordance.text = string.Empty;
                HideHeaderActionButton();
                return;
            }

            _lastHeaderTick = snap != null ? snap.tick : _lastHeaderTick;
            int sendSeconds = snapshotApplier != null
                ? snapshotApplier.GetBarracksSendSecondsRemaining(lane.laneIndex)
                : Mathf.CeilToInt(lane.barracksSendTimerTicksRemaining / 20f);
            int waveSeconds = snapshotApplier != null
                ? snapshotApplier.GetWaveTimerSecondsRemaining()
                : Mathf.CeilToInt((snap != null ? snap.waveTimerTicksRemaining : 0) / 20f);
            var focusedBarracks = GetFocusedBarracksSite(lane);
            var focusedPad = GetFocusedPad(lane);

            if (focusedBarracks != null)
            {
                if (focusedBarracks.isBuilt && IsPendingBarracksBuild(focusedBarracks))
                    _pendingBarracksBuildId = null;

                string barracksName = ResolveBarracksDisplayName(focusedBarracks);
                if (TxtTitle != null)
                    TxtTitle.text = $"{barracksName}   Gold {Mathf.FloorToInt(lane.gold)}   Inc {lane.income:0.#}";

                if (TxtBenefits != null)
                    TxtBenefits.text = BuildFocusedBarracksHeaderText(lane, focusedBarracks, waveSeconds);

                if (TxtCost != null)
                    TxtCost.text = focusedBarracks.isBuilt
                        ? BuildFocusedBarracksRosterStatus(lane, focusedBarracks)
                        : BuildFocusedBarracksPurchaseStatus(focusedBarracks);

                if (TxtAffordance != null)
                {
                    TxtAffordance.text = string.IsNullOrWhiteSpace(_statusMessage)
                        ? focusedBarracks.isBuilt
                            ? BuildFocusedBarracksHint(lane, focusedBarracks)
                            : BuildFocusedBarracksPurchaseHint(lane, focusedBarracks)
                        : _statusMessage;
                    TxtAffordance.color = string.IsNullOrWhiteSpace(_statusMessage)
                        ? new Color(0.82f, 0.88f, 0.95f, 0.95f)
                        : new Color(1f, 0.88f, 0.55f, 0.98f);
                }

                if (_usingMinimalFocusedHeader)
                    HideHeaderActionButton();
                else
                    SyncFocusedBarracksHeaderAction(lane, focusedBarracks);
                return;
            }

            HideHeaderActionButton();

            if (TxtTitle != null)
                TxtTitle.text = focusedPad != null && !string.Equals(focusedPad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase)
                    ? $"{focusedPad.buildingName}  {HumanizeBuildState(focusedPad.buildState)}"
                    : "Building Overview";

            if (TxtBenefits != null)
                TxtBenefits.text = focusedPad != null && !string.Equals(focusedPad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase)
                    ? $"Gold {Mathf.FloorToInt(lane.gold)}   Income {lane.income:0.#}   Send {sendSeconds}s   Wave {waveSeconds}s"
                    : BuildBuildingOverviewHeaderText(lane, sendSeconds, waveSeconds);

            if (TxtCost != null)
                TxtCost.text = focusedPad != null && !string.Equals(focusedPad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase)
                    ? BuildFocusedPadSummary(lane, focusedPad, lane.barracksRoster, lane.heroRoster)
                    : BuildBuildingOverviewStatus(lane);

            if (TxtAffordance != null)
            {
                string guidedPadMessage = null;
                bool hasGuidedPadMessage = focusedPad != null
                    && !string.Equals(focusedPad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase)
                    && TryGetGuidedUnlockForPad(focusedPad, out _, out guidedPadMessage);
                TxtAffordance.text = string.IsNullOrWhiteSpace(_statusMessage)
                    ? hasGuidedPadMessage
                        ? guidedPadMessage
                        : focusedPad != null && !string.Equals(focusedPad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase)
                        ? BuildFocusedPadHint(lane, focusedPad, lane.barracksRoster, lane.heroRoster, sendSeconds, waveSeconds)
                        : BuildBuildingOverviewHint(lane)
                    : _statusMessage;
                TxtAffordance.color = string.IsNullOrWhiteSpace(_statusMessage)
                    ? new Color(0.82f, 0.88f, 0.95f, 0.95f)
                    : new Color(1f, 0.88f, 0.55f, 0.98f);
            }
        }

        void RefreshContentIfNeeded()
        {
            var lane = SnapshotApplier.Instance?.MyLane;
            if (lane == null)
                return;

            bool useMinimalFocusedHeader = UseMinimalFocusedHeader(lane);
            bool headerLayoutChanged = _usingMinimalFocusedHeader != useMinimalFocusedHeader;
            _usingMinimalFocusedHeader = useMinimalFocusedHeader;
            EnsureRuntimePanelChrome();
            EnsureRuntimeContentRoot(forceReconfigure: headerLayoutChanged);

            if (_contentRoot != null
                && _contentRoot.childCount > 0
                && (_contentRoot.rect.height <= 0.5f || _contentRoot.sizeDelta.y <= 0.5f))
            {
                UpdateRuntimeContentLayout();
                RestoreRuntimeContentScrollPosition();
            }

            float viewportWidth = GetContentViewportWidth();
            float viewportHeight = GetContentViewportHeight();
            string signature = BuildContentSignature(lane);
            if (!headerLayoutChanged
                && signature == _lastContentSignature
                && Mathf.Abs(viewportWidth - _lastViewportWidth) < 0.5f
                && Mathf.Abs(viewportHeight - _lastViewportHeight) < 0.5f)
                return;

            RefreshContent(force: true);
        }

        void RefreshContent(bool force)
        {
            var lane = SnapshotApplier.Instance?.MyLane;
            if (!force && lane == null)
                return;

            EnsureRuntimeContentRoot();
            if (_contentRoot == null)
                return;

            CaptureRuntimeContentScrollPosition();
            ClearContent();
            if (lane == null)
                return;

            var focusedBarracks = GetFocusedBarracksSite(lane);
            var focusedPad = GetFocusedPad(lane);
            if (_scrollRect != null)
                _scrollRect.vertical = true;

            if (focusedBarracks != null)
            {
                CreateFocusedBarracksSection(lane, focusedBarracks);
                CreateFocusedBarracksRosterSection(lane, focusedBarracks);
                if (focusedBarracks.isBuilt)
                {
                    CreateFocusedBarracksHeroSection(lane, focusedBarracks);
                }
            }
            else if (focusedPad != null)
            {
                if (string.Equals(focusedPad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
                {
                    CreateFocusedPadSection(lane);
                    CreateFocusedMarketRosterSection(lane, focusedPad);
                    CreateFocusedPadUpgradeSections(lane, focusedPad);
                }
                else
                {
                    CreateFocusedPadSection(lane);
                }
            }
            else
            {
                CreateBuildingOverviewSection(lane);
            }
            UpdateRuntimeContentLayout();
            _lastContentSignature = BuildContentSignature(lane);
            _lastViewportWidth = GetContentViewportWidth();
            _lastViewportHeight = GetContentViewportHeight();
            RestoreRuntimeContentScrollPosition();
        }

        string BuildContentSignature(MLLaneSnap lane)
        {
            if (lane == null)
                return "no-lane";

            bool suppressVolatileTownCoreTimers = IsTownCoreCommandView(lane);
            int gold = Mathf.FloorToInt(lane.gold);
            int level = lane.barracksLevel;
            var sig =
                $"{gold}|{level}|focus-pad:{_focusedPadId}|focus-barracks:{_focusedBarracksId}|" +
                $"guided-pad:{_guidedUnlockPadId}|guided-unit:{_guidedUnlockUnitKey}|guided-tier:{_guidedUnlockRequiredTier}|" +
                $"catalog:{CatalogLoader.UnitByKey.Count}|";

            if (lane.fortressPads != null)
            {
                for (int i = 0; i < lane.fortressPads.Length; i++)
                {
                    var pad = lane.fortressPads[i];
                    if (pad == null) continue;
                    sig +=
                        $"{pad.padId}:{pad.tier}:{pad.buildState}:{pad.isConstructing}:{pad.constructionKind}:" +
                        $"{pad.constructionTargetTier}:{(suppressVolatileTownCoreTimers ? 0 : pad.constructionTimerTicksRemaining)}:{pad.canBuild}:{pad.canUpgrade}:" +
                        $"{Mathf.RoundToInt(pad.hp)}:{Mathf.RoundToInt(pad.maxHp)}:{pad.foodUsed}:{pad.foodLimit}:{pad.foodRemaining}:{pad.isAtFoodLimit}:{pad.lockedReason}:{pad.upgradePanelDescription}|";
                    if (pad.buildingUpgrades == null) continue;
                    for (int upgradeIndex = 0; upgradeIndex < pad.buildingUpgrades.Length; upgradeIndex++)
                    {
                        var upgrade = pad.buildingUpgrades[upgradeIndex];
                        if (upgrade == null) continue;
                        sig +=
                            $"upgrade:{pad.padId}:{upgrade.upgradeKey}:{upgrade.purchaseCount}:{upgrade.cost}:{upgrade.canPurchase}:{upgrade.isPurchased}:" +
                            $"{upgrade.currentBonusText}:{upgrade.nextBonusText}:{upgrade.lockedReason}|";
                    }
                }
            }

            if (lane.barracksRoster != null)
            {
                for (int i = 0; i < lane.barracksRoster.Length; i++)
                {
                    var entry = lane.barracksRoster[i];
                    if (entry == null) continue;
                    sig += $"{entry.rosterKey}:{entry.skinKey}:{entry.ownedCount}:{entry.buyCost}:{entry.sellRefund}:{entry.foodCost}:{entry.unlocked}:{entry.availableForPurchase}:{entry.currentTier}:{entry.lockedReason}|";
                }
            }

            if (lane.barracksSites != null)
            {
                for (int i = 0; i < lane.barracksSites.Length; i++)
                {
                    var site = lane.barracksSites[i];
                    if (site == null) continue;
                    sig +=
                        $"{site.barracksId}:{site.isBuilt}:{site.level}:{site.buildState}:{site.isConstructing}:" +
                        $"{site.constructionKind}:{site.constructionTargetLevel}:{(suppressVolatileTownCoreTimers ? 0 : site.constructionTimerTicksRemaining)}:" +
                        $"{site.canBuild}:{site.canUpgrade}:{Mathf.RoundToInt(site.hp)}:{Mathf.RoundToInt(site.maxHp)}:{site.foodUsed}:{site.foodLimit}:{site.foodRemaining}:{site.isAtFoodLimit}:" +
                        $"{site.hasActiveFoodState}:{site.activeFoodUsed}:{site.activeFoodRemaining}:{site.isAtActiveFoodLimit}:{site.lockedReason}|";
                    if (site.roster == null) continue;
                    for (int rosterIndex = 0; rosterIndex < site.roster.Length; rosterIndex++)
                    {
                        var entry = site.roster[rosterIndex];
                        if (entry == null) continue;
                        sig += $"{site.barracksId}:{entry.rosterKey}:{entry.skinKey}:{entry.ownedCount}:{entry.buyCost}:{entry.sellRefund}:{entry.foodCost}:{entry.unlocked}:{entry.availableForPurchase}:{entry.currentTier}:{entry.lockedReason}|";
                    }
                }
            }

            if (lane.marketRoster != null)
            {
                for (int i = 0; i < lane.marketRoster.Length; i++)
                {
                    var entry = lane.marketRoster[i];
                    if (entry == null) continue;
                    sig +=
                        $"market:{entry.unitKey}:{entry.skinKey}:{entry.ownedCount}:{entry.buyCost}:{entry.foodCost}:{entry.unlocked}:" +
                        $"{entry.availableForPurchase}:{entry.currentTier}:{entry.economyLapGold}:{entry.lockedReason}|";
                }
            }

            if (lane.heroRoster != null)
            {
                for (int i = 0; i < lane.heroRoster.Length; i++)
                {
                    var hero = lane.heroRoster[i];
                    if (hero == null) continue;
                    sig +=
                        $"hero:{hero.heroKey}:{hero.state}:{hero.canSummon}:{(suppressVolatileTownCoreTimers ? 0 : hero.cooldownTicksRemaining)}:" +
                        $"{hero.activeCount}:{hero.activeLimit}:{hero.disabledReason}:{hero.lockedReason}|";
                }
            }

            return sig;
        }

        bool IsTownCoreCommandView(MLLaneSnap lane)
        {
            var focusedPad = GetFocusedPad(lane);
            return focusedPad != null
                && string.Equals(focusedPad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase);
        }

        void HandleRuntimeScrollValueChanged(Vector2 _)
        {
            CaptureRuntimeContentScrollPosition();
        }

        void UpdateRuntimeContentLayout()
        {
            if (_contentRoot == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            float preferredHeight = LayoutUtility.GetPreferredHeight(_contentRoot);
            float viewportHeight = _scrollRect != null && _scrollRect.viewport != null
                ? _scrollRect.viewport.rect.height
                : 0f;
            float resolvedHeight = Mathf.Max(preferredHeight, viewportHeight);
            if (resolvedHeight > 0f)
                _contentRoot.sizeDelta = new Vector2(_contentRoot.sizeDelta.x, resolvedHeight);

            _contentRoot.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            if (_verticalScrollbar != null && resolvedHeight > 0f && viewportHeight > 0f)
                _verticalScrollbar.size = Mathf.Clamp01(viewportHeight / resolvedHeight);
        }

        void CaptureRuntimeContentScrollPosition()
        {
            if (_scrollRect == null)
                return;

            _runtimeContentNormalizedPosition = Mathf.Clamp01(_scrollRect.verticalNormalizedPosition);
        }

        void RestoreRuntimeContentScrollPosition()
        {
            if (_scrollRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            _scrollRect.StopMovement();
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(_runtimeContentNormalizedPosition);
        }

        void ResetRuntimeContentScrollPosition()
        {
            _runtimeContentNormalizedPosition = 1f;
            RestoreRuntimeContentScrollPosition();
        }

        void ClearContent()
        {
            for (int i = _contentRoot.childCount - 1; i >= 0; i--)
                Destroy(_contentRoot.GetChild(i).gameObject);
        }

        void CreateFocusedBarracksSection(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return;

            CreateSectionHeader("Barracks");
            CreatePanelTemplateRow(_contentRoot, BuildFocusedBarracksSummaryRowData(lane, site));
        }

        void CreateFocusedBarracksRosterSection(MLLaneSnap lane, MLBarracksSite site)
        {
            var visibleEntries = GetCurrentBarracksRosterEntries(site?.roster);
            CreateSectionHeader(site != null && !site.isBuilt
                ? $"Unit Preview ({visibleEntries.Count})"
                : $"Units ({visibleEntries.Count})");
            if (site?.roster == null || site.roster.Length == 0)
            {
                CreateInfoCard("No barracks-specific roster data is available yet.");
                return;
            }

            if (visibleEntries.Count <= 0)
            {
                CreateInfoCard(site != null && !site.isBuilt
                    ? "Open Town Core to purchase this barracks. Until then, each branch previews its tier 1 unit so you can jump straight to the required unlock path."
                    : "No current unit options are purchasable yet. Use Town Core to build or upgrade the linked branches that unlock each line.");
                return;
            }

            var ordered = visibleEntries.ToArray();
            System.Array.Sort(ordered, CompareFocusedBarracksRosterEntries);
            for (int i = 0; i < ordered.Length; i++)
            {
                var entry = ordered[i];
                if (entry == null) continue;

                CreatePanelTemplateRow(_contentRoot, BuildFocusedBarracksUnitRowData(lane, site, entry));
            }
        }

        void CreateFocusedMarketRosterSection(MLLaneSnap lane, MLFortressPad pad)
        {
            CreateSectionHeader("Market Contracts");
            if (lane == null || pad == null)
            {
                CreateInfoCard("Market data is not available yet.");
                return;
            }

            if (!pad.isBuilt)
            {
                CreateInfoCard("Build the Market first. Market purchases add timed gold directly on the shared income cycle instead of spawning a trade unit.");
                return;
            }

            var currentEntry = GetCurrentMarketRosterEntry(lane);
            if (currentEntry == null)
            {
                CreateInfoCard("The active Market income tier is missing from the snapshot.");
                return;
            }

            var nextEntry = GetNextMarketRosterEntry(lane, currentEntry);
            CreatePanelTemplateRow(_contentRoot, BuildFocusedMarketEntryRowData(lane, pad, currentEntry, nextEntry));
        }

        void CreateFocusedBarracksHeroSection(MLLaneSnap lane, MLBarracksSite site)
        {
            CreateSectionHeader("Heroes");
            if (lane?.heroRoster == null || lane.heroRoster.Length == 0)
            {
                CreateInfoCard("Castle heroes are not available in the snapshot yet.");
                return;
            }

            var ordered = (MLHeroRosterEntry[])lane.heroRoster.Clone();
            System.Array.Sort(ordered, CompareHeroEntries);
            for (int i = 0; i < ordered.Length; i++)
            {
                var hero = ordered[i];
                if (hero == null)
                    continue;

                CreatePanelTemplateRow(_contentRoot, BuildFocusedBarracksHeroRowData(lane, site, hero));
            }
        }

        void CreateFocusedBarracksHeroCard(MLLaneSnap lane, MLBarracksSite site, MLHeroRosterEntry hero)
        {
            if (lane == null || site == null || hero == null)
                return;

            var card = CreateCardContainer();
            TintCard(card, ResolveHeroPanelCardTint(hero));
            var cardElement = card.GetComponent<LayoutElement>();
            if (cardElement != null)
                cardElement.minHeight = IsCompactPanelLayout() ? 128f : 150f;

            var accent = new GameObject("HeroAccent", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accent.transform.SetParent(card, false);
            accent.GetComponent<Image>().color = ResolveHeroAccentColor(hero);
            var accentLayout = accent.GetComponent<LayoutElement>();
            accentLayout.minHeight = 6f;
            accentLayout.preferredHeight = 6f;

            var header = CreateHorizontalFillBlock(card, "HeroHeader", 6f);
            var name = CreateInlineText(
                header,
                "HeroName",
                hero.displayName,
                IsCompactPanelLayout() ? 14f : 16f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            var nameLayout = name.gameObject.AddComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1f;
            CreateStatusChip(
                header,
                "HERO",
                ResolveHeroAccentColor(hero),
                new Color(0.10f, 0.09f, 0.06f, 1f));

            CreateInlineText(
                card,
                "HeroMeta",
                $"{hero.unlockBuildingTierName} unlock   Summon from {hero.summonSourceBuildingName}   Cost {hero.summonCost}g",
                IsCompactPanelLayout() ? 10f : 11f,
                new Color(0.95f, 0.93f, 0.85f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            CreateInlineText(
                card,
                "HeroState",
                BuildFocusedBarracksHeroStateText(lane, site, hero),
                IsCompactPanelLayout() ? 10f : 11f,
                ResolveHeroPanelStateTextColor(hero),
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            var statRow = CreateHorizontalBlock(card, "HeroStats", 6f);
            CreateCountChip(statRow, $"CD {GetHeroCooldownSeconds(hero)}s", new Color(0.17f, 0.19f, 0.26f, 0.96f));
            CreateCountChip(statRow, $"Active {Mathf.Max(0, hero.activeCount)}/{Mathf.Max(1, hero.activeLimit)}", new Color(0.17f, 0.21f, 0.18f, 0.96f));
            if (TryGetHeroCardStats(hero, out var unit))
            {
                CreateCountChip(statRow, $"HP {FormatStatNumber(unit.hp)}", new Color(0.18f, 0.20f, 0.28f, 0.96f));
                CreateCountChip(statRow, $"ATK {FormatStatNumber(unit.attack_damage)}", new Color(0.20f, 0.18f, 0.28f, 0.96f));
            }

            var actions = CreateHorizontalFillBlock(card, "HeroActions", 6f);
            CreateFocusedBarracksActionChip(
                actions,
                BuildFocusedBarracksHeroDeployLabel(lane, site, hero),
                ResolveHeroAccentColor(hero),
                CanDeployBarracksHero(lane, site, hero),
                () => ExecuteFocusedBarracksHeroDeploy(site, hero));
            CreateFocusedBarracksActionChip(
                actions,
                BuildFocusedBarracksHeroStatusChip(hero),
                new Color(0.20f, 0.22f, 0.28f, 0.96f),
                false,
                null);
        }

        RectTransform CreateFocusedBarracksCardRail()
        {
            var railGo = new GameObject("FocusedBarracksCardRail", typeof(RectTransform), typeof(LayoutElement));
            railGo.transform.SetParent(_contentRoot, false);

            GetFocusedBarracksCardSize(out _, out float cardHeight);
            float footerGap = GetFocusedBarracksRailFooterGap();
            float footerHeight = GetFocusedBarracksRailFooterHeight();
            float footerButtonWidth = GetFocusedBarracksRailButtonWidth();
            float footerSideGap = GetFocusedBarracksRailSideGap();
            float scrollbarHeight = GetFocusedBarracksScrollbarHeight();
            float railHeight = cardHeight + footerGap + footerHeight;

            var railElement = railGo.GetComponent<LayoutElement>();
            railElement.minHeight = railHeight;
            railElement.preferredHeight = railHeight;
            railElement.flexibleWidth = 1f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(NestedHorizontalScrollRect));
            viewportGo.transform.SetParent(railGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = new Vector2(0f, 0f);
            viewportRt.anchorMax = new Vector2(1f, 1f);
            viewportRt.offsetMin = new Vector2(0f, footerHeight + footerGap);
            viewportRt.offsetMax = Vector2.zero;

            var viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = new Color(0.10f, 0.13f, 0.18f, 0.88f);
            viewportImage.raycastTarget = true;
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var layout = contentGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = FocusedBarracksCardSpacing;
            layout.padding = new RectOffset(0, Mathf.RoundToInt(FocusedBarracksCardSpacing), 0, 0);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = viewportGo.GetComponent<NestedHorizontalScrollRect>();
            scrollRect.viewport = viewportRt;
            scrollRect.content = contentRt;
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 32f;

            var footerGo = new GameObject("Footer", typeof(RectTransform));
            footerGo.transform.SetParent(railGo.transform, false);
            var footerRt = footerGo.GetComponent<RectTransform>();
            footerRt.anchorMin = new Vector2(0f, 0f);
            footerRt.anchorMax = new Vector2(1f, 0f);
            footerRt.pivot = new Vector2(0.5f, 0f);
            footerRt.anchoredPosition = Vector2.zero;
            footerRt.sizeDelta = new Vector2(0f, footerHeight);

            var previousButton = CreateFocusedBarracksRailButton(footerGo.transform, "PreviousButton", "<");
            var previousRt = previousButton.GetComponent<RectTransform>();
            previousRt.anchorMin = new Vector2(0f, 0.5f);
            previousRt.anchorMax = new Vector2(0f, 0.5f);
            previousRt.pivot = new Vector2(0f, 0.5f);
            previousRt.anchoredPosition = Vector2.zero;
            previousRt.sizeDelta = new Vector2(footerButtonWidth, footerHeight);

            var nextButton = CreateFocusedBarracksRailButton(footerGo.transform, "NextButton", ">");
            var nextRt = nextButton.GetComponent<RectTransform>();
            nextRt.anchorMin = new Vector2(1f, 0.5f);
            nextRt.anchorMax = new Vector2(1f, 0.5f);
            nextRt.pivot = new Vector2(1f, 0.5f);
            nextRt.anchoredPosition = Vector2.zero;
            nextRt.sizeDelta = new Vector2(footerButtonWidth, footerHeight);

            var scrollbarGo = new GameObject("HorizontalScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarGo.transform.SetParent(footerGo.transform, false);
            var scrollbarRt = scrollbarGo.GetComponent<RectTransform>();
            scrollbarRt.anchorMin = new Vector2(0f, 0.5f);
            scrollbarRt.anchorMax = new Vector2(1f, 0.5f);
            scrollbarRt.offsetMin = new Vector2(footerButtonWidth + footerSideGap, -scrollbarHeight * 0.5f);
            scrollbarRt.offsetMax = new Vector2(-(footerButtonWidth + footerSideGap), scrollbarHeight * 0.5f);

            var scrollbarTrack = scrollbarGo.GetComponent<Image>();
            scrollbarTrack.color = new Color(0.10f, 0.12f, 0.16f, 0.95f);

            var slidingAreaGo = new GameObject("SlidingArea", typeof(RectTransform));
            slidingAreaGo.transform.SetParent(scrollbarGo.transform, false);
            var slidingAreaRt = slidingAreaGo.GetComponent<RectTransform>();
            slidingAreaRt.anchorMin = Vector2.zero;
            slidingAreaRt.anchorMax = Vector2.one;
            slidingAreaRt.offsetMin = new Vector2(4f, 2f);
            slidingAreaRt.offsetMax = new Vector2(-4f, -2f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(slidingAreaGo.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            var handleImage = handleGo.GetComponent<Image>();
            handleImage.color = new Color(0.88f, 0.76f, 0.28f, 0.98f);

            var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImage;
            scrollbar.numberOfSteps = 0;
            scrollbar.size = 1f;

            scrollRect.horizontalScrollbar = scrollbar;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.horizontalScrollbarSpacing = 0f;

            previousButton.onClick.AddListener(() => ScrollFocusedBarracksRail(scrollRect, 1f));
            nextButton.onClick.AddListener(() => ScrollFocusedBarracksRail(scrollRect, -1f));
            scrollRect.onValueChanged.AddListener(_ =>
            {
                if (CanScrollHorizontally(scrollRect))
                    _focusedBarracksRailNormalizedPosition = scrollRect.horizontalNormalizedPosition;
                UpdateFocusedBarracksRailButtons(scrollRect, previousButton, nextButton);
            });

            return contentRt;
        }

        void CreateFocusedBarracksRosterCard(RectTransform grid, MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (grid == null || entry == null)
                return;

            GetFocusedBarracksCardSize(out float cardWidth, out float cardHeight);

            var card = CreateCardContainer(grid);
            card.name = BuildFocusedBarracksCardObjectName(entry.rosterKey);
            card.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cardWidth);
            card.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardHeight);
            var cardElement = card.GetComponent<LayoutElement>();
            if (cardElement != null)
            {
                cardElement.minWidth = cardWidth;
                cardElement.preferredWidth = cardWidth;
                cardElement.flexibleWidth = 0f;
                cardElement.minHeight = cardHeight;
                cardElement.preferredHeight = cardHeight;
                cardElement.flexibleHeight = 0f;
            }

            var cardLayout = card.GetComponent<VerticalLayoutGroup>();
            if (cardLayout != null)
            {
                int horizontalPadding = IsCompactPanelLayout() ? 10 : 14;
                int verticalPadding = IsCompactPanelLayout() ? 8 : 14;
                cardLayout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
                cardLayout.spacing = IsCompactPanelLayout() ? 6f : 12f;
            }

            TintCard(card, ResolveFocusedBarracksCardTint(entry));
            if (!entry.unlocked)
                WireLockedRosterCardNavigation(card, () => RedirectLockedUnitToUnlockBuilding(lane, entry, site));

            var top = CreateVerticalBlock(card, "Top", IsCompactPanelLayout() ? 4f : 6f);
            var topElement = top.gameObject.AddComponent<LayoutElement>();
            topElement.flexibleHeight = 1f;

            var header = CreateHorizontalBlock(top, "Header", 6f);
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = IsCompactPanelLayout() ? 20f : 28f;
            if (header.TryGetComponent<HorizontalLayoutGroup>(out var headerLayout))
                headerLayout.childControlWidth = true;

            var nameLabel = CreateInlineText(
                header,
                "Name",
                entry.displayName,
                IsCompactPanelLayout() ? 14f : 15.5f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            var nameElement = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameElement.flexibleWidth = 1f;
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;

            string branchMeta = BuildFocusedBarracksEntryMeta(entry);
            if (!string.IsNullOrWhiteSpace(branchMeta))
            {
                CreateInlineText(
                    top,
                    "BranchMeta",
                    branchMeta,
                    IsCompactPanelLayout() ? 10f : 11f,
                    new Color(0.95f, 0.90f, 0.72f, 0.96f),
                    FontStyles.Bold,
                    TextAlignmentOptions.Left);
            }

            CreateRosterPortrait(top, entry, IsCompactPanelLayout() ? (IsTightPanelLayout() ? 52f : 74f) : 118f);
            CreateFocusedBarracksOwnedStrip(top, entry);

            CreateFocusedBarracksStateStrip(top, lane, site, entry);

            if (!entry.unlocked)
            {
                CreateInlineText(
                    top,
                    "UnlockRedirectHint",
                    BuildLockedUnitRedirectHint(entry),
                    IsCompactPanelLayout() ? 9.5f : 10.5f,
                    new Color(0.96f, 0.90f, 0.64f, 0.98f),
                    FontStyles.Bold,
                    TextAlignmentOptions.Left);
            }

            CreateFocusedBarracksPrimaryActions(top, lane, site, entry);

            if (TryGetUnitCardStats(entry, out var unit))
            {
                var statGrid = CreateFocusedBarracksStatGrid(top, cardWidth);
                CreateStatTile(statGrid, "ATK", FormatStatNumber(unit.attack_damage));
                CreateStatTile(statGrid, "ARMOR", $"{Mathf.Max(0f, unit.damage_reduction_pct):0.#}%");
                CreateStatTile(statGrid, "DEF", HumanizeCombatType(unit.armor_type));
                CreateStatTile(statGrid, "HP", FormatStatNumber(unit.hp));
                if (!IsTightPanelLayout())
                {
                    CreateInlineText(
                        top,
                        "CombatMeta",
                        $"Atk Spd {Mathf.Max(0.01f, unit.attack_speed):0.##}/s   Dmg Type {HumanizeCombatType(unit.damage_type)}",
                        10.5f,
                        new Color(0.78f, 0.85f, 0.93f, 0.94f),
                        FontStyles.Bold,
                        TextAlignmentOptions.Left);
                }
            }
            else
            {
                CreateInlineText(
                    top,
                    "MissingStats",
                    "Stat data unavailable for this unit.",
                    11f,
                    new Color(1f, 0.76f, 0.56f, 0.98f),
                    FontStyles.Bold,
                    TextAlignmentOptions.Left);
            }

        }

        void CreateBuildingOverviewSection(MLLaneSnap lane)
        {
            CreateSectionHeader("Human Tech Tree");
            if ((lane.fortressPads == null || lane.fortressPads.Length == 0)
                && (lane.barracksSites == null || lane.barracksSites.Length == 0))
            {
                CreateInfoCard("No building data is available yet.");
                return;
            }

            CreateBuildingOverviewSummaryCard(lane);
            CreateHumanTechTreeBranchSection(lane);
            CreateBarracksInstanceOverviewSection(lane, "Barracks Summoning");
        }

        void CreateHumanTechTreeBranchSection(MLLaneSnap lane)
        {
            CreateSectionHeader("Building Branches");
            string[] orderedBranchTypes =
            {
                "town_core",
                "blacksmith",
                "temple",
                "wizard_tower",
                "archery_tower",
                "market",
                "stable",
                "workshop",
                "library",
                "lumber_mill",
            };

            bool createdAny = false;
            for (int i = 0; i < orderedBranchTypes.Length; i++)
            {
                var pad = FindFortressPadByBuildingType(lane, orderedBranchTypes[i]);
                if (pad == null)
                    continue;

                CreateTechBranchCard(lane, pad);
                createdAny = true;
            }

            if (!createdAny)
                CreateInfoCard("No branch data is available for this lane yet.");
        }

        void CreateTechBranchCard(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null)
                return;

            var card = CreateCardContainer();
            TintCard(card, ResolvePadCardTint(pad));
            WireOverviewCardNavigation(card, () => OpenOverviewPad(lane, pad));
            CreateCardTitle(card, BuildTechBranchTitle(pad));
            CreateBodyText(card, BuildTechBranchSummary(lane, pad));

            var tiers = CreateVerticalBlock(card, "TierTrack", 6f);
            int maxTier = Mathf.Max(1, pad.maxTier);
            for (int tier = 1; tier <= maxTier; tier++)
                CreateTechTierCard(tiers, lane, pad, tier);

            if (string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase))
                CreateTechHeroDependencyStrip(card, lane);
        }

        void CreateTechTierCard(Transform parent, MLLaneSnap lane, MLFortressPad pad, int tier)
        {
            if (parent == null || pad == null)
                return;

            var go = new GameObject("TierCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = ResolveTechTierBackground(pad, tier);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var element = go.GetComponent<LayoutElement>();
            element.minHeight = IsCompactPanelLayout() ? 50f : 58f;

            var header = CreateHorizontalFillBlock(go.transform, "TierHeader", 6f);
            var title = CreateInlineText(
                header,
                "TierTitle",
                $"{GetTechTierLabel(pad, tier)}",
                IsCompactPanelLayout() ? 11f : 12.5f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            var titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;

            CreateStatusChip(
                header,
                ResolveTechTierStateLabel(pad, tier),
                ResolveTechTierChipColor(pad, tier),
                ResolveTechTierChipTextColor(pad, tier));

            CreateInlineText(
                go.transform,
                "TierBody",
                BuildTechTierUnlockText(lane, pad, tier),
                IsCompactPanelLayout() ? 10.5f : 12f,
                new Color(0.86f, 0.90f, 0.95f, 0.96f),
                FontStyles.Normal,
                TextAlignmentOptions.Left);
        }

        void CreateTechHeroDependencyStrip(Transform parent, MLLaneSnap lane)
        {
            if (parent == null)
                return;

            var block = CreateVerticalBlock(parent, "HeroUnlockStrip", 6f);
            CreateInlineText(
                block,
                "HeroHeader",
                "Castle unlocks hero summons at the Barracks.",
                IsCompactPanelLayout() ? 11f : 12f,
                new Color(0.98f, 0.91f, 0.68f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Left);

            if (lane?.heroRoster == null || lane.heroRoster.Length == 0)
            {
                CreateInlineText(
                    block,
                    "HeroMissing",
                    "Hero data is not available yet.",
                    IsCompactPanelLayout() ? 10f : 11f,
                    new Color(0.84f, 0.88f, 0.93f, 0.92f),
                    FontStyles.Normal,
                    TextAlignmentOptions.Left);
                return;
            }

            var row = CreateHorizontalFillBlock(block, "HeroRow", 6f);
            var ordered = (MLHeroRosterEntry[])lane.heroRoster.Clone();
            System.Array.Sort(ordered, CompareHeroEntries);
            for (int i = 0; i < ordered.Length; i++)
            {
                var hero = ordered[i];
                if (hero == null)
                    continue;

                CreateTechHeroBadge(row, hero);
            }
        }

        void CreateTechHeroBadge(Transform parent, MLHeroRosterEntry hero)
        {
            if (parent == null || hero == null)
                return;

            var go = new GameObject("HeroBadge", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = ResolveHeroBadgeBackground(hero);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var element = go.GetComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = IsCompactPanelLayout() ? 50f : 58f;

            CreateInlineText(
                go.transform,
                "HeroName",
                hero.displayName,
                IsCompactPanelLayout() ? 10.5f : 11.5f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            CreateInlineText(
                go.transform,
                "HeroState",
                BuildHeroOverviewStateText(hero),
                IsCompactPanelLayout() ? 9.5f : 10.5f,
                ResolveHeroBadgeTextColor(hero),
                FontStyles.Bold,
                TextAlignmentOptions.Left);
        }

        void CreateFocusedPadSection(MLLaneSnap lane)
        {
            var pad = GetFocusedPad(lane);
            if (pad == null)
                return;

            if (string.Equals(pad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase))
            {
                CreateSectionHeader("Barracks Instances");
                CreateInfoCard("Choose Center, Left, or Right Barracks. Town Core handles barracks purchases and upgrades; the barracks screens handle unit buying.");
                CreateBarracksInstanceOverviewSection(lane, null);
                return;
            }

            if (string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase))
            {
                CreateTownCoreCommandSection(lane, pad);
                return;
            }

            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                CreateSectionHeader("Market");
                CreatePanelTemplateRow(_contentRoot, BuildFocusedMarketSummaryRowData(lane, pad));
                return;
            }

            CreateSectionHeader(string.IsNullOrWhiteSpace(pad.buildingName) ? "Building" : pad.buildingName);
            CreatePanelTemplateRow(_contentRoot, BuildFocusedPadSummaryRowData(lane, pad));

            CreateFocusedPadUpgradeSections(lane, pad);

            if (SupportsBarracksPurchaseFlow(lane, pad) && pad.isBuilt)
                CreatePurchaseInBarracksSection(lane, pad);
        }

        void CreateTownCoreCommandSection(MLLaneSnap lane, MLFortressPad townCorePad)
        {
            if (lane == null || townCorePad == null)
                return;

            var orderedBarracks = new List<MLBarracksSite>();
            string[] orderedBarracksIds = { "center", "left", "right" };
            for (int i = 0; i < orderedBarracksIds.Length; i++)
            {
                var site = FindBarracksSiteById(lane, orderedBarracksIds[i]);
                if (site != null)
                    orderedBarracks.Add(site);
            }
            var buildingTypes = GetTownCoreVisibleBuildingTypes(lane);

            CreateSectionHeader("Town Core");
            CreateTownCoreSummaryCard(lane, townCorePad, orderedBarracks, buildingTypes);

            var militaryTypes = GetTownCoreBuildingTypesForSection(buildingTypes, "Military");
            if (orderedBarracks.Count > 0 || militaryTypes.Count > 0)
            {
                CreateSectionHeader("Military");
                CreateTownCoreCardRows(
                    orderedBarracks,
                    "TownCoreMilitaryBarracksRow",
                    (parent, site) => CreateTownCoreBarracksCard(parent, lane, site));
                CreateTownCoreBuildingRows(lane, militaryTypes, "TownCoreMilitaryBuildingRow");
            }

            CreateTownCoreBuildingSection(lane, buildingTypes, "Economy");
            CreateTownCoreBuildingSection(lane, buildingTypes, "Knowledge");
            CreateTownCoreBuildingSection(lane, buildingTypes, "Defense");
            CreateTownCoreBuildingSection(lane, buildingTypes, "Additional Branches");
        }

        PanelRowTemplateData BuildTownCoreSummaryRowData(MLLaneSnap lane, MLFortressPad townCorePad, int builtBarracks, int totalBarracks, int builtBuildings, int totalBuildings)
        {
            if (townCorePad == null)
                return null;

            string tierName = ResolveBuildingTierName("town_core", Mathf.Max(1, townCorePad.tier), townCorePad.currentTierName);
            var row = new PanelRowTemplateData
            {
                ObjectName = BuildTownCorePadRowObjectName(townCorePad.padId),
                Eyebrow = "TOWN CORE",
                Title = $"Town Core - {tierName}",
                StatusText = ResolveTownCorePadStatusLabel(townCorePad),
                StatusColor = ResolveTownCoreStatusPillColor(
                    townCorePad.buildState,
                    townCorePad.canBuild,
                    townCorePad.canUpgrade,
                    townCorePad.isBuilt),
                Description = BuildTownCoreCommandSummary(lane, townCorePad, builtBarracks, totalBarracks, builtBuildings, totalBuildings),
                BackgroundColor = new Color(0.10f, 0.10f, 0.12f, 0.98f),
                AccentColor = GoldAccentColor,
                Highlighted = false,
                MinHeight = IsCompactPanelLayout() ? 148f : 168f,
            };

            string healthPill = BuildTownCoreHealthPill(townCorePad);
            if (!string.IsNullOrWhiteSpace(healthPill))
                row.Pills.Add(CreatePanelRowPill(healthPill, new Color(0.28f, 0.18f, 0.16f, 0.98f), new Color(0.98f, 0.88f, 0.82f, 0.98f)));

            string nextUnlockPill = BuildTownCoreNextUnlockPill(townCorePad);
            if (!string.IsNullOrWhiteSpace(nextUnlockPill))
                row.Pills.Add(CreatePanelRowPill(nextUnlockPill, GoldSurfaceColor, GoldTextColor));

            row.Pills.Add(CreatePanelRowPill($"Barracks {builtBarracks}/{totalBarracks}", GunmetalColor, SilverTextColor));
            row.Pills.Add(CreatePanelRowPill($"Branches {builtBuildings}/{totalBuildings}", GunmetalSoftColor, SilverTextColor));

            if (townCorePad.canUpgrade)
            {
                bool canAfford = CanSpendGold(lane, townCorePad.upgradeCost);
                int missingGold = lane != null ? Mathf.Max(0, townCorePad.upgradeCost - Mathf.FloorToInt(lane.gold)) : 0;
                string label = canAfford
                    ? $"Upgrade {Mathf.Max(0, townCorePad.upgradeCost)}g"
                    : $"Need {missingGold}g";
                row.PrimaryAction = CreatePanelRowAction(label, () =>
                {
                    _statusMessage = $"Upgrading {townCorePad.buildingName}...";
                    ActionSender.UpgradeBuilding(townCorePad.padId, townCorePad.buildingType);
                    RefreshHeader(force: true);
                }, canAfford, highlighted: true, objectName: BuildTownCorePadActionObjectName("Primary", townCorePad.padId));
            }
            else
            {
                string disabledLabel = string.Equals(townCorePad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase)
                    ? "Max Tier"
                    : string.Equals(townCorePad.buildState, "upgrading", System.StringComparison.OrdinalIgnoreCase)
                        ? "Upgrading"
                        : ResolveTownCorePadStatusLabel(townCorePad);
                row.PrimaryAction = CreatePanelRowAction(disabledLabel, null, false, objectName: BuildTownCorePadActionObjectName("Primary", townCorePad.padId));
            }

            return row;
        }

        PanelRowTemplateData BuildTownCoreBarracksRowData(MLLaneSnap lane, MLBarracksSite site)
        {
            if (lane == null || site == null)
                return null;

            bool highlighted = string.Equals(_guidedUnlockBarracksId, NormalizeBarracksId(site.barracksId), System.StringComparison.OrdinalIgnoreCase);
            var row = new PanelRowTemplateData
            {
                ObjectName = BuildTownCoreBarracksRowObjectName(site.barracksId),
                Eyebrow = "BARRACKS",
                Title = ResolveBarracksDisplayName(site),
                StatusText = ResolveTownCoreBarracksStatusLabel(site),
                StatusColor = ResolveTownCoreStatusPillColor(site.buildState, site.canBuild, site.canUpgrade, site.isBuilt),
                Description = BuildTownCoreBarracksBody(lane, site),
                BackgroundColor = highlighted
                    ? new Color(0.18f, 0.15f, 0.10f, 0.98f)
                    : ResolveBarracksOverviewCardTint(site),
                AccentColor = ResolveTownCoreAccentColor("barracks"),
                Highlighted = highlighted,
                MinHeight = IsCompactPanelLayout() ? 132f : 148f,
            };

            string tierPill = BuildTownCoreBarracksTierPill(site);
            if (!string.IsNullOrWhiteSpace(tierPill))
                row.Pills.Add(CreatePanelRowPill(tierPill, GunmetalColor, SilverTextColor));

            string gatePill = BuildTownCoreBarracksGatePill(lane, site);
            if (!string.IsNullOrWhiteSpace(gatePill))
                row.Pills.Add(CreatePanelRowPill(gatePill, GoldSurfaceColor, GoldTextColor));

            string costPill = BuildTownCoreBarracksCostPill(site);
            if (!string.IsNullOrWhiteSpace(costPill))
                row.Pills.Add(CreatePanelRowPill(costPill, GoldSurfaceBrightColor, GoldTextColor));

            if (site.isConstructing)
            {
                row.PrimaryAction = CreatePanelRowAction(
                    BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site)),
                    null,
                    false,
                    objectName: BuildTownCoreBarracksActionObjectName("Primary", site.barracksId));
            }
            else if (!site.isBuilt && site.canBuild)
            {
                bool canAfford = CanSpendGold(lane, site.buildCost);
                int missingGold = lane != null ? Mathf.Max(0, site.buildCost - Mathf.FloorToInt(lane.gold)) : 0;
                string label = canAfford
                    ? $"Purchase {Mathf.Max(0, site.buildCost)}g"
                    : $"Need {missingGold}g";
                row.PrimaryAction = CreatePanelRowAction(
                    label,
                    () => ExecuteFocusedBarracksBuild(site),
                    canAfford,
                    highlighted: true,
                    objectName: BuildTownCoreBarracksActionObjectName("Primary", site.barracksId));
            }
            else if (site.isBuilt)
            {
                row.PrimaryAction = CreatePanelRowAction(
                    "Open Barracks",
                    () => OpenOverviewBarracks(lane, site),
                    true,
                    objectName: BuildTownCoreBarracksActionObjectName("Primary", site.barracksId));
                row.SecondaryAction = BuildTownCoreBarracksSecondaryAction(lane, site);
            }
            else
            {
                row.PrimaryAction = CreatePanelRowAction(
                    "Locked",
                    null,
                    false,
                    objectName: BuildTownCoreBarracksActionObjectName("Primary", site.barracksId));
            }

            return row;
        }

        PanelRowActionData BuildTownCoreBarracksSecondaryAction(MLLaneSnap lane, MLBarracksSite site)
        {
            if (lane == null || site == null || !site.isBuilt)
                return null;

            if (site.canUpgrade)
            {
                bool canAfford = CanSpendGold(lane, site.upgradeCost);
                int missingGold = Mathf.Max(0, site.upgradeCost - Mathf.FloorToInt(lane.gold));
                string label = canAfford
                    ? $"Upgrade {Mathf.Max(0, site.upgradeCost)}g"
                    : $"Need {missingGold}g";
                return CreatePanelRowAction(
                    label,
                    canAfford ? () => ExecuteFocusedBarracksUpgrade(site) : null,
                    canAfford,
                    highlighted: true,
                    objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));
            }

            if (string.Equals(site.buildState, "upgrading", System.StringComparison.OrdinalIgnoreCase))
                return CreatePanelRowAction("Upgrading", null, false, objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));
            if (string.Equals(site.buildState, "under_repair", System.StringComparison.OrdinalIgnoreCase))
                return CreatePanelRowAction("Under Repair", null, false, objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));
            if (string.Equals(site.buildState, "destroyed", System.StringComparison.OrdinalIgnoreCase))
                return CreatePanelRowAction("Destroyed", null, false, objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));
            if (site.level >= Mathf.Max(1, site.maxLevel))
                return CreatePanelRowAction("Max Level", null, false, objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));
            if (!string.IsNullOrWhiteSpace(site.lockedReason))
                return CreatePanelRowAction("Upgrade Locked", null, false, objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));

            return CreatePanelRowAction("Upgrade", null, false, objectName: BuildTownCoreBarracksActionObjectName("Secondary", site.barracksId));
        }

        PanelRowTemplateData BuildTownCorePadRowData(MLLaneSnap lane, MLFortressBuildingConfig config, MLFortressPad pad)
        {
            if (lane == null || (config == null && pad == null))
                return null;

            string buildingType = !string.IsNullOrWhiteSpace(pad?.buildingType)
                ? pad.buildingType
                : config.buildingType;
            string displayName = !string.IsNullOrWhiteSpace(pad?.displayName)
                ? pad.displayName
                : !string.IsNullOrWhiteSpace(pad?.buildingName)
                    ? pad.buildingName
                    : !string.IsNullOrWhiteSpace(config?.displayName)
                        ? config.displayName
                        : HumanizeCombatType(buildingType);
            bool highlighted = !string.IsNullOrWhiteSpace(_guidedUnlockPadId)
                && string.Equals(_guidedUnlockPadId, pad?.padId, System.StringComparison.OrdinalIgnoreCase);
            var row = new PanelRowTemplateData
            {
                ObjectName = pad != null ? BuildTownCorePadRowObjectName(pad.padId) : null,
                Eyebrow = "BUILDING BRANCH",
                Title = displayName,
                StatusText = ResolveTownCorePadStatusLabel(pad),
                StatusColor = ResolveTownCoreStatusPillColor(
                    pad?.buildState,
                    pad != null && pad.canBuild,
                    pad != null && pad.canUpgrade,
                    pad != null && pad.isBuilt),
                Description = BuildTownCorePadBody(lane, pad, config, lane.barracksRoster, lane.heroRoster),
                BackgroundColor = highlighted
                    ? new Color(0.18f, 0.15f, 0.10f, 0.98f)
                    : ResolveTownCoreCardColor(pad),
                AccentColor = ResolveTownCoreAccentColor(buildingType),
                Highlighted = highlighted,
                MinHeight = IsCompactPanelLayout() ? 132f : 148f,
            };

            string tierPill = BuildTownCorePadTierPill(pad, config);
            if (!string.IsNullOrWhiteSpace(tierPill))
                row.Pills.Add(CreatePanelRowPill(tierPill, GunmetalColor, SilverTextColor));

            string gatePill = BuildTownCorePadGatePill(lane, pad, config);
            if (!string.IsNullOrWhiteSpace(gatePill))
                row.Pills.Add(CreatePanelRowPill(gatePill, GoldSurfaceColor, GoldTextColor));

            string costPill = BuildTownCorePadCostPill(pad, config);
            if (!string.IsNullOrWhiteSpace(costPill))
                row.Pills.Add(CreatePanelRowPill(costPill, GoldSurfaceBrightColor, GoldTextColor));

            if (pad != null && !pad.isBuilt && pad.canBuild)
            {
                bool canAfford = CanSpendGold(lane, pad.buildCost);
                int missingGold = lane != null ? Mathf.Max(0, pad.buildCost - Mathf.FloorToInt(lane.gold)) : 0;
                string label = canAfford
                    ? $"Purchase {pad.buildCost}g"
                    : $"Need {missingGold}g";
                row.PrimaryAction = CreatePanelRowAction(label, () =>
                {
                    _statusMessage = $"Building {pad.buildingName}...";
                    ActionSender.BuildOnPad(pad.padId);
                    RefreshHeader(force: true);
                }, canAfford, highlighted: true, objectName: BuildTownCorePadActionObjectName("Primary", pad.padId));
            }
            else if (pad != null && pad.isBuilt)
            {
                row.PrimaryAction = CreatePanelRowAction("Open Branch", () => OpenOverviewPad(lane, pad), true, objectName: BuildTownCorePadActionObjectName("Primary", pad.padId));
                row.SecondaryAction = BuildTownCorePadSecondaryAction(lane, pad);
            }
            else
            {
                row.PrimaryAction = CreatePanelRowAction("Locked", null, false, objectName: pad != null ? BuildTownCorePadActionObjectName("Primary", pad.padId) : null);
            }

            return row;
        }

        PanelRowActionData BuildTownCorePadSecondaryAction(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null || !pad.isBuilt)
                return null;

            if (pad.canUpgrade)
            {
                bool canAfford = CanSpendGold(lane, pad.upgradeCost);
                int missingGold = Mathf.Max(0, pad.upgradeCost - Mathf.FloorToInt(lane.gold));
                string label = canAfford
                    ? $"Upgrade {pad.upgradeCost}g"
                    : $"Need {missingGold}g";
                return CreatePanelRowAction(
                    label,
                    canAfford
                        ? () =>
                        {
                            _statusMessage = $"Upgrading {pad.buildingName}...";
                            ActionSender.UpgradeBuilding(pad.padId, pad.buildingType);
                            RefreshHeader(force: true);
                        }
                        : null,
                    canAfford,
                    highlighted: true,
                    objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));
            }

            if (string.Equals(pad.buildState, "upgrading", System.StringComparison.OrdinalIgnoreCase))
                return CreatePanelRowAction("Upgrading", null, false, objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));
            if (string.Equals(pad.buildState, "under_repair", System.StringComparison.OrdinalIgnoreCase))
                return CreatePanelRowAction("Under Repair", null, false, objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));
            if (string.Equals(pad.buildState, "destroyed", System.StringComparison.OrdinalIgnoreCase))
                return CreatePanelRowAction("Destroyed", null, false, objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));
            if (pad.tier >= Mathf.Max(1, pad.maxTier))
                return CreatePanelRowAction("Max Tier", null, false, objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));
            if (!string.IsNullOrWhiteSpace(pad.lockedReason))
                return CreatePanelRowAction("Upgrade Locked", null, false, objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));

            return CreatePanelRowAction("Upgrade", null, false, objectName: BuildTownCorePadActionObjectName("Secondary", pad.padId));
        }

        void CreateFocusedPadUpgradeSections(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null || pad.buildingUpgrades == null || pad.buildingUpgrades.Length == 0)
                return;

            var repeatable = new List<MLBuildingUpgrade>();
            var oneTime = new List<MLBuildingUpgrade>();
            for (int i = 0; i < pad.buildingUpgrades.Length; i++)
            {
                var upgrade = pad.buildingUpgrades[i];
                if (upgrade == null)
                    continue;
                if (upgrade.isOneTime || string.Equals(upgrade.section, "one_time", System.StringComparison.OrdinalIgnoreCase))
                    oneTime.Add(upgrade);
                else
                    repeatable.Add(upgrade);
            }

            repeatable.Sort(CompareBuildingUpgradeEntries);
            oneTime.Sort(CompareBuildingUpgradeEntries);

            if (repeatable.Count > 0)
            {
                CreateSectionHeader("Repeatable Upgrades");
                for (int i = 0; i < repeatable.Count; i++)
                    CreatePanelTemplateRow(_contentRoot, BuildFocusedPadUpgradeRowData(lane, pad, repeatable[i]));
            }

            if (oneTime.Count > 0)
            {
                CreateSectionHeader("One-Time Unlocks");
                for (int i = 0; i < oneTime.Count; i++)
                    CreatePanelTemplateRow(_contentRoot, BuildFocusedPadUpgradeRowData(lane, pad, oneTime[i]));
            }
        }

        static int CompareBuildingUpgradeEntries(MLBuildingUpgrade left, MLBuildingUpgrade right)
        {
            if (left == null && right == null)
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int sortDelta = left.sortIndex.CompareTo(right.sortIndex);
            if (sortDelta != 0)
                return sortDelta;
            return string.Compare(left.upgradeKey, right.upgradeKey, System.StringComparison.OrdinalIgnoreCase);
        }

        PanelRowTemplateData BuildFocusedPadUpgradeRowData(MLLaneSnap lane, MLFortressPad pad, MLBuildingUpgrade upgrade)
        {
            if (lane == null || pad == null || upgrade == null)
                return null;

            bool isPurchasedOneTime = upgrade.isOneTime && upgrade.isPurchased;
            bool canAfford = upgrade.cost <= Mathf.FloorToInt(lane.gold);
            int missingGold = Mathf.Max(0, upgrade.cost - Mathf.FloorToInt(lane.gold));
            string statusText = isPurchasedOneTime
                ? "Unlocked"
                : upgrade.canPurchase
                    ? (upgrade.isRepeatable ? "Ready" : "Available")
                    : !string.IsNullOrWhiteSpace(upgrade.lockedReason) && upgrade.lockedReason.StartsWith("Need", System.StringComparison.OrdinalIgnoreCase)
                        ? "Need Gold"
                        : "Locked";
            Color statusColor = isPurchasedOneTime
                ? SilverAccentColor
                : upgrade.canPurchase
                    ? GoldSurfaceColor
                    : GunmetalColor;
            var row = new PanelRowTemplateData
            {
                Eyebrow = upgrade.isRepeatable ? "REPEATABLE" : "ONE-TIME",
                Title = string.IsNullOrWhiteSpace(upgrade.upgradeName) ? "Upgrade" : upgrade.upgradeName,
                StatusText = statusText,
                StatusColor = statusColor,
                Description = BuildFocusedPadUpgradeDescription(upgrade),
                BackgroundColor = upgrade.isRepeatable
                    ? ObsidianSurfaceColor
                    : ObsidianElevatedColor,
                AccentColor = ResolveTownCoreAccentColor(pad.buildingType),
                MinHeight = IsCompactPanelLayout() ? 142f : 156f,
            };

            if (!string.IsNullOrWhiteSpace(upgrade.affectedLabel))
            {
                row.Pills.Add(CreatePanelRowPill(
                    upgrade.affectedLabel,
                    GunmetalColor,
                    SilverTextColor));
            }

            if (!string.IsNullOrWhiteSpace(upgrade.currentBonusText))
            {
                row.Pills.Add(CreatePanelRowPill(
                    $"Current {upgrade.currentBonusText}",
                    GoldSurfaceBrightColor,
                    GoldTextColor));
            }

            if (upgrade.cost > 0)
            {
                row.Pills.Add(CreatePanelRowPill(
                    $"{upgrade.cost}g",
                    new Color(0.27f, 0.20f, 0.10f, 0.98f),
                    new Color(0.97f, 0.86f, 0.62f, 0.98f)));
            }

            if (upgrade.canPurchase)
            {
                string label = upgrade.isRepeatable
                    ? $"Upgrade {upgrade.cost}g"
                    : $"Purchase {upgrade.cost}g";
                row.PrimaryAction = CreatePanelRowAction(
                    label,
                    () =>
                    {
                        _statusMessage = $"{pad.buildingName}: buying {upgrade.upgradeName}...";
                        ActionSender.PurchaseBuildingUpgrade(pad.padId, upgrade.upgradeKey);
                        RefreshHeader(force: true);
                    },
                    true,
                    highlighted: true);
            }
            else
            {
                string lockedLabel = isPurchasedOneTime
                    ? "Purchased"
                    : !pad.isBuilt
                        ? "Build First"
                        : !canAfford && upgrade.cost > 0
                            ? $"Need {missingGold}g"
                            : "Locked";
                row.PrimaryAction = CreatePanelRowAction(lockedLabel, null, false);
            }

            return row;
        }

        string BuildFocusedPadUpgradeDescription(MLBuildingUpgrade upgrade)
        {
            if (upgrade == null)
                return string.Empty;

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(upgrade.description))
                builder.Append(upgrade.description);
            if (!string.IsNullOrWhiteSpace(upgrade.currentBonusText))
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append($"Current Bonus: {upgrade.currentBonusText}");
            }
            if (upgrade.isRepeatable && !string.IsNullOrWhiteSpace(upgrade.nextBonusText))
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append($"Next Bonus: {upgrade.nextBonusText}");
            }
            if (!string.IsNullOrWhiteSpace(upgrade.lockedReason) && !upgrade.canPurchase && !(upgrade.isOneTime && upgrade.isPurchased))
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(upgrade.lockedReason);
            }
            return builder.ToString();
        }

        PanelRowTemplateData BuildFocusedPadSummaryRowData(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null)
                return null;

            bool hasGuidedUnlock = TryGetGuidedUnlockForPad(pad, out var guidedAction, out string guidedHelperText);
            var row = new PanelRowTemplateData
            {
                ObjectName = BuildFocusedPadRowObjectName(pad.padId),
                Eyebrow = "BUILDING BRANCH",
                Title = string.IsNullOrWhiteSpace(pad.displayName) ? pad.buildingName : pad.displayName,
                StatusText = ResolveTownCorePadStatusLabel(pad),
                StatusColor = ResolveTownCoreStatusPillColor(pad.buildState, pad.canBuild, pad.canUpgrade, pad.isBuilt),
                Description = BuildFocusedPadSummaryDescription(lane, pad, guidedHelperText),
                BackgroundColor = ResolvePadCardTint(pad),
                AccentColor = ResolveTownCoreAccentColor(pad.buildingType),
                Highlighted = hasGuidedUnlock,
                MinHeight = IsCompactPanelLayout() ? 150f : 168f,
            };

            string tierPill = BuildTownCorePadTierPill(pad, null);
            if (!string.IsNullOrWhiteSpace(tierPill))
                row.Pills.Add(CreatePanelRowPill(tierPill, GunmetalColor, SilverTextColor));

            string healthPill = BuildFocusedPadHealthPill(pad);
            if (!string.IsNullOrWhiteSpace(healthPill))
                row.Pills.Add(CreatePanelRowPill(healthPill, GunmetalSoftColor, SilverTextColor));

            string costPill = BuildTownCorePadCostPill(pad, null);
            if (!string.IsNullOrWhiteSpace(costPill))
                row.Pills.Add(CreatePanelRowPill(costPill, GoldSurfaceBrightColor, GoldTextColor));

            string unlockPill = BuildFocusedPadUnlockPill(lane, pad);
            if (!string.IsNullOrWhiteSpace(unlockPill))
                row.Pills.Add(CreatePanelRowPill(unlockPill, GoldSurfaceColor, GoldTextColor));

            row.PrimaryAction = BuildFocusedPadPrimaryAction(lane, pad, guidedAction);
            row.SecondaryAction = BuildFocusedPadSecondaryAction(lane, pad, guidedAction);
            return row;
        }

        string BuildFocusedPadSummaryDescription(MLLaneSnap lane, MLFortressPad pad, string guidedHelperText)
        {
            if (pad == null)
                return "No building pad selected.";

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(pad.upgradePanelDescription))
                lines.Add(pad.upgradePanelDescription);

            string body = BuildFocusedPadCardBody(lane, pad, lane?.barracksRoster, lane?.heroRoster);
            if (!string.IsNullOrWhiteSpace(body))
                lines.Add(body);

            if (!string.IsNullOrWhiteSpace(guidedHelperText))
                lines.Add(guidedHelperText);

            return string.Join("\n", lines);
        }

        string BuildFocusedPadHealthPill(MLFortressPad pad)
        {
            if (pad == null || pad.maxHp <= 0f || !pad.isBuilt)
                return null;

            return $"Health {Mathf.RoundToInt(Mathf.Max(0f, pad.hp))}/{Mathf.RoundToInt(Mathf.Max(0f, pad.maxHp))}";
        }

        string BuildFocusedPadUnlockPill(MLLaneSnap lane, MLFortressPad pad)
        {
            if (pad == null)
                return null;

            if (SupportsBarracksPurchaseFlow(lane, pad) && pad.isBuilt)
                return "Use Units At Barracks";

            if (pad.canUpgrade)
            {
                string nextTierName = ResolveBuildingTierName(
                    pad.buildingType,
                    pad.nextTier > 0 ? pad.nextTier : Mathf.Max(1, pad.tier + 1),
                    pad.nextTierName);
                return string.IsNullOrWhiteSpace(nextTierName) ? "Upgrade Ready" : $"Next {nextTierName}";
            }

            return null;
        }

        PanelRowActionData BuildFocusedPadPrimaryAction(MLLaneSnap lane, MLFortressPad pad, GuidedPadAction guidedAction)
        {
            if (pad == null)
                return null;

            if (!pad.isBuilt || pad.canBuild || pad.canUpgrade)
            {
                return CreatePanelRowAction(
                    "Open Town Core",
                    lane != null ? () => OpenTownCore(lane) : null,
                    lane != null,
                    highlighted: guidedAction != GuidedPadAction.None,
                    objectName: BuildFocusedPadActionObjectName("Primary", pad.padId));
            }

            if (SupportsBarracksPurchaseFlow(lane, pad))
            {
                return CreatePanelRowAction(
                    "Open Barracks",
                    null,
                    false,
                    objectName: BuildFocusedPadActionObjectName("Primary", pad.padId));
            }

            string label = pad.isBuilt && string.Equals(pad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase)
                ? "Max Tier"
                : "View";
            return CreatePanelRowAction(
                label,
                null,
                false,
                objectName: BuildFocusedPadActionObjectName("Primary", pad.padId));
        }

        PanelRowActionData BuildFocusedPadSecondaryAction(MLLaneSnap lane, MLFortressPad pad, GuidedPadAction guidedAction)
        {
            if (pad == null)
                return null;

            if (TryBuildLumberMillRepairAction(lane, pad, out string repairLabel, out bool repairEnabled, out _))
            {
                return CreatePanelRowAction(
                    repairLabel,
                    repairEnabled ? () => ExecuteLumberMillRepairAll(lane, pad) : null,
                    repairEnabled,
                    highlighted: repairEnabled,
                    objectName: BuildFocusedPadActionObjectName("Repair", pad.padId));
            }

            if (!string.IsNullOrWhiteSpace(pad.lockedReason))
            {
                return CreatePanelRowAction(
                    "Why",
                    () =>
                    {
                        _statusMessage = $"{pad.buildingName}: {pad.lockedReason}";
                        RefreshHeader(force: true);
                    },
                    true,
                    highlighted: guidedAction == GuidedPadAction.Explain,
                    objectName: BuildFocusedPadActionObjectName("Why", pad.padId));
            }

            return null;
        }

        PanelRowTemplateData BuildFocusedMarketSummaryRowData(MLLaneSnap lane, MLFortressPad pad)
        {
            if (pad == null)
                return null;

            var currentEntry = GetCurrentMarketRosterEntry(lane);
            var nextEntry = GetNextMarketRosterEntry(lane, currentEntry);
            var row = new PanelRowTemplateData
            {
                Eyebrow = "MARKET",
                Title = pad.buildingName,
                StatusText = ResolveTownCorePadStatusLabel(pad),
                StatusColor = ResolveTownCoreStatusPillColor(pad.buildState, pad.canBuild, pad.canUpgrade, pad.isBuilt),
                Description = BuildFocusedPadCardBody(
                    lane,
                    pad,
                    lane != null ? lane.barracksRoster : null,
                    lane != null ? lane.heroRoster : null),
                BackgroundColor = ResolvePadCardTint(pad),
                AccentColor = ResolveTownCoreAccentColor("market"),
                MinHeight = IsCompactPanelLayout() ? 150f : 168f,
            };

            string tierPill = BuildTownCorePadTierPill(pad, null);
            if (!string.IsNullOrWhiteSpace(tierPill))
                row.Pills.Add(CreatePanelRowPill(tierPill, GunmetalColor, SilverTextColor));

            string marketFood = BuildMarketFoodLabel(lane, pad);
            if (!string.IsNullOrWhiteSpace(marketFood))
                row.Pills.Add(CreatePanelRowPill(marketFood, GunmetalSoftColor, SilverTextColor));

            if (currentEntry != null)
            {
                row.Pills.Add(CreatePanelRowPill(currentEntry.displayName, GoldSurfaceColor, GoldTextColor));
                row.Pills.Add(CreatePanelRowPill($"+{Mathf.Max(0, currentEntry.economyLapGold)}g each", GoldSurfaceBrightColor, GoldTextColor));
            }
            else if (!pad.isBuilt && pad.buildCost > 0)
            {
                row.Pills.Add(CreatePanelRowPill($"{Mathf.Max(0, pad.buildCost)}g", GoldSurfaceBrightColor, GoldTextColor));
            }

            if (nextEntry != null
                && (currentEntry == null
                    || !string.Equals(nextEntry.unitKey, currentEntry.unitKey, System.StringComparison.OrdinalIgnoreCase)))
            {
                row.Pills.Add(CreatePanelRowPill($"Next {nextEntry.displayName}", GunmetalColor, SilverTextColor));
            }

            if (!pad.isBuilt || pad.canBuild || pad.canUpgrade)
            {
                row.PrimaryAction = CreatePanelRowAction(
                    "Open Town Core",
                    lane != null ? () => OpenTownCore(lane) : null,
                    lane != null,
                    highlighted: true,
                    objectName: BuildFocusedMarketActionObjectName("TownCore", nextEntry != null ? nextEntry.unitKey : currentEntry?.unitKey));
            }
            else if (currentEntry != null)
            {
                bool canBuy = CanBuyMarketWorker(lane, pad, currentEntry);
                row.PrimaryAction = CreatePanelRowAction(
                    BuildFocusedMarketBuyLabel(lane, pad, currentEntry),
                    canBuy ? () => ExecuteFocusedMarketBuy(currentEntry) : null,
                    canBuy,
                    highlighted: true,
                    objectName: BuildFocusedMarketActionObjectName("Buy", currentEntry.unitKey));
            }
            else
            {
                row.PrimaryAction = CreatePanelRowAction(
                    "No Contracts",
                    null,
                    false,
                    objectName: BuildFocusedMarketActionObjectName("Buy"));
            }

            row.SecondaryAction = BuildFocusedMarketSummarySecondaryAction(lane, pad, currentEntry, nextEntry);
            return row;
        }

        PanelRowActionData BuildFocusedMarketSummarySecondaryAction(MLLaneSnap lane, MLFortressPad pad, MLMarketRosterEntry currentEntry, MLMarketRosterEntry nextEntry)
        {
            if (pad == null)
                return null;

            if (pad.canUpgrade)
            {
                return CreatePanelRowAction(
                    nextEntry != null ? $"Upgrade {nextEntry.displayName}" : "Open Town Core",
                    lane != null ? () => OpenTownCore(lane) : null,
                    lane != null,
                    highlighted: true,
                    objectName: BuildFocusedMarketActionObjectName("Upgrade", nextEntry != null ? nextEntry.unitKey : currentEntry?.unitKey));
            }

            if (nextEntry != null
                && (currentEntry == null
                    || !string.Equals(nextEntry.unitKey, currentEntry.unitKey, System.StringComparison.OrdinalIgnoreCase)))
            {
                return CreatePanelRowAction(
                    $"Next {nextEntry.displayName}",
                    null,
                    false,
                    objectName: BuildFocusedMarketActionObjectName("Next", nextEntry.unitKey));
            }

            if (!string.IsNullOrWhiteSpace(pad.lockedReason))
            {
                return CreatePanelRowAction(
                    "Why",
                    () =>
                    {
                        _statusMessage = $"{pad.buildingName}: {pad.lockedReason}";
                        RefreshHeader(force: true);
                    },
                    true,
                    objectName: BuildFocusedMarketActionObjectName("Why", currentEntry?.unitKey));
            }

            return null;
        }

        PanelRowTemplateData BuildFocusedMarketEntryRowData(MLLaneSnap lane, MLFortressPad pad, MLMarketRosterEntry entry, MLMarketRosterEntry nextEntry)
        {
            if (entry == null)
                return null;

            string blockedReason = GetMarketBuyBlockedReason(lane, pad, entry);
            bool canBuy = CanBuyMarketWorker(lane, pad, entry);
            string statusText;
            Color statusColor;
            if (pad == null || !pad.isBuilt)
            {
                statusText = "Open Town Core";
                statusColor = GunmetalColor;
            }
            else if (pad.canUpgrade && nextEntry != null)
            {
                statusText = "Upgrade Ready";
                statusColor = GoldSurfaceColor;
            }
            else if (string.IsNullOrWhiteSpace(blockedReason))
            {
                statusText = "Ready";
                statusColor = GoldSurfaceBrightColor;
            }
            else if (blockedReason.IndexOf("Need", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusText = blockedReason.EndsWith("g", System.StringComparison.OrdinalIgnoreCase) ? "Need Gold" : blockedReason;
                statusColor = GoldSurfaceColor;
            }
            else if (blockedReason.IndexOf("Cap", System.StringComparison.OrdinalIgnoreCase) >= 0
                || blockedReason.IndexOf("Slot", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statusText = blockedReason;
                statusColor = GunmetalColor;
            }
            else
            {
                statusText = blockedReason;
                statusColor = GunmetalSoftColor;
            }

            var row = new PanelRowTemplateData
            {
                Eyebrow = "MARKET CONTRACT",
                Title = $"{entry.displayName} Tier {Mathf.Max(1, entry.tier)}",
                StatusText = statusText,
                StatusColor = statusColor,
                Description = BuildFocusedMarketEntryBody(entry, nextEntry, pad),
                BackgroundColor = pad != null && pad.canUpgrade
                    ? new Color(0.16f, 0.14f, 0.10f, 0.98f)
                    : new Color(0.11f, 0.12f, 0.15f, 0.98f),
                AccentColor = ResolveTownCoreAccentColor("market"),
                MinHeight = IsCompactPanelLayout() ? 142f : 156f,
            };

            row.Pills.Add(CreatePanelRowPill($"Owned x{Mathf.Max(0, entry.ownedCount)}", GunmetalColor, SilverTextColor));
            row.Pills.Add(CreatePanelRowPill($"Cost {Mathf.Max(0, entry.buyCost)}g", GoldSurfaceBrightColor, GoldTextColor));

            string marketFood = BuildMarketFoodLabel(lane, pad);
            if (!string.IsNullOrWhiteSpace(marketFood))
                row.Pills.Add(CreatePanelRowPill(marketFood, GunmetalSoftColor, SilverTextColor));

            row.Pills.Add(CreatePanelRowPill($"+{Mathf.Max(0, entry.economyLapGold)}g each", GoldSurfaceColor, GoldTextColor));

            row.PrimaryAction = CreatePanelRowAction(
                BuildFocusedMarketBuyLabel(lane, pad, entry),
                canBuy ? () => ExecuteFocusedMarketBuy(entry) : null,
                canBuy,
                highlighted: true,
                objectName: BuildFocusedMarketActionObjectName("Buy", entry.unitKey));

            if (pad != null && pad.canUpgrade)
            {
                row.SecondaryAction = CreatePanelRowAction(
                    nextEntry != null ? $"Upgrade {nextEntry.displayName}" : "Open Town Core",
                    lane != null ? () => OpenTownCore(lane) : null,
                    lane != null,
                    highlighted: true,
                    objectName: BuildFocusedMarketActionObjectName("Upgrade", nextEntry != null ? nextEntry.unitKey : entry.unitKey));
            }
            else if (nextEntry != null
                && !string.Equals(nextEntry.unitKey, entry.unitKey, System.StringComparison.OrdinalIgnoreCase))
            {
                row.SecondaryAction = CreatePanelRowAction(
                    $"Next {nextEntry.displayName}",
                    null,
                    false,
                    objectName: BuildFocusedMarketActionObjectName("Next", nextEntry.unitKey));
            }

            return row;
        }

        PanelRowTemplateData BuildFocusedBarracksSummaryRowData(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return null;

            var row = new PanelRowTemplateData
            {
                Eyebrow = "BARRACKS",
                Title = ResolveBarracksDisplayName(site),
                StatusText = ResolveTownCoreBarracksStatusLabel(site),
                StatusColor = ResolveTownCoreStatusPillColor(site.buildState, site.canBuild, site.canUpgrade, site.isBuilt),
                Description = BuildFocusedBarracksOverview(lane, site),
                BackgroundColor = site.isBuilt
                    ? new Color(0.14f, 0.12f, 0.10f, 0.98f)
                    : ObsidianElevatedColor,
                AccentColor = ResolveTownCoreAccentColor("barracks"),
                MinHeight = IsCompactPanelLayout() ? 144f : 158f,
            };

            if (site.isConstructing)
            {
                row.Pills.Add(CreatePanelRowPill(
                    BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site)),
                    new Color(0.36f, 0.24f, 0.10f, 0.98f),
                    new Color(0.98f, 0.92f, 0.74f, 0.98f)));
            }
            else if (!site.isBuilt)
            {
                row.Pills.Add(CreatePanelRowPill(
                    BuildFocusedBarracksCostText(site),
                    GunmetalColor,
                    SilverTextColor));
                row.Pills.Add(CreatePanelRowPill(
                    BuildFocusedBarracksRequirementText(site),
                    GoldSurfaceColor,
                    GoldTextColor));
            }
            else
            {
                row.Pills.Add(CreatePanelRowPill(
                    $"Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}",
                    GunmetalColor,
                    SilverTextColor));
                row.Pills.Add(CreatePanelRowPill(
                    $"Owned {CountOwnedUnits(site.roster)}",
                    GoldSurfaceBrightColor,
                    GoldTextColor));
                row.Pills.Add(CreatePanelRowPill(
                    BuildBarracksRosterFoodLabel(site),
                    GoldSurfaceColor,
                    GoldTextColor));
                string activeFoodLabel = BuildBarracksActiveFoodLabel(site);
                if (!string.IsNullOrWhiteSpace(activeFoodLabel))
                {
                    row.Pills.Add(CreatePanelRowPill(
                        activeFoodLabel,
                        GunmetalSoftColor,
                        SilverTextColor));
                }
                row.Pills.Add(CreatePanelRowPill(
                    $"Active {CountActiveUnitsForBarracks(lane, site)}",
                    GunmetalSoftColor,
                    SilverTextColor));

                int sendIntervalSeconds = GetFocusedBarracksSpawnIntervalSeconds(site);
                if (sendIntervalSeconds >= 0)
                {
                    row.Pills.Add(CreatePanelRowPill(
                        $"Sends {sendIntervalSeconds}s",
                        new Color(0.28f, 0.20f, 0.10f, 0.98f),
                        new Color(0.97f, 0.86f, 0.62f, 0.98f)));
                }
            }

            row.PrimaryAction = CreatePanelRowAction(
                "Open Town Core",
                lane != null ? () => OpenTownCore(lane) : null,
                lane != null,
                highlighted: true);
            return row;
        }

        PanelRowTemplateData BuildFocusedBarracksUnitRowData(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return null;

            bool isMilitia = string.Equals(entry.rosterKey, MilitiaRosterKey, System.StringComparison.OrdinalIgnoreCase);
            bool highlighted = !string.IsNullOrWhiteSpace(_guidedUnlockUnitKey)
                && string.Equals(_guidedUnlockUnitKey, entry.rosterKey, System.StringComparison.OrdinalIgnoreCase);
            bool hasStats = TryBuildFocusedBarracksUnitStatTiles(entry, out var statTiles);
            bool needsGuidance = !site.isBuilt || !entry.unlocked;
            var row = new PanelRowTemplateData
            {
                Eyebrow = BuildFocusedBarracksUnitEyebrow(entry),
                Title = entry.displayName,
                StatusText = ResolveFocusedBarracksUnitRowStatusLabel(lane, site, entry),
                StatusColor = ResolveFocusedBarracksUnitRowStatusColor(lane, site, entry),
                Description = BuildFocusedBarracksUnitRowBody(lane, site, entry),
                BackgroundColor = highlighted
                    ? Color.Lerp(ResolveFocusedBarracksCardTint(entry), new Color(0.95f, 0.76f, 0.34f, 0.98f), 0.18f)
                    : ResolveFocusedBarracksCardTint(entry),
                AccentColor = ResolveTownCoreAccentColor(!string.IsNullOrWhiteSpace(entry.productionBuildingType) ? entry.productionBuildingType : entry.unlockBuildingType),
                Highlighted = highlighted,
                MinHeight = hasStats
                    ? IsCompactPanelLayout()
                        ? (needsGuidance ? 194f : 176f)
                        : (needsGuidance ? 210f : 188f)
                    : (IsCompactPanelLayout() ? 146f : 158f),
            };

            row.Pills.Add(CreateFocusedBarracksSummaryPill($"Cost {Mathf.Max(0, entry.buyCost)}g"));
            row.Pills.Add(CreateFocusedBarracksSummaryPill($"Food {ResolveBarracksEntryFoodCost(entry)}"));
            row.Pills.Add(CreateFocusedBarracksSummaryPill($"Owned x{Mathf.Max(0, entry.ownedCount)}"));
            row.Pills.Add(CreateFocusedBarracksSummaryPill(
                site.isBuilt
                    ? $"Active x{CountActiveUnitsForRosterEntry(lane, site, entry)}"
                    : "Preview"));

            if (hasStats)
                row.Stats.AddRange(statTiles);

            if (!site.isBuilt)
            {
                row.PrimaryAction = CreatePanelRowAction(
                    "Open Town Core",
                    lane != null ? () => OpenTownCore(lane, entry, targetSite: site) : null,
                    lane != null,
                    highlighted: true);
            }
            else if (!entry.unlocked)
            {
                row.PrimaryAction = CreatePanelRowAction(
                    BuildLockedUnitRedirectActionLabel(entry),
                    lane != null ? () => RedirectLockedUnitToUnlockBuilding(lane, entry, site) : null,
                    lane != null,
                    highlighted: true);
            }
            else
            {
                bool canBuy = CanBuyBarracksRosterEntry(lane, site, entry);
                row.PrimaryAction = CreatePanelRowAction(
                    BuildFocusedBarracksBuyLabel(lane, site, entry),
                    canBuy ? () => ExecuteFocusedBarracksBuy(site, entry) : null,
                    canBuy,
                    highlighted: true,
                    objectName: BuildFocusedBarracksActionObjectName("Buy", entry.rosterKey));
            }

            row.SecondaryAction = BuildFocusedBarracksSecondaryAction(lane, site, entry, isMilitia);
            return row;
        }

        PanelRowActionData BuildFocusedBarracksSecondaryAction(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry, bool isMilitia)
        {
            if (site == null || entry == null)
                return null;

            bool canSell = CanSellBarracksRosterEntry(site, entry);
            if (isMilitia && !canSell)
            {
                bool canSingleBuy = CanBuyBarracksRosterEntry(lane, site, entry);
                return CreatePanelRowAction(
                    BuildFocusedBarracksBuyLabel(lane, site, entry),
                    canSingleBuy ? () => ExecuteFocusedBarracksBuy(site, entry) : null,
                    canSingleBuy,
                    objectName: BuildFocusedBarracksActionObjectName("Buy", entry.rosterKey));
            }

            return BuildFocusedBarracksSellAction(site, entry);
        }

        PanelRowActionData BuildFocusedBarracksSellAction(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return null;

            bool canSell = CanSellBarracksRosterEntry(site, entry);
            return CreatePanelRowAction(
                BuildFocusedBarracksSellLabel(site, entry),
                canSell ? () => ExecuteFocusedBarracksSell(site, entry) : null,
                canSell,
                objectName: BuildFocusedBarracksActionObjectName("Sell", entry.rosterKey));
        }

        PanelRowTemplateData BuildFocusedBarracksHeroRowData(MLLaneSnap lane, MLBarracksSite site, MLHeroRosterEntry hero)
        {
            if (hero == null)
                return null;

            var accent = ResolveHeroAccentColor(hero);
            var row = new PanelRowTemplateData
            {
                Eyebrow = "HERO",
                Title = hero.displayName,
                StatusText = BuildFocusedBarracksHeroStatusChip(hero),
                StatusColor = accent,
                Description = BuildFocusedBarracksHeroRowBody(lane, site, hero),
                BackgroundColor = ResolveHeroPanelCardTint(hero),
                AccentColor = accent,
                MinHeight = IsCompactPanelLayout() ? 142f : 156f,
            };

            if (!string.IsNullOrWhiteSpace(hero.unlockBuildingTierName))
            {
                row.Pills.Add(CreatePanelRowPill(
                    hero.unlockBuildingTierName,
                    GunmetalColor,
                    SilverTextColor));
            }

            if (!string.IsNullOrWhiteSpace(hero.summonSourceBuildingName))
            {
                row.Pills.Add(CreatePanelRowPill(
                    $"Summon {hero.summonSourceBuildingName}",
                    new Color(0.28f, 0.20f, 0.10f, 0.98f),
                    new Color(0.97f, 0.86f, 0.62f, 0.98f)));
            }

            row.Pills.Add(CreatePanelRowPill(
                $"Cost {Mathf.Max(0, hero.summonCost)}g",
                GoldSurfaceBrightColor,
                GoldTextColor));
            row.Pills.Add(CreatePanelRowPill(
                $"Active {Mathf.Max(0, hero.activeCount)}/{Mathf.Max(1, hero.activeLimit)}",
                GunmetalSoftColor,
                SilverTextColor));

            string heroState = (hero.state ?? string.Empty).Trim().ToLowerInvariant();
            if (site == null || !site.isBuilt || string.Equals(heroState, "locked", System.StringComparison.Ordinal))
            {
                row.PrimaryAction = CreatePanelRowAction(
                    "Open Town Core",
                    lane != null ? () => OpenTownCore(lane) : null,
                    lane != null,
                    highlighted: true);
            }
            else
            {
                bool canDeploy = CanDeployBarracksHero(lane, site, hero);
                row.PrimaryAction = CreatePanelRowAction(
                    BuildFocusedBarracksHeroDeployLabel(lane, site, hero),
                    canDeploy ? () => ExecuteFocusedBarracksHeroDeploy(site, hero) : null,
                    canDeploy,
                    highlighted: true);
            }

            return row;
        }

        string BuildFocusedBarracksUnitRowBody(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Unit data unavailable.";

            if (!site.isBuilt)
                return "Purchase this barracks in Town Core first. This row stays visible here so the lane roster is readable before it goes live.";

            if (!entry.unlocked)
                return BuildLockedUnitRedirectHint(entry);

            return TryGetUnitCardStats(entry, out _)
                ? string.Empty
                : "Stat data unavailable for this unit.";
        }

        string BuildFocusedBarracksHeroRowBody(MLLaneSnap lane, MLBarracksSite site, MLHeroRosterEntry hero)
        {
            if (hero == null)
                return "Hero data unavailable.";

            var lines = new List<string>
            {
                BuildFocusedBarracksHeroStateText(lane, site, hero)
            };

            if (TryGetHeroCardStats(hero, out var unit))
            {
                lines.Add(
                    $"HP {FormatStatNumber(unit.hp)}   ATK {FormatStatNumber(unit.attack_damage)}   Active {Mathf.Max(0, hero.activeCount)}/{Mathf.Max(1, hero.activeLimit)}");
            }

            return string.Join("\n", lines);
        }

        string ResolveFocusedBarracksUnitRowStatusLabel(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Waiting";

            if (!site.isBuilt)
                return site.canBuild ? "Town Core" : "Locked";

            if (!entry.unlocked)
                return "Locked";

            string blockedReason = GetBarracksBuyBlockedReason(lane, site, entry);
            if (!string.IsNullOrWhiteSpace(blockedReason))
                return blockedReason;

            return "Ready";
        }

        Color ResolveFocusedBarracksUnitRowStatusColor(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return GunmetalColor;

            if (!site.isBuilt || !entry.unlocked)
                return GoldSurfaceColor;

            if (!string.IsNullOrWhiteSpace(GetBarracksBuyBlockedReason(lane, site, entry)))
                return GunmetalSoftColor;

            return GoldSurfaceBrightColor;
        }

        void CreateTownCoreSummaryCard(MLLaneSnap lane, MLFortressPad townCorePad, IReadOnlyList<MLBarracksSite> orderedBarracks, IReadOnlyList<string> buildingTypes)
        {
            int builtBuildings = 0;
            if (buildingTypes != null)
            {
                for (int i = 0; i < buildingTypes.Count; i++)
                {
                    var pad = FindFortressPadByBuildingType(lane, buildingTypes[i]);
                    if (pad != null && pad.isBuilt)
                        builtBuildings += 1;
                }
            }

            int totalBuildings = buildingTypes != null ? buildingTypes.Count : 0;
            int builtBarracks = CountBuiltBarracksSites(lane?.barracksSites);
            int totalBarracks = orderedBarracks != null ? orderedBarracks.Count : 0;
            CreatePanelTemplateRow(
                _contentRoot,
                BuildTownCoreSummaryRowData(lane, townCorePad, builtBarracks, totalBarracks, builtBuildings, totalBuildings));
        }

        string BuildTownCoreCommandSummary(MLLaneSnap lane, MLFortressPad townCorePad, int builtBarracks, int totalBarracks, int builtBuildings, int totalBuildings)
        {
            var lines = new List<string>
            {
                $"Fortress Growth: {builtBarracks}/{totalBarracks} barracks and {builtBuildings}/{totalBuildings} building branches are online.",
                BuildTownCoreNextUnlockSummary(lane, townCorePad),
                "Town Core is your command ledger for fortress growth. Every branch stays visible here so locked progression is readable before it becomes purchasable."
            };

            if (!string.IsNullOrWhiteSpace(_guidedUnlockBuildingName))
            {
                string unitName = string.IsNullOrWhiteSpace(_guidedUnlockUnitName) ? "Locked unit" : _guidedUnlockUnitName;
                lines.Add($"{unitName} needs {_guidedUnlockBuildingName} Tier {Mathf.Max(1, _guidedUnlockRequiredTier)}. Use the highlighted entry below.");
            }

            return string.Join("\n", lines);
        }

        string BuildTownCoreHealthPill(MLFortressPad townCorePad)
        {
            if (townCorePad == null)
                return null;

            return $"Health {Mathf.CeilToInt(Mathf.Max(0f, townCorePad.hp))}/{Mathf.CeilToInt(Mathf.Max(0f, townCorePad.maxHp))}";
        }

        string BuildTownCoreNextUnlockPill(MLFortressPad townCorePad)
        {
            if (townCorePad == null)
                return null;

            if (string.Equals(townCorePad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase))
                return "Next Unlock: Max Tier";

            int nextTier = townCorePad.nextTier > 0
                ? townCorePad.nextTier
                : Mathf.Clamp(Mathf.Max(1, townCorePad.tier + 1), 1, Mathf.Max(1, townCorePad.maxTier));
            string nextTierName = ResolveBuildingTierName("town_core", nextTier, townCorePad.nextTierName);
            return string.IsNullOrWhiteSpace(nextTierName) ? null : $"Next Unlock: {nextTierName}";
        }

        string BuildTownCoreNextUnlockSummary(MLLaneSnap lane, MLFortressPad townCorePad)
        {
            if (townCorePad == null)
                return "Next unlock data is unavailable.";

            if (string.Equals(townCorePad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase))
                return "Castle is online. Town Core has reached its final tier.";

            int nextTier = townCorePad.nextTier > 0
                ? townCorePad.nextTier
                : Mathf.Clamp(Mathf.Max(1, townCorePad.tier + 1), 1, Mathf.Max(1, townCorePad.maxTier));
            string nextTierName = ResolveBuildingTierName("town_core", nextTier, townCorePad.nextTierName);
            string unlockText = BuildTechTierUnlockText(lane, townCorePad, nextTier);

            if (string.Equals(townCorePad.buildState, "upgrading", System.StringComparison.OrdinalIgnoreCase))
                return $"{nextTierName} is upgrading now. {unlockText}";

            if (townCorePad.canUpgrade)
                return $"{nextTierName} is the next civic unlock for {Mathf.Max(0, townCorePad.upgradeCost)}g. {unlockText}";

            string lockedReason = NormalizeTownCoreRequirementText(townCorePad.lockedReason);
            return string.IsNullOrWhiteSpace(lockedReason)
                ? unlockText
                : $"{nextTierName} is locked. {lockedReason}";
        }

        List<string> GetTownCoreVisibleBuildingTypes(MLLaneSnap lane)
        {
            var types = new List<string>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            void TryAdd(string buildingType)
            {
                if (!ShouldShowTownCoreMenuBuildingType(buildingType) || !seen.Add(buildingType))
                    return;

                if (FindFortressBuildingConfig(buildingType) == null
                    && FindFortressPadByBuildingType(lane, buildingType) == null)
                    return;

                types.Add(buildingType);
            }

            for (int i = 0; i < TownCorePreferredBuildingOrder.Length; i++)
                TryAdd(TownCorePreferredBuildingOrder[i]);

            var configs = SnapshotApplier.Instance?.LatestMLMatchConfig?.fortressBuildingConfigs;
            if (configs != null)
            {
                for (int i = 0; i < configs.Length; i++)
                    TryAdd(configs[i]?.buildingType);
            }

            if (lane?.fortressPads != null)
            {
                for (int i = 0; i < lane.fortressPads.Length; i++)
                    TryAdd(lane.fortressPads[i]?.buildingType);
            }

            return types;
        }

        void CreateTownCoreBuildingSection(MLLaneSnap lane, IReadOnlyList<string> buildingTypes, string sectionName)
        {
            var sectionTypes = GetTownCoreBuildingTypesForSection(buildingTypes, sectionName);
            if (sectionTypes.Count == 0)
                return;

            CreateSectionHeader(sectionName);
            CreateTownCoreBuildingRows(lane, sectionTypes, $"TownCore{sectionName.Replace(" ", string.Empty)}BuildingRow");
        }

        void CreateTownCoreBuildingRows(MLLaneSnap lane, IReadOnlyList<string> buildingTypes, string rowName)
        {
            if (lane == null || buildingTypes == null || buildingTypes.Count == 0)
                return;

            CreateTownCoreCardRows(
                buildingTypes,
                rowName,
                (parent, buildingType) => CreateTownCorePadCard(
                    parent,
                    lane,
                    FindFortressBuildingConfig(buildingType),
                    FindFortressPadByBuildingType(lane, buildingType)));
        }

        List<string> GetTownCoreBuildingTypesForSection(IReadOnlyList<string> buildingTypes, string sectionName)
        {
            var matches = new List<string>();
            if (buildingTypes == null || string.IsNullOrWhiteSpace(sectionName))
                return matches;

            for (int i = 0; i < buildingTypes.Count; i++)
            {
                string buildingType = buildingTypes[i];
                if (string.Equals(ResolveTownCoreBuildingSectionName(buildingType), sectionName, System.StringComparison.OrdinalIgnoreCase))
                    matches.Add(buildingType);
            }

            return matches;
        }

        static string ResolveTownCoreBuildingSectionName(string buildingType)
        {
            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "blacksmith":
                case "archery_tower":
                case "workshop":
                    return "Military";
                case "market":
                case "lumber_mill":
                case "stable":
                    return "Economy";
                case "wizard_tower":
                case "temple":
                case "library":
                    return "Knowledge";
                case "wall":
                case "gate":
                case "turret":
                    return "Defense";
                default:
                    return "Additional Branches";
            }
        }

        static bool ShouldShowTownCoreMenuBuildingType(string buildingType)
        {
            if (string.IsNullOrWhiteSpace(buildingType))
                return false;

            switch (buildingType.Trim().ToLowerInvariant())
            {
                case "town_core":
                case "barracks":
                case "gate":
                case "turret":
                case "tower_archer":
                    return false;
                default:
                    return true;
            }
        }

        MLFortressBuildingConfig FindFortressBuildingConfig(string buildingType)
        {
            var configs = SnapshotApplier.Instance?.LatestMLMatchConfig?.fortressBuildingConfigs;
            if (configs == null || string.IsNullOrWhiteSpace(buildingType))
                return null;

            for (int i = 0; i < configs.Length; i++)
            {
                var config = configs[i];
                if (config != null && string.Equals(config.buildingType, buildingType, System.StringComparison.OrdinalIgnoreCase))
                    return config;
            }

            return null;
        }

        void CreateTownCoreCardRows<T>(IReadOnlyList<T> items, string rowName, System.Action<Transform, T> render)
        {
            if (items == null || items.Count == 0 || render == null)
                return;

            for (int index = 0; index < items.Count; index += 1)
                render(_contentRoot, items[index]);
        }

        int GetTownCoreMenuColumnCount()
        {
            float width = GetContentViewportWidth();
            return width > 760f ? 2 : 1;
        }

        void CreateTownCoreRowSpacer(Transform parent)
        {
            var spacer = new GameObject("TownCoreSpacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(parent, false);
            var layout = spacer.GetComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minWidth = 0f;
        }

        RectTransform CreateTownCoreCardShell(Transform parent, Color backgroundColor, Color accentColor, bool highlighted)
        {
            var card = CreateCardContainer(parent);
            var image = card.GetComponent<Image>();
            if (image != null)
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PaperMedium, true, backgroundColor);

            var layout = card.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(IsCompactPanelLayout() ? 12 : 14, IsCompactPanelLayout() ? 12 : 14, IsCompactPanelLayout() ? 10 : 12, IsCompactPanelLayout() ? 10 : 12);
                layout.spacing = IsCompactPanelLayout() ? 6f : 8f;
            }

            var element = card.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.flexibleWidth = 1f;
                element.minHeight = IsCompactPanelLayout() ? 178f : 196f;
            }

            ApplyTownCoreCardFrame(card.gameObject, backgroundColor, highlighted ? new Color(0.98f, 0.80f, 0.40f, 0.98f) : accentColor);
            return card;
        }

        RectTransform CreateTownCorePurchaseRowShell(Transform parent, Color backgroundColor, Color accentColor, bool highlighted)
        {
            var card = CreateTownCoreCardShell(parent, backgroundColor, accentColor, highlighted);
            var layout = card.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(IsCompactPanelLayout() ? 12 : 16, IsCompactPanelLayout() ? 12 : 16, IsCompactPanelLayout() ? 10 : 12, IsCompactPanelLayout() ? 10 : 12);
                layout.spacing = IsCompactPanelLayout() ? 4f : 6f;
            }

            var element = card.GetComponent<LayoutElement>();
            if (element != null)
                element.minHeight = IsCompactPanelLayout() ? 132f : 148f;

            return card;
        }

        RectTransform CreateTownCorePurchaseDetailsLayout(Transform parent, out RectTransform detailColumn, out RectTransform actionColumn)
        {
            var row = CreateHorizontalFillBlock(parent, "TownCorePurchaseLayout", IsCompactPanelLayout() ? 10f : 14f);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            if (rowLayout != null)
                rowLayout.childAlignment = TextAnchor.UpperLeft;

            detailColumn = CreateVerticalBlock(row, "Details", IsCompactPanelLayout() ? 5f : 7f);
            var detailLayout = detailColumn.gameObject.AddComponent<LayoutElement>();
            detailLayout.flexibleWidth = 1f;
            detailLayout.minWidth = 0f;

            actionColumn = CreateVerticalBlock(row, "ActionColumn", GetTownCoreActionColumnSpacing());
            var actionLayoutGroup = actionColumn.GetComponent<VerticalLayoutGroup>();
            if (actionLayoutGroup != null)
            {
                actionLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
                actionLayoutGroup.childForceExpandHeight = false;
                actionLayoutGroup.childForceExpandWidth = true;
            }

            var actionLayout = actionColumn.gameObject.AddComponent<LayoutElement>();
            actionLayout.minWidth = GetTownCorePrimaryActionColumnWidth();
            actionLayout.preferredWidth = GetTownCorePrimaryActionColumnWidth();
            actionLayout.flexibleWidth = 0f;

            return row;
        }

        float GetTownCorePrimaryActionColumnWidth() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 168f : 180f) : 220f;
        float GetTownCorePrimaryActionHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 48f : 54f) : 62f;
        float GetTownCoreActionColumnSpacing() => IsCompactPanelLayout() ? 6f : 8f;
        float GetTownCoreActionColumnRequiredHeight(int actionCount)
        {
            actionCount = Mathf.Max(0, actionCount);
            if (actionCount <= 0)
                return 0f;

            float cardVerticalPadding = IsCompactPanelLayout() ? 20f : 24f;
            return (GetTownCorePrimaryActionHeight() * actionCount)
                + (GetTownCoreActionColumnSpacing() * Mathf.Max(0, actionCount - 1))
                + cardVerticalPadding;
        }

        Button CreateTownCorePrimaryActionButton(
            Transform parent,
            string objectName,
            string label,
            UnityEngine.Events.UnityAction action,
            bool interactable,
            bool highlighted = false)
        {
            var button = CreateActionButton(
                parent,
                label,
                action,
                interactable,
                minWidth: GetTownCorePrimaryActionColumnWidth(),
                highlighted: highlighted);
            if (button != null && !string.IsNullOrWhiteSpace(objectName))
                button.gameObject.name = objectName;
            var layout = button != null ? button.GetComponent<LayoutElement>() : null;
            if (layout != null)
            {
                float buttonHeight = GetTownCorePrimaryActionHeight();
                layout.minWidth = GetTownCorePrimaryActionColumnWidth();
                layout.preferredWidth = GetTownCorePrimaryActionColumnWidth();
                layout.minHeight = buttonHeight;
                layout.preferredHeight = buttonHeight;
            }

            var text = button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (text != null)
            {
                text.fontSize = IsCompactPanelLayout() ? 15f : 18f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.enableAutoSizing = true;
                text.fontSizeMin = 12f;
                text.fontSizeMax = IsCompactPanelLayout() ? 15f : 18f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }

            return button;
        }

        static PanelRowPillData CreatePanelRowPill(string text, Color backgroundColor, Color textColor)
        {
            return new PanelRowPillData
            {
                Text = text,
                BackgroundColor = backgroundColor,
                TextColor = textColor,
            };
        }

        static PanelRowPillData CreateFocusedBarracksSummaryPill(string text)
        {
            return CreatePanelRowPill(
                text,
                new Color(0.16f, 0.17f, 0.20f, 0.96f),
                SilverTextColor);
        }

        static PanelRowStatData CreatePanelRowStat(string label, string value, Color backgroundColor, Color labelColor, Color valueColor)
        {
            return new PanelRowStatData
            {
                Label = label,
                Value = value,
                BackgroundColor = backgroundColor,
                LabelColor = labelColor,
                ValueColor = valueColor,
            };
        }

        static PanelRowActionData CreatePanelRowAction(string label, UnityEngine.Events.UnityAction action, bool interactable, bool highlighted = false, string objectName = null)
        {
            return new PanelRowActionData
            {
                ObjectName = objectName,
                Label = label,
                Action = action,
                Interactable = interactable,
                Highlighted = highlighted,
            };
        }

        static int CountPanelRowActions(PanelRowTemplateData data)
        {
            if (data == null)
                return 0;

            int count = data.PrimaryAction != null ? 1 : 0;
            if (data.SecondaryAction != null)
                count += 1;
            return count;
        }

        RectTransform CreatePanelTemplateRow(Transform parent, PanelRowTemplateData data)
        {
            if (data == null)
                return null;

            var row = CreateTownCorePurchaseRowShell(
                parent,
                data.BackgroundColor,
                data.AccentColor,
                data.Highlighted);
            if (!string.IsNullOrWhiteSpace(data.ObjectName))
                row.name = data.ObjectName;

            var rowLayout = row.GetComponent<LayoutElement>();
            float resolvedMinHeight = data.MinHeight;
            int actionCount = CountPanelRowActions(data);
            if (actionCount > 0)
                resolvedMinHeight = Mathf.Max(resolvedMinHeight, GetTownCoreActionColumnRequiredHeight(actionCount));

            if (rowLayout != null && resolvedMinHeight > 0f)
            {
                rowLayout.minHeight = resolvedMinHeight;
                rowLayout.preferredHeight = resolvedMinHeight;
            }

            CreateTownCorePurchaseDetailsLayout(row, out var detailColumn, out var actionColumn);
            if (!string.IsNullOrWhiteSpace(data.Eyebrow))
                CreateTownCoreEyebrow(detailColumn, data.Eyebrow);

            CreateTownCoreHeaderRow(detailColumn, data.Title, data.StatusText, data.StatusColor);

            if (data.Pills.Count > 0)
            {
                var meta = CreateHorizontalBlock(detailColumn, "TemplateMeta", 6f);
                for (int i = 0; i < data.Pills.Count; i++)
                {
                    var pill = data.Pills[i];
                    if (pill == null || string.IsNullOrWhiteSpace(pill.Text))
                        continue;

                    CreateTownCorePill(meta, pill.Text, pill.BackgroundColor, pill.TextColor);
                }
            }

            if (data.Stats.Count > 0)
                CreatePanelRowStatGrid(detailColumn, data.Stats);

            if (!string.IsNullOrWhiteSpace(data.Description))
                CreateBodyText(detailColumn, data.Description);

            if (data.PrimaryAction != null)
            {
                CreateTownCorePrimaryActionButton(
                    actionColumn,
                    data.PrimaryAction.ObjectName,
                    data.PrimaryAction.Label,
                    data.PrimaryAction.Action,
                    data.PrimaryAction.Interactable,
                    data.PrimaryAction.Highlighted);
            }

            if (data.SecondaryAction != null)
            {
                CreateTownCorePrimaryActionButton(
                    actionColumn,
                    data.SecondaryAction.ObjectName,
                    data.SecondaryAction.Label,
                    data.SecondaryAction.Action,
                    data.SecondaryAction.Interactable,
                    data.SecondaryAction.Highlighted);
            }

            return row;
        }

        void CreatePanelRowStatGrid(Transform parent, IReadOnlyList<PanelRowStatData> stats)
        {
            if (parent == null || stats == null || stats.Count == 0)
                return;

            var statBlock = CreateVerticalBlock(parent, "TemplateStats", 6f);
            for (int i = 0; i < stats.Count; i += 2)
            {
                var statRow = CreateHorizontalFillBlock(statBlock, $"TemplateStatRow{i / 2}", 6f);
                CreatePanelRowStatTile(statRow, stats[i]);
                if (i + 1 < stats.Count)
                    CreatePanelRowStatTile(statRow, stats[i + 1]);
            }
        }

        void CreatePanelRowStatTile(Transform parent, PanelRowStatData stat)
        {
            if (parent == null || stat == null || (string.IsNullOrWhiteSpace(stat.Label) && string.IsNullOrWhiteSpace(stat.Value)))
                return;

            var tile = new GameObject("TemplateStatTile", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            tile.transform.SetParent(parent, false);

            var image = tile.GetComponent<Image>();
            image.color = stat.BackgroundColor;

            var layout = tile.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var element = tile.GetComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = IsCompactPanelLayout() ? 50f : 56f;
            element.preferredHeight = element.minHeight;

            var label = CreateInlineText(
                tile.transform,
                "Label",
                stat.Label,
                12f,
                stat.LabelColor,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            var value = CreateInlineText(
                tile.transform,
                "Value",
                stat.Value,
                IsCompactPanelLayout() ? 12f : 13f,
                stat.ValueColor,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.overflowMode = TextOverflowModes.Ellipsis;
        }

        static void ApplyTownCoreCardFrame(GameObject target, Color backgroundColor, Color accentColor)
        {
            if (target == null)
                return;

            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
                shadow = target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.34f);
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;

            var outline = target.GetComponent<Outline>();
            if (outline == null)
                outline = target.AddComponent<Outline>();
            outline.effectColor = new Color(backgroundColor.r * 0.55f, backgroundColor.g * 0.55f, backgroundColor.b * 0.55f, 0.92f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(target.transform, false);
            accent.transform.SetAsFirstSibling();
            var rect = accent.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 3f);
            rect.anchoredPosition = Vector2.zero;
            var accentImage = accent.GetComponent<Image>();
            accentImage.color = accentColor;
            accentImage.raycastTarget = false;
        }

        void CreateTownCoreEyebrow(Transform parent, string text)
        {
            var eyebrow = CreateInlineText(
                parent,
                "Eyebrow",
                text,
                IsCompactPanelLayout() ? 9.5f : 10.5f,
                new Color(0.95f, 0.79f, 0.42f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            ClassicRpgUiRuntime.ApplyTextStyle(
                eyebrow,
                ClassicRpgTextStyle.SmallBody,
                TextAlignmentOptions.Left,
                new Color(0.95f, 0.79f, 0.42f, 0.98f),
                allowWrap: false);
            eyebrow.fontStyle = FontStyles.Bold;
        }

        void CreateTownCoreHeaderRow(Transform parent, string title, string statusText, Color statusColor)
        {
            var header = CreateHorizontalFillBlock(parent, "TownCoreHeader", 8f);
            var titleLabel = CreateInlineText(
                header,
                "Name",
                title,
                IsCompactPanelLayout() ? 14.5f : 16.5f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            var titleLayout = titleLabel.gameObject.AddComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;
            titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
            titleLabel.overflowMode = TextOverflowModes.Ellipsis;
            ClassicRpgUiRuntime.ApplyTextStyle(
                titleLabel,
                ClassicRpgTextStyle.SectionHeader,
                TextAlignmentOptions.Left,
                Color.white,
                allowWrap: false);

            CreateTownCorePill(header, statusText, statusColor, new Color(0.98f, 0.98f, 0.98f, 0.98f));
        }

        void CreateTownCorePill(Transform parent, string text, Color backgroundColor, Color textColor)
        {
            if (parent == null || string.IsNullOrWhiteSpace(text))
                return;

            var go = new GameObject("TownCorePill", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = backgroundColor;

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(9, 9, 4, 4);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var element = go.GetComponent<LayoutElement>();
            element.minHeight = 24f;

            var label = CreateInlineText(
                go.transform,
                "Label",
                text,
                IsCompactPanelLayout() ? 9f : 10f,
                textColor,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            ClassicRpgUiRuntime.ApplyTextStyle(
                label,
                ClassicRpgTextStyle.SmallBody,
                TextAlignmentOptions.Center,
                textColor,
                allowWrap: false);
            label.fontSize = 12f;
            label.fontStyle = FontStyles.Bold;
        }

        void TryAddTownCoreMetaPill(Transform parent, string text, Color backgroundColor, Color textColor)
        {
            if (!string.IsNullOrWhiteSpace(text))
                CreateTownCorePill(parent, text, backgroundColor, textColor);
        }

        void CreateTownCoreBarracksCard(Transform parent, MLLaneSnap lane, MLBarracksSite site)
        {
            if (lane == null || site == null)
                return;

            CreatePanelTemplateRow(parent, BuildTownCoreBarracksRowData(lane, site));
        }

        string BuildTownCoreBarracksBody(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Barracks data unavailable.";

            var lines = new List<string>();
            if (site.isConstructing)
            {
                lines.Add($"{BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site))}. The barracks roster unlocks when construction finishes.");
            }
            else if (!site.isBuilt)
            {
                lines.Add(site.canBuild
                    ? lane != null && lane.gold < site.buildCost
                        ? $"Need {Mathf.Max(0, site.buildCost - Mathf.FloorToInt(lane.gold))}g more before purchase."
                        : "This barracks is ready to purchase from Town Core."
                    : NormalizeTownCoreRequirementText(site.lockedReason) ?? BuildFocusedBarracksRequirementText(site));
            }
            else if (site.canUpgrade)
            {
                lines.Add($"Next upgrade: Level {Mathf.Max(1, site.level + 1)} for {Mathf.Max(0, site.upgradeCost)}g.");
            }
            else if (!string.IsNullOrWhiteSpace(site.lockedReason) && site.level < site.maxLevel)
            {
                lines.Add(NormalizeTownCoreRequirementText(site.lockedReason));
            }
            else
            {
                lines.Add($"Unit buying is live from the barracks screen. Owned Units {CountOwnedUnits(site.roster)}.");
            }

            int sendIntervalSeconds = GetFocusedBarracksSpawnIntervalSeconds(site);
            lines.Add(site.isBuilt && sendIntervalSeconds >= 0
                ? $"Current: Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}   Sends every {sendIntervalSeconds}s"
                : $"Current: Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}");
            return string.Join("\n", lines);
        }

        void CreateTownCorePadCard(Transform parent, MLLaneSnap lane, MLFortressBuildingConfig config, MLFortressPad pad)
        {
            if (lane == null || (config == null && pad == null))
                return;

            CreatePanelTemplateRow(parent, BuildTownCorePadRowData(lane, config, pad));
        }

        string BuildTownCorePadBody(MLLaneSnap lane, MLFortressPad pad, MLFortressBuildingConfig config, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster)
        {
            string buildingType = !string.IsNullOrWhiteSpace(pad?.buildingType)
                ? pad.buildingType
                : config?.buildingType;
            if (string.IsNullOrWhiteSpace(buildingType))
                return "Building data unavailable.";

            var lines = new List<string>();
            if (pad == null)
            {
                lines.Add(ResolveTownCoreRequirementLabel(null, config?.requiredTownCoreTier ?? 0));
                lines.Add($"Unlocks: {BuildUnlockPreview(buildingType, roster, heroRoster)}");
                return string.Join("\n", lines);
            }

            if (!pad.isBuilt)
            {
                lines.Add(pad.canBuild
                    ? "Ready for purchase in Town Core."
                    : NormalizeTownCoreRequirementText(pad.lockedReason) ?? ResolveTownCoreRequirementLabel(pad.requiredTownCoreTierName, pad.requiredTownCoreTier));
            }
            else if (pad.canUpgrade)
            {
                int nextTier = Mathf.Max(1, pad.tier + 1);
                lines.Add($"Next upgrade: {ResolveBuildingTierName(buildingType, nextTier, pad.nextTierName)} for {Mathf.Max(0, pad.upgradeCost)}g.");
            }
            else if (string.Equals(pad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("This branch has reached its final tier.");
            }
            else if (!string.IsNullOrWhiteSpace(pad.lockedReason) && pad.tier < pad.maxTier)
            {
                lines.Add(NormalizeTownCoreRequirementText(pad.lockedReason));
            }
            else
            {
                lines.Add(SupportsBarracksPurchaseFlow(lane, buildingType)
                    ? "Use Go To to review this branch, then buy its unlocked units from a Barracks."
                    : "Use Go To to inspect this building branch.");
            }

            lines.Add(string.Equals(buildingType, "wall", System.StringComparison.OrdinalIgnoreCase)
                ? "Shared defense path: Walls, Gates, and Turrets rise together."
                : $"Unlocks: {BuildUnlockPreview(buildingType, roster, heroRoster)}");
            return string.Join("\n", lines);
        }

        string BuildTownCoreBarracksTierPill(MLBarracksSite site)
        {
            if (site == null)
                return null;

            return $"Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}";
        }

        string BuildTownCoreBarracksGatePill(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return null;

            int currentTownCoreTier = GetCurrentTownCoreTier(lane);
            if (site.requiredTownCoreTier > currentTownCoreTier)
                return $"Unlock {ResolveTownCoreRequirementLabel(site.requiredTownCoreTierName, site.requiredTownCoreTier, includeVerb: false)}";

            return null;
        }

        string BuildTownCoreBarracksCostPill(MLBarracksSite site)
        {
            if (site == null)
                return null;

            if (!site.isBuilt && site.buildCost > 0)
                return $"{Mathf.Max(0, site.buildCost)}g";
            if (site.isBuilt && site.canUpgrade && site.upgradeCost > 0)
                return $"{Mathf.Max(0, site.upgradeCost)}g";
            return null;
        }

        string BuildTownCorePadTierPill(MLFortressPad pad, MLFortressBuildingConfig config)
        {
            if (pad != null)
                return $"Tier {Mathf.Max(0, pad.tier)}/{Mathf.Max(1, pad.maxTier)}";

            int maxTier = Mathf.Max(1, config != null ? config.maxTier : 1);
            return $"Tier 0/{maxTier}";
        }

        string BuildTownCorePadGatePill(MLLaneSnap lane, MLFortressPad pad, MLFortressBuildingConfig config)
        {
            int currentTownCoreTier = GetCurrentTownCoreTier(lane);
            int requiredTier = pad != null ? pad.requiredTownCoreTier : Mathf.Max(1, config != null ? config.requiredTownCoreTier : 1);
            string requiredTierName = pad != null
                ? pad.requiredTownCoreTierName
                : ResolveBuildingTierName("town_core", Mathf.Max(1, config != null ? config.requiredTownCoreTier : 1), null);

            if (requiredTier > currentTownCoreTier)
                return $"Unlock {ResolveTownCoreRequirementLabel(requiredTierName, requiredTier, includeVerb: false)}";

            return null;
        }

        string BuildTownCorePadCostPill(MLFortressPad pad, MLFortressBuildingConfig config)
        {
            if (pad != null)
            {
                if (!pad.isBuilt && pad.buildCost > 0)
                    return $"{Mathf.Max(0, pad.buildCost)}g";
                if (pad.canUpgrade && pad.upgradeCost > 0)
                    return $"{Mathf.Max(0, pad.upgradeCost)}g";
            }
            else if (config != null && config.buildCost > 0)
            {
                return $"{Mathf.Max(0, config.buildCost)}g";
            }

            return null;
        }

        int GetCurrentTownCoreTier(MLLaneSnap lane)
        {
            var townCorePad = FindFortressPadByBuildingType(lane, "town_core");
            return townCorePad != null ? Mathf.Max(1, townCorePad.tier) : 1;
        }

        string ResolveTownCorePadStatusLabel(MLFortressPad pad)
        {
            if (pad == null)
                return "Locked";

            return pad.buildState switch
            {
                "available_to_build" => "Available",
                "upgrade_available" => "Upgrade Ready",
                "constructing" => "Constructing",
                "upgrading" => "Upgrading",
                "under_repair" => "Under Repair",
                "destroyed" => "Destroyed",
                "max_tier" => "Max Tier",
                "locked" => "Locked",
                _ => pad.isBuilt ? "Built" : "Locked",
            };
        }

        string ResolveTownCoreBarracksStatusLabel(MLBarracksSite site)
        {
            if (site == null)
                return "Locked";

            if (!site.isBuilt && site.canBuild)
                return "Available";
            if (site.isBuilt && site.canUpgrade)
                return "Upgrade Ready";

            return site.buildState switch
            {
                "constructing" => "Constructing",
                "upgrading" => "Upgrading",
                "under_repair" => "Under Repair",
                "destroyed" => "Destroyed",
                "locked" => "Locked",
                _ => site.isBuilt ? "Built" : "Locked",
            };
        }

        static Color ResolveTownCoreStatusPillColor(string buildState, bool canBuild, bool canUpgrade, bool isBuilt)
        {
            if (!isBuilt && canBuild)
                return GoldSurfaceColor;
            if (canUpgrade)
                return GoldSurfaceBrightColor;

            return buildState switch
            {
                "constructing" => GoldSurfaceColor,
                "upgrading" => GoldSurfaceBrightColor,
                "under_repair" => new Color(0.38f, 0.29f, 0.13f, 0.98f),
                "destroyed" => new Color(0.52f, 0.18f, 0.16f, 0.98f),
                "locked" => GunmetalColor,
                "max_tier" => new Color(0.52f, 0.40f, 0.16f, 0.98f),
                _ => GunmetalSoftColor,
            };
        }

        static Color ResolveTownCoreAccentColor(string buildingType)
        {
            return (buildingType ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "barracks" => GoldAccentColor,
                "blacksmith" => GoldAccentColor,
                "market" => GoldAccentColor,
                "stable" => GoldAccentColor,
                "workshop" => GoldAccentColor,
                "wall" => SilverAccentColor,
                _ => SilverAccentColor,
            };
        }

        static Color ResolveTownCoreCardColor(MLFortressPad pad)
        {
            return pad != null ? ResolvePadCardTint(pad) : ObsidianElevatedColor;
        }

        string ResolveTownCoreRequirementLabel(string tierName, int requiredTier, bool includeVerb = true)
        {
            int safeTier = Mathf.Max(1, requiredTier);
            string resolvedTierName = !string.IsNullOrWhiteSpace(tierName)
                ? tierName
                : ResolveBuildingTierName("town_core", safeTier, null);
            if (string.IsNullOrWhiteSpace(resolvedTierName))
                resolvedTierName = $"Town Core Tier {safeTier}";
            return includeVerb ? $"Requires {resolvedTierName}" : resolvedTierName;
        }

        string NormalizeTownCoreRequirementText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string trimmed = text.Trim();
            const string requiresPrefix = "Requires Town Core: ";
            const string upgradePrefix = "Upgrade requires Town Core: ";
            if (trimmed.StartsWith(requiresPrefix, System.StringComparison.OrdinalIgnoreCase))
                return $"Requires {trimmed.Substring(requiresPrefix.Length).Trim()}";
            if (trimmed.StartsWith(upgradePrefix, System.StringComparison.OrdinalIgnoreCase))
                return $"Upgrade requires {trimmed.Substring(upgradePrefix.Length).Trim()}";
            return trimmed;
        }

        bool SupportsBarracksPurchaseFlow(MLLaneSnap lane, MLFortressPad pad)
        {
            return SupportsBarracksPurchaseFlow(lane, pad?.buildingType);
        }

        bool SupportsBarracksPurchaseFlow(MLLaneSnap lane, string buildingType)
        {
            if (lane?.barracksRoster == null || string.IsNullOrWhiteSpace(buildingType))
                return false;

            for (int i = 0; i < lane.barracksRoster.Length; i++)
            {
                var entry = lane.barracksRoster[i];
                if (entry == null)
                    continue;
                if (string.Equals(entry.unlockBuildingType, buildingType, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        void CreatePurchaseInBarracksSection(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null)
                return;

            CreateSectionHeader("Purchase In Barracks");
            var card = CreateCardContainer();
            TintCard(card, new Color(0.14f, 0.20f, 0.16f, 0.98f));
            CreateCardTitle(card, $"Use {pad.buildingName} Units At Any Barracks");
            CreateBodyText(card, $"Unlocked here: {BuildUnlockPreview(pad, lane.barracksRoster, lane.heroRoster)}\nChoose a Barracks to buy and spawn those units.");

            var actions = CreateActionRow(card);
            string[] orderedBarracksIds = { "left", "center", "right" };
            for (int i = 0; i < orderedBarracksIds.Length; i++)
            {
                var site = FindBarracksSiteById(lane, orderedBarracksIds[i]);
                if (site == null)
                    continue;

                if (site.isBuilt)
                {
                    var resolvedSite = site;
                    CreateActionButton(actions, ResolveBarracksDisplayName(resolvedSite), () => OpenOverviewBarracks(lane, resolvedSite), true, minWidth: 140f);
                }
                else
                {
                    var lockedSite = site;
                    CreateActionButton(actions, $"Unlock {ResolveBarracksDisplayName(lockedSite)}", () => OpenTownCore(lane), true, minWidth: 160f);
                }
            }
        }

        void CreateBuildingOverviewSummaryCard(MLLaneSnap lane)
        {
            var card = CreateCardContainer();
            var element = card.GetComponent<LayoutElement>();
            if (element != null)
                element.minHeight = 92f;
            TintCard(card, new Color(0.11f, 0.19f, 0.24f, 0.98f));
            CreateCardTitle(card, "Overview");
            CreateBodyText(card, BuildBuildingOverviewSummaryBody(lane));
        }

        void CreateBarracksInstanceOverviewSection(MLLaneSnap lane, string headerText)
        {
            if (!string.IsNullOrWhiteSpace(headerText))
                CreateSectionHeader(headerText);

            if (lane?.barracksSites == null || lane.barracksSites.Length == 0)
            {
                CreateInfoCard("No barracks instance data is available yet.");
                return;
            }

            var ordered = (MLBarracksSite[])lane.barracksSites.Clone();
            System.Array.Sort(ordered, CompareBarracksSites);
            for (int i = 0; i < ordered.Length; i++)
            {
                var site = ordered[i];
                if (site == null) continue;

                CreateBarracksOverviewCard(lane, site);
            }
        }

        void CreateBarracksOverviewCard(MLLaneSnap lane, MLBarracksSite site)
        {
            if (lane == null || site == null)
                return;

            var card = CreateCardContainer();
            TintCard(card, IsBarracksFocused(site.barracksId)
                ? new Color(0.20f, 0.38f, 0.24f, 0.98f)
                : ResolveBarracksOverviewCardTint(site));
            WireOverviewCardNavigation(card, () => OpenOverviewBarracks(lane, site));
            CreateCardTitle(card, ResolveBarracksDisplayName(site));
            CreateBodyText(card, BuildBuildingOverviewBarracksBody(lane, site));
            CreateInlineText(
                card,
                "FocusHint",
                "Select to focus this barracks for unit buying and live spawn management.",
                12f,
                new Color(0.96f, 0.90f, 0.64f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Left);
        }

        void CreateBuildingOverviewCard(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null)
                return;

            var card = CreateCardContainer();
            TintCard(card, IsPadFocused(pad.padId)
                ? new Color(0.20f, 0.38f, 0.24f, 0.98f)
                : ResolvePadCardTint(pad));
            WireOverviewCardNavigation(card, () => OpenOverviewPad(lane, pad));
            CreateCardTitle(card, string.IsNullOrWhiteSpace(pad.displayName) ? pad.buildingName : pad.displayName);
            CreateBodyText(card, BuildBuildingOverviewPadBody(lane, pad, lane.barracksRoster, lane.heroRoster));
            CreateInlineText(
                card,
                "FocusHint",
                "Select to focus this building and open its details.",
                12f,
                new Color(0.96f, 0.90f, 0.64f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Left);
        }

        bool CanEditBarracks()
        {
            return SnapshotApplier.Instance?.MyLane != null;
        }

        bool CanSpendGold(MLLaneSnap lane, int cost)
        {
            return CanEditBarracks() && lane != null && lane.gold >= cost;
        }

        static int ResolveFoodLimitForTier(int tier)
        {
            if (tier <= 0)
                return 0;
            if (tier == 1)
                return 20;
            if (tier == 2)
                return 40;
            return 60;
        }

        static int ResolveFoodCostForTier(int tier)
        {
            return Mathf.Clamp(tier, 1, 3);
        }

        static int ResolveBarracksEntryFoodCost(MLBarracksRosterEntry entry)
        {
            return Mathf.Max(1, entry != null && entry.foodCost > 0 ? entry.foodCost : ResolveFoodCostForTier(entry != null ? entry.tier : 1));
        }

        static int ResolveMarketEntryFoodCost(MLMarketRosterEntry entry)
        {
            return Mathf.Max(1, entry != null && entry.foodCost > 0 ? entry.foodCost : ResolveFoodCostForTier(entry != null ? entry.tier : 1));
        }

        static int SumBarracksFoodUsed(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int total = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null)
                    continue;
                total += Mathf.Max(0, entry.ownedCount) * ResolveBarracksEntryFoodCost(entry);
            }

            return total;
        }

        static int SumMarketFoodUsed(MLMarketRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int total = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null)
                    continue;
                total += Mathf.Max(0, entry.ownedCount) * ResolveMarketEntryFoodCost(entry);
            }

            return total;
        }

        int GetBarracksFoodLimit(MLBarracksSite site)
        {
            if (site == null)
                return 0;

            return site.foodLimit > 0
                ? Mathf.Max(0, site.foodLimit)
                : ResolveFoodLimitForTier(Mathf.Max(0, site.level));
        }

        int GetBarracksFoodUsed(MLBarracksSite site)
        {
            if (site == null)
                return 0;

            if (site.foodLimit > 0 || site.foodUsed > 0 || site.foodRemaining > 0 || site.isAtFoodLimit)
                return Mathf.Max(0, site.foodUsed);

            return SumBarracksFoodUsed(site.roster);
        }

        int GetBarracksFoodRemaining(MLBarracksSite site)
        {
            int limit = GetBarracksFoodLimit(site);
            if (limit <= 0)
                return 0;

            if (site != null && (site.foodLimit > 0 || site.foodUsed > 0 || site.foodRemaining > 0 || site.isAtFoodLimit))
                return Mathf.Clamp(site.foodRemaining, 0, limit);

            return Mathf.Max(0, limit - GetBarracksFoodUsed(site));
        }

        bool HasBarracksActiveFoodState(MLBarracksSite site)
        {
            return site != null && site.hasActiveFoodState;
        }

        int GetBarracksActiveFoodUsed(MLBarracksSite site)
        {
            if (!HasBarracksActiveFoodState(site))
                return 0;

            return Mathf.Max(0, site.activeFoodUsed);
        }

        string BuildBarracksRosterFoodLabel(MLBarracksSite site)
        {
            int limit = GetBarracksFoodLimit(site);
            return limit > 0
                ? $"Roster {GetBarracksFoodUsed(site)}/{limit}"
                : string.Empty;
        }

        string BuildBarracksActiveFoodLabel(MLBarracksSite site)
        {
            if (!HasBarracksActiveFoodState(site))
                return string.Empty;

            int limit = GetBarracksFoodLimit(site);
            return limit > 0
                ? $"Field {GetBarracksActiveFoodUsed(site)}/{limit}"
                : string.Empty;
        }

        string BuildBarracksFoodLabel(MLBarracksSite site)
        {
            string rosterLabel = BuildBarracksRosterFoodLabel(site);
            string activeLabel = BuildBarracksActiveFoodLabel(site);
            if (string.IsNullOrWhiteSpace(activeLabel))
                return rosterLabel;
            if (string.IsNullOrWhiteSpace(rosterLabel))
                return activeLabel;
            return $"{rosterLabel}   {activeLabel}";
        }

        int GetMarketFoodLimit(MLFortressPad pad)
        {
            if (pad == null)
                return 0;

            return pad.foodLimit > 0
                ? Mathf.Max(0, pad.foodLimit)
                : ResolveFoodLimitForTier(Mathf.Max(0, pad.tier));
        }

        int GetMarketFoodUsed(MLLaneSnap lane, MLFortressPad pad)
        {
            if (pad != null && (pad.foodLimit > 0 || pad.foodUsed > 0 || pad.foodRemaining > 0 || pad.isAtFoodLimit))
                return Mathf.Max(0, pad.foodUsed);

            return SumMarketFoodUsed(lane?.marketRoster);
        }

        int GetMarketFoodRemaining(MLLaneSnap lane, MLFortressPad pad)
        {
            int limit = GetMarketFoodLimit(pad);
            if (limit <= 0)
                return 0;

            if (pad != null && (pad.foodLimit > 0 || pad.foodUsed > 0 || pad.foodRemaining > 0 || pad.isAtFoodLimit))
                return Mathf.Clamp(pad.foodRemaining, 0, limit);

            return Mathf.Max(0, limit - GetMarketFoodUsed(lane, pad));
        }

        string BuildMarketFoodLabel(MLLaneSnap lane, MLFortressPad pad)
        {
            int limit = GetMarketFoodLimit(pad);
            return limit > 0
                ? $"Traders {GetMarketFoodUsed(lane, pad)}/{limit}"
                : string.Empty;
        }

        int GetMarketIncomeSeconds()
        {
            const int tickHz = 20;
            int incomeTicks = SnapshotApplier.Instance?.LatestMLMatchConfig?.incomeIntervalTicks ?? 0;
            if (incomeTicks <= 0)
                incomeTicks = 15 * tickHz;
            return Mathf.Max(1, Mathf.CeilToInt(incomeTicks / (float)tickHz));
        }

        int GetMarketIncomePerTick(MLMarketRosterEntry entry)
        {
            if (entry == null)
                return 0;
            return Mathf.Max(0, entry.ownedCount) * Mathf.Max(0, entry.economyLapGold);
        }

        string GetBarracksBuyBlockedReason(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry, int count = 1)
        {
            if (site == null || entry == null)
                return "Buy";

            count = Mathf.Max(1, count);
            if (!site.isBuilt)
                return "Open Town Core";
            if (!entry.unlocked)
                return "Tech Locked";
            if (!entry.currentTier)
                return string.IsNullOrWhiteSpace(entry.lockedReason) ? "Tier Advanced" : entry.lockedReason;

            int foodNeeded = ResolveBarracksEntryFoodCost(entry) * count;
            int foodRemaining = GetBarracksFoodRemaining(site);
            if (GetBarracksFoodLimit(site) > 0 && foodRemaining < foodNeeded)
                return foodRemaining <= 0 ? "Roster Full" : $"Need {Mathf.Max(0, foodNeeded - foodRemaining)} Roster Food";

            int totalCost = Mathf.Max(0, entry.buyCost * count);
            if (lane != null && lane.gold < totalCost)
                return $"Need {Mathf.Max(0, totalCost - Mathf.FloorToInt(lane.gold))}g";

            if (!entry.availableForPurchase)
                return string.IsNullOrWhiteSpace(entry.lockedReason) ? "Unavailable" : entry.lockedReason;

            return null;
        }

        bool CanBuyBarracksRosterEntry(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry, int count = 1)
        {
            return CanEditBarracks()
                && lane != null
                && site != null
                && entry != null
                && string.IsNullOrWhiteSpace(GetBarracksBuyBlockedReason(lane, site, entry, count));
        }

        string GetMarketBuyBlockedReason(MLLaneSnap lane, MLFortressPad pad, MLMarketRosterEntry entry, int count = 1)
        {
            if (entry == null)
                return "Buy Trader";

            count = Mathf.Max(1, count);
            if (pad == null || !pad.isBuilt)
                return "Open Town Core";
            if (!entry.unlocked)
                return "Tech Locked";
            if (!entry.currentTier)
                return string.IsNullOrWhiteSpace(entry.lockedReason) ? "Tier Advanced" : entry.lockedReason;

            int foodNeeded = ResolveMarketEntryFoodCost(entry) * count;
            int foodRemaining = GetMarketFoodRemaining(lane, pad);
            if (GetMarketFoodLimit(pad) > 0 && foodRemaining < foodNeeded)
                return foodRemaining <= 0 ? "Cap Full" : $"Need {Mathf.Max(0, foodNeeded - foodRemaining)} Slot";

            int totalCost = Mathf.Max(0, entry.buyCost * count);
            if (lane != null && lane.gold < totalCost)
                return $"Need {Mathf.Max(0, totalCost - Mathf.FloorToInt(lane.gold))}g";

            if (!entry.availableForPurchase)
                return string.IsNullOrWhiteSpace(entry.lockedReason) ? "Unavailable" : entry.lockedReason;

            return null;
        }

        string BuildBuildingOverviewHeaderText(MLLaneSnap lane, int sendSeconds, int waveSeconds)
        {
            if (lane == null)
                return string.Empty;

            string laneLabel = !string.IsNullOrWhiteSpace(lane.branchLabel)
                ? lane.branchLabel
                : $"Lane {lane.laneIndex + 1}";
            return
                $"{laneLabel}   Gold {Mathf.FloorToInt(lane.gold)}   Income {lane.income:0.#}   " +
                $"Send {Mathf.Max(0, sendSeconds)}s   Wave {Mathf.Max(0, waveSeconds)}s";
        }

        string BuildBuildingOverviewStatus(MLLaneSnap lane)
        {
            if (lane == null)
                return string.Empty;

            int slotTotal = CountOverviewPads(lane.fortressPads);
            int slotBuilt = CountBuiltOverviewPads(lane.fortressPads);
            int slotUnbuilt = Mathf.Max(0, slotTotal - slotBuilt);
            int slotLocked = CountLockedOverviewPads(lane.fortressPads);
            int barracksTotal = lane.barracksSites != null ? lane.barracksSites.Length : 0;
            int barracksBuilt = CountBuiltBarracksSites(lane.barracksSites);
            return
                $"Barracks Purchased {barracksBuilt}/{barracksTotal}   " +
                $"Other Buildings Owned {slotBuilt}/{slotTotal}   Not Yet Purchased {slotUnbuilt}   Locked {slotLocked}";
        }

        string BuildBuildingOverviewHint(MLLaneSnap lane)
        {
            if ((lane?.fortressPads == null || lane.fortressPads.Length == 0)
                && (lane?.barracksSites == null || lane.barracksSites.Length == 0))
                return "Waiting for building slot data...";

            return "Read the branch map to see current tiers, future unlocks, and Castle hero access. Select a branch card to focus that real building.";
        }

        string BuildBuildingOverviewSummaryBody(MLLaneSnap lane)
        {
            if ((lane?.fortressPads == null || lane.fortressPads.Length == 0)
                && (lane?.barracksSites == null || lane.barracksSites.Length == 0))
                return "No building slots are available for this lane yet.";

            int built = CountBuiltOverviewPads(lane.fortressPads);
            int total = CountOverviewPads(lane.fortressPads);
            int locked = CountLockedOverviewPads(lane.fortressPads);
            int barracksBuilt = CountBuiltBarracksSites(lane.barracksSites);
            int barracksTotal = lane.barracksSites != null ? lane.barracksSites.Length : 0;
            return
                $"Town Core now owns building purchases and upgrades. Advance House to Castle here to unlock heroes in the Barracks.\n" +
                $"Barracks Purchased {barracksBuilt}/{barracksTotal}   Other Buildings Owned {built}/{total}   Locked {locked}\n" +
                "Select a branch card to inspect details, then use Town Core whenever you need to purchase or upgrade.";
        }

        string BuildBuildingOverviewPadBody(MLLaneSnap lane, MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return "No building selected.";

            string ownership = pad.isBuilt
                ? $"Owned   Tier {Mathf.Max(0, pad.tier)}/{Mathf.Max(1, pad.maxTier)}"
                : "Not Yet Purchased";
            string stateLine = pad.isBuilt
                ? $"{HumanizeBuildState(pad.buildState)}   HP {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}"
                : string.IsNullOrWhiteSpace(pad.lockedReason)
                    ? "Available from Town Core."
                    : pad.lockedReason;

            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var currentMarketEntry = GetCurrentMarketRosterEntry(lane);
                string marketLine = currentMarketEntry != null && pad.isBuilt
                    ? $"Current Tier: {currentMarketEntry.displayName}   {BuildMarketFoodLabel(lane, pad)}   +{GetMarketIncomePerTick(currentMarketEntry)}g / {GetMarketIncomeSeconds()}s"
                    : $"Trade Unlocks: {BuildUnlockPreview(pad, roster, heroRoster)}";
                return $"{ownership}\n{stateLine}\n{marketLine}";
            }

            return
                $"{ownership}\n" +
                $"{stateLine}\n" +
                $"Unlock Track: {BuildUnlockPreview(pad, roster, heroRoster)}";
        }

        MLFortressPad FindFortressPadByBuildingType(MLLaneSnap lane, string buildingType)
        {
            if (lane?.fortressPads == null || string.IsNullOrWhiteSpace(buildingType))
                return null;

            for (int i = 0; i < lane.fortressPads.Length; i++)
            {
                var pad = lane.fortressPads[i];
                if (pad != null && string.Equals(pad.buildingType, buildingType, System.StringComparison.OrdinalIgnoreCase))
                    return pad;
            }

            return null;
        }

        MLBarracksSite FindBarracksSiteById(MLLaneSnap lane, string barracksId)
        {
            if (lane?.barracksSites == null || string.IsNullOrWhiteSpace(barracksId))
                return null;

            string normalizedId = NormalizeBarracksId(barracksId);
            for (int i = 0; i < lane.barracksSites.Length; i++)
            {
                var site = lane.barracksSites[i];
                if (site != null && string.Equals(NormalizeBarracksId(site.barracksId), normalizedId, System.StringComparison.OrdinalIgnoreCase))
                    return site;
            }

            return null;
        }

        void OpenTownCore(MLLaneSnap lane, MLBarracksRosterEntry entry = null, MLFortressPad targetPad = null, MLBarracksSite targetSite = null)
        {
            if (lane == null)
                return;

            var townCorePad = FindFortressPadByBuildingType(lane, "town_core");
            if (townCorePad == null)
            {
                _statusMessage = "Town Core route is unavailable for this lane.";
                RefreshHeader(force: true);
                return;
            }

            FortressSelectionController.FocusFortressPad(lane.laneIndex, townCorePad.padId);
            ShowForPad(townCorePad.padId);
            if (entry != null && (targetPad != null || targetSite != null))
                SetGuidedUnlockContext(entry, targetPad, targetSite);
            else
                ClearGuidedUnlockContext();
            _statusMessage = null;
            RefreshHeader(force: true);
            RefreshContent(force: true);
        }

        MLMarketRosterEntry GetCurrentMarketRosterEntry(MLLaneSnap lane)
        {
            if (lane?.marketRoster == null)
                return null;

            MLMarketRosterEntry unlockedFallback = null;
            for (int i = 0; i < lane.marketRoster.Length; i++)
            {
                var entry = lane.marketRoster[i];
                if (entry == null)
                    continue;
                if (entry.availableForPurchase || entry.currentTier)
                    return entry;
                if (unlockedFallback == null && entry.unlocked)
                    unlockedFallback = entry;
            }

            return unlockedFallback;
        }

        MLMarketRosterEntry FindMarketRosterEntry(MLLaneSnap lane, string unitKey)
        {
            if (lane?.marketRoster == null || string.IsNullOrWhiteSpace(unitKey))
                return null;

            for (int i = 0; i < lane.marketRoster.Length; i++)
            {
                var entry = lane.marketRoster[i];
                if (entry != null && string.Equals(entry.unitKey, unitKey, System.StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        MLMarketRosterEntry FindMarketRosterEntryByTier(MLLaneSnap lane, int tier)
        {
            if (lane?.marketRoster == null)
                return null;

            int targetTier = Mathf.Max(1, tier);
            for (int i = 0; i < lane.marketRoster.Length; i++)
            {
                var entry = lane.marketRoster[i];
                if (entry != null && Mathf.Max(1, entry.tier) == targetTier)
                    return entry;
            }

            return null;
        }

        MLMarketRosterEntry GetNextMarketRosterEntry(MLLaneSnap lane, MLMarketRosterEntry currentEntry)
        {
            if (currentEntry == null)
                return null;

            if (!string.IsNullOrWhiteSpace(currentEntry.nextUnitKey))
                return FindMarketRosterEntry(lane, currentEntry.nextUnitKey);

            return FindMarketRosterEntryByTier(lane, Mathf.Max(1, currentEntry.tier + 1));
        }

        static string BuildMarketRouteSummary(MLMarketRosterEntry entry)
        {
            return "Processed on the shared income timer.";
        }

        string BuildTechBranchTitle(MLFortressPad pad)
        {
            if (pad == null)
                return "Branch";

            string branchName = !string.IsNullOrWhiteSpace(pad.branchLabel)
                ? pad.branchLabel
                : pad.buildingName;
            string tierName = pad.isBuilt
                ? ResolveBuildingTierName(pad.buildingType, Mathf.Max(1, pad.tier), pad.currentTierName)
                : "Unbuilt";
            return $"{branchName}  {tierName}";
        }

        string BuildTechBranchSummary(MLLaneSnap lane, MLFortressPad pad)
        {
            if (pad == null)
                return "No branch data available.";

            string currentLine = pad.isBuilt
                ? $"Current: {ResolveBuildingTierName(pad.buildingType, Mathf.Max(1, pad.tier), pad.currentTierName)}"
                : "Current: Not built yet";
            string nextLine;
            if (string.Equals(pad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase))
            {
                nextLine = "Next: Max tier reached.";
            }
            else if (pad.canBuild)
            {
                nextLine = $"Next: Buy {pad.buildingName} to unlock {BuildTechTierUnlockText(lane, pad, 1)}";
            }
            else if (pad.canUpgrade)
            {
                int nextTier = Mathf.Max(1, pad.tier + 1);
                nextLine = $"Next: {ResolveBuildingTierName(pad.buildingType, nextTier, pad.nextTierName)} grants {BuildTechTierUnlockText(lane, pad, nextTier)}";
            }
            else
            {
                nextLine = string.IsNullOrWhiteSpace(pad.lockedReason)
                    ? "Next: Waiting for requirements."
                    : $"Next: {pad.lockedReason}";
            }

            if (string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase))
                return $"{currentLine}\n{nextLine}\nCastle unlocks King, Paladin, and Bishop in the Barracks.";

            return $"{currentLine}\n{nextLine}";
        }

        string BuildTechTierUnlockText(MLLaneSnap lane, MLFortressPad pad, int tier)
        {
            if (pad == null)
                return "No unlock data.";

            if (string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase))
            {
                switch (tier)
                {
                    case 1: return "House starts with Town Core built. Center Barracks is the first 100g Town Core purchase and unlocks the first militia route.";
                    case 2: return "Town Hall unlocks Tier 1 buildings, the second Barracks, and Center Barracks Tier 2.";
                    case 3: return "Keep unlocks Tier 2 building upgrades, the third Barracks, and broader Barracks upgrades.";
                    case 4: return "Castle unlocks King, Paladin, and Bishop at the Barracks.";
                    default: return "No Town Core unlock data.";
                }
            }

            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var marketConfigs = SnapshotApplier.Instance?.LatestMLMatchConfig?.marketRosterConfigs;
                if (marketConfigs != null)
                {
                    var marketNames = new List<string>();
                    for (int i = 0; i < marketConfigs.Length; i++)
                    {
                        var entry = marketConfigs[i];
                        if (entry == null)
                            continue;
                        if (!string.Equals(entry.unlockBuildingType, "market", System.StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (Mathf.Max(1, entry.requiredBuildingTier) != tier)
                            continue;
                        marketNames.Add(entry.displayName);
                    }

                    var names = marketNames;
                    if (marketNames.Count > 0)
                        return string.Join("   •   ", names);
                }

                return tier switch
                {
                    1 => "Peasant contracts add 4 gold every income cycle.",
                    2 => "Settler contracts replace prior contracts and add 7 gold every income cycle.",
                    3 => "Trader contracts replace prior contracts and add 10 gold every income cycle.",
                    _ => "No market unlock data.",
                };
            }

            if (lane?.barracksRoster == null || lane.barracksRoster.Length == 0)
            {
                return pad.buildingType switch
                {
                    "stable" => tier switch
                    {
                        1 => "Unlocks mounted unit progression once Town Hall is built.",
                        2 => "Keep-tier stable expansion placeholder.",
                        3 => "Castle-tier mounted expansion placeholder.",
                        _ => "No stable unlock data.",
                    },
                    "workshop" => tier switch
                    {
                        1 => "Workshop siege building placeholder.",
                        2 => "Expanded siege workshop placeholder.",
                        3 => "Final workshop tier placeholder.",
                        _ => "No workshop unlock data.",
                    },
                    "library" => tier switch
                    {
                        1 => "Library ability and aura placeholder.",
                        2 => "Advanced library upgrade placeholder.",
                        3 => "Final library upgrade placeholder.",
                        _ => "No library unlock data.",
                    },
                    "lumber_mill" => tier switch
                    {
                        1 => "Economic support building for future town upgrades.",
                        2 => "Advanced resource support for later structures.",
                        3 => "Final lumber support tier.",
                        _ => "No lumber mill unlock data.",
                    },
                    "wall" => tier switch
                    {
                        1 => "Builds the town perimeter and shared early defense path.",
                        2 => "Strengthens Walls and attached defenses together.",
                        3 => "Final shared wall path before later Turret specialization.",
                        _ => "No wall unlock data.",
                    },
                    "gate" => tier switch
                    {
                        1 => "Part of the shared Walls defense path.",
                        2 => "Strengthens with the Walls upgrade path.",
                        3 => "Final shared gate defense tier.",
                        _ => "No gate unlock data.",
                    },
                    "turret" => tier switch
                    {
                        1 => "Emerges from the shared Walls path once the perimeter matures.",
                        2 => "Stronger upgraded defensive hardpoints.",
                        3 => "Final Turret hardpoint tier.",
                        _ => "No tower unlock data.",
                    },
                    _ => "Waiting for roster data.",
                };
            }

            {
                var names = new List<string>();
            for (int i = 0; i < lane.barracksRoster.Length; i++)
            {
                var entry = lane.barracksRoster[i];
                if (entry == null)
                    continue;
                if (!string.Equals(entry.unlockBuildingType, pad.buildingType, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Mathf.Max(1, entry.requiredBuildingTier) != tier)
                    continue;
                names.Add(entry.displayName);
            }

            return names.Count > 0
                ? string.Join("   •   ", names)
                : "No units on this tier.";
            }
        }

        string GetTechTierLabel(MLFortressPad pad, int tier)
        {
            if (pad == null)
                return $"Tier {Mathf.Max(1, tier)}";

            return ResolveBuildingTierName(pad.buildingType, Mathf.Max(1, tier), $"Tier {Mathf.Max(1, tier)}");
        }

        string ResolveBuildingTierName(string buildingType, int tier, string fallback = null)
        {
            int safeTier = Mathf.Max(1, tier);
            var configs = SnapshotApplier.Instance?.LatestMLMatchConfig?.fortressBuildingConfigs;
            if (configs != null)
            {
                for (int i = 0; i < configs.Length; i++)
                {
                    var config = configs[i];
                    if (config == null || !string.Equals(config.buildingType, buildingType, System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (config.tierDisplayNames != null && safeTier < config.tierDisplayNames.Length)
                    {
                        string configured = config.tierDisplayNames[safeTier];
                        if (!string.IsNullOrWhiteSpace(configured))
                            return configured;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(fallback))
                return fallback;

            return safeTier <= 1 ? "Tier 1" : $"Tier {safeTier}";
        }

        string ResolveTechTierStateLabel(MLFortressPad pad, int tier)
        {
            if (pad == null)
                return "Locked";

            int currentTier = Mathf.Max(0, pad.tier);
            if (currentTier <= 0)
            {
                if (tier == 1)
                    return pad.canBuild ? "Available Now" : "Locked";
                return "Future Tier";
            }

            if (tier < currentTier)
                return "Unlocked";
            if (tier == currentTier)
                return string.Equals(pad.buildState, "max_tier", System.StringComparison.OrdinalIgnoreCase)
                    ? "Current Max"
                    : "Current Tier";
            if (tier == currentTier + 1 && pad.canUpgrade)
                return "Next Upgrade";
            return "Future Tier";
        }

        Color ResolveTechTierBackground(MLFortressPad pad, int tier)
        {
            string state = ResolveTechTierStateLabel(pad, tier);
            switch (state)
            {
                case "Unlocked":
                    return new Color(0.14f, 0.26f, 0.20f, 0.96f);
                case "Current Tier":
                    return new Color(0.16f, 0.28f, 0.36f, 0.98f);
                case "Current Max":
                    return new Color(0.30f, 0.24f, 0.10f, 0.98f);
                case "Available Now":
                case "Next Upgrade":
                    return new Color(0.27f, 0.22f, 0.10f, 0.98f);
                case "Future Tier":
                    return new Color(0.12f, 0.15f, 0.20f, 0.94f);
                default:
                    return new Color(0.14f, 0.14f, 0.16f, 0.92f);
            }
        }

        Color ResolveTechTierChipColor(MLFortressPad pad, int tier)
        {
            string state = ResolveTechTierStateLabel(pad, tier);
            switch (state)
            {
                case "Unlocked":
                    return new Color(0.26f, 0.50f, 0.34f, 0.98f);
                case "Current Tier":
                    return new Color(0.37f, 0.49f, 0.72f, 0.98f);
                case "Current Max":
                    return new Color(0.72f, 0.56f, 0.18f, 0.98f);
                case "Available Now":
                case "Next Upgrade":
                    return new Color(0.78f, 0.62f, 0.18f, 0.98f);
                case "Future Tier":
                    return new Color(0.22f, 0.24f, 0.30f, 0.96f);
                default:
                    return new Color(0.20f, 0.20f, 0.22f, 0.94f);
            }
        }

        Color ResolveTechTierChipTextColor(MLFortressPad pad, int tier)
        {
            string state = ResolveTechTierStateLabel(pad, tier);
            return state == "Future Tier" || state == "Locked"
                ? new Color(0.90f, 0.90f, 0.94f, 1f)
                : new Color(0.10f, 0.09f, 0.06f, 1f);
        }

        void CreateStatusChip(Transform parent, string text, Color backgroundColor, Color textColor)
        {
            if (parent == null)
                return;

            var chip = new GameObject("StatusChip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            chip.transform.SetParent(parent, false);
            chip.GetComponent<Image>().color = backgroundColor;
            var layout = chip.GetComponent<LayoutElement>();
            layout.minHeight = 22f;
            layout.preferredHeight = 22f;
            layout.preferredWidth = 92f;

            var label = CreateInlineText(
                chip.transform,
                "Label",
                text,
                IsCompactPanelLayout() ? 9.5f : 10.5f,
                textColor,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 3f);
            rect.offsetMax = new Vector2(-6f, -3f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        static int CompareHeroEntries(MLHeroRosterEntry a, MLHeroRosterEntry b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.sortIndex.CompareTo(b.sortIndex);
        }

        Color ResolveHeroAccentColor(MLHeroRosterEntry hero)
        {
            switch ((hero?.heroVisualStyleKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "regal_gold":
                    return new Color(0.92f, 0.74f, 0.24f, 0.98f);
                case "holy_silver":
                    return new Color(0.78f, 0.87f, 1.00f, 0.98f);
                case "radiant_bishop":
                    return new Color(0.82f, 0.66f, 0.94f, 0.98f);
                default:
                    return new Color(0.82f, 0.76f, 0.48f, 0.98f);
            }
        }

        Color ResolveHeroBadgeBackground(MLHeroRosterEntry hero)
        {
            var accent = ResolveHeroAccentColor(hero);
            return hero != null && string.Equals(hero.state, "locked", System.StringComparison.OrdinalIgnoreCase)
                ? Color.Lerp(accent, new Color(0.12f, 0.12f, 0.14f, 1f), 0.72f)
                : Color.Lerp(accent, new Color(0.16f, 0.17f, 0.20f, 1f), 0.42f);
        }

        Color ResolveHeroBadgeTextColor(MLHeroRosterEntry hero)
        {
            return hero != null && string.Equals(hero.state, "ready", System.StringComparison.OrdinalIgnoreCase)
                ? new Color(1f, 0.97f, 0.90f, 1f)
                : new Color(0.94f, 0.90f, 0.82f, 1f);
        }

        Color ResolveHeroPanelCardTint(MLHeroRosterEntry hero)
        {
            var accent = ResolveHeroAccentColor(hero);
            float mix = hero != null && string.Equals(hero.state, "locked", System.StringComparison.OrdinalIgnoreCase) ? 0.16f : 0.32f;
            return Color.Lerp(new Color(0.11f, 0.12f, 0.18f, 0.98f), accent, mix);
        }

        Color ResolveHeroPanelStateTextColor(MLHeroRosterEntry hero)
        {
            switch ((hero?.state ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "ready":
                    return new Color(0.94f, 1f, 0.92f, 1f);
                case "cooldown":
                    return new Color(1f, 0.90f, 0.72f, 1f);
                case "active":
                    return new Color(0.88f, 0.96f, 1f, 1f);
                default:
                    return new Color(0.96f, 0.90f, 0.82f, 1f);
            }
        }

        string BuildHeroOverviewStateText(MLHeroRosterEntry hero)
        {
            if (hero == null)
                return "Unavailable";

            switch ((hero.state ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "ready":
                    return "Ready in Barracks";
                case "cooldown":
                    return $"Cooldown {GetHeroCooldownSeconds(hero, true)}s";
                case "active":
                    return $"Active {Mathf.Max(0, hero.activeCount)}/{Mathf.Max(1, hero.activeLimit)}";
                case "locked":
                    return string.IsNullOrWhiteSpace(hero.lockedReason) ? "Locked" : hero.lockedReason;
                default:
                    return string.IsNullOrWhiteSpace(hero.disabledReason) ? "Unavailable" : hero.disabledReason;
            }
        }

        string BuildFocusedBarracksHeroStateText(MLLaneSnap lane, MLBarracksSite site, MLHeroRosterEntry hero)
        {
            if (hero == null)
                return "Hero data unavailable.";

            if (site == null || !site.isBuilt)
                return "Buy this Barracks to summon heroes.";

            switch ((hero.state ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "locked":
                    return string.IsNullOrWhiteSpace(hero.lockedReason) ? "Castle required." : hero.lockedReason;
                case "cooldown":
                    return $"{hero.displayName} is recharging. Ready in {GetHeroCooldownSeconds(hero, true)}s.";
                case "active":
                    return $"{hero.displayName} is already deployed. Active {Mathf.Max(0, hero.activeCount)}/{Mathf.Max(1, hero.activeLimit)}.";
                case "disabled":
                    return string.IsNullOrWhiteSpace(hero.disabledReason) ? "Hero unavailable." : hero.disabledReason;
                case "ready":
                    if (lane != null && lane.gold < hero.summonCost)
                        return $"Ready, but need {Mathf.Max(0, hero.summonCost - Mathf.FloorToInt(lane.gold))}g more.";
                    return $"Ready to deploy from {ResolveBarracksDisplayName(site)}.";
                default:
                    return "Hero unavailable.";
            }
        }

        string BuildFocusedBarracksHeroDeployLabel(MLLaneSnap lane, MLBarracksSite site, MLHeroRosterEntry hero)
        {
            if (hero == null)
                return "Deploy";
            if (site == null || !site.isBuilt)
                return "Open Town Core";

            switch ((hero.state ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "locked":
                    return "Castle Locked";
                case "cooldown":
                    return $"Cooldown {GetHeroCooldownSeconds(hero, true)}s";
                case "active":
                    return "Already Active";
                case "disabled":
                    return "Unavailable";
                case "ready":
                    if (lane != null && lane.gold < hero.summonCost)
                        return $"Need {Mathf.Max(0, hero.summonCost - Mathf.FloorToInt(lane.gold))}g";
                    return $"Deploy {hero.summonCost}g";
                default:
                    return "Unavailable";
            }
        }

        string BuildFocusedBarracksHeroStatusChip(MLHeroRosterEntry hero)
        {
            if (hero == null)
                return "Hero";

            switch ((hero.state ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "locked":
                    return hero.unlockBuildingTierName;
                case "cooldown":
                    return $"CD {GetHeroCooldownSeconds(hero, true)}s";
                case "active":
                    return $"{Mathf.Max(0, hero.activeCount)}/{Mathf.Max(1, hero.activeLimit)} Active";
                case "ready":
                    return "Ready";
                default:
                    return "Unavailable";
            }
        }

        bool CanDeployBarracksHero(MLLaneSnap lane, MLBarracksSite site, MLHeroRosterEntry hero)
        {
            return CanEditBarracks()
                && lane != null
                && site != null
                && site.isBuilt
                && hero != null
                && string.Equals(hero.state, "ready", System.StringComparison.OrdinalIgnoreCase)
                && lane.gold >= hero.summonCost;
        }

        void ExecuteFocusedBarracksHeroDeploy(MLBarracksSite site, MLHeroRosterEntry hero)
        {
            if (site == null || hero == null)
                return;

            _statusMessage = $"Deploying {hero.displayName} from {ResolveBarracksDisplayName(site)}...";
            ActionSender.DeployBarracksHero(hero.heroKey, site.barracksId);
            RefreshHeader(force: true);
        }

        int GetHeroCooldownSeconds(MLHeroRosterEntry hero, bool remaining = false)
        {
            int tickHz = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20;
            int ticks = remaining ? hero != null ? hero.cooldownTicksRemaining : 0 : hero != null ? hero.cooldownTicks : 0;
            return Mathf.Max(0, Mathf.CeilToInt(ticks / Mathf.Max(1f, tickHz)));
        }

        int CountBuiltFortressPads(MLFortressPad[] pads)
        {
            if (pads == null)
                return 0;

            int total = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i] != null && pads[i].isBuilt)
                    total += 1;
            }

            return total;
        }

        int CountLockedFortressPads(MLFortressPad[] pads)
        {
            if (pads == null)
                return 0;

            int total = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad != null && !pad.isBuilt && string.Equals(pad.buildState, "locked", System.StringComparison.OrdinalIgnoreCase))
                    total += 1;
            }

            return total;
        }

        int CountOverviewPads(MLFortressPad[] pads)
        {
            if (pads == null)
                return 0;

            int total = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad == null || string.Equals(pad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                total += 1;
            }

            return total;
        }

        int CountBuiltOverviewPads(MLFortressPad[] pads)
        {
            if (pads == null)
                return 0;

            int total = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad == null || string.Equals(pad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (pad.isBuilt)
                    total += 1;
            }

            return total;
        }

        int CountLockedOverviewPads(MLFortressPad[] pads)
        {
            if (pads == null)
                return 0;

            int total = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad == null || string.Equals(pad.buildingType, "barracks", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!pad.isBuilt && string.Equals(pad.buildState, "locked", System.StringComparison.OrdinalIgnoreCase))
                    total += 1;
            }

            return total;
        }

        int CountBuiltBarracksSites(MLBarracksSite[] sites)
        {
            if (sites == null)
                return 0;

            int total = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i] != null && sites[i].isBuilt)
                    total += 1;
            }

            return total;
        }

        string BuildBuildingOverviewBarracksBody(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "No barracks selected.";

            string ownership = site.isBuilt
                ? $"Purchased   Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}"
                : site.canBuild
                    ? $"Buy It   Cost {Mathf.Max(0, site.buildCost)}g"
                    : "Locked";

            string stateLine = site.isBuilt
                ? $"Send {GetBarracksSiteSendSecondsRemaining(site)}s   Active Units {CountActiveUnitsForBarracks(lane, site)}"
                : string.IsNullOrWhiteSpace(site.lockedReason)
                    ? "Purchase or upgrade this barracks from Town Core."
                    : site.lockedReason;

            return
                $"{ownership}\n" +
                $"{stateLine}\n" +
                $"Current Options: {CountPurchasableUnits(site.roster)}   Owned Units: {CountOwnedUnits(site.roster)}";
        }

        void OpenOverviewPad(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null)
                return;

            bool opened = FortressSelectionController.OpenFortressPad(lane.laneIndex, pad.padId);
            if (!opened)
            {
                _statusMessage = $"Unable to focus {pad.buildingName}.";
                RefreshHeader(force: true);
            }
        }

        void OpenOverviewBarracks(MLLaneSnap lane, MLBarracksSite site)
        {
            if (lane == null || site == null)
                return;

            bool opened = FortressSelectionController.OpenBarracksSite(lane.laneIndex, site.barracksId);
            if (!opened)
            {
                _statusMessage = $"Unable to focus {ResolveBarracksDisplayName(site)}.";
                RefreshHeader(force: true);
            }
        }

        void ClearGuidedUnlockContext()
        {
            _guidedUnlockPadId = null;
            _guidedUnlockUnitKey = null;
            _guidedUnlockUnitName = null;
            _guidedUnlockBuildingType = null;
            _guidedUnlockBuildingName = null;
            _guidedUnlockBarracksId = null;
            _guidedUnlockRequiredTier = 0;
        }

        void SetGuidedUnlockContext(MLBarracksRosterEntry entry, MLFortressPad pad = null, MLBarracksSite site = null)
        {
            if (entry == null || (pad == null && site == null))
            {
                ClearGuidedUnlockContext();
                return;
            }

            _guidedUnlockPadId = pad != null ? pad.padId : null;
            _guidedUnlockUnitKey = entry.rosterKey;
            _guidedUnlockUnitName = entry.displayName;
            _guidedUnlockBuildingType = entry.unlockBuildingType;
            _guidedUnlockBuildingName = !string.IsNullOrWhiteSpace(entry.unlockBuildingName)
                ? entry.unlockBuildingName
                : site != null
                    ? ResolveBarracksDisplayName(site)
                    : !string.IsNullOrWhiteSpace(pad?.buildingName)
                        ? pad.buildingName
                        : HumanizeCombatType(entry.unlockBuildingType);
            _guidedUnlockBarracksId = site != null ? NormalizeBarracksId(site.barracksId) : null;
            _guidedUnlockRequiredTier = Mathf.Max(1, entry.requiredBuildingTier);
        }

        bool TryGetGuidedUnlockForPad(MLFortressPad pad, out GuidedPadAction action, out string helperText)
        {
            action = GuidedPadAction.None;
            helperText = null;

            if (pad == null
                || string.IsNullOrWhiteSpace(_guidedUnlockPadId)
                || !string.Equals(_guidedUnlockPadId, pad.padId, System.StringComparison.OrdinalIgnoreCase))
                return false;

            string unitName = string.IsNullOrWhiteSpace(_guidedUnlockUnitName) ? "This unit" : _guidedUnlockUnitName;
            string buildingName = string.IsNullOrWhiteSpace(_guidedUnlockBuildingName)
                ? (!string.IsNullOrWhiteSpace(pad.buildingName) ? pad.buildingName : HumanizeCombatType(_guidedUnlockBuildingType))
                : _guidedUnlockBuildingName;
            int requiredTier = Mathf.Max(1, _guidedUnlockRequiredTier);

            if (!pad.isBuilt)
            {
                if (pad.canBuild)
                {
                    action = GuidedPadAction.Build;
                    helperText = $"{unitName} unlocks from {buildingName} Tier {requiredTier}. Build this branch here to start that unlock path.";
                    return true;
                }

                action = GuidedPadAction.Explain;
                helperText = $"{unitName} unlocks from {buildingName} Tier {requiredTier}. {pad.lockedReason ?? "This branch is not buildable yet."}";
                return true;
            }

            if (Mathf.Max(0, pad.tier) < requiredTier)
            {
                if (pad.canUpgrade)
                {
                    action = GuidedPadAction.Upgrade;
                    helperText = $"{unitName} needs {buildingName} Tier {requiredTier}. Upgrade this branch here.";
                    return true;
                }

                action = GuidedPadAction.Explain;
                helperText = $"{unitName} needs {buildingName} Tier {requiredTier}. {pad.lockedReason ?? "This branch cannot upgrade yet."}";
                return true;
            }

            helperText = $"{unitName} routes through {buildingName} Tier {requiredTier}. This branch already meets that requirement.";
            return true;
        }

        bool TryResolveLockedUnitUnlockPad(MLLaneSnap lane, MLBarracksRosterEntry entry, out MLFortressPad pad)
        {
            pad = null;
            if (lane?.fortressPads == null || entry == null)
                return false;

            if (!string.IsNullOrWhiteSpace(entry.unlockPadId))
            {
                for (int i = 0; i < lane.fortressPads.Length; i++)
                {
                    var candidate = lane.fortressPads[i];
                    if (candidate != null && string.Equals(candidate.padId, entry.unlockPadId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        pad = candidate;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.unlockBuildingType))
            {
                pad = FindFortressPadByBuildingType(lane, entry.unlockBuildingType);
                if (pad != null)
                    return true;
            }

            return false;
        }

        void RedirectLockedUnitToUnlockBuilding(MLLaneSnap lane, MLBarracksRosterEntry entry, MLBarracksSite sourceSite = null)
        {
            if (lane == null || entry == null)
                return;

            if (string.Equals(entry.unlockBuildingType, "barracks", System.StringComparison.OrdinalIgnoreCase))
            {
                if (sourceSite != null)
                {
                    OpenTownCore(lane, entry, targetSite: sourceSite);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(entry.unlockBuildingType) && string.IsNullOrWhiteSpace(entry.unlockPadId))
            {
                string message =
                    $"[BarracksPanel] Locked-unit redirect failed for unit '{entry.displayName}' " +
                    $"(rosterKey='{entry.rosterKey}'): missing unlock requirement mapping.";
                Debug.LogWarning(message);
                _statusMessage = $"{entry.displayName}: unlock building mapping missing. See console.";
                RefreshHeader(force: true);
                return;
            }

            if (!TryResolveLockedUnitUnlockPad(lane, entry, out var pad) || pad == null)
            {
                string message =
                    $"[BarracksPanel] Locked-unit redirect failed for unit '{entry.displayName}' " +
                    $"(rosterKey='{entry.rosterKey}'): missing fortress pad mapping for " +
                    $"unlockBuildingType='{entry.unlockBuildingType}' tier='{Mathf.Max(1, entry.requiredBuildingTier)}'.";
                Debug.LogWarning(message);
                _statusMessage = $"{entry.displayName}: unlock route missing. See console.";
                RefreshHeader(force: true);
                return;
            }

            OpenTownCore(lane, entry, targetPad: pad);
        }

        string BuildLockedUnitRedirectHint(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return "Click to open Town Core.";

            string unlockLabel = BuildFocusedBarracksUnlockLabel(entry);
            return string.IsNullOrWhiteSpace(unlockLabel) || string.Equals(unlockLabel, "unlock requirement", System.StringComparison.OrdinalIgnoreCase)
                ? "Click to open Town Core."
                : $"Click to open Town Core for {unlockLabel}.";
        }

        string BuildLockedUnitRedirectActionLabel(MLBarracksRosterEntry entry)
        {
            return entry == null ? "Open Town Core" : "Open Town Core";
        }

        string BuildFocusedBarracksSummary(MLBarracksSite site)
        {
            if (site == null)
                return string.Empty;

            if (site.isConstructing)
                return $"{BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site))}   {site.constructionTargetTierName}";

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseStatus(site);

            int ownedUnits = CountOwnedUnits(site.roster);
            return
                $"Health {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}   " +
                $"{BuildBarracksFoodLabel(site)}   Owned Units {ownedUnits}   Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}";
        }

        string BuildFocusedBarracksHeaderText(MLLaneSnap lane, MLBarracksSite site, int waveSeconds)
        {
            if (site == null)
                return string.Empty;

            if (site.isConstructing)
                return $"{BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site))}   Target {site.constructionTargetTierName}";

            if (site.isDestroyed)
                return $"Destroyed   {site.displayName} is offline";

            if (!site.isBuilt)
            {
                return site.canBuild
                    ? $"Purchasable   Town Core T{Mathf.Max(1, site.requiredTownCoreTier)} met"
                    : $"Locked   {BuildFocusedBarracksRequirementText(site)}";
            }

            int sendSeconds = GetBarracksSiteSendSecondsRemaining(site);
            int spawnIntervalSeconds = GetFocusedBarracksSpawnIntervalSeconds(site);
            var parts = new List<string>
            {
                $"HP {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}",
                BuildBarracksRosterFoodLabel(site),
                $"Active {CountActiveUnitsForBarracks(lane, site)}",
                $"Owned {CountOwnedUnits(site.roster)}",
                $"Current Options {CountPurchasableUnits(site.roster)}",
                $"Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}",
            };
            string activeFoodLabel = BuildBarracksActiveFoodLabel(site);
            if (!string.IsNullOrWhiteSpace(activeFoodLabel))
                parts.Add(activeFoodLabel);
            if (spawnIntervalSeconds >= 0)
                parts.Add($"Interval {spawnIntervalSeconds}s");
            if (sendSeconds >= 0)
                parts.Add($"Send {sendSeconds}s");
            if (waveSeconds >= 0)
                parts.Add($"Wave {waveSeconds}s");
            return string.Join("   ", parts);
        }

        string BuildFocusedBarracksRosterStatus(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return string.Empty;

            if (site.isConstructing)
                return $"{site.displayName} is still under construction.";

            if (site.isDestroyed)
                return $"{site.displayName} is destroyed and cannot send units.";

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseStatus(site);

            string activeLead = BuildBarracksActivityLead(lane, site, string.Empty);
            if (!string.IsNullOrWhiteSpace(activeLead))
                return $"Active {activeLead}";

            int purchasableUnits = CountPurchasableUnits(site.roster);
            return purchasableUnits > 0
                ? $"This barracks currently offers {purchasableUnits} live unit option{(purchasableUnits == 1 ? string.Empty : "s")}. Each branch only shows its active tier."
                : "No current unit options are available yet. Use Town Core to build or upgrade the linked branches for each line.";
        }

        string BuildFocusedBarracksHint(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Waiting for barracks data...";

            if (site.isConstructing)
                return $"{BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site))}. The barracks roster unlocks when construction finishes.";

            if (site.isDestroyed)
                return $"{site.displayName} is destroyed and currently offline.";

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseHint(lane, site);

            int purchasableUnits = CountPurchasableUnits(site.roster);
            if (purchasableUnits <= 0)
                return "No current barracks options are live yet. Use Town Core to unlock the required buildings first.";

            if (GetBarracksFoodLimit(site) > 0 && GetBarracksFoodRemaining(site) <= 0)
                return $"{site.displayName} is at its roster cap. Sell owned units or wait for losses before buying more.";

            int cheapestCost = GetCheapestPurchasableCost(site.roster);
            if (lane != null && cheapestCost > 0 && lane.gold < cheapestCost)
                return $"Need {Mathf.Max(0, cheapestCost - Mathf.FloorToInt(lane.gold))} more gold for the cheapest current unit purchase.";

            return $"Scroll down to review the live roster. Each unit row keeps its purchase action on the right for quick taps. {BuildBarracksFoodLabel(site)}.";
        }

        string BuildFocusedBarracksOverview(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "No barracks selected.";

            if (site.isConstructing)
            {
                return
                    $"State {HumanizeBuildState(site.buildState)}\n" +
                    $"{BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site))}\n" +
                    $"Target {site.constructionTargetTierName}";
            }

            if (site.isDestroyed)
                return $"HP 0/{Mathf.RoundToInt(site.maxHp)}   {site.displayName} is destroyed.";

            if (!site.isBuilt)
            {
                var lines = new List<string>
                {
                    $"State {ResolveFocusedBarracksStateLabel(site)}",
                    BuildFocusedBarracksCostText(site),
                };
                lines.Add(site.canBuild
                    ? "Manage this barracks from Town Core."
                    : BuildFocusedBarracksRequirementText(site));
                return string.Join("\n", lines);
            }

            int spawnIntervalSeconds = GetFocusedBarracksSpawnIntervalSeconds(site);
            string intervalLine = spawnIntervalSeconds >= 0 ? $"Spawn Interval {spawnIntervalSeconds}s" : "Spawn Interval unavailable";
            return
                $"HP {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}   {BuildBarracksFoodLabel(site)}   {intervalLine}   Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}\n" +
                $"Currently active: {BuildBarracksActivityLead(lane, site, "No active units are being sent from this barracks.")}";
        }

        void CaptureFocusedBarracksRailPosition()
        {
            if (_contentRoot == null)
                return;

            var rail = _contentRoot.Find("FocusedBarracksCardRail");
            var viewport = rail != null ? rail.Find("Viewport") : null;
            var scrollRect = viewport != null ? viewport.GetComponent<ScrollRect>() : null;
            if (scrollRect == null || !CanScrollHorizontally(scrollRect))
                return;

            _focusedBarracksRailNormalizedPosition = scrollRect.horizontalNormalizedPosition;
        }

        void RestoreFocusedBarracksRailPosition(RectTransform contentRoot)
        {
            if (contentRoot == null)
                return;

            var scrollRect = contentRoot.GetComponentInParent<ScrollRect>();
            if (scrollRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            scrollRect.StopMovement();
            scrollRect.horizontalNormalizedPosition = CanScrollHorizontally(scrollRect)
                ? Mathf.Clamp01(_focusedBarracksRailNormalizedPosition)
                : 0f;
        }

        string BuildFocusedBarracksCardBody(MLBarracksSite site)
        {
            if (site == null)
                return "No barracks selected.";

            if (site.isConstructing)
            {
                return
                    $"{BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site))}\n" +
                    $"Target {site.constructionTargetTierName}";
            }

            if (site.isDestroyed)
                return $"Health 0/{Mathf.RoundToInt(site.maxHp)}\nDestroyed";

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseStatus(site);

            string text =
                $"Health {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}\n" +
                $"{BuildBarracksFoodLabel(site)}\n" +
                $"{BuildBarracksSpawnSummary(site.roster)}";

            if (!string.IsNullOrWhiteSpace(site.lockedReason))
                text += $"\n{site.lockedReason}";

            return text;
        }

        string BuildFocusedBarracksPurchaseStatus(MLBarracksSite site)
        {
            if (site == null)
                return string.Empty;

            if (site.isConstructing)
                return BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site));

            return BuildFocusedBarracksCostText(site);
        }

        string BuildFocusedBarracksPurchaseHint(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Waiting for barracks data...";

            if (!site.canBuild)
                return BuildFocusedBarracksRequirementText(site);

            if (lane != null && lane.gold < site.buildCost)
                return $"Need {Mathf.Max(0, site.buildCost - Mathf.FloorToInt(lane.gold))} more gold to purchase this barracks in Town Core.";

            return "Open Town Core to purchase this barracks. Unit buying stays on the barracks screen after it is built.";
        }

        void SyncFocusedBarracksHeaderAction(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
            {
                HideHeaderActionButton();
                return;
            }

            if (site.isConstructing || site.isDestroyed)
            {
                ConfigureHeaderActionButton(
                    "Open Town Core",
                    () => OpenTownCore(lane),
                    true,
                    new Color(0.34f, 0.26f, 0.08f, 0.98f));
                return;
            }

            if (!site.isBuilt || site.canUpgrade)
            {
                ConfigureHeaderActionButton(
                    "Open Town Core",
                    () => OpenTownCore(lane),
                    true,
                    new Color(0.19f, 0.31f, 0.19f, 0.98f));
                return;
            }

            HideHeaderActionButton();
        }

        static string BuildFocusedBarracksRequirementText(MLBarracksSite site)
        {
            return $"Requires Town Core T{Mathf.Max(1, site != null ? site.requiredTownCoreTier : 1)}";
        }

        static string BuildFocusedBarracksCostText(MLBarracksSite site)
        {
            return $"Cost: {Mathf.Max(0, site != null ? site.buildCost : 0)}g";
        }

        static string BuildFocusedBarracksBuildLabel(MLBarracksSite site)
        {
            return "Open Town Core";
        }

        static string ResolveFocusedBarracksStateLabel(MLBarracksSite site)
        {
            if (site == null)
                return "Unavailable";

            if (!site.isBuilt)
                return site.canBuild ? "Purchasable" : "Locked";

            return HumanizeBuildState(site.buildState);
        }

        bool IsPendingBarracksBuild(MLBarracksSite site)
        {
            return site != null
                && !string.IsNullOrWhiteSpace(_pendingBarracksBuildId)
                && string.Equals(_pendingBarracksBuildId, NormalizeBarracksId(site.barracksId), System.StringComparison.OrdinalIgnoreCase);
        }

        static string BuildPendingBarracksSellKey(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return null;

            string barracksId = NormalizeBarracksId(site.barracksId);
            string rosterKey = string.IsNullOrWhiteSpace(entry.rosterKey)
                ? null
                : entry.rosterKey.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(barracksId) || string.IsNullOrWhiteSpace(rosterKey))
                return null;

            return $"{barracksId}:{rosterKey}";
        }

        bool IsPendingBarracksSell(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            string key = BuildPendingBarracksSellKey(site, entry);
            return !string.IsNullOrWhiteSpace(key)
                && string.Equals(_pendingBarracksSellKey, key, System.StringComparison.OrdinalIgnoreCase);
        }

        void ClearPendingBarracksSell()
        {
            _pendingBarracksSellKey = null;
        }

        bool CanSellBarracksRosterEntry(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            return site != null
                && entry != null
                && site.isBuilt
                && entry.ownedCount > 0
                && CanEditBarracks()
                && !IsPendingBarracksSell(site, entry);
        }

        int GetFocusedBarracksSpawnIntervalSeconds(MLBarracksSite site)
        {
            if (site == null || site.sendIntervalTicks <= 0)
                return -1;

            int tickHz = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20;
            return Mathf.Max(1, Mathf.CeilToInt(site.sendIntervalTicks / (float)Mathf.Max(1, tickHz)));
        }

        string BuildBarracksRosterLead(MLBarracksRosterEntry[] roster, string emptyText = "No roster data available for this barracks.")
        {
            if (roster == null || roster.Length == 0)
                return emptyText;

            var parts = new List<string>();
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null || entry.ownedCount <= 0)
                    continue;

                parts.Add($"{entry.displayName} x{entry.ownedCount}");
            }

            return parts.Count > 0
                ? string.Join("   |   ", parts)
                : emptyText;
        }

        string BuildBarracksSpawnSummary(MLBarracksRosterEntry[] roster)
        {
            string summary = BuildBarracksRosterLead(roster, "No units attached to this barracks yet.");
            return summary.StartsWith("No ", System.StringComparison.OrdinalIgnoreCase)
                ? summary
                : $"Spawning {summary}";
        }

        string BuildBarracksActivityLead(MLLaneSnap lane, MLBarracksSite site, string emptyText)
        {
            if (lane == null || site == null)
                return emptyText;

            return BarracksActivityUtility.BuildActivityLead(
                SnapshotApplier.Instance != null ? SnapshotApplier.Instance.LatestML : null,
                lane.laneIndex,
                site.barracksId,
                emptyText);
        }

        int CountActiveUnitsForBarracks(MLLaneSnap lane, MLBarracksSite site)
        {
            if (lane == null || site == null)
                return 0;

            return BarracksActivityUtility.CountActiveUnitsForBarracks(
                SnapshotApplier.Instance != null ? SnapshotApplier.Instance.LatestML : null,
                lane.laneIndex,
                site.barracksId);
        }

        int CountActiveUnitsForRosterEntry(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (lane == null || site == null || entry == null)
                return 0;

            return BarracksActivityUtility.CountActiveUnitsForRosterEntry(
                SnapshotApplier.Instance != null ? SnapshotApplier.Instance.LatestML : null,
                lane.laneIndex,
                site.barracksId,
                entry);
        }

        int CountOwnedUnits(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int total = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null) continue;
                total += Mathf.Max(0, entry.ownedCount);
            }

            return total;
        }

        List<MLBarracksRosterEntry> GetCurrentBarracksRosterEntries(MLBarracksRosterEntry[] roster)
        {
            var visibleEntries = new List<MLBarracksRosterEntry>();
            if (roster == null)
                return visibleEntries;

            var visibleByBranch = new Dictionary<string, MLBarracksRosterEntry>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null)
                    continue;

                string branchKey = string.IsNullOrWhiteSpace(entry.branchKey)
                    ? entry.rosterKey ?? string.Empty
                    : entry.branchKey;
                if (entry.availableForPurchase || entry.currentTier)
                {
                    visibleByBranch[branchKey] = entry;
                    continue;
                }

                if (visibleByBranch.TryGetValue(branchKey, out var existingVisible)
                    && (existingVisible.availableForPurchase || existingVisible.currentTier))
                {
                    continue;
                }

                if (!visibleByBranch.TryGetValue(branchKey, out existingVisible)
                    || Mathf.Max(1, entry.tier) < Mathf.Max(1, existingVisible.tier)
                    || (Mathf.Max(1, entry.tier) == Mathf.Max(1, existingVisible.tier) && entry.sortIndex < existingVisible.sortIndex))
                {
                    visibleByBranch[branchKey] = entry;
                }
            }

            foreach (var entry in visibleByBranch.Values)
                visibleEntries.Add(entry);
            return visibleEntries;
        }

        int CountUnlockedUnits(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int total = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && entry.unlocked)
                    total += 1;
            }

            return total;
        }

        int CountPurchasableUnits(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int total = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && entry.availableForPurchase)
                    total += 1;
            }

            return total;
        }

        int GetCheapestUnlockedCost(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int cheapest = int.MaxValue;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null || !entry.unlocked)
                    continue;

                cheapest = Mathf.Min(cheapest, Mathf.Max(0, entry.buyCost));
            }

            return cheapest == int.MaxValue ? 0 : cheapest;
        }

        int GetCheapestPurchasableCost(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int cheapest = int.MaxValue;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry == null || !entry.availableForPurchase)
                    continue;

                cheapest = Mathf.Min(cheapest, Mathf.Max(0, entry.buyCost));
            }

            return cheapest == int.MaxValue ? 0 : cheapest;
        }

        string BuildRosterSummary(MLLaneSnap lane)
        {
            if (lane == null || lane.barracksRoster == null)
                return string.Empty;

            int owned = 0;
            int unlocked = 0;
            for (int i = 0; i < lane.barracksRoster.Length; i++)
            {
                var entry = lane.barracksRoster[i];
                if (entry == null) continue;
                owned += Mathf.Max(0, entry.ownedCount);
                if (entry.unlocked) unlocked += 1;
            }

            return $"Owned Units {owned}   Unlocked Entries {unlocked}/{lane.barracksRoster.Length}";
        }

        string BuildHeaderHint(MLLaneSnap lane, int sendSeconds, int waveSeconds)
        {
            if (lane == null)
                return "Waiting for lane data...";

            return $"Fortress loop live. Next barracks send in {Mathf.Max(0, sendSeconds)}s. Next wave in {Mathf.Max(0, waveSeconds)}s.";
        }

        string BuildFocusedPadSummary(MLLaneSnap lane, MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return string.Empty;

            if (pad.isConstructing)
                return $"{BuildConstructionTimerLabel(pad.constructionKind, GetConstructionSecondsRemaining(pad))}   {pad.constructionTargetTierName}";

            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var currentEntry = GetCurrentMarketRosterEntry(lane);
                return pad.isBuilt
                    ? currentEntry != null
                        ? $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}   {BuildMarketFoodLabel(lane, pad)}   {currentEntry.displayName} tier   +{GetMarketIncomePerTick(currentEntry)}g / {GetMarketIncomeSeconds()}s"
                        : $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}   {BuildMarketFoodLabel(lane, pad)}   Market ready"
                    : $"Cost {pad.buildCost}g   Unlocks {BuildUnlockPreview(pad, roster, heroRoster)}";
            }

            string unlocks = BuildUnlockPreview(pad, roster, heroRoster);
            return pad.isBuilt
                ? $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}   Unlocks {unlocks}"
                : $"Cost {pad.buildCost}g   Unlocks {unlocks}";
        }

        string BuildFocusedPadHint(MLLaneSnap lane, MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster, int sendSeconds, int waveSeconds)
        {
            if (pad == null)
                return "Select a fortress pad.";

            if (pad.isConstructing)
                return $"{BuildConstructionTimerLabel(pad.constructionKind, GetConstructionSecondsRemaining(pad))}. {pad.constructionTargetTierName} is still under construction.";

            if (pad.isDestroyed)
                return $"{pad.buildingName} is destroyed and offline.";

            string primary;
            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var currentEntry = GetCurrentMarketRosterEntry(lane);
                var nextEntry = GetNextMarketRosterEntry(lane, currentEntry);
                string blockedReason = GetMarketBuyBlockedReason(lane, pad, currentEntry);
                primary = !pad.isBuilt && pad.canBuild
                    ? $"Open Town Core to construct {pad.buildingName}."
                    : pad.canUpgrade
                        ? nextEntry != null
                            ? $"Open Town Core to convert all current market contracts into {nextEntry.displayName} income."
                            : $"Open Town Core to advance {pad.buildingName}."
                        : currentEntry != null
                            ? !string.IsNullOrWhiteSpace(blockedReason)
                                ? $"{blockedReason} before buying more {currentEntry.displayName} contracts. {BuildMarketFoodLabel(lane, pad)}."
                                : $"Buy {currentEntry.displayName} contracts to raise timed income. {BuildMarketFoodLabel(lane, pad)}."
                            : !string.IsNullOrWhiteSpace(pad.lockedReason)
                                ? pad.lockedReason
                                : $"{pad.buildingName} is ready.";
            }
            else
            {
                primary = !pad.isBuilt && pad.canBuild
                    ? $"Open Town Core to construct {pad.buildingName}."
                    : pad.canUpgrade
                        ? $"Open Town Core to advance {pad.buildingName}."
                        : !string.IsNullOrWhiteSpace(pad.lockedReason)
                            ? pad.lockedReason
                            : $"{pad.buildingName} is ready.";
            }

            string nextPreview = BuildNextUpgradePreview(lane, pad, roster, heroRoster);
            if (!string.IsNullOrWhiteSpace(nextPreview))
                primary += $"  {nextPreview}";

            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
                return primary;

            return $"{primary}  Send {Mathf.Max(0, sendSeconds)}s  Wave {Mathf.Max(0, waveSeconds)}s.";
        }

        static bool IsRepairEligiblePad(MLFortressPad pad)
        {
            return pad != null
                && pad.isBuilt
                && !pad.isConstructing
                && !string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase)
                && pad.maxHp > 0f
                && pad.hp < pad.maxHp;
        }

        static bool IsRepairEligibleBarracksSite(MLBarracksSite site)
        {
            return site != null
                && site.isBuilt
                && !site.isConstructing
                && site.maxHp > 0f
                && site.hp < site.maxHp;
        }

        static int GetRepairAllEligibleCount(MLLaneSnap lane)
        {
            if (lane == null)
                return 0;

            int total = 0;
            if (lane.fortressPads != null)
            {
                for (int i = 0; i < lane.fortressPads.Length; i++)
                {
                    if (IsRepairEligiblePad(lane.fortressPads[i]))
                        total += 1;
                }
            }

            if (lane.barracksSites != null)
            {
                for (int i = 0; i < lane.barracksSites.Length; i++)
                {
                    if (IsRepairEligibleBarracksSite(lane.barracksSites[i]))
                        total += 1;
                }
            }

            return total;
        }

        static int GetRepairAllMissingHp(MLLaneSnap lane)
        {
            if (lane == null)
                return 0;

            int total = 0;
            if (lane.fortressPads != null)
            {
                for (int i = 0; i < lane.fortressPads.Length; i++)
                {
                    var pad = lane.fortressPads[i];
                    if (!IsRepairEligiblePad(pad))
                        continue;
                    total += Mathf.Max(0, Mathf.RoundToInt(pad.maxHp - pad.hp));
                }
            }

            if (lane.barracksSites != null)
            {
                for (int i = 0; i < lane.barracksSites.Length; i++)
                {
                    var site = lane.barracksSites[i];
                    if (!IsRepairEligibleBarracksSite(site))
                        continue;
                    total += Mathf.Max(0, Mathf.RoundToInt(site.maxHp - site.hp));
                }
            }

            return total;
        }

        string BuildLumberMillRepairSummary(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null || !string.Equals(pad.buildingType, "lumber_mill", System.StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            int eligibleCount = GetRepairAllEligibleCount(lane);
            if (eligibleCount <= 0)
                return "Repair All: no damaged non-Town-Core buildings.";

            int totalMissingHp = GetRepairAllMissingHp(lane);
            int availableGold = Mathf.Max(0, Mathf.FloorToInt(lane.gold));
            int spendNow = Mathf.Min(totalMissingHp, availableGold);
            return
                $"Repair All: {eligibleCount} target{(eligibleCount == 1 ? string.Empty : "s")}, " +
                $"{totalMissingHp} HP missing, {spendNow}g ready.";
        }

        bool TryBuildLumberMillRepairAction(MLLaneSnap lane, MLFortressPad pad, out string label, out bool enabled, out string lockedReason)
        {
            label = string.Empty;
            enabled = false;
            lockedReason = null;

            if (pad == null || !string.Equals(pad.buildingType, "lumber_mill", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (pad.isConstructing)
            {
                label = "Constructing";
                lockedReason = "Lumber Mill construction is still in progress.";
                return true;
            }

            if (!pad.isBuilt)
            {
                label = "Build Lumber Mill";
                lockedReason = "Build the Lumber Mill first.";
                return true;
            }

            int totalMissingHp = GetRepairAllMissingHp(lane);
            if (totalMissingHp <= 0)
            {
                label = "No Repairs";
                lockedReason = "All eligible buildings are already at full HP.";
                return true;
            }

            int availableGold = lane != null ? Mathf.Max(0, Mathf.FloorToInt(lane.gold)) : 0;
            int spendNow = Mathf.Min(totalMissingHp, availableGold);
            if (spendNow <= 0)
            {
                label = "Need Gold";
                lockedReason = "Repair All needs gold.";
                return true;
            }

            enabled = true;
            label = spendNow >= totalMissingHp
                ? $"Repair All {totalMissingHp}g"
                : $"Repair {spendNow}/{totalMissingHp}g";
            return true;
        }

        string BuildFocusedPadCardBody(MLLaneSnap lane, MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return "No building pad selected.";

            if (pad.isConstructing)
            {
                return
                    $"{BuildConstructionTimerLabel(pad.constructionKind, GetConstructionSecondsRemaining(pad))}\n" +
                    $"Target {pad.constructionTargetTierName}\n" +
                    $"Unlocks: {BuildUnlockPreview(pad, roster, heroRoster)}";
            }

            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var currentEntry = GetCurrentMarketRosterEntry(lane);
                string marketText =
                    $"Tier {Mathf.Max(0, pad.tier)}/{Mathf.Max(1, pad.maxTier)}   " +
                    $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}   " +
                    $"{BuildMarketFoodLabel(lane, pad)}";

                if (currentEntry != null)
                {
                    marketText +=
                        $"\nCurrent Tier: {currentEntry.displayName}   Owned x{Mathf.Max(0, currentEntry.ownedCount)} / {GetMarketFoodLimit(pad)}   Buy {Mathf.Max(0, currentEntry.buyCost)}g each" +
                        $"\nIncome Tick: +{GetMarketIncomePerTick(currentEntry)} gold every {GetMarketIncomeSeconds()}s" +
                        $"\nTier Value: +{Mathf.Max(0, currentEntry.economyLapGold)} gold per contract";
                }
                else
                {
                    marketText += $"\nUnlocks: {BuildUnlockPreview(pad, roster, heroRoster)}";
                }

                string marketNextPreview = BuildNextUpgradePreview(lane, pad, roster, heroRoster);
                if (!string.IsNullOrWhiteSpace(marketNextPreview))
                    marketText += $"\n{marketNextPreview}";

                string lumberMillRepairSummary = BuildLumberMillRepairSummary(lane, pad);
                if (!string.IsNullOrWhiteSpace(lumberMillRepairSummary))
                    marketText += $"\n{lumberMillRepairSummary}";

                if (!string.IsNullOrWhiteSpace(pad.lockedReason))
                    marketText += $"\nRequirement: {pad.lockedReason}";

                return marketText;
            }

            string text =
                $"Tier {Mathf.Max(0, pad.tier)}/{Mathf.Max(1, pad.maxTier)}   " +
                $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}\n" +
                $"Unlocks: {BuildUnlockPreview(pad, roster, heroRoster)}";

            string nextPreview = BuildNextUpgradePreview(lane, pad, roster, heroRoster);
            if (!string.IsNullOrWhiteSpace(nextPreview))
                text += $"\n{nextPreview}";

            string repairSummary = BuildLumberMillRepairSummary(lane, pad);
            if (!string.IsNullOrWhiteSpace(repairSummary))
                text += $"\n{repairSummary}";

            if (!string.IsNullOrWhiteSpace(pad.lockedReason))
                text += $"\nRequirement: {pad.lockedReason}";

            return text;
        }

        string BuildNextUpgradePreview(MLLaneSnap lane, MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return string.Empty;

            if (pad.maxTier <= 0 || pad.tier >= pad.maxTier)
                return "Next Upgrade: Max tier reached.";

            int targetTier = Mathf.Max(1, pad.tier + 1);
            if (string.Equals(pad.buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var nextEntry = FindMarketRosterEntryByTier(lane, targetTier);
                if (nextEntry != null)
                    return $"Next Upgrade: {ResolveBuildingTierName(pad.buildingType, targetTier, pad.nextTierName)} converts all existing market contracts into {nextEntry.displayName} income.";

                return $"Next Upgrade: {ResolveBuildingTierName(pad.buildingType, targetTier, pad.nextTierName)} strengthens timed market income.";
            }

            var unlocks = new List<string>();
            if (roster != null)
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    var entry = roster[i];
                    if (entry == null)
                        continue;
                    if (!string.Equals(entry.unlockBuildingType, pad.buildingType))
                        continue;
                    if (entry.requiredBuildingTier != targetTier)
                        continue;
                    unlocks.Add(entry.displayName);
                }
            }

            if (heroRoster != null
                && string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase)
                && targetTier >= 4)
            {
                for (int i = 0; i < heroRoster.Length; i++)
                {
                    var hero = heroRoster[i];
                    if (hero == null)
                        continue;
                    if (!string.Equals(hero.unlockBuildingType, pad.buildingType, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Mathf.Max(1, hero.requiredBuildingTier) != targetTier)
                        continue;
                    unlocks.Add(hero.displayName);
                }
            }

            if (unlocks.Count == 0)
                return $"Next Upgrade: T{targetTier} with no new roster unlocks.";

            return $"Next Upgrade: T{targetTier} unlocks {string.Join(", ", unlocks)}.";
        }

        MLBarracksSite GetFocusedBarracksSite(MLLaneSnap lane)
        {
            if (lane == null || string.IsNullOrWhiteSpace(_focusedBarracksId))
                return null;

            if (lane.barracksSites != null)
            {
                for (int i = 0; i < lane.barracksSites.Length; i++)
                {
                    var site = lane.barracksSites[i];
                    if (site != null && string.Equals(site.barracksId, _focusedBarracksId, System.StringComparison.OrdinalIgnoreCase))
                        return site;
                }
            }

            string key = $"{lane.slotColor}:{_focusedBarracksId}";
            if (_missingBarracksSiteLogs.Add(key))
            {
                Debug.LogError(
                    $"[BarracksPanel] Missing barracks snapshot data for lane '{lane.slotColor}' barracks '{_focusedBarracksId}'. " +
                    "The panel will not fabricate a fallback barracks site.");
            }

            return null;
        }

        MLFortressPad GetFocusedPad(MLLaneSnap lane)
        {
            if (lane?.fortressPads == null || string.IsNullOrWhiteSpace(_focusedPadId))
                return null;

            for (int i = 0; i < lane.fortressPads.Length; i++)
            {
                var pad = lane.fortressPads[i];
                if (pad != null && string.Equals(pad.padId, _focusedPadId, System.StringComparison.OrdinalIgnoreCase))
                    return pad;
            }

            return null;
        }

        static int ComparePads(MLFortressPad a, MLFortressPad b)
        {
            return GetPadSortIndex(a).CompareTo(GetPadSortIndex(b));
        }

        static int CompareBarracksSites(MLBarracksSite a, MLBarracksSite b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.sortIndex.CompareTo(b.sortIndex);
        }

        static int CompareRosterEntries(MLBarracksRosterEntry a, MLBarracksRosterEntry b)
        {
            int roleCompare = GetRoleSortIndex(a).CompareTo(GetRoleSortIndex(b));
            if (roleCompare != 0) return roleCompare;
            return a.sortIndex.CompareTo(b.sortIndex);
        }

        string BuildFocusedBarracksEntryMeta(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return string.Empty;

            bool displayUnlockSource = !string.IsNullOrWhiteSpace(entry.unlockBuildingType)
                && !string.Equals(entry.unlockBuildingType, entry.productionBuildingType, System.StringComparison.OrdinalIgnoreCase);
            string displayBuildingType = displayUnlockSource
                ? entry.unlockBuildingType
                : entry.productionBuildingType;
            string buildingName = displayUnlockSource
                ? entry.unlockBuildingName
                : entry.productionBuildingName;
            if (string.IsNullOrWhiteSpace(buildingName))
                buildingName = displayUnlockSource ? entry.productionBuildingName : entry.unlockBuildingName;

            int displayTier = displayUnlockSource
                ? Mathf.Max(1, entry.requiredBuildingTier)
                : Mathf.Max(1, entry.tier);
            string tierName = ResolveBuildingTierName(displayBuildingType, displayTier, $"Tier {displayTier}");
            if (string.Equals(buildingName, tierName, System.StringComparison.OrdinalIgnoreCase))
                buildingName = null;
            return string.IsNullOrWhiteSpace(buildingName)
                ? tierName
                : $"{buildingName}  •  {tierName}";
        }

        string BuildFocusedBarracksUnitEyebrow(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return "UNIT";

            bool displayUnlockSource = !string.IsNullOrWhiteSpace(entry.unlockBuildingType)
                && !string.Equals(entry.unlockBuildingType, entry.productionBuildingType, System.StringComparison.OrdinalIgnoreCase);
            string buildingName = displayUnlockSource
                ? entry.unlockBuildingName
                : entry.productionBuildingName;
            if (string.IsNullOrWhiteSpace(buildingName))
                buildingName = displayUnlockSource ? entry.productionBuildingName : entry.unlockBuildingName;

            string buildingType = displayUnlockSource ? entry.unlockBuildingType : entry.productionBuildingType;
            int displayTier = displayUnlockSource
                ? Mathf.Max(1, entry.requiredBuildingTier)
                : Mathf.Max(1, entry.tier);
            string tierName = ResolveBuildingTierName(buildingType, displayTier, $"Tier {displayTier}");
            if (string.IsNullOrWhiteSpace(buildingName))
                return tierName;

            return string.Equals(buildingName, tierName, System.StringComparison.OrdinalIgnoreCase)
                ? tierName
                : $"{buildingName} - {tierName}";
        }

        bool TryBuildFocusedBarracksUnitStatTiles(MLBarracksRosterEntry entry, out List<PanelRowStatData> stats)
        {
            stats = null;
            if (!TryGetUnitCardStats(entry, out var unit) || unit == null)
                return false;

            stats = new List<PanelRowStatData>
            {
                CreatePanelRowStat(
                    "ATTACK",
                    $"{HumanizeCombatType(unit.damage_type)} {FormatStatNumber(unit.attack_damage)} @ {Mathf.Max(0.01f, unit.attack_speed):0.##}/s",
                    new Color(0.19f, 0.13f, 0.10f, 0.98f),
                    new Color(0.98f, 0.88f, 0.76f, 0.98f),
                    Color.white),
                CreatePanelRowStat(
                    "HEALTH",
                    $"{FormatStatNumber(unit.hp)} HP",
                    new Color(0.14f, 0.16f, 0.19f, 0.98f),
                    SilverTextColor,
                    Color.white),
                CreatePanelRowStat(
                    "DEFENSE",
                    $"{HumanizeCombatType(unit.armor_type)} | {Mathf.Max(0f, unit.damage_reduction_pct):0.#}%",
                    new Color(0.11f, 0.15f, 0.19f, 0.98f),
                    new Color(0.84f, 0.91f, 0.97f, 0.98f),
                    Color.white),
                CreatePanelRowStat(
                    "REACH",
                    $"{FormatStatNumber(unit.range)} rng | {FormatStatNumber(Mathf.Max(0f, unit.path_speed))} mv",
                    new Color(0.14f, 0.12f, 0.18f, 0.98f),
                    new Color(0.89f, 0.86f, 0.97f, 0.98f),
                    Color.white),
            };

            return true;
        }

        static int GetRosterBuildingSortIndex(MLBarracksRosterEntry entry)
        {
            switch ((entry?.productionBuildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "blacksmith": return 0;
                case "temple": return 1;
                case "wizard_tower": return 2;
                case "archery_tower": return 3;
                default: return 99;
            }
        }

        static int CompareFocusedBarracksRosterEntries(MLBarracksRosterEntry a, MLBarracksRosterEntry b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int buildingCompare = GetRosterBuildingSortIndex(a).CompareTo(GetRosterBuildingSortIndex(b));
            if (buildingCompare != 0) return buildingCompare;

            int tierCompare = Mathf.Max(1, a.tier).CompareTo(Mathf.Max(1, b.tier));
            if (tierCompare != 0) return tierCompare;

            return CompareRosterEntries(a, b);
        }

        static int GetPadSortIndex(MLFortressPad pad)
        {
            if (pad == null || string.IsNullOrWhiteSpace(pad.buildingType))
                return 999;

            switch (pad.buildingType)
            {
                case "town_core": return 0;
                case "barracks": return 1;
                case "blacksmith": return 2;
                case "temple": return 3;
                case "wizard_tower": return 4;
                case "archery_tower": return 5;
                case "market": return 6;
                case "stable": return 7;
                case "workshop": return 8;
                case "library": return 9;
                case "lumber_mill": return 10;
                case "wall": return 40;
                case "gate": return 41;
                case "turret": return 42;
                default: return (pad.gridY * 100) + pad.gridX;
            }
        }

        static Color ResolveBarracksOverviewCardTint(MLBarracksSite site)
        {
            if (site == null)
                return ObsidianElevatedColor;

            if (site.isBuilt)
                return site.canUpgrade
                    ? new Color(0.18f, 0.16f, 0.11f, 0.98f)
                    : new Color(0.11f, 0.12f, 0.14f, 0.98f);

            if (site.canBuild)
                return new Color(0.16f, 0.13f, 0.10f, 0.98f);

            return ObsidianElevatedColor;
        }

        static int GetRoleSortIndex(MLBarracksRosterEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.role))
                return 99;

            switch (entry.role)
            {
                case "melee": return 0;
                case "ranged": return 1;
                case "support": return 2;
                default: return 99;
            }
        }

        static string BuildUnlockPreview(MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            return BuildUnlockPreview(pad != null ? pad.buildingType : null, roster, heroRoster);
        }

        static string BuildUnlockPreview(string buildingType, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (string.IsNullOrWhiteSpace(buildingType))
                return "No unlock data";

            if (string.Equals(buildingType, "market", System.StringComparison.OrdinalIgnoreCase))
            {
                var marketConfigs = SnapshotApplier.Instance?.LatestMLMatchConfig?.marketRosterConfigs;
                if (marketConfigs != null)
                {
                    var unlocks = new List<string>();
                    for (int i = 0; i < marketConfigs.Length; i++)
                    {
                        var entry = marketConfigs[i];
                        if (entry == null)
                            continue;
                        unlocks.Add($"T{Mathf.Max(1, entry.requiredBuildingTier)}: {entry.displayName}");
                    }

                    if (unlocks.Count > 0)
                        return string.Join("  |  ", unlocks);
                }

                return "T1: Peasant  |  T2: Settler  |  T3: Trader";
            }

            var preview = string.Empty;
            if (roster != null)
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    var entry = roster[i];
                    if (entry == null || !string.Equals(entry.unlockBuildingType, buildingType))
                        continue;

                    if (preview.Length > 0) preview += "  |  ";
                    preview += $"T{entry.requiredBuildingTier}: {entry.displayName}";
                }
            }

            if (heroRoster != null && string.Equals(buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase))
            {
                var heroNames = new List<string>();
                for (int i = 0; i < heroRoster.Length; i++)
                {
                    var hero = heroRoster[i];
                    if (hero == null)
                        continue;
                    if (!string.Equals(hero.unlockBuildingType, buildingType, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    heroNames.Add(hero.displayName);
                }

                if (heroNames.Count > 0)
                {
                    if (preview.Length > 0) preview += "  |  ";
                    preview += $"Castle: {string.Join(", ", heroNames)}";
                }
            }

            return preview.Length > 0 ? preview : "No unlocks on this branch yet";
        }

        static Color ResolvePadCardTint(MLFortressPad pad)
        {
            if (pad == null)
                return new Color(0.10f, 0.11f, 0.14f, 0.95f);

            switch (pad.buildState)
            {
                case "available_to_build":
                    return new Color(0.16f, 0.13f, 0.10f, 0.98f);
                case "constructing":
                    return new Color(0.17f, 0.13f, 0.10f, 0.98f);
                case "upgrading":
                    return new Color(0.18f, 0.15f, 0.11f, 0.98f);
                case "upgrade_available":
                    return new Color(0.18f, 0.16f, 0.11f, 0.98f);
                case "destroyed":
                    return new Color(0.24f, 0.11f, 0.09f, 0.98f);
                case "locked":
                    return ObsidianElevatedColor;
                case "max_tier":
                    return new Color(0.16f, 0.14f, 0.10f, 0.98f);
                default:
                    return new Color(0.11f, 0.12f, 0.15f, 0.98f);
            }
        }

        static string HumanizeBuildState(string value)
        {
            switch (value)
            {
                case "available_to_build": return "Available To Build";
                case "constructing": return "Constructing";
                case "upgrading": return "Upgrading";
                case "under_repair": return "Under Repair";
                case "upgrade_available": return "Upgrade Available";
                case "destroyed": return "Destroyed";
                case "max_tier": return "Max Tier";
                case "locked": return "Locked";
                default: return "Built";
            }
        }

        static string HumanizePhase(string value)
        {
            switch (value)
            {
                case "build": return "Build";
                case "combat": return "Combat";
                case "transition": return "Transition";
                default: return string.IsNullOrWhiteSpace(value) ? "Build" : value;
            }
        }

        static string HumanizeAction(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Update";
            return value.Replace("_", " ");
        }

        static string NormalizeBarracksId(string barracksId)
        {
            if (string.IsNullOrWhiteSpace(barracksId))
                return string.Empty;

            switch (barracksId.Trim().ToLowerInvariant())
            {
                case "left_barracks":
                case "barracks_left":
                    return "left";
                case "right_barracks":
                case "barracks_right":
                    return "right";
                case "centre":
                case "center_barracks":
                case "barracks_center":
                case "barracks_centre":
                    return "center";
                default:
                    return barracksId.Trim().ToLowerInvariant();
            }
        }

        static string HumanizeBarracksId(string barracksId)
        {
            switch (NormalizeBarracksId(barracksId))
            {
                case "left": return "Left Barracks";
                case "right": return "Right Barracks";
                case "center": return "Center Barracks";
                default: return "Barracks";
            }
        }

        static string ResolveBarracksDisplayName(MLBarracksSite site)
        {
            if (site == null)
                return "Barracks";

            string fromId = HumanizeBarracksId(site.barracksId);
            return string.Equals(fromId, "Barracks", System.StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(site.displayName)
                    ? site.displayName
                    : fromId;
        }

        int GetBarracksSendSecondsRemaining(MLLaneSnap lane)
        {
            if (lane == null)
                return -1;

            var snapshotApplier = SnapshotApplier.Instance;
            return snapshotApplier != null
                ? snapshotApplier.GetBarracksSendSecondsRemaining(lane.laneIndex)
                : -1;
        }

        int GetBarracksSiteSendSecondsRemaining(MLBarracksSite site)
        {
            if (site == null)
                return -1;

            int laneIndex = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null && laneIndex >= 0)
                return snapshotApplier.GetBarracksSiteSendSecondsRemaining(laneIndex, site.barracksId);

            return Mathf.CeilToInt(site.sendTimerTicksRemaining / Mathf.Max(1f, SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20));
        }

        int GetConstructionSecondsRemaining(MLFortressPad pad)
        {
            if (pad == null)
                return -1;

            return Mathf.CeilToInt(pad.constructionTimerTicksRemaining / Mathf.Max(1f, SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20));
        }

        int GetConstructionSecondsRemaining(MLBarracksSite site)
        {
            if (site == null)
                return -1;

            return Mathf.CeilToInt(site.constructionTimerTicksRemaining / Mathf.Max(1f, SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20));
        }

        static string BuildConstructionTimerLabel(string constructionKind, int seconds)
        {
            string verb = string.Equals(constructionKind, "upgrade", System.StringComparison.OrdinalIgnoreCase)
                ? "Upgrading"
                : "Building";
            return $"{verb} {Mathf.Max(0, seconds)}s";
        }

        bool IsPadFocused(string padId)
        {
            return !string.IsNullOrWhiteSpace(padId)
                && string.Equals(_focusedPadId, padId, System.StringComparison.OrdinalIgnoreCase)
                && Time.unscaledTime <= _focusedPadUntil;
        }

        bool IsBarracksFocused(string barracksId)
        {
            return !string.IsNullOrWhiteSpace(barracksId)
                && string.Equals(_focusedBarracksId, NormalizeBarracksId(barracksId), System.StringComparison.OrdinalIgnoreCase);
        }

        static string BuildFocusedBarracksActionObjectName(string actionKey, string rosterKey = null)
        {
            string safeAction = SanitizeUiNameToken(actionKey);
            string safeRosterKey = string.IsNullOrWhiteSpace(rosterKey)
                ? string.Empty
                : $"_{SanitizeUiNameToken(rosterKey)}";
            return $"FocusedBarracksAction_{safeAction}{safeRosterKey}";
        }

        static string BuildFocusedBarracksCardObjectName(string rosterKey)
        {
            return $"FocusedBarracksCard_{SanitizeUiNameToken(rosterKey)}";
        }

        static string BuildTownCoreBarracksRowObjectName(string barracksId)
        {
            return $"TownCoreBarracksRow_{SanitizeUiNameToken(NormalizeBarracksId(barracksId))}";
        }

        static string BuildTownCoreBarracksActionObjectName(string actionKey, string barracksId)
        {
            string safeAction = SanitizeUiNameToken(actionKey);
            string safeBarracksId = SanitizeUiNameToken(NormalizeBarracksId(barracksId));
            return $"TownCoreBarracksAction_{safeAction}_{safeBarracksId}";
        }

        static string BuildTownCorePadRowObjectName(string padId)
        {
            return $"TownCorePadRow_{SanitizeUiNameToken(padId)}";
        }

        static string BuildTownCorePadActionObjectName(string actionKey, string padId)
        {
            string safeAction = SanitizeUiNameToken(actionKey);
            string safePadId = SanitizeUiNameToken(padId);
            return $"TownCorePadAction_{safeAction}_{safePadId}";
        }

        static string BuildFocusedPadRowObjectName(string padId)
        {
            return $"FocusedPadRow_{SanitizeUiNameToken(padId)}";
        }

        static string BuildFocusedPadActionObjectName(string actionKey, string padId)
        {
            string safeAction = SanitizeUiNameToken(actionKey);
            string safePadId = SanitizeUiNameToken(padId);
            return $"FocusedPadAction_{safeAction}_{safePadId}";
        }

        static string BuildFocusedMarketActionObjectName(string actionKey, string unitKey = null)
        {
            string safeAction = SanitizeUiNameToken(actionKey);
            string safeUnitKey = string.IsNullOrWhiteSpace(unitKey)
                ? string.Empty
                : $"_{SanitizeUiNameToken(unitKey)}";
            return $"FocusedMarketAction_{safeAction}{safeUnitKey}";
        }

        static string SanitizeUiNameToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.Length > 0 ? builder.ToString() : "Unknown";
        }

        static Button FindNamedButton(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
                return null;

            var buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button != null && string.Equals(button.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                    return button;
            }

            return null;
        }

        static Transform FindNamedTransform(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
                return null;

            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null)
                    continue;
                if (string.Equals(child.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                    return child;

                var nested = FindNamedTransform(child, targetName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        RectTransform CreateVerticalBlock(Transform parent, string name, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return go.GetComponent<RectTransform>();
        }

        RectTransform CreateHorizontalBlock(Transform parent, string name, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            return go.GetComponent<RectTransform>();
        }

        RectTransform CreateHorizontalFillBlock(Transform parent, string name, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return go.GetComponent<RectTransform>();
        }

        TMP_Text CreateInlineText(
            Transform parent,
            string name,
            string text,
            float fontSize,
            Color color,
            FontStyles fontStyle,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = Mathf.Max(MinimumPanelFontSize, fontSize);
            tmp.fontStyle = fontStyle;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.text = text;
            return tmp;
        }

        void CreateCountChip(Transform parent, string text, Color backgroundColor)
        {
            var chip = new GameObject("CountChip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            chip.transform.SetParent(parent, false);
            chip.GetComponent<Image>().color = backgroundColor;

            var layout = chip.GetComponent<LayoutElement>();
            layout.minWidth = 56f;
            layout.preferredWidth = 56f;
            layout.minHeight = 34f;

            var label = CreateInlineText(
                chip.transform,
                "Label",
                text,
                16f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        void CreateRosterPortrait(Transform parent, MLBarracksRosterEntry entry, float preferredHeight)
        {
            if (entry == null)
                return;

            CreateRosterPortrait(
                parent,
                new[] { entry.portraitKey, entry.skinKey, entry.catalogUnitKey, entry.unitTypeKey, entry.archetypeKey, entry.rosterKey },
                $"{entry.rosterKey}:{entry.archetypeKey}:{entry.presentationKey}:{entry.skinKey}:{entry.unitTypeKey}",
                preferredHeight);
        }

        void CreateRosterPortrait(Transform parent, string[] lookupKeys, string logKey, float preferredHeight)
        {
            var frame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frame.transform.SetParent(parent, false);
            frame.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.14f, 0.98f);

            var frameLayout = frame.GetComponent<LayoutElement>();
            frameLayout.minHeight = preferredHeight;
            frameLayout.preferredHeight = preferredHeight;
            frameLayout.flexibleWidth = 1f;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(frame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(3f, 3f);
            portraitRect.offsetMax = new Vector2(-3f, -3f);

            var raw = portraitGo.GetComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = new Color(1f, 1f, 1f, 0f);

            var fitter = portraitGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;

            var missingLabel = CreateInlineText(
                frame.transform,
                "MissingPortrait",
                "No Portrait",
                13f,
                new Color(1f, 0.82f, 0.60f, 0.95f),
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var missingRect = missingLabel.rectTransform;
            missingRect.anchorMin = Vector2.zero;
            missingRect.anchorMax = Vector2.one;
            missingRect.offsetMin = new Vector2(8f, 8f);
            missingRect.offsetMax = new Vector2(-8f, -8f);

            if (TryGetRosterPortraitTexture(lookupKeys, logKey, out var texture))
            {
                raw.texture = texture;
                raw.color = Color.white;
                missingLabel.gameObject.SetActive(false);
            }
        }

        bool TryGetRosterPortraitTexture(MLBarracksRosterEntry entry, out Texture2D texture)
        {
            if (entry == null)
            {
                texture = null;
                return false;
            }

            return TryGetRosterPortraitTexture(
                new[] { entry.portraitKey, entry.skinKey, entry.catalogUnitKey, entry.unitTypeKey, entry.archetypeKey, entry.rosterKey },
                $"{entry.rosterKey}:{entry.archetypeKey}:{entry.presentationKey}:{entry.skinKey}:{entry.unitTypeKey}",
                out texture);
        }

        bool TryGetRosterPortraitTexture(string[] lookupKeys, string logKey, out Texture2D texture)
        {
            texture = null;
            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null)
            {
                LogMissingPortrait(logKey, "Remote content manager is unavailable.");
                return false;
            }

            if (lookupKeys != null)
            {
                for (int i = 0; i < lookupKeys.Length; i++)
                {
                    string key = lookupKeys[i];
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (remoteContent.TryGetLoadedPortraitTexture(key, out texture) && texture != null)
                        return true;
                }
            }

            LogMissingPortrait(logKey, "No loaded portrait matched the requested keys.");
            return false;
        }

        static void TintCard(RectTransform card, Color color)
        {
            if (card == null)
                return;

            var image = card.GetComponent<Image>();
            if (image != null)
                image.color = color;
        }

        RectTransform CreateCardContainer(Transform parent = null)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent != null ? parent : _contentRoot, false);
            var rt = go.GetComponent<RectTransform>();
            var image = go.GetComponent<Image>();
            image.color = new Color(0.11f, 0.14f, 0.20f, 0.95f);
            ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PortraitBackdrop, true, image.color);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            int horizontalPadding = IsCompactPanelLayout() ? 10 : 12;
            int verticalPadding = IsCompactPanelLayout() ? 8 : 10;
            layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
            layout.spacing = IsCompactPanelLayout() ? 6f : 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var element = go.GetComponent<LayoutElement>();
            element.minHeight = IsCompactPanelLayout() ? 72f : 88f;
            return rt;
        }

        void WireOverviewCardNavigation(RectTransform card, UnityEngine.Events.UnityAction action)
        {
            if (card == null || action == null)
                return;

            var button = card.GetComponent<Button>();
            if (button == null)
                button = card.gameObject.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            button.transition = Selectable.Transition.ColorTint;

            var image = card.GetComponent<Image>();
            if (image != null)
                button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.88f, 1f);
            colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.selectedColor = new Color(1f, 0.94f, 0.78f, 1f);
            colors.disabledColor = new Color(0.82f, 0.82f, 0.82f, 0.9f);
            button.colors = colors;
        }

        void WireLockedRosterCardNavigation(RectTransform card, UnityEngine.Events.UnityAction action)
        {
            if (card == null || action == null)
                return;

            var button = card.GetComponent<Button>();
            if (button == null)
                button = card.gameObject.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            button.transition = Selectable.Transition.ColorTint;

            var image = card.GetComponent<Image>();
            if (image != null)
                button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.90f, 0.76f, 1f);
            colors.pressedColor = new Color(0.88f, 0.80f, 0.70f, 1f);
            colors.selectedColor = new Color(1f, 0.94f, 0.82f, 1f);
            colors.disabledColor = new Color(0.74f, 0.74f, 0.74f, 0.9f);
            button.colors = colors;
        }

        RectTransform CreateActionRow(Transform parent)
        {
            var go = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = IsCompactPanelLayout() ? 6f : 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            return go.GetComponent<RectTransform>();
        }

        float GetContentViewportWidth()
        {
            if (_scrollRect != null && _scrollRect.viewport != null)
            {
                float viewportWidth = _scrollRect.viewport.rect.width;
                if (viewportWidth > 0f)
                    return viewportWidth;
            }

            if (PanelBarracks != null && PanelBarracks.TryGetComponent<RectTransform>(out var panelRect))
            {
                float panelWidth = panelRect.rect.width;
                if (panelWidth > 0f)
                    return Mathf.Max(0f, panelWidth * 0.90f);
            }

            return 0f;
        }

        float GetContentViewportHeight()
        {
            if (_scrollRect != null && _scrollRect.viewport != null)
            {
                float viewportHeight = _scrollRect.viewport.rect.height;
                if (viewportHeight > 0f)
                    return viewportHeight;
            }

            if (PanelBarracks != null && PanelBarracks.TryGetComponent<RectTransform>(out var panelRect))
            {
                float panelHeight = panelRect.rect.height;
                if (panelHeight > 0f)
                    return Mathf.Max(0f, panelHeight * 0.64f);
            }

            return 0f;
        }

        float GetSectionHeaderHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 18f : 20f) : 28f;
        float GetFocusedBarracksDetailCardHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 58f : 76f) : 110f;
        float GetFocusedBarracksCardMinWidth() => IsCompactPanelLayout() ? 196f : FocusedBarracksMinCardWidth;
        float GetFocusedBarracksCardMaxWidth() => IsCompactPanelLayout() ? 236f : FocusedBarracksMaxCardWidth;
        float GetFocusedBarracksCardMinHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 224f : 276f) : FocusedBarracksMinCardHeight;
        float GetFocusedBarracksCardMaxHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 272f : 332f) : FocusedBarracksMaxCardHeight;
        float GetFocusedBarracksRailFooterGap() => IsCompactPanelLayout() ? 4f : FocusedBarracksRailFooterGap;
        float GetFocusedBarracksRailFooterHeight() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 22f : 24f) : FocusedBarracksRailFooterHeight;
        float GetFocusedBarracksScrollbarHeight() => IsCompactPanelLayout() ? 10f : FocusedBarracksScrollbarHeight;
        float GetFocusedBarracksRailButtonWidth() => IsCompactPanelLayout() ? 28f : FocusedBarracksRailButtonWidth;
        float GetFocusedBarracksRailSideGap() => IsCompactPanelLayout() ? 6f : 8f;

        float GetFocusedBarracksReservedHeight()
        {
            int blocksBeforeRail = IsCompactPanelLayout() ? 2 : 3;
            float reserved = GetFocusedBarracksDetailCardHeight() + GetSectionHeaderHeight();
            if (!IsCompactPanelLayout())
                reserved += GetSectionHeaderHeight();
            reserved += GetContentStackSpacing() * blocksBeforeRail;
            reserved += GetFocusedBarracksRailFooterGap() + GetFocusedBarracksRailFooterHeight();
            return reserved;
        }

        void GetFocusedBarracksCardSize(out float cardWidth, out float cardHeight)
        {
            float availableWidth = Mathf.Max(280f, GetContentViewportWidth() - 8f);
            int visibleCards = GetFocusedBarracksVisibleCardCount(availableWidth);
            float totalSpacing = FocusedBarracksCardSpacing * Mathf.Max(0, visibleCards - 1);
            cardWidth = Mathf.Floor((availableWidth - totalSpacing) / Mathf.Max(1, visibleCards));
            cardWidth = Mathf.Clamp(cardWidth, GetFocusedBarracksCardMinWidth(), GetFocusedBarracksCardMaxWidth());

            float reservedHeight = GetFocusedBarracksReservedHeight();
            float availableHeight = GetContentViewportHeight() - reservedHeight;
            cardHeight = Mathf.Clamp(availableHeight, GetFocusedBarracksCardMinHeight(), GetFocusedBarracksCardMaxHeight());
        }

        static int GetFocusedBarracksVisibleCardCount(float availableWidth)
        {
            if (availableWidth <= 430f)
                return 1;

            if (availableWidth <= 640f)
                return 2;

            if (availableWidth <= 860f)
                return 3;

            return FocusedBarracksTargetVisibleCards;
        }

        int CountRosterEntries(MLBarracksRosterEntry[] roster)
        {
            if (roster == null)
                return 0;

            int total = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                if (roster[i] != null)
                    total += 1;
            }

            return total;
        }

        int GetFocusedBarracksExpectedRosterCount(MLBarracksSite site)
        {
            var configRoster = SnapshotApplier.Instance?.LatestMLMatchConfig?.barracksRosterConfigs;
            if (configRoster != null && configRoster.Length > 0)
                return configRoster.Length;

            return CountRosterEntries(site?.roster);
        }

        Button CreateFocusedBarracksRailButton(Transform parent, string name, string labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.16f, 0.19f, 0.25f, 0.98f);

            var button = go.GetComponent<Button>();

            var label = CreateInlineText(
                go.transform,
                "Label",
                labelText,
                IsCompactPanelLayout() ? 13f : 16f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            return button;
        }

        void ScrollFocusedBarracksRail(ScrollRect scrollRect, float direction)
        {
            if (scrollRect?.content == null || scrollRect.viewport == null)
                return;

            float hiddenWidth = scrollRect.content.rect.width - scrollRect.viewport.rect.width;
            if (hiddenWidth <= 0f)
                return;

            float step = Mathf.Clamp(scrollRect.viewport.rect.width * 0.82f, 180f, hiddenWidth);
            float nextX = Mathf.Clamp(scrollRect.content.anchoredPosition.x + (direction * step), -hiddenWidth, 0f);
            var anchored = scrollRect.content.anchoredPosition;
            anchored.x = nextX;
            scrollRect.StopMovement();
            scrollRect.content.anchoredPosition = anchored;
            UpdateFocusedBarracksRailButtons(
                scrollRect,
                scrollRect.transform.parent.Find("Footer/PreviousButton")?.GetComponent<Button>(),
                scrollRect.transform.parent.Find("Footer/NextButton")?.GetComponent<Button>());
        }

        void SyncFocusedBarracksRailChrome(RectTransform contentRoot)
        {
            if (contentRoot == null)
                return;

            var scrollRect = contentRoot.GetComponentInParent<ScrollRect>();
            if (scrollRect == null)
                return;

            var previousButton = scrollRect.transform.parent.Find("Footer/PreviousButton")?.GetComponent<Button>();
            var nextButton = scrollRect.transform.parent.Find("Footer/NextButton")?.GetComponent<Button>();
            UpdateFocusedBarracksRailButtons(scrollRect, previousButton, nextButton);
        }

        void UpdateFocusedBarracksRailButtons(ScrollRect scrollRect, Button previousButton, Button nextButton)
        {
            bool canScroll = CanScrollHorizontally(scrollRect);
            float hiddenWidth = canScroll && scrollRect?.content != null && scrollRect.viewport != null
                ? scrollRect.content.rect.width - scrollRect.viewport.rect.width
                : 0f;
            float currentX = scrollRect?.content != null ? scrollRect.content.anchoredPosition.x : 0f;

            if (previousButton != null)
                previousButton.interactable = canScroll && currentX < -0.5f;
            if (nextButton != null)
                nextButton.interactable = canScroll && currentX > (-hiddenWidth + 0.5f);
        }

        static bool CanScrollHorizontally(ScrollRect scrollRect)
        {
            return scrollRect != null
                && scrollRect.content != null
                && scrollRect.viewport != null
                && scrollRect.content.rect.width > scrollRect.viewport.rect.width + 0.5f;
        }

        static Color ResolveFocusedBarracksCardTint(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return new Color(0.10f, 0.11f, 0.14f, 0.96f);

            if (!entry.unlocked)
                return new Color(0.15f, 0.14f, 0.15f, 0.96f);

            Color baseColor = entry.productionBuildingType switch
            {
                "blacksmith" => new Color(0.16f, 0.13f, 0.10f, 0.98f),
                "barracks" => new Color(0.16f, 0.13f, 0.10f, 0.98f),
                "market" => new Color(0.16f, 0.13f, 0.10f, 0.98f),
                _ => new Color(0.11f, 0.12f, 0.15f, 0.96f),
            };

            return entry.ownedCount > 0
                ? Color.Lerp(baseColor, GoldSurfaceColor, 0.18f)
                : baseColor;
        }

        RectTransform CreateFocusedBarracksStatGrid(Transform parent, float cardWidth)
        {
            var go = new GameObject("StatGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            float totalSpacing = 6f;
            float cellWidth = Mathf.Max(44f, Mathf.Floor((Mathf.Max(100f, cardWidth) - 20f - totalSpacing) / 2f));
            float cellHeight = IsCompactPanelLayout() ? 30f : 38f;

            var grid = go.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.spacing = new Vector2(6f, 6f);
            grid.cellSize = new Vector2(cellWidth, cellHeight);
            grid.childAlignment = TextAnchor.UpperLeft;

            var element = go.GetComponent<LayoutElement>();
            float gridHeight = IsCompactPanelLayout() ? 66f : 82f;
            element.minHeight = gridHeight;
            element.preferredHeight = gridHeight;

            return go.GetComponent<RectTransform>();
        }

        void CreateFocusedBarracksPrimaryActions(Transform parent, MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (parent == null || site == null || entry == null)
                return;

            var actionRow = CreateHorizontalFillBlock(parent, "PrimaryActions", 6f);
            bool unlocksFromBarracks = string.Equals(entry.unlockBuildingType, "barracks", System.StringComparison.OrdinalIgnoreCase);
            if (!site.isBuilt && unlocksFromBarracks)
            {
                CreateFocusedBarracksActionChip(
                    actionRow,
                    BuildFocusedBarracksActionObjectName("BuyBuilding", site.barracksId),
                    "Open Town Core",
                    new Color(0.36f, 0.24f, 0.10f, 0.98f),
                    true,
                    () => OpenTownCore(lane, entry, targetSite: site));
                return;
            }

            if (!entry.unlocked)
            {
                CreateFocusedBarracksActionChip(
                    actionRow,
                    BuildFocusedBarracksActionObjectName("Redirect", entry.rosterKey),
                    BuildLockedUnitRedirectActionLabel(entry),
                    new Color(0.36f, 0.24f, 0.10f, 0.98f),
                    true,
                    () => RedirectLockedUnitToUnlockBuilding(lane, entry, site));
                return;
            }

            if (ShouldShowFocusedBarracksBulkBuy(entry))
            {
                CreateFocusedBarracksActionChip(
                    actionRow,
                    BuildFocusedBarracksActionObjectName("BuyTen", entry.rosterKey),
                    BuildFocusedBarracksBulkBuyLabel(lane, site, entry, 10),
                    new Color(0.18f, 0.34f, 0.16f, 0.98f),
                    CanBuyBarracksRosterEntry(lane, site, entry, 10),
                    () => ExecuteFocusedBarracksBuy(site, entry, 10));
            }

            CreateFocusedBarracksActionChip(
                actionRow,
                BuildFocusedBarracksActionObjectName("Buy", entry.rosterKey),
                BuildFocusedBarracksBuyLabel(lane, site, entry),
                new Color(0.34f, 0.26f, 0.08f, 0.98f),
                CanBuyBarracksRosterEntry(lane, site, entry),
                () => ExecuteFocusedBarracksBuy(site, entry));
            CreateFocusedBarracksActionChip(
                actionRow,
                BuildFocusedBarracksActionObjectName("Sell", entry.rosterKey),
                BuildFocusedBarracksSellLabel(site, entry),
                new Color(0.22f, 0.16f, 0.16f, 0.98f),
                CanSellBarracksRosterEntry(site, entry),
                () => ExecuteFocusedBarracksSell(site, entry));
        }

        void CreateFocusedBarracksOwnedStrip(Transform parent, MLBarracksRosterEntry entry)
        {
            if (parent == null || entry == null)
                return;

            var strip = new GameObject("OwnedStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            strip.transform.SetParent(parent, false);

            var layout = strip.GetComponent<LayoutElement>();
            float stripHeight = IsCompactPanelLayout() ? 20f : 26f;
            layout.minHeight = stripHeight;
            layout.preferredHeight = stripHeight;

            var image = strip.GetComponent<Image>();
            image.color = entry.ownedCount > 0
                ? new Color(0.17f, 0.15f, 0.11f, 0.98f)
                : new Color(0.14f, 0.15f, 0.18f, 0.96f);

            var label = CreateInlineText(
                strip.transform,
                "Label",
                $"Owned x{Mathf.Max(0, entry.ownedCount)}",
                IsCompactPanelLayout() ? 10f : 11f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 3f);
            rect.offsetMax = new Vector2(-8f, -3f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        void CreateFocusedBarracksStateStrip(Transform parent, MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (parent == null || site == null || entry == null)
                return;

            var strip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            strip.transform.SetParent(parent, false);

            var layout = strip.GetComponent<LayoutElement>();
            float stripHeight = IsCompactPanelLayout() ? 22f : 28f;
            layout.minHeight = stripHeight;
            layout.preferredHeight = stripHeight;

            var image = strip.GetComponent<Image>();
            image.color = ResolveFocusedBarracksStateStripColor(lane, site, entry);

            var label = CreateInlineText(
                strip.transform,
                "Label",
                BuildFocusedBarracksStateText(lane, site, entry),
                IsCompactPanelLayout() ? 9.5f : 10.5f,
                ResolveFocusedBarracksStateTextColor(lane, site, entry),
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 4f);
            rect.offsetMax = new Vector2(-8f, -4f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        string BuildFocusedBarracksStateText(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Waiting for roster data";

            if (!site.isBuilt)
            {
                if (!string.Equals(entry.unlockBuildingType, "barracks", System.StringComparison.OrdinalIgnoreCase))
                    return $"Locked - {BuildFocusedBarracksUnlockLabel(entry)}";
                return "Manage in Town Core";
            }

            if (!entry.unlocked)
                return $"Locked - {BuildFocusedBarracksUnlockLabel(entry)}";

            string blockedReason = GetBarracksBuyBlockedReason(lane, site, entry);
            string foodLabel = BuildBarracksFoodLabel(site);
            if (!string.IsNullOrWhiteSpace(blockedReason))
                return string.IsNullOrWhiteSpace(foodLabel)
                    ? blockedReason
                    : $"{blockedReason} | {foodLabel}";

            int activeCount = CountActiveUnitsForRosterEntry(lane, site, entry);
            return entry.ownedCount > 0 || activeCount > 0
                ? $"Ready to buy | {foodLabel} | Owned x{Mathf.Max(0, entry.ownedCount)} | Active x{Mathf.Max(0, activeCount)}"
                : string.IsNullOrWhiteSpace(foodLabel)
                    ? "Ready to buy"
                    : $"Ready to buy | {foodLabel}";
        }

        static string BuildFocusedBarracksUnlockLabel(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return "unlock requirement";

            string buildingName = string.IsNullOrWhiteSpace(entry.unlockBuildingName)
                ? string.IsNullOrWhiteSpace(entry.unlockBuildingType)
                    ? null
                    : HumanizeCombatType(entry.unlockBuildingType)
                : entry.unlockBuildingName;
            string tierName = !string.IsNullOrWhiteSpace(entry.unlockBuildingTierName)
                ? entry.unlockBuildingTierName
                : $"Tier {Mathf.Max(1, entry.requiredBuildingTier)}";
            return string.IsNullOrWhiteSpace(buildingName)
                ? "unlock requirement"
                : string.Equals(buildingName, tierName, System.StringComparison.OrdinalIgnoreCase)
                    ? buildingName
                    : $"{buildingName} {tierName}";
        }

        Color ResolveFocusedBarracksStateStripColor(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return GunmetalColor;

            if (!site.isBuilt || !entry.unlocked)
                return GoldSurfaceColor;

            if (!string.IsNullOrWhiteSpace(GetBarracksBuyBlockedReason(lane, site, entry)))
                return GunmetalSoftColor;

            return GoldSurfaceBrightColor;
        }

        Color ResolveFocusedBarracksStateTextColor(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site != null && entry != null && site.isBuilt && entry.unlocked && string.IsNullOrWhiteSpace(GetBarracksBuyBlockedReason(lane, site, entry)))
                return GoldTextColor;

            return SilverTextColor;
        }

        string BuildFocusedBarracksBuyLabel(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Buy";

            if (!site.isBuilt)
                return "Open Town Core";

            if (!entry.unlocked)
                return "Tech Locked";

            string blockedReason = GetBarracksBuyBlockedReason(lane, site, entry);
            if (!string.IsNullOrWhiteSpace(blockedReason))
                return blockedReason;

            return $"Buy {entry.buyCost}g";
        }

        static bool ShouldShowFocusedBarracksBulkBuy(MLBarracksRosterEntry entry)
        {
            return entry != null
                && string.Equals(entry.rosterKey, MilitiaRosterKey, System.StringComparison.OrdinalIgnoreCase);
        }

        string BuildFocusedBarracksBulkBuyLabel(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry, int count)
        {
            count = Mathf.Max(2, count);
            if (site == null || entry == null)
                return $"Buy x{count}";

            if (!site.isBuilt)
                return "Open Town Core";

            if (!entry.unlocked)
                return "Tech Locked";

            string blockedReason = GetBarracksBuyBlockedReason(lane, site, entry, count);
            if (!string.IsNullOrWhiteSpace(blockedReason))
                return blockedReason;

            int totalCost = Mathf.Max(0, entry.buyCost * count);
            return $"Buy x{count} {totalCost}g";
        }

        string BuildFocusedBarracksSellLabel(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Sell";

            if (IsPendingBarracksSell(site, entry))
                return "Selling...";

            if (!site.isBuilt)
                return "Locked";

            return entry.ownedCount > 0
                ? $"Sell 1 for {entry.sellRefund}g"
                : "No Units";
        }

        void ExecuteFocusedBarracksBuy(MLBarracksSite site, MLBarracksRosterEntry entry, int count = 1)
        {
            if (site == null || entry == null)
                return;

            count = Mathf.Max(1, count);
            _statusMessage = count > 1
                ? $"Buying {entry.displayName} x{count} for {ResolveBarracksDisplayName(site)}..."
                : $"Buying {entry.displayName} for {ResolveBarracksDisplayName(site)}...";
            ActionSender.BuyBarracksUnit(entry.rosterKey, site.barracksId, count);
            RefreshHeader(force: true);
        }

        string BuildFocusedMarketEntryBody(MLMarketRosterEntry entry, MLMarketRosterEntry nextEntry, MLFortressPad pad)
        {
            if (entry == null)
                return "No active market income tier is available.";

            string text =
                $"Owned x{Mathf.Max(0, entry.ownedCount)} / {GetMarketFoodLimit(pad)}   Buy {Mathf.Max(0, entry.buyCost)}g each   +{GetMarketIncomePerTick(entry)}g / {GetMarketIncomeSeconds()}s\n" +
                $"Tier Value: +{Mathf.Max(0, entry.economyLapGold)}g per contract each income cycle";

            if (!string.IsNullOrWhiteSpace(entry.description))
                text += $"\n{entry.description}";

            if (nextEntry != null)
            {
                text += pad != null && pad.canUpgrade
                    ? $"\nUpgrade Preview: {nextEntry.displayName} replaces every current contract when the Market reaches Tier {Mathf.Max(1, nextEntry.tier)}."
                    : $"\nNext Income Tier: {nextEntry.displayName} at Market Tier {Mathf.Max(1, nextEntry.tier)}.";
            }

            return text;
        }

        bool CanBuyMarketWorker(MLLaneSnap lane, MLFortressPad pad, MLMarketRosterEntry entry)
        {
            return CanEditBarracks()
                && lane != null
                && pad != null
                && entry != null
                && string.IsNullOrWhiteSpace(GetMarketBuyBlockedReason(lane, pad, entry));
        }

        string BuildFocusedMarketBuyLabel(MLLaneSnap lane, MLFortressPad pad, MLMarketRosterEntry entry)
        {
            if (entry == null)
                return "Buy Trader";

            string blockedReason = GetMarketBuyBlockedReason(lane, pad, entry);
            if (!string.IsNullOrWhiteSpace(blockedReason))
                return blockedReason;

            return $"Buy {entry.displayName} {Mathf.Max(0, entry.buyCost)}g";
        }

        void ExecuteFocusedMarketBuy(MLMarketRosterEntry entry)
        {
            if (entry == null)
                return;

            _statusMessage = $"Buying {entry.displayName} market income...";
            ActionSender.BuyMarketUnit(entry.unitKey);
            RefreshHeader(force: true);
        }

        void ExecuteFocusedBarracksBuild(MLBarracksSite site)
        {
            if (site == null)
                return;

            _pendingBarracksBuildId = NormalizeBarracksId(site.barracksId);
            _statusMessage = $"Buying {ResolveBarracksDisplayName(site)}...";
            ActionSender.BuildBarracksSite(site.barracksId);
            RefreshHeader(force: true);
        }

        void ExecuteFocusedBarracksUpgrade(MLBarracksSite site)
        {
            if (site == null)
                return;

            _statusMessage = $"Upgrading {ResolveBarracksDisplayName(site)}...";
            ActionSender.UpgradeBarracksSite(site.barracksId);
            RefreshHeader(force: true);
        }

        void ExecuteLumberMillRepairAll(MLLaneSnap lane, MLFortressPad pad)
        {
            if (lane == null || pad == null)
                return;

            _statusMessage = $"Lumber Mill repairing {GetRepairAllEligibleCount(lane)} damaged structure{(GetRepairAllEligibleCount(lane) == 1 ? string.Empty : "s")}...";
            ActionSender.RepairAllBuildings(pad.padId);
            RefreshHeader(force: true);
        }

        void ExecuteFocusedBarracksSell(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return;

            string pendingKey = BuildPendingBarracksSellKey(site, entry);
            if (!string.IsNullOrWhiteSpace(pendingKey)
                && string.Equals(_pendingBarracksSellKey, pendingKey, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pendingBarracksSellKey = pendingKey;
            _statusMessage = $"Selling 1 {entry.displayName} from {ResolveBarracksDisplayName(site)}...";
            ActionSender.SellBarracksUnit(entry.rosterKey, site.barracksId);
            RefreshHeader(force: true);
        }

        void CreateFocusedBarracksActionChip(
            Transform parent,
            string text,
            Color activeColor,
            bool interactable,
            UnityEngine.Events.UnityAction action)
        {
            CreateFocusedBarracksActionChip(parent, "ValueChip", text, activeColor, interactable, action);
        }

        void CreateFocusedBarracksActionChip(
            Transform parent,
            string name,
            string text,
            Color activeColor,
            bool interactable,
            UnityEngine.Events.UnityAction action)
        {
            var chip = new GameObject(string.IsNullOrWhiteSpace(name) ? "ValueChip" : name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            chip.transform.SetParent(parent, false);

            var image = chip.GetComponent<Image>();
            image.color = interactable
                ? activeColor
                : new Color(0.20f, 0.22f, 0.26f, 0.92f);

            var button = chip.GetComponent<Button>();
            button.interactable = interactable;
            if (action != null)
                button.onClick.AddListener(action);

            var layout = chip.GetComponent<LayoutElement>();
            float chipHeight = IsCompactPanelLayout() ? 28f : 34f;
            layout.minHeight = chipHeight;
            layout.preferredHeight = chipHeight;
            layout.flexibleWidth = 1f;

            var label = CreateInlineText(
                chip.transform,
                "Label",
                text,
                IsCompactPanelLayout() ? 10f : 11.5f,
                interactable
                    ? new Color(0.98f, 0.96f, 0.91f, 1f)
                    : new Color(0.72f, 0.72f, 0.72f, 0.95f),
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, IsCompactPanelLayout() ? 2f : 4f);
            rect.offsetMax = new Vector2(-6f, IsCompactPanelLayout() ? -2f : -4f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = IsCompactPanelLayout() ? 12f : 13f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        void CreateStatTile(Transform parent, string label, string value)
        {
            var tile = new GameObject("StatTile", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            tile.transform.SetParent(parent, false);
            tile.GetComponent<Image>().color = new Color(0.06f, 0.10f, 0.16f, 0.96f);

            var layout = tile.GetComponent<VerticalLayoutGroup>();
            layout.padding = IsCompactPanelLayout()
                ? new RectOffset(5, 5, 4, 4)
                : new RectOffset(6, 6, 5, 5);
            layout.spacing = 1f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            CreateInlineText(
                tile.transform,
                "Label",
                label,
                IsCompactPanelLayout() ? 8f : 9f,
                new Color(0.76f, 0.84f, 0.93f, 0.95f),
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            CreateInlineText(
                tile.transform,
                "Value",
                value,
                IsCompactPanelLayout() ? 10f : 11f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
        }

        void CreateSectionHeader(string text)
        {
            var go = new GameObject("SectionHeader", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(_contentRoot, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = IsCompactPanelLayout() ? (IsTightPanelLayout() ? 17f : 18f) : 24f;
            tmp.color = new Color(0.95f, 0.90f, 0.62f, 1f);
            ClassicRpgUiRuntime.ApplyTextStyle(
                tmp,
                ClassicRpgTextStyle.SectionHeader,
                TextAlignmentOptions.Left,
                new Color(0.96f, 0.89f, 0.58f, 1f),
                allowWrap: false);
            tmp.text = text;
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = GetSectionHeaderHeight();
            element.preferredHeight = GetSectionHeaderHeight();
        }

        void CreateInfoCard(string text)
        {
            var card = CreateCardContainer();
            CreateBodyText(card, text);
        }

        void CreateCardTitle(Transform parent, string text)
        {
            var go = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = IsCompactPanelLayout() ? 16f : 20f;
            tmp.color = Color.white;
            ClassicRpgUiRuntime.ApplyTextStyle(
                tmp,
                ClassicRpgTextStyle.SectionHeader,
                TextAlignmentOptions.Left,
                Color.white,
                allowWrap: false);
            tmp.text = text;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
        }

        void CreateBodyText(Transform parent, string text)
        {
            var go = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = IsCompactPanelLayout() ? 13f : 16f;
            tmp.color = new Color(0.82f, 0.87f, 0.93f, 0.95f);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            ClassicRpgUiRuntime.ApplyTextStyle(
                tmp,
                IsCompactPanelLayout() ? ClassicRpgTextStyle.SmallBody : ClassicRpgTextStyle.Body,
                TextAlignmentOptions.Left,
                new Color(0.84f, 0.89f, 0.95f, 0.96f));
            tmp.text = text;
        }

        bool TryGetUnitCardStats(MLBarracksRosterEntry entry, out UnitCatalogEntry unit)
        {
            unit = null;
            if (entry == null)
                return false;

            return TryGetUnitCardStats(
                $"{entry.rosterKey}:{entry.archetypeKey}:{entry.presentationKey}:{entry.skinKey}:{entry.unitTypeKey}",
                ResolveCatalogUnitKey(entry.archetypeKey, entry.catalogUnitKey, entry.unitTypeKey, entry.skinKey),
                out unit);
        }

        bool TryGetHeroCardStats(MLHeroRosterEntry hero, out UnitCatalogEntry unit)
        {
            unit = null;
            if (hero == null)
                return false;

            return TryGetUnitCardStats(
                $"{hero.heroKey}:{hero.archetypeKey}:{hero.presentationKey}:{hero.skinKey}:{hero.unitTypeKey}",
                ResolveCatalogUnitKey(hero.archetypeKey, hero.catalogUnitKey, hero.unitTypeKey, hero.skinKey),
                out unit);
        }

        bool TryGetUnitCardStats(string logKey, string unitTypeKey, out UnitCatalogEntry unit)
        {
            unit = null;
            if (string.IsNullOrWhiteSpace(unitTypeKey))
                return false;

            if (CatalogLoader.UnitByKey.TryGetValue(unitTypeKey.Trim(), out unit) && unit != null)
                return true;

            if (_missingUnitCardStatLogs.Add(logKey))
                Debug.LogWarning($"[BarracksPanel] Missing unit card stats for '{logKey}'.");

            return false;
        }

        static string HumanizeCombatType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            string normalized = value.Trim().Replace("_", " ").ToLowerInvariant();
            if (normalized.Length <= 1)
                return normalized.ToUpperInvariant();

            return char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
        }

        static string FormatStatNumber(float value)
        {
            float rounded = Mathf.Round(value * 10f) / 10f;
            return Mathf.Approximately(rounded, Mathf.Round(rounded))
                ? Mathf.RoundToInt(rounded).ToString()
                : rounded.ToString("0.0");
        }

        void LogMissingPortrait(MLBarracksRosterEntry entry, string reason)
        {
            if (entry == null)
                return;

            string key = $"{entry.rosterKey}:{entry.archetypeKey}:{entry.presentationKey}:{entry.skinKey}:{entry.unitTypeKey}";
            LogMissingPortrait(key, reason);
        }

        static string ResolveCatalogUnitKey(string archetypeKey, string catalogUnitKey, string unitTypeKey, string skinKey)
        {
            if (!string.IsNullOrWhiteSpace(catalogUnitKey))
                return catalogUnitKey.Trim();

            return FortUnitIdentityCatalog.ResolveCatalogUnitKey(archetypeKey, unitTypeKey, skinKey);
        }

        void LogMissingPortrait(string key, string reason)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!_missingPortraitLogs.Add(key))
                return;

            Debug.LogWarning($"[BarracksPanel] Missing portrait for '{key}'. {reason}");
        }

        Button CreateActionButton(
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction action,
            bool interactable,
            float minWidth = 110f,
            bool highlighted = false)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = interactable
                ? highlighted
                    ? GoldSurfaceBrightColor
                    : GunmetalColor
                : DisabledSurfaceColor;

            var button = go.GetComponent<Button>();
            button.interactable = interactable;
            if (action != null) button.onClick.AddListener(action);

            var element = go.GetComponent<LayoutElement>();
            element.minWidth = IsCompactPanelLayout() ? Mathf.Max(92f, minWidth - 14f) : minWidth;
            element.preferredHeight = IsCompactPanelLayout() ? 32f : 34f;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = IsCompactPanelLayout() ? 13f : 15f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = interactable
                ? highlighted
                    ? new Color(0.12f, 0.09f, 0.04f, 1f)
                    : SilverTextColor
                : MutedSilverTextColor;
            tmp.text = label;
            ClassicRpgUiRuntime.ApplyButton(
                button,
                !interactable
                    ? ClassicRpgButtonSkin.MiniBrown
                    : highlighted
                        ? ClassicRpgButtonSkin.MiniGold
                        : ClassicRpgButtonSkin.MiniBrown,
                tmp,
                label);
            return button;
        }

        static IEnumerator ScaleIn(Transform t, float dur)
        {
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                t.localScale = Vector3.one * EaseOutBack(n);
                yield return null;
            }

            t.localScale = Vector3.one;
        }

        IEnumerator ScaleOut(Transform t, float dur)
        {
            Vector3 start = t.localScale;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                t.localScale = Vector3.Lerp(start, Vector3.zero, n * n);
                yield return null;
            }

            t.localScale = Vector3.zero;
            PanelBarracks.SetActive(false);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float tm1 = t - 1f;
            return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        }
    }

    sealed class NestedHorizontalScrollRect : ScrollRect
    {
        ScrollRect _parentScrollRect;
        bool _routeToParent;

        public override void OnInitializePotentialDrag(PointerEventData eventData)
        {
            CacheParentScrollRect();
            base.OnInitializePotentialDrag(eventData);
            _parentScrollRect?.OnInitializePotentialDrag(eventData);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            CacheParentScrollRect();
            _routeToParent = _parentScrollRect != null && ShouldRouteToParent(eventData);
            if (_routeToParent)
            {
                _parentScrollRect.OnBeginDrag(eventData);
                return;
            }

            base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (_routeToParent)
            {
                _parentScrollRect?.OnDrag(eventData);
                return;
            }

            base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (_routeToParent)
                _parentScrollRect?.OnEndDrag(eventData);
            else
                base.OnEndDrag(eventData);

            _routeToParent = false;
        }

        public override void OnScroll(PointerEventData eventData)
        {
            CacheParentScrollRect();
            if (!CanScrollHorizontally() && _parentScrollRect != null)
            {
                _parentScrollRect.OnScroll(eventData);
                return;
            }

            if (eventData == null)
                return;

            Vector2 scrollDelta = eventData.scrollDelta;
            if (Mathf.Abs(scrollDelta.x) < 0.01f && Mathf.Abs(scrollDelta.y) > 0.01f)
                scrollDelta = new Vector2(-scrollDelta.y, 0f);

            var owningEventSystem = EventSystem.current;
            if (owningEventSystem == null)
            {
                base.OnScroll(eventData);
                return;
            }

            var translated = new PointerEventData(owningEventSystem)
            {
                pointerId = eventData.pointerId,
                position = eventData.position,
                delta = eventData.delta,
                scrollDelta = scrollDelta,
                button = eventData.button,
            };
            base.OnScroll(translated);
        }

        void CacheParentScrollRect()
        {
            if (_parentScrollRect != null)
                return;

            var current = transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent<ScrollRect>(out var scrollRect) && scrollRect != this)
                {
                    _parentScrollRect = scrollRect;
                    return;
                }

                current = current.parent;
            }
        }

        bool ShouldRouteToParent(PointerEventData eventData)
        {
            if (!CanScrollHorizontally())
                return true;

            Vector2 delta = eventData.position - eventData.pressPosition;
            return Mathf.Abs(delta.y) > Mathf.Abs(delta.x);
        }

        bool CanScrollHorizontally()
        {
            return content != null
                && viewport != null
                && content.rect.width > viewport.rect.width + 0.5f;
        }
    }
}
