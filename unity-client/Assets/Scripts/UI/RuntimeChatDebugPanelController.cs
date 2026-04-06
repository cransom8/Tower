using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CastleDefender.Game;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DefaultExecutionOrder(-850)]
    public sealed class RuntimeChatDebugPanelController : MonoBehaviour
    {
        enum PanelTab
        {
            Chat,
            Friends,
            System,
        }

        sealed class FriendViewState
        {
            public string PlayerId;
            public string DisplayName;
            public string Status;
            public bool Online;
        }

        sealed class PendingLobbyInviteState
        {
            public string LobbyId;
            public string Code;
            public string FromPlayerId;
            public string FromDisplayName;
        }

        static RuntimeChatDebugPanelController _instance;

        readonly List<FriendViewState> _friends = new();
        readonly List<PendingLobbyInviteState> _pendingLobbyInvites = new();

        NetworkManager _networkManager;
        RectTransform _widgetRect;
        RectTransform _bodyRect;
        RectTransform _bubbleRect;
        Image _bubbleImage;
        Button _bubbleButton;
        Button _collapseButton;
        Button _maximizeButton;
        Button _chatTabButton;
        Button _friendsTabButton;
        Button _systemTabButton;
        Button _errorFilterButton;
        Button _warningFilterButton;
        Button _infoFilterButton;
        Button _copyButton;
        Button _sendButton;
        Button _addFriendButton;
        Scrollbar _logScrollbar;
        Scrollbar _friendsScrollbar;
        TMP_Text _bubbleLabel;
        TMP_Text _badgeLabel;
        TMP_Text _titleLabel;
        TMP_Text _logLabel;
        TMP_Text _statusLabel;
        TMP_InputField _chatInput;
        TMP_InputField _friendInput;
        ScrollRect _scrollRect;
        ScrollRect _friendsScrollRect;
        RectTransform _friendsListRoot;
        GameObject _logScrollRoot;
        GameObject _friendsScrollRoot;
        GameObject _chatInputRow;
        GameObject _friendsActionRow;
        GameObject _systemFilterRow;
        Coroutine _eventSystemValidationRoutine;

        PanelTab _currentTab = PanelTab.Chat;
        bool _isExpanded;
        bool _isMaximized;
        bool _showErrors = true;
        bool _showWarnings = true;
        bool _showInfo;
        bool _layoutDirty = true;
        bool _textDirty = true;
        bool _friendsDirty = true;
        bool _lastMobileState;
        bool _eventSystemIssueLogged;
        long _lastSeenSystemSequence;
        long _lastSeenChatSequence;
        long _socialRevision;
        long _lastSeenSocialRevision;
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
            AuthManager.AuthStateChanged += HandleAuthStateChanged;
            BindNetworkManagerIfNeeded();
            ScheduleEventSystemValidation();
        }

        void OnDisable()
        {
            RuntimeDiagnosticsService.Changed -= HandleDiagnosticsChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            AuthManager.AuthStateChanged -= HandleAuthStateChanged;
            UnbindNetworkManager();
            if (_eventSystemValidationRoutine != null)
            {
                StopCoroutine(_eventSystemValidationRoutine);
                _eventSystemValidationRoutine = null;
            }
        }

        void Update()
        {
            BindNetworkManagerIfNeeded();

            bool mobile = IsMobileLayout();
            if (_layoutDirty || mobile != _lastMobileState)
            {
                _lastMobileState = mobile;
                RefreshLayout();
            }

            bool shouldRefreshText =
                _textDirty ||
                (_currentTab == PanelTab.Friends && _friendsDirty);
            if (shouldRefreshText)
                RefreshRenderedText(_currentTab != PanelTab.Friends);

            if (_isExpanded && Input.GetKeyDown(KeyCode.Return))
            {
                if (_currentTab == PanelTab.Chat && _chatInput != null && _chatInput.isFocused)
                    SubmitChat();
                else if (_currentTab == PanelTab.Friends && _friendInput != null && _friendInput.isFocused)
                    SubmitFriendAdd();
            }
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

        void HandleAuthStateChanged()
        {
            if (!AuthManager.IsAuthenticated)
            {
                _friends.Clear();
                _pendingLobbyInvites.Clear();
                if (_friendInput != null)
                    _friendInput.text = string.Empty;
                NotifySocialChanged();
                return;
            }

            RequestFriendsIfReady();
            NotifySocialChanged();
        }

        void BindNetworkManagerIfNeeded()
        {
            if (_networkManager == NetworkManager.Instance)
                return;

            UnbindNetworkManager();
            _networkManager = NetworkManager.Instance;
            if (_networkManager == null)
                return;

            _networkManager.OnConnected += HandleConnected;
            _networkManager.OnDisconnected += HandleDisconnected;
            _networkManager.OnFriendsList += HandleFriendsList;
            _networkManager.OnFriendOnline += HandleFriendOnline;
            _networkManager.OnFriendOffline += HandleFriendOffline;
            _networkManager.OnFriendRequest += HandleFriendRequest;
            _networkManager.OnFriendAccepted += HandleFriendAccepted;
            _networkManager.OnFriendRemoved += HandleFriendRemoved;
            _networkManager.OnFriendError += HandleFriendError;
            _networkManager.OnLobbyCreated += HandleLobbyCreated;
            _networkManager.OnLobbyJoined += HandleLobbyJoined;
            _networkManager.OnLobbyUpdate += HandleLobbyUpdate;
            _networkManager.OnLobbyLeft += HandleLobbyLeft;
            _networkManager.OnMatchFound += HandleMatchFound;
            _networkManager.OnLobbyInvite += HandleLobbyInvite;
            _networkManager.OnLobbyInviteSent += HandleLobbyInviteSent;

            RequestFriendsIfReady();
            NotifySocialChanged();
        }

        void UnbindNetworkManager()
        {
            if (_networkManager == null)
                return;

            _networkManager.OnConnected -= HandleConnected;
            _networkManager.OnDisconnected -= HandleDisconnected;
            _networkManager.OnFriendsList -= HandleFriendsList;
            _networkManager.OnFriendOnline -= HandleFriendOnline;
            _networkManager.OnFriendOffline -= HandleFriendOffline;
            _networkManager.OnFriendRequest -= HandleFriendRequest;
            _networkManager.OnFriendAccepted -= HandleFriendAccepted;
            _networkManager.OnFriendRemoved -= HandleFriendRemoved;
            _networkManager.OnFriendError -= HandleFriendError;
            _networkManager.OnLobbyCreated -= HandleLobbyCreated;
            _networkManager.OnLobbyJoined -= HandleLobbyJoined;
            _networkManager.OnLobbyUpdate -= HandleLobbyUpdate;
            _networkManager.OnLobbyLeft -= HandleLobbyLeft;
            _networkManager.OnMatchFound -= HandleMatchFound;
            _networkManager.OnLobbyInvite -= HandleLobbyInvite;
            _networkManager.OnLobbyInviteSent -= HandleLobbyInviteSent;
            _networkManager = null;
        }

        void HandleConnected()
        {
            RequestFriendsIfReady();
            NotifySocialChanged();
        }

        void HandleDisconnected()
        {
            for (int i = 0; i < _friends.Count; i++)
                _friends[i].Online = false;

            NotifySocialChanged();
        }

        void HandleFriendsList(FriendListEntryPayload[] payload)
        {
            _friends.Clear();
            if (payload != null)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    var entry = payload[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.playerId))
                        continue;

                    _friends.Add(new FriendViewState
                    {
                        PlayerId = entry.playerId,
                        DisplayName = NormalizeDisplayName(entry.displayName),
                        Status = NormalizeFriendStatus(entry.status),
                        Online = entry.online,
                    });
                }
            }

            SortFriends();
            NotifySocialChanged();
        }

        void HandleFriendOnline(FriendPresencePayload payload)
        {
            SetFriendOnlineState(payload, true);
            NotifySocialChanged();
        }

        void HandleFriendOffline(FriendPresencePayload payload)
        {
            SetFriendOnlineState(payload, false);
            RemoveLobbyInviteFromPlayer(payload?.playerId);
            NotifySocialChanged();
        }

        void HandleFriendRequest(FriendPresencePayload payload)
        {
            UpsertFriend(payload, "pending_received", true);
            RuntimeDiagnosticsService.PublishSystem(
                RuntimeLogSeverity.Info,
                "Friends",
                $"{NormalizeDisplayName(payload?.displayName)} sent you a friend request.");
            NotifySocialChanged();
        }

        void HandleFriendAccepted(FriendPresencePayload payload)
        {
            UpsertFriend(payload, "accepted", true);
            RuntimeDiagnosticsService.PublishSystem(
                RuntimeLogSeverity.Info,
                "Friends",
                $"{NormalizeDisplayName(payload?.displayName)} accepted your friend request.");
            NotifySocialChanged();
        }

        void HandleFriendRemoved(FriendPresencePayload payload)
        {
            RemoveFriendById(payload?.playerId);
            RemoveLobbyInviteFromPlayer(payload?.playerId);
            RuntimeDiagnosticsService.PublishSystem(
                RuntimeLogSeverity.Info,
                "Friends",
                $"{NormalizeDisplayName(payload?.displayName)} was removed from your friends list.");
            NotifySocialChanged();
        }

        void HandleFriendError(ErrorPayload payload)
        {
            if (!string.IsNullOrWhiteSpace(payload?.message))
            {
                RuntimeDiagnosticsService.PublishSystem(
                    RuntimeLogSeverity.Warning,
                    "Friends",
                    payload.message);
            }

            NotifySocialChanged();
        }

        void HandleLobbyCreated(LobbyCreatedPayload _)
        {
            _pendingLobbyInvites.Clear();
            NotifySocialChanged();
        }

        void HandleLobbyJoined(LobbyJoinedPayload _)
        {
            _pendingLobbyInvites.Clear();
            NotifySocialChanged();
        }

        void HandleLobbyUpdate(LobbyUpdatePayload _)
        {
            NotifySocialChanged();
        }

        void HandleLobbyLeft(LobbyLeftPayload _)
        {
            NotifySocialChanged();
        }

        void HandleMatchFound(MatchFoundPayload _)
        {
            _pendingLobbyInvites.Clear();
            NotifySocialChanged();
        }

        void HandleLobbyInvite(LobbyInvitePayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.code))
                return;

            UpsertLobbyInvite(payload);
            RuntimeDiagnosticsService.PublishSystem(
                RuntimeLogSeverity.Info,
                "Friends",
                $"{NormalizeDisplayName(payload.fromDisplayName)} invited you to lobby {payload.code}.");
            NotifySocialChanged();
        }

        void HandleLobbyInviteSent(LobbyInviteSentPayload payload)
        {
            if (!string.IsNullOrWhiteSpace(payload?.displayName))
            {
                RuntimeDiagnosticsService.PublishSystem(
                    RuntimeLogSeverity.Info,
                    "Friends",
                    $"Lobby invite sent to {payload.displayName}.");
            }

            NotifySocialChanged();
        }

        void RequestFriendsIfReady()
        {
            if (!AuthManager.IsAuthenticated || _networkManager == null || !_networkManager.IsConnected)
                return;

            _networkManager.RequestFriendsList();
        }

        void NotifySocialChanged()
        {
            _socialRevision++;
            _friendsDirty = true;
            if (_currentTab == PanelTab.Friends)
                _textDirty = true;
            _layoutDirty = true;
            RefreshBadge();
            RefreshWindowVisuals();
        }

        void SetFriendOnlineState(FriendPresencePayload payload, bool online)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.playerId))
                return;

            FriendViewState friend = FindFriend(payload.playerId);
            if (friend == null)
            {
                friend = new FriendViewState
                {
                    PlayerId = payload.playerId,
                    DisplayName = NormalizeDisplayName(payload.displayName),
                    Status = "accepted",
                    Online = online,
                };
                _friends.Add(friend);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(payload.displayName))
                    friend.DisplayName = NormalizeDisplayName(payload.displayName);
                friend.Online = online;
            }

            SortFriends();
        }

        void UpsertFriend(FriendPresencePayload payload, string status, bool online)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.playerId))
                return;

            FriendViewState friend = FindFriend(payload.playerId);
            if (friend == null)
            {
                friend = new FriendViewState { PlayerId = payload.playerId };
                _friends.Add(friend);
            }

            friend.DisplayName = NormalizeDisplayName(payload.displayName);
            friend.Status = NormalizeFriendStatus(status);
            friend.Online = online;
            SortFriends();
        }

        FriendViewState FindFriend(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return null;

            for (int i = 0; i < _friends.Count; i++)
            {
                if (string.Equals(_friends[i].PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                    return _friends[i];
            }

            return null;
        }

        void RemoveFriendById(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            for (int i = _friends.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_friends[i].PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                    _friends.RemoveAt(i);
            }
        }

        void UpsertLobbyInvite(LobbyInvitePayload payload)
        {
            for (int i = 0; i < _pendingLobbyInvites.Count; i++)
            {
                if (string.Equals(_pendingLobbyInvites[i].Code, payload.code, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingLobbyInvites[i].LobbyId = payload.lobbyId;
                    _pendingLobbyInvites[i].Code = payload.code;
                    _pendingLobbyInvites[i].FromPlayerId = payload.fromPlayerId;
                    _pendingLobbyInvites[i].FromDisplayName = NormalizeDisplayName(payload.fromDisplayName);
                    return;
                }
            }

            _pendingLobbyInvites.Add(new PendingLobbyInviteState
            {
                LobbyId = payload.lobbyId,
                Code = payload.code,
                FromPlayerId = payload.fromPlayerId,
                FromDisplayName = NormalizeDisplayName(payload.fromDisplayName),
            });
        }

        void RemoveLobbyInvite(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            for (int i = _pendingLobbyInvites.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_pendingLobbyInvites[i].Code, code, StringComparison.OrdinalIgnoreCase))
                    _pendingLobbyInvites.RemoveAt(i);
            }
        }

        void RemoveLobbyInviteFromPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            for (int i = _pendingLobbyInvites.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_pendingLobbyInvites[i].FromPlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                    _pendingLobbyInvites.RemoveAt(i);
            }
        }

        void SortFriends()
        {
            _friends.Sort(CompareFriends);
        }

        static int CompareFriends(FriendViewState a, FriendViewState b)
        {
            int weightCompare = GetFriendSortWeight(a).CompareTo(GetFriendSortWeight(b));
            if (weightCompare != 0)
                return weightCompare;

            if (a.Online != b.Online)
                return a.Online ? -1 : 1;

            return string.Compare(
                NormalizeDisplayName(a.DisplayName),
                NormalizeDisplayName(b.DisplayName),
                StringComparison.OrdinalIgnoreCase);
        }

        static int GetFriendSortWeight(FriendViewState friend)
        {
            return friend?.Status switch
            {
                "pending_received" => 0,
                "accepted" => 1,
                "pending_sent" => 2,
                _ => 3,
            };
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
            _widgetRect.sizeDelta = new Vector2(520f, 420f);
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
            _badgeLabel = CreateText("BubbleBadge", _bubbleRect, string.Empty, 15f, FontStyles.Bold, TextAlignmentOptions.Center);
            _badgeLabel.color = new Color(1f, 0.55f, 0.36f, 1f);
            Stretch(_badgeLabel.rectTransform, new Vector2(0.56f, 0.55f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero);

            _bodyRect = CreatePanel("ExpandedPanel", _widgetRect, new Color(0.03f, 0.05f, 0.09f, 0.96f));
            Stretch(_bodyRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var header = CreatePanel("Header", _bodyRect, new Color(0.10f, 0.16f, 0.24f, 0.98f));
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 44f), Vector2.zero);
            header.gameObject.AddComponent<DraggablePanel>().Configure(_widgetRect, "runtime.chatdebug");

            _titleLabel = CreateText("Title", header, "CHAT / FRIENDS / SYSTEM", 18f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(_titleLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 0f), new Vector2(-112f, 0f));

            _maximizeButton = CreateButton("MaximizeButton", header, "Max", new Color(0.18f, 0.26f, 0.36f, 1f));
            _maximizeButton.onClick.AddListener(ToggleMaximized);
            Anchor(_maximizeButton.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(54f, 26f), new Vector2(-52f, 0f));
            SetButtonWidth(_maximizeButton, 54f);

            _collapseButton = CreateButton("CollapseButton", header, "-", new Color(0.18f, 0.26f, 0.36f, 1f));
            _collapseButton.onClick.AddListener(() => SetExpanded(false));
            Anchor(_collapseButton.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(34f, 26f), new Vector2(-12f, 0f));
            SetButtonWidth(_collapseButton, 34f);

            var tabRow = CreateHorizontalRow("Tabs", _bodyRect, 40f, 8f);
            Stretch(tabRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -92f), new Vector2(-12f, -52f));
            _chatTabButton = CreateButton("ChatTab", tabRow, "Chat", new Color(0.12f, 0.23f, 0.34f, 1f));
            _friendsTabButton = CreateButton("FriendsTab", tabRow, "Friends", new Color(0.12f, 0.23f, 0.34f, 1f));
            _systemTabButton = CreateButton("SystemTab", tabRow, "System", new Color(0.12f, 0.23f, 0.34f, 1f));
            _chatTabButton.onClick.AddListener(() => SetTab(PanelTab.Chat));
            _friendsTabButton.onClick.AddListener(() => SetTab(PanelTab.Friends));
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

            _logScrollRoot = CreatePanel("ScrollRoot", _bodyRect, new Color(0.06f, 0.08f, 0.12f, 0.98f)).gameObject;
            Stretch(_logScrollRoot.transform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 58f), new Vector2(-12f, -140f));
            _scrollRect = _logScrollRoot.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 20f;

            var viewport = CreatePanel("Viewport", _logScrollRoot.transform, new Color(1f, 1f, 1f, 0.02f));
            Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logLabel = CreateText("LogText", content, string.Empty, 16f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _logLabel.richText = true;
            _logLabel.textWrappingMode = TextWrappingModes.Normal;
            _logLabel.overflowMode = TextOverflowModes.Overflow;
            _logLabel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var logLayout = _logLabel.gameObject.AddComponent<LayoutElement>();
            logLayout.flexibleWidth = 1f;
            logLayout.minHeight = 24f;
            _scrollRect.viewport = viewport;
            _scrollRect.content = content;
            _logScrollbar = CreateVerticalScrollbar("LogScrollbar", _logScrollRoot.transform);
            _scrollRect.verticalScrollbar = _logScrollbar;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scrollRect.verticalScrollbarSpacing = 6f;

            _friendsScrollRoot = CreatePanel("FriendsScrollRoot", _bodyRect, new Color(0.06f, 0.08f, 0.12f, 0.98f)).gameObject;
            Stretch(_friendsScrollRoot.transform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 58f), new Vector2(-12f, -140f));
            var friendsViewport = CreatePanel("Viewport", _friendsScrollRoot.transform, new Color(1f, 1f, 1f, 0.02f));
            Stretch(friendsViewport, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            friendsViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            _friendsListRoot = CreateRect("Content", friendsViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            _friendsListRoot.offsetMin = Vector2.zero;
            _friendsListRoot.offsetMax = Vector2.zero;
            _friendsListRoot.sizeDelta = new Vector2(0f, 320f);
            var friendsLayout = _friendsListRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            friendsLayout.childAlignment = TextAnchor.UpperLeft;
            friendsLayout.childControlWidth = true;
            friendsLayout.childControlHeight = true;
            friendsLayout.childForceExpandWidth = true;
            friendsLayout.childForceExpandHeight = false;
            friendsLayout.spacing = 10f;
            _friendsListRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _friendsScrollRect = _friendsScrollRoot.AddComponent<ScrollRect>();
            _friendsScrollRect.horizontal = false;
            _friendsScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _friendsScrollRect.scrollSensitivity = 20f;
            _friendsScrollRect.viewport = friendsViewport;
            _friendsScrollRect.content = _friendsListRoot;
            _friendsScrollbar = CreateVerticalScrollbar("FriendsScrollbar", _friendsScrollRoot.transform);
            _friendsScrollRect.verticalScrollbar = _friendsScrollbar;
            _friendsScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _friendsScrollRect.verticalScrollbarSpacing = 6f;

            _statusLabel = CreateText("Status", _bodyRect, string.Empty, 14f, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            _statusLabel.color = new Color(0.82f, 0.85f, 0.90f, 0.92f);
            Stretch(_statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 36f), new Vector2(-12f, 58f));

            _chatInputRow = CreateHorizontalRow("ChatInputRow", _bodyRect, 40f, 8f).gameObject;
            Stretch(_chatInputRow.transform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-12f, 52f));
            _chatInput = CreateInputField("ChatInput", _chatInputRow.transform, "Type a message...");
            _sendButton = CreateButton("SendButton", _chatInputRow.transform, "Send", new Color(0.16f, 0.38f, 0.28f, 1f));
            _sendButton.onClick.AddListener(SubmitChat);
            SetButtonWidth(_sendButton, 88f);

            _friendsActionRow = CreateHorizontalRow("FriendsActionRow", _bodyRect, 40f, 8f).gameObject;
            Stretch(_friendsActionRow.transform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 12f), new Vector2(-12f, 52f));
            _friendInput = CreateInputField("FriendInput", _friendsActionRow.transform, "Add friend by display name...");
            _addFriendButton = CreateButton("AddFriendButton", _friendsActionRow.transform, "Add Friend", new Color(0.23f, 0.36f, 0.19f, 1f));
            _addFriendButton.onClick.AddListener(SubmitFriendAdd);
            SetButtonWidth(_addFriendButton, 118f);
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
                switch (_currentTab)
                {
                    case PanelTab.Chat:
                        MarkChatSeen();
                        break;
                    case PanelTab.Friends:
                        MarkSocialSeen();
                        break;
                    default:
                        MarkSystemSeen();
                        break;
                }
            }

            _layoutDirty = true;
            RefreshBadge();
        }

        void SetTab(PanelTab tab)
        {
            _currentTab = tab;
            switch (_currentTab)
            {
                case PanelTab.Chat:
                    MarkChatSeen();
                    break;
                case PanelTab.Friends:
                    MarkSocialSeen();
                    break;
                default:
                    MarkSystemSeen();
                    break;
            }

            _textDirty = true;
            _friendsDirty = true;
            RefreshTabVisuals();
            RefreshBadge();
        }

        void RefreshAll(bool forceScrollToBottom)
        {
            RefreshLayout();
            RefreshFilterVisuals();
            RefreshTabVisuals();
            RefreshWindowVisuals();
            RefreshRenderedText(forceScrollToBottom);
            RefreshBadge();
        }

        void RefreshLayout()
        {
            _layoutDirty = false;
            bool mobile = IsMobileLayout();
            Vector2 targetSize = _isExpanded ? GetExpandedSize(mobile) : GetCollapsedSize(mobile);

            if (_widgetRect != null)
            {
                _widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize.x);
                _widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize.y);
                ClampWidgetToParent();
            }

            if (_bubbleLabel != null)
                _bubbleLabel.fontSize = mobile ? 16f : 18f;
            if (_badgeLabel != null)
                _badgeLabel.fontSize = mobile ? 13f : 15f;

            bool chatTab = _currentTab == PanelTab.Chat;
            bool friendsTab = _currentTab == PanelTab.Friends;
            bool systemTab = _currentTab == PanelTab.System;

            if (_chatInputRow != null)
                _chatInputRow.SetActive(chatTab);
            if (_friendsActionRow != null)
                _friendsActionRow.SetActive(friendsTab && AuthManager.IsAuthenticated);
            if (_systemFilterRow != null)
                _systemFilterRow.SetActive(systemTab);
            if (_logScrollRoot != null)
                _logScrollRoot.SetActive(!friendsTab);
            if (_friendsScrollRoot != null)
                _friendsScrollRoot.SetActive(friendsTab);

            bool canSubmitFriendRequest = friendsTab
                && AuthManager.IsAuthenticated
                && _networkManager != null
                && _networkManager.IsConnected;
            if (_friendInput != null)
                _friendInput.readOnly = !canSubmitFriendRequest;
            if (_addFriendButton != null)
                _addFriendButton.interactable = canSubmitFriendRequest;
        }

        Vector2 GetExpandedSize(bool mobile)
        {
            if (_isMaximized)
                return GetMaximizedSize(mobile);

            return mobile ? new Vector2(360f, 360f) : new Vector2(520f, 420f);
        }

        static Vector2 GetCollapsedSize(bool mobile)
        {
            return mobile ? new Vector2(64f, 64f) : new Vector2(72f, 72f);
        }

        Vector2 GetMaximizedSize(bool mobile)
        {
            var parentRect = _widgetRect != null ? _widgetRect.parent as RectTransform : null;
            if (parentRect == null)
                return mobile ? new Vector2(360f, 360f) : new Vector2(520f, 420f);

            float margin = mobile ? 12f : 20f;
            Vector2 parentSize = parentRect.rect.size;
            return new Vector2(
                Mathf.Max(mobile ? 320f : 480f, parentSize.x - (margin * 2f)),
                Mathf.Max(mobile ? 260f : 360f, parentSize.y - (margin * 2f)));
        }

        void ToggleMaximized()
        {
            _isMaximized = !_isMaximized;
            _layoutDirty = true;
            RefreshWindowVisuals();
        }

        void RefreshWindowVisuals()
        {
            SetButtonText(_maximizeButton, _isMaximized ? "Fit" : "Max");
            StyleButton(_maximizeButton, _isMaximized ? new Color(0.30f, 0.40f, 0.18f, 1f) : new Color(0.18f, 0.26f, 0.36f, 1f));
        }

        void ClampWidgetToParent()
        {
            if (_widgetRect == null)
                return;

            var parentRect = _widgetRect.parent as RectTransform;
            if (parentRect == null)
                return;

            Vector2 parentSize = parentRect.rect.size;
            if (parentSize.x <= 0.01f || parentSize.y <= 0.01f)
                return;

            Vector2 size = _widgetRect.rect.size;
            Vector2 parentPivot = parentRect.pivot;
            Vector2 anchorCenter = (_widgetRect.anchorMin + _widgetRect.anchorMax) * 0.5f;
            Vector2 anchorLocal = new Vector2(
                (anchorCenter.x - parentPivot.x) * parentSize.x,
                (anchorCenter.y - parentPivot.y) * parentSize.y);

            Vector2 minPivot = new Vector2(
                -parentSize.x * parentPivot.x + size.x * _widgetRect.pivot.x + 8f,
                -parentSize.y * parentPivot.y + size.y * _widgetRect.pivot.y + 8f);
            Vector2 maxPivot = new Vector2(
                parentSize.x * (1f - parentPivot.x) - size.x * (1f - _widgetRect.pivot.x) - 8f,
                parentSize.y * (1f - parentPivot.y) - size.y * (1f - _widgetRect.pivot.y) - 8f);

            Vector2 pivotLocal = anchorLocal + _widgetRect.anchoredPosition;
            pivotLocal.x = Mathf.Clamp(pivotLocal.x, minPivot.x, maxPivot.x);
            pivotLocal.y = Mathf.Clamp(pivotLocal.y, minPivot.y, maxPivot.y);
            _widgetRect.anchoredPosition = pivotLocal - anchorLocal;
        }

        void RefreshTabVisuals()
        {
            StyleButton(_chatTabButton, _currentTab == PanelTab.Chat ? new Color(0.23f, 0.43f, 0.64f, 1f) : new Color(0.12f, 0.23f, 0.34f, 1f));
            StyleButton(_friendsTabButton, _currentTab == PanelTab.Friends ? new Color(0.34f, 0.30f, 0.16f, 1f) : new Color(0.12f, 0.23f, 0.34f, 1f));
            StyleButton(_systemTabButton, _currentTab == PanelTab.System ? new Color(0.23f, 0.43f, 0.64f, 1f) : new Color(0.12f, 0.23f, 0.34f, 1f));

            _layoutDirty = true;
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

            switch (_currentTab)
            {
                case PanelTab.Chat:
                    _currentRenderedText = BuildChatText();
                    _titleLabel.text = "ALL CHAT";
                    _statusLabel.text = _networkManager != null && _networkManager.IsConnected
                        ? "Messages go to the active multiplayer room."
                        : "Chat send is disabled until the socket is connected.";
                    if (_logLabel != null)
                        _logLabel.text = _currentRenderedText;
                    RefreshLogScrollLayout(forceScrollToBottom);
                    break;

                case PanelTab.Friends:
                    _friendsDirty = false;
                    _currentRenderedText = BuildFriendsSummaryText();
                    _titleLabel.text = "FRIENDS";
                    _statusLabel.text = BuildFriendsStatusText();
                    if (_logLabel != null)
                        _logLabel.text = string.Empty;
                    RefreshFriendsContent();
                    RefreshFriendsScrollLayout(forceScrollToBottom);
                    break;

                default:
                    _currentRenderedText = BuildSystemText();
                    _titleLabel.text = "SYSTEM / ERRORS";
                    _statusLabel.text = "Unity log stream plus loud runtime diagnostics. Errors and warnings always surface here.";
                    if (_logLabel != null)
                        _logLabel.text = _currentRenderedText;
                    RefreshLogScrollLayout(forceScrollToBottom);
                    break;
            }
        }

        void RefreshLogScrollLayout(bool forceScrollToBottom)
        {
            if (_scrollRect == null || _logLabel == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_logLabel.rectTransform);
            if (_scrollRect.content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
            Canvas.ForceUpdateCanvases();

            if (forceScrollToBottom)
                _scrollRect.verticalNormalizedPosition = 0f;
        }

        void RefreshFriendsScrollLayout(bool forceScrollToBottom)
        {
            if (_friendsScrollRect == null || _friendsListRoot == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_friendsListRoot);
            Canvas.ForceUpdateCanvases();

            if (forceScrollToBottom)
                _friendsScrollRect.verticalNormalizedPosition = 1f;
        }

        void RefreshFriendsContent()
        {
            if (_friendsListRoot == null)
                return;

            ClearChildren(_friendsListRoot);

            if (!AuthManager.IsAuthenticated)
            {
                AddInfoCard("Sign in to manage friends, track who is online, and send private lobby invites.");
                return;
            }

            if (_pendingLobbyInvites.Count > 0)
            {
                AddSectionHeader($"Lobby Invites ({_pendingLobbyInvites.Count})");
                for (int i = 0; i < _pendingLobbyInvites.Count; i++)
                    AddInviteCard(_pendingLobbyInvites[i]);
            }

            if (_friends.Count == 0)
            {
                AddInfoCard("No allies are listed yet. Add a friend by display name to start building your war band.");
                return;
            }

            int pendingReceived = CountFriendsWithStatus("pending_received");
            int accepted = CountFriendsWithStatus("accepted");
            int pendingSent = CountFriendsWithStatus("pending_sent");

            if (pendingReceived > 0)
                AddSectionHeader($"Requests Waiting ({pendingReceived})");
            for (int i = 0; i < _friends.Count; i++)
            {
                if (string.Equals(_friends[i].Status, "pending_received", StringComparison.OrdinalIgnoreCase))
                    AddFriendCard(_friends[i]);
            }

            if (accepted > 0)
                AddSectionHeader($"Allies ({accepted})");
            for (int i = 0; i < _friends.Count; i++)
            {
                if (string.Equals(_friends[i].Status, "accepted", StringComparison.OrdinalIgnoreCase))
                    AddFriendCard(_friends[i]);
            }

            if (pendingSent > 0)
                AddSectionHeader($"Requests Sent ({pendingSent})");
            for (int i = 0; i < _friends.Count; i++)
            {
                if (string.Equals(_friends[i].Status, "pending_sent", StringComparison.OrdinalIgnoreCase))
                    AddFriendCard(_friends[i]);
            }
        }

        void AddSectionHeader(string text)
        {
            var label = CreateText($"Header_{_friendsListRoot.childCount}", _friendsListRoot, text, 15f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            label.color = new Color(0.95f, 0.83f, 0.50f, 0.98f);
            label.richText = false;
            var layout = label.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 24f;
        }

        void AddInfoCard(string message)
        {
            var card = CreatePanel($"InfoCard_{_friendsListRoot.childCount}", _friendsListRoot, new Color(0.10f, 0.13f, 0.18f, 0.96f));
            ConfigureVerticalCard(card, 10f, new RectOffset(14, 14, 14, 14));
            var label = CreateText("Message", card, message, 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            label.color = new Color(0.86f, 0.90f, 0.95f, 0.98f);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.richText = false;
        }

        void AddInviteCard(PendingLobbyInviteState invite)
        {
            if (invite == null)
                return;

            var card = CreatePanel($"InviteCard_{_friendsListRoot.childCount}", _friendsListRoot, new Color(0.15f, 0.13f, 0.08f, 0.98f));
            ConfigureVerticalCard(card, 8f, new RectOffset(12, 12, 12, 12));

            var title = CreateText("Title", card, NormalizeDisplayName(invite.FromDisplayName), 16f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            title.color = Color.white;
            title.richText = false;

            var subtitle = CreateText(
                "Subtitle",
                card,
                $"<color=#F3D48B>Lobby Invite</color>\n<color=#AFC0D3>Code: {Escape(invite.Code)}</color>",
                14f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            subtitle.textWrappingMode = TextWrappingModes.Normal;

            var actionRow = CreateHorizontalRow("Actions", card, 34f, 8f);
            var joinButton = CreateButton("JoinButton", actionRow, "Join", new Color(0.19f, 0.38f, 0.27f, 1f));
            joinButton.onClick.AddListener(() => JoinLobbyInvite(invite));
            SetButtonWidth(joinButton, 94f);

            var dismissButton = CreateButton("DismissButton", actionRow, "Dismiss", new Color(0.28f, 0.18f, 0.18f, 1f));
            dismissButton.onClick.AddListener(() =>
            {
                RemoveLobbyInvite(invite.Code);
                NotifySocialChanged();
            });
            SetButtonWidth(dismissButton, 94f);
        }

        void AddFriendCard(FriendViewState friend)
        {
            if (friend == null)
                return;

            var card = CreatePanel($"FriendCard_{_friendsListRoot.childCount}", _friendsListRoot, new Color(0.08f, 0.11f, 0.16f, 0.98f));
            ConfigureVerticalCard(card, 8f, new RectOffset(12, 12, 12, 12));

            var title = CreateText("Title", card, NormalizeDisplayName(friend.DisplayName), 16f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            title.color = Color.white;
            title.richText = false;

            var detailText = CreateText("Details", card, BuildFriendDetailText(friend), 13f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            detailText.textWrappingMode = TextWrappingModes.Normal;

            var actionRow = CreateHorizontalRow("Actions", card, 34f, 8f);

            if (string.Equals(friend.Status, "pending_received", StringComparison.OrdinalIgnoreCase))
            {
                var acceptButton = CreateButton("AcceptButton", actionRow, "Accept", new Color(0.19f, 0.38f, 0.27f, 1f));
                acceptButton.onClick.AddListener(() => _networkManager?.AcceptFriendRequest(friend.PlayerId));
                SetButtonWidth(acceptButton, 96f);

                var declineButton = CreateButton("DeclineButton", actionRow, "Decline", new Color(0.30f, 0.18f, 0.18f, 1f));
                declineButton.onClick.AddListener(() => _networkManager?.DeclineFriendRequest(friend.PlayerId));
                SetButtonWidth(declineButton, 96f);
            }
            else if (string.Equals(friend.Status, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                var inviteButton = CreateButton("InviteButton", actionRow, "Invite", new Color(0.36f, 0.28f, 0.12f, 1f));
                inviteButton.onClick.AddListener(() => _networkManager?.InviteFriendToLobby(friend.PlayerId));
                inviteButton.interactable = CanInviteFriend(friend);
                SetButtonWidth(inviteButton, 92f);

                var removeButton = CreateButton("RemoveButton", actionRow, "Remove", new Color(0.30f, 0.18f, 0.18f, 1f));
                removeButton.onClick.AddListener(() => _networkManager?.RemoveFriend(friend.PlayerId));
                SetButtonWidth(removeButton, 96f);
            }
            else
            {
                var cancelButton = CreateButton("CancelButton", actionRow, "Cancel", new Color(0.30f, 0.18f, 0.18f, 1f));
                cancelButton.onClick.AddListener(() => _networkManager?.DeclineFriendRequest(friend.PlayerId));
                SetButtonWidth(cancelButton, 96f);
            }
        }

        static void ConfigureVerticalCard(RectTransform rect, float spacing, RectOffset padding)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            layout.padding = padding;
            rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        bool CanInviteFriend(FriendViewState friend)
        {
            if (friend == null
                || !string.Equals(friend.Status, "accepted", StringComparison.OrdinalIgnoreCase)
                || !friend.Online
                || _networkManager == null
                || !_networkManager.IsConnected)
            {
                return false;
            }

            LobbySnapshot lobby = _networkManager.CurrentLobby;
            if (lobby == null)
                return false;

            return string.Equals(lobby.status, "open", StringComparison.OrdinalIgnoreCase)
                && string.Equals(lobby.hostSocketId, _networkManager.MySocketId, StringComparison.Ordinal);
        }

        void JoinLobbyInvite(PendingLobbyInviteState invite)
        {
            if (invite == null || string.IsNullOrWhiteSpace(invite.Code))
                return;

            ActionSender.LobbyJoin(invite.Code, AuthManager.IsAuthenticated ? AuthManager.DisplayName : "Player");
            RuntimeDiagnosticsService.PublishSystem(
                RuntimeLogSeverity.Info,
                "Friends",
                $"Joining lobby {invite.Code} from {NormalizeDisplayName(invite.FromDisplayName)}.");
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

            int unreadSocial = _socialRevision > _lastSeenSocialRevision
                ? CountPendingSocialActions()
                : 0;

            int unreadCount = unreadChat + unreadSystem + unreadSocial;
            _badgeLabel.text = unreadCount > 0 ? unreadCount.ToString() : string.Empty;
            if (_bubbleImage != null)
            {
                _bubbleImage.color = unreadSystem > 0
                    ? new Color(0.22f, 0.08f, 0.08f, 0.98f)
                    : unreadSocial > 0
                        ? new Color(0.19f, 0.14f, 0.05f, 0.98f)
                        : unreadChat > 0
                            ? new Color(0.05f, 0.12f, 0.16f, 0.98f)
                            : new Color(0.05f, 0.08f, 0.12f, 0.96f);
            }
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

        void MarkSocialSeen()
        {
            _lastSeenSocialRevision = _socialRevision;
        }

        int CountPendingSocialActions()
        {
            return CountFriendsWithStatus("pending_received") + _pendingLobbyInvites.Count;
        }

        int CountFriendsWithStatus(string status)
        {
            int count = 0;
            for (int i = 0; i < _friends.Count; i++)
            {
                if (string.Equals(_friends[i].Status, status, StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            return count;
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

        void SubmitFriendAdd()
        {
            string displayName = _friendInput?.text?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                RuntimeDiagnosticsService.PublishSystem(RuntimeLogSeverity.Warning, "Friends", "Enter a display name before sending a friend request.");
                return;
            }

            if (!AuthManager.IsAuthenticated)
            {
                RuntimeDiagnosticsService.PublishSystem(RuntimeLogSeverity.Warning, "Friends", "Sign in before managing your friends list.");
                return;
            }

            if (_networkManager == null || !_networkManager.IsConnected)
            {
                RuntimeDiagnosticsService.PublishSystem(RuntimeLogSeverity.Warning, "Friends", "Reconnect to the server before sending a friend request.");
                return;
            }

            _networkManager.SendFriendRequest(displayName);
            if (_friendInput != null)
            {
                _friendInput.text = string.Empty;
                _friendInput.ActivateInputField();
            }
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

        string BuildFriendsSummaryText()
        {
            if (!AuthManager.IsAuthenticated)
                return "Sign in to manage friends, see who is online, and send private lobby invites.";

            var builder = new StringBuilder(256);
            if (_pendingLobbyInvites.Count > 0)
            {
                builder.AppendLine("Lobby invites:");
                for (int i = 0; i < _pendingLobbyInvites.Count; i++)
                {
                    var invite = _pendingLobbyInvites[i];
                    builder.Append("- ");
                    builder.Append(NormalizeDisplayName(invite.FromDisplayName));
                    builder.Append(" (");
                    builder.Append(invite.Code);
                    builder.Append(')');
                    builder.AppendLine();
                }
            }

            if (_friends.Count == 0)
            {
                builder.Append("No friends listed.");
                return builder.ToString();
            }

            builder.AppendLine("Friends:");
            for (int i = 0; i < _friends.Count; i++)
            {
                var friend = _friends[i];
                builder.Append("- ");
                builder.Append(NormalizeDisplayName(friend.DisplayName));
                builder.Append(" [");
                builder.Append(friend.Status);
                builder.Append(friend.Online ? ", online" : ", offline");
                builder.Append(']');
                if (i < _friends.Count - 1)
                    builder.AppendLine();
            }

            return builder.ToString();
        }

        string BuildFriendsStatusText()
        {
            if (!AuthManager.IsAuthenticated)
                return "Sign in to manage friends, see who is online, and send private lobby invites.";

            int accepted = CountFriendsWithStatus("accepted");
            int onlineAccepted = 0;
            for (int i = 0; i < _friends.Count; i++)
            {
                if (string.Equals(_friends[i].Status, "accepted", StringComparison.OrdinalIgnoreCase) && _friends[i].Online)
                    onlineAccepted++;
            }

            int pendingReceived = CountFriendsWithStatus("pending_received");
            int pendingSent = CountFriendsWithStatus("pending_sent");

            var builder = new StringBuilder();
            if (accepted > 0)
                builder.Append($"{onlineAccepted}/{accepted} allies online.");
            else
                builder.Append("No accepted allies yet.");

            if (pendingReceived > 0)
                builder.Append($" {pendingReceived} request(s) waiting.");
            if (pendingSent > 0)
                builder.Append($" {pendingSent} request(s) sent.");
            if (_pendingLobbyInvites.Count > 0)
                builder.Append($" {_pendingLobbyInvites.Count} lobby invite(s) waiting.");

            if (_networkManager == null || !_networkManager.IsConnected)
                builder.Append(" Presence will refresh after the socket reconnects.");
            else if (CanSendLobbyInvites())
                builder.Append(" Your private lobby is open, so online allies can be invited now.");
            else if (_networkManager.CurrentLobby != null)
                builder.Append(" Only the current lobby host can invite allies.");
            else
                builder.Append(" Open a private lobby to invite online allies.");

            return builder.ToString();
        }

        bool CanSendLobbyInvites()
        {
            LobbySnapshot lobby = _networkManager?.CurrentLobby;
            if (lobby == null || _networkManager == null)
                return false;

            return string.Equals(lobby.status, "open", StringComparison.OrdinalIgnoreCase)
                && string.Equals(lobby.hostSocketId, _networkManager.MySocketId, StringComparison.Ordinal);
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

        static string BuildFriendDetailText(FriendViewState friend)
        {
            string presenceColor = friend.Online ? "#79D59C" : "#90A3B9";
            string presenceLabel = friend.Online ? "Online" : "Offline";
            string statusLabel = friend.Status switch
            {
                "pending_received" => "Incoming request",
                "pending_sent" => "Request sent",
                "accepted" => "Accepted ally",
                _ => "Unknown",
            };

            return $"<color={presenceColor}>{presenceLabel}</color>\n<color=#D4DEE9>{Escape(statusLabel)}</color>";
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

        static string NormalizeDisplayName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();
        }

        static string NormalizeFriendStatus(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "accepted" : value.Trim();
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
                && string.Equals(activeScene.name, "Bootstrap", StringComparison.OrdinalIgnoreCase);
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

                if (string.Equals(eventSystem.name, "RuntimeDiagnosticsEventSystem", StringComparison.Ordinal))
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
            label.raycastTarget = false;
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

        static Scrollbar CreateVerticalScrollbar(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 0.5f);
            rootRect.sizeDelta = new Vector2(14f, 0f);
            rootRect.anchoredPosition = new Vector2(-6f, 0f);
            root.GetComponent<Image>().color = new Color(0.11f, 0.14f, 0.19f, 0.92f);

            var slidingArea = CreateRect("SlidingArea", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            slidingArea.offsetMin = new Vector2(2f, 2f);
            slidingArea.offsetMax = new Vector2(-2f, -2f);

            var handle = CreatePanel("Handle", slidingArea, new Color(0.76f, 0.66f, 0.42f, 0.95f));
            Stretch(handle, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 48f));

            var scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.size = 0.2f;
            scrollbar.numberOfSteps = 0;
            return scrollbar;
        }

        static void SetButtonWidth(Button button, float width)
        {
            if (button == null)
                return;

            var layout = button.GetComponent<LayoutElement>();
            if (layout == null)
                layout = button.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 0f;
            layout.minWidth = width;
            layout.preferredWidth = width;
        }

        static void SetButtonText(Button button, string text)
        {
            if (button == null)
                return;

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.text = text ?? string.Empty;
        }

        static void ClearChildren(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
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
