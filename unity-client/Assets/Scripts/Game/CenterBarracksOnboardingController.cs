using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class CenterBarracksOnboardingController : MonoBehaviour
    {
        const string CenterBarracksId = "center";
        const string MilitiaRosterKey = "militia";
        const string TownCorePadId = "town_core_pad";
        const int MilitiaTargetCount = 5;
        const string PromptRootName = "CenterBarracksOnboardingPrompt";
        const string ArrowRootName = "CenterBarracksOnboardingArrow";
        const string UiArrowRootName = "CenterBarracksOnboardingUiArrow";
        const string TooltipButtonRootName = "CenterBarracksOnboardingTooltipButton";
        const float PromptMinWidth = 420f;
        const float PromptMaxWidth = 620f;
        const float PromptRegularHeight = 160f;
        const float PromptCompactHeight = 176f;

        enum CenterBarracksStage
        {
            None = 0,
            Purchase = 1,
            Constructing = 2,
            OpenBarracks = 3,
            BuyMilitia = 4,
            UpgradeTownCoreTier2 = 5,
            WaitForTownCoreTier2 = 6,
            BuyMarket = 7,
            WaitForMarket = 8,
            BuyTraders = 9,
        }

        Canvas _mainCanvas;
        RectTransform _promptRoot;
        TMP_Text _headlineLabel;
        TMP_Text _detailLabel;
        Button _focusButton;
        TMP_Text _focusButtonLabel;
        Button _promptHideButton;
        TMP_Text _promptHideButtonLabel;
        Toggle _promptTooltipToggle;
        RectTransform _tooltipButtonRoot;
        Button _tooltipButton;
        TMP_Text _tooltipButtonLabel;

        Transform _worldArrowRoot;
        RectTransform _worldArrowRect;
        TMP_Text _worldArrowGlowLabel;
        TMP_Text _worldArrowCoreLabel;
        TMP_Text _worldArrowCaptionLabel;

        RectTransform _uiArrowRoot;
        TMP_Text _uiArrowGlowLabel;
        TMP_Text _uiArrowCoreLabel;
        TMP_Text _uiArrowCaptionLabel;

        CenterBarracksStage _initialStage;
        CenterBarracksStage _stage;
        bool _flowResolved;
        bool _flowCompleted;
        bool _snapshotSubscribed;
        bool _matchReadySubscribed;
        bool _actionAppliedSubscribed;
        bool _tooltipExpanded = true;
        int _laneIndex = -1;
        string _laneSlotColor;
        BarracksSiteView _centerBarracksView;
        BarracksPanel _barracksPanel;
        CenterBarracksStage _lastVisibleStage;

        void OnEnable()
        {
            EnsureSubscriptions();
            EnsureUi();
            RefreshOnboardingState();
        }

        void OnDisable()
        {
            Unsubscribe();
            SetPromptVisible(false);
            SetTooltipButtonVisible(false);
            SetWorldArrowVisible(false);
            SetUiArrowVisible(false);
        }

        void Update()
        {
            EnsureSubscriptions();
            RefreshOnboardingState();

            if (!_flowResolved || _flowCompleted || _stage == CenterBarracksStage.None)
                return;

            if (!UserPreferencesManager.ShowTooltips)
            {
                SetPromptVisible(false);
                SetTooltipButtonVisible(false);
                HideIndicators();
                return;
            }

            EnsureUi();
            SetPromptVisible(_tooltipExpanded);
            UpdateIndicators();
            UpdateTooltipButtonVisual();
        }

        void EnsureSubscriptions()
        {
            if (!_snapshotSubscribed)
            {
                var snapshotApplier = SnapshotApplier.Instance;
                if (snapshotApplier != null)
                {
                    snapshotApplier.OnMLSnapshotApplied += HandleSnapshotApplied;
                    _snapshotSubscribed = true;
                }
            }

            if (!_matchReadySubscribed)
            {
                var networkManager = NetworkManager.Instance;
                if (networkManager != null)
                {
                    networkManager.OnMLMatchReady += HandleMatchReady;
                    _matchReadySubscribed = true;
                }
            }

            if (!_actionAppliedSubscribed)
            {
                var networkManager = NetworkManager.Instance;
                if (networkManager != null)
                {
                    networkManager.OnActionApplied += HandleActionApplied;
                    _actionAppliedSubscribed = true;
                }
            }
        }

        void Unsubscribe()
        {
            if (_snapshotSubscribed)
            {
                var snapshotApplier = SnapshotApplier.Instance;
                if (snapshotApplier != null)
                    snapshotApplier.OnMLSnapshotApplied -= HandleSnapshotApplied;
                _snapshotSubscribed = false;
            }

            if (_matchReadySubscribed)
            {
                var networkManager = NetworkManager.Instance;
                if (networkManager != null)
                    networkManager.OnMLMatchReady -= HandleMatchReady;
                _matchReadySubscribed = false;
            }

            if (_actionAppliedSubscribed)
            {
                var networkManager = NetworkManager.Instance;
                if (networkManager != null)
                    networkManager.OnActionApplied -= HandleActionApplied;
                _actionAppliedSubscribed = false;
            }
        }

        void HandleMatchReady(MLMatchReadyPayload _)
        {
            ResetFlow();
            RefreshOnboardingState();
        }

        void HandleSnapshotApplied(MLSnapshot _)
        {
            RefreshOnboardingState();
        }

        void HandleActionApplied(ActionAppliedPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.type))
                return;

            if ((payload.type == "buy_barracks_unit" && _stage == CenterBarracksStage.BuyMilitia)
                || (payload.type == "buy_market_unit" && _stage == CenterBarracksStage.BuyTraders)
                || payload.type == "build_barracks_site"
                || payload.type == "upgrade_building"
                || payload.type == "build_on_pad")
                RefreshOnboardingState();
        }

        void ResetFlow()
        {
            _initialStage = CenterBarracksStage.None;
            _stage = CenterBarracksStage.None;
            _flowResolved = false;
            _flowCompleted = false;
            _laneIndex = -1;
            _laneSlotColor = null;
            _centerBarracksView = null;
            _tooltipExpanded = true;
            _lastVisibleStage = CenterBarracksStage.None;
            SetPromptVisible(false);
            SetTooltipButtonVisible(false);
            SetWorldArrowVisible(false);
            SetUiArrowVisible(false);
        }

        void RefreshOnboardingState()
        {
            var snapshotApplier = SnapshotApplier.Instance;
            var lane = snapshotApplier?.MyLane;
            if (lane == null)
            {
                SetPromptVisible(false);
                HideIndicators();
                return;
            }

            var site = snapshotApplier.GetBarracksSite(lane.laneIndex, CenterBarracksId);
            if (site == null)
            {
                SetPromptVisible(false);
                HideIndicators();
                return;
            }

            _laneIndex = lane.laneIndex;
            _laneSlotColor = lane.slotColor;

            if (_flowCompleted)
            {
                SetPromptVisible(false);
                HideIndicators();
                return;
            }

            if (!_flowResolved)
            {
                _initialStage = ResolveInitialStage(lane, site);
                _stage = _initialStage;
                _flowResolved = true;
            }
            else
            {
                _stage = ResolveCurrentStage(lane, site);
            }

            if (_stage != _lastVisibleStage)
            {
                _lastVisibleStage = _stage;
                _tooltipExpanded = true;
            }

            if (_stage == CenterBarracksStage.None)
            {
                CompleteOnboarding();
                return;
            }

            _centerBarracksView = BarracksSiteView.FindSite(CenterBarracksId, lane.slotColor, lane.laneIndex) ?? _centerBarracksView;

            EnsureUi();
            UpdatePrompt(lane, site);
            if (!UserPreferencesManager.ShowTooltips)
            {
                SetPromptVisible(false);
                SetTooltipButtonVisible(false);
                HideIndicators();
                return;
            }

            SetPromptVisible(_tooltipExpanded);
            UpdateIndicators();
            UpdateTooltipButtonVisual();
        }

        void CompleteOnboarding()
        {
            _stage = CenterBarracksStage.None;
            _flowCompleted = true;
            _centerBarracksView = null;
            _tooltipExpanded = true;
            _lastVisibleStage = CenterBarracksStage.None;
            SetPromptVisible(false);
            SetTooltipButtonVisible(false);
            HideIndicators();
        }

        CenterBarracksStage ResolveInitialStage(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return CenterBarracksStage.None;

            if (IsBarracksConstructing(site))
                return CenterBarracksStage.Constructing;

            if (!site.isBuilt)
                return CenterBarracksStage.Purchase;

            return ResolvePostBuildStage(lane, site);
        }

        CenterBarracksStage ResolveCurrentStage(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return CenterBarracksStage.None;

            if (IsBarracksConstructing(site))
                return CenterBarracksStage.Constructing;

            if (!site.isBuilt)
                return CenterBarracksStage.Purchase;

            return ResolvePostBuildStage(lane, site);
        }

        CenterBarracksStage ResolvePostBuildStage(MLLaneSnap lane, MLBarracksSite site)
        {
            if (GetOwnedCount(site, MilitiaRosterKey) < MilitiaTargetCount)
            {
                if (!CanShowMilitiaPurchaseStep())
                    return CenterBarracksStage.OpenBarracks;

                return CenterBarracksStage.BuyMilitia;
            }

            var townCorePad = FindFortressPadByBuildingType(lane, "town_core");
            if (townCorePad != null && Mathf.Max(0, townCorePad.tier) < 2)
            {
                if (IsPadConstructingTowardTier(townCorePad, 2))
                    return CenterBarracksStage.WaitForTownCoreTier2;
                return CenterBarracksStage.UpgradeTownCoreTier2;
            }

            var marketPad = FindFortressPadByBuildingType(lane, "market");
            if (marketPad != null && !marketPad.isBuilt)
            {
                if (IsPadConstructing(marketPad))
                    return CenterBarracksStage.WaitForMarket;
                return CenterBarracksStage.BuyMarket;
            }

            var currentMarketEntry = GetCurrentMarketRosterEntry(lane);
            if (currentMarketEntry == null)
                return CenterBarracksStage.BuyMarket;

            return Mathf.Max(0, currentMarketEntry.ownedCount) > 0
                ? CenterBarracksStage.None
                : CenterBarracksStage.BuyTraders;
        }

        static MLFortressPad FindFortressPadByBuildingType(MLLaneSnap lane, string buildingType)
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

        static MLMarketRosterEntry GetCurrentMarketRosterEntry(MLLaneSnap lane)
        {
            if (lane?.marketRoster == null)
                return null;

            MLMarketRosterEntry unlockedFallback = null;
            for (int i = 0; i < lane.marketRoster.Length; i++)
            {
                var entry = lane.marketRoster[i];
                if (entry == null)
                    continue;
                if (entry.currentTier)
                    return entry;
                if (entry.unlocked && unlockedFallback == null)
                    unlockedFallback = entry;
            }

            return unlockedFallback;
        }

        bool CanShowMilitiaPurchaseStep()
        {
            var panel = ResolveBarracksPanel();
            if (panel == null)
                return false;

            if (panel.GetFocusedBarracksUnitBuyButton(MilitiaRosterKey) != null)
                return true;

            if (panel.GetFocusedBarracksUnitCard(MilitiaRosterKey) != null)
                return true;

            return panel.IsShowingFocusedBarracks(CenterBarracksId);
        }

        static int GetOwnedCount(MLBarracksSite site, string rosterKey)
        {
            var entry = FindRosterEntry(site, rosterKey);
            return entry != null ? Mathf.Max(0, entry.ownedCount) : 0;
        }

        static MLBarracksRosterEntry FindRosterEntry(MLBarracksSite site, string rosterKey)
        {
            if (site?.roster == null || string.IsNullOrWhiteSpace(rosterKey))
                return null;

            for (int i = 0; i < site.roster.Length; i++)
            {
                var entry = site.roster[i];
                if (entry != null && string.Equals(entry.rosterKey, rosterKey, System.StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        static bool IsBarracksConstructing(MLBarracksSite site)
        {
            return site != null
                && (site.isConstructing
                    || string.Equals(site.buildState, "constructing", System.StringComparison.OrdinalIgnoreCase));
        }

        static bool IsPadConstructing(MLFortressPad pad)
        {
            return pad != null
                && (pad.isConstructing
                    || string.Equals(pad.buildState, "constructing", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pad.buildState, "upgrading", System.StringComparison.OrdinalIgnoreCase));
        }

        static bool IsPadConstructingTowardTier(MLFortressPad pad, int targetTier)
        {
            return pad != null
                && IsPadConstructing(pad)
                && Mathf.Max(Mathf.Max(0, pad.tier), Mathf.Max(0, pad.constructionTargetTier)) >= Mathf.Max(1, targetTier);
        }

        static int GetConstructionSecondsRemaining(MLBarracksSite site)
        {
            if (site == null)
                return -1;

            return Mathf.CeilToInt(site.constructionTimerTicksRemaining / Mathf.Max(1f, SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20));
        }

        static int GetConstructionSecondsRemaining(MLFortressPad pad)
        {
            if (pad == null)
                return -1;

            return Mathf.CeilToInt(pad.constructionTimerTicksRemaining / Mathf.Max(1f, SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetTickHz() : 20));
        }

        static string BuildConstructionTimerLabel(string constructionKind, int seconds)
        {
            string verb = string.Equals(constructionKind, "upgrade", System.StringComparison.OrdinalIgnoreCase)
                ? "Upgrading"
                : "Building";
            return $"{verb} {Mathf.Max(0, seconds)}s";
        }

        void HideIndicators()
        {
            SetWorldArrowVisible(false);
            SetUiArrowVisible(false);
        }

        void EnsureUi()
        {
            EnsureWorldArrowRoot();

            _mainCanvas = ResolveMainCanvas();
            if (_mainCanvas == null)
                return;

            EnsurePromptRoot();
            EnsureTooltipButtonRoot();
            EnsureUiArrowRoot();
        }

        Canvas ResolveMainCanvas()
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
                    continue;

                if (canvas.name == "Canvas")
                    return canvas;
            }

            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                    return canvas;
            }

            return FindFirstObjectByType<Canvas>();
        }

        void EnsurePromptRoot()
        {
            if (_mainCanvas == null)
                return;

            if (_promptRoot == null)
            {
                Transform existing = _mainCanvas.transform.Find(PromptRootName);
                if (existing != null)
                {
                    _promptRoot = existing as RectTransform;
                }
                else
                {
                    var root = new GameObject(PromptRootName, typeof(RectTransform), typeof(Image), typeof(Outline));
                    root.transform.SetParent(_mainCanvas.transform, false);
                    _promptRoot = root.GetComponent<RectTransform>();
                }
            }
            else if (_promptRoot.parent != _mainCanvas.transform)
            {
                _promptRoot.SetParent(_mainCanvas.transform, false);
            }

            if (_promptRoot == null)
                return;

            RectTransform canvasRect = _mainCanvas.transform as RectTransform;
            float canvasWidth = canvasRect != null ? canvasRect.rect.width : 1920f;
            float canvasHeight = canvasRect != null ? canvasRect.rect.height : 1080f;
            bool compactLayout = canvasWidth < 1200f || canvasHeight < 760f;

            _promptRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _promptRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _promptRoot.pivot = new Vector2(0.5f, 0.5f);
            _promptRoot.anchoredPosition = new Vector2(0f, Mathf.Clamp(canvasHeight * 0.16f, 64f, 168f));
            _promptRoot.sizeDelta = new Vector2(
                Mathf.Clamp(canvasWidth * (compactLayout ? 0.74f : 0.54f), PromptMinWidth, PromptMaxWidth),
                compactLayout ? PromptCompactHeight : PromptRegularHeight);

            var background = _promptRoot.GetComponent<Image>();
            background.color = new Color(0.04f, 0.08f, 0.13f, 0.92f);
            background.raycastTarget = false;

            var outline = _promptRoot.GetComponent<Outline>();
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.effectColor = new Color(0.32f, 0.88f, 1f, 0.70f);
            outline.useGraphicAlpha = true;

            _headlineLabel = EnsurePromptText(
                _headlineLabel,
                "Headline",
                new Vector2(0.06f, 0.68f),
                new Vector2(compactLayout ? 0.70f : 0.72f, 0.90f),
                compactLayout ? 24f : 28f,
                FontStyles.Bold,
                new Color(0.97f, 0.98f, 1f, 1f),
                TextAlignmentOptions.Left);
            _headlineLabel.enableAutoSizing = true;
            _headlineLabel.fontSizeMin = 20f;
            _headlineLabel.fontSizeMax = compactLayout ? 24f : 28f;
            _headlineLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _headlineLabel.overflowMode = TextOverflowModes.Ellipsis;

            _detailLabel = EnsurePromptText(
                _detailLabel,
                "Detail",
                new Vector2(0.06f, 0.36f),
                new Vector2(0.94f, 0.64f),
                compactLayout ? 15f : 16f,
                FontStyles.Normal,
                new Color(0.78f, 0.88f, 0.96f, 1f),
                TextAlignmentOptions.Left);
            _detailLabel.enableAutoSizing = true;
            _detailLabel.fontSizeMin = 13f;
            _detailLabel.fontSizeMax = compactLayout ? 15f : 16f;
            _detailLabel.textWrappingMode = TextWrappingModes.Normal;
            _detailLabel.overflowMode = TextOverflowModes.Ellipsis;

            _focusButton = EnsurePromptButton(
                _focusButton,
                "FocusButton",
                new Vector2(compactLayout ? 0.18f : 0.29f, 0.08f),
                new Vector2(compactLayout ? 0.82f : 0.71f, 0.32f),
                out _focusButtonLabel);
            _focusButton.onClick.RemoveListener(HandleFocusButtonPressed);
            _focusButton.onClick.AddListener(HandleFocusButtonPressed);
            _focusButtonLabel.enableAutoSizing = true;
            _focusButtonLabel.fontSizeMin = 16f;
            _focusButtonLabel.fontSizeMax = compactLayout ? 20f : 22f;

            _promptHideButton = EnsurePromptButton(
                _promptHideButton,
                "HideButton",
                new Vector2(compactLayout ? 0.74f : 0.78f, 0.08f),
                new Vector2(0.94f, 0.26f),
                out _promptHideButtonLabel);
            _promptHideButton.onClick.RemoveListener(HandlePromptHidePressed);
            _promptHideButton.onClick.AddListener(HandlePromptHidePressed);
            if (_promptHideButtonLabel != null)
            {
                _promptHideButtonLabel.enableAutoSizing = true;
                _promptHideButtonLabel.fontSizeMin = 12f;
                _promptHideButtonLabel.fontSizeMax = compactLayout ? 15f : 16f;
                _promptHideButtonLabel.text = "HIDE";
            }

            var hideButtonImage = _promptHideButton != null ? _promptHideButton.GetComponent<Image>() : null;
            if (hideButtonImage != null)
                hideButtonImage.color = new Color(0.10f, 0.24f, 0.34f, 0.98f);

            _promptTooltipToggle = EnsurePromptTooltipToggle(
                _promptTooltipToggle,
                "TooltipToggle",
                new Vector2(compactLayout ? 0.58f : 0.62f, 0.74f),
                new Vector2(0.94f, 0.92f),
                "Display tooltips");
            if (_promptTooltipToggle != null)
            {
                _promptTooltipToggle.onValueChanged.RemoveListener(HandleTooltipPreferenceChanged);
                _promptTooltipToggle.onValueChanged.AddListener(HandleTooltipPreferenceChanged);
                _promptTooltipToggle.SetIsOnWithoutNotify(UserPreferencesManager.ShowTooltips);
            }
        }

        TMP_Text EnsurePromptText(
            TMP_Text label,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment)
        {
            if (_promptRoot == null)
                return label;

            Transform existing = label != null ? label.transform : _promptRoot.Find(name);
            TextMeshProUGUI tmp;
            if (existing != null)
            {
                tmp = existing.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(_promptRoot, false);
                tmp = go.GetComponent<TextMeshProUGUI>();
            }

            var rect = tmp.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        Button EnsurePromptButton(
            Button button,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            out TMP_Text label)
        {
            if (_promptRoot == null)
            {
                label = null;
                return button;
            }

            Transform existing = button != null ? button.transform : _promptRoot.Find(name);
            if (existing == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_promptRoot, false);
                button = go.GetComponent<Button>();
                existing = go.transform;
            }
            else
            {
                button = existing.GetComponent<Button>();
            }

            var rect = existing as RectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = button.GetComponent<Image>();
            image.color = new Color(0.09f, 0.38f, 0.19f, 0.98f);
            image.raycastTarget = true;

            var buttonOutline = button.GetComponent<Outline>();
            if (buttonOutline == null)
                buttonOutline = button.gameObject.AddComponent<Outline>();
            buttonOutline.effectDistance = new Vector2(1.5f, -1.5f);
            buttonOutline.effectColor = new Color(0.73f, 1f, 0.79f, 0.34f);
            buttonOutline.useGraphicAlpha = true;

            Transform labelTransform = existing.Find("Label");
            if (labelTransform == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(existing, false);
                labelTransform = labelGo.transform;
            }

            label = labelTransform.GetComponent<TextMeshProUGUI>();
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 6f);
            labelRect.offsetMax = new Vector2(-8f, -6f);
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.96f, 0.99f, 0.98f, 1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            var labelOutline = label.GetComponent<Outline>();
            if (labelOutline == null)
                labelOutline = label.gameObject.AddComponent<Outline>();
            labelOutline.effectDistance = new Vector2(0.9f, -0.9f);
            labelOutline.effectColor = new Color(0.03f, 0.12f, 0.06f, 0.96f);
            labelOutline.useGraphicAlpha = true;
            return button;
        }

        Toggle EnsurePromptTooltipToggle(
            Toggle toggle,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            string labelText)
        {
            if (_promptRoot == null)
                return toggle;

            Transform existing = toggle != null ? toggle.transform : _promptRoot.Find(name);
            if (existing == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
                go.transform.SetParent(_promptRoot, false);
                toggle = go.GetComponent<Toggle>();
                existing = go.transform;
            }
            else
            {
                toggle = existing.GetComponent<Toggle>();
            }

            var rect = existing as RectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Transform labelTransform = existing.Find("Label");
            if (labelTransform == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(existing, false);
                labelTransform = labelGo.transform;
            }

            var label = labelTransform.GetComponent<TextMeshProUGUI>();
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(0f, 0f);
            labelRect.offsetMax = new Vector2(-34f, 0f);
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Right;
            label.color = new Color(0.96f, 0.97f, 0.90f, 0.98f);
            label.text = labelText;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            Transform checkboxTransform = existing.Find("Checkbox");
            if (checkboxTransform == null)
            {
                var checkboxGo = new GameObject("Checkbox", typeof(RectTransform), typeof(Image));
                checkboxGo.transform.SetParent(existing, false);
                checkboxTransform = checkboxGo.transform;
            }

            var checkboxRect = checkboxTransform as RectTransform;
            checkboxRect.anchorMin = new Vector2(1f, 0.5f);
            checkboxRect.anchorMax = new Vector2(1f, 0.5f);
            checkboxRect.pivot = new Vector2(1f, 0.5f);
            checkboxRect.sizeDelta = new Vector2(24f, 24f);
            checkboxRect.anchoredPosition = Vector2.zero;

            var checkboxImage = checkboxTransform.GetComponent<Image>();
            checkboxImage.color = new Color(0.15f, 0.22f, 0.28f, 1f);

            Transform checkmarkTransform = checkboxTransform.Find("Checkmark");
            if (checkmarkTransform == null)
            {
                var checkmarkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
                checkmarkGo.transform.SetParent(checkboxTransform, false);
                checkmarkTransform = checkmarkGo.transform;
            }

            var checkmarkRect = checkmarkTransform as RectTransform;
            checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
            checkmarkRect.sizeDelta = new Vector2(14f, 14f);
            checkmarkRect.anchoredPosition = Vector2.zero;

            var checkmarkImage = checkmarkTransform.GetComponent<Image>();
            checkmarkImage.color = new Color(0.96f, 0.84f, 0.44f, 1f);

            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = checkboxImage;
            toggle.graphic = checkmarkImage;
            toggle.isOn = UserPreferencesManager.ShowTooltips;
            return toggle;
        }

        void EnsureTooltipButtonRoot()
        {
            if (_mainCanvas == null)
                return;

            if (_tooltipButtonRoot == null)
            {
                Transform existing = _mainCanvas.transform.Find(TooltipButtonRootName);
                if (existing != null)
                {
                    _tooltipButtonRoot = existing as RectTransform;
                }
                else
                {
                    var root = new GameObject(TooltipButtonRootName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
                    root.transform.SetParent(_mainCanvas.transform, false);
                    _tooltipButtonRoot = root.GetComponent<RectTransform>();
                }
            }
            else if (_tooltipButtonRoot.parent != _mainCanvas.transform)
            {
                _tooltipButtonRoot.SetParent(_mainCanvas.transform, false);
            }

            if (_tooltipButtonRoot == null)
                return;

            _tooltipButtonRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _tooltipButtonRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltipButtonRoot.pivot = new Vector2(0.5f, 0.5f);
            _tooltipButtonRoot.sizeDelta = new Vector2(84f, 34f);
            _tooltipButtonRoot.SetAsLastSibling();

            _tooltipButton = _tooltipButtonRoot.GetComponent<Button>();
            var image = _tooltipButtonRoot.GetComponent<Image>();
            image.raycastTarget = true;

            var outline = _tooltipButtonRoot.GetComponent<Outline>();
            outline.effectDistance = new Vector2(1.4f, -1.4f);
            outline.effectColor = new Color(0.34f, 0.82f, 1f, 0.50f);
            outline.useGraphicAlpha = true;

            _tooltipButton.onClick.RemoveListener(HandleTooltipButtonPressed);
            _tooltipButton.onClick.AddListener(HandleTooltipButtonPressed);

            Transform labelTransform = _tooltipButtonRoot.Find("Label");
            if (labelTransform == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(_tooltipButtonRoot, false);
                labelTransform = labelGo.transform;
            }

            _tooltipButtonLabel = labelTransform.GetComponent<TextMeshProUGUI>();
            var labelRect = _tooltipButtonLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 4f);
            labelRect.offsetMax = new Vector2(-6f, -4f);
            _tooltipButtonLabel.font = TMP_Settings.defaultFontAsset;
            _tooltipButtonLabel.fontSize = 14f;
            _tooltipButtonLabel.fontStyle = FontStyles.Bold;
            _tooltipButtonLabel.alignment = TextAlignmentOptions.Center;
            _tooltipButtonLabel.color = new Color(0.96f, 0.99f, 1f, 1f);
            _tooltipButtonLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _tooltipButtonLabel.overflowMode = TextOverflowModes.Ellipsis;
            _tooltipButtonLabel.raycastTarget = false;

            UpdateTooltipButtonStateVisual();
            SetTooltipButtonVisible(false);
        }

        void EnsureWorldArrowRoot()
        {
            if (_worldArrowRoot == null)
            {
                Transform existing = transform.Find(ArrowRootName);
                if (existing != null)
                {
                    _worldArrowRoot = existing;
                }
                else
                {
                    var root = new GameObject(ArrowRootName, typeof(RectTransform), typeof(Canvas));
                    root.transform.SetParent(transform, false);
                    _worldArrowRoot = root.transform;
                }
            }

            if (_worldArrowRoot == null)
                return;

            var canvas = _worldArrowRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 320;

            _worldArrowRect = _worldArrowRoot as RectTransform;
            _worldArrowRect.sizeDelta = new Vector2(300f, 380f);

            _worldArrowGlowLabel = EnsureArrowText(
                _worldArrowGlowLabel,
                _worldArrowRoot,
                "Glow",
                new Vector2(0f, 0.16f),
                new Vector2(1f, 1f),
                208f,
                new Color(0.39f, 1f, 0.53f, 0.30f),
                FontStyles.Bold);

            _worldArrowCoreLabel = EnsureArrowText(
                _worldArrowCoreLabel,
                _worldArrowRoot,
                "Core",
                new Vector2(0f, 0.18f),
                new Vector2(1f, 1f),
                146f,
                new Color(0.68f, 1f, 0.76f, 1f),
                FontStyles.Bold);

            _worldArrowCaptionLabel = EnsureArrowText(
                _worldArrowCaptionLabel,
                _worldArrowRoot,
                "Caption",
                new Vector2(0f, 0f),
                new Vector2(1f, 0.28f),
                34f,
                new Color(0.86f, 1f, 0.89f, 0.98f),
                FontStyles.Bold);

            _worldArrowGlowLabel.text = "\u25BC";
            _worldArrowCoreLabel.text = "\u25BC";
            _worldArrowCaptionLabel.text = "BARRACKS";
            SetWorldArrowVisible(false);
        }

        TMP_Text EnsureArrowText(
            TMP_Text label,
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            Color color,
            FontStyles style)
        {
            if (parent == null)
                return label;

            Transform existing = label != null ? label.transform : parent.Find(name);
            TextMeshProUGUI tmp;
            if (existing != null)
            {
                tmp = existing.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
                tmp = go.GetComponent<TextMeshProUGUI>();
            }

            var rect = tmp.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        void EnsureUiArrowRoot()
        {
            if (_mainCanvas == null)
                return;

            if (_uiArrowRoot == null)
            {
                Transform existing = _mainCanvas.transform.Find(UiArrowRootName);
                if (existing != null)
                {
                    _uiArrowRoot = existing as RectTransform;
                }
                else
                {
                    var root = new GameObject(UiArrowRootName, typeof(RectTransform), typeof(Canvas));
                    root.transform.SetParent(_mainCanvas.transform, false);
                    _uiArrowRoot = root.GetComponent<RectTransform>();
                }
            }
            else if (_uiArrowRoot.parent != _mainCanvas.transform)
            {
                _uiArrowRoot.SetParent(_mainCanvas.transform, false);
            }

            if (_uiArrowRoot == null)
                return;

            _uiArrowRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _uiArrowRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _uiArrowRoot.pivot = new Vector2(0.5f, 0.5f);
            _uiArrowRoot.sizeDelta = new Vector2(240f, 220f);
            _uiArrowRoot.SetAsLastSibling();

            var overlayCanvas = _uiArrowRoot.GetComponent<Canvas>();
            if (overlayCanvas == null)
                overlayCanvas = _uiArrowRoot.gameObject.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = 2400;
            overlayCanvas.renderMode = _mainCanvas.renderMode;
            overlayCanvas.worldCamera = _mainCanvas.worldCamera;

            _uiArrowGlowLabel = EnsureArrowText(
                _uiArrowGlowLabel,
                _uiArrowRoot,
                "Glow",
                new Vector2(0f, 0.18f),
                new Vector2(1f, 1f),
                180f,
                new Color(0.39f, 1f, 0.53f, 0.26f),
                FontStyles.Bold);

            _uiArrowCoreLabel = EnsureArrowText(
                _uiArrowCoreLabel,
                _uiArrowRoot,
                "Core",
                new Vector2(0f, 0.20f),
                new Vector2(1f, 1f),
                124f,
                new Color(0.68f, 1f, 0.76f, 1f),
                FontStyles.Bold);

            _uiArrowCaptionLabel = EnsureArrowText(
                _uiArrowCaptionLabel,
                _uiArrowRoot,
                "Caption",
                new Vector2(0f, 0f),
                new Vector2(1f, 0.26f),
                26f,
                new Color(0.88f, 1f, 0.90f, 0.98f),
                FontStyles.Bold);

            _uiArrowGlowLabel.text = "\u25B2";
            _uiArrowCoreLabel.text = "\u25B2";
            _uiArrowCaptionLabel.text = "BUY";
            SetUiArrowVisible(false);
        }

        void UpdatePrompt(MLLaneSnap lane, MLBarracksSite site)
        {
            if (_headlineLabel == null || _detailLabel == null || _focusButton == null)
                return;

            _headlineLabel.text = _stage switch
            {
                CenterBarracksStage.Purchase => "Build Center Barracks",
                CenterBarracksStage.Constructing => "Wait For Center Barracks",
                CenterBarracksStage.OpenBarracks => "Open Center Barracks",
                CenterBarracksStage.BuyMilitia => "Buy Militia",
                CenterBarracksStage.UpgradeTownCoreTier2 => "Upgrade Town Core",
                CenterBarracksStage.WaitForTownCoreTier2 => "Wait For Town Core",
                CenterBarracksStage.BuyMarket => "Buy Market",
                CenterBarracksStage.WaitForMarket => "Wait For Market",
                CenterBarracksStage.BuyTraders => "Buy Traders",
                _ => string.Empty,
            };

            _detailLabel.text = BuildDetailText(lane, site);
            _focusButton.interactable = true;
            _focusButtonLabel.text = _stage switch
            {
                CenterBarracksStage.Purchase => "Open Town Core",
                CenterBarracksStage.Constructing => "Open Town Core",
                CenterBarracksStage.OpenBarracks => "Open Center Barracks",
                CenterBarracksStage.BuyMilitia => "Open Center Barracks",
                CenterBarracksStage.UpgradeTownCoreTier2 => "Open Town Core",
                CenterBarracksStage.WaitForTownCoreTier2 => "Open Town Core",
                CenterBarracksStage.BuyMarket => "Open Town Core",
                CenterBarracksStage.WaitForMarket => "View Market",
                CenterBarracksStage.BuyTraders => "Open Market",
                _ => "Focus Barracks",
            };
        }

        string BuildDetailText(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Waiting for the center barracks state...";

            if (_stage == CenterBarracksStage.Purchase)
            {
                int buildCost = Mathf.Max(0, site.buildCost);
                if (lane != null && lane.gold < buildCost)
                    return $"Center Barracks costs {buildCost}g in Town Core. Need {Mathf.Max(0, buildCost - Mathf.FloorToInt(lane.gold))} more gold before you can purchase it.";

                return $"Town Core starts built. Open Town Core and purchase Center Barracks for {buildCost}g, then wait for construction to finish.";
            }

            if (_stage == CenterBarracksStage.Constructing)
            {
                string timerLabel = BuildConstructionTimerLabel(site.constructionKind, GetConstructionSecondsRemaining(site));
                return $"Center Barracks has been purchased and is still under construction. {timerLabel}. Wait for it to finish before opening the barracks.";
            }

            if (_stage == CenterBarracksStage.OpenBarracks)
            {
                return $"Center Barracks is finished. Open the barracks, then keep buying Militia until you own {MilitiaTargetCount}.";
            }

            if (_stage == CenterBarracksStage.BuyMilitia)
            {
                var militia = FindRosterEntry(site, MilitiaRosterKey);
                int ownedCount = GetOwnedCount(site, MilitiaRosterKey);
                if (militia == null)
                    return $"Open the center barracks and start buying Militia. Progress {ownedCount}/{MilitiaTargetCount}.";

                int unitCost = Mathf.Max(0, militia.buyCost);
                if (lane != null && lane.gold < unitCost)
                    return $"Militia progress {ownedCount}/{MilitiaTargetCount}. Need {Mathf.Max(0, unitCost - Mathf.FloorToInt(lane.gold))} more gold to buy the next Militia.";

                return $"Center Barracks is open. Buy Militia one at a time until you own {MilitiaTargetCount}. Progress {ownedCount}/{MilitiaTargetCount}.";
            }

            var townCorePad = FindFortressPadByBuildingType(lane, "town_core");
            if (_stage == CenterBarracksStage.UpgradeTownCoreTier2)
            {
                if (townCorePad == null)
                    return "Town Core upgrade data is unavailable.";

                int upgradeCost = Mathf.Max(0, townCorePad.upgradeCost);
                if (lane != null && lane.gold < upgradeCost)
                    return $"Town Core Tier 2 needs {Mathf.Max(0, upgradeCost - Mathf.FloorToInt(lane.gold))} more gold before you can upgrade it.";

                return $"Militia is online. Open Town Core and upgrade it to Tier 2 so the Market branch unlocks.";
            }

            if (_stage == CenterBarracksStage.WaitForTownCoreTier2)
            {
                string timerLabel = BuildConstructionTimerLabel(townCorePad != null ? townCorePad.constructionKind : null, GetConstructionSecondsRemaining(townCorePad));
                return $"Town Core is upgrading toward Tier 2. {timerLabel}. Wait for it to finish so you can unlock the Market.";
            }

            var marketPad = FindFortressPadByBuildingType(lane, "market");
            if (_stage == CenterBarracksStage.BuyMarket)
            {
                if (marketPad == null)
                    return "Market branch data is unavailable.";

                int buildCost = Mathf.Max(0, marketPad.buildCost);
                if (lane != null && lane.gold < buildCost)
                    return $"Market costs {buildCost}g. Need {Mathf.Max(0, buildCost - Mathf.FloorToInt(lane.gold))} more gold before you can purchase it.";

                return $"Town Core Tier 2 is ready. Open Town Core and buy the Market to start your income branch.";
            }

            if (_stage == CenterBarracksStage.WaitForMarket)
            {
                string timerLabel = BuildConstructionTimerLabel(marketPad != null ? marketPad.constructionKind : null, GetConstructionSecondsRemaining(marketPad));
                return $"Market has been purchased and is still under construction. {timerLabel}. Wait for it to finish before buying traders.";
            }

            if (_stage == CenterBarracksStage.BuyTraders)
            {
                var currentMarketEntry = GetCurrentMarketRosterEntry(lane);
                if (currentMarketEntry == null)
                    return "Open the Market and buy trader income.";

                int totalCost = Mathf.Max(0, currentMarketEntry.buyCost);
                if (lane != null && lane.gold < totalCost)
                    return $"Need {Mathf.Max(0, totalCost - Mathf.FloorToInt(lane.gold))} more gold to buy {currentMarketEntry.displayName}.";

                return $"Market is open. Buy {currentMarketEntry.displayName} to start your trader income line.";
            }

            return string.Empty;
        }

        void UpdateIndicators()
        {
            if (TryResolveUiArrowTarget(out var uiTarget, out string caption, out bool pointDownFromAbove))
            {
                UpdateUiArrowVisual(uiTarget, caption, pointDownFromAbove);
                SetWorldArrowVisible(false);
                return;
            }

            SetUiArrowVisible(false);
            UpdateWorldArrowVisual();
        }

        void UpdateWorldArrowVisual()
        {
            if (!TryResolveStageWorldPosition(out Vector3 worldPosition))
            {
                SetWorldArrowVisible(false);
                return;
            }

            if (_worldArrowRoot == null)
                EnsureWorldArrowRoot();
            if (_worldArrowRoot == null)
                return;

            float bob = Mathf.Sin(Time.unscaledTime * 3.1f) * 0.42f;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 5.1f) * 0.08f;

            _worldArrowCaptionLabel.text = ResolveWorldArrowCaption();
            _worldArrowRoot.position = worldPosition + new Vector3(0f, bob, 0f);
            _worldArrowRoot.rotation = ResolveFacingRotation(_worldArrowRoot.position);
            _worldArrowRoot.localScale = Vector3.one * (0.0155f * pulse);
            SetWorldArrowVisible(true);
        }

        string ResolveWorldArrowCaption()
        {
            return _stage switch
            {
                CenterBarracksStage.Purchase => "BUILD",
                CenterBarracksStage.Constructing => "WAIT",
                CenterBarracksStage.OpenBarracks => "OPEN",
                CenterBarracksStage.BuyMilitia => "BUY",
                CenterBarracksStage.UpgradeTownCoreTier2 => "UPGRADE",
                CenterBarracksStage.WaitForTownCoreTier2 => "WAIT",
                CenterBarracksStage.BuyMarket => "BUILD",
                CenterBarracksStage.WaitForMarket => "WAIT",
                CenterBarracksStage.BuyTraders => "BUY",
                _ => "BARRACKS",
            };
        }

        bool TryResolveUiArrowTarget(out RectTransform target, out string caption, out bool pointDownFromAbove)
        {
            target = null;
            caption = null;
            pointDownFromAbove = false;

            if (!TryResolveInteractiveTarget(out target, out pointDownFromAbove))
                return false;

            caption = _stage switch
            {
                CenterBarracksStage.Purchase => "BUILD",
                CenterBarracksStage.Constructing => "WAIT",
                CenterBarracksStage.OpenBarracks => "OPEN",
                CenterBarracksStage.BuyMilitia => "BUY",
                CenterBarracksStage.UpgradeTownCoreTier2 => "UPGRADE",
                CenterBarracksStage.WaitForTownCoreTier2 => "WAIT",
                CenterBarracksStage.BuyMarket => "BUILD",
                CenterBarracksStage.WaitForMarket => "WAIT",
                CenterBarracksStage.BuyTraders => "BUY",
                _ => null,
            };

            return target != null && !string.IsNullOrWhiteSpace(caption);
        }

        bool TryResolveInteractiveTarget(out RectTransform target, out bool pointDownFromAbove)
        {
            target = null;
            pointDownFromAbove = false;

            var panel = ResolveBarracksPanel();
            if (panel == null)
                return false;

            switch (_stage)
            {
                case CenterBarracksStage.Purchase:
                    {
                        if (!panel.IsShowingFocusedPad(TownCorePadId))
                            return false;

                        var button = panel.GetTownCoreBarracksPrimaryActionButton(CenterBarracksId);
                        if (button == null || !button.gameObject.activeInHierarchy)
                            return false;

                        target = button.GetComponent<RectTransform>();
                        pointDownFromAbove = true;
                        return target != null;
                    }

                case CenterBarracksStage.OpenBarracks:
                    {
                        if (!panel.IsShowingFocusedPad(TownCorePadId))
                            return false;

                        var button = panel.GetTownCoreBarracksPrimaryActionButton(CenterBarracksId);
                        if (button == null || !button.gameObject.activeInHierarchy)
                            return false;

                        target = button.GetComponent<RectTransform>();
                        pointDownFromAbove = true;
                        return target != null;
                    }

                case CenterBarracksStage.Constructing:
                    {
                        if (!panel.IsShowingFocusedPad(TownCorePadId))
                            return false;

                        var button = panel.GetTownCoreBarracksPrimaryActionButton(CenterBarracksId);
                        if (button == null || !button.gameObject.activeInHierarchy)
                            return false;

                        target = button.GetComponent<RectTransform>();
                        pointDownFromAbove = true;
                        return target != null;
                    }

                case CenterBarracksStage.UpgradeTownCoreTier2:
                case CenterBarracksStage.WaitForTownCoreTier2:
                    {
                        if (!panel.IsShowingFocusedPad(TownCorePadId))
                            return false;

                        var button = panel.GetTownCorePadPrimaryActionButton(TownCorePadId);
                        if (button == null || !button.gameObject.activeInHierarchy)
                            return false;

                        target = button.GetComponent<RectTransform>();
                        pointDownFromAbove = true;
                        return target != null;
                    }

                case CenterBarracksStage.BuyMarket:
                    {
                        var marketPad = FindFortressPadByBuildingType(SnapshotApplier.Instance?.MyLane, "market");
                        if (marketPad == null || !panel.IsShowingFocusedPad(TownCorePadId))
                            return false;

                        var button = panel.GetTownCorePadPrimaryActionButton(marketPad.padId);
                        if (button == null || !button.gameObject.activeInHierarchy)
                            return false;

                        target = button.GetComponent<RectTransform>();
                        pointDownFromAbove = true;
                        return target != null;
                    }

                case CenterBarracksStage.WaitForMarket:
                    return false;

                case CenterBarracksStage.BuyMilitia:
                    {
                        var buyButton = panel.GetFocusedBarracksUnitBuyButton(MilitiaRosterKey);
                        if (buyButton != null && buyButton.gameObject.activeInHierarchy)
                        {
                            target = buyButton.GetComponent<RectTransform>();
                            pointDownFromAbove = true;
                            return target != null;
                        }

                        var card = panel.GetFocusedBarracksUnitCard(MilitiaRosterKey);
                        if (card == null || !card.gameObject.activeInHierarchy)
                            return false;

                        target = card;
                        pointDownFromAbove = true;
                        return target != null;
                    }

                case CenterBarracksStage.BuyTraders:
                    {
                        var currentMarketEntry = GetCurrentMarketRosterEntry(SnapshotApplier.Instance?.MyLane);
                        var buyButton = panel.GetFocusedMarketBuyButton(currentMarketEntry != null ? currentMarketEntry.unitKey : null);
                        if (buyButton == null || !buyButton.gameObject.activeInHierarchy)
                            return false;

                        target = buyButton.GetComponent<RectTransform>();
                        pointDownFromAbove = true;
                        return target != null;
                    }
            }

            return false;
        }

        BarracksPanel ResolveBarracksPanel()
        {
            if (_barracksPanel != null)
                return _barracksPanel;

            var panels = FindObjectsByType<BarracksPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];
                if (panel != null)
                {
                    _barracksPanel = panel;
                    break;
                }
            }

            return _barracksPanel;
        }

        void UpdateUiArrowVisual(RectTransform target, string caption, bool pointDownFromAbove)
        {
            if (_mainCanvas == null || target == null)
            {
                SetUiArrowVisible(false);
                return;
            }

            if (_uiArrowRoot == null)
                EnsureUiArrowRoot();
            if (_uiArrowRoot == null)
                return;

            RectTransform canvasRect = _mainCanvas.transform as RectTransform;
            if (canvasRect == null)
            {
                SetUiArrowVisible(false);
                return;
            }

            var camera = _mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _mainCanvas.worldCamera;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 anchorPoint = pointDownFromAbove
                ? (corners[1] + corners[2]) * 0.5f
                : (corners[0] + corners[3]) * 0.5f;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, anchorPoint);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, camera, out var localPoint))
            {
                SetUiArrowVisible(false);
                return;
            }

            float bob = Mathf.Sin(Time.unscaledTime * 4.2f) * 14f;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 5.4f) * 0.08f;
            float halfWidth = canvasRect.rect.width * 0.5f;
            float halfHeight = canvasRect.rect.height * 0.5f;
            Vector2 anchoredPosition = pointDownFromAbove
                ? localPoint + new Vector2(0f, 116f + bob)
                : localPoint + new Vector2(0f, -116f - bob);
            anchoredPosition.x = Mathf.Clamp(anchoredPosition.x, -halfWidth + 120f, halfWidth - 120f);
            anchoredPosition.y = Mathf.Clamp(anchoredPosition.y, -halfHeight + 112f, halfHeight - 112f);

            _uiArrowGlowLabel.text = pointDownFromAbove ? "\u25BC" : "\u25B2";
            _uiArrowCoreLabel.text = pointDownFromAbove ? "\u25BC" : "\u25B2";
            _uiArrowCaptionLabel.text = caption;
            _uiArrowRoot.anchoredPosition = anchoredPosition;
            _uiArrowRoot.localScale = Vector3.one * pulse;
            _uiArrowRoot.SetAsLastSibling();
            SetUiArrowVisible(true);
        }

        void UpdateTooltipButtonVisual()
        {
            if (_mainCanvas == null || !UserPreferencesManager.ShowTooltips)
            {
                SetTooltipButtonVisible(false);
                return;
            }

            if (_tooltipButtonRoot == null)
                EnsureTooltipButtonRoot();
            if (_tooltipButtonRoot == null)
                return;

            RectTransform canvasRect = _mainCanvas.transform as RectTransform;
            if (canvasRect == null)
            {
                SetTooltipButtonVisible(false);
                return;
            }

            Vector2 localPoint;
            if (!TryResolveTooltipAnchor(out localPoint))
            {
                if (_promptRoot == null)
                {
                    SetTooltipButtonVisible(false);
                    return;
                }

                localPoint = _promptRoot.anchoredPosition + new Vector2(0f, -_promptRoot.sizeDelta.y * 0.5f - 36f);
            }

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 4.6f) * 0.04f;
            float halfWidth = canvasRect.rect.width * 0.5f;
            float halfHeight = canvasRect.rect.height * 0.5f;
            localPoint.x = Mathf.Clamp(localPoint.x, -halfWidth + 54f, halfWidth - 54f);
            localPoint.y = Mathf.Clamp(localPoint.y, -halfHeight + 26f, halfHeight - 26f);

            _tooltipButtonRoot.anchoredPosition = localPoint;
            _tooltipButtonRoot.localScale = Vector3.one * pulse;
            _tooltipButtonRoot.SetAsLastSibling();
            UpdateTooltipButtonStateVisual();
            SetTooltipButtonVisible(!_tooltipExpanded);
        }

        bool TryResolveTooltipAnchor(out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (_mainCanvas == null)
                return false;

            if (TryResolveInteractiveTarget(out var target, out bool pointDownFromAbove)
                && TryProjectTargetToCanvasPoint(target, pointDownFromAbove, out localPoint))
            {
                localPoint += pointDownFromAbove ? new Vector2(0f, 102f) : new Vector2(0f, -102f);
                return true;
            }

            if (!TryResolveStageWorldPosition(out Vector3 worldPosition))
                return false;

            if (!TryProjectWorldToCanvasPoint(worldPosition, out localPoint))
                return false;

            localPoint += new Vector2(0f, 122f);
            return true;
        }

        bool TryResolveStageWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            if (_stage == CenterBarracksStage.Purchase
                || _stage == CenterBarracksStage.UpgradeTownCoreTier2
                || _stage == CenterBarracksStage.WaitForTownCoreTier2)
            {
                var townCoreAnchor = FortressPadAnchor.FindAnchor(TownCorePadId, _laneSlotColor, _laneIndex);
                if (townCoreAnchor != null)
                {
                    worldPosition = ResolveArrowWorldPosition(townCoreAnchor);
                    return true;
                }
            }

            if (_stage == CenterBarracksStage.BuyMarket || _stage == CenterBarracksStage.WaitForMarket || _stage == CenterBarracksStage.BuyTraders)
            {
                var marketPad = FindFortressPadByBuildingType(SnapshotApplier.Instance?.MyLane, "market");
                var marketAnchor = FortressPadAnchor.FindAnchor(marketPad != null ? marketPad.padId : null, _laneSlotColor, _laneIndex);
                if (marketAnchor != null)
                {
                    worldPosition = ResolveArrowWorldPosition(marketAnchor);
                    return true;
                }
            }

            if (_centerBarracksView == null || !_centerBarracksView.isActiveAndEnabled)
                _centerBarracksView = BarracksSiteView.FindSite(CenterBarracksId, _laneSlotColor, _laneIndex);

            if (_centerBarracksView == null)
                return false;

            worldPosition = ResolveArrowWorldPosition(_centerBarracksView);
            return true;
        }

        bool TryProjectTargetToCanvasPoint(RectTransform target, bool pointDownFromAbove, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (_mainCanvas == null || target == null)
                return false;

            RectTransform canvasRect = _mainCanvas.transform as RectTransform;
            if (canvasRect == null)
                return false;

            var camera = ResolveCanvasEventCamera();
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 anchorPoint = pointDownFromAbove
                ? (corners[1] + corners[2]) * 0.5f
                : (corners[0] + corners[3]) * 0.5f;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, anchorPoint);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, camera, out localPoint);
        }

        bool TryProjectWorldToCanvasPoint(Vector3 worldPosition, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (_mainCanvas == null)
                return false;

            RectTransform canvasRect = _mainCanvas.transform as RectTransform;
            if (canvasRect == null)
                return false;

            var worldCamera = _mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? Camera.main
                : (_mainCanvas.worldCamera != null ? _mainCanvas.worldCamera : Camera.main);
            var screenPoint = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPosition);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, ResolveCanvasEventCamera(), out localPoint);
        }

        Camera ResolveCanvasEventCamera()
        {
            return _mainCanvas != null && _mainCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _mainCanvas.worldCamera
                : null;
        }

        void HandleTooltipButtonPressed()
        {
            if (!UserPreferencesManager.ShowTooltips)
                return;

            _tooltipExpanded = true;
            UpdateTooltipButtonStateVisual();
            SetPromptVisible(_tooltipExpanded);
        }

        void UpdateTooltipButtonStateVisual()
        {
            if (_tooltipButton == null)
                return;

            var image = _tooltipButton.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.10f, 0.22f, 0.30f, 0.96f);
            }

            if (_tooltipButtonLabel != null)
                _tooltipButtonLabel.text = "TIP";
        }

        static Quaternion ResolveFacingRotation(Vector3 worldPosition)
        {
            var camera = Camera.main;
            if (camera == null)
                return Quaternion.identity;

            Vector3 direction = worldPosition - camera.transform.position;
            return direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
        }

        static Vector3 ResolveArrowWorldPosition(BarracksSiteView barracksView)
        {
            if (barracksView != null && TryGetActiveRendererBounds(barracksView.transform, out var bounds))
            {
                float lift = Mathf.Max(2.8f, bounds.size.y * 0.70f);
                return new Vector3(bounds.center.x, bounds.max.y + lift, bounds.center.z);
            }

            Vector3 fallback = barracksView != null ? barracksView.FocusTransform.position : Vector3.zero;
            return fallback + new Vector3(0f, 4.4f, 0f);
        }

        static Vector3 ResolveArrowWorldPosition(FortressPadAnchor anchor)
        {
            if (anchor != null)
            {
                var bounds = anchor.GetWorldBounds();
                float lift = Mathf.Max(2.8f, bounds.size.y * 0.70f);
                return new Vector3(bounds.center.x, bounds.max.y + lift, bounds.center.z);
            }

            return Vector3.zero;
        }

        static bool TryGetActiveRendererBounds(Transform root, out Bounds bounds)
        {
            var renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null || renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bool hasBounds = false;
            Bounds combined = default;

            for (int pass = 0; pass < 2; pass++)
            {
                bool requireVisible = pass == 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null)
                        continue;
                    if (requireVisible && (!renderer.enabled || !renderer.gameObject.activeInHierarchy))
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

                if (hasBounds)
                    break;
            }

            bounds = combined;
            return hasBounds;
        }

        void HandleFocusButtonPressed()
        {
            int laneIndex = _laneIndex >= 0
                ? _laneIndex
                : SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
            if (laneIndex < 0)
                return;

            if (_stage == CenterBarracksStage.Purchase)
            {
                if (!FortressSelectionController.OpenFortressPad(laneIndex, TownCorePadId))
                    FortressSelectionController.FocusFortressPad(laneIndex, TownCorePadId);
                return;
            }

            if (_stage == CenterBarracksStage.Constructing)
            {
                if (!FortressSelectionController.OpenFortressPad(laneIndex, TownCorePadId))
                    FortressSelectionController.FocusFortressPad(laneIndex, TownCorePadId);
                return;
            }

            if (_stage == CenterBarracksStage.UpgradeTownCoreTier2 || _stage == CenterBarracksStage.WaitForTownCoreTier2 || _stage == CenterBarracksStage.BuyMarket)
            {
                if (!FortressSelectionController.OpenFortressPad(laneIndex, TownCorePadId))
                    FortressSelectionController.FocusFortressPad(laneIndex, TownCorePadId);
                return;
            }

            if (_stage == CenterBarracksStage.WaitForMarket || _stage == CenterBarracksStage.BuyTraders)
            {
                var marketPad = FindFortressPadByBuildingType(SnapshotApplier.Instance?.MyLane, "market");
                if (marketPad != null)
                {
                    FortressSelectionController.OpenFortressPad(laneIndex, marketPad.padId);
                    return;
                }
            }

            FortressSelectionController.OpenBarracksSite(laneIndex, CenterBarracksId);
        }

        void HandlePromptHidePressed()
        {
            _tooltipExpanded = false;
            UpdateTooltipButtonStateVisual();
            SetPromptVisible(false);
            UpdateTooltipButtonVisual();
        }

        void HandleTooltipPreferenceChanged(bool enabled)
        {
            UserPreferencesManager.SetTooltipsVisible(enabled);
            if (!enabled)
            {
                _tooltipExpanded = false;
                SetPromptVisible(false);
                SetTooltipButtonVisible(false);
                HideIndicators();
                return;
            }

            _tooltipExpanded = true;
            SetPromptVisible(true);
            UpdateIndicators();
            UpdateTooltipButtonVisual();
        }

        void SetPromptVisible(bool visible)
        {
            _promptTooltipToggle?.SetIsOnWithoutNotify(UserPreferencesManager.ShowTooltips);
            if (_promptRoot != null)
                _promptRoot.gameObject.SetActive(visible);
        }

        void SetTooltipButtonVisible(bool visible)
        {
            if (_tooltipButtonRoot != null)
                _tooltipButtonRoot.gameObject.SetActive(visible);
        }

        void SetWorldArrowVisible(bool visible)
        {
            if (_worldArrowRoot != null)
                _worldArrowRoot.gameObject.SetActive(visible);
        }

        void SetUiArrowVisible(bool visible)
        {
            if (_uiArrowRoot != null)
                _uiArrowRoot.gameObject.SetActive(visible);
        }
    }
}
