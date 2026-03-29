using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CastleDefender.Game;
using CastleDefender.Net;
using Newtonsoft.Json.Linq;
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

        enum DetailPreviewMotion
        {
            Idle,
            Walk,
            March,
            Run,
            Strike,
            Special,
            Hit,
            Death,
        }

        enum ClassicRpgButtonSize
        {
            Medium,
            Long,
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

        const float PortraitFrameHeight = 118f;
        const float UnitCardWidth = 216f;
        const float UnitCardHeight = 252f;
        const float BuildingCardHeight = 264f;
        const float BuildingImageFrameHeight = 84f;
        const float RequirementCardWidth = 160f;
        const float RequirementCardHeight = 126f;
        const float CompactRequirementCardHeight = 116f;
        const float LaneRowHeight = 308f;
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
        GameObject _treeSectionRoot;
        GameObject _detailsOverlayRoot;
        GameObject _runtimePreviewRoot;
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
        TMP_Text _txtDetailsMoves;
        TMP_Text _txtDetailsAudioStatus;
        TMP_Text _txtDetailsBody;
        TMP_Text _txtDetailsPreviewStatus;
        Button _btnDetailsClose;
        Button _btnDetailsPreviewSfx;
        TMP_Text _txtDetailsPreviewSfx;
        Button _btnDetailsPreviewVoice;
        TMP_Text _txtDetailsPreviewVoice;
        Button _btnPreviewIdle;
        TMP_Text _txtPreviewIdle;
        Button _btnPreviewWalk;
        TMP_Text _txtPreviewWalk;
        Button _btnPreviewMarch;
        TMP_Text _txtPreviewMarch;
        Button _btnPreviewRun;
        TMP_Text _txtPreviewRun;
        Button _btnPreviewStrike;
        TMP_Text _txtPreviewStrike;
        Button _btnPreviewSpecial;
        TMP_Text _txtPreviewSpecial;
        Button _btnPreviewHit;
        TMP_Text _txtPreviewHit;
        Button _btnPreviewDeath;
        TMP_Text _txtPreviewDeath;

        RaceProgressionDefinition _selectedRace;
        RaceProgressionUnitDefinition _selectedUnit;
        string[] _availableRaceIds = Array.Empty<string>();
        float _timerRemaining;
        float _phaseStartTime;
        bool _detailsModalOpen;

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
        RenderTexture _runtimePreviewTexture;
        bool _isCapturingPortraits;
        UnitPortraitCamera _detailsPreviewCam;
        Coroutine _detailsPreviewResetRoutine;
        string _detailsPreviewStagedKey;

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
            StopDetailsPreviewResetRoutine();
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
            var rootImage = _panelRoot.AddComponent<Image>();
            rootImage.color = ClassicRpgUiRuntime.BackdropColor;
            ClassicRpgUiRuntime.ApplyPanel(rootImage, ClassicRpgPanelSkin.DarkSpell, false, new Color(1f, 1f, 1f, 0.26f));

            var rootFrame = new GameObject("RootFrame", typeof(RectTransform), typeof(Image));
            rootFrame.transform.SetParent(_panelRoot.transform, false);
            var rootFrameRect = rootFrame.GetComponent<RectTransform>();
            rootFrameRect.anchorMin = Vector2.zero;
            rootFrameRect.anchorMax = Vector2.one;
            rootFrameRect.offsetMin = new Vector2(4f, 4f);
            rootFrameRect.offsetMax = new Vector2(-4f, -4f);
            var rootFrameImage = rootFrame.GetComponent<Image>();
            rootFrameImage.raycastTarget = false;
            ClassicRpgUiRuntime.ApplyPanel(rootFrameImage, ClassicRpgPanelSkin.Frame, true, new Color(1f, 1f, 1f, 0.92f));

            var layout = _panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(14, 14, 14, 14);

            _txtTitle = MakeLabel(_panelRoot.transform, "Txt_Title", "Race Progression", 28, Color.white, 34f);
            _txtTitle.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(_txtTitle, true, true);

            _txtSubtitle = MakeLabel(_panelRoot.transform, "Txt_Subtitle", "", 16, new Color(0.82f, 0.85f, 0.92f), 28f);
            _txtSubtitle.alignment = TextAlignmentOptions.Center;
            ApplyClassicRpgLabelTheme(_txtSubtitle, false, true);

            _txtTimer = MakeLabel(_panelRoot.transform, "Txt_Timer", "", 20, timerNormalColor, 26f);
            _txtTimer.gameObject.SetActive(_mode == ProgressionViewerMode.PreMatchConfirm);
            ApplyClassicRpgLabelTheme(_txtTimer, false, true);

            _txtStatus = MakeLabel(_panelRoot.transform, "Txt_Status", "", 16, new Color(0.74f, 0.78f, 0.85f), 26f);
            _txtStatus.alignment = TextAlignmentOptions.Center;
            ApplyClassicRpgLabelTheme(_txtStatus, false, true);

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
            _treeSectionRoot = null;
            _detailsOverlayRoot = null;
            _playerPanelRoot = null;
            _playerRows.Clear();
            _raceCards.Clear();
            _unitCards.Clear();
            _requirementCards.Clear();
            _arrowViews.Clear();
            _treeTabButtons.Clear();
            _pendingPortraitTargets.Clear();
            _detailsPortrait = null;
            _detailsBuildingIcon = null;
            _detailsBuildingFallback = null;
            _txtDetailsTitle = null;
            _txtDetailsStats = null;
            _txtDetailsState = null;
            _txtDetailsRequirement = null;
            _txtDetailsMoves = null;
            _txtDetailsAudioStatus = null;
            _txtDetailsBody = null;
            _btnDetailsClose = null;
            _btnDetailsPreviewSfx = null;
            _txtDetailsPreviewSfx = null;
            _btnDetailsPreviewVoice = null;
            _txtDetailsPreviewVoice = null;
            _btnPreviewIdle = null;
            _txtPreviewIdle = null;
            _btnPreviewWalk = null;
            _txtPreviewWalk = null;
            _btnPreviewMarch = null;
            _txtPreviewMarch = null;
            _btnPreviewRun = null;
            _txtPreviewRun = null;
            _btnPreviewStrike = null;
            _txtPreviewStrike = null;
            _btnPreviewSpecial = null;
            _txtPreviewSpecial = null;
            _btnPreviewHit = null;
            _txtPreviewHit = null;
            _btnPreviewDeath = null;
            _txtPreviewDeath = null;
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
                    BuildDetailsPanel(_treeSectionRoot != null ? _treeSectionRoot.transform : parent);
                    break;
                case WizardPage.UnitDetails:
                    _activePage = WizardPage.ProgressionTree;
                    _detailsModalOpen = true;
                    BuildProgressionTree(parent);
                    BuildDetailsPanel(_treeSectionRoot != null ? _treeSectionRoot.transform : parent);
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
            _treeSectionRoot = section;
            var treeHeader = MakeLabel(section.transform, "Txt_TreeHeader", "Upgrade Progression Tree", 18, Color.white, 28f);
            treeHeader.fontStyle = FontStyles.Bold;

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
            layoutElement.preferredHeight = 54f;
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
            layoutElement.preferredHeight = 36f;
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
                14,
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
            SetResponsiveSingleLine(laneHeader, 13f, 17f);

            var laneSummary = CreateInlineText(
                headerRow.transform,
                "Txt_LaneSummary",
                BuildLaneSummaryText(lane),
                13f,
                new Color(0.72f, 0.79f, 0.90f),
                FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            SetResponsiveSingleLine(laneSummary, 11f, 13f);

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
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 10, 10);

            var stateStrip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            stateStrip.transform.SetParent(cardGo.transform, false);
            stateStrip.GetComponent<LayoutElement>().preferredHeight = 28f;
            var stateBackground = stateStrip.GetComponent<Image>();
            stateBackground.color = new Color(0.16f, 0.22f, 0.32f, 0.96f);
            var state = CreateInlineText(stateStrip.transform, "Txt_State", "", 12f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = Vector2.zero;
            stateRect.anchorMax = Vector2.one;
            stateRect.offsetMin = new Vector2(8f, 3f);
            stateRect.offsetMax = new Vector2(-8f, -3f);
            SetResponsiveSingleLine(state, 12f, 12f);

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(cardGo.transform, false);
            portraitFrame.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.22f, 0.98f);
            portraitFrame.GetComponent<LayoutElement>().preferredHeight = PortraitFrameHeight;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(portraitFrame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(8f, 4f);
            portraitRect.offsetMax = new Vector2(-8f, -4f);
            var portrait = portraitGo.GetComponent<RawImage>();
            portrait.color = new Color(1f, 1f, 1f, 0f);
            portrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 0.72f;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            iconGo.transform.SetParent(portraitFrame.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(8f, 4f);
            iconRect.offsetMax = new Vector2(-8f, -4f);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            iconGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            iconGo.GetComponent<AspectRatioFitter>().aspectRatio = 0.72f;

            var iconFallback = CreateInlineText(portraitFrame.transform, "Txt_IconFallback", BuildNameFallbackIcon(unit.DisplayName), 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var iconFallbackRect = iconFallback.rectTransform;
            iconFallbackRect.anchorMin = Vector2.zero;
            iconFallbackRect.anchorMax = Vector2.one;
            iconFallbackRect.offsetMin = Vector2.zero;
            iconFallbackRect.offsetMax = Vector2.zero;
            SetResponsiveSingleLine(iconFallback, 12f, 24f);

            ApplyFeatureCardArt(unit, portrait, icon, iconFallback);

            var name = MakeLabel(cardGo.transform, "Txt_Name", unit.DisplayName, 17, Color.white, 24f);
            name.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(name, 12f, 17f);

            var stats = MakeLabel(cardGo.transform, "Txt_Stats", BuildUnitCardStatsText(unit), 13, new Color(0.86f, 0.88f, 0.92f), 28f);
            stats.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(stats, 12f, 13f);

            var laneHint = MakeLabel(
                cardGo.transform,
                "Txt_LaneHint",
                BuildUnitCardSubtitle(lane, unit),
                12,
                new Color(0.70f, 0.78f, 0.90f),
                24f);
            SetResponsiveSingleLine(laneHint, 12f, 12f);

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

            var name = MakeLabel(headerRow.transform, "Txt_Name", BuildBuildingCardTitle(unit), 17, Color.white, 48f);
            var nameLayout = name.GetComponent<LayoutElement>();
            if (nameLayout != null)
            {
                nameLayout.flexibleWidth = 1f;
                nameLayout.minWidth = 0f;
            }
            name.fontStyle = FontStyles.Bold;
            name.alignment = TextAlignmentOptions.Left;
            name.overflowMode = TextOverflowModes.Ellipsis;
            SetResponsiveWrappedText(name, 12f, 17f);

            var stateStrip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            stateStrip.transform.SetParent(headerRow.transform, false);
            var stateStripLayout = stateStrip.GetComponent<LayoutElement>();
            stateStripLayout.preferredWidth = 92f;
            stateStripLayout.minWidth = 92f;
            stateStripLayout.preferredHeight = 26f;
            var stateBackground = stateStrip.GetComponent<Image>();
            stateBackground.color = new Color(0.16f, 0.22f, 0.32f, 0.96f);
            var state = CreateInlineText(stateStrip.transform, "Txt_State", "", 12f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = Vector2.zero;
            stateRect.anchorMax = Vector2.one;
            stateRect.offsetMin = new Vector2(6f, 2f);
            stateRect.offsetMax = new Vector2(-6f, -2f);
            SetResponsiveSingleLine(state, 12f, 12f);

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

            var label = MakeLabel(row.transform, "Txt_Label", labelText.ToUpperInvariant(), 10, new Color(0.66f, 0.73f, 0.84f, 0.92f), 12f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(label, 10f, 10f);

            var value = MakeLabel(row.transform, "Txt_Value", valueText, 13, valueColor, 20f);
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(value, 12f, 13f);
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

            float iconFallbackFont = compact ? 14f : 16f;
            var iconFallback = CreateInlineText(iconFrame.transform, "Txt_IconFallback", BuildRequirementFallbackIcon(requirement), iconFallbackFont, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var fallbackRect = iconFallback.rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;
            SetResponsiveSingleLine(iconFallback, compact ? 12f : 12f, iconFallbackFont);

            float nameFont = compact ? 12f : 14f;
            var name = MakeLabel(cardGo.transform, "Txt_Name", requirement.BuildingName, (int)Mathf.Round(nameFont), Color.white, 20f);
            name.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(name, 12f, nameFont);

            float tierFont = compact ? 12f : 13f;
            var tier = MakeLabel(cardGo.transform, "Txt_Tier", BuildRequirementTierText(requirement), (int)Mathf.Round(tierFont), new Color(0.77f, 0.82f, 0.90f), 16f);
            SetResponsiveSingleLine(tier, 12f, tierFont);

            var statusStrip = new GameObject("StatusStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            statusStrip.transform.SetParent(cardGo.transform, false);
            statusStrip.GetComponent<LayoutElement>().preferredHeight = compact ? 20f : 24f;
            var statusBackground = statusStrip.GetComponent<Image>();
            statusBackground.color = new Color(0.22f, 0.26f, 0.32f, 0.98f);
            float statusFont = compact ? 12f : 13f;
            var status = CreateInlineText(statusStrip.transform, "Txt_Status", "", statusFont, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(8f, 3f);
            statusRect.offsetMax = new Vector2(-8f, -3f);
            SetResponsiveSingleLine(status, 12f, statusFont);

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
            var overlayGo = new GameObject("Overlay_Details", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(Button));
            overlayGo.transform.SetParent(parent, false);
            var overlayLayout = overlayGo.GetComponent<LayoutElement>();
            overlayLayout.ignoreLayout = true;
            var overlayRect = overlayGo.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            var overlayImage = overlayGo.GetComponent<Image>();
            overlayImage.color = new Color(0.02f, 0.03f, 0.06f, 0.84f);
            var overlayButton = overlayGo.GetComponent<Button>();
            overlayButton.targetGraphic = overlayImage;
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(CloseDetailsModal);
            _detailsOverlayRoot = overlayGo;

            var modalGo = new GameObject("DetailsModal", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(Button));
            modalGo.transform.SetParent(overlayGo.transform, false);
            var modalLayoutElement = modalGo.GetComponent<LayoutElement>();
            modalLayoutElement.ignoreLayout = true;
            var modalRect = modalGo.GetComponent<RectTransform>();
            modalRect.anchorMin = new Vector2(0.005f, 0.01f);
            modalRect.anchorMax = new Vector2(0.995f, 0.992f);
            modalRect.offsetMin = Vector2.zero;
            modalRect.offsetMax = Vector2.zero;
            var modalImage = modalGo.GetComponent<Image>();
            modalImage.color = new Color(0.07f, 0.09f, 0.15f, 0.985f);
            var modalButton = modalGo.GetComponent<Button>();
            modalButton.targetGraphic = modalImage;
            modalButton.transition = Selectable.Transition.None;
            var modalLayout = modalGo.GetComponent<VerticalLayoutGroup>();
            modalLayout.childAlignment = TextAnchor.UpperCenter;
            modalLayout.childControlWidth = true;
            modalLayout.childControlHeight = true;
            modalLayout.childForceExpandWidth = true;
            modalLayout.childForceExpandHeight = false;
            modalLayout.spacing = 16f;
            modalLayout.padding = new RectOffset(38, 38, 22, 28);

            var modalFrameArt = new GameObject("ModalFrameArt", typeof(RectTransform), typeof(Image));
            modalFrameArt.transform.SetParent(modalGo.transform, false);
            var modalFrameRect = modalFrameArt.GetComponent<RectTransform>();
            modalFrameRect.anchorMin = Vector2.zero;
            modalFrameRect.anchorMax = Vector2.one;
            modalFrameRect.offsetMin = Vector2.zero;
            modalFrameRect.offsetMax = Vector2.zero;
            var modalFrameImage = modalFrameArt.GetComponent<Image>();
            modalFrameImage.raycastTarget = false;
            modalFrameImage.color = new Color(1f, 1f, 1f, 0.88f);
            ApplyClassicRpgFrameTheme(modalFrameImage, "Assets/ClassicRPGUI2/UIElementsPNG/FrameForSlicing.png", true);

            var controlRow = new GameObject("ControlRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            controlRow.transform.SetParent(modalGo.transform, false);
            var controlLayoutElement = controlRow.GetComponent<LayoutElement>();
            controlLayoutElement.preferredHeight = 60f;
            var controlLayout = controlRow.GetComponent<HorizontalLayoutGroup>();
            controlLayout.childAlignment = TextAnchor.MiddleRight;
            controlLayout.childControlWidth = false;
            controlLayout.childControlHeight = true;
            controlLayout.childForceExpandWidth = false;
            controlLayout.childForceExpandHeight = false;
            controlLayout.spacing = 10f;

            _btnDetailsClose = MakeButton(controlRow.transform, "Btn_DetailsClose", "Close", 60f, new Color(0.20f, 0.28f, 0.40f, 1f));
            var closeLayout = _btnDetailsClose.GetComponent<LayoutElement>();
            if (closeLayout != null)
                closeLayout.preferredWidth = 220f;
            var closeText = _btnDetailsClose.GetComponentInChildren<TMP_Text>();
            if (closeText != null)
            {
                closeText.fontSize = 19f;
                closeText.fontStyle = FontStyles.Bold;
            }
            ApplyClassicRpgButtonTheme(_btnDetailsClose, ClassicRpgButtonSize.Long);
            ApplyClassicRpgLabelTheme(closeText, false, true);
            _btnDetailsClose.onClick.AddListener(CloseDetailsModal);

            var titlePlate = new GameObject("TitlePlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            titlePlate.transform.SetParent(modalGo.transform, false);
            var titlePlateLayoutElement = titlePlate.GetComponent<LayoutElement>();
            titlePlateLayoutElement.preferredHeight = 76f;
            titlePlateLayoutElement.preferredWidth = 560f;
            var titlePlateLayout = titlePlate.GetComponent<HorizontalLayoutGroup>();
            titlePlateLayout.childAlignment = TextAnchor.MiddleCenter;
            titlePlateLayout.childControlWidth = true;
            titlePlateLayout.childControlHeight = true;
            titlePlateLayout.childForceExpandWidth = true;
            titlePlateLayout.childForceExpandHeight = true;
            titlePlateLayout.padding = new RectOffset(18, 18, 10, 12);
            var titlePlateImage = titlePlate.GetComponent<Image>();
            titlePlateImage.color = Color.white;
            ApplyClassicRpgFrameTheme(titlePlateImage, "Assets/ClassicRPGUI2/UIElementsPNG/TitleLong.png");

            _txtDetailsTitle = MakeLabel(titlePlate.transform, "Txt_Title", "", 34, new Color(0.96f, 0.84f, 0.50f, 1f), 52f);
            _txtDetailsTitle.alignment = TextAlignmentOptions.Center;
            _txtDetailsTitle.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(_txtDetailsTitle, 22f, 34f);
            ApplyClassicRpgLabelTheme(_txtDetailsTitle, true, true);

            var portraitRow = new GameObject("PortraitRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            portraitRow.transform.SetParent(modalGo.transform, false);
            var portraitRowLayoutElement = portraitRow.GetComponent<LayoutElement>();
            portraitRowLayoutElement.preferredHeight = 292f;
            var portraitRowLayout = portraitRow.GetComponent<HorizontalLayoutGroup>();
            portraitRowLayout.childAlignment = TextAnchor.MiddleCenter;
            portraitRowLayout.childControlWidth = false;
            portraitRowLayout.childControlHeight = true;
            portraitRowLayout.childForceExpandWidth = false;
            portraitRowLayout.childForceExpandHeight = false;

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(portraitRow.transform, false);
            var portraitFrameImage = portraitFrame.GetComponent<Image>();
            portraitFrameImage.color = Color.white;
            ApplyClassicRpgFrameTheme(portraitFrameImage, "Assets/ClassicRPGUI2/UIElementsPNG/HpBar_PortraitFrame.png", true);
            var portraitLayout = portraitFrame.GetComponent<LayoutElement>();
            portraitLayout.preferredWidth = 212f;
            portraitLayout.preferredHeight = 276f;

            var portraitInnerFrame = new GameObject("PortraitInnerFrame", typeof(RectTransform), typeof(Image));
            portraitInnerFrame.transform.SetParent(portraitFrame.transform, false);
            var innerRect = portraitInnerFrame.GetComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(18f, 22f);
            innerRect.offsetMax = new Vector2(-18f, -22f);
            var portraitInnerImage = portraitInnerFrame.GetComponent<Image>();
            portraitInnerImage.color = new Color(0.07f, 0.10f, 0.16f, 1f);
            ApplyClassicRpgFrameTheme(portraitInnerImage, "Assets/ClassicRPGUI2/UIElementsPNG/HpBar_PortraitFrameBg.png");

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(portraitInnerFrame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(8f, 10f);
            portraitRect.offsetMax = new Vector2(-8f, -10f);
            _detailsPortrait = portraitGo.GetComponent<RawImage>();
            _detailsPortrait.color = new Color(1f, 1f, 1f, 0f);
            _detailsPortrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 0.68f;

            var detailsIconGo = new GameObject("BuildingIcon", typeof(RectTransform), typeof(Image));
            detailsIconGo.transform.SetParent(portraitInnerFrame.transform, false);
            var detailsIconRect = detailsIconGo.GetComponent<RectTransform>();
            detailsIconRect.anchorMin = Vector2.zero;
            detailsIconRect.anchorMax = Vector2.one;
            detailsIconRect.offsetMin = new Vector2(18f, 20f);
            detailsIconRect.offsetMax = new Vector2(-18f, -20f);
            _detailsBuildingIcon = detailsIconGo.GetComponent<Image>();
            _detailsBuildingIcon.preserveAspect = true;
            _detailsBuildingIcon.enabled = false;

            _detailsBuildingFallback = CreateInlineText(
                portraitInnerFrame.transform,
                "Txt_BuildingFallback",
                "",
                44f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var detailsFallbackRect = _detailsBuildingFallback.rectTransform;
            detailsFallbackRect.anchorMin = Vector2.zero;
            detailsFallbackRect.anchorMax = Vector2.one;
            detailsFallbackRect.offsetMin = Vector2.zero;
            detailsFallbackRect.offsetMax = Vector2.zero;
            _detailsBuildingFallback.gameObject.SetActive(false);

            var previewGrid = new GameObject("MotionPreviewGrid", typeof(RectTransform), typeof(LayoutElement), typeof(GridLayoutGroup));
            previewGrid.transform.SetParent(modalGo.transform, false);
            var previewGridLayout = previewGrid.GetComponent<LayoutElement>();
            previewGridLayout.preferredHeight = 220f;
            var previewGridGroup = previewGrid.GetComponent<GridLayoutGroup>();
            previewGridGroup.cellSize = new Vector2(170f, 44f);
            previewGridGroup.spacing = new Vector2(10f, 10f);
            previewGridGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            previewGridGroup.constraintCount = 2;
            previewGridGroup.childAlignment = TextAnchor.MiddleCenter;
            previewGridGroup.startAxis = GridLayoutGroup.Axis.Horizontal;
            previewGridGroup.startCorner = GridLayoutGroup.Corner.UpperLeft;

            _btnPreviewIdle = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewIdle", "Idle", PreviewIdleMotion, out _txtPreviewIdle);
            _btnPreviewWalk = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewWalk", "Walk", PreviewWalkMotion, out _txtPreviewWalk);
            _btnPreviewMarch = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewMarch", "March", PreviewMarchMotion, out _txtPreviewMarch);
            _btnPreviewRun = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewRun", "Run", PreviewRunMotion, out _txtPreviewRun);
            _btnPreviewStrike = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewStrike", "Strike", PreviewStrikeMotion, out _txtPreviewStrike);
            _btnPreviewSpecial = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewSpecial", "Special Move", PreviewSpecialMotion, out _txtPreviewSpecial);
            _btnPreviewHit = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewHit", "Hit React", PreviewHitMotion, out _txtPreviewHit);
            _btnPreviewDeath = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewDeath", "Death", PreviewDeathMotion, out _txtPreviewDeath);

            _txtDetailsPreviewStatus = MakeLabel(modalGo.transform, "Txt_PreviewStatus", "", 15, new Color(0.84f, 0.88f, 0.95f), 28f);
            _txtDetailsPreviewStatus.alignment = TextAlignmentOptions.Center;
            SetResponsiveWrappedText(_txtDetailsPreviewStatus, 12f, 15f);
            ApplyClassicRpgLabelTheme(_txtDetailsPreviewStatus, false, true);

            var scrollGo = new GameObject("DetailsScroll", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            scrollGo.transform.SetParent(modalGo.transform, false);
            var scrollLayoutElement = scrollGo.GetComponent<LayoutElement>();
            scrollLayoutElement.flexibleHeight = 1f;
            scrollLayoutElement.minHeight = 220f;
            scrollGo.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.14f, 0.92f);
            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 24f;
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

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(18f, 0f);
            contentRect.offsetMax = new Vector2(-18f, 0f);
            var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = false;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 12f;
            contentLayout.padding = new RectOffset(0, 0, 20, 20);
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            var contentColumn = new GameObject("ContentColumn", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentColumn.transform.SetParent(contentGo.transform, false);
            var contentColumnLayout = contentColumn.GetComponent<LayoutElement>();
            contentColumnLayout.preferredWidth = 760f;
            var contentColumnGroup = contentColumn.GetComponent<VerticalLayoutGroup>();
            contentColumnGroup.childAlignment = TextAnchor.UpperCenter;
            contentColumnGroup.childControlWidth = true;
            contentColumnGroup.childControlHeight = true;
            contentColumnGroup.childForceExpandWidth = true;
            contentColumnGroup.childForceExpandHeight = false;
            contentColumnGroup.spacing = 12f;
            contentColumnGroup.padding = new RectOffset(0, 0, 4, 8);
            contentColumn.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentColumn.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var stateCard = CreateDetailsModalCard(contentColumn.transform, "StateCard", new Color(0.09f, 0.12f, 0.19f, 0.96f), 104f);
            var stateHeader = MakeLabel(stateCard.transform, "Txt_StateHeader", "Battle Standing", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            stateHeader.alignment = TextAlignmentOptions.Center;
            stateHeader.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(stateHeader, true, true);
            _txtDetailsState = MakeLabel(stateCard.transform, "Txt_State", "", 19, selectedColor, 56f);
            _txtDetailsState.alignment = TextAlignmentOptions.Center;
            _txtDetailsState.fontStyle = FontStyles.Bold;
            SetResponsiveWrappedText(_txtDetailsState, 13f, 19f);
            ApplyClassicRpgLabelTheme(_txtDetailsState, false, true);

            var statsCard = CreateDetailsModalCard(contentColumn.transform, "StatsCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 220f);
            var statsHeader = MakeLabel(statsCard.transform, "Txt_StatsHeader", "War Ledger", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            statsHeader.alignment = TextAlignmentOptions.Center;
            statsHeader.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(statsHeader, true, true);
            _txtDetailsStats = MakeLabel(statsCard.transform, "Txt_Stats", "", 16, new Color(0.88f, 0.90f, 0.96f), 182f);
            _txtDetailsStats.alignment = TextAlignmentOptions.TopLeft;
            SetResponsiveWrappedText(_txtDetailsStats, 13f, 16f);
            ApplyClassicRpgLabelTheme(_txtDetailsStats);

            var requirementCard = CreateDetailsModalCard(contentColumn.transform, "RequirementCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 132f);
            var requirementHeader = MakeLabel(requirementCard.transform, "Txt_RequirementHeader", "Unlock Decree", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            requirementHeader.alignment = TextAlignmentOptions.Center;
            requirementHeader.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(requirementHeader, true, true);
            _txtDetailsRequirement = MakeLabel(requirementCard.transform, "Txt_Requirement", "", 15, new Color(0.82f, 0.86f, 0.92f), 102f);
            _txtDetailsRequirement.alignment = TextAlignmentOptions.TopLeft;
            SetResponsiveWrappedText(_txtDetailsRequirement, 13f, 15f);
            ApplyClassicRpgLabelTheme(_txtDetailsRequirement);

            var movesCard = CreateDetailsModalCard(contentColumn.transform, "MovesCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 196f);
            var movesHeader = MakeLabel(movesCard.transform, "Txt_MovesHeader", "Move Scroll", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            movesHeader.alignment = TextAlignmentOptions.Center;
            movesHeader.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(movesHeader, true, true);
            _txtDetailsMoves = MakeLabel(movesCard.transform, "Txt_Moves", "", 15, new Color(0.84f, 0.88f, 0.95f), 160f);
            _txtDetailsMoves.alignment = TextAlignmentOptions.TopLeft;
            SetResponsiveWrappedText(_txtDetailsMoves, 13f, 15f);
            ApplyClassicRpgLabelTheme(_txtDetailsMoves);

            var soundCard = CreateDetailsModalCard(contentColumn.transform, "SoundCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 164f);
            var soundHeader = MakeLabel(soundCard.transform, "Txt_SoundHeader", "Sound Hall", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            soundHeader.alignment = TextAlignmentOptions.Center;
            soundHeader.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(soundHeader, true, true);

            var soundRow = new GameObject("SoundPreviewRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            soundRow.transform.SetParent(soundCard.transform, false);
            soundRow.GetComponent<LayoutElement>().preferredHeight = 46f;
            var soundRowLayout = soundRow.GetComponent<HorizontalLayoutGroup>();
            soundRowLayout.childAlignment = TextAnchor.MiddleCenter;
            soundRowLayout.childControlWidth = false;
            soundRowLayout.childControlHeight = true;
            soundRowLayout.childForceExpandWidth = false;
            soundRowLayout.childForceExpandHeight = false;
            soundRowLayout.spacing = 12f;

            _btnDetailsPreviewSfx = MakeButton(soundRow.transform, "Btn_PreviewSfx", "Preview SFX", 44f, new Color(0.24f, 0.33f, 0.49f, 1f));
            var sfxLayout = _btnDetailsPreviewSfx.GetComponent<LayoutElement>();
            if (sfxLayout != null)
                sfxLayout.preferredWidth = 184f;
            _txtDetailsPreviewSfx = _btnDetailsPreviewSfx.GetComponentInChildren<TMP_Text>();
            if (_txtDetailsPreviewSfx != null)
                _txtDetailsPreviewSfx.fontSize = 14f;
            ApplyClassicRpgButtonTheme(_btnDetailsPreviewSfx, ClassicRpgButtonSize.Medium);
            ApplyClassicRpgLabelTheme(_txtDetailsPreviewSfx, false, true);
            _btnDetailsPreviewSfx.onClick.AddListener(PreviewSelectedUnitSfx);

            _btnDetailsPreviewVoice = MakeButton(soundRow.transform, "Btn_PreviewVoice", "Voice Lines", 44f, new Color(0.19f, 0.24f, 0.34f, 1f));
            var voiceLayout = _btnDetailsPreviewVoice.GetComponent<LayoutElement>();
            if (voiceLayout != null)
                voiceLayout.preferredWidth = 184f;
            _txtDetailsPreviewVoice = _btnDetailsPreviewVoice.GetComponentInChildren<TMP_Text>();
            if (_txtDetailsPreviewVoice != null)
                _txtDetailsPreviewVoice.fontSize = 14f;
            ApplyClassicRpgButtonTheme(_btnDetailsPreviewVoice, ClassicRpgButtonSize.Medium);
            ApplyClassicRpgLabelTheme(_txtDetailsPreviewVoice, false, true);
            _btnDetailsPreviewVoice.onClick.AddListener(PreviewSelectedUnitVoice);

            _txtDetailsAudioStatus = MakeLabel(soundCard.transform, "Txt_AudioStatus", "", 14, new Color(0.80f, 0.84f, 0.92f), 72f);
            _txtDetailsAudioStatus.alignment = TextAlignmentOptions.Center;
            SetResponsiveWrappedText(_txtDetailsAudioStatus, 12f, 14f);
            ApplyClassicRpgLabelTheme(_txtDetailsAudioStatus, false, true);

            var bodyCard = CreateDetailsModalCard(contentColumn.transform, "BodyCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 276f);
            var bodyHeader = MakeLabel(bodyCard.transform, "Txt_BodyHeader", "Chronicle", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            bodyHeader.alignment = TextAlignmentOptions.Center;
            bodyHeader.fontStyle = FontStyles.Bold;
            ApplyClassicRpgLabelTheme(bodyHeader, true, true);
            _txtDetailsBody = MakeLabel(bodyCard.transform, "Txt_Body", "", 15, new Color(0.78f, 0.82f, 0.90f), 240f);
            _txtDetailsBody.alignment = TextAlignmentOptions.TopLeft;
            SetResponsiveWrappedText(_txtDetailsBody, 13f, 15f);
            ApplyClassicRpgLabelTheme(_txtDetailsBody);
        }

        GameObject CreateDetailsModalCard(Transform parent, string name, Color color, float preferredHeight)
        {
            var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            card.transform.SetParent(parent, false);
            var image = card.GetComponent<Image>();
            image.color = color;
            ApplyClassicRpgFrameTheme(image, "Assets/ClassicRPGUI2/UIElementsPNG/PaperMedium.png");
            var layoutElement = card.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = preferredHeight;
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;
            layout.padding = new RectOffset(18, 18, 16, 16);
            return card;
        }

        Button CreateMotionPreviewButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick, out TMP_Text text)
        {
            var button = MakeButton(parent, name, label, 44f, new Color(0.19f, 0.26f, 0.38f, 1f));
            ApplyClassicRpgButtonTheme(button, ClassicRpgButtonSize.Medium);
            text = button.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.fontSize = 14f;
                ApplyClassicRpgLabelTheme(text, false, true);
            }
            button.onClick.AddListener(onClick);
            return button;
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
            ApplyClassicRpgButtonTheme(_btnSecondaryAction, ClassicRpgButtonSize.Medium);
            ApplyClassicRpgLabelTheme(_txtSecondaryAction, false, true);
            _btnSecondaryAction.onClick.AddListener(HandleSecondaryAction);

            _btnPrimaryAction = MakeButton(row.transform, "Btn_Primary", "Continue", 46f, new Color(0.20f, 0.58f, 0.30f, 1f));
            _txtPrimaryAction = _btnPrimaryAction.GetComponentInChildren<TMP_Text>();
            ApplyClassicRpgButtonTheme(_btnPrimaryAction, ClassicRpgButtonSize.Long);
            ApplyClassicRpgLabelTheme(_txtPrimaryAction, false, true);
            _btnPrimaryAction.onClick.AddListener(HandlePrimaryAction);
            RefreshPrimaryAction();
        }

        void OnRaceSelected(string raceId)
        {
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, raceId, "race button");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "race button");
            _selectedUnit = GetDefaultUnit(_selectedRace);
            _detailsModalOpen = false;
            NavigateToPage(WizardPage.ProgressionTree);
        }

        void OnUnitSelected(string unitId)
        {
            if (_selectedRace == null || !_selectedRace.TryGetUnit(unitId, out var unit))
                return;

            _selectedUnit = unit;
            _detailsModalOpen = true;
            _activePage = WizardPage.ProgressionTree;
            RefreshCopy();
            RefreshVisuals();
        }

        void CloseDetailsModal()
        {
            if (!_detailsModalOpen)
                return;

            _detailsModalOpen = false;
            RefreshCopy();
            RefreshVisuals();
        }

        void OnTreeTabSelected(RaceProgressionTab tab)
        {
            if (_selectedTreeTab == tab)
                return;

            _selectedTreeTab = tab;
            _detailsModalOpen = false;
            RebuildPanel();
        }

        void NavigateToPage(WizardPage page)
        {
            if (page == WizardPage.UnitDetails && _selectedUnit == null)
                _selectedUnit = GetDefaultUnit(_selectedRace);

            if (page != WizardPage.UnitDetails)
                _detailsModalOpen = false;

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
                    CloseDetailsModal();
                    break;
                case WizardPage.ProgressionTree:
                    if (_detailsModalOpen)
                        CloseDetailsModal();
                    else
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
                WizardPage.RaceSelection => "Step 1 of 2. Choose a race to begin.",
                WizardPage.ProgressionTree => _detailsModalOpen
                    ? "Step 2 of 2. Review the selected unit in the readable popup."
                    : "Step 2 of 2. Review the upgrade chain and tap any card for readable details.",
                WizardPage.UnitDetails => "Step 2 of 2. Review the selected unit in the readable popup.",
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
                WizardPage.ProgressionTree => _detailsModalOpen && _selectedUnit != null
                    ? $"{_selectedUnit.DisplayName} is open. Tap outside the panel or press Close to return to the tree."
                    : _selectedRace != null
                        ? $"{_selectedRace.DisplayName} selected. Tap any card to open a larger readable detail popup."
                        : "Select a race to continue.",
                WizardPage.UnitDetails => _selectedUnit != null
                    ? $"{_selectedUnit.DisplayName} is open. Tap outside the panel or press Close to return to the tree."
                    : "Select a race to continue.",
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
            bool showModal = _detailsModalOpen
                && _selectedUnit != null
                && (_activePage == WizardPage.ProgressionTree || _activePage == WizardPage.UnitDetails);
            if (_detailsOverlayRoot != null)
                _detailsOverlayRoot.SetActive(showModal);
            if (!showModal)
            {
                ClearDetailsLivePreview();
                return;
            }

            if (_selectedUnit == null)
                _selectedUnit = GetDefaultUnit(_selectedRace);
            if (_selectedUnit == null)
                return;

            if (_txtDetailsTitle != null)
                _txtDetailsTitle.text = _selectedUnit.DisplayName;
            if (_txtDetailsStats != null)
                _txtDetailsStats.text = BuildUnitDetailStatsText(_selectedUnit);
            if (_txtDetailsState != null)
                _txtDetailsState.text = BuildUnitDetailsStateText(_selectedUnit);
            if (_txtDetailsRequirement != null)
                _txtDetailsRequirement.text = BuildUnitDetailsRequirementText(_selectedUnit);
            if (_txtDetailsMoves != null)
                _txtDetailsMoves.text = BuildUnitMoveSetText(_selectedUnit);
            if (_txtDetailsAudioStatus != null)
                _txtDetailsAudioStatus.text = BuildUnitAudioStatusText(_selectedUnit);
            if (_txtDetailsBody != null)
                _txtDetailsBody.text = BuildUnitDetailsBodyText(_selectedUnit);

            RefreshDetailsPreviewButtons(_selectedUnit);
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
            string requirementText;
            if (unit == null || unit.IsStartUnit)
            {
                if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
                    return "[Gate] Starting civic tier";
                if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
                    return "[Gate] Available immediately";
                requirementText = "[Gate] Start unit";
            }
            else
            {
                var requirement = unit.UnlockRequirement;
                if (requirement == null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
                {
                    if (_selectedRace != null
                        && _selectedRace.TryGetLane(unit.LaneId, out var lane)
                        && GetUnitIndex(lane, unit.Id) == 0)
                    {
                        return "[Gate] Available immediately";
                    }

                    return "[Gate] Previous tier in this row";
                }

                requirementText = requirement == null
                    ? "[Gate] Unknown"
                    : $"[Gate] {requirement.BuildingName} T{requirement.RequiredTier}";
            }

            if (unit == null
                || unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                || unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep
                || unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
            {
                return requirementText;
            }

            var builder = new StringBuilder();
            builder.AppendLine(requirementText);
            builder.AppendLine($"[Rank] {BuildFormationDetailText(unit)}");
            builder.Append($"[Source] {BuildCurrentSourceText(TryGetCatalogEntry(unit, out var catalog) ? catalog : null)}");
            return builder.ToString().TrimEnd();
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

        void PreviewSelectedUnitSfx()
        {
            if (!TryResolvePreviewSfx(_selectedUnit, out var sfx, out var label))
            {
                SetDetailsPreviewStatus("No dedicated sound preview is wired for this entry yet.");
                return;
            }

            AudioManager.I?.Play(sfx, 0.9f);
            SetDetailsPreviewStatus($"{_selectedUnit?.DisplayName ?? "This entry"} is previewing {label.ToLowerInvariant()} audio.");
        }

        void PreviewSelectedUnitVoice()
        {
            if (!TryResolvePreviewVoice(_selectedUnit, out var sfx, out var label))
            {
                SetDetailsPreviewStatus("Voice lines are still pending for this unit.");
                return;
            }

            AudioManager.I?.Play(sfx, 0.9f);
            SetDetailsPreviewStatus($"{_selectedUnit?.DisplayName ?? "This entry"} is previewing {label.ToLowerInvariant()} voice.");
        }

        void PreviewIdleMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Idle);
        void PreviewWalkMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Walk);
        void PreviewMarchMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.March);
        void PreviewRunMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Run);
        void PreviewStrikeMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Strike);
        void PreviewSpecialMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Special);
        void PreviewHitMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Hit);
        void PreviewDeathMotion() => PlayDetailsPreviewMotion(DetailPreviewMotion.Death);

        void RefreshDetailsPreviewButtons(RaceProgressionUnitDefinition unit)
        {
            bool hasSfx = TryResolvePreviewSfx(unit, out _, out var sfxLabel);
            bool hasVoice = TryResolvePreviewVoice(unit, out _, out var voiceLabel);

            SetPreviewButtonState(
                _btnDetailsPreviewSfx,
                _txtDetailsPreviewSfx,
                hasSfx,
                hasSfx ? $"Play {sfxLabel}" : "No SFX Wired",
                new Color(0.24f, 0.33f, 0.49f, 1f),
                new Color(0.15f, 0.18f, 0.24f, 0.92f));

            SetPreviewButtonState(
                _btnDetailsPreviewVoice,
                _txtDetailsPreviewVoice,
                hasVoice,
                hasVoice ? $"Play {voiceLabel}" : "Voices Pending",
                new Color(0.30f, 0.24f, 0.41f, 1f),
                new Color(0.15f, 0.18f, 0.24f, 0.92f));

            bool hasLivePreview = TryEnsureDetailsPreviewUnit(unit);
            SetDetailsPreviewStatus(
                hasLivePreview
                    ? $"Preview ready. Choose a motion to watch how {unit?.DisplayName ?? "this unit"} moves in battle."
                    : $"Live motion preview is not wired for {unit?.DisplayName ?? "this entry"} yet.");
            RefreshMotionPreviewButton(_btnPreviewIdle, _txtPreviewIdle, "Idle", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Idle));
            RefreshMotionPreviewButton(_btnPreviewWalk, _txtPreviewWalk, "Walk", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Walk));
            RefreshMotionPreviewButton(_btnPreviewMarch, _txtPreviewMarch, "March", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.March));
            RefreshMotionPreviewButton(_btnPreviewRun, _txtPreviewRun, "Run", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Run));
            RefreshMotionPreviewButton(_btnPreviewStrike, _txtPreviewStrike, "Strike", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Strike));
            RefreshMotionPreviewButton(
                _btnPreviewSpecial,
                _txtPreviewSpecial,
                BuildSpecialPreviewButtonLabel(unit),
                hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Special));
            RefreshMotionPreviewButton(_btnPreviewHit, _txtPreviewHit, "Hit React", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Hit));
            RefreshMotionPreviewButton(_btnPreviewDeath, _txtPreviewDeath, "Death", hasLivePreview && CanPlayDetailsPreviewMotion(DetailPreviewMotion.Death));
        }

        static void SetPreviewButtonState(Button button, TMP_Text label, bool enabled, string text, Color enabledColor, Color disabledColor)
        {
            if (button == null)
                return;

            button.interactable = enabled;
            if (button.targetGraphic is Image image)
            {
                bool usingSpriteSkin = image.sprite != null;
                image.color = usingSpriteSkin
                    ? enabled
                        ? Color.white
                        : new Color(0.52f, 0.52f, 0.52f, 0.92f)
                    : enabled
                        ? enabledColor
                        : disabledColor;
            }

            if (label != null)
            {
                label.text = text;
                label.color = enabled ? Color.white : new Color(0.70f, 0.74f, 0.80f, 1f);
            }
        }

        void RefreshMotionPreviewButton(Button button, TMP_Text label, string text, bool enabled)
        {
            SetPreviewButtonState(
                button,
                label,
                enabled,
                text,
                new Color(0.19f, 0.26f, 0.38f, 1f),
                new Color(0.15f, 0.18f, 0.24f, 0.92f));
        }

        void PlayDetailsPreviewMotion(DetailPreviewMotion motion)
        {
            if (!TryEnsureDetailsPreviewUnit(_selectedUnit))
            {
                SetDetailsPreviewStatus("This entry does not have a live rig preview yet.");
                return;
            }

            if (_detailsPreviewCam == null)
                return;

            _detailsPreviewCam.SetAnimatorSpeed(GetPreviewMotionSpeed(motion));
            if (!_detailsPreviewCam.TryPlayFirstAvailableState(GetPreviewMotionStates(motion), out var playedState, out var clipLength, 0f))
            {
                SetDetailsPreviewStatus($"No {BuildPreviewMotionLabel(motion, _selectedUnit).ToLowerInvariant()} animation is wired for {_selectedUnit?.DisplayName ?? "this unit"} yet.");
                return;
            }

            StopDetailsPreviewResetRoutine();
            SetDetailsPreviewStatus(BuildPreviewStatusText(motion, _selectedUnit, playedState));

            if (IsTransientPreviewMotion(motion))
            {
                float resetDelay = ResolveTransientPreviewDelay(motion, clipLength);
                _detailsPreviewResetRoutine = StartCoroutine(ReturnDetailsPreviewToIdle(resetDelay));
            }
        }

        bool CanPlayDetailsPreviewMotion(DetailPreviewMotion motion)
        {
            return _detailsPreviewCam != null
                && _detailsPreviewCam.HasAnyState(GetPreviewMotionStates(motion));
        }

        void RefreshDetailsPortrait(RaceProgressionUnitDefinition unit)
        {
            bool showBuildingIcon = unit != null
                && (unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep
                    || unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                    || !string.IsNullOrWhiteSpace(unit.ImageResourcePath));

            if (showBuildingIcon)
            {
                ClearDetailsLivePreview();
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

            if (TryEnsureDetailsPreviewUnit(unit) && _detailsPortrait != null && _runtimePreviewTexture != null)
            {
                _detailsPortrait.texture = _runtimePreviewTexture;
                _detailsPortrait.color = Color.white;
                return;
            }

            if (_detailsPortrait != null)
                StartPortraitCapture(unit?.PortraitKey, _detailsPortrait);
        }

        bool TryEnsureDetailsPreviewUnit(RaceProgressionUnitDefinition unit)
        {
            if (unit == null || unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep || unit.CardDisplay != null)
                return false;

            string previewKey = ResolveDetailsPreviewKey(unit);
            if (string.IsNullOrWhiteSpace(previewKey))
                return false;

            var previewCam = EnsureDetailsPreviewCamera();
            if (previewCam == null)
                return false;

            if (!string.Equals(_detailsPreviewStagedKey, previewKey, StringComparison.OrdinalIgnoreCase) || previewCam.StagedObject == null)
            {
                StopDetailsPreviewResetRoutine();
                previewCam.ShowUnit(previewKey);
                previewCam.SetAnimatorSpeed(1f);
                previewCam.PlayFirstAvailableState(GetPreviewMotionStates(DetailPreviewMotion.Idle), 0.05f);
                _detailsPreviewStagedKey = previewKey;
            }

            return previewCam.StagedObject != null;
        }

        void ClearDetailsLivePreview()
        {
            StopDetailsPreviewResetRoutine();
            _detailsPreviewStagedKey = null;
            if (_detailsPreviewCam != null)
                _detailsPreviewCam.Clear();
        }

        string ResolveDetailsPreviewKey(RaceProgressionUnitDefinition unit)
        {
            if (!string.IsNullOrWhiteSpace(unit?.PortraitKey))
                return ResolvePortraitLookupKey(unit.PortraitKey);

            return ResolveTechTreeCatalogKey(unit);
        }

        UnitPortraitCamera EnsureDetailsPreviewCamera()
        {
            if (_detailsPreviewCam != null && _detailsPreviewCam.Registry != null)
                return _detailsPreviewCam;

            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry == null)
                return null;

            if (_runtimePreviewRoot == null)
                _detailsPreviewCam = RuntimePortraitStudio.Create("RaceProgressionDetailsPreviewStudio", registry, out _runtimePreviewRoot, out _runtimePreviewTexture, textureSize: 512);

            _detailsPreviewCam.Registry = registry;
            _detailsPreviewCam.transform.position = new Vector3(0f, 0f, 80f);
            _detailsPreviewCam.FitHeight = 2.2f;
            _detailsPreviewCam.FrameFill = 0.78f;
            _detailsPreviewCam.VerticalFocus = 0.54f;
            _detailsPreviewCam.CameraHeightBias = -0.02f;
            _detailsPreviewCam.LookAtHeightBias = 0.02f;
            return _detailsPreviewCam;
        }

        IEnumerator ReturnDetailsPreviewToIdle(float delay)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.35f, delay));

            if (_detailsPreviewCam == null || _selectedUnit == null)
                yield break;

            _detailsPreviewCam.SetAnimatorSpeed(1f);
            _detailsPreviewCam.PlayFirstAvailableState(GetPreviewMotionStates(DetailPreviewMotion.Idle), 0.08f);
            SetDetailsPreviewStatus($"Preview reset to idle for {_selectedUnit.DisplayName}.");
            _detailsPreviewResetRoutine = null;
        }

        void StopDetailsPreviewResetRoutine()
        {
            if (_detailsPreviewResetRoutine == null)
                return;

            StopCoroutine(_detailsPreviewResetRoutine);
            _detailsPreviewResetRoutine = null;
        }

        static float GetPreviewMotionSpeed(DetailPreviewMotion motion)
        {
            return motion switch
            {
                DetailPreviewMotion.March => 0.72f,
                _ => 1f,
            };
        }

        static string[] GetPreviewMotionStates(DetailPreviewMotion motion)
        {
            return motion switch
            {
                DetailPreviewMotion.Idle => new[] { "Idle", "IdleNormal", "IdleCombat", "idle" },
                DetailPreviewMotion.Walk => new[] { "Walk", "walk" },
                DetailPreviewMotion.March => new[] { "Walk", "walk" },
                DetailPreviewMotion.Run => new[] { "Run", "run", "Walk", "walk" },
                DetailPreviewMotion.Strike => new[] { "Attack1", "Attack", "attack" },
                DetailPreviewMotion.Special => new[] { "Attack2", "Attack1", "Attack", "attack" },
                DetailPreviewMotion.Hit => new[] { "Damage", "Hit", "damage", "hit" },
                DetailPreviewMotion.Death => new[] { "Death", "death", "die" },
                _ => Array.Empty<string>(),
            };
        }

        void SetDetailsPreviewStatus(string text)
        {
            if (_txtDetailsPreviewStatus == null)
                return;

            _txtDetailsPreviewStatus.text = text;
        }

        static string BuildPreviewStatusText(DetailPreviewMotion motion, RaceProgressionUnitDefinition unit, string stateName)
        {
            string unitName = unit?.DisplayName ?? "This unit";
            string moveName = BuildPreviewMotionLabel(motion, unit);
            return $"{unitName} is now previewing {moveName.ToLowerInvariant()} ({stateName}).";
        }

        static string BuildPreviewMotionLabel(DetailPreviewMotion motion, RaceProgressionUnitDefinition unit)
        {
            return motion switch
            {
                DetailPreviewMotion.Idle => "Idle",
                DetailPreviewMotion.Walk => "Walk",
                DetailPreviewMotion.March => "March",
                DetailPreviewMotion.Run => "Run",
                DetailPreviewMotion.Strike => "Strike",
                DetailPreviewMotion.Special => BuildSpecialPreviewButtonLabel(unit),
                DetailPreviewMotion.Hit => "Hit React",
                DetailPreviewMotion.Death => "Death",
                _ => "Motion",
            };
        }

        static bool IsTransientPreviewMotion(DetailPreviewMotion motion)
        {
            return motion == DetailPreviewMotion.Strike
                || motion == DetailPreviewMotion.Special
                || motion == DetailPreviewMotion.Hit
                || motion == DetailPreviewMotion.Death;
        }

        static float ResolveTransientPreviewDelay(DetailPreviewMotion motion, float clipLength)
        {
            float fallback = motion switch
            {
                DetailPreviewMotion.Death => 2.2f,
                DetailPreviewMotion.Hit => 1.2f,
                _ => 1.45f,
            };

            return clipLength > 0.01f ? clipLength + 0.1f : fallback;
        }

        static string BuildSpecialPreviewButtonLabel(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "king" => "Heroic Strike",
                "paladin" => "Holy Vanguard",
                "bishop" => "Blessing Rite",
                "archer" => "Volley Fire",
                "crossbowman" => "Piercing Bolt",
                "ranger" => "Skirmish Volley",
                "mage" => "Arcane Burst",
                "wizard" => "Spell Volley",
                "thaumaturge" => "Arcane Control",
                "cleric" => "Field Mend",
                "priest" => "Battle Prayer",
                "high_priest" => "Sanctified Blessing",
                "shieldman" => "Shield Wall",
                "shield_guard" => "Heavy Brace",
                "guardian" => "Bulwark Push",
                "spearman" => "Brace Reach",
                "halberdier" => "Heavy Cleave",
                "lancer" => "Lance Charge",
                _ => "Special Move",
            };
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

        string BuildUnitCardStatsText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Stats unavailable";

            if (unit.CardDisplay != null)
                return $"{BuildBuildingTierLabel(unit)}   {BuildBuildingTimeText(unit)}   {BuildBuildingCostText(unit)}";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return !string.IsNullOrWhiteSpace(unit.StatsSummary) ? unit.StatsSummary : "Building requirement";

            if (IsStableDisplayOnlyUnit(unit))
            {
                return "Mount Unlock";
            }

            if (IsEconomyUnit(unit))
                return BuildEconomyLapText(unit);

            return BuildUnitRoleLabel(unit);
        }

        string BuildUnitCardSubtitle(RaceProgressionLaneDefinition lane, RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return lane?.Label ?? "Unit";

            if (IsStableDisplayOnlyUnit(unit))
                return !string.IsNullOrWhiteSpace(unit.StatsSummary) ? unit.StatsSummary : "Future stable branch";

            string formationText = BuildCompactFormationTag(unit);
            if (!string.IsNullOrWhiteSpace(formationText))
                return formationText;

            if (IsEconomyUnit(unit))
                return "Trade Route Unit";

            return !string.IsNullOrWhiteSpace(unit.CardTag)
                ? unit.CardTag
                : lane?.Label ?? "Unit";
        }

        string BuildUnitDetailStatsText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Stats unavailable";

            if (unit.CardDisplay != null)
                return $"{BuildBuildingTierLabel(unit)}   {BuildBuildingTimeText(unit)}   {BuildBuildingCostText(unit)}";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return !string.IsNullOrWhiteSpace(unit.StatsSummary) ? unit.StatsSummary : "Building requirement";

            if (IsStableDisplayOnlyUnit(unit))
            {
                return !string.IsNullOrWhiteSpace(unit.StatsSummary)
                    ? $"[Stable] {unit.StatsSummary}\n[Status] Runtime output not wired yet."
                    : "[Status] Runtime output not wired yet.";
            }

            if (!TryGetCatalogEntry(unit, out var catalog))
                return !string.IsNullOrWhiteSpace(unit.StatsSummary) ? unit.StatsSummary : "Catalog data unavailable";

            var builder = new StringBuilder();
            if (IsEconomyUnit(unit))
                builder.AppendLine($"[Coin] Route Value {BuildEconomyLapText(unit)}");

            builder.AppendLine($"[Role] {BuildUnitRoleLabel(unit)}");
            builder.AppendLine($"[Rank] {BuildFormationDetailText(unit)}");
            builder.AppendLine($"[Blade] Attack {FormatStatNumber(catalog.attack_damage)}");
            builder.AppendLine($"[Tempo] Attack Speed {Mathf.Max(0.01f, catalog.attack_speed):0.##}/s");
            builder.AppendLine($"[Strike] Damage per second {ComputeUnitDps(catalog):0.#}");
            builder.AppendLine($"[Heart] Vitality {FormatStatNumber(catalog.hp)}");
            builder.AppendLine($"[Shield] Armor {HumanizeLabel(catalog.armor_type)}   Guard {Mathf.Max(0f, catalog.damage_reduction_pct):0.#}%");
            builder.AppendLine($"[Reach] Range {FormatStatNumber(catalog.range)}   [Type] {HumanizeLabel(catalog.damage_type)}");
            builder.Append($"[Stride] Move Speed {FormatStatNumber(catalog.path_speed)}");
            if (catalog.send_cost > 0)
                builder.Append($"   [Send] {catalog.send_cost}g");
            return builder.ToString();
        }

        bool TryGetCatalogEntry(RaceProgressionUnitDefinition unit, out UnitCatalogEntry catalog)
        {
            catalog = null;
            string catalogKey = ResolveTechTreeCatalogKey(unit);
            if (string.IsNullOrWhiteSpace(catalogKey))
                return false;

            if (CatalogLoader.UnitByKey.TryGetValue(catalogKey, out catalog) && catalog != null)
                return true;

            if (_missingCatalogLogs.Add(catalogKey))
                Debug.LogError($"[RaceProgression] Missing catalog entry for '{catalogKey}'. The unit card will render an explicit catalog error state.");

            return false;
        }

        static string ResolveTechTreeCatalogKey(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return null;

            if (!string.IsNullOrWhiteSpace(unit.CatalogKey))
                return unit.CatalogKey.Trim();

            if ((IsEconomyUnit(unit) || IsStableDisplayOnlyUnit(unit)) && !string.IsNullOrWhiteSpace(unit.PortraitKey))
                return unit.PortraitKey.Trim();

            return null;
        }

        static bool IsEconomyUnit(RaceProgressionUnitDefinition unit)
        {
            return string.Equals(unit?.LaneId, "market", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsStableDisplayOnlyUnit(RaceProgressionUnitDefinition unit)
        {
            return string.Equals(unit?.LaneId, "stable_horses", StringComparison.OrdinalIgnoreCase);
        }

        static string BuildEconomyLapText(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "settler" => "+7g / lap",
                "trader" => "+10g / lap",
                _ => "+4g / lap",
            };
        }

        static string BuildCompactFormationTag(RaceProgressionUnitDefinition unit)
        {
            int rank = GetFormationRank(unit);
            return rank > 0
                ? $"{Ordinal(rank)} Rank"
                : IsEconomyUnit(unit)
                    ? "Trade Route"
                    : IsStableDisplayOnlyUnit(unit)
                        ? "Stable Branch"
                        : null;
        }

        static string BuildFormationDetailText(RaceProgressionUnitDefinition unit)
        {
            int rank = GetFormationRank(unit);
            if (rank > 0)
                return $"Formation {Ordinal(rank)} rank";

            if (IsEconomyUnit(unit))
                return "Formation trade route / not in the main battle line";

            if (IsStableDisplayOnlyUnit(unit))
                return "Formation not assigned because this branch is display-only";

            return "Formation not assigned";
        }

        static int GetFormationRank(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "shieldman" => 1,
                "shield_guard" => 1,
                "guardian" => 1,
                "paladin" => 1,
                "militia" => 2,
                "swordsman" => 2,
                "knight" => 2,
                "king" => 2,
                "spearman" => 3,
                "halberdier" => 3,
                "lancer" => 3,
                "mage" => 4,
                "wizard" => 4,
                "thaumaturge" => 4,
                "archer" => 5,
                "crossbowman" => 5,
                "ranger" => 5,
                "cleric" => 6,
                "priest" => 6,
                "high_priest" => 6,
                "bishop" => 6,
                _ => 0,
            };
        }

        static string BuildCompactSpecialTag(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "militia" => "Cheap frontline",
                "swordsman" => "Line fighter",
                "knight" => "Charge break",
                "spearman" => "Brace / reach",
                "halberdier" => "Heavy cleave",
                "lancer" => "Lance charge",
                "shieldman" => "Shield wall",
                "shield_guard" => "Heavy brace",
                "guardian" => "Bulwark push",
                "cleric" => "Field mend",
                "priest" => "Battle prayer",
                "high_priest" => "Mass sustain",
                "mage" => "Arcane burst",
                "wizard" => "Spell volley",
                "thaumaturge" => "Arcane control",
                "archer" => "Volley fire",
                "crossbowman" => "Piercing bolt",
                "ranger" => "Skirmish shots",
                "peasant" => "+4g lap",
                "settler" => "+7g lap",
                "trader" => "+10g lap",
                "king" => "Royal command",
                "paladin" => "Holy vanguard",
                "bishop" => "Blessing support",
                "colt" => "Future mount",
                "stallion" => "Future warhorse",
                "dark_stallion" => "Future elite mount",
                _ => unit?.CardTag,
            };
        }

        static string BuildUnitRoleLabel(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "militia" => "Frontline levy",
                "swordsman" => "Line infantry",
                "knight" => "Shock cavalry",
                "spearman" => "Reach frontline",
                "halberdier" => "Anti-armor polearm",
                "lancer" => "Flank charger",
                "shieldman" => "Frontline anchor",
                "shield_guard" => "Heavy bulwark",
                "guardian" => "Elite bulwark",
                "cleric" => "Backline support",
                "priest" => "Backline healer",
                "high_priest" => "High support caster",
                "mage" => "Arcane artillery",
                "wizard" => "Battle mage",
                "thaumaturge" => "Arcane master",
                "archer" => "Ranged damage",
                "crossbowman" => "Anti-armor ranged",
                "ranger" => "Skirmish ranged",
                "peasant" => "Economy runner",
                "settler" => "Economy runner",
                "trader" => "Economy runner",
                "king" => "Hero commander",
                "paladin" => "Hero vanguard",
                "bishop" => "Hero support",
                "colt" => "Mount unlock",
                "stallion" => "Mount unlock",
                "dark_stallion" => "Mount unlock",
                _ => "Unit role pending",
            };
        }

        static string BuildUnitOriginText(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "militia" => "Raised from the town levy once the first Barracks is standing.",
                "swordsman" => "Trained out of militia once the Blacksmith professionalizes the infantry line.",
                "knight" => "Fielded from the elite mounted arm once the Blacksmith reaches its final tier.",
                "spearman" => "Recruited into the disciplined polearm ranks through the Blacksmith.",
                "halberdier" => "Advanced polearm specialist forged from the same Blacksmith branch.",
                "lancer" => "Cavalry polearm veteran released at the top of the spear line.",
                "shieldman" => "Drawn from the city guard and equipped as the front-rank wall.",
                "shield_guard" => "Veteran shield-line soldier outfitted through upgraded Blacksmith support.",
                "guardian" => "Late-game elite guard mounted and armored for the final defensive tier.",
                "cleric" => "Temple acolyte attached to marching companies for field care.",
                "priest" => "Ordained support unit sent from the upgraded Temple.",
                "high_priest" => "Senior temple leader deployed once the faith branch is fully matured.",
                "mage" => "Early battle caster licensed through the Wizard Tower.",
                "wizard" => "Veteran spellcaster trained after the tower reaches its middle tier.",
                "thaumaturge" => "Master arcane operative released at the peak of the Wizard Tower.",
                "archer" => "Drawn from huntsmen and garrison bowmen once the Archery Tower is built.",
                "crossbowman" => "Armory-trained marksman issued heavier ranged weapons at tier two.",
                "ranger" => "Veteran frontier skirmisher fielded from the fully upgraded Archery Tower.",
                "peasant" => "Starter trade laborer sent between the Town Core and Market.",
                "settler" => "Experienced civilian courier trusted with higher-value cargo.",
                "trader" => "Top-tier commercial runner representing the Market's late-game route economy.",
                "king" => "The sovereign enters the field only after Castle is secured.",
                "paladin" => "Holy champion released when the realm reaches Castle.",
                "bishop" => "Senior church leader unlocked at Castle to support the army.",
                "colt" => "Stable-bred mount slot prepared for future mounted expansion.",
                "stallion" => "Warhorse bred for the middle stable tier while mounted gameplay is still pending.",
                "dark_stallion" => "Late-tier stable mount reserved for future cavalry expansion.",
                _ => "Origin note pending.",
            };
        }

        static string BuildUnitSkillText(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "militia" => "Cheap early frontline body used to establish the second rank.",
                "swordsman" => "Reliable line fighter that upgrades early militia pressure into a sturdier core.",
                "knight" => "Shock cavalry finisher for the infantry branch.",
                "spearman" => "Third-rank reach support that helps control enemy approach.",
                "halberdier" => "Higher-tier polearm pressure with better anti-armor identity.",
                "lancer" => "Fast reach cavalry used to punish openings once the line is established.",
                "shieldman" => "First-rank anchor that protects the rest of the formation.",
                "shield_guard" => "Improved first-rank tank that stabilizes longer engagements.",
                "guardian" => "Elite defensive anchor for the late-game frontline.",
                "cleric" => "Back-rank sustain support and early healing coverage.",
                "priest" => "Stronger backline sustain with more reliable healing uptime.",
                "high_priest" => "Peak support output for extended battles.",
                "mage" => "Backline arcane damage with early spell pressure.",
                "wizard" => "Stronger magical throughput from deeper in the formation.",
                "thaumaturge" => "Late-game caster that should define the arcane back line.",
                "archer" => "Baseline ranged pressure from the fifth rank.",
                "crossbowman" => "Tier-two ranged specialist intended to hit harder than base archers.",
                "ranger" => "Late-game skirmisher intended to finish the ranged branch cleanly.",
                "peasant" => "Carries the starter economy route for the human trade branch.",
                "settler" => "Improves the value of every completed route lap.",
                "trader" => "Represents the fully upgraded market economy runner.",
                "king" => "Frontline hero commander for the Castle outcome row.",
                "paladin" => "Holy frontline hero meant to absorb and punish pressure.",
                "bishop" => "Backline hero support that should sit behind the main damage ranks.",
                "colt" => "Visual stable unlock prepared for future mounted roster logic.",
                "stallion" => "Mid-tier mount unlock awaiting live stable gameplay.",
                "dark_stallion" => "End-tier mount unlock awaiting live stable gameplay.",
                _ => "Skill note pending.",
            };
        }

        static string BuildUnitSpecialAttackText(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "militia" => "Swarm rush and rough melee pressure.",
                "swordsman" => "Disciplined sword-line strikes with steadier sustained hits.",
                "knight" => "Mounted breakthrough charge that punishes opened fronts.",
                "spearman" => "Brace-and-reach attacks that punish chargers.",
                "halberdier" => "Heavy halberd swings aimed at armored targets.",
                "lancer" => "Fast lance impact for flank breaks.",
                "shieldman" => "Shield wall and forward brace to absorb the opening clash.",
                "shield_guard" => "Heavy brace with longer hold time under pressure.",
                "guardian" => "Elite guard impact that keeps the first rank intact.",
                "cleric" => "Field mend and close support blessings.",
                "priest" => "Battle prayer and stronger targeted healing.",
                "high_priest" => "High-output sustain with stronger blessing coverage.",
                "mage" => "Arcane burst volleys from the back line.",
                "wizard" => "Heavier spell volleys with stronger magical pressure.",
                "thaumaturge" => "Late-game arcane control and high burst casting.",
                "archer" => "Rapid volley fire into softened targets.",
                "crossbowman" => "Armor-piercing bolt fire with heavier single shots.",
                "ranger" => "Skirmish volleys and mobile precision fire.",
                "peasant" => "Completes route laps for steady gold income.",
                "settler" => "Higher-value cargo runs with better route returns.",
                "trader" => "Top-tier trade deliveries with the highest lap income.",
                "king" => "Royal command presence with crushing frontline hits.",
                "paladin" => "Holy vanguard pressure with strong frontline resilience.",
                "bishop" => "Blessing support from the rear with strong sustain.",
                "colt" => "Mount unlock only; live combat output is not wired yet.",
                "stallion" => "Mount unlock only; live combat output is not wired yet.",
                "dark_stallion" => "Mount unlock only; live combat output is not wired yet.",
                _ => "Special attack note pending.",
            };
        }

        static string BuildCompactArmorText(UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return "--";

            return $"{HumanizeLabel(catalog.armor_type)}+{Mathf.Max(0f, catalog.damage_reduction_pct):0.#}%";
        }

        static float ComputeUnitDps(UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return 0f;

            return Mathf.Max(0f, catalog.attack_damage) * Mathf.Max(0.01f, catalog.attack_speed);
        }

        static float ComputeEffectiveHp(UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return 0f;

            float reduction = Mathf.Clamp01((Mathf.Max(0f, catalog.damage_reduction_pct) / 100f));
            float divisor = Mathf.Max(0.01f, 1f - reduction);
            return Mathf.Max(0f, catalog.hp) / divisor;
        }

        static string HumanizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var parts = value.Trim().ToLowerInvariant().Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "Unknown";

            var builder = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                string part = parts[i];
                builder.Append(part.Length <= 1
                    ? part.ToUpperInvariant()
                    : char.ToUpperInvariant(part[0]) + part.Substring(1));
            }

            return builder.ToString();
        }

        static string FormatStatNumber(float value)
        {
            float rounded = Mathf.Round(value * 10f) / 10f;
            return Mathf.Approximately(rounded, Mathf.Round(rounded))
                ? Mathf.RoundToInt(rounded).ToString()
                : rounded.ToString("0.0");
        }

        static string NormalizeTechTreeKey(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        static string Ordinal(int value)
        {
            return value switch
            {
                1 => "1st",
                2 => "2nd",
                3 => "3rd",
                _ => $"{value}th",
            };
        }

        static string BuildCurrentSourceText(UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return "Unavailable";

            string sourceUnit = HumanizeLabel(catalog.canonical_unit_type);
            if (string.IsNullOrWhiteSpace(catalog.canonical_unit_type))
                return "Not specified";

            return sourceUnit;
        }

        static string BuildLiveHookSummary(UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return "No live ability hooks configured.";

            var parts = new List<string>();
            if (catalog.abilities != null)
            {
                for (int i = 0; i < catalog.abilities.Length; i++)
                {
                    var ability = catalog.abilities[i];
                    if (ability == null || string.IsNullOrWhiteSpace(ability.ability_key))
                        continue;

                    string label = HumanizeLabel(ability.ability_key);
                    if (ability.@params is JObject abilityParams && abilityParams.Count > 0)
                        label = $"{label} ({abilityParams.ToString(Newtonsoft.Json.Formatting.None)})";
                    parts.Add(label);
                }
            }

            if (catalog.special_props is JObject specialProps)
            {
                foreach (var property in specialProps.Properties())
                {
                    if (property == null || property.Value == null || property.Value.Type == JTokenType.Null)
                        continue;

                    string valueText = property.Value.Type switch
                    {
                        JTokenType.Boolean => property.Value.Value<bool>() ? "On" : "Off",
                        JTokenType.Float => property.Value.Value<float>().ToString("0.##"),
                        JTokenType.Integer => property.Value.Value<long>().ToString(),
                        _ => property.Value.ToString(Newtonsoft.Json.Formatting.None),
                    };
                    parts.Add($"{HumanizeLabel(property.Name)} {valueText}");
                }
            }

            return parts.Count > 0
                ? string.Join(" | ", parts)
                : "No live ability hooks configured.";
        }

        string BuildUnitMoveSetText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Move preview unavailable.";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return "[Gate] This entry exists to show a build requirement, not a combat move set.";

            if (unit.CardDisplay != null)
                return BuildBuildingMoveSetText(unit);

            if (IsEconomyUnit(unit))
            {
                return
                    $"[Route] {BuildUnitSpecialAttackText(unit)}\n" +
                    "[Formation] This runner travels the trade route instead of joining the battle line.\n" +
                    "[Preview] Economy audio can be previewed below. Combat animation preview is not applicable.";
            }

            if (IsStableDisplayOnlyUnit(unit))
            {
                return
                    $"[Stable] {BuildUnitSpecialAttackText(unit)}\n" +
                    "[Formation] This branch is display-only while mounted gameplay is still pending.\n" +
                    "[Preview] No combat move preview is wired yet because the live stable roster is not active.";
            }

            var builder = new StringBuilder();
            builder.AppendLine($"[Primary] {BuildUnitSpecialAttackText(unit)}");
            builder.AppendLine($"[Stance] {BuildMoveStanceText(unit)}");

            if (TryGetCatalogEntry(unit, out var catalog))
            {
                builder.AppendLine($"[Delivery] {BuildUnitDeliveryText(unit, catalog)}");
                builder.AppendLine($"[Skills] {BuildCondensedLiveHookSummary(catalog)}");
                builder.Append(
                    TryResolvePreviewSfx(unit, out _, out var sfxLabel)
                        ? $"[Preview] Use the live rig above to preview idle, walk, march, run, strike, {BuildSpecialPreviewButtonLabel(unit)}, hit, and death. {sfxLabel} audio can also be previewed below."
                        : $"[Preview] Use the live rig above to preview idle, walk, march, run, strike, {BuildSpecialPreviewButtonLabel(unit)}, hit, and death. No dedicated unit SFX is wired for this entry yet.");
            }
            else
            {
                builder.Append("[Preview] Move buttons will still try to play the prefab states, but catalog-backed detail data is missing.");
            }

            return builder.ToString().TrimEnd();
        }

        static string BuildBuildingMoveSetText(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "ballista" =>
                    "[Siege] Launches a heavy bolt into lane targets.\n" +
                    "[Action] Fires a slow, armor-punching ranged shot.\n" +
                    "[Preview] Siege SFX can be previewed below.",
                "cannon" =>
                    "[Siege] Fires explosive artillery into clustered targets.\n" +
                    "[Action] Uses a blast-impact shot with a heavier report.\n" +
                    "[Preview] Siege SFX can be previewed below.",
                _ =>
                    "[Build] This entry upgrades a fortress structure or civic path.\n" +
                    "[Action] It changes unlocks and battlefield tools rather than performing a unit attack.\n" +
                    "[Preview] Construction or upgrade SFX can be previewed below when available.",
            };
        }

        static string BuildMoveStanceText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Stance unavailable.";

            int rank = GetFormationRank(unit);
            if (rank > 0)
                return $"Holds the {Ordinal(rank)} rank as {BuildUnitRoleLabel(unit).ToLowerInvariant()}.";

            if (IsEconomyUnit(unit))
                return "Runs the market route and avoids the main battle formation.";

            if (IsStableDisplayOnlyUnit(unit))
                return "Reserved for future cavalry positioning once mounted combat is live.";

            return "No live formation stance is assigned to this entry.";
        }

        static string BuildUnitDeliveryText(RaceProgressionUnitDefinition unit, UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return "Delivery data unavailable.";

            int rank = GetFormationRank(unit);
            string rankText = rank > 0 ? $"{Ordinal(rank)} rank" : "assigned position";
            string damageType = HumanizeLabel(catalog.damage_type);
            if (!string.IsNullOrWhiteSpace(catalog.proj_behavior))
            {
                return $"{HumanizeLabel(catalog.proj_behavior)} attack using {damageType.ToLowerInvariant()} damage from the {rankText}.";
            }

            if (catalog.range > 1f)
                return $"{damageType} ranged strike fired from the {rankText} at {FormatStatNumber(catalog.range)} range.";

            if (catalog.range > 0.30f)
                return $"{damageType} reach attack delivered from the {rankText}.";

            return $"{damageType} close-range strike delivered from the {rankText}.";
        }

        static string BuildCondensedLiveHookSummary(UnitCatalogEntry catalog)
        {
            string summary = BuildLiveHookSummary(catalog);
            return string.IsNullOrWhiteSpace(summary)
                ? "No extra live hooks wired."
                : summary;
        }

        string BuildUnitAudioStatusText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Audio preview unavailable.";

            bool hasSfx = TryResolvePreviewSfx(unit, out _, out var sfxLabel);
            bool hasVoice = TryResolvePreviewVoice(unit, out _, out var voiceLabel);

            var builder = new StringBuilder();
            builder.AppendLine(hasSfx
                ? $"[SFX] {sfxLabel} is ready to preview."
                : "[SFX] No dedicated preview clip is wired for this entry yet.");
            builder.Append(hasVoice
                ? $"[Voice] {voiceLabel} is ready to preview."
                : "[Voice] No voice lines are wired for this unit in the current project.");
            return builder.ToString();
        }

        bool TryResolvePreviewSfx(RaceProgressionUnitDefinition unit, out AudioManager.SFX sfx, out string label)
        {
            sfx = default;
            label = null;

            if (unit == null || unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return false;

            string unitId = NormalizeTechTreeKey(unit.Id);
            string laneId = NormalizeTechTreeKey(unit.LaneId);

            if (unitId == "ballista")
            {
                sfx = AudioManager.SFX.BallistaShoot;
                label = "Siege Bolt";
                return true;
            }

            if (unitId == "cannon")
            {
                sfx = AudioManager.SFX.CannonShoot;
                label = "Siege Cannon";
                return true;
            }

            if (IsEconomyUnit(unit))
            {
                sfx = AudioManager.SFX.GoldGain;
                label = "Trade Coin";
                return true;
            }

            if (unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                || unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
            {
                sfx = unit.IsStartUnit ? AudioManager.SFX.BuildTower : AudioManager.SFX.UpgradeTower;
                label = unit.IsStartUnit ? "Build Signal" : "Upgrade Fanfare";
                return true;
            }

            switch (laneId)
            {
                case "archery":
                    sfx = AudioManager.SFX.ArcherShoot;
                    label = "Arrow Volley";
                    return true;
                case "wizard":
                    sfx = AudioManager.SFX.MageShoot;
                    label = "Arcane Cast";
                    return true;
                case "infantry":
                case "polearm":
                case "shield":
                    sfx = AudioManager.SFX.FighterSlash;
                    label = "Steel Clash";
                    return true;
            }

            switch (unitId)
            {
                case "king":
                case "paladin":
                    sfx = AudioManager.SFX.FighterSlash;
                    label = "Hero Strike";
                    return true;
            }

            return false;
        }

        static bool TryResolvePreviewVoice(RaceProgressionUnitDefinition unit, out AudioManager.SFX sfx, out string label)
        {
            sfx = default;
            label = null;
            return false;
        }

        string BuildUnitDetailsBodyText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Tech tree entry unavailable.";

            if (unit.CardDisplay != null || unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return $"{unit.Description}\n{BuildUnitDetailsBodySuffix(unit)}";

            var builder = new StringBuilder();
            builder.AppendLine($"Tech tree note: {unit.Description}");

            if (TryGetCatalogEntry(unit, out var catalog)
                && !string.IsNullOrWhiteSpace(catalog.description)
                && !string.Equals(catalog.description.Trim(), unit.Description?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Current catalog note: {catalog.description.Trim()}");
            }

            builder.AppendLine($"Origin: {BuildUnitOriginText(unit)}");
            builder.AppendLine($"Role note: {BuildUnitSkillText(unit)}");

            string runtimeStatus = BuildUnitRuntimeStatusText(unit);
            if (!string.IsNullOrWhiteSpace(runtimeStatus))
                builder.AppendLine(runtimeStatus);

            string progressionAudit = BuildUnitProgressionAuditText(unit);
            if (!string.IsNullOrWhiteSpace(progressionAudit))
                builder.AppendLine(progressionAudit);

            builder.Append(BuildUnitDetailsBodySuffix(unit));
            return builder.ToString().TrimEnd();
        }

        string BuildUnitRuntimeStatusText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return null;

            if (unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
                return "Runtime note: Castle unlock is live, but summoning still also requires at least one built Barracks site.";

            if (IsStableDisplayOnlyUnit(unit))
                return "Runtime note: Stable progression exists in the tree, but the live game does not yet attach a horse roster to it.";

            if (IsEconomyUnit(unit))
                return "Runtime note: Market runners are live and generate gold on completed route laps.";

            return null;
        }

        string BuildUnitProgressionAuditText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null || _selectedRace == null || !_selectedRace.TryGetLane(unit.LaneId, out var lane))
                return null;

            var previousUnit = GetPreviousComparableUnit(lane.Units, unit);
            if (previousUnit == null || !TryGetCatalogEntry(unit, out var currentCatalog) || !TryGetCatalogEntry(previousUnit, out var previousCatalog))
                return null;

            var issues = new List<string>();
            float currentDps = ComputeUnitDps(currentCatalog);
            float previousDps = ComputeUnitDps(previousCatalog);
            if (currentDps + 0.05f < previousDps)
                issues.Add($"DPS falls from {previousDps:0.#} to {currentDps:0.#} versus {previousUnit.DisplayName}.");

            float currentEffectiveHp = ComputeEffectiveHp(currentCatalog);
            float previousEffectiveHp = ComputeEffectiveHp(previousCatalog);
            if (currentEffectiveHp + 0.5f < previousEffectiveHp)
                issues.Add($"Durability falls from {previousEffectiveHp:0.#} to {currentEffectiveHp:0.#} effective HP.");

            if (ShouldAuditRangeProgression(unit) && currentCatalog.range + 0.01f < previousCatalog.range)
                issues.Add($"Range falls from {FormatStatNumber(previousCatalog.range)} to {FormatStatNumber(currentCatalog.range)}.");

            return issues.Count > 0
                ? $"Progression warning: {string.Join(" ", issues)}"
                : $"Progression check: No numeric regression detected versus {previousUnit.DisplayName}.";
        }

        static bool ShouldAuditRangeProgression(RaceProgressionUnitDefinition unit)
        {
            string laneId = NormalizeTechTreeKey(unit?.LaneId);
            return laneId == "archery"
                || laneId == "wizard"
                || laneId == "temple";
        }

        static RaceProgressionUnitDefinition GetPreviousComparableUnit(RaceProgressionUnitDefinition[] units, RaceProgressionUnitDefinition currentUnit)
        {
            if (units == null || currentUnit == null)
                return null;

            int currentIndex = -1;
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] != null && string.Equals(units[i].Id, currentUnit.Id, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex <= 0)
                return null;

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                var candidate = units[i];
                if (candidate == null)
                    continue;
                if (candidate.CardStyle == RaceProgressionUnitCardStyle.BuildingTier
                    || candidate.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep
                    || candidate.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(ResolveTechTreeCatalogKey(candidate)))
                    return candidate;
            }

            return null;
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
                    _txtSecondaryAction.text = _activePage == WizardPage.ProgressionTree && _detailsModalOpen
                        ? "Close Details"
                        : _activePage == WizardPage.RaceSelection
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
            var manager = FindFirstObjectByType<LoadoutPhaseManager>(FindObjectsInactive.Include);
            var existing = SceneEventSystemUtility.FindBest(manager);

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
            PortraitCam.FitHeight = 2.45f;
            PortraitCam.FrameFill = 0.92f;
            PortraitCam.VerticalFocus = 0.70f;
            PortraitCam.CameraHeightBias = 0.00f;
            PortraitCam.LookAtHeightBias = 0.08f;
            PortraitCam.transform.position = new Vector3(0f, 0f, 50f);
            return PortraitCam;
        }

        void DestroyRuntimePortraitStudio()
        {
            if (_runtimePortraitRoot != null)
                Destroy(_runtimePortraitRoot);

            if (_runtimePortraitTexture != null)
                _runtimePortraitTexture.Release();

            if (_runtimePreviewRoot != null)
                Destroy(_runtimePreviewRoot);

            if (_runtimePreviewTexture != null)
                _runtimePreviewTexture.Release();

            _runtimePortraitRoot = null;
            _runtimePortraitTexture = null;
            _runtimePreviewRoot = null;
            _runtimePreviewTexture = null;
            _detailsPreviewStagedKey = null;
            PortraitCam = null;
            _detailsPreviewCam = null;
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
            var sectionImage = section.GetComponent<Image>();
            sectionImage.color = color;
            ClassicRpgUiRuntime.ApplyPanel(sectionImage, ClassicRpgPanelSkin.PaperMedium, true, color);
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

        void ApplyClassicRpgLabelTheme(TMP_Text label, bool title = false, bool centered = false)
        {
            if (label == null)
                return;

            ClassicRpgUiRuntime.ApplyText(
                label,
                title ? ClassicRpgTextTone.Title : centered ? ClassicRpgTextTone.Heading : ClassicRpgTextTone.Body,
                centered ? TextAlignmentOptions.Center : label.alignment);
        }

        void ApplyClassicRpgFrameTheme(Image image, string assetPath, bool sliced = false)
        {
            if (image == null)
                return;

            if (TryResolvePanelSkin(assetPath, out var skin))
                ClassicRpgUiRuntime.ApplyPanel(image, skin, sliced, image.color);
        }

        void ApplyClassicRpgButtonTheme(Button button, ClassicRpgButtonSize size)
        {
            if (button == null)
                return;

            ClassicRpgUiRuntime.ApplyButton(
                button,
                size == ClassicRpgButtonSize.Long ? ClassicRpgButtonSkin.LongGold : ClassicRpgButtonSkin.MediumGold);
        }

        static bool TryResolvePanelSkin(string assetPath, out ClassicRpgPanelSkin skin)
        {
            switch (assetPath)
            {
                case "Assets/ClassicRPGUI2/UIElementsPNG/FrameForSlicing.png":
                    skin = ClassicRpgPanelSkin.Frame;
                    return true;
                case "Assets/ClassicRPGUI2/UIElementsPNG/TitleLong.png":
                    skin = ClassicRpgPanelSkin.TitleLong;
                    return true;
                case "Assets/ClassicRPGUI2/UIElementsPNG/HpBar_PortraitFrame.png":
                    skin = ClassicRpgPanelSkin.PortraitFrame;
                    return true;
                case "Assets/ClassicRPGUI2/UIElementsPNG/HpBar_PortraitFrameBg.png":
                    skin = ClassicRpgPanelSkin.PortraitBackdrop;
                    return true;
                case "Assets/ClassicRPGUI2/UIElementsPNG/PaperMedium.png":
                    skin = ClassicRpgPanelSkin.PaperMedium;
                    return true;
                default:
                    skin = ClassicRpgPanelSkin.Frame;
                    return false;
            }
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
            ClassicRpgUiRuntime.ApplyText(txt, ClassicRpgTextTone.Body, txt.alignment, color);
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
            ClassicRpgUiRuntime.ApplyText(label, ClassicRpgTextTone.Body, label.alignment, color);
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
            ClassicRpgUiRuntime.ApplyText(text, ClassicRpgTextTone.Heading, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold);
            return button;
        }
    }
}
