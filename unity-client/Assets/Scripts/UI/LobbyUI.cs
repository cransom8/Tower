// LobbyUI.cs — Wizard lobby (Phase U5 + U7 + U8).
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
//   ├── Panel_Loadout                 — Phase U7: loadout slot picker (LoadoutUI component)
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
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
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

        [Header("Loadout Step (Phase U7)")]
        public LoadoutUI  LoadoutStep;          // Panel_Loadout with LoadoutUI component

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

        // ── Wizard state ──────────────────────────────────────────────────────
        const  string _gameType = "line_wars";
        string _matchFormat = "1v1";
        bool   _pendingRanked;
        bool   _pendingPrivateLobby;
        int[]  _pendingUnitTypeIds;

        // ── Lobby state ───────────────────────────────────────────────────────
        bool          _isHost;
        bool          _isReady;
        LobbySnapshot _currentLobby;

        // ── Queue state ───────────────────────────────────────────────────────
        float _queueElapsed;
        bool  _inQueue;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            var nm = NetworkManager.Instance;
            if (nm != null)
            {
                nm.OnConnected      += HandleConnected;
                nm.OnDisconnected   += HandleDisconnected;
                nm.OnQueueStatus    += HandleQueueStatus;
                nm.OnMatchFound     += HandleMatchFound;
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

            // Step 2 — Format (shown second, back returns to Type)
            Btn_1v1.onClick.AddListener(() => SelectFormat("1v1"));
            Btn_2v2.onClick.AddListener(() => SelectFormat("2v2"));
            Btn_Back_Step2.onClick.AddListener(() => GoToStep(2));

            // Step 4A
            Btn_CancelQueue.onClick.AddListener(OnCancelQueue);

            // Step 4B
            Btn_Ready.onClick.AddListener(OnToggleReady);
            Btn_Launch.onClick.AddListener(OnLaunch);
            Btn_AddBot_Easy.onClick.AddListener(() => { ActionSender.LobbyAddBot("easy");   Play(AudioManager.SFX.ButtonClick); });
            Btn_AddBot_Medium.onClick.AddListener(() => { ActionSender.LobbyAddBot("medium"); Play(AudioManager.SFX.ButtonClick); });
            Btn_AddBot_Hard.onClick.AddListener(() => { ActionSender.LobbyAddBot("hard");   Play(AudioManager.SFX.ButtonClick); });
            Btn_Leave.onClick.AddListener(OnLeaveLobby);

            // Loadout step (Phase U7)
            if (LoadoutStep != null)
            {
                LoadoutStep.OnConfirmed += OnLoadoutConfirmed;
                LoadoutStep.OnBack      += () => GoToStep(3);
                LoadoutStep.gameObject.SetActive(false);
            }

            // Leaderboard (Phase U8)
            if (Btn_ToggleLeaderboard != null) Btn_ToggleLeaderboard.onClick.AddListener(ShowLeaderboard);
            if (Btn_HideLeaderboard   != null) Btn_HideLeaderboard.onClick.AddListener(HideLeaderboard);
            if (Panel_Leaderboard     != null) Panel_Leaderboard.SetActive(false);

            if (Txt_DisplayName != null)
                Txt_DisplayName.text = AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Guest";

            GoToStep(2);
            SetStatus(nm?.IsConnected == true ? "Choose a type." : "Connecting...");

            // Fetch leaderboard + season info in background
            StartCoroutine(FetchLeaderboard());
            StartCoroutine(FetchSeasonInfo());
        }

        void OnDestroy()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnConnected      -= HandleConnected;
            nm.OnDisconnected   -= HandleDisconnected;
            nm.OnQueueStatus    -= HandleQueueStatus;
            nm.OnMatchFound     -= HandleMatchFound;
            nm.OnLobbyCreated   -= HandleLobbyCreated;
            nm.OnLobbyJoined    -= HandleLobbyJoined;
            nm.OnLobbyUpdate    -= HandleLobbyUpdate;
            nm.OnLobbyLeft      -= HandleLobbyLeft;
            nm.OnLobbyError     -= HandleLobbyError;
            nm.OnErrorMsg       -= HandleError;

            if (LoadoutStep != null)
            {
                LoadoutStep.OnConfirmed -= OnLoadoutConfirmed;
            }
        }

        void Update()
        {
            if (!_inQueue) return;
            _queueElapsed += Time.deltaTime;
            RefreshQueueDisplay(null);
        }

        // ── Step navigation ───────────────────────────────────────────────────
        // Steps: 2=Type (first), 3=Format (second), 4=Queue, 5=Lobby, 6=Loadout
        void GoToStep(int step)
        {
            Panel_Step3_Type.SetActive(step == 2);
            Panel_Step2_Format.SetActive(step == 3);
            Panel_Step4A_Queue.SetActive(step == 4);
            Panel_Step4B_Lobby.SetActive(step == 5);
            if (LoadoutStep != null)
            {
                LoadoutStep.gameObject.SetActive(step == 6);
                if (step == 6) LoadoutStep.Refresh();
            }

            if (step == 2) RefreshTypeButtons();
            if (step == 3) RefreshFormatButtons();
        }

        // ── Step 3 — Type (shown first) ───────────────────────────────────────
        void RefreshTypeButtons()
        {
            Btn_Ranked.gameObject.SetActive(true);
            Btn_Casual.gameObject.SetActive(true);
            if (Input_JoinCode != null)  Input_JoinCode.gameObject.SetActive(false);
            if (Btn_JoinConfirm != null) Btn_JoinConfirm.gameObject.SetActive(false);
        }

        void OnQueueRanked()
        {
            if (!AuthManager.IsAuthenticated) { SetStatus("Sign in to use ranked queue."); Play(AudioManager.SFX.Error); return; }
            Play(AudioManager.SFX.ButtonClick);
            _pendingRanked = true;
            GoToStep(3);
            SetStatus("Choose a format.");
        }

        void OnQueueCasual()
        {
            Play(AudioManager.SFX.ButtonClick);
            _pendingRanked = false;
            GoToStep(3);
            SetStatus("Choose a format.");
        }

        // ── Step 2 — Format (shown second) ───────────────────────────────────
        void SelectFormat(string format)
        {
            _matchFormat = format;
            Play(AudioManager.SFX.ButtonClick);
            if (LoadoutStep != null)
            {
                GoToStep(6);
                SetStatus("Choose a loadout (optional).");
            }
            else if (_pendingPrivateLobby)
            {
                _pendingPrivateLobby = false;
                CreatePrivateLobby();
            }
            else
            {
                EnterQueue();
            }
        }

        void RefreshFormatButtons()
        {
            Btn_1v1.gameObject.SetActive(true);
            Btn_2v2.gameObject.SetActive(true);
        }

        void OnLoadoutConfirmed(int[] unitTypeIds)
        {
            _pendingUnitTypeIds = unitTypeIds;
            if (_pendingPrivateLobby)
            {
                _pendingPrivateLobby = false;
                CreatePrivateLobby();
            }
            else
            {
                EnterQueue();
            }
        }

        void EnterQueue()
        {
            ActionSender.QueueEnter(_gameType, _matchFormat, ranked: _pendingRanked, unitTypeIds: _pendingUnitTypeIds);
            _pendingUnitTypeIds = null;
            _inQueue      = true;
            _queueElapsed = 0f;
            GoToStep(4);
            string rankLabel = _pendingRanked ? "ranked" : "casual";
            SetStatus($"Finding {rankLabel} {_matchFormat} match...");
        }

        void OnCreatePrivateLobby()
        {
            Play(AudioManager.SFX.ButtonClick);
            _pendingPrivateLobby = true;
            GoToStep(3);
            SetStatus("Choose a format.");
        }

        void CreatePrivateLobby()
        {
            string pvpMode = _matchFormat == "ffa" ? "ffa" : "teams";
            ActionSender.LobbyCreate(_gameType, _matchFormat, pvpMode, DisplayName, _pendingUnitTypeIds);
            _pendingUnitTypeIds = null;
            SetStatus("Creating lobby...");
        }

        void OnShowJoinInput()
        {
            Play(AudioManager.SFX.ButtonClick);
            if (Input_JoinCode != null)  Input_JoinCode.gameObject.SetActive(true);
            if (Btn_JoinConfirm != null) Btn_JoinConfirm.gameObject.SetActive(true);
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
            GoToStep(3);
            SetStatus("Queue cancelled.");
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
                    string team = !string.IsNullOrEmpty(mem.team) ? $" [{FormatTeamName(mem.team)}]" : "";
                    sb.AppendLine($"{mem.name}{you}{host}{team}{rdy}");
                }
            }
            if (lobby.botSlots != null)
                foreach (var bot in lobby.botSlots)
                    sb.AppendLine($"CPU ({bot.difficulty}) [Bot]");
            Txt_MemberList.text = sb.ToString();
        }

        static string FormatTeamName(string t) => (t ?? string.Empty).ToLowerInvariant() switch
        {
            "left"  => "Left Team",
            "right" => "Right Team",
            _       => t
        };

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
        void HandleConnected()   => SetStatus("Choose a type.");
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

            LoadingScreen.LoadScene("Game_ML");
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

            string url = BaseUrl + "/leaderboard?mode=2v2_ranked&limit=20";
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

            string url = BaseUrl + "/leaderboard?mode=2v2_ranked&limit=1";
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
                    ? NetworkManager.Instance.ServerUrl
                    : "http://localhost:3000";
#endif
            }
        }

        void SetStatus(string msg) { if (TxtStatus != null) TxtStatus.text = msg; }
        void Play(AudioManager.SFX sfx) => AudioManager.I?.Play(sfx);
    }
}
