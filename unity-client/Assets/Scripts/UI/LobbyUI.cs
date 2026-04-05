// LobbyUI.cs — Wizard lobby (queue/lobby only; loadout selection happens in-match).
//
// SCENE SETUP (Lobby.unity):
//
//   Canvas
//   ├── Panel_Step3_Type              — shown first: pick Ranked / Casual / Private
//   │   ├── Btn_Ranked / Btn_Casual / Btn_PrivateLobby
//   │   ├── Btn_JoinByCode / Input_JoinCode / Btn_JoinConfirm
//   │   └── Btn_Back_Step3
//   ├── Panel_Step2_Format            — shown second: pick 1v1 or 2v2
//   │   ├── Btn_1v1 / Btn_2v2 / Btn_Back_Step2
//   ├── Panel_Step4A_Queue
//   │   ├── Txt_QueueStatus
//   │   └── Btn_CancelQueue
//   ├── Panel_Step4B_Lobby
//   │   ├── Txt_LobbyCode / Txt_MemberList
//   │   ├── Btn_Ready / TxtReadyBtn / Btn_Launch
//   │   ├── Btn_AddBot_Easy/Medium/Hard / Btn_Leave
//   ├── Panel_Leaderboard             — Phase U8: scrollable leaderboard overlay
//   │   ├── Txt_LeaderboardList       — TMP_Text (scrollable)
//   │   └── Btn_HideLeaderboard
//   ├── Btn_ToggleLeaderboard         — always visible tab
//   ├── Txt_SeasonInfo (optional)     — "Season 1 — ends Mar 31"
//   ├── Txt_Status
//   └── Txt_DisplayName (optional)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using TMPro;
using Newtonsoft.Json;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class LobbyUI : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Step 2 — Format (shown second)")]
        public GameObject Panel_Step2_Format;
        public Button     Btn_1v1;
        public Button     Btn_2v2;
        public Button     Btn_Back_Step2;

        [Header("Step 3 — Type (shown first)")]
        public GameObject     Panel_Step3_Type;
        public Button         Btn_Ranked;
        public Button         Btn_Casual;
        public Button         Btn_PrivateLobby;
        public Button         Btn_JoinByCode;
        public TMP_InputField Input_JoinCode;
        public Button         Btn_JoinConfirm;
        public Button         Btn_Back_Step3;

        [Header("Step 4A — Queue")]
        public GameObject Panel_Step4A_Queue;
        public TMP_Text   Txt_QueueStatus;
        public Button     Btn_CancelQueue;

        [Header("Step 4B — Lobby")]
        public GameObject Panel_Step4B_Lobby;
        public TMP_Text   Txt_LobbyCode;
        public TMP_Text   Txt_MemberList;
        public Button     Btn_Ready;
        public TMP_Text   TxtReadyBtn;
        public Button     Btn_Launch;
        public Button     Btn_AddBot_Easy;
        public Button     Btn_AddBot_Medium;
        public Button     Btn_AddBot_Hard;
        public Button     Btn_Leave;

        [Header("Leaderboard (Phase U8)")]
        public GameObject Panel_Leaderboard;
        public TMP_Text   Txt_LeaderboardList;
        public Button     Btn_ToggleLeaderboard;
        public Button     Btn_HideLeaderboard;

        [Header("Season + Status")]
        public TMP_Text Txt_SeasonInfo;
        public TMP_Text TxtStatus;

        [Header("Display name (optional)")]
        public TMP_Text Txt_DisplayName;

        [Header("Settings Panel")]
        [SerializeField] bool showSettingsPanel = true;
        [SerializeField] float settingsButtonSize = 46f;
        [SerializeField] Vector2 settingsPanelSize = new Vector2(172f, 228f);
        [SerializeField] float settingsTopInset = 14f;
        [SerializeField] float settingsRightInset = 10f;
        [SerializeField] float settingsPanelGap = 10f;
        [SerializeField] float settingsButtonSpacing = 6f;
        [SerializeField] float settingsValueWidth = 36f;
        [SerializeField] float settingsZoomStep = 2f;
        [SerializeField] float settingsTiltStep = 8f;
        [SerializeField] float settingsRotateStep = 20f;
        [SerializeField] float typePanelSpacing = 12f;

        // ── Wizard state ──────────────────────────────────────────────────────
        const  string _gameType = "line_wars";
        const  int RankedQueueCasualRequirement = 5;
        const  string WinterBackdropResourcePath = "UI/Lobby/WinterForestBackdrop";
        static Sprite _winterBackdropSprite;
        string _matchFormat = "ffa";
        bool   _pendingRanked;
        bool   _showingJoinInput;
        int    _rankedCasualMatchesCompleted;
        int    _rankedCasualMatchesRequired = RankedQueueCasualRequirement;
        bool   _rankedQueueUnlocked;
        bool   _rankedEligibilityKnown;
        Coroutine _rankedEligibilityRoutine;

        // ── Lobby state ───────────────────────────────────────────────────────
        bool          _isHost;
        bool          _isReady;
        LobbySnapshot _currentLobby;

        // ── Queue state ───────────────────────────────────────────────────────
        float _queueElapsed;
        bool  _inQueue;
        bool  _awaitingLoadoutScene; // true after match_found, until ml_loadout_phase_start arrives
        Coroutine _lobbyWarmupRoutine;
        RectTransform _canvasRect;
        RectTransform _settingsPanelRoot;
        RectTransform _settingsOverlayPanelRoot;
        RectTransform _progressionButtonRoot;
        RectTransform _progressionOverlayRoot;
        RectTransform _premiumTypeStack;
        RectTransform _premiumStageRoot;
        RectTransform _premiumMainColumn;
        RectTransform _premiumRailColumn;
        TMP_Text _premiumScreenTitle;
        TMP_Text _premiumScreenSubtitle;
        bool _premiumPresentationApplied;
        bool _premiumCompactLayout;
        LoadoutPhaseManager _progressionViewer;
        TMP_Text _txtSettingsTiltValue;
        TMP_Text _txtSettingsZoomValue;
        TMP_Text _txtSettingsRotationValue;
        TMP_Text _txtSettingsSfxValue;
        TMP_Text _txtSettingsMusicValue;
        TMP_Text _txtSettingsEngagementValue;
        TMP_Text _txtSettingsAttackRangeValue;
        TMP_Text _txtSettingsHealthBarsValue;
        TMP_Text _txtSettingsTooltipsValue;
        GameObject _settingsOverlay;
        Button _settingsMenuButton;
        TMP_Text _settingsMenuButtonLabel;
        bool _loggingOut;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            EnsureEventSystem();
            CacheCanvas();

            var nm = NetworkManager.Instance;
            if (nm != null)
            {
                nm.OnConnected      += HandleConnected;
                nm.OnDisconnected   += HandleDisconnected;
                nm.OnQueueStatus    += HandleQueueStatus;
                nm.OnMatchFound            += HandleMatchFound;
                nm.OnMLLoadoutPhaseStart   += HandleLoadoutPhaseStart;
                nm.OnLobbyCreated   += HandleLobbyCreated;
                nm.OnLobbyJoined    += HandleLobbyJoined;
                nm.OnLobbyUpdate    += HandleLobbyUpdate;
                nm.OnLobbyLeft      += HandleLobbyLeft;
                nm.OnLobbyError     += HandleLobbyError;
                nm.OnErrorMsg       += HandleError;
            }
            AuthManager.AuthStateChanged += HandleAuthStateChanged;

            // Step 3 — Type (shown first)
            Btn_Ranked.onClick.AddListener(OnQueueRanked);
            Btn_Casual.onClick.AddListener(OnQueueCasual);
            Btn_PrivateLobby.onClick.AddListener(OnCreatePrivateLobby);
            Btn_JoinByCode.onClick.AddListener(OnShowJoinInput);
            Btn_JoinConfirm.onClick.AddListener(OnJoinByCode);
            if (Btn_Back_Step3 != null) Btn_Back_Step3.onClick.AddListener(HideJoinInput);

            // Step 2 — Format (shown second, back returns to Type)
            Btn_1v1.onClick.AddListener(() => SelectFormat("ffa"));
            Btn_2v2.onClick.AddListener(() => SelectFormat("ffa"));
            Btn_Back_Step2.onClick.AddListener(() =>
            {
                GoToStep(2);
                SetStatus("Choose a queue.");
            });

            // Step 4A
            Btn_CancelQueue.onClick.AddListener(OnCancelQueue);

            // Step 4B
            Btn_Ready.onClick.AddListener(OnToggleReady);
            Btn_Launch.onClick.AddListener(OnLaunch);
            Btn_AddBot_Easy.onClick.AddListener(() => { ActionSender.LobbyAddBot("easy");   Play(AudioManager.SFX.ButtonClick); });
            Btn_AddBot_Medium.onClick.AddListener(() => { ActionSender.LobbyAddBot("medium"); Play(AudioManager.SFX.ButtonClick); });
            Btn_AddBot_Hard.onClick.AddListener(() => { ActionSender.LobbyAddBot("hard");   Play(AudioManager.SFX.ButtonClick); });
            Btn_Leave.onClick.AddListener(OnLeaveLobby);

            // Leaderboard (Phase U8)
            if (Btn_ToggleLeaderboard != null) Btn_ToggleLeaderboard.onClick.AddListener(ShowLeaderboard);
            if (Btn_HideLeaderboard   != null) Btn_HideLeaderboard.onClick.AddListener(HideLeaderboard);
            if (Panel_Leaderboard     != null) Panel_Leaderboard.SetActive(false);

            if (Txt_DisplayName != null)
                Txt_DisplayName.text = AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Guest";

            GoToStep(2);
            SetStatus(nm?.IsConnected == true ? "Choose a queue." : "Connecting...");
            BuildSettingsPanel();
            BuildProgressionButton();
            ApplyPremiumPresentation();
            BeginLobbyT1Warmup();
            BeginRankedEligibilityRefresh();

            // Fetch leaderboard + season info in background
            StartCoroutine(FetchLeaderboard());
            StartCoroutine(FetchSeasonInfo());
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Instance;
            if (nm != null)
            {
                nm.OnConnected      -= HandleConnected;
                nm.OnDisconnected   -= HandleDisconnected;
                nm.OnQueueStatus    -= HandleQueueStatus;
                nm.OnMatchFound            -= HandleMatchFound;
                nm.OnMLLoadoutPhaseStart   -= HandleLoadoutPhaseStart;
                nm.OnLobbyCreated   -= HandleLobbyCreated;
                nm.OnLobbyJoined    -= HandleLobbyJoined;
                nm.OnLobbyUpdate    -= HandleLobbyUpdate;
                nm.OnLobbyLeft      -= HandleLobbyLeft;
                nm.OnLobbyError     -= HandleLobbyError;
                nm.OnErrorMsg       -= HandleError;
            }

            if (_lobbyWarmupRoutine != null)
                StopCoroutine(_lobbyWarmupRoutine);

            if (_rankedEligibilityRoutine != null)
                StopCoroutine(_rankedEligibilityRoutine);

            AuthManager.AuthStateChanged -= HandleAuthStateChanged;
            CloseProgressionViewer();
        }

        void Update()
        {
            if (_inQueue)
            {
                _queueElapsed += Time.deltaTime;
                RefreshQueueDisplay(null);
            }

            RefreshSettingsPanelLayout();
            RefreshSettingsPanelValues();
        }

        // ── Step navigation ───────────────────────────────────────────────────
        // Steps: 2=Type (first), 3=Format (second), 4=Queue, 5=Lobby
        void GoToStep(int step)
        {
            Panel_Step3_Type.SetActive(step == 2);
            Panel_Step2_Format.SetActive(step == 3);
            Panel_Step4A_Queue.SetActive(step == 4);
            Panel_Step4B_Lobby.SetActive(step == 5);

            if (step == 2) RefreshTypeButtons();
            if (step == 3) RefreshFormatButtons();
            RefreshPremiumStepCopy(step);
        }

        // ── Step 3 — Type (shown first) ───────────────────────────────────────
        void RefreshTypeButtons()
        {
            SetButtonLabel(Btn_Ranked, "Ranked Match");
            SetButtonLabel(Btn_Casual, "Casual Match");
            SetButtonLabel(Btn_PrivateLobby, "Private Lobby");
            _showingJoinInput = false;
            Btn_Ranked.gameObject.SetActive(true);
            Btn_Casual.gameObject.SetActive(true);
            if (Btn_PrivateLobby != null) Btn_PrivateLobby.gameObject.SetActive(true);
            if (Btn_JoinByCode != null) Btn_JoinByCode.gameObject.SetActive(true);
            if (_progressionButtonRoot != null) _progressionButtonRoot.gameObject.SetActive(true);
            if (Btn_Back_Step3 != null) Btn_Back_Step3.gameObject.SetActive(false);
            if (Input_JoinCode != null)  Input_JoinCode.gameObject.SetActive(false);
            if (Btn_JoinConfirm != null) Btn_JoinConfirm.gameObject.SetActive(false);
            RefreshRankedQueueButtonState();
            RefreshTypePanelLayout();
        }

        void OnQueueRanked()
        {
            if (!AuthManager.IsAuthenticated)
            {
                SetStatus("Sign in to track your war record. Ranked queue opens after 5 casual matches.");
                Play(AudioManager.SFX.Error);
                return;
            }

            if (!_rankedEligibilityKnown)
            {
                BeginRankedEligibilityRefresh();
                SetStatus($"Checking war record. Ranked queue opens after {_rankedCasualMatchesRequired} casual matches.");
                Play(AudioManager.SFX.Error);
                return;
            }

            if (!_rankedQueueUnlocked)
            {
                SetStatus(BuildRankedQueueLockedStatus());
                Play(AudioManager.SFX.Error);
                return;
            }

            Play(AudioManager.SFX.ButtonClick);
            _pendingRanked = true;
            EnterQueue();
        }

        void OnQueueCasual()
        {
            Play(AudioManager.SFX.ButtonClick);
            _pendingRanked = false;
            EnterQueue();
        }

        // ── Step 2 — Format (shown second) ───────────────────────────────────
        void SelectFormat(string format)
        {
            _matchFormat = "ffa";
            Play(AudioManager.SFX.ButtonClick);
            EnterQueue();
        }

        void RefreshFormatButtons()
        {
            Btn_1v1.gameObject.SetActive(false);
            Btn_2v2.gameObject.SetActive(false);
            if (Btn_Back_Step2 != null) Btn_Back_Step2.gameObject.SetActive(false);
        }

        void EnterQueue()
        {
            _matchFormat = "ffa";
            ActionSender.QueueEnter(_gameType, _matchFormat, ranked: _pendingRanked);
            _inQueue      = true;
            _queueElapsed = 0f;
            GoToStep(4);
            string queueLabel = _pendingRanked ? "public" : "casual";
            SetStatus($"Finding {queueLabel} survival match...");
        }

        void OnCreatePrivateLobby()
        {
            Play(AudioManager.SFX.ButtonClick);
            _matchFormat = "ffa";
            CreatePrivateLobby();
        }

        void CreatePrivateLobby()
        {
            ActionSender.LobbyCreate(_gameType, "ffa", "ffa", DisplayName);
            SetStatus("Creating lobby...");
        }

        void OnShowJoinInput()
        {
            Play(AudioManager.SFX.ButtonClick);
            _showingJoinInput = true;
            if (Btn_JoinByCode != null) Btn_JoinByCode.gameObject.SetActive(false);
            if (_progressionButtonRoot != null) _progressionButtonRoot.gameObject.SetActive(false);
            if (Input_JoinCode != null)  Input_JoinCode.gameObject.SetActive(true);
            if (Btn_JoinConfirm != null) Btn_JoinConfirm.gameObject.SetActive(true);
            if (Btn_Back_Step3 != null) Btn_Back_Step3.gameObject.SetActive(true);
            RefreshTypePanelLayout();
            Input_JoinCode?.ActivateInputField();
        }

        void HideJoinInput()
        {
            _showingJoinInput = false;
            if (Btn_JoinByCode != null) Btn_JoinByCode.gameObject.SetActive(true);
            if (_progressionButtonRoot != null) _progressionButtonRoot.gameObject.SetActive(true);
            if (Input_JoinCode != null)  Input_JoinCode.gameObject.SetActive(false);
            if (Btn_JoinConfirm != null) Btn_JoinConfirm.gameObject.SetActive(false);
            if (Btn_Back_Step3 != null) Btn_Back_Step3.gameObject.SetActive(false);
            RefreshTypePanelLayout();
        }

        void OnJoinByCode()
        {
            if (Input_JoinCode == null) return;
            string code = Input_JoinCode.text.Trim().ToUpper();
            if (code.Length != 6) { SetStatus("Code must be 6 characters."); Play(AudioManager.SFX.Error); return; }
            Play(AudioManager.SFX.ButtonClick);
            ActionSender.LobbyJoin(code, DisplayName);
            SetStatus($"Joining lobby {code}...");
        }

        // ── Step 4A — Queue ───────────────────────────────────────────────────
        void OnCancelQueue()
        {
            Play(AudioManager.SFX.ButtonClick);
            ActionSender.QueueLeave();
            _inQueue = false;
            CloseProgressionViewer();
            GoToStep(2);
            SetStatus("Queue cancelled. Choose a queue.");
        }

        void RefreshQueueDisplay(QueueStatusPayload p)
        {
            if (Txt_QueueStatus == null) return;
            int secs = p != null ? p.elapsed : (int)_queueElapsed;
            int m = secs / 60, s = secs % 60;
            string size = (p != null && p.queueSize > 0) ? $" ({p.queueSize} in queue)" : "";
            Txt_QueueStatus.text = $"Finding match...{size}\n{m:00}:{s:00}";
        }

        // ── Step 4B — Lobby ───────────────────────────────────────────────────
        void ShowLobby(LobbySnapshot lobby)
        {
            _currentLobby = lobby;
            _isHost = lobby.hostSocketId == NetworkManager.Instance.MySocketId;
            _isReady = false;

            if (Txt_LobbyCode != null) Txt_LobbyCode.text = $"Code: {lobby.code}";
            RefreshMemberList(lobby);
            RefreshLobbyButtons();
            GoToStep(5);
        }

        void RefreshMemberList(LobbySnapshot lobby)
        {
            if (Txt_MemberList == null) return;
            var sb = new StringBuilder();
            if (lobby.members != null)
            {
                foreach (var mem in lobby.members)
                {
                    string you  = mem.socketId == NetworkManager.Instance.MySocketId ? " (You)" : "";
                    string host = mem.isHost  ? " [Host]"  : "";
                    string rdy  = mem.isReady ? " [Ready]" : "";
                    sb.AppendLine($"{mem.name}{you}{host}{rdy}");
                }
            }
            if (lobby.botSlots != null)
                foreach (var bot in lobby.botSlots)
                    sb.AppendLine($"CPU ({bot.difficulty}) [Bot]");
            Txt_MemberList.text = sb.ToString();
        }

        void RefreshLobbyButtons()
        {
            if (Btn_Launch != null)      Btn_Launch.gameObject.SetActive(_isHost);
            if (Btn_Ready != null)       Btn_Ready.gameObject.SetActive(!_isHost);
            if (Btn_AddBot_Easy != null) Btn_AddBot_Easy.gameObject.SetActive(_isHost);
            if (Btn_AddBot_Medium != null) Btn_AddBot_Medium.gameObject.SetActive(_isHost);
            if (Btn_AddBot_Hard != null) Btn_AddBot_Hard.gameObject.SetActive(_isHost);
            RefreshReadyButtonVisualState();
        }

        void OnToggleReady()
        {
            _isReady = !_isReady;
            RefreshReadyButtonVisualState();
            ActionSender.LobbyReady(_isReady);
            Play(AudioManager.SFX.ButtonClick);
        }

        void OnLaunch()
        {
            if (!_isHost) return;
            Play(AudioManager.SFX.ButtonClick);
            ActionSender.LobbyLaunch();
            SetStatus("Launching...");
        }

        void OnLeaveLobby()
        {
            Play(AudioManager.SFX.ButtonClick);
            ActionSender.LobbyLeave();
            _currentLobby = null;
            _isReady = false;
            GoToStep(2);
            SetStatus("Left lobby.");
        }

        // ── NetworkManager event handlers ─────────────────────────────────────
        void HandleConnected()
        {
            if (_loggingOut)
            {
                SetStatus("Returning to sign-in...");
                return;
            }

            BeginRankedEligibilityRefresh();
            SetStatus("Choose a queue.");
        }

        void HandleAuthStateChanged()
        {
            if (Txt_DisplayName != null)
                Txt_DisplayName.text = AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Guest";

            _rankedEligibilityKnown = !AuthManager.IsAuthenticated;
            _rankedQueueUnlocked = false;
            _rankedCasualMatchesCompleted = 0;
            _rankedCasualMatchesRequired = RankedQueueCasualRequirement;
            RefreshRankedQueueButtonState();
            BeginRankedEligibilityRefresh();
        }

        void HandleDisconnected()
        {
            _inQueue = false;
            GoToStep(2);
            if (_loggingOut)
            {
                SetStatus("Returning to sign-in...");
                return;
            }

            SetStatus("Disconnected. Reconnecting...");
        }

        void HandleQueueStatus(QueueStatusPayload p)
        {
            if (p.status == "idle")
            {
                _inQueue = false;
                GoToStep(2);
                return;
            }
            _inQueue = true;
            if (_queueElapsed == 0f && p.elapsed > 0) _queueElapsed = p.elapsed;
            if (Panel_Step4A_Queue.activeSelf) RefreshQueueDisplay(p);
        }

        void HandleMatchFound(MatchFoundPayload p)
        {
            _inQueue = false;
            CloseProgressionViewer();

            // Phase U8 — store reconnect token so we can recover from a disconnect mid-game
            if (!string.IsNullOrEmpty(p.reconnectToken))
            {
                PlayerPrefs.SetString("reconnect_token",   p.reconnectToken);
                PlayerPrefs.SetString("reconnect_code",    p.roomCode ?? "");
                PlayerPrefs.SetInt   ("reconnect_lane",    p.laneIndex);
                PlayerPrefs.SetString("reconnect_gametype", p.gameType ?? _gameType);
                PlayerPrefs.Save();
                Debug.Log("[Lobby] Stored reconnect token");
            }

            // Stay in Lobby until ml_loadout_phase_start arrives — transitioning
            // immediately causes a socket disconnect while the scene loads, so
            // PendingLoadoutPhase ends up null.  The flag is cleared in HandleLoadoutPhaseStart.
            _awaitingLoadoutScene = true;

            // Emit ml_loadout_ready once critical content is ready.
            // We do this here (before the Loadout scene loads) to break the circular
            // dependency: server Barrier 1 waits for ml_loadout_ready before emitting
            // ml_loadout_phase_start, which is what causes the Loadout scene to load.
            StartCoroutine(WaitForContentAndEmitLoadoutReady());

            // If PendingLoadoutPhase was already cached by NetworkManager (extremely
            // fast server), handle it now rather than waiting for the event.
            var nm = NetworkManager.Instance;
            if (nm != null && nm.PendingLoadoutPhase != null)
                HandleLoadoutPhaseStart(nm.PendingLoadoutPhase);
        }

        IEnumerator WaitForContentAndEmitLoadoutReady()
        {
            // Wait for any in-progress lobby warmup to finish first
            while (_lobbyWarmupRoutine != null)
                yield return null;

            // If critical content wasn't completed during lobby warmup, run it now
            var rc = RemoteContentManager.EnsureInstance();
            if (!rc.HasCompletedLoadoutPreload)
            {
                Debug.Log("[Lobby] Loadout content not yet ready — running preload before emitting loadout ready");
                yield return rc.PreloadLoadoutContentForSession(requester: "LobbyUI.MatchReady");
            }

            Debug.Log("[Lobby] Emitting ml_loadout_ready");
            NetworkManager.Instance?.RequestLoadoutReady();
        }

        void HandleLoadoutPhaseStart(MLLoadoutPhaseStartPayload payload)
        {
            if (!_awaitingLoadoutScene) return;
            _awaitingLoadoutScene = false;
            CloseProgressionViewer();
            LoadingScreen.LoadSceneWithRemoteContentGate(
                "Loadout",
                portraitKeys: ExtractProgressionPortraitKeys(payload));
        }

        void HandleLobbyCreated(LobbyCreatedPayload p)
        {
            ShowLobby(p.lobby);
            SetStatus("Lobby created. Waiting for players...");
        }

        void HandleLobbyJoined(LobbyJoinedPayload p)
        {
            ShowLobby(p.lobby);
            SetStatus("Joined lobby.");
        }

        void HandleLobbyUpdate(LobbyUpdatePayload p)
        {
            if (p?.lobby == null) return;
            _currentLobby = p.lobby;
            _isHost = p.lobby.hostSocketId == NetworkManager.Instance.MySocketId;
            RefreshMemberList(p.lobby);
            RefreshLobbyButtons();
        }

        void HandleLobbyLeft(LobbyLeftPayload _)
        {
            _currentLobby = null;
            _isReady = false;
            GoToStep(2);
        }

        void HandleLobbyError(LobbyErrorPayload p)
        {
            SetStatus($"Lobby error: {p.message}");
            Play(AudioManager.SFX.Error);
        }

        void HandleError(ErrorPayload p) => SetStatus($"Error: {p.message}");

        void BeginRankedEligibilityRefresh()
        {
            if (_rankedEligibilityRoutine != null)
                StopCoroutine(_rankedEligibilityRoutine);

            _rankedEligibilityRoutine = StartCoroutine(FetchRankedEligibility());
        }

        IEnumerator FetchRankedEligibility()
        {
            if (!AuthManager.IsAuthenticated)
            {
                _rankedEligibilityKnown = true;
                _rankedQueueUnlocked = false;
                _rankedCasualMatchesCompleted = 0;
                _rankedCasualMatchesRequired = RankedQueueCasualRequirement;
                RefreshRankedQueueButtonState();
                _rankedEligibilityRoutine = null;
                yield break;
            }

            string url = BaseUrl + "/players/me";
            using var req = UnityWebRequest.Get(url);
            ApplyAuthorization(req);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Lobby] Ranked eligibility fetch failed: {req.error}");
                RefreshRankedQueueButtonState();
                _rankedEligibilityRoutine = null;
                yield break;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<PlayerProfileResponse>(req.downloadHandler.text);
                var progression = resp?.queue_progression;
                if (progression != null)
                {
                    _rankedCasualMatchesCompleted = Mathf.Max(0, progression.ranked_casual_matches_completed);
                    _rankedCasualMatchesRequired = Mathf.Max(1, progression.ranked_casual_matches_required);
                    _rankedQueueUnlocked = progression.ranked_queue_unlocked
                        || _rankedCasualMatchesCompleted >= _rankedCasualMatchesRequired;
                    _rankedEligibilityKnown = true;
                }
                else
                {
                    Debug.LogWarning("[Lobby] Ranked eligibility payload was missing queue progression data.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Lobby] Ranked eligibility parse failed: {ex.Message}");
            }

            RefreshRankedQueueButtonState();
            _rankedEligibilityRoutine = null;
        }

        // ── Leaderboard (Phase U8) ────────────────────────────────────────────
        void ShowLeaderboard()
        {
            if (Panel_Leaderboard != null) Panel_Leaderboard.SetActive(true);
            Play(AudioManager.SFX.ButtonClick);
        }

        void HideLeaderboard()
        {
            if (Panel_Leaderboard != null) Panel_Leaderboard.SetActive(false);
            Play(AudioManager.SFX.ButtonClick);
        }

        IEnumerator FetchLeaderboard()
        {
            if (Txt_LeaderboardList == null) yield break;

            string url = BaseUrl + "/leaderboard?mode=ffa_ranked&limit=20";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Lobby] Leaderboard fetch failed: {req.error}");
                yield break;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<LeaderboardResponse>(req.downloadHandler.text);
                if (resp?.entries == null) yield break;

                var sb = new StringBuilder();
                sb.AppendLine("<b>Leaderboard</b>");
                foreach (var e in resp.entries)
                {
                    string losses = e.losses > 0 ? $"  {e.wins}W/{e.losses}L" : "";
                    sb.AppendLine($"{e.rank,3}. {e.display_name,-20} {e.rating:F0}{losses}");
                }
                Txt_LeaderboardList.text = sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Lobby] Leaderboard parse error: {ex.Message}");
            }
        }

        // ── Season info (Phase U8) ────────────────────────────────────────────
        IEnumerator FetchSeasonInfo()
        {
            if (Txt_SeasonInfo == null) yield break;

            string url = BaseUrl + "/leaderboard?mode=ffa_ranked&limit=1";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) yield break;

            try
            {
                var resp = JsonConvert.DeserializeObject<LeaderboardResponse>(req.downloadHandler.text);
                if (resp?.season == null)
                {
                    Txt_SeasonInfo.text = "";
                    yield break;
                }
                Txt_SeasonInfo.text = resp.season.id.ToString();
            }
            catch
            {
                Txt_SeasonInfo.text = "";
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        string DisplayName => AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Player";

        void BeginLobbyT1Warmup()
        {
            if (_lobbyWarmupRoutine != null)
                StopCoroutine(_lobbyWarmupRoutine);

            _lobbyWarmupRoutine = StartCoroutine(WarmLobbyT1Content());
        }

        IEnumerator WarmLobbyT1Content()
        {
            var remoteContent = RemoteContentManager.EnsureInstance();
            yield return remoteContent.PreloadLoadoutContentForSession(requester: "LobbyUI.WarmLobbyLoadoutContent");

            if (!remoteContent.HasCompletedLoadoutPreload)
            {
                Debug.LogWarning($"[LobbyUI] Loadout warmup did not complete in lobby: {remoteContent.LastError}");
                if (!_inQueue && _currentLobby == null)
                    SetStatus("Remote loadout content failed to warm up. It will retry before loadout.");
            }

            yield return remoteContent.EnsureEnvironmentReady(
                RemoteContentManager.GameMlEnvironmentAddress,
                requester: "LobbyUI.WarmLobbyEnvironment");

            if (!remoteContent.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress))
            {
                Debug.LogWarning($"[LobbyUI] Environment warmup did not complete in lobby: {remoteContent.LastError}");
                if (!_inQueue && _currentLobby == null)
                    SetStatus("Remote environment failed to warm up. It will retry before match start.");
            }

            _lobbyWarmupRoutine = null;
        }

        void BuildProgressionButton()
        {
            if (_progressionButtonRoot != null)
            {
                Destroy(_progressionButtonRoot.gameObject);
                _progressionButtonRoot = null;
            }

            var joinButton = Btn_JoinByCode;
            if (joinButton == null)
                return;

            var joinRect = joinButton.GetComponent<RectTransform>();
            if (joinRect == null || joinRect.parent == null)
                return;

            var root = new GameObject("LobbyProgressionButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.transform.SetParent(joinRect.parent, false);
            root.name = "LobbyProgressionButton";
            var rect = root.GetComponent<RectTransform>();
            _progressionButtonRoot = rect;
            var button = root.GetComponent<Button>();
            var image = root.GetComponent<Image>();
            var joinImage = joinButton.GetComponent<Image>();
            if (button == null || image == null || joinImage == null)
            {
                Destroy(root);
                _progressionButtonRoot = null;
                return;
            }

            image.sprite = joinImage.sprite;
            image.overrideSprite = joinImage.overrideSprite;
            image.type = joinImage.type;
            image.preserveAspect = joinImage.preserveAspect;
            image.fillCenter = joinImage.fillCenter;
            image.fillMethod = joinImage.fillMethod;
            image.fillAmount = joinImage.fillAmount;
            image.fillClockwise = joinImage.fillClockwise;
            image.fillOrigin = joinImage.fillOrigin;
            image.useSpriteMesh = joinImage.useSpriteMesh;
            image.pixelsPerUnitMultiplier = joinImage.pixelsPerUnitMultiplier;
            image.material = joinImage.material;
            image.color = joinImage.color;
            image.raycastTarget = true;

            button.interactable = true;
            button.transition = joinButton.transition;
            button.colors = joinButton.colors;
            button.spriteState = joinButton.spriteState;
            button.animationTriggers = joinButton.animationTriggers;
            button.navigation = joinButton.navigation;
            button.targetGraphic = image;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OpenProgressionViewer);

            var templateText = joinButton.GetComponentInChildren<TMP_Text>(true);
            if (templateText != null)
            {
                var label = Instantiate(templateText.gameObject, root.transform, false);
                label.name = "Text";
                var labelRect = label.GetComponent<RectTransform>();
                if (labelRect != null)
                {
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;
                }

                foreach (var text in label.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.text = "Tech Tree";
                    text.raycastTarget = false;
                }
            }

            var joinSiblingIndex = joinRect.GetSiblingIndex();
            rect.SetSiblingIndex(joinSiblingIndex);
            rect.anchorMin = joinRect.anchorMin;
            rect.anchorMax = joinRect.anchorMax;
            rect.pivot = joinRect.pivot;
            rect.sizeDelta = joinRect.sizeDelta;
            rect.localScale = joinRect.localScale;
            rect.anchoredPosition = joinRect.anchoredPosition;
            root.SetActive(!_showingJoinInput);
            RefreshTypePanelLayout();
        }

        void RefreshTypePanelLayout()
        {
            if (_premiumTypeStack != null)
            {
                RebuildPremiumTypeStack();
                return;
            }

            if (Panel_Step3_Type == null)
                return;

            var panelRect = Panel_Step3_Type.GetComponent<RectTransform>();
            if (panelRect == null)
                return;

            var orderedRects = new List<RectTransform>(8);
            AddTypePanelRect(orderedRects, Btn_Ranked);
            AddTypePanelRect(orderedRects, Btn_Casual);
            AddTypePanelRect(orderedRects, Btn_PrivateLobby);

            if (_showingJoinInput)
            {
                AddTypePanelRect(orderedRects, Input_JoinCode);
                AddTypePanelRect(orderedRects, Btn_JoinConfirm);
                AddTypePanelRect(orderedRects, Btn_Back_Step3);
            }
            else
            {
                AddTypePanelRect(orderedRects, _progressionButtonRoot);
                AddTypePanelRect(orderedRects, Btn_JoinByCode);
            }

            if (orderedRects.Count == 0)
                return;

            float totalHeight = 0f;
            for (int i = 0; i < orderedRects.Count; i++)
                totalHeight += GetTypePanelRectHeight(orderedRects[i]);

            float gap = 0f;
            if (orderedRects.Count > 1)
                gap = Mathf.Min(typePanelSpacing, Mathf.Max(0f, (panelRect.rect.height - totalHeight) / (orderedRects.Count - 1)));

            float usedHeight = totalHeight + gap * Mathf.Max(0, orderedRects.Count - 1);
            float currentY = (usedHeight * 0.5f) - (GetTypePanelRectHeight(orderedRects[0]) * 0.5f);

            for (int i = 0; i < orderedRects.Count; i++)
            {
                var rect = orderedRects[i];
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, currentY);

                if (i < orderedRects.Count - 1)
                {
                    float currentHeight = GetTypePanelRectHeight(rect);
                    float nextHeight = GetTypePanelRectHeight(orderedRects[i + 1]);
                    currentY -= (currentHeight * 0.5f) + gap + (nextHeight * 0.5f);
                }
            }
        }

        void ApplyPremiumPresentation()
        {
            CacheCanvas();
            if (_canvasRect == null)
                return;

            ClassicRpgUiRuntime.ApplyCanvasScaler(_canvasRect.GetComponent<CanvasScaler>(), ClassicRpgUiRuntime.ReferenceResolution);
            _premiumCompactLayout = ClassicRpgUiRuntime.IsCompactLayout(_canvasRect);
            DestroyCanvasChild("PremiumLobbyBackdrop");
            DestroyCanvasChild("PremiumLobbySafeArea");
            DestroyCanvasChild("PremiumLobbyStage");
            DestroyCanvasChild("PremiumLeaderboardDock");

            BuildPremiumBackdrop(_premiumCompactLayout);

            var safeArea = CreateUiRect("PremiumLobbySafeArea", _canvasRect);
            ClassicRpgUiRuntime.ApplySafeArea(
                safeArea,
                _canvasRect,
                _premiumCompactLayout ? 18f : 42f,
                _premiumCompactLayout ? 18f : 34f,
                _premiumCompactLayout ? 20f : 28f);

            _premiumStageRoot = CreateUiRect("PremiumLobbyStage", safeArea);
            Stretch(_premiumStageRoot);
            var stageLayout = _premiumStageRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            stageLayout.childAlignment = TextAnchor.UpperCenter;
            stageLayout.childControlWidth = true;
            stageLayout.childControlHeight = true;
            stageLayout.childForceExpandWidth = true;
            stageLayout.childForceExpandHeight = false;
            stageLayout.spacing = _premiumCompactLayout ? 14f : 18f;

            BuildPremiumHeader(_premiumStageRoot, _premiumCompactLayout);
            BuildPremiumBody(_premiumStageRoot, _premiumCompactLayout);
            BuildPremiumRail(_premiumRailColumn, _premiumCompactLayout);
            StyleStepPanels(_premiumMainColumn, _premiumCompactLayout);
            StyleLobbyButtons();
            StyleLeaderboardPanel();

            _premiumPresentationApplied = true;
            RefreshTypePanelLayout();
            RefreshPremiumStepCopy(Panel_Step4B_Lobby != null && Panel_Step4B_Lobby.activeSelf ? 5 :
                Panel_Step4A_Queue != null && Panel_Step4A_Queue.activeSelf ? 4 :
                Panel_Step2_Format != null && Panel_Step2_Format.activeSelf ? 3 : 2);
        }

        void BuildPremiumBackdrop(bool compact)
        {
            var root = CreateUiRect("PremiumLobbyBackdrop", _canvasRect);
            Stretch(root);
            root.SetSiblingIndex(0);

            var fallback = root.gameObject.AddComponent<Image>();
            fallback.color = new Color(0.02f, 0.03f, 0.06f, 1f);
            fallback.raycastTarget = false;

            var scenic = CreateUiRect("ScenicBackdrop", root);
            Stretch(scenic);
            scenic.SetSiblingIndex(0);

            var scenicImage = scenic.gameObject.AddComponent<Image>();
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

            CreateTintLayer(root, "BackdropWash", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.07f, 0.11f, 0.42f));
            CreateTintLayer(root, "TopShade", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, compact ? -6f : -12f), new Vector2(0f, compact ? 210f : 250f), new Color(0.01f, 0.02f, 0.05f, 0.68f));
            CreateTintLayer(root, "BottomShade", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, compact ? 0f : 12f), new Vector2(0f, compact ? 180f : 220f), new Color(0.02f, 0.03f, 0.05f, 0.60f));
            CreateTintLayer(root, "LeftShade", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(compact ? 84f : 170f, 0f), new Color(0.01f, 0.02f, 0.04f, 0.42f));
            CreateTintLayer(root, "RightShade", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(compact ? 84f : 170f, 0f), new Color(0.01f, 0.02f, 0.04f, 0.48f));
        }

        void BuildPremiumHeader(Transform parent, bool compact)
        {
            var header = CreateUiRect("PremiumLobbyHeader", parent);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 126f : 154f;

            var layout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 4f : 8f;

            var overline = CreateUiText("Overline", header, "WAR COUNCIL", compact ? 16f : 18f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            overline.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 20f : 24f;

            var titlePlate = CreateUiImage("TitlePlate", header, ClassicRpgPanelSkin.TitleLong, Color.white, false);
            var titlePlateLayout = titlePlate.gameObject.AddComponent<LayoutElement>();
            titlePlateLayout.preferredWidth = compact ? 430f : 560f;
            titlePlateLayout.preferredHeight = compact ? 78f : 92f;

            _premiumScreenTitle = CreateUiText("Title", titlePlate.transform, "Choose Your Match", compact ? 30f : 38f, ClassicRpgTextTone.Title, ClassicRpgUiRuntime.WarmGold);
            Stretch(_premiumScreenTitle.rectTransform, new Vector2(26f, 12f), new Vector2(-26f, -18f));

            _premiumScreenSubtitle = CreateUiText(
                "Subtitle",
                header,
                compact
                    ? "Queue, gather, and enter the warpath."
                    : "Choose your next march, gather the war council, and step into battle.",
                compact ? 14f : 16f,
                ClassicRpgTextTone.Body,
                new Color(0.92f, 0.90f, 0.84f, 0.88f));
            _premiumScreenSubtitle.textWrappingMode = TextWrappingModes.Normal;
            _premiumScreenSubtitle.alignment = TextAlignmentOptions.Center;
            _premiumScreenSubtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 26f : 30f;
        }

        void BuildPremiumBody(Transform parent, bool compact)
        {
            var body = CreateUiRect("PremiumLobbyBody", parent);
            var bodyLayoutElement = body.gameObject.AddComponent<LayoutElement>();
            bodyLayoutElement.flexibleHeight = 1f;
            bodyLayoutElement.flexibleWidth = 1f;

            if (compact)
            {
                var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 14f;
            }
            else
            {
                var layout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.spacing = 18f;
            }

            _premiumMainColumn = CreateUiRect("MainColumn", body);
            var mainLayout = _premiumMainColumn.gameObject.AddComponent<LayoutElement>();
            mainLayout.flexibleWidth = 1f;
            mainLayout.flexibleHeight = 1f;
            mainLayout.minWidth = 0f;
            var mainStack = _premiumMainColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            mainStack.childAlignment = compact ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
            mainStack.childControlWidth = true;
            mainStack.childControlHeight = false;
            mainStack.childForceExpandWidth = true;
            mainStack.childForceExpandHeight = false;
            mainStack.spacing = 0f;

            _premiumRailColumn = CreateUiRect("RailColumn", body);
            var railLayout = _premiumRailColumn.gameObject.AddComponent<LayoutElement>();
            railLayout.preferredWidth = compact ? 0f : 286f;
            railLayout.flexibleWidth = compact ? 1f : 0f;
            railLayout.minWidth = 0f;
        }

        void BuildPremiumRail(Transform parent, bool compact)
        {
            if (parent == null)
                return;

            if (compact)
            {
                var compactLayout = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
                compactLayout.childAlignment = TextAnchor.MiddleCenter;
                compactLayout.childControlWidth = true;
                compactLayout.childControlHeight = true;
                compactLayout.childForceExpandWidth = true;
                compactLayout.childForceExpandHeight = false;
                compactLayout.spacing = 10f;
            }
            else
            {
                var layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 18f;
                layout.padding = new RectOffset(6, 6, 6, 6);
            }

            var commandCard = CreateCard(parent, "CommanderCard", compact ? 104f : 112f);
            if (compact) commandCard.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var commandHeader = CreateUiText("Header", commandCard.transform, "Commander", compact ? 20f : 22f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.WarmGold);
            commandHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 24f : 26f;
            string commanderName = !string.IsNullOrWhiteSpace(Txt_DisplayName?.text)
                ? Txt_DisplayName.text
                : (AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Guest");
            HideLegacyText(Txt_DisplayName);
            Txt_DisplayName = CreateCardValueText(
                commandCard,
                "CommanderValue",
                commanderName,
                compact ? 24f : 26f,
                compact ? 16f : 18f,
                ClassicRpgTextTone.BodyStrong,
                ClassicRpgUiRuntime.WarmGold,
                TextAlignmentOptions.Center,
                allowWrap: false);

            var seasonCard = CreateCard(parent, "SeasonCard", compact ? 100f : 108f);
            if (compact) seasonCard.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var seasonHeader = CreateUiText("Header", seasonCard.transform, "Season", compact ? 20f : 22f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.WarmGold);
            seasonHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 24f : 26f;
            string seasonSummary = ExtractSeasonDisplayValue(Txt_SeasonInfo != null ? Txt_SeasonInfo.text : string.Empty);
            HideLegacyText(Txt_SeasonInfo);
            Txt_SeasonInfo = CreateCardValueText(
                seasonCard,
                "SeasonValue",
                seasonSummary,
                compact ? 22f : 24f,
                compact ? 16f : 18f,
                ClassicRpgTextTone.BodyStrong,
                ClassicRpgUiRuntime.WarmGold,
                TextAlignmentOptions.Center,
                allowWrap: false);

            var statusCard = CreateCard(parent, "StatusCard", compact ? 112f : 124f);
            if (compact) statusCard.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var statusHeader = CreateUiText("Header", statusCard.transform, "Status", compact ? 20f : 22f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.WarmGold);
            statusHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 24f : 26f;
            string statusSummary = TxtStatus != null ? TxtStatus.text : string.Empty;
            HideLegacyText(TxtStatus);
            TxtStatus = CreateCardValueText(
                statusCard,
                "StatusValue",
                statusSummary,
                compact ? 28f : 34f,
                compact ? 15f : 16f,
                ClassicRpgTextTone.Body,
                ClassicRpgUiRuntime.WarmGold,
                TextAlignmentOptions.Center);

            if (Btn_ToggleLeaderboard != null)
            {
                var dock = CreateLeaderboardDock(compact);
                Btn_ToggleLeaderboard.transform.SetParent(dock, false);
                PrepareButton(Btn_ToggleLeaderboard, 46f, compact ? 0f : 208f);
                SetButtonLabel(Btn_ToggleLeaderboard, "Show Leaderboard");
                ApplyButtonStyle(Btn_ToggleLeaderboard, ClassicRpgButtonSkin.MiniGold, 46f);
                Stretch(Btn_ToggleLeaderboard.transform as RectTransform);
            }
        }

        RectTransform CreateCard(Transform parent, string name, float preferredHeight)
        {
            var card = CreateUiImage(name, parent, ClassicRpgPanelSkin.PortraitBackdrop, new Color(0.01f, 0.02f, 0.04f, 0.94f), true);
            var rect = card.rectTransform;
            var layoutElement = card.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(16, 16, 14, 14);
            EnsureDecorativeFrame(rect);
            return rect;
        }

        void StyleStepPanels(Transform host, bool compact)
        {
            if (host == null)
                return;

            if (Panel_Step3_Type != null)
            {
                Panel_Step3_Type.transform.SetParent(host, false);
                StyleTypePanelShell(Panel_Step3_Type, compact ? 326f : 392f);
                _premiumTypeStack = EnsureContentRoot(Panel_Step3_Type.transform as RectTransform, "PremiumTypeStack");
                var typeLayout = _premiumTypeStack.GetComponent<VerticalLayoutGroup>() ?? _premiumTypeStack.gameObject.AddComponent<VerticalLayoutGroup>();
                typeLayout.childAlignment = TextAnchor.MiddleCenter;
                typeLayout.childControlWidth = true;
                typeLayout.childControlHeight = false;
                typeLayout.childForceExpandWidth = true;
                typeLayout.childForceExpandHeight = false;
                typeLayout.spacing = compact ? 14f : 16f;
                typeLayout.padding = compact ? new RectOffset(34, 34, 14, 14) : new RectOffset(96, 96, 18, 18);
            }

            if (Panel_Step4A_Queue != null)
            {
                Panel_Step4A_Queue.transform.SetParent(host, false);
                StylePanelShell(Panel_Step4A_Queue, compact ? 248f : 280f);
                var content = EnsureContentRoot(Panel_Step4A_Queue.transform as RectTransform, "QueueContent");
                var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 16f;
                layout.padding = compact ? new RectOffset(24, 24, 24, 24) : new RectOffset(32, 32, 34, 30);

                ReparentTo(content, Txt_QueueStatus?.rectTransform, compact ? 92f : 110f);
                ReparentTo(content, Btn_CancelQueue?.transform as RectTransform, 48f, 220f);
            }

            if (Panel_Step4B_Lobby != null)
            {
                Panel_Step4B_Lobby.transform.SetParent(host, false);
                StylePanelShell(Panel_Step4B_Lobby, compact ? 420f : 610f, flexibleHeight: true);
                var content = EnsureContentRoot(Panel_Step4B_Lobby.transform as RectTransform, "LobbyContent");
                var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 14f;
                layout.padding = compact ? new RectOffset(24, 24, 24, 22) : new RectOffset(32, 32, 34, 26);

                ReparentTo(content, Txt_LobbyCode?.rectTransform, 44f);
                ReparentTo(content, Txt_MemberList?.rectTransform, compact ? 152f : 192f);

                var actionRow = CreateUiRect("PrimaryActions", content);
                actionRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
                var actionLayout = actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                actionLayout.childAlignment = TextAnchor.MiddleCenter;
                actionLayout.childControlWidth = true;
                actionLayout.childControlHeight = true;
                actionLayout.childForceExpandWidth = true;
                actionLayout.childForceExpandHeight = false;
                actionLayout.spacing = 12f;
                ReparentTo(actionRow, Btn_Ready?.transform as RectTransform, 48f);
                ReparentTo(actionRow, Btn_Launch?.transform as RectTransform, 48f);

                var botRow = CreateUiRect("BotRow", content);
                botRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
                var botLayout = botRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                botLayout.childAlignment = TextAnchor.MiddleCenter;
                botLayout.childControlWidth = true;
                botLayout.childControlHeight = true;
                botLayout.childForceExpandWidth = true;
                botLayout.childForceExpandHeight = false;
                botLayout.spacing = 10f;
                ReparentTo(botRow, Btn_AddBot_Easy?.transform as RectTransform, 42f);
                ReparentTo(botRow, Btn_AddBot_Medium?.transform as RectTransform, 42f);
                ReparentTo(botRow, Btn_AddBot_Hard?.transform as RectTransform, 42f);

                ReparentTo(content, Btn_Leave?.transform as RectTransform, 44f, 220f);
            }

            if (Panel_Step2_Format != null)
            {
                Panel_Step2_Format.transform.SetParent(host, false);
                StylePanelShell(Panel_Step2_Format, compact ? 196f : 220f);
            }
        }

        void StylePanelShell(GameObject panel, float preferredHeight, bool flexibleHeight = false)
        {
            if (panel == null)
                return;

            var rect = panel.transform as RectTransform;
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, preferredHeight);

            var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleHeight = flexibleHeight ? 1f : 0f;
            layoutElement.flexibleWidth = 1f;

            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = true;
                image.raycastTarget = false;
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.07f, 0.09f, 0.13f, 0.86f));
            }

            EnsureDecorativeFrame(rect);
        }

        void StyleTypePanelShell(GameObject panel, float preferredHeight)
        {
            if (panel == null)
                return;

            var rect = panel.transform as RectTransform;
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, preferredHeight);

            var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleHeight = 1f;
            layoutElement.flexibleWidth = 1f;

            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false;
                image.raycastTarget = false;
            }

            var frame = rect.Find("PremiumFrame");
            if (frame != null)
                frame.gameObject.SetActive(false);
        }

        RectTransform EnsureContentRoot(RectTransform panelRect, string name)
        {
            if (panelRect == null)
                return null;

            var existing = panelRect.Find(name) as RectTransform;
            if (existing != null)
                return existing;

            var content = CreateUiRect(name, panelRect);
            Stretch(content, new Vector2(26f, 24f), new Vector2(-26f, -24f));
            return content;
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

            var frame = CreateUiImage("PremiumFrame", panelRect, ClassicRpgPanelSkin.Frame, new Color(0.90f, 0.84f, 0.72f, 0.50f), true);
            Stretch(frame.rectTransform, new Vector2(-4f, -4f), new Vector2(4f, 4f));
        }

        void StyleLobbyButtons()
        {
            ApplyButtonStyle(Btn_Ranked, ClassicRpgButtonSkin.LongGold, 56f);
            ApplyButtonStyle(Btn_Casual, ClassicRpgButtonSkin.LongGold, 56f);
            ApplyButtonStyle(Btn_PrivateLobby, ClassicRpgButtonSkin.LongGold, 56f);
            ApplyButtonStyle(Btn_JoinByCode, ClassicRpgButtonSkin.MediumGold, 50f);
            ApplyButtonStyle(Btn_JoinConfirm, ClassicRpgButtonSkin.MiniGold, 42f);
            ApplyButtonStyle(Btn_Back_Step3, ClassicRpgButtonSkin.MiniBrown, 42f);
            ApplyButtonStyle(Btn_CancelQueue, ClassicRpgButtonSkin.MiniBrown, 46f);
            ApplyButtonStyle(Btn_Ready, ClassicRpgButtonSkin.MiniGreen, 48f);
            ApplyButtonStyle(Btn_Launch, ClassicRpgButtonSkin.MiniGold, 48f);
            ApplyButtonStyle(Btn_AddBot_Easy, ClassicRpgButtonSkin.MiniBrown, 42f);
            ApplyButtonStyle(Btn_AddBot_Medium, ClassicRpgButtonSkin.MiniBrown, 42f);
            ApplyButtonStyle(Btn_AddBot_Hard, ClassicRpgButtonSkin.MiniBrown, 42f);
            ApplyButtonStyle(Btn_Leave, ClassicRpgButtonSkin.MiniBrown, 44f);

            if (Btn_AddBot_Easy != null) SetButtonLabel(Btn_AddBot_Easy, "Bot Easy");
            if (Btn_AddBot_Medium != null) SetButtonLabel(Btn_AddBot_Medium, "Bot Medium");
            if (Btn_AddBot_Hard != null) SetButtonLabel(Btn_AddBot_Hard, "Bot Hard");
            if (Btn_JoinByCode != null) SetButtonLabel(Btn_JoinByCode, "Join by Code");
            if (Btn_JoinConfirm != null) SetButtonLabel(Btn_JoinConfirm, "Join Lobby");
            if (Btn_CancelQueue != null) SetButtonLabel(Btn_CancelQueue, "Cancel Search");
            if (Btn_Leave != null) SetButtonLabel(Btn_Leave, "Leave Lobby");
            if (Btn_Ready != null) SetButtonLabel(Btn_Ready, "Ready");
            if (Btn_Launch != null) SetButtonLabel(Btn_Launch, "Launch Match");
            RefreshRankedQueueButtonState();

            if (Input_JoinCode != null)
            {
                ClassicRpgUiRuntime.StyleInputField(Input_JoinCode, "Enter code");
                PrepareForLayout(Input_JoinCode.transform as RectTransform, 60f);
            }

            if (_progressionButtonRoot != null)
            {
                var progressionButton = _progressionButtonRoot.GetComponent<Button>();
                if (progressionButton != null)
                    ApplyButtonStyle(progressionButton, ClassicRpgButtonSkin.MiniGold, 46f);
            }

            StyleTextBlock(Txt_LobbyCode, ClassicRpgTextTone.Title, 24f, ClassicRpgUiRuntime.WarmGold);
            StyleTextBlock(Txt_MemberList, ClassicRpgTextTone.Body, 18f, ClassicRpgUiRuntime.BrightText);
            StyleTextBlock(Txt_QueueStatus, ClassicRpgTextTone.Body, 22f, ClassicRpgUiRuntime.BrightText);
            RefreshReadyButtonVisualState();
        }

        void StyleLeaderboardPanel()
        {
            if (Panel_Leaderboard == null)
                return;

            var rect = Panel_Leaderboard.transform as RectTransform;
            if (rect == null)
                return;

            rect.anchorMin = _premiumCompactLayout ? new Vector2(0.04f, 0.08f) : new Vector2(0.17f, 0.12f);
            rect.anchorMax = _premiumCompactLayout ? new Vector2(0.96f, 0.92f) : new Vector2(0.83f, 0.88f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = Panel_Leaderboard.GetComponent<Image>();
            if (image != null)
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.07f, 0.09f, 0.13f, 0.92f));

            EnsureDecorativeFrame(rect);

            string leaderboardText = Txt_LeaderboardList != null ? Txt_LeaderboardList.text : "<b>Leaderboard</b>";
            HideLegacyText(Txt_LeaderboardList);

            var content = EnsureContentRoot(rect, "LeaderboardContent");
            Stretch(content, new Vector2(28f, 30f), new Vector2(-28f, -90f));
            content.SetAsLastSibling();

            var leaderboardValue = content.Find("LeaderboardValue") as RectTransform;
            if (leaderboardValue == null)
                leaderboardValue = CreateUiRect("LeaderboardValue", content);

            Stretch(leaderboardValue);
            var leaderboardLabel = leaderboardValue.GetComponent<TextMeshProUGUI>();
            if (leaderboardLabel == null)
                leaderboardLabel = leaderboardValue.gameObject.AddComponent<TextMeshProUGUI>();

            Txt_LeaderboardList = leaderboardLabel;
            Txt_LeaderboardList.text = leaderboardText;
            Txt_LeaderboardList.fontSize = _premiumCompactLayout ? 16f : 18f;
            Txt_LeaderboardList.enableAutoSizing = false;
            Txt_LeaderboardList.textWrappingMode = TextWrappingModes.Normal;
            Txt_LeaderboardList.overflowMode = TextOverflowModes.Overflow;
            Txt_LeaderboardList.raycastTarget = false;
            ClassicRpgUiRuntime.ApplyText(Txt_LeaderboardList, ClassicRpgTextTone.Body, TextAlignmentOptions.TopLeft, ClassicRpgUiRuntime.BrightText);
            Txt_LeaderboardList.overflowMode = TextOverflowModes.Overflow;

            ApplyButtonStyle(Btn_HideLeaderboard, ClassicRpgButtonSkin.MiniBrown, 42f);
            if (Btn_HideLeaderboard != null)
            {
                SetButtonLabel(Btn_HideLeaderboard, "Close");
                var closeRect = Btn_HideLeaderboard.transform as RectTransform;
                if (closeRect != null)
                {
                    closeRect.SetParent(rect, false);
                    closeRect.anchorMin = new Vector2(1f, 1f);
                    closeRect.anchorMax = new Vector2(1f, 1f);
                    closeRect.pivot = new Vector2(1f, 1f);
                    closeRect.anchoredPosition = new Vector2(-28f, -26f);
                    closeRect.sizeDelta = new Vector2(154f, 42f);
                    closeRect.localScale = Vector3.one;
                    closeRect.localRotation = Quaternion.identity;
                    closeRect.SetAsLastSibling();
                }
            }
        }

        void RefreshPremiumStepCopy(int step)
        {
            if (!_premiumPresentationApplied || _premiumScreenTitle == null || _premiumScreenSubtitle == null)
                return;

            switch (step)
            {
                case 4:
                    _premiumScreenTitle.text = "Searching For Battle";
                    _premiumScreenSubtitle.text = "Hold position while scouts find a worthy battlefront.";
                    break;
                case 5:
                    _premiumScreenTitle.text = "Lobby Assembly";
                    _premiumScreenSubtitle.text = "Review the roster, ready the council, and launch when the host gives the word.";
                    break;
                case 3:
                    _premiumScreenTitle.text = "Match Format";
                    _premiumScreenSubtitle.text = "Set the terms of the coming engagement.";
                    break;
                default:
                    _premiumScreenTitle.text = "Choose Your Match";
                    _premiumScreenSubtitle.text = "Choose your next march, gather the war council, and step into battle.";
                    break;
            }
        }

        void RebuildPremiumTypeStack()
        {
            if (_premiumTypeStack == null)
                return;

            PlaceTypeControl(Btn_Ranked?.transform as RectTransform, 56f);
            PlaceTypeControl(Btn_Casual?.transform as RectTransform, 56f);
            PlaceTypeControl(Btn_PrivateLobby?.transform as RectTransform, 56f);

            if (_showingJoinInput)
            {
                PlaceTypeControl(Input_JoinCode?.transform as RectTransform, 60f);
                PlaceTypeControl(Btn_JoinConfirm?.transform as RectTransform, 42f, 240f);
                PlaceTypeControl(Btn_Back_Step3?.transform as RectTransform, 42f, 220f);
            }
            else
            {
                PlaceTypeControl(_progressionButtonRoot, 46f, 240f);
                PlaceTypeControl(Btn_JoinByCode?.transform as RectTransform, 46f, 240f);
            }
        }

        void PlaceTypeControl(RectTransform rect, float height, float width = 0f)
        {
            if (rect == null || _premiumTypeStack == null || !rect.gameObject.activeSelf)
                return;

            rect.SetParent(_premiumTypeStack, false);
            PrepareForLayout(rect, height);
            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (width > 0f)
                layout.preferredWidth = width;
        }

        void ApplyButtonStyle(Button button, ClassicRpgButtonSkin skin, float height)
        {
            if (button == null)
                return;

            PrepareButton(button, height, 0f);

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

        void RefreshReadyButtonVisualState()
        {
            if (Btn_Ready == null)
                return;

            ApplyButtonStyle(Btn_Ready, _isReady ? ClassicRpgButtonSkin.MiniGreen : ClassicRpgButtonSkin.MiniBrown, 48f);
            SetButtonLabel(Btn_Ready, _isReady ? "Ready!" : "Ready");
        }

        void RefreshRankedQueueButtonState()
        {
            if (Btn_Ranked == null)
                return;

            ApplyButtonStyle(Btn_Ranked, ClassicRpgButtonSkin.LongGold, 56f);
            Btn_Ranked.interactable = true;

            bool locked = !AuthManager.IsAuthenticated || !_rankedEligibilityKnown || !_rankedQueueUnlocked;
            SetButtonLabel(Btn_Ranked, BuildRankedQueueButtonLabel());

            var image = Btn_Ranked.targetGraphic as Image ?? Btn_Ranked.GetComponent<Image>();
            if (image != null)
                image.color = locked
                    ? new Color(0.46f, 0.49f, 0.56f, 0.96f)
                    : Color.white;

            var label = Btn_Ranked.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.richText = true;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.fontSize = locked ? 20f : 24f;
                label.color = locked
                    ? new Color(0.90f, 0.91f, 0.94f, 1f)
                    : ClassicRpgUiRuntime.WarmGold;
            }

            var colors = Btn_Ranked.colors;
            if (locked)
            {
                colors.normalColor = new Color(0.92f, 0.94f, 0.98f, 1f);
                colors.highlightedColor = new Color(0.98f, 0.99f, 1f, 1f);
                colors.pressedColor = new Color(0.80f, 0.83f, 0.90f, 1f);
                colors.selectedColor = new Color(0.95f, 0.97f, 1f, 1f);
            }
            Btn_Ranked.colors = colors;
        }

        string BuildRankedQueueButtonLabel()
        {
            if (!AuthManager.IsAuthenticated)
                return "Ranked Match <color=#E67C7C><size=80%>Sign In Required</size></color>";

            if (!_rankedEligibilityKnown)
                return "Ranked Match <color=#E67C7C><size=80%>Checking Record</size></color>";

            if (_rankedQueueUnlocked)
                return "Ranked Match";

            int remaining = Mathf.Max(0, _rankedCasualMatchesRequired - _rankedCasualMatchesCompleted);
            return $"Ranked Match <color=#E67C7C><size=80%>{remaining} Casual Required</size></color>";
        }

        string BuildRankedQueueLockedStatus()
        {
            int requiredMatches = Mathf.Max(1, _rankedCasualMatchesRequired);
            if (!AuthManager.IsAuthenticated)
                return $"Sign in to track your war record. Ranked queue opens after {requiredMatches} casual matches.";

            if (!_rankedEligibilityKnown)
                return $"Checking war record. Ranked queue opens after {requiredMatches} casual matches.";

            int remaining = Mathf.Max(0, requiredMatches - _rankedCasualMatchesCompleted);
            if (remaining <= 0)
                return "Ranked queue is still updating your war record. Try again in a moment.";

            return remaining == 1
                ? "Complete 1 more casual match to unlock ranked queue."
                : $"Complete {remaining} more casual matches to unlock ranked queue.";
        }

        void ReparentTo(Transform parent, RectTransform rect, float height, float width = 0f)
        {
            if (parent == null || rect == null)
                return;

            rect.SetParent(parent, false);
            PrepareForLayout(rect, height);
            if (width > 0f)
            {
                var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = width;
            }
        }

        void PrepareButton(Button button, float height, float width)
        {
            if (button == null)
                return;

            var rect = button.transform as RectTransform;
            if (rect == null)
                return;

            PrepareForLayout(rect, height);
            if (width > 0f)
            {
                var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = width;
            }
        }

        void StyleTextBlock(TMP_Text text, ClassicRpgTextTone tone, float fontSize, Color color)
        {
            if (text == null)
                return;

            text.fontSize = fontSize;
            text.textWrappingMode = TextWrappingModes.Normal;
            ClassicRpgUiRuntime.ApplyText(text, tone, text.alignment, color);
        }

        TMP_Text CreateCardValueText(
            RectTransform parent,
            string name,
            string value,
            float preferredHeight,
            float fontSize,
            ClassicRpgTextTone tone,
            Color color,
            TextAlignmentOptions alignment,
            bool allowWrap = true)
        {
            var text = CreateUiText(name, parent, value, fontSize, tone, color);
            PrepareForLayout(text.rectTransform, preferredHeight);
            text.enableAutoSizing = false;
            text.textWrappingMode = allowWrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = allowWrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            text.alignment = alignment;
            ClassicRpgUiRuntime.ApplyText(text, tone, alignment, color);
            return text;
        }

        static void HideLegacyText(TMP_Text text)
        {
            if (text == null)
                return;

            text.gameObject.SetActive(false);
        }

        static void PrepareForLayout(RectTransform rect, float preferredHeight)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(0f, preferredHeight);
            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = 1f;
        }

        void CreateBackdropBar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var image = CreateUiImage(name, parent, ClassicRpgPanelSkin.MainMenuBar, color, false);
            image.rectTransform.anchorMin = anchorMin;
            image.rectTransform.anchorMax = anchorMax;
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = anchoredPosition;
            image.rectTransform.sizeDelta = size;
            image.gameObject.AddComponent<UiAmbientMotion>();
        }

        void CreateBackdropFlag(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color, bool mirror)
        {
            var image = CreateUiImage(name, parent, ClassicRpgPanelSkin.FlagClassic, color, false);
            image.rectTransform.anchorMin = anchorMin;
            image.rectTransform.anchorMax = anchorMax;
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = anchoredPosition;
            image.rectTransform.sizeDelta = size;
            if (mirror)
                image.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            image.gameObject.AddComponent<UiAmbientMotion>();
        }

        RectTransform CreateLeaderboardDock(bool compact)
        {
            var dock = CreateUiRect("PremiumLeaderboardDock", _canvasRect);
            dock.anchorMin = compact ? new Vector2(0.5f, 0f) : new Vector2(1f, 0f);
            dock.anchorMax = compact ? new Vector2(0.5f, 0f) : new Vector2(1f, 0f);
            dock.pivot = compact ? new Vector2(0.5f, 0f) : new Vector2(1f, 0f);
            dock.anchoredPosition = compact ? new Vector2(0f, 28f) : new Vector2(-34f, 46f);
            dock.sizeDelta = compact ? new Vector2(248f, 46f) : new Vector2(208f, 46f);
            dock.localScale = Vector3.one;
            dock.localRotation = Quaternion.identity;
            return dock;
        }

        static string ExtractSeasonDisplayValue(string seasonSummary)
        {
            if (string.IsNullOrWhiteSpace(seasonSummary))
                return string.Empty;

            const string prefix = "Season ";
            if (seasonSummary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string remainder = seasonSummary.Substring(prefix.Length);
                int colonIndex = remainder.IndexOf(':');
                if (colonIndex >= 0)
                    remainder = remainder.Substring(0, colonIndex);

                return remainder.Trim();
            }

            return seasonSummary.Trim();
        }

        static void CreateTintLayer(
            RectTransform parent,
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
                Debug.LogWarning($"[LobbyUI] Missing lobby backdrop resource at Resources/{WinterBackdropResourcePath}.");
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

        static RectTransform CreateUiRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Image CreateUiImage(string name, Transform parent, ClassicRpgPanelSkin skin, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            ClassicRpgUiRuntime.ApplyPanel(image, skin, sliced, color);
            return image;
        }

        static TMP_Text CreateUiText(string name, Transform parent, string value, float fontSize, ClassicRpgTextTone tone, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.textWrappingMode = TextWrappingModes.Normal;
            ClassicRpgUiRuntime.ApplyText(text, tone, TextAlignmentOptions.Center, color);
            return text;
        }

        static void Stretch(RectTransform rect, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        static void AddTypePanelRect(List<RectTransform> orderedRects, Component component)
        {
            if (component == null || !component.gameObject.activeSelf)
                return;

            var rect = component.GetComponent<RectTransform>();
            if (rect != null)
                orderedRects.Add(rect);
        }

        static void AddTypePanelRect(List<RectTransform> orderedRects, RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeSelf)
                return;

            orderedRects.Add(rect);
        }

        static float GetTypePanelRectHeight(RectTransform rect)
        {
            if (rect == null)
                return 0f;

            float height = LayoutUtility.GetPreferredHeight(rect);
            if (height <= 0f)
                height = rect.rect.height;
            if (height <= 0f)
                height = rect.sizeDelta.y;
            return height;
        }

        void OpenProgressionViewer()
        {
            if (_awaitingLoadoutScene)
            {
                SetStatus("Wait for the incoming match flow before opening the race progression viewer.");
                Play(AudioManager.SFX.Error);
                return;
            }

            if (LoadingScreen.IsTransitionInProgress)
            {
                SetStatus("The war room is already shifting to another screen.");
                Play(AudioManager.SFX.Error);
                return;
            }

            Play(AudioManager.SFX.ButtonClick);
            SetStatus("Opening Tech Tree.");
            ProgressionViewerLaunchContext.OpenLobbyViewer(RaceProgressionCatalog.DefaultRaceId);
            LoadingScreen.LoadSceneWithRemoteContentGate(
                "Loadout",
                portraitKeys: RaceProgressionCatalog.GetPortraitWarmupKeys(null));
        }

        void EnsureProgressionOverlay()
        {
            CacheCanvas();
            if (_canvasRect == null || _progressionOverlayRoot != null)
                return;

            var root = new GameObject("LobbyProgressionOverlay", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(_canvasRect, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var background = root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.45f);
            _progressionOverlayRoot = rect;
        }

        void CloseProgressionViewer()
        {
            if (_progressionViewer != null)
            {
                Destroy(_progressionViewer.gameObject);
                _progressionViewer = null;
            }

            if (_progressionOverlayRoot != null)
            {
                Destroy(_progressionOverlayRoot.gameObject);
                _progressionOverlayRoot = null;
            }
        }

        static string[] ExtractProgressionPortraitKeys(MLLoadoutPhaseStartPayload payload)
        {
            return RaceProgressionCatalog.GetPortraitWarmupKeys(payload?.availableRaceIds);
        }

        string BaseUrl
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var page = new Uri(Application.absoluteURL);
                bool standard = (page.Scheme == "https" && page.Port == 443)
                             || (page.Scheme == "http"  && page.Port == 80)
                             || page.Port < 0;
                return standard
                    ? $"{page.Scheme}://{page.Host}"
                    : $"{page.Scheme}://{page.Host}:{page.Port}";
#else
                return NetworkManager.Instance != null
                    ? NetworkManager.Instance.ResolvedServerUrl
                    : "http://localhost:3000";
#endif
            }
        }

        static void ApplyAuthorization(UnityWebRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(AuthManager.Token))
                return;

            req.SetRequestHeader("Authorization", $"Bearer {AuthManager.Token}");
        }

        static void EnsureEventSystem()
        {
            var lobbyUi = FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            SceneEventSystemUtility.EnsureSceneLocal(lobbyUi, "LobbyEventSystem", "LobbyUI");
        }

        void CacheCanvas()
        {
            if (_canvasRect != null)
                return;

            Canvas canvas = null;

            if (Panel_Step3_Type != null)
                canvas = Panel_Step3_Type.GetComponentInParent<Canvas>();

            if (canvas == null)
                canvas = GetComponentInParent<Canvas>();

            if (canvas == null || canvas.gameObject.scene != gameObject.scene)
            {
                foreach (var candidate in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                    {
                        canvas = candidate;
                        break;
                    }
                }
            }

            if (canvas != null)
                _canvasRect = canvas.GetComponent<RectTransform>();
        }

        void BuildSettingsPanel()
        {
            CacheCanvas();
            DestroyCanvasChild("LobbySettingsPanel");

            if (!showSettingsPanel)
                return;

            if (_canvasRect == null)
                return;

            var root = new GameObject("LobbySettingsPanel", typeof(RectTransform));
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
            StyleSettingsSurface(panel, panelImage.color, new Color(0.86f, 0.66f, 0.28f, 0.98f));

            var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.spacing = 12f;
            panelLayout.padding = new RectOffset(18, 18, 18, 18);

            var eyebrow = CreateSettingsText(panel.transform, "Eyebrow", "COMMAND MENU", 11f, new Color(0.95f, 0.79f, 0.42f, 0.98f));
            eyebrow.fontStyle = FontStyles.SmallCaps;
            eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var title = CreateSettingsText(panel.transform, "Title", "Settings", 24f, Color.white);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var subtitle = CreateSettingsText(panel.transform, "Subtitle", "Tap each selector to cycle its saved option.", 12f, new Color(0.84f, 0.89f, 0.95f, 0.96f));
            subtitle.textWrappingMode = TextWrappingModes.Normal;
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var rows = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            rows.transform.SetParent(panel.transform, false);
            rows.GetComponent<LayoutElement>().flexibleHeight = 1f;

            var rowsLayout = rows.GetComponent<VerticalLayoutGroup>();
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = false;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.spacing = 10f;

            var tiltButton = CreateSettingsSelectorRow(rows.transform, "TiltRow", "Camera Tilt", "Cycle the battlefield viewing angle.", new Color(0.16f, 0.24f, 0.32f, 0.98f), out _txtSettingsTiltValue);
            var zoomButton = CreateSettingsSelectorRow(rows.transform, "ZoomRow", "Camera Zoom", "Cycle how close your command view sits.", new Color(0.18f, 0.28f, 0.22f, 0.98f), out _txtSettingsZoomValue);
            var rotationButton = CreateSettingsSelectorRow(rows.transform, "RotateRow", "Camera Rotation", "Cycle your battlefield facing.", new Color(0.28f, 0.20f, 0.16f, 0.98f), out _txtSettingsRotationValue);
            var sfxButton = CreateSettingsSelectorRow(rows.transform, "SfxRow", "Sound Effects", "Cycle combat, build, and UI volume.", new Color(0.20f, 0.22f, 0.34f, 0.98f), out _txtSettingsSfxValue);
            var musicButton = CreateSettingsSelectorRow(rows.transform, "MusicRow", "Music Loop", "Cycle the background soundtrack level.", new Color(0.16f, 0.26f, 0.30f, 0.98f), out _txtSettingsMusicValue);
            var engagementButton = CreateSettingsSelectorRow(rows.transform, "EngagementRow", "Engagement Rings", "Show or hide combat range circles.", new Color(0.24f, 0.18f, 0.32f, 0.98f), out _txtSettingsEngagementValue);
            var attackRangeButton = CreateSettingsSelectorRow(rows.transform, "AttackRangeRow", "Attack Range Rings", "Show or hide attack range circles.", new Color(0.18f, 0.24f, 0.34f, 0.98f), out _txtSettingsAttackRangeValue);
            var healthBarsButton = CreateSettingsSelectorRow(rows.transform, "HealthBarsRow", "Health Bars", "Show or hide unit health bars.", new Color(0.23f, 0.26f, 0.16f, 0.98f), out _txtSettingsHealthBarsValue);
            var tooltipsButton = CreateSettingsSelectorRow(rows.transform, "TooltipsRow", "Display Tooltips", "Save barracks hints and onboarding tips.", new Color(0.28f, 0.24f, 0.12f, 0.98f), out _txtSettingsTooltipsValue);

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

            var closeButton = CreateSettingsButton(footer.transform, "CloseButton", "Close", new Color(0.18f, 0.24f, 0.30f, 0.98f), 46f);
            var logoutButton = CreateSettingsButton(footer.transform, "LogoutButton", "Log Out", new Color(0.34f, 0.24f, 0.12f, 0.98f), 46f);
            var quitButton = CreateSettingsButton(footer.transform, "QuitButton", "Quit Game", new Color(0.42f, 0.17f, 0.17f, 0.98f), 46f);

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
            StyleSettingsSurface(gear, gearImage.color, new Color(0.86f, 0.66f, 0.28f, 0.98f));

            _settingsMenuButtonLabel = CreateSettingsText(gear.transform, "Label", "Menu", 14f, new Color(0.96f, 0.97f, 0.99f, 1f));
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
            attackRangeButton.onClick.AddListener(ToggleAttackRangeCirclesSetting);
            healthBarsButton.onClick.AddListener(ToggleHealthBarsSetting);
            tooltipsButton.onClick.AddListener(ToggleTooltipsSetting);
            closeButton.onClick.AddListener(() => SetSettingsOverlayVisible(false));
            logoutButton.onClick.AddListener(OnLogoutPressed);
            quitButton.onClick.AddListener(OnQuitPressed);

            RefreshSettingsPanelValues();
            SetSettingsOverlayVisible(false, immediate: true);
        }

        Button CreateSettingsSelectorRow(
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
            StyleSettingsSurface(row, rowImage.color, selectorColor);

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

            var title = CreateSettingsText(copy.transform, "Title", labelText, 13f, Color.white);
            title.fontStyle = FontStyles.SmallCaps;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            var detail = CreateSettingsText(copy.transform, "Detail", detailText, 10f, new Color(0.78f, 0.85f, 0.92f, 0.94f));
            detail.textWrappingMode = TextWrappingModes.Normal;
            detail.overflowMode = TextOverflowModes.Ellipsis;
            detail.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            return CreateSettingsSelectorButton(row.transform, "Selector", selectorColor, out valueLabel);
        }

        Button CreateSettingsButton(Transform parent, string name, string label, Color backgroundColor, float preferredHeight = 0f)
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

            var labelText = CreateSettingsText(buttonGo.transform, "Label", label, 10, new Color(0.96f, 0.97f, 0.99f, 1f));
            labelText.rectTransform.anchorMin = Vector2.zero;
            labelText.rectTransform.anchorMax = Vector2.one;
            labelText.rectTransform.offsetMin = new Vector2(4f, 2f);
            labelText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            labelText.alignment = TextAlignmentOptions.Center;
            return buttonGo.GetComponent<Button>();
        }

        Button CreateSettingsSelectorButton(Transform parent, string name, Color backgroundColor, out TMP_Text valueLabel)
        {
            var button = CreateSettingsButton(parent, name, "--", backgroundColor, 42f);
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                float selectorMinWidth = Mathf.Max(118f, settingsValueWidth * 4f);
                float selectorPreferredWidth = Mathf.Max(132f, settingsValueWidth * 4.4f);
                layout.minWidth = selectorMinWidth;
                layout.preferredWidth = selectorPreferredWidth;
                layout.flexibleWidth = 0f;
            }

            StyleSettingsSurface(button.gameObject, backgroundColor, new Color(0.94f, 0.96f, 0.99f, 0.42f));
            valueLabel = button.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (valueLabel != null)
            {
                valueLabel.fontSize = 12f;
                valueLabel.fontStyle = FontStyles.Bold;
            }

            return button;
        }

        TMP_Text CreateSettingsText(Transform parent, string name, string value, float fontSize, Color color)
        {
            var textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(parent, false);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        void StyleSettingsSurface(GameObject target, Color backgroundColor, Color accentColor)
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

            if (!immediate && wasVisible != visible)
                Play(AudioManager.SFX.ButtonClick);

            UpdateSettingsMenuButtonState();
        }

        void UpdateSettingsMenuButtonState()
        {
            if (_settingsMenuButtonLabel != null)
                _settingsMenuButtonLabel.text = _settingsOverlay != null && _settingsOverlay.activeSelf ? "Back" : "Menu";
        }

        void RefreshSettingsPanelLayout()
        {
            if (_settingsPanelRoot != null)
                _settingsPanelRoot.anchoredPosition = new Vector2(-settingsRightInset, -settingsTopInset);

            if (_settingsOverlayPanelRoot != null)
            {
                float panelHorizontalInset = Mathf.Max(18f, settingsPanelGap + 12f);
                float panelVerticalInset = Mathf.Max(18f, settingsPanelGap + 8f);
                _settingsOverlayPanelRoot.offsetMin = new Vector2(panelHorizontalInset, panelVerticalInset);
                _settingsOverlayPanelRoot.offsetMax = new Vector2(-panelHorizontalInset, -panelVerticalInset);
            }
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

        void ToggleAttackRangeCirclesSetting()
        {
            UserPreferencesManager.SetAttackRangeCirclesVisible(!UserPreferencesManager.ShowAttackRangeCircles);
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
            float currentVolume = UserPreferencesManager.CurrentPreferenceView.audio.menuMusicVolume
                ?? UserPreferencesManager.CurrentPreferenceView.audio.ambientVolume;
            float nextVolume = GetWrappedSelectorValue(currentVolume, 0f, 1f, 0.25f);
            if (AudioManager.I != null)
                AudioManager.I.SetMenuMusicVolume(nextVolume);
            else
                UserPreferencesManager.NotifyMenuMusicVolumeChanged(nextVolume);
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
                && _txtSettingsAttackRangeValue == null
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
            SetSettingsValue(_txtSettingsMusicValue, FormatVolumeValue(preferences.audio.menuMusicVolume ?? preferences.audio.ambientVolume));
            SetSettingsValue(_txtSettingsEngagementValue, FormatToggleValue(preferences.visuals.showEngagementCircles));
            SetSettingsValue(_txtSettingsAttackRangeValue, FormatToggleValue(preferences.visuals.showAttackRangeCircles));
            SetSettingsValue(_txtSettingsHealthBarsValue, FormatToggleValue(preferences.visuals.showHealthBars));
            SetSettingsValue(_txtSettingsTooltipsValue, FormatToggleValue(preferences.visuals.showTooltips));
            UpdateSettingsMenuButtonState();
        }

        void OnLogoutPressed()
        {
            if (LoadingScreen.IsTransitionInProgress)
            {
                SetStatus("The war room is already shifting to another screen.");
                Play(AudioManager.SFX.Error);
                return;
            }

            _loggingOut = true;
            _inQueue = false;
            _awaitingLoadoutScene = false;
            CloseProgressionViewer();
            SetSettingsOverlayVisible(false, immediate: true);
            ClearReconnectPrefs();
            AuthManager.BeginLogout(NetworkManager.Instance != null ? NetworkManager.Instance.ResolvedServerUrl : null);
            NetworkManager.Instance?.ReconnectForCurrentAuth("lobby logout");
            SetStatus("Returning to sign-in...");
            Play(AudioManager.SFX.ButtonClick);
            LoadingScreen.LoadScene("Login");
        }

        void OnQuitPressed()
        {
            Play(AudioManager.SFX.ButtonClick);
            if (TryQuitGame(SetStatus))
                SetStatus("Closing game...");
        }

        static void ClearReconnectPrefs()
        {
            PlayerPrefs.DeleteKey("reconnect_token");
            PlayerPrefs.DeleteKey("reconnect_code");
            PlayerPrefs.DeleteKey("reconnect_lane");
            PlayerPrefs.DeleteKey("reconnect_gametype");
            PlayerPrefs.Save();
        }

        static bool TryQuitGame(Action<string> onUnsupported = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            onUnsupported?.Invoke("Exit is not supported on WebGL. Close the browser tab to leave the game.");
            return false;
#elif UNITY_EDITOR
            onUnsupported?.Invoke("Exit from the menu will stop Play Mode in the Unity Editor.");
            return false;
#else
            Application.Quit();
            return true;
#endif
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

        void DestroyCanvasChild(string childName)
        {
            if (_canvasRect == null)
                return;

            for (int i = _canvasRect.childCount - 1; i >= 0; i--)
            {
                var child = _canvasRect.GetChild(i);
                if (child == null || child.name != childName)
                    continue;

                Destroy(child.gameObject);
            }
        }

        void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
        }

        [Serializable]
        sealed class PlayerProfileResponse
        {
            public QueueProgressionResponse queue_progression;
        }

        [Serializable]
        sealed class QueueProgressionResponse
        {
            public int ranked_casual_matches_completed;
            public int ranked_casual_matches_required;
            public bool ranked_queue_unlocked;
        }

        void SetStatus(string msg) { if (TxtStatus != null) TxtStatus.text = msg; }
        void Play(AudioManager.SFX sfx) => AudioManager.I?.Play(sfx);
    }
}
