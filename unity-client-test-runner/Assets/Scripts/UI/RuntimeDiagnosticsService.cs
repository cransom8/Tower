using System;
using System.Collections.Generic;
using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.UI
{
    public enum RuntimeLogSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class RuntimeLogEntry
    {
        public long Sequence;
        public DateTime TimestampUtc;
        public RuntimeLogSeverity Severity;
        public string Source;
        public string Message;
        public string Details;
        public string SceneName;
    }

    public sealed class RuntimeChatEntry
    {
        public long Sequence;
        public DateTime TimestampUtc;
        public string Sender;
        public string Message;
        public int LaneIndex;
        public string TeamKey;
        public bool IsSystem;
    }

    [DefaultExecutionOrder(-900)]
    public sealed class RuntimeDiagnosticsService : MonoBehaviour
    {
        const int MaxSystemEntries = 300;
        const int MaxChatEntries = 200;

        static RuntimeDiagnosticsService _instance;
        static bool _captureInfoLogsInitialized;

        readonly List<RuntimeLogEntry> _systemEntries = new();
        readonly List<RuntimeChatEntry> _chatEntries = new();

        NetworkManager _networkManager;
        long _nextSequence = 1;

        public static event Action Changed;

        public static RuntimeDiagnosticsService Instance => EnsureInstance();

        public static IReadOnlyList<RuntimeLogEntry> SystemEntries => Instance._systemEntries;
        public static IReadOnlyList<RuntimeChatEntry> ChatEntries => Instance._chatEntries;
        public static bool CaptureInfoLogsInRuntimeBuilds { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            EnsureInstance();
        }

        public static RuntimeDiagnosticsService EnsureInstance()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject(nameof(RuntimeDiagnosticsService));
            return go.AddComponent<RuntimeDiagnosticsService>();
        }

        public static void PublishSystem(RuntimeLogSeverity severity, string source, string message, string details = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var service = EnsureInstance();
            service.AppendSystemEntry(new RuntimeLogEntry
            {
                Sequence = service._nextSequence++,
                TimestampUtc = DateTime.UtcNow,
                Severity = severity,
                Source = string.IsNullOrWhiteSpace(source) ? "Runtime" : source.Trim(),
                Message = message.Trim(),
                Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            });
        }

        public static void PublishChat(RuntimeChatEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Message))
                return;

            var service = EnsureInstance();
            entry.Sequence = service._nextSequence++;
            if (entry.TimestampUtc == default)
                entry.TimestampUtc = DateTime.UtcNow;
            service.AppendChatEntry(entry);
        }

        public static bool TrySendChat(string rawMessage, out string failureReason)
        {
            failureReason = null;
            string message = rawMessage?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                failureReason = "Cannot send an empty chat message.";
                PublishSystem(RuntimeLogSeverity.Warning, "RuntimeChat", failureReason);
                return false;
            }

            if (NetworkManager.Instance == null)
            {
                failureReason = "NetworkManager is missing. Chat send failed.";
                PublishSystem(RuntimeLogSeverity.Error, "RuntimeChat", failureReason);
                return false;
            }

            if (!NetworkManager.Instance.IsConnected)
            {
                failureReason = "Socket is not connected. Chat send failed.";
                PublishSystem(RuntimeLogSeverity.Error, "RuntimeChat", failureReason);
                return false;
            }

            NetworkManager.Instance.EmitAllChatMessage(message);
            return true;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            if (!_captureInfoLogsInitialized)
            {
                CaptureInfoLogsInRuntimeBuilds = Debug.isDebugBuild;
                _captureInfoLogsInitialized = true;
            }
        }

        void OnEnable()
        {
            Application.logMessageReceived += HandleUnityLog;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= HandleUnityLog;
            UnbindNetworkManager();
        }

        void Update()
        {
            BindNetworkManagerIfNeeded();
        }

        void BindNetworkManagerIfNeeded()
        {
            if (_networkManager == NetworkManager.Instance)
                return;

            UnbindNetworkManager();
            _networkManager = NetworkManager.Instance;
            if (_networkManager == null)
                return;

            _networkManager.OnAllChatMessage += HandleAllChatMessage;
        }

        void UnbindNetworkManager()
        {
            if (_networkManager == null)
                return;

            _networkManager.OnAllChatMessage -= HandleAllChatMessage;
            _networkManager = null;
        }

        void HandleAllChatMessage(MLAllChatMessagePayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.message))
                return;

            DateTime timestampUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(payload.timestampUtc)
                && DateTime.TryParse(
                    payload.timestampUtc,
                    null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                timestampUtc = parsed;
            }

            AppendChatEntry(new RuntimeChatEntry
            {
                Sequence = _nextSequence++,
                TimestampUtc = timestampUtc,
                Sender = string.IsNullOrWhiteSpace(payload.displayName) ? "Player" : payload.displayName.Trim(),
                Message = payload.message.Trim(),
                LaneIndex = payload.laneIndex,
                TeamKey = payload.team,
                IsSystem = false,
            });
        }

        void HandleUnityLog(string condition, string stackTrace, LogType type)
        {
            RuntimeLogSeverity severity;
            switch (type)
            {
                case LogType.Warning:
                    severity = RuntimeLogSeverity.Warning;
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    severity = RuntimeLogSeverity.Error;
                    break;
                default:
                    if (!Application.isEditor && !CaptureInfoLogsInRuntimeBuilds)
                        return;

                    severity = RuntimeLogSeverity.Info;
                    break;
            }

            AppendSystemEntry(new RuntimeLogEntry
            {
                Sequence = _nextSequence++,
                TimestampUtc = DateTime.UtcNow,
                Severity = severity,
                Source = ExtractSource(condition),
                Message = condition ?? string.Empty,
                Details = type == LogType.Exception ? stackTrace : string.IsNullOrWhiteSpace(stackTrace) ? null : stackTrace,
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            });
        }

        void AppendSystemEntry(RuntimeLogEntry entry)
        {
            _systemEntries.Add(entry);
            if (_systemEntries.Count > MaxSystemEntries)
                _systemEntries.RemoveAt(0);

            Changed?.Invoke();
        }

        void AppendChatEntry(RuntimeChatEntry entry)
        {
            _chatEntries.Add(entry);
            if (_chatEntries.Count > MaxChatEntries)
                _chatEntries.RemoveAt(0);

            Changed?.Invoke();
        }

        static string ExtractSource(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Unity";

            string trimmed = message.Trim();
            if (trimmed.Length > 2 && trimmed[0] == '[')
            {
                int close = trimmed.IndexOf(']');
                if (close > 1)
                    return trimmed.Substring(1, close - 1);
            }

            return "Unity";
        }
    }
}
