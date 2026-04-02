using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CastleDefender.Net;
using UnityEngine.SceneManagement;

namespace CastleDefender.UI
{
    [DefaultExecutionOrder(-850)]
    public sealed class RuntimeChatDebugPanelController : MonoBehaviour
    {
        enum PanelTab
        {
            Chat,
            System,
        }

        static RuntimeChatDebugPanelController _instance;

        RectTransform _widgetRect;
        RectTransform _bodyRect;
        RectTransform _bubbleRect;
        Image _bubbleImage;
        Button _bubbleButton;
        Button _collapseButton;
        Button _chatTabButton;
        Button _systemTabButton;
        Button _errorFilterButton;
        Button _warningFilterButton;
        Button _infoFilterButton;
        Button _copyButton;
        Button _sendButton;
        TMP_Text _bubbleLabel;
        TMP_Text _badgeLabel;
        TMP_Text _titleLabel;
        TMP_Text _logLabel;
        TMP_Text _statusLabel;
        TMP_InputField _chatInput;
        ScrollRect _scrollRect;
        GameObject _chatInputRow;
        GameObject _systemFilterRow;
        Coroutine _eventSystemValidationRoutine;

        PanelTab _currentTab = PanelTab.Chat;
        bool _isExpanded;
        bool _showErrors = true;
        bool _showWarnings = true;
        bool _showInfo = false;
        bool _layoutDirty = true;
        bool _textDirty = true;
        bool _lastMobileState;
        bool _eventSystemIssueLogged;
        long _lastSeenSystemSequence;
        long _lastSeenChatSequence;
        string _currentRenderedText = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            EnsureInstance();
        }

        public static RuntimeChatDebugPanelController EnsureInstance()
        {
            if (_instance != null)
                return _instance;

            RuntimeDiagnosticsService.EnsureInstance();
            var go = new GameObject(
                nameof(RuntimeChatDebugPanelController),
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            return go.AddComponent<RuntimeChatDebugPanelController>();
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
            BuildUi();
            if (!enabled)
                return;
            SetExpanded(false);
            RefreshAll(true);
        }

        void OnEnable()
        {
            RuntimeDiagnosticsService.Changed += HandleDiagnosticsChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ScheduleEventSystemValidation();
        }

        void OnDisable()
        {
            RuntimeDiagnosticsService.Changed -= HandleDiagnosticsChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (_eventSystemValidationRoutine != null)
            {
                StopCoroutine(_eventSystemValidationRoutine);
                _eventSystemValidationRoutine = null;
            }
        }

        void Update()
        {
            bool mobile = IsMobileLayout();
            if (_layoutDirty || mobile != _lastMobileState)
            {
                _lastMobileState = mobile;
                RefreshLayout();
            }

            if (_textDirty)
                RefreshRenderedText(true);

            if (_isExpanded && _chatInput != null && _chatInput.isFocused && Input.GetKeyDown(KeyCode.Return))
                SubmitChat();
        }

        void HandleDiagnosticsChanged()
        {
            _textDirty = true;
            RefreshBadge();
        }

        void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            _eventSystemIssueLogged = false;
            ScheduleEventSystemValidation();
        }

        void BuildUi()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[RuntimeChatDebugPanel] Panel root is missing Canvas. Chat/debug panel bootstrap is aborted.", this);
                enabled = false;
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4200;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                Debug.LogError("[RuntimeChatDebugPanel] Panel root is missing CanvasScaler. Chat/debug panel bootstrap is aborted.", this);
                enabled = false;
                return;
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.65f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                Debug.LogError("[RuntimeChatDebugPanel] Panel root is missing GraphicRaycaster. Chat/debug panel bootstrap is aborted.", this);
                enabled = false;
                return;
            }

            _widgetRect = CreateRect("ChatDebugWidget", transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            _widgetRect.sizeDelta = new Vector2(420f, 360f);
            _widgetRect.anchoredPosition = new Vector2(-28f, 28f);

            _bubbleRect = CreatePanel("CollapsedBubble", _widgetRect, new Color(0.05f, 0.08f, 0.12f, 0.96f));
            Stretch(_bubbleRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _bubbleImage = _bubbleRect.GetComponent<Image>();
            _bubbleButton = _bubbleRect.gameObject.AddComponent<Button>();
            _bubbleButton.targetGraphic = _bubbleImage;
            _bubbleButton.onClick.AddListener(() => SetExpanded(true));
            _bubbleRect.gameObject.AddComponent<DraggablePanel>().Configure(_widgetRect, "runtime.chatdebug");

            _bubbleLabel = CreateText("BubbleLabel", _bubbleRect, "CHAT", 18f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(_bubbleLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _badgeLabel = CreateText("BubbleBadge", _bubbleRect, "", 15f, FontStyles.Bold, TextAlignmentOptions.Center);
            _badgeLabel.color = new Color(1f, 0.55f, 0.36f, 1f);
            Stretch(_badgeLabel.rectTransform, new Vector2(0.56f, 0.55f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);

            _bodyRect = CreatePanel("ExpandedPanel", _widgetRect, new Color(0.03f, 0.05f, 0.09f, 0.96f));
            Stretch(_bodyRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var header = CreatePanel("Header", _bodyRect, new Color(0.10f, 0.16f, 0.24f, 0.98f));
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 44f), Vector2.zero);
            header.gameObject.AddComponent<DraggablePanel>().Configure(_widgetRect, "runtime.chatdebug");

            _titleLabel = CreateText("Title", header, "CHAT / SYSTEM", 18f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(_titleLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0.72f, 1f), new Vector2(16f, 0f), new Vector2(-8f, 0f));

            _collapseButton = CreateButton("CollapseButton", header, "-", new Color(0.18f, 0.26f, 0.36f, 1f));
            _collapseButton.onClick.AddListener(() => SetExpanded(false));
            Anchor(_collapseButton.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(34f, 26f), new Vector2(-12f, 0f));

            var tabRow = CreateHorizontalRow("Tabs", _bodyRect, 40f, 8f);
            Stretch(tabRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -92f), new Vector2(-12f, -52f));
            _chatTabButton = CreateButton("ChatTab", tabRow, "All Chat", new Color(0.12f, 0.23f, 0.34f, 1f));
            _systemTabButton = CreateButton("SystemTab", tabRow, "System / Errors", new Color(0.12f, 0.23f, 0.34f, 1f));
            _chatTabButton.onClick.AddListener(() => SetTab(PanelTab.Chat));
            _systemTabButton.onClick.AddListener(() => SetTab(PanelTab.System));

            _systemFilterRow = CreateHorizontalRow("SystemFilters", _bodyRect, 32f, 8f).gameObject;
            Stretch(_systemFilterRow.transform as RectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -132f), new Vector2(-12f, -100f));
            _errorFilterButton = CreateButton("Errors", _systemFilterRow.transform, "Error", new Color(0.45f, 0.17f, 0.16f, 1f));
            _warningFilterButton = CreateButton("Warnings", _systemFilterRow.transform, "Warn", new Color(0.46f, 0.30f, 0.12f, 1f));
            _infoFilterButton = CreateButton("Info", _systemFilterRow.transform, "Info", new Color(0.16f, 0.28f, 0.42f, 1f));
            _copyButton = CreateButton("Copy", _systemFilterRow.transform, "Copy", new Color(0.18f, 0.26f, 0.36f, 1f));
            _errorFilterButton.onClick.AddListener(() => { _showErrors = !_showErrors; _textDirty = true; RefreshFilterVisuals(); });
            _warningFilterButton.onClick.AddListener(() => { _showWarnings = !_showWarnings; _textDirty = true; RefreshFilterVisuals(); });
            _infoFilterButton.onClick.AddListener(() => { _showInfo = !_showInfo; _textDirty = true; RefreshFilterVisuals(); });
            _copyButton.onClick.AddListener(CopyCurrentText);

            var scrollRoot = CreatePanel("ScrollRoot", _bodyRect, new Color(0.06f, 0.08f, 0.12f, 0.98f));
            Stretch(scrollRoot, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 58f), new Vector2(-12f, -140f));
            _scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 20f;

            var viewport = CreatePanel("Viewport", scrollRoot, new Color(1f, 1f, 1f, 0.02f));
            Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 600f);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logLabel = CreateText("LogText", content, "", 16f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _logLabel.richText = true;
            _logLabel.textWrappingMode = TextWrappingModes.Normal;
            _logLabel.overflowMode = TextOverflowModes.Overflow;
            Stretch(_logLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            _scrollRect.viewport = viewport;
            _scrollRect.content = content;

            _statusLabel = CreateText("Status", _bodyRect, "", 14f, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            _statusLabel.color = new Color(0.82f, 0.85f, 0.90f, 0.92f);
            Stretch(_statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 36f), new Vector2(-12f, 58f));

            _chatInputRow = CreateHorizontalRow("ChatInputRow", _bodyRect, 40f, 8f).gameObject;
            Stretch(_chatInputRow.transform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-12f, 52f));
            _chatInput = CreateInputField("ChatInput", _chatInputRow.transform, "Type a message...");
            _sendButton = CreateButton("SendButton", _chatInputRow.transform, "Send", new Color(0.16f, 0.38f, 0.28f, 1f));
            (_sendButton.transform as RectTransform).sizeDelta = new Vector2(88f, 0f);
            _sendButton.onClick.AddListener(SubmitChat);
        }

        void SetExpanded(bool expanded)
        {
            _isExpanded = expanded;
            if (_bubbleRect != null)
                _bubbleRect.gameObject.SetActive(!expanded);
            if (_bodyRect != null)
                _bodyRect.gameObject.SetActive(expanded);

            if (expanded)
            {
                if (_currentTab == PanelTab.Chat)
                    MarkChatSeen();
                else
                    MarkSystemSeen();
            }

            _layoutDirty = true;
            RefreshBadge();
        }

        void SetTab(PanelTab tab)
        {
            _currentTab = tab;
            if (_currentTab == PanelTab.Chat)
                MarkChatSeen();
            else
                MarkSystemSeen();

            _textDirty = true;
            RefreshTabVisuals();
        }

        void RefreshAll(bool forceScrollToBottom)
        {
            RefreshLayout();
            RefreshFilterVisuals();
            RefreshTabVisuals();
            RefreshRenderedText(forceScrollToBottom);
            RefreshBadge();
        }

        void RefreshLayout()
        {
            _layoutDirty = false;
            bool mobile = IsMobileLayout();

            if (_widgetRect != null)
            {
                _widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, mobile ? 350f : 420f);
                _widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, mobile ? 320f : 360f);
            }

            if (_bubbleRect != null)
                _bubbleRect.sizeDelta = mobile ? new Vector2(64f, 64f) : new Vector2(72f, 72f);

            if (_chatInputRow != null)
                _chatInputRow.SetActive(_currentTab == PanelTab.Chat);
            if (_systemFilterRow != null)
                _systemFilterRow.SetActive(_currentTab == PanelTab.System);
        }

        void RefreshTabVisuals()
        {
            StyleButton(_chatTabButton, _currentTab == PanelTab.Chat ? new Color(0.23f, 0.43f, 0.64f, 1f) : new Color(0.12f, 0.23f, 0.34f, 1f));
            StyleButton(_systemTabButton, _currentTab == PanelTab.System ? new Color(0.23f, 0.43f, 0.64f, 1f) : new Color(0.12f, 0.23f, 0.34f, 1f));
            if (_chatInputRow != null)
                _chatInputRow.SetActive(_currentTab == PanelTab.Chat);
            if (_systemFilterRow != null)
                _systemFilterRow.SetActive(_currentTab == PanelTab.System);
        }

        void RefreshFilterVisuals()
        {
            StyleButton(_errorFilterButton, _showErrors ? new Color(0.55f, 0.20f, 0.18f, 1f) : new Color(0.16f, 0.20f, 0.27f, 1f));
            StyleButton(_warningFilterButton, _showWarnings ? new Color(0.60f, 0.38f, 0.13f, 1f) : new Color(0.16f, 0.20f, 0.27f, 1f));
            StyleButton(_infoFilterButton, _showInfo ? new Color(0.20f, 0.36f, 0.56f, 1f) : new Color(0.16f, 0.20f, 0.27f, 1f));
        }

        void RefreshRenderedText(bool forceScrollToBottom)
        {
            _textDirty = false;

            if (_currentTab == PanelTab.Chat)
            {
                _currentRenderedText = BuildChatText();
                _titleLabel.text = "ALL CHAT";
                _statusLabel.text = NetworkManager.Instance != null && NetworkManager.Instance.IsConnected
                    ? "Messages go to the active multiplayer room."
                    : "Chat send is disabled until the socket is connected.";
            }
            else
            {
                _currentRenderedText = BuildSystemText();
                _titleLabel.text = "SYSTEM / ERRORS";
                _statusLabel.text = "Unity log stream plus loud runtime diagnostics. Errors and warnings always surface here.";
            }

            if (_logLabel != null)
                _logLabel.text = _currentRenderedText;

            if (forceScrollToBottom && _scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        void RefreshBadge()
        {
            if (_badgeLabel == null)
                return;

            int unreadChat = 0;
            int unreadSystem = 0;
            var chatEntries = RuntimeDiagnosticsService.ChatEntries;
            for (int i = 0; i < chatEntries.Count; i++)
            {
                if (!chatEntries[i].IsSystem && chatEntries[i].Sequence > _lastSeenChatSequence)
                    unreadChat++;
            }

            var systemEntries = RuntimeDiagnosticsService.SystemEntries;
            for (int i = 0; i < systemEntries.Count; i++)
            {
                if (systemEntries[i].Sequence > _lastSeenSystemSequence && systemEntries[i].Severity != RuntimeLogSeverity.Info)
                    unreadSystem++;
            }

            int unreadCount = unreadChat + unreadSystem;
            _badgeLabel.text = unreadCount > 0 ? unreadCount.ToString() : string.Empty;
            if (_bubbleImage != null)
                _bubbleImage.color = unreadSystem > 0 ? new Color(0.22f, 0.08f, 0.08f, 0.98f)
                    : unreadChat > 0 ? new Color(0.05f, 0.12f, 0.16f, 0.98f)
                    : new Color(0.05f, 0.08f, 0.12f, 0.96f);
        }

        void MarkChatSeen()
        {
            var chatEntries = RuntimeDiagnosticsService.ChatEntries;
            if (chatEntries.Count > 0)
                _lastSeenChatSequence = chatEntries[chatEntries.Count - 1].Sequence;
        }

        void MarkSystemSeen()
        {
            var systemEntries = RuntimeDiagnosticsService.SystemEntries;
            if (systemEntries.Count > 0)
                _lastSeenSystemSequence = systemEntries[systemEntries.Count - 1].Sequence;
        }

        void SubmitChat()
        {
            if (_chatInput == null)
                return;

            if (!RuntimeDiagnosticsService.TrySendChat(_chatInput.text, out _))
                return;

            _chatInput.text = string.Empty;
            _chatInput.ActivateInputField();
        }

        void CopyCurrentText()
        {
            GUIUtility.systemCopyBuffer = _currentRenderedText ?? string.Empty;
            RuntimeDiagnosticsService.PublishSystem(RuntimeLogSeverity.Info, "RuntimeChatDebugPanel", "Diagnostics copied to clipboard.");
        }

        string BuildChatText()
        {
            var entries = RuntimeDiagnosticsService.ChatEntries;
            if (entries.Count == 0)
                return "<color=#9FB0C4>No chat messages yet.</color>";

            var builder = new StringBuilder(entries.Count * 56);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                builder.Append("<color=#8AA1BD>[");
                builder.Append(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
                builder.Append("]</color> <color=#F4D58A>");
                builder.Append(Escape(entry.Sender));
                builder.Append("</color>: ");
                builder.Append(Escape(entry.Message));
                if (i < entries.Count - 1)
                    builder.Append('\n');
            }

            return builder.ToString();
        }

        string BuildSystemText()
        {
            var entries = RuntimeDiagnosticsService.SystemEntries;
            if (entries.Count == 0)
                return "<color=#9FB0C4>No runtime diagnostics yet.</color>";

            var builder = new StringBuilder(entries.Count * 88);
            bool wroteAny = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!ShouldRender(entry))
                    continue;

                wroteAny = true;
                builder.Append(GetSeverityColor(entry.Severity));
                builder.Append('[');
                builder.Append(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
                builder.Append("] ");
                builder.Append(entry.Severity.ToString().ToUpperInvariant());
                builder.Append("</color> <color=#F4D58A>");
                builder.Append(Escape(entry.Source));
                builder.Append("</color> ");
                builder.Append(Escape(entry.Message));
                if (!string.IsNullOrWhiteSpace(entry.SceneName))
                {
                    builder.Append(" <color=#6D819A>(");
                    builder.Append(Escape(entry.SceneName));
                    builder.Append(")</color>");
                }

                if (!string.IsNullOrWhiteSpace(entry.Details))
                {
                    builder.Append('\n');
                    builder.Append("<color=#8FA2B8>");
                    builder.Append(Escape(TrimDetail(entry.Details)));
                    builder.Append("</color>");
                }

                if (i < entries.Count - 1)
                    builder.Append("\n\n");
            }

            return wroteAny ? builder.ToString() : "<color=#9FB0C4>No diagnostics match the current filters.</color>";
        }

        bool ShouldRender(RuntimeLogEntry entry)
        {
            return entry.Severity switch
            {
                RuntimeLogSeverity.Error => _showErrors,
                RuntimeLogSeverity.Warning => _showWarnings,
                _ => _showInfo,
            };
        }

        static string GetSeverityColor(RuntimeLogSeverity severity)
        {
            return severity switch
            {
                RuntimeLogSeverity.Error => "<color=#FF6B5B>",
                RuntimeLogSeverity.Warning => "<color=#F3B14A>",
                _ => "<color=#78A9FF>",
            };
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("<", "&lt;").Replace(">", "&gt;");
        }

        static string TrimDetail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            return trimmed.Length <= 320 ? trimmed : trimmed.Substring(0, 320) + "...";
        }

        static bool IsMobileLayout()
        {
            float shortestSide = Mathf.Min(Screen.width, Screen.height);
            return Application.isMobilePlatform || shortestSide < 1000f;
        }

        void ScheduleEventSystemValidation(int waitFrames = 10)
        {
            if (!isActiveAndEnabled)
                return;

            if (_eventSystemValidationRoutine != null)
                StopCoroutine(_eventSystemValidationRoutine);

            _eventSystemValidationRoutine = StartCoroutine(ValidateEventSystemNextFrame(waitFrames));
        }

        IEnumerator ValidateEventSystemNextFrame(int waitFrames)
        {
            for (int i = 0; i < waitFrames; i++)
                yield return null;

            _eventSystemValidationRoutine = null;
            ValidateEventSystem();
        }

        void ValidateEventSystem()
        {
            var allEventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (TryRemoveStaleRuntimeDiagnosticsEventSystem(allEventSystems))
                allEventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            int totalCount = allEventSystems != null ? allEventSystems.Length : 0;
            if (totalCount == 0)
            {
                if (ShouldDeferEventSystemValidation())
                {
                    ScheduleEventSystemValidation();
                    return;
                }

                ReportEventSystemIssue(
                    "[RuntimeChatDebugPanel] No EventSystem exists in the loaded scenes. " +
                    "The chat/debug panel will render, but interaction is disabled until the scene provides one.");
                return;
            }

            EventSystem existing = null;
            int activeCount = 0;
            for (int i = 0; i < totalCount; i++)
            {
                var candidate = allEventSystems[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy)
                    continue;

                activeCount++;
                if (existing == null)
                    existing = candidate;
            }

            if (activeCount == 0)
            {
                if (ShouldDeferEventSystemValidation())
                {
                    ScheduleEventSystemValidation();
                    return;
                }

                ReportEventSystemIssue(
                    $"[RuntimeChatDebugPanel] Found {totalCount} EventSystem object(s), but none are active in loaded scenes. " +
                    "Panel interaction is disabled until the scene enables it.");
                return;
            }

            if (activeCount > 1)
            {
                if (ShouldDeferEventSystemValidation())
                {
                    ScheduleEventSystemValidation();
                    return;
                }

                ReportEventSystemIssue(
                    $"[RuntimeChatDebugPanel] Detected {activeCount} active EventSystem instances across loaded scenes. " +
                    "The panel will not create or replace EventSystems at runtime.");
                return;
            }

            if (existing.GetComponent<BaseInputModule>() == null)
            {
                ReportEventSystemIssue(
                    $"[RuntimeChatDebugPanel] EventSystem '{existing.name}' is missing an input module in scene '{gameObject.scene.name}'. " +
                    "Panel interaction is disabled until scene wiring is fixed.");
                return;
            }
        }

        void ReportEventSystemIssue(string message)
        {
            if (_eventSystemIssueLogged)
                return;

            _eventSystemIssueLogged = true;
            Debug.LogError(message, this);
            RuntimeDiagnosticsService.PublishSystem(RuntimeLogSeverity.Error, "RuntimeChatDebugPanel", message, gameObject.scene.name);
        }

        static bool ShouldDeferEventSystemValidation()
        {
            if (LoadingScreen.IsTransitionInProgress)
                return true;

            Scene activeScene = SceneManager.GetActiveScene();
            return activeScene.IsValid()
                && string.Equals(activeScene.name, "Bootstrap", System.StringComparison.OrdinalIgnoreCase);
        }

        bool TryRemoveStaleRuntimeDiagnosticsEventSystem(EventSystem[] eventSystems)
        {
            EventSystem runtimeFallback = null;
            EventSystem sceneEventSystem = null;
            for (int i = 0; i < eventSystems.Length; i++)
            {
                var eventSystem = eventSystems[i];
                if (eventSystem == null)
                    continue;

                if (string.Equals(eventSystem.name, "RuntimeDiagnosticsEventSystem", System.StringComparison.Ordinal))
                {
                    runtimeFallback = eventSystem;
                    continue;
                }

                sceneEventSystem = eventSystem;
            }

            if (runtimeFallback == null || sceneEventSystem == null)
                return false;

            Debug.LogWarning(
                $"[RuntimeChatDebugPanel] Removing stale runtime-created EventSystem '{runtimeFallback.name}' because scene EventSystem '{sceneEventSystem.name}' is now available.",
                this);
            Destroy(runtimeFallback.gameObject);
            return true;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            return rect;
        }

        static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<RectTransform>();
        }

        static TMP_Text CreateText(string name, Transform parent, string text, float fontSize, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;
            return label;
        }

        static Button CreateButton(string name, Transform parent, string text, Color background)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            var label = CreateText("Label", go.transform, text, 14f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            return button;
        }

        static RectTransform CreateHorizontalRow(string name, Transform parent, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.spacing = spacing;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, height);
            return rect;
        }

        static TMP_InputField CreateInputField(string name, Transform parent, string placeholderText)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.14f, 1f);
            root.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-10f, -6f));

            var text = CreateText("Text", viewport.transform, string.Empty, 15f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft) as TextMeshProUGUI;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var placeholder = CreateText("Placeholder", viewport.transform, placeholderText, 15f, FontStyles.Italic, TextAlignmentOptions.MidlineLeft) as TextMeshProUGUI;
            placeholder.color = new Color(0.62f, 0.68f, 0.76f, 0.72f);
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var input = root.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 240;
            input.richText = false;
            return input;
        }

        static void StyleButton(Button button, Color color)
        {
            if (button?.image != null)
                button.image.color = color;
        }

        static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rect == null)
                return;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 anchoredPosition)
        {
            if (rect == null)
                return;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }
    }
}
