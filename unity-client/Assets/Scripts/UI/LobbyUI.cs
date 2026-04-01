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
        [SerializeField] bool showSettingsPanel = false;
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
        string _matchFormat = "ffa";
        bool   _pendingRanked;
        bool   _showingJoinInput;

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
        TMP_Text _txtSettingsEngagementValue;
        TMP_Text _txtSettingsHealthBarsValue;

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

            CloseProgressionViewer();
        }

        void Update()
        {
            if (_inQueue)
            {
                _queueElapsed += Time.deltaTime;
                RefreshQueueDisplay(null);
            }

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
            RefreshTypePanelLayout();
        }

        void OnQueueRanked()
        {
            if (!AuthManager.IsAuthenticated) { SetStatus("Sign in to use public queue."); Play(AudioManager.SFX.Error); return; }
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
        }

        void OnToggleReady()
        {
            _isReady = !_isReady;
            if (TxtReadyBtn != null) TxtReadyBtn.text = _isReady ? "Ready!" : "Ready";
            if (Btn_Ready != null)
                Btn_Ready.image.color = _isReady
                    ? new Color(0.2f, 0.7f, 0.3f)
                    : new Color(0.25f, 0.25f, 0.3f);
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
        void HandleConnected()   => SetStatus("Choose a queue.");
        void HandleDisconnected()
        {
            _inQueue = false;
            GoToStep(2);
            SetStatus("Disconnected. Reconnecting...");
        }

        void HandleQueueStatus(QueueStatusPayload p)
        {
            if (p.status == "idle")
            {
                _inQueue = false;
                GoToStep(3);
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
                Txt_SeasonInfo.text = $"Season {resp.season.id}: {resp.season.name}";
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

            var background = root.gameObject.AddComponent<Image>();
            ClassicRpgUiRuntime.ApplyPanel(background, ClassicRpgPanelSkin.DarkSpell, false, new Color(1f, 1f, 1f, 0.22f));

            CreateBackdropBar(root, "TopBanner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, compact ? -34f : -44f), new Vector2(compact ? 780f : 1060f, compact ? 86f : 112f), new Color(1f, 1f, 1f, 0.74f));
            CreateBackdropBar(root, "BottomBanner", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, compact ? 34f : 44f), new Vector2(compact ? 780f : 1060f, compact ? 88f : 112f), new Color(1f, 1f, 1f, 0.30f));

            if (compact)
            {
                CreateBackdropFlag(root, "TopHeraldry", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(190f, 260f), new Color(1f, 1f, 1f, 0.13f), false);
            }
            else
            {
                CreateBackdropFlag(root, "LeftHeraldry", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(128f, 0f), new Vector2(240f, 332f), new Color(1f, 1f, 1f, 0.18f), false);
                CreateBackdropFlag(root, "RightHeraldry", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-374f, 18f), new Vector2(220f, 308f), new Color(1f, 1f, 1f, 0.14f), true);
            }
        }

        void BuildPremiumHeader(Transform parent, bool compact)
        {
            var header = CreateUiRect("PremiumLobbyHeader", parent);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 152f : 176f;

            var layout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;

            var overline = CreateUiText("Overline", header, "WAR COUNCIL", 18f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            overline.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            var titlePlate = CreateUiImage("TitlePlate", header, ClassicRpgPanelSkin.TitleLong, Color.white, false);
            var titlePlateLayout = titlePlate.gameObject.AddComponent<LayoutElement>();
            titlePlateLayout.preferredWidth = compact ? 460f : 520f;
            titlePlateLayout.preferredHeight = compact ? 86f : 96f;

            _premiumScreenTitle = CreateUiText("Title", titlePlate.transform, "Choose Your Match", compact ? 30f : 34f, ClassicRpgTextTone.Title, ClassicRpgUiRuntime.WarmGold);
            Stretch(_premiumScreenTitle.rectTransform, new Vector2(26f, 16f), new Vector2(-26f, -18f));

            _premiumScreenSubtitle = CreateUiText(
                "Subtitle",
                header,
                compact
                    ? "Queue, inspect progression, and ready the match from a cleaner command hub."
                    : "Assemble your session, inspect progression, and step into battle with a stronger front-end presence.",
                compact ? 16f : 18f,
                ClassicRpgTextTone.Body,
                ClassicRpgUiRuntime.BrightText);
            _premiumScreenSubtitle.textWrappingMode = TextWrappingModes.Normal;
            _premiumScreenSubtitle.alignment = TextAlignmentOptions.Center;
            _premiumScreenSubtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 36f : 44f;
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
            railLayout.preferredWidth = compact ? 0f : 312f;
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
                layout.spacing = 14f;
                layout.padding = new RectOffset(6, 6, 6, 6);
            }

            var commandCard = CreateCard(parent, "CommanderCard", compact ? 132f : 122f);
            if (compact) commandCard.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var commandHeader = CreateUiText("Header", commandCard.transform, "COMMANDER", 18f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            commandHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            string commanderName = !string.IsNullOrWhiteSpace(Txt_DisplayName?.text)
                ? Txt_DisplayName.text
                : (AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Guest");
            HideLegacyText(Txt_DisplayName);
            Txt_DisplayName = CreateCardValueText(
                commandCard,
                "CommanderValue",
                commanderName,
                42f,
                24f,
                ClassicRpgTextTone.Title,
                ClassicRpgUiRuntime.WarmGold,
                TextAlignmentOptions.Center,
                allowWrap: false);

            var seasonCard = CreateCard(parent, "SeasonCard", compact ? 132f : 136f);
            if (compact) seasonCard.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var seasonHeader = CreateUiText("Header", seasonCard.transform, "SEASON", 18f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            seasonHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            string seasonSummary = Txt_SeasonInfo != null ? Txt_SeasonInfo.text : string.Empty;
            HideLegacyText(Txt_SeasonInfo);
            Txt_SeasonInfo = CreateCardValueText(
                seasonCard,
                "SeasonValue",
                seasonSummary,
                72f,
                compact ? 16f : 18f,
                ClassicRpgTextTone.Body,
                ClassicRpgUiRuntime.BrightText,
                TextAlignmentOptions.Center);

            var statusCard = CreateCard(parent, "StatusCard", compact ? 154f : 220f);
            if (compact) statusCard.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var statusHeader = CreateUiText("Header", statusCard.transform, "STATUS", 18f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            statusHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            string statusSummary = TxtStatus != null ? TxtStatus.text : string.Empty;
            HideLegacyText(TxtStatus);
            TxtStatus = CreateCardValueText(
                statusCard,
                "StatusValue",
                statusSummary,
                compact ? 62f : 116f,
                compact ? 15f : 18f,
                ClassicRpgTextTone.Body,
                ClassicRpgUiRuntime.BrightText,
                TextAlignmentOptions.TopLeft);

            if (Btn_ToggleLeaderboard != null)
            {
                Btn_ToggleLeaderboard.transform.SetParent(statusCard.transform, false);
                PrepareButton(Btn_ToggleLeaderboard, 46f, compact ? 0f : 240f);
                SetButtonLabel(Btn_ToggleLeaderboard, "Show Leaderboard");
                ClassicRpgUiRuntime.ApplyButton(Btn_ToggleLeaderboard, ClassicRpgButtonSkin.MiniGold);
            }
        }

        RectTransform CreateCard(Transform parent, string name, float preferredHeight)
        {
            var card = CreateUiImage(name, parent, ClassicRpgPanelSkin.PaperMedium, new Color(0.15f, 0.13f, 0.10f, 0.96f), true);
            var rect = card.rectTransform;
            var layoutElement = card.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(18, 18, 18, 18);
            return rect;
        }

        void StyleStepPanels(Transform host, bool compact)
        {
            if (host == null)
                return;

            if (Panel_Step3_Type != null)
            {
                Panel_Step3_Type.transform.SetParent(host, false);
                StylePanelShell(Panel_Step3_Type, compact ? 386f : 544f, flexibleHeight: true);
                _premiumTypeStack = EnsureContentRoot(Panel_Step3_Type.transform as RectTransform, "PremiumTypeStack");
                var typeLayout = _premiumTypeStack.GetComponent<VerticalLayoutGroup>() ?? _premiumTypeStack.gameObject.AddComponent<VerticalLayoutGroup>();
                typeLayout.childAlignment = TextAnchor.MiddleCenter;
                typeLayout.childControlWidth = true;
                typeLayout.childControlHeight = false;
                typeLayout.childForceExpandWidth = true;
                typeLayout.childForceExpandHeight = false;
                typeLayout.spacing = 12f;
                typeLayout.padding = compact ? new RectOffset(24, 24, 26, 24) : new RectOffset(32, 32, 42, 32);
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
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.16f, 0.14f, 0.10f, 0.96f));

            EnsureDecorativeFrame(rect);
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

            if (panelRect.Find("PremiumFrame") == null)
            {
                var frame = CreateUiImage("PremiumFrame", panelRect, ClassicRpgPanelSkin.Frame, new Color(1f, 1f, 1f, 0.92f), true);
                Stretch(frame.rectTransform, new Vector2(-4f, -4f), new Vector2(4f, 4f));
            }
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

            if (Input_JoinCode != null)
            {
                ClassicRpgUiRuntime.StyleInputField(Input_JoinCode, "Enter code");
                PrepareForLayout(Input_JoinCode.transform as RectTransform, 60f);
            }

            if (_progressionButtonRoot != null)
            {
                var progressionButton = _progressionButtonRoot.GetComponent<Button>();
                if (progressionButton != null)
                {
                    PrepareButton(progressionButton, 46f, 240f);
                    ClassicRpgUiRuntime.ApplyButton(progressionButton, ClassicRpgButtonSkin.MiniGold);
                }
            }

            StyleTextBlock(Txt_LobbyCode, ClassicRpgTextTone.Title, 24f, ClassicRpgUiRuntime.WarmGold);
            StyleTextBlock(Txt_MemberList, ClassicRpgTextTone.Body, 18f, ClassicRpgUiRuntime.BrightText);
            StyleTextBlock(Txt_QueueStatus, ClassicRpgTextTone.Body, 22f, ClassicRpgUiRuntime.BrightText);
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
                ClassicRpgUiRuntime.ApplyPanel(image, ClassicRpgPanelSkin.PortraitBackdrop, true, new Color(0.16f, 0.13f, 0.10f, 0.97f));

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
                    _premiumScreenSubtitle.text = "Stay ready while the war room prepares your match and the battlefield content warms up.";
                    break;
                case 5:
                    _premiumScreenTitle.text = "Lobby Assembly";
                    _premiumScreenSubtitle.text = "Review the roster, ready your party, and launch with a stronger sense of occasion.";
                    break;
                case 3:
                    _premiumScreenTitle.text = "Match Format";
                    _premiumScreenSubtitle.text = "Set the format for the upcoming battle.";
                    break;
                default:
                    _premiumScreenTitle.text = "Choose Your Match";
                    _premiumScreenSubtitle.text = "Enter matchmaking, create a private room, or inspect progression from a more premium command hub.";
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
            ClassicRpgUiRuntime.ApplyButton(button, skin);
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

        static void EnsureEventSystem()
        {
            var lobbyUi = FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            var existing = SceneEventSystemUtility.FindBest(lobbyUi);

            if (existing == null)
            {
                var go = new GameObject("LobbyEventSystem");
                existing = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                Debug.Log("[LobbyUI] Created fallback EventSystem for lobby UI.");
            }
            else if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
                Debug.Log("[LobbyUI] Reactivated inactive EventSystem for lobby UI.");
            }

            if (existing.GetComponent<BaseInputModule>() == null)
            {
                existing.gameObject.AddComponent<StandaloneInputModule>();
                Debug.Log("[LobbyUI] Added missing StandaloneInputModule to existing EventSystem.");
            }

            if (existing.GetComponent<SingleEventSystem>() == null)
            {
                existing.gameObject.AddComponent<SingleEventSystem>();
            }
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

            const int settingRowCount = 5;
            const float quitButtonHeight = 42f;
            float rowsHeight = ResolveSettingsRowsHeight(settingRowCount);
            float panelHeight = ResolveSettingsPanelHeight(rowsHeight, quitButtonHeight);

            var root = new GameObject("LobbySettingsPanel", typeof(RectTransform), typeof(FloatingSettingsPanel));
            root.transform.SetParent(_canvasRect, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(settingsPanelSize.x + settingsButtonSize + settingsPanelGap, Mathf.Max(panelHeight, settingsButtonSize));
            rect.anchoredPosition = new Vector2(-settingsRightInset, -settingsTopInset);
            _settingsPanelRoot = rect;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(root.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(settingsPanelSize.x, panelHeight);

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.08f, 0.11f, 0.15f, 0.96f);

            var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.spacing = 6f;
            panelLayout.padding = new RectOffset(10, 10, 10, 10);

            var title = CreateSettingsText(panel.transform, "Title", "Settings", 12, new Color(0.90f, 0.93f, 0.97f, 0.94f));
            title.fontStyle = FontStyles.SmallCaps;
            var titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 16f;

            var rows = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            rows.transform.SetParent(panel.transform, false);
            var rowsLayoutElement = rows.GetComponent<LayoutElement>();
            rowsLayoutElement.preferredHeight = rowsHeight;
            rowsLayoutElement.flexibleHeight = 1f;

            var rowsLayout = rows.GetComponent<VerticalLayoutGroup>();
            rowsLayout.childAlignment = TextAnchor.UpperCenter;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = false;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.spacing = settingsButtonSpacing;

            CreateSettingsActionRow(
                rows.transform,
                "TiltRow",
                "TiltUp",
                "Tilt +",
                new Color(0.16f, 0.24f, 0.32f, 0.98f),
                out var tiltUpButton,
                out _txtSettingsTiltValue,
                "TiltDown",
                "Tilt -",
                new Color(0.16f, 0.24f, 0.32f, 0.98f),
                out var tiltDownButton);

            CreateSettingsActionRow(
                rows.transform,
                "ZoomRow",
                "ZoomIn",
                "Zoom +",
                new Color(0.18f, 0.28f, 0.22f, 0.98f),
                out var zoomInButton,
                out _txtSettingsZoomValue,
                "ZoomOut",
                "Zoom -",
                new Color(0.18f, 0.28f, 0.22f, 0.98f),
                out var zoomOutButton);

            CreateSettingsActionRow(
                rows.transform,
                "RotateRow",
                "RotateLeft",
                "Rot L",
                new Color(0.28f, 0.20f, 0.16f, 0.98f),
                out var rotateLeftButton,
                out _txtSettingsRotationValue,
                "RotateRight",
                "Rot R",
                new Color(0.28f, 0.20f, 0.16f, 0.98f),
                out var rotateRightButton);

            CreateSettingsActionRow(
                rows.transform,
                "EngagementRow",
                "EngagementOff",
                "Ring Off",
                new Color(0.24f, 0.18f, 0.32f, 0.98f),
                out var engagementOffButton,
                out _txtSettingsEngagementValue,
                "EngagementOn",
                "Ring On",
                new Color(0.24f, 0.18f, 0.32f, 0.98f),
                out var engagementOnButton);

            CreateSettingsActionRow(
                rows.transform,
                "HealthBarsRow",
                "HealthBarsOff",
                "HP Off",
                new Color(0.23f, 0.26f, 0.16f, 0.98f),
                out var healthBarsOffButton,
                out _txtSettingsHealthBarsValue,
                "HealthBarsOn",
                "HP On",
                new Color(0.23f, 0.26f, 0.16f, 0.98f),
                out var healthBarsOnButton);

            var quitButton = CreateSettingsButton(rows.transform, "QuitButton", "Quit Game", new Color(0.42f, 0.17f, 0.17f, 0.98f), 42f);

            var gear = new GameObject("GearButton", typeof(RectTransform), typeof(Image), typeof(Button));
            gear.transform.SetParent(root.transform, false);
            var gearRect = gear.GetComponent<RectTransform>();
            gearRect.anchorMin = new Vector2(1f, 1f);
            gearRect.anchorMax = new Vector2(1f, 1f);
            gearRect.pivot = new Vector2(1f, 1f);
            gearRect.sizeDelta = new Vector2(settingsButtonSize, settingsButtonSize);
            gearRect.anchoredPosition = Vector2.zero;

            var gearImage = gear.GetComponent<Image>();
            gearImage.color = new Color(0.11f, 0.15f, 0.19f, 0.98f);

            var gearLabel = CreateSettingsText(gear.transform, "Label", "Menu", 16, new Color(0.96f, 0.97f, 0.99f, 1f));
            gearLabel.rectTransform.anchorMin = Vector2.zero;
            gearLabel.rectTransform.anchorMax = Vector2.one;
            gearLabel.rectTransform.offsetMin = Vector2.zero;
            gearLabel.rectTransform.offsetMax = Vector2.zero;

            var settingsPanel = root.GetComponent<FloatingSettingsPanel>();
            Vector2 expandedPosition = new Vector2(-settingsButtonSize - settingsPanelGap, 0f);
            Vector2 collapsedPosition = expandedPosition + new Vector2(18f, 0f);
            settingsPanel.Configure(
                rect,
                panelRect,
                panel.GetComponent<CanvasGroup>(),
                gear.GetComponent<Button>(),
                false,
                "lobby.settings_panel",
                true,
                expandedPosition,
                collapsedPosition);

            tiltUpButton.onClick.AddListener(() => AdjustCameraTilt(settingsTiltStep));
            tiltDownButton.onClick.AddListener(() => AdjustCameraTilt(-settingsTiltStep));
            zoomInButton.onClick.AddListener(() => AdjustCameraZoom(-settingsZoomStep));
            zoomOutButton.onClick.AddListener(() => AdjustCameraZoom(settingsZoomStep));
            rotateLeftButton.onClick.AddListener(() => AdjustCameraRotation(-settingsRotateStep));
            rotateRightButton.onClick.AddListener(() => AdjustCameraRotation(settingsRotateStep));
            engagementOffButton.onClick.AddListener(() => UserPreferencesManager.SetEngagementCirclesVisible(false));
            engagementOnButton.onClick.AddListener(() => UserPreferencesManager.SetEngagementCirclesVisible(true));
            healthBarsOffButton.onClick.AddListener(() => UserPreferencesManager.SetHealthBarsVisible(false));
            healthBarsOnButton.onClick.AddListener(() => UserPreferencesManager.SetHealthBarsVisible(true));
            quitButton.onClick.AddListener(OnQuitPressed);

            RefreshSettingsPanelValues();
        }

        void CreateSettingsActionRow(
            Transform parent,
            string name,
            string leftButtonName,
            string leftLabel,
            Color leftButtonColor,
            out Button leftButton,
            out TMP_Text valueLabel,
            string rightButtonName,
            string rightLabel,
            Color rightButtonColor,
            out Button rightButton)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var rowLayoutElement = row.GetComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = 42f;
            rowLayoutElement.flexibleWidth = 1f;

            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = settingsButtonSpacing * 0.5f;

            leftButton = CreateSettingsButton(row.transform, leftButtonName, leftLabel, leftButtonColor);
            valueLabel = CreateSettingsValueDisplay(row.transform, "Value");
            rightButton = CreateSettingsButton(row.transform, rightButtonName, rightLabel, rightButtonColor);
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

        TMP_Text CreateSettingsValueDisplay(Transform parent, string name)
        {
            var valueGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            valueGo.transform.SetParent(parent, false);
            valueGo.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.18f, 0.98f);

            var layout = valueGo.GetComponent<LayoutElement>();
            layout.minWidth = settingsValueWidth;
            layout.preferredWidth = settingsValueWidth;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 1f;

            var valueText = CreateSettingsText(valueGo.transform, "Label", "--", 11, new Color(0.96f, 0.97f, 0.99f, 0.96f));
            valueText.rectTransform.anchorMin = Vector2.zero;
            valueText.rectTransform.anchorMax = Vector2.one;
            valueText.rectTransform.offsetMin = new Vector2(2f, 2f);
            valueText.rectTransform.offsetMax = new Vector2(-2f, -2f);
            valueText.alignment = TextAlignmentOptions.Center;
            return valueText;
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

        void AdjustCameraTilt(float delta)
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
                controller.AdjustTilt(delta);
        }

        void AdjustCameraZoom(float delta)
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
                controller.AdjustZoom(delta);
        }

        void AdjustCameraRotation(float delta)
        {
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
                controller.AdjustRotation(delta);
        }

        void RefreshSettingsPanelValues()
        {
            if (_txtSettingsTiltValue == null
                && _txtSettingsZoomValue == null
                && _txtSettingsRotationValue == null
                && _txtSettingsEngagementValue == null
                && _txtSettingsHealthBarsValue == null)
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

            SetSettingsValue(_txtSettingsEngagementValue, FormatToggleValue(preferences.visuals.showEngagementCircles));
            SetSettingsValue(_txtSettingsHealthBarsValue, FormatToggleValue(preferences.visuals.showHealthBars));
        }

        void OnQuitPressed()
        {
            Play(AudioManager.SFX.ButtonClick);
            if (TryQuitGame(SetStatus))
                SetStatus("Closing game...");
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

        void SetStatus(string msg) { if (TxtStatus != null) TxtStatus.text = msg; }
        void Play(AudioManager.SFX sfx) => AudioManager.I?.Play(sfx);
    }
}
