// NetworkManager.cs — Socket.IO singleton. Persists across scenes.
//
// SETUP (Unity Inspector):
//   1. Create empty GameObject "NetworkManager" in Lobby scene.
//   2. Attach this script.
//   3. Set ServerUrl to Railway URL or http://localhost:3000
//
// WebGL builds: uses native browser Socket.IO via SocketIOBridge.jslib.
// Editor / Standalone: uses SocketIOUnity (itisnajim/SocketIOUnity).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using CastleDefender.Net;

namespace CastleDefender.Net
{
    public class NetworkManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static NetworkManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Connection")]
        [Tooltip("Production server URL (used in builds and WebGL)")]
        public string ServerUrl = "https://castle-defender-production.up.railway.app";

        [Tooltip("Server URL used when running in the Unity Editor (overrides ServerUrl)")]
        public string EditorServerUrl = "http://127.0.0.1:3000";

        // ── Session state ─────────────────────────────────────────────────────
        public string MySocketId   { get; private set; }
        public int    MyLaneIndex  { get; set; } = 0;
        public string MySide       { get; set; }
        public string MyRoomCode   { get; private set; }
        public bool   IsConnected  { get; private set; }
        public LobbySnapshot CurrentLobby { get; private set; }

        // ── Cross-scene loadout cache ─────────────────────────────────────────
        // Populated as soon as the per-player ml_match_config arrives, which
        // typically happens before Game_ML finishes loading.  CmdBar and
        // Legacy gameplay UI used this on Start() to avoid the
        // race condition where OnMLMatchConfig fires before any scene subscriber
        // has a chance to hook in.
        public LoadoutEntry[] LastMatchLoadout { get; private set; }
        public MLMatchConfig LastMLMatchConfig { get; private set; }

        // Cached so LoadoutPhaseManager.Start() can pick it up after scene load.
        // Cleared when ml_loadout_phase_end or ml_match_config(loadout) arrives.
        public MLLoadoutPhaseStartPayload PendingLoadoutPhase { get; private set; }

        // Last preparation state — replayed in LoadoutPhaseManager.Start() because
        // the Loadout scene may still be loading when the first broadcast arrives.
        public MLMatchPreparationStatePayload LastPreparationState { get; private set; }
        public MLMatchReadyPayload LastMLMatchReady { get; private set; }
        public MLWaveReadyStatePayload LastMLWaveReadyState { get; private set; }
        public MLWaveStartPayload LastMLWaveStart { get; private set; }

        public string CurrentMLMatchState { get; private set; } = "active_survival";
        public MLPvPResolvedPayload LastMLPvPResolved { get; private set; }
        public MLGameOverPayload LastMLGameOver { get; private set; }
        public ClassicGameOverPayload LastClassicGameOver { get; private set; }
        public RematchStatusPayload LastRematchStatus { get; private set; }
        bool _finalGameOverHandledForCurrentMatch;
        bool _pendingLoadoutReadySignal;
        bool _pendingGameplayReadySignal;
        bool _loggedFirstMLSnapshotForCurrentMatch;

        // ── ML Lobby events ───────────────────────────────────────────────────
        public event Action<MLRoomCreatedPayload>     OnMLRoomCreated;
        public event Action<MLRoomJoinedPayload>      OnMLRoomJoined;
        public event Action<MLLobbyUpdate>            OnMLLobbyUpdate;
        public event Action<MLMatchReadyPayload>      OnMLMatchReady;
        public event Action<MLMatchConfig>            OnMLMatchConfig;

        // ── Loadout phase events ──────────────────────────────────────────────
        public event Action<MLLoadoutPhaseStartPayload>       OnMLLoadoutPhaseStart;
        public event Action<MLLoadoutPhaseEndPayload>         OnMLLoadoutPhaseEnd;
        public event Action<MLMatchPreparationStatePayload>   OnMLMatchPreparationState;
        public event Action<MLMatchCancelledPayload>          OnMLMatchCancelled;
        public event Action<MLWaveReadyStatePayload>          OnMLWaveReadyState;
        public event Action<MLWaveStartPayload>               OnMLWaveStart;

        // ── Classic Lobby events ──────────────────────────────────────────────
        public event Action<ClassicRoomCreatedPayload>  OnClassicRoomCreated;
        public event Action<ClassicRoomJoinedPayload>   OnClassicRoomJoined;
        public event Action<ClassicMatchReadyPayload>   OnClassicMatchReady;

        // ── Snapshot events ───────────────────────────────────────────────────
        public event Action<MLSnapshot>      OnMLStateSnapshot;
        public event Action<ClassicSnapshot> OnClassicStateSnapshot;

        // ── Game over ─────────────────────────────────────────────────────────
        public event Action<MLPvPResolvedPayload>      OnMLPvPResolved;
        public event Action<MLSurvivalContinuationStartedPayload> OnMLSurvivalContinuationStarted;
        public event Action<MLGameOverPayload>      OnMLGameOver;
        public event Action<ClassicGameOverPayload> OnClassicGameOver;

        // ── Gameplay events ───────────────────────────────────────────────────
        public event Action<RematchVotePayload>          OnRematchVote;
        public event Action<RematchStatusPayload>        OnRematchStatus;
        public event Action<RematchStartingPayload>      OnRematchStarting;
        public event Action<ActionAppliedPayload>        OnActionApplied;
        public event Action<MLPlayerEliminatedPayload>   OnMLPlayerEliminated;
        public event Action<MLSpectatorJoinPayload>      OnMLSpectatorJoin;
        public event Action<MLLaneReassignedPayload>     OnMLLaneReassigned;

        // ── Send queue ────────────────────────────────────────────────────────

        // ── Queue & Lobby system (Phase U5) ───────────────────────────────────
        public event Action<QueueStatusPayload>  OnQueueStatus;
        public event Action<MatchFoundPayload>   OnMatchFound;
        public event Action<LobbyCreatedPayload> OnLobbyCreated;
        public event Action<LobbyJoinedPayload>  OnLobbyJoined;
        public event Action<LobbyUpdatePayload>  OnLobbyUpdate;
        public event Action<LobbyLeftPayload>    OnLobbyLeft;
        public event Action<LobbyErrorPayload>   OnLobbyError;

        // ── Competitive events (Phase U8) ─────────────────────────────────────
        public event Action<RatingUpdatePayload> OnRatingUpdate;

        // ── Connection events ─────────────────────────────────────────────────
        public event Action<ErrorPayload> OnErrorMsg;
        public event Action<MLAllChatMessagePayload> OnAllChatMessage;
        public event Action<FriendListEntryPayload[]> OnFriendsList;
        public event Action<FriendPresencePayload> OnFriendOnline;
        public event Action<FriendPresencePayload> OnFriendOffline;
        public event Action<FriendPresencePayload> OnFriendRequest;
        public event Action<FriendPresencePayload> OnFriendAccepted;
        public event Action<FriendPresencePayload> OnFriendRemoved;
        public event Action<ErrorPayload> OnFriendError;
        public event Action<LobbyInvitePayload> OnLobbyInvite;
        public event Action<LobbyInviteSentPayload> OnLobbyInviteSent;
        public event Action               OnConnected;
        public event Action               OnDisconnected;

        // ── Private (native/editor only) ──────────────────────────────────────
#if !UNITY_WEBGL || UNITY_EDITOR
        private SocketIOUnity _socket;
#endif

        // ── WebGL jslib imports ───────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void   JSIO_Connect(string url, string goName);
        [DllImport("__Internal")] static extern void   JSIO_On(string ev);
        [DllImport("__Internal")] static extern void   JSIO_Emit(string eventName, string json);
        [DllImport("__Internal")] static extern void   JSIO_Disconnect();
        // JWT helpers — declared here for completeness; AuthManager also imports them
        [DllImport("__Internal")] static extern string JSIO_GetJWT();
        [DllImport("__Internal")] static extern void   JSIO_SetJWT(string token);
        [DllImport("__Internal")] static extern void   JSIO_ClearJWT();
#endif

        // ── Resolved URL ──────────────────────────────────────────────────────
        public string ResolvedServerUrl
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
#elif UNITY_EDITOR
                return !string.IsNullOrEmpty(EditorServerUrl) ? EditorServerUrl : ServerUrl;
#else
                return ServerUrl;
#endif
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        static readonly JsonSerializerSettings _respSettings = new JsonSerializerSettings
        {
            // Server sends null for value-type fields (e.g. "winner":null when no winner yet).
            // Ignore nulls so int/float/bool fields keep their C# defaults instead of throwing.
            NullValueHandling = NullValueHandling.Ignore
        };

        void Start() => Connect();

        public void ReconnectForCurrentAuth(string reason = null)
        {
            string resolvedReason = string.IsNullOrWhiteSpace(reason) ? "auth change" : reason;
            Debug.Log($"[NM] Reconnecting socket for {resolvedReason}.");
            ResetConnectionSessionState(resolvedReason);
            DisconnectActiveSocket();
            Connect();
        }

        // ─────────────────────────────────────────────────────────────────────
        public void Connect()
        {
            string url = ResolvedServerUrl;
            Debug.Log($"[NM] Connecting to {url}...");

#if UNITY_WEBGL && !UNITY_EDITOR
            ConnectWebGL(url);
#else
            ConnectNative(url);
#endif
        }

        // ═════════════════════════════════════════════════════════════════════
        // WebGL path — uses SocketIOBridge.jslib (browser native Socket.IO)
        // ═════════════════════════════════════════════════════════════════════
#if UNITY_WEBGL && !UNITY_EDITOR

        void ConnectWebGL(string url)
        {
            // gameObject.name is the SendMessage target — must match NetworkManager GO name
            JSIO_Connect(url, gameObject.name);

            // Register every server event (connect/disconnect/error are wired by jslib)
            JSIO_On("ml_room_created");
            JSIO_On("ml_room_joined");
            JSIO_On("ml_lobby_update");
            JSIO_On("ml_match_ready");
            JSIO_On("ml_match_config");
            JSIO_On("ml_loadout_phase_start");
            JSIO_On("ml_loadout_phase_end");
            JSIO_On("ml_match_preparation_state");
            JSIO_On("ml_match_cancelled");
            JSIO_On("ml_wave_ready_state");
            JSIO_On("ml_wave_start");
            JSIO_On("room_created");
            JSIO_On("room_joined");
            JSIO_On("match_ready");
            JSIO_On("ml_state_snapshot");
            JSIO_On("state_snapshot");
            JSIO_On("ml_pvp_resolved");
            JSIO_On("ml_survival_continuation_started");
            JSIO_On("ml_game_over");
            JSIO_On("game_over");
            JSIO_On("rematch_vote");
            JSIO_On("rematch_status");
            JSIO_On("rematch_starting");
            JSIO_On("action_applied");
            JSIO_On("ml_player_eliminated");
            JSIO_On("ml_spectator_join");
            JSIO_On("ml_lane_reassigned");
            JSIO_On("error_message");
            JSIO_On("ml_chat_message");
            JSIO_On("friends_list");
            JSIO_On("friend_online");
            JSIO_On("friend_offline");
            JSIO_On("friend_request");
            JSIO_On("friend_accepted");
            JSIO_On("friend_removed");
            JSIO_On("friend_error");
            // Queue & Lobby (Phase U5)
            JSIO_On("queue_status");
            JSIO_On("match_found");
            JSIO_On("lobby_created");
            JSIO_On("lobby_joined");
            JSIO_On("lobby_update");
            JSIO_On("lobby_left");
            JSIO_On("lobby_error");
            JSIO_On("lobby_invite");
            JSIO_On("lobby_invite_sent");
            // Competitive (Phase U8)
            JSIO_On("rating_update");
        }

        // Called by jslib SendMessage when the socket connects
        void OnJSIO_connect(string socketId)
        {
            IsConnected = true;
            MySocketId  = socketId;
            Debug.Log($"[NM] Connected: {socketId}");
            EmitReconnectIfPending();
            OnConnected?.Invoke();
        }

        // Called by jslib SendMessage when the socket disconnects
        void OnJSIO_disconnect(string reason)
        {
            IsConnected = false;
            CurrentLobby = null;
            Debug.Log($"[NM] Disconnected: {reason}");
            OnDisconnected?.Invoke();
        }

        // Called by jslib SendMessage on connection error
        void OnJSIO_error(string message)
        {
            Debug.LogError($"[NM] Socket error: {message}");
        }

        // Called by jslib SendMessage for all registered game events.
        // Payload format: "eventName\x01{...json...}"
        void OnJSIOEvent(string payload)
        {
            int sep = payload.IndexOf('\x01');
            if (sep < 0) { Debug.LogWarning($"[NM] Malformed jslib payload: {payload}"); return; }
            string ev   = payload.Substring(0, sep);
            string json = payload.Substring(sep + 1);
            DispatchEvent(ev, json);
        }

        void DispatchEvent(string ev, string json)
        {
            switch (ev)
            {
                case "ml_room_created":
                {
                    var p = JsonUtility.FromJson<MLRoomCreatedPayload>(json);
                    MyLaneIndex = p.laneIndex;
                    MyRoomCode  = p.code;
                    Debug.Log($"[NM] ml_room_created: {p.code} lane={p.laneIndex}");
                    OnMLRoomCreated?.Invoke(p);
                    break;
                }
                case "ml_room_joined":
                {
                    var p = JsonUtility.FromJson<MLRoomJoinedPayload>(json);
                    MyLaneIndex = p.laneIndex;
                    MyRoomCode  = p.code;
                    Debug.Log($"[NM] ml_room_joined: {p.code} lane={p.laneIndex}");
                    OnMLRoomJoined?.Invoke(p);
                    break;
                }
                case "ml_lobby_update":
                    OnMLLobbyUpdate?.Invoke(JsonUtility.FromJson<MLLobbyUpdate>(json));
                    break;
                case "ml_match_ready":
                {
                    var p = JsonUtility.FromJson<MLMatchReadyPayload>(json);
                    ResetPostGameFlowState("ml_match_ready");
                    CurrentMLMatchState = "active_survival";
                    LastMLPvPResolved = null;
                    LastMLGameOver = null;
                    LastPreparationState = null;
                    LastMLWaveReadyState = null;
                    LastMLWaveStart = null;
                    LastMLMatchConfig = null;
                    LastMatchLoadout = null;
                    LastMLMatchReady = p;
                    _loggedFirstMLSnapshotForCurrentMatch = false;
                    Debug.Log($"[NM] ml_match_ready playerCount={p.playerCount}");
                    OnMLMatchReady?.Invoke(p);
                    TryEmitPendingCriticalReadySignals("ml_match_ready");
                    break;
                }
                case "ml_match_config":
                {
                    var cfg = JsonUtility.FromJson<MLMatchConfig>(json);
                    cfg = CacheMLMatchConfig(cfg, "ml_match_config");
                    if (cfg == null)
                        break;
                    OnMLMatchConfig?.Invoke(cfg);
                    TryEmitPendingCriticalReadySignals("ml_match_config");
                    break;
                }
                case "ml_loadout_phase_start":
                {
                    var p = JsonUtility.FromJson<MLLoadoutPhaseStartPayload>(json);
                    PendingLoadoutPhase = p;
                    ClearPendingLoadoutReady("ml_loadout_phase_start");
                    Debug.Log($"[NM] ml_loadout_phase_start mode={p.selectionMode} timeout={p.timeoutSeconds}s races={p.availableRaceIds?.Length ?? 0} units={p.availableUnits?.Length ?? 0}");
                    OnMLLoadoutPhaseStart?.Invoke(p);
                    break;
                }
                case "ml_loadout_phase_end":
                {
                    var p = JsonUtility.FromJson<MLLoadoutPhaseEndPayload>(json);
                    PendingLoadoutPhase = null;
                    Debug.Log($"[NM] ml_loadout_phase_end reason={p.reason}");
                    OnMLLoadoutPhaseEnd?.Invoke(p);
                    break;
                }
                case "ml_match_preparation_state":
                {
                    var p = JsonUtility.FromJson<MLMatchPreparationStatePayload>(json);
                    LastPreparationState = p;
                    OnMLMatchPreparationState?.Invoke(p);
                    break;
                }
                case "ml_match_cancelled":
                {
                    var p = JsonUtility.FromJson<MLMatchCancelledPayload>(json);
                    ClearPendingLoadoutReady("ml_match_cancelled");
                    ClearPendingGameplayReady("ml_match_cancelled");
                    Debug.LogWarning($"[NM] ml_match_cancelled reason={p.reason} message={p.message}");
                    OnMLMatchCancelled?.Invoke(p);
                    break;
                }
                case "ml_wave_ready_state":
                {
                    var p = JsonUtility.FromJson<MLWaveReadyStatePayload>(json);
                    LastMLWaveReadyState = p;
                    OnMLWaveReadyState?.Invoke(p);
                    break;
                }
                case "ml_wave_start":
                {
                    var p = JsonUtility.FromJson<MLWaveStartPayload>(json);
                    LastMLWaveStart = p;
                    Debug.Log($"[NM] ml_wave_start round={p.roundNumber}");
                    OnMLWaveStart?.Invoke(p);
                    break;
                }
                case "room_created":
                {
                    var p = JsonUtility.FromJson<ClassicRoomCreatedPayload>(json);
                    MySide     = p.side;
                    MyRoomCode = p.code;
                    Debug.Log($"[NM] room_created: {p.code} side={p.side}");
                    OnClassicRoomCreated?.Invoke(p);
                    break;
                }
                case "room_joined":
                {
                    var p = JsonUtility.FromJson<ClassicRoomJoinedPayload>(json);
                    MySide     = p.side;
                    MyRoomCode = p.code;
                    Debug.Log($"[NM] room_joined: {p.code} side={p.side}");
                    OnClassicRoomJoined?.Invoke(p);
                    break;
                }
                case "match_ready":
                {
                    var p = JsonUtility.FromJson<ClassicMatchReadyPayload>(json);
                    Debug.Log($"[NM] match_ready: {p.code}");
                    OnClassicMatchReady?.Invoke(p);
                    break;
                }
                case "ml_state_snapshot":
                {
                    var p = JsonUtility.FromJson<MLSnapshot>(json);
                    CurrentMLMatchState = !string.IsNullOrEmpty(p.matchState) ? p.matchState : CurrentMLMatchState;
                    ClearPendingGameplayReady("ml_state_snapshot");
                    if (!_loggedFirstMLSnapshotForCurrentMatch)
                    {
                        _loggedFirstMLSnapshotForCurrentMatch = true;
                        Debug.Log(
                            $"[NM] first ml_state_snapshot round={p.roundNumber} matchState={p.matchState ?? "<null>"} " +
                            $"lanes={p.lanes?.Length ?? 0} hasCachedLayout={(LastMLMatchConfig?.battlefieldLayout != null)}");
                    }
                    OnMLStateSnapshot?.Invoke(p);
                    break;
                }
                case "ml_pvp_resolved":
                {
                    var p = JsonUtility.FromJson<MLPvPResolvedPayload>(json);
                    LastMLPvPResolved = p;
                    CurrentMLMatchState = "pvp_resolved";
                    OnMLPvPResolved?.Invoke(p);
                    break;
                }
                case "ml_survival_continuation_started":
                {
                    LastMLPvPResolved = null;
                    CurrentMLMatchState = "active_survival";
                    OnMLSurvivalContinuationStarted?.Invoke(JsonUtility.FromJson<MLSurvivalContinuationStartedPayload>(json));
                    break;
                }
                case "state_snapshot":
                    OnClassicStateSnapshot?.Invoke(JsonUtility.FromJson<ClassicSnapshot>(json));
                    break;
                case "ml_game_over":
                {
                    var p = JsonUtility.FromJson<MLGameOverPayload>(json);
                    Debug.Log($"[NM] ml_game_over winner lane={p.winnerLaneIndex}");
                    HandleIncomingMLGameOver(p);
                    break;
                }
                case "game_over":
                {
                    var p = JsonUtility.FromJson<ClassicGameOverPayload>(json);
                    Debug.Log($"[NM] game_over winner={p.winner}");
                    LastClassicGameOver = p;
                    OnClassicGameOver?.Invoke(p);
                    break;
                }
                case "rematch_vote":
                    OnRematchVote?.Invoke(JsonUtility.FromJson<RematchVotePayload>(json));
                    break;
                case "rematch_status":
                {
                    var p = JsonUtility.FromJson<RematchStatusPayload>(json);
                    LastRematchStatus = p;
                    OnRematchStatus?.Invoke(p);
                    break;
                }
                case "rematch_starting":
                    OnRematchStarting?.Invoke(JsonUtility.FromJson<RematchStartingPayload>(json));
                    break;
                case "action_applied":
                {
                    var p = JsonUtility.FromJson<ActionAppliedPayload>(json);
                    Debug.Log($"[NM] action_applied type={p.type} lane={p.laneIndex} tick={p.tick} gold={p.gold} income={p.income}");
                    OnActionApplied?.Invoke(p);
                    break;
                }
                case "ml_player_eliminated":
                {
                    var p = JsonUtility.FromJson<MLPlayerEliminatedPayload>(json);
                    Debug.Log($"[NM] ml_player_eliminated lane={p.laneIndex}");
                    OnMLPlayerEliminated?.Invoke(p);
                    break;
                }
                case "ml_spectator_join":
                    OnMLSpectatorJoin?.Invoke(JsonUtility.FromJson<MLSpectatorJoinPayload>(json));
                    break;
                case "ml_lane_reassigned":
                {
                    var p = JsonUtility.FromJson<MLLaneReassignedPayload>(json);
                    MyLaneIndex = p.laneIndex;
                    OnMLLaneReassigned?.Invoke(p);
                    break;
                }
                case "error_message":
                {
                    var p = JsonUtility.FromJson<ErrorPayload>(json);
                    Debug.LogWarning($"[NM] error_message: {p.message}");
                    OnErrorMsg?.Invoke(p);
                    break;
                }
                // ── Queue & Lobby (Phase U5) ──────────────────────────────────
                case "ml_chat_message":
                {
                    var p = JsonUtility.FromJson<MLAllChatMessagePayload>(json);
                    OnAllChatMessage?.Invoke(p);
                    break;
                }
                case "friends_list":
                    OnFriendsList?.Invoke(FromJson<FriendListEntryPayload[]>(json) ?? Array.Empty<FriendListEntryPayload>());
                    break;
                case "friend_online":
                    OnFriendOnline?.Invoke(FromJson<FriendPresencePayload>(json));
                    break;
                case "friend_offline":
                    OnFriendOffline?.Invoke(FromJson<FriendPresencePayload>(json));
                    break;
                case "friend_request":
                    OnFriendRequest?.Invoke(FromJson<FriendPresencePayload>(json));
                    break;
                case "friend_accepted":
                    OnFriendAccepted?.Invoke(FromJson<FriendPresencePayload>(json));
                    break;
                case "friend_removed":
                    OnFriendRemoved?.Invoke(FromJson<FriendPresencePayload>(json));
                    break;
                case "friend_error":
                    OnFriendError?.Invoke(FromJson<ErrorPayload>(json));
                    break;
                case "queue_status":
                    OnQueueStatus?.Invoke(JsonUtility.FromJson<QueueStatusPayload>(json));
                    break;
                case "match_found":
                {
                    var p = JsonUtility.FromJson<MatchFoundPayload>(json);
                    MyLaneIndex = p.laneIndex;
                    MyRoomCode  = p.roomCode;
                    CurrentLobby = null;
                    Debug.Log($"[NM] match_found: {p.roomCode} lane={p.laneIndex} gameType={p.gameType}");
                    OnMatchFound?.Invoke(p);
                    break;
                }
                case "lobby_created":
                {
                    var p = JsonUtility.FromJson<LobbyCreatedPayload>(json);
                    CurrentLobby = p?.lobby;
                    Debug.Log($"[NM] lobby_created: {p.code}");
                    OnLobbyCreated?.Invoke(p);
                    break;
                }
                case "lobby_joined":
                {
                    var p = JsonUtility.FromJson<LobbyJoinedPayload>(json);
                    CurrentLobby = p?.lobby;
                    Debug.Log($"[NM] lobby_joined: {p.code}");
                    OnLobbyJoined?.Invoke(p);
                    break;
                }
                case "lobby_update":
                {
                    var p = JsonUtility.FromJson<LobbyUpdatePayload>(json);
                    CurrentLobby = p?.lobby;
                    OnLobbyUpdate?.Invoke(p);
                    break;
                }
                case "lobby_left":
                    CurrentLobby = null;
                    OnLobbyLeft?.Invoke(JsonUtility.FromJson<LobbyLeftPayload>(json));
                    break;
                case "lobby_error":
                {
                    var p = JsonUtility.FromJson<LobbyErrorPayload>(json);
                    Debug.LogWarning($"[NM] lobby_error: {p.message}");
                    OnLobbyError?.Invoke(p);
                    break;
                }
                case "lobby_invite":
                    OnLobbyInvite?.Invoke(FromJson<LobbyInvitePayload>(json));
                    break;
                case "lobby_invite_sent":
                    OnLobbyInviteSent?.Invoke(FromJson<LobbyInviteSentPayload>(json));
                    break;
                // ── Competitive (Phase U8) ────────────────────────────────────
                case "rating_update":
                {
                    var p = JsonUtility.FromJson<RatingUpdatePayload>(json);
                    Debug.Log($"[NM] rating_update: {p.oldRating:F0} → {p.newRating:F0} (delta={p.delta:+0;-0})");
                    OnRatingUpdate?.Invoke(p);
                    break;
                }
                default:
                    Debug.LogWarning($"[NM] Unhandled jslib event: {ev}");
                    break;
            }
        }

        void EmitReconnectIfPending()
        {
            string token = PlayerPrefs.GetString("reconnect_token", null);
            if (!string.IsNullOrEmpty(token))
            {
                Debug.Log("[NM] Emitting reconnect with stored token");
                // Inline emit — socket not fully ready yet, queue via a short delay
                StartCoroutine(EmitReconnectDelayed(token));
            }
        }

        System.Collections.IEnumerator EmitReconnectDelayed(string token)
        {
            yield return null; // wait one frame for socket to be fully ready
            Emit("reconnect", new { token });
        }

#endif // UNITY_WEBGL && !UNITY_EDITOR

        // ═════════════════════════════════════════════════════════════════════
        // Native / Editor path — uses SocketIOUnity
        // ═════════════════════════════════════════════════════════════════════
#if !UNITY_WEBGL || UNITY_EDITOR

        void ConnectNative(string url)
        {
            _socket?.Disconnect();
            _socket = null;

            var uri  = new Uri(url);
            var opts = new SocketIOClient.SocketIOOptions
            {
                Transport            = SocketIOClient.Transport.TransportProtocol.WebSocket,
                ReconnectionAttempts = 5,
                ReconnectionDelay    = 2000,
            };

            // Pass JWT as Socket.IO auth if authenticated
            if (AuthManager.IsAuthenticated)
                opts.Auth = new Dictionary<string, string> { ["token"] = AuthManager.Token };

            _socket = new SocketIOUnity(uri, opts);

            _socket.OnConnected += (_, __) =>
            {
                IsConnected = true;
                MySocketId  = _socket.Id;
                Debug.Log($"[NM] Connected: {MySocketId}");
                EmitReconnectIfPendingNative();
                OnConnected?.Invoke();
            };

            _socket.OnDisconnected += (_, __) =>
            {
                IsConnected = false;
                CurrentLobby = null;
                Debug.Log("[NM] Disconnected");
                OnDisconnected?.Invoke();
            };

            _socket.OnError += (_, err) =>
            {
                Debug.LogError($"[NM] Socket error: {err}");
            };

            // ── ML Lobby ─────────────────────────────────────────────────────
            _socket.OnUnityThread("ml_room_created", resp =>
            {
                var p = FromResp<MLRoomCreatedPayload>(resp);
                MyLaneIndex = p.laneIndex;
                MyRoomCode  = p.code;
                Debug.Log($"[NM] ml_room_created: {p.code} lane={p.laneIndex}");
                OnMLRoomCreated?.Invoke(p);
            });

            _socket.OnUnityThread("ml_room_joined", resp =>
            {
                var p = FromResp<MLRoomJoinedPayload>(resp);
                MyLaneIndex = p.laneIndex;
                MyRoomCode  = p.code;
                Debug.Log($"[NM] ml_room_joined: {p.code} lane={p.laneIndex}");
                OnMLRoomJoined?.Invoke(p);
            });

            _socket.OnUnityThread("ml_lobby_update", resp =>
            {
                OnMLLobbyUpdate?.Invoke(FromResp<MLLobbyUpdate>(resp));
            });

            _socket.OnUnityThread("ml_match_ready", resp =>
            {
                var p = FromResp<MLMatchReadyPayload>(resp);
                ResetPostGameFlowState("ml_match_ready");
                CurrentMLMatchState = "active_survival";
                LastMLPvPResolved = null;
                LastMLGameOver = null;
                LastPreparationState = null;
                LastMLWaveReadyState = null;
                LastMLMatchConfig = null;
                LastMatchLoadout = null;
                LastMLMatchReady = p;
                _loggedFirstMLSnapshotForCurrentMatch = false;
                Debug.Log($"[NM] ml_match_ready playerCount={p.playerCount}");
                OnMLMatchReady?.Invoke(p);
                TryEmitPendingCriticalReadySignals("ml_match_ready");
            });

            _socket.OnUnityThread("ml_match_config", resp =>
            {
                var cfg = FromResp<MLMatchConfig>(resp);
                cfg = CacheMLMatchConfig(cfg, "ml_match_config");
                if (cfg == null)
                    return;
                OnMLMatchConfig?.Invoke(cfg);
                TryEmitPendingCriticalReadySignals("ml_match_config");
            });

            _socket.OnUnityThread("ml_loadout_phase_start", resp =>
            {
                var p = FromResp<MLLoadoutPhaseStartPayload>(resp);
                PendingLoadoutPhase = p;
                ClearPendingLoadoutReady("ml_loadout_phase_start");
                Debug.Log($"[NM] ml_loadout_phase_start mode={p.selectionMode} timeout={p.timeoutSeconds}s races={p.availableRaceIds?.Length ?? 0} units={p.availableUnits?.Length ?? 0}");
                OnMLLoadoutPhaseStart?.Invoke(p);
            });

            _socket.OnUnityThread("ml_loadout_phase_end", resp =>
            {
                var p = FromResp<MLLoadoutPhaseEndPayload>(resp);
                PendingLoadoutPhase = null;
                Debug.Log($"[NM] ml_loadout_phase_end reason={p.reason}");
                OnMLLoadoutPhaseEnd?.Invoke(p);
            });

            _socket.OnUnityThread("ml_match_preparation_state", resp =>
            {
                var p = FromResp<MLMatchPreparationStatePayload>(resp);
                LastPreparationState = p;
                OnMLMatchPreparationState?.Invoke(p);
            });

            _socket.OnUnityThread("ml_match_cancelled", resp =>
            {
                var p = FromResp<MLMatchCancelledPayload>(resp);
                ClearPendingLoadoutReady("ml_match_cancelled");
                ClearPendingGameplayReady("ml_match_cancelled");
                Debug.LogWarning($"[NM] ml_match_cancelled reason={p.reason} message={p.message}");
                OnMLMatchCancelled?.Invoke(p);
            });

            _socket.OnUnityThread("ml_wave_ready_state", resp =>
            {
                var p = FromResp<MLWaveReadyStatePayload>(resp);
                LastMLWaveReadyState = p;
                OnMLWaveReadyState?.Invoke(p);
            });

            _socket.OnUnityThread("ml_wave_start", resp =>
            {
                var p = FromResp<MLWaveStartPayload>(resp);
                LastMLWaveStart = p;
                Debug.Log($"[NM] ml_wave_start round={p.roundNumber}");
                OnMLWaveStart?.Invoke(p);
            });

            // ── Classic Lobby ─────────────────────────────────────────────────
            _socket.OnUnityThread("room_created", resp =>
            {
                var p = FromResp<ClassicRoomCreatedPayload>(resp);
                MySide     = p.side;
                MyRoomCode = p.code;
                Debug.Log($"[NM] room_created: {p.code} side={p.side}");
                OnClassicRoomCreated?.Invoke(p);
            });

            _socket.OnUnityThread("room_joined", resp =>
            {
                var p = FromResp<ClassicRoomJoinedPayload>(resp);
                MySide     = p.side;
                MyRoomCode = p.code;
                Debug.Log($"[NM] room_joined: {p.code} side={p.side}");
                OnClassicRoomJoined?.Invoke(p);
            });

            _socket.OnUnityThread("match_ready", resp =>
            {
                var p = FromResp<ClassicMatchReadyPayload>(resp);
                Debug.Log($"[NM] match_ready: {p.code}");
                OnClassicMatchReady?.Invoke(p);
            });

            // ── Snapshots ─────────────────────────────────────────────────────
            _socket.OnUnityThread("ml_state_snapshot", resp =>
            {
                var p = FromResp<MLSnapshot>(resp);
                CurrentMLMatchState = !string.IsNullOrEmpty(p.matchState) ? p.matchState : CurrentMLMatchState;
                ClearPendingGameplayReady("ml_state_snapshot");
                if (!_loggedFirstMLSnapshotForCurrentMatch)
                {
                    _loggedFirstMLSnapshotForCurrentMatch = true;
                    Debug.Log(
                        $"[NM] first ml_state_snapshot round={p.roundNumber} matchState={p.matchState ?? "<null>"} " +
                        $"lanes={p.lanes?.Length ?? 0} hasCachedLayout={(LastMLMatchConfig?.battlefieldLayout != null)}");
                }
                OnMLStateSnapshot?.Invoke(p);
            });

            _socket.OnUnityThread("ml_pvp_resolved", resp =>
            {
                var p = FromResp<MLPvPResolvedPayload>(resp);
                LastMLPvPResolved = p;
                CurrentMLMatchState = "pvp_resolved";
                OnMLPvPResolved?.Invoke(p);
            });

            _socket.OnUnityThread("ml_survival_continuation_started", resp =>
            {
                LastMLPvPResolved = null;
                CurrentMLMatchState = "active_survival";
                OnMLSurvivalContinuationStarted?.Invoke(FromResp<MLSurvivalContinuationStartedPayload>(resp));
            });

            _socket.OnUnityThread("state_snapshot", resp =>
            {
                OnClassicStateSnapshot?.Invoke(FromResp<ClassicSnapshot>(resp));
            });

            // ── Game over ─────────────────────────────────────────────────────
            _socket.OnUnityThread("ml_game_over", resp =>
            {
                var p = FromResp<MLGameOverPayload>(resp);
                Debug.Log($"[NM] ml_game_over winner lane={p.winnerLaneIndex}");
                HandleIncomingMLGameOver(p);
            });

            _socket.OnUnityThread("game_over", resp =>
            {
                var p = FromResp<ClassicGameOverPayload>(resp);
                Debug.Log($"[NM] game_over winner={p.winner}");
                LastClassicGameOver = p;
                OnClassicGameOver?.Invoke(p);
            });

            // ── Gameplay ──────────────────────────────────────────────────────
            _socket.OnUnityThread("rematch_vote", resp =>
            {
                OnRematchVote?.Invoke(FromResp<RematchVotePayload>(resp));
            });

            _socket.OnUnityThread("rematch_status", resp =>
            {
                var p = FromResp<RematchStatusPayload>(resp);
                LastRematchStatus = p;
                OnRematchStatus?.Invoke(p);
            });

            _socket.OnUnityThread("rematch_starting", resp =>
            {
                OnRematchStarting?.Invoke(FromResp<RematchStartingPayload>(resp));
            });

            _socket.OnUnityThread("action_applied", resp =>
            {
                OnActionApplied?.Invoke(FromResp<ActionAppliedPayload>(resp));
            });

            _socket.OnUnityThread("ml_player_eliminated", resp =>
            {
                var p = FromResp<MLPlayerEliminatedPayload>(resp);
                Debug.Log($"[NM] ml_player_eliminated lane={p.laneIndex}");
                OnMLPlayerEliminated?.Invoke(p);
            });

            _socket.OnUnityThread("ml_spectator_join", resp =>
            {
                OnMLSpectatorJoin?.Invoke(FromResp<MLSpectatorJoinPayload>(resp));
            });

            _socket.OnUnityThread("ml_lane_reassigned", resp =>
            {
                var p = FromResp<MLLaneReassignedPayload>(resp);
                MyLaneIndex = p.laneIndex;
                OnMLLaneReassigned?.Invoke(p);
            });


            // ── Queue & Lobby (Phase U5) ───────────────────────────────────────
            _socket.OnUnityThread("queue_status", resp =>
            {
                OnQueueStatus?.Invoke(FromResp<QueueStatusPayload>(resp));
            });

            _socket.OnUnityThread("friends_list", resp =>
            {
                OnFriendsList?.Invoke(FromResp<FriendListEntryPayload[]>(resp) ?? Array.Empty<FriendListEntryPayload>());
            });

            _socket.OnUnityThread("friend_online", resp =>
            {
                OnFriendOnline?.Invoke(FromResp<FriendPresencePayload>(resp));
            });

            _socket.OnUnityThread("friend_offline", resp =>
            {
                OnFriendOffline?.Invoke(FromResp<FriendPresencePayload>(resp));
            });

            _socket.OnUnityThread("friend_request", resp =>
            {
                OnFriendRequest?.Invoke(FromResp<FriendPresencePayload>(resp));
            });

            _socket.OnUnityThread("friend_accepted", resp =>
            {
                OnFriendAccepted?.Invoke(FromResp<FriendPresencePayload>(resp));
            });

            _socket.OnUnityThread("friend_removed", resp =>
            {
                OnFriendRemoved?.Invoke(FromResp<FriendPresencePayload>(resp));
            });

            _socket.OnUnityThread("friend_error", resp =>
            {
                var p = FromResp<ErrorPayload>(resp);
                Debug.LogWarning($"[NM] friend_error: {p?.message}");
                OnFriendError?.Invoke(p);
            });

            _socket.OnUnityThread("match_found", resp =>
            {
                var p = FromResp<MatchFoundPayload>(resp);
                MyLaneIndex = p.laneIndex;
                MyRoomCode  = p.roomCode;
                CurrentLobby = null;
                Debug.Log($"[NM] match_found: {p.roomCode} lane={p.laneIndex} gameType={p.gameType}");
                OnMatchFound?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_created", resp =>
            {
                var p = FromResp<LobbyCreatedPayload>(resp);
                CurrentLobby = p?.lobby;
                Debug.Log($"[NM] lobby_created: {p.code}");
                OnLobbyCreated?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_joined", resp =>
            {
                var p = FromResp<LobbyJoinedPayload>(resp);
                CurrentLobby = p?.lobby;
                Debug.Log($"[NM] lobby_joined: {p.code}");
                OnLobbyJoined?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_update", resp =>
            {
                var p = FromResp<LobbyUpdatePayload>(resp);
                CurrentLobby = p?.lobby;
                OnLobbyUpdate?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_left", resp =>
            {
                CurrentLobby = null;
                OnLobbyLeft?.Invoke(FromResp<LobbyLeftPayload>(resp));
            });

            _socket.OnUnityThread("lobby_error", resp =>
            {
                var p = FromResp<LobbyErrorPayload>(resp);
                Debug.LogWarning($"[NM] lobby_error: {p?.message}");
                OnLobbyError?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_invite", resp =>
            {
                OnLobbyInvite?.Invoke(FromResp<LobbyInvitePayload>(resp));
            });

            _socket.OnUnityThread("lobby_invite_sent", resp =>
            {
                OnLobbyInviteSent?.Invoke(FromResp<LobbyInviteSentPayload>(resp));
            });

            // ── Competitive (Phase U8) ────────────────────────────────────────
            _socket.OnUnityThread("rating_update", resp =>
            {
                var p = FromResp<RatingUpdatePayload>(resp);
                Debug.Log($"[NM] rating_update: {p.oldRating:F0} → {p.newRating:F0} (delta={p.delta:+0;-0})");
                OnRatingUpdate?.Invoke(p);
            });

            // ── Errors ────────────────────────────────────────────────────────
            _socket.OnUnityThread("error_message", resp =>
            {
                var p = FromResp<ErrorPayload>(resp);
                Debug.LogWarning($"[NM] error_message: {p?.message}");
                OnErrorMsg?.Invoke(p);
            });

            _socket.OnUnityThread("ml_chat_message", resp =>
            {
                OnAllChatMessage?.Invoke(FromResp<MLAllChatMessagePayload>(resp));
            });

            _socket.Connect();
        }

        void EmitReconnectIfPendingNative()
        {
            string token = PlayerPrefs.GetString("reconnect_token", null);
            if (!string.IsNullOrEmpty(token))
            {
                Debug.Log("[NM] Emitting reconnect with stored token");
                StartCoroutine(EmitReconnectDelayedNative(token));
            }
        }

        System.Collections.IEnumerator EmitReconnectDelayedNative(string token)
        {
            yield return null;
            Emit("reconnect", new { token });
        }

        // SocketIOUnity's GetValue<T> uses System.Text.Json which ignores public fields.
        // All GameState classes use fields, not properties, so we route through Newtonsoft.
        static T FromResp<T>(SocketIOClient.SocketIOResponse resp) =>
            FromJson<T>(resp.GetValue<System.Text.Json.JsonElement>().GetRawText());

#endif // !UNITY_WEBGL || UNITY_EDITOR

        static T FromJson<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, _respSettings);

        MLMatchConfig CacheMLMatchConfig(MLMatchConfig incoming, string source)
        {
            if (incoming == null)
            {
                Debug.LogError($"[NM] Received null ml_match_config payload via {source}.");
                return null;
            }

            if (LastMLMatchConfig == null)
                LastMLMatchConfig = incoming;
            else
                MergeMLMatchConfig(LastMLMatchConfig, incoming);

            if (LastMLMatchConfig.loadout != null && LastMLMatchConfig.loadout.Length > 0)
            {
                LastMatchLoadout = LastMLMatchConfig.loadout;
                PendingLoadoutPhase = null;
                ClearPendingLoadoutReady(source);
            }

            Debug.Log($"[NM] {source} {DescribeMLMatchConfig(LastMLMatchConfig)}");
            return LastMLMatchConfig;
        }

        static void MergeMLMatchConfig(MLMatchConfig target, MLMatchConfig incoming)
        {
            if (target == null || incoming == null)
                return;

            if (incoming.tickHz > 0) target.tickHz = incoming.tickHz;
            if (incoming.incomeIntervalTicks > 0) target.incomeIntervalTicks = incoming.incomeIntervalTicks;
            if (incoming.startGold > 0f) target.startGold = incoming.startGold;
            if (incoming.startIncome > 0f) target.startIncome = incoming.startIncome;
            if (incoming.livesStart > 0) target.livesStart = incoming.livesStart;
            if (incoming.teamHpStart > 0) target.teamHpStart = incoming.teamHpStart;
            if (incoming.buildPhaseTicks > 0) target.buildPhaseTicks = incoming.buildPhaseTicks;
            if (incoming.transitionPhaseTicks > 0) target.transitionPhaseTicks = incoming.transitionPhaseTicks;
            if (incoming.gridW > 0) target.gridW = incoming.gridW;
            if (incoming.gridH > 0) target.gridH = incoming.gridH;
            if (!string.IsNullOrWhiteSpace(incoming.raceId)) target.raceId = incoming.raceId;
            if (incoming.loadout != null && incoming.loadout.Length > 0) target.loadout = incoming.loadout;
            if (!string.IsNullOrWhiteSpace(incoming.reconnectToken)) target.reconnectToken = incoming.reconnectToken;
            if (incoming.ranked) target.ranked = true;
            if (incoming.battlefieldTopology != null) target.battlefieldTopology = incoming.battlefieldTopology;
            if (incoming.battlefieldLayout != null) target.battlefieldLayout = incoming.battlefieldLayout;
            if (incoming.slotDefinitions != null && incoming.slotDefinitions.Length > 0) target.slotDefinitions = incoming.slotDefinitions;
            if (incoming.fortressBuildingConfigs != null && incoming.fortressBuildingConfigs.Length > 0) target.fortressBuildingConfigs = incoming.fortressBuildingConfigs;
            if (incoming.fortressPadConfigs != null && incoming.fortressPadConfigs.Length > 0) target.fortressPadConfigs = incoming.fortressPadConfigs;
            if (incoming.barracksSiteConfigs != null && incoming.barracksSiteConfigs.Length > 0) target.barracksSiteConfigs = incoming.barracksSiteConfigs;
            if (incoming.barracksRosterConfigs != null && incoming.barracksRosterConfigs.Length > 0) target.barracksRosterConfigs = incoming.barracksRosterConfigs;
            if (incoming.heroRosterConfigs != null && incoming.heroRosterConfigs.Length > 0) target.heroRosterConfigs = incoming.heroRosterConfigs;
            if (incoming.marketRosterConfigs != null && incoming.marketRosterConfigs.Length > 0) target.marketRosterConfigs = incoming.marketRosterConfigs;
            if (incoming.barracksRosterRefundPct > 0) target.barracksRosterRefundPct = incoming.barracksRosterRefundPct;
            if (incoming.barracksSendTimerTicks > 0) target.barracksSendTimerTicks = incoming.barracksSendTimerTicks;
            if (incoming.waveTimerTicks > 0) target.waveTimerTicks = incoming.waveTimerTicks;
            if (incoming.movementTuning != null) target.movementTuning = incoming.movementTuning;
        }

        static string DescribeMLMatchConfig(MLMatchConfig config)
        {
            if (config == null)
                return "ml_match_config=<null>";

            int loadoutCount = config.loadout != null ? config.loadout.Length : 0;
            int slotCount = config.slotDefinitions != null ? config.slotDefinitions.Length : 0;
            var layout = config.battlefieldLayout;
            return
                $"ml_match_config loadout={loadoutCount} raceId={config.raceId ?? "<null>"} " +
                $"slots={slotCount} layoutId={layout?.layoutId ?? "<none>"} layoutHash={layout?.contentHash ?? "<none>"} " +
                $"layoutLanes={layout?.lanes?.Length ?? 0} routeNodes={layout?.routeNodes?.Length ?? 0} routeSegments={layout?.routeSegments?.Length ?? 0}";
        }

        void HandleIncomingMLGameOver(MLGameOverPayload payload)
        {
            if (payload == null)
            {
                Debug.LogWarning("[PostGameFlow] Ignoring null ml_game_over payload.");
                return;
            }

            if (_finalGameOverHandledForCurrentMatch)
            {
                LastMLGameOver = payload;
                Debug.LogWarning(
                    $"[PostGameFlow] Duplicate ml_game_over ignored " +
                    $"(winnerLane={payload.winnerLaneIndex}, round={payload.finalRound}, state={payload.matchState ?? "final_game_over"}).");
                return;
            }

            _finalGameOverHandledForCurrentMatch = true;
            CurrentMLMatchState = !string.IsNullOrEmpty(payload.matchState) ? payload.matchState : "final_game_over";
            LastMLPvPResolved = null;
            LastMLGameOver = payload;
            LastRematchStatus = null;
            PendingLoadoutPhase = null;
            LastPreparationState = null;
            LastMLWaveReadyState = null;
            Debug.Log(
                $"[PostGameFlow] Match ended. winnerLane={payload.winnerLaneIndex} " +
                $"round={payload.finalRound} state={CurrentMLMatchState} survival={payload.continuedIntoSurvival}.");
            OnMLGameOver?.Invoke(payload);

            string activeScene = SceneManager.GetActiveScene().name;
            if (activeScene != "PostGame")
            {
                Debug.Log($"[PostGameFlow] Opening post-game scene from '{activeScene}'.");
                LoadingScreen.LoadScene("PostGame");
            }
            else
            {
                Debug.Log("[PostGameFlow] Post-game scene already active; keeping modal open.");
            }
        }

        public void ClearPostGameData()
        {
            CurrentMLMatchState = "active_survival";
            LastMLPvPResolved = null;
            LastMLGameOver = null;
            LastClassicGameOver = null;
            LastRematchStatus = null;
            LastMLWaveReadyState = null;
            _pendingLoadoutReadySignal = false;
            _pendingGameplayReadySignal = false;
        }

        void ResetConnectionSessionState(string reason)
        {
            Debug.Log($"[NM] Clearing cached session state ({reason}).");
            IsConnected = false;
            MySocketId = null;
            MyLaneIndex = 0;
            MySide = null;
            MyRoomCode = null;
            CurrentLobby = null;
            LastMatchLoadout = null;
            LastMLMatchConfig = null;
            PendingLoadoutPhase = null;
            LastPreparationState = null;
            LastMLMatchReady = null;
            LastMLWaveStart = null;
            _loggedFirstMLSnapshotForCurrentMatch = false;
            ClearPostGameData();
            ResetPostGameFlowState(reason);
        }

        void ResetPostGameFlowState(string reason)
        {
            if (_finalGameOverHandledForCurrentMatch || LastMLGameOver != null || LastRematchStatus != null)
                Debug.Log($"[PostGameFlow] Resetting post-game flow ({reason}).");

            _finalGameOverHandledForCurrentMatch = false;
        }

        void TryEmitPendingCriticalReadySignals(string reason)
        {
            if (!IsConnected)
                return;

            if (_pendingLoadoutReadySignal)
            {
                Debug.Log($"[NM] Emitting pending ml_loadout_ready ({reason})");
                Emit("ml_loadout_ready");
            }

            if (_pendingGameplayReadySignal)
            {
                Debug.Log($"[NM] Emitting pending ml_gameplay_ready ({reason})");
                Emit("ml_gameplay_ready");
            }
        }

        void ClearPendingLoadoutReady(string reason)
        {
            if (!_pendingLoadoutReadySignal)
                return;

            _pendingLoadoutReadySignal = false;
            Debug.Log($"[NM] Cleared pending ml_loadout_ready ({reason})");
        }

        void ClearPendingGameplayReady(string reason)
        {
            if (!_pendingGameplayReadySignal)
                return;

            _pendingGameplayReadySignal = false;
            Debug.Log($"[NM] Cleared pending ml_gameplay_ready ({reason})");
        }

        public void RequestLoadoutReady()
        {
            _pendingLoadoutReadySignal = true;
            if (!IsConnected)
                Debug.Log("[NM] Queueing ml_loadout_ready until the socket reconnects.");
            TryEmitPendingCriticalReadySignals("request_loadout_ready");
        }

        public void RequestGameplayReady()
        {
            _pendingGameplayReadySignal = true;
            if (!IsConnected)
                Debug.Log("[NM] Queueing ml_gameplay_ready until the socket reconnects.");
            TryEmitPendingCriticalReadySignals("request_gameplay_ready");
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>Send the player's confirmed race selection to the server.</summary>
        public void EmitLoadoutConfirm(string raceId)
        {
            Emit("ml_loadout_confirm", new { raceId });
        }

        /// <summary>Legacy path: send explicit loadout unit type IDs to the server.</summary>
        public void EmitLoadoutConfirm(int[] unitTypeIds)
        {
            Emit("ml_loadout_confirm", new { unitTypeIds });
        }

        public void EmitAllChatMessage(string message)
        {
            string trimmed = message?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Debug.LogWarning("[NM] Refusing to emit ml_chat_message because the message was empty.");
                return;
            }

            Emit("ml_chat_message", new { message = trimmed });
        }

        public void RequestFriendsList()
        {
            Emit("friend:list");
        }

        public void SendFriendRequest(string displayName)
        {
            string trimmed = displayName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Debug.LogWarning("[NM] Refusing to emit friend:add because the target display name was empty.");
                return;
            }

            Emit("friend:add", new { displayName = trimmed });
        }

        public void AcceptFriendRequest(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[NM] Refusing to emit friend:accept because playerId was empty.");
                return;
            }

            Emit("friend:accept", new { playerId });
        }

        public void DeclineFriendRequest(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[NM] Refusing to emit friend:decline because playerId was empty.");
                return;
            }

            Emit("friend:decline", new { playerId });
        }

        public void RemoveFriend(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[NM] Refusing to emit friend:remove because playerId was empty.");
                return;
            }

            Emit("friend:remove", new { playerId });
        }

        public void InviteFriendToLobby(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[NM] Refusing to emit lobby:invite because playerId was empty.");
                return;
            }

            Emit("lobby:invite", new { targetPlayerId = playerId });
        }

        /// <summary>Emit a socket event. Pass null to emit with no payload.</summary>
        public void Emit(string eventName, object data = null)
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"[NM] Emit '{eventName}' skipped — not connected");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Newtonsoft.Json handles anonymous types and Dictionary<> that JsonUtility cannot.
            string json = data != null ? JsonConvert.SerializeObject(data) : "";
            JSIO_Emit(eventName, json);
#else
            if (data == null)
            {
                _socket.Emit(eventName);
            }
            else
            {
                // Keep native/editor payloads aligned with the WebGL path so gameplay
                // actions serialize as plain JSON objects instead of library-specific wrappers.
                string json = JsonConvert.SerializeObject(data);
                _socket.EmitStringAsJSON(eventName, json);
            }
#endif
        }

        void DisconnectActiveSocket()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JSIO_Disconnect();
#else
            _socket?.Disconnect();
            _socket = null;
#endif
        }

        void OnDestroy()
        {
            DisconnectActiveSocket();
        }
    }
}
