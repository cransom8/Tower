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
            public Button ContinueButton;
            public TMP_Text Title;
            public TMP_Text Subtitle;
            public TMP_Text Summary;
            public Image StatusBackground;
            public TMP_Text Status;
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

        readonly struct DetailRowData
        {
            public readonly string IconResourcePath;
            public readonly string Label;
            public readonly string Value;
            public readonly Color ValueColor;
            public readonly Color IconColor;

            public DetailRowData(string iconResourcePath, string label, string value, Color valueColor, Color? iconColor = null)
            {
                IconResourcePath = iconResourcePath;
                Label = label;
                Value = value;
                ValueColor = valueColor;
                IconColor = iconColor ?? ClassicRpgUiRuntime.WarmGold;
            }
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
            Spawn,
            Idle,
            Walk,
            March,
            Run,
            Strike,
            Special,
            Defend,
            Retreat,
            Hit,
            Death,
        }

        struct DetailPreviewCycleEntry
        {
            public DetailPreviewMotion Motion;
            public string StateName;
            public float ClipLength;
            public float Speed;

            public DetailPreviewCycleEntry(DetailPreviewMotion motion, string stateName, float clipLength, float speed)
            {
                Motion = motion;
                StateName = stateName;
                ClipLength = clipLength;
                Speed = speed;
            }
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

        const float PortraitFrameHeight = 132f;
        const float UnitCardWidth = 236f;
        const float UnitCardHeight = 278f;
        const float BuildingCardHeight = 286f;
        const float BuildingTierCardWidth = 272f;
        const float BuildingImageFrameHeight = 84f;
        const float RequirementCardWidth = 168f;
        const float RequirementCardHeight = 126f;
        const float CompactRequirementCardHeight = 116f;
        const float LaneRowHeight = 332f;
        const float BuildingLaneRowHeight = 374f;
        const float CivicLaneRowHeight = 564f;
        const float ChainArrowWidth = 34f;
        const float CompactArrowWidth = 28f;
        // Race cards include portrait, dossier copy, and the selected-card Continue button.
        // Keep enough fixed height so the footer action does not get clipped off-screen.
        const float RaceCardHeight = 484f;
        const float RaceCardWidth = 316f;
        const float CompactRaceCardHeight = 392f;
        const float CompactRaceCardWidth = 264f;
        const int PlaceholderRaceCardCount = 3;
        const float MinLaneRowWidth = 960f;
        const float UnitCardFlexWidth = 4.6f;
        const float BuildingCardFlexWidth = 5.2f;
        const float HeroOutcomeCardWidth = 236f;
        const float UpgradeStepCardWidth = 252f;
        const float UpgradeStepCardHeight = 216f;
        const float RequirementCardFlexWidth = 2.7f;
        const float CompactRequirementCardFlexWidth = 2.1f;
        const float ArrowFlexWidth = 0.45f;
        const float RequiredRaceSelectionPortraitTimeoutSeconds = 10f;
        const float DetailsPreviewCycleInitialDelay = 1.2f;
        const float DetailsPreviewManualResumeDelay = 2.6f;
        const string WinterBackdropResourcePath = "UI/Lobby/WinterForestBackdrop";

        static Sprite _winterBackdropSprite;

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
        TMP_Text _txtDetailsState;
        TMP_Text _txtDetailsCustomization;
        TMP_Text _txtDetailsAudioStatus;
        TMP_Text _txtDetailsBody;
        TMP_Text _txtDetailsPreviewStatus;
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
        Transform _detailsStatsRowsRoot;
        Transform _detailsRequirementRowsRoot;
        Transform _detailsMovesRowsRoot;
        ScrollRect _raceCarouselScroll;
        Button _btnRaceCarouselPrev;
        Button _btnRaceCarouselNext;
        int _raceCarouselItemCount;

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
        readonly Dictionary<string, Sprite> _detailIconCache = new(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<string, Texture2D> _portraitCache = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<RawImage>> _pendingPortraitTargets = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _capturePending = new(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> _captureQueue = new();
        Coroutine _portraitWarmupRoutine;
        Coroutine _criticalWarmupRoutine;
        Coroutine _environmentWarmupRoutine;
        Coroutine _raceSelectionGateRoutine;
        GameObject _runtimePortraitRoot;
        RenderTexture _runtimePortraitTexture;
        RenderTexture _runtimePreviewTexture;
        bool _isCapturingPortraits;
        UnitPortraitCamera _detailsPreviewCam;
        Coroutine _detailsPreviewResetRoutine;
        Coroutine _detailsPreviewCycleRoutine;
        string _detailsPreviewStagedKey;
        string _detailsPreviewCycleSignature;
        int _raceSelectionGateVersion;

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
            StopRaceSelectionGate();
            StopDetailsPreviewResetRoutine();
            StopDetailsPreviewCycleRoutine();
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
                    if (secs > 0)
                        _txtTimer.SetText("{0}s", secs);
                    else
                        _txtTimer.text = "0s";
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
            _selectedUnit = GetDefaultUnitForTab(_selectedRace, _selectedTreeTab);
            _timerRemaining = 0f;
            _phaseStartTime = Time.unscaledTime;
            StartViewerWarmup();
            BeginRaceSelectionPresentationGate("Preparing War Council", BuildRequiredRaceSelectionLoadingDetail());
        }

        void OpenEmbeddedViewer()
        {
            _mode = ProgressionViewerMode.LobbyViewer;
            _state = PhaseState.Viewing;
            _activePage = WizardPage.RaceSelection;
            _availableRaceIds = GetAvailableRaceIds(_embeddedAvailableRaceIds);
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, _embeddedRequestedRaceId, "embedded viewer");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "embedded viewer");
            _selectedUnit = GetDefaultUnitForTab(_selectedRace, _selectedTreeTab);
            _timerRemaining = 0f;
            _phaseStartTime = Time.unscaledTime;
            StartViewerWarmup();
            BeginRaceSelectionPresentationGate("Preparing War Council", BuildRequiredRaceSelectionLoadingDetail());
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
            _selectedUnit = GetDefaultUnitForTab(_selectedRace, _selectedTreeTab);

            StartPreMatchWarmup();
            BeginRaceSelectionPresentationGate("Preparing Race Progression", BuildRequiredRaceSelectionLoadingDetail());

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

        void BeginRaceSelectionPresentationGate(string status, string detail)
        {
            StopRaceSelectionGate();

            if (!_isEmbeddedViewer)
                SetPrepOverlayText(status, detail);

            int gateVersion = ++_raceSelectionGateVersion;
            _raceSelectionGateRoutine = StartCoroutine(WaitForRaceSelectionPresentationReady(gateVersion));
        }

        void StopRaceSelectionGate()
        {
            _raceSelectionGateVersion++;
            if (_raceSelectionGateRoutine == null)
                return;

            StopCoroutine(_raceSelectionGateRoutine);
            _raceSelectionGateRoutine = null;
        }

        IEnumerator WaitForRaceSelectionPresentationReady(int gateVersion)
        {
            string portraitKey = _selectedRace?.FeaturedPortraitKey;
            if (!string.IsNullOrWhiteSpace(portraitKey))
            {
                string blockingIssue = null;
                yield return EnsureRequiredRaceSelectionPortraitReady(portraitKey, issue => blockingIssue = issue);

                if (gateVersion != _raceSelectionGateVersion)
                    yield break;

                if (!string.IsNullOrWhiteSpace(blockingIssue))
                {
                    Debug.LogError($"[RaceProgression] {blockingIssue}");
                    if (!_isEmbeddedViewer)
                        SetPrepOverlayText("Race Selection Blocked", blockingIssue);
                    _raceSelectionGateRoutine = null;
                    yield break;
                }
            }

            RebuildPanel();
            if (!_isEmbeddedViewer)
                HidePrepOverlay();
            _raceSelectionGateRoutine = null;
        }

        IEnumerator EnsureRequiredRaceSelectionPortraitReady(string key, Action<string> onFailure)
        {
            if (TryGetReadyPortraitTexture(key, out _))
                yield break;

            if (ShouldUseRuntimeSkinPortrait(key))
            {
                string sourceIssue = ValidateRequiredRuntimePortraitSource(key);
                if (!string.IsNullOrWhiteSpace(sourceIssue))
                {
                    onFailure?.Invoke(sourceIssue);
                    yield break;
                }

                if (EnsurePortraitCamera() == null)
                {
                    onFailure?.Invoke($"Could not create the runtime portrait camera for required featured portrait '{key}'.");
                    yield break;
                }

                QueueRuntimePortraitCapture(key);
                float timeoutAt = Time.unscaledTime + RequiredRaceSelectionPortraitTimeoutSeconds;
                while (!TryGetReadyPortraitTexture(key, out _))
                {
                    if (Time.unscaledTime >= timeoutAt && !_capturePending.Contains(key) && !_isCapturingPortraits)
                    {
                        onFailure?.Invoke($"Timed out waiting for required featured portrait '{key}' to finish rendering.");
                        yield break;
                    }

                    yield return null;
                }

                yield break;
            }

            string portraitLookupKey = ResolvePortraitLookupKey(key);
            if (string.IsNullOrWhiteSpace(portraitLookupKey))
            {
                onFailure?.Invoke($"Portrait lookup key for required featured portrait '{key}' is invalid.");
                yield break;
            }

            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                onFailure?.Invoke($"Remote content manager is unavailable, so required featured portrait '{key}' could not be loaded.");
                yield break;
            }

            yield return remoteContent.EnsurePortraitsReady(
                new[] { portraitLookupKey },
                requester: _mode == ProgressionViewerMode.PreMatchConfirm
                    ? "RaceProgression.RequiredPreMatchPortrait"
                    : "RaceProgression.RequiredLobbyPortrait");

            if (!TryGetReadyPortraitTexture(key, out _))
                onFailure?.Invoke($"Required featured portrait '{key}' did not finish loading.");
        }

        string BuildRequiredRaceSelectionLoadingDetail()
        {
            if (_selectedRace == null)
                return "Loading featured race presentation...";

            string featuredTitle = string.IsNullOrWhiteSpace(_selectedRace.FeaturedTitle)
                ? _selectedRace.DisplayName
                : _selectedRace.FeaturedTitle;
            return $"Loading required featured portrait for {featuredTitle}...";
        }

        string ValidateRequiredRuntimePortraitSource(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "Required featured portrait key is missing.";

            var registry = ResolvePortraitRegistry();
            if (registry == null)
                return $"Portrait registry is missing, so required featured portrait '{key}' cannot be staged.";

            if (registry.TryGetRenderableExactSkinPrefab(key, out _, out string issue))
                return null;

            string normalizedIssue = string.IsNullOrWhiteSpace(issue) ? "exact skin prefab could not be resolved" : issue;
            return $"Required featured portrait '{key}' is unavailable because its exact prefab is not renderable ({normalizedIssue}). Race selection cannot open until this is fixed.";
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

            var hostCanvas = parent.GetComponentInParent<Canvas>();
            var canvasRect = hostCanvas != null ? hostCanvas.GetComponent<RectTransform>() : null;
            if (hostCanvas != null)
                ClassicRpgUiRuntime.ApplyCanvasScaler(hostCanvas.GetComponent<CanvasScaler>(), ClassicRpgUiRuntime.ReferenceResolution);

            bool compact = ClassicRpgUiRuntime.IsCompactLayout(canvasRect);
            bool premiumShellPresentation = UsePremiumShellPresentation();

            _panelRoot = new GameObject("Panel_RaceProgression");
            _panelRoot.transform.SetParent(parent, false);
            var rootRect = _panelRoot.AddComponent<RectTransform>();
            if (_isEmbeddedViewer || premiumShellPresentation)
            {
                ClassicRpgUiRuntime.Stretch(rootRect);
            }
            else
            {
                ClassicRpgUiRuntime.ApplySafeArea(
                    rootRect,
                    canvasRect,
                    compact ? 16f : 26f,
                    compact ? 16f : 24f,
                    compact ? 18f : 24f);
            }
            var rootImage = _panelRoot.AddComponent<Image>();
            rootImage.color = premiumShellPresentation
                ? Color.clear
                : ClassicRpgUiRuntime.BackdropColor;
            if (premiumShellPresentation)
                BuildWinterBackdrop(_panelRoot.transform, compact);
            else
                ClassicRpgUiRuntime.ApplyPanel(rootImage, ClassicRpgPanelSkin.DarkSpell, false, new Color(1f, 1f, 1f, 0.26f));

            if (!premiumShellPresentation)
            {
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
            }

            var stage = new GameObject("Stage", typeof(RectTransform), typeof(VerticalLayoutGroup));
            stage.transform.SetParent(_panelRoot.transform, false);
            var stageRect = stage.GetComponent<RectTransform>();
            if (premiumShellPresentation && !_isEmbeddedViewer)
            {
                ClassicRpgUiRuntime.ApplySafeArea(
                    stageRect,
                    canvasRect,
                    compact ? 12f : 18f,
                    compact ? 10f : 14f,
                    compact ? 8f : 10f);
            }
            else
            {
                ClassicRpgUiRuntime.Stretch(
                    stageRect,
                    premiumShellPresentation
                        ? new Vector2(compact ? 10f : 18f, compact ? 10f : 16f)
                        : new Vector2(12f, 12f),
                    premiumShellPresentation
                        ? new Vector2(compact ? -10f : -18f, compact ? -10f : -16f)
                        : new Vector2(-12f, -12f));
            }
            var layout = stage.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = premiumShellPresentation ? (compact ? 8f : 10f) : (compact ? 10f : 14f);
            layout.padding = premiumShellPresentation
                ? (compact ? new RectOffset(8, 8, 10, 8) : new RectOffset(14, 14, 14, 10))
                : (compact ? new RectOffset(12, 12, 12, 12) : new RectOffset(18, 18, 18, 18));

            if (premiumShellPresentation)
                BuildPremiumPageHeader(stage.transform, compact);
            else
            {
                var headerPlate = new GameObject("HeaderPlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                headerPlate.transform.SetParent(stage.transform, false);
                var headerLayout = headerPlate.GetComponent<LayoutElement>();
                headerLayout.preferredHeight = compact ? 124f : 144f;
                var headerImage = headerPlate.GetComponent<Image>();
                ClassicRpgUiRuntime.ApplyPanel(headerImage, ClassicRpgPanelSkin.TitleLong, false, Color.white);
                var headerGroup = headerPlate.AddComponent<VerticalLayoutGroup>();
                headerGroup.childAlignment = TextAnchor.MiddleCenter;
                headerGroup.childControlWidth = true;
                headerGroup.childControlHeight = true;
                headerGroup.childForceExpandWidth = true;
                headerGroup.childForceExpandHeight = false;
                headerGroup.spacing = 2f;
                headerGroup.padding = new RectOffset(28, 28, compact ? 18 : 20, compact ? 14 : 18);

                _txtTitle = MakeLabel(headerPlate.transform, "Txt_Title", "Choose Your Race", compact ? 20 : 22, Color.white, compact ? 34f : 40f);
                ApplyPlateTitleStyle(_txtTitle, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center);
                SetResponsiveSingleLine(_txtTitle, compact ? 16f : 18f, compact ? 20f : 22f);

                _txtSubtitle = MakeLabel(headerPlate.transform, "Txt_Subtitle", "", compact ? 13 : 15, new Color(0.82f, 0.85f, 0.92f), compact ? 22f : 24f);
                _txtSubtitle.alignment = TextAlignmentOptions.Center;
                ApplyReadableTextStyle(_txtSubtitle, new Color(0.82f, 0.85f, 0.92f), TextAlignmentOptions.Center);

                _txtTimer = MakeLabel(headerPlate.transform, "Txt_Timer", "", compact ? 18 : 20, timerNormalColor, 26f);
                _txtTimer.gameObject.SetActive(_mode == ProgressionViewerMode.PreMatchConfirm);
                ApplyReadableTextStyle(_txtTimer, timerNormalColor, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            var bodyRoot = new GameObject("BodyRoot", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            bodyRoot.transform.SetParent(stage.transform, false);
            var bodyLayoutElement = bodyRoot.GetComponent<LayoutElement>();
            bodyLayoutElement.flexibleHeight = 1f;
            bodyLayoutElement.flexibleWidth = 1f;
            var bodyLayout = bodyRoot.GetComponent<VerticalLayoutGroup>();
            bodyLayout.childAlignment = TextAnchor.UpperCenter;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = true;
            bodyLayout.spacing = 0f;
            bodyLayout.padding = new RectOffset(0, 0, 0, 0);

            BuildCurrentPage(bodyRoot.transform);

            var footer = premiumShellPresentation
                ? CreateFloatingFooter(stage.transform, compact)
                : CreateSectionPanel(stage.transform, "FooterStrip", ClassicRpgUiRuntime.PanelFill, compact ? 132f : 118f, flexibleHeight: 0f);
            if (!premiumShellPresentation)
                footer.GetComponent<LayoutElement>().preferredHeight = _mode == ProgressionViewerMode.PreMatchConfirm ? (compact ? 172f : 146f) : (compact ? 132f : 118f);

            _txtStatus = MakeLabel(
                footer.transform,
                "Txt_Status",
                "",
                compact ? 13 : 15,
                premiumShellPresentation ? new Color(0.88f, 0.85f, 0.79f, 0.94f) : new Color(0.74f, 0.78f, 0.85f),
                premiumShellPresentation ? (compact ? 24f : 28f) : (compact ? 46f : 34f));
            _txtStatus.alignment = TextAlignmentOptions.Center;
            ApplyReadableTextStyle(
                _txtStatus,
                premiumShellPresentation ? new Color(0.90f, 0.86f, 0.80f, 0.96f) : new Color(0.78f, 0.82f, 0.90f),
                TextAlignmentOptions.Center);

            if (_mode == ProgressionViewerMode.PreMatchConfirm)
                BuildPlayerPanel(footer.transform);

            BuildActionRow(footer.transform);
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
            _txtDetailsState = null;
            _detailsStatsRowsRoot = null;
            _detailsRequirementRowsRoot = null;
            _detailsMovesRowsRoot = null;
            _txtDetailsCustomization = null;
            _txtDetailsAudioStatus = null;
            _txtDetailsBody = null;
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
            _raceCarouselScroll = null;
            _btnRaceCarouselPrev = null;
            _btnRaceCarouselNext = null;
            _raceCarouselItemCount = 0;
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
                    BuildProgressionWorkspace(parent);
                    break;
                case WizardPage.UnitDetails:
                    _activePage = WizardPage.ProgressionTree;
                    BuildProgressionWorkspace(parent);
                    break;
            }
        }

        void BuildProgressionWorkspace(Transform parent)
        {
            SyncSelectedUnitToCurrentTab();
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_panelRoot != null ? _panelRoot.GetComponent<RectTransform>() : null);
            bool scenicProgressionPage = UseScenicProgressionPresentation();
            var workspace = new GameObject(
                "ProgressionWorkspace",
                typeof(RectTransform),
                typeof(LayoutElement),
                compact ? typeof(VerticalLayoutGroup) : typeof(HorizontalLayoutGroup));
            workspace.transform.SetParent(parent, false);
            var workspaceLayout = workspace.GetComponent<LayoutElement>();
            workspaceLayout.flexibleWidth = 1f;
            workspaceLayout.flexibleHeight = 1f;

            if (compact)
            {
                var layout = workspace.GetComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 12f;
            }
            else
            {
                var layout = workspace.GetComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.spacing = scenicProgressionPage ? 22f : 14f;
            }

            var summaryColumn = new GameObject("SummaryColumn", typeof(RectTransform), typeof(LayoutElement));
            summaryColumn.transform.SetParent(workspace.transform, false);
            var summaryLayout = summaryColumn.GetComponent<LayoutElement>();
            summaryLayout.preferredWidth = compact ? 0f : scenicProgressionPage ? 228f : 272f;
            summaryLayout.flexibleWidth = compact ? 1f : 0f;
            summaryLayout.flexibleHeight = compact ? 0f : 1f;
            summaryLayout.minWidth = compact ? 0f : scenicProgressionPage ? 214f : 248f;
            summaryLayout.preferredHeight = compact ? 242f : 0f;
            BuildRaceSummarySidebar(summaryColumn.transform, compact);

            var treeColumn = new GameObject("TreeColumn", typeof(RectTransform), typeof(LayoutElement));
            treeColumn.transform.SetParent(workspace.transform, false);
            var treeLayout = treeColumn.GetComponent<LayoutElement>();
            treeLayout.flexibleWidth = 1f;
            treeLayout.flexibleHeight = 1f;
            treeLayout.minWidth = compact ? 0f : scenicProgressionPage ? 780f : 640f;
            treeLayout.minHeight = compact ? 320f : 0f;
            BuildProgressionTree(treeColumn.transform);

            var detailColumn = new GameObject("DetailColumn", typeof(RectTransform), typeof(LayoutElement));
            detailColumn.transform.SetParent(workspace.transform, false);
            var detailLayout = detailColumn.GetComponent<LayoutElement>();
            detailLayout.preferredWidth = compact ? 0f : scenicProgressionPage ? 256f : 320f;
            detailLayout.flexibleWidth = compact ? 1f : 0f;
            detailLayout.flexibleHeight = compact ? 0f : 1f;
            detailLayout.minWidth = compact ? 0f : scenicProgressionPage ? 240f : 292f;
            detailLayout.preferredHeight = compact ? 338f : 0f;
            BuildDetailsPanel(detailColumn.transform);
        }

        void BuildRaceSummarySidebar(Transform parent, bool compact)
        {
            bool scenicProgressionPage = UseScenicProgressionPresentation();
            var section = CreateSectionPanel(
                parent,
                "Section_Summary",
                scenicProgressionPage ? new Color(0.07f, 0.07f, 0.09f, 0.88f) : new Color(0.09f, 0.11f, 0.17f, 0.98f),
                compact ? 236f : 0f,
                flexibleHeight: compact ? 0f : 1f);
            var sectionLayout = section.GetComponent<VerticalLayoutGroup>();
            if (sectionLayout != null && scenicProgressionPage)
            {
                sectionLayout.padding = compact ? new RectOffset(10, 10, 10, 10) : new RectOffset(12, 12, 12, 12);
                sectionLayout.spacing = compact ? 6f : 8f;
            }
            var title = MakeLabel(section.transform, "Txt_SummaryHeader", "Race Dossier", compact ? 18 : 20, Color.white, 28f);
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;

            if (_selectedRace != null)
            {
                var portraitCard = new GameObject("RacePortrait", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                portraitCard.transform.SetParent(section.transform, false);
                portraitCard.GetComponent<LayoutElement>().preferredHeight = compact ? 64f : scenicProgressionPage ? 92f : 116f;
                ClassicRpgUiRuntime.ApplyPanel(portraitCard.GetComponent<Image>(), ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.07f, 0.10f, 0.16f, 0.98f));

                var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
                portrait.transform.SetParent(portraitCard.transform, false);
                var portraitRect = portrait.GetComponent<RectTransform>();
                ClassicRpgUiRuntime.Stretch(portraitRect, new Vector2(6f, 6f), new Vector2(-6f, -6f));
                var portraitImage = portrait.GetComponent<RawImage>();
                portraitImage.color = new Color(1f, 1f, 1f, 0f);
                portraitImage.raycastTarget = false;
                var portraitFitter = portrait.GetComponent<AspectRatioFitter>();
                portraitFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                portraitFitter.aspectRatio = 1f;
                StartPortraitCapture(_selectedRace.FeaturedPortraitKey, portraitImage);

                var raceName = MakeLabel(section.transform, "Txt_RaceName", _selectedRace.DisplayName, compact ? 22 : 24, ClassicRpgUiRuntime.WarmGold, 34f);
                raceName.fontStyle = FontStyles.Bold;
                raceName.alignment = TextAlignmentOptions.Center;

                var raceTitle = MakeLabel(section.transform, "Txt_RaceTitle", _selectedRace.FeaturedTitle, compact ? 13 : 14, new Color(0.84f, 0.88f, 0.95f), 26f);
                raceTitle.alignment = TextAlignmentOptions.Center;

                var raceSummary = MakeLabel(section.transform, "Txt_RaceSummary", _selectedRace.Summary, compact ? 12 : 14, new Color(0.86f, 0.89f, 0.94f), compact ? 40f : 112f);
                raceSummary.alignment = TextAlignmentOptions.TopLeft;
                raceSummary.textWrappingMode = TextWrappingModes.Normal;
                SetResponsiveWrappedText(raceSummary, compact ? 11f : 13f, compact ? 12f : 14f);
            }

            var tabsHeader = MakeLabel(section.transform, "Txt_TabHeader", compact ? "Categories" : "Progression Categories", 14, ClassicRpgUiRuntime.SoftGold, 24f);
            tabsHeader.fontStyle = FontStyles.Bold;
            tabsHeader.alignment = TextAlignmentOptions.Center;

            BuildTreeTabBar(section.transform, vertical: !compact);
        }

        void BuildRaceSelector(Transform parent)
        {
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_panelRoot != null ? _panelRoot.GetComponent<RectTransform>() : null);
            bool premiumShellPresentation = UsePremiumShellPresentation();
            float cardWidth = compact ? CompactRaceCardWidth : RaceCardWidth;
            float cardHeight = compact ? CompactRaceCardHeight : RaceCardHeight;

            var section = CreateSectionPanel(
                parent,
                "Section_RaceSelector",
                premiumShellPresentation ? new Color(0.02f, 0.02f, 0.03f, 0.80f) : new Color(0.08f, 0.11f, 0.17f, 0.98f),
                0f,
                flexibleHeight: 1f);
            var sectionLayout = section.GetComponent<VerticalLayoutGroup>();
            if (sectionLayout != null && premiumShellPresentation)
            {
                sectionLayout.padding = compact ? new RectOffset(12, 12, 12, 12) : new RectOffset(18, 18, 16, 16);
                sectionLayout.spacing = compact ? 8f : 10f;
            }

            var header = MakeLabel(section.transform, "Txt_RaceHeader", premiumShellPresentation ? "War Council Banners" : "Select Race", compact ? 18 : 20, Color.white, 28f);
            header.fontStyle = FontStyles.Bold;
            header.alignment = TextAlignmentOptions.Center;

            var helper = MakeLabel(
                section.transform,
                "Txt_RaceHelper",
                "Choose a banner from the war council. Swipe or use the side buttons to inspect upcoming factions.",
                compact ? 12 : 13,
                new Color(0.82f, 0.88f, 0.96f),
                compact ? 40f : 28f);
            helper.alignment = TextAlignmentOptions.Center;
            SetResponsiveWrappedText(helper, compact ? 11f : 12f, compact ? 12f : 13f);

            var carouselRow = new GameObject("RaceCarouselRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            carouselRow.transform.SetParent(section.transform, false);
            carouselRow.GetComponent<LayoutElement>().preferredHeight = cardHeight + 18f;
            carouselRow.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var carouselLayout = carouselRow.GetComponent<HorizontalLayoutGroup>();
            carouselLayout.childAlignment = TextAnchor.MiddleCenter;
            carouselLayout.childControlWidth = true;
            carouselLayout.childControlHeight = true;
            carouselLayout.childForceExpandWidth = false;
            carouselLayout.childForceExpandHeight = false;
            carouselLayout.spacing = compact ? 8f : 12f;

            _btnRaceCarouselPrev = MakeButton(carouselRow.transform, "Btn_RacePrev", "<", compact ? 48f : 54f, new Color(0.18f, 0.24f, 0.35f, 1f));
            var prevLayout = _btnRaceCarouselPrev.GetComponent<LayoutElement>();
            if (prevLayout != null)
            {
                prevLayout.preferredWidth = compact ? 54f : 62f;
                prevLayout.minWidth = compact ? 54f : 62f;
            }
            if (premiumShellPresentation)
                ApplyLobbyButtonStyle(_btnRaceCarouselPrev, ClassicRpgButtonSkin.MiniBrown, compact ? 42f : 46f, compact ? 54f : 62f);
            else
                ApplyClassicRpgButtonTheme(_btnRaceCarouselPrev, ClassicRpgButtonSize.Medium);
            _btnRaceCarouselPrev.onClick.AddListener(() => ShiftRaceCarousel(-1));

            var scrollGo = new GameObject("RaceCarouselScroll", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            scrollGo.transform.SetParent(carouselRow.transform, false);
            if (premiumShellPresentation)
            {
                ClassicRpgUiRuntime.ApplyPanel(scrollGo.GetComponent<Image>(), ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.02f, 0.02f, 0.03f, 0.82f));
                EnsureDecorativeFrame(scrollGo.GetComponent<RectTransform>());
            }
            else
            {
                ApplyReadablePanelStyle(scrollGo.GetComponent<Image>(), new Color(0.07f, 0.10f, 0.16f, 0.92f));
            }
            var scrollLayout = scrollGo.GetComponent<LayoutElement>();
            scrollLayout.flexibleWidth = 1f;
            scrollLayout.preferredHeight = cardHeight + 18f;
            scrollLayout.minWidth = compact ? 0f : 720f;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.scrollSensitivity = 28f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(12f, 8f);
            viewportRect.offsetMax = new Vector2(-12f, -8f);
            viewportGo.GetComponent<Image>().color = Color.white;
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0.5f);
            contentRect.anchorMax = new Vector2(0f, 0.5f);
            contentRect.pivot = new Vector2(0f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var contentLayout = contentGo.GetComponent<HorizontalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.MiddleLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = compact ? 14f : 18f;
            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            var contentFitter = contentGo.GetComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            _raceCarouselScroll = scrollRect;
            _raceCarouselItemCount = _availableRaceIds.Length + PlaceholderRaceCardCount;

            for (int i = 0; i < _availableRaceIds.Length; i++)
            {
                var race = RaceProgressionCatalog.GetOrDefault(_availableRaceIds[i], "race selector");
                _raceCards.Add(BuildRaceCard(contentGo.transform, race, compact));
            }

            for (int i = 0; i < PlaceholderRaceCardCount; i++)
                BuildComingSoonRaceCard(contentGo.transform, compact, i + 1);

            _btnRaceCarouselNext = MakeButton(carouselRow.transform, "Btn_RaceNext", ">", compact ? 48f : 54f, new Color(0.18f, 0.24f, 0.35f, 1f));
            var nextLayout = _btnRaceCarouselNext.GetComponent<LayoutElement>();
            if (nextLayout != null)
            {
                nextLayout.preferredWidth = compact ? 54f : 62f;
                nextLayout.minWidth = compact ? 54f : 62f;
            }
            if (premiumShellPresentation)
                ApplyLobbyButtonStyle(_btnRaceCarouselNext, ClassicRpgButtonSkin.MiniBrown, compact ? 42f : 46f, compact ? 54f : 62f);
            else
                ApplyClassicRpgButtonTheme(_btnRaceCarouselNext, ClassicRpgButtonSize.Medium);
            _btnRaceCarouselNext.onClick.AddListener(() => ShiftRaceCarousel(1));

            scrollRect.onValueChanged.AddListener(_ => RefreshRaceCarouselButtons());
            Canvas.ForceUpdateCanvases();
            ScrollRaceCarouselToIndex(GetSelectedRaceCarouselIndex());
            RefreshRaceCarouselButtons();
        }

        RaceCardView BuildRaceCard(Transform parent, RaceProgressionDefinition race, bool compact)
        {
            bool premiumShellPresentation = UsePremiumShellPresentation();
            float cardWidth = compact ? CompactRaceCardWidth : RaceCardWidth;
            float cardHeight = compact ? CompactRaceCardHeight : RaceCardHeight;
            var cardGo = new GameObject($"Race_{race.Id}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(Button), typeof(VerticalLayoutGroup));
            cardGo.transform.SetParent(parent, false);
            var cardLayout = cardGo.GetComponent<LayoutElement>();
            cardLayout.preferredWidth = cardWidth;
            cardLayout.preferredHeight = cardHeight;
            cardLayout.minWidth = cardWidth;
            cardLayout.minHeight = cardHeight;

            var background = cardGo.GetComponent<Image>();
            if (premiumShellPresentation)
            {
                ClassicRpgUiRuntime.ApplyPanel(background, ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.07f, 0.05f, 0.04f, 0.96f));
                EnsureDecorativeFrame(cardGo.GetComponent<RectTransform>());
            }
            else
            {
                ApplyReadablePanelStyle(background, lockedColor);
            }

            var button = cardGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnRaceSelected(race.Id));

            var layout = cardGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 8f : 10f;
            layout.padding = compact ? new RectOffset(14, 14, 14, 14) : new RectOffset(18, 18, 18, 18);

            var frame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(cardGo.transform, false);
            ApplyReadablePanelStyle(frame.GetComponent<Image>(), new Color(0.09f, 0.12f, 0.20f, 0.98f));
            frame.AddComponent<LayoutElement>().preferredHeight = compact ? 148f : 196f;

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(frame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(12f, 10f);
            portraitRect.offsetMax = new Vector2(-12f, -10f);
            var portrait = portraitGo.GetComponent<RawImage>();
            portrait.color = new Color(1f, 1f, 1f, 0f);
            portrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 0.88f;
            StartPortraitCapture(race.FeaturedPortraitKey, portrait);

            var title = MakeLabel(cardGo.transform, "Txt_Title", race.DisplayName, compact ? 22 : 26, Color.white, compact ? 34f : 40f);
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(title, compact ? 18f : 20f, compact ? 22f : 26f);

            var subtitle = MakeLabel(cardGo.transform, "Txt_Subtitle", NormalizeDossierValue(race.FeaturedTitle, "Faction ready"), compact ? 13 : 14, new Color(0.82f, 0.88f, 0.96f), 24f);
            subtitle.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(subtitle, 11f, compact ? 13f : 14f);

            var summary = MakeLabel(cardGo.transform, "Txt_Summary", BuildRaceCardSummary(race), compact ? 12 : 13, new Color(0.84f, 0.88f, 0.95f), compact ? 76f : 92f);
            summary.alignment = TextAlignmentOptions.TopLeft;
            SetResponsiveWrappedText(summary, compact ? 11f : 12f, compact ? 12f : 13f);

            var continueButton = MakeButton(cardGo.transform, "Btn_Continue", "Continue", compact ? 42f : 44f, Color.white);
            continueButton.onClick.AddListener(() =>
            {
                OnRaceSelected(race.Id);
                NavigateToPage(WizardPage.ProgressionTree);
            });
            if (premiumShellPresentation)
                ApplyLobbyButtonStyle(continueButton, ClassicRpgButtonSkin.MiniGold, compact ? 42f : 44f, compact ? 150f : 172f);
            else
                ApplyClassicRpgButtonTheme(continueButton, ClassicRpgButtonSize.Medium);
            continueButton.gameObject.SetActive(false);

            return new RaceCardView
            {
                RaceId = race.Id,
                Background = background,
                Button = button,
                ContinueButton = continueButton,
                Title = title,
                Subtitle = subtitle,
                Summary = summary,
                Portrait = portrait,
            };
        }

        void BuildComingSoonRaceCard(Transform parent, bool compact, int slotIndex)
        {
            bool premiumShellPresentation = UsePremiumShellPresentation();
            float cardWidth = compact ? CompactRaceCardWidth : RaceCardWidth;
            float cardHeight = compact ? CompactRaceCardHeight : RaceCardHeight;
            var cardGo = new GameObject($"RaceComingSoon_{slotIndex}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            cardGo.transform.SetParent(parent, false);
            var cardLayout = cardGo.GetComponent<LayoutElement>();
            cardLayout.preferredWidth = cardWidth;
            cardLayout.preferredHeight = cardHeight;
            cardLayout.minWidth = cardWidth;
            cardLayout.minHeight = cardHeight;
            if (premiumShellPresentation)
            {
                ClassicRpgUiRuntime.ApplyPanel(cardGo.GetComponent<Image>(), ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.05f, 0.04f, 0.04f, 0.94f));
                EnsureDecorativeFrame(cardGo.GetComponent<RectTransform>());
            }
            else
            {
                ApplyReadablePanelStyle(cardGo.GetComponent<Image>(), new Color(0.10f, 0.11f, 0.15f, 0.98f));
            }
            cardGo.GetComponent<CanvasGroup>().alpha = 0.94f;

            var layout = cardGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 8f : 10f;
            layout.padding = compact ? new RectOffset(14, 14, 14, 14) : new RectOffset(18, 18, 18, 18);

            var crestFrame = new GameObject("CrestFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            crestFrame.transform.SetParent(cardGo.transform, false);
            crestFrame.GetComponent<LayoutElement>().preferredHeight = compact ? 148f : 196f;
            ApplyReadablePanelStyle(crestFrame.GetComponent<Image>(), new Color(0.09f, 0.12f, 0.19f, 0.98f));

            var crestGo = new GameObject("Crest", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            crestGo.transform.SetParent(crestFrame.transform, false);
            ClassicRpgUiRuntime.Stretch(crestGo.GetComponent<RectTransform>(), new Vector2(22f, 16f), new Vector2(-22f, -16f));
            var crestImage = crestGo.GetComponent<Image>();
            crestImage.raycastTarget = false;
            crestImage.preserveAspect = true;
            crestImage.sprite = LoadDetailIcon("ClassicRpgIcons/Badge_Warrior");
            crestImage.color = crestImage.sprite != null ? new Color(0.68f, 0.63f, 0.58f, 0.86f) : new Color(1f, 1f, 1f, 0f);
            crestGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            crestGo.GetComponent<AspectRatioFitter>().aspectRatio = 1.28f;

            var title = MakeLabel(cardGo.transform, "Txt_Title", "Coming Soon", compact ? 22 : 26, new Color(0.92f, 0.89f, 0.84f), compact ? 34f : 40f);
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(title, compact ? 18f : 20f, compact ? 22f : 26f);

            var subtitle = MakeLabel(cardGo.transform, "Txt_Subtitle", $"Future faction slot {slotIndex}", compact ? 13 : 14, new Color(0.76f, 0.81f, 0.90f), 24f);
            subtitle.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(subtitle, 11f, compact ? 13f : 14f);

            var summary = MakeLabel(
                cardGo.transform,
                "Txt_Summary",
                "Reserved for a future kingdom with its own commander, progression tree, and battlefield roster.",
                compact ? 12 : 13,
                new Color(0.80f, 0.84f, 0.92f),
                compact ? 90f : 112f);
            summary.alignment = TextAlignmentOptions.TopLeft;
            SetResponsiveWrappedText(summary, compact ? 11f : 12f, compact ? 12f : 13f);

            var statusGo = new GameObject("StatusChip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            statusGo.transform.SetParent(cardGo.transform, false);
            statusGo.GetComponent<LayoutElement>().preferredHeight = 32f;
            ApplyReadablePanelStyle(statusGo.GetComponent<Image>(), new Color(0.16f, 0.18f, 0.24f, 0.98f));
            var status = CreateAnchoredText(statusGo.transform, "Txt_Status", "Awaiting Banner", 12, new Color(0.90f, 0.82f, 0.60f), Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            ApplyReadableTextStyle(status, new Color(0.90f, 0.82f, 0.60f), TextAlignmentOptions.Center, FontStyles.Bold);
            SetResponsiveSingleLine(status, 10f, 12f);
        }

        void BuildProgressionTree(Transform parent)
        {
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_panelRoot != null ? _panelRoot.GetComponent<RectTransform>() : null);
            bool scenicProgressionPage = UseScenicProgressionPresentation();
            var section = CreateSectionPanel(
                parent,
                "Section_Tree",
                scenicProgressionPage ? new Color(0.06f, 0.06f, 0.08f, 0.90f) : new Color(0.07f, 0.10f, 0.16f, 0.98f),
                0f,
                flexibleHeight: 1f);
            _treeSectionRoot = section;
            var sectionLayout = section.GetComponent<VerticalLayoutGroup>();
            if (sectionLayout != null && scenicProgressionPage)
            {
                sectionLayout.padding = compact ? new RectOffset(12, 12, 10, 10) : new RectOffset(16, 16, 12, 12);
                sectionLayout.spacing = compact ? 8f : 10f;
            }

            var treeHeader = MakeLabel(
                section.transform,
                "Txt_TreeHeader",
                "Upgrade Progression Tree",
                scenicProgressionPage ? 18 : 20,
                scenicProgressionPage ? new Color(0.94f, 0.91f, 0.84f, 1f) : Color.white,
                scenicProgressionPage ? 28f : 32f);
            treeHeader.fontStyle = FontStyles.Bold;
            treeHeader.alignment = TextAlignmentOptions.Center;

            var scrollGo = new GameObject("TreeScroll", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            scrollGo.transform.SetParent(section.transform, false);
            ApplyReadablePanelStyle(
                scrollGo.GetComponent<Image>(),
                scenicProgressionPage ? new Color(0.05f, 0.05f, 0.07f, 0.68f) : new Color(0.08f, 0.11f, 0.17f, 0.88f));
            var scrollLayout = scrollGo.GetComponent<LayoutElement>();
            scrollLayout.minHeight = 0f;
            scrollLayout.preferredHeight = 0f;
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.flexibleWidth = 1f;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = true;
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
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var contentLayoutElement = contentGo.GetComponent<LayoutElement>();
            contentLayoutElement.flexibleWidth = 0f;
            contentLayoutElement.flexibleHeight = 1f;

            var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = scenicProgressionPage ? 18f : 16f;
            contentLayout.padding = scenicProgressionPage ? new RectOffset(16, 16, 14, 14) : new RectOffset(12, 12, 12, 12);
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
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

        void BuildTreeTabBar(Transform parent, bool vertical = false)
        {
            var tabBar = new GameObject(
                "TreeTabBar",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement),
                vertical ? typeof(VerticalLayoutGroup) : typeof(HorizontalLayoutGroup));
            tabBar.transform.SetParent(parent, false);
            tabBar.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.20f, 0.98f);
            var layoutElement = tabBar.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = vertical ? 214f : 54f;
            layoutElement.flexibleWidth = 1f;

            var layout = vertical
                ? (HorizontalOrVerticalLayoutGroup)tabBar.GetComponent<VerticalLayoutGroup>()
                : tabBar.GetComponent<HorizontalLayoutGroup>();
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
            layoutElement.preferredHeight = 42f;
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
            ApplyReadablePanelStyle(laneGo.GetComponent<Image>(), new Color(0.08f, 0.11f, 0.18f, 0.97f));
            var laneLayoutElement = laneGo.GetComponent<LayoutElement>();
            laneLayoutElement.minWidth = CalculateLaneRowWidth(lane);
            laneLayoutElement.preferredWidth = CalculateLaneRowWidth(lane);
            laneLayoutElement.preferredHeight = ResolveLaneRowHeight(lane);
            laneLayoutElement.flexibleWidth = 0f;

            var layout = laneGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 12f;
            layout.padding = new RectOffset(16, 16, 16, 16);

            var headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            headerRow.transform.SetParent(laneGo.transform, false);
            var headerLayoutElement = headerRow.GetComponent<LayoutElement>();
            headerLayoutElement.preferredHeight = 30f;
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
            var summaryLayout = laneSummary.GetComponent<LayoutElement>();
            if (summaryLayout != null)
            {
                summaryLayout.flexibleWidth = 1f;
                summaryLayout.minWidth = 0f;
            }

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
            sectionLayoutElement.preferredHeight = UnitCardHeight + 86f;
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
            bool isHeroOutcome = unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome;
            var cardGo = new GameObject($"Unit_{unit.Id}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(CanvasGroup), typeof(Button));
            cardGo.transform.SetParent(parent, false);
            var layoutElement = cardGo.GetComponent<LayoutElement>();
            layoutElement.minWidth = 0f;
            layoutElement.preferredWidth = isHeroOutcome
                ? HeroOutcomeCardWidth
                : 0f;
            layoutElement.preferredHeight = UnitCardHeight;
            layoutElement.flexibleWidth = isHeroOutcome
                ? 0f
                : UnitCardFlexWidth;
            var background = cardGo.GetComponent<Image>();
            ApplyReadablePanelStyle(background, unlockedColor);
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
            layout.spacing = isHeroOutcome ? 6f : 8f;
            layout.padding = isHeroOutcome
                ? new RectOffset(12, 12, 12, 12)
                : new RectOffset(10, 10, 10, 10);

            var stateStrip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            stateStrip.transform.SetParent(cardGo.transform, false);
            stateStrip.GetComponent<LayoutElement>().preferredHeight = isHeroOutcome ? 26f : 28f;
            var stateBackground = stateStrip.GetComponent<Image>();
            ApplyReadablePanelStyle(
                stateBackground,
                isHeroOutcome
                    ? new Color(0.21f, 0.19f, 0.12f, 0.98f)
                    : new Color(0.16f, 0.22f, 0.32f, 0.96f));
            var state = CreateInlineText(stateStrip.transform, "Txt_State", "", 12f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = Vector2.zero;
            stateRect.anchorMax = Vector2.one;
            stateRect.offsetMin = new Vector2(8f, 3f);
            stateRect.offsetMax = new Vector2(-8f, -3f);
            SetResponsiveSingleLine(state, 12f, 12f);

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(cardGo.transform, false);
            ApplyReadablePanelStyle(
                portraitFrame.GetComponent<Image>(),
                isHeroOutcome
                    ? new Color(0.11f, 0.12f, 0.18f, 0.99f)
                    : new Color(0.10f, 0.14f, 0.22f, 0.98f));
            portraitFrame.GetComponent<LayoutElement>().preferredHeight = isHeroOutcome ? 122f : PortraitFrameHeight;

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

            if (isHeroOutcome)
                CreateHeroOutcomeBadge(cardGo.transform, unit);

            var name = MakeLabel(cardGo.transform, "Txt_Name", unit.DisplayName, 17, Color.white, 24f);
            name.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(name, 12f, 17f);

            var stats = MakeLabel(
                cardGo.transform,
                "Txt_Stats",
                BuildUnitCardStatsText(unit),
                13,
                isHeroOutcome ? ClassicRpgUiRuntime.WarmGold : new Color(0.86f, 0.88f, 0.92f),
                isHeroOutcome ? 24f : 28f);
            stats.alignment = TextAlignmentOptions.Center;
            stats.fontStyle = isHeroOutcome ? FontStyles.Bold : FontStyles.Normal;
            SetResponsiveSingleLine(stats, 11f, 13f);

            var laneHint = MakeLabel(
                cardGo.transform,
                "Txt_LaneHint",
                BuildUnitCardSubtitle(lane, unit),
                isHeroOutcome ? 11 : 12,
                isHeroOutcome ? new Color(0.86f, 0.90f, 0.96f) : new Color(0.70f, 0.78f, 0.90f),
                isHeroOutcome ? 34f : 24f);
            laneHint.alignment = TextAlignmentOptions.Center;
            if (isHeroOutcome)
                SetResponsiveWrappedText(laneHint, 10f, 11f);
            else
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
            BuildBuildingProgressCard(parent, lane, unit, BuildingTierCardWidth, BuildingCardHeight, 0f);
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
            float resolvedWidth = preferredWidth > 0f ? preferredWidth : BuildingTierCardWidth;
            layoutElement.minWidth = 0f;
            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredWidth = resolvedWidth;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleWidth = flexibleWidth;

            var background = cardGo.GetComponent<Image>();
            ApplyReadablePanelStyle(background, unlockedColor);
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
            layout.padding = new RectOffset(12, 12, 12, 12);

            var stateStrip = new GameObject("StateStrip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            stateStrip.transform.SetParent(cardGo.transform, false);
            var stateStripLayout = stateStrip.GetComponent<LayoutElement>();
            stateStripLayout.preferredHeight = 24f;
            var stateBackground = stateStrip.GetComponent<Image>();
            ApplyReadablePanelStyle(stateBackground, new Color(0.18f, 0.23f, 0.34f, 0.98f));
            var state = CreateInlineText(stateStrip.transform, "Txt_State", "", 12f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            var stateRect = state.rectTransform;
            stateRect.anchorMin = Vector2.zero;
            stateRect.anchorMax = Vector2.one;
            stateRect.offsetMin = new Vector2(6f, 2f);
            stateRect.offsetMax = new Vector2(-6f, -2f);
            SetResponsiveSingleLine(state, 12f, 12f);

            var imageFrame = new GameObject("ImageFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(RectMask2D));
            imageFrame.transform.SetParent(cardGo.transform, false);
            ApplyReadablePanelStyle(imageFrame.GetComponent<Image>(), new Color(0.09f, 0.13f, 0.20f, 0.98f));
            var imageLayout = imageFrame.GetComponent<LayoutElement>();
            imageLayout.preferredHeight = preferredHeight >= 260f ? 104f : 90f;

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

            var infoColumn = new GameObject("InfoColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
            infoColumn.transform.SetParent(cardGo.transform, false);
            var infoLayout = infoColumn.GetComponent<VerticalLayoutGroup>();
            infoLayout.childAlignment = TextAnchor.UpperCenter;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            infoLayout.spacing = 4f;

            var name = MakeLabel(infoColumn.transform, "Txt_Name", BuildBuildingCardTitle(unit), 17, Color.white, 36f);
            name.fontStyle = FontStyles.Bold;
            name.alignment = TextAlignmentOptions.Center;
            SetResponsiveWrappedText(name, 12f, 17f);

            var laneHint = MakeLabel(
                infoColumn.transform,
                "Txt_LaneHint",
                BuildUnitCardSubtitle(lane, unit),
                12,
                new Color(0.74f, 0.81f, 0.92f),
                18f);
            laneHint.alignment = TextAlignmentOptions.Center;
            SetResponsiveSingleLine(laneHint, 11f, 12f);

            var statsRow = new GameObject("StatsRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            statsRow.transform.SetParent(cardGo.transform, false);
            statsRow.GetComponent<LayoutElement>().preferredHeight = preferredHeight >= 260f ? 48f : 44f;
            var statsRowLayout = statsRow.GetComponent<HorizontalLayoutGroup>();
            statsRowLayout.childAlignment = TextAnchor.MiddleCenter;
            statsRowLayout.childControlWidth = true;
            statsRowLayout.childControlHeight = true;
            statsRowLayout.childForceExpandWidth = true;
            statsRowLayout.childForceExpandHeight = false;
            statsRowLayout.spacing = 6f;

            var tier = CreateBuildingStatRow(
                statsRow.transform,
                "Tier",
                BuildCompactBuildingTierValue(unit),
                new Color(0.82f, 0.87f, 0.95f));
            var time = CreateBuildingStatRow(
                statsRow.transform,
                "Build",
                BuildBuildingTimeValue(unit),
                new Color(0.72f, 0.80f, 0.91f));
            var cost = CreateBuildingStatRow(
                statsRow.transform,
                "Gold",
                BuildBuildingCostValue(unit),
                new Color(0.98f, 0.87f, 0.56f));
            var requirement = CreateBuildingStatRow(
                statsRow.transform,
                "Gate",
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
                Subtitle = laneHint,
                Requirement = requirement,
                Cost = cost,
                State = state,
                Icon = icon,
                IconFallback = iconFallback,
            };
        }

        TMP_Text CreateBuildingStatRow(Transform parent, string labelText, string valueText, Color valueColor)
        {
            var row = new GameObject($"Stat_{labelText}", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            row.transform.SetParent(parent, false);
            ApplyReadablePanelStyle(row.GetComponent<Image>(), new Color(0.11f, 0.15f, 0.24f, 0.98f));
            var rowLayoutElement = row.GetComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = 44f;
            rowLayoutElement.minHeight = 44f;
            rowLayoutElement.flexibleWidth = 1f;
            rowLayoutElement.minWidth = 0f;

            var rowLayout = row.GetComponent<VerticalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 0f;
            rowLayout.padding = new RectOffset(4, 4, 5, 4);

            var label = MakeLabel(row.transform, "Txt_Label", labelText.ToUpperInvariant(), 9, new Color(0.66f, 0.73f, 0.84f, 0.92f), 10f);
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(label, 8f, 9f);

            var value = MakeLabel(row.transform, "Txt_Value", valueText, 12, valueColor, 18f);
            value.alignment = TextAlignmentOptions.Center;
            value.fontStyle = FontStyles.Bold;
            SetResponsiveSingleLine(value, 10f, 12f);
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
            BuildDetailsPanelLayout(parent);
            return;

#if false
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
            modalRect.anchorMin = new Vector2(0.008f, 0.014f);
            modalRect.anchorMax = new Vector2(0.992f, 0.988f);
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
            modalLayout.spacing = 14f;
            modalLayout.padding = new RectOffset(24, 24, 20, 20);

            var modalFrameArt = new GameObject("ModalFrameArt", typeof(RectTransform), typeof(Image));
            modalFrameArt.transform.SetParent(modalGo.transform, false);
            var modalFrameRect = modalFrameArt.GetComponent<RectTransform>();
            modalFrameRect.anchorMin = Vector2.zero;
            modalFrameRect.anchorMax = Vector2.one;
            modalFrameRect.offsetMin = Vector2.zero;
            modalFrameRect.offsetMax = Vector2.zero;
            var modalFrameImage = modalFrameArt.GetComponent<Image>();
            modalFrameImage.raycastTarget = false;
            ApplyReadablePanelStyle(modalFrameImage, new Color(0.16f, 0.20f, 0.30f, 0.9f));

            var topRow = new GameObject("DetailsTopRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            topRow.transform.SetParent(modalGo.transform, false);
            var topRowLayoutElement = topRow.GetComponent<LayoutElement>();
            topRowLayoutElement.preferredHeight = 78f;
            var topRowLayout = topRow.GetComponent<HorizontalLayoutGroup>();
            topRowLayout.childAlignment = TextAnchor.MiddleCenter;
            topRowLayout.childControlWidth = true;
            topRowLayout.childControlHeight = true;
            topRowLayout.childForceExpandWidth = false;
            topRowLayout.childForceExpandHeight = true;
            topRowLayout.spacing = 14f;

            var titlePlate = new GameObject("TitlePlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            titlePlate.transform.SetParent(topRow.transform, false);
            var titlePlateLayoutElement = titlePlate.GetComponent<LayoutElement>();
            titlePlateLayoutElement.preferredHeight = 78f;
            titlePlateLayoutElement.flexibleWidth = 1f;
            var titlePlateLayout = titlePlate.GetComponent<HorizontalLayoutGroup>();
            titlePlateLayout.childAlignment = TextAnchor.MiddleCenter;
            titlePlateLayout.childControlWidth = true;
            titlePlateLayout.childControlHeight = true;
            titlePlateLayout.childForceExpandWidth = true;
            titlePlateLayout.childForceExpandHeight = true;
            titlePlateLayout.padding = new RectOffset(22, 22, 12, 14);
            var titlePlateImage = titlePlate.GetComponent<Image>();
            ApplyReadablePanelStyle(titlePlateImage, new Color(0.16f, 0.20f, 0.32f, 0.98f));

            _txtDetailsTitle = MakeLabel(titlePlate.transform, "Txt_Title", "", 36, new Color(0.96f, 0.84f, 0.50f, 1f), 54f);
            ApplyReadableTextStyle(_txtDetailsTitle, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            SetResponsiveSingleLine(_txtDetailsTitle, 24f, 36f);

            var btnDetailsClose = MakeButton(topRow.transform, "Btn_DetailsClose", "Close", 58f, new Color(0.20f, 0.28f, 0.40f, 1f));
            var closeLayout = btnDetailsClose.GetComponent<LayoutElement>();
            if (closeLayout != null)
                closeLayout.preferredWidth = 196f;
            var closeText = btnDetailsClose.GetComponentInChildren<TMP_Text>();
            if (closeText != null)
                ApplyReadableTextStyle(closeText, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            if (closeText != null)
                closeText.fontSize = 18f;
            btnDetailsClose.onClick.AddListener(CloseDetailsModal);

            var mainRow = new GameObject("DetailsMainRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            mainRow.transform.SetParent(modalGo.transform, false);
            var mainRowLayoutElement = mainRow.GetComponent<LayoutElement>();
            mainRowLayoutElement.flexibleHeight = 1f;
            mainRowLayoutElement.minHeight = 0f;
            var mainRowLayout = mainRow.GetComponent<HorizontalLayoutGroup>();
            mainRowLayout.childAlignment = TextAnchor.UpperLeft;
            mainRowLayout.childControlWidth = true;
            mainRowLayout.childControlHeight = true;
            mainRowLayout.childForceExpandWidth = false;
            mainRowLayout.childForceExpandHeight = true;
            mainRowLayout.spacing = 18f;

            var infoScrollGo = new GameObject("DetailsInfoScroll", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            infoScrollGo.transform.SetParent(mainRow.transform, false);
            var infoScrollLayout = infoScrollGo.GetComponent<LayoutElement>();
            infoScrollLayout.flexibleWidth = 1f;
            infoScrollLayout.flexibleHeight = 1f;
            infoScrollLayout.minWidth = 540f;
            infoScrollGo.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.13f, 0.94f);
            var infoScrollRect = infoScrollGo.GetComponent<ScrollRect>();
            infoScrollRect.horizontal = false;
            infoScrollRect.vertical = true;
            infoScrollRect.scrollSensitivity = 28f;
            infoScrollRect.movementType = ScrollRect.MovementType.Clamped;

            var infoViewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            infoViewportGo.transform.SetParent(infoScrollGo.transform, false);
            var infoViewportRect = infoViewportGo.GetComponent<RectTransform>();
            infoViewportRect.anchorMin = Vector2.zero;
            infoViewportRect.anchorMax = Vector2.one;
            infoViewportRect.offsetMin = Vector2.zero;
            infoViewportRect.offsetMax = Vector2.zero;
            infoViewportGo.GetComponent<Image>().color = Color.white;
            infoViewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var infoContentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            infoContentGo.transform.SetParent(infoViewportGo.transform, false);
            var infoContentRect = infoContentGo.GetComponent<RectTransform>();
            infoContentRect.anchorMin = new Vector2(0f, 1f);
            infoContentRect.anchorMax = new Vector2(1f, 1f);
            infoContentRect.pivot = new Vector2(0.5f, 1f);
            infoContentRect.offsetMin = new Vector2(16f, 0f);
            infoContentRect.offsetMax = new Vector2(-16f, 0f);
            var infoContentLayout = infoContentGo.GetComponent<VerticalLayoutGroup>();
            infoContentLayout.childAlignment = TextAnchor.UpperCenter;
            infoContentLayout.childControlWidth = true;
            infoContentLayout.childControlHeight = true;
            infoContentLayout.childForceExpandWidth = true;
            infoContentLayout.childForceExpandHeight = false;
            infoContentLayout.spacing = 14f;
            infoContentLayout.padding = new RectOffset(0, 0, 16, 16);
            infoContentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            infoContentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            infoScrollRect.viewport = infoViewportRect;
            infoScrollRect.content = infoContentRect;

            var showcaseColumn = new GameObject("ShowcaseColumn", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            showcaseColumn.transform.SetParent(mainRow.transform, false);
            var showcaseLayoutElement = showcaseColumn.GetComponent<LayoutElement>();
            showcaseLayoutElement.preferredWidth = 612f;
            showcaseLayoutElement.minWidth = 520f;
            showcaseLayoutElement.flexibleHeight = 1f;
            var showcaseLayout = showcaseColumn.GetComponent<VerticalLayoutGroup>();
            showcaseLayout.childAlignment = TextAnchor.UpperCenter;
            showcaseLayout.childControlWidth = true;
            showcaseLayout.childControlHeight = true;
            showcaseLayout.childForceExpandWidth = true;
            showcaseLayout.childForceExpandHeight = false;
            showcaseLayout.spacing = 14f;

            var stageCard = CreateDetailsModalCard(showcaseColumn.transform, "StageCard", new Color(0.08f, 0.11f, 0.18f, 0.97f), 0f);
            var stageLayout = stageCard.GetComponent<LayoutElement>();
            stageLayout.preferredHeight = 0f;
            stageLayout.flexibleHeight = 1f;

            var stageHeader = MakeLabel(stageCard.transform, "Txt_StageHeader", "Preview Stage", 19, new Color(0.96f, 0.84f, 0.50f, 1f), 30f);
            ApplyReadableTextStyle(stageHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(stageCard.transform, false);
            var portraitFrameImage = portraitFrame.GetComponent<Image>();
            ApplyReadablePanelStyle(portraitFrameImage, new Color(0.13f, 0.17f, 0.25f, 1f));
            var portraitLayout = portraitFrame.GetComponent<LayoutElement>();
            portraitLayout.preferredHeight = 438f;
            portraitLayout.flexibleHeight = 1f;

            var portraitInnerFrame = new GameObject("PortraitInnerFrame", typeof(RectTransform), typeof(Image));
            portraitInnerFrame.transform.SetParent(portraitFrame.transform, false);
            var innerRect = portraitInnerFrame.GetComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(18f, 18f);
            innerRect.offsetMax = new Vector2(-18f, -18f);
            var portraitInnerImage = portraitInnerFrame.GetComponent<Image>();
            ApplyReadablePanelStyle(portraitInnerImage, new Color(0.07f, 0.10f, 0.16f, 1f));

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(portraitInnerFrame.transform, false);
            var portraitRect = portraitGo.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(14f, 14f);
            portraitRect.offsetMax = new Vector2(-14f, -14f);
            _detailsPortrait = portraitGo.GetComponent<RawImage>();
            _detailsPortrait.color = new Color(1f, 1f, 1f, 0f);
            _detailsPortrait.raycastTarget = false;
            portraitGo.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitGo.GetComponent<AspectRatioFitter>().aspectRatio = 0.82f;

            var detailsIconGo = new GameObject("BuildingIcon", typeof(RectTransform), typeof(Image));
            detailsIconGo.transform.SetParent(portraitInnerFrame.transform, false);
            var detailsIconRect = detailsIconGo.GetComponent<RectTransform>();
            detailsIconRect.anchorMin = Vector2.zero;
            detailsIconRect.anchorMax = Vector2.one;
            detailsIconRect.offsetMin = new Vector2(22f, 22f);
            detailsIconRect.offsetMax = new Vector2(-22f, -22f);
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

            _txtDetailsPreviewStatus = MakeLabel(stageCard.transform, "Txt_PreviewStatus", "", 16, new Color(0.84f, 0.88f, 0.95f), 84f);
            ApplyReadableTextStyle(_txtDetailsPreviewStatus, new Color(0.84f, 0.88f, 0.95f), TextAlignmentOptions.Center);
            SetResponsiveWrappedText(_txtDetailsPreviewStatus, 13f, 16f);

            var previewGridCard = CreateDetailsModalCard(showcaseColumn.transform, "MotionCard", new Color(0.08f, 0.11f, 0.18f, 0.97f), 246f);
            var previewHeader = MakeLabel(previewGridCard.transform, "Txt_PreviewHeader", "Motion Reels", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(previewHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);

            var previewGrid = new GameObject("MotionPreviewGrid", typeof(RectTransform), typeof(LayoutElement), typeof(GridLayoutGroup));
            previewGrid.transform.SetParent(previewGridCard.transform, false);
            var previewGridLayout = previewGrid.GetComponent<LayoutElement>();
            previewGridLayout.preferredHeight = 176f;
            var previewGridGroup = previewGrid.GetComponent<GridLayoutGroup>();
            previewGridGroup.cellSize = new Vector2(134f, 54f);
            previewGridGroup.spacing = new Vector2(10f, 10f);
            previewGridGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            previewGridGroup.constraintCount = 4;
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

            var stateCard = CreateDetailsModalCard(infoContentGo.transform, "StateCard", new Color(0.09f, 0.12f, 0.19f, 0.96f), 112f);
            var stateHeader = MakeLabel(stateCard.transform, "Txt_StateHeader", "Battle Standing", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(stateHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            _txtDetailsState = MakeLabel(stateCard.transform, "Txt_State", "", 19, selectedColor, 56f);
            ApplyReadableTextStyle(_txtDetailsState, selectedColor, TextAlignmentOptions.Center, FontStyles.Bold);
            SetResponsiveWrappedText(_txtDetailsState, 13f, 19f);

            var statsCard = CreateDetailsModalCard(infoContentGo.transform, "StatsCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 230f);
            var statsHeader = MakeLabel(statsCard.transform, "Txt_StatsHeader", "War Ledger", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(statsHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            var txtDetailsStats = MakeLabel(statsCard.transform, "Txt_Stats", "", 16, new Color(0.88f, 0.90f, 0.96f), 182f);
            ApplyReadableTextStyle(txtDetailsStats, new Color(0.88f, 0.90f, 0.96f), TextAlignmentOptions.TopLeft);
            SetResponsiveWrappedText(txtDetailsStats, 13f, 16f);

            var requirementCard = CreateDetailsModalCard(infoContentGo.transform, "RequirementCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 134f);
            var requirementHeader = MakeLabel(requirementCard.transform, "Txt_RequirementHeader", "Unlock Decree", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(requirementHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            var txtDetailsRequirement = MakeLabel(requirementCard.transform, "Txt_Requirement", "", 15, new Color(0.82f, 0.86f, 0.92f), 102f);
            ApplyReadableTextStyle(txtDetailsRequirement, new Color(0.82f, 0.86f, 0.92f), TextAlignmentOptions.TopLeft);
            SetResponsiveWrappedText(txtDetailsRequirement, 13f, 15f);

            var movesCard = CreateDetailsModalCard(infoContentGo.transform, "MovesCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 204f);
            var movesHeader = MakeLabel(movesCard.transform, "Txt_MovesHeader", "Move Scroll", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(movesHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            var txtDetailsMoves = MakeLabel(movesCard.transform, "Txt_Moves", "", 15, new Color(0.84f, 0.88f, 0.95f), 160f);
            ApplyReadableTextStyle(txtDetailsMoves, new Color(0.84f, 0.88f, 0.95f), TextAlignmentOptions.TopLeft);
            SetResponsiveWrappedText(txtDetailsMoves, 13f, 15f);

            var customizationCard = CreateDetailsModalCard(infoContentGo.transform, "CustomizationCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 238f);
            var customizationHeader = MakeLabel(customizationCard.transform, "Txt_CustomizationHeader", "Armory & Vanity Shop", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(customizationHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);

            var tagRow = new GameObject("CustomizationTags", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            tagRow.transform.SetParent(customizationCard.transform, false);
            tagRow.GetComponent<LayoutElement>().preferredHeight = 40f;
            var tagLayout = tagRow.GetComponent<HorizontalLayoutGroup>();
            tagLayout.childAlignment = TextAnchor.MiddleCenter;
            tagLayout.childControlWidth = false;
            tagLayout.childControlHeight = true;
            tagLayout.childForceExpandWidth = false;
            tagLayout.childForceExpandHeight = false;
            tagLayout.spacing = 8f;
            CreateStoreBadge(tagRow.transform, "Abilities", new Color(0.27f, 0.35f, 0.54f, 0.98f));
            CreateStoreBadge(tagRow.transform, "Skins", new Color(0.44f, 0.27f, 0.51f, 0.98f));
            CreateStoreBadge(tagRow.transform, "Weapons", new Color(0.43f, 0.31f, 0.20f, 0.98f));
            CreateStoreBadge(tagRow.transform, "Victory", new Color(0.20f, 0.39f, 0.32f, 0.98f));
            CreateStoreBadge(tagRow.transform, "Position", new Color(0.36f, 0.25f, 0.25f, 0.98f));

            _txtDetailsCustomization = MakeLabel(customizationCard.transform, "Txt_Customization", "", 15, new Color(0.84f, 0.88f, 0.95f), 168f);
            ApplyReadableTextStyle(_txtDetailsCustomization, new Color(0.84f, 0.88f, 0.95f), TextAlignmentOptions.TopLeft);
            SetResponsiveWrappedText(_txtDetailsCustomization, 13f, 15f);

            var soundCard = CreateDetailsModalCard(infoContentGo.transform, "SoundCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 172f);
            var soundHeader = MakeLabel(soundCard.transform, "Txt_SoundHeader", "Sound Hall", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(soundHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);

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
            ApplyReadableTextStyle(_txtDetailsPreviewSfx, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            _btnDetailsPreviewSfx.onClick.AddListener(PreviewSelectedUnitSfx);

            _btnDetailsPreviewVoice = MakeButton(soundRow.transform, "Btn_PreviewVoice", "Voice Lines", 44f, new Color(0.19f, 0.24f, 0.34f, 1f));
            var voiceLayout = _btnDetailsPreviewVoice.GetComponent<LayoutElement>();
            if (voiceLayout != null)
                voiceLayout.preferredWidth = 184f;
            _txtDetailsPreviewVoice = _btnDetailsPreviewVoice.GetComponentInChildren<TMP_Text>();
            if (_txtDetailsPreviewVoice != null)
                _txtDetailsPreviewVoice.fontSize = 14f;
            ApplyReadableTextStyle(_txtDetailsPreviewVoice, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            _btnDetailsPreviewVoice.onClick.AddListener(PreviewSelectedUnitVoice);

            _txtDetailsAudioStatus = MakeLabel(soundCard.transform, "Txt_AudioStatus", "", 14, new Color(0.80f, 0.84f, 0.92f), 72f);
            ApplyReadableTextStyle(_txtDetailsAudioStatus, new Color(0.80f, 0.84f, 0.92f), TextAlignmentOptions.Center);
            SetResponsiveWrappedText(_txtDetailsAudioStatus, 12f, 14f);

            var bodyCard = CreateDetailsModalCard(infoContentGo.transform, "BodyCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 284f);
            var bodyHeader = MakeLabel(bodyCard.transform, "Txt_BodyHeader", "Chronicle", 18, new Color(0.96f, 0.84f, 0.50f, 1f), 28f);
            ApplyReadableTextStyle(bodyHeader, new Color(0.96f, 0.84f, 0.50f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            _txtDetailsBody = MakeLabel(bodyCard.transform, "Txt_Body", "", 15, new Color(0.78f, 0.82f, 0.90f), 240f);
            ApplyReadableTextStyle(_txtDetailsBody, new Color(0.78f, 0.82f, 0.90f), TextAlignmentOptions.TopLeft);
            SetResponsiveWrappedText(_txtDetailsBody, 13f, 15f);
#endif
        }

        void BuildDetailsPanelLayout(Transform parent)
        {
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_panelRoot != null ? _panelRoot.GetComponent<RectTransform>() : null);
            bool scenicProgressionPage = UseScenicProgressionPresentation();
            var section = CreateSectionPanel(
                parent,
                "Section_Details",
                scenicProgressionPage ? new Color(0.07f, 0.07f, 0.09f, 0.88f) : new Color(0.08f, 0.11f, 0.18f, 0.98f),
                compact ? 338f : 0f,
                flexibleHeight: compact ? 0f : 1f);
            _detailsOverlayRoot = section;
            var sectionLayout = section.GetComponent<VerticalLayoutGroup>();
            if (sectionLayout != null && scenicProgressionPage)
            {
                sectionLayout.padding = compact ? new RectOffset(10, 10, 10, 10) : new RectOffset(12, 12, 12, 12);
                sectionLayout.spacing = compact ? 8f : 10f;
            }

            var titlePlate = new GameObject("TitlePlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            titlePlate.transform.SetParent(section.transform, false);
            var titlePlateLayout = titlePlate.GetComponent<LayoutElement>();
            titlePlateLayout.preferredHeight = compact ? 74f : scenicProgressionPage ? 82f : 96f;
            ClassicRpgUiRuntime.ApplyPanel(
                titlePlate.GetComponent<Image>(),
                scenicProgressionPage ? ClassicRpgPanelSkin.TitleMini : ClassicRpgPanelSkin.TitleMedium,
                false,
                Color.white);

            var titleLayout = titlePlate.GetComponent<VerticalLayoutGroup>();
            titleLayout.childAlignment = TextAnchor.MiddleCenter;
            titleLayout.childControlWidth = true;
            titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = true;
            titleLayout.childForceExpandHeight = false;
            titleLayout.spacing = 2f;
            titleLayout.padding = new RectOffset(18, 18, compact ? 16 : 18, compact ? 12 : 14);

            var detailLabel = MakeLabel(titlePlate.transform, "Txt_DetailLabel", "Selected Upgrade", compact ? 11 : 12, ClassicRpgUiRuntime.MutedText, 18f);
            ApplyReadableTextStyle(detailLabel, ClassicRpgUiRuntime.MutedText, TextAlignmentOptions.Center);
            SetResponsiveSingleLine(detailLabel, 10f, compact ? 11f : 12f);

            _txtDetailsTitle = MakeLabel(titlePlate.transform, "Txt_Title", "", compact ? 18 : 20, ClassicRpgUiRuntime.WarmGold, 30f);
            ApplyPlateTitleStyle(_txtDetailsTitle, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center);
            SetResponsiveSingleLine(_txtDetailsTitle, 14f, compact ? 18f : 20f);

            var scrollGo = new GameObject("DetailsScroll", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(ScrollRect));
            scrollGo.transform.SetParent(section.transform, false);
            ClassicRpgUiRuntime.ApplyPanel(
                scrollGo.GetComponent<Image>(),
                ClassicRpgPanelSkin.PortraitBackdrop,
                true,
                scenicProgressionPage ? new Color(0.05f, 0.05f, 0.07f, 0.72f) : new Color(0.08f, 0.11f, 0.17f, 0.88f));
            var scrollLayout = scrollGo.GetComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.minHeight = compact ? 188f : 0f;

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
            viewportRect.offsetMin = new Vector2(8f, 8f);
            viewportRect.offsetMax = new Vector2(-8f, -8f);
            viewportGo.GetComponent<Image>().color = Color.white;
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.spacing = 10f;
            contentLayout.padding = new RectOffset(0, 0, 0, 4);
            contentGo.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            BuildDetailsPreviewCard(contentGo.transform, compact);
            _detailsStatsRowsRoot = CreateDetailsIconListCard(contentGo.transform, "StatsCard", "War Ledger", compact ? 170f : 182f, compact);
            _txtDetailsState = CreateDetailsTextCard(contentGo.transform, "StateCard", "Unlock State", compact ? 78f : 84f, compact ? 15 : 16, selectedColor, 32f, compact, TextAlignmentOptions.Center, FontStyles.Bold);
            _detailsRequirementRowsRoot = CreateDetailsIconListCard(contentGo.transform, "RequirementCard", "Unlock Requirements", compact ? 116f : 126f, compact);
            _detailsMovesRowsRoot = CreateDetailsIconListCard(contentGo.transform, "MovesCard", "Combat Readout", compact ? 146f : 156f, compact);
            _txtDetailsBody = CreateDetailsTextCard(contentGo.transform, "BodyCard", "Chronicle", compact ? 162f : 174f, compact ? 12 : 13, new Color(0.78f, 0.82f, 0.90f), compact ? 116f : 126f, compact);
            if (_txtDetailsBody != null)
            {
                _txtDetailsBody.lineSpacing = 8f;
                _txtDetailsBody.paragraphSpacing = 6f;
            }

            BuildDetailsSoundCard(contentGo.transform, compact);
        }

        void BuildDetailsPreviewCard(Transform parent, bool compact)
        {
            var previewCard = CreateDetailsModalCard(parent, "PreviewCard", new Color(0.08f, 0.11f, 0.18f, 0.97f), compact ? 204f : 238f);
            var previewHeader = MakeLabel(previewCard.transform, "Txt_PreviewHeader", "Preview", 16, ClassicRpgUiRuntime.WarmGold, 24f);
            ApplyReadableTextStyle(previewHeader, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var portraitFrame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            portraitFrame.transform.SetParent(previewCard.transform, false);
            ClassicRpgUiRuntime.ApplyPanel(portraitFrame.GetComponent<Image>(), ClassicRpgPanelSkin.PortraitFrame, true, Color.white);
            portraitFrame.GetComponent<LayoutElement>().preferredHeight = compact ? 122f : 144f;

            var portraitInnerFrame = new GameObject("PortraitInnerFrame", typeof(RectTransform), typeof(Image));
            portraitInnerFrame.transform.SetParent(portraitFrame.transform, false);
            ClassicRpgUiRuntime.Stretch(portraitInnerFrame.GetComponent<RectTransform>(), new Vector2(12f, 12f), new Vector2(-12f, -12f));
            ClassicRpgUiRuntime.ApplyPanel(portraitInnerFrame.GetComponent<Image>(), ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.08f, 0.11f, 0.18f, 0.98f));

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            portraitGo.transform.SetParent(portraitInnerFrame.transform, false);
            ClassicRpgUiRuntime.Stretch(portraitGo.GetComponent<RectTransform>(), new Vector2(12f, 12f), new Vector2(-12f, -12f));
            _detailsPortrait = portraitGo.GetComponent<RawImage>();
            _detailsPortrait.color = new Color(1f, 1f, 1f, 0f);
            _detailsPortrait.raycastTarget = false;
            var portraitFitter = portraitGo.GetComponent<AspectRatioFitter>();
            portraitFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            portraitFitter.aspectRatio = compact ? 1.08f : 0.92f;

            var detailsIconGo = new GameObject("BuildingIcon", typeof(RectTransform), typeof(Image));
            detailsIconGo.transform.SetParent(portraitInnerFrame.transform, false);
            ClassicRpgUiRuntime.Stretch(detailsIconGo.GetComponent<RectTransform>(), new Vector2(18f, 18f), new Vector2(-18f, -18f));
            _detailsBuildingIcon = detailsIconGo.GetComponent<Image>();
            _detailsBuildingIcon.preserveAspect = true;
            _detailsBuildingIcon.enabled = false;

            _detailsBuildingFallback = CreateInlineText(
                portraitInnerFrame.transform,
                "Txt_BuildingFallback",
                "",
                compact ? 30f : 36f,
                Color.white,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            var detailsFallbackRect = _detailsBuildingFallback.rectTransform;
            detailsFallbackRect.anchorMin = Vector2.zero;
            detailsFallbackRect.anchorMax = Vector2.one;
            detailsFallbackRect.offsetMin = Vector2.zero;
            detailsFallbackRect.offsetMax = Vector2.zero;
            _detailsBuildingFallback.gameObject.SetActive(false);

            _txtDetailsPreviewStatus = MakeLabel(previewCard.transform, "Txt_PreviewStatus", "", compact ? 12 : 13, new Color(0.84f, 0.88f, 0.95f), compact ? 34f : 40f);
            ApplyReadableTextStyle(_txtDetailsPreviewStatus, new Color(0.84f, 0.88f, 0.95f), TextAlignmentOptions.Center);
            SetResponsiveWrappedText(_txtDetailsPreviewStatus, 10f, compact ? 12f : 13f);
        }

        TMP_Text CreateDetailsTextCard(
            Transform parent,
            string cardName,
            string headerText,
            float preferredHeight,
            int fontSize,
            Color textColor,
            float bodyHeight,
            bool compact,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft,
            FontStyles fontStyle = FontStyles.Normal)
        {
            var card = CreateDetailsModalCard(parent, cardName, new Color(0.08f, 0.11f, 0.18f, 0.96f), preferredHeight);
            var header = MakeLabel(card.transform, "Txt_Header", headerText, 15, ClassicRpgUiRuntime.WarmGold, 22f);
            ApplyReadableTextStyle(header, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var body = MakeLabel(card.transform, "Txt_Body", "", fontSize, textColor, bodyHeight);
            var bodyLayout = body.GetComponent<LayoutElement>();
            if (bodyLayout != null)
            {
                bodyLayout.minHeight = bodyHeight;
                bodyLayout.preferredHeight = -1f;
                bodyLayout.flexibleHeight = 0f;
            }
            var bodyFitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ApplyReadableTextStyle(body, textColor, alignment, fontStyle);
            SetResponsiveWrappedText(body, Mathf.Max(10f, fontSize - 2f), compact ? fontSize : fontSize + 1f);
            return body;
        }

        Transform CreateDetailsIconListCard(Transform parent, string cardName, string headerText, float preferredHeight, bool compact)
        {
            var card = CreateDetailsModalCard(parent, cardName, new Color(0.08f, 0.11f, 0.18f, 0.96f), preferredHeight);
            var header = MakeLabel(card.transform, "Txt_Header", headerText, 15, ClassicRpgUiRuntime.WarmGold, 22f);
            ApplyReadableTextStyle(header, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var rowsRoot = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rowsRoot.transform.SetParent(card.transform, false);
            var rowsLayout = rowsRoot.GetComponent<VerticalLayoutGroup>();
            rowsLayout.childAlignment = TextAnchor.UpperLeft;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.spacing = compact ? 6f : 8f;
            var rowsFitter = rowsRoot.GetComponent<ContentSizeFitter>();
            rowsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rowsRoot.transform;
        }

        static DetailRowData[] BuildPlaceholderDetailRows(params DetailRowData[] rows)
        {
            return rows ?? Array.Empty<DetailRowData>();
        }

        void PopulateDetailRows(Transform root, IReadOnlyList<DetailRowData> rows)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);

            if (rows == null || rows.Count == 0)
                return;

            for (int i = 0; i < rows.Count; i++)
                CreateDetailRow(root, rows[i]);
        }

        void CreateDetailRow(Transform parent, DetailRowData rowData)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            row.transform.SetParent(parent, false);
            ApplyReadablePanelStyle(row.GetComponent<Image>(), new Color(0.10f, 0.14f, 0.22f, 0.96f));
            var rowLayoutElement = row.GetComponent<LayoutElement>();
            bool hideLabel = string.IsNullOrWhiteSpace(rowData.Label);
            rowLayoutElement.minHeight = hideLabel ? 38f : 48f;
            rowLayoutElement.preferredHeight = -1f;
            var rowFitter = row.GetComponent<ContentSizeFitter>();
            rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 8f;
            rowLayout.padding = new RectOffset(10, 10, 5, 5);

            var iconFrame = new GameObject("IconFrame", typeof(RectTransform), typeof(LayoutElement));
            iconFrame.transform.SetParent(row.transform, false);
            var iconFrameLayout = iconFrame.GetComponent<LayoutElement>();
            iconFrameLayout.preferredWidth = 28f;
            iconFrameLayout.preferredHeight = 28f;
            iconFrameLayout.minWidth = 28f;
            iconFrameLayout.minHeight = 28f;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(iconFrame.transform, false);
            ClassicRpgUiRuntime.Stretch(iconGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.sprite = LoadDetailIcon(rowData.IconResourcePath);
            icon.color = icon.sprite != null ? rowData.IconColor : new Color(1f, 1f, 1f, 0f);

            var textColumn = new GameObject("TextColumn", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            textColumn.transform.SetParent(row.transform, false);
            var textLayoutElement = textColumn.GetComponent<LayoutElement>();
            textLayoutElement.flexibleWidth = 1f;
            textLayoutElement.minWidth = 0f;
            var textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;
            textLayout.spacing = 2f;

            if (!hideLabel)
            {
                var label = MakeLabel(textColumn.transform, "Txt_Label", rowData.Label, 11, new Color(0.70f, 0.76f, 0.86f, 0.92f), 12f);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontStyle = FontStyles.Bold;
                SetResponsiveSingleLine(label, 10f, 11f);
            }

            var value = MakeLabel(textColumn.transform, "Txt_Value", rowData.Value, hideLabel ? 13 : 14, rowData.ValueColor, hideLabel ? 24f : 22f);
            var valueLayout = value.GetComponent<LayoutElement>();
            if (valueLayout != null)
            {
                valueLayout.minHeight = hideLabel ? 24f : 22f;
                valueLayout.preferredHeight = -1f;
                valueLayout.flexibleHeight = 0f;
            }
            var valueFitter = value.GetComponent<ContentSizeFitter>() ?? value.gameObject.AddComponent<ContentSizeFitter>();
            valueFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            valueFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            value.alignment = hideLabel ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.TopLeft;
            value.fontStyle = hideLabel ? FontStyles.Bold : FontStyles.Normal;
            value.lineSpacing = 3f;
            SetResponsiveWrappedText(value, 11f, hideLabel ? 13f : 14f);
        }

        Sprite LoadDetailIcon(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            if (_detailIconCache.TryGetValue(resourcePath, out var cached))
                return cached;

            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
            {
                var texture = Resources.Load<Texture2D>(resourcePath);
                if (texture != null)
                {
                    sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    sprite.name = $"{texture.name}_RuntimeSprite";
                }
            }
            _detailIconCache[resourcePath] = sprite;
            return sprite;
        }

        static string NormalizeDossierValue(string value, string fallback = "--")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        static string BuildRaceCardSummary(RaceProgressionDefinition race)
        {
            string summary = NormalizeDossierValue(race?.Summary, "Faction dossier pending.");
            const int maxLength = 124;
            if (summary.Length <= maxLength)
                return summary;

            int cutoff = summary.LastIndexOf(' ', maxLength);
            if (cutoff < 72)
                cutoff = maxLength;

            return $"{summary.Substring(0, cutoff).TrimEnd('.', ',', ';', ':')}...";
        }

        DetailRowData[] BuildWarLedgerRows(RaceProgressionUnitDefinition unit)
        {
            Color statIconColor = new Color(0.90f, 0.24f, 0.20f, 1f);
            if (unit == null)
            {
                return BuildPlaceholderDetailRows(
                    new DetailRowData("ClassicRpgIcons/Icon_Information", string.Empty, "Choose a node to reveal its ledger.", new Color(0.88f, 0.90f, 0.96f, 1f)));
            }

            if (unit.CardDisplay != null)
            {
                return new[]
                {
                    new DetailRowData("ClassicRpgIcons/Icon_Upgrade", string.Empty, NormalizeDossierValue(BuildCompactBuildingTierValue(unit)), new Color(0.88f, 0.90f, 0.96f, 1f)),
                    new DetailRowData("ClassicRpgIcons/Icon_Craft", string.Empty, NormalizeDossierValue(BuildBuildingTimeValue(unit)), new Color(0.82f, 0.86f, 0.92f, 1f)),
                    new DetailRowData("ClassicRpgIcons/Icon_Reward", string.Empty, NormalizeDossierValue(BuildBuildingCostValue(unit)), new Color(0.98f, 0.87f, 0.56f, 1f)),
                    new DetailRowData("ClassicRpgIcons/Icon_Castle", string.Empty, NormalizeDossierValue(BuildBuildingRequirementValue(_selectedRace != null && _selectedRace.TryGetLane(unit.LaneId, out var lane) ? lane : null, unit)), new Color(0.82f, 0.86f, 0.92f, 1f)),
                };
            }

            if (!TryGetCatalogEntry(unit, out var catalog))
            {
                string fallbackSignature = NormalizeDossierValue(
                    BuildCompactSpecialTag(unit),
                    NormalizeDossierValue(unit.StatsSummary, "Field report pending."));
                return new[]
                {
                    new DetailRowData("ClassicRpgIcons/Badge_Warrior", string.Empty, NormalizeDossierValue(BuildCompactPositionTag(unit) ?? BuildPositionDetailText(unit)), new Color(0.88f, 0.90f, 0.96f, 1f), statIconColor),
                    new DetailRowData("ClassicRpgIcons/Icon_Character", string.Empty, BuildUnitRoleLabel(unit), new Color(0.82f, 0.86f, 0.92f, 1f)),
                    new DetailRowData("ClassicRpgIcons/Stat_Guard", string.Empty, fallbackSignature, new Color(0.84f, 0.88f, 0.95f, 1f), statIconColor),
                };
            }

            string rankValue = NormalizeDossierValue(BuildCompactPositionTag(unit) ?? BuildPositionDetailText(unit));
            string attackValue = $"{FormatStatNumber(catalog.attack_damage)} dmg at {Mathf.Max(0.01f, catalog.attack_speed):0.##}/s | {FormatStatNumber(catalog.range)} rng";
            string vitalityValue = $"{FormatStatNumber(catalog.hp)} HP | {HumanizeLabel(catalog.armor_type)} {Mathf.Max(0f, catalog.damage_reduction_pct):0.#}%";

            return new[]
            {
                new DetailRowData("ClassicRpgIcons/Badge_Warrior", string.Empty, rankValue, new Color(0.88f, 0.90f, 0.96f, 1f), statIconColor),
                new DetailRowData("ClassicRpgIcons/Stat_Attack", string.Empty, attackValue, new Color(0.92f, 0.88f, 0.82f, 1f), statIconColor),
                new DetailRowData("ClassicRpgIcons/Icon_Armor", string.Empty, vitalityValue, new Color(0.84f, 0.88f, 0.95f, 1f), statIconColor),
            };
        }

        DetailRowData[] BuildRequirementRows(RaceProgressionUnitDefinition unit)
        {
            string gateValue = "Start unit";
            if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.UpgradeStep)
                gateValue = unit.IsStartUnit
                    ? "Starting civic tier"
                    : NormalizeDossierValue(unit.UnlockRequirement?.Label, unit.UnlockRequirement == null ? "Unknown gate" : $"{unit.UnlockRequirement.BuildingName} T{unit.UnlockRequirement.RequiredTier}");
            else if (unit != null && unit.CardStyle == RaceProgressionUnitCardStyle.BuildingTier)
                gateValue = BuildBuildingRequirementValue(_selectedRace != null && _selectedRace.TryGetLane(unit.LaneId, out var lane) ? lane : null, unit);
            else if (unit != null && !unit.IsStartUnit)
                gateValue = unit.UnlockRequirement == null ? "Unknown gate" : $"{unit.UnlockRequirement.BuildingName} T{unit.UnlockRequirement.RequiredTier}";

            return new[]
            {
                new DetailRowData("ClassicRpgIcons/Icon_Castle", string.Empty, gateValue, new Color(0.88f, 0.90f, 0.96f, 1f)),
                new DetailRowData(
                    unit != null && unit.CardDisplay != null ? "ClassicRpgIcons/Icon_Upgrade" : "ClassicRpgIcons/Badge_Warrior",
                    string.Empty,
                    unit != null && unit.CardDisplay != null
                        ? HumanizeLabel(unit.LaneId)
                        : NormalizeDossierValue(BuildCompactPositionTag(unit) ?? BuildPositionDetailText(unit)),
                    new Color(0.82f, 0.86f, 0.92f, 1f)),
                new DetailRowData("ClassicRpgIcons/Icon_Character", string.Empty, BuildRequirementSourceText(unit), new Color(0.82f, 0.86f, 0.92f, 1f)),
            };
        }

        DetailRowData[] BuildCombatRows(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
            {
                return BuildPlaceholderDetailRows(
                    new DetailRowData("ClassicRpgIcons/Icon_Battle", string.Empty, "Choose a node to reveal its battlefield role.", new Color(0.84f, 0.88f, 0.95f, 1f)));
            }

            string primaryValue = unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep
                ? "Requirement marker only"
                : unit.CardDisplay != null
                    ? BuildBuildingMoveSetSummary(unit)
                    : BuildUnitSpecialAttackText(unit);
            string stanceValue = unit.CardDisplay != null ? "Structure or civic unlock rather than a combat role." : BuildMoveStanceText(unit);
            string deliveryValue = TryGetCatalogEntry(unit, out var catalog)
                ? BuildUnitDeliveryText(unit, catalog)
                : unit.CardDisplay != null
                    ? "Unlocks new battlefield tools rather than a live attack."
                    : "Catalog-backed attack delivery data is not available.";
            string previewValue = BuildPreviewSummaryText(unit, catalog);

            return new[]
            {
                new DetailRowData("ClassicRpgIcons/Stat_Attack", string.Empty, primaryValue, new Color(0.88f, 0.90f, 0.96f, 1f), new Color(0.90f, 0.24f, 0.20f, 1f)),
                new DetailRowData("ClassicRpgIcons/Badge_Warrior", string.Empty, stanceValue, new Color(0.84f, 0.88f, 0.95f, 1f), new Color(0.90f, 0.24f, 0.20f, 1f)),
                new DetailRowData("ClassicRpgIcons/Stat_Guard", string.Empty, deliveryValue, new Color(0.84f, 0.88f, 0.95f, 1f), new Color(0.90f, 0.24f, 0.20f, 1f)),
                new DetailRowData("ClassicRpgIcons/Icon_Information", string.Empty, previewValue, new Color(0.80f, 0.84f, 0.92f, 1f)),
            };
        }

        string BuildRequirementSourceText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Unavailable";

            if (unit.CardDisplay != null)
                return $"{HumanizeLabel(unit.LaneId)} branch";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return "Gate node";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
                return "Castle outcome row";

            return BuildCurrentSourceText(TryGetCatalogEntry(unit, out var catalog) ? catalog : null);
        }

        static string BuildBuildingMoveSetSummary(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "ballista" => "Heavy siege bolt volley",
                "cannon" => "Explosive artillery blast",
                _ => "Fortress upgrade or structure unlock",
            };
        }

        string BuildPreviewSummaryText(RaceProgressionUnitDefinition unit, UnitCatalogEntry catalog)
        {
            if (unit == null)
                return "Preview unavailable.";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return "Gate nodes do not have combat previews.";

            if (unit.CardDisplay != null)
                return TryResolvePreviewSfx(unit, out _, out var buildingSfxLabel)
                    ? $"{buildingSfxLabel} SFX is ready in the preview hall."
                    : "Structure audio appears here when it is wired.";

            if (IsEconomyUnit(unit))
                return "Route audio is ready below. Combat reels are not used for market runners.";

            if (IsStableDisplayOnlyUnit(unit))
                return "Display branch only until mounted combat is live.";

            return TryResolvePreviewSfx(unit, out _, out var sfxLabel)
                ? $"Motion reels are ready, and {sfxLabel} audio is wired."
                : "Motion reels are ready. Dedicated audio is still pending.";
        }

        void BuildDetailsCustomizationCard(Transform parent, bool compact)
        {
            var customizationCard = CreateDetailsModalCard(parent, "CustomizationCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), 188f);
            var customizationHeader = MakeLabel(customizationCard.transform, "Txt_CustomizationHeader", "Armory & Vanity", 15, ClassicRpgUiRuntime.WarmGold, 22f);
            ApplyReadableTextStyle(customizationHeader, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var tagRow = new GameObject("CustomizationTags", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            tagRow.transform.SetParent(customizationCard.transform, false);
            tagRow.GetComponent<LayoutElement>().preferredHeight = 34f;
            var tagLayout = tagRow.GetComponent<HorizontalLayoutGroup>();
            tagLayout.childAlignment = TextAnchor.MiddleCenter;
            tagLayout.childControlWidth = false;
            tagLayout.childControlHeight = true;
            tagLayout.childForceExpandWidth = false;
            tagLayout.childForceExpandHeight = false;
            tagLayout.spacing = 6f;
            CreateStoreBadge(tagRow.transform, "Skins", new Color(0.44f, 0.27f, 0.51f, 0.98f));
            CreateStoreBadge(tagRow.transform, "Weapons", new Color(0.43f, 0.31f, 0.20f, 0.98f));
            CreateStoreBadge(tagRow.transform, "Victory", new Color(0.20f, 0.39f, 0.32f, 0.98f));

            _txtDetailsCustomization = MakeLabel(customizationCard.transform, "Txt_Customization", "", compact ? 12 : 13, new Color(0.84f, 0.88f, 0.95f), 98f);
            ApplyReadableTextStyle(_txtDetailsCustomization, new Color(0.84f, 0.88f, 0.95f), TextAlignmentOptions.TopLeft);
            SetResponsiveWrappedText(_txtDetailsCustomization, 10f, compact ? 12f : 13f);
        }

        void BuildDetailsSoundCard(Transform parent, bool compact)
        {
            var soundCard = CreateDetailsModalCard(parent, "SoundCard", new Color(0.08f, 0.11f, 0.18f, 0.96f), compact ? 186f : 172f);
            var soundHeader = MakeLabel(soundCard.transform, "Txt_SoundHeader", "Sound Hall", 15, ClassicRpgUiRuntime.WarmGold, 22f);
            ApplyReadableTextStyle(soundHeader, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var soundRow = new GameObject("SoundPreviewStack", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            soundRow.transform.SetParent(soundCard.transform, false);
            soundRow.GetComponent<LayoutElement>().preferredHeight = compact ? 96f : 88f;
            var soundRowLayout = soundRow.GetComponent<VerticalLayoutGroup>();
            soundRowLayout.childAlignment = TextAnchor.UpperCenter;
            soundRowLayout.childControlWidth = true;
            soundRowLayout.childControlHeight = true;
            soundRowLayout.childForceExpandWidth = true;
            soundRowLayout.childForceExpandHeight = false;
            soundRowLayout.spacing = 8f;

            _btnDetailsPreviewSfx = MakeButton(soundRow.transform, "Btn_PreviewSfx", "Preview SFX", 42f, new Color(0.24f, 0.33f, 0.49f, 1f));
            if (_btnDetailsPreviewSfx.TryGetComponent(out LayoutElement sfxLayout))
                sfxLayout.preferredHeight = 42f;
            _txtDetailsPreviewSfx = _btnDetailsPreviewSfx.GetComponentInChildren<TMP_Text>();
            if (_txtDetailsPreviewSfx != null)
                _txtDetailsPreviewSfx.fontSize = compact ? 12f : 13f;
            ApplyReadableTextStyle(_txtDetailsPreviewSfx, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            _btnDetailsPreviewSfx.onClick.AddListener(PreviewSelectedUnitSfx);

            _btnDetailsPreviewVoice = MakeButton(soundRow.transform, "Btn_PreviewVoice", "Voice Lines", 42f, new Color(0.19f, 0.24f, 0.34f, 1f));
            if (_btnDetailsPreviewVoice.TryGetComponent(out LayoutElement voiceLayout))
                voiceLayout.preferredHeight = 42f;
            _txtDetailsPreviewVoice = _btnDetailsPreviewVoice.GetComponentInChildren<TMP_Text>();
            if (_txtDetailsPreviewVoice != null)
                _txtDetailsPreviewVoice.fontSize = compact ? 12f : 13f;
            ApplyReadableTextStyle(_txtDetailsPreviewVoice, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            _btnDetailsPreviewVoice.onClick.AddListener(PreviewSelectedUnitVoice);

            _txtDetailsAudioStatus = MakeLabel(soundCard.transform, "Txt_AudioStatus", "", compact ? 11 : 12, new Color(0.80f, 0.84f, 0.92f), compact ? 64f : 46f);
            ApplyReadableTextStyle(_txtDetailsAudioStatus, new Color(0.80f, 0.84f, 0.92f), TextAlignmentOptions.Center);
            SetResponsiveWrappedText(_txtDetailsAudioStatus, 10f, compact ? 11f : 12f);
        }

        void BuildDetailsMotionCard(Transform parent, bool compact)
        {
            var previewGridCard = CreateDetailsModalCard(parent, "MotionCard", new Color(0.08f, 0.11f, 0.18f, 0.97f), 196f);
            var previewGridHeader = MakeLabel(previewGridCard.transform, "Txt_MotionHeader", "Motion Reels", 15, ClassicRpgUiRuntime.WarmGold, 22f);
            ApplyReadableTextStyle(previewGridHeader, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);

            var previewGrid = new GameObject("MotionPreviewGrid", typeof(RectTransform), typeof(LayoutElement), typeof(GridLayoutGroup));
            previewGrid.transform.SetParent(previewGridCard.transform, false);
            previewGrid.GetComponent<LayoutElement>().preferredHeight = 146f;
            var previewGridGroup = previewGrid.GetComponent<GridLayoutGroup>();
            previewGridGroup.cellSize = compact ? new Vector2(118f, 40f) : new Vector2(136f, 44f);
            previewGridGroup.spacing = new Vector2(8f, 8f);
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
            _btnPreviewSpecial = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewSpecial", "Special", PreviewSpecialMotion, out _txtPreviewSpecial);
            _btnPreviewHit = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewHit", "Hit React", PreviewHitMotion, out _txtPreviewHit);
            _btnPreviewDeath = CreateMotionPreviewButton(previewGrid.transform, "Btn_PreviewDeath", "Death", PreviewDeathMotion, out _txtPreviewDeath);
        }

        GameObject CreateDetailsModalCard(Transform parent, string name, Color color, float preferredHeight)
        {
            var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            card.transform.SetParent(parent, false);
            var image = card.GetComponent<Image>();
            ApplyReadablePanelStyle(image, color);
            var layoutElement = card.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.minHeight = preferredHeight > 0f ? preferredHeight : 0f;
            layoutElement.preferredHeight = -1f;
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(18, 18, 16, 16);
            var fitter = card.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return card;
        }

        Button CreateMotionPreviewButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick, out TMP_Text text)
        {
            var button = MakeButton(parent, name, label, 54f, new Color(0.19f, 0.26f, 0.38f, 1f));
            text = button.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.fontSize = 15f;
                ApplyReadableTextStyle(text, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            }
            button.onClick.AddListener(onClick);
            return button;
        }

        void CreateStoreBadge(Transform parent, string label, Color color)
        {
            var badge = new GameObject($"Badge_{label}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            badge.transform.SetParent(parent, false);
            var badgeImage = badge.GetComponent<Image>();
            ApplyReadablePanelStyle(badgeImage, color);
            var badgeLayout = badge.GetComponent<LayoutElement>();
            badgeLayout.preferredWidth = 96f;
            badgeLayout.preferredHeight = 32f;

            var badgeLabel = CreateAnchoredText(
                badge.transform,
                "Txt_Badge",
                label,
                12,
                Color.white,
                Vector2.zero,
                Vector2.one,
                new Vector2(6f, 2f),
                new Vector2(-6f, -2f));
            ApplyReadableTextStyle(badgeLabel, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            SetResponsiveSingleLine(badgeLabel, 10f, 12f);
        }

        void CreateHeroOutcomeBadge(Transform parent, RaceProgressionUnitDefinition unit)
        {
            var badgeRow = new GameObject("HeroBadgeRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            badgeRow.transform.SetParent(parent, false);
            var badgeRowLayoutElement = badgeRow.GetComponent<LayoutElement>();
            badgeRowLayoutElement.preferredHeight = 30f;

            var badgeRowLayout = badgeRow.GetComponent<HorizontalLayoutGroup>();
            badgeRowLayout.childAlignment = TextAnchor.MiddleCenter;
            badgeRowLayout.childControlWidth = false;
            badgeRowLayout.childControlHeight = true;
            badgeRowLayout.childForceExpandWidth = false;
            badgeRowLayout.childForceExpandHeight = false;
            badgeRowLayout.spacing = 0f;
            badgeRowLayout.padding = new RectOffset(0, 0, 0, 0);

            var badge = new GameObject("HeroBadge", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            badge.transform.SetParent(badgeRow.transform, false);
            ApplyReadablePanelStyle(badge.GetComponent<Image>(), new Color(0.18f, 0.20f, 0.31f, 0.98f));
            var badgeLayoutElement = badge.GetComponent<LayoutElement>();
            badgeLayoutElement.preferredWidth = 138f;
            badgeLayoutElement.preferredHeight = 28f;

            var badgeLayout = badge.GetComponent<HorizontalLayoutGroup>();
            badgeLayout.childAlignment = TextAnchor.MiddleCenter;
            badgeLayout.childControlWidth = false;
            badgeLayout.childControlHeight = true;
            badgeLayout.childForceExpandWidth = false;
            badgeLayout.childForceExpandHeight = false;
            badgeLayout.spacing = 6f;
            badgeLayout.padding = new RectOffset(10, 10, 4, 4);

            var iconGo = new GameObject("BadgeIcon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(badge.transform, false);
            var iconLayout = iconGo.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = 16f;
            iconLayout.preferredHeight = 16f;
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.sprite = LoadDetailIcon("ClassicRpgIcons/Icon_Castle");
            icon.color = icon.sprite != null ? ClassicRpgUiRuntime.WarmGold : new Color(1f, 1f, 1f, 0f);

            var badgeLabel = MakeLabel(badge.transform, "Txt_HeroBadge", BuildHeroOutcomeBadgeText(unit), 11, ClassicRpgUiRuntime.WarmGold, 18f);
            ApplyReadableTextStyle(badgeLabel, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center, FontStyles.Bold);
            SetResponsiveSingleLine(badgeLabel, 10f, 11f);
        }

        static void ApplyReadableTextStyle(TMP_Text label, Color color, TextAlignmentOptions alignment, FontStyles fontStyle = FontStyles.Normal)
        {
            if (label == null)
                return;

            ClassicRpgUiRuntime.ApplyTextStyle(
                label,
                ClassicRpgTextStyle.Body,
                alignment,
                color);
            label.fontStyle = fontStyle;
            label.outlineWidth = 0f;
        }

        static void ApplyPlateTitleStyle(TMP_Text label, Color color, TextAlignmentOptions alignment)
        {
            if (label == null)
                return;

            ClassicRpgUiRuntime.ApplyTextStyle(
                label,
                ClassicRpgTextStyle.Body,
                alignment,
                color,
                allowWrap: false);
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0f;
        }

        static void ApplyReadablePanelStyle(Image image, Color color)
        {
            if (image == null)
                return;

            ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PaperMedium, true, color);
        }

        void BuildActionRow(Transform parent)
        {
            bool premiumShellPresentation = UsePremiumShellPresentation();
            bool compact = ClassicRpgUiRuntime.IsCompactLayout(_panelRoot != null ? _panelRoot.GetComponent<RectTransform>() : null);
            var row = new GameObject("ActionRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = premiumShellPresentation ? 56f : 48f;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = premiumShellPresentation ? 16f : 10f;

            _btnSecondaryAction = MakeButton(row.transform, "Btn_Secondary", "Back", 46f, new Color(0.20f, 0.26f, 0.38f, 1f));
            _txtSecondaryAction = _btnSecondaryAction.GetComponentInChildren<TMP_Text>();
            if (premiumShellPresentation)
                ApplyLobbyButtonStyle(_btnSecondaryAction, ClassicRpgButtonSkin.MiniBrown, 44f, compact ? 132f : 156f);
            else
            {
                ApplyClassicRpgButtonTheme(_btnSecondaryAction, ClassicRpgButtonSize.Medium);
                ApplyClassicRpgLabelTheme(_txtSecondaryAction, false, true);
            }
            _btnSecondaryAction.onClick.AddListener(HandleSecondaryAction);
            var secondaryLayout = _btnSecondaryAction.GetComponent<LayoutElement>();
            if (secondaryLayout != null && premiumShellPresentation)
                secondaryLayout.preferredWidth = compact ? 132f : 156f;

            _btnPrimaryAction = MakeButton(row.transform, "Btn_Primary", "Continue", 46f, new Color(0.20f, 0.58f, 0.30f, 1f));
            _txtPrimaryAction = _btnPrimaryAction.GetComponentInChildren<TMP_Text>();
            if (premiumShellPresentation)
                ApplyLobbyButtonStyle(_btnPrimaryAction, ClassicRpgButtonSkin.MiniGold, 44f, compact ? 156f : 186f);
            else
            {
                ApplyClassicRpgButtonTheme(_btnPrimaryAction, ClassicRpgButtonSize.Long);
                ApplyClassicRpgLabelTheme(_txtPrimaryAction, false, true);
            }
            _btnPrimaryAction.onClick.AddListener(HandlePrimaryAction);
            var primaryLayout = _btnPrimaryAction.GetComponent<LayoutElement>();
            if (primaryLayout != null && premiumShellPresentation)
                primaryLayout.preferredWidth = compact ? 156f : 186f;
            RefreshPrimaryAction();
        }

        void ApplyLobbyButtonStyle(Button button, ClassicRpgButtonSkin skin, float height, float preferredWidth = 0f)
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
                label.fontSize = height >= 54f ? 24f : height >= 46f ? 19f : 17f;
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

        bool UsePremiumShellPresentation()
        {
            return _activePage == WizardPage.RaceSelection || UseScenicProgressionPresentation();
        }

        bool UseScenicProgressionPresentation()
        {
            return _activePage == WizardPage.ProgressionTree || _activePage == WizardPage.UnitDetails;
        }

        void BuildPremiumPageHeader(Transform parent, bool compact)
        {
            var header = new GameObject("HeaderPlate", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            header.transform.SetParent(parent, false);
            var headerLayout = header.GetComponent<LayoutElement>();
            headerLayout.preferredHeight = _mode == ProgressionViewerMode.PreMatchConfirm
                ? (compact ? 146f : 176f)
                : (compact ? 116f : 142f);

            var layout = header.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 4f : 6f;

            var overline = MakeLabel(header.transform, "Txt_Overline", "WAR COUNCIL", compact ? 14 : 16, ClassicRpgUiRuntime.SoftGold, compact ? 18f : 20f);
            ApplyReadableTextStyle(overline, ClassicRpgUiRuntime.SoftGold, TextAlignmentOptions.Center, FontStyles.Bold);
            SetResponsiveSingleLine(overline, 11f, compact ? 14f : 16f);

            var titlePlate = new GameObject("TitlePlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            titlePlate.transform.SetParent(header.transform, false);
            var titlePlateLayout = titlePlate.GetComponent<LayoutElement>();
            titlePlateLayout.preferredWidth = compact ? 430f : 560f;
            titlePlateLayout.preferredHeight = compact ? 78f : 92f;
            ClassicRpgUiRuntime.ApplyPanel(titlePlate.GetComponent<Image>(), ClassicRpgPanelSkin.TitleLong, false, Color.white);

            _txtTitle = CreateAnchoredText(
                titlePlate.transform,
                "Txt_Title",
                BuildPageTitle(),
                compact ? 26 : 34,
                ClassicRpgUiRuntime.WarmGold,
                Vector2.zero,
                Vector2.one,
                new Vector2(24f, 10f),
                new Vector2(-24f, compact ? -14f : -18f));
            ApplyPlateTitleStyle(_txtTitle, ClassicRpgUiRuntime.WarmGold, TextAlignmentOptions.Center);
            SetResponsiveSingleLine(_txtTitle, compact ? 18f : 20f, compact ? 26f : 34f);

            _txtSubtitle = MakeLabel(
                header.transform,
                "Txt_Subtitle",
                BuildPageSubtitle(),
                compact ? 13 : 15,
                new Color(0.92f, 0.90f, 0.84f, 0.88f),
                compact ? 26f : 30f);
            _txtSubtitle.alignment = TextAlignmentOptions.Center;
            ApplyReadableTextStyle(_txtSubtitle, new Color(0.92f, 0.90f, 0.84f, 0.88f), TextAlignmentOptions.Center);
            SetResponsiveWrappedText(_txtSubtitle, compact ? 11f : 12f, compact ? 13f : 15f);

            _txtTimer = null;
            if (_mode == ProgressionViewerMode.PreMatchConfirm)
            {
                var timerPlate = new GameObject("TimerPlate", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                timerPlate.transform.SetParent(header.transform, false);
                var timerPlateLayout = timerPlate.GetComponent<LayoutElement>();
                timerPlateLayout.preferredWidth = compact ? 190f : 232f;
                timerPlateLayout.preferredHeight = compact ? 34f : 38f;
                ClassicRpgUiRuntime.ApplyPanel(timerPlate.GetComponent<Image>(), ClassicRpgPanelSkin.TitleMini, false, Color.white);

                _txtTimer = MakeLabel(timerPlate.transform, "Txt_Timer", "", compact ? 16 : 18, timerNormalColor, compact ? 24f : 26f);
                ApplyReadableTextStyle(_txtTimer, timerNormalColor, TextAlignmentOptions.Center, FontStyles.Bold);
                _txtTimer.gameObject.SetActive(true);
            }
        }

        GameObject CreateFloatingFooter(Transform parent, bool compact)
        {
            var footer = new GameObject("FooterStrip", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            footer.transform.SetParent(parent, false);
            var layoutElement = footer.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = _mode == ProgressionViewerMode.PreMatchConfirm
                ? (compact ? 112f : 102f)
                : (compact ? 86f : 76f);

            var layout = footer.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 4f : 6f;
            layout.padding = compact ? new RectOffset(0, 0, 2, 6) : new RectOffset(0, 0, 0, 8);
            return footer;
        }

        void BuildWinterBackdrop(Transform parent, bool compact)
        {
            var scenic = new GameObject("ScenicBackdrop", typeof(RectTransform), typeof(Image));
            scenic.transform.SetParent(parent, false);
            var scenicRect = scenic.GetComponent<RectTransform>();
            ClassicRpgUiRuntime.Stretch(scenicRect);
            scenic.transform.SetSiblingIndex(0);

            var scenicImage = scenic.GetComponent<Image>();
            scenicImage.raycastTarget = false;
            scenicImage.color = new Color(1f, 1f, 1f, 0.98f);
            var backdropSprite = LoadWinterBackdropSprite();
            if (backdropSprite != null)
            {
                scenicImage.sprite = backdropSprite;
                scenicImage.type = Image.Type.Simple;
                scenicImage.preserveAspect = true;

                var fitter = scenic.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = backdropSprite.rect.width / Mathf.Max(1f, backdropSprite.rect.height);
            }

            CreateBackdropTintLayer(parent, "BackdropWash", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.06f, 0.09f, 0.28f));
            CreateBackdropTintLayer(parent, "TopShade", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, compact ? -8f : -12f), new Vector2(0f, compact ? 168f : 208f), new Color(0.01f, 0.02f, 0.04f, 0.72f));
            CreateBackdropTintLayer(parent, "BottomShade", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, compact ? 0f : 10f), new Vector2(0f, compact ? 156f : 188f), new Color(0.01f, 0.01f, 0.03f, 0.68f));
            CreateBackdropTintLayer(parent, "LeftShade", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(compact ? 54f : 110f, 0f), new Color(0.01f, 0.02f, 0.04f, 0.34f));
            CreateBackdropTintLayer(parent, "RightShade", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(compact ? 54f : 110f, 0f), new Color(0.01f, 0.02f, 0.04f, 0.40f));
        }

        static void CreateBackdropTintLayer(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
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

        static Sprite LoadWinterBackdropSprite()
        {
            if (_winterBackdropSprite != null)
                return _winterBackdropSprite;

            var texture = Resources.Load<Texture2D>(WinterBackdropResourcePath);
            if (texture == null)
            {
                Debug.LogWarning($"[RaceProgression] Missing progression backdrop resource at Resources/{WinterBackdropResourcePath}.");
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            _winterBackdropSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _winterBackdropSprite.name = "WinterForestBackdrop_Runtime";
            return _winterBackdropSprite;
        }

        void EnsureDecorativeFrame(RectTransform panelRect)
        {
            if (panelRect == null)
                return;

            var existing = panelRect.Find("PremiumFrame") as RectTransform;
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return;
            }

            var frame = new GameObject("PremiumFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(panelRect, false);
            var image = frame.GetComponent<Image>();
            image.raycastTarget = false;
            ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.Frame, true, new Color(0.90f, 0.84f, 0.72f, 0.50f));
            ClassicRpgUiRuntime.Stretch(frame.GetComponent<RectTransform>(), new Vector2(-4f, -4f), new Vector2(4f, 4f));
        }

        void OnRaceSelected(string raceId)
        {
            string resolvedRaceId = RaceProgressionCatalog.ResolveAllowedRaceId(_availableRaceIds, raceId, "race button");
            _selectedRace = RaceProgressionCatalog.GetOrDefault(resolvedRaceId, "race button");
            _selectedUnit = GetDefaultUnitForTab(_selectedRace, _selectedTreeTab);
            ScrollRaceCarouselToIndex(GetSelectedRaceCarouselIndex());
            RefreshCopy();
            RefreshVisuals();
        }

        void OnUnitSelected(string unitId)
        {
            if (!TryResolveRaceUnit(unitId, out var unit))
                return;

            _selectedUnit = unit;
            _activePage = WizardPage.ProgressionTree;
            RefreshCopy();
            RefreshVisuals();
        }

        bool TryResolveRaceUnit(string unitId, out RaceProgressionUnitDefinition unit)
        {
            unit = null;
            if (_selectedRace == null || string.IsNullOrWhiteSpace(unitId))
                return false;

            if (_selectedRace.TryGetUnit(unitId, out unit))
                return true;

            for (int laneIndex = 0; laneIndex < _selectedRace.Lanes.Length; laneIndex++)
            {
                var lane = _selectedRace.Lanes[laneIndex];
                if (lane == null)
                    continue;

                int unitIndex = GetUnitIndex(lane.Units, unitId);
                if (unitIndex >= 0)
                {
                    unit = lane.Units[unitIndex];
                    return unit != null;
                }

                int outcomeIndex = GetUnitIndex(lane.OutcomeUnits, unitId);
                if (outcomeIndex >= 0)
                {
                    unit = lane.OutcomeUnits[outcomeIndex];
                    return unit != null;
                }
            }

            Debug.LogError($"[RaceProgression] Could not resolve selected unit '{unitId}' for race '{_selectedRace.Id}'.");
            return false;
        }

        void CloseDetailsModal()
        {
            RefreshCopy();
            RefreshVisuals();
        }

        void OnTreeTabSelected(RaceProgressionTab tab)
        {
            if (_selectedTreeTab == tab)
                return;

            _selectedTreeTab = tab;
            SyncSelectedUnitToCurrentTab();
            RebuildPanel();
        }

        void NavigateToPage(WizardPage page)
        {
            if (page == WizardPage.UnitDetails && _selectedUnit == null)
                _selectedUnit = GetDefaultUnitForTab(_selectedRace, _selectedTreeTab);

            if (page != WizardPage.UnitDetails)
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

        RaceProgressionUnitDefinition GetDefaultUnitForTab(RaceProgressionDefinition race, RaceProgressionTab tab)
        {
            if (race == null || race.Lanes == null)
                return null;

            RaceProgressionUnitDefinition firstUnitInTab = null;
            for (int laneIndex = 0; laneIndex < race.Lanes.Length; laneIndex++)
            {
                var lane = race.Lanes[laneIndex];
                if (lane == null || lane.Tab != tab || lane.Units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
                {
                    var unit = lane.Units[unitIndex];
                    if (unit == null)
                        continue;

                    firstUnitInTab ??= unit;
                    if (unit.StartsUnlocked)
                        return unit;
                }
            }

            return firstUnitInTab ?? GetDefaultUnit(race);
        }

        bool IsSelectedUnitInCurrentTab()
        {
            if (_selectedRace == null || _selectedUnit == null || _selectedRace.Lanes == null)
                return false;

            for (int laneIndex = 0; laneIndex < _selectedRace.Lanes.Length; laneIndex++)
            {
                var lane = _selectedRace.Lanes[laneIndex];
                if (lane == null
                    || lane.Tab != _selectedTreeTab
                    || lane.Units == null
                    || !string.Equals(lane.Id, _selectedUnit.LaneId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (GetUnitIndex(lane.Units, _selectedUnit.Id) >= 0)
                    return true;

                if (GetOutcomeUnitIndex(lane, _selectedUnit.Id) >= 0)
                    return true;
            }

            return false;
        }

        void SyncSelectedUnitToCurrentTab()
        {
            if (_selectedRace == null)
            {
                _selectedUnit = null;
                return;
            }

            if (IsSelectedUnitInCurrentTab())
                return;

            _selectedUnit = GetDefaultUnitForTab(_selectedRace, _selectedTreeTab);
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
            if (_txtTitle != null)
                _txtTitle.text = BuildPageTitle();

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
                WizardPage.ProgressionTree => "Step 2 of 2. Review the upgrade chain and inspect the dossier panel before confirming.",
                WizardPage.UnitDetails => "Step 2 of 2. Review the upgrade chain and inspect the dossier panel before confirming.",
                _ => "Review the race progression.",
            };
        }

        string BuildPageTitle()
        {
            return _activePage switch
            {
                WizardPage.RaceSelection => "Choose Your Race",
                WizardPage.ProgressionTree => "Faction Tech Tree",
                WizardPage.UnitDetails => "Faction Tech Tree",
                _ => "Race Progression",
            };
        }

        string BuildPageStatus()
        {
            return _activePage switch
            {
                WizardPage.RaceSelection => _selectedRace != null
                    ? $"Use the Continue button on {_selectedRace.DisplayName} to review its upgrade chain."
                    : "Select a race card to continue.",
                WizardPage.ProgressionTree => _selectedRace != null
                    ? _selectedUnit != null
                        ? $"{_selectedUnit.DisplayName} selected. Review its dossier and confirm when your build is ready."
                        : $"{_selectedRace.DisplayName} selected. Tap any node to inspect it in the dossier panel."
                    : "Select a race to continue.",
                WizardPage.UnitDetails => _selectedUnit != null
                    ? $"{_selectedUnit.DisplayName} selected. Review its dossier and confirm when your build is ready."
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
                view.Background.color = selected ? selectedColor : new Color(0.08f, 0.06f, 0.05f, 0.96f);
                view.Title.color = selected ? new Color(0.10f, 0.09f, 0.06f, 1f) : Color.white;
                view.Subtitle.color = selected ? new Color(0.18f, 0.13f, 0.08f, 1f) : new Color(0.82f, 0.88f, 0.96f, 1f);
                if (view.Summary != null)
                    view.Summary.color = selected ? new Color(0.20f, 0.15f, 0.09f, 1f) : new Color(0.84f, 0.88f, 0.95f, 1f);
                if (view.ContinueButton != null)
                {
                    view.ContinueButton.gameObject.SetActive(selected);
                    view.ContinueButton.interactable = selected;
                }
            }

            RefreshRaceCarouselButtons();
        }

        int GetSelectedRaceCarouselIndex()
        {
            if (_selectedRace == null || _availableRaceIds == null)
                return 0;

            for (int i = 0; i < _availableRaceIds.Length; i++)
            {
                if (string.Equals(_availableRaceIds[i], _selectedRace.Id, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }

        void ScrollRaceCarouselToIndex(int index)
        {
            if (_raceCarouselScroll == null || _raceCarouselItemCount <= 1)
                return;

            float normalized = Mathf.Clamp01((float)Mathf.Clamp(index, 0, _raceCarouselItemCount - 1) / (_raceCarouselItemCount - 1));
            _raceCarouselScroll.horizontalNormalizedPosition = normalized;
            RefreshRaceCarouselButtons();
        }

        void ShiftRaceCarousel(int direction)
        {
            if (_raceCarouselScroll == null || _raceCarouselItemCount <= 1)
                return;

            float step = 1f / Mathf.Max(1, _raceCarouselItemCount - 1);
            _raceCarouselScroll.horizontalNormalizedPosition = Mathf.Clamp01(_raceCarouselScroll.horizontalNormalizedPosition + (direction * step));
            RefreshRaceCarouselButtons();
        }

        void RefreshRaceCarouselButtons()
        {
            if (_btnRaceCarouselPrev != null)
                _btnRaceCarouselPrev.interactable = _raceCarouselScroll != null && _raceCarouselScroll.horizontalNormalizedPosition > 0.02f;
            if (_btnRaceCarouselNext != null)
                _btnRaceCarouselNext.interactable = _raceCarouselScroll != null && _raceCarouselScroll.horizontalNormalizedPosition < 0.98f;
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
            bool showDetails = _activePage == WizardPage.ProgressionTree || _activePage == WizardPage.UnitDetails;
            if (_detailsOverlayRoot != null)
                _detailsOverlayRoot.SetActive(showDetails);
            if (!showDetails)
            {
                ClearDetailsLivePreview();
                return;
            }

            SyncSelectedUnitToCurrentTab();
            if (_selectedUnit == null)
            {
                if (_txtDetailsTitle != null)
                    _txtDetailsTitle.text = "Select an Upgrade";
                if (_txtDetailsState != null)
                    _txtDetailsState.text = "Awaiting Selection";
                PopulateDetailRows(_detailsStatsRowsRoot, BuildPlaceholderDetailRows(
                    new DetailRowData("ClassicRpgIcons/Icon_Information", "Inspect", "Tap a large node to review role, lane, and cost.", new Color(0.88f, 0.90f, 0.96f, 1f))));
                PopulateDetailRows(_detailsRequirementRowsRoot, BuildPlaceholderDetailRows(
                    new DetailRowData("ClassicRpgIcons/Icon_Castle", "Gate", "Requirements appear when a node is selected.", new Color(0.82f, 0.86f, 0.92f, 1f))));
                PopulateDetailRows(_detailsMovesRowsRoot, BuildPlaceholderDetailRows(
                    new DetailRowData("ClassicRpgIcons/Icon_Battle", "Readout", "Combat and travel notes appear here.", new Color(0.84f, 0.88f, 0.95f, 1f))));
                if (_txtDetailsCustomization != null)
                    _txtDetailsCustomization.text = "Visual customization hooks will appear here.";
                if (_txtDetailsAudioStatus != null)
                    _txtDetailsAudioStatus.text = "Audio preview unlocks when a node is selected.";
                if (_txtDetailsBody != null)
                    _txtDetailsBody.text = "Choose a faction node to inspect the full dossier.";
                if (_txtDetailsPreviewStatus != null)
                    _txtDetailsPreviewStatus.text = "Portrait and building preview appear here.";

                RefreshDetailsPreviewButtons(null);
                RefreshDetailsPortrait(null);
                return;
            }

            if (_txtDetailsTitle != null)
                _txtDetailsTitle.text = _selectedUnit.DisplayName;
            if (_txtDetailsState != null)
                _txtDetailsState.text = BuildUnitDetailsStateText(_selectedUnit);
            PopulateDetailRows(_detailsStatsRowsRoot, BuildWarLedgerRows(_selectedUnit));
            PopulateDetailRows(_detailsRequirementRowsRoot, BuildRequirementRows(_selectedUnit));
            PopulateDetailRows(_detailsMovesRowsRoot, BuildCombatRows(_selectedUnit));
            if (_txtDetailsCustomization != null)
                _txtDetailsCustomization.text = BuildUnitCustomizationText(_selectedUnit);
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
            if (lane == null)
                return MinLaneRowWidth;

            if (lane.Layout == RaceProgressionLaneLayout.BuildingStepsToOutcomeCards)
            {
                int unitCount = CountLaneUnits(lane);
                int outcomeCount = CountOutcomeUnits(lane);
                float progressionWidth = (unitCount * UpgradeStepCardWidth)
                    + (Mathf.Max(0, unitCount - 1) * CompactArrowWidth)
                    + (Mathf.Max(0, unitCount - 1) * 8f)
                    + 48f;
                float heroWidth = (outcomeCount * HeroOutcomeCardWidth)
                    + (Mathf.Max(0, outcomeCount - 1) * 10f)
                    + 24f;
                return Mathf.Max(MinLaneRowWidth, progressionWidth, heroWidth);
            }

            float width = 32f;
            int visibleElements = 0;
            for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
            {
                var unit = lane.Units[unitIndex];
                if (unit == null)
                    continue;

                width += ResolveTreeNodeWidth(unit);
                visibleElements++;

                if (unitIndex >= lane.Units.Length - 1)
                    continue;

                var nextUnit = lane.Units[unitIndex + 1];
                if (nextUnit == null)
                    continue;

                if (lane.ShowRequirementCards
                    && nextUnit.UnlockRequirement != null
                    && !nextUnit.SuppressInlineRequirementCard)
                {
                    width += ChainArrowWidth + RequirementCardWidth + ChainArrowWidth;
                    visibleElements += 3;
                }
                else
                {
                    width += ChainArrowWidth;
                    visibleElements++;
                }
            }

            width += Mathf.Max(0, visibleElements - 1) * 8f;
            return Mathf.Max(MinLaneRowWidth, width);
        }

        static float ResolveTreeNodeWidth(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return UnitCardWidth;

            return unit.CardStyle switch
            {
                RaceProgressionUnitCardStyle.RequirementStep => RequirementCardWidth,
                RaceProgressionUnitCardStyle.UpgradeStep => UpgradeStepCardWidth,
                RaceProgressionUnitCardStyle.BuildingTier => BuildingTierCardWidth,
                RaceProgressionUnitCardStyle.HeroOutcome => HeroOutcomeCardWidth,
                _ => UnitCardWidth,
            };
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
                    UnitProgressVisualState.Unlocked => "Town Core requirement already met",
                    UnitProgressVisualState.Available => "Current Town Core upgrade available",
                    _ => "Locked Town Core upgrade",
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
            builder.AppendLine($"[Position] {BuildPositionDetailText(unit)}");
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
            if (TryPlayPreviewCombatSfx(_selectedUnit, out var generatedLabel))
            {
                SetDetailsPreviewStatus($"{_selectedUnit?.DisplayName ?? "This entry"} is previewing {generatedLabel.ToLowerInvariant()} audio.");
                return;
            }

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
            if (!TryResolvePreviewVoice(_selectedUnit, out var label))
            {
                SetDetailsPreviewStatus(
                    IsSiegeDisplayOnlyUnit(_selectedUnit)
                        ? "This siege entry uses mechanical SFX only."
                        : "Voice lines are still pending for this unit.");
                return;
            }

            if (!TryPlayPreviewVoice(_selectedUnit))
            {
                SetDetailsPreviewStatus("Voice mapping exists, but the generated clips have not been imported yet.");
                return;
            }

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
            bool hasVoice = TryResolvePreviewVoice(unit, out var voiceLabel);

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
                hasVoice ? $"Play {voiceLabel}" : IsSiegeDisplayOnlyUnit(unit) ? "SFX Only" : "Voices Pending",
                new Color(0.30f, 0.24f, 0.41f, 1f),
                new Color(0.15f, 0.18f, 0.24f, 0.92f));

            bool hasLivePreview = TryEnsureDetailsPreviewUnit(unit);
            List<DetailPreviewCycleEntry> cycleEntries = hasLivePreview ? BuildDetailsPreviewCycleEntries() : null;
            SetDetailsPreviewStatus(
                hasLivePreview
                    ? BuildDetailsPreviewReadyStatus(unit, cycleEntries)
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
            RefreshDetailsPreviewCycle(unit, hasLivePreview, cycleEntries);
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
            StopDetailsPreviewCycleRoutine();
            StopDetailsPreviewResetRoutine();

            if (!TryPlayDetailsPreviewMotion(motion, updateStatus: true, scheduleReset: true))
                return;

            StartDetailsPreviewCycle(
                _selectedUnit,
                BuildDetailsPreviewCycleEntries(),
                BuildDetailsPreviewCycleSignature(_selectedUnit),
                DetailsPreviewManualResumeDelay);
        }

        bool TryPlayDetailsPreviewMotion(DetailPreviewMotion motion, bool updateStatus, bool scheduleReset)
        {
            return TryPlayDetailsPreviewStates(motion, ResolvePreviewMotionStates(motion), updateStatus, scheduleReset);
        }

        bool TryPlayDetailsPreviewStates(DetailPreviewMotion motion, string[] candidateStates, bool updateStatus, bool scheduleReset)
        {
            if (!TryEnsureDetailsPreviewUnit(_selectedUnit))
            {
                if (updateStatus)
                    SetDetailsPreviewStatus("This entry does not have a live rig preview yet.");
                return false;
            }

            if (_detailsPreviewCam == null)
                return false;

            _detailsPreviewCam.SetAnimatorSpeed(GetPreviewMotionSpeed(motion));
            if (!_detailsPreviewCam.TryPlayFirstAvailableState(candidateStates, out var playedState, out var clipLength, 0f))
            {
                if (updateStatus)
                {
                    SetDetailsPreviewStatus($"No {BuildPreviewMotionLabel(motion, _selectedUnit).ToLowerInvariant()} animation is wired for {_selectedUnit?.DisplayName ?? "this unit"} yet.");
                }

                return false;
            }

            StopDetailsPreviewResetRoutine();
            if (updateStatus)
                SetDetailsPreviewStatus(BuildPreviewStatusText(motion, _selectedUnit, playedState));

            if (scheduleReset && IsTransientPreviewMotion(motion))
            {
                float resetDelay = ResolveTransientPreviewDelay(motion, clipLength);
                _detailsPreviewResetRoutine = StartCoroutine(ReturnDetailsPreviewToIdle(resetDelay));
            }

            return true;
        }

        bool CanPlayDetailsPreviewMotion(DetailPreviewMotion motion)
        {
            return _detailsPreviewCam != null
                && _detailsPreviewCam.HasAnyState(ResolvePreviewMotionStates(motion));
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
                StopDetailsPreviewCycleRoutine();
                previewCam.ShowUnit(previewKey);
                previewCam.SetAnimatorSpeed(1f);
                previewCam.PlayFirstAvailableState(ResolvePreviewMotionStates(DetailPreviewMotion.Idle), 0.05f);
                _detailsPreviewStagedKey = previewKey;
            }

            return previewCam.StagedObject != null;
        }

        void ClearDetailsLivePreview()
        {
            StopDetailsPreviewResetRoutine();
            StopDetailsPreviewCycleRoutine();
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
                _detailsPreviewCam = RuntimePortraitStudio.Create("RaceProgressionDetailsPreviewStudio", registry, out _runtimePreviewRoot, out _runtimePreviewTexture, textureSize: 768);

            _detailsPreviewCam.Registry = registry;
            _detailsPreviewCam.transform.position = new Vector3(0f, 0f, 80f);
            _detailsPreviewCam.FitHeight = 2.25f;
            _detailsPreviewCam.FrameFill = 0.86f;
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
            _detailsPreviewCam.PlayFirstAvailableState(ResolvePreviewMotionStates(DetailPreviewMotion.Idle), 0.08f);
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

        void RefreshDetailsPreviewCycle(RaceProgressionUnitDefinition unit, bool hasLivePreview, List<DetailPreviewCycleEntry> cycleEntries)
        {
            if (!hasLivePreview || unit == null)
            {
                StopDetailsPreviewCycleRoutine();
                return;
            }

            List<DetailPreviewCycleEntry> resolvedEntries = cycleEntries ?? BuildDetailsPreviewCycleEntries();
            if (resolvedEntries.Count < 2)
            {
                StopDetailsPreviewCycleRoutine();
                return;
            }

            string cycleSignature = BuildDetailsPreviewCycleSignature(unit);
            if (_detailsPreviewCycleRoutine != null
                && string.Equals(_detailsPreviewCycleSignature, cycleSignature, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StartDetailsPreviewCycle(unit, resolvedEntries, cycleSignature, DetailsPreviewCycleInitialDelay);
        }

        void StartDetailsPreviewCycle(
            RaceProgressionUnitDefinition unit,
            List<DetailPreviewCycleEntry> cycleEntries,
            string cycleSignature,
            float initialDelay)
        {
            StopDetailsPreviewCycleRoutine();
            if (unit == null || cycleEntries == null || cycleEntries.Count < 2 || string.IsNullOrWhiteSpace(cycleSignature))
                return;

            _detailsPreviewCycleSignature = cycleSignature;
            _detailsPreviewCycleRoutine = StartCoroutine(RunDetailsPreviewCycle(cycleSignature, cycleEntries.ToArray(), initialDelay));
        }

        IEnumerator RunDetailsPreviewCycle(string cycleSignature, DetailPreviewCycleEntry[] cycleEntries, float initialDelay)
        {
            if (cycleEntries == null || cycleEntries.Length < 2)
            {
                _detailsPreviewCycleRoutine = null;
                _detailsPreviewCycleSignature = null;
                yield break;
            }

            if (initialDelay > 0f)
                yield return new WaitForSecondsRealtime(initialDelay);

            if (!IsDetailsPreviewCycleCurrent(cycleSignature))
            {
                _detailsPreviewCycleRoutine = null;
                _detailsPreviewCycleSignature = null;
                yield break;
            }

            int index = cycleEntries[0].Motion == DetailPreviewMotion.Idle && cycleEntries.Length > 1 ? 1 : 0;
            while (IsDetailsPreviewCycleCurrent(cycleSignature))
            {
                DetailPreviewCycleEntry entry = cycleEntries[index];
                if (TryPlayDetailsPreviewStates(entry.Motion, new[] { entry.StateName }, updateStatus: false, scheduleReset: false))
                    yield return new WaitForSecondsRealtime(ResolveDetailsPreviewCycleDwell(entry));
                else
                    yield return new WaitForSecondsRealtime(0.4f);

                index = (index + 1) % cycleEntries.Length;
            }

            if (string.Equals(_detailsPreviewCycleSignature, cycleSignature, StringComparison.OrdinalIgnoreCase))
            {
                _detailsPreviewCycleRoutine = null;
                _detailsPreviewCycleSignature = null;
            }
        }

        bool IsDetailsPreviewCycleCurrent(string cycleSignature)
        {
            return !string.IsNullOrWhiteSpace(cycleSignature)
                && _detailsPreviewCam != null
                && _detailsPreviewCam.StagedObject != null
                && _selectedUnit != null
                && string.Equals(_detailsPreviewCycleSignature, cycleSignature, StringComparison.OrdinalIgnoreCase)
                && string.Equals(BuildDetailsPreviewCycleSignature(_selectedUnit), cycleSignature, StringComparison.OrdinalIgnoreCase);
        }

        void StopDetailsPreviewCycleRoutine()
        {
            if (_detailsPreviewCycleRoutine != null)
            {
                StopCoroutine(_detailsPreviewCycleRoutine);
                _detailsPreviewCycleRoutine = null;
            }

            _detailsPreviewCycleSignature = null;
        }

        List<DetailPreviewCycleEntry> BuildDetailsPreviewCycleEntries()
        {
            var entries = new List<DetailPreviewCycleEntry>();
            if (_detailsPreviewCam == null)
                return entries;

            var seenStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Spawn, ResolvePreviewMotionStates(DetailPreviewMotion.Spawn), 1);
            AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Idle, ResolvePreviewMotionStates(DetailPreviewMotion.Idle), 1);

            string[] moveStates = ResolvePreviewMoveStates(preferRun: false);
            if (moveStates.Length > 0)
            {
                AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Walk, new[] { moveStates[0] }, 1);
                if (moveStates.Length > 1)
                    AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Run, SlicePreviewStates(moveStates, 1), 1);
                if (moveStates.Length > 2)
                    AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.March, SlicePreviewStates(moveStates, 2), int.MaxValue);
            }

            string[] attackStates = ResolvePreviewAttackStates(preferAlternate: false);
            if (attackStates.Length > 0)
            {
                AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Strike, new[] { attackStates[0] }, 1);
                if (attackStates.Length > 1)
                    AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Special, SlicePreviewStates(attackStates, 1), int.MaxValue);
            }

            AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Defend, ResolvePreviewMotionStates(DetailPreviewMotion.Defend), 1);
            AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Retreat, ResolvePreviewMotionStates(DetailPreviewMotion.Retreat), 1);
            AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Hit, ResolvePreviewMotionStates(DetailPreviewMotion.Hit), 1);
            AppendDetailsPreviewCycleEntries(entries, seenStates, DetailPreviewMotion.Death, ResolvePreviewMotionStates(DetailPreviewMotion.Death), 1);

            return entries;
        }

        void AppendDetailsPreviewCycleEntries(
            List<DetailPreviewCycleEntry> entries,
            HashSet<string> seenStates,
            DetailPreviewMotion motion,
            string[] candidateStates,
            int maxEntries)
        {
            if (entries == null || seenStates == null || candidateStates == null || candidateStates.Length == 0 || maxEntries <= 0)
                return;

            int added = 0;
            for (int i = 0; i < candidateStates.Length && added < maxEntries; i++)
            {
                if (!TryResolveDetailsPreviewCycleEntry(motion, candidateStates[i], out var entry))
                    continue;

                string dedupeKey = ResolvePreviewStateIdentity(entry.StateName);
                if (string.IsNullOrWhiteSpace(dedupeKey) || !seenStates.Add(dedupeKey))
                    continue;

                entries.Add(entry);
                added++;
            }
        }

        bool TryResolveDetailsPreviewCycleEntry(DetailPreviewMotion motion, string candidateStateName, out DetailPreviewCycleEntry entry)
        {
            entry = default;
            if (_detailsPreviewCam == null || string.IsNullOrWhiteSpace(candidateStateName))
                return false;

            Animator[] animators = _detailsPreviewCam.GetStagedAnimators();
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                int stateHash = Animator.StringToHash(candidateStateName);
                if (!animator.HasState(0, stateHash))
                    continue;

                entry = new DetailPreviewCycleEntry(
                    motion,
                    candidateStateName,
                    UnitAnimationResolver.ResolveClipLength(animator, candidateStateName),
                    GetPreviewMotionSpeed(motion));
                return true;
            }

            return false;
        }

        static float ResolveDetailsPreviewCycleDwell(DetailPreviewCycleEntry entry)
        {
            float playbackSeconds = entry.ClipLength > 0.01f && entry.Speed > 0.05f
                ? entry.ClipLength / entry.Speed
                : 0f;

            return entry.Motion switch
            {
                DetailPreviewMotion.Idle => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds, 1.15f, 1.75f) : 1.25f,
                DetailPreviewMotion.Walk => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds, 1.05f, 1.55f) : 1.15f,
                DetailPreviewMotion.March => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds, 1.20f, 1.85f) : 1.35f,
                DetailPreviewMotion.Run => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds, 1.00f, 1.40f) : 1.10f,
                DetailPreviewMotion.Hit => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds + 0.10f, 0.90f, 1.35f) : 1.00f,
                DetailPreviewMotion.Death => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds + 0.20f, 1.40f, 2.80f) : 1.90f,
                _ => playbackSeconds > 0.01f ? Mathf.Clamp(playbackSeconds + 0.10f, 1.00f, 1.90f) : 1.20f,
            };
        }

        string BuildDetailsPreviewCycleSignature(RaceProgressionUnitDefinition unit)
        {
            string previewKey = ResolveDetailsPreviewKey(unit);
            string unitKey = NormalizeTechTreeKey(unit?.Id);
            return $"{unitKey}|{previewKey ?? string.Empty}";
        }

        static float GetPreviewMotionSpeed(DetailPreviewMotion motion)
        {
            return motion switch
            {
                DetailPreviewMotion.March => 0.72f,
                DetailPreviewMotion.Retreat => 0.9f,
                _ => 1f,
            };
        }

        string[] ResolvePreviewMotionStates(DetailPreviewMotion motion)
        {
            var profile = _detailsPreviewCam != null ? _detailsPreviewCam.StagedAnimationProfile : null;
            return motion switch
            {
                DetailPreviewMotion.Spawn => ChoosePreviewStateCandidates(profile?.SpawnStates, GetLegacyPreviewMotionStates(motion)),
                DetailPreviewMotion.Idle => ChoosePreviewStateCandidates(profile?.IdleStates, GetLegacyPreviewMotionStates(motion)),
                DetailPreviewMotion.Walk => ResolvePreviewMoveStates(preferRun: false),
                DetailPreviewMotion.March => ResolvePreviewMoveStates(preferRun: false),
                DetailPreviewMotion.Run => ResolvePreviewMoveStates(preferRun: true),
                DetailPreviewMotion.Strike => ResolvePreviewAttackStates(preferAlternate: false),
                DetailPreviewMotion.Special => ResolvePreviewAttackStates(preferAlternate: true),
                DetailPreviewMotion.Defend => ChoosePreviewStateCandidates(profile?.DefendStates, GetLegacyPreviewMotionStates(motion)),
                DetailPreviewMotion.Retreat => ChoosePreviewStateCandidates(profile?.RetreatStates, GetLegacyPreviewMotionStates(motion)),
                DetailPreviewMotion.Hit => ChoosePreviewStateCandidates(profile?.HitReactStates, GetLegacyPreviewMotionStates(motion)),
                DetailPreviewMotion.Death => ChoosePreviewStateCandidates(profile?.DeathStates, GetLegacyPreviewMotionStates(motion)),
                _ => Array.Empty<string>(),
            };
        }

        string[] ResolvePreviewMoveStates(bool preferRun)
        {
            var profile = _detailsPreviewCam != null ? _detailsPreviewCam.StagedAnimationProfile : null;
            string[] candidates = ChoosePreviewStateCandidates(profile?.MoveStates, GetLegacyPreviewMotionStates(preferRun ? DetailPreviewMotion.Run : DetailPreviewMotion.Walk));
            if (candidates.Length <= 1)
                return candidates;

            var preferred = new List<string>(candidates.Length);
            var fallback = new List<string>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                string stateName = candidates[i];
                string stateId = ResolvePreviewStateIdentity(stateName);
                bool isRunLike = stateId.Contains("run", StringComparison.OrdinalIgnoreCase);
                bool isWalkLike = stateId.Contains("walk", StringComparison.OrdinalIgnoreCase)
                    || stateId.Contains("move", StringComparison.OrdinalIgnoreCase);

                if ((preferRun && isRunLike) || (!preferRun && isWalkLike))
                    preferred.Add(stateName);
                else
                    fallback.Add(stateName);
            }

            if (preferred.Count == 0)
                return candidates;

            preferred.AddRange(fallback);
            return preferred.ToArray();
        }

        string[] ResolvePreviewAttackStates(bool preferAlternate)
        {
            var profile = _detailsPreviewCam != null ? _detailsPreviewCam.StagedAnimationProfile : null;
            string[] candidates = ChoosePreviewStateCandidates(profile?.AttackStates, GetLegacyPreviewMotionStates(preferAlternate ? DetailPreviewMotion.Special : DetailPreviewMotion.Strike));
            if (!preferAlternate || candidates.Length < 2)
                return candidates;

            var rotated = new string[candidates.Length];
            for (int i = 1; i < candidates.Length; i++)
                rotated[i - 1] = candidates[i];
            rotated[^1] = candidates[0];
            return rotated;
        }

        static string[] ChoosePreviewStateCandidates(string[] preferredStates, string[] fallbackStates)
        {
            return preferredStates != null && preferredStates.Length > 0
                ? DeduplicatePreviewStateCandidates(preferredStates)
                : DeduplicatePreviewStateCandidates(fallbackStates);
        }

        static string[] DeduplicatePreviewStateCandidates(string[] stateNames)
        {
            if (stateNames == null || stateNames.Length == 0)
                return Array.Empty<string>();

            var ordered = new List<string>(stateNames.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < stateNames.Length; i++)
            {
                string stateName = stateNames[i];
                string stateId = ResolvePreviewStateIdentity(stateName);
                if (string.IsNullOrWhiteSpace(stateId) || !seen.Add(stateId))
                    continue;

                ordered.Add(stateName.Trim());
            }

            return ordered.ToArray();
        }

        static string[] SlicePreviewStates(string[] stateNames, int startIndex)
        {
            if (stateNames == null || startIndex < 0 || startIndex >= stateNames.Length)
                return Array.Empty<string>();

            string[] slice = new string[stateNames.Length - startIndex];
            Array.Copy(stateNames, startIndex, slice, 0, slice.Length);
            return slice;
        }

        static string ResolvePreviewStateIdentity(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
                return string.Empty;

            string trimmed = stateName.Trim();
            int lastDot = trimmed.LastIndexOf('.');
            return lastDot >= 0 && lastDot < trimmed.Length - 1
                ? trimmed[(lastDot + 1)..]
                : trimmed;
        }

        static string[] GetLegacyPreviewMotionStates(DetailPreviewMotion motion)
        {
            return motion switch
            {
                DetailPreviewMotion.Spawn => new[] { "WeaponUnSheath", "WeaponUnsheath2", "UnSheathed", "Unsheathed", "Spawn", "Summon", "Idle", "idle" },
                DetailPreviewMotion.Idle => new[] { "Idle", "IdleNormal", "IdleCombat", "Idle-Sheathed", "Sheathed", "UnSheathed", "Unsheathed", "idle" },
                DetailPreviewMotion.Walk => new[] { "WalkRun", "Walk", "Move", "walk", "move" },
                DetailPreviewMotion.March => new[] { "WalkRun", "Walk", "Move", "walk", "move" },
                DetailPreviewMotion.Run => new[] { "Run", "WalkRun", "Move", "run", "walkrun", "move" },
                DetailPreviewMotion.Strike => new[] { "Attack1", "MoveAttack1", "Attack", "attack" },
                DetailPreviewMotion.Special => new[] { "Attack2", "Attack3", "MoveAttack2", "Run2-Attack1", "Jump-Attack1", "SpecialAttack1", "SpecialAttack2", "Attack1", "Attack", "attack" },
                DetailPreviewMotion.Defend => new[] { "Blocking", "Block", "Defend", "ShieldBlock", "IdleCombat", "Idle", "idle" },
                DetailPreviewMotion.Retreat => new[] { "Retreat", "WalkRun", "Run", "Walk", "Move", "run", "walk", "move" },
                DetailPreviewMotion.Hit => new[] { "Damage", "Hit", "HitReact", "Hurt", "damage", "hit" },
                DetailPreviewMotion.Death => new[] { "Death", "Die", "Knockout", "death", "die" },
                _ => Array.Empty<string>(),
            };
        }

        void SetDetailsPreviewStatus(string text)
        {
            if (_txtDetailsPreviewStatus == null)
                return;

            _txtDetailsPreviewStatus.text = text;
        }

        static string BuildDetailsPreviewReadyStatus(RaceProgressionUnitDefinition unit, List<DetailPreviewCycleEntry> cycleEntries)
        {
            string unitName = unit?.DisplayName ?? "this unit";
            return cycleEntries != null && cycleEntries.Count > 1
                ? $"Preview ready. Auto-cycling the bound controller states for {unitName}. Tap any motion button to inspect a specific move."
                : $"Preview ready. Tap a motion button to inspect how {unitName} moves in battle.";
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
                DetailPreviewMotion.Spawn => "Spawn",
                DetailPreviewMotion.Idle => "Idle",
                DetailPreviewMotion.Walk => "Walk",
                DetailPreviewMotion.March => "March",
                DetailPreviewMotion.Run => "Run",
                DetailPreviewMotion.Strike => "Strike",
                DetailPreviewMotion.Special => BuildSpecialPreviewButtonLabel(unit),
                DetailPreviewMotion.Defend => "Defend",
                DetailPreviewMotion.Retreat => "Retreat",
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

            if (unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
                return string.IsNullOrWhiteSpace(BuildCompactSpecialTag(unit)) ? "Castle reward" : BuildCompactSpecialTag(unit);

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

            if (unit.CardStyle == RaceProgressionUnitCardStyle.HeroOutcome)
                return $"{BuildUnitRoleLabel(unit)} | {BuildCompactPositionTag(unit) ?? "Castle reward"}";

            if (IsStableDisplayOnlyUnit(unit))
                return !string.IsNullOrWhiteSpace(unit.StatsSummary) ? unit.StatsSummary : "Future stable branch";

            string positionText = BuildCompactPositionTag(unit);
            if (!string.IsNullOrWhiteSpace(positionText))
                return positionText;

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
            builder.AppendLine($"[Position] {BuildPositionDetailText(unit)}");
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

        static bool IsSiegeDisplayOnlyUnit(RaceProgressionUnitDefinition unit)
        {
            string unitId = NormalizeTechTreeKey(unit?.Id);
            string laneId = NormalizeTechTreeKey(unit?.LaneId);
            return (unitId != null && unitId.EndsWith("_siege", StringComparison.Ordinal))
                || laneId == "siege_tier1"
                || laneId == "siege_tier2";
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

        static string BuildCompactPositionTag(RaceProgressionUnitDefinition unit)
        {
            int positionIndex = GetPositionSlotIndex(unit);
            return positionIndex > 0
                ? BuildPositionLabel(positionIndex)
                : IsEconomyUnit(unit)
                    ? "Trade Route"
                    : IsStableDisplayOnlyUnit(unit)
                        ? "Stable Branch"
                        : null;
        }

        static string BuildPositionDetailText(RaceProgressionUnitDefinition unit)
        {
            int positionIndex = GetPositionSlotIndex(unit);
            if (positionIndex > 0)
                return BuildPositionDetailLabel(positionIndex);

            if (IsEconomyUnit(unit))
                return "Trade route / out of lane combat";

            if (IsStableDisplayOnlyUnit(unit))
                return "No live lane-combat role; display-only branch";

            return "No live lane-combat role assigned";
        }

        static int GetPositionSlotIndex(RaceProgressionUnitDefinition unit)
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

        static string BuildPositionLabel(int positionIndex)
        {
            return positionIndex switch
            {
                1 => "Frontline",
                2 => "Melee Line",
                3 => "Reach Support",
                4 => "Arcane Support",
                5 => "Ranged Support",
                6 => "Rear Support",
                _ => "Assigned Role",
            };
        }

        static string BuildPositionDetailLabel(int positionIndex)
        {
            return positionIndex switch
            {
                1 => "Frontline anchor role",
                2 => "Main melee pressure role",
                3 => "Reach-support role",
                4 => "Arcane support role",
                5 => "Ranged support role",
                6 => "Rear-support role",
                _ => "Assigned combat role",
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

        static string BuildHeroOutcomeBadgeText(RaceProgressionUnitDefinition unit)
        {
            return NormalizeTechTreeKey(unit?.Id) switch
            {
                "king" => "Castle Hero",
                "paladin" => "Castle Champion",
                "bishop" => "Castle Oracle",
                _ => "Castle Reward",
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
                "king" => "Royal commander",
                "paladin" => "Holy champion",
                "bishop" => "Sacred support",
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
                "spearman" => "Recruited into the disciplined polearm corps through the Blacksmith.",
                "halberdier" => "Advanced polearm specialist forged from the same Blacksmith branch.",
                "lancer" => "Cavalry polearm veteran released at the top of the spear line.",
                "shieldman" => "Drawn from the city guard and equipped as a frontline wall.",
                "shield_guard" => "Veteran shield-line soldier outfitted through upgraded Blacksmith support.",
                "guardian" => "Late-game elite guard mounted and armored for the final defensive tier.",
                "cleric" => "Temple acolyte attached to marching companies for field care.",
                "priest" => "Ordained support unit sent from the upgraded Temple.",
                "high_priest" => "Senior temple leader deployed once the faith branch is fully matured.",
                "mage" => "Early battle caster licensed through the Mage Tower.",
                "wizard" => "Veteran spellcaster trained after the tower reaches its middle tier.",
                "thaumaturge" => "Master arcane operative released at the peak of the Mage Tower.",
                "archer" => "Drawn from huntsmen and garrison bowmen once the Archery building is built.",
                "crossbowman" => "Armory-trained marksman issued heavier ranged weapons at tier two.",
                "ranger" => "Veteran frontier skirmisher fielded from the fully upgraded Archery building.",
                "peasant" => "Starter trade laborer sent from the Market through the Rear Gate to the Beast Lair.",
                "settler" => "Experienced civilian courier that replaces Peasants on the higher-value market route.",
                "trader" => "Top-tier commercial runner representing the Market's final rear-gate trade economy.",
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
                "militia" => "Cheap early frontline body used to establish the first melee role.",
                "swordsman" => "Reliable line fighter that upgrades early militia pressure into a sturdier core.",
                "knight" => "Shock cavalry finisher for the infantry branch.",
                "spearman" => "Reach support that helps control enemy approach.",
                "halberdier" => "Higher-tier polearm pressure with better anti-armor identity.",
                "lancer" => "Fast reach cavalry used to punish openings once the line is established.",
                "shieldman" => "Frontline anchor that protects the rest of the force.",
                "shield_guard" => "Improved frontline tank that stabilizes longer engagements.",
                "guardian" => "Elite defensive anchor for the late-game frontline.",
                "cleric" => "Back-rank sustain support and early healing coverage.",
                "priest" => "Stronger backline sustain with more reliable healing uptime.",
                "high_priest" => "Peak support output for extended battles.",
                "mage" => "Backline arcane damage with early spell pressure.",
                "wizard" => "Stronger magical throughput from a safer support position.",
                "thaumaturge" => "Late-game caster that should define the arcane branch.",
                "archer" => "Baseline ranged pressure for the ranged branch.",
                "crossbowman" => "Tier-two ranged specialist intended to hit harder than base archers.",
                "ranger" => "Late-game skirmisher intended to finish the ranged branch cleanly.",
                "peasant" => "Carries the starter rear-gate economy route for the human trade branch.",
                "settler" => "Improves the value of every completed market route lap.",
                "trader" => "Represents the fully upgraded rear-gate market runner.",
                "king" => "Frontline hero commander for the Castle outcome row.",
                "paladin" => "Holy frontline hero meant to absorb and punish pressure.",
                "bishop" => "Rear-support hero that sustains the main force.",
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
                "guardian" => "Elite guard impact that keeps the frontline intact.",
                "cleric" => "Field mend and close support blessings.",
                "priest" => "Battle prayer and stronger targeted healing.",
                "high_priest" => "High-output sustain with stronger blessing coverage.",
                "mage" => "Arcane burst volleys from the rear support role.",
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

            if (!string.IsNullOrWhiteSpace(catalog.name))
                return catalog.name.Trim();

            string sourceUnit = !string.IsNullOrWhiteSpace(catalog.canonical_unit_type)
                ? catalog.canonical_unit_type
                : catalog.key;
            if (string.IsNullOrWhiteSpace(sourceUnit))
                return "Not specified";

            if (sourceUnit.StartsWith("tt_", StringComparison.OrdinalIgnoreCase))
                sourceUnit = sourceUnit.Substring(3);

            sourceUnit = HumanizeLabel(sourceUnit);
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
                    "[Position] This runner travels the trade route instead of entering lane combat.\n" +
                    "[Preview] Economy audio can be previewed below. Combat animation preview is not applicable.";
            }

            if (IsStableDisplayOnlyUnit(unit))
            {
                return
                    $"[Stable] {BuildUnitSpecialAttackText(unit)}\n" +
                    "[Position] This branch is display-only while mounted gameplay is still pending.\n" +
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
                        ? $"[Preview] The live rig above auto-cycles the bound controller states for this unit, including movement, attacks, stance, react, and death clips when they are wired on the prefab profile. Tap a motion button to inspect one directly. {sfxLabel} audio can also be previewed below."
                        : $"[Preview] The live rig above auto-cycles the bound controller states for this unit, including movement, attacks, stance, react, and death clips when they are wired on the prefab profile. Tap a motion button to inspect one directly. No dedicated unit SFX is wired for this entry yet.");
            }
            else
            {
                builder.Append("[Preview] The live rig will auto-cycle any bound prefab states it can resolve here. Motion buttons still let you inspect a specific state, but catalog-backed detail data is missing.");
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

            string positionLabel = BuildCompactPositionTag(unit);
            if (!string.IsNullOrWhiteSpace(positionLabel))
                return $"Fills the {positionLabel.ToLowerInvariant()} role.";

            if (IsEconomyUnit(unit))
                return "Runs the market route and stays out of lane combat.";

            if (IsStableDisplayOnlyUnit(unit))
                return "Reserved for future cavalry positioning once mounted combat is live.";

            return "No live lane-combat role is assigned to this entry.";
        }

        static string BuildUnitDeliveryText(RaceProgressionUnitDefinition unit, UnitCatalogEntry catalog)
        {
            if (catalog == null)
                return "Delivery data unavailable.";

            string positionText = BuildCompactPositionTag(unit) ?? "assigned role";
            string damageType = HumanizeLabel(catalog.damage_type);
            if (!string.IsNullOrWhiteSpace(catalog.proj_behavior))
            {
                return $"{HumanizeLabel(catalog.proj_behavior)} attack using {damageType.ToLowerInvariant()} damage from the {positionText.ToLowerInvariant()} role.";
            }

            if (catalog.range > 1f)
                return $"{damageType} ranged strike fired from the {positionText.ToLowerInvariant()} role at {FormatStatNumber(catalog.range)} range.";

            if (catalog.range > 0.30f)
                return $"{damageType} reach attack delivered from the {positionText.ToLowerInvariant()} role.";

            return $"{damageType} close-range strike delivered from the {positionText.ToLowerInvariant()} role.";
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
            bool hasVoice = TryResolvePreviewVoice(unit, out var voiceLabel);

            var builder = new StringBuilder();
            builder.AppendLine(hasSfx
                ? $"[SFX] {sfxLabel} is ready to preview."
                : "[SFX] No dedicated preview clip is wired for this entry yet.");
            builder.Append(hasVoice
                ? $"[Voice] {voiceLabel} is ready to preview."
                : IsSiegeDisplayOnlyUnit(unit)
                    ? "[Voice] Siege entries use mechanical SFX only."
                    : "[Voice] No voice lines are wired for this unit in the current project.");
            return builder.ToString();
        }

        string BuildUnitCustomizationText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Customization catalog unavailable.";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
            {
                return
                    "[Store Surface] This slot is reserved for a build requirement card, not a purchasable unit cosmetic.\n" +
                    "[Future Use] Requirement cards can still advertise banners, construction finishers, and branch-wide flair later.";
            }

            if (unit.CardDisplay != null)
            {
                return
                    "[Skins] Alternate fortress trims, banners, glow treatments, and upgrade VFX can live here.\n" +
                    "[Victory] Branch-wide fanfares, town speeches, and castle celebrations fit this store lane.\n" +
                    "[Status] The showcase layout is ready for store hooks, but purchase wiring is still pending.";
            }

            if (IsEconomyUnit(unit))
            {
                return
                    "[Abilities] Route flourish variants, coin toss finishers, and delivery callouts can be sold here.\n" +
                    "[Skins] Wagon colors, heraldry, worker outfits, and cargo props fit this unit family.\n" +
                    "[Position] Route emotes and arrival celebrations can surface once the store data is wired.";
            }

            if (IsStableDisplayOnlyUnit(unit))
            {
                return
                    "[Skins] Saddle kits, barding, horse armor, and elite stable dyes fit this branch.\n" +
                    "[Abilities] Mounted charges, rear kicks, and cavalry flourish reels can be merchandised here later.\n" +
                    "[Status] Stable store slots are layout-ready even though live mounted combat is still pending.";
            }

            string signatureMove = BuildSpecialPreviewButtonLabel(unit);
            return
                $"[Abilities] {signatureMove}, whirlwind strikes, backflip slashes, shield crashes, and other alternate move reels can live here.\n" +
                "[Skins] Armor variants, cloaks, helmets, heraldry, weapon swaps, and material dyes fit the showcase.\n" +
                "[Victory] Defeat monologues, taunts, dances, and coordinated finishers can be sold per unit line.\n" +
                "[Position] Banner calls, synchronized emotes, and squad coordination sets can sit beside the combat loadout once store hooks are live.";
        }

        bool TryResolvePreviewSfx(RaceProgressionUnitDefinition unit, out AudioManager.SFX sfx, out string label)
        {
            sfx = default;
            label = null;

            if (unit == null || unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return false;

            string unitId = NormalizeTechTreeKey(unit.Id);
            string laneId = NormalizeTechTreeKey(unit.LaneId);
            string catalogKey = NormalizeTechTreeKey(ResolveTechTreeCatalogKey(unit) ?? unit.PortraitKey);

            if (unitId == "ballista" || unitId == "ballista_siege")
            {
                sfx = AudioManager.SFX.BallistaShoot;
                label = "Siege Bolt";
                return true;
            }

            if (unitId == "cannon" || unitId == "cannon_siege")
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

            switch (catalogKey)
            {
                case "tt_archer":
                case "tt_crossbowman":
                case "tt_mounted_scout":
                    sfx = AudioManager.SFX.ArcherShoot;
                    label = "Arrow Volley";
                    return true;
                case "tt_mage":
                case "tt_mounted_mage":
                case "tt_mounted_king":
                    sfx = AudioManager.SFX.MageShoot;
                    label = "Arcane Cast";
                    return true;
                case "tt_priest":
                case "tt_high_priest":
                case "tt_mounted_priest":
                    sfx = AudioManager.SFX.MageShoot;
                    label = "Sacred Cast";
                    return true;
                case "tt_peasant":
                case "tt_scout":
                case "tt_light_infantry":
                case "tt_mounted_knight":
                case "tt_spearman":
                case "tt_halberdier":
                case "tt_light_cavalry":
                case "tt_heavy_infantry":
                case "tt_heavy_swordman":
                case "tt_heavy_cavalry":
                case "tt_mounted_paladin":
                case "tt_king":
                case "tt_paladin":
                case "tt_commander":
                    sfx = AudioManager.SFX.FighterSlash;
                    label = "Steel Clash";
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

        static bool TryResolvePreviewVoice(RaceProgressionUnitDefinition unit, out string label)
        {
            label = null;

            MLUnit previewUnit = BuildPreviewVoiceUnit(unit);
            if (previewUnit == null || !UnitVoiceLibrary.HasVoiceProfile(previewUnit))
                return false;

            label = previewUnit.isHero
                ? $"{unit.DisplayName} Voice"
                : $"{unit.DisplayName} Bark";
            return true;
        }

        static bool TryPlayPreviewVoice(RaceProgressionUnitDefinition unit)
        {
            MLUnit previewUnit = BuildPreviewVoiceUnit(unit);
            return previewUnit != null && UnitVoiceLibrary.TryPlay(previewUnit, UnitVoiceCue.Attack, 0.9f);
        }

        static bool TryPlayPreviewCombatSfx(RaceProgressionUnitDefinition unit, out string label)
        {
            label = null;

            MLUnit previewUnit = BuildPreviewVoiceUnit(unit);
            UnitCombatSfxLibrary.ResolvedProfile profile = UnitCombatSfxLibrary.ResolveForUnit(null, previewUnit);
            if (previewUnit == null || profile == null || !UnitCombatSfxLibrary.HasGeneratedClips(profile, UnitCombatSfxCue.Attack))
                return false;

            UnitCombatSfxPlaybackResult result = UnitCombatSfxLibrary.TryPlay(
                profile,
                previewUnit.id,
                UnitCombatSfxCue.Attack,
                Time.unscaledTime,
                0.9f,
                bypassChance: true);
            if (result != UnitCombatSfxPlaybackResult.Played)
                return false;

            label = ResolvePreviewCombatSfxLabel(profile);
            return true;
        }

        static string ResolvePreviewCombatSfxLabel(UnitCombatSfxLibrary.ResolvedProfile profile)
        {
            string profileKey = profile != null ? (profile.ProfileKey ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
            return profileKey switch
            {
                "light_melee" => "Light Melee Clash",
                "heavy_melee" => "Heavy Melee Cleave",
                "polearm" => "Polearm Strike",
                "bow" => "Bow Release",
                "crossbow" => "Crossbow Fire",
                "arcane" => "Arcane Cast",
                "support" => "Holy Cast",
                _ => "Combat SFX",
            };
        }

        static MLUnit BuildPreviewVoiceUnit(RaceProgressionUnitDefinition unit)
        {
            if (unit == null || unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
                return null;

            string unitId = NormalizeTechTreeKey(unit.Id);
            string catalogKey = NormalizeTechTreeKey(ResolveTechTreeCatalogKey(unit) ?? unit.PortraitKey);
            string heroKey = unitId switch
            {
                "king" => "king",
                "paladin" => "paladin",
                "bishop" => "bishop",
                "commander" => "bishop",
                _ => null,
            };

            return new MLUnit
            {
                id = $"preview:{unitId ?? catalogKey ?? "unknown"}",
                type = catalogKey,
                unitTypeKey = catalogKey,
                catalogUnitKey = catalogKey,
                skinKey = catalogKey,
                isHero = !string.IsNullOrWhiteSpace(heroKey),
                heroKey = heroKey,
            };
        }

        string BuildUnitDetailsBodyText(RaceProgressionUnitDefinition unit)
        {
            if (unit == null)
                return "Tech tree entry unavailable.";

            if (unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep)
            {
                return
                    $"{NormalizeDossierValue(unit.Description, "This tile marks a branch requirement.")}\n\n" +
                    "Field note: This node exists to show the gate for the next unlock.\n\n" +
                    BuildUnitDetailsBodySuffix(unit);
            }

            if (unit.CardDisplay != null)
            {
                return
                    $"{NormalizeDossierValue(unit.Description, "Fortress upgrade dossier pending.")}\n\n" +
                    $"Branch: {HumanizeLabel(unit.LaneId)} {NormalizeDossierValue(BuildCompactBuildingTierValue(unit))}.\n\n" +
                    BuildUnitDetailsBodySuffix(unit);
            }

            var sections = new List<string>
            {
                NormalizeDossierValue(unit.Description, "Chronicle note pending."),
                $"Origin: {BuildUnitOriginText(unit)}",
                $"Battlefield role: {BuildUnitSkillText(unit)}",
            };

            if (TryGetCatalogEntry(unit, out var catalog)
                && !string.IsNullOrWhiteSpace(catalog.description)
                && !string.Equals(catalog.description.Trim(), unit.Description?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                sections.Add($"Current field note: {catalog.description.Trim()}");
            }

            string runtimeStatus = BuildUnitRuntimeStatusText(unit);
            if (!string.IsNullOrWhiteSpace(runtimeStatus))
                sections.Add(runtimeStatus.Replace("Runtime note: ", "Field note: "));

            string progressionAudit = BuildUnitProgressionAuditText(unit);
            if (!string.IsNullOrWhiteSpace(progressionAudit))
                sections.Add(progressionAudit);

            sections.Add(BuildUnitDetailsBodySuffix(unit));
            return string.Join("\n\n", sections);
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
                return "Runtime note: Market runners are live, loop through the Rear Gate, and generate gold on completed route laps.";

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
                : null;
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

            bool premiumShellPresentation = UsePremiumShellPresentation();
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
                _btnPrimaryAction.gameObject.SetActive(false);
                if (premiumShellPresentation && _btnSecondaryAction != null)
                    ApplyLobbyButtonStyle(_btnSecondaryAction, ClassicRpgButtonSkin.MiniBrown, 44f, 156f);
                return;
            }

            _btnPrimaryAction.gameObject.SetActive(true);
            if (_txtPrimaryAction != null)
            {
                _txtPrimaryAction.text = _mode == ProgressionViewerMode.LobbyViewer
                    ? "Close"
                    : (_state == PhaseState.Active && _selectedRace != null ? "Confirm Ready" : "Waiting...");
            }

            _btnPrimaryAction.interactable = _mode == ProgressionViewerMode.LobbyViewer
                || (_state == PhaseState.Active && _selectedRace != null);

            if (premiumShellPresentation)
            {
                if (_btnSecondaryAction != null)
                    ApplyLobbyButtonStyle(_btnSecondaryAction, ClassicRpgButtonSkin.MiniBrown, 44f, 156f);

                var primarySkin = _mode != ProgressionViewerMode.LobbyViewer && _state == PhaseState.Active && _selectedRace != null
                    ? ClassicRpgButtonSkin.MiniGreen
                    : ClassicRpgButtonSkin.MiniGold;
                ApplyLobbyButtonStyle(_btnPrimaryAction, primarySkin, 44f, 186f);
            }
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
            SceneEventSystemUtility.EnsureSceneLocal(manager, "LoadoutEventSystem", "RaceProgression");
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

        bool TryGetReadyPortraitTexture(string key, out Texture2D portrait)
        {
            portrait = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (_portraitCache.TryGetValue(key, out portrait) && portrait != null)
                return true;

            if (ShouldUseRuntimeSkinPortrait(key))
                return false;

            var remoteContent = RemoteContentManager.Instance;
            string portraitLookupKey = ResolvePortraitLookupKey(key);
            if (remoteContent != null
                && !string.IsNullOrWhiteSpace(portraitLookupKey)
                && remoteContent.TryGetLoadedPortraitTexture(portraitLookupKey, out portrait)
                && portrait != null)
            {
                _portraitCache[key] = portrait;
                return true;
            }

            portrait = null;
            return false;
        }

        void StartPortraitCapture(string key, RawImage target)
        {
            if (target == null || string.IsNullOrWhiteSpace(key))
                return;

            if (TryGetReadyPortraitTexture(key, out var cached))
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

            string blockingIssue = ValidateRequiredRuntimePortraitSource(key);
            if (string.IsNullOrWhiteSpace(blockingIssue))
                yield break;

            Debug.LogError($"[RaceProgression] {blockingIssue}");
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

        void SetPrepOverlayText(string status, string detail)
        {
            ShowPrepOverlay();
            if (_txtPrepStatus != null && !string.IsNullOrWhiteSpace(status))
                _txtPrepStatus.text = status;
            if (_txtPrepDetail != null && !string.IsNullOrWhiteSpace(detail))
                _txtPrepDetail.text = detail;
        }

        void UpdatePrepOverlayStatus(string detail)
        {
            if (_mode != ProgressionViewerMode.PreMatchConfirm)
                return;

            SetPrepOverlayText(null, detail);
        }

        void ShowWaitingForMatchOverlay()
        {
            SetPrepOverlayText("Preparing Battlefield", "Waiting for players...");
        }

        void BuildPlayerPanel(Transform parent)
        {
            if (_playerPanelRoot != null || parent == null)
                return;

            _playerPanelRoot = new GameObject("Panel_Players", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
            _playerPanelRoot.transform.SetParent(parent, false);
            var panelLayoutElement = _playerPanelRoot.GetComponent<LayoutElement>();
            panelLayoutElement.preferredHeight = 56f;
            ClassicRpgUiRuntime.ApplyPanel(_playerPanelRoot.GetComponent<Image>(), ClassicRpgPanelSkin.MainMenuBar, false, new Color(1f, 1f, 1f, 0.92f));

            var layout = _playerPanelRoot.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 8f;
            layout.padding = new RectOffset(12, 12, 8, 8);

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
            if (parent != null && parent.GetComponent<LayoutGroup>() == null)
                ClassicRpgUiRuntime.Stretch(section.GetComponent<RectTransform>());
            var sectionImage = section.GetComponent<Image>();
            sectionImage.color = color;
            if (UsePremiumShellPresentation())
            {
                ClassicRpgUiRuntime.ApplyPanel(sectionImage, ClassicRpgPanelSkin.PortraitBackdrop, true, color);
                EnsureDecorativeFrame(section.GetComponent<RectTransform>());
            }
            else
            {
                ClassicRpgUiRuntime.ApplyPanel(sectionImage, ClassicRpgPanelSkin.PaperMedium, true, color);
            }
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
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            ClassicRpgUiRuntime.ApplyTextStyle(
                label,
                fontStyle == FontStyles.Bold ? ClassicRpgTextStyle.SectionHeader : ClassicRpgTextStyle.Body,
                alignment,
                color);
            label.fontStyle = fontStyle;
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
            ClassicRpgUiRuntime.ApplyPanel(go.GetComponent<Image>(), ClassicRpgPanelSkin.Frame, true, color);
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
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            ClassicRpgUiRuntime.ApplyTextStyle(text, ClassicRpgTextStyle.ButtonLabel, TextAlignmentOptions.Center, Color.white, allowWrap: false);
            return button;
        }
    }
}
