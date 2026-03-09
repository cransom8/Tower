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
        [Tooltip("Railway URL or http://localhost:3000")]
        public string ServerUrl = "https://castle-defender-production.up.railway.app";

        // ── Session state ─────────────────────────────────────────────────────
        public string MySocketId   { get; private set; }
        public int    MyLaneIndex  { get; set; } = 0;
        public string MySide       { get; set; }
        public string MyRoomCode   { get; private set; }
        public bool   IsConnected  { get; private set; }

        // ── Cross-scene loadout cache ─────────────────────────────────────────
        // Populated as soon as the per-player ml_match_config arrives, which
        // typically happens before Game_ML finishes loading.  CmdBar and
        // TileMenuUI read this on Start() / EnsureInitialized() to avoid the
        // race condition where OnMLMatchConfig fires before any scene subscriber
        // has a chance to hook in.
        public LoadoutEntry[] LastMatchLoadout { get; private set; }

        // ── ML Lobby events ───────────────────────────────────────────────────
        public event Action<MLRoomCreatedPayload>     OnMLRoomCreated;
        public event Action<MLRoomJoinedPayload>      OnMLRoomJoined;
        public event Action<MLLobbyUpdate>            OnMLLobbyUpdate;
        public event Action<MLMatchReadyPayload>      OnMLMatchReady;
        public event Action<MLMatchConfig>            OnMLMatchConfig;

        // ── Classic Lobby events ──────────────────────────────────────────────
        public event Action<ClassicRoomCreatedPayload>  OnClassicRoomCreated;
        public event Action<ClassicRoomJoinedPayload>   OnClassicRoomJoined;
        public event Action<ClassicMatchReadyPayload>   OnClassicMatchReady;

        // ── Snapshot events ───────────────────────────────────────────────────
        public event Action<MLSnapshot>      OnMLStateSnapshot;
        public event Action<ClassicSnapshot> OnClassicStateSnapshot;

        // ── Game over ─────────────────────────────────────────────────────────
        public event Action<MLGameOverPayload>      OnMLGameOver;
        public event Action<ClassicGameOverPayload> OnClassicGameOver;

        // ── Gameplay events ───────────────────────────────────────────────────
        public event Action<RematchVotePayload>          OnRematchVote;
        public event Action<ActionAppliedPayload>        OnActionApplied;
        public event Action<MLPlayerEliminatedPayload>   OnMLPlayerEliminated;
        public event Action<MLSpectatorJoinPayload>      OnMLSpectatorJoin;
        public event Action<MLLaneReassignedPayload>     OnMLLaneReassigned;

        // ── Send queue ────────────────────────────────────────────────────────
        public event Action<QueueUpdatePayload> OnQueueUpdate;

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
        string ResolvedServerUrl
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

        void Start() => Connect();

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
            JSIO_On("room_created");
            JSIO_On("room_joined");
            JSIO_On("match_ready");
            JSIO_On("ml_state_snapshot");
            JSIO_On("state_snapshot");
            JSIO_On("ml_game_over");
            JSIO_On("game_over");
            JSIO_On("rematch_vote");
            JSIO_On("action_applied");
            JSIO_On("ml_player_eliminated");
            JSIO_On("ml_spectator_join");
            JSIO_On("ml_lane_reassigned");
            JSIO_On("queue_update");
            JSIO_On("error_message");
            // Queue & Lobby (Phase U5)
            JSIO_On("queue_status");
            JSIO_On("match_found");
            JSIO_On("lobby_created");
            JSIO_On("lobby_joined");
            JSIO_On("lobby_update");
            JSIO_On("lobby_left");
            JSIO_On("lobby_error");
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
                    Debug.Log($"[NM] ml_match_ready playerCount={p.playerCount}");
                    OnMLMatchReady?.Invoke(p);
                    break;
                }
                case "ml_match_config":
                {
                    var cfg = JsonUtility.FromJson<MLMatchConfig>(json);
                    if (cfg.loadout != null && cfg.loadout.Length > 0)
                        LastMatchLoadout = cfg.loadout;
                    OnMLMatchConfig?.Invoke(cfg);
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
                    OnMLStateSnapshot?.Invoke(JsonUtility.FromJson<MLSnapshot>(json));
                    break;
                case "state_snapshot":
                    OnClassicStateSnapshot?.Invoke(JsonUtility.FromJson<ClassicSnapshot>(json));
                    break;
                case "ml_game_over":
                {
                    var p = JsonUtility.FromJson<MLGameOverPayload>(json);
                    Debug.Log($"[NM] ml_game_over winner lane={p.winnerLaneIndex}");
                    OnMLGameOver?.Invoke(p);
                    break;
                }
                case "game_over":
                {
                    var p = JsonUtility.FromJson<ClassicGameOverPayload>(json);
                    Debug.Log($"[NM] game_over winner={p.winner}");
                    OnClassicGameOver?.Invoke(p);
                    break;
                }
                case "rematch_vote":
                    OnRematchVote?.Invoke(JsonUtility.FromJson<RematchVotePayload>(json));
                    break;
                case "action_applied":
                    OnActionApplied?.Invoke(JsonUtility.FromJson<ActionAppliedPayload>(json));
                    break;
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
                case "queue_update":
                    OnQueueUpdate?.Invoke(JsonUtility.FromJson<QueueUpdatePayload>(json));
                    break;
                case "error_message":
                {
                    var p = JsonUtility.FromJson<ErrorPayload>(json);
                    Debug.LogWarning($"[NM] error_message: {p.message}");
                    OnErrorMsg?.Invoke(p);
                    break;
                }
                // ── Queue & Lobby (Phase U5) ──────────────────────────────────
                case "queue_status":
                    OnQueueStatus?.Invoke(JsonUtility.FromJson<QueueStatusPayload>(json));
                    break;
                case "match_found":
                {
                    var p = JsonUtility.FromJson<MatchFoundPayload>(json);
                    MyLaneIndex = p.laneIndex;
                    MyRoomCode  = p.roomCode;
                    Debug.Log($"[NM] match_found: {p.roomCode} lane={p.laneIndex} gameType={p.gameType}");
                    OnMatchFound?.Invoke(p);
                    break;
                }
                case "lobby_created":
                {
                    var p = JsonUtility.FromJson<LobbyCreatedPayload>(json);
                    Debug.Log($"[NM] lobby_created: {p.code}");
                    OnLobbyCreated?.Invoke(p);
                    break;
                }
                case "lobby_joined":
                {
                    var p = JsonUtility.FromJson<LobbyJoinedPayload>(json);
                    Debug.Log($"[NM] lobby_joined: {p.code}");
                    OnLobbyJoined?.Invoke(p);
                    break;
                }
                case "lobby_update":
                    OnLobbyUpdate?.Invoke(JsonUtility.FromJson<LobbyUpdatePayload>(json));
                    break;
                case "lobby_left":
                    OnLobbyLeft?.Invoke(JsonUtility.FromJson<LobbyLeftPayload>(json));
                    break;
                case "lobby_error":
                {
                    var p = JsonUtility.FromJson<LobbyErrorPayload>(json);
                    Debug.LogWarning($"[NM] lobby_error: {p.message}");
                    OnLobbyError?.Invoke(p);
                    break;
                }
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
                Debug.Log($"[NM] ml_match_ready playerCount={p.playerCount}");
                OnMLMatchReady?.Invoke(p);
            });

            _socket.OnUnityThread("ml_match_config", resp =>
            {
                var cfg = FromResp<MLMatchConfig>(resp);
                if (cfg.loadout != null && cfg.loadout.Length > 0)
                    LastMatchLoadout = cfg.loadout;
                OnMLMatchConfig?.Invoke(cfg);
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
                OnMLStateSnapshot?.Invoke(FromResp<MLSnapshot>(resp));
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
                OnMLGameOver?.Invoke(p);
            });

            _socket.OnUnityThread("game_over", resp =>
            {
                var p = FromResp<ClassicGameOverPayload>(resp);
                Debug.Log($"[NM] game_over winner={p.winner}");
                OnClassicGameOver?.Invoke(p);
            });

            // ── Gameplay ──────────────────────────────────────────────────────
            _socket.OnUnityThread("rematch_vote", resp =>
            {
                OnRematchVote?.Invoke(FromResp<RematchVotePayload>(resp));
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

            _socket.OnUnityThread("queue_update", resp =>
            {
                OnQueueUpdate?.Invoke(FromResp<QueueUpdatePayload>(resp));
            });

            // ── Queue & Lobby (Phase U5) ───────────────────────────────────────
            _socket.OnUnityThread("queue_status", resp =>
            {
                OnQueueStatus?.Invoke(FromResp<QueueStatusPayload>(resp));
            });

            _socket.OnUnityThread("match_found", resp =>
            {
                var p = FromResp<MatchFoundPayload>(resp);
                MyLaneIndex = p.laneIndex;
                MyRoomCode  = p.roomCode;
                Debug.Log($"[NM] match_found: {p.roomCode} lane={p.laneIndex} gameType={p.gameType}");
                OnMatchFound?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_created", resp =>
            {
                var p = FromResp<LobbyCreatedPayload>(resp);
                Debug.Log($"[NM] lobby_created: {p.code}");
                OnLobbyCreated?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_joined", resp =>
            {
                var p = FromResp<LobbyJoinedPayload>(resp);
                Debug.Log($"[NM] lobby_joined: {p.code}");
                OnLobbyJoined?.Invoke(p);
            });

            _socket.OnUnityThread("lobby_update", resp =>
            {
                OnLobbyUpdate?.Invoke(FromResp<LobbyUpdatePayload>(resp));
            });

            _socket.OnUnityThread("lobby_left", resp =>
            {
                OnLobbyLeft?.Invoke(FromResp<LobbyLeftPayload>(resp));
            });

            _socket.OnUnityThread("lobby_error", resp =>
            {
                var p = FromResp<LobbyErrorPayload>(resp);
                Debug.LogWarning($"[NM] lobby_error: {p?.message}");
                OnLobbyError?.Invoke(p);
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
        static readonly JsonSerializerSettings _respSettings = new JsonSerializerSettings
        {
            // Server sends null for value-type fields (e.g. "winner":null when no winner yet).
            // Ignore nulls so int/float/bool fields keep their C# defaults instead of throwing.
            NullValueHandling = NullValueHandling.Ignore
        };

        static T FromResp<T>(SocketIOClient.SocketIOResponse resp) =>
            JsonConvert.DeserializeObject<T>(
                resp.GetValue<System.Text.Json.JsonElement>().GetRawText(),
                _respSettings);

#endif // !UNITY_WEBGL || UNITY_EDITOR

        // ─────────────────────────────────────────────────────────────────────
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
            if (data == null) _socket.Emit(eventName);
            else              _socket.Emit(eventName, data);
#endif
        }

        void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JSIO_Disconnect();
#else
            _socket?.Disconnect();
            _socket = null;
#endif
        }
    }
}
