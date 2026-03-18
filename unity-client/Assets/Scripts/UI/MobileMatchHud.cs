using System.Collections.Generic;
using TMPro;
using UnityEngine;
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

        [Header("Editor Preview")]
        [SerializeField] bool previewInEditMode = true;
        [SerializeField] int previewWave = 7;
        [SerializeField] string previewPhase = "BUILD";
        [SerializeField] float previewCountdown = 14f;
        [SerializeField] float previewBuild = 148f;
        [SerializeField] float previewGold = 96f;
        [SerializeField] float previewIncome = 10f;
        [SerializeField] float previewForge = 0f;
        [SerializeField] int previewForgeWorkers = 10;
        [SerializeField] int previewQueue = 2;
        [SerializeField] int previewBarracksLevel = 2;

        RectTransform _canvasRect;
        RectTransform _ribbonRoot;
        RectTransform _rightRailRoot;
        RectTransform _topRightStatsRoot;
        Rect _lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
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

        TMP_Text _myStatsText;
        TMP_Text _teamStatsText;
        TMP_Text _playerStatsText;
        TMP_Text _waveIntelText;
        MyStatsHudWidget _myStatsWidget;
        WaveStatusHudWidget _waveStatusWidget;

        CollapsibleHudCard _myStatsCard;
        CollapsibleHudCard _teamStatsCard;
        CollapsibleHudCard _playerStatsCard;
        CollapsibleHudCard _waveIntelCard;

        readonly Dictionary<string, UnitCatalogEntry> _catalogByKey = new();

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
            if (!myStatsOnlyMode && showLegacyRightRail)
                BuildRightRail();
            else
                DestroyCanvasChildren("RightHudRail");
            if (!myStatsOnlyMode)
                BindLegacyHudRefs();
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
            DestroyCanvasChildren("TopRightStatBar");
            DestroyCanvasChildren("RightHudRail");
            DestroyCanvasChildren("MyStatsWidget");
            DestroyCanvasChildren("WaveStatusWidget");
            DestroyCanvasChildren("WaveHUD(Clone)");

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
            _txtPhase = CreateValueLabel(waveModule, "Txt_Phase", "BUILD", 16, TextAlignmentOptions.Left, new Color(0.38f, 1f, 0.55f));
            _txtCountdown = CreateValueLabel(waveModule, "Txt_Countdown", "30s", 16, TextAlignmentOptions.Left, new Color(1f, 0.89f, 0.34f));

            var buildModule = CreateRibbonModule("RecommendedBuildModule", 212f, new Color(0.10f, 0.14f, 0.16f, 0.94f), new Color(0.78f, 0.48f, 0.18f, 0.98f));
            CreateCaptionLabel(buildModule, "Recommended Build");
            _txtRecommendedHeadline = CreateValueLabel(buildModule, "Txt_RecommendedBuildHeadline", "Build 0 / 0", 22, TextAlignmentOptions.Center);
            _txtRecommendedDetail = CreateValueLabel(buildModule, "Txt_RecommendedBuildDetail", "Target pending", 14, TextAlignmentOptions.Center, new Color(0.85f, 0.88f, 0.93f, 0.92f));
            _recommendedFill = CreateProgressBar(buildModule, "RecommendedBuildBar", new Color(0.34f, 0.86f, 0.48f, 1f));

            var statusModule = CreateRibbonModule("MatchStatusModule", 290f, new Color(0.12f, 0.12f, 0.14f, 0.94f), new Color(0.88f, 0.84f, 0.36f, 0.98f));
            _txtTeamHpLeft = CreateValueLabel(statusModule, "Txt_TeamHpLeft", "Left Team 20", 16, TextAlignmentOptions.Left, new Color(1f, 0.90f, 0.24f));
            _barTeamHpLeft = CreateProgressBar(statusModule, "Bar_TeamHpLeft", new Color(1f, 0.78f, 0.18f, 1f));
            _txtTeamHpRight = CreateValueLabel(statusModule, "Txt_TeamHpRight", "Right Team 20", 16, TextAlignmentOptions.Left, new Color(0.58f, 0.86f, 1f, 1f));
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

            _myStatsCard = null;
            _myStatsText = null;
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
            rect.sizeDelta = new Vector2(230f, 116f);
            rect.anchoredPosition = new Vector2(Mathf.Max(12f, _canvasRect.rect.width - 276f), -128f);

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
            bodyRect.offsetMin = new Vector2(10f, 8f);
            bodyRect.offsetMax = new Vector2(-10f, -6f);
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
            rowsLayoutElement.preferredHeight = 54f;
            rowsLayoutElement.flexibleHeight = 0f;

            CreateLinkedStatPair(rows.transform, "GoldIncomeRow",
                new Color(0.95f, 0.74f, 0.28f, 1f), "GLD", out var goldValue,
                new Color(0.48f, 0.90f, 0.95f, 1f), "INC", out var incomeValue);

            CreateLinkedStatPair(rows.transform, "SecondaryWorkersRow",
                new Color(0.98f, 0.79f, 0.24f, 1f), "FOR", out var secondaryValue,
                new Color(0.82f, 0.86f, 0.93f, 1f), "WRK", out var workersValue);

            CreateLinkedStatPair(rows.transform, "BuildTargetRow",
                new Color(0.43f, 0.82f, 1f, 1f), "BLD", out var buildValue,
                new Color(0.98f, 0.30f, 0.24f, 1f), "TGT", out var targetValue);

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
                "hud.my_stats_widget");
        }

        void BuildWaveStatusWidget()
        {
            if (_canvasRect == null)
                return;

            DestroyCanvasChildren("WaveStatusWidget");

            var root = new GameObject("WaveStatusWidget", typeof(RectTransform), typeof(Image), typeof(WaveStatusHudWidget));
            root.transform.SetParent(_canvasRect, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(452f, 132f);
            rect.anchoredPosition = new Vector2(0f, -18f);

            var panelImage = root.GetComponent<Image>();
            panelImage.color = new Color(0.08f, 0.11f, 0.15f, 0.94f);
            ApplyPanelFrame(root, panelImage.color, new Color(0.34f, 0.78f, 0.98f, 0.92f));

            var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggle.transform.SetParent(root.transform, false);
            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.sizeDelta = new Vector2(24f, 24f);
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
            bodyRect.offsetMin = new Vector2(8f, 8f);
            bodyRect.offsetMax = new Vector2(-8f, -8f);

            var chipRow = new GameObject("ChipRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            chipRow.transform.SetParent(body.transform, false);
            var chipRowRect = chipRow.GetComponent<RectTransform>();
            chipRowRect.anchorMin = new Vector2(0.5f, 1f);
            chipRowRect.anchorMax = new Vector2(0.5f, 1f);
            chipRowRect.pivot = new Vector2(0.5f, 1f);
            chipRowRect.sizeDelta = new Vector2(172f, 24f);
            chipRowRect.anchoredPosition = new Vector2(0f, 0f);
            var chipLayout = chipRow.GetComponent<HorizontalLayoutGroup>();
            chipLayout.childAlignment = TextAnchor.MiddleCenter;
            chipLayout.childControlWidth = false;
            chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = false;
            chipLayout.childForceExpandHeight = false;
            chipLayout.spacing = 4f;
            chipLayout.padding = new RectOffset(0, 0, 0, 0);

            CreateWaveStatusChip(chipRow.transform, "GoldChip", "GLD", "96", new Color(0.94f, 0.76f, 0.28f, 0.98f));
            CreateWaveStatusChip(chipRow.transform, "BuildChip", "BLD", "148", new Color(0.44f, 0.80f, 1f, 0.98f));
            CreateWaveStatusChip(chipRow.transform, "QueueChip", "Q", "2", new Color(0.52f, 0.95f, 0.68f, 0.98f));
            CreateWaveStatusChip(chipRow.transform, "IntelChip", "NXT", "7", new Color(0.98f, 0.56f, 0.28f, 0.98f));

            var barsRoot = new GameObject("BarsRoot", typeof(RectTransform));
            barsRoot.transform.SetParent(body.transform, false);
            var barsRect = barsRoot.GetComponent<RectTransform>();
            barsRect.anchorMin = Vector2.zero;
            barsRect.anchorMax = Vector2.one;
            barsRect.offsetMin = Vector2.zero;
            barsRect.offsetMax = new Vector2(0f, -16f);

            var leftBar = CreateWaveSideBar(barsRoot.transform, "LeftStatusBar", true,
                new Color(0.58f, 0.14f, 0.14f, 0.92f), new Color(0.25f, 0.95f, 0.39f, 0.98f),
                out var leftValue, out var leftLabel);
            var rightBar = CreateWaveSideBar(barsRoot.transform, "RightStatusBar", false,
                new Color(0.16f, 0.24f, 0.34f, 0.92f), new Color(0.98f, 0.50f, 0.50f, 0.98f),
                out var rightValue, out var rightLabel);

            var centerPlate = new GameObject("CenterPlate", typeof(RectTransform), typeof(Image));
            centerPlate.transform.SetParent(barsRoot.transform, false);
            var centerRect = centerPlate.GetComponent<RectTransform>();
            centerRect.anchorMin = new Vector2(0.5f, 0.5f);
            centerRect.anchorMax = new Vector2(0.5f, 0.5f);
            centerRect.pivot = new Vector2(0.5f, 0.5f);
            centerRect.sizeDelta = new Vector2(144f, 92f);
            centerRect.anchoredPosition = new Vector2(0f, 12f);
            var centerImage = centerPlate.GetComponent<Image>();
            centerImage.color = new Color(0.09f, 0.13f, 0.17f, 0.98f);
            ApplyPanelFrame(centerPlate, centerImage.color, new Color(0.53f, 0.85f, 1f, 0.92f));

            var waveLabel = CreateText(centerPlate.transform, "WaveLabel", "WAVE 6", 24, TextAlignmentOptions.Center, Color.white);
            waveLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            waveLabel.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            waveLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            waveLabel.rectTransform.sizeDelta = new Vector2(0f, 34f);
            waveLabel.rectTransform.anchoredPosition = new Vector2(0f, 8f);

            var phaseLabel = CreateText(centerPlate.transform, "PhaseLabel", "BUILD 14s", 11, TextAlignmentOptions.Center, new Color(0.84f, 0.90f, 0.97f, 0.94f));
            phaseLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            phaseLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            phaseLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            phaseLabel.rectTransform.sizeDelta = new Vector2(0f, 18f);
            phaseLabel.rectTransform.anchoredPosition = new Vector2(0f, 10f);

            var footer = CreateText(body.transform, "FooterLabel", "Wave target 158 | Build healthy", 11, TextAlignmentOptions.Center, new Color(0.86f, 0.90f, 0.96f, 0.9f));
            footer.rectTransform.anchorMin = new Vector2(0f, 0f);
            footer.rectTransform.anchorMax = new Vector2(1f, 0f);
            footer.rectTransform.pivot = new Vector2(0.5f, 0f);
            footer.rectTransform.sizeDelta = new Vector2(0f, 16f);
            footer.rectTransform.anchoredPosition = new Vector2(0f, -2f);

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

            var collapsedLabel = CreateText(collapsed.transform, "CollapsedLabel", "W6", 14, TextAlignmentOptions.Center, Color.white);
            collapsedLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            collapsedLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            collapsedLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            collapsedLabel.rectTransform.sizeDelta = new Vector2(0f, 18f);
            collapsedLabel.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            _waveStatusWidget = root.GetComponent<WaveStatusHudWidget>();
            _waveStatusWidget.Configure(
                rect,
                bodyRect,
                collapsedRect,
                toggle.GetComponent<Button>(),
                toggleLabel,
                waveLabel,
                phaseLabel,
                leftValue,
                rightValue,
                leftLabel,
                rightLabel,
                footer,
                leftBar,
                rightBar,
                collapsedRingImage,
                collapsedLabel,
                false,
                "hud.wave_status_widget");
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

        void CreateLinkedStatPair(Transform parent, string name,
            Color leftColor, string leftLabel, out TMP_Text leftValue,
            Color rightColor, string rightLabel, out TMP_Text rightValue)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rootLayoutElement = root.GetComponent<LayoutElement>();
            rootLayoutElement.preferredHeight = 16f;
            rootLayoutElement.minHeight = 16f;

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
            leftGroupLayoutElement.preferredWidth = 86f;
            leftGroupLayoutElement.minWidth = 86f;
            leftGroupLayoutElement.preferredHeight = 16f;
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

            var leftTag = CreateText(leftGroup.transform, "LeftTag", leftLabel, 9, TextAlignmentOptions.Left, leftColor);
            leftTag.fontStyle = FontStyles.SmallCaps;
            leftTag.enableWordWrapping = false;
            leftTag.overflowMode = TextOverflowModes.Overflow;
            leftTag.rectTransform.sizeDelta = new Vector2(22f, 14f);

            leftValue = CreateText(leftGroup.transform, "LeftValue", "0", 14, TextAlignmentOptions.Left, new Color(0.94f, 0.96f, 0.99f, 1f));
            leftValue.fontStyle = FontStyles.Bold;
            leftValue.rectTransform.sizeDelta = new Vector2(54f, 16f);
            leftValue.enableAutoSizing = true;
            leftValue.fontSizeMin = 12f;
            leftValue.fontSizeMax = Mathf.RoundToInt(14 * GetFontScale());

            var arrow = CreateText(root.transform, "Arrow", "<", 12, TextAlignmentOptions.Center, new Color(0.55f, 0.76f, 0.96f, 0.96f));
            arrow.rectTransform.sizeDelta = new Vector2(12f, 16f);

            var rightGroup = new GameObject("RightGroup", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rightGroup.transform.SetParent(root.transform, false);
            var rightGroupLayoutElement = rightGroup.GetComponent<LayoutElement>();
            rightGroupLayoutElement.preferredWidth = 86f;
            rightGroupLayoutElement.minWidth = 86f;
            rightGroupLayoutElement.preferredHeight = 16f;
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

            var rightTag = CreateText(rightGroup.transform, "RightTag", rightLabel, 9, TextAlignmentOptions.Left, rightColor);
            rightTag.fontStyle = FontStyles.SmallCaps;
            rightTag.enableWordWrapping = false;
            rightTag.overflowMode = TextOverflowModes.Overflow;
            rightTag.rectTransform.sizeDelta = new Vector2(22f, 14f);

            rightValue = CreateText(rightGroup.transform, "RightValue", "0", 14, TextAlignmentOptions.Left, new Color(0.94f, 0.96f, 0.99f, 1f));
            rightValue.fontStyle = FontStyles.Bold;
            rightValue.rectTransform.sizeDelta = new Vector2(54f, 16f);
            rightValue.enableAutoSizing = true;
            rightValue.fontSizeMin = 12f;
            rightValue.fontSizeMax = Mathf.RoundToInt(14 * GetFontScale());
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
            accent.GetComponent<Image>().color = accentColor;
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
                _txtRecommendedDetail.text = delta >= 0 ? $"+{delta} over target" : $"{delta} under target";
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

            if (_waveStatusWidget != null)
            {
                int teamHpMax = snap.teamHpMax > 0 ? snap.teamHpMax : 20;
                int leftHp = snap.teamHp?.left ?? 0;
                int rightHp = snap.teamHp?.right ?? 0;
                float leftRatio = teamHpMax > 0 ? (float)leftHp / teamHpMax : 0f;
                float rightRatio = teamHpMax > 0 ? (float)rightHp / teamHpMax : 0f;
                string phase = $"{(snap.roundState ?? "build").ToUpperInvariant()} {Mathf.Max(0, snap.roundStateTicks)}s";
                string footer = $"Wave target {Mathf.RoundToInt(recommendedBuild)} | {BuildWavePreviewSummary(myLane)}";
                _waveStatusWidget.SetStatus(
                    $"WAVE {snap.roundNumber}",
                    phase,
                    $"{Mathf.RoundToInt(leftRatio * 100f)}%",
                    $"{Mathf.RoundToInt(rightRatio * 100f)}%",
                    "LEFT",
                    "RIGHT",
                    footer,
                    leftRatio,
                    rightRatio,
                    buildColor);
            }

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
                int teamHpMax = snap.teamHpMax > 0 ? snap.teamHpMax : 20;
                int waveUnits = CountWaveUnits(myLane);
                _waveIntelText.text =
                    $"{FormatStatChip("N", snap.roundNumber.ToString(), "#F2C35A")}  {FormatStatChip("X", nextWave.ToString(), "#F29C52")}\n" +
                    $"{FormatStatChip("P", (snap.roundState ?? "-").ToUpperInvariant(), "#63E08A")}\n" +
                    $"{FormatStatChip("W", waveUnits.ToString(), "#C4C9D4")}  {FormatStatChip("T", Mathf.RoundToInt(recommendedBuild).ToString(), "#5AD8F2")}\n" +
                    $"{FormatStatChip("L", $"{snap.teamHp?.left ?? 0}/{teamHpMax}", "#F2C35A")}  {FormatStatChip("R", $"{snap.teamHp?.right ?? 0}/{teamHpMax}", "#63C2FF")}";
            }

            if (_txtWavePreview != null)
            {
                string summary = BuildWavePreviewSummary(myLane);
                _txtWavePreview.text = $"NOW  {summary} | NXT  W{nextWave}  TGT {Mathf.RoundToInt(recommendedBuild)}";
            }
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
                _txtRecommendedDetail.text = delta >= 0 ? $"+{delta} over target" : $"{delta} under target";
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
                _txtTeamHpLeft.text = "Left Team 20";
            if (_txtTeamHpRight != null)
                _txtTeamHpRight.text = "Right Team 17";
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

            if (_waveStatusWidget != null)
            {
                _waveStatusWidget.SetStatus(
                    $"WAVE {previewWave}",
                    $"{previewPhase.ToUpperInvariant()} {previewCountdown:0}s",
                    "96%",
                    "94%",
                    "LEFT",
                    "RIGHT",
                    $"Wave target {Mathf.RoundToInt(recommendedBuild)} | Next {previewWave + 1}",
                    0.96f,
                    0.94f,
                    buildColor);
            }

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
                    $"{FormatStatChip("L", "20/20", "#F2C35A")}  {FormatStatChip("R", "17/20", "#63C2FF")}";
            }

            if (_txtWavePreview != null)
                _txtWavePreview.text = "NOW  GOBx6 ORCx4 KOBx3 | NXT  W8  TGT 164";
        }

        float CalculateLaneBuildValue(MLLaneSnap lane)
        {
            if (lane == null)
                return 0f;

            var seen = new HashSet<string>();
            float total = 0f;

            AddCells(lane.towerCells);
            AddCells(lane.mobilizedCells);
            return total;

            void AddCells(MLTowerCell[] cells)
            {
                if (cells == null)
                    return;

                for (int i = 0; i < cells.Length; i++)
                {
                    var cell = cells[i];
                    if (cell == null || string.IsNullOrWhiteSpace(cell.type))
                        continue;

                    string key = $"{cell.type}:{cell.X}:{cell.Y}";
                    if (!seen.Add(key))
                        continue;

                    if (_catalogByKey.TryGetValue(cell.type, out var entry))
                        total += Mathf.Max(0f, entry.build_cost);
                    else
                        total += 10f;
                }
            }
        }

        int CountWaveUnits(MLLaneSnap lane)
        {
            if (lane?.units == null)
                return 0;

            int count = 0;
            for (int i = 0; i < lane.units.Length; i++)
            {
                var unit = lane.units[i];
                if (unit != null && unit.isWaveUnit)
                    count++;
            }
            return count;
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
                if (unit == null || !unit.isWaveUnit || string.IsNullOrWhiteSpace(unit.type))
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
            if (safeArea == _lastSafeArea)
                return;

            _lastSafeArea = safeArea;

            float canvasWidth = _canvasRect.rect.width;
            float canvasHeight = _canvasRect.rect.height;
            if (canvasWidth <= 0f || canvasHeight <= 0f || Screen.width <= 0f || Screen.height <= 0f)
                return;

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
