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
        const float PanelBaseWidth = 1080f;
        const float PanelBaseHeight = 760f;
        const float PanelViewportWidthRatio = 0.94f;
        const float PanelViewportHeightRatio = 0.95f;
        const string MilitiaRosterKey = "militia";

        public GameObject PanelBarracks;
        public TMP_Text TxtTitle;
        public TMP_Text TxtBenefits;
        public TMP_Text TxtCost;
        public TMP_Text TxtAffordance;
        public Button BtnConfirm;
        public Button BtnCancel;

        RectTransform _contentRoot;
        ScrollRect _scrollRect;
        string _lastContentSignature;
        int _lastHeaderTick = -1;
        bool _initialized;
        bool _networkHooksRegistered;
        string _statusMessage;
        string _pendingBarracksBuildId;
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
        int _guidedUnlockRequiredTier;

        enum GuidedPadAction
        {
            None,
            Build,
            Upgrade,
            Explain,
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
            EnsureRuntimePanelChrome();
            EnsureRuntimeContentRoot(forceReconfigure: true);
            RefreshHeader(force: true);
            OpenPanel();
            Canvas.ForceUpdateCanvases();
            RefreshContent(force: true);
            if (_scrollRect != null)
            {
                _scrollRect.StopMovement();
                _scrollRect.verticalNormalizedPosition = 1f;
            }
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

            string buttonName = BuildFocusedBarracksActionObjectName(quantity >= 10 ? "BuyTen" : "Buy", rosterKey);
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
            panelImage.color = new Color(0.05f, 0.07f, 0.12f, 0.98f);

            TxtTitle = EnsureRuntimeLabel(
                TxtTitle,
                "RuntimeTitle",
                new Vector2(0.05f, 0.92f),
                new Vector2(0.54f, 0.978f),
                24f,
                FontStyles.Bold,
                Color.white,
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
                new Color(0.86f, 0.90f, 0.95f, 0.98f),
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
                new Color(0.98f, 0.90f, 0.52f, 1f),
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
                new Color(0.82f, 0.88f, 0.95f, 0.95f),
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
                    confirmImage.color = new Color(0.20f, 0.29f, 0.18f, 0.98f);
            }

            ApplyResponsiveChromeLayout();
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
            label.fontSize = fontSize;
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

            var image = button.GetComponent<Image>();
            image.color = new Color(0.28f, 0.18f, 0.16f, 0.96f);

            var label = EnsureButtonLabel(button, labelText);
            label.color = Color.white;
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

            var image = BtnConfirm.GetComponent<Image>();
            if (image != null)
            {
                image.color = interactable
                    ? backgroundColor
                    : new Color(0.20f, 0.22f, 0.26f, 0.92f);
            }

            var labelText = EnsureButtonLabel(BtnConfirm, label);
            if (labelText != null)
            {
                labelText.color = interactable
                    ? Color.white
                    : new Color(0.72f, 0.72f, 0.72f, 0.95f);
            }

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
                case "upgrade_barracks":
                case "build_barracks_site":
                case "upgrade_barracks_site":
                case "buy_barracks_unit":
                case "sell_barracks_unit":
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
            _statusMessage = payload.message;
            RefreshHeader(force: true);
        }

        void HideImmediate()
        {
            DestroyProgressionViewer();
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
            scrollRt.offsetMin = new Vector2(GetPanelSidePadding(), GetPanelBottomPadding());
            scrollRt.offsetMax = new Vector2(-GetPanelSidePadding(), -GetContentTopInset());

            scrollImage.color = new Color(0.06f, 0.08f, 0.12f, 0.82f);
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
            viewportRt.offsetMin = new Vector2(viewportInset, viewportInset);
            viewportRt.offsetMax = new Vector2(-viewportInset, -viewportInset);
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
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _scrollRect.viewport = viewportRt;
            _scrollRect.content = _contentRoot;
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
                SetTopRightRect(BtnCancel.GetComponent<RectTransform>(), topPadding, sidePadding, closeWidth, buttonHeight);
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

            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Left;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        void ConfigureHeaderButtonLabel(Button button)
        {
            var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null)
                return;

            label.fontSize = GetHeaderButtonFontSize();
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
        float GetPanelSidePadding() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 16f : 18f) : 22f;
        float GetPanelTopPadding() => IsCompactPanelLayout() ? 10f : 12f;
        float GetPanelBottomPadding() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 8f : 10f) : 12f;
        float GetContentViewportInset() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 5f : 6f) : 8f;
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
        float GetHeaderToContentGap() => IsCompactPanelLayout() ? (IsTightPanelLayout() ? 6f : 8f) : 10f;

        float GetContentTopInset()
        {
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
                GetHeaderToContentGap();
        }

        void RefreshHeader(bool force = false)
        {
            var snapshotApplier = SnapshotApplier.Instance;
            var snap = snapshotApplier?.LatestML;
            var lane = snapshotApplier?.MyLane;
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
                    ? BuildFocusedPadSummary(focusedPad, lane.barracksRoster, lane.heroRoster)
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
                        ? BuildFocusedPadHint(focusedPad, lane.barracksRoster, lane.heroRoster, sendSeconds, waveSeconds)
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

            EnsureRuntimePanelChrome();
            EnsureRuntimeContentRoot(forceReconfigure: true);

            float viewportWidth = GetContentViewportWidth();
            float viewportHeight = GetContentViewportHeight();
            string signature = BuildContentSignature(lane);
            if (signature == _lastContentSignature
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

            CaptureFocusedBarracksRailPosition();
            ClearContent();
            if (lane == null)
                return;

            var focusedBarracks = GetFocusedBarracksSite(lane);
            var focusedPad = GetFocusedPad(lane);
            if (_scrollRect != null)
                _scrollRect.vertical = focusedBarracks == null && focusedPad == null;

            if (focusedBarracks != null)
            {
                CreateFocusedBarracksSection(lane, focusedBarracks);
                if (focusedBarracks.isBuilt)
                {
                    CreateFocusedBarracksRosterSection(lane, focusedBarracks);
                    CreateFocusedBarracksHeroSection(lane, focusedBarracks);
                }
            }
            else if (focusedPad != null)
            {
                CreateFocusedPadSection(lane);
            }
            else
            {
                CreateBuildingOverviewSection(lane);
            }
            _lastContentSignature = BuildContentSignature(lane);
            _lastViewportWidth = GetContentViewportWidth();
            _lastViewportHeight = GetContentViewportHeight();
        }

        string BuildContentSignature(MLLaneSnap lane)
        {
            if (lane == null)
                return "no-lane";

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
                    sig += $"{pad.padId}:{pad.tier}:{pad.buildState}:{pad.canBuild}:{pad.canUpgrade}:{Mathf.RoundToInt(pad.hp)}:{Mathf.RoundToInt(pad.maxHp)}:{pad.lockedReason}|";
                }
            }

            if (lane.barracksRoster != null)
            {
                for (int i = 0; i < lane.barracksRoster.Length; i++)
                {
                    var entry = lane.barracksRoster[i];
                    if (entry == null) continue;
                    sig += $"{entry.rosterKey}:{entry.skinKey}:{entry.ownedCount}:{entry.buyCost}:{entry.sellRefund}:{entry.unlocked}:{entry.lockedReason}|";
                }
            }

            if (lane.barracksSites != null)
            {
                for (int i = 0; i < lane.barracksSites.Length; i++)
                {
                    var site = lane.barracksSites[i];
                    if (site == null) continue;
                    sig +=
                        $"{site.barracksId}:{site.isBuilt}:{site.level}:{site.buildState}:{site.canBuild}:{site.canUpgrade}:" +
                        $"{Mathf.RoundToInt(site.hp)}:{Mathf.RoundToInt(site.maxHp)}:{site.lockedReason}|";
                    if (site.roster == null) continue;
                    for (int rosterIndex = 0; rosterIndex < site.roster.Length; rosterIndex++)
                    {
                        var entry = site.roster[rosterIndex];
                        if (entry == null) continue;
                        sig += $"{site.barracksId}:{entry.rosterKey}:{entry.skinKey}:{entry.ownedCount}:{entry.buyCost}:{entry.sellRefund}:{entry.unlocked}:{entry.lockedReason}|";
                    }
                }
            }

            if (lane.heroRoster != null)
            {
                for (int i = 0; i < lane.heroRoster.Length; i++)
                {
                    var hero = lane.heroRoster[i];
                    if (hero == null) continue;
                    sig +=
                        $"hero:{hero.heroKey}:{hero.state}:{hero.canSummon}:{hero.cooldownTicksRemaining}:" +
                        $"{hero.activeCount}:{hero.activeLimit}:{hero.disabledReason}:{hero.lockedReason}|";
                }
            }

            return sig;
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

            if (!IsCompactPanelLayout())
                CreateSectionHeader("Barracks Details");
            var card = CreateCardContainer();
            var element = card.GetComponent<LayoutElement>();
            if (element != null)
            {
                float cardHeight = GetFocusedBarracksDetailCardHeight();
                element.minHeight = cardHeight;
                element.preferredHeight = cardHeight;
            }
            TintCard(card, site.isBuilt
                ? new Color(0.13f, 0.22f, 0.18f, 0.98f)
                : new Color(0.17f, 0.18f, 0.22f, 0.98f));
            CreateCardTitle(card, $"{ResolveBarracksDisplayName(site)}  {ResolveFocusedBarracksStateLabel(site)}");
            CreateBodyText(card, BuildFocusedBarracksOverview(lane, site));
        }

        void CreateFocusedBarracksRosterSection(MLLaneSnap lane, MLBarracksSite site)
        {
            int rosterEntries = CountRosterEntries(site?.roster);
            int expectedEntries = GetFocusedBarracksExpectedRosterCount(site);
            string headerText = expectedEntries > 0
                ? $"Standard Units ({rosterEntries}/{expectedEntries})"
                : $"Standard Units ({rosterEntries})";
            CreateSectionHeader(headerText);
            if (site?.roster == null || site.roster.Length == 0)
            {
                CreateInfoCard("No barracks-specific roster data is available yet.");
                return;
            }

            var ordered = (MLBarracksRosterEntry[])site.roster.Clone();
            System.Array.Sort(ordered, CompareFocusedBarracksRosterEntries);
            var grid = CreateFocusedBarracksCardRail();
            for (int i = 0; i < ordered.Length; i++)
            {
                var entry = ordered[i];
                if (entry == null) continue;

                CreateFocusedBarracksRosterCard(grid, lane, site, entry);
            }

            Canvas.ForceUpdateCanvases();
            RestoreFocusedBarracksRailPosition(grid);
            SyncFocusedBarracksRailChrome(grid);
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

                CreateFocusedBarracksHeroCard(lane, site, hero);
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
                WireLockedRosterCardNavigation(card, () => RedirectLockedUnitToUnlockBuilding(lane, entry));

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
                CreateInfoCard("Choose Barracks Left, Center, or Right. Building purchase, upgrades, and unit buying happen on the real barracks instance screen.");
                CreateBarracksInstanceOverviewSection(lane, null);
                return;
            }

            CreateSectionHeader("Selected Building");
            var card = CreateCardContainer();
            TintCard(card, ResolvePadCardTint(pad));
            CreateCardTitle(card, $"{pad.buildingName}  {HumanizeBuildState(pad.buildState)}");
            CreateBodyText(card, BuildFocusedPadCardBody(pad, lane.barracksRoster, lane.heroRoster));

            bool hasGuidedUnlock = TryGetGuidedUnlockForPad(pad, out var guidedAction, out string guidedHelperText);
            if (hasGuidedUnlock && !string.IsNullOrWhiteSpace(guidedHelperText))
            {
                CreateInlineText(
                    card,
                    "GuidedUnlockHint",
                    guidedHelperText,
                    IsCompactPanelLayout() ? 10f : 11f,
                    new Color(0.98f, 0.92f, 0.70f, 0.98f),
                    FontStyles.Bold,
                    TextAlignmentOptions.Left);
            }

            var actions = CreateActionRow(card);
            if (pad.canBuild)
            {
                CreateActionButton(actions, $"Build {pad.buildCost}g", () =>
                {
                    _statusMessage = $"Building {pad.buildingName}...";
                    ActionSender.BuildOnPad(pad.padId);
                    RefreshHeader(force: true);
                }, CanSpendGold(lane, pad.buildCost), highlighted: guidedAction == GuidedPadAction.Build);
            }
            else if (pad.canUpgrade)
            {
                CreateActionButton(actions, $"Upgrade {pad.upgradeCost}g", () =>
                {
                    _statusMessage = $"Upgrading {pad.buildingName}...";
                    ActionSender.UpgradeBuilding(pad.padId, pad.buildingType);
                    RefreshHeader(force: true);
                }, CanSpendGold(lane, pad.upgradeCost), highlighted: guidedAction == GuidedPadAction.Upgrade);
            }
            else
            {
                string label = pad.isBuilt && pad.buildState == "max_tier" ? "Max Tier" : "Unavailable";
                CreateActionButton(actions, label, null, false);
            }

            if (!string.IsNullOrWhiteSpace(pad.lockedReason))
            {
                CreateActionButton(actions, "Why", () =>
                {
                    _statusMessage = $"{pad.buildingName}: {pad.lockedReason}";
                    RefreshHeader(force: true);
                }, true, highlighted: guidedAction == GuidedPadAction.Explain);
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
                "Select to focus this real barracks and open its own screen.",
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
            CreateBodyText(card, BuildBuildingOverviewPadBody(pad, lane.barracksRoster, lane.heroRoster));
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
                $"Human progression is building-driven. Upgrade the Civic branch to Castle to unlock heroes in the Barracks.\n" +
                $"Barracks Purchased {barracksBuilt}/{barracksTotal}   Other Buildings Owned {built}/{total}   Locked {locked}\n" +
                "Select a branch card to focus that world building and open its detailed interaction UI.";
        }

        string BuildBuildingOverviewPadBody(MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return "No building selected.";

            string ownership = pad.isBuilt
                ? $"Owned   Tier {Mathf.Max(0, pad.tier)}/{Mathf.Max(1, pad.maxTier)}"
                : "Not Yet Purchased";
            string stateLine = pad.isBuilt
                ? $"{HumanizeBuildState(pad.buildState)}   HP {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}"
                : string.IsNullOrWhiteSpace(pad.lockedReason)
                    ? "Available to purchase from its detailed view."
                    : pad.lockedReason;

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
                    case 1: return "House foundation for civic progression.";
                    case 2: return "Town Hall expands the city core.";
                    case 3: return "Keep prepares the final civic step.";
                    case 4: return "Castle unlocks King, Paladin, and Bishop at the Barracks.";
                    default: return "No civic unlock data.";
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
                    1 => "Economy routes begin here.",
                    2 => "Expands the market economy route.",
                    3 => "Unlocks the final market route tier.",
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
                        1 => "Enables Turret Tier 1 construction.",
                        2 => "Enables Wall and Gate Tier 2 upgrades.",
                        3 => "Enables Wall and Gate Tier 3 upgrades.",
                        _ => "No lumber mill unlock data.",
                    },
                    "wall" => tier switch
                    {
                        1 => "Builds the first wall segment tier.",
                        2 => "Requires Lumber Mill Tier 2.",
                        3 => "Requires Lumber Mill Tier 3.",
                        _ => "No wall unlock data.",
                    },
                    "gate" => tier switch
                    {
                        1 => "Builds the first gate tier.",
                        2 => "Requires Lumber Mill Tier 2.",
                        3 => "Requires Lumber Mill Tier 3.",
                        _ => "No gate unlock data.",
                    },
                    "turret" => tier switch
                    {
                        1 => "Requires Lumber Mill Tier 1.",
                        2 => "Strengthens this tower hardpoint.",
                        3 => "Final base tower tier.",
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
                return "Buy Building";

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
                    ? "Open this barracks to buy the building from its own screen."
                    : site.lockedReason;

            return
                $"{ownership}\n" +
                $"{stateLine}\n" +
                $"Tech: {CountUnlockedUnits(site.roster)}/{CountRosterEntries(site.roster)} unit entries unlocked";
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
            _guidedUnlockRequiredTier = 0;
        }

        void SetGuidedUnlockContext(MLBarracksRosterEntry entry, MLFortressPad pad)
        {
            if (entry == null || pad == null)
            {
                ClearGuidedUnlockContext();
                return;
            }

            _guidedUnlockPadId = pad.padId;
            _guidedUnlockUnitKey = entry.rosterKey;
            _guidedUnlockUnitName = entry.displayName;
            _guidedUnlockBuildingType = entry.unlockBuildingType;
            _guidedUnlockBuildingName = !string.IsNullOrWhiteSpace(entry.unlockBuildingName)
                ? entry.unlockBuildingName
                : !string.IsNullOrWhiteSpace(pad.buildingName)
                    ? pad.buildingName
                    : HumanizeCombatType(entry.unlockBuildingType);
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

        void RedirectLockedUnitToUnlockBuilding(MLLaneSnap lane, MLBarracksRosterEntry entry)
        {
            if (lane == null || entry == null)
                return;

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

            FortressSelectionController.FocusFortressPad(lane.laneIndex, pad.padId);
            ShowForPad(pad.padId);
            SetGuidedUnlockContext(entry, pad);
            _statusMessage = null;
            RefreshHeader(force: true);
            RefreshContent(force: true);
        }

        string BuildLockedUnitRedirectHint(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return "Click to open unlock building.";

            string unlockLabel = BuildFocusedBarracksUnlockLabel(entry);
            return string.IsNullOrWhiteSpace(unlockLabel) || string.Equals(unlockLabel, "unlock requirement", System.StringComparison.OrdinalIgnoreCase)
                ? "Click to open unlock building."
                : $"Click to open {unlockLabel}.";
        }

        string BuildLockedUnitRedirectActionLabel(MLBarracksRosterEntry entry)
        {
            if (entry == null)
                return "Open Unlock Building";

            string buildingName = !string.IsNullOrWhiteSpace(entry.unlockBuildingName)
                ? entry.unlockBuildingName
                : string.IsNullOrWhiteSpace(entry.unlockBuildingType)
                    ? null
                    : HumanizeCombatType(entry.unlockBuildingType);
            return string.IsNullOrWhiteSpace(buildingName)
                ? "Open Unlock Building"
                : $"Open {buildingName}";
        }

        string BuildFocusedBarracksSummary(MLBarracksSite site)
        {
            if (site == null)
                return string.Empty;

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseStatus(site);

            int ownedUnits = CountOwnedUnits(site.roster);
            return
                $"Health {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}   " +
                $"Owned Units {ownedUnits}   Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}";
        }

        string BuildFocusedBarracksHeaderText(MLLaneSnap lane, MLBarracksSite site, int waveSeconds)
        {
            if (site == null)
                return string.Empty;

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
                $"Active {CountActiveUnitsForBarracks(lane, site)}",
                $"Owned {CountOwnedUnits(site.roster)}",
                $"Unlocked {CountUnlockedUnits(site.roster)}/{(site.roster != null ? site.roster.Length : 0)}",
                $"Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}",
            };
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

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseStatus(site);

            string activeLead = BuildBarracksActivityLead(lane, site, string.Empty);
            if (!string.IsNullOrWhiteSpace(activeLead))
                return $"Active {activeLead}";

            int unlockedUnits = CountUnlockedUnits(site.roster);
            return unlockedUnits > 0
                ? $"Unlocked {unlockedUnits} unit option{(unlockedUnits == 1 ? string.Empty : "s")} for this barracks. Buy units here to add live barracks output."
                : "No units unlocked yet. Build the listed fortress branches to unlock barracks units.";
        }

        string BuildFocusedBarracksHint(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Waiting for barracks data...";

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseHint(lane, site);

            int unlockedUnits = CountUnlockedUnits(site.roster);
            if (unlockedUnits <= 0)
                return "All units are still tech-locked. Build the listed fortress structures first.";

            int cheapestCost = GetCheapestUnlockedCost(site.roster);
            if (lane != null && cheapestCost > 0 && lane.gold < cheapestCost)
                return $"Need {Mathf.Max(0, cheapestCost - Mathf.FloorToInt(lane.gold))} more gold for the cheapest unlocked purchase.";

            return "Swipe on mobile or use the mouse wheel on desktop to browse. Buy and sell from the top row on each card.";
        }

        string BuildFocusedBarracksOverview(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "No barracks selected.";

            if (!site.isBuilt)
            {
                var lines = new List<string>
                {
                    $"State {ResolveFocusedBarracksStateLabel(site)}",
                    BuildFocusedBarracksCostText(site),
                };
                lines.Add(site.canBuild
                    ? "Ready to purchase from this barracks screen."
                    : BuildFocusedBarracksRequirementText(site));
                return string.Join("\n", lines);
            }

            int spawnIntervalSeconds = GetFocusedBarracksSpawnIntervalSeconds(site);
            string intervalLine = spawnIntervalSeconds >= 0 ? $"Spawn Interval {spawnIntervalSeconds}s" : "Spawn Interval unavailable";
            return
                $"HP {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}   {intervalLine}   Level {Mathf.Max(1, site.level)}/{Mathf.Max(1, site.maxLevel)}\n" +
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

            if (!site.isBuilt)
                return BuildFocusedBarracksPurchaseStatus(site);

            string text =
                $"Health {Mathf.RoundToInt(site.hp)}/{Mathf.RoundToInt(site.maxHp)}\n" +
                $"{BuildBarracksSpawnSummary(site.roster)}";

            if (!string.IsNullOrWhiteSpace(site.lockedReason))
                text += $"\n{site.lockedReason}";

            return text;
        }

        string BuildFocusedBarracksPurchaseStatus(MLBarracksSite site)
        {
            if (site == null)
                return string.Empty;

            return BuildFocusedBarracksCostText(site);
        }

        string BuildFocusedBarracksPurchaseHint(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
                return "Waiting for barracks data...";

            if (!site.canBuild)
                return BuildFocusedBarracksRequirementText(site);

            if (lane != null && lane.gold < site.buildCost)
                return $"Need {Mathf.Max(0, site.buildCost - Mathf.FloorToInt(lane.gold))} more gold to buy this building.";

            return "Purchase this barracks here to unlock its roster and start its send timer.";
        }

        void SyncFocusedBarracksHeaderAction(MLLaneSnap lane, MLBarracksSite site)
        {
            if (site == null)
            {
                HideHeaderActionButton();
                return;
            }

            if (!site.isBuilt)
            {
                if (!site.canBuild)
                {
                    HideHeaderActionButton();
                    return;
                }

                ConfigureHeaderActionButton(
                    IsPendingBarracksBuild(site)
                        ? $"Buying - {Mathf.Max(0, site.buildCost)}g"
                        : BuildFocusedBarracksBuildLabel(site),
                    () => ExecuteFocusedBarracksBuild(site),
                    !IsPendingBarracksBuild(site) && CanSpendGold(lane, site.buildCost),
                    new Color(0.34f, 0.26f, 0.08f, 0.98f));
                return;
            }

            if (site.canUpgrade)
            {
                ConfigureHeaderActionButton(
                    $"Upgrade {Mathf.Max(0, site.upgradeCost)}g",
                    () => ExecuteFocusedBarracksUpgrade(site),
                    CanSpendGold(lane, site.upgradeCost),
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
            return $"Buy Building - {Mathf.Max(0, site != null ? site.buildCost : 0)}g";
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

        string BuildFocusedPadSummary(MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return string.Empty;

            string unlocks = BuildUnlockPreview(pad, roster, heroRoster);
            return pad.isBuilt
                ? $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}   Unlocks {unlocks}"
                : $"Cost {pad.buildCost}g   Unlocks {unlocks}";
        }

        string BuildFocusedPadHint(MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster, int sendSeconds, int waveSeconds)
        {
            if (pad == null)
                return "Select a fortress pad.";

            string primary = !pad.isBuilt && pad.canBuild
                ? $"Tap Build to construct {pad.buildingName}."
                : pad.canUpgrade
                    ? $"Tap Upgrade to advance {pad.buildingName}."
                    : !string.IsNullOrWhiteSpace(pad.lockedReason)
                        ? pad.lockedReason
                        : $"{pad.buildingName} is ready.";

            string nextPreview = BuildNextUpgradePreview(pad, roster, heroRoster);
            if (!string.IsNullOrWhiteSpace(nextPreview))
                primary += $"  {nextPreview}";

            return $"{primary}  Send {Mathf.Max(0, sendSeconds)}s  Wave {Mathf.Max(0, waveSeconds)}s.";
        }

        string BuildFocusedPadCardBody(MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return "No building pad selected.";

            string text =
                $"Tier {Mathf.Max(0, pad.tier)}/{Mathf.Max(1, pad.maxTier)}   " +
                $"Health {Mathf.RoundToInt(pad.hp)}/{Mathf.RoundToInt(pad.maxHp)}\n" +
                $"Unlocks: {BuildUnlockPreview(pad, roster, heroRoster)}";

            string nextPreview = BuildNextUpgradePreview(pad, roster, heroRoster);
            if (!string.IsNullOrWhiteSpace(nextPreview))
                text += $"\n{nextPreview}";

            if (!string.IsNullOrWhiteSpace(pad.lockedReason))
                text += $"\nRequirement: {pad.lockedReason}";

            return text;
        }

        string BuildNextUpgradePreview(MLFortressPad pad, MLBarracksRosterEntry[] roster, MLHeroRosterEntry[] heroRoster = null)
        {
            if (pad == null)
                return string.Empty;

            if (pad.maxTier <= 0 || pad.tier >= pad.maxTier)
                return "Next Upgrade: Max tier reached.";

            int targetTier = Mathf.Max(1, pad.tier + 1);
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
                case "tower_archer": return 43;
                default: return (pad.gridY * 100) + pad.gridX;
            }
        }

        static Color ResolveBarracksOverviewCardTint(MLBarracksSite site)
        {
            if (site == null)
                return new Color(0.15f, 0.16f, 0.20f, 0.98f);

            if (site.isBuilt)
                return site.canUpgrade
                    ? new Color(0.14f, 0.24f, 0.18f, 0.98f)
                    : new Color(0.12f, 0.18f, 0.24f, 0.98f);

            if (site.canBuild)
                return new Color(0.26f, 0.22f, 0.10f, 0.98f);

            return new Color(0.15f, 0.16f, 0.20f, 0.98f);
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
            if (pad == null)
                return "No unlock data";

            var preview = string.Empty;
            if (roster != null)
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    var entry = roster[i];
                    if (entry == null || !string.Equals(entry.unlockBuildingType, pad.buildingType))
                        continue;

                    if (preview.Length > 0) preview += "  |  ";
                    preview += $"T{entry.requiredBuildingTier}: {entry.displayName}";
                }
            }

            if (heroRoster != null && string.Equals(pad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase))
            {
                var heroNames = new List<string>();
                for (int i = 0; i < heroRoster.Length; i++)
                {
                    var hero = heroRoster[i];
                    if (hero == null)
                        continue;
                    if (!string.Equals(hero.unlockBuildingType, pad.buildingType, System.StringComparison.OrdinalIgnoreCase))
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
                return new Color(0.11f, 0.14f, 0.20f, 0.95f);

            switch (pad.buildState)
            {
                case "available_to_build":
                    return new Color(0.26f, 0.22f, 0.10f, 0.98f);
                case "upgrade_available":
                    return new Color(0.14f, 0.24f, 0.18f, 0.98f);
                case "locked":
                    return new Color(0.15f, 0.16f, 0.20f, 0.98f);
                case "max_tier":
                    return new Color(0.22f, 0.20f, 0.11f, 0.98f);
                default:
                    return new Color(0.12f, 0.18f, 0.24f, 0.98f);
            }
        }

        static string HumanizeBuildState(string value)
        {
            switch (value)
            {
                case "available_to_build": return "Available To Build";
                case "upgrade_available": return "Upgrade Available";
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
                case "left": return "Barracks Left";
                case "right": return "Barracks Right";
                case "center": return "Barracks Center";
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
            tmp.fontSize = fontSize;
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
                return new Color(0.12f, 0.15f, 0.20f, 0.96f);

            if (!entry.unlocked)
                return new Color(0.18f, 0.16f, 0.18f, 0.96f);

            Color baseColor = entry.productionBuildingType switch
            {
                "blacksmith" => new Color(0.27f, 0.18f, 0.14f, 0.98f),
                "temple" => new Color(0.16f, 0.25f, 0.19f, 0.98f),
                "wizard_tower" => new Color(0.18f, 0.18f, 0.31f, 0.98f),
                "archery_tower" => new Color(0.16f, 0.24f, 0.16f, 0.98f),
                _ => new Color(0.12f, 0.15f, 0.22f, 0.96f),
            };

            return entry.ownedCount > 0
                ? Color.Lerp(baseColor, new Color(0.10f, 0.30f, 0.20f, 0.98f), 0.28f)
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
            if (!site.isBuilt)
            {
                CreateFocusedBarracksActionChip(
                    actionRow,
                    BuildFocusedBarracksActionObjectName("BuyBuilding", site.barracksId),
                    "Buy Building",
                    new Color(0.22f, 0.18f, 0.18f, 0.95f),
                    false,
                    null);
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
                    () => RedirectLockedUnitToUnlockBuilding(lane, entry));
                return;
            }

            if (ShouldShowFocusedBarracksBulkBuy(entry))
            {
                CreateFocusedBarracksActionChip(
                    actionRow,
                    BuildFocusedBarracksActionObjectName("BuyTen", entry.rosterKey),
                    BuildFocusedBarracksBulkBuyLabel(lane, site, entry, 10),
                    new Color(0.18f, 0.34f, 0.16f, 0.98f),
                    site.isBuilt && entry.unlocked && CanSpendGold(lane, entry.buyCost * 10),
                    () => ExecuteFocusedBarracksBuy(site, entry, 10));
            }

            CreateFocusedBarracksActionChip(
                actionRow,
                BuildFocusedBarracksActionObjectName("Buy", entry.rosterKey),
                BuildFocusedBarracksBuyLabel(lane, site, entry),
                new Color(0.34f, 0.26f, 0.08f, 0.98f),
                site.isBuilt && entry.unlocked && CanSpendGold(lane, entry.buyCost),
                () => ExecuteFocusedBarracksBuy(site, entry));
            CreateFocusedBarracksActionChip(
                actionRow,
                BuildFocusedBarracksActionObjectName("Sell", entry.rosterKey),
                BuildFocusedBarracksSellLabel(site, entry),
                new Color(0.22f, 0.16f, 0.16f, 0.98f),
                site.isBuilt && entry.ownedCount > 0 && CanEditBarracks(),
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
                ? new Color(0.18f, 0.27f, 0.21f, 0.98f)
                : new Color(0.14f, 0.17f, 0.22f, 0.96f);

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
                return "Buy Building first";

            if (!entry.unlocked)
                return $"Locked - {BuildFocusedBarracksUnlockLabel(entry)}";

            if (lane != null && lane.gold < entry.buyCost)
                return $"Need {Mathf.Max(0, entry.buyCost - Mathf.FloorToInt(lane.gold))}g more";

            int activeCount = CountActiveUnitsForRosterEntry(lane, site, entry);
            return entry.ownedCount > 0 || activeCount > 0
                ? $"Ready to buy | Owned x{Mathf.Max(0, entry.ownedCount)} | Active x{Mathf.Max(0, activeCount)}"
                : "Ready to buy";
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
                return new Color(0.22f, 0.24f, 0.28f, 0.96f);

            if (!site.isBuilt || !entry.unlocked)
                return new Color(0.33f, 0.22f, 0.10f, 0.98f);

            if (lane != null && lane.gold < entry.buyCost)
                return new Color(0.17f, 0.22f, 0.30f, 0.98f);

            return new Color(0.12f, 0.28f, 0.20f, 0.98f);
        }

        Color ResolveFocusedBarracksStateTextColor(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site != null && entry != null && site.isBuilt && entry.unlocked && (lane == null || lane.gold >= entry.buyCost))
                return new Color(0.92f, 1f, 0.92f, 1f);

            return new Color(1f, 0.94f, 0.82f, 1f);
        }

        string BuildFocusedBarracksBuyLabel(MLLaneSnap lane, MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Buy";

            if (!site.isBuilt)
                return "Buy Building";

            if (!entry.unlocked)
                return "Tech Locked";

            if (lane != null && lane.gold < entry.buyCost)
                return $"Need {Mathf.Max(0, entry.buyCost - Mathf.FloorToInt(lane.gold))}g";

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
                return "Buy Building";

            if (!entry.unlocked)
                return "Tech Locked";

            int totalCost = Mathf.Max(0, entry.buyCost * count);
            if (lane != null && lane.gold < totalCost)
                return $"Need {Mathf.Max(0, totalCost - Mathf.FloorToInt(lane.gold))}g";

            return $"Buy x{count} {totalCost}g";
        }

        string BuildFocusedBarracksSellLabel(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return "Sell";

            if (!site.isBuilt)
                return "Locked";

            return entry.ownedCount > 0
                ? $"Sell {entry.sellRefund}g"
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

        void ExecuteFocusedBarracksSell(MLBarracksSite site, MLBarracksRosterEntry entry)
        {
            if (site == null || entry == null)
                return;

            _statusMessage = $"Selling {entry.displayName} from {ResolveBarracksDisplayName(site)}...";
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
            label.fontSizeMin = IsCompactPanelLayout() ? 8f : 8.5f;
            label.fontSizeMax = IsCompactPanelLayout() ? 10f : 11.5f;
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
            tmp.text = text;
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

        void CreateActionButton(
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
                    ? new Color(0.62f, 0.44f, 0.10f, 0.98f)
                    : new Color(0.18f, 0.43f, 0.27f, 0.95f)
                : new Color(0.22f, 0.22f, 0.24f, 0.85f);

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
                    : Color.white
                : new Color(0.72f, 0.72f, 0.72f, 0.95f);
            tmp.text = label;
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
