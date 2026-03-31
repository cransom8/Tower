using System;
using System.Collections;
using System.Collections.Generic;
using CastleDefender.Game;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DefaultExecutionOrder(-50)]
    public class LoadoutPhaseManager : MonoBehaviour
    {
        sealed class RaceCardView
        {
            public string RaceId;
            public Image Background;
            public Button Button;
            public TMP_Text Title;
            public TMP_Text Subtitle;
            public RawImage Portrait;
        }

        sealed class UnitCardView
        {
            public RaceProgressionUnitDefinition Unit;
            public RaceProgressionUnitCardStyle CardStyle;
            public Image Background;
            public Image StateBackground;
            public Button Button;
            public CanvasGroup CanvasGroup;
            public TMP_Text Name;
            public TMP_Text Stats;
            public TMP_Text Subtitle;
            public TMP_Text Requirement;
            public TMP_Text Cost;
            public TMP_Text State;
            public RawImage Portrait;
            public Image Icon;
            public TMP_Text IconFallback;
        }

        sealed class RequirementCardView
        {
            public string LaneId;
            public RaceProgressionUnitDefinition SourceUnit;
            public RaceProgressionUnitDefinition TargetUnit;
            public RaceProgressionRequirementDefinition Requirement;
            public Image Background;
            public Image Icon;
            public Image StatusBackground;
            public Button Button;
            public CanvasGroup CanvasGroup;
            public TMP_Text IconFallback;
            public TMP_Text Name;
            public TMP_Text Tier;
            public TMP_Text Status;
        }

        sealed class ArrowView
        {
            public string LaneId;
            public string TargetUnitId;
            public TMP_Text Glyph;
        }

        sealed class TreeTabButtonView
        {
            public RaceProgressionTab Tab;
            public Image Background;
            public Button Button;
            public TMP_Text Label;
        }

        enum PhaseState
        {
            Idle,
            Viewing,
            Active,
            Confirming,
            WaitingForMatch,
            Done,
        }

        enum UnitProgressVisualState
        {
            Start,
            Unlocked,
            Available,
            Locked,
        }

        enum RequirementProgressVisualState
        {
            Met,
            Available,
            Locked,
        }

        enum WizardPage
        {
            RaceSelection,
            ProgressionTree,
            UnitDetails,
        }

        [Header("Portrait Fallbacks")]
        [SerializeField] UnitPrefabRegistry PortraitRegistry;
        [SerializeField] UnitPortraitCamera PortraitCam;

        [Header("Theme")]
        [SerializeField] Color selectedColor = new Color(0.84f, 0.67f, 0.28f, 1f);
        [SerializeField] Color highlightedColor = new Color(0.25f, 0.40f, 0.65f, 1f);
        [SerializeField] Color unlockedColor = new Color(0.12f, 0.18f, 0.28f, 0.98f);
        [SerializeField] Color lockedColor = new Color(0.12f, 0.12f, 0.14f, 0.92f);
        [SerializeField] Color timerNormalColor = new Color(1f, 0.92f, 0.38f, 1f);
        [SerializeField] Color timerUrgentColor = new Color(1f, 0.38f, 0.32f, 1f);

        const float PortraitFrameHeight = 94f;
        const float UnitCardWidth = 216f;
        const float UnitCardHeight = 214f;
        const float BuildingCardHeight = 264f;
        const float BuildingImageFrameHeight = 84f;
        const float RequirementCardWidth = 160f;
        const float RequirementCardHeight = 126f;
        const float CompactRequirementCardHeight = 116f;
        const float LaneRowHeight = 292f;
        const float BuildingLaneRowHeight = 352f;
        const float CivicLaneRowHeight = 548f;
        const float ChainArrowWidth = 34f;
        const float CompactArrowWidth = 28f;
        const float RaceCardHeight = 112f;
        const float RaceCardWidth = 220f;
        const float MinLaneRowWidth = 760f;
        const float UnitCardFlexWidth = 4.6f;
        const float BuildingCardFlexWidth = 5.2f;
        const float HeroOutcomeCardWidth = 216f;
        const float UpgradeStepCardWidth = 212f;
        const float UpgradeStepCardHeight = 206f;
        const float RequirementCardFlexWidth = 2.7f;
        const float CompactRequirementCardFlexWidth = 2.1f;
        const float ArrowFlexWidth = 0.45f;

        PhaseState _state = PhaseState.Idle;
        ProgressionViewerMode _mode = ProgressionViewerMode.LobbyViewer;
        WizardPage _activePage = WizardPage.RaceSelection;
        RaceProgressionTab _selectedTreeTab = RaceProgressionTab.Units;

        GameObject _panelRoot;
        TMP_Text _txtTitle;
        TMP_Text _txtSubtitle;
        TMP_Text _txtTimer;
        TMP_Text _txtStatus;
        Button _btnPrimaryAction;
        TMP_Text _txtPrimaryAction;
        Button _btnSecondaryAction;
        TMP_Text _txtSecondaryAction;

        GameObject _prepOverlay;
        TMP_Text _txtPrepStatus;
        TMP_Text _txtPrepDetail;

        GameObject _playerPanelRoot;
        readonly List<(TMP_Text name, TMP_Text state, Image bar)> _playerRows = new();

        RawImage _detailsPortrait;
        Image _detailsBuildingIcon;
        TMP_Text _detailsBuildingFallback;
        TMP_Text _txtDetailsTitle;
        TMP_Text _txtDetailsStats;
        TMP_Text _txtDetailsState;
        TMP_Text _txtDetailsRequirement;
        TMP_Text _txtDetailsBody;

        RaceProgressionDefinition _selectedRace;
        RaceProgressionUnitDefinition _selectedUnit;
        string[] _availableRaceIds = Array.Empty<string>();
        float _timerRemaining;
        float _phaseStartTime;

        readonly List<RaceCardView> _raceCards = new();
        readonly Dictionary<string, UnitCardView> _unitCards = new(StringComparer.OrdinalIgnoreCase);
        readonly List<RequirementCardView> _requirementCards = new();
        readonly List<ArrowView> _arrowViews = new();
        readonly List<TreeTabButtonView> _treeTabButtons = new();
        readonly HashSet<string> _missingCatalogLogs = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _missingPreparationStateLogs = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Sprite> _buildingIconCache = new(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<string, Texture2D> _portraitCache = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<RawImage>> _pendingPortraitTargets = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _capturePending = new(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> _captureQueue = new();
        Coroutine _portraitWarmupRoutine;
        Coroutine _criticalWarmupRoutine;
        Coroutine _environmentWarmupRoutine;
        GameObject _runtimePortraitRoot;
        RenderTexture _runtimePortraitTexture;
        bool _isCapturingPortraits;

        bool _loadoutReadyEmitted;
        bool _criticalWarmupDone;
        bool _gameplayTransitionStarted;
        bool _isEmbeddedViewer;
        RectTransform _embeddedHost;
        Action _embeddedCloseAction;
        string _embeddedRequestedRaceId;
        string[] _embeddedAvailableRaceIds = Array.Empty<string>();

        public static LoadoutPhaseManager AttachEmbeddedViewer(
            RectTransform host,
            string requestedRaceId = null,
            string[] availableRaceIds = null,
            Action onClose = null)
        {
            if (host == null)
                return null;

            var viewerGo = new GameObject("EmbeddedRaceProgressionViewer", typeof(RectTransform));
            viewerGo.SetActive(false);
            viewerGo.transform.SetParent(host, false);
            var viewerRect = viewerGo.GetComponent<RectTransform>();
            viewerRect.anchorMin = Vector2.zero;
            viewerRect.anchorMax = Vector2.one;
            viewerRect.offsetMin = Vector2.zero;
            viewerRect.offsetMax = Vector2.zero;

            var viewer = viewerGo.AddComponent<LoadoutPhaseManager>();
            viewer.ConfigureEmbeddedViewer(viewerRect, requestedRaceId, availableRaceIds, onClose);
            viewerGo.SetActive(true);
            return viewer;
        }

        public void ConfigureEmbeddedViewer(
            RectTransform host,
            string requestedRaceId = null,
            string[] availableRaceIds = null,
            Action onClose = null)
        {
            _isEmbeddedViewer = true;
            _embeddedHost = host;
            _embeddedRequestedRaceId = requestedRaceId;
            _embeddedAvailableRaceIds = availableRaceIds ?? Array.Empty<string>();
            _embeddedCloseAction = onClose;
        }

        void OnEnable()
        {
            EnsureEventSystem();
            CatalogLoader.OnCatalogReady += HandleCatalogReady;

            var nm = NetworkManager.Instance;
            if (nm == null || _isEmbeddedViewer)
                return;

            nm.OnMLLoadoutPhaseStart += HandlePhaseStart;
            nm.OnMLLoadoutPhaseEnd += HandlePhaseEnd;
            nm.OnMLMatchConfig += HandleMatchConfig;
            nm.OnMLMatchPreparationState += HandlePreparationState;
            nm.OnMLMatchCancelled += HandleMatchCancelled;
        }

        void OnDisable()
        {
            CatalogLoader.OnCatalogReady -= HandleCatalogReady;

            var nm = NetworkManager.Instance;
            if (nm != null && !_isEmbeddedViewer)
            {
                nm.OnMLLoadoutPhaseStart -= HandlePhaseStart;
                nm.OnMLLoadoutPhaseEnd -= HandlePhaseEnd;
                nm.OnMLMatchConfig -= HandleMatchConfig;
                nm.OnMLMatchPreparationState -= HandlePreparationState;
                nm.OnMLMatchCancelled -= HandleMatchCancelled;
            }

            _portraitCache.Clear();
            _pendingPortraitTargets.Clear();
            _capturePending.Clear();
            _captureQueue.Clear();
            _buildingIconCache.Clear();
            StopWarmupRoutines();
            DestroyRuntimePortraitStudio();
        }

        void Start()
        {
            if (_isEmbeddedViewer)
            {
                OpenEmbeddedViewer();
                return;
            }

            BuildPrepOverlay();

            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent != null && remoteContent.HasCompletedLoadoutPreload)
            {
                _criticalWarmupDone = true;
                TryEmitLoadoutReady();
            }

            var nm = NetworkManager.Instance;
            if (nm != null)
            {
                if (nm.PendingLoadoutPhase != null)
                {
                    HandlePhaseStart(nm.PendingLoadoutPhase);
                    return;
                }

                if (nm.LastPreparationState != null)
                    HandlePreparationState(nm.LastPreparationState);
            }

            if (ProgressionViewerLaunchContext.TryConsume(out var request))
            {
                OpenLobbyViewer(request.raceId);
                return;
            }

            Debug.LogWarning("[RaceProgression] Loadout scene opened without a pending pre-match phase. Falling back to the lobby viewer.");
            OpenLobbyViewer(RaceProgressionCatalog.DefaultRaceId);
        }

        void Update()
        {
            if (_mode == ProgressionViewerMode.PreMatchConfirm && _state == PhaseState.Active)
            {
                _timerRemaining -= Time.deltaTime;
                if (_txtTimer != null)
                {
                    int secs = Mathf.CeilToInt(_timerRemaining);
                    _txtTimer.text = secs > 0 ? $"{secs}s" : "0s";
                    _txtTimer.color = _timerRemaining <= 5f ? timerUrgentColor : timerNormalColor;
                }

                if (_timerRemaining <= 0f)
                    SubmitConfirm();
            }

            RefreshPendingPortraits();
            RefreshFallbackPlayerPanel();
        }

        void HandleCatalogReady()
        {
            if (_panelRoot == null || _selectedRace == null)
                return;

            RebuildPanel();
        }

        void OpenLobbyViewer(string requestedRaceId)
        {
            _mode = ProgressionViewerMode.LobbyViewer;
            _state = PhaseState.Viewing;
            _activePage = WizardPage.RaceSelection;
            _availableRaceIds = GetAvailableRaceIds(null);
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, requestedRaceId, "lobby viewer");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "lobby viewer");
            _selectedUnit = GetDefaultUnit(_selectedRace);
            _timerRemaining = 0f;
            _phaseStartTime = Time.unscaledTime;
            HidePrepOverlay();
            RebuildPanel();
            StartViewerWarmup();
        }

        void OpenEmbeddedViewer()
        {
            _mode = ProgressionViewerMode.LobbyViewer;
            _state = PhaseState.Viewing;
            _activePage = WizardPage.RaceSelection;
            _availableRaceIds = GetAvailableRaceIds(_embeddedAvailableRaceIds);
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, _embeddedRequestedRaceId, "embedded viewer");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "embedded viewer");
            _selectedUnit = GetDefaultUnit(_selectedRace);
            _timerRemaining = 0f;
            _phaseStartTime = Time.unscaledTime;
            RebuildPanel();
            StartViewerWarmup();
        }

        void HandlePhaseStart(MLLoadoutPhaseStartPayload payload)
        {
            _mode = ProgressionViewerMode.PreMatchConfirm;
            _state = PhaseState.Active;
            _activePage = WizardPage.RaceSelection;
            _timerRemaining = Mathf.Max(1f, payload != null ? payload.timeoutSeconds : 0f);
            _phaseStartTime = Time.unscaledTime;
            _loadoutReadyEmitted = false;
            _gameplayTransitionStarted = false;

            var remoteContent = RemoteContentManager.EnsureInstance();
            _criticalWarmupDone = remoteContent != null && remoteContent.HasCompletedLoadoutPreload;

            _availableRaceIds = GetAvailableRaceIds(payload != null ? payload.availableRaceIds : null);
            string requestedRaceId = payload != null
                ? (!string.IsNullOrWhiteSpace(payload.selectedRaceId) ? payload.selectedRaceId : payload.defaultRaceId)
                : RaceProgressionCatalog.DefaultRaceId;
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, requestedRaceId, "ml_loadout_phase_start");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "ml_loadout_phase_start");
            _selectedUnit = GetDefaultUnit(_selectedRace);

            HidePrepOverlay();
            RebuildPanel();
            StartPreMatchWarmup();

            if (payload != null && string.Equals(payload.selectionMode, "random", StringComparison.OrdinalIgnoreCase))
                StartCoroutine(AutoConfirmDelayed(0.75f));
        }

        void HandlePhaseEnd(MLLoadoutPhaseEndPayload _)
        {
            if (_mode == ProgressionViewerMode.PreMatchConfirm && _state == PhaseState.Active)
                SubmitConfirm();
        }

        void HandleMatchConfig(MLMatchConfig cfg)
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm)
                return;

            if (cfg == null || cfg.loadout == null || cfg.loadout.Length == 0 || _gameplayTransitionStarted)
                return;

            _gameplayTransitionStarted = true;
            _state = PhaseState.WaitingForMatch;
            if (_txtStatus != null)
                _txtStatus.text = "Race confirmed. Preparing the battlefield...";
            if (_btnPrimaryAction != null)
                _btnPrimaryAction.interactable = false;
            ShowWaitingForMatchOverlay();

            var currentRemoteContent = RemoteContentManager.EnsureInstance();
            bool needT1 = currentRemoteContent == null || !currentRemoteContent.HasCompletedWavePreload;
            bool needEnvironment = currentRemoteContent == null || !currentRemoteContent.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress);
            LoadingScreen.LoadSceneWithRemoteContentGate(
                "Game_ML",
                preloadT1Gameplay: needT1,
                preloadEnvironment: needEnvironment);
        }

        void HandlePreparationState(MLMatchPreparationStatePayload payload)
        {
            if (payload?.players == null)
                return;

            UpdatePlayerPanel(payload.players);
            if (_state == PhaseState.WaitingForMatch && _txtPrepDetail != null)
            {
                int ready = 0;
                int total = 0;
                for (int i = 0; i < payload.players.Length; i++)
                {
                    total++;
                    if (payload.players[i].gameplayReady)
                        ready++;
                }

                _txtPrepDetail.text = ready >= total
                    ? "All players ready. Starting match..."
                    : $"Waiting for players ({ready}/{total} ready)";
            }
        }

        void HandleMatchCancelled(MLMatchCancelledPayload payload)
        {
            Debug.LogWarning($"[RaceProgression] Match cancelled: {payload?.message}");
            _state = PhaseState.Done;
            if (_panelRoot != null)
                _panelRoot.SetActive(false);
            HidePrepOverlay();
            StartCoroutine(ShowCancelledAndReturn(payload?.message ?? "Match cancelled."));
        }

        IEnumerator ShowCancelledAndReturn(string message)
        {
            ShowPrepOverlay();
            if (_txtPrepStatus != null)
                _txtPrepStatus.text = "Match Cancelled";
            if (_txtPrepDetail != null)
                _txtPrepDetail.text = message;
            yield return new WaitForSeconds(3f);
            LoadingScreen.LoadScene("Lobby");
        }

        void StartViewerWarmup()
        {
            StopWarmupRoutines();
            _pendingPortraitTargets.Clear();
            _portraitWarmupRoutine = StartCoroutine(WarmPortraitsInBackground());
        }

        void StartPreMatchWarmup()
        {
            StopWarmupRoutines(stopCritical: false);
            _pendingPortraitTargets.Clear();

            if (!_criticalWarmupDone && _criticalWarmupRoutine == null)
                _criticalWarmupRoutine = StartCoroutine(WarmCriticalContentInBackground());

            _portraitWarmupRoutine = StartCoroutine(WarmPortraitsInBackground());
            _environmentWarmupRoutine = StartCoroutine(WarmEnvironmentInBackground());
            TryEmitLoadoutReady();
        }

        void RebuildPanel()
        {
            DestroyPanel();

            Transform parent = _embeddedHost;
            if (parent == null)
            {
                Canvas canvas = FindCanvasInCurrentScene();
                if (canvas == null)
                {
                    Debug.LogWarning("[RaceProgression] No Canvas found in the Loadout scene.");
                    return;
                }

                parent = canvas.transform;
            }

            if (parent == null)
            {
                Debug.LogWarning("[RaceProgression] No valid host found for the race progression viewer.");
                return;
            }

            _panelRoot = new GameObject("Panel_RaceProgression");
            _panelRoot.transform.SetParent(parent, false);
            var rootRect = _panelRoot.AddComponent<RectTransform>();
            if (_isEmbeddedViewer)
            {
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;
            }
            else
            {
                rootRect.anchorMin = new Vector2(0.015f, 0.025f);
                rootRect.anchorMax = new Vector2(0.985f, 0.975f);
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;
            }
            _panelRoot.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.09f, 0.97f);

            var layout = _panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(14, 14, 14, 14);

            _txtTitle = MakeLabel(_panelRoot.transform, "Txt_Title", "Race Progression", 28, Color.white, 34f);
            _txtTitle.fontStyle = FontStyles.Bold;

            _txtSubtitle = MakeLabel(_panelRoot.transform, "Txt_Subtitle", "", 14, new Color(0.82f, 0.85f, 0.92f), 24f);
            _txtSubtitle.alignment = TextAlignmentOptions.Center;

            _txtTimer = MakeLabel(_panelRoot.transform, "Txt_Timer", "", 20, timerNormalColor, 26f);
            _txtTimer.gameObject.SetActive(_mode == ProgressionViewerMode.PreMatchConfirm);

            _txtStatus = MakeLabel(_panelRoot.transform, "Txt_Status", "", 14, new Color(0.74f, 0.78f, 0.85f), 22f);
            _txtStatus.alignment = TextAlignmentOptions.Center;

            BuildCurrentPage(_panelRoot.transform);

            if (_mode == ProgressionViewerMode.PreMatchConfirm)
                BuildPlayerPanel();

            BuildActionRow(_panelRoot.transform);
            RefreshCopy();
            RefreshVisuals();
        }

        void DestroyPanel()
        {
            if (_panelRoot != null)
                Destroy(_panelRoot);

            _panelRoot = null;
            _playerPanelRoot = null;
            _playerRows.Clear();
            _raceCards.Clear();
            _unitCards.Clear();
            _requirementCards.Clear();
            _arrowViews.Clear();
            _treeTabButtons.Clear();
            _pendingPortraitTargets.Clear();
            _detailsPortrait = null;
            _txtDetailsTitle = null;
            _txtDetailsStats = null;
            _txtDetailsState = null;
            _txtDetailsRequirement = null;
            _txtDetailsBody = null;
            _btnPrimaryAction = null;
            _txtPrimaryAction = null;
            _btnSecondaryAction = null;
            _txtSecondaryAction = null;
            _txtTitle = null;
            _txtSubtitle = null;
            _txtTimer = null;
            _txtStatus = null;
        }

        void BuildCurrentPage(Transform parent)
        {
            switch (_activePage)
            {
                case WizardPage.RaceSelection:
                    BuildRaceSelector(parent);
                    break;
                case WizardPage.ProgressionTree:
                    BuildProgressionTree(parent);
                    break;
                case WizardPage.UnitDetails:
                    BuildDetailsPanel(parent);
                    break;
            }
        }

        void BuildRaceSelector(Transform parent)
        {
            var section = CreateSectionPanel(parent, "Section_RaceSelector", new Color(0.08f, 0.11f, 0.17f, 0.98f), 0f, flexibleHeight: 1f);
            MakeLabel(section.transform, "Txt_RaceHeader", "Select Race", 16, Color.white, 24f).fontStyle = FontStyles.Bold;

            var row = new GameObject("RaceRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(section.transform, false);
            row.GetComponent<LayoutElement>().preferredHeight = RaceCardHeight;
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 10f;

            for (int i = 0; i < _availableRaceIds.Length; i++)
            {
                var race = RaceProgressionCatalog.GetOrDefault(_availableRaceIds[i], "race selector");
                _raceCards.Add(BuildRaceCard(row.transform, race));
            }
        }

        RaceCardView BuildRaceCard(Transform parent, RaceProgressionDefinition race)
        {
            var cardGo = new GameObject($"Race_{race.Id}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(Button));
            cardGo.transform.SetParent(parent, false);
            cardGo.GetComponent<LayoutElement>().preferredWidth = RaceCardWidth;
            cardGo.GetComponent<LayoutElement>().preferredHeight = RaceCardHeight;

            var background = cardGo.GetComponent<Image>();
            background.color = lockedColor;

            var button = cardGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnRaceSelected(race.Id));

            var frame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(cardGo.transform, false);
            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0f, 0f);
            frameRect.anchorMax = new Vector2(0f, 1f);
            frameRect.pivot = new Vector2(0f, 0.5f);
            frameRect.sizeDelta = new Vector2(92f, 0f);
            frameRect.anchoredPosition = new Vector2(10f, 0f);
            frame.GetComponent<Image>().color = new Color(0.11f, 0.15f, 0.24f, 1f);

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(frame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(6f, 6f);
            portraitRect.offsetMax = new Vector2(-6f, -6f);
            var portrait = portraitGo.GetComponent<RawImage>();
            portrait.color = new Color(1f, 1f, 1f, 0f);
            portrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 1f;
            StartPortraitCapture(race.FeaturedPortraitKey, portrait);

            var title = CreateAnchoredText(cardGo.transform, "Txt_Title", race.DisplayName, 22, Color.white, new Vector2(0f, 0.52f), new Vector2(1f, 0.88f), new Vector2(112f, 0f), new Vector2(-10f, 0f));
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;

            var subtitle = CreateAnchoredText(cardGo.transform, "Txt_Subtitle", race.FeaturedTitle, 13, new Color(0.82f, 0.88f, 0.96f), new Vector2(0f, 0.18f), new Vector2(1f, 0.50f), new Vector2(112f, 0f), new Vector2(-10f, 0f));
            subtitle.alignment = TextAlignmentOptions.TopLeft;
            subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            subtitle.overflowMode = TextOverflowModes.Ellipsis;

            return new RaceCardView
            {
                RaceId = race.Id,
                Background = background,
                Button = button,
                Title = title,
                Subtitle = subtitle,
                Portrait = portrait,
            };
        }

        void BuildProgressionTree(Transform parent)
        {
            var section = CreateSectionPanel(parent, "Section_Tree", new Color(0.07f, 0.10f, 0.16f, 0.98f), 0f, flexibleHeight: 1f);
            MakeLabel(section.transform, "Txt_TreeHeader", "Upgrade Progression Tree", 16, Color.white, 24f).fontStyle = FontStyles.Bold;

            BuildTreeTabBar(section.transform);

            var scrollGo = new GameObject("TreeScroll", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            scrollGo.transform.SetParent(section.transform, false);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var scrollLayout = scrollGo.GetComponent<LayoutElement>();
            scrollLayout.minHeight = 0f;
            scrollLayout.preferredHeight = 0f;
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.flexibleWidth = 1f;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = Color.white;
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            var contentLayoutElement = contentGo.GetComponent<LayoutElement>();
            contentLayoutElement.flexibleWidth = 1f;
            contentLayoutElement.flexibleHeight = 1f;

            var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 12f;
            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            if (_selectedRace == null || _selectedRace.Lanes == null)
                return;

            for (int laneIndex = 0; laneIndex < _selectedRace.Lanes.Length; laneIndex++)
            {
                var lane = _selectedRace.Lanes[laneIndex];
                if (lane == null || lane.Tab != _selectedTreeTab)
                    continue;

                BuildLaneRow(contentGo.transform, lane);
            }

            if (contentGo.transform.childCount == 0)
                BuildTreeSectionHeader(contentGo.transform, "No progression rows in this tab yet");

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(section.GetComponent<RectTransform>());
        }

        void BuildTreeTabBar(Transform parent)
        {
            var tabBar = new GameObject("TreeTabBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            tabBar.transform.SetParent(parent, false);
            tabBar.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.20f, 0.98f);
            var layoutElement = tabBar.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 48f;
            layoutElement.flexibleWidth = 1f;

            var layout = tabBar.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            BuildTreeTabButton(tabBar.transform, RaceProgressionTab.Units, "Units");
            BuildTreeTabButton(tabBar.transform, RaceProgressionTab.Buildings, "Buildings");
            BuildTreeTabButton(tabBar.transform, RaceProgressionTab.Siege, "Siege");
            BuildTreeTabButton(tabBar.transform, RaceProgressionTab.Abilities, "Abilities");
        }

        void BuildTreeTabButton(Transform parent, RaceProgressionTab tab, string label)
        {
            var buttonGo = new GameObject($"Tab_{tab}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(Button));
            buttonGo.transform.SetParent(parent, false);
            var layoutElement = buttonGo.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = 32f;
            layoutElement.flexibleWidth = 1f;

            var background = buttonGo.GetComponent<Image>();
            background.color = new Color(0.15f, 0.18f, 0.27f, 1f);

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnTreeTabSelected(tab));

            var text = CreateAnchoredText(
                buttonGo.transform,
                "Txt_Label",
                label,
                12,
                Color.white,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 4f),
                new Vector2(-8f, -4f));
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;

            _treeTabButtons.Add(new TreeTabButtonView
            {
                Tab = tab,
                Background = background,
                Button = button,
                Label = text,
            });
        }

        void BuildLaneRow(Transform parent, RaceProgressionLaneDefinition lane)
        {
            if (lane?.Units == null)
                return;

            var laneGo = new GameObject($"Lane_{lane.Id}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            laneGo.transform.SetParent(parent, false);
            laneGo.GetComponent<Image>().color = new Color(0.09f, 0.12f, 0.19f, 0.92f);
            var laneLayoutElement = laneGo.GetComponent<LayoutElement>();
            laneLayoutElement.minWidth = 0f;
            laneLayoutElement.preferredWidth = 0f;
            laneLayoutElement.preferredHeight = ResolveLaneRowHeight(lane);
            laneLayoutElement.flexibleWidth = 1f;

            var layout = laneGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(12, 12, 12, 12);

            var headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            headerRow.transform.SetParent(laneGo.transform, false);
            var headerLayoutElement = headerRow.GetComponent<LayoutElement>();
            headerLayoutElement.preferredHeight = 26f;
            headerLayoutElement.flexibleWidth = 1f;
            var headerLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            headerLayout.spacing = 10f;

            var laneHeader = CreateInlineText(headerRow.transform, "Txt_Lane", lane.Label, 15f, new Color(0.90f, 0.93f, 1f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            SetSingleLine(laneHeader);

            var laneSummary = CreateInlineText(
                headerRow.transform,
                "Txt_LaneSummary",
                BuildLaneSummaryText(lane),
                11f,
                new Color(0.72f, 0.79f, 0.90f),
                FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            SetSingleLine(laneSummary);

            if (lane.Layout == RaceProgressionLaneLayout.BuildingStepsToOutcomeCards)
                BuildCivicLaneRows(laneGo.transform, lane);
            else
                BuildLinearChainRow(laneGo.transform, lane);
        }

        void BuildUnitCard(Transform parent, RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return;

            if (unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
            {
                BuildBuildingTierCard(parent, lane, unit);
                return;
            }

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
            {
                BuildRequirementStepCard(parent, lane, unit);
                return;
            }

            if (unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
            {
                BuildUpgradeStepCard(parent, lane, unit);
                return;
            }

            BuildFeaturedUnitCard(parent, lane, unit);
        }

        string BuildLaneSummaryText(RaceProgressionLaneDefinition lane)
        {
            if (lane == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(lane.SummaryOverride))
                return lane.SummaryOverride;

            if (lane.Layout == RaceProgressionLaneLayout.BuildingStepsToOutcomeCards)
            {
                int buildingCount = CountLaneUnits(lane);
                int heroCount = CountOutcomeUnits(lane);
                return $"{buildingCount} building steps -> {heroCount} hero unlocks";
            }

            int stepCount = Mathf.Max(0, CountLaneUnits(lane) - 1);
            return stepCount <= 0
                ? "Single-tier lane"
                : $"{stepCount} upgrade {(stepCount == 1 ? "step" : "steps")}";
        }

        void BuildLinearChainRow(Transform parent, RaceProgressionLaneDefinition lane)
        {
            var chainRow = new GameObject("ChainRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            chainRow.transform.SetParent(parent, false);
            var chainLayoutElement = chainRow.GetComponent<LayoutElement>();
            chainLayoutElement.minWidth = 0f;
            chainLayoutElement.preferredWidth = 0f;
            chainLayoutElement.preferredHeight = ResolveLaneChainHeight(lane);
            chainLayoutElement.flexibleWidth = 1f;
            var chainLayout = chainRow.GetComponent<HorizontalLayoutGroup>();
            chainLayout.childAlignment = TextAnchor.MiddleCenter;
            chainLayout.childControlWidth = true;
            chainLayout.childControlHeight = true;
            chainLayout.childForceExpandWidth = false;
            chainLayout.childForceExpandHeight = false;
            chainLayout.spacing = 8f;

            for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
            {
                var unit = lane.Units[unitIndex];
                if (unit == null)
                    continue;

                if (unitIndex == 0
                    && lane.ShowRequirementCards
                    && unit.UnlockRequirement != null
                    && (unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                        || unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
                    && !unit.SuppressInlineRequirementCard)
                {
                    BuildRequirementCard(chainRow.transform, lane, null, unit);
                    BuildArrow(chainRow.transform, lane.Id, unit.Id);
                }

                BuildUnitCard(chainRow.transform, lane, unit);
                if (unitIndex < lane.Units.Length - 1)
                {
                    var nextUnit = lane.Units[unitIndex + 1];
                    if (nextUnit == null)
                        continue;

                    if (lane.ShowRequirementCards
                        && nextUnit.UnlockRequirement != null
                        && !nextUnit.SuppressInlineRequirementCard)
                    {
                        BuildArrow(chainRow.transform, lane.Id, nextUnit.Id);
                        BuildRequirementCard(chainRow.transform, lane, unit, nextUnit);
                        BuildArrow(chainRow.transform, lane.Id, nextUnit.Id);
                    }
                    else
                    {
                        BuildArrow(chainRow.transform, lane.Id, nextUnit.Id);
                    }
                }
            }
        }

        void BuildTreeSectionHeader(Transform parent, string title)
        {
            var headerGo = new GameObject("TreeSectionHeader", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            headerGo.transform.SetParent(parent, false);
            headerGo.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f, 0.98f);
            var layout = headerGo.GetComponent<LayoutElement>();
            layout.preferredHeight = 40f;
            layout.flexibleWidth = 1f;

            var text = CreateAnchoredText(
                headerGo.transform,
                "Txt_SectionTitle",
                title,
                15,
                new Color(0.96f, 0.91f, 0.72f),
                Vector2.zero,
                Vector2.one,
                new Vector2(12f, 6f),
                new Vector2(-12f, -6f));
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        void BuildCivicLaneRows(Transform parent, RaceProgressionLaneDefinition lane)
        {
            var progressionRow = new GameObject("ProgressionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            progressionRow.transform.SetParent(parent, false);
            var progressionLayoutElement = progressionRow.GetComponent<LayoutElement>();
            progressionLayoutElement.minWidth = 0f;
            progressionLayoutElement.preferredWidth = 0f;
            progressionLayoutElement.preferredHeight = UpgradeStepCardHeight;
            progressionLayoutElement.flexibleWidth = 1f;
            var progressionLayout = progressionRow.GetComponent<HorizontalLayoutGroup>();
            progressionLayout.childAlignment = TextAnchor.MiddleCenter;
            progressionLayout.childControlWidth = true;
            progressionLayout.childControlHeight = true;
            progressionLayout.childForceExpandWidth = false;
            progressionLayout.childForceExpandHeight = false;
            progressionLayout.spacing = 8f;

            for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
            {
                var unit = lane.Units[unitIndex];
                if (unit == null)
                    continue;

                BuildUnitCard(progressionRow.transform, lane, unit);
                if (unitIndex < lane.Units.Length - 1)
                {
                    var nextUnit = lane.Units[unitIndex + 1];
                    if (nextUnit == null)
                        continue;

                    BuildArrow(progressionRow.transform, lane.Id, nextUnit.Id, CompactArrowWidth);
                }
            }

            var unlockHint = new GameObject("HeroUnlockHint", typeof(RectTransform), typeof(LayoutElement));
            unlockHint.transform.SetParent(parent, false);
            unlockHint.GetComponent<LayoutElement>().preferredHeight = 24f;
            var unlockText = CreateAnchoredText(
                unlockHint.transform,
                "Txt_HeroUnlockHint",
                "Castle -> Hero Unlocks",
                12,
                new Color(0.98f, 0.91f, 0.66f, 0.98f),
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            unlockText.alignment = TextAlignmentOptions.Center;
            unlockText.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(unlockText, 9f, 12f);

            BuildHeroOutcomeSection(parent, lane);
        }

        void BuildHeroOutcomeSection(Transform parent, RaceProgressionLaneDefinition lane)
        {
            if (lane?.OutcomeUnits == null || lane.OutcomeUnits.Length == 0)
                return;

            var section = new GameObject("HeroOutcomeSection", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            section.transform.SetParent(parent, false);
            var sectionLayoutElement = section.GetComponent<LayoutElement>();
            sectionLayoutElement.minWidth = 0f;
            sectionLayoutElement.preferredWidth = 0f;
            sectionLayoutElement.preferredHeight = 228f;
            sectionLayoutElement.flexibleWidth = 1f;

            var sectionLayout = section.GetComponent<VerticalLayoutGroup>();
            sectionLayout.childAlignment = TextAnchor.UpperCenter;
            sectionLayout.childControlWidth = true;
            sectionLayout.childControlHeight = true;
            sectionLayout.childForceExpandWidth = true;
            sectionLayout.childForceExpandHeight = false;
            sectionLayout.spacing = 8f;
            sectionLayout.padding = new RectOffset(12, 12, 10, 12);

            var header = MakeLabel(
                section.transform,
                "Txt_HeroHeader",
                "Reach Castle to unlock King, Paladin, and Bishop.",
                12,
                new Color(0.90f, 0.94f, 1f),
                20f);
            header.alignment = TextAlignmentOptions.Center;
            header.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(header, 9f, 12f);

            var heroRow = new GameObject("HeroRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            heroRow.transform.SetParent(section.transform, false);
            var heroRowLayoutElement = heroRow.GetComponent<LayoutElement>();
            heroRowLayoutElement.minWidth = 0f;
            heroRowLayoutElement.preferredWidth = 0f;
            heroRowLayoutElement.preferredHeight = UnitCardHeight;
            heroRowLayoutElement.flexibleWidth = 1f;
            var heroRowLayout = heroRow.GetComponent<HorizontalLayoutGroup>();
            heroRowLayout.childAlignment = TextAnchor.MiddleCenter;
            heroRowLayout.childControlWidth = true;
            heroRowLayout.childControlHeight = true;
            heroRowLayout.childForceExpandWidth = false;
            heroRowLayout.childForceExpandHeight = false;
            heroRowLayout.spacing = 10f;

            for (int outcomeIndex = 0; outcomeIndex < lane.OutcomeUnits.Length; outcomeIndex++)
            {
                var outcomeUnit = lane.OutcomeUnits[outcomeIndex];
                if (outcomeUnit == null)
                    continue;

                BuildUnitCard(heroRow.transform, lane, outcomeUnit);
            }
        }

        void BuildFeaturedUnitCard(Transform parent, RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            var cardGo = new GameObject($"Unit_{unit.Id}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(CanvasGroup), typeof(Button));
            cardGo.transform.SetParent(parent, false);
            var layoutElement = cardGo.GetComponent<LayoutElement>();
            layoutElement.minWidth = 0f;
            layoutElement.preferredWidth = unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome
                ? HeroOutcomeCardWidth
                : 0f;
            layoutElement.preferredHeight = UnitCardHeight;
            layoutElement.flexibleWidth = unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome
                ? 0f
                : UnitCardFlexWidth;
            var background = cardGo.GetComponent<Image>();
            background.color = unlockedColor;
            var button = cardGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnUnitSelected(unit.Id));

            var layout = cardGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(10, 10, 10, 10);

            var stateStrip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            stateStrip.transform.SetParent(cardGo.transform, false);
            stateStrip.GetComponent<LayoutElement>().preferredHeight = 24f;
            var stateBackground = stateStrip.GetComponent<Image>();
            stateBackground.color = new Color(0.16f, 0.22f, 0.32f, 0.96f);
            var state = CreateInlineText(stateStrip.transform, "Txt_State", "", 11f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = Vector2.zero;
            stateRect.anchorMax = Vector2.one;
            stateRect.offsetMin = new Vector2(8f, 3f);
            stateRect.offsetMax = new Vector2(-8f, -3f);
            SetResponsiveSingleLine(state, 8f, 11f);

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(cardGo.transform, false);
            portraitFrame.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.22f, 0.98f);
            portraitFrame.GetComponent<LayoutElement>().preferredHeight = PortraitFrameHeight;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(portraitFrame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(6f, 4f);
            portraitRect.offsetMax = new Vector2(-6f, -4f);
            var portrait = portraitGo.GetComponent<RawImage>();
            portrait.color = new Color(1f, 1f, 1f, 0f);
            portrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 1f;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            iconGo.transform.SetParent(portraitFrame.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(6f, 4f);
            iconRect.offsetMax = new Vector2(-6f, -4f);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            iconGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            iconGo.GetComponent<AspectRatioFitter>().aspectRatio = 1f;

            var iconFallback = CreateInlineText(portraitFrame.transform, "Txt_IconFallback", BuildNameFallbackIcon(unit.DisplayName), 22f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var iconFallbackRect = iconFallback.rectTransform;
            iconFallbackRect.anchorMin = Vector2.zero;
            iconFallbackRect.anchorMax = Vector2.one;
            iconFallbackRect.offsetMin = Vector2.zero;
            iconFallbackRect.offsetMax = Vector2.zero;
            SetResponsiveSingleLine(iconFallback, 11f, 22f);

            ApplyFeatureCardArt(unit, portrait, icon, iconFallback);

            var name = MakeLabel(cardGo.transform, "Txt_Name", unit.DisplayName, 15, Color.white, 20f);
            name.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(name, 10f, 15f);

            var stats = MakeLabel(cardGo.transform, "Txt_Stats", BuildUnitStatsLine(unit), 10, new Color(0.86f, 0.88f, 0.92f), 20f);
            stats.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(stats, 7f, 10f);

            var laneHint = MakeLabel(
                cardGo.transform,
                "Txt_LaneHint",
                string.IsNullOrWhiteSpace(unit.CardTag) ? lane.Label : unit.CardTag,
                10,
                new Color(0.70f, 0.78f, 0.90f),
                16f);
            SetResponsiveSingleLine(laneHint, 7f, 10f);

            _unitCards[unit.Id] = new UnitCardView
            {
                Unit = unit,
                CardStyle = unit.CardStyle,
                Background = background,
                StateBackground = stateBackground,
                Button = button,
                CanvasGroup = cardGo.GetComponent<CanvasGroup>(),
                Name = name,
                Stats = stats,
                Subtitle = laneHint,
                State = state,
                Portrait = portrait,
                Icon = icon,
                IconFallback = iconFallback,
            };
        }

        void BuildUpgradeStepCard(Transform parent, RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            BuildBuildingProgressCard(parent, lane, unit, UpgradeStepCardWidth, UpgradeStepCardHeight, 0f);
        }

        void BuildBuildingTierCard(Transform parent, RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            BuildBuildingProgressCard(parent, lane, unit, 0f, BuildingCardHeight, BuildingCardFlexWidth);
        }

        void BuildRequirementStepCard(Transform parent, RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            BuildRequirementCard(parent, lane, unit, unit);
        }

        void BuildBuildingProgressCard(Transform parent, RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit, float preferredWidth, float preferredHeight, float flexibleWidth)
        {
            var cardGo = new GameObject($"Unit_{unit.Id}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(CanvasGroup), typeof(Button));
            cardGo.transform.SetParent(parent, false);
            var layoutElement = cardGo.GetComponent<LayoutElement>();
            layoutElement.minWidth = 0f;
            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredWidth = preferredWidth;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleWidth = flexibleWidth;

            var background = cardGo.GetComponent<Image>();
            background.color = unlockedColor;
            var button = cardGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnUnitSelected(unit.Id));

            var layout = cardGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(10, 10, 10, 10);

            var headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            headerRow.transform.SetParent(cardGo.transform, false);
            headerRow.GetComponent<LayoutElement>().preferredHeight = 44f;
            var headerLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;
            headerLayout.spacing = 10f;

            var name = MakeLabel(headerRow.transform, "Txt_Name", BuildBuildingCardTitle(unit), 15, Color.white, 44f);
            var nameLayout = name.GetComponent<LayoutElement>();
            if (nameLayout != null)
            {
                nameLayout.flexibleWidth = 1f;
                nameLayout.minWidth = 0f;
            }
            name.fontStyle = FontStyles.Bold;
            name.alignment = TextAlignmentOptions.Left;
            name.overflowMode = TextOverflowModes.Ellipsis;
            SetResponsiveWrappedText(name, 9f, 15f);

            var stateStrip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            stateStrip.transform.SetParent(headerRow.transform, false);
            var stateStripLayout = stateStrip.GetComponent<LayoutElement>();
            stateStripLayout.preferredWidth = 92f;
            stateStripLayout.minWidth = 92f;
            stateStripLayout.preferredHeight = 26f;
            var stateBackground = stateStrip.GetComponent<Image>();
            stateBackground.color = new Color(0.16f, 0.22f, 0.32f, 0.96f);
            var state = CreateInlineText(stateStrip.transform, "Txt_State", "", 10.5f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = Vector2.zero;
            stateRect.anchorMax = Vector2.one;
            stateRect.offsetMin = new Vector2(6f, 2f);
            stateRect.offsetMax = new Vector2(-6f, -2f);
            SetResponsiveSingleLine(state, 8f, 10.5f);

            float bodyHeight = Mathf.Max(132f, preferredHeight - 66f);
            var bodyRow = new GameObject("BodyRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            bodyRow.transform.SetParent(cardGo.transform, false);
            var bodyRowLayoutElement = bodyRow.GetComponent<LayoutElement>();
            bodyRowLayoutElement.preferredHeight = bodyHeight;
            bodyRowLayoutElement.flexibleHeight = 1f;
            var bodyLayout = bodyRow.GetComponent<HorizontalLayoutGroup>();
            bodyLayout.childAlignment = TextAnchor.UpperLeft;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = false;
            bodyLayout.childForceExpandHeight = true;
            bodyLayout.spacing = 10f;

            var imageFrame = new GameObject("ImageFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(RectMask2D));
            imageFrame.transform.SetParent(bodyRow.transform, false);
            imageFrame.GetComponent<Image>().color = new Color(0.09f, 0.13f, 0.20f, 0.98f);
            var imageLayout = imageFrame.GetComponent<LayoutElement>();
            imageLayout.minWidth = preferredWidth > 0f ? preferredWidth * 0.74f : 140f;
            imageLayout.flexibleWidth = 4f;
            imageLayout.flexibleHeight = 1f;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            iconGo.transform.SetParent(imageFrame.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(1f, 1f);
            iconRect.offsetMax = new Vector2(-1f, -1f);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var iconFitter = iconGo.GetComponent<AspectRatioFitter>();
            iconFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            iconFitter.aspectRatio = 1f;

            var iconFallback = CreateInlineText(imageFrame.transform, "Txt_IconFallback", BuildNameFallbackIcon(unit.DisplayName), 20f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var fallbackRect = iconFallback.rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;
            SetResponsiveSingleLine(iconFallback, 11f, 20f);
            ApplyBuildingCardArt(icon, iconFallback, unit);

            var infoColumn = new GameObject("InfoColumn", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            infoColumn.transform.SetParent(bodyRow.transform, false);
            var infoLayoutElement = infoColumn.GetComponent<LayoutElement>();
            infoLayoutElement.minWidth = preferredWidth > 0f ? preferredWidth * 0.18f : 78f;
            infoLayoutElement.preferredWidth = preferredWidth > 0f ? preferredWidth * 0.20f : 82f;
            infoLayoutElement.flexibleWidth = 1f;
            infoLayoutElement.flexibleHeight = 1f;
            var infoLayout = infoColumn.GetComponent<VerticalLayoutGroup>();
            infoLayout.childAlignment = TextAnchor.UpperLeft;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            infoLayout.spacing = 6f;

            var tier = CreateBuildingStatRow(
                infoColumn.transform,
                "Tier",
                BuildCompactBuildingTierValue(unit),
                new Color(0.82f, 0.87f, 0.95f));
            var time = CreateBuildingStatRow(
                infoColumn.transform,
                "Build",
                BuildBuildingTimeValue(unit),
                new Color(0.72f, 0.80f, 0.91f));
            var cost = CreateBuildingStatRow(
                infoColumn.transform,
                "Gold",
                BuildBuildingCostValue(unit),
                new Color(0.98f, 0.87f, 0.56f));
            var requirement = CreateBuildingStatRow(
                infoColumn.transform,
                "Req",
                BuildBuildingRequirementValue(lane, unit),
                new Color(0.77f, 0.82f, 0.90f));

            _unitCards[unit.Id] = new UnitCardView
            {
                Unit = unit,
                CardStyle = unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep
                    ? RaceProgressionUnitCardStyle.UpgradeStep
                    : RaceProgressionUnitCardStyle.BuildingTier,
                Background = background,
                StateBackground = stateBackground,
                Button = button,
                CanvasGroup = cardGo.GetComponent<CanvasGroup>(),
                Name = name,
                Stats = tier,
                Subtitle = time,
                Requirement = requirement,
                Cost = cost,
                State = state,
                Icon = icon,
                IconFallback = iconFallback,
            };
        }

        TMP_Text CreateBuildingStatRow(Transform parent, string labelText, string valueText, Color valueColor)
        {
            var row = new GameObject($"Stat_{labelText}", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            row.transform.SetParent(parent, false);
            var rowLayoutElement = row.GetComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = 36f;
            rowLayoutElement.minHeight = 36f;

            var rowLayout = row.GetComponent<VerticalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 0f;

            var label = MakeLabel(row.transform, "Txt_Label", labelText.ToUpperInvariant(), 8, new Color(0.66f, 0.73f, 0.84f, 0.92f), 10f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(label, 6.5f, 8f);

            var value = MakeLabel(row.transform, "Txt_Value", valueText, 11, valueColor, 18f);
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(value, 7f, 11f);
            return value;
        }

        void BuildRequirementCard(
            Transform parent,
            RaceProgressionLaneDefinition lane,
            RaceProgressionUnitDefinition sourceUnit,
            RaceProgressionUnitDefinition targetUnit,
            bool compact = false)
        {
            var requirement = targetUnit?.UnlockRequirement;
            if (requirement == null)
                return;

            string sourceId = !string.IsNullOrWhiteSpace(sourceUnit?.Id) ? sourceUnit.Id : "root";

            var cardGo = new GameObject(
                $"Requirement_{sourceId}_{targetUnit.Id}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement),
                typeof(VerticalLayoutGroup),
                typeof(CanvasGroup),
                typeof(Button));
            cardGo.transform.SetParent(parent, false);
            var layoutElement = cardGo.GetComponent<LayoutElement>();
            layoutElement.minWidth = 0f;
            layoutElement.preferredWidth = 0f;
            layoutElement.preferredHeight = compact ? CompactRequirementCardHeight : RequirementCardHeight;
            layoutElement.flexibleWidth = compact ? CompactRequirementCardFlexWidth : RequirementCardFlexWidth;

            var background = cardGo.GetComponent<Image>();
            background.color = new Color(0.15f, 0.18f, 0.24f, 0.96f);
            var button = cardGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { TryOpenRequirementInWorld(requirement); });

            var layout = cardGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 3f : 4f;
            layout.padding = compact ? new RectOffset(6, 6, 6, 6) : new RectOffset(8, 8, 8, 8);

            var iconFrame = new GameObject("IconFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconFrame.transform.SetParent(cardGo.transform, false);
            iconFrame.GetComponent<LayoutElement>().preferredWidth = compact ? 34f : 40f;
            iconFrame.GetComponent<LayoutElement>().preferredHeight = compact ? 34f : 40f;
            iconFrame.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.21f, 0.98f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(iconFrame.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            float iconInset = compact ? 5f : 6f;
            iconRect.offsetMin = new Vector2(iconInset, iconInset);
            iconRect.offsetMax = new Vector2(-iconInset, -iconInset);
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;

            float iconFallbackFont = compact ? 13f : 15f;
            var iconFallback = CreateInlineText(iconFrame.transform, "Txt_IconFallback", BuildRequirementFallbackIcon(requirement), iconFallbackFont, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var fallbackRect = iconFallback.rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;
            SetResponsiveSingleLine(iconFallback, compact ? 9f : 10f, iconFallbackFont);

            float nameFont = compact ? 11f : 12f;
            var name = MakeLabel(cardGo.transform, "Txt_Name", requirement.BuildingName, (int)Mathf.Round(nameFont), Color.white, 20f);
            name.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(name, compact ? 7f : 8f, nameFont);

            float tierFont = compact ? 9f : 10f;
            var tier = MakeLabel(cardGo.transform, "Txt_Tier", BuildRequirementTierText(requirement), (int)Mathf.Round(tierFont), new Color(0.77f, 0.82f, 0.90f), 16f);
            SetResponsiveSingleLine(tier, compact ? 6.5f : 7f, tierFont);

            var statusStrip = new GameObject("StatusStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            statusStrip.transform.SetParent(cardGo.transform, false);
            statusStrip.GetComponent<LayoutElement>().preferredHeight = compact ? 20f : 24f;
            var statusBackground = statusStrip.GetComponent<Image>();
            statusBackground.color = new Color(0.22f, 0.26f, 0.32f, 0.98f);
            float statusFont = compact ? 9.5f : 10.5f;
            var status = CreateInlineText(statusStrip.transform, "Txt_Status", "", statusFont, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(8f, 3f);
            statusRect.offsetMax = new Vector2(-8f, -3f);
            SetResponsiveSingleLine(status, compact ? 6.5f : 7f, statusFont);

            ApplyRequirementIcon(icon, iconFallback, requirement);

            _requirementCards.Add(new RequirementCardView
            {
                LaneId = lane.Id,
                SourceUnit = sourceUnit,
                TargetUnit = targetUnit,
                Requirement = requirement,
                Background = background,
                Icon = icon,
                StatusBackground = statusBackground,
                Button = button,
                CanvasGroup = cardGo.GetComponent<CanvasGroup>(),
                IconFallback = iconFallback,
                Name = name,
                Tier = tier,
                Status = status,
            });
        }

        void BuildArrow(Transform parent, string laneId, string targetUnitId, float preferredWidth = 0f)
        {
            var arrowGo = new GameObject($"Arrow_{laneId}_{targetUnitId}", typeof(RectTransform), typeof(LayoutElement));
            arrowGo.transform.SetParent(parent, false);
            var layout = arrowGo.GetComponent<LayoutElement>();
            layout.minWidth = 0f;
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = 28f;
            layout.flexibleWidth = preferredWidth > 0f ? 0f : ArrowFlexWidth;

            var label = CreateAnchoredText(arrowGo.transform, "Txt_Arrow", "->", 20, new Color(0.66f, 0.74f, 0.86f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            label.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(label, 10f, 20f);

            _arrowViews.Add(new ArrowView
            {
                LaneId = laneId,
                TargetUnitId = targetUnitId,
                Glyph = label,
            });
        }

        void BuildDetailsPanel(Transform parent)
        {
            var panel = CreateSectionPanel(parent, "Section_Details", new Color(0.08f, 0.10f, 0.15f, 0.98f), 0f, flexibleHeight: 1f);
            MakeLabel(panel.transform, "Txt_DetailsHeader", "Upgrade Details", 16, Color.white, 22f).fontStyle = FontStyles.Bold;

            var content = new GameObject("DetailsContent", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            content.transform.SetParent(panel.transform, false);
            content.GetComponent<LayoutElement>().preferredHeight = 172f;
            var horizontal = content.GetComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.UpperLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            horizontal.spacing = 12f;

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(content.transform, false);
            portraitFrame.GetComponent<Image>().color = new Color(0.11f, 0.15f, 0.22f, 1f);
            portraitFrame.GetComponent<LayoutElement>().preferredWidth = 118f;
            portraitFrame.GetComponent<LayoutElement>().preferredHeight = 148f;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(portraitFrame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(6f, 6f);
            portraitRect.offsetMax = new Vector2(-6f, -6f);
            _detailsPortrait = portraitGo.GetComponent<RawImage>();
            _detailsPortrait.color = new Color(1f, 1f, 1f, 0f);
            _detailsPortrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 1f;

            var detailsIconGo = new GameObject("BuildingIcon", typeof(RectTransform), typeof(Image));
            detailsIconGo.transform.SetParent(portraitFrame.transform, false);
            var detailsIconRect = detailsIconGo.GetComponent<RectTransform>();
            detailsIconRect.anchorMin = Vector2.zero;
            detailsIconRect.anchorMax = Vector2.one;
            detailsIconRect.offsetMin = new Vector2(14f, 14f);
            detailsIconRect.offsetMax = new Vector2(-14f, -14f);
            _detailsBuildingIcon = detailsIconGo.GetComponent<Image>();
            _detailsBuildingIcon.preserveAspect = true;
            _detailsBuildingIcon.enabled = false;

            _detailsBuildingFallback = CreateInlineText(
                portraitFrame.transform,
                "Txt_BuildingFallback",
                "",
                30f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var detailsFallbackRect = _detailsBuildingFallback.rectTransform;
            detailsFallbackRect.anchorMin = Vector2.zero;
            detailsFallbackRect.anchorMax = Vector2.one;
            detailsFallbackRect.offsetMin = Vector2.zero;
            detailsFallbackRect.offsetMax = Vector2.zero;
            _detailsBuildingFallback.gameObject.SetActive(false);

            var textColumn = new GameObject("TextColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            textColumn.transform.SetParent(content.transform, false);
            textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
            textLayout.childAlignment = TextAnchor.UpperLeft;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;
            textLayout.spacing = 4f;

            _txtDetailsTitle = MakeLabel(textColumn.transform, "Txt_Title", "", 18, Color.white, 24f);
            _txtDetailsTitle.alignment = TextAlignmentOptions.MidlineLeft;
            _txtDetailsTitle.fontStyle = FontStyles.Bold;

            _txtDetailsStats = MakeLabel(textColumn.transform, "Txt_Stats", "", 12, new Color(0.88f, 0.90f, 0.96f), 22f);
            _txtDetailsStats.alignment = TextAlignmentOptions.MidlineLeft;

            _txtDetailsState = MakeLabel(textColumn.transform, "Txt_State", "", 12, selectedColor, 20f);
            _txtDetailsState.alignment = TextAlignmentOptions.MidlineLeft;
            _txtDetailsState.fontStyle = FontStyles.Bold;

            _txtDetailsRequirement = MakeLabel(textColumn.transform, "Txt_Requirement", "", 11, new Color(0.82f, 0.86f, 0.92f), 22f);
            _txtDetailsRequirement.alignment = TextAlignmentOptions.MidlineLeft;

            _txtDetailsBody = MakeLabel(textColumn.transform, "Txt_Body", "", 12, new Color(0.78f, 0.82f, 0.90f), 64f);
            _txtDetailsBody.alignment = TextAlignmentOptions.TopLeft;
            _txtDetailsBody.textWrappingMode = TextWrappingModes.Normal;
        }

        void BuildActionRow(Transform parent)
        {
            var row = new GameObject("ActionRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 48f;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;

            _btnSecondaryAction = MakeButton(row.transform, "Btn_Secondary", "Back", 46f, new Color(0.20f, 0.26f, 0.38f, 1f));
            _txtSecondaryAction = _btnSecondaryAction.GetComponentInChildren<TMP_Text>();
            _btnSecondaryAction.onClick.AddListener(HandleSecondaryAction);

            _btnPrimaryAction = MakeButton(row.transform, "Btn_Primary", "Continue", 46f, new Color(0.20f, 0.58f, 0.30f, 1f));
            _txtPrimaryAction = _btnPrimaryAction.GetComponentInChildren<TMP_Text>();
            _btnPrimaryAction.onClick.AddListener(HandlePrimaryAction);
            RefreshPrimaryAction();
        }

        void OnRaceSelected(string raceId)
        {
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, raceId, "race button");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "race button");
            _selectedUnit = GetDefaultUnit(_selectedRace);
            NavigateToPage(WizardPage.ProgressionTree);
        }

        void OnUnitSelected(string unitId)
        {
            if (_selectedRace == null || !_selectedRace.TryGetUnit(unitId, out var unit))
                return;

            _selectedUnit = unit;
            NavigateToPage(WizardPage.UnitDetails);
        }

        void OnTreeTabSelected(RaceProgressionTab tab)
        {
            if (_selectedTreeTab == tab)
                return;

            _selectedTreeTab = tab;
            RebuildPanel();
        }

        void NavigateToPage(WizardPage page)
        {
            if (page == WizardPage.UnitDetails && _selectedUnit == null)
                _selectedUnit = GetDefaultUnit(_selectedRace);

            _activePage = page;
            RebuildPanel();
        }

        void HandlePrimaryAction()
        {
            switch (_activePage)
            {
                case WizardPage.RaceSelection:
                    if (_selectedRace != null)
                        NavigateToPage(WizardPage.ProgressionTree);
                    break;
                case WizardPage.ProgressionTree:
                case WizardPage.UnitDetails:
                    if (_mode == ProgressionViewerMode.LobbyViewer)
                        ReturnToLobby();
                    else
                        SubmitConfirm();
                    break;
            }
        }

        void HandleSecondaryAction()
        {
            switch (_activePage)
            {
                case WizardPage.UnitDetails:
                    NavigateToPage(WizardPage.ProgressionTree);
                    break;
                case WizardPage.ProgressionTree:
                    NavigateToPage(WizardPage.RaceSelection);
                    break;
                case WizardPage.RaceSelection:
                    if (_mode == ProgressionViewerMode.LobbyViewer)
                        ReturnToLobby();
                    break;
            }
        }

        RaceProgressionUnitDefinition GetDefaultUnit(RaceProgressionDefinition race)
        {
            if (race == null || race.Lanes == null)
                return null;

            for (int laneIndex = 0; laneIndex < race.Lanes.Length; laneIndex++)
            {
                var lane = race.Lanes[laneIndex];
                if (lane == null || lane.Units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
                {
                    var unit = lane.Units[unitIndex];
                    if (unit != null && unit.StartsUnlocked)
                        return unit;
                }
            }

            Debug.LogError(
                $"[RaceProgression] Race '{race.Id}' has no StartsUnlocked unit. " +
                "Runtime will not auto-select the first progression unit as a fallback.");
            return null;
        }

        string[] GetAvailableRaceIds(string[] payloadRaceIds)
        {
            if (payloadRaceIds != null && payloadRaceIds.Length > 0)
                return payloadRaceIds;

            var ids = new string[RaceProgressionCatalog.All.Count];
            for (int i = 0; i < RaceProgressionCatalog.All.Count; i++)
                ids[i] = RaceProgressionCatalog.All[i].Id;
            return ids;
        }

        void RefreshCopy()
        {
            if (_txtSubtitle != null)
                _txtSubtitle.text = BuildPageSubtitle();

            if (_txtStatus == null)
                return;

            switch (_state)
            {
                case PhaseState.Confirming:
                    _txtStatus.text = "Waiting for other players to confirm their race...";
                    break;
                case PhaseState.WaitingForMatch:
                    _txtStatus.text = "All confirmations received. Preparing the battlefield...";
                    break;
                default:
                    _txtStatus.text = BuildPageStatus();
                    break;
            }
        }

        string BuildPageSubtitle()
        {
            return _activePage switch
            {
                WizardPage.RaceSelection => "Step 1 of 3. Choose a race to begin.",
                WizardPage.ProgressionTree => "Step 2 of 3. Review the upgrade chain for this race.",
                WizardPage.UnitDetails => "Step 3 of 3. Review the selected card's stats and unlock path.",
                _ => "Review the race progression.",
            };
        }

        string BuildPageStatus()
        {
            return _activePage switch
            {
                WizardPage.RaceSelection => _selectedRace != null
                    ? $"Open {_selectedRace.DisplayName} to review its upgrade chain."
                    : "Select a race to continue.",
                WizardPage.ProgressionTree => _selectedRace != null
                    ? $"{_selectedRace.DisplayName} selected. Click any card to open its details."
                    : "Select a race to continue.",
                WizardPage.UnitDetails => _selectedUnit != null
                    ? $"{_selectedUnit.DisplayName} selected. Use Back to return to the upgrade tree."
                    : "Choose a card from the tree to continue.",
                _ => "Review the race progression.",
            };
        }

        void RefreshVisuals()
        {
            RefreshCopy();
            RefreshPrimaryAction();
            RefreshTreeTabButtons();
            RefreshRaceCards();
            RefreshUnitCards();
            RefreshRequirementCards();
            RefreshArrowVisuals();
            RefreshDetailsPanel();
        }

        void RefreshTreeTabButtons()
        {
            for (int i = 0; i < _treeTabButtons.Count; i++)
            {
                var view = _treeTabButtons[i];
                bool isSelected = view.Tab == _selectedTreeTab;
                view.Background.color = isSelected
                    ? new Color(0.86f, 0.68f, 0.30f, 1f)
                    : new Color(0.15f, 0.18f, 0.27f, 1f);

                if (view.Label != null)
                    view.Label.color = isSelected ? new Color(0.10f, 0.09f, 0.06f, 1f) : Color.white;
            }
        }

        void RefreshRaceCards()
        {
            for (int i = 0; i < _raceCards.Count; i++)
            {
                var view = _raceCards[i];
                bool selected = _selectedRace != null && string.Equals(view.RaceId, _selectedRace.Id, StringComparison.OrdinalIgnoreCase);
                view.Background.color = selected ? selectedColor : new Color(0.13f, 0.17f, 0.26f, 0.98f);
                view.Title.color = selected ? new Color(0.10f, 0.09f, 0.06f, 1f) : Color.white;
                view.Subtitle.color = selected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.82f, 0.88f, 0.96f, 1f);
            }
        }

        void RefreshUnitCards()
        {
            foreach (var pair in _unitCards)
            {
                var view = pair.Value;
                bool isSelected = _selectedUnit != null && string.Equals(view.Unit.Id, _selectedUnit.Id, StringComparison.OrdinalIgnoreCase);
                var state = GetUnitProgressState(view.Unit);

                if (view.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
                    RefreshBuildingTierCard(view, state, isSelected);
                else if (view.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
                    RefreshUpgradeStepCard(view, state, isSelected);
                else
                    RefreshFeaturedUnitCard(view, state, isSelected);
            }
        }

        void RefreshFeaturedUnitCard(UnitCardView view, UnitProgressVisualState state, bool isSelected)
        {
            Color color = ResolveUnitCardColor(state, view.Unit);
            if (isSelected)
                color = selectedColor;

            view.Background.color = color;
            view.StateBackground.color = ResolveUnitStateChipColor(state, isSelected);
            view.CanvasGroup.alpha = isSelected
                ? 1f
                : (view.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome && state == UnitProgressVisualState.Locked
                    ? 0.82f
                    : ResolveUnitCardAlpha(state));
            view.Name.color = isSelected ? new Color(0.10f, 0.09f, 0.06f, 1f) : Color.white;
            if (view.Stats != null)
                view.Stats.color = isSelected ? new Color(0.17f, 0.14f, 0.10f, 1f) : new Color(0.86f, 0.88f, 0.92f, 1f);
            if (view.Subtitle != null)
                view.Subtitle.color = isSelected ? new Color(0.19f, 0.14f, 0.09f, 1f) : new Color(0.70f, 0.78f, 0.90f, 1f);
            view.State.text = BuildUnitStateLabel(view.Unit, state);
            view.State.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : ResolveUnitStateTextColor(state);
            if (view.Portrait != null)
            {
                view.Portrait.color = state == UnitProgressVisualState.Locked && !isSelected
                    ? new Color(1f, 1f, 1f, 0.45f)
                    : Color.white;
            }
            if (view.Icon != null)
                view.Icon.color = state == UnitProgressVisualState.Locked && !isSelected ? new Color(1f, 1f, 1f, 0.58f) : Color.white;
            if (view.IconFallback != null)
                view.IconFallback.color = state == UnitProgressVisualState.Locked && !isSelected ? new Color(1f, 1f, 1f, 0.72f) : Color.white;
        }

        void RefreshUpgradeStepCard(UnitCardView view, UnitProgressVisualState state, bool isSelected)
        {
            Color color = ResolveUpgradeStepCardColor(state);
            if (isSelected)
                color = selectedColor;

            view.Background.color = color;
            view.StateBackground.color = ResolveUnitStateChipColor(state, isSelected);
            view.CanvasGroup.alpha = isSelected ? 1f : (state == UnitProgressVisualState.Locked ? 0.62f : 1f);
            view.Name.color = isSelected ? new Color(0.10f, 0.09f, 0.06f, 1f) : Color.white;
            if (view.Stats != null)
                view.Stats.color = isSelected ? new Color(0.17f, 0.14f, 0.10f, 1f) : new Color(0.82f, 0.87f, 0.95f, 1f);
            if (view.Subtitle != null)
                view.Subtitle.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.72f, 0.80f, 0.91f, 1f);
            if (view.Requirement != null)
                view.Requirement.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.77f, 0.82f, 0.90f, 1f);
            if (view.Cost != null)
                view.Cost.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.98f, 0.87f, 0.56f, 1f);
            view.State.text = BuildUnitStateLabel(view.Unit, state);
            view.State.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : ResolveUnitStateTextColor(state);
            if (view.Icon != null)
                view.Icon.color = state == UnitProgressVisualState.Locked && !isSelected ? new Color(1f, 1f, 1f, 0.58f) : Color.white;
            if (view.IconFallback != null)
                view.IconFallback.color = state == UnitProgressVisualState.Locked && !isSelected ? new Color(1f, 1f, 1f, 0.72f) : Color.white;
        }

        void RefreshBuildingTierCard(UnitCardView view, UnitProgressVisualState state, bool isSelected)
        {
            Color color = ResolveBuildingTierCardColor(state);
            if (isSelected)
                color = selectedColor;

            view.Background.color = color;
            view.StateBackground.color = ResolveUnitStateChipColor(state, isSelected);
            view.CanvasGroup.alpha = isSelected ? 1f : (state == UnitProgressVisualState.Locked ? 0.62f : 1f);
            view.Name.color = isSelected ? new Color(0.10f, 0.09f, 0.06f, 1f) : Color.white;
            if (view.Stats != null)
                view.Stats.color = isSelected ? new Color(0.17f, 0.14f, 0.10f, 1f) : new Color(0.82f, 0.87f, 0.95f, 1f);
            if (view.Subtitle != null)
                view.Subtitle.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.72f, 0.80f, 0.91f, 1f);
            if (view.Cost != null)
                view.Cost.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.98f, 0.87f, 0.56f, 1f);
            view.State.text = BuildUnitStateLabel(view.Unit, state);
            view.State.color = isSelected ? new Color(0.18f, 0.13f, 0.08f, 1f) : ResolveUnitStateTextColor(state);
            if (view.Icon != null)
                view.Icon.color = state == UnitProgressVisualState.Locked && !isSelected ? new Color(1f, 1f, 1f, 0.58f) : Color.white;
            if (view.IconFallback != null)
                view.IconFallback.color = state == UnitProgressVisualState.Locked && !isSelected ? new Color(1f, 1f, 1f, 0.72f) : Color.white;
        }

        void RefreshRequirementCards()
        {
            for (int i = 0; i < _requirementCards.Count; i++)
            {
                var view = _requirementCards[i];
                bool isSelectedLink = _selectedUnit != null
                    && (string.Equals(view.SourceUnit?.Id, _selectedUnit.Id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(view.TargetUnit.Id, _selectedUnit.Id, StringComparison.OrdinalIgnoreCase));
                var state = GetRequirementProgressState(view.TargetUnit);

                Color background = ResolveRequirementCardColor(state);
                if (isSelectedLink)
                    background = Color.Lerp(background, selectedColor, 0.38f);

                view.Background.color = background;
                view.StatusBackground.color = ResolveRequirementStatusBackground(state, isSelectedLink);
                view.CanvasGroup.alpha = state == RequirementProgressVisualState.Locked && !isSelectedLink ? 0.48f : 1f;
                view.Name.color = isSelectedLink ? new Color(0.14f, 0.11f, 0.07f, 1f) : Color.white;
                view.Tier.text = BuildRequirementTierText(view.Requirement);
                view.Tier.color = isSelectedLink ? new Color(0.18f, 0.14f, 0.09f, 1f) : new Color(0.77f, 0.82f, 0.90f, 1f);
                view.Status.text = BuildRequirementStatusLabel(state, view.Requirement);
                view.Status.color = isSelectedLink ? new Color(0.18f, 0.13f, 0.08f, 1f) : Color.white;
                view.Button.interactable = CanOpenRequirementInWorld(view.Requirement);
            }
        }

        void RefreshArrowVisuals()
        {
            for (int i = 0; i < _arrowViews.Count; i++)
            {
                var view = _arrowViews[i];
                bool selectedLane = _selectedUnit != null
                    && string.Equals(view.LaneId, _selectedUnit.LaneId, StringComparison.OrdinalIgnoreCase);
                bool availableTarget = _selectedRace != null
                    && _selectedRace.TryGetUnit(view.TargetUnitId, out var targetUnit)
                    && GetUnitProgressState(targetUnit) == UnitProgressVisualState.Available;

                view.Glyph.color = selectedLane
                    ? selectedColor
                    : availableTarget
                        ? highlightedColor
                        : new Color(0.68f, 0.74f, 0.84f, 1f);
            }
        }

        void RefreshDetailsPanel()
        {
            if (_selectedUnit == null)
                _selectedUnit = GetDefaultUnit(_selectedRace);
            if (_selectedUnit == null)
                return;

            if (_txtDetailsTitle != null)
                _txtDetailsTitle.text = _selectedUnit.DisplayName;
            if (_txtDetailsStats != null)
                _txtDetailsStats.text = BuildUnitStatsLine(_selectedUnit);
            if (_txtDetailsState != null)
                _txtDetailsState.text = BuildUnitDetailsStateText(_selectedUnit);
            if (_txtDetailsRequirement != null)
                _txtDetailsRequirement.text = BuildUnitDetailsRequirementText(_selectedUnit);
            if (_txtDetailsBody != null)
                _txtDetailsBody.text = $"{_selectedUnit.Description}\n{BuildUnitDetailsBodySuffix(_selectedUnit)}";

            RefreshDetailsPortrait(_selectedUnit);
        }

        UnitProgressVisualState GetUnitProgressState(RaceProgressionUnitDefinition unit)
        {
            if (unit == null || _selectedRace == null || !_selectedRace.TryGetLane(unit.LaneId, out var lane))
                return UnitProgressVisualState.Locked;

            if (GetOutcomeUnitIndex(lane, unit.Id) >= 0)
                return GetOutcomeUnitProgressState(lane, unit);

            if (unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                || unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
            {
                return GetBuildingUnitProgressState(lane, unit);
            }

            int unitIndex = GetUnitIndex(lane, unit.Id);
            if (unitIndex < 0)
                return UnitProgressVisualState.Locked;

            if (unit.IsStartUnit)
                return UnitProgressVisualState.Start;

            int unlockedPrefix = GetUnlockedPrefixLength(lane);
            if (unitIndex < unlockedPrefix)
                return UnitProgressVisualState.Unlocked;
            if (unitIndex == unlockedPrefix)
                return UnitProgressVisualState.Available;
            return UnitProgressVisualState.Locked;
        }

        UnitProgressVisualState GetBuildingUnitProgressState(RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            if (lane == null || unit == null)
                return UnitProgressVisualState.Locked;

            if (unit.IsStartUnit)
                return UnitProgressVisualState.Start;

            if (IsBuildingUnitOwned(unit))
                return UnitProgressVisualState.Unlocked;

            return IsBuildingUnitAvailable(lane, unit)
                ? UnitProgressVisualState.Available
                : UnitProgressVisualState.Locked;
        }

        bool IsBuildingUnitOwned(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return false;

            string buildingType = GetBuildingTypeForUnit(unit);
            if (string.IsNullOrWhiteSpace(buildingType))
                return false;

            int targetTier = GetBuildingProgressTier(unit);
            if (targetTier <= 0)
                return false;

            return GetLiveBuildingTier(buildingType) >= targetTier
                || GetCatalogStartBuildingTier(buildingType) >= targetTier;
        }

        bool IsBuildingUnitAvailable(RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            if (lane == null || unit == null)
                return false;

            if (unit.UnlockRequirement != null)
                return IsRequirementMet(unit.UnlockRequirement);

            int unitIndex = GetUnitIndex(lane, unit.Id);
            if (unitIndex <= 0)
                return true;

            var previousUnit = GetPreviousLaneUnit(lane.Units, unitIndex);
            if (previousUnit == null)
                return true;

            return previousUnit.IsStartUnit || IsBuildingUnitOwned(previousUnit);
        }

        bool IsRequirementMet(RaceProgressionRequirementDefinition requirement)
        {
            if (requirement == null)
                return true;

            string buildingType = requirement.BuildingType;
            if (string.IsNullOrWhiteSpace(buildingType))
                return false;

            int requiredTier = Mathf.Max(1, requirement.RequiredTier);
            return GetLiveBuildingTier(buildingType) >= requiredTier
                || GetCatalogStartBuildingTier(buildingType) >= requiredTier;
        }

        int GetLiveBuildingTier(string buildingType)
        {
            if (string.IsNullOrWhiteSpace(buildingType))
                return 0;

            var lane = SnapshotApplier.Instance?.MyLane;
            var pads = lane?.fortressPads;
            if (pads == null || pads.Length == 0)
                return 0;

            int maxTier = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad == null || !string.Equals(pad.buildingType, buildingType, StringComparison.OrdinalIgnoreCase))
                    continue;

                maxTier = Mathf.Max(maxTier, Mathf.Max(0, pad.tier));
            }

            return maxTier;
        }

        int GetCatalogStartBuildingTier(string buildingType)
        {
            if (_selectedRace?.Lanes == null || string.IsNullOrWhiteSpace(buildingType))
                return 0;

            int maxTier = 0;
            for (int laneIndex = 0; laneIndex < _selectedRace.Lanes.Length; laneIndex++)
            {
                var lane = _selectedRace.Lanes[laneIndex];
                if (lane?.Units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
                {
                    var unit = lane.Units[unitIndex];
                    if (unit == null
                        || !unit.IsStartUnit
                        || !string.Equals(GetBuildingTypeForUnit(unit), buildingType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    maxTier = Mathf.Max(maxTier, GetBuildingProgressTier(unit));
                }
            }

            return maxTier;
        }

        RequirementProgressVisualState GetRequirementProgressState(RaceProgressionUnitDefinition targetUnit)
        {
            if (targetUnit == null)
                return RequirementProgressVisualState.Locked;

            return GetUnitProgressState(targetUnit) switch
            {
                UnitProgressVisualState.Start => RequirementProgressVisualState.Met,
                UnitProgressVisualState.Unlocked => RequirementProgressVisualState.Met,
                UnitProgressVisualState.Available => RequirementProgressVisualState.Available,
                _ => RequirementProgressVisualState.Locked,
            };
        }

        UnitProgressVisualState GetOutcomeUnitProgressState(RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            if (lane == null || unit == null)
                return UnitProgressVisualState.Locked;

            var requirement = unit.UnlockRequirement;
            if (requirement == null)
                return UnitProgressVisualState.Available;

            return IsRequirementMet(requirement)
                ? UnitProgressVisualState.Available
                : UnitProgressVisualState.Locked;
        }

        static RaceProgressionUnitDefinition GetPreviousLaneUnit(RaceProgressionUnitDefinition[] units, int startIndexExclusive)
        {
            if (units == null)
                return null;

            for (int i = Mathf.Min(startIndexExclusive - 1, units.Length - 1); i >= 0; i--)
            {
                if (units[i] != null)
                    return units[i];
            }

            return null;
        }

        int GetUnlockedPrefixLength(RaceProgressionLaneDefinition lane)
        {
            if (lane?.Units == null)
                return 0;

            int count = 0;
            for (int i = 0; i < lane.Units.Length; i++)
            {
                var unit = lane.Units[i];
                if (unit == null || !unit.StartsUnlocked)
                    break;

                count++;
            }

            return count;
        }

        static int GetUnitIndex(RaceProgressionLaneDefinition lane, string unitId)
        {
            return GetUnitIndex(lane?.Units, unitId);
        }

        static int GetOutcomeUnitIndex(RaceProgressionLaneDefinition lane, string unitId)
        {
            return GetUnitIndex(lane?.OutcomeUnits, unitId);
        }

        static int GetUnitIndex(RaceProgressionUnitDefinition[] units, string unitId)
        {
            if (units == null || string.IsNullOrWhiteSpace(unitId))
                return -1;

            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] != null && string.Equals(units[i].Id, unitId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        static int CountLaneUnits(RaceProgressionLaneDefinition lane)
        {
            if (lane?.Units == null)
                return 0;

            int count = 0;
            for (int i = 0; i < lane.Units.Length; i++)
            {
                if (lane.Units[i] != null)
                    count++;
            }

            return count;
        }

        static bool LaneUsesBuildingTierCards(RaceProgressionLaneDefinition lane)
        {
            if (lane?.Units == null)
                return false;

            for (int i = 0; i < lane.Units.Length; i++)
            {
                if (lane.Units[i]?.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
                    return true;
            }

            return false;
        }

        static float ResolveLaneRowHeight(RaceProgressionLaneDefinition lane)
        {
            if (lane == null)
                return LaneRowHeight;

            if (lane.Layout == RaceProgressionLaneLayout.BuildingStepsToOutcomeCards)
                return CivicLaneRowHeight;

            return LaneUsesBuildingTierCards(lane) ? BuildingLaneRowHeight : LaneRowHeight;
        }

        static float ResolveLaneChainHeight(RaceProgressionLaneDefinition lane)
        {
            if (lane != null && lane.Layout == RaceProgressionLaneLayout.BuildingStepsToOutcomeCards)
                return UpgradeStepCardHeight;

            return LaneUsesBuildingTierCards(lane) ? BuildingCardHeight : UnitCardHeight;
        }

        static int CountOutcomeUnits(RaceProgressionLaneDefinition lane)
        {
            if (lane?.OutcomeUnits == null)
                return 0;

            int count = 0;
            for (int i = 0; i < lane.OutcomeUnits.Length; i++)
            {
                if (lane.OutcomeUnits[i] != null)
                    count++;
            }

            return count;
        }

        static float CalculateLaneRowWidth(RaceProgressionLaneDefinition lane)
        {
            int unitCount = CountLaneUnits(lane);
            if (unitCount <= 0)
                return MinLaneRowWidth;

            int linkCount = Mathf.Max(0, unitCount - 1);
            int childCount = unitCount + (linkCount * 3);
            float baseWidth = (unitCount * UnitCardWidth) + (linkCount * RequirementCardWidth) + (linkCount * ChainArrowWidth * 2f);
            float spacingWidth = Mathf.Max(0, childCount - 1) * 8f;
            return baseWidth + spacingWidth + 24f;
        }

        static string BuildBuildingTierLabel(RaceProgressionUnitDefinition unit)
        {
            if (unit?.CardDisplay != null && !string.IsNullOrWhiteSpace(unit.CardDisplay.TierLabel))
                return unit.CardDisplay.TierLabel;

            return !string.IsNullOrWhiteSpace(unit?.StatsSummary)
                ? unit.StatsSummary
                : "Tier";
        }

        static string BuildBuildingCardTitle(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Building";

            return string.IsNullOrWhiteSpace(unit.DisplayName)
                ? "Building"
                : unit.DisplayName.Trim();
        }

        static string BuildBuildingTimeText(RaceProgressionUnitDefinition unit)
        {
            string timeText = unit?.CardDisplay?.TimeText;
            return string.IsNullOrWhiteSpace(timeText)
                ? "Time --"
                : $"Time {timeText}";
        }

        static string BuildBuildingCostText(RaceProgressionUnitDefinition unit)
        {
            if (unit?.CardDisplay == null)
                return "Cost --";

            return $"Cost {Mathf.Max(0, unit.CardDisplay.Cost)}g";
        }

        static string BuildCompactBuildingTierValue(RaceProgressionUnitDefinition unit)
        {
            string tierLabel = BuildBuildingTierLabel(unit);
            return string.IsNullOrWhiteSpace(tierLabel)
                ? "--"
                : tierLabel.Replace("Tier ", "T");
        }

        static string BuildBuildingTimeValue(RaceProgressionUnitDefinition unit)
        {
            string timeText = unit?.CardDisplay?.TimeText;
            return string.IsNullOrWhiteSpace(timeText) ? "--" : timeText.Trim();
        }

        static string BuildBuildingCostValue(RaceProgressionUnitDefinition unit)
        {
            return unit?.CardDisplay == null
                ? "--"
                : $"{Mathf.Max(0, unit.CardDisplay.Cost)}g";
        }

        static string BuildBuildingRequirementValue(RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "--";

            if (unit.UnlockRequirement != null)
                return unit.UnlockRequirement.Label;

            return unit.IsStartUnit || GetUnitIndex(lane, unit.Id) <= 0
                ? "Start"
                : "Previous tier";
        }

        static int GetBuildingProgressTier(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return 0;

            int parsed = ExtractFirstPositiveInteger(unit.CardDisplay?.TierLabel);
            if (parsed > 0)
                return parsed;

            parsed = ExtractFirstPositiveInteger(unit.StatsSummary);
            if (parsed > 0)
                return parsed;

            parsed = ExtractFirstPositiveInteger(unit.DisplayName);
            if (parsed > 0)
                return parsed;

            return unit.IsStartUnit ? 1 : 0;
        }

        static int ExtractFirstPositiveInteger(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int value = 0;
            bool readingDigits = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    readingDigits = true;
                    value = (value * 10) + (c - '0');
                }
                else if (readingDigits)
                {
                    break;
                }
            }

            return value > 0 ? value : 0;
        }

        Color ResolveUnitCardColor(UnitProgressVisualState state, RaceProgressionUnitDefinition unit = null)
        {
            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
                return ResolveHeroOutcomeCardColor(unit, state);

            Color color = state switch
            {
                UnitProgressVisualState.Start => unlockedColor,
                UnitProgressVisualState.Unlocked => Color.Lerp(unlockedColor, highlightedColor, 0.22f),
                UnitProgressVisualState.Available => Color.Lerp(highlightedColor, selectedColor, 0.14f),
                _ => lockedColor,
            };

            return color;
        }

        static Color ResolveHeroOutcomeCardColor(RaceProgressionUnitDefinition unit, UnitProgressVisualState state)
        {
            var baseColor = ResolveHeroOutcomeBaseColor(unit);
            return state switch
            {
                UnitProgressVisualState.Available => baseColor,
                UnitProgressVisualState.Unlocked => Color.Lerp(baseColor, Color.white, 0.08f),
                _ => Color.Lerp(baseColor, new Color(0.10f, 0.11f, 0.15f, 0.98f), 0.42f),
            };
        }

        static Color ResolveHeroOutcomeBaseColor(RaceProgressionUnitDefinition unit)
        {
            return (unit?.Id ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "bishop" => new Color(0.45f, 0.27f, 0.60f, 0.98f),
                "paladin" => new Color(0.55f, 0.48f, 0.22f, 0.98f),
                "king" => new Color(0.24f, 0.39f, 0.68f, 0.98f),
                _ => new Color(0.22f, 0.19f, 0.12f, 0.98f),
            };
        }

        static Color ResolveUpgradeStepCardColor(UnitProgressVisualState state)
        {
            return state switch
            {
                UnitProgressVisualState.Start => new Color(0.30f, 0.24f, 0.12f, 0.98f),
                UnitProgressVisualState.Unlocked => new Color(0.24f, 0.30f, 0.39f, 0.98f),
                UnitProgressVisualState.Available => new Color(0.32f, 0.40f, 0.60f, 0.98f),
                _ => new Color(0.13f, 0.15f, 0.18f, 0.94f),
            };
        }

        static Color ResolveBuildingTierCardColor(UnitProgressVisualState state)
        {
            return state switch
            {
                UnitProgressVisualState.Start => new Color(0.24f, 0.28f, 0.18f, 0.98f),
                UnitProgressVisualState.Unlocked => new Color(0.22f, 0.30f, 0.38f, 0.98f),
                UnitProgressVisualState.Available => new Color(0.30f, 0.39f, 0.58f, 0.98f),
                _ => new Color(0.12f, 0.14f, 0.18f, 0.94f),
            };
        }

        static float ResolveUnitCardAlpha(UnitProgressVisualState state)
        {
            return state == UnitProgressVisualState.Locked ? 0.56f : 1f;
        }

        static string BuildUnitStateLabel(RaceProgressionUnitDefinition unit, UnitProgressVisualState state)
        {
            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
            {
                return state switch
                {
                    UnitProgressVisualState.Available => "Hero Unlocked",
                    UnitProgressVisualState.Unlocked => "Hero Unlocked",
                    _ => "Castle Required",
                };
            }

            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
            {
                return state switch
                {
                    UnitProgressVisualState.Start => "Start Tier",
                    UnitProgressVisualState.Unlocked => "Unlocked",
                    UnitProgressVisualState.Available => "Available Now",
                    _ => "Future Tier",
                };
            }

            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
            {
                return state switch
                {
                    UnitProgressVisualState.Start => "Start Tier",
                    UnitProgressVisualState.Unlocked => "Unlocked",
                    UnitProgressVisualState.Available => "Available Now",
                    _ => "Future Tier",
                };
            }

            return state switch
            {
                UnitProgressVisualState.Start => "Start Unit",
                UnitProgressVisualState.Unlocked => "Unlocked",
                UnitProgressVisualState.Available => "Available Now",
                _ => "Future Upgrade",
            };
        }

        Color ResolveUnitStateChipColor(UnitProgressVisualState state, bool isSelected)
        {
            if (isSelected)
                return new Color(0.97f, 0.87f, 0.60f, 0.96f);

            return state switch
            {
                UnitProgressVisualState.Start => new Color(0.16f, 0.30f, 0.24f, 0.96f),
                UnitProgressVisualState.Unlocked => new Color(0.16f, 0.28f, 0.34f, 0.96f),
                UnitProgressVisualState.Available => new Color(0.30f, 0.39f, 0.60f, 0.98f),
                _ => new Color(0.20f, 0.22f, 0.26f, 0.96f),
            };
        }

        static Color ResolveUnitStateTextColor(UnitProgressVisualState state)
        {
            return state switch
            {
                UnitProgressVisualState.Start => new Color(0.87f, 0.98f, 0.91f, 1f),
                UnitProgressVisualState.Unlocked => new Color(0.86f, 0.95f, 1f, 1f),
                UnitProgressVisualState.Available => new Color(0.97f, 0.89f, 0.66f, 1f),
                _ => new Color(0.90f, 0.90f, 0.94f, 1f),
            };
        }

        Color ResolveRequirementCardColor(RequirementProgressVisualState state)
        {
            return state switch
            {
                RequirementProgressVisualState.Met => new Color(0.14f, 0.27f, 0.20f, 0.96f),
                RequirementProgressVisualState.Available => new Color(0.17f, 0.23f, 0.35f, 0.98f),
                _ => new Color(0.14f, 0.16f, 0.20f, 0.94f),
            };
        }

        Color ResolveRequirementStatusBackground(RequirementProgressVisualState state, bool isSelectedLink)
        {
            if (isSelectedLink)
                return new Color(0.96f, 0.85f, 0.58f, 0.96f);

            return state switch
            {
                RequirementProgressVisualState.Met => new Color(0.18f, 0.41f, 0.28f, 0.98f),
                RequirementProgressVisualState.Available => new Color(0.36f, 0.45f, 0.68f, 0.98f),
                _ => new Color(0.29f, 0.25f, 0.16f, 0.98f),
            };
        }

        static string BuildRequirementStatusLabel(RequirementProgressVisualState state, RaceProgressionRequirementDefinition requirement)
        {
            if (state == RequirementProgressVisualState.Met)
                return "Owned";

            if (state == RequirementProgressVisualState.Available)
                return requirement != null && requirement.RequiredTier > 1
                    ? $"Requires T{requirement.RequiredTier}"
                    : "Buy It";

            return "Locked";
        }

        static string BuildRequirementTierText(RaceProgressionRequirementDefinition requirement)
        {
            if (requirement == null)
                return "Tier requirement missing";

            return requirement.RequiredTier > 1
                ? $"Tier {requirement.RequiredTier} required"
                : "Tier 1 unlock";
        }

        string BuildUnitDetailsStateText(RaceProgressionUnitDefinition unit)
        {
            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
            {
                return GetUnitProgressState(unit) == UnitProgressVisualState.Available
                    ? "Castle reached. Hero unlock is ready."
                    : "Castle must be reached before this hero unlocks.";
            }

            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
            {
                return GetUnitProgressState(unit) switch
                {
                    UnitProgressVisualState.Start => "Starting civic tier",
                    UnitProgressVisualState.Unlocked => "Civic requirement already met",
                    UnitProgressVisualState.Available => "Current civic upgrade available",
                    _ => "Locked civic upgrade",
                };
            }

            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
            {
                return GetUnitProgressState(unit) switch
                {
                    UnitProgressVisualState.Start => "Starting building tier",
                    UnitProgressVisualState.Unlocked => "Building requirement already met",
                    UnitProgressVisualState.Available => "Current building upgrade available",
                    _ => "Locked building upgrade",
                };
            }

            return GetUnitProgressState(unit) switch
            {
                UnitProgressVisualState.Start => "Start unit for this lane",
                UnitProgressVisualState.Unlocked => "Requirement already met",
                UnitProgressVisualState.Available => "Current available upgrade",
                _ => "Locked future upgrade",
            };
        }

        string BuildUnitDetailsRequirementText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null || unit.IsStartUnit)
            {
                if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
                    return "Requirement: Starting civic tier";
                if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
                    return "Requirement: Available immediately";
                return "Requirement: Start unit";
            }

            var requirement = unit.UnlockRequirement;
            if (requirement == null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
            {
                if (_selectedRace != null
                    && _selectedRace.TryGetLane(unit.LaneId, out var lane)
                    && GetUnitIndex(lane, unit.Id) == 0)
                {
                    return "Requirement: Available immediately";
                }

                return "Requirement: Previous tier in this row";
            }

            return requirement == null
                ? "Requirement: Unknown"
                : $"Requirement: {requirement.BuildingName} T{requirement.RequiredTier}";
        }

        string BuildUnitDetailsBodySuffix(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Next upgrade: Final tier in this lane.";

            if (_selectedRace != null && _selectedRace.TryGetLane(unit.LaneId, out var lane))
            {
                if (unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
                    return "Outcome: Castle unlocks this hero for Barracks summons.";

                if (unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep
                    && string.IsNullOrWhiteSpace(unit.NextUnitId)
                    && CountOutcomeUnits(lane) > 0)
                {
                    return $"Unlocks: {BuildOutcomeUnitSummary(lane)}.";
                }
            }

            return string.IsNullOrWhiteSpace(unit.NextUnitId) || _selectedRace == null || !_selectedRace.TryGetUnit(unit.NextUnitId, out var nextUnit)
                ? "Next upgrade: Final tier in this lane."
                : $"Next upgrade: {nextUnit.DisplayName}.";
        }

        string BuildOutcomeUnitSummary(RaceProgressionLaneDefinition lane)
        {
            if (lane?.OutcomeUnits == null || lane.OutcomeUnits.Length == 0)
                return "No hero unlocks";

            var names = new List<string>();
            for (int i = 0; i < lane.OutcomeUnits.Length; i++)
            {
                var outcomeUnit = lane.OutcomeUnits[i];
                if (outcomeUnit != null && !string.IsNullOrWhiteSpace(outcomeUnit.DisplayName))
                    names.Add(outcomeUnit.DisplayName);
            }

            if (names.Count == 0)
                return "No hero unlocks";
            if (names.Count == 1)
                return names[0];
            if (names.Count == 2)
                return $"{names[0]} and {names[1]}";

            return $"{string.Join(", ", names.GetRange(0, names.Count - 1))}, and {names[names.Count - 1]}";
        }

        void RefreshDetailsPortrait(RaceProgressionUnitDefinition unit)
        {
            bool showBuildingIcon = unit != null
                && (unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep
                    || unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                    || !string.IsNullOrWhiteSpace(unit.ImageResourcePath));

            if (showBuildingIcon)
            {
                if (_detailsPortrait != null)
                {
                    _detailsPortrait.texture = null;
                    _detailsPortrait.color = new Color(1f, 1f, 1f, 0f);
                }

                var sprite = GetProgressionCardArt(unit);
                if (_detailsBuildingIcon != null)
                {
                    _detailsBuildingIcon.sprite = sprite;
                    _detailsBuildingIcon.enabled = sprite != null;
                }

                if (_detailsBuildingFallback != null)
                {
                    _detailsBuildingFallback.text = BuildNameFallbackIcon(unit.DisplayName);
                    _detailsBuildingFallback.gameObject.SetActive(sprite == null);
                }

                return;
            }

            if (_detailsBuildingIcon != null)
                _detailsBuildingIcon.enabled = false;
            if (_detailsBuildingFallback != null)
                _detailsBuildingFallback.gameObject.SetActive(false);

            if (_detailsPortrait != null)
                StartPortraitCapture(unit?.PortraitKey, _detailsPortrait);
        }

        bool CanOpenRequirementInWorld(RaceProgressionRequirementDefinition requirement)
        {
            return requirement != null
                && !string.IsNullOrWhiteSpace(requirement.PadId)
                && SnapshotApplier.Instance != null;
        }

        bool TryOpenRequirementInWorld(RaceProgressionRequirementDefinition requirement)
        {
            if (!CanOpenRequirementInWorld(requirement))
                return false;

            int laneIndex = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
            if (laneIndex < 0)
                return false;

            return FortressSelectionController.OpenFortressPad(laneIndex, requirement.PadId)
                || FortressSelectionController.FocusFortressPad(laneIndex, requirement.PadId);
        }

        void ApplyRequirementIcon(Image icon, TMP_Text fallback, RaceProgressionRequirementDefinition requirement)
        {
            var sprite = GetBuildingIcon(requirement?.BuildingType);
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }

            if (fallback != null)
                fallback.gameObject.SetActive(sprite == null);
        }

        void ApplyBuildingCardArt(Image icon, TMP_Text fallback, RaceProgressionUnitDefinition unit)
        {
            var sprite = GetProgressionCardArt(unit);
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }

            if (fallback != null)
            {
                fallback.text = BuildNameFallbackIcon(unit?.DisplayName);
                fallback.gameObject.SetActive(sprite == null);
            }
        }

        void ApplyFeatureCardArt(RaceProgressionUnitDefinition unit, RawImage portrait, Image icon, TMP_Text fallback)
        {
            var sprite = GetProgressionCardArt(unit);
            bool showSprite = sprite != null;

            if (portrait != null)
            {
                portrait.texture = null;
                portrait.color = showSprite ? new Color(1f, 1f, 1f, 0f) : new Color(1f, 1f, 1f, 0f);
                portrait.gameObject.SetActive(!showSprite);
                if (!showSprite && !string.IsNullOrWhiteSpace(unit?.PortraitKey))
                    StartPortraitCapture(unit.PortraitKey, portrait);
            }

            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = showSprite;
                icon.gameObject.SetActive(showSprite);
            }

            if (fallback != null)
            {
                fallback.text = BuildNameFallbackIcon(unit?.DisplayName);
                fallback.gameObject.SetActive(!showSprite && string.IsNullOrWhiteSpace(unit?.PortraitKey));
            }
        }

        static string GetBuildingTypeForUnit(RaceProgressionUnitDefinition unit)
        {
            return unit?.CardDisplay?.BuildingType
                ?? unit?.UnlockRequirement?.BuildingType
                ?? "town_core";
        }

        Sprite GetProgressionCardArt(RaceProgressionUnitDefinition unit)
        {
            string standaloneImageResourcePath = unit?.ImageResourcePath;
            if (!string.IsNullOrWhiteSpace(standaloneImageResourcePath))
            {
                string cacheKey = $"art:{standaloneImageResourcePath}";
                if (_buildingIconCache.TryGetValue(cacheKey, out var cachedStandaloneArt))
                    return cachedStandaloneArt;

                var standaloneArt = Resources.Load<Sprite>(standaloneImageResourcePath);
                _buildingIconCache[cacheKey] = standaloneArt;
                return standaloneArt;
            }

            string imageResourcePath = unit?.CardDisplay?.ImageResourcePath;
            if (!string.IsNullOrWhiteSpace(imageResourcePath))
            {
                string cacheKey = $"art:{imageResourcePath}";
                if (_buildingIconCache.TryGetValue(cacheKey, out var cachedArt))
                    return cachedArt;

                var artSprite = Resources.Load<Sprite>(imageResourcePath);
                _buildingIconCache[cacheKey] = artSprite;
                return artSprite;
            }

            return null;
        }

        Sprite GetBuildingIcon(string buildingType)
        {
            if (string.IsNullOrWhiteSpace(buildingType))
                return null;

            if (_buildingIconCache.TryGetValue(buildingType, out var cached))
                return cached;

            string resourcePath = buildingType switch
            {
                "town_core" => "Icons/towers/fighter_icon",
                "barracks" => "Icons/towers/fighter_icon",
                "blacksmith" => "Icons/towers/fighter_icon",
                "archery_tower" => "Icons/towers/archer_icon",
                "tower_archer" => "Icons/towers/archer_icon",
                "turret" => "Icons/towers/fighter_icon",
                "stable" => "Icons/towers/fighter_icon",
                "market" => "Icons/towers/fighter_icon",
                "lumber_mill" => "Icons/towers/fighter_icon",
                "wall" => "Icons/towers/fighter_icon",
                "temple" => "Icons/towers/mage_icon",
                "wizard_tower" => "Icons/towers/mage_icon",
                "library" => "Icons/towers/mage_icon",
                _ => null,
            };

            var sprite = string.IsNullOrWhiteSpace(resourcePath) ? null : Resources.Load<Sprite>(resourcePath);
            _buildingIconCache[buildingType] = sprite;
            return sprite;
        }

        static string BuildRequirementFallbackIcon(RaceProgressionRequirementDefinition requirement)
        {
            return BuildNameFallbackIcon(requirement?.BuildingName);
        }

        static string BuildNameFallbackIcon(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "?";

            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
                return name.Substring(0, 1).ToUpperInvariant();

            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        }

        string BuildUnitStatsLine(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Stats unavailable";

            if (unit.CardDisplay != null)
                return $"{BuildBuildingTierLabel(unit)}   {BuildBuildingTimeText(unit)}   {BuildBuildingCostText(unit)}";

            if (!string.IsNullOrWhiteSpace(unit.StatsSummary))
                return unit.StatsSummary;

            if (!TryGetCatalogEntry(unit, out var catalog))
                return "CATALOG ERROR";

            string incomeText = catalog.income > 0f ? $"+{catalog.income:0.#}/wave" : "No income bonus";
            return $"HP {Mathf.RoundToInt(catalog.hp)}   Cost {catalog.send_cost}g   {incomeText}";
        }

        bool TryGetCatalogEntry(RaceProgressionUnitDefinition unit, out UnitCatalogEntry catalog)
        {
            catalog = null;
            if (unit == null || string.IsNullOrWhiteSpace(unit.CatalogKey))
                return false;

            if (CatalogLoader.UnitByKey.TryGetValue(unit.CatalogKey, out catalog) && catalog != null)
                return true;

            if (_missingCatalogLogs.Add(unit.CatalogKey))
                Debug.LogError($"[RaceProgression] Missing catalog entry for '{unit.CatalogKey}'. The unit card will render an explicit catalog error state.");

            return false;
        }

        void RefreshPrimaryAction()
        {
            if (_btnPrimaryAction == null)
                return;

            if (_btnSecondaryAction != null)
            {
                bool showSecondary = _activePage != WizardPage.RaceSelection || _mode == ProgressionViewerMode.LobbyViewer;
                _btnSecondaryAction.gameObject.SetActive(showSecondary);
                _btnSecondaryAction.interactable = showSecondary;
                if (_txtSecondaryAction != null)
                {
                    _txtSecondaryAction.text = _activePage == WizardPage.RaceSelection
                        ? "Back to Lobby"
                        : "Back";
                }
            }

            if (_activePage == WizardPage.RaceSelection)
            {
                bool canContinue = _selectedRace != null
                    && (_mode == ProgressionViewerMode.LobbyViewer || _state == PhaseState.Active || _state == PhaseState.Viewing);
                _btnPrimaryAction.interactable = canContinue;
                if (_txtPrimaryAction != null)
                    _txtPrimaryAction.text = canContinue ? "Continue" : "Waiting...";
                return;
            }

            if (_txtPrimaryAction != null)
            {
                _txtPrimaryAction.text = _mode == ProgressionViewerMode.LobbyViewer
                    ? "Close"
                    : (_state == PhaseState.Active && _selectedRace != null ? "Confirm Ready" : "Waiting...");
            }

            _btnPrimaryAction.interactable = _mode == ProgressionViewerMode.LobbyViewer
                || (_state == PhaseState.Active && _selectedRace != null);
        }

        void SubmitConfirm()
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm || _state != PhaseState.Active)
                return;

            string raceId = _selectedRace != null ? _selectedRace.Id : RaceProgressionCatalog.DefaultRaceId;
            _state = PhaseState.Confirming;
            if (_btnPrimaryAction != null)
                _btnPrimaryAction.interactable = false;
            if (_txtStatus != null)
                _txtStatus.text = "Waiting for other players to confirm their race...";

            Debug.Log($"[RaceProgression] Confirming race '{raceId}'.");
            NetworkManager.Instance?.EmitLoadoutConfirm(raceId);
        }

        IEnumerator AutoConfirmDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            SubmitConfirm();
        }

        void ReturnToLobby()
        {
            if (_isEmbeddedViewer)
            {
                _embeddedCloseAction?.Invoke();
                return;
            }

            ProgressionViewerLaunchContext.Clear();
            LoadingScreen.LoadScene("Lobby");
        }

        static void EnsureEventSystem()
        {
            var existing = EventSystem.current;
            if (existing == null)
                existing = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);

            if (existing == null)
            {
                var go = new GameObject("LoadoutEventSystem");
                existing = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                go.AddComponent<SingleEventSystem>();
                Debug.Log("[RaceProgression] Created fallback EventSystem.");
                return;
            }

            if (!existing.gameObject.activeSelf)
                existing.gameObject.SetActive(true);

            if (existing.GetComponent<BaseInputModule>() == null)
                existing.gameObject.AddComponent<StandaloneInputModule>();

            if (existing.GetComponent<SingleEventSystem>() == null)
                existing.gameObject.AddComponent<SingleEventSystem>();
        }

        Canvas FindCanvasInCurrentScene()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.gameObject.scene == gameObject.scene)
                    return canvas;
            }

            return null;
        }

        void StartPortraitCapture(string key, RawImage target)
        {
            if (target == null || string.IsNullOrWhiteSpace(key))
                return;

            if (_portraitCache.TryGetValue(key, out var cached) && cached != null)
            {
                target.texture = cached;
                target.color = Color.white;
                return;
            }

            if (ShouldUseRuntimeSkinPortrait(key))
            {
                target.texture = null;
                target.color = new Color(1f, 1f, 1f, 0f);
                TrackPendingPortraitTarget(key, target);
                QueueRuntimePortraitCapture(key);
                return;
            }

            var remoteContent = RemoteContentManager.Instance;
            string portraitLookupKey = ResolvePortraitLookupKey(key);
            if (remoteContent != null && remoteContent.TryGetLoadedPortraitTexture(portraitLookupKey, out var portrait) && portrait != null)
            {
                _portraitCache[key] = portrait;
                target.texture = portrait;
                target.color = Color.white;
                return;
            }

            target.texture = null;
            target.color = new Color(1f, 1f, 1f, 0f);
            TrackPendingPortraitTarget(key, target);
        }

        void StopWarmupRoutines(bool stopCritical = true)
        {
            if (_portraitWarmupRoutine != null)
            {
                StopCoroutine(_portraitWarmupRoutine);
                _portraitWarmupRoutine = null;
            }

            if (stopCritical && _criticalWarmupRoutine != null)
            {
                StopCoroutine(_criticalWarmupRoutine);
                _criticalWarmupRoutine = null;
            }

            if (_environmentWarmupRoutine != null)
            {
                StopCoroutine(_environmentWarmupRoutine);
                _environmentWarmupRoutine = null;
            }
        }

        IEnumerator WarmPortraitsInBackground()
        {
            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
                yield break;

            string[] portraitKeys = RaceProgressionCatalog.GetPortraitWarmupKeys(_availableRaceIds);
            if (portraitKeys == null || portraitKeys.Length == 0)
            {
                TryEmitLoadoutReady();
                yield break;
            }

            var requests = new List<string>(portraitKeys.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < portraitKeys.Length; i++)
            {
                string key = portraitKeys[i];
                if (ShouldUseRuntimeSkinPortrait(key))
                    continue;

                string portraitLookupKey = ResolvePortraitLookupKey(key);
                if (string.IsNullOrWhiteSpace(portraitLookupKey) || !seen.Add(portraitLookupKey))
                    continue;

                requests.Add(portraitLookupKey);
            }

            if (requests.Count > 0)
            {
                yield return remoteContent.EnsurePortraitsReady(
                    requests,
                    requester: _mode == ProgressionViewerMode.PreMatchConfirm
                        ? "RaceProgression.PreMatchPortraitWarmup"
                        : "RaceProgression.ViewerPortraitWarmup");
            }

            RefreshPendingPortraits();
            _portraitWarmupRoutine = null;
            TryEmitLoadoutReady();
        }

        IEnumerator WarmCriticalContentInBackground()
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm)
                yield break;

            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                _criticalWarmupDone = true;
                TryEmitLoadoutReady();
                yield break;
            }

            yield return remoteContent.PreloadLoadoutContentForSession(
                (progress, status) =>
                {
                    NetworkManager.Instance?.Emit("ml_content_progress",
                        new { percent = Mathf.Clamp01(progress * 0.5f), state = status ?? "Preparing pre-match content" });
                    UpdatePrepOverlayStatus(status ?? "Preparing pre-match content");
                },
                requester: "RaceProgression.PreMatchCriticalWarmup");

            _criticalWarmupDone = true;
            _criticalWarmupRoutine = null;
            TryEmitLoadoutReady();
        }

        IEnumerator WarmEnvironmentInBackground()
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm)
                yield break;

            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                _environmentWarmupRoutine = null;
                yield break;
            }

            yield return remoteContent.EnsureEnvironmentReady(
                RemoteContentManager.GameMlEnvironmentAddress,
                requester: "RaceProgression.EnvironmentWarmup");

            _environmentWarmupRoutine = null;
        }

        void TryEmitLoadoutReady()
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm || _loadoutReadyEmitted || !_criticalWarmupDone)
                return;

            _loadoutReadyEmitted = true;
            NetworkManager.Instance?.RequestLoadoutReady();
            NetworkManager.Instance?.Emit("ml_content_progress",
                new { percent = 0.5f, state = "Race progression ready" });
            UpdatePrepOverlayStatus("Ready. Waiting for the race progression phase to continue.");
        }

        void RefreshPendingPortraits()
        {
            if (_pendingPortraitTargets.Count == 0)
                return;

            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null)
                return;

            List<string> resolvedKeys = null;
            foreach (var entry in _pendingPortraitTargets)
            {
                if (ShouldUseRuntimeSkinPortrait(entry.Key))
                    continue;

                string portraitLookupKey = ResolvePortraitLookupKey(entry.Key);
                if (!remoteContent.TryGetLoadedPortraitTexture(portraitLookupKey, out var portrait) || portrait == null)
                    continue;

                _portraitCache[entry.Key] = portrait;
                for (int i = 0; i < entry.Value.Count; i++)
                {
                    var target = entry.Value[i];
                    if (target == null)
                        continue;

                    target.texture = portrait;
                    target.color = Color.white;
                }

                resolvedKeys ??= new List<string>();
                resolvedKeys.Add(entry.Key);
            }

            if (resolvedKeys == null)
                return;

            for (int i = 0; i < resolvedKeys.Count; i++)
                _pendingPortraitTargets.Remove(resolvedKeys[i]);
        }

        void TrackPendingPortraitTarget(string key, RawImage target)
        {
            if (!_pendingPortraitTargets.TryGetValue(key, out var targets))
            {
                targets = new List<RawImage>();
                _pendingPortraitTargets[key] = targets;
            }

            if (!targets.Contains(target))
                targets.Add(target);
        }

        void QueueRuntimePortraitCapture(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _capturePending.Contains(key))
                return;

            var portraitCam = EnsurePortraitCamera();
            if (portraitCam == null)
                return;

            _capturePending.Add(key);
            _captureQueue.Enqueue(key);
            if (!_isCapturingPortraits)
                StartCoroutine(ProcessRuntimePortraitQueue(portraitCam));
        }

        IEnumerator ProcessRuntimePortraitQueue(UnitPortraitCamera portraitCam)
        {
            _isCapturingPortraits = true;
            while (_captureQueue.Count > 0)
            {
                string key = _captureQueue.Dequeue();
                yield return EnsureRuntimePortraitPrefabReady(key);

                bool done = false;
                Texture2D captured = null;
                portraitCam.StartIconCapture(key, tex =>
                {
                    captured = tex;
                    done = true;
                });

                while (!done)
                    yield return null;

                _capturePending.Remove(key);
                if (captured == null)
                    continue;

                _portraitCache[key] = captured;
                if (_pendingPortraitTargets.TryGetValue(key, out var targets))
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var target = targets[i];
                        if (target == null)
                            continue;

                        target.texture = captured;
                        target.color = Color.white;
                    }

                    _pendingPortraitTargets.Remove(key);
                }
            }

            _isCapturingPortraits = false;
        }

        IEnumerator EnsureRuntimePortraitPrefabReady(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                yield break;

            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent != null && remoteContent.TryGetLoadedPrefabForSkin(key, out var loadedSkinPrefab) && loadedSkinPrefab != null)
                yield break;

            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry != null && registry.GetPrefabForSkin(null, key) != null)
                yield break;

            Debug.LogWarning($"[RaceProgression] Runtime skin portrait source for '{key}' is unavailable.");
        }

        UnitPortraitCamera EnsurePortraitCamera()
        {
            if (PortraitCam != null && PortraitCam.Registry != null)
                return PortraitCam;

            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry == null)
                return null;

            if (PortraitCam != null)
            {
                PortraitCam.Registry = registry;
                return PortraitCam;
            }

            if (_runtimePortraitRoot == null)
                PortraitCam = RuntimePortraitStudio.Create("RaceProgressionPortraitStudio", registry, out _runtimePortraitRoot, out _runtimePortraitTexture);

            PortraitCam.Registry = registry;
            PortraitCam.transform.position = new Vector3(0f, 0f, 50f);
            return PortraitCam;
        }

        void DestroyRuntimePortraitStudio()
        {
            if (_runtimePortraitRoot != null)
                Destroy(_runtimePortraitRoot);

            if (_runtimePortraitTexture != null)
                _runtimePortraitTexture.Release();

            _runtimePortraitRoot = null;
            _runtimePortraitTexture = null;
            PortraitCam = null;
        }

        UnitPrefabRegistry ResolvePortraitRegistry()
        {
            return RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
        }

        bool ShouldUseRuntimeSkinPortrait(string key)
        {
            string normalizedKey = key?.Trim();
            var registry = ResolvePortraitRegistry();
            if (registry == null || string.IsNullOrWhiteSpace(normalizedKey))
                return false;

            if (normalizedKey.StartsWith("tt_", StringComparison.OrdinalIgnoreCase)
                && registry.TryGet(normalizedKey, out _))
            {
                return true;
            }

            return registry.TryGetUnitTypeForSkin(normalizedKey, out _);
        }

        string ResolvePortraitLookupKey(string key)
        {
            string normalizedKey = key?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return normalizedKey;

            var registry = ResolvePortraitRegistry();
            if (registry != null
                && normalizedKey.StartsWith("tt_", StringComparison.OrdinalIgnoreCase)
                && registry.TryGet(normalizedKey, out _))
            {
                return normalizedKey;
            }

            var remoteContent = RemoteContentManager.Instance;
            string manifestResolvedKey = remoteContent != null ? remoteContent.ResolvePortraitLookupKey(normalizedKey) : normalizedKey;
            if (!string.IsNullOrWhiteSpace(manifestResolvedKey)
                && !string.Equals(manifestResolvedKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
            {
                return manifestResolvedKey;
            }

            if (registry != null && registry.TryGetUnitTypeForSkin(normalizedKey, out string unitType))
                return unitType;

            return normalizedKey;
        }

        void BuildPrepOverlay()
        {
            if (_prepOverlay != null)
                return;

            Canvas canvas = FindCanvasInCurrentScene();
            if (canvas == null)
                return;

            _prepOverlay = new GameObject("Panel_Prep");
            _prepOverlay.transform.SetParent(canvas.transform, false);
            var rect = _prepOverlay.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _prepOverlay.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.08f, 0.97f);

            var layout = _prepOverlay.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 14f;
            layout.padding = new RectOffset(40, 40, 60, 60);

            _txtPrepStatus = MakeLabel(_prepOverlay.transform, "Txt_PrepStatus", "Preparing Race Progression", 24, Color.white, 36f);
            _txtPrepDetail = MakeLabel(_prepOverlay.transform, "Txt_PrepDetail", "Downloading content...", 16, new Color(0.75f, 0.75f, 0.75f), 26f);
            _prepOverlay.SetActive(false);
        }

        void HidePrepOverlay()
        {
            if (_prepOverlay != null)
                _prepOverlay.SetActive(false);
        }

        void ShowPrepOverlay()
        {
            if (_prepOverlay == null)
                BuildPrepOverlay();
            if (_prepOverlay != null)
                _prepOverlay.SetActive(true);
        }

        void UpdatePrepOverlayStatus(string detail)
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm)
                return;

            ShowPrepOverlay();
            if (_txtPrepDetail != null)
                _txtPrepDetail.text = detail;
        }

        void ShowWaitingForMatchOverlay()
        {
            ShowPrepOverlay();
            if (_txtPrepStatus != null)
                _txtPrepStatus.text = "Preparing Battlefield";
            if (_txtPrepDetail != null)
                _txtPrepDetail.text = "Waiting for players...";
        }

        void BuildPlayerPanel()
        {
            if (_playerPanelRoot != null || _panelRoot == null)
                return;

            _playerPanelRoot = new GameObject("Panel_Players", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
            _playerPanelRoot.transform.SetParent(_panelRoot.transform, false);
            _playerPanelRoot.GetComponent<LayoutElement>().preferredHeight = 36f;
            _playerPanelRoot.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.13f, 0.9f);

            var layout = _playerPanelRoot.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 4, 4);

            _playerRows.Clear();

            var cachedPlayers = NetworkManager.Instance?.LastPreparationState?.players;
            if (cachedPlayers != null && cachedPlayers.Length > 0)
            {
                UpdatePlayerPanel(cachedPlayers);
                return;
            }

            var laneAssignments = NetworkManager.Instance?.LastMLMatchReady?.laneAssignments;
            if (laneAssignments == null || laneAssignments.Length == 0)
                return;

            UpdatePlayerPanel(BuildMissingPreparationStateRows(laneAssignments, "panel_build"));
        }

        void UpdatePlayerPanel(MLPlayerPreparationState[] players)
        {
            if (_state == PhaseState.Done || _playerPanelRoot == null || players == null)
                return;

            int myLane = NetworkManager.Instance?.MyLaneIndex ?? -1;
            while (_playerRows.Count < players.Length)
            {
                var colGo = new GameObject($"Col_{_playerRows.Count}", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(VerticalLayoutGroup));
                colGo.transform.SetParent(_playerPanelRoot.transform, false);
                var layoutElement = colGo.GetComponent<LayoutElement>();
                layoutElement.minWidth = 110f;
                layoutElement.preferredWidth = 140f;
                layoutElement.flexibleWidth = 1f;
                layoutElement.preferredHeight = 28f;
                colGo.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.16f, 0.85f);

                var layout = colGo.GetComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 1f;
                layout.padding = new RectOffset(6, 6, 3, 3);

                var name = MakeLabel(colGo.transform, "Txt_Name", "", 11, Color.white, 12f);
                var barBg = new GameObject("BarBG", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                barBg.transform.SetParent(colGo.transform, false);
                barBg.GetComponent<LayoutElement>().preferredHeight = 4f;
                barBg.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.20f, 1f);
                var fill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(barBg.transform, false);
                var fillRect = fill.GetComponent<RectTransform>();
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(0f, 1f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                var fillImage = fill.GetComponent<Image>();
                fillImage.color = new Color(0.2f, 0.7f, 0.3f, 1f);
                var state = MakeLabel(colGo.transform, "Txt_State", "", 10, new Color(0.75f, 0.75f, 0.75f), 11f);
                _playerRows.Add((name, state, fillImage));
            }

            for (int i = 0; i < players.Length && i < _playerRows.Count; i++)
            {
                var player = players[i];
                var row = _playerRows[i];
                bool isMe = player.laneIndex == myLane;
                row.name.text = isMe ? $"<b>{player.displayName}</b>" : player.displayName;
                row.name.color = isMe ? new Color(0.85f, 0.95f, 1f) : Color.white;

                string stateText;
                Color stateColor;
                if (player.gameplayReady)
                {
                    stateText = "Ready";
                    stateColor = new Color(0.3f, 0.9f, 0.4f);
                }
                else if (player.loadoutReady)
                {
                    stateText = string.IsNullOrEmpty(player.contentState) ? "Downloading" : player.contentState;
                    stateColor = new Color(0.9f, 0.8f, 0.3f);
                }
                else
                {
                    stateText = "Preparing...";
                    stateColor = new Color(0.6f, 0.6f, 0.6f);
                }

                row.state.text = stateText;
                row.state.color = stateColor;

                if (row.bar != null)
                {
                    float pct = player.gameplayReady ? 1f : Mathf.Clamp01(player.contentPercent);
                    row.bar.rectTransform.anchorMax = new Vector2(pct, 1f);
                    row.bar.color = player.gameplayReady
                        ? new Color(0.2f, 0.7f, 0.3f)
                        : new Color(0.3f, 0.55f, 0.9f);
                }
            }
        }

        void RefreshFallbackPlayerPanel()
        {
            if (_playerPanelRoot == null)
                return;

            var nm = NetworkManager.Instance;
            if (nm == null)
                return;

            var authoritative = nm.LastPreparationState?.players;
            if (authoritative != null && authoritative.Length > 0)
                return;

            var laneAssignments = nm.LastMLMatchReady?.laneAssignments;
            if (laneAssignments == null || laneAssignments.Length == 0)
                return;

            UpdatePlayerPanel(BuildMissingPreparationStateRows(laneAssignments, "panel_refresh"));
        }

        MLPlayerPreparationState[] BuildMissingPreparationStateRows(MLLaneAssignment[] laneAssignments, string source)
        {
            if (laneAssignments == null || laneAssignments.Length == 0)
                return Array.Empty<MLPlayerPreparationState>();

            if (_missingPreparationStateLogs.Add(source))
            {
                Debug.LogError(
                    $"[RaceProgression] Missing authoritative ml_match_preparation_state while building the player preparation panel from '{source}'. " +
                    "Showing an explicit server-state error instead of simulated progress.");
            }

            var players = new MLPlayerPreparationState[laneAssignments.Length];
            for (int i = 0; i < laneAssignments.Length; i++)
            {
                var lane = laneAssignments[i];
                players[i] = new MLPlayerPreparationState
                {
                    laneIndex = lane.laneIndex,
                    displayName = string.IsNullOrWhiteSpace(lane.displayName) ? $"Lane {lane.laneIndex + 1}" : lane.displayName,
                    loadoutReady = false,
                    gameplayReady = false,
                    contentPercent = 0f,
                    contentState = "NO SERVER STATE",
                };
            }

            return players;
        }

        GameObject CreateSectionPanel(Transform parent, string name, Color color, float preferredHeight, float flexibleHeight = 0f)
        {
            var section = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            section.transform.SetParent(parent, false);
            section.GetComponent<Image>().color = color;
            var layoutElement = section.GetComponent<LayoutElement>();
            if (preferredHeight > 0f)
                layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleHeight = flexibleHeight;

            var layout = section.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            return section;
        }

        static TMP_Text MakeLabel(Transform parent, string goName, string text, int fontSize, Color color, float preferredHeight)
        {
            var go = new GameObject(goName, typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;
            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = TextAlignmentOptions.Center;
            return txt;
        }

        static TMP_Text CreateAnchoredText(Transform parent, string name, string text, int fontSize, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            return label;
        }

        static TMP_Text CreateInlineText(
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
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.text = text;
            return label;
        }

        static void SetSingleLine(TMP_Text label)
        {
            if (label == null)
                return;

            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
        }

        static void SetResponsiveSingleLine(TMP_Text label, float minFontSize, float maxFontSize)
        {
            if (label == null)
                return;

            SetSingleLine(label);
            label.enableAutoSizing = true;
            label.fontSizeMin = minFontSize;
            label.fontSizeMax = maxFontSize;
        }

        static void SetResponsiveWrappedText(TMP_Text label, float minFontSize, float maxFontSize)
        {
            if (label == null)
                return;

            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.enableAutoSizing = true;
            label.fontSizeMin = minFontSize;
            label.fontSizeMax = maxFontSize;
        }

        static Button MakeButton(Transform parent, string goName, string label, float height, Color color)
        {
            var go = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            go.GetComponent<Image>().color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();

            var labelGo = new GameObject("Lbl", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            return button;
        }
    }
}
