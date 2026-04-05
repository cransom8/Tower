using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CastleDefender.Game;
using CastleDefender.Net;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CastleDefender.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class MobileMatchHud : MonoBehaviour
    {
        const string RuntimeBarracksButtonName = "RuntimeBuildingOverviewButton";
        const string LegacyRuntimeBarracksButtonName = "RuntimeBarracksButton";
        const string RuntimeBarracksPanelHostName = "RuntimeBuildingOverviewPanelHost";
        const string LegacyRuntimeBarracksPanelHostName = "RuntimeBarracksPanelHost";
        const string ProgressionDockWidgetName = "ProgressionDockWidget";
        const string QuitConfirmationModalName = "QuitConfirmationModal";
        const string WaveOverviewWidgetPrefsKey = "hud.wave_overview_widget.command_deck_v4";
        const string MyStatsWidgetPrefsKey = "hud.my_stats_widget.top_right_v3";
        const string MiniMapWidgetPrefsKey = "hud.minimap_widget.top_right_v2";
        const string WaveTimeIcon = "ETA ";
        const string WaveMobsIcon = "MOB ";
        const string WaveValueShieldIcon = "GOAL ";
        const string WaveValueSwordIcon = "";
        const float HudWidgetMinimumFontSize = 12f;
        const float HudDockGap = 8f;
        const float HudDockSnapThreshold = 32f;
        static readonly string[] WaveBranchBuildingOrder = { "blacksmith", "archery_tower", "temple", "wizard_tower" };

        enum SettingsMenuTab
        {
            General,
            Graphics,
            Sound,
        }

        enum SettingsConfirmationAction
        {
            None,
            Logout,
            QuitGame,
        }

        sealed class BranchStatusView
        {
            public string buildingType;
            public TMP_Text nameLabel;
            public TMP_Text tierLabel;
            public TMP_Text hpLabel;
            public Image hpFill;
        }

        sealed class UpcomingWavePortraitView
        {
            public string entryKey;
            public Button button;
            public Image frame;
            public RawImage portrait;
            public TMP_Text countLabel;
            public TMP_Text nameLabel;
        }

        sealed class UpcomingWaveQueueView
        {
            public int waveNumber;
            public Button button;
            public Image frame;
            public Image icon;
            public TMP_Text waveLabel;
            public TMP_Text countLabel;
            public TMP_Text summaryLabel;
        }

        sealed class SettingsMenuTabView
        {
            public SettingsMenuTab Tab;
            public Button Button;
            public TMP_Text Label;
            public Image Background;
            public Image Accent;
        }

        sealed class SettingsMenuRowView
        {
            public SettingsMenuTab Tab;
            public GameObject Root;
            public Button Button;
            public TMP_Text Eyebrow;
            public TMP_Text Title;
            public TMP_Text Detail;
            public TMP_Text Value;
            public Image Accent;
            public Image Divider;
            public Image ValueFrame;
            public string LabelText;
            public string DetailText;
        }

        [Header("Top Ribbon")]
        [SerializeField] float ribbonHeight = 66f;
        [SerializeField] Vector2 ribbonPadding = new Vector2(10f, 8f);
        [SerializeField] float ribbonTopInset = 8f;
        [SerializeField] float ribbonSpacing = 10f;
        [SerializeField] bool showTopRibbon = false;

        [Header("Top Right Stat Bar")]
        [SerializeField] float statBarWidth = 196f;
        [SerializeField] float statBarHeight = 108f;
        [SerializeField] float statBarTopInset = 8f;
        [SerializeField] float statBarRightInset = 8f;
        [SerializeField] float statBarBottomGap = 8f;
        [SerializeField] bool showTopRightStatBar = false;

        [Header("Right Rail")]
        [SerializeField] float rightRailWidth = 212f;
        [SerializeField] float rightRailTopInset = 78f;
        [SerializeField] float rightRailBottomInset = 18f;
        [SerializeField] float rightRailEdgeInset = 8f;
        [SerializeField] float rightRailSpacing = 6f;
        [SerializeField] Vector2 rightRailPadding = new Vector2(8f, 8f);
        [SerializeField] bool showLegacyRightRail = false;
        [SerializeField] bool myStatsOnlyMode = true;

        [Header("Recommended Build")]
        [SerializeField] float targetBaseValue = 32f;
        [SerializeField] float targetPerWave = 18f;
        [SerializeField] float targetLateWaveBonus = 6f;
        [SerializeField] float healthyThreshold = 1.0f;
        [SerializeField] float cautionThreshold = 0.9f;

        [Header("Phone Scaling")]
        [SerializeField] float tabletFontScale = 1.0f;
        [SerializeField] float phoneFontScale = 0.92f;
        [SerializeField] float compactPhoneFontScale = 0.84f;
        [SerializeField] float phoneWidthThreshold = 900f;
        [SerializeField] float compactPhoneWidthThreshold = 720f;

        [Header("Wave Status Widget")]
        [SerializeField] bool showWaveStatusWidget = true;

        [Header("Mini Map Widget")]
        [SerializeField] bool showMiniMapWidget = true;

        [Header("Settings Panel")]
        [SerializeField] bool showSettingsPanel = true;
        [SerializeField] float settingsButtonSize = 46f;
        [SerializeField] Vector2 settingsPanelSize = new Vector2(164f, 178f);
        [SerializeField] float settingsTopInset = 14f;
        [SerializeField] float settingsRightInset = 10f;
        [SerializeField] float settingsPanelGap = 10f;
        [SerializeField] float settingsButtonSpacing = 6f;
        [SerializeField] float settingsValueWidth = 32f;
        [SerializeField] float settingsZoomStep = 2f;
        [SerializeField] float settingsTiltStep = 8f;
        [SerializeField] float settingsRotateStep = 20f;

        [Header("Editor Preview")]
        [SerializeField] bool previewInEditMode = true;
        [SerializeField] int previewWave = 7;
        [SerializeField] string previewPhase = "LIVE";
        [SerializeField] float previewCountdown = 14f;
        [SerializeField] float previewBuild = 148f;
        [SerializeField] float previewGold = 96f;
        [SerializeField] float previewIncome = 10f;
        [SerializeField] float previewForge = 0f;
        [SerializeField] int previewForgeWorkers = 10;

        RectTransform _canvasRect;
        RectTransform _ribbonRoot;
        RectTransform _rightRailRoot;
        RectTransform _topRightStatsRoot;
        RectTransform _settingsPanelRoot;
        Rect _lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        Vector2 _lastCanvasSize = new(-1f, -1f);
        bool _built;
        bool _editorRebuildQueued;

        TMP_Text _txtRound;
        TMP_Text _txtPhase;
        TMP_Text _txtCountdown;
        TMP_Text _txtGoldTop;
        TMP_Text _txtIncomeTop;
        TMP_Text _txtWavePreview;
        TMP_Text _txtTeamHpLeft;
        TMP_Text _txtTeamHpRight;
        Image _barTeamHpLeft;
        Image _barTeamHpRight;

        TMP_Text _txtRecommendedHeadline;
        TMP_Text _txtRecommendedDetail;
        Image _recommendedFill;
        TMP_Text _txtForgeTop;
        TMP_Text _txtWorkersTop;
        TMP_Text _txtBuildStatTop;
        TMP_Text _txtBuildDeltaTop;
        Image _buildStatFill;

        TMP_Text _teamStatsText;
        TMP_Text _playerStatsText;
        TMP_Text _waveIntelText;
        MyStatsHudWidget _myStatsWidget;
        BattlefieldMiniMapWidget _miniMapWidget;
        DraggableHudPanel _miniMapDockPanel;
        DraggableHudPanel _waveOverviewWidget;
        DraggableHudPanel _progressionDockWidget;
        GameObject _settingsOverlay;
        RectTransform _settingsOverlayPanelRoot;
        Button _settingsMenuButton;
        TMP_Text _settingsMenuButtonLabel;
        TMP_Text _settingsOverlaySubtitle;
        TMP_Text _settingsInfoCategoryValue;
        TMP_Text _settingsInfoDescriptionValue;
        TMP_Text _settingsInfoPresetValue;
        TMP_Text _txtSettingsTiltValue;
        TMP_Text _txtSettingsZoomValue;
        TMP_Text _txtSettingsRotationValue;
        TMP_Text _txtSettingsSfxValue;
        TMP_Text _txtSettingsMusicValue;
        TMP_Text _txtSettingsEngagementValue;
        TMP_Text _txtSettingsHealthBarsValue;
        TMP_Text _txtSettingsTooltipsValue;
        TMP_Text _txtBarracksLevel;
        TMP_Text _progressionDockStatus;
        BarracksPanel _runtimeBarracksPanel;
        Button _runtimeBarracksButton;
        Button _startWaveButton;
        TMP_Text _startWaveButtonLabel;
        TMP_Text _waveOverviewTitle;
        TMP_Text _waveOverviewPhase;
        TMP_Text _waveOverviewReady;
        TMP_Text _waveOverviewCollapsedLabel;
        TMP_Text _waveQueueEmptyLabel;
        RectTransform _waveQueueStrip;
        GameObject _upcomingWavePopup;
        TMP_Text _upcomingWavePopupTitle;
        TMP_Text _upcomingWavePopupSubtitle;
        TMP_Text _wavePortraitEmptyLabel;
        RectTransform _wavePortraitStrip;
        GameObject _waveDetailPanel;
        RawImage _waveDetailPortrait;
        TMP_Text _waveDetailTitle;
        TMP_Text _waveDetailSummary;
        TMP_Text _waveDetailStats;
        TMP_Text _waveDetailSource;
        GameObject _quitConfirmationModal;
        Button _quitConfirmationConfirmButton;
        Button _quitConfirmationCancelButton;
        TMP_Text _quitConfirmationConfirmLabel;
        TMP_Text _quitConfirmationTitleLabel;
        TMP_Text _quitConfirmationBodyLabel;
        bool _isLeavingMatch;
        bool _myStatsDockedToMiniMap;
        bool _waveOverviewDockedToMyStats;
        SettingsMenuTab _activeSettingsTab = SettingsMenuTab.General;
        SettingsMenuRowView _selectedSettingsRow;
        SettingsConfirmationAction _pendingSettingsConfirmationAction;

        CollapsibleHudCard _teamStatsCard;
        CollapsibleHudCard _playerStatsCard;
        CollapsibleHudCard _waveIntelCard;

        readonly Dictionary<string, UnitCatalogEntry> _catalogByKey = new();
        readonly Dictionary<string, BranchStatusView> _branchStatusViews = new(StringComparer.OrdinalIgnoreCase);
        readonly List<UpcomingWaveQueueView> _upcomingWaveQueueViews = new();
        readonly List<UpcomingWavePortraitView> _upcomingWavePortraitViews = new();
        readonly List<SettingsMenuTabView> _settingsTabViews = new();
        readonly List<SettingsMenuRowView> _settingsRowViews = new();
        readonly HashSet<string> _missingUpcomingWavePortraitLogs = new(StringComparer.OrdinalIgnoreCase);
        Coroutine _wavePortraitLoadCoroutine;
        string _lastUpcomingWaveQueueSignature;
        string _lastUpcomingWavePortraitSignature;
        int _selectedUpcomingWaveNumber = -1;
        string _selectedUpcomingWaveEntryKey;

        void Start()
        {
            PostWinFlowUI.EnsureInstance();
            RebuildHud();
        }

        void OnEnable()
        {
            if (!Application.isPlaying && previewInEditMode)
                QueueEditorRebuild();
        }

        void OnValidate()
        {
            if (!Application.isPlaying && previewInEditMode)
                QueueEditorRebuild();
        }

        void Update()
        {
            if (!_built)
                RebuildHud();

            ApplySafeAreaLayout();
            if (Application.isPlaying)
                RefreshHud();
            else if (previewInEditMode)
                RefreshPreviewHud();

            RefreshSettingsPanelValues();
        }

        void RebuildHud()
        {
            _editorRebuildQueued = false;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return;

            _canvasRect = canvas.GetComponent<RectTransform>();
            CacheCatalog();
            DestroyLegacyHudPanels();
            if (!myStatsOnlyMode && showTopRibbon)
                BuildTopRibbon();
            else
                ClearTopRibbon();

            if (!myStatsOnlyMode && showTopRightStatBar)
                BuildTopRightStatBar();
            else
                DestroyCanvasChildren("TopRightStatBar");
            BuildMyStatsWidget();
            if (showWaveStatusWidget)
                BuildWaveStatusWidget();
            else
                DestroyCanvasChildren("WaveStatusWidget");
            if (showMiniMapWidget)
                BuildMiniMapWidget();
            else
                DestroyCanvasChildren("MiniMapWidget");
            if (showSettingsPanel)
            {
                BuildSettingsPanel();
                BuildQuitConfirmationModal();
            }
            else
            {
                DestroyCanvasChildren("SettingsPanel");
                DestroyCanvasChildren(QuitConfirmationModalName);
            }
            EnsureBarracksAccess();
            if (!myStatsOnlyMode && showLegacyRightRail)
                BuildRightRail();
            else
                DestroyCanvasChildren("RightHudRail");
            if (!myStatsOnlyMode)
                BindLegacyHudRefs();
            BindHudDockCallbacks();
            ForceApplySafeAreaLayout();
            _built = true;

            if (Application.isPlaying)
                RefreshHud();
            else if (previewInEditMode)
                RefreshPreviewHud();
        }

        public void ForceRebuildNow()
        {
            _built = false;
            RebuildHud();
        }

        void QueueEditorRebuild()
        {
            _built = false;
#if UNITY_EDITOR
            if (_editorRebuildQueued)
                return;

            _editorRebuildQueued = true;
            EditorApplication.delayCall += DelayedEditorRebuild;
#endif
        }

#if UNITY_EDITOR
        void DelayedEditorRebuild()
        {
            EditorApplication.delayCall -= DelayedEditorRebuild;
            if (this == null || Application.isPlaying || !previewInEditMode)
            {
                _editorRebuildQueued = false;
                return;
            }

            RebuildHud();
        }
#endif

        void DestroyLegacyHudPanels()
        {
            if (_wavePortraitLoadCoroutine != null)
            {
                StopCoroutine(_wavePortraitLoadCoroutine);
                _wavePortraitLoadCoroutine = null;
            }

            DestroyCanvasChildren("TopRightStatBar");
            DestroyCanvasChildren("RightHudRail");
            DestroyCanvasChildren("MyStatsWidget");
            DestroyCanvasChildren("WaveStatusWidget");
            DestroyCanvasChildren("MiniMapWidget");
            DestroyCanvasChildren(ProgressionDockWidgetName);
            DestroyCanvasChildren(RuntimeBarracksButtonName);
            DestroyCanvasChildren(LegacyRuntimeBarracksButtonName);
            DestroyCanvasChildren(QuitConfirmationModalName);
            DestroyCanvasChildren("UpcomingWavePopup");
            DestroyCanvasChildren("WaveHUD(Clone)");
            _quitConfirmationModal = null;
            _quitConfirmationConfirmButton = null;
            _quitConfirmationCancelButton = null;
            _quitConfirmationConfirmLabel = null;
            _miniMapWidget = null;
            _miniMapDockPanel = null;
            _waveOverviewWidget = null;
            _progressionDockWidget = null;
            _runtimeBarracksButton = null;
            _startWaveButton = null;
            _startWaveButtonLabel = null;
            _waveOverviewTitle = null;
            _waveOverviewPhase = null;
            _waveOverviewReady = null;
            _waveOverviewCollapsedLabel = null;
            _waveQueueEmptyLabel = null;
            _waveQueueStrip = null;
            _upcomingWavePopup = null;
            _upcomingWavePopupTitle = null;
            _upcomingWavePopupSubtitle = null;
            _wavePortraitEmptyLabel = null;
            _wavePortraitStrip = null;
            _waveDetailPanel = null;
            _waveDetailPortrait = null;
            _waveDetailTitle = null;
            _waveDetailSummary = null;
            _waveDetailStats = null;
            _waveDetailSource = null;
            _progressionDockStatus = null;
            _txtBarracksLevel = null;
            _branchStatusViews.Clear();
            _upcomingWaveQueueViews.Clear();
            _upcomingWavePortraitViews.Clear();
            _missingUpcomingWavePortraitLogs.Clear();
            _lastUpcomingWaveQueueSignature = null;
            _lastUpcomingWavePortraitSignature = null;
            _selectedUpcomingWaveNumber = -1;
            _selectedUpcomingWaveEntryKey = null;
            _isLeavingMatch = false;
            _myStatsDockedToMiniMap = false;
            _waveOverviewDockedToMyStats = false;

            var topRibbon = transform.Find("WaveModule");
            if (topRibbon != null)
                ClearTopRibbon();

            var infoBar = GetComponent<InfoBar>();
            if (infoBar != null)
                infoBar.enabled = false;
        }

        void CacheCatalog()
        {
            _catalogByKey.Clear();
            if (CatalogLoader.Units == null)
                return;

            for (int i = 0; i < CatalogLoader.Units.Count; i++)
            {
                var entry = CatalogLoader.Units[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                    continue;
                _catalogByKey[entry.key] = entry;
            }
        }

        void BuildTopRibbon()
        {
            ClearChildren(transform);

            var root = gameObject;
            root.name = "WaveHUD";

            var rootRect = root.GetComponent<RectTransform>();
            if (rootRect == null)
                rootRect = root.AddComponent<RectTransform>();

            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -ribbonTopInset);
            rootRect.offsetMin = new Vector2(12f, -ribbonHeight);
            rootRect.offsetMax = new Vector2(-12f, 0f);
            rootRect.sizeDelta = new Vector2(0f, ribbonHeight);

            var image = root.GetComponent<Image>();
            if (image == null)
                image = root.AddComponent<Image>();
            image.color = new Color(0.01f, 0.02f, 0.04f, 0.18f);

            var layout = root.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
                layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.spacing = ribbonSpacing;
            layout.padding = new RectOffset(
                Mathf.RoundToInt(ribbonPadding.x),
                Mathf.RoundToInt(ribbonPadding.x),
                Mathf.RoundToInt(ribbonPadding.y),
                Mathf.RoundToInt(ribbonPadding.y));

            _ribbonRoot = rootRect;
            ApplySafeAreaLayout();

            var waveModule = CreateRibbonModule("WaveModule", 176f, new Color(0.09f, 0.13f, 0.22f, 0.92f), new Color(0.32f, 0.76f, 1f, 0.95f));
            _txtRound = CreateValueLabel(waveModule, "Txt_Round", "Wave 1", 22, TextAlignmentOptions.Left);
            _txtPhase = CreateValueLabel(waveModule, "Txt_Phase", "LIVE", 16, TextAlignmentOptions.Left, new Color(0.38f, 1f, 0.55f));
            _txtCountdown = CreateValueLabel(waveModule, "Txt_Countdown", "30s", 16, TextAlignmentOptions.Left, new Color(1f, 0.89f, 0.34f));

            var buildModule = CreateRibbonModule("RecommendedBuildModule", 212f, new Color(0.10f, 0.14f, 0.16f, 0.94f), new Color(0.78f, 0.48f, 0.18f, 0.98f));
            CreateCaptionLabel(buildModule, "Recommended Build");
            _txtRecommendedHeadline = CreateValueLabel(buildModule, "Txt_RecommendedBuildHeadline", "Build 0 / 0", 22, TextAlignmentOptions.Center);
            _txtRecommendedDetail = CreateValueLabel(buildModule, "Txt_RecommendedBuildDetail", "Goal pending", 14, TextAlignmentOptions.Center, new Color(0.85f, 0.88f, 0.93f, 0.92f));
            _recommendedFill = CreateProgressBar(buildModule, "RecommendedBuildBar", new Color(0.34f, 0.86f, 0.48f, 1f));

            var statusModule = CreateRibbonModule("MatchStatusModule", 290f, new Color(0.12f, 0.12f, 0.14f, 0.94f), new Color(0.88f, 0.84f, 0.36f, 0.98f));
            _txtTeamHpLeft = CreateValueLabel(statusModule, "Txt_TeamHpLeft", "Left Side 20/20", 16, TextAlignmentOptions.Left, new Color(1f, 0.90f, 0.24f));
            _barTeamHpLeft = CreateProgressBar(statusModule, "Bar_TeamHpLeft", new Color(1f, 0.78f, 0.18f, 1f));
            _txtTeamHpRight = CreateValueLabel(statusModule, "Txt_TeamHpRight", "Right Side 20/20", 16, TextAlignmentOptions.Left, new Color(0.58f, 0.86f, 1f, 1f));
            _barTeamHpRight = CreateProgressBar(statusModule, "Bar_TeamHpRight", new Color(0.34f, 0.84f, 1f, 1f));

        }

        void ClearTopRibbon()
        {
            ClearChildren(transform);

            var rootRect = GetComponent<RectTransform>();
            if (rootRect == null)
                rootRect = gameObject.AddComponent<RectTransform>();

            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -ribbonTopInset);
            rootRect.offsetMin = new Vector2(0f, 0f);
            rootRect.offsetMax = new Vector2(0f, 0f);
            rootRect.sizeDelta = Vector2.zero;

            var image = GetComponent<Image>();
            if (image != null)
                image.color = new Color(0f, 0f, 0f, 0f);

            var layout = GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;
        }

        void BuildTopRightStatBar()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("TopRightStatBar");

            var root = new GameObject("TopRightStatBar", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            root.transform.SetParent(_canvasRect, false);
            _topRightStatsRoot = root.GetComponent<RectTransform>();
            _topRightStatsRoot.anchorMin = new Vector2(1f, 1f);
            _topRightStatsRoot.anchorMax = new Vector2(1f, 1f);
            _topRightStatsRoot.pivot = new Vector2(1f, 1f);
            _topRightStatsRoot.sizeDelta = new Vector2(statBarWidth, statBarHeight);
            _topRightStatsRoot.anchoredPosition = new Vector2(-statBarRightInset, -statBarTopInset);

            var image = root.GetComponent<Image>();
            image.color = new Color(0.08f, 0.10f, 0.14f, 0.94f);
            ApplyPanelFrame(root, image.color, new Color(0.86f, 0.62f, 0.22f, 0.98f));

            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(10, 10, 10, 8);

            var waveHeader = new GameObject("WaveHeader", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            waveHeader.transform.SetParent(root.transform, false);
            var waveHeaderLayout = waveHeader.GetComponent<VerticalLayoutGroup>();
            waveHeaderLayout.childAlignment = TextAnchor.UpperLeft;
            waveHeaderLayout.childControlWidth = true;
            waveHeaderLayout.childControlHeight = false;
            waveHeaderLayout.childForceExpandWidth = true;
            waveHeaderLayout.childForceExpandHeight = false;
            waveHeaderLayout.spacing = 1f;
            waveHeaderLayout.padding = new RectOffset(0, 0, 0, 0);
            waveHeader.GetComponent<LayoutElement>().preferredHeight = 28f;

            CreateCaptionLabel(waveHeader.transform, "Wave Strip");
            _txtWavePreview = CreateValueLabel(waveHeader.transform, "Txt_WavePreview", "NOW -- | NXT --", 11, TextAlignmentOptions.Left, new Color(0.94f, 0.90f, 0.82f, 1f));

            var topRows = new GameObject("TopRows", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            topRows.transform.SetParent(root.transform, false);
            var topRowsLayout = topRows.GetComponent<HorizontalLayoutGroup>();
            topRowsLayout.childAlignment = TextAnchor.MiddleCenter;
            topRowsLayout.childControlWidth = true;
            topRowsLayout.childControlHeight = true;
            topRowsLayout.childForceExpandWidth = true;
            topRowsLayout.childForceExpandHeight = false;
            topRowsLayout.spacing = 6f;
            var topRowsElement = topRows.GetComponent<LayoutElement>();
            topRowsElement.preferredHeight = 42f;

            CreateMiniStat(topRows.transform, "GoldStat", "GLD", new Color(0.96f, 0.78f, 0.26f, 1f), out _txtGoldTop);
            CreateMiniStat(topRows.transform, "IncomeStat", "INC", new Color(0.34f, 0.88f, 0.96f, 1f), out _txtIncomeTop);
            CreateMiniStat(topRows.transform, "ForgeStat", "FGE", new Color(0.92f, 0.52f, 0.24f, 1f), out _txtForgeTop);
            CreateMiniStat(topRows.transform, "WorkerStat", "WRK", new Color(0.84f, 0.86f, 0.92f, 1f), out _txtWorkersTop);

            var buildBlock = new GameObject("BuildStatBlock", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            buildBlock.transform.SetParent(root.transform, false);
            var buildLayout = buildBlock.GetComponent<VerticalLayoutGroup>();
            buildLayout.childAlignment = TextAnchor.UpperCenter;
            buildLayout.childControlWidth = true;
            buildLayout.childControlHeight = false;
            buildLayout.childForceExpandWidth = true;
            buildLayout.childForceExpandHeight = false;
            buildLayout.spacing = 3f;
            buildLayout.padding = new RectOffset(0, 0, 0, 0);
            buildBlock.GetComponent<LayoutElement>().preferredHeight = 44f;

            var buildHeader = new GameObject("BuildHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buildHeader.transform.SetParent(buildBlock.transform, false);
            var buildHeaderLayout = buildHeader.GetComponent<HorizontalLayoutGroup>();
            buildHeaderLayout.childAlignment = TextAnchor.MiddleCenter;
            buildHeaderLayout.childControlWidth = true;
            buildHeaderLayout.childControlHeight = true;
            buildHeaderLayout.childForceExpandWidth = false;
            buildHeaderLayout.childForceExpandHeight = false;
            buildHeaderLayout.spacing = 8f;
            buildHeader.GetComponent<LayoutElement>().preferredHeight = 18f;

            _txtBuildStatTop = CreateText(buildHeader.transform, "BuildValue", "BLD 0", 15, TextAlignmentOptions.Left, new Color(0.86f, 0.92f, 1f, 1f));
            _txtBuildDeltaTop = CreateText(buildHeader.transform, "BuildDelta", "+0", 15, TextAlignmentOptions.Right, new Color(0.34f, 0.88f, 0.46f, 1f));

            var buildMeter = new GameObject("BuildMeter", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            buildMeter.transform.SetParent(buildBlock.transform, false);
            buildMeter.GetComponent<LayoutElement>().preferredHeight = 14f;
            buildMeter.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
            _buildStatFill = CreateSegmentedBar(buildMeter.transform, "BuildMeterFill", new Color(0.34f, 0.88f, 0.46f, 1f));
        }

        void BuildRightRail()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("RightHudRail");

            var rail = new GameObject("RightHudRail", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(VerticalLayoutGroup));
            rail.transform.SetParent(_canvasRect, false);
            _rightRailRoot = rail.GetComponent<RectTransform>();
            _rightRailRoot.anchorMin = new Vector2(1f, 0f);
            _rightRailRoot.anchorMax = new Vector2(1f, 1f);
            _rightRailRoot.pivot = new Vector2(1f, 1f);
            _rightRailRoot.offsetMin = new Vector2(-rightRailWidth, rightRailBottomInset);
            _rightRailRoot.offsetMax = new Vector2(-rightRailEdgeInset, -GetEffectiveRightRailTopInset());

            var railImage = rail.GetComponent<Image>();
            railImage.color = new Color(0f, 0f, 0f, 0f);
            railImage.raycastTarget = false;

            var layout = rail.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = rightRailSpacing;
            layout.padding = new RectOffset(
                Mathf.RoundToInt(rightRailPadding.x),
                Mathf.RoundToInt(rightRailPadding.x),
                Mathf.RoundToInt(rightRailPadding.y),
                Mathf.RoundToInt(rightRailPadding.y));

            _teamStatsCard = CreateStatsCard(rail.transform, "TeamStatsCard", "Team Stats", true, 134f, new Color(0.15f, 0.13f, 0.11f, 0.95f), new Color(0.88f, 0.76f, 0.28f, 0.98f), out _teamStatsText);
            _playerStatsCard = CreateStatsCard(rail.transform, "PlayerStatsCard", "Player Stats", true, 170f, new Color(0.11f, 0.12f, 0.17f, 0.95f), new Color(0.44f, 0.68f, 1f, 0.98f), out _playerStatsText);
            _waveIntelCard = CreateStatsCard(rail.transform, "WaveIntelCard", "Wave Intel", true, 128f, new Color(0.16f, 0.14f, 0.10f, 0.95f), new Color(0.92f, 0.58f, 0.24f, 0.98f), out _waveIntelText);

            ApplySafeAreaLayout();
        }

        void BuildMyStatsWidget()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("MyStatsWidget");

            var root = new GameObject("MyStatsWidget", typeof(RectTransform), typeof(Image), typeof(MyStatsHudWidget));
            root.transform.SetParent(_canvasRect, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(220f, 80f);
            rect.anchoredPosition = new Vector2(Mathf.Max(12f, _canvasRect.rect.width - 276f), -96f);

            var panelImage = root.GetComponent<Image>();
            panelImage.color = new Color(0.09f, 0.13f, 0.16f, 0.96f);
            ApplyPanelFrame(root, panelImage.color, new Color(0.25f, 0.42f, 0.48f, 0.98f));

            var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggle.transform.SetParent(root.transform, false);
            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.sizeDelta = new Vector2(22f, 22f);
            toggleRect.anchoredPosition = new Vector2(-6f, -6f);
            toggle.GetComponent<Image>().color = new Color(0.15f, 0.21f, 0.24f, 0.95f);
            var toggleLabel = CreateText(toggle.transform, "Label", "-", 13, TextAlignmentOptions.Center, Color.white);
            var toggleLabelRect = toggleLabel.rectTransform;
            toggleLabelRect.anchorMin = Vector2.zero;
            toggleLabelRect.anchorMax = Vector2.one;
            toggleLabelRect.offsetMin = Vector2.zero;
            toggleLabelRect.offsetMax = Vector2.zero;

            var body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup));
            body.transform.SetParent(root.transform, false);
            var bodyRect = body.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(8f, 6f);
            bodyRect.offsetMax = new Vector2(-8f, -4f);
            var bodyLayout = body.GetComponent<VerticalLayoutGroup>();
            bodyLayout.childAlignment = TextAnchor.UpperCenter;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;
            bodyLayout.spacing = 4f;
            bodyLayout.padding = new RectOffset(0, 0, 0, 0);

            var rows = new GameObject("StatRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            rows.transform.SetParent(body.transform, false);
            var rowsLayout = rows.GetComponent<VerticalLayoutGroup>();
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.spacing = 3f;
            rowsLayout.padding = new RectOffset(0, 0, 0, 0);
            var rowsLayoutElement = rows.GetComponent<LayoutElement>();
            rowsLayoutElement.preferredHeight = 60f;
            rowsLayoutElement.flexibleHeight = 0f;

            CreateLinkedStatPair(rows.transform, "GoldIncomeRow",
                new Color(0.95f, 0.74f, 0.28f, 1f), "GLD", out var goldValue,
                new Color(0.48f, 0.90f, 0.95f, 1f), "INC", out var incomeValue);

            CreateLinkedStatPair(rows.transform, "SecondaryWorkersRow",
                new Color(0.98f, 0.79f, 0.24f, 1f), "FOR", out var secondaryValue,
                new Color(0.82f, 0.86f, 0.93f, 1f), "WRK", out var workersValue);

            CreateLinkedStatPair(rows.transform, "BuildTargetRow",
                new Color(0.43f, 0.82f, 1f, 1f), "BLD", out var buildValue,
                new Color(0.98f, 0.30f, 0.24f, 1f), "GOAL", out var targetValue);

            var meter = new GameObject("Meter", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            meter.transform.SetParent(body.transform, false);
            meter.GetComponent<Image>().color = new Color(0.11f, 0.17f, 0.20f, 1f);
            var meterLayoutElement = meter.GetComponent<LayoutElement>();
            meterLayoutElement.preferredHeight = 3f;
            meterLayoutElement.minHeight = 3f;
            meterLayoutElement.flexibleHeight = 0f;

            var meterFill = CreateSegmentedBar(meter.transform, "MeterFill", new Color(1f, 0.43f, 0.19f, 1f));

            var badge = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(meter.transform, false);
            var badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0.5f, 0.5f);
            badgeRect.anchorMax = new Vector2(0.5f, 0.5f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(10f, 10f);
            badgeRect.anchoredPosition = new Vector2(0f, 0f);
            badge.GetComponent<Image>().color = new Color(0.13f, 0.28f, 0.18f, 1f);

            var badgeGlow = new GameObject("BadgeGlow", typeof(RectTransform), typeof(Image));
            badgeGlow.transform.SetParent(badge.transform, false);
            var badgeGlowRect = badgeGlow.GetComponent<RectTransform>();
            badgeGlowRect.anchorMin = new Vector2(0.18f, 0.18f);
            badgeGlowRect.anchorMax = new Vector2(0.82f, 0.82f);
            badgeGlowRect.offsetMin = Vector2.zero;
            badgeGlowRect.offsetMax = Vector2.zero;
            var badgeGlowImage = badgeGlow.GetComponent<Image>();
            badgeGlowImage.color = new Color(0.42f, 0.92f, 0.36f, 1f);

            var collapsed = new GameObject("CollapsedView", typeof(RectTransform), typeof(Image));
            collapsed.transform.SetParent(root.transform, false);
            var collapsedRect = collapsed.GetComponent<RectTransform>();
            collapsedRect.anchorMin = Vector2.zero;
            collapsedRect.anchorMax = Vector2.one;
            collapsedRect.offsetMin = new Vector2(10f, 10f);
            collapsedRect.offsetMax = new Vector2(-10f, -10f);
            collapsed.GetComponent<Image>().color = new Color(0.11f, 0.16f, 0.19f, 1f);
            collapsed.SetActive(false);

            var collapsedGem = new GameObject("CollapsedGem", typeof(RectTransform), typeof(Image));
            collapsedGem.transform.SetParent(collapsed.transform, false);
            var collapsedGemRect = collapsedGem.GetComponent<RectTransform>();
            collapsedGemRect.anchorMin = new Vector2(0.5f, 0.5f);
            collapsedGemRect.anchorMax = new Vector2(0.5f, 0.5f);
            collapsedGemRect.pivot = new Vector2(0.5f, 0.5f);
            collapsedGemRect.sizeDelta = new Vector2(22f, 22f);
            collapsedGemRect.anchoredPosition = new Vector2(0f, -4f);
            collapsedGem.GetComponent<Image>().color = new Color(0.20f, 0.58f, 0.24f, 1f);

            var collapsedLabel = CreateText(collapsed.transform, "CollapsedLabel", "MY", 14, TextAlignmentOptions.Center, new Color(0.94f, 0.95f, 0.98f, 1f));
            var collapsedLabelRect = collapsedLabel.rectTransform;
            collapsedLabelRect.anchorMin = new Vector2(0f, 1f);
            collapsedLabelRect.anchorMax = new Vector2(1f, 1f);
            collapsedLabelRect.pivot = new Vector2(0.5f, 1f);
            collapsedLabelRect.sizeDelta = new Vector2(0f, 18f);
            collapsedLabelRect.anchoredPosition = new Vector2(0f, -2f);

            _myStatsWidget = root.GetComponent<MyStatsHudWidget>();
            _myStatsWidget.Configure(
                _canvasRect.GetComponentInParent<Canvas>(),
                rect,
                bodyRect,
                collapsedRect,
                toggle.GetComponent<Button>(),
                toggleLabel,
                goldValue,
                incomeValue,
                secondaryValue,
                workersValue,
                buildValue,
                targetValue,
                meterFill,
                badgeGlowImage,
                false,
                MyStatsWidgetPrefsKey);

            toggle.transform.SetAsLastSibling();
        }

        void BuildWaveStatusWidget()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("WaveStatusWidget");
            _branchStatusViews.Clear();
            _upcomingWaveQueueViews.Clear();
            _upcomingWavePortraitViews.Clear();
            _waveQueueStrip = null;
            _waveQueueEmptyLabel = null;

            var root = new GameObject("WaveStatusWidget", typeof(RectTransform), typeof(Image), typeof(DraggableHudPanel));
            root.transform.SetParent(_canvasRect, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(960f, 82f);
            rect.anchoredPosition = new Vector2(226f, -10f);

            var panelImage = root.GetComponent<Image>();
            panelImage.color = new Color(0.08f, 0.11f, 0.15f, 0.94f);
            ApplyPanelFrame(root, panelImage.color, new Color(0.34f, 0.78f, 0.98f, 0.92f));

            var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggle.transform.SetParent(root.transform, false);
            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.sizeDelta = new Vector2(26f, 26f);
            toggleRect.anchoredPosition = new Vector2(-6f, -6f);
            toggle.GetComponent<Image>().color = new Color(0.14f, 0.18f, 0.22f, 0.96f);
            var toggleLabel = CreateText(toggle.transform, "Label", "-", 13, TextAlignmentOptions.Center, Color.white);
            toggleLabel.rectTransform.anchorMin = Vector2.zero;
            toggleLabel.rectTransform.anchorMax = Vector2.one;
            toggleLabel.rectTransform.offsetMin = Vector2.zero;
            toggleLabel.rectTransform.offsetMax = Vector2.zero;

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(root.transform, false);
            var bodyRect = body.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(6f, 6f);
            bodyRect.offsetMax = new Vector2(-6f, -6f);

            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(body.transform, false);
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 0f);
            headerRect.anchorMax = new Vector2(0f, 1f);
            headerRect.pivot = new Vector2(0f, 0.5f);
            headerRect.sizeDelta = new Vector2(308f, 0f);
            headerRect.anchoredPosition = Vector2.zero;
            var headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.10f, 0.14f, 0.19f, 0.96f);
            ApplyPanelFrame(header, headerImage.color, new Color(0.42f, 0.82f, 1f, 0.94f));

            _waveOverviewTitle = CreateText(header.transform, "WaveLabel", "WAVE W1", 17, TextAlignmentOptions.Left, Color.white);
            _waveOverviewTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _waveOverviewTitle.rectTransform.anchorMax = new Vector2(0f, 1f);
            _waveOverviewTitle.rectTransform.pivot = new Vector2(0f, 1f);
            _waveOverviewTitle.rectTransform.sizeDelta = new Vector2(96f, 24f);
            _waveOverviewTitle.rectTransform.anchoredPosition = new Vector2(12f, -6f);
            ConfigureSingleLineText(_waveOverviewTitle);

            _waveOverviewPhase = CreateText(header.transform, "PhaseLabel", "--", 14, TextAlignmentOptions.Left, new Color(0.84f, 0.90f, 0.97f, 0.94f));
            EnsureMinimumFontSize(_waveOverviewPhase);
            ConfigureSingleLineText(_waveOverviewPhase);
            _waveOverviewPhase.rectTransform.anchorMin = new Vector2(0f, 0f);
            _waveOverviewPhase.rectTransform.anchorMax = new Vector2(1f, 0f);
            _waveOverviewPhase.rectTransform.pivot = new Vector2(0f, 0f);
            _waveOverviewPhase.rectTransform.offsetMin = new Vector2(12f, 7f);
            _waveOverviewPhase.rectTransform.offsetMax = new Vector2(-10f, 25f);

            _startWaveButton = CreateHudActionButton(header.transform, "StartWaveButton", "Start Wave", new Color(0.18f, 0.34f, 0.22f, 0.98f), out _startWaveButtonLabel);
            var startWaveRect = _startWaveButton.GetComponent<RectTransform>();
            ConfigureSingleLineText(_startWaveButtonLabel);
            startWaveRect.anchorMin = new Vector2(0f, 1f);
            startWaveRect.anchorMax = new Vector2(0f, 1f);
            startWaveRect.pivot = new Vector2(0f, 1f);
            startWaveRect.sizeDelta = new Vector2(104f, 24f);
            startWaveRect.anchoredPosition = new Vector2(108f, -8f);
            _startWaveButton.onClick.RemoveAllListeners();
            _startWaveButton.onClick.AddListener(OnStartWavePressed);

            var statusBar = new GameObject("StatusBar", typeof(RectTransform), typeof(Image));
            statusBar.transform.SetParent(body.transform, false);
            var statusBarRect = statusBar.GetComponent<RectTransform>();
            statusBarRect.anchorMin = new Vector2(0f, 0f);
            statusBarRect.anchorMax = new Vector2(0f, 1f);
            statusBarRect.pivot = new Vector2(0f, 0.5f);
            statusBarRect.sizeDelta = new Vector2(0f, 0f);
            statusBarRect.anchoredPosition = new Vector2(308f, 0f);
            statusBarRect.offsetMin = new Vector2(6f, 0f);
            statusBarRect.offsetMax = new Vector2(-6f, 0f);
            var statusBarImage = statusBar.GetComponent<Image>();
            statusBarImage.color = new Color(0.08f, 0.11f, 0.16f, 0.92f);
            ApplyPanelFrame(statusBar, statusBarImage.color, new Color(0.20f, 0.56f, 0.80f, 0.78f));

            var statusHeader = CreateText(statusBar.transform, "StatusHeader", "MOB COUNT", 10, TextAlignmentOptions.Left, new Color(0.80f, 0.88f, 0.96f, 0.88f));
            ConfigureSingleLineText(statusHeader);
            statusHeader.rectTransform.anchorMin = new Vector2(0f, 1f);
            statusHeader.rectTransform.anchorMax = new Vector2(1f, 1f);
            statusHeader.rectTransform.pivot = new Vector2(0f, 1f);
            statusHeader.rectTransform.sizeDelta = new Vector2(-12f, 14f);
            statusHeader.rectTransform.anchoredPosition = new Vector2(10f, -6f);
            statusHeader.raycastTarget = false;

            _waveOverviewReady = CreateText(statusBar.transform, "ReadyLabel", "Ready --/--", 11, TextAlignmentOptions.Left, new Color(0.84f, 0.90f, 0.97f, 0.98f));
            EnsureMinimumFontSize(_waveOverviewReady);
            ConfigureSingleLineText(_waveOverviewReady);
            _waveOverviewReady.rectTransform.anchorMin = Vector2.zero;
            _waveOverviewReady.rectTransform.anchorMax = Vector2.one;
            _waveOverviewReady.rectTransform.offsetMin = new Vector2(10f, 7f);
            _waveOverviewReady.rectTransform.offsetMax = new Vector2(-10f, -22f);
            statusBar.SetActive(false);

            var queueShelf = new GameObject("QueueShelf", typeof(RectTransform), typeof(Image));
            queueShelf.transform.SetParent(body.transform, false);
            var queueShelfRect = queueShelf.GetComponent<RectTransform>();
            queueShelfRect.anchorMin = new Vector2(0f, 0f);
            queueShelfRect.anchorMax = new Vector2(1f, 1f);
            queueShelfRect.offsetMin = new Vector2(314f, 0f);
            queueShelfRect.offsetMax = Vector2.zero;
            var queueShelfImage = queueShelf.GetComponent<Image>();
            queueShelfImage.color = new Color(0.09f, 0.13f, 0.17f, 0.98f);
            ApplyPanelFrame(queueShelf, queueShelfImage.color, new Color(0.98f, 0.68f, 0.30f, 0.96f));

            var queueHeader = CreateText(queueShelf.transform, "QueueHeader", "UPCOMING RAIDERS", 12, TextAlignmentOptions.Left, new Color(0.98f, 0.84f, 0.56f, 0.98f));
            ConfigureSingleLineText(queueHeader);
            queueHeader.rectTransform.anchorMin = new Vector2(0f, 1f);
            queueHeader.rectTransform.anchorMax = new Vector2(1f, 1f);
            queueHeader.rectTransform.pivot = new Vector2(0f, 1f);
            queueHeader.rectTransform.sizeDelta = new Vector2(-16f, 16f);
            queueHeader.rectTransform.anchoredPosition = new Vector2(10f, -5f);

            var queueViewport = new GameObject("QueueViewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            queueViewport.transform.SetParent(queueShelf.transform, false);
            var queueViewportRect = queueViewport.GetComponent<RectTransform>();
            queueViewportRect.anchorMin = new Vector2(0f, 0f);
            queueViewportRect.anchorMax = new Vector2(1f, 1f);
            queueViewportRect.offsetMin = new Vector2(8f, 6f);
            queueViewportRect.offsetMax = new Vector2(-8f, -24f);
            queueViewport.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.12f, 0.01f);
            queueViewport.GetComponent<Mask>().showMaskGraphic = false;

            var queueStrip = new GameObject("QueueStrip", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            queueStrip.transform.SetParent(queueViewport.transform, false);
            _waveQueueStrip = queueStrip.GetComponent<RectTransform>();
            _waveQueueStrip.anchorMin = new Vector2(0f, 0.5f);
            _waveQueueStrip.anchorMax = new Vector2(0f, 0.5f);
            _waveQueueStrip.pivot = new Vector2(0f, 0.5f);
            _waveQueueStrip.anchoredPosition = Vector2.zero;
            _waveQueueStrip.sizeDelta = new Vector2(0f, 32f);
            var queueLayout = queueStrip.GetComponent<HorizontalLayoutGroup>();
            queueLayout.childAlignment = TextAnchor.MiddleLeft;
            queueLayout.childControlWidth = false;
            queueLayout.childControlHeight = false;
            queueLayout.childForceExpandWidth = false;
            queueLayout.childForceExpandHeight = false;
            queueLayout.spacing = 6f;
            queueLayout.padding = new RectOffset(2, 2, 1, 1);
            var queueFitter = queueStrip.GetComponent<ContentSizeFitter>();
            queueFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            queueFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var queueScrollRect = queueViewport.GetComponent<ScrollRect>();
            queueScrollRect.horizontal = true;
            queueScrollRect.vertical = false;
            queueScrollRect.viewport = queueViewportRect;
            queueScrollRect.content = _waveQueueStrip;
            queueScrollRect.scrollSensitivity = 20f;

            _waveQueueEmptyLabel = CreateText(queueViewport.transform, "EmptyLabel", "Waiting for wave queue...", 12, TextAlignmentOptions.Center, new Color(0.74f, 0.82f, 0.90f, 0.9f));
            EnsureMinimumFontSize(_waveQueueEmptyLabel);
            _waveQueueEmptyLabel.rectTransform.anchorMin = Vector2.zero;
            _waveQueueEmptyLabel.rectTransform.anchorMax = Vector2.one;
            _waveQueueEmptyLabel.rectTransform.offsetMin = Vector2.zero;
            _waveQueueEmptyLabel.rectTransform.offsetMax = Vector2.zero;
            _waveQueueEmptyLabel.raycastTarget = false;

            var collapsed = new GameObject("CollapsedView", typeof(RectTransform), typeof(Image));
            collapsed.transform.SetParent(root.transform, false);
            var collapsedRect = collapsed.GetComponent<RectTransform>();
            collapsedRect.anchorMin = Vector2.zero;
            collapsedRect.anchorMax = Vector2.one;
            collapsedRect.offsetMin = new Vector2(8f, 8f);
            collapsedRect.offsetMax = new Vector2(-8f, -8f);
            collapsed.GetComponent<Image>().color = new Color(0.09f, 0.13f, 0.17f, 1f);
            collapsed.SetActive(false);

            var collapsedRing = new GameObject("CollapsedRing", typeof(RectTransform), typeof(Image));
            collapsedRing.transform.SetParent(collapsed.transform, false);
            var collapsedRingRect = collapsedRing.GetComponent<RectTransform>();
            collapsedRingRect.anchorMin = new Vector2(0.5f, 0.5f);
            collapsedRingRect.anchorMax = new Vector2(0.5f, 0.5f);
            collapsedRingRect.pivot = new Vector2(0.5f, 0.5f);
            collapsedRingRect.sizeDelta = new Vector2(28f, 28f);
            collapsedRingRect.anchoredPosition = new Vector2(0f, -4f);
            var collapsedRingImage = collapsedRing.GetComponent<Image>();
            collapsedRingImage.color = new Color(0.28f, 0.74f, 0.96f, 0.98f);

            _waveOverviewCollapsedLabel = CreateText(collapsed.transform, "CollapsedLabel", "W?", 14, TextAlignmentOptions.Center, Color.white);
            _waveOverviewCollapsedLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            _waveOverviewCollapsedLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _waveOverviewCollapsedLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            _waveOverviewCollapsedLabel.rectTransform.sizeDelta = new Vector2(0f, 18f);
            _waveOverviewCollapsedLabel.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            _waveOverviewWidget = root.GetComponent<DraggableHudPanel>();
            _waveOverviewWidget.Configure(
                rect,
                bodyRect,
                collapsedRect,
                toggle.GetComponent<Button>(),
                toggleLabel,
                _waveOverviewCollapsedLabel,
                false,
                WaveOverviewWidgetPrefsKey,
                new Vector2(960f, 82f),
                new Vector2(92f, 60f));

            toggle.transform.SetAsLastSibling();
        }

        void BuildMiniMapWidget()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("MiniMapWidget");

            var root = new GameObject("MiniMapWidget", typeof(RectTransform), typeof(Image), typeof(DraggableHudPanel), typeof(BattlefieldMiniMapWidget));
            root.transform.SetParent(_canvasRect, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(220f, 220f);
            float menuButtonWidth = Mathf.Max(settingsButtonSize + 28f, 74f);
            rect.anchoredPosition = new Vector2(-(settingsRightInset + menuButtonWidth + 8f), -settingsTopInset);

            var rootImage = root.GetComponent<Image>();
            rootImage.sprite = null;
            rootImage.type = Image.Type.Simple;
            rootImage.color = new Color(0.02f, 0.03f, 0.04f, 0.68f);

            var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggle.transform.SetParent(root.transform, false);
            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.sizeDelta = new Vector2(24f, 24f);
            toggleRect.anchoredPosition = new Vector2(-4f, -4f);
            var toggleImage = toggle.GetComponent<Image>();
            toggleImage.sprite = null;
            toggleImage.type = Image.Type.Simple;
            toggleImage.color = new Color(0.05f, 0.06f, 0.08f, 0.94f);
            var toggleLabel = CreateText(toggle.transform, "Label", "-", 13, TextAlignmentOptions.Center, Color.white);
            toggleLabel.rectTransform.anchorMin = Vector2.zero;
            toggleLabel.rectTransform.anchorMax = Vector2.one;
            toggleLabel.rectTransform.offsetMin = Vector2.zero;
            toggleLabel.rectTransform.offsetMax = Vector2.zero;
            toggleLabel.fontStyle = FontStyles.Bold;
            toggleLabel.fontSize = Mathf.RoundToInt(13f * GetFontScale());

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(root.transform, false);
            var bodyRect = body.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(3f, 3f);
            bodyRect.offsetMax = new Vector2(-3f, -3f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(body.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.sprite = null;
            viewportImage.type = Image.Type.Simple;
            viewportImage.color = new Color(0.04f, 0.05f, 0.06f, 0.96f);

            var map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
            map.transform.SetParent(viewport.transform, false);
            var mapRect = map.GetComponent<RectTransform>();
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;
            var mapImage = map.GetComponent<RawImage>();
            mapImage.color = Color.white;
            mapImage.raycastTarget = false;

            var unitDots = new GameObject("UnitDots", typeof(RectTransform));
            unitDots.transform.SetParent(viewport.transform, false);
            var unitDotsRect = unitDots.GetComponent<RectTransform>();
            unitDotsRect.anchorMin = Vector2.zero;
            unitDotsRect.anchorMax = Vector2.one;
            unitDotsRect.offsetMin = Vector2.zero;
            unitDotsRect.offsetMax = Vector2.zero;

            var focusIndicator = new GameObject("FocusIndicator", typeof(RectTransform), typeof(Image), typeof(Outline));
            focusIndicator.transform.SetParent(viewport.transform, false);
            var focusIndicatorRect = focusIndicator.GetComponent<RectTransform>();
            focusIndicatorRect.anchorMin = new Vector2(0.5f, 0.5f);
            focusIndicatorRect.anchorMax = new Vector2(0.5f, 0.5f);
            focusIndicatorRect.pivot = new Vector2(0.5f, 0.5f);
            focusIndicatorRect.sizeDelta = new Vector2(10f, 10f);
            var focusIndicatorImage = focusIndicator.GetComponent<Image>();
            focusIndicatorImage.color = new Color(0.95f, 0.82f, 0.28f, 0.98f);
            var focusOutline = focusIndicator.GetComponent<Outline>();
            focusOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            focusOutline.effectDistance = new Vector2(1f, -1f);
            focusIndicator.SetActive(false);

            var collapsed = new GameObject("CollapsedView", typeof(RectTransform), typeof(Image));
            collapsed.transform.SetParent(root.transform, false);
            var collapsedRect = collapsed.GetComponent<RectTransform>();
            collapsedRect.anchorMin = Vector2.zero;
            collapsedRect.anchorMax = Vector2.one;
            collapsedRect.offsetMin = Vector2.zero;
            collapsedRect.offsetMax = Vector2.zero;
            var collapsedImage = collapsed.GetComponent<Image>();
            collapsedImage.sprite = null;
            collapsedImage.type = Image.Type.Simple;
            collapsedImage.color = new Color(0.03f, 0.04f, 0.05f, 0.90f);
            collapsed.SetActive(false);

            var collapsedLabel = CreateText(collapsed.transform, "CollapsedLabel", "MAP", 13, TextAlignmentOptions.Center, Color.white);
            collapsedLabel.rectTransform.anchorMin = Vector2.zero;
            collapsedLabel.rectTransform.anchorMax = Vector2.one;
            collapsedLabel.rectTransform.offsetMin = Vector2.zero;
            collapsedLabel.rectTransform.offsetMax = Vector2.zero;
            collapsedLabel.fontStyle = FontStyles.Bold;

            var panel = root.GetComponent<DraggableHudPanel>();
            _miniMapDockPanel = panel;
            panel.Configure(
                rect,
                bodyRect,
                collapsedRect,
                toggle.GetComponent<Button>(),
                toggleLabel,
                collapsedLabel,
                false,
                MiniMapWidgetPrefsKey,
                new Vector2(220f, 220f),
                new Vector2(72f, 48f));

            _miniMapWidget = root.GetComponent<BattlefieldMiniMapWidget>();
            _miniMapWidget.Configure(
                panel,
                viewportRect,
                unitDotsRect,
                mapImage,
                focusIndicatorRect,
                null,
                null);

            toggle.transform.SetAsLastSibling();
        }

        void OnStartWavePressed()
        {
            var readyState = NetworkManager.Instance != null ? NetworkManager.Instance.LastMLWaveReadyState : null;
            Debug.Log(
                $"[WaveStart][Client] button_click lane={NetworkManager.Instance?.MyLaneIndex ?? -1} " +
                $"upcomingWave={readyState?.upcomingWaveNumber ?? -1} " +
                $"remainingWaveMobs={readyState?.remainingWaveMobCount ?? -1} " +
                $"currentWaveComplete={(readyState != null && readyState.currentWaveComplete).ToString().ToLowerInvariant()} " +
                $"allReady={(readyState != null && readyState.allReady).ToString().ToLowerInvariant()}");
            ActionSender.RequestStartWaveVote();
        }

        void RefreshProgressionDock(MLLaneSnap myLane)
        {
            if (_txtBarracksLevel != null)
                _txtBarracksLevel.text = "Open Progression";

            if (_progressionDockWidget != null)
                _progressionDockWidget.SetCollapsedLabel("TECH");

            if (_progressionDockStatus == null)
                return;

            var townCore = SnapshotApplier.Instance?.GetTownCorePad(myLane?.laneIndex ?? -1);
            int builtBranches = CountBuiltBranchPads(myLane);
            string townCoreText = townCore != null
                ? $"Town Core T{Mathf.Max(1, townCore.tier)}"
                : "Town Core --";
            _progressionDockStatus.text = $"{townCoreText} | Branches {builtBranches}/4 | Drag or minimize";
        }

        void RefreshProgressionDockPreview()
        {
            if (_txtBarracksLevel != null)
                _txtBarracksLevel.text = "Open Progression";
            if (_progressionDockWidget != null)
                _progressionDockWidget.SetCollapsedLabel("TECH");
            if (_progressionDockStatus != null)
                _progressionDockStatus.text = "Town Core T2 | Branches 4/4 | Drag or minimize";
        }

        void RefreshWaveOverview(MLLaneSnap myLane, MLSnapshot snap, float recommendedBuild, Color accentColor)
        {
            if (_waveOverviewWidget == null || myLane == null || snap == null)
                return;

            var readyState = NetworkManager.Instance != null ? NetworkManager.Instance.LastMLWaveReadyState : null;
            int upcomingWaveNumber = myLane.upcomingWave != null && myLane.upcomingWave.waveNumber > 0
                ? myLane.upcomingWave.waveNumber
                : readyState != null && readyState.upcomingWaveNumber > 0
                    ? readyState.upcomingWaveNumber
                    : snap.roundNumber + 1;

            if (_waveOverviewTitle != null)
                _waveOverviewTitle.text = $"WAVE W{upcomingWaveNumber}";
            int waveSeconds = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetWaveTimerSecondsRemaining() : 0;
            int sendSeconds = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.GetBarracksSendSecondsRemaining(myLane.laneIndex) : 0;
            _waveOverviewWidget.SetCollapsedLabel($"W{upcomingWaveNumber}+");

            int readyCount = readyState?.readyLaneIndices?.Length ?? 0;
            int requiredReadyCount = Mathf.Max(readyState?.requiredReadyCount ?? 0, readyState?.eligibleLaneIndices?.Length ?? 0);
            bool eligible = ContainsLaneIndex(readyState?.eligibleLaneIndices, myLane.laneIndex);
            bool isReady = ContainsLaneIndex(readyState?.readyLaneIndices, myLane.laneIndex);
            int activeWaveMobCount = CountActiveWaveMobs(snap);
            int remainingWaveMobCount = readyState != null
                ? Mathf.Max(0, readyState.remainingWaveMobCount)
                : CountRemainingWaveMobs(snap);
            bool currentWaveComplete = remainingWaveMobCount <= 0;
            bool allReady = readyState != null && readyState.allReady;

            if (_waveOverviewPhase != null)
            {
                string stateLabel = !currentWaveComplete
                    ? BuildCompactWaveActivityLabel(activeWaveMobCount, remainingWaveMobCount)
                    : BuildCompactReadyLabel(readyCount, requiredReadyCount, eligible, isReady);
                string sendLabel = sendSeconds > 0 ? $" | Send {sendSeconds}s" : string.Empty;
                _waveOverviewPhase.text = $"{WaveTimeIcon}{waveSeconds}s{sendLabel} | {stateLabel} | {WaveValueShieldIcon}{Mathf.RoundToInt(recommendedBuild)}{WaveValueSwordIcon}";
                _waveOverviewPhase.color = accentColor;
            }

            if (_waveOverviewReady != null)
                _waveOverviewReady.text = string.Empty;

            if (_startWaveButton != null)
            {
                if (allReady)
                {
                    SetHudButtonVisual(_startWaveButton, _startWaveButtonLabel, "Launching", new Color(0.26f, 0.42f, 0.58f, 0.98f), false);
                }
                else if (!currentWaveComplete)
                {
                    SetHudButtonVisual(
                        _startWaveButton,
                        _startWaveButtonLabel,
                        activeWaveMobCount > 0 ? "In Combat" : "Wave Active",
                        new Color(0.24f, 0.26f, 0.30f, 0.96f),
                        false);
                }
                else if (!eligible)
                {
                    SetHudButtonVisual(_startWaveButton, _startWaveButtonLabel, "Waiting", new Color(0.24f, 0.26f, 0.30f, 0.96f), false);
                }
                else if (isReady)
                {
                    SetHudButtonVisual(_startWaveButton, _startWaveButtonLabel, "Ready", new Color(0.20f, 0.42f, 0.52f, 0.98f), false);
                }
                else
                {
                    string label = requiredReadyCount > 1 && readyCount == requiredReadyCount - 1
                        ? "Start Now"
                        : "Start Wave";
                    SetHudButtonVisual(_startWaveButton, _startWaveButtonLabel, label, new Color(0.22f, 0.40f, 0.22f, 0.98f), true);
                }
            }

            RefreshUpcomingWaveQueue(myLane);
            RefreshUpcomingWavePopup(myLane);
        }

        void RefreshWaveOverviewPreview(float recommendedBuild, Color accentColor)
        {
            if (_waveOverviewWidget == null)
                return;

            int upcomingWaveNumber = previewWave + 1;
            if (_waveOverviewTitle != null)
                _waveOverviewTitle.text = $"WAVE W{upcomingWaveNumber}";
            if (_waveOverviewPhase != null)
            {
                _waveOverviewPhase.text = $"{WaveTimeIcon}{previewCountdown:0}s | Preview | {WaveValueShieldIcon}{Mathf.RoundToInt(recommendedBuild)}{WaveValueSwordIcon}";
                _waveOverviewPhase.color = accentColor;
            }
            if (_waveOverviewReady != null)
                _waveOverviewReady.text = string.Empty;
            _waveOverviewWidget.SetCollapsedLabel($"W{upcomingWaveNumber}+");

            if (_startWaveButton != null)
                SetHudButtonVisual(_startWaveButton, _startWaveButtonLabel, "Start Wave", new Color(0.22f, 0.40f, 0.22f, 0.98f), false);

            if (_waveQueueEmptyLabel != null)
            {
                _waveQueueEmptyLabel.gameObject.SetActive(true);
                _waveQueueEmptyLabel.text = "Wave queue loads in play mode.";
            }
            if (_waveQueueStrip != null)
                ClearChildren(_waveQueueStrip);
            _upcomingWaveQueueViews.Clear();
            CloseUpcomingWavePopup();
        }

        void RefreshBranchStatusCards(MLLaneSnap myLane)
        {
            for (int i = 0; i < WaveBranchBuildingOrder.Length; i++)
            {
                string buildingType = WaveBranchBuildingOrder[i];
                if (!_branchStatusViews.TryGetValue(buildingType, out var view))
                    continue;

                var pad = FindFortressPad(myLane, buildingType);
                if (view.nameLabel != null)
                    view.nameLabel.text = ResolveBranchDisplayName(buildingType);

                if (pad == null)
                {
                    if (view.tierLabel != null)
                        view.tierLabel.text = "Unavailable";
                    if (view.hpLabel != null)
                        view.hpLabel.text = "No pad";
                    if (view.hpFill != null)
                    {
                        view.hpFill.fillAmount = 0f;
                        view.hpFill.color = new Color(0.42f, 0.46f, 0.54f, 0.92f);
                    }
                    continue;
                }

                float ratio = pad.maxHp > 0f ? Mathf.Clamp01(pad.hp / pad.maxHp) : (pad.isBuilt ? 1f : 0f);
                if (view.tierLabel != null)
                        view.tierLabel.text = ResolveBranchTierLabel(pad);
                if (view.hpLabel != null)
                {
                    view.hpLabel.text = pad.maxHp > 0f
                        ? $"{Mathf.RoundToInt(Mathf.Max(0f, pad.hp))}/{Mathf.RoundToInt(pad.maxHp)} HP"
                        : pad.isBuilt ? "Built" : "Not built";
                }
                if (view.hpFill != null)
                {
                    view.hpFill.fillAmount = ratio;
                    view.hpFill.color = ResolvePadHealthColor(ratio);
                }
            }
        }

        void RefreshUpcomingWaveQueue(MLLaneSnap myLane)
        {
            if (_waveQueueStrip == null)
                return;

            var queue = GetUpcomingWaveQueue(myLane);
            string signature = BuildUpcomingWaveQueueSignature(queue);
            if (!string.Equals(_lastUpcomingWaveQueueSignature, signature, StringComparison.Ordinal))
            {
                _lastUpcomingWaveQueueSignature = signature;
                ClearChildren(_waveQueueStrip);
                _upcomingWaveQueueViews.Clear();

                if (queue != null)
                {
                    for (int i = 0; i < queue.Length; i++)
                    {
                        var upcomingWave = queue[i];
                        if (!HasUpcomingWaveEntries(upcomingWave))
                            continue;

                        _upcomingWaveQueueViews.Add(CreateUpcomingWaveQueueCard(_waveQueueStrip, upcomingWave));
                    }
                }

                if (_wavePortraitLoadCoroutine != null)
                    StopCoroutine(_wavePortraitLoadCoroutine);
                _wavePortraitLoadCoroutine = StartUpcomingWavePortraitLoad(queue);
            }
            else if (_wavePortraitLoadCoroutine == null)
            {
                _wavePortraitLoadCoroutine = StartUpcomingWavePortraitLoad(queue);
            }

            bool hasQueue = _upcomingWaveQueueViews.Count > 0;
            if (_waveQueueEmptyLabel != null)
            {
                _waveQueueEmptyLabel.gameObject.SetActive(!hasQueue);
                if (!hasQueue)
                    _waveQueueEmptyLabel.text = "Waiting for wave queue...";
            }

            for (int i = 0; i < _upcomingWaveQueueViews.Count; i++)
            {
                var view = _upcomingWaveQueueViews[i];
                var upcomingWave = FindUpcomingWaveByNumber(queue, view.waveNumber);
                if (view == null || !HasUpcomingWaveEntries(upcomingWave))
                    continue;

                bool selected = _upcomingWavePopup != null
                    && _upcomingWavePopup.activeSelf
                    && view.waveNumber == _selectedUpcomingWaveNumber;

                if (view.frame != null)
                    view.frame.color = selected
                        ? new Color(0.28f, 0.34f, 0.46f, 0.98f)
                        : new Color(0.15f, 0.19f, 0.25f, 0.96f);

                if (view.waveLabel != null)
                    view.waveLabel.text = $"W{upcomingWave.waveNumber}";
                if (view.countLabel != null)
                    view.countLabel.text = $"x{Mathf.Max(1, upcomingWave.totalUnits)}";
                if (view.summaryLabel != null)
                    view.summaryLabel.text = BuildUpcomingWaveCardSummary(upcomingWave);

                var primaryEntry = GetPrimaryUpcomingWaveEntry(upcomingWave);
                if (view.icon != null)
                {
                    view.icon.sprite = ResolveUpcomingWaveQueueIconSprite(primaryEntry);
                    view.icon.color = view.icon.sprite != null
                        ? ResolveUpcomingWaveQueueIconTint(primaryEntry)
                        : new Color(1f, 1f, 1f, 0f);
                }
            }
        }

        UpcomingWaveQueueView CreateUpcomingWaveQueueCard(Transform parent, MLUpcomingWave upcomingWave)
        {
            var root = new GameObject($"Wave_{upcomingWave.waveNumber}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(76f, 34f);
            var image = root.GetComponent<Image>();
            image.color = new Color(0.15f, 0.19f, 0.25f, 0.96f);
            ApplyPanelFrame(root, image.color, new Color(0.95f, 0.76f, 0.38f, 0.84f));

            var layout = root.GetComponent<LayoutElement>();
            layout.preferredWidth = 78f;
            layout.preferredHeight = 34f;

            var iconPlate = new GameObject("IconPlate", typeof(RectTransform), typeof(Image));
            iconPlate.transform.SetParent(root.transform, false);
            var iconPlateRect = iconPlate.GetComponent<RectTransform>();
            iconPlateRect.anchorMin = new Vector2(0f, 0.5f);
            iconPlateRect.anchorMax = new Vector2(0f, 0.5f);
            iconPlateRect.pivot = new Vector2(0f, 0.5f);
            iconPlateRect.sizeDelta = new Vector2(18f, 18f);
            iconPlateRect.anchoredPosition = new Vector2(5f, -1f);
            var iconPlateImage = iconPlate.GetComponent<Image>();
            ClassicRpgUiRuntime.ApplyPanel(iconPlateImage, ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.08f, 0.11f, 0.16f, 0.98f));
            iconPlateImage.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(iconPlate.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            ClassicRpgUiRuntime.Stretch(iconRect, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var waveLabel = CreateText(root.transform, "WaveLabel", $"W{upcomingWave.waveNumber}", 10, TextAlignmentOptions.Left, Color.white);
            ConfigureSingleLineText(waveLabel);
            waveLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            waveLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            waveLabel.rectTransform.pivot = new Vector2(0f, 1f);
            waveLabel.rectTransform.sizeDelta = new Vector2(-10f, 12f);
            waveLabel.rectTransform.anchoredPosition = new Vector2(5f, -2f);
            waveLabel.raycastTarget = false;

            var countLabel = CreateText(root.transform, "CountLabel", $"x{Mathf.Max(1, upcomingWave.totalUnits)}", 10, TextAlignmentOptions.Right, new Color(0.98f, 0.84f, 0.56f, 0.98f));
            ConfigureSingleLineText(countLabel);
            countLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            countLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            countLabel.rectTransform.pivot = new Vector2(1f, 1f);
            countLabel.rectTransform.sizeDelta = new Vector2(-10f, 12f);
            countLabel.rectTransform.anchoredPosition = new Vector2(-5f, -2f);
            countLabel.raycastTarget = false;

            var summaryLabel = CreateText(root.transform, "SummaryLabel", BuildUpcomingWaveCardSummary(upcomingWave), 10, TextAlignmentOptions.Left, new Color(0.90f, 0.94f, 0.98f, 0.96f));
            ConfigureSingleLineText(summaryLabel);
            summaryLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            summaryLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            summaryLabel.rectTransform.pivot = new Vector2(0f, 0f);
            summaryLabel.rectTransform.sizeDelta = new Vector2(-28f, 12f);
            summaryLabel.rectTransform.anchoredPosition = new Vector2(26f, 3f);
            summaryLabel.raycastTarget = false;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OpenUpcomingWavePopup(upcomingWave.waveNumber));

            return new UpcomingWaveQueueView
            {
                waveNumber = upcomingWave.waveNumber,
                button = button,
                frame = image,
                icon = icon,
                waveLabel = waveLabel,
                countLabel = countLabel,
                summaryLabel = summaryLabel,
            };
        }

        void OpenUpcomingWavePopup(int waveNumber)
        {
            var myLane = SnapshotApplier.Instance?.MyLane;
            var upcomingWave = FindUpcomingWaveByNumber(GetUpcomingWaveQueue(myLane), waveNumber);
            if (!HasUpcomingWaveEntries(upcomingWave))
                return;

            EnsureUpcomingWavePopup();
            if (_upcomingWavePopup == null)
                return;

            _selectedUpcomingWaveNumber = upcomingWave.waveNumber;
            _selectedUpcomingWaveEntryKey = null;
            _lastUpcomingWavePortraitSignature = null;
            _upcomingWavePopup.SetActive(true);
            _upcomingWavePopup.transform.SetAsLastSibling();
            RefreshUpcomingWaveQueue(myLane);
            RefreshUpcomingWavePopup(myLane);
        }

        void CloseUpcomingWavePopup()
        {
            _selectedUpcomingWaveNumber = -1;
            _selectedUpcomingWaveEntryKey = null;
            _lastUpcomingWavePortraitSignature = null;

            if (_upcomingWavePopup != null)
                _upcomingWavePopup.SetActive(false);

            if (_wavePortraitStrip != null)
                ClearChildren(_wavePortraitStrip);
            _upcomingWavePortraitViews.Clear();

            RefreshUpcomingWaveQueue(SnapshotApplier.Instance?.MyLane);
        }

        void EnsureUpcomingWavePopup()
        {
            if (_canvasRect == null || _upcomingWavePopup != null)
                return;

            var overlay = new GameObject("UpcomingWavePopup", typeof(RectTransform), typeof(Image), typeof(Button));
            overlay.transform.SetParent(_canvasRect, false);
            overlay.transform.SetAsLastSibling();
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0.02f, 0.04f, 0.07f, 0.72f);
            var overlayButton = overlay.GetComponent<Button>();
            overlayButton.targetGraphic = overlayImage;
            overlayButton.onClick.RemoveAllListeners();
            overlayButton.onClick.AddListener(CloseUpcomingWavePopup);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlay.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, 246f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.08f, 0.11f, 0.15f, 0.98f);
            ApplyPanelFrame(panel, panelImage.color, new Color(0.34f, 0.78f, 0.98f, 0.92f));

            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 40f);
            headerRect.anchoredPosition = Vector2.zero;
            var headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.10f, 0.14f, 0.19f, 0.98f);
            ApplyPanelFrame(header, headerImage.color, new Color(0.42f, 0.82f, 1f, 0.92f));

            _upcomingWavePopupTitle = CreateText(header.transform, "Title", "WAVE DETAILS", 16, TextAlignmentOptions.Left, Color.white);
            _upcomingWavePopupTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _upcomingWavePopupTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _upcomingWavePopupTitle.rectTransform.pivot = new Vector2(0f, 1f);
            _upcomingWavePopupTitle.rectTransform.sizeDelta = new Vector2(-64f, 18f);
            _upcomingWavePopupTitle.rectTransform.anchoredPosition = new Vector2(10f, -6f);

            _upcomingWavePopupSubtitle = CreateText(header.transform, "Subtitle", "Tap an attacker for details.", 10, TextAlignmentOptions.Left, new Color(0.84f, 0.90f, 0.97f, 0.94f));
            _upcomingWavePopupSubtitle.rectTransform.anchorMin = new Vector2(0f, 0f);
            _upcomingWavePopupSubtitle.rectTransform.anchorMax = new Vector2(1f, 0f);
            _upcomingWavePopupSubtitle.rectTransform.pivot = new Vector2(0f, 0f);
            _upcomingWavePopupSubtitle.rectTransform.sizeDelta = new Vector2(-64f, 16f);
            _upcomingWavePopupSubtitle.rectTransform.anchoredPosition = new Vector2(10f, 5f);

            var closeButton = CreateHudActionButton(header.transform, "CloseButton", "X", new Color(0.22f, 0.28f, 0.36f, 0.98f), out var closeLabel);
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(28f, 24f);
            closeRect.anchoredPosition = new Vector2(-10f, -2f);
            closeLabel.fontSize = 10;
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(CloseUpcomingWavePopup);

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(panel.transform, false);
            var bodyRect = body.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(10f, 10f);
            bodyRect.offsetMax = new Vector2(-10f, -46f);

            var portraitShelf = new GameObject("PortraitShelf", typeof(RectTransform), typeof(Image));
            portraitShelf.transform.SetParent(body.transform, false);
            var portraitShelfRect = portraitShelf.GetComponent<RectTransform>();
            portraitShelfRect.anchorMin = new Vector2(0f, 1f);
            portraitShelfRect.anchorMax = new Vector2(1f, 1f);
            portraitShelfRect.pivot = new Vector2(0.5f, 1f);
            portraitShelfRect.sizeDelta = new Vector2(0f, 82f);
            portraitShelfRect.anchoredPosition = Vector2.zero;
            var portraitShelfImage = portraitShelf.GetComponent<Image>();
            portraitShelfImage.color = new Color(0.09f, 0.13f, 0.17f, 0.98f);
            ApplyPanelFrame(portraitShelf, portraitShelfImage.color, new Color(0.98f, 0.68f, 0.30f, 0.96f));

            var portraitHeader = CreateText(portraitShelf.transform, "PortraitHeader", "ATTACKERS", 10, TextAlignmentOptions.Left, new Color(0.98f, 0.84f, 0.56f, 0.98f));
            portraitHeader.rectTransform.anchorMin = new Vector2(0f, 1f);
            portraitHeader.rectTransform.anchorMax = new Vector2(1f, 1f);
            portraitHeader.rectTransform.pivot = new Vector2(0f, 1f);
            portraitHeader.rectTransform.sizeDelta = new Vector2(-16f, 16f);
            portraitHeader.rectTransform.anchoredPosition = new Vector2(10f, -6f);

            var portraitViewport = new GameObject("PortraitViewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            portraitViewport.transform.SetParent(portraitShelf.transform, false);
            var portraitViewportRect = portraitViewport.GetComponent<RectTransform>();
            portraitViewportRect.anchorMin = new Vector2(0f, 0f);
            portraitViewportRect.anchorMax = new Vector2(1f, 1f);
            portraitViewportRect.offsetMin = new Vector2(8f, 8f);
            portraitViewportRect.offsetMax = new Vector2(-8f, -24f);
            portraitViewport.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.12f, 0.01f);
            portraitViewport.GetComponent<Mask>().showMaskGraphic = false;

            var portraitStrip = new GameObject("PortraitStrip", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            portraitStrip.transform.SetParent(portraitViewport.transform, false);
            _wavePortraitStrip = portraitStrip.GetComponent<RectTransform>();
            _wavePortraitStrip.anchorMin = new Vector2(0f, 0.5f);
            _wavePortraitStrip.anchorMax = new Vector2(0f, 0.5f);
            _wavePortraitStrip.pivot = new Vector2(0f, 0.5f);
            _wavePortraitStrip.anchoredPosition = Vector2.zero;
            _wavePortraitStrip.sizeDelta = new Vector2(0f, 48f);
            var portraitLayout = portraitStrip.GetComponent<HorizontalLayoutGroup>();
            portraitLayout.childAlignment = TextAnchor.MiddleLeft;
            portraitLayout.childControlWidth = false;
            portraitLayout.childControlHeight = false;
            portraitLayout.childForceExpandWidth = false;
            portraitLayout.childForceExpandHeight = false;
            portraitLayout.spacing = 8f;
            portraitLayout.padding = new RectOffset(2, 2, 2, 2);
            var portraitFitter = portraitStrip.GetComponent<ContentSizeFitter>();
            portraitFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            portraitFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var portraitScrollRect = portraitViewport.GetComponent<ScrollRect>();
            portraitScrollRect.horizontal = true;
            portraitScrollRect.vertical = false;
            portraitScrollRect.viewport = portraitViewportRect;
            portraitScrollRect.content = _wavePortraitStrip;
            portraitScrollRect.scrollSensitivity = 20f;

            _wavePortraitEmptyLabel = CreateText(portraitViewport.transform, "EmptyLabel", "Waiting for attackers...", 11, TextAlignmentOptions.Center, new Color(0.74f, 0.82f, 0.90f, 0.9f));
            _wavePortraitEmptyLabel.rectTransform.anchorMin = Vector2.zero;
            _wavePortraitEmptyLabel.rectTransform.anchorMax = Vector2.one;
            _wavePortraitEmptyLabel.rectTransform.offsetMin = Vector2.zero;
            _wavePortraitEmptyLabel.rectTransform.offsetMax = Vector2.zero;
            _wavePortraitEmptyLabel.raycastTarget = false;

            _waveDetailPanel = new GameObject("WaveDetailPanel", typeof(RectTransform), typeof(Image));
            _waveDetailPanel.transform.SetParent(body.transform, false);
            var waveDetailRect = _waveDetailPanel.GetComponent<RectTransform>();
            waveDetailRect.anchorMin = new Vector2(0f, 0f);
            waveDetailRect.anchorMax = new Vector2(1f, 0f);
            waveDetailRect.pivot = new Vector2(0.5f, 0f);
            waveDetailRect.sizeDelta = new Vector2(0f, 96f);
            waveDetailRect.anchoredPosition = Vector2.zero;
            var waveDetailImage = _waveDetailPanel.GetComponent<Image>();
            waveDetailImage.color = new Color(0.08f, 0.11f, 0.15f, 0.98f);
            ApplyPanelFrame(_waveDetailPanel, waveDetailImage.color, new Color(0.98f, 0.68f, 0.30f, 0.92f));

            var detailPortraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image));
            detailPortraitFrame.transform.SetParent(_waveDetailPanel.transform, false);
            var detailPortraitFrameRect = detailPortraitFrame.GetComponent<RectTransform>();
            detailPortraitFrameRect.anchorMin = new Vector2(0f, 0.5f);
            detailPortraitFrameRect.anchorMax = new Vector2(0f, 0.5f);
            detailPortraitFrameRect.pivot = new Vector2(0f, 0.5f);
            detailPortraitFrameRect.sizeDelta = new Vector2(66f, 66f);
            detailPortraitFrameRect.anchoredPosition = new Vector2(10f, 0f);
            var detailPortraitFrameImage = detailPortraitFrame.GetComponent<Image>();
            detailPortraitFrameImage.color = new Color(0.13f, 0.18f, 0.24f, 0.98f);
            ApplyPanelFrame(detailPortraitFrame, detailPortraitFrameImage.color, new Color(0.95f, 0.76f, 0.38f, 0.92f));

            var detailPortraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            detailPortraitGo.transform.SetParent(detailPortraitFrame.transform, false);
            var detailPortraitRect = detailPortraitGo.GetComponent<RectTransform>();
            detailPortraitRect.anchorMin = new Vector2(0.5f, 0.5f);
            detailPortraitRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailPortraitRect.pivot = new Vector2(0.5f, 0.5f);
            detailPortraitRect.sizeDelta = new Vector2(54f, 54f);
            _waveDetailPortrait = detailPortraitGo.GetComponent<RawImage>();
            _waveDetailPortrait.color = new Color(1f, 1f, 1f, 0f);
            var detailPortraitFitter = detailPortraitGo.GetComponent<AspectRatioFitter>();
            detailPortraitFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            detailPortraitFitter.aspectRatio = 1f;

            _waveDetailTitle = CreateText(_waveDetailPanel.transform, "DetailTitle", "Tap a unit portrait for details.", 13, TextAlignmentOptions.Left, Color.white);
            _waveDetailTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _waveDetailTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _waveDetailTitle.rectTransform.pivot = new Vector2(0f, 1f);
            _waveDetailTitle.rectTransform.sizeDelta = new Vector2(-96f, 18f);
            _waveDetailTitle.rectTransform.anchoredPosition = new Vector2(86f, -8f);

            _waveDetailSummary = CreateText(_waveDetailPanel.transform, "DetailSummary", "Select a queued attacker to inspect its stats and modifiers.", 10, TextAlignmentOptions.Left, new Color(0.94f, 0.90f, 0.76f, 0.96f));
            _waveDetailSummary.rectTransform.anchorMin = new Vector2(0f, 1f);
            _waveDetailSummary.rectTransform.anchorMax = new Vector2(1f, 1f);
            _waveDetailSummary.rectTransform.pivot = new Vector2(0f, 1f);
            _waveDetailSummary.rectTransform.sizeDelta = new Vector2(-96f, 16f);
            _waveDetailSummary.rectTransform.anchoredPosition = new Vector2(86f, -28f);

            _waveDetailStats = CreateText(_waveDetailPanel.transform, "DetailStats", string.Empty, 9, TextAlignmentOptions.Left, new Color(0.84f, 0.90f, 0.97f, 0.94f));
            _waveDetailStats.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _waveDetailStats.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            _waveDetailStats.rectTransform.pivot = new Vector2(0f, 0.5f);
            _waveDetailStats.rectTransform.sizeDelta = new Vector2(-96f, 16f);
            _waveDetailStats.rectTransform.anchoredPosition = new Vector2(86f, -2f);

            _waveDetailSource = CreateText(_waveDetailPanel.transform, "DetailSource", string.Empty, 9, TextAlignmentOptions.Left, new Color(0.74f, 0.82f, 0.90f, 0.92f));
            _waveDetailSource.rectTransform.anchorMin = new Vector2(0f, 0f);
            _waveDetailSource.rectTransform.anchorMax = new Vector2(1f, 0f);
            _waveDetailSource.rectTransform.pivot = new Vector2(0f, 0f);
            _waveDetailSource.rectTransform.sizeDelta = new Vector2(-96f, 16f);
            _waveDetailSource.rectTransform.anchoredPosition = new Vector2(86f, 8f);

            overlay.SetActive(false);
            _upcomingWavePopup = overlay;
        }

        void RefreshUpcomingWavePopup(MLLaneSnap myLane)
        {
            if (_upcomingWavePopup == null || !_upcomingWavePopup.activeSelf)
                return;

            var upcomingWave = FindUpcomingWaveByNumber(GetUpcomingWaveQueue(myLane), _selectedUpcomingWaveNumber);
            if (!HasUpcomingWaveEntries(upcomingWave))
            {
                CloseUpcomingWavePopup();
                return;
            }

            if (_upcomingWavePopupTitle != null)
                _upcomingWavePopupTitle.text = $"WAVE {upcomingWave.waveNumber}";
            if (_upcomingWavePopupSubtitle != null)
                _upcomingWavePopupSubtitle.text = $"{Mathf.Max(1, upcomingWave.totalUnits)} attackers queued | tap a portrait for details";

            RefreshUpcomingWavePortraits(upcomingWave);
        }

        void RefreshUpcomingWavePortraits(MLUpcomingWave upcomingWave)
        {
            if (_wavePortraitStrip == null)
                return;

            string signature = BuildUpcomingWaveSignature(upcomingWave);
            if (!string.Equals(_lastUpcomingWavePortraitSignature, signature, StringComparison.Ordinal))
            {
                _lastUpcomingWavePortraitSignature = signature;
                ClearChildren(_wavePortraitStrip);
                _upcomingWavePortraitViews.Clear();

                if (upcomingWave?.entries != null)
                {
                    for (int i = 0; i < upcomingWave.entries.Length; i++)
                    {
                        var entry = upcomingWave.entries[i];
                        if (entry == null)
                            continue;

                        _upcomingWavePortraitViews.Add(CreateUpcomingWavePortrait(_wavePortraitStrip, entry));
                    }
                }

            }

            bool hasEntries = _upcomingWavePortraitViews.Count > 0;
            if (_wavePortraitEmptyLabel != null)
            {
                _wavePortraitEmptyLabel.gameObject.SetActive(!hasEntries);
                if (!hasEntries)
                    _wavePortraitEmptyLabel.text = "Waiting for queued attackers...";
            }

            if (!hasEntries)
            {
                _selectedUpcomingWaveEntryKey = null;
                RefreshUpcomingWaveDetail(null);
                return;
            }

            if (FindUpcomingWaveEntryByKey(upcomingWave, _selectedUpcomingWaveEntryKey) == null)
                _selectedUpcomingWaveEntryKey = BuildUpcomingWaveEntryKey(GetPrimaryUpcomingWaveEntry(upcomingWave));

            for (int i = 0; i < _upcomingWavePortraitViews.Count; i++)
            {
                var view = _upcomingWavePortraitViews[i];
                var entry = FindUpcomingWaveEntryByKey(upcomingWave, view.entryKey);
                if (view == null || view.button == null)
                    continue;

                bool selected = string.Equals(view.entryKey, _selectedUpcomingWaveEntryKey, StringComparison.Ordinal);
                if (view.frame != null)
                    view.frame.color = selected ? new Color(0.98f, 0.76f, 0.36f, 1f) : new Color(0.16f, 0.22f, 0.30f, 0.96f);

                if (entry != null)
                {
                    if (view.countLabel != null)
                        view.countLabel.text = $"x{Mathf.Max(1, entry.count)}";
                    if (view.nameLabel != null)
                        view.nameLabel.text = AbbreviateLabel(ResolveUpcomingWaveEntryDisplayName(entry), 5);
                    if (view.portrait != null)
                    {
                        if (TryGetUpcomingWavePortraitTexture(entry, out var texture))
                        {
                            view.portrait.texture = texture;
                            view.portrait.color = Color.white;
                        }
                        else
                        {
                            view.portrait.texture = null;
                            view.portrait.color = new Color(1f, 1f, 1f, 0f);
                        }
                    }
                }
            }

            RefreshUpcomingWaveDetail(FindUpcomingWaveEntryByKey(upcomingWave, _selectedUpcomingWaveEntryKey));
        }

        UpcomingWavePortraitView CreateUpcomingWavePortrait(Transform parent, MLUpcomingWaveEntry entry)
        {
            string entryKey = BuildUpcomingWaveEntryKey(entry);
            var root = new GameObject($"Upcoming_{entryKey}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(58f, 52f);
            var image = root.GetComponent<Image>();
            image.color = new Color(0.16f, 0.22f, 0.30f, 0.96f);
            ApplyPanelFrame(root, image.color, new Color(0.98f, 0.76f, 0.36f, 0.86f));
            var layout = root.GetComponent<LayoutElement>();
            layout.preferredWidth = 58f;
            layout.preferredHeight = 52f;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(root.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0.5f, 0.5f);
            portraitRect.anchorMax = new Vector2(0.5f, 0.5f);
            portraitRect.pivot = new Vector2(0.5f, 0.5f);
            portraitRect.sizeDelta = new Vector2(36f, 36f);
            portraitRect.anchoredPosition = new Vector2(0f, -2f);
            var portrait = portraitGo.GetComponent<RawImage>();
            portrait.color = new Color(1f, 1f, 1f, 0f);
            portrait.raycastTarget = false;
            var portraitFitter = portraitGo.GetComponent<AspectRatioFitter>();
            portraitFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitFitter.aspectRatio = 1f;

            var countLabel = CreateText(root.transform, "Count", $"x{Mathf.Max(1, entry.count)}", 8, TextAlignmentOptions.Center, Color.white);
            countLabel.rectTransform.anchorMin = new Vector2(1f, 1f);
            countLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            countLabel.rectTransform.pivot = new Vector2(1f, 1f);
            countLabel.rectTransform.sizeDelta = new Vector2(24f, 12f);
            countLabel.rectTransform.anchoredPosition = new Vector2(-2f, -2f);
            countLabel.raycastTarget = false;

            var nameLabel = CreateText(root.transform, "Name", AbbreviateLabel(ResolveUpcomingWaveEntryDisplayName(entry), 5), 7, TextAlignmentOptions.Center, new Color(0.92f, 0.95f, 0.98f, 0.98f));
            nameLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            nameLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            nameLabel.rectTransform.sizeDelta = new Vector2(0f, 10f);
            nameLabel.rectTransform.anchoredPosition = new Vector2(0f, 1f);
            nameLabel.raycastTarget = false;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => HandleUpcomingWavePortraitSelected(entryKey));

            return new UpcomingWavePortraitView
            {
                entryKey = entryKey,
                button = button,
                frame = image,
                portrait = portrait,
                countLabel = countLabel,
                nameLabel = nameLabel,
            };
        }

        void HandleUpcomingWavePortraitSelected(string entryKey)
        {
            _selectedUpcomingWaveEntryKey = entryKey;
            RefreshUpcomingWavePopup(SnapshotApplier.Instance?.MyLane);
        }

        void RefreshUpcomingWaveDetail(MLUpcomingWaveEntry entry)
        {
            if (_waveDetailTitle == null || _waveDetailSummary == null || _waveDetailStats == null || _waveDetailSource == null)
                return;

            if (entry == null)
            {
                _waveDetailTitle.text = "Tap a queued attacker for details.";
                _waveDetailSummary.text = "Select a wave entry to inspect its stats and modifiers.";
                _waveDetailStats.text = string.Empty;
                _waveDetailSource.text = string.Empty;
                if (_waveDetailPortrait != null)
                {
                    _waveDetailPortrait.texture = null;
                    _waveDetailPortrait.color = new Color(1f, 1f, 1f, 0f);
                }
                return;
            }

            TryGetUnitCatalogEntry(entry.unitType, out var catalogEntry);
            _waveDetailTitle.text = $"{ResolveUpcomingWaveEntryDisplayName(entry)} x{Mathf.Max(1, entry.count)}";
            _waveDetailSummary.text = !string.IsNullOrWhiteSpace(catalogEntry?.description)
                ? catalogEntry.description
                : "Snapshot-driven upcoming wave entry.";
            _waveDetailStats.text = FormatUpcomingWaveStats(entry, catalogEntry);
            _waveDetailSource.text = ResolveUpcomingWaveSourceText(entry);

            if (_waveDetailPortrait != null)
            {
                if (TryGetUpcomingWavePortraitTexture(entry, out var texture))
                {
                    _waveDetailPortrait.texture = texture;
                    _waveDetailPortrait.color = Color.white;
                }
                else
                {
                    _waveDetailPortrait.texture = null;
                    _waveDetailPortrait.color = new Color(1f, 1f, 1f, 0f);
                }
            }
        }

        Coroutine StartUpcomingWavePortraitLoad(MLUpcomingWave upcomingWave)
        {
            return StartUpcomingWavePortraitLoad(upcomingWave != null ? new[] { upcomingWave } : null);
        }

        Coroutine StartUpcomingWavePortraitLoad(IEnumerable<MLUpcomingWave> upcomingWaves)
        {
            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null || upcomingWaves == null)
                return null;

            var unitKeys = CollectUpcomingWavePortraitKeys(upcomingWaves);

            if (unitKeys.Count == 0 || remoteContent.ArePortraitsReady(unitKeys))
                return null;

            return StartCoroutine(LoadUpcomingWavePortraits(remoteContent, unitKeys));
        }

        static List<string> CollectUpcomingWavePortraitKeys(IEnumerable<MLUpcomingWave> upcomingWaves)
        {
            var unitKeys = new List<string>();
            if (upcomingWaves == null)
                return unitKeys;

            foreach (var upcomingWave in upcomingWaves)
            {
                if (upcomingWave?.entries == null)
                    continue;

                for (int i = 0; i < upcomingWave.entries.Length; i++)
                {
                    string unitKey = ResolveUpcomingWavePortraitLookupKey(upcomingWave.entries[i]);
                    if (!string.IsNullOrWhiteSpace(unitKey) && !unitKeys.Contains(unitKey))
                        unitKeys.Add(unitKey);
                }
            }

            return unitKeys;
        }

        void CreateBranchStatusCard(Transform parent, string buildingType)
        {
            var card = new GameObject($"{buildingType}_Status", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            card.transform.SetParent(parent, false);
            var image = card.GetComponent<Image>();
            image.color = new Color(0.13f, 0.17f, 0.22f, 0.98f);
            ApplyPanelFrame(card, image.color, ResolveBranchAccentColor(buildingType));

            var layout = card.GetComponent<LayoutElement>();
            layout.preferredWidth = 126f;
            layout.preferredHeight = 48f;

            var nameLabel = CreateText(card.transform, "Title", ResolveBranchDisplayName(buildingType), 11, TextAlignmentOptions.Left, Color.white);
            nameLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            nameLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            nameLabel.rectTransform.pivot = new Vector2(0f, 1f);
            nameLabel.rectTransform.sizeDelta = new Vector2(-16f, 14f);
            nameLabel.rectTransform.anchoredPosition = new Vector2(8f, -6f);

            var tierLabel = CreateText(card.transform, "Tier", "Tier --", 9, TextAlignmentOptions.Left, new Color(0.84f, 0.90f, 0.97f, 0.94f));
            tierLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            tierLabel.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            tierLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
            tierLabel.rectTransform.sizeDelta = new Vector2(-16f, 12f);
            tierLabel.rectTransform.anchoredPosition = new Vector2(8f, -2f);

            var hpTrack = new GameObject("HpTrack", typeof(RectTransform), typeof(Image));
            hpTrack.transform.SetParent(card.transform, false);
            var hpTrackRect = hpTrack.GetComponent<RectTransform>();
            hpTrackRect.anchorMin = new Vector2(0f, 0f);
            hpTrackRect.anchorMax = new Vector2(1f, 0f);
            hpTrackRect.pivot = new Vector2(0.5f, 0f);
            hpTrackRect.sizeDelta = new Vector2(-16f, 8f);
            hpTrackRect.anchoredPosition = new Vector2(0f, 6f);
            var hpTrackImage = hpTrack.GetComponent<Image>();
            hpTrackImage.color = new Color(0.18f, 0.21f, 0.27f, 0.96f);

            var hpFill = new GameObject("HpFill", typeof(RectTransform), typeof(Image));
            hpFill.transform.SetParent(hpTrack.transform, false);
            var hpFillRect = hpFill.GetComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.offsetMin = Vector2.zero;
            hpFillRect.offsetMax = Vector2.zero;
            var hpFillImage = hpFill.GetComponent<Image>();
            hpFillImage.type = Image.Type.Filled;
            hpFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpFillImage.fillAmount = 1f;
            hpFillImage.color = new Color(0.36f, 0.86f, 0.48f, 0.96f);

            var hpLabel = CreateText(card.transform, "HpLabel", "0/0 HP", 8, TextAlignmentOptions.Right, new Color(0.92f, 0.95f, 0.98f, 0.96f));
            hpLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            hpLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            hpLabel.rectTransform.pivot = new Vector2(1f, 0f);
            hpLabel.rectTransform.sizeDelta = new Vector2(-16f, 10f);
            hpLabel.rectTransform.anchoredPosition = new Vector2(-8f, 16f);

            _branchStatusViews[buildingType] = new BranchStatusView
            {
                buildingType = buildingType,
                nameLabel = nameLabel,
                tierLabel = tierLabel,
                hpLabel = hpLabel,
                hpFill = hpFillImage,
            };
        }

        Button CreateHudActionButton(Transform parent, string name, string label, Color backgroundColor, out TMP_Text labelText)
        {
            var buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent, false);
            var image = buttonGo.GetComponent<Image>();
            image.color = backgroundColor;
            ApplyPanelFrame(buttonGo, backgroundColor, new Color(0.92f, 0.95f, 0.98f, 0.86f));

            labelText = CreateText(buttonGo.transform, "Label", label, 12, TextAlignmentOptions.Center, Color.white);
            EnsureMinimumFontSize(labelText);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = Vector2.zero;
            labelText.rectTransform.offsetMax = Vector2.zero;
            labelText.alignment = TextAlignmentOptions.Center;
            return buttonGo.GetComponent<Button>();
        }

        void SetHudButtonVisual(Button button, TMP_Text label, string text, Color backgroundColor, bool interactable)
        {
            if (button == null)
                return;

            button.interactable = interactable;
            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = backgroundColor;
            if (label != null)
            {
                label.text = text;
                label.color = interactable ? Color.white : new Color(0.88f, 0.91f, 0.96f, 0.96f);
            }
        }

        static bool ContainsLaneIndex(int[] laneIndices, int laneIndex)
        {
            if (laneIndices == null)
                return false;

            for (int i = 0; i < laneIndices.Length; i++)
            {
                if (laneIndices[i] == laneIndex)
                    return true;
            }

            return false;
        }

        static int CountBuiltBranchPads(MLLaneSnap lane)
        {
            int count = 0;
            if (lane?.fortressPads == null)
                return count;

            for (int i = 0; i < WaveBranchBuildingOrder.Length; i++)
            {
                var pad = FindFortressPad(lane, WaveBranchBuildingOrder[i]);
                if (pad != null && pad.isBuilt)
                    count++;
            }

            return count;
        }

        static MLFortressPad FindFortressPad(MLLaneSnap lane, string buildingType)
        {
            if (lane?.fortressPads == null || string.IsNullOrWhiteSpace(buildingType))
                return null;

            for (int i = 0; i < lane.fortressPads.Length; i++)
            {
                var pad = lane.fortressPads[i];
                if (pad != null && string.Equals(pad.buildingType, buildingType, StringComparison.OrdinalIgnoreCase))
                    return pad;
            }

            return null;
        }

        bool TryGetUnitCatalogEntry(string unitKey, out UnitCatalogEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(unitKey))
                return false;

            if (_catalogByKey.TryGetValue(unitKey, out entry) && entry != null)
                return true;

            if (CatalogLoader.UnitByKey.TryGetValue(unitKey, out entry) && entry != null)
            {
                _catalogByKey[unitKey] = entry;
                return true;
            }

            return false;
        }

        bool TryGetUpcomingWavePortraitTexture(MLUpcomingWaveEntry entry, out Texture2D texture)
        {
            texture = null;
            var remoteContent = RemoteContentManager.Instance;
            if (entry == null)
                return false;

            string lookupKey = ResolveUpcomingWavePortraitLookupKey(entry);
            if (string.IsNullOrWhiteSpace(lookupKey))
            {
                LogMissingUpcomingWavePortrait(entry, "Wave entry did not provide a portrait lookup key.");
                return false;
            }

            if (remoteContent == null)
            {
                LogMissingUpcomingWavePortrait(entry, $"Remote content manager is unavailable for portrait key '{lookupKey}'.");
                return false;
            }

            return remoteContent.TryGetLoadedPortraitTexture(lookupKey, out texture)
                && texture != null;
        }

        static string BuildUpcomingWaveEntryKey(MLUpcomingWaveEntry entry)
        {
            if (entry == null)
                return string.Empty;

            return string.Join("|",
                entry.source ?? string.Empty,
                entry.unitType ?? string.Empty,
                entry.skinKey ?? string.Empty,
                entry.isHero ? "hero" : "unit",
                entry.hpMult.ToString("0.###"),
                entry.dmgMult.ToString("0.###"),
                entry.speedMult.ToString("0.###"),
                entry.sourceLaneIndex.ToString(),
                entry.sourceBarracksId ?? string.Empty,
                entry.heroKey ?? string.Empty);
        }

        static string BuildUpcomingWaveSignature(MLUpcomingWave upcomingWave)
        {
            if (upcomingWave?.entries == null || upcomingWave.entries.Length == 0)
                return string.Empty;

            var parts = new string[upcomingWave.entries.Length];
            for (int i = 0; i < upcomingWave.entries.Length; i++)
            {
                var entry = upcomingWave.entries[i];
                parts[i] = entry == null
                    ? "null"
                    : $"{BuildUpcomingWaveEntryKey(entry)}:{entry.count}:{entry.hpMult:0.###}:{entry.dmgMult:0.###}:{entry.speedMult:0.###}";
            }

            return string.Join(";", parts);
        }

        static string BuildUpcomingWaveQueueSignature(MLUpcomingWave[] queue)
        {
            if (queue == null || queue.Length == 0)
                return string.Empty;

            var parts = new List<string>(queue.Length);
            for (int i = 0; i < queue.Length; i++)
            {
                var upcomingWave = queue[i];
                if (!HasUpcomingWaveEntries(upcomingWave))
                    continue;

                parts.Add($"W{upcomingWave.waveNumber}:{BuildUpcomingWaveSignature(upcomingWave)}");
            }

            return string.Join("||", parts);
        }

        static MLUpcomingWave[] GetUpcomingWaveQueue(MLLaneSnap lane)
        {
            if (lane?.upcomingWaveQueue != null && lane.upcomingWaveQueue.Length > 0)
                return lane.upcomingWaveQueue;

            return HasUpcomingWaveEntries(lane?.upcomingWave)
                ? new[] { lane.upcomingWave }
                : Array.Empty<MLUpcomingWave>();
        }

        static MLUpcomingWave FindUpcomingWaveByNumber(MLUpcomingWave[] queue, int waveNumber)
        {
            if (queue == null || waveNumber <= 0)
                return null;

            for (int i = 0; i < queue.Length; i++)
            {
                var upcomingWave = queue[i];
                if (upcomingWave != null && upcomingWave.waveNumber == waveNumber)
                    return upcomingWave;
            }

            return null;
        }

        static bool HasUpcomingWaveEntries(MLUpcomingWave upcomingWave)
        {
            return upcomingWave?.entries != null && upcomingWave.entries.Length > 0;
        }

        static MLUpcomingWaveEntry GetPrimaryUpcomingWaveEntry(MLUpcomingWave upcomingWave)
        {
            if (upcomingWave?.entries == null)
                return null;

            for (int i = 0; i < upcomingWave.entries.Length; i++)
            {
                if (upcomingWave.entries[i] != null)
                    return upcomingWave.entries[i];
            }

            return null;
        }

        Sprite ResolveUpcomingWaveQueueIconSprite(MLUpcomingWaveEntry entry)
        {
            var theme = ClassicRpgUiRuntime.Theme;
            if (theme == null)
                return null;

            return ResolveUpcomingWaveQueueIconKind(entry) switch
            {
                BarracksActivityIconKind.Shield => theme.ActivityShieldIcon,
                BarracksActivityIconKind.Archer => theme.ActivityArcherIcon,
                BarracksActivityIconKind.Priest => theme.ActivityPriestIcon,
                BarracksActivityIconKind.Mage => theme.ActivityMageIcon,
                BarracksActivityIconKind.Hero => theme.ActivityShieldIcon,
                _ => theme.ActivityInfantryIcon,
            };
        }

        static Color ResolveUpcomingWaveQueueIconTint(MLUpcomingWaveEntry entry)
        {
            return ResolveUpcomingWaveQueueIconKind(entry) switch
            {
                BarracksActivityIconKind.Shield => new Color(0.72f, 0.86f, 0.98f, 0.98f),
                BarracksActivityIconKind.Archer => new Color(0.76f, 0.92f, 0.70f, 0.98f),
                BarracksActivityIconKind.Priest => new Color(0.85f, 0.96f, 0.84f, 0.98f),
                BarracksActivityIconKind.Mage => new Color(0.88f, 0.78f, 0.98f, 0.98f),
                BarracksActivityIconKind.Hero => new Color(0.98f, 0.84f, 0.42f, 0.98f),
                _ => new Color(0.96f, 0.92f, 0.84f, 0.98f),
            };
        }

        static BarracksActivityIconKind ResolveUpcomingWaveQueueIconKind(MLUpcomingWaveEntry entry)
        {
            if (entry != null && entry.isHero)
                return BarracksActivityIconKind.Hero;

            string summary = string.Join(
                " ",
                entry?.unitType ?? string.Empty,
                entry?.skinKey ?? string.Empty,
                entry?.heroKey ?? string.Empty,
                entry?.archetypeKey ?? string.Empty,
                entry?.presentationKey ?? string.Empty).ToLowerInvariant();

            if (summary.Contains("mage")
                || summary.Contains("wizard")
                || summary.Contains("sorcer")
                || summary.Contains("warlock")
                || summary.Contains("arcane")
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
                || summary.Contains("archery")
                || summary.Contains("hunter")
                || summary.Contains("marksman"))
            {
                return BarracksActivityIconKind.Archer;
            }

            if (summary.Contains("shield")
                || summary.Contains("guardian")
                || summary.Contains("paladin")
                || summary.Contains("defender")
                || summary.Contains("protector"))
            {
                return BarracksActivityIconKind.Shield;
            }

            return BarracksActivityIconKind.Infantry;
        }

        string BuildUpcomingWaveCardSummary(MLUpcomingWave upcomingWave)
        {
            if (!HasUpcomingWaveEntries(upcomingWave))
                return "Empty";

            int entryCount = 0;
            for (int i = 0; i < upcomingWave.entries.Length; i++)
            {
                if (upcomingWave.entries[i] != null)
                    entryCount++;
            }

            if (entryCount <= 1)
                return AbbreviateLabel(ResolveUpcomingWaveEntryDisplayName(GetPrimaryUpcomingWaveEntry(upcomingWave)), 10);

            return $"{entryCount} types";
        }

        static MLUpcomingWaveEntry FindUpcomingWaveEntryByKey(MLUpcomingWave upcomingWave, string entryKey)
        {
            if (upcomingWave?.entries == null || string.IsNullOrWhiteSpace(entryKey))
                return null;

            for (int i = 0; i < upcomingWave.entries.Length; i++)
            {
                var entry = upcomingWave.entries[i];
                if (entry != null && string.Equals(BuildUpcomingWaveEntryKey(entry), entryKey, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        string ResolveUpcomingWaveEntryDisplayName(MLUpcomingWaveEntry entry)
        {
            if (entry == null)
                return "Wave Entry";
            if (!string.IsNullOrWhiteSpace(entry.heroKey))
                return HumanizeIdentifier(entry.heroKey);
            if (!string.IsNullOrWhiteSpace(entry.skinKey))
            {
                string displayKey = entry.skinKey.StartsWith("tt_", StringComparison.OrdinalIgnoreCase)
                    ? entry.skinKey.Substring(3)
                    : entry.skinKey;
                return HumanizeIdentifier(displayKey);
            }
            if (TryGetUnitCatalogEntry(entry.unitType, out var catalog) && !string.IsNullOrWhiteSpace(catalog?.name))
                return catalog.name;
            return HumanizeIdentifier(!string.IsNullOrWhiteSpace(entry.unitType) ? entry.unitType : entry.skinKey);
        }

        string FormatUpcomingWaveStats(MLUpcomingWaveEntry entry, UnitCatalogEntry catalogEntry)
        {
            if (entry == null)
                return string.Empty;

            float hp = catalogEntry != null ? catalogEntry.hp * Mathf.Max(0.01f, entry.hpMult) : 0f;
            float damage = catalogEntry != null ? catalogEntry.attack_damage * Mathf.Max(0.01f, entry.dmgMult) : 0f;
            float speed = BarracksSpawnCombatProfileResolver.ResolveUpcomingWaveServerPathSpeed(
                entry,
                SnapshotApplier.Instance?.LatestMLMatchConfig?.movementTuning,
                catalogEntry != null ? Mathf.Max(0f, catalogEntry.path_speed) : 0f);
            float range = catalogEntry != null ? catalogEntry.range : 0f;

            if (catalogEntry == null)
                return $"Count {Mathf.Max(1, entry.count)} | HP x{entry.hpMult:0.##} | DMG x{entry.dmgMult:0.##} | SPD x{entry.speedMult:0.##}";

            return $"HP {Mathf.RoundToInt(hp)} | DMG {damage:0.#} | SPD {speed:0.##} | RNG {range:0.#}";
        }

        string ResolveUpcomingWaveSourceText(MLUpcomingWaveEntry entry)
        {
            if (entry == null)
                return string.Empty;

            string source = string.IsNullOrWhiteSpace(entry.source)
                ? "Wave script"
                : HumanizeIdentifier(entry.source);
            if (entry.isHero && !string.IsNullOrWhiteSpace(entry.heroKey))
                source = $"Hero deployment | {HumanizeIdentifier(entry.heroKey)}";
            else if (!string.IsNullOrWhiteSpace(entry.sourceBarracksId))
                source = $"{source} | {HumanizeIdentifier(entry.sourceBarracksId)}";

            if (entry.sourceLaneIndex >= 0)
                source += $" | Lane {entry.sourceLaneIndex + 1}";

            return source;
        }

        IEnumerator LoadUpcomingWavePortraits(RemoteContentManager remoteContent, List<string> unitKeys)
        {
            yield return remoteContent.EnsurePortraitsReady(unitKeys, requester: "MobileMatchHud.WaveOverview");

            if (!remoteContent.ArePortraitsReady(unitKeys))
            {
                string reason = string.IsNullOrWhiteSpace(remoteContent.LastError)
                    ? "Portrait request finished without loading the requested upcoming-wave portraits."
                    : remoteContent.LastError;
                LogMissingUpcomingWavePortrait($"batch:{string.Join(",", unitKeys)}", reason);
            }

            _wavePortraitLoadCoroutine = null;
        }

        static string ResolveUpcomingWavePortraitLookupKey(MLUpcomingWaveEntry entry)
        {
            if (entry == null)
                return null;

            return !string.IsNullOrWhiteSpace(entry.skinKey)
                ? entry.skinKey.Trim()
                : entry.unitType?.Trim();
        }

        void LogMissingUpcomingWavePortrait(MLUpcomingWaveEntry entry, string reason)
        {
            if (entry == null)
                return;

            LogMissingUpcomingWavePortrait(
                $"{BuildUpcomingWaveEntryKey(entry)}:{ResolveUpcomingWavePortraitLookupKey(entry)}",
                reason);
        }

        void LogMissingUpcomingWavePortrait(string key, string reason)
        {
            if (string.IsNullOrWhiteSpace(key) || !_missingUpcomingWavePortraitLogs.Add(key))
                return;

            Debug.LogWarning($"[MobileMatchHud] Missing upcoming-wave portrait for '{key}'. {reason}");
        }

        static string ResolveBranchDisplayName(string buildingType)
        {
            return buildingType switch
            {
                "blacksmith" => "Blacksmith",
                "archery_tower" => "Archery",
                "temple" => "Temple",
                "wizard_tower" => "Mage Tower",
                _ => HumanizeIdentifier(buildingType),
            };
        }

        static string ResolveBranchTierLabel(MLFortressPad pad)
        {
            if (pad == null)
                return "Unavailable";

            if (!pad.isBuilt)
            {
                if (pad.canBuild)
                    return $"Build {pad.buildCost}g";
                if (!string.IsNullOrWhiteSpace(pad.lockedReason))
                    return pad.lockedReason;
                return "Not built";
            }

            if (!string.IsNullOrWhiteSpace(pad.currentTierName))
                return pad.currentTierName;

            return $"Tier {Mathf.Max(1, pad.tier)}";
        }

        static Color ResolveBranchAccentColor(string buildingType)
        {
            return buildingType switch
            {
                "blacksmith" => new Color(0.86f, 0.54f, 0.34f, 0.96f),
                "archery_tower" => new Color(0.48f, 0.86f, 0.46f, 0.96f),
                "temple" => new Color(0.48f, 0.82f, 0.96f, 0.96f),
                "wizard_tower" => new Color(0.78f, 0.58f, 0.98f, 0.96f),
                _ => new Color(0.72f, 0.78f, 0.86f, 0.96f),
            };
        }

        static Color ResolvePadHealthColor(float ratio)
        {
            if (ratio >= 0.7f)
                return new Color(0.36f, 0.86f, 0.48f, 0.96f);
            if (ratio >= 0.35f)
                return new Color(0.94f, 0.74f, 0.28f, 0.96f);
            return new Color(0.95f, 0.38f, 0.34f, 0.96f);
        }

        static string AbbreviateLabel(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "--";

            string compact = value.Trim();
            return compact.Length <= maxChars
                ? compact.ToUpperInvariant()
                : compact.Substring(0, maxChars).ToUpperInvariant();
        }

        static string HumanizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "--";

            string[] parts = value.Replace('_', ' ').Replace('-', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0)
                    continue;

                parts[i] = part.Length == 1
                    ? part.ToUpperInvariant()
                    : char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
            }

            return string.Join(" ", parts);
        }

        void BuildSettingsPanel()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("SettingsPanel");
            _settingsTabViews.Clear();
            _settingsRowViews.Clear();
            _settingsOverlaySubtitle = null;
            _settingsInfoCategoryValue = null;
            _settingsInfoDescriptionValue = null;
            _settingsInfoPresetValue = null;
            _selectedSettingsRow = null;

            var root = new GameObject("SettingsPanel", typeof(RectTransform));
            root.transform.SetParent(_canvasRect, false);
            root.transform.SetAsLastSibling();
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
            overlay.transform.SetParent(root.transform, false);
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.86f);
            var overlayButton = overlay.GetComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(() => SetSettingsOverlayVisible(false));
            _settingsOverlay = overlay;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(overlay.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            float panelHorizontalInset = Mathf.Max(18f, settingsPanelGap + 12f);
            float panelVerticalInset = Mathf.Max(18f, settingsPanelGap + 8f);
            panelRect.offsetMin = new Vector2(panelHorizontalInset, panelVerticalInset);
            panelRect.offsetMax = new Vector2(-panelHorizontalInset, -panelVerticalInset);
            _settingsOverlayPanelRoot = panelRect;

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.06f, 0.10f, 0.14f, 0.98f);
            StyleFlatSettingsSurface(panel, panelImage.color, new Color(0.86f, 0.66f, 0.28f, 0.98f));

            var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.spacing = 12f;
            panelLayout.padding = new RectOffset(18, 18, 18, 18);

            var eyebrow = CreateFlatSettingsText(panel.transform, "Eyebrow", "COMMAND MENU", 11f, new Color(0.95f, 0.79f, 0.42f, 0.98f));
            eyebrow.fontStyle = FontStyles.SmallCaps;
            eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var title = CreateFlatSettingsText(panel.transform, "Title", "Settings", 24f, Color.white);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            _settingsOverlaySubtitle = CreateFlatSettingsText(
                panel.transform,
                "Subtitle",
                "Tap each selector to cycle its saved option.",
                12f,
                new Color(0.84f, 0.89f, 0.95f, 0.96f));
            _settingsOverlaySubtitle.textWrappingMode = TextWrappingModes.Normal;
            _settingsOverlaySubtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var scrollBody = new GameObject("ScrollBody", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            scrollBody.transform.SetParent(panel.transform, false);
            var scrollBodyImage = scrollBody.GetComponent<Image>();
            scrollBodyImage.color = new Color(0.04f, 0.07f, 0.10f, 0.52f);
            var scrollBodyLayoutElement = scrollBody.GetComponent<LayoutElement>();
            scrollBodyLayoutElement.flexibleHeight = 1f;
            scrollBodyLayoutElement.minHeight = 0f;

            var scrollBodyLayout = scrollBody.GetComponent<HorizontalLayoutGroup>();
            scrollBodyLayout.childAlignment = TextAnchor.UpperCenter;
            scrollBodyLayout.childControlWidth = true;
            scrollBodyLayout.childControlHeight = true;
            scrollBodyLayout.childForceExpandWidth = false;
            scrollBodyLayout.childForceExpandHeight = true;
            scrollBodyLayout.spacing = 10f;
            scrollBodyLayout.padding = new RectOffset(0, 0, 0, 0);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
            viewport.transform.SetParent(scrollBody.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var viewportLayout = viewport.GetComponent<LayoutElement>();
            viewportLayout.flexibleWidth = 1f;
            viewportLayout.flexibleHeight = 1f;
            viewportLayout.minHeight = 0f;
            viewportLayout.minWidth = 0f;

            var rows = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rows.transform.SetParent(viewport.transform, false);
            var rowsRect = rows.GetComponent<RectTransform>();
            rowsRect.anchorMin = new Vector2(0f, 1f);
            rowsRect.anchorMax = new Vector2(1f, 1f);
            rowsRect.pivot = new Vector2(0.5f, 1f);
            rowsRect.anchoredPosition = Vector2.zero;
            rowsRect.sizeDelta = Vector2.zero;

            var rowsLayout = rows.GetComponent<VerticalLayoutGroup>();
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.spacing = 10f;

            var rowsFitter = rows.GetComponent<ContentSizeFitter>();
            rowsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbarRoot = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar), typeof(LayoutElement));
            scrollbarRoot.transform.SetParent(scrollBody.transform, false);
            scrollbarRoot.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.18f, 0.96f);

            var scrollbarLayoutElement = scrollbarRoot.GetComponent<LayoutElement>();
            scrollbarLayoutElement.preferredWidth = 16f;
            scrollbarLayoutElement.minWidth = 16f;
            scrollbarLayoutElement.flexibleHeight = 1f;

            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
            slidingArea.transform.SetParent(scrollbarRoot.transform, false);
            var slidingAreaRect = slidingArea.GetComponent<RectTransform>();
            slidingAreaRect.anchorMin = Vector2.zero;
            slidingAreaRect.anchorMax = Vector2.one;
            slidingAreaRect.offsetMin = new Vector2(3f, 3f);
            slidingAreaRect.offsetMax = new Vector2(-3f, -3f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(slidingArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handle.GetComponent<Image>().color = new Color(0.86f, 0.66f, 0.28f, 0.96f);

            var scrollbar = scrollbarRoot.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle.GetComponent<Image>();

            var scrollRect = viewport.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.viewport = viewportRect;
            scrollRect.content = rowsRect;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;
            scrollRect.verticalNormalizedPosition = 1f;

            var tiltButton = CreateFlatSettingsSelectorRow(rows.transform, "TiltRow", "Camera Tilt", "Cycle the battlefield viewing angle.", new Color(0.16f, 0.24f, 0.32f, 0.98f), out _txtSettingsTiltValue);
            var zoomButton = CreateFlatSettingsSelectorRow(rows.transform, "ZoomRow", "Camera Zoom", "Cycle how close your command view sits.", new Color(0.18f, 0.28f, 0.22f, 0.98f), out _txtSettingsZoomValue);
            var rotationButton = CreateFlatSettingsSelectorRow(rows.transform, "RotateRow", "Camera Rotation", "Cycle your battlefield facing.", new Color(0.28f, 0.20f, 0.16f, 0.98f), out _txtSettingsRotationValue);
            var sfxButton = CreateFlatSettingsSelectorRow(rows.transform, "SfxRow", "Sound Effects", "Cycle combat, build, and UI volume.", new Color(0.20f, 0.22f, 0.34f, 0.98f), out _txtSettingsSfxValue);
            var musicButton = CreateFlatSettingsSelectorRow(rows.transform, "MusicRow", "Music Loop", "Cycle the background soundtrack level.", new Color(0.16f, 0.26f, 0.30f, 0.98f), out _txtSettingsMusicValue);
            var engagementButton = CreateFlatSettingsSelectorRow(rows.transform, "EngagementRow", "Engagement Rings", "Show or hide combat range circles.", new Color(0.24f, 0.18f, 0.32f, 0.98f), out _txtSettingsEngagementValue);
            var healthBarsButton = CreateFlatSettingsSelectorRow(rows.transform, "HealthBarsRow", "Health Bars", "Show or hide unit health bars.", new Color(0.23f, 0.26f, 0.16f, 0.98f), out _txtSettingsHealthBarsValue);
            var tooltipsButton = CreateFlatSettingsSelectorRow(rows.transform, "TooltipsRow", "Display Tooltips", "Save barracks hints and onboarding tips.", new Color(0.28f, 0.24f, 0.12f, 0.98f), out _txtSettingsTooltipsValue);

            var footer = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footer.transform.SetParent(panel.transform, false);
            footer.GetComponent<LayoutElement>().preferredHeight = 46f;
            var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
            footerLayout.childAlignment = TextAnchor.MiddleCenter;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = false;
            footerLayout.spacing = 12f;

            var closeButton = CreateFlatSettingsButton(footer.transform, "CloseButton", "Close", new Color(0.18f, 0.24f, 0.30f, 0.98f), 46f);
            Button logoutButton = null;
            if (AuthManager.IsAuthenticated)
                logoutButton = CreateFlatSettingsButton(footer.transform, "LogoutButton", "Log Out", new Color(0.34f, 0.24f, 0.12f, 0.98f), 46f);
            var quitButton = CreateFlatSettingsButton(footer.transform, "QuitButton", "Quit Game", new Color(0.42f, 0.17f, 0.17f, 0.98f), 46f);

            var gear = new GameObject("GearButton", typeof(RectTransform), typeof(Image), typeof(Button));
            gear.transform.SetParent(root.transform, false);
            var gearRect = gear.GetComponent<RectTransform>();
            gearRect.anchorMin = new Vector2(1f, 1f);
            gearRect.anchorMax = new Vector2(1f, 1f);
            gearRect.pivot = new Vector2(1f, 1f);
            gearRect.sizeDelta = new Vector2(Mathf.Max(settingsButtonSize + 28f, 74f), settingsButtonSize);
            gearRect.anchoredPosition = new Vector2(-settingsRightInset, -settingsTopInset);
            _settingsPanelRoot = gearRect;
            _settingsMenuButton = gear.GetComponent<Button>();

            var gearImage = gear.GetComponent<Image>();
            gearImage.color = new Color(0.11f, 0.15f, 0.19f, 0.98f);
            StyleFlatSettingsSurface(gear, gearImage.color, new Color(0.86f, 0.66f, 0.28f, 0.98f));

            _settingsMenuButtonLabel = CreateFlatSettingsText(gear.transform, "Label", "Menu", 14f, new Color(0.96f, 0.97f, 0.99f, 1f));
            _settingsMenuButtonLabel.rectTransform.anchorMin = Vector2.zero;
            _settingsMenuButtonLabel.rectTransform.anchorMax = Vector2.one;
            _settingsMenuButtonLabel.rectTransform.offsetMin = Vector2.zero;
            _settingsMenuButtonLabel.rectTransform.offsetMax = Vector2.zero;

            _settingsMenuButton.onClick.AddListener(ToggleSettingsOverlay);
            tiltButton.onClick.AddListener(CycleCameraTiltSetting);
            zoomButton.onClick.AddListener(CycleCameraZoomSetting);
            rotationButton.onClick.AddListener(CycleCameraRotationSetting);
            sfxButton.onClick.AddListener(CycleSfxVolumeSetting);
            musicButton.onClick.AddListener(CycleMusicVolumeSetting);
            engagementButton.onClick.AddListener(ToggleEngagementCirclesSetting);
            healthBarsButton.onClick.AddListener(ToggleHealthBarsSetting);
            tooltipsButton.onClick.AddListener(ToggleTooltipsSetting);
            closeButton.onClick.AddListener(() => SetSettingsOverlayVisible(false));
            if (logoutButton != null)
                logoutButton.onClick.AddListener(OnLogoutPressed);
            quitButton.onClick.AddListener(OnQuitPressed);

            RefreshSettingsPanelValues();
            SetSettingsOverlayVisible(false, immediate: true);
        }

        Button CreateFlatSettingsSelectorRow(
            Transform parent,
            string name,
            string labelText,
            string detailText,
            Color selectorColor,
            out TMP_Text valueLabel)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var rowImage = row.GetComponent<Image>();
            rowImage.color = new Color(0.09f, 0.14f, 0.18f, 0.98f);
            StyleFlatSettingsSurface(row, rowImage.color, selectorColor);

            var rowLayoutElement = row.GetComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = 54f;
            rowLayoutElement.flexibleWidth = 1f;

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 12f;
            rowLayout.padding = new RectOffset(14, 14, 8, 8);

            var copy = new GameObject("Copy", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            copy.transform.SetParent(row.transform, false);
            copy.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var copyLayout = copy.GetComponent<VerticalLayoutGroup>();
            copyLayout.childAlignment = TextAnchor.UpperLeft;
            copyLayout.childControlWidth = true;
            copyLayout.childControlHeight = false;
            copyLayout.childForceExpandWidth = true;
            copyLayout.childForceExpandHeight = false;
            copyLayout.spacing = 2f;

            var title = CreateFlatSettingsText(copy.transform, "Title", labelText, 13f, Color.white);
            title.fontStyle = FontStyles.SmallCaps;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            var detail = CreateFlatSettingsText(copy.transform, "Detail", detailText, 10f, new Color(0.78f, 0.85f, 0.92f, 0.94f));
            detail.textWrappingMode = TextWrappingModes.Normal;
            detail.overflowMode = TextOverflowModes.Ellipsis;
            detail.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            return CreateFlatSettingsSelectorButton(row.transform, "Selector", selectorColor, out valueLabel);
        }

        Button CreateFlatSettingsButton(Transform parent, string name, string label, Color backgroundColor, float preferredHeight = 0f)
        {
            var buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(parent, false);
            buttonGo.GetComponent<Image>().color = backgroundColor;

            var layout = buttonGo.GetComponent<LayoutElement>();
            layout.minWidth = 44f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 1f;
            if (preferredHeight > 0f)
                layout.preferredHeight = preferredHeight;

            var labelText = CreateFlatSettingsText(buttonGo.transform, "Label", label, 10f, new Color(0.96f, 0.97f, 0.99f, 1f));
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(4f, 2f);
            labelText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            labelText.alignment = TextAlignmentOptions.Center;
            return buttonGo.GetComponent<Button>();
        }

        Button CreateFlatSettingsSelectorButton(Transform parent, string name, Color backgroundColor, out TMP_Text valueLabel)
        {
            var button = CreateFlatSettingsButton(parent, name, "--", backgroundColor, 42f);
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                float selectorMinWidth = Mathf.Max(118f, settingsValueWidth * 4f);
                float selectorPreferredWidth = Mathf.Max(132f, settingsValueWidth * 4.4f);
                layout.minWidth = selectorMinWidth;
                layout.preferredWidth = selectorPreferredWidth;
                layout.flexibleWidth = 0f;
            }

            StyleFlatSettingsSurface(button.gameObject, backgroundColor, new Color(0.94f, 0.96f, 0.99f, 0.42f));
            valueLabel = button.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (valueLabel != null)
            {
                valueLabel.fontSize = Mathf.RoundToInt(12f * GetFontScale());
                valueLabel.fontStyle = FontStyles.Bold;
            }

            return button;
        }

        TMP_Text CreateFlatSettingsText(Transform parent, string name, string value, float fontSize, Color color)
        {
            var textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(parent, false);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize * GetFontScale();
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        void StyleFlatSettingsSurface(GameObject target, Color backgroundColor, Color accentColor)
        {
            if (target == null)
                return;

            var outline = target.GetComponent<Outline>();
            if (outline == null)
                outline = target.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1.2f, -1.2f);
            outline.effectColor = accentColor;
            outline.useGraphicAlpha = true;

            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
                shadow = target.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.30f);
            shadow.useGraphicAlpha = true;

            var image = target.GetComponent<Image>();
            if (image != null)
                image.color = backgroundColor;
        }

        void BuildQuitConfirmationModal()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren(QuitConfirmationModalName);

            var overlay = new GameObject(QuitConfirmationModalName, typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(_canvasRect, false);
            overlay.transform.SetAsLastSibling();
            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.84f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(overlay.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(360f, 220f);

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.06f, 0.06f, 0.08f, 0.95f);
            StyleCommandMenuSurface(panel, panelImage.color, new Color(0.90f, 0.84f, 0.72f, 0.54f));

            var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.spacing = 12f;
            panelLayout.padding = new RectOffset(18, 18, 18, 18);

            var eyebrow = CreateText(panel.transform, "Eyebrow", "FIELD COMMAND", 10, TextAlignmentOptions.Center, new Color(0.95f, 0.79f, 0.42f, 0.98f));
            eyebrow.fontStyle = FontStyles.SmallCaps;
            eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            var titlePlate = new GameObject("TitlePlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            titlePlate.transform.SetParent(panel.transform, false);
            var titlePlateLayout = titlePlate.GetComponent<LayoutElement>();
            titlePlateLayout.preferredWidth = 260f;
            titlePlateLayout.preferredHeight = 62f;
            ClassicRpgUiRuntime.ApplyPanel(titlePlate.GetComponent<Image>(), ClassicRpgPanelSkin.TitleLong, false, Color.white);

            _quitConfirmationTitleLabel = CreateText(titlePlate.transform, "Title", "Log Out", 20, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold);
            _quitConfirmationTitleLabel.rectTransform.anchorMin = Vector2.zero;
            _quitConfirmationTitleLabel.rectTransform.anchorMax = Vector2.one;
            _quitConfirmationTitleLabel.rectTransform.offsetMin = new Vector2(18f, 8f);
            _quitConfirmationTitleLabel.rectTransform.offsetMax = new Vector2(-18f, -12f);

            _quitConfirmationBodyLabel = CreateText(
                panel.transform,
                "Body",
                "Leave the current match, clear your commander session, and return to sign-in?",
                13,
                TextAlignmentOptions.Center,
                new Color(0.90f, 0.86f, 0.80f, 0.96f));
            _quitConfirmationBodyLabel.fontStyle = FontStyles.Normal;
            _quitConfirmationBodyLabel.textWrappingMode = TextWrappingModes.Normal;
            _quitConfirmationBodyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            var buttonsLayout = buttons.GetComponent<HorizontalLayoutGroup>();
            buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = true;
            buttonsLayout.childForceExpandHeight = false;
            buttonsLayout.spacing = 12f;
            buttons.GetComponent<LayoutElement>().preferredHeight = 44f;

            _quitConfirmationCancelButton = CreateQuitConfirmationButton(buttons.transform, "CancelButton", "Stay", new Color(0.18f, 0.24f, 0.30f, 0.98f), out _);
            _quitConfirmationConfirmButton = CreateQuitConfirmationButton(buttons.transform, "ConfirmButton", "Log Out", new Color(0.42f, 0.17f, 0.17f, 0.98f), out _quitConfirmationConfirmLabel);
            ApplyCommandMenuButtonStyle(_quitConfirmationConfirmButton, ClassicRpgButtonSkin.MiniGold, 44f);
            _quitConfirmationCancelButton.onClick.AddListener(CancelSettingsAction);
            _quitConfirmationConfirmButton.onClick.AddListener(ConfirmSettingsAction);

            _quitConfirmationModal = overlay;
            HideQuitConfirmation();
        }

        Vector2 ResolveSettingsOverlayPanelSize(float safeWidth, float safeHeight)
        {
            float maxWidth = Mathf.Max(340f, safeWidth - 28f);
            float maxHeight = Mathf.Max(420f, safeHeight - 28f);

            float targetWidth = safeWidth >= 1080f ? 1080f : safeWidth * 0.92f;
            float targetHeight = safeHeight >= 820f ? 760f : safeHeight * 0.94f;

            float width = Mathf.Clamp(targetWidth, Mathf.Min(620f, maxWidth), Mathf.Min(1180f, maxWidth));
            float height = Mathf.Clamp(targetHeight, Mathf.Min(540f, maxHeight), Mathf.Min(820f, maxHeight));
            return new Vector2(width, height);
        }

        Button CreateSettingsTabButton(Transform parent, string name, string label, SettingsMenuTab tab)
        {
            bool compact = parent != null && parent.GetComponent<HorizontalLayoutGroup>() != null;

            var buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(parent, false);
            var layout = buttonGo.GetComponent<LayoutElement>();
            layout.preferredHeight = compact ? 56f : 62f;
            layout.minHeight = layout.preferredHeight;
            layout.flexibleWidth = compact ? 1f : 0f;
            layout.minWidth = 0f;
            if (!compact)
                layout.preferredWidth = 186f;

            var background = buttonGo.GetComponent<Image>();
            var fill = new Color(0.12f, 0.16f, 0.22f, 0.98f);
            StyleSettingsBuildingCard(buttonGo, fill, ResolveSettingsTabAccent(tab), false);

            var button = buttonGo.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;

            var accentImage = buttonGo.transform.Find("CardAccent")?.GetComponent<Image>();
            var labelText = CreateText(buttonGo.transform, "Label", label, compact ? 12 : 13, TextAlignmentOptions.Center, ClassicRpgUiRuntime.BrightText);
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(14f, 8f);
            labelText.rectTransform.offsetMax = new Vector2(-14f, -10f);
            labelText.fontStyle = FontStyles.SmallCaps;

            _settingsTabViews.Add(new SettingsMenuTabView
            {
                Tab = tab,
                Button = button,
                Label = labelText,
                Background = background,
                Accent = accentImage,
            });
            return button;
        }

        void BindSettingsRow(SettingsMenuRowView rowView, UnityEngine.Events.UnityAction action)
        {
            if (rowView?.Button == null)
                return;

            rowView.Button.onClick.AddListener(() => SelectSettingsRow(rowView));
            if (action != null)
                rowView.Button.onClick.AddListener(action);
        }

        void SetSettingsMenuTab(SettingsMenuTab tab, bool playSound = true)
        {
            bool changed = _activeSettingsTab != tab;
            _activeSettingsTab = tab;

            for (int i = 0; i < _settingsRowViews.Count; i++)
            {
                var row = _settingsRowViews[i];
                if (row?.Root != null)
                    row.Root.SetActive(row.Tab == tab);
            }

            if (_settingsOverlaySubtitle != null)
                _settingsOverlaySubtitle.text = BuildSettingsTabSubtitle(tab);

            if (_selectedSettingsRow == null || _selectedSettingsRow.Tab != tab)
                _selectedSettingsRow = GetFirstSettingsRowForTab(tab);

            RefreshSettingsMenuTabVisuals();
            RefreshSettingsRowVisuals();
            RefreshSettingsInfoRail();

            if (changed && playSound)
                AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void RefreshSettingsMenuTabVisuals()
        {
            for (int i = 0; i < _settingsTabViews.Count; i++)
            {
                var view = _settingsTabViews[i];
                if (view?.Button == null)
                    continue;

                bool selected = view.Tab == _activeSettingsTab;
                Color fill = selected
                    ? new Color(0.24f, 0.19f, 0.10f, 0.98f)
                    : new Color(0.12f, 0.16f, 0.22f, 0.98f);
                var accent = selected
                    ? new Color(0.97f, 0.80f, 0.42f, 0.98f)
                    : ResolveSettingsTabAccent(view.Tab);
                StyleSettingsBuildingCard(view.Button.gameObject, fill, accent, selected);

                if (view.Label != null)
                    view.Label.color = selected
                        ? ClassicRpgUiRuntime.WarmGold
                        : new Color(0.86f, 0.89f, 0.94f, 0.90f);
            }
        }

        void SelectSettingsRow(SettingsMenuRowView rowView, bool playSound = true)
        {
            if (rowView == null)
                return;

            bool changed = _selectedSettingsRow != rowView;
            _selectedSettingsRow = rowView;
            RefreshSettingsRowVisuals();
            RefreshSettingsInfoRail();

            if (changed && playSound)
                AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        SettingsMenuRowView GetFirstSettingsRowForTab(SettingsMenuTab tab)
        {
            for (int i = 0; i < _settingsRowViews.Count; i++)
            {
                var row = _settingsRowViews[i];
                if (row != null && row.Tab == tab)
                    return row;
            }

            return null;
        }

        void RefreshSettingsRowVisuals()
        {
            for (int i = 0; i < _settingsRowViews.Count; i++)
            {
                var row = _settingsRowViews[i];
                if (row?.Root == null)
                    continue;

                bool isActive = row.Tab == _activeSettingsTab;
                row.Root.SetActive(isActive);
                if (!isActive)
                    continue;

                bool selected = row == _selectedSettingsRow;
                var fill = selected
                    ? new Color(0.24f, 0.19f, 0.10f, 0.98f)
                    : new Color(0.12f, 0.16f, 0.21f, 0.98f);
                var accent = selected
                    ? new Color(0.97f, 0.80f, 0.42f, 0.98f)
                    : ResolveSettingsTabAccent(row.Tab);
                StyleSettingsBuildingCard(row.Root, fill, accent, selected);

                if (row.Divider != null)
                    row.Divider.color = selected
                        ? new Color(0.92f, 0.84f, 0.66f, 0.28f)
                        : new Color(0.26f, 0.31f, 0.38f, 0.14f);

                if (row.Eyebrow != null)
                    row.Eyebrow.color = selected
                        ? ClassicRpgUiRuntime.WarmGold
                        : new Color(0.92f, 0.79f, 0.44f, 0.96f);

                if (row.Title != null)
                    row.Title.color = selected
                        ? ClassicRpgUiRuntime.WarmGold
                        : new Color(0.94f, 0.94f, 0.92f, 0.96f);

                if (row.Detail != null)
                    row.Detail.color = selected
                        ? new Color(0.90f, 0.88f, 0.84f, 0.96f)
                        : new Color(0.72f, 0.76f, 0.82f, 0.92f);

                if (row.ValueFrame != null)
                {
                    row.ValueFrame.color = selected
                        ? new Color(0.30f, 0.22f, 0.11f, 0.98f)
                        : new Color(0.18f, 0.15f, 0.09f, 0.98f);
                    ClassicRpgUiRuntime.ApplyPanel(row.ValueFrame, ClassicRpgPanelSkin.InventoryTitle, true, Color.white);
                }

                if (row.Value != null)
                    row.Value.color = selected
                        ? new Color(0.99f, 0.94f, 0.76f, 1f)
                        : ClassicRpgUiRuntime.WarmGold;
            }
        }

        void RefreshSettingsInfoRail()
        {
            if (_settingsInfoCategoryValue != null)
                _settingsInfoCategoryValue.text = GetSettingsTabLabel(_activeSettingsTab);

            if (_settingsInfoDescriptionValue != null)
            {
                var focusedRow = _selectedSettingsRow != null && _selectedSettingsRow.Tab == _activeSettingsTab
                    ? _selectedSettingsRow
                    : GetFirstSettingsRowForTab(_activeSettingsTab);
                string title = focusedRow != null ? focusedRow.LabelText : GetSettingsTabLabel(_activeSettingsTab);
                string detail = focusedRow != null ? focusedRow.DetailText : BuildSettingsTabSubtitle(_activeSettingsTab);
                _settingsInfoDescriptionValue.text = $"<b>{title}</b>\n<size=78%>{detail}</size>";
            }

            if (_settingsInfoPresetValue != null)
                _settingsInfoPresetValue.text = BuildSettingsPresetSummary(_activeSettingsTab);
        }

        static string GetSettingsTabLabel(SettingsMenuTab tab)
        {
            return tab switch
            {
                SettingsMenuTab.General => "General",
                SettingsMenuTab.Graphics => "Graphics",
                SettingsMenuTab.Sound => "Sound",
                _ => "Settings",
            };
        }

        static Color ResolveSettingsTabAccent(SettingsMenuTab tab)
        {
            return tab switch
            {
                SettingsMenuTab.General => new Color(0.53f, 0.72f, 0.90f, 0.92f),
                SettingsMenuTab.Graphics => new Color(0.55f, 0.85f, 0.66f, 0.92f),
                SettingsMenuTab.Sound => new Color(0.88f, 0.62f, 0.38f, 0.92f),
                _ => new Color(0.76f, 0.70f, 0.52f, 0.92f),
            };
        }

        string BuildSettingsPresetSummary(SettingsMenuTab tab)
        {
            var preferences = UserPreferencesManager.CurrentPreferenceView;
            switch (tab)
            {
                case SettingsMenuTab.Graphics:
                {
                    int enabledCount = 0;
                    if (preferences.visuals.showEngagementCircles)
                        enabledCount++;
                    if (preferences.visuals.showHealthBars)
                        enabledCount++;
                    if (preferences.visuals.showTooltips)
                        enabledCount++;

                    return enabledCount switch
                    {
                        3 => "High",
                        2 => "Medium",
                        0 => "Low",
                        _ => "Custom",
                    };
                }
                case SettingsMenuTab.Sound:
                {
                    float musicVolume = preferences.audio.gameplayMusicVolume
                        ?? preferences.audio.menuMusicVolume
                        ?? preferences.audio.ambientVolume;
                    float averageVolume = (preferences.audio.sfxVolume + musicVolume) * 0.5f;
                    if (averageVolume >= 0.75f)
                        return "High";
                    if (averageVolume >= 0.35f)
                        return "Medium";
                    return averageVolume > 0.01f ? "Low" : "Muted";
                }
                default:
                {
                    ResolveCurrentCameraValues(out float tilt, out float zoom, out float rotation);
                    float expectedRotation = UserPreferencesManager.CurrentPreferenceView.camera.rotation ?? rotation;
                    bool defaultView = Mathf.Abs(tilt - UserCameraPreferences.DefaultTilt) <= 0.1f
                        && Mathf.Abs(zoom - UserCameraPreferences.DefaultZoom) <= 0.1f
                        && Mathf.Abs(rotation - expectedRotation) <= 0.1f;
                    return defaultView ? "Standard" : "Custom";
                }
            }
        }

        static string BuildSettingsTabSubtitle(SettingsMenuTab tab)
        {
            return tab switch
            {
                SettingsMenuTab.General => "Adjust camera framing and battlefield control comfort.",
                SettingsMenuTab.Graphics => "Tune combat readability, markers, and on-field clarity.",
                SettingsMenuTab.Sound => "Balance war drums, effects, and battlefield audio.",
                _ => "Adjust battlefield settings.",
            };
        }

        RectTransform CreateSettingsInfoCard(
            Transform parent,
            string name,
            string header,
            float preferredHeight,
            TextAlignmentOptions alignment,
            out TMP_Text valueLabel)
        {
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_canvasRect);
            var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            card.transform.SetParent(parent, false);

            var layoutElement = card.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleWidth = 1f;

            var image = card.GetComponent<Image>();
            var fill = new Color(0.12f, 0.16f, 0.21f, 0.98f);
            StyleSettingsBuildingCard(card, fill, new Color(0.57f, 0.65f, 0.75f, 0.86f), false);

            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = alignment == TextAlignmentOptions.Left ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(16, 16, 16, 16);

            var headerLabel = CreateText(
                card.transform,
                "Header",
                header,
                compact ? 10 : 11,
                alignment == TextAlignmentOptions.Left ? TextAlignmentOptions.Left : TextAlignmentOptions.Center,
                ClassicRpgUiRuntime.SoftGold);
            headerLabel.fontStyle = FontStyles.SmallCaps;
            headerLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            valueLabel = CreateText(
                card.transform,
                "Value",
                "--",
                alignment == TextAlignmentOptions.Left ? (compact ? 12 : 13) : (compact ? 17 : 19),
                alignment == TextAlignmentOptions.Left ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Center,
                alignment == TextAlignmentOptions.Left ? new Color(0.92f, 0.90f, 0.84f, 0.96f) : ClassicRpgUiRuntime.WarmGold);
            valueLabel.fontStyle = alignment == TextAlignmentOptions.Left ? FontStyles.Normal : FontStyles.Bold;
            valueLabel.textWrappingMode = TextWrappingModes.Normal;
            valueLabel.overflowMode = TextOverflowModes.Ellipsis;
            var valueLayout = valueLabel.gameObject.AddComponent<LayoutElement>();
            valueLayout.preferredHeight = Mathf.Max(42f, preferredHeight - 46f);
            valueLayout.flexibleWidth = 1f;
            return card.GetComponent<RectTransform>();
        }

        Button CreateSettingsActionButton(Transform parent, string name, string label, Color backgroundColor)
        {
            var buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(parent, false);
            buttonGo.GetComponent<Image>().color = backgroundColor;
            var layout = buttonGo.GetComponent<LayoutElement>();
            layout.minWidth = 44f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 1f;

            var labelText = CreateText(buttonGo.transform, "Label", label, 10, TextAlignmentOptions.Center, new Color(0.96f, 0.97f, 0.99f, 1f));
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(4f, 2f);
            labelText.rectTransform.offsetMax = new Vector2(-4f, -2f);

            var button = buttonGo.GetComponent<Button>();
            ApplyCommandMenuButtonStyle(button, ClassicRpgButtonSkin.MiniBrown, 44f);
            return button;
        }

        GameObject CreateSettingsFooterSpacer(Transform parent)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(parent, false);
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            return spacer;
        }

        SettingsMenuRowView CreateSettingsSelectorRow(
            Transform parent,
            string name,
            SettingsMenuTab tab,
            string labelText,
            string detailText,
            out TMP_Text valueLabel)
        {
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_canvasRect);

            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var rowImage = row.GetComponent<Image>();
            var baseFill = new Color(0.12f, 0.16f, 0.21f, 0.98f);
            StyleSettingsBuildingCard(row, baseFill, ResolveSettingsTabAccent(tab), false);

            var button = row.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = rowImage;

            var rowLayoutElement = row.GetComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = compact ? 104f : 112f;
            rowLayoutElement.minHeight = rowLayoutElement.preferredHeight;
            rowLayoutElement.flexibleWidth = 1f;

            var rowLayout = row.GetComponent<VerticalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = compact ? 4f : 6f;
            rowLayout.padding = compact
                ? new RectOffset(14, 14, 12, 12)
                : new RectOffset(16, 16, 14, 14);

            var accentImage = row.transform.Find("CardAccent")?.GetComponent<Image>();

            var eyebrow = CreateText(row.transform, "Eyebrow", GetSettingsTabLabel(tab).ToUpperInvariant(), compact ? 9 : 10, TextAlignmentOptions.Left, ClassicRpgUiRuntime.SoftGold);
            eyebrow.fontStyle = FontStyles.SmallCaps;
            eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 12f : 14f;

            var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(row.transform, false);
            var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
            headerLayout.childAlignment = TextAnchor.MiddleCenter;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            headerLayout.spacing = compact ? 10f : 12f;

            var title = CreateText(header.transform, "Title", labelText, compact ? 15 : 16, TextAlignmentOptions.Left, new Color(0.94f, 0.94f, 0.92f, 0.96f));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            var titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = compact ? 22f : 24f;
            titleLayout.flexibleWidth = 1f;
            titleLayout.minWidth = 0f;

            var valueFrame = new GameObject("ValueFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            valueFrame.transform.SetParent(header.transform, false);
            var valueLayoutElement = valueFrame.GetComponent<LayoutElement>();
            valueLayoutElement.preferredWidth = Mathf.Max(132f, settingsValueWidth * 4.4f);
            valueLayoutElement.minWidth = Mathf.Max(120f, settingsValueWidth * 4.0f);
            valueLayoutElement.preferredHeight = compact ? 30f : 34f;
            var valueFrameImage = valueFrame.GetComponent<Image>();
            valueFrameImage.color = new Color(0.19f, 0.15f, 0.09f, 0.98f);
            ClassicRpgUiRuntime.ApplyPanel(valueFrameImage, ClassicRpgPanelSkin.InventoryTitle, true, Color.white);

            valueLabel = CreateText(valueFrame.transform, "Value", "--", compact ? 12 : 13, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold);
            valueLabel.rectTransform.anchorMin = Vector2.zero;
            valueLabel.rectTransform.anchorMax = Vector2.one;
            valueLabel.rectTransform.offsetMin = new Vector2(8f, 3f);
            valueLabel.rectTransform.offsetMax = new Vector2(-8f, -4f);
            valueLabel.overflowMode = TextOverflowModes.Ellipsis;
            valueLabel.textWrappingMode = TextWrappingModes.NoWrap;
            valueLabel.fontStyle = FontStyles.SmallCaps;

            var detail = CreateText(row.transform, "Detail", detailText, compact ? 10 : 11, TextAlignmentOptions.Left, new Color(0.72f, 0.76f, 0.82f, 0.92f));
            detail.fontStyle = FontStyles.Normal;
            detail.textWrappingMode = TextWrappingModes.Normal;
            detail.overflowMode = TextOverflowModes.Ellipsis;
            detail.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 24f : 28f;

            var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            divider.transform.SetParent(row.transform, false);
            divider.GetComponent<LayoutElement>().ignoreLayout = true;
            var dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.anchoredPosition = new Vector2(0f, 1f);
            dividerRect.sizeDelta = new Vector2(0f, 1f);
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.raycastTarget = false;
            dividerImage.color = new Color(0.24f, 0.30f, 0.38f, 0.16f);

            var view = new SettingsMenuRowView
            {
                Tab = tab,
                Root = row,
                Button = button,
                Eyebrow = eyebrow,
                Title = title,
                Detail = detail,
                Value = valueLabel,
                Accent = accentImage,
                Divider = dividerImage,
                ValueFrame = valueFrameImage,
                LabelText = labelText,
                DetailText = detailText,
            };
            _settingsRowViews.Add(view);
            return view;
        }

        Button CreateQuitConfirmationButton(Transform parent, string name, string label, Color backgroundColor, out TMP_Text labelText)
        {
            var button = CreateSettingsActionButton(parent, name, label, backgroundColor);
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredHeight = 44f;
                layout.flexibleHeight = 0f;
            }

            labelText = button.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (labelText != null)
                labelText.fontSize = Mathf.RoundToInt(12f * GetFontScale());

            return button;
        }

        void AddSettingsPanelAtmosphere(RectTransform panelRect, bool compact)
        {
            if (panelRect == null)
                return;

            CreateSettingsTintLayer(
                panelRect,
                "TopGlow",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, compact ? -6f : -10f),
                new Vector2(0f, compact ? 150f : 210f),
                new Color(0.07f, 0.10f, 0.16f, 0.28f));
            CreateSettingsTintLayer(
                panelRect,
                "CenterVeil",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                new Color(0.02f, 0.04f, 0.08f, 0.10f));
            CreateSettingsTintLayer(
                panelRect,
                "BottomGlow",
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, compact ? 6f : 10f),
                new Vector2(0f, compact ? 104f : 132f),
                new Color(0.12f, 0.09f, 0.04f, 0.20f));
        }

        static void CreateSettingsTintLayer(
            RectTransform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        void ToggleSettingsOverlay()
        {
            bool visible = _settingsOverlay != null && _settingsOverlay.activeSelf;
            SetSettingsOverlayVisible(!visible);
        }

        void SetSettingsOverlayVisible(bool visible)
        {
            SetSettingsOverlayVisible(visible, immediate: false);
        }

        void SetSettingsOverlayVisible(bool visible, bool immediate)
        {
            bool wasVisible = _settingsOverlay != null && _settingsOverlay.activeSelf;
            if (_settingsOverlay != null)
                _settingsOverlay.SetActive(visible);

            if (visible && _settingsOverlay != null && _settingsOverlay.transform.parent != null)
                _settingsOverlay.transform.parent.SetAsLastSibling();

            if (!visible)
                HideQuitConfirmation();

            if (!immediate && wasVisible != visible)
                AudioManager.I?.Play(AudioManager.SFX.ButtonClick);

            UpdateSettingsMenuButtonState();
        }

        void UpdateSettingsMenuButtonState()
        {
            if (_settingsMenuButtonLabel != null)
                _settingsMenuButtonLabel.text = _settingsOverlay != null && _settingsOverlay.activeSelf ? "Back" : "Menu";
        }

        void CycleCameraTiltSetting()
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
            {
                controller.SetTilt(GetWrappedSelectorValue(controller.CurrentTilt, controller.TiltMin, controller.TiltMax, settingsTiltStep));
                return;
            }

            ResolveCurrentCameraValues(out _, out float zoom, out float rotation);
            float nextTilt = GetWrappedSelectorValue(UserCameraPreferences.ResolveTilt(UserPreferencesManager.CurrentPreferenceView.camera.tilt), 0f, 52f, settingsTiltStep);
            UserPreferencesManager.NotifyCameraPreferencesChanged(nextTilt, zoom, rotation);
        }

        void CycleCameraZoomSetting()
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
            {
                controller.SetZoom(GetWrappedSelectorValue(controller.CurrentZoom, controller.ZoomMin, controller.ZoomMax, settingsZoomStep));
                return;
            }

            ResolveCurrentCameraValues(out float tilt, out _, out float rotation);
            float nextZoom = GetWrappedSelectorValue(UserCameraPreferences.ResolveZoom(UserPreferencesManager.CurrentPreferenceView.camera.zoom), 1f, 1000f, settingsZoomStep);
            UserPreferencesManager.NotifyCameraPreferencesChanged(tilt, nextZoom, rotation);
        }

        void CycleCameraRotationSetting()
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
            {
                controller.SetRotation(GetWrappedRotationSelectorValue(controller.CurrentRotation, settingsRotateStep));
                return;
            }

            ResolveCurrentCameraValues(out float tilt, out float zoom, out _);
            float nextRotation = GetWrappedRotationSelectorValue(UserCameraPreferences.ResolveRotation(UserPreferencesManager.CurrentPreferenceView.camera.rotation), settingsRotateStep);
            UserPreferencesManager.NotifyCameraPreferencesChanged(tilt, zoom, nextRotation);
        }

        void ToggleEngagementCirclesSetting()
        {
            UserPreferencesManager.SetEngagementCirclesVisible(!UserPreferencesManager.ShowEngagementCircles);
        }

        void ToggleHealthBarsSetting()
        {
            UserPreferencesManager.SetHealthBarsVisible(!UserPreferencesManager.ShowHealthBars);
        }

        void ToggleTooltipsSetting()
        {
            UserPreferencesManager.SetTooltipsVisible(!UserPreferencesManager.ShowTooltips);
        }

        void CycleSfxVolumeSetting()
        {
            float nextVolume = GetWrappedSelectorValue(UserPreferencesManager.CurrentPreferenceView.audio.sfxVolume, 0f, 1f, 0.25f);
            if (AudioManager.I != null)
                AudioManager.I.SetSFXVolume(nextVolume);
            else
                UserPreferencesManager.NotifySfxVolumeChanged(nextVolume);
        }

        void CycleMusicVolumeSetting()
        {
            float currentVolume = UserPreferencesManager.CurrentPreferenceView.audio.gameplayMusicVolume
                ?? UserPreferencesManager.CurrentPreferenceView.audio.menuMusicVolume
                ?? UserPreferencesManager.CurrentPreferenceView.audio.ambientVolume;
            float nextVolume = GetWrappedSelectorValue(currentVolume, 0f, 1f, 0.25f);
            if (AudioManager.I != null)
                AudioManager.I.SetGameplayMusicVolume(nextVolume);
            else
                UserPreferencesManager.NotifyGameplayMusicVolumeChanged(nextVolume);
        }

        void ResolveCurrentCameraValues(out float tilt, out float zoom, out float rotation)
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
            {
                tilt = controller.CurrentTilt;
                zoom = controller.CurrentZoom;
                rotation = controller.CurrentRotation;
                return;
            }

            var preferences = UserPreferencesManager.CurrentPreferenceView.camera;
            tilt = UserCameraPreferences.ResolveTilt(preferences.tilt);
            zoom = UserCameraPreferences.ResolveZoom(preferences.zoom);
            rotation = UserCameraPreferences.ResolveRotation(preferences.rotation);
        }

        static float GetWrappedSelectorValue(float current, float min, float max, float step)
        {
            if (max <= min + 0.001f)
                return min;

            step = Mathf.Max(0.01f, step);
            float clampedCurrent = Mathf.Clamp(current, min, max);
            var options = new List<float>();

            for (float value = min; value < max - 0.001f; value += step)
                options.Add(Mathf.Clamp(value, min, max));

            if (options.Count == 0 || !Mathf.Approximately(options[options.Count - 1], max))
                options.Add(max);

            int closestIndex = 0;
            float closestDelta = float.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                float delta = Mathf.Abs(options[i] - clampedCurrent);
                if (delta < closestDelta)
                {
                    closestDelta = delta;
                    closestIndex = i;
                }
            }

            return options[(closestIndex + 1) % options.Count];
        }

        static float GetWrappedRotationSelectorValue(float current, float step)
        {
            step = Mathf.Max(0.01f, step);
            var options = new List<float>();

            for (float value = 0f; value < 360f - 0.001f; value += step)
                AddUniqueRotationOption(options, value);

            AddUniqueRotationOption(options, 90f);
            AddUniqueRotationOption(options, 180f);
            AddUniqueRotationOption(options, 270f);
            AddUniqueRotationOption(options, 360f);
            options.Sort();

            float normalizedCurrent = Mathf.Repeat(current, 360f);
            if (Mathf.Approximately(normalizedCurrent, 0f) && current > 0.001f)
                normalizedCurrent = 360f;

            int closestIndex = 0;
            float closestDelta = float.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                float delta = Mathf.Abs(options[i] - normalizedCurrent);
                if (delta < closestDelta)
                {
                    closestDelta = delta;
                    closestIndex = i;
                }
            }

            return options[(closestIndex + 1) % options.Count];
        }

        static void AddUniqueRotationOption(List<float> options, float value)
        {
            float normalized = Mathf.Repeat(value, 360f);
            if (Mathf.Approximately(normalized, 0f) && value > 0.001f)
                normalized = 360f;

            for (int i = 0; i < options.Count; i++)
            {
                if (Mathf.Abs(options[i] - normalized) < 0.001f)
                    return;
            }

            options.Add(normalized);
        }

        void RefreshSettingsPanelValues()
        {
            if (_txtSettingsTiltValue == null
                && _txtSettingsZoomValue == null
                && _txtSettingsRotationValue == null
                && _txtSettingsSfxValue == null
                && _txtSettingsMusicValue == null
                && _txtSettingsEngagementValue == null
                && _txtSettingsHealthBarsValue == null
                && _txtSettingsTooltipsValue == null)
            {
                return;
            }

            var controller = FindFirstObjectByType<global::CameraController>();
            var preferences = UserPreferencesManager.CurrentPreferenceView;
            if (controller == null)
            {
                SetSettingsValue(_txtSettingsTiltValue, preferences.camera.tilt.HasValue ? FormatSettingsValue(preferences.camera.tilt.Value) : "--");
                SetSettingsValue(_txtSettingsZoomValue, preferences.camera.zoom.HasValue ? FormatSettingsValue(preferences.camera.zoom.Value) : "--");
                SetSettingsValue(_txtSettingsRotationValue, preferences.camera.rotation.HasValue ? FormatSettingsValue(preferences.camera.rotation.Value) : "--");
            }
            else
            {
                SetSettingsValue(_txtSettingsTiltValue, FormatSettingsValue(controller.CurrentTilt));
                SetSettingsValue(_txtSettingsZoomValue, FormatSettingsValue(controller.CurrentZoom));
                SetSettingsValue(_txtSettingsRotationValue, FormatSettingsValue(controller.CurrentRotation));
            }

            SetSettingsValue(_txtSettingsSfxValue, FormatVolumeValue(preferences.audio.sfxVolume));
            SetSettingsValue(
                _txtSettingsMusicValue,
                FormatVolumeValue(preferences.audio.gameplayMusicVolume ?? preferences.audio.menuMusicVolume ?? preferences.audio.ambientVolume));
            SetSettingsValue(_txtSettingsEngagementValue, FormatToggleValue(preferences.visuals.showEngagementCircles));
            SetSettingsValue(_txtSettingsHealthBarsValue, FormatToggleValue(preferences.visuals.showHealthBars));
            SetSettingsValue(_txtSettingsTooltipsValue, FormatToggleValue(preferences.visuals.showTooltips));
            UpdateSettingsMenuButtonState();
        }

        static void SetSettingsValue(TMP_Text label, string value)
        {
            if (label != null)
                label.text = value;
        }

        static string FormatSettingsValue(float value)
        {
            float roundedValue = Mathf.Round(value * 10f) * 0.1f;
            if (Mathf.Approximately(roundedValue, Mathf.Round(roundedValue)))
                return Mathf.RoundToInt(roundedValue).ToString();

            return roundedValue.ToString("0.#");
        }

        float ResolveSettingsRowsHeight(int rowCount)
        {
            if (rowCount <= 0)
                return 0f;

            const float rowHeight = 42f;
            return (rowCount * rowHeight) + ((rowCount - 1) * settingsButtonSpacing);
        }

        float ResolveSettingsPanelHeight(float rowsHeight, float quitButtonHeight)
        {
            const float titleHeight = 16f;
            const float verticalPadding = 20f;
            const float layoutGaps = 12f;
            return rowsHeight + quitButtonHeight + titleHeight + verticalPadding + layoutGaps;
        }

        static string FormatToggleValue(bool enabled)
        {
            return enabled ? "On" : "Off";
        }

        static string FormatVolumeValue(float linear)
        {
            if (linear <= 0.01f)
                return "Off";

            return $"{Mathf.RoundToInt(Mathf.Clamp01(linear) * 100f)}%";
        }

        void OnLogoutPressed()
        {
            if (LoadingScreen.IsTransitionInProgress)
                return;

            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            ShowSettingsConfirmation(SettingsConfirmationAction.Logout);
        }

        void OnQuitPressed()
        {
            if (LoadingScreen.IsTransitionInProgress)
                return;

            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            ShowSettingsConfirmation(SettingsConfirmationAction.QuitGame);
        }

        void ShowSettingsConfirmation(SettingsConfirmationAction action)
        {
            if (_quitConfirmationModal == null)
                BuildQuitConfirmationModal();

            if (_quitConfirmationModal == null)
                return;

            _pendingSettingsConfirmationAction = action;
            _isLeavingMatch = false;
            if (_quitConfirmationTitleLabel != null)
            {
                _quitConfirmationTitleLabel.text = action switch
                {
                    SettingsConfirmationAction.Logout => "Log Out",
                    SettingsConfirmationAction.QuitGame => "Quit Game",
                    _ => "Confirm Action",
                };
            }

            if (_quitConfirmationBodyLabel != null)
            {
                _quitConfirmationBodyLabel.text = action switch
                {
                    SettingsConfirmationAction.Logout => "Leave the current match, clear your commander session, and return to sign-in?",
                    SettingsConfirmationAction.QuitGame => "Leave the battlefield and close the game?",
                    _ => "Confirm this action.",
                };
            }

            if (_quitConfirmationConfirmLabel != null)
                _quitConfirmationConfirmLabel.text = action switch
                {
                    SettingsConfirmationAction.Logout => "Log Out",
                    SettingsConfirmationAction.QuitGame => "Quit Game",
                    _ => "Confirm",
                };
            if (_quitConfirmationConfirmButton != null)
                _quitConfirmationConfirmButton.interactable = true;
            if (_quitConfirmationCancelButton != null)
                _quitConfirmationCancelButton.interactable = true;
            _quitConfirmationModal.SetActive(true);
            _quitConfirmationModal.transform.SetAsLastSibling();
        }

        void HideQuitConfirmation()
        {
            if (_quitConfirmationModal != null)
                _quitConfirmationModal.SetActive(false);

            _pendingSettingsConfirmationAction = SettingsConfirmationAction.None;
            _isLeavingMatch = false;
            if (_quitConfirmationTitleLabel != null)
                _quitConfirmationTitleLabel.text = "Confirm Action";
            if (_quitConfirmationBodyLabel != null)
                _quitConfirmationBodyLabel.text = "Confirm this action.";
            if (_quitConfirmationConfirmLabel != null)
                _quitConfirmationConfirmLabel.text = "Confirm";
            if (_quitConfirmationConfirmButton != null)
                _quitConfirmationConfirmButton.interactable = true;
            if (_quitConfirmationCancelButton != null)
                _quitConfirmationCancelButton.interactable = true;
        }

        void ConfirmSettingsAction()
        {
            if (_isLeavingMatch)
                return;

            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            _isLeavingMatch = true;
            if (_quitConfirmationConfirmLabel != null)
                _quitConfirmationConfirmLabel.text = _pendingSettingsConfirmationAction switch
                {
                    SettingsConfirmationAction.Logout => "Logging Out...",
                    SettingsConfirmationAction.QuitGame => "Quitting...",
                    _ => "Working...",
                };
            if (_quitConfirmationConfirmButton != null)
                _quitConfirmationConfirmButton.interactable = false;
            if (_quitConfirmationCancelButton != null)
                _quitConfirmationCancelButton.interactable = false;

            if (_pendingSettingsConfirmationAction == SettingsConfirmationAction.Logout)
            {
                SetSettingsOverlayVisible(false, immediate: true);
                ClearReconnectPrefs();
                NetworkManager.Instance?.Emit("leave_game", null);
                AuthManager.BeginLogout(NetworkManager.Instance != null ? NetworkManager.Instance.ResolvedServerUrl : null);
                NetworkManager.Instance?.ReconnectForCurrentAuth("match logout");
                Debug.Log("[MobileMatchHud] Logout confirmed. Returning to Login.");
                LoadingScreen.LoadScene("Login");
                return;
            }

            if (_pendingSettingsConfirmationAction == SettingsConfirmationAction.QuitGame)
            {
                SetSettingsOverlayVisible(false, immediate: true);
                TryQuitGameFromMatch();
                return;
            }

            HideQuitConfirmation();
        }

        void CancelSettingsAction()
        {
            if (_isLeavingMatch)
                return;

            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            HideQuitConfirmation();
        }

        static void ClearReconnectPrefs()
        {
            PlayerPrefs.DeleteKey("reconnect_token");
            PlayerPrefs.DeleteKey("reconnect_code");
            PlayerPrefs.DeleteKey("reconnect_lane");
            PlayerPrefs.DeleteKey("reconnect_gametype");
            PlayerPrefs.Save();
        }

        static void TryQuitGameFromMatch()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogWarning("[MobileMatchHud] Quit Game is not supported on WebGL. Close the browser tab to leave the game.");
#elif UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void CreateWaveStatusChip(Transform parent, string name, string tag, string value, Color accentColor)
        {
            var chip = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            chip.transform.SetParent(parent, false);
            var chipRect = chip.GetComponent<RectTransform>();
            chipRect.sizeDelta = new Vector2(40f, 22f);
            var chipImage = chip.GetComponent<Image>();
            chipImage.color = new Color(0.11f, 0.15f, 0.19f, 0.98f);

            var layout = chip.GetComponent<LayoutElement>();
            layout.preferredWidth = 40f;
            layout.preferredHeight = 22f;
            layout.flexibleWidth = 0f;

            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(chip.transform, false);
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 2f);
            accentRect.anchoredPosition = Vector2.zero;
            accent.GetComponent<Image>().color = accentColor;

            var tagLabel = CreateText(chip.transform, "Tag", tag, 8, TextAlignmentOptions.Top, accentColor);
            tagLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            tagLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            tagLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            tagLabel.rectTransform.sizeDelta = new Vector2(0f, 10f);
            tagLabel.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            tagLabel.fontStyle = FontStyles.SmallCaps;

            var valueLabel = CreateText(chip.transform, "Value", value, 11, TextAlignmentOptions.Bottom, Color.white);
            valueLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            valueLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueLabel.rectTransform.offsetMin = new Vector2(0f, 4f);
            valueLabel.rectTransform.offsetMax = new Vector2(0f, -2f);
        }

        Image CreateWaveSideBar(Transform parent, string name, bool leftAligned, Color shellColor, Color fillColor, out TMP_Text valueLabel, out TMP_Text sideLabel)
        {
            var shell = new GameObject(name, typeof(RectTransform), typeof(Image));
            shell.transform.SetParent(parent, false);
            var shellRect = shell.GetComponent<RectTransform>();
            shellRect.anchorMin = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            shellRect.anchorMax = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            shellRect.pivot = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            shellRect.sizeDelta = new Vector2(166f, 48f);
            shellRect.anchoredPosition = new Vector2(leftAligned ? 18f : -18f, 10f);
            shell.GetComponent<Image>().color = shellColor;

            var portrait = new GameObject("SideBadge", typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(shell.transform, false);
            var portraitRect = portrait.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            portraitRect.anchorMax = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            portraitRect.pivot = new Vector2(leftAligned ? 0f : 1f, 0.5f);
            portraitRect.sizeDelta = new Vector2(42f, 42f);
            portraitRect.anchoredPosition = new Vector2(leftAligned ? -8f : 8f, 0f);
            portrait.GetComponent<Image>().color = new Color(0.16f, 0.20f, 0.24f, 1f);

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(shell.transform, false);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(-52f, 18f);
            trackRect.anchoredPosition = new Vector2(leftAligned ? 16f : -16f, 0f);
            track.GetComponent<Image>().color = new Color(0.11f, 0.14f, 0.18f, 0.98f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = leftAligned ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
            fillImage.fillAmount = leftAligned ? 0.96f : 0.94f;

            valueLabel = CreateText(track.transform, "Value", leftAligned ? "96%" : "94%", 13, leftAligned ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, Color.white);
            valueLabel.rectTransform.anchorMin = Vector2.zero;
            valueLabel.rectTransform.anchorMax = Vector2.one;
            valueLabel.rectTransform.offsetMin = new Vector2(8f, 0f);
            valueLabel.rectTransform.offsetMax = new Vector2(-8f, 0f);

            sideLabel = CreateText(shell.transform, "TeamLabel", leftAligned ? "LEFT" : "RIGHT", 10, leftAligned ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, new Color(0.84f, 0.90f, 0.96f, 0.88f));
            sideLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            sideLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            sideLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            sideLabel.rectTransform.sizeDelta = new Vector2(-56f, 12f);
            sideLabel.rectTransform.anchoredPosition = new Vector2(leftAligned ? 14f : -14f, 2f);

            return fillImage;
        }

        CollapsibleHudCard CreateStatsCard(Transform parent, string name, string title, bool startCollapsed, float preferredHeight, Color headerColor, Color accentColor, out TMP_Text bodyLabel)
        {
            var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(CollapsibleHudCard));
            card.transform.SetParent(parent, false);
            var rect = card.GetComponent<RectTransform>();
            rect.pivot = new Vector2(1f, 1f);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);

            var image = card.GetComponent<Image>();
            image.color = new Color(0.04f, 0.06f, 0.08f, 0.88f);
            ApplyPanelFrame(card, image.color, accentColor);

            var layout = card.GetComponent<LayoutElement>();
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
            layout.minWidth = GetExpandedCardWidth();
            layout.preferredWidth = GetExpandedCardWidth();
            layout.flexibleHeight = 0f;

            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(card.transform, false);
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -34f);
            headerRect.offsetMax = new Vector2(0f, 0f);
            header.GetComponent<Image>().color = headerColor;

            var titleLabel = CreateText(header.transform, "Title", title, 16, TextAlignmentOptions.Left, Color.white);
            var titleRect = titleLabel.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(14f, 0f);
            titleRect.offsetMax = new Vector2(-44f, 0f);

            var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggle.transform.SetParent(header.transform, false);
            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 0.5f);
            toggleRect.anchorMax = new Vector2(1f, 0.5f);
            toggleRect.pivot = new Vector2(1f, 0.5f);
            toggleRect.sizeDelta = new Vector2(30f, 30f);
            toggleRect.anchoredPosition = new Vector2(-4f, 0f);
            toggle.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.28f, 0.95f);

            var toggleLabel = CreateText(toggle.transform, "ToggleLabel", ">", 18, TextAlignmentOptions.Center, Color.white);
            var toggleLabelRect = toggleLabel.rectTransform;
            toggleLabelRect.anchorMin = Vector2.zero;
            toggleLabelRect.anchorMax = Vector2.one;
            toggleLabelRect.offsetMin = Vector2.zero;
            toggleLabelRect.offsetMax = Vector2.zero;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(CanvasGroup));
            viewport.transform.SetParent(card.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = new Vector2(12f, 12f);
            viewportRect.offsetMax = new Vector2(-12f, -42f);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);

            bodyLabel = CreateText(content.transform, "Body", "--", 13, TextAlignmentOptions.TopLeft, new Color(0.92f, 0.95f, 0.98f, 0.96f));
            bodyLabel.textWrappingMode = TextWrappingModes.Normal;
            bodyLabel.overflowMode = TextOverflowModes.Overflow;
            bodyLabel.fontStyle = FontStyles.Normal;
            var bodyRect = bodyLabel.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;
            var fitter = bodyLabel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = viewport.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 18f;

            var cardComp = card.GetComponent<CollapsibleHudCard>();
            ApplyCardConfig(cardComp, rect, headerRect, viewportRect, toggle.GetComponent<Button>(), toggleLabel, titleLabel, image, layout, viewport.GetComponent<CanvasGroup>(), startCollapsed);
            return cardComp;
        }

        void ApplyCardConfig(CollapsibleHudCard card, RectTransform rect, RectTransform headerRoot, RectTransform contentRoot, Button toggle, TMP_Text toggleLabel, TMP_Text titleLabel, Graphic background, LayoutElement layout, CanvasGroup canvasGroup, bool startCollapsed)
        {
            card.Configure(
                rect,
                contentRoot,
                headerRoot,
                toggle,
                toggleLabel,
                titleLabel,
                background,
                layout,
                canvasGroup,
                GetExpandedCardWidth(),
                28f,
                startCollapsed);
        }

        float GetExpandedCardWidth()
        {
            return Mathf.Max(120f, rightRailWidth - (rightRailPadding.x * 2f));
        }

        float GetEffectiveRightRailTopInset()
        {
            float reservedByStatBar = statBarTopInset + statBarHeight + statBarBottomGap;
            return Mathf.Max(rightRailTopInset, reservedByStatBar);
        }

        RectTransform CreateRibbonModule(string name, float preferredWidth, Color backgroundColor, Color accentColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            go.transform.SetParent(transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(preferredWidth, ribbonHeight - 16f);

            var img = go.GetComponent<Image>();
            img.color = backgroundColor;
            ApplyPanelFrame(go, backgroundColor, accentColor);

            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.flexibleWidth = 0f;

            var vertical = go.GetComponent<VerticalLayoutGroup>();
            vertical.childAlignment = TextAnchor.UpperLeft;
            vertical.childControlWidth = true;
            vertical.childControlHeight = false;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            vertical.spacing = 1f;
            vertical.padding = new RectOffset(10, 10, 7, 7);

            return rect;
        }

        TMP_Text CreateCaptionLabel(Transform parent, string text)
        {
            var label = CreateText(parent, "Caption", text, 13, TextAlignmentOptions.Center, new Color(0.78f, 0.82f, 0.88f, 0.88f));
            label.fontStyle = FontStyles.SmallCaps;
            return label;
        }

        TMP_Text CreateValueLabel(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment, Color? color = null)
        {
            return CreateText(parent, name, text, fontSize, alignment, color ?? Color.white);
        }

        Image CreateProgressBar(Transform parent, string name, Color fillColor)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 14f);

            var layout = root.GetComponent<LayoutElement>();
            layout.preferredHeight = 14f;
            layout.minHeight = 14f;

            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(root.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var image = fill.GetComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.color = fillColor;
            return image;
        }

        Image CreateSegmentedBar(Transform parent, string name, Color fillColor)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(2f, 0f);
            rootRect.offsetMax = new Vector2(-2f, 0f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(root.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var image = fill.GetComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.color = fillColor;

            for (int i = 1; i < 8; i++)
            {
                var divider = new GameObject($"Divider{i}", typeof(RectTransform), typeof(Image));
                divider.transform.SetParent(root.transform, false);
                var dividerRect = divider.GetComponent<RectTransform>();
                dividerRect.anchorMin = new Vector2(i / 8f, 0f);
                dividerRect.anchorMax = new Vector2(i / 8f, 1f);
                dividerRect.sizeDelta = new Vector2(1f, 0f);
                divider.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);
            }

            return image;
        }

        void EnsureMinimumFontSize(TMP_Text text, float minimum = HudWidgetMinimumFontSize)
        {
            if (text == null)
                return;

            text.fontSize = Mathf.Max(text.fontSize, minimum);
            if (text.enableAutoSizing)
            {
                text.fontSizeMin = Mathf.Max(text.fontSizeMin, minimum);
                text.fontSizeMax = Mathf.Max(text.fontSizeMax, text.fontSize);
            }
        }

        static void ConfigureSingleLineText(TMP_Text text)
        {
            if (text == null)
                return;

            text.enableWordWrapping = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        static bool HasSavedHudPanelPosition(string prefsKey)
        {
            return !string.IsNullOrWhiteSpace(prefsKey)
                && PlayerPrefs.HasKey($"{prefsKey}.x")
                && PlayerPrefs.HasKey($"{prefsKey}.y");
        }

        static bool IsWithinHudSnapThreshold(Vector2 current, Vector2 target)
        {
            return Mathf.Abs(current.x - target.x) <= HudDockSnapThreshold
                && Mathf.Abs(current.y - target.y) <= HudDockSnapThreshold;
        }

        void BindHudDockCallbacks()
        {
            if (_myStatsWidget != null)
            {
                _myStatsWidget.LayoutCommitted -= HandleMyStatsLayoutCommitted;
                _myStatsWidget.LayoutCommitted += HandleMyStatsLayoutCommitted;
            }

            if (_waveOverviewWidget != null)
            {
                _waveOverviewWidget.LayoutCommitted -= HandleWaveOverviewLayoutCommitted;
                _waveOverviewWidget.LayoutCommitted += HandleWaveOverviewLayoutCommitted;
            }

            if (_miniMapDockPanel != null)
            {
                _miniMapDockPanel.LayoutCommitted -= HandleMiniMapLayoutCommitted;
                _miniMapDockPanel.LayoutCommitted += HandleMiniMapLayoutCommitted;
            }
        }

        void ForceApplySafeAreaLayout()
        {
            _lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
            _lastCanvasSize = new Vector2(-1f, -1f);
            ApplySafeAreaLayout();
        }

        bool TryGetSafeAreaInsets(out float leftInset, out float rightInset, out float topInset, out float bottomInset)
        {
            leftInset = 0f;
            rightInset = 0f;
            topInset = 0f;
            bottomInset = 0f;

            if (_canvasRect == null)
                return false;

            float canvasWidth = _canvasRect.rect.width;
            float canvasHeight = _canvasRect.rect.height;
            if (canvasWidth <= 0f || canvasHeight <= 0f || Screen.width <= 0f || Screen.height <= 0f)
                return false;

            var safeArea = Screen.safeArea;
            leftInset = safeArea.xMin * (canvasWidth / Screen.width);
            rightInset = (Screen.width - safeArea.xMax) * (canvasWidth / Screen.width);
            topInset = (Screen.height - safeArea.yMax) * (canvasHeight / Screen.height);
            bottomInset = safeArea.yMin * (canvasHeight / Screen.height);
            return true;
        }

        bool TryGetMyStatsDockPosition(float leftInset, float rightInset, float topInset, out Vector2 anchoredPosition)
        {
            anchoredPosition = Vector2.zero;

            if (_canvasRect == null || _myStatsWidget == null)
                return false;

            var statsRect = _myStatsWidget.WidgetRect;
            if (statsRect == null)
                return false;

            float anchoredY = -(settingsTopInset + topInset);
            float anchoredX;

            var miniMapRect = _miniMapWidget != null ? _miniMapWidget.transform as RectTransform : null;
            if (showMiniMapWidget && miniMapRect != null)
            {
                float miniMapLeft = _canvasRect.rect.width + miniMapRect.anchoredPosition.x - miniMapRect.rect.width;
                anchoredX = miniMapLeft - statsRect.rect.width - HudDockGap;
                anchoredY = miniMapRect.anchoredPosition.y;
            }
            else
            {
                float menuButtonWidth = Mathf.Max(settingsButtonSize + 28f, 74f);
                anchoredX = _canvasRect.rect.width - rightInset - settingsRightInset - menuButtonWidth - HudDockGap - statsRect.rect.width;
            }

            anchoredPosition = new Vector2(
                Mathf.Max(12f + leftInset, anchoredX),
                anchoredY);
            return true;
        }

        bool TryGetWaveOverviewDockLayout(
            float leftInset,
            float rightInset,
            float topInset,
            float bottomInset,
            out Vector2 anchoredPosition,
            out float targetWidth,
            out float reservedRightMargin)
        {
            anchoredPosition = Vector2.zero;
            targetWidth = 0f;
            reservedRightMargin = rightInset;

            if (_canvasRect == null || _waveOverviewWidget == null)
                return false;

            const float leftGap = 14f;
            float defaultLeft = leftGap + leftInset;
            float maxRightEdge = _canvasRect.rect.width - rightInset - HudDockGap;
            float anchoredY = -10f - topInset;

            var statsRect = _myStatsWidget != null ? _myStatsWidget.WidgetRect : null;
            if (statsRect != null)
            {
                maxRightEdge = Mathf.Min(maxRightEdge, statsRect.anchoredPosition.x - HudDockGap);
                anchoredY = statsRect.anchoredPosition.y;
            }
            else
            {
                var miniMapRect = _miniMapWidget != null ? _miniMapWidget.transform as RectTransform : null;
                if (showMiniMapWidget && miniMapRect != null)
                {
                    maxRightEdge = Mathf.Min(maxRightEdge, _canvasRect.rect.width + miniMapRect.anchoredPosition.x - miniMapRect.rect.width - HudDockGap);
                    anchoredY = miniMapRect.anchoredPosition.y;
                }
            }

            float availableWidth = Mathf.Max(0f, maxRightEdge - defaultLeft);
            if (availableWidth <= 0f)
                return false;

            targetWidth = availableWidth < 420f
                ? availableWidth
                : Mathf.Min(availableWidth, 580f);
            reservedRightMargin = Mathf.Max(rightInset, _canvasRect.rect.width - maxRightEdge - HudDockGap);
            anchoredPosition = new Vector2(
                Mathf.Max(defaultLeft, maxRightEdge - targetWidth),
                anchoredY);
            return true;
        }

        void ApplyWaveOverviewDockSnap(bool persist)
        {
            if (!TryGetSafeAreaInsets(out float leftInset, out float rightInset, out float topInset, out float bottomInset))
                return;

            if (!TryGetWaveOverviewDockLayout(leftInset, rightInset, topInset, bottomInset, out var targetPosition, out float targetWidth, out float reservedRightMargin))
                return;

            _waveOverviewWidget.SetExpandedSize(new Vector2(targetWidth, 82f));
            _waveOverviewWidget.SetCollapsedSize(new Vector2(92f, 60f));
            _waveOverviewWidget.SetClampMargins(leftInset, topInset, reservedRightMargin, bottomInset);

            if (_waveOverviewDockedToMyStats || !HasSavedHudPanelPosition(WaveOverviewWidgetPrefsKey))
                _waveOverviewWidget.SetAnchoredPosition(targetPosition, persist);
        }

        void SyncHudDockStatesFromCurrentLayout(float leftInset, float rightInset, float topInset, float bottomInset)
        {
            var statsRect = _myStatsWidget != null ? _myStatsWidget.WidgetRect : null;
            if (statsRect != null && showMiniMapWidget && _miniMapWidget != null && TryGetMyStatsDockPosition(leftInset, rightInset, topInset, out var statsTarget))
                _myStatsDockedToMiniMap = IsWithinHudSnapThreshold(statsRect.anchoredPosition, statsTarget);
            else
                _myStatsDockedToMiniMap = false;

            var waveRect = _waveOverviewWidget != null ? _waveOverviewWidget.WidgetRect : null;
            if (waveRect != null && _myStatsWidget != null && TryGetWaveOverviewDockLayout(leftInset, rightInset, topInset, bottomInset, out var waveTarget, out _, out _))
                _waveOverviewDockedToMyStats = IsWithinHudSnapThreshold(waveRect.anchoredPosition, waveTarget);
            else
                _waveOverviewDockedToMyStats = false;
        }

        void HandleMiniMapLayoutCommitted(HudPanelLayoutChangeKind changeKind)
        {
            if (_myStatsWidget != null
                && TryGetSafeAreaInsets(out float leftInset, out float rightInset, out float topInset, out _)
                && TryGetMyStatsDockPosition(leftInset, rightInset, topInset, out var statsTarget))
            {
                var statsRect = _myStatsWidget.WidgetRect;
                _myStatsDockedToMiniMap = showMiniMapWidget
                    && _miniMapWidget != null
                    && (_myStatsDockedToMiniMap || IsWithinHudSnapThreshold(statsRect.anchoredPosition, statsTarget));

                if (_myStatsDockedToMiniMap)
                    _myStatsWidget.SetAnchoredPosition(statsTarget, true);
            }

            if (_waveOverviewWidget != null)
                ApplyWaveOverviewDockSnap(true);
        }

        void HandleMyStatsLayoutCommitted(HudPanelLayoutChangeKind changeKind)
        {
            if (_myStatsWidget != null
                && TryGetSafeAreaInsets(out float leftInset, out float rightInset, out float topInset, out _)
                && TryGetMyStatsDockPosition(leftInset, rightInset, topInset, out var statsTarget))
            {
                var statsRect = _myStatsWidget.WidgetRect;
                _myStatsDockedToMiniMap = showMiniMapWidget
                    && _miniMapWidget != null
                    && (changeKind == HudPanelLayoutChangeKind.CollapseChanged
                        ? _myStatsDockedToMiniMap
                        : IsWithinHudSnapThreshold(statsRect.anchoredPosition, statsTarget));

                if (_myStatsDockedToMiniMap)
                    _myStatsWidget.SetAnchoredPosition(statsTarget, true);
            }
            else
            {
                _myStatsDockedToMiniMap = false;
            }

            if (_waveOverviewWidget != null
                && TryGetSafeAreaInsets(out float waveLeftInset, out float waveRightInset, out float waveTopInset, out float waveBottomInset)
                && TryGetWaveOverviewDockLayout(waveLeftInset, waveRightInset, waveTopInset, waveBottomInset, out var waveTarget, out float targetWidth, out float reservedRightMargin))
            {
                _waveOverviewWidget.SetExpandedSize(new Vector2(targetWidth, 82f));
                _waveOverviewWidget.SetCollapsedSize(new Vector2(92f, 60f));
                _waveOverviewWidget.SetClampMargins(waveLeftInset, waveTopInset, reservedRightMargin, waveBottomInset);

                var waveRect = _waveOverviewWidget.WidgetRect;
                _waveOverviewDockedToMyStats = _myStatsWidget != null
                    && ((changeKind == HudPanelLayoutChangeKind.CollapseChanged && _waveOverviewDockedToMyStats)
                        || _waveOverviewDockedToMyStats
                        || IsWithinHudSnapThreshold(waveRect.anchoredPosition, waveTarget));

                if (_waveOverviewDockedToMyStats)
                    _waveOverviewWidget.SetAnchoredPosition(waveTarget, true);
            }
        }

        void HandleWaveOverviewLayoutCommitted(HudPanelLayoutChangeKind changeKind)
        {
            if (_waveOverviewWidget == null || !TryGetSafeAreaInsets(out float leftInset, out float rightInset, out float topInset, out float bottomInset))
                return;

            if (!TryGetWaveOverviewDockLayout(leftInset, rightInset, topInset, bottomInset, out var targetPosition, out float targetWidth, out float reservedRightMargin))
            {
                _waveOverviewDockedToMyStats = false;
                return;
            }

            _waveOverviewWidget.SetExpandedSize(new Vector2(targetWidth, 82f));
            _waveOverviewWidget.SetCollapsedSize(new Vector2(92f, 60f));
            _waveOverviewWidget.SetClampMargins(leftInset, topInset, reservedRightMargin, bottomInset);

            var waveRect = _waveOverviewWidget.WidgetRect;
            _waveOverviewDockedToMyStats = _myStatsWidget != null
                && (changeKind == HudPanelLayoutChangeKind.CollapseChanged
                    ? _waveOverviewDockedToMyStats
                    : IsWithinHudSnapThreshold(waveRect.anchoredPosition, targetPosition));

            if (_waveOverviewDockedToMyStats)
                _waveOverviewWidget.SetAnchoredPosition(targetPosition, true);
        }

        void ApplyWaveOverviewDockLayout(float leftInset, float rightInset, float topInset, float bottomInset)
        {
            if (_canvasRect == null || _waveOverviewWidget == null)
                return;

            var waveRect = _waveOverviewWidget.WidgetRect;
            if (waveRect == null)
                return;

            if (!TryGetWaveOverviewDockLayout(leftInset, rightInset, topInset, bottomInset, out var targetPosition, out float targetWidth, out float reservedRightMargin))
                return;

            _waveOverviewWidget.SetExpandedSize(new Vector2(targetWidth, 82f));
            _waveOverviewWidget.SetCollapsedSize(new Vector2(92f, 60f));
            _waveOverviewWidget.SetClampMargins(leftInset, topInset, reservedRightMargin, bottomInset);

            if (!HasSavedHudPanelPosition(WaveOverviewWidgetPrefsKey) || _waveOverviewDockedToMyStats)
                waveRect.anchoredPosition = targetPosition;
        }

        void ApplyMiniMapDockLayout(float leftInset, float rightInset, float topInset, float bottomInset)
        {
            if (_miniMapWidget == null)
                return;

            var panel = _miniMapWidget.GetComponent<DraggableHudPanel>();
            var miniMapRect = _miniMapWidget.transform as RectTransform;
            if (panel == null || miniMapRect == null)
                return;

            panel.SetClampMargins(leftInset, topInset, rightInset, bottomInset);

            if (!HasSavedHudPanelPosition(MiniMapWidgetPrefsKey))
            {
                float menuButtonWidth = Mathf.Max(settingsButtonSize + 28f, 74f);
                miniMapRect.anchoredPosition = new Vector2(
                    -(settingsRightInset + rightInset + menuButtonWidth + 8f),
                    -(settingsTopInset + topInset));
            }
        }

        void ApplyMyStatsDockLayout(float leftInset, float rightInset, float topInset)
        {
            if (_canvasRect == null || _myStatsWidget == null)
                return;

            var statsRect = _myStatsWidget.WidgetRect;
            if (statsRect == null)
                return;

            if (!TryGetMyStatsDockPosition(leftInset, rightInset, topInset, out var targetPosition))
                return;

            if (!HasSavedHudPanelPosition(MyStatsWidgetPrefsKey) || _myStatsDockedToMiniMap)
                statsRect.anchoredPosition = targetPosition;
        }

        void CreateLinkedStatPair(Transform parent, string name,
            Color leftColor, string leftLabel, out TMP_Text leftValue,
            Color rightColor, string rightLabel, out TMP_Text rightValue)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rootLayoutElement = root.GetComponent<LayoutElement>();
            rootLayoutElement.preferredHeight = 18f;
            rootLayoutElement.minHeight = 18f;

            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            layout.padding = new RectOffset(0, 0, 0, 0);

            var leftGroup = new GameObject("LeftGroup", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            leftGroup.transform.SetParent(root.transform, false);
            var leftGroupLayoutElement = leftGroup.GetComponent<LayoutElement>();
            leftGroupLayoutElement.preferredWidth = 92f;
            leftGroupLayoutElement.minWidth = 92f;
            leftGroupLayoutElement.preferredHeight = 18f;
            var leftLayout = leftGroup.GetComponent<HorizontalLayoutGroup>();
            leftLayout.childAlignment = TextAnchor.MiddleLeft;
            leftLayout.childControlWidth = false;
            leftLayout.childControlHeight = true;
            leftLayout.childForceExpandWidth = false;
            leftLayout.childForceExpandHeight = false;
            leftLayout.spacing = 3f;
            leftLayout.padding = new RectOffset(0, 0, 0, 0);

            var leftIcon = new GameObject("LeftIcon", typeof(RectTransform), typeof(Image));
            leftIcon.transform.SetParent(leftGroup.transform, false);
            leftIcon.GetComponent<RectTransform>().sizeDelta = new Vector2(7f, 7f);
            leftIcon.GetComponent<Image>().color = leftColor;

            var leftTag = CreateText(leftGroup.transform, "LeftTag", leftLabel, 12, TextAlignmentOptions.Left, leftColor);
            EnsureMinimumFontSize(leftTag);
            leftTag.fontStyle = FontStyles.SmallCaps;
            leftTag.textWrappingMode = TextWrappingModes.NoWrap;
            leftTag.overflowMode = TextOverflowModes.Overflow;
            leftTag.rectTransform.sizeDelta = new Vector2(28f, 16f);

            leftValue = CreateText(leftGroup.transform, "LeftValue", "0", 15, TextAlignmentOptions.Left, new Color(0.94f, 0.96f, 0.99f, 1f));
            leftValue.fontStyle = FontStyles.Bold;
            leftValue.rectTransform.sizeDelta = new Vector2(56f, 18f);
            leftValue.enableAutoSizing = true;
            EnsureMinimumFontSize(leftValue);

            var arrow = CreateText(root.transform, "Arrow", "<", 12, TextAlignmentOptions.Center, new Color(0.55f, 0.76f, 0.96f, 0.96f));
            EnsureMinimumFontSize(arrow);
            arrow.rectTransform.sizeDelta = new Vector2(14f, 18f);

            var rightGroup = new GameObject("RightGroup", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rightGroup.transform.SetParent(root.transform, false);
            var rightGroupLayoutElement = rightGroup.GetComponent<LayoutElement>();
            rightGroupLayoutElement.preferredWidth = 92f;
            rightGroupLayoutElement.minWidth = 92f;
            rightGroupLayoutElement.preferredHeight = 18f;
            var rightLayout = rightGroup.GetComponent<HorizontalLayoutGroup>();
            rightLayout.childAlignment = TextAnchor.MiddleLeft;
            rightLayout.childControlWidth = false;
            rightLayout.childControlHeight = true;
            rightLayout.childForceExpandWidth = false;
            rightLayout.childForceExpandHeight = false;
            rightLayout.spacing = 3f;
            rightLayout.padding = new RectOffset(0, 0, 0, 0);

            var rightIcon = new GameObject("RightIcon", typeof(RectTransform), typeof(Image));
            rightIcon.transform.SetParent(rightGroup.transform, false);
            rightIcon.GetComponent<RectTransform>().sizeDelta = new Vector2(7f, 7f);
            rightIcon.GetComponent<Image>().color = rightColor;

            var rightTag = CreateText(rightGroup.transform, "RightTag", rightLabel, 12, TextAlignmentOptions.Left, rightColor);
            EnsureMinimumFontSize(rightTag);
            rightTag.fontStyle = FontStyles.SmallCaps;
            rightTag.textWrappingMode = TextWrappingModes.NoWrap;
            rightTag.overflowMode = TextOverflowModes.Overflow;
            rightTag.rectTransform.sizeDelta = new Vector2(28f, 16f);

            rightValue = CreateText(rightGroup.transform, "RightValue", "0", 15, TextAlignmentOptions.Left, new Color(0.94f, 0.96f, 0.99f, 1f));
            rightValue.fontStyle = FontStyles.Bold;
            rightValue.rectTransform.sizeDelta = new Vector2(56f, 18f);
            rightValue.enableAutoSizing = true;
            EnsureMinimumFontSize(rightValue);
        }

        void CreateMiniStat(Transform parent, string name, string tag, Color accentColor, out TMP_Text valueLabel)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.20f, 0.86f);
            root.GetComponent<LayoutElement>().preferredWidth = 39f;

            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            layout.padding = new RectOffset(2, 2, 3, 3);

            var tagLabel = CreateText(root.transform, "Tag", tag, 9, TextAlignmentOptions.Center, accentColor);
            tagLabel.fontStyle = FontStyles.SmallCaps;
            valueLabel = CreateText(root.transform, "Value", "0", 16, TextAlignmentOptions.Center, Color.white);
        }

        TMP_Text CreateText(Transform parent, string name, string value, int fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = Mathf.RoundToInt(fontSize * GetFontScale());
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = FontStyles.Bold;
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        void StyleCommandMenuSurface(GameObject target, Color backgroundColor, Color frameTint)
        {
            if (target == null)
                return;

            var image = target.GetComponent<Image>();
            if (image != null)
            {
                image.color = backgroundColor;
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PortraitBackdrop, true, backgroundColor);
            }

            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
                shadow = target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.30f);
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;

            EnsureCommandMenuFrame(target.transform as RectTransform, frameTint);
        }

        void StyleSettingsBuildingCard(GameObject target, Color backgroundColor, Color accentColor, bool highlighted)
        {
            if (target == null)
                return;

            var image = target.GetComponent<Image>();
            if (image != null)
            {
                image.color = backgroundColor;
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PaperMedium, true, backgroundColor);
            }

            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
                shadow = target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, highlighted ? 0.40f : 0.32f);
            shadow.effectDistance = highlighted ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;

            var outline = target.GetComponent<Outline>();
            if (outline == null)
                outline = target.AddComponent<Outline>();
            outline.effectColor = highlighted
                ? new Color(0.97f, 0.80f, 0.42f, 0.96f)
                : new Color(backgroundColor.r * 0.58f, backgroundColor.g * 0.58f, backgroundColor.b * 0.58f, 0.92f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            EnsureSettingsCardAccent(target.transform as RectTransform, accentColor);
        }

        static void EnsureSettingsCardAccent(RectTransform rect, Color tint)
        {
            if (rect == null)
                return;

            var accent = rect.Find("CardAccent") as RectTransform;
            if (accent == null)
            {
                var go = new GameObject("CardAccent", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.transform.SetParent(rect, false);
                accent = go.GetComponent<RectTransform>();
            }

            accent.SetAsFirstSibling();
            var layout = accent.GetComponent<LayoutElement>() ?? accent.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            accent.anchorMin = new Vector2(0f, 1f);
            accent.anchorMax = new Vector2(1f, 1f);
            accent.pivot = new Vector2(0.5f, 1f);
            accent.anchoredPosition = Vector2.zero;
            accent.sizeDelta = new Vector2(0f, 4f);

            var image = accent.GetComponent<Image>();
            image.color = tint;
            image.raycastTarget = false;
        }

        void EnsureCommandMenuFrame(RectTransform rect, Color tint)
        {
            if (rect == null)
                return;

            var existing = rect.Find("PremiumFrame") as RectTransform;
            if (existing != null)
            {
                var existingLayout = existing.GetComponent<LayoutElement>() ?? existing.gameObject.AddComponent<LayoutElement>();
                existingLayout.ignoreLayout = true;
                var existingImage = existing.GetComponent<Image>();
                if (existingImage != null)
                    ClassicRpgUiRuntime.ApplyPanel(existingImage, ClassicRpgPanelSkin.Frame, true, tint);
                existing.gameObject.SetActive(true);
                return;
            }

            var frame = new GameObject("PremiumFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frame.transform.SetParent(rect, false);
            var image = frame.GetComponent<Image>();
            image.raycastTarget = false;
            frame.GetComponent<LayoutElement>().ignoreLayout = true;
            ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.Frame, true, tint);
            ClassicRpgUiRuntime.Stretch(frame.GetComponent<RectTransform>(), new Vector2(-4f, -4f), new Vector2(4f, 4f));
        }

        void ApplyCommandMenuButtonStyle(Button button, ClassicRpgButtonSkin skin, float height, float preferredWidth = 0f)
        {
            if (button == null)
                return;

            var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            if (preferredWidth > 0f)
            {
                layout.preferredWidth = preferredWidth;
                layout.minWidth = preferredWidth;
            }

            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image != null)
            {
                button.targetGraphic = image;
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.InventoryTitle, true, Color.white);
                image.raycastTarget = true;
            }

            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                var labelColor = skin switch
                {
                    ClassicRpgButtonSkin.MiniGreen => ClassicRpgUiRuntime.SuccessText,
                    ClassicRpgButtonSkin.MiniBrown => ClassicRpgUiRuntime.BrightText,
                    _ => ClassicRpgUiRuntime.WarmGold,
                };

                ClassicRpgUiRuntime.ApplyTextStyle(
                    label,
                    ClassicRpgTextStyle.ButtonLabel,
                    TextAlignmentOptions.Center,
                    labelColor,
                    allowWrap: false);
                label.fontSize = Mathf.RoundToInt((height >= 54f ? 24f : height >= 46f ? 19f : 17f) * GetFontScale());
                label.raycastTarget = false;
            }

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.95f, 0.98f, 1f, 1f);
            colors.pressedColor = skin switch
            {
                ClassicRpgButtonSkin.MiniGreen => new Color(0.84f, 0.95f, 0.86f, 1f),
                ClassicRpgButtonSkin.MiniBrown => new Color(0.84f, 0.88f, 0.94f, 1f),
                _ => new Color(0.88f, 0.93f, 1f, 1f),
            };
            colors.selectedColor = new Color(0.97f, 0.99f, 1f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.44f);
            colors.fadeDuration = 0.10f;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = colors;
        }

        void ApplyPanelFrame(GameObject target, Color backgroundColor, Color accentColor)
        {
            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
                shadow = target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var outline = target.GetComponent<Outline>();
            if (outline == null)
                outline = target.AddComponent<Outline>();
            outline.effectColor = new Color(backgroundColor.r * 0.55f, backgroundColor.g * 0.55f, backgroundColor.b * 0.55f, 0.92f);
            outline.effectDistance = new Vector2(1f, -1f);

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

        void BindLegacyHudRefs()
        {
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
            {
                gameManager.TxtRound = _txtRound;
                gameManager.TxtPhase = _txtPhase;
                gameManager.TxtCountdown = _txtCountdown;
                gameManager.TxtGoldTop = _txtGoldTop;
                gameManager.TxtIncomeTop = _txtIncomeTop;
                gameManager.TxtTeamHpLeft = _txtTeamHpLeft;
                gameManager.TxtTeamHpRight = _txtTeamHpRight;
            }

            var infoBar = GetComponent<InfoBar>();
            if (infoBar != null)
            {
                infoBar.BtnBarracks = _runtimeBarracksButton;
                infoBar.BarracksPanel = _runtimeBarracksPanel;
                infoBar.TxtBarracksLv = _txtBarracksLevel;
                infoBar.TxtWave = _txtRound;
                infoBar.TxtPhase = _txtPhase;
                infoBar.TxtCountdown = _txtCountdown;
                infoBar.TxtGoldTop = _txtGoldTop;
                infoBar.TxtIncomeTop = _txtIncomeTop;
                infoBar.TxtTeamHpLeft = _txtTeamHpLeft;
                infoBar.TxtTeamHpRight = _txtTeamHpRight;
            }
        }

        void RefreshHud()
        {
            var sa = SnapshotApplier.Instance;
            var snap = sa?.LatestML;
            if (snap == null)
                return;

            var myLane = sa.MyLane;
            if (myLane == null)
                return;

            int configuredCoreHp = sa.LatestMLMatchConfig != null
                ? Mathf.Max(0, sa.LatestMLMatchConfig.teamHpStart)
                : 0;
            int myCoreHp = Mathf.Max(0, myLane.lives);
            int myCoreHpMax = Mathf.Max(myCoreHp, configuredCoreHp);
            if (!sa.TryGetTownCoreHp(myLane.laneIndex, out myCoreHp, out myCoreHpMax))
                myCoreHpMax = Mathf.Max(myCoreHp, myCoreHpMax, configuredCoreHp);

            int sideHpMax = snap.teamHpMax > 0
                ? snap.teamHpMax
                : Mathf.Max(snap.teamHp?.left ?? 0, snap.teamHp?.right ?? 0, myCoreHpMax, 20);
            int leftSideHp = snap.teamHp?.left ?? 0;
            int rightSideHp = snap.teamHp?.right ?? 0;

            float myBuild = CalculateLaneBuildValue(myLane);
            float recommendedBuild = EstimateRecommendedBuild(snap.roundNumber);
            int nextWave = snap.roundNumber + 1;
            float ratio = recommendedBuild > 0.01f ? myBuild / recommendedBuild : 1f;
            var buildColor = ratio >= healthyThreshold
                ? new Color(0.34f, 0.88f, 0.46f, 1f)
                : ratio >= cautionThreshold
                    ? new Color(0.97f, 0.79f, 0.24f, 1f)
                    : new Color(0.95f, 0.36f, 0.34f, 1f);

            if (_txtRecommendedHeadline != null)
                _txtRecommendedHeadline.text = $"Build {Mathf.RoundToInt(myBuild)} / {Mathf.RoundToInt(recommendedBuild)}";
            if (_txtRecommendedHeadline != null)
                _txtRecommendedHeadline.color = buildColor;
            if (_txtRecommendedDetail != null)
            {
                int delta = Mathf.RoundToInt(myBuild - recommendedBuild);
                _txtRecommendedDetail.text = delta >= 0 ? $"+{delta} over goal" : $"{delta} under goal";
                _txtRecommendedDetail.color = buildColor;
            }
            if (_recommendedFill != null)
            {
                _recommendedFill.fillAmount = Mathf.Clamp01(ratio);
                _recommendedFill.color = buildColor;
            }

            UpdateTopRightStatBar(
                Mathf.FloorToInt(myLane.gold).ToString(),
                myLane.income.ToString("0.0"),
                "0",
                "10",
                Mathf.RoundToInt(myBuild),
                Mathf.RoundToInt(myBuild - recommendedBuild),
                ratio,
                buildColor);

            if (_myStatsWidget != null)
            {
                _myStatsWidget.SetStats(
                    Mathf.FloorToInt(myLane.gold).ToString(),
                    myLane.income.ToString("0.0"),
                    "0",
                    "10",
                    Mathf.RoundToInt(myBuild).ToString(),
                    Mathf.RoundToInt(recommendedBuild).ToString(),
                    ratio,
                    buildColor);
            }

            RefreshProgressionDock(myLane);
            RefreshWaveOverview(myLane, snap, recommendedBuild, buildColor);

            if (_teamStatsText != null)
            {
                float leftBuild = 0f;
                float rightBuild = 0f;
                float leftIncome = 0f;
                float rightIncome = 0f;
                int leftPlayers = 0;
                int rightPlayers = 0;

                if (snap.lanes != null)
                {
                    for (int i = 0; i < snap.lanes.Length; i++)
                    {
                        var lane = snap.lanes[i];
                        if (lane == null) continue;

                        float build = CalculateLaneBuildValue(lane);
                        if (string.Equals(lane.side, "left", System.StringComparison.OrdinalIgnoreCase))
                        {
                            leftBuild += build;
                            leftIncome += lane.income;
                            leftPlayers++;
                        }
                        else
                        {
                            rightBuild += build;
                            rightIncome += lane.income;
                            rightPlayers++;
                        }
                    }
                }

                _teamStatsText.text =
                    $"LEFT\n" +
                    $"{FormatStatChip("B", Mathf.RoundToInt(leftBuild).ToString(), "#F2C35A")}  {FormatStatChip("I", leftIncome.ToString("0.0"), "#5AD8F2")}\n" +
                    $"{FormatStatChip("P", leftPlayers.ToString(), "#C4C9D4")}\n\n" +
                    $"RIGHT\n" +
                    $"{FormatStatChip("B", Mathf.RoundToInt(rightBuild).ToString(), "#F2C35A")}  {FormatStatChip("I", rightIncome.ToString("0.0"), "#5AD8F2")}\n" +
                    $"{FormatStatChip("P", rightPlayers.ToString(), "#C4C9D4")}";
            }

            if (_playerStatsText != null)
            {
                var lines = new List<string>();
                if (snap.lanes != null)
                {
                    for (int i = 0; i < snap.lanes.Length; i++)
                    {
                        var lane = snap.lanes[i];
                        if (lane == null) continue;

                        string display = !string.IsNullOrWhiteSpace(lane.branchLabel)
                            ? lane.branchLabel
                            : $"Lane {lane.laneIndex + 1}";
                        float build = CalculateLaneBuildValue(lane);
                        lines.Add($"{display}\n{FormatStatChip("B", Mathf.RoundToInt(build).ToString(), "#F2C35A")}  {FormatStatChip("I", lane.income.ToString("0.0"), "#5AD8F2")}  {FormatStatChip("G", Mathf.FloorToInt(lane.gold).ToString(), "#63E08A")}");
                    }
                }
                _playerStatsText.text = string.Join("\n\n", lines);
            }

            if (_waveIntelText != null)
            {
                int waveUnits = CountWaveUnits(myLane);
                int waveSeconds = sa != null ? sa.GetWaveTimerSecondsRemaining() : 0;
                int sendSeconds = sa != null ? sa.GetBarracksSendSecondsRemaining(myLane.laneIndex) : 0;
                string coreChipValue = myCoreHpMax > 0 ? $"{myCoreHp}/{myCoreHpMax}" : myCoreHp.ToString();
                string leftSideChipValue = sideHpMax > 0 ? $"{leftSideHp}/{sideHpMax}" : leftSideHp.ToString();
                string rightSideChipValue = sideHpMax > 0 ? $"{rightSideHp}/{sideHpMax}" : rightSideHp.ToString();
                _waveIntelText.text =
                    $"{FormatStatChip("N", snap.roundNumber.ToString(), "#F2C35A")}  {FormatStatChip("X", nextWave.ToString(), "#F29C52")}\n" +
                    $"{FormatStatChip("WT", waveSeconds.ToString(), "#63E08A")}  {FormatStatChip("ST", sendSeconds.ToString(), "#5AD8F2")}\n" +
                    $"{FormatStatChip("W", waveUnits.ToString(), "#C4C9D4")}  {FormatStatChip("T", Mathf.RoundToInt(recommendedBuild).ToString(), "#5AD8F2")}\n" +
                    $"{FormatStatChip("C", coreChipValue, "#63E08A")}  {FormatStatChip("SL", leftSideChipValue, "#F2C35A")}  {FormatStatChip("SR", rightSideChipValue, "#63C2FF")}";
            }

            if (_txtWavePreview != null)
            {
                string summary = BuildWavePreviewSummary(myLane);
                _txtWavePreview.text = $"NOW  {summary} | NXT  W{nextWave}  GOAL {Mathf.RoundToInt(recommendedBuild)}";
            }
        }

        void EnsureBarracksAccess()
        {
            var canvas = _canvasRect != null ? _canvasRect.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return;

            EnsureHudInputInfrastructure(canvas);
            _runtimeBarracksPanel = EnsureBarracksPanel(canvas);
            _runtimeBarracksButton = EnsureBarracksButton(canvas);

            var infoBar = GetComponent<InfoBar>();
            if (infoBar != null)
            {
                infoBar.BtnBarracks = _runtimeBarracksButton;
                infoBar.BarracksPanel = _runtimeBarracksPanel;
                infoBar.TxtBarracksLv = _txtBarracksLevel;
            }
        }

        BarracksPanel EnsureBarracksPanel(Canvas canvas)
        {
            var existing = canvas.transform.Find(RuntimeBarracksPanelHostName) ?? canvas.transform.Find(LegacyRuntimeBarracksPanelHostName);
            GameObject host;
            if (existing != null)
            {
                host = existing.gameObject;
            }
            else
            {
                host = new GameObject(RuntimeBarracksPanelHostName, typeof(RectTransform));
                host.transform.SetParent(canvas.transform, false);
                var rect = host.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }
            host.name = RuntimeBarracksPanelHostName;

            var panel = host.GetComponent<BarracksPanel>();
            if (panel == null)
                panel = host.AddComponent<BarracksPanel>();
            return panel;
        }

        Button EnsureBarracksButton(Canvas canvas)
        {
            DestroyCanvasChildren(RuntimeBarracksButtonName);
            DestroyCanvasChildren(LegacyRuntimeBarracksButtonName);

            Transform existing = canvas.transform.Find(ProgressionDockWidgetName);
            GameObject root;
            if (existing != null)
            {
                root = existing.gameObject;
            }
            else
            {
                root = new GameObject(ProgressionDockWidgetName, typeof(RectTransform), typeof(Image), typeof(DraggableHudPanel));
                root.transform.SetParent(canvas.transform, false);
            }

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(232f, 124f);
            rootRect.anchoredPosition = new Vector2(-20f, -248f);

            var rootImage = root.GetComponent<Image>();
            rootImage.color = new Color(0.09f, 0.12f, 0.16f, 0.96f);
            ApplyPanelFrame(root, rootImage.color, new Color(0.82f, 0.64f, 0.30f, 0.96f));

            var toggle = root.transform.Find("Toggle");
            if (toggle == null)
            {
                var toggleGo = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
                toggleGo.transform.SetParent(root.transform, false);
                toggle = toggleGo.transform;
            }

            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.sizeDelta = new Vector2(24f, 24f);
            toggleRect.anchoredPosition = new Vector2(-6f, -6f);
            toggle.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.28f, 0.96f);
            var toggleLabel = toggle.Find("Label")?.GetComponent<TextMeshProUGUI>() ?? CreateText(toggle, "Label", "-", 13, TextAlignmentOptions.Center, Color.white);
            toggleLabel.rectTransform.anchorMin = Vector2.zero;
            toggleLabel.rectTransform.anchorMax = Vector2.one;
            toggleLabel.rectTransform.offsetMin = Vector2.zero;
            toggleLabel.rectTransform.offsetMax = Vector2.zero;

            var body = root.transform.Find("Body")?.GetComponent<RectTransform>();
            if (body == null)
            {
                var bodyGo = new GameObject("Body", typeof(RectTransform));
                bodyGo.transform.SetParent(root.transform, false);
                body = bodyGo.GetComponent<RectTransform>();
            }

            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(8f, 8f);
            body.offsetMax = new Vector2(-8f, -8f);
            ClearChildren(body);

            var title = CreateText(body, "Title", "TECH TREE", 15, TextAlignmentOptions.Left, Color.white);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-16f, 18f);
            title.rectTransform.anchoredPosition = new Vector2(8f, -6f);

            _progressionDockStatus = CreateText(body, "Status", "Town Core -- | Drag or minimize", 10, TextAlignmentOptions.Left, new Color(0.86f, 0.90f, 0.95f, 0.94f));
            _progressionDockStatus.rectTransform.anchorMin = new Vector2(0f, 1f);
            _progressionDockStatus.rectTransform.anchorMax = new Vector2(1f, 1f);
            _progressionDockStatus.rectTransform.pivot = new Vector2(0f, 1f);
            _progressionDockStatus.rectTransform.sizeDelta = new Vector2(-16f, 30f);
            _progressionDockStatus.rectTransform.anchoredPosition = new Vector2(8f, -28f);
            _progressionDockStatus.textWrappingMode = TextWrappingModes.Normal;

            var openButton = CreateHudActionButton(body, RuntimeBarracksButtonName, "Open Progression", new Color(0.22f, 0.34f, 0.22f, 0.98f), out _txtBarracksLevel);
            var openButtonRect = openButton.GetComponent<RectTransform>();
            openButtonRect.anchorMin = new Vector2(0f, 0f);
            openButtonRect.anchorMax = new Vector2(1f, 0f);
            openButtonRect.pivot = new Vector2(0.5f, 0f);
            openButtonRect.sizeDelta = new Vector2(0f, 34f);
            openButtonRect.anchoredPosition = new Vector2(0f, 2f);
            _txtBarracksLevel.alignment = TextAlignmentOptions.Center;
            _txtBarracksLevel.fontSize = Mathf.RoundToInt(13f * GetFontScale());
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(OnTechButtonClicked);

            var collapsed = root.transform.Find("CollapsedView")?.GetComponent<RectTransform>();
            if (collapsed == null)
            {
                var collapsedGo = new GameObject("CollapsedView", typeof(RectTransform), typeof(Image));
                collapsedGo.transform.SetParent(root.transform, false);
                collapsed = collapsedGo.GetComponent<RectTransform>();
            }

            collapsed.anchorMin = Vector2.zero;
            collapsed.anchorMax = Vector2.one;
            collapsed.offsetMin = new Vector2(8f, 8f);
            collapsed.offsetMax = new Vector2(-8f, -8f);
            var collapsedImage = collapsed.GetComponent<Image>();
            collapsedImage.color = new Color(0.11f, 0.16f, 0.19f, 1f);
            var collapsedButton = collapsed.GetComponent<Button>() ?? collapsed.gameObject.AddComponent<Button>();
            collapsedButton.targetGraphic = collapsedImage;
            collapsedButton.onClick.RemoveAllListeners();
            collapsedButton.onClick.AddListener(OnTechButtonClicked);
            ClearChildren(collapsed);
            var collapsedLabel = CreateText(collapsed, "CollapsedLabel", "TECH", 13, TextAlignmentOptions.Center, new Color(0.96f, 0.97f, 0.99f, 1f));
            collapsedLabel.rectTransform.anchorMin = Vector2.zero;
            collapsedLabel.rectTransform.anchorMax = Vector2.one;
            collapsedLabel.rectTransform.offsetMin = Vector2.zero;
            collapsedLabel.rectTransform.offsetMax = Vector2.zero;
            collapsedLabel.raycastTarget = false;

            _progressionDockWidget = root.GetComponent<DraggableHudPanel>();
            _progressionDockWidget.Configure(
                rootRect,
                body,
                collapsed,
                toggle.GetComponent<Button>(),
                toggleLabel,
                collapsedLabel,
                false,
                "hud.progression_dock_widget.right_v2",
                new Vector2(232f, 124f),
                new Vector2(90f, 54f));

            toggle.SetAsLastSibling();
            root.name = ProgressionDockWidgetName;
            root.transform.SetAsLastSibling();
            return openButton;
        }

        void OnRuntimeBarracksPressed()
        {
            OnTechButtonClicked();
        }

        void OnTechButtonClicked()
        {
            Debug.Log("TECH button clicked");
            if (_runtimeBarracksPanel == null)
            {
                Debug.LogError("Tech panel is NULL");
                return;
            }

            _runtimeBarracksPanel.ToggleProgression();
        }

        static void EnsureHudInputInfrastructure(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log("[MobileMatchHud] Added missing GraphicRaycaster to the gameplay canvas.");
            }

            var hud = FindFirstObjectByType<MobileMatchHud>(FindObjectsInactive.Include);
            SceneEventSystemUtility.EnsureSceneLocal(hud, "GameplayEventSystem", "MobileMatchHud");
        }

        void RefreshPreviewHud()
        {
            float recommendedBuild = EstimateRecommendedBuild(previewWave);
            float ratio = recommendedBuild > 0.01f ? previewBuild / recommendedBuild : 1f;
            var buildColor = ratio >= healthyThreshold
                ? new Color(0.34f, 0.88f, 0.46f, 1f)
                : ratio >= cautionThreshold
                    ? new Color(0.97f, 0.79f, 0.24f, 1f)
                    : new Color(0.95f, 0.36f, 0.34f, 1f);

            if (_txtRound != null)
                _txtRound.text = $"Wave {previewWave}";
            if (_txtPhase != null)
                _txtPhase.text = previewPhase;
            if (_txtCountdown != null)
                _txtCountdown.text = $"{previewCountdown:0}s";
            if (_txtGoldTop != null)
                _txtGoldTop.text = $"Gold {Mathf.RoundToInt(previewGold)}";
            if (_txtIncomeTop != null)
                _txtIncomeTop.text = $"Inc {previewIncome:0.0}";

            if (_txtRecommendedHeadline != null)
            {
                _txtRecommendedHeadline.text = $"Build {Mathf.RoundToInt(previewBuild)} / {Mathf.RoundToInt(recommendedBuild)}";
                _txtRecommendedHeadline.color = buildColor;
            }

            if (_txtRecommendedDetail != null)
            {
                int delta = Mathf.RoundToInt(previewBuild - recommendedBuild);
                _txtRecommendedDetail.text = delta >= 0 ? $"+{delta} over goal" : $"{delta} under goal";
                _txtRecommendedDetail.color = buildColor;
            }

            if (_recommendedFill != null)
            {
                _recommendedFill.fillAmount = Mathf.Clamp01(ratio);
                _recommendedFill.color = buildColor;
            }

            UpdateTopRightStatBar(
                Mathf.RoundToInt(previewGold).ToString(),
                previewIncome.ToString("0.0"),
                Mathf.RoundToInt(previewForge).ToString(),
                previewForgeWorkers.ToString(),
                Mathf.RoundToInt(previewBuild),
                Mathf.RoundToInt(previewBuild - recommendedBuild),
                ratio,
                buildColor);

            if (_txtTeamHpLeft != null)
                _txtTeamHpLeft.text = "Left Side 20/20";
            if (_txtTeamHpRight != null)
                _txtTeamHpRight.text = "Right Side 17/20";
            if (_barTeamHpLeft != null)
                _barTeamHpLeft.fillAmount = 1f;
            if (_barTeamHpRight != null)
                _barTeamHpRight.fillAmount = 0.85f;

            if (_myStatsWidget != null)
            {
                _myStatsWidget.SetStats(
                    Mathf.RoundToInt(previewGold).ToString(),
                    previewIncome.ToString("0.0"),
                    Mathf.RoundToInt(previewForge).ToString(),
                    previewForgeWorkers.ToString(),
                    Mathf.RoundToInt(previewBuild).ToString(),
                    Mathf.RoundToInt(recommendedBuild).ToString(),
                    ratio,
                    buildColor);
            }

            RefreshProgressionDockPreview();
            RefreshWaveOverviewPreview(recommendedBuild, buildColor);

            if (_teamStatsText != null)
            {
                _teamStatsText.text =
                    $"LEFT\n" +
                    $"{FormatStatChip("B", "284", "#F2C35A")}  {FormatStatChip("I", "22.0", "#5AD8F2")}\n" +
                    $"{FormatStatChip("P", "2", "#C4C9D4")}\n\n" +
                    $"RIGHT\n" +
                    $"{FormatStatChip("B", "251", "#F2C35A")}  {FormatStatChip("I", "19.0", "#5AD8F2")}\n" +
                    $"{FormatStatChip("P", "2", "#C4C9D4")}";
            }

            if (_playerStatsText != null)
            {
                _playerStatsText.text =
                    $"Forge Lane\n{FormatStatChip("B", "148", "#F2C35A")}  {FormatStatChip("I", "10.0", "#5AD8F2")}  {FormatStatChip("G", "96", "#63E08A")}\n\n" +
                    $"Stone Lane\n{FormatStatChip("B", "136", "#F2C35A")}  {FormatStatChip("I", "12.0", "#5AD8F2")}  {FormatStatChip("G", "74", "#63E08A")}\n\n" +
                    $"Ash Lane\n{FormatStatChip("B", "132", "#F2C35A")}  {FormatStatChip("I", "9.0", "#5AD8F2")}  {FormatStatChip("G", "62", "#63E08A")}\n\n" +
                    $"Frost Lane\n{FormatStatChip("B", "119", "#F2C35A")}  {FormatStatChip("I", "10.0", "#5AD8F2")}  {FormatStatChip("G", "58", "#63E08A")}";
            }

            if (_waveIntelText != null)
            {
                _waveIntelText.text =
                    $"{FormatStatChip("N", previewWave.ToString(), "#F2C35A")}  {FormatStatChip("X", (previewWave + 1).ToString(), "#F29C52")}\n" +
                    $"{FormatStatChip("P", previewPhase.ToUpperInvariant(), "#63E08A")}\n" +
                    $"{FormatStatChip("W", "18", "#C4C9D4")}  {FormatStatChip("T", Mathf.RoundToInt(recommendedBuild).ToString(), "#5AD8F2")}\n" +
                    $"{FormatStatChip("C", "20/20", "#63E08A")}  {FormatStatChip("SL", "20/20", "#F2C35A")}  {FormatStatChip("SR", "17/20", "#63C2FF")}";
            }

            if (_txtWavePreview != null)
                _txtWavePreview.text = "NOW  GOBx6 ORCx4 KOBx3 | NXT  W8  GOAL 164";
        }

        float CalculateLaneBuildValue(MLLaneSnap lane)
        {
            return lane != null ? Mathf.Max(0f, lane.buildValue) : 0f;
        }

        int CountWaveUnits(MLLaneSnap lane)
        {
            return CountScheduledWaveUnits(lane?.units);
        }

        int CountActiveWaveMobs(MLSnapshot snap)
        {
            if (snap?.lanes == null)
                return 0;

            int total = 0;
            for (int i = 0; i < snap.lanes.Length; i++)
            {
                var lane = snap.lanes[i];
                if (lane == null || lane.eliminated)
                    continue;

                total += CountWaveUnits(lane);
            }

            return total;
        }

        int CountRemainingWaveMobs(MLSnapshot snap)
        {
            if (snap?.lanes == null)
                return 0;

            int total = 0;
            for (int i = 0; i < snap.lanes.Length; i++)
            {
                var lane = snap.lanes[i];
                if (lane == null || lane.eliminated)
                    continue;

                total += CountWaveUnits(lane);
                total += CountScheduledWaveUnits(lane.spawnQueueUnits);
            }

            return total;
        }

        static string BuildCompactWaveActivityLabel(int activeWaveMobCount, int remainingWaveMobCount)
        {
            activeWaveMobCount = Mathf.Max(0, activeWaveMobCount);
            remainingWaveMobCount = Mathf.Max(activeWaveMobCount, remainingWaveMobCount);

            return remainingWaveMobCount > activeWaveMobCount
                ? $"{WaveMobsIcon}{activeWaveMobCount}/{remainingWaveMobCount}"
                : $"{WaveMobsIcon}{activeWaveMobCount}";
        }

        static string BuildCompactReadyLabel(int readyCount, int requiredReadyCount, bool eligible, bool isReady)
        {
            if (requiredReadyCount <= 0)
                return "Timer running";

            string suffix = isReady
                ? " | You ready"
                : eligible
                    ? " | Vote now"
                    : string.Empty;
            return $"{readyCount}/{requiredReadyCount} ready{suffix}";
        }

        static int CountScheduledWaveUnits(MLUnit[] units)
        {
            if (units == null)
                return 0;

            int count = 0;
            for (int i = 0; i < units.Length; i++)
            {
                if (IsScheduledWaveUnit(units[i]))
                    count++;
            }
            return count;
        }

        static bool IsScheduledWaveUnit(MLUnit unit)
        {
            if (unit == null)
                return false;

            if (!string.IsNullOrWhiteSpace(unit.spawnSourceType))
            {
                return string.Equals(unit.spawnSourceType, "dungeon_wave", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(unit.spawnSourceType, "scheduled_wave", StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(unit.allegianceKey))
                return string.Equals(unit.allegianceKey, "dungeon", StringComparison.OrdinalIgnoreCase);

            return unit.isWaveUnit;
        }

        float EstimateRecommendedBuild(int roundNumber)
        {
            float lateWaveRounds = Mathf.Max(0, roundNumber - 5);
            return targetBaseValue + (roundNumber * targetPerWave) + (lateWaveRounds * targetLateWaveBonus);
        }

        string BuildWavePreviewSummary(MLLaneSnap lane)
        {
            if (lane?.units == null || lane.units.Length == 0)
                return "--";

            var counts = new Dictionary<string, int>();
            for (int i = 0; i < lane.units.Length; i++)
            {
                var unit = lane.units[i];
                if (!IsScheduledWaveUnit(unit) || string.IsNullOrWhiteSpace(unit.type))
                    continue;

                if (!counts.ContainsKey(unit.type))
                    counts[unit.type] = 0;
                counts[unit.type]++;
            }

            if (counts.Count == 0)
                return "--";

            var parts = new List<string>();
            foreach (var pair in counts)
                parts.Add($"{AbbreviateUnitKey(pair.Key)}x{pair.Value}");

            parts.Sort((a, b) => string.CompareOrdinal(a, b));
            if (parts.Count > 3)
                parts.RemoveRange(3, parts.Count - 3);
            return string.Join(" ", parts);
        }

        string AbbreviateUnitKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "--";

            string[] parts = key.Split('_');
            if (parts.Length == 1)
                return key.Length <= 4 ? key.ToUpperInvariant() : key.Substring(0, 4).ToUpperInvariant();

            string compact = "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                    continue;
                compact += char.ToUpperInvariant(parts[i][0]);
            }
            return compact.Length > 0 ? compact : key.Substring(0, Mathf.Min(4, key.Length)).ToUpperInvariant();
        }

        string FormatStatChip(string tag, string value, string colorHex)
        {
            return $"<color={colorHex}>[{tag}]</color> {value}";
        }

        void UpdateTopRightStatBar(string gold, string income, string forge, string workers, int buildValue, int buildDelta, float ratio, Color buildColor)
        {
            if (_txtGoldTop != null)
                _txtGoldTop.text = gold;
            if (_txtIncomeTop != null)
                _txtIncomeTop.text = income;
            if (_txtForgeTop != null)
                _txtForgeTop.text = forge;
            if (_txtWorkersTop != null)
                _txtWorkersTop.text = workers;
            if (_txtBuildStatTop != null)
                _txtBuildStatTop.text = $"BLD {buildValue}";
            if (_txtBuildDeltaTop != null)
            {
                _txtBuildDeltaTop.text = buildDelta >= 0 ? $"+{buildDelta}" : buildDelta.ToString();
                _txtBuildDeltaTop.color = buildColor;
            }
            if (_buildStatFill != null)
            {
                _buildStatFill.fillAmount = Mathf.Clamp01(ratio);
                _buildStatFill.color = buildColor;
            }
        }

        float GetFontScale()
        {
            float shortest = Mathf.Min(Screen.width, Screen.height);
            if (shortest <= compactPhoneWidthThreshold)
                return compactPhoneFontScale;
            if (shortest <= phoneWidthThreshold || Application.isMobilePlatform)
                return phoneFontScale;
            return tabletFontScale;
        }

        void ApplySafeAreaLayout()
        {
            if (_canvasRect == null)
                return;

            var safeArea = Screen.safeArea;
            float canvasWidth = _canvasRect.rect.width;
            float canvasHeight = _canvasRect.rect.height;
            if (canvasWidth <= 0f || canvasHeight <= 0f || Screen.width <= 0f || Screen.height <= 0f)
                return;

            var canvasSize = new Vector2(canvasWidth, canvasHeight);
            if (safeArea == _lastSafeArea && canvasSize == _lastCanvasSize)
                return;

            _lastSafeArea = safeArea;
            _lastCanvasSize = canvasSize;

            float leftInset = safeArea.xMin * (canvasWidth / Screen.width);
            float rightInset = (Screen.width - safeArea.xMax) * (canvasWidth / Screen.width);
            float topInset = (Screen.height - safeArea.yMax) * (canvasHeight / Screen.height);
            float bottomInset = safeArea.yMin * (canvasHeight / Screen.height);

            if (_ribbonRoot != null)
            {
                _ribbonRoot.offsetMin = new Vector2(12f + leftInset, -ribbonHeight - ribbonTopInset - topInset);
                _ribbonRoot.offsetMax = new Vector2(-12f - rightInset, -ribbonTopInset - topInset);
            }

            if (_rightRailRoot != null)
            {
                _rightRailRoot.offsetMin = new Vector2(-rightRailWidth - rightInset, rightRailBottomInset + bottomInset);
                _rightRailRoot.offsetMax = new Vector2(-rightRailEdgeInset - rightInset, -GetEffectiveRightRailTopInset() - topInset);
            }

            if (_topRightStatsRoot != null)
                _topRightStatsRoot.anchoredPosition = new Vector2(-statBarRightInset - rightInset, -statBarTopInset - topInset);

            if (_settingsPanelRoot != null)
                _settingsPanelRoot.anchoredPosition = new Vector2(-settingsRightInset - rightInset, -settingsTopInset - topInset);

            ApplyMiniMapDockLayout(leftInset, rightInset, topInset, bottomInset);
            ApplyMyStatsDockLayout(leftInset, rightInset, topInset);
            ApplyWaveOverviewDockLayout(leftInset, rightInset, topInset, bottomInset);
            SyncHudDockStatesFromCurrentLayout(leftInset, rightInset, topInset, bottomInset);

            if (_myStatsDockedToMiniMap
                && _myStatsWidget != null
                && TryGetMyStatsDockPosition(leftInset, rightInset, topInset, out var statsTarget))
                _myStatsWidget.SetAnchoredPosition(statsTarget);

            if (_waveOverviewDockedToMyStats)
                ApplyWaveOverviewDockSnap(false);

            if (_settingsOverlayPanelRoot != null)
            {
                bool centeredPanel = Mathf.Approximately(_settingsOverlayPanelRoot.anchorMin.x, 0.5f)
                    && Mathf.Approximately(_settingsOverlayPanelRoot.anchorMax.x, 0.5f)
                    && Mathf.Approximately(_settingsOverlayPanelRoot.anchorMin.y, 0.5f)
                    && Mathf.Approximately(_settingsOverlayPanelRoot.anchorMax.y, 0.5f);

                if (centeredPanel)
                {
                    float safeWidth = Mathf.Max(320f, _canvasRect.rect.width - leftInset - rightInset);
                    float safeHeight = Mathf.Max(320f, _canvasRect.rect.height - topInset - bottomInset);
                    _settingsOverlayPanelRoot.sizeDelta = ResolveSettingsOverlayPanelSize(safeWidth, safeHeight);
                    _settingsOverlayPanelRoot.anchoredPosition = new Vector2((leftInset - rightInset) * 0.5f, (bottomInset - topInset) * 0.5f);
                }
                else
                {
                    float panelHorizontalInset = Mathf.Max(18f, settingsPanelGap + 12f);
                    float panelVerticalInset = Mathf.Max(18f, settingsPanelGap + 8f);
                    _settingsOverlayPanelRoot.offsetMin = new Vector2(panelHorizontalInset + leftInset, panelVerticalInset + bottomInset);
                    _settingsOverlayPanelRoot.offsetMax = new Vector2(-panelHorizontalInset - rightInset, -panelVerticalInset - topInset);
                }
            }
        }

        void DestroyCanvasChildren(string childName)
        {
            if (_canvasRect == null)
                return;

            for (int i = _canvasRect.childCount - 1; i >= 0; i--)
            {
                var child = _canvasRect.GetChild(i);
                if (child == null || child.name != childName)
                    continue;

                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }
    }
}
