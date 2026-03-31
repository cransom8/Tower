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
        const int MilitiaTargetCount = 10;
        const string PromptRootName = "CenterBarracksOnboardingPrompt";
        const string ArrowRootName = "CenterBarracksOnboardingArrow";
        const string UiArrowRootName = "CenterBarracksOnboardingUiArrow";
        const float PromptMinWidth = 420f;
        const float PromptMaxWidth = 620f;
        const float PromptRegularHeight = 160f;
        const float PromptCompactHeight = 176f;

        enum CenterBarracksStage
        {
            None = 0,
            Purchase = 1,
            Upgrade = 2,
            BuyMilitia = 3,
        }

        Canvas _mainCanvas;
        RectTransform _promptRoot;
        TMP_Text _headlineLabel;
        TMP_Text _detailLabel;
        Button _focusButton;
        TMP_Text _focusButtonLabel;

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
        bool _pendingPurchaseHandoff;
        bool _snapshotSubscribed;
        bool _matchReadySubscribed;
        bool _actionAppliedSubscribed;
        int _laneIndex = -1;
        string _laneSlotColor;
        BarracksSiteView _centerBarracksView;
        BarracksPanel _barracksPanel;

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
            SetWorldArrowVisible(false);
            SetUiArrowVisible(false);
        }

        void Update()
        {
            EnsureSubscriptions();

            if (!_flowResolved || _flowCompleted || _stage == CenterBarracksStage.None)
                return;

            EnsureUi();
            UpdateIndicators();
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

            if (payload.type == "build_barracks_site")
            {
                var snapshotApplier = SnapshotApplier.Instance;
                int myLaneIndex = _laneIndex >= 0
                    ? _laneIndex
                    : snapshotApplier != null ? snapshotApplier.MyLaneIndex : -1;
                var centerSite = myLaneIndex >= 0
                    ? snapshotApplier?.GetBarracksSite(myLaneIndex, CenterBarracksId)
                    : null;

                if (centerSite == null || !centerSite.isBuilt)
                    _pendingPurchaseHandoff = true;

                RefreshOnboardingState();
                return;
            }

            if (payload.type == "buy_barracks_unit" && (_stage == CenterBarracksStage.BuyMilitia || _pendingPurchaseHandoff))
                RefreshOnboardingState();
        }

        void ResetFlow()
        {
            _initialStage = CenterBarracksStage.None;
            _stage = CenterBarracksStage.None;
            _flowResolved = false;
            _flowCompleted = false;
            _pendingPurchaseHandoff = false;
            _laneIndex = -1;
            _laneSlotColor = null;
            _centerBarracksView = null;
            SetPromptVisible(false);
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

            if (_pendingPurchaseHandoff && site.isBuilt)
            {
                _initialStage = CenterBarracksStage.Purchase;
                _stage = ResolveMilitiaStage(site);
                _flowResolved = true;
                _flowCompleted = false;
                _pendingPurchaseHandoff = false;
            }
            else if (!_flowResolved)
            {
                _initialStage = ResolveInitialStage(site);
                _stage = _initialStage;
                _flowResolved = true;
            }
            else
            {
                _stage = ResolveCurrentStage(site);
            }

            if (_stage == CenterBarracksStage.None)
            {
                CompleteOnboarding();
                return;
            }

            _centerBarracksView = BarracksSiteView.FindSite(CenterBarracksId, lane.slotColor, lane.laneIndex) ?? _centerBarracksView;

            EnsureUi();
            UpdatePrompt(lane, site);
            SetPromptVisible(true);
            UpdateIndicators();
        }

        void CompleteOnboarding()
        {
            _stage = CenterBarracksStage.None;
            _flowCompleted = true;
            _centerBarracksView = null;
            SetPromptVisible(false);
            HideIndicators();
        }

        CenterBarracksStage ResolveInitialStage(MLBarracksSite site)
        {
            if (site == null)
                return CenterBarracksStage.None;

            if (!site.isBuilt)
                return CenterBarracksStage.Purchase;

            return site.maxLevel > 1 && site.level <= 1
                ? CenterBarracksStage.Upgrade
                : CenterBarracksStage.None;
        }

        CenterBarracksStage ResolveCurrentStage(MLBarracksSite site)
        {
            if (site == null)
                return CenterBarracksStage.None;

            return _initialStage switch
            {
                CenterBarracksStage.Purchase => !site.isBuilt
                    ? CenterBarracksStage.Purchase
                    : ResolveMilitiaStage(site),
                CenterBarracksStage.Upgrade => site.isBuilt && site.level > 1
                    ? CenterBarracksStage.None
                    : CenterBarracksStage.Upgrade,
                CenterBarracksStage.BuyMilitia => ResolveMilitiaStage(site),
                _ => CenterBarracksStage.None,
            };
        }

        CenterBarracksStage ResolveMilitiaStage(MLBarracksSite site)
        {
            return GetOwnedCount(site, MilitiaRosterKey) >= MilitiaTargetCount
                ? CenterBarracksStage.None
                : CenterBarracksStage.BuyMilitia;
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
                new Vector2(0.94f, 0.90f),
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
                CenterBarracksStage.Purchase => "Purchase Center Barracks",
                CenterBarracksStage.Upgrade => "Upgrade your Barracks",
                CenterBarracksStage.BuyMilitia => "Buy Militia x10",
                _ => string.Empty,
            };

            _detailLabel.text = BuildDetailText(lane, site);
            _focusButton.interactable = true;
            _focusButtonLabel.text = _stage switch
            {
                CenterBarracksStage.Purchase => "Open To Purchase",
                CenterBarracksStage.Upgrade => "Open To Upgrade",
                CenterBarracksStage.BuyMilitia => "Open To Buy",
                _ => "Focus Barracks",
            };
        }

        string BuildDetailText(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Waiting for the center barracks state...";

            if (_stage == CenterBarracksStage.Purchase)
            {
                if (!site.canBuild && !string.IsNullOrWhiteSpace(site.lockedReason))
                    return site.lockedReason;

                if (lane != null && lane.gold < site.buildCost)
                    return $"Need {Mathf.Max(0, site.buildCost - Mathf.FloorToInt(lane.gold))} more gold to purchase the center barracks.";

                return "Focus the center barracks and buy it to unlock your first troop source.";
            }

            if (_stage == CenterBarracksStage.Upgrade)
            {
                if (!site.canUpgrade && !string.IsNullOrWhiteSpace(site.lockedReason))
                    return site.lockedReason;

                if (lane != null && lane.gold < site.upgradeCost)
                    return $"Need {Mathf.Max(0, site.upgradeCost - Mathf.FloorToInt(lane.gold))} more gold to upgrade the center barracks.";

                return "Focus the center barracks and upgrade it to improve its spawn strength and cadence.";
            }

            if (_stage == CenterBarracksStage.BuyMilitia)
            {
                var militia = FindRosterEntry(site, MilitiaRosterKey);
                if (militia == null)
                    return "Open the center barracks and stock it with your first militia squad.";

                int totalCost = Mathf.Max(0, militia.buyCost * MilitiaTargetCount);
                if (lane != null && lane.gold < totalCost)
                    return $"Need {Mathf.Max(0, totalCost - Mathf.FloorToInt(lane.gold))} more gold to buy Militia x{MilitiaTargetCount}.";

                return $"Open the center barracks and buy Militia x{MilitiaTargetCount} to start your first live barracks deployment.";
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
            if (_centerBarracksView == null || !_centerBarracksView.isActiveAndEnabled)
                _centerBarracksView = BarracksSiteView.FindSite(CenterBarracksId, _laneSlotColor, _laneIndex);

            if (_centerBarracksView == null)
            {
                SetWorldArrowVisible(false);
                return;
            }

            if (_worldArrowRoot == null)
                EnsureWorldArrowRoot();
            if (_worldArrowRoot == null)
                return;

            Vector3 worldPosition = ResolveArrowWorldPosition(_centerBarracksView);
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
                CenterBarracksStage.Purchase => "PURCHASE",
                CenterBarracksStage.Upgrade => "UPGRADE",
                CenterBarracksStage.BuyMilitia => "OPEN",
                _ => "BARRACKS",
            };
        }

        bool TryResolveUiArrowTarget(out RectTransform target, out string caption, out bool pointDownFromAbove)
        {
            target = null;
            caption = null;
            pointDownFromAbove = false;

            var panel = ResolveBarracksPanel();
            if (panel == null || !panel.IsShowingFocusedBarracks(CenterBarracksId))
                return false;

            switch (_stage)
            {
                case CenterBarracksStage.Purchase:
                case CenterBarracksStage.Upgrade:
                    {
                        var button = panel.GetFocusedBarracksHeaderActionButton();
                        if (button == null || !button.gameObject.activeInHierarchy)
                            return false;

                        target = button.GetComponent<RectTransform>();
                        caption = _stage == CenterBarracksStage.Purchase ? "BUY" : "UPGRADE";
                        pointDownFromAbove = false;
                        return target != null;
                    }

                case CenterBarracksStage.BuyMilitia:
                    {
                        var card = panel.GetFocusedBarracksUnitCard(MilitiaRosterKey);
                        if (card == null || !card.gameObject.activeInHierarchy)
                            return false;

                        target = card;
                        caption = "BUY x10";
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

            FortressSelectionController.OpenBarracksSite(laneIndex, CenterBarracksId);
        }

        void SetPromptVisible(bool visible)
        {
            if (_promptRoot != null)
                _promptRoot.gameObject.SetActive(visible);
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
