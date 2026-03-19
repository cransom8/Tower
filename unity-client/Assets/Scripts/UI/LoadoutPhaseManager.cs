// LoadoutPhaseManager.cs — In-game loadout selection phase panel.
//
// Subscribes to NetworkManager.OnMLLoadoutPhaseStart / OnMLLoadoutPhaseEnd.
// Builds a full-screen overlay at runtime (no prefab required).
// Player selects 5 units from the available catalog, then confirms.
// On timer expiry the current selection (padded with random picks) is auto-confirmed.
//
// SCENE SETUP:
//   Add a LoadoutPhaseManager component to any persistent GameObject in Game_ML.
//   No inspector wiring required — the panel is built programmatically.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    [DefaultExecutionOrder(-50)]
    public class LoadoutPhaseManager : MonoBehaviour
    {
        // ── Inspector (optional colour overrides) ─────────────────────────────
        [Header("Card Colors")]
        public Color ColorSelected   = new Color(0.20f, 0.70f, 0.30f);
        public Color ColorUnselected = new Color(0.22f, 0.22f, 0.28f);

        [Header("Timer Colors")]
        public Color ColorTimerNormal = new Color(1f, 0.9f, 0.3f);
        public Color ColorTimerUrgent = new Color(1f, 0.3f, 0.3f);

        [Header("Card Layout")]
        public float CardWidth   = 170f;
        public float CardHeight  = 84f;
        public float CardSpacing = 10f;
        public int   CardColumns = 5;

        const float PortraitFrameHeight = 92f;
        const float CardTextExtraHeight = 34f;


        // ── Phase state ───────────────────────────────────────────────────────
        enum PhaseState { Idle, PreparingLoadout, Active, Confirming, WaitingForMatch, Done }
        PhaseState _state = PhaseState.Idle;

        // ── Panel references (created at runtime) ─────────────────────────────
        GameObject     _panelRoot;
        TMP_Text       _txtTimer;
        TMP_Text       _txtStatus;
        Button         _btnConfirm;
        TMP_Text       _txtConfirmLabel;
        Transform      _gridParent;

        // ── Preparation overlay (shown before loadout phase starts) ───────────
        GameObject _prepOverlay;
        TMP_Text   _txtPrepStatus;
        TMP_Text   _txtPrepDetail;

        // ── Per-player status panel ───────────────────────────────────────────
        GameObject              _playerPanelRoot;
        readonly List<(TMP_Text name, TMP_Text state, Image bar)> _playerRows = new();

        // ── Data ──────────────────────────────────────────────────────────────
        LoadoutEntry[]           _available;
        readonly int[]           _selected  = new int[5] { -1, -1, -1, -1, -1 };
        readonly string[]        _selNames  = new string[5];
        readonly List<GameObject> _cards    = new();
        readonly Dictionary<string, Texture2D> _portraitCache = new();
        readonly Dictionary<string, List<RawImage>> _pendingPortraitTargets = new(StringComparer.OrdinalIgnoreCase);
        float _timerRemaining;
        float _phaseStartTime;
        Coroutine _portraitWarmupRoutine;
        Coroutine _criticalWarmupRoutine;
        Coroutine _environmentWarmupRoutine;

        // ── Readiness tracking ────────────────────────────────────────────────
        bool _loadoutReadyEmitted;
        bool _portraitWarmupDone;
        bool _criticalWarmupDone;
        bool _gameplayTransitionStarted;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()
        {
            EnsureEventSystem();
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLLoadoutPhaseStart      += HandlePhaseStart;
            nm.OnMLLoadoutPhaseEnd        += HandlePhaseEnd;
            nm.OnMLMatchConfig            += HandleMatchConfig;
            nm.OnMLMatchPreparationState  += HandlePreparationState;
            nm.OnMLMatchCancelled         += HandleMatchCancelled;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLLoadoutPhaseStart      -= HandlePhaseStart;
            nm.OnMLLoadoutPhaseEnd        -= HandlePhaseEnd;
            nm.OnMLMatchConfig            -= HandleMatchConfig;
            nm.OnMLMatchPreparationState  -= HandlePreparationState;
            nm.OnMLMatchCancelled         -= HandleMatchCancelled;
            _portraitCache.Clear();
            _pendingPortraitTargets.Clear();
            StopWarmupRoutines();
        }

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            BuildPrepOverlay();

            // LobbyUI / PostGameSceneController already emits ml_loadout_ready and
            // ensures critical content is ready before the server sends ml_loadout_phase_start.
            // If that preload completed, skip the redundant warmup coroutine here.
            var rc = RemoteContentManager.EnsureInstance();
            if (rc != null && rc.HasCompletedCriticalPreload)
            {
                _criticalWarmupDone = true;
                // Still emit ml_loadout_ready as safety net (server deduplicates)
                TryEmitLoadoutReady();
            }

            // ml_loadout_phase_start may have fired before this scene finished
            // loading (reconnect / race condition).  NetworkManager caches it so
            // we can pick it up here on the first frame after scene init.
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            var pending = nm.PendingLoadoutPhase;
            if (pending != null && _state == PhaseState.Idle)
                HandlePhaseStart(pending);

            // Replay the last preparation state in case it arrived before this scene loaded.
            if (nm.LastPreparationState != null)
                HandlePreparationState(nm.LastPreparationState);
        }

        void HandlePhaseStart(MLLoadoutPhaseStartPayload payload)
        {
            _available = payload.availableUnits ?? Array.Empty<LoadoutEntry>();
            _timerRemaining = Mathf.Max(1f, payload.timeoutSeconds);

            // Reset readiness tracking for this phase
            var rc = RemoteContentManager.EnsureInstance();
            _loadoutReadyEmitted = false;
            _portraitWarmupDone  = false;
            _criticalWarmupDone  = rc != null && rc.HasCompletedCriticalPreload;
            _gameplayTransitionStarted = false;
            _phaseStartTime = Time.unscaledTime;

            Array.Fill(_selected, -1);
            Array.Fill(_selNames, null);
            _cards.Clear();
            _pendingPortraitTargets.Clear();
            StopWarmupRoutines(stopCritical: false);

            // Hide preparation overlay, show loadout panel (player strip built inside BuildPanel)
            HidePrepOverlay();
            if (_panelRoot != null) Destroy(_panelRoot);
            _playerPanelRoot = null;  // destroyed with _panelRoot above
            _playerRows.Clear();
            BuildPanel();
            StartBackgroundWarmup();

            if (payload.selectionMode == "random")
            {
                AutoRandomSelect();
                StartCoroutine(AutoConfirmDelayed(1f));
                return;
            }

            _state = PhaseState.Active;
        }

        void HandlePhaseEnd(MLLoadoutPhaseEndPayload _)
        {
            // Only relevant if still waiting (timer-drift edge case)
            if (_state == PhaseState.Active) SubmitConfirm();
        }

        void HandleMatchConfig(MLMatchConfig cfg)
        {
            if (cfg.loadout == null || cfg.loadout.Length == 0) return;
            if (_gameplayTransitionStarted) return;

            // Loadout resolved — switch to "Preparing battlefield / Waiting for players" state
            _gameplayTransitionStarted = true;
            _state = PhaseState.WaitingForMatch;
            if (_txtStatus  != null) _txtStatus.text  = "Loadout locked";
            if (_btnConfirm != null) _btnConfirm.interactable = false;
            ShowWaitingForMatchOverlay();

            // Skip redundant preload steps if the environment warmup from the lobby
            // already completed these downloads (prevents a second "Preparing content" flash).
            var rc = RemoteContentManager.EnsureInstance();
            bool needT1  = rc == null || !rc.HasCompletedCriticalPreload;
            bool needEnv = rc == null || !rc.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress);
            LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML",
                preloadT1Gameplay:  needT1,
                preloadEnvironment: needEnv);
        }

        void HandlePreparationState(MLMatchPreparationStatePayload payload)
        {
            if (payload?.players == null) return;
            UpdatePlayerPanel(payload.players);
            // Also refresh the waiting overlay detail text
            if (_state == PhaseState.WaitingForMatch && _txtPrepDetail != null)
            {
                int ready  = 0;
                int total  = 0;
                foreach (var p in payload.players)
                {
                    total++;
                    if (p.gameplayReady) ready++;
                }
                _txtPrepDetail.text = ready >= total
                    ? "All players ready — starting match..."
                    : $"Waiting for players ({ready}/{total} ready)";
            }
        }

        void HandleMatchCancelled(MLMatchCancelledPayload payload)
        {
            Debug.LogWarning($"[LoadoutPhase] Match cancelled: {payload?.message}");
            _state = PhaseState.Done;
            if (_panelRoot != null) _panelRoot.SetActive(false);
            HidePrepOverlay();
            // Show cancellation message then return to lobby
            StartCoroutine(ShowCancelledAndReturn(payload?.message ?? "Match cancelled."));
        }

        IEnumerator ShowCancelledAndReturn(string message)
        {
            ShowPrepOverlay();
            if (_txtPrepStatus != null) _txtPrepStatus.text = "Match Cancelled";
            if (_txtPrepDetail != null) _txtPrepDetail.text = message;
            yield return new WaitForSeconds(3f);
            LoadingScreen.LoadScene("Lobby");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Timer
        void Update()
        {
            if (_state == PhaseState.Active)
            {
                _timerRemaining -= Time.deltaTime;
                if (_txtTimer != null)
                {
                    int secs = Mathf.CeilToInt(_timerRemaining);
                    _txtTimer.text  = secs > 0 ? $"{secs}s" : "0s";
                    _txtTimer.color = _timerRemaining < 5f ? ColorTimerUrgent : ColorTimerNormal;
                }
                if (_timerRemaining <= 0f) SubmitConfirm();
            }

            RefreshPendingPortraits();
            RefreshFallbackPlayerPanel();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Selection logic

        void ToggleUnit(int id, string unitName)
        {
            // Check if already selected — find slot
            for (int i = 0; i < 5; i++)
            {
                if (_selected[i] == id)
                {
                    // Deselect: shift remaining slots left
                    for (int j = i; j < 4; j++)
                    {
                        _selected[j]  = _selected[j + 1];
                        _selNames[j]  = _selNames[j + 1];
                    }
                    _selected[4] = -1;
                    _selNames[4] = null;
                    RefreshSlots();
                    RefreshCardColors();
                    RefreshConfirmButton();
                    return;
                }
            }
            // Add to first empty slot
            for (int i = 0; i < 5; i++)
            {
                if (_selected[i] == -1)
                {
                    _selected[i] = id;
                    _selNames[i] = unitName;
                    RefreshSlots();
                    RefreshCardColors();
                    RefreshConfirmButton();
                    return;
                }
            }
            // All slots full — replace last slot
            _selected[4] = id;
            _selNames[4] = unitName;
            RefreshSlots();
            RefreshCardColors();
            RefreshConfirmButton();
        }

        void RefreshCardColors()
        {
            for (int c = 0; c < _cards.Count && c < _available.Length; c++)
            {
                int id = _available[c].id;
                bool sel = Array.IndexOf(_selected, id) >= 0;
                var img = _cards[c].GetComponent<Image>();
                if (img != null) img.color = sel ? ColorSelected : ColorUnselected;
            }
        }

        void RefreshSlots() { } // slot strip removed — selection shown via card highlight only

        void RefreshConfirmButton()
        {
            if (_btnConfirm == null) return;
            bool active = _state == PhaseState.Active;
            _btnConfirm.interactable = active;
            if (_txtConfirmLabel != null)
                _txtConfirmLabel.color = active ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }

        void AutoRandomSelect()
        {
            if (_available == null || _available.Length == 0) return;
            // Fisher-Yates shuffle on a copy of indices
            int[] indices = new int[_available.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            int count = Mathf.Min(5, _available.Length);
            for (int i = 0; i < count; i++)
            {
                _selected[i] = _available[indices[i]].id;
                _selNames[i] = _available[indices[i]].name;
            }
            RefreshSlots();
            RefreshCardColors();
        }

        void SubmitConfirm()
        {
            if (_state != PhaseState.Active) return;
            _state = PhaseState.Confirming;
            if (_btnConfirm != null) _btnConfirm.interactable = false;
            if (_txtStatus  != null) _txtStatus.text = "Waiting for other players...";

            // Fill any empty slots with random picks
            var available = new List<LoadoutEntry>(_available ?? Array.Empty<LoadoutEntry>());
            for (int i = 0; i < 5; i++)
            {
                if (_selected[i] != -1) continue;
                if (available.Count == 0) break;
                int pick = UnityEngine.Random.Range(0, available.Count);
                _selected[i] = available[pick].id;
                available.RemoveAt(pick);
            }

            var ids = new int[5];
            for (int i = 0; i < 5; i++) ids[i] = _selected[i] != -1 ? _selected[i] : 0;

            Debug.Log($"[LoadoutPhase] SubmitConfirm ids=[{string.Join(",", ids)}] selected=[{string.Join(",", _selected)}] available={_available?.Length ?? 0}");

            NetworkManager.Instance?.EmitLoadoutConfirm(ids);
        }

        IEnumerator AutoConfirmDelayed(float delay)
        {
            _state = PhaseState.Active;
            yield return new WaitForSeconds(delay);
            SubmitConfirm();
        }

        IEnumerator HidePanelDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            HidePanel();
        }

        void HidePanel()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
        }

        static void EnsureEventSystem()
        {
            var existing = EventSystem.current;
            if (existing == null)
            {
                existing = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            }

            if (existing == null)
            {
                var go = new GameObject("LoadoutEventSystem");
                existing = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                go.AddComponent<SingleEventSystem>();
                Debug.Log("[LoadoutPhaseManager] Created fallback EventSystem for loadout UI.");
                return;
            }

            if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
                Debug.Log("[LoadoutPhaseManager] Reactivated inactive EventSystem for loadout UI.");
            }

            if (existing.GetComponent<BaseInputModule>() == null)
            {
                existing.gameObject.AddComponent<StandaloneInputModule>();
                Debug.Log("[LoadoutPhaseManager] Added missing StandaloneInputModule to existing EventSystem.");
            }

            if (existing.GetComponent<SingleEventSystem>() == null)
            {
                existing.gameObject.AddComponent<SingleEventSystem>();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Panel construction

        void BuildPanel()
        {
            // Find a Canvas in the same scene as this component.
            // FindFirstObjectByType searches all loaded scenes including the still-active
            // Loading scene, so we must filter to our own scene to avoid parenting the
            // panel to a Canvas that gets unloaded moments later.
            Canvas canvas = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.gameObject.scene == gameObject.scene) { canvas = c; break; }
            }
            if (canvas == null)
            {
                Debug.LogWarning("[LoadoutPhaseManager] No Canvas found in Loadout scene.");
                return;
            }

            // Root overlay
            _panelRoot = new GameObject("Panel_LoadoutPhase");
            _panelRoot.transform.SetParent(canvas.transform, false);
            var rootRT = _panelRoot.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0.015f, 0.03f);
            rootRT.anchorMax = new Vector2(0.985f, 0.97f);
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            var rootImg = _panelRoot.AddComponent<Image>();
            rootImg.color = new Color(0.05f, 0.05f, 0.1f, 0.95f);

            var vlg = _panelRoot.AddComponent<VerticalLayoutGroup>();
            vlg.spacing            = 10f;
            vlg.childAlignment     = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(12, 12, 12, 12);

            // Title
            _txtStatus = MakeLabel(_panelRoot.transform, "Txt_Title", "Choose Your Units", 22, Color.white,     preferredHeight: 32f);
            _txtTimer  = MakeLabel(_panelRoot.transform, "Txt_Timer",  "25s",              20, ColorTimerNormal, preferredHeight: 28f);
            var txtSub = MakeLabel(_panelRoot.transform, "Txt_Sub",    "Select up to 5 units — empty slots filled randomly", 14, new Color(0.8f, 0.8f, 0.8f), preferredHeight: 22f);
            _txtStatus = txtSub; // reuse sub-title as status line

            // Grid container — ScrollRect so cards don't overflow off-screen
            var scrollGO = new GameObject("Panel_Grid");
            scrollGO.transform.SetParent(_panelRoot.transform, false);
            var scrollLE = scrollGO.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollLE.flexibleWidth = 1f;
            var scrollImg = scrollGO.AddComponent<Image>();
            scrollImg.color = new Color(0f, 0f, 0f, 0f);
            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical   = true;
            scrollRect.scrollSensitivity = 30f;

            // Viewport (clips content)
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportGO.AddComponent<Image>().color = Color.white; // opaque required for Mask stencil
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            // Content (GridLayoutGroup + auto-size)
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            var grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(CardWidth, TotalCardHeight);
            grid.spacing         = new Vector2(CardSpacing, CardSpacing);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = CardColumns;
            grid.childAlignment  = TextAnchor.UpperCenter;
            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRT;
            scrollRect.content  = contentRT;
            _gridParent = contentGO.transform;

            // Spawn unit cards
            BuildCards();

            // Player readiness strip — horizontal row just above confirm button
            BuildPlayerPanel();

            // Confirm button
            _btnConfirm = MakeButton(_panelRoot.transform, "Btn_Confirm", "Confirm", 42f);
            _txtConfirmLabel = _btnConfirm.GetComponentInChildren<TMP_Text>();
            _btnConfirm.onClick.AddListener(SubmitConfirm);
            _btnConfirm.interactable = true;

            RefreshSlots();
            RefreshConfirmButton();
        }

        void BuildCards()
        {
            _cards.Clear();
            for (int i = 0; i < _available.Length; i++)
            {
                var entry = _available[i];
                var cardGO = new GameObject($"Card_{entry.key}");
                cardGO.transform.SetParent(_gridParent, false);
                var cardRT = cardGO.AddComponent<RectTransform>();
                cardRT.sizeDelta = new Vector2(CardWidth, TotalCardHeight);
                var cardImg = cardGO.AddComponent<Image>();
                cardImg.color = ColorUnselected;

                // Inner layout
                var vlg = cardGO.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 2f;
                vlg.childAlignment     = TextAnchor.MiddleCenter;
                vlg.childForceExpandWidth  = true;
                vlg.childForceExpandHeight = false;
                vlg.padding = new RectOffset(4, 4, 4, 4);

                var portraitFrame = new GameObject("PortraitFrame");
                portraitFrame.transform.SetParent(cardGO.transform, false);
                var portraitFrameImg = portraitFrame.AddComponent<Image>();
                portraitFrameImg.color = new Color(0.10f, 0.14f, 0.22f, 0.95f);
                var portraitFrameLE = portraitFrame.AddComponent<LayoutElement>();
                portraitFrameLE.preferredHeight = PortraitFrameHeight;

                var portraitGO = new GameObject("Portrait");
                portraitGO.transform.SetParent(portraitFrame.transform, false);
                var portraitRT = portraitGO.AddComponent<RectTransform>();
                portraitRT.anchorMin = Vector2.zero;
                portraitRT.anchorMax = Vector2.one;
                portraitRT.offsetMin = new Vector2(4f, 2f);
                portraitRT.offsetMax = new Vector2(-4f, -2f);
                var portraitRaw = portraitGO.AddComponent<RawImage>();
                portraitRaw.color = new Color(1f, 1f, 1f, 0f);
                portraitRaw.raycastTarget = false;
                var portraitFit = portraitGO.AddComponent<AspectRatioFitter>();
                portraitFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                portraitFit.aspectRatio = 1f;

                StartPortraitCapture(entry.key, portraitRaw);

                // Unit name
                var nameGO = new GameObject("Txt_Name");
                nameGO.transform.SetParent(cardGO.transform, false);
                var nameTxt = nameGO.AddComponent<TextMeshProUGUI>();
                nameTxt.text      = entry.name ?? entry.key;
                nameTxt.fontSize  = 14;
                nameTxt.color     = Color.white;
                nameTxt.alignment = TextAlignmentOptions.Center;
                nameTxt.fontStyle = FontStyles.Bold;
                var nameLE = nameGO.AddComponent<LayoutElement>();
                nameLE.preferredHeight = 20f;

                // Stats line
                var statsGO = new GameObject("Txt_Stats");
                statsGO.transform.SetParent(cardGO.transform, false);
                var statsTxt = statsGO.AddComponent<TextMeshProUGUI>();
                statsTxt.text     = $"HP {entry.hp}  {entry.send_cost}g  +{entry.income:0.#}/wave";
                statsTxt.fontSize = 11;
                statsTxt.color    = new Color(0.85f, 0.85f, 0.85f);
                statsTxt.alignment = TextAlignmentOptions.Center;
                var statsLE = statsGO.AddComponent<LayoutElement>();
                statsLE.preferredHeight = 18f;

                // Click button (transparent, covers card)
                var btnGO = new GameObject("Btn");
                btnGO.transform.SetParent(cardGO.transform, false);
                var btnRT = btnGO.AddComponent<RectTransform>();
                btnRT.anchorMin = Vector2.zero;
                btnRT.anchorMax = Vector2.one;
                btnRT.offsetMin = Vector2.zero;
                btnRT.offsetMax = Vector2.zero;
                var btnImg = btnGO.AddComponent<Image>();
                btnImg.color = Color.clear;
                var btnLE = btnGO.AddComponent<LayoutElement>();
                btnLE.ignoreLayout = true;
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                var capturedId   = entry.id;
                var capturedName = entry.name ?? entry.key;
                btn.onClick.AddListener(() =>
                {
                    if (_state == PhaseState.Active) ToggleUnit(capturedId, capturedName);
                });

                _cards.Add(cardGO);
            }
        }

        void StartPortraitCapture(string key, RawImage target)
        {
            if (target == null || string.IsNullOrEmpty(key)) return;
            if (_portraitCache.TryGetValue(key, out var cached) && cached != null)
            {
                target.texture = cached;
                target.color = Color.white;
                return;
            }

            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent != null && remoteContent.TryGetLoadedPortraitTexture(key, out var portrait) && portrait != null)
            {
                _portraitCache[key] = portrait;
                target.texture = portrait;
                target.color = Color.white;
                return;
            }

            target.texture = null;
            target.color = new Color(1f, 1f, 1f, 0f);

            if (!_pendingPortraitTargets.TryGetValue(key, out var targets))
            {
                targets = new List<RawImage>();
                _pendingPortraitTargets[key] = targets;
            }

            if (!targets.Contains(target))
                targets.Add(target);
        }

        void StartBackgroundWarmup()
        {
            _portraitWarmupRoutine    = StartCoroutine(WarmPortraitsInBackground());
            _environmentWarmupRoutine = StartCoroutine(WarmEnvironmentInBackground());
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
            if (remoteContent == null || _available == null || _available.Length == 0)
            {
                _portraitWarmupDone = true;
                TryEmitLoadoutReady();
                yield break;
            }

            var keys = new List<string>(_available.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _available.Length; i++)
            {
                string key = _available[i]?.key?.Trim();
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                    continue;

                keys.Add(key);
            }

            if (keys.Count == 0)
            {
                _portraitWarmupDone = true;
                TryEmitLoadoutReady();
                yield break;
            }

            yield return remoteContent.EnsurePortraitsReady(
                keys,
                requester: "LoadoutPhaseManager.BackgroundPortraitWarmup");

            RefreshPendingPortraits();
            _portraitWarmupRoutine = null;
            _portraitWarmupDone = true;
            TryEmitLoadoutReady();
        }

        IEnumerator WarmCriticalContentInBackground()
        {
            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                _criticalWarmupDone = true;
                TryEmitLoadoutReady();
                yield break;
            }

            yield return remoteContent.PreloadCriticalContentForSession(
                (progress, status) =>
                {
                    NetworkManager.Instance?.Emit("ml_content_progress",
                        new { percent = Mathf.Clamp01(progress * 0.5f), state = status ?? "Preparing loadout content" });
                    UpdatePrepOverlayStatus(status ?? "Preparing loadout content", Mathf.Clamp01(progress));
                },
                requester: "LoadoutPhaseManager.BackgroundGameplayWarmup");

            _criticalWarmupRoutine = null;
            _criticalWarmupDone = true;
            TryEmitLoadoutReady();
        }

        IEnumerator WarmEnvironmentInBackground()
        {
            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                _environmentWarmupRoutine = null;
                yield break;
            }

            yield return remoteContent.EnsureEnvironmentReady(
                RemoteContentManager.GameMlEnvironmentAddress,
                requester: "LoadoutPhaseManager.BackgroundEnvironmentWarmup");

            _environmentWarmupRoutine = null;
        }

        void TryEmitLoadoutReady()
        {
            if (_loadoutReadyEmitted) return;
            if (!_criticalWarmupDone) return;   // portraits are not blocking; they load during selection
            _loadoutReadyEmitted = true;
            NetworkManager.Instance?.Emit("ml_loadout_ready");
            NetworkManager.Instance?.Emit("ml_content_progress",
                new { percent = 0.5f, state = "Selecting units" });
            Debug.Log("[LoadoutPhase] ml_loadout_ready emitted");
            UpdatePrepOverlayStatus("Ready — waiting for loadout", 1f);
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
                if (!remoteContent.TryGetLoadedPortraitTexture(entry.Key, out var portrait) || portrait == null)
                    continue;

                _portraitCache[entry.Key] = portrait;
                foreach (var target in entry.Value)
                {
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

        float TotalCardHeight => CardHeight + PortraitFrameHeight + CardTextExtraHeight;

        // ─────────────────────────────────────────────────────────────────────
        // Preparation overlay (shown while downloading loadout-critical assets)

        void BuildPrepOverlay()
        {
            if (_prepOverlay != null) return;
            Canvas canvas = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.gameObject.scene == gameObject.scene) { canvas = c; break; }
            if (canvas == null) return;

            _prepOverlay = new GameObject("Panel_Prep");
            _prepOverlay.transform.SetParent(canvas.transform, false);
            var rt = _prepOverlay.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = _prepOverlay.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.08f, 0.97f);

            var vlg = _prepOverlay.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment     = TextAnchor.MiddleCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 14f;
            vlg.padding = new RectOffset(40, 40, 60, 60);

            _txtPrepStatus = MakeLabel(_prepOverlay.transform, "Txt_PrepStatus", "Preparing Match Assets", 24, Color.white, 36f);
            _txtPrepDetail = MakeLabel(_prepOverlay.transform, "Txt_PrepDetail", "Downloading content...", 16, new Color(0.75f, 0.75f, 0.75f), 26f);
        }

        void HidePrepOverlay()
        {
            if (_prepOverlay != null) _prepOverlay.SetActive(false);
        }

        void ShowPrepOverlay()
        {
            if (_prepOverlay == null) BuildPrepOverlay();
            if (_prepOverlay != null) _prepOverlay.SetActive(true);
        }

        void UpdatePrepOverlayStatus(string detail, float progress)
        {
            ShowPrepOverlay();
            if (_txtPrepDetail != null)
                _txtPrepDetail.text = detail;
        }

        void ShowWaitingForMatchOverlay()
        {
            ShowPrepOverlay();
            if (_txtPrepStatus != null) _txtPrepStatus.text = "Preparing Battlefield";
            if (_txtPrepDetail != null) _txtPrepDetail.text = "Waiting for players...";
            // Player panel is already built and visible from HandlePhaseStart — no rebuild needed.
        }

        // ─────────────────────────────────────────────────────────────────────
        // Per-player status panel

        void BuildPlayerPanel()
        {
            if (_playerPanelRoot != null) return;
            // If called before _panelRoot exists (shouldn't happen), skip.
            if (_panelRoot == null) return;

            _playerPanelRoot = new GameObject("Panel_Players", typeof(RectTransform));
            _playerPanelRoot.transform.SetParent(_panelRoot.transform, false);

            var le = _playerPanelRoot.AddComponent<LayoutElement>();
            le.minHeight       = 36f;
            le.preferredHeight = 36f;
            le.flexibleWidth   = 1f;

            var bg = _playerPanelRoot.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.07f, 0.13f, 0.9f);

            var hlg = _playerPanelRoot.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment         = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = false;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 4);

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

            var placeholderPlayers = new MLPlayerPreparationState[laneAssignments.Length];
            for (int i = 0; i < laneAssignments.Length; i++)
            {
                var lane = laneAssignments[i];
                placeholderPlayers[i] = new MLPlayerPreparationState
                {
                    laneIndex = lane.laneIndex,
                    displayName = string.IsNullOrWhiteSpace(lane.displayName) ? $"Lane {lane.laneIndex + 1}" : lane.displayName,
                    loadoutReady = lane.isAI,
                    gameplayReady = false,
                    contentPercent = lane.isAI ? 0.12f : 0.08f,
                    contentState = lane.isAI ? "Loading..." : "Preparing..."
                };
            }

            UpdatePlayerPanel(placeholderPlayers);
        }

        void UpdatePlayerPanel(MLPlayerPreparationState[] players)
        {
            if (_state == PhaseState.Done) return;
            if (_playerPanelRoot == null) return;

            int myLane = NetworkManager.Instance?.MyLaneIndex ?? -1;

            // Add missing columns (one per player, side by side)
            while (_playerRows.Count < players.Length)
            {
                var colGO = new GameObject($"Col_{_playerRows.Count}", typeof(RectTransform));
                colGO.transform.SetParent(_playerPanelRoot.transform, false);
                var colLE = colGO.AddComponent<LayoutElement>();
                colLE.minWidth = 110f;
                colLE.preferredWidth = 140f;
                colLE.flexibleWidth = 1f;
                colLE.minHeight = 28f;
                colLE.preferredHeight = 28f;
                colLE.flexibleHeight = 0f;
                var colBG = colGO.AddComponent<Image>();
                colBG.color = new Color(0.1f, 0.1f, 0.16f, 0.85f);
                var colVLG = colGO.AddComponent<VerticalLayoutGroup>();
                colVLG.childAlignment        = TextAnchor.MiddleCenter;
                colVLG.childForceExpandWidth = true;
                colVLG.childForceExpandHeight = false;
                colVLG.spacing = 1f;
                colVLG.padding = new RectOffset(6, 6, 3, 3);

                var nameTxt = MakeLabel(colGO.transform, "Txt_Name", "", 11, Color.white, 12f);
                nameTxt.alignment = TextAlignmentOptions.Center;

                // Thin progress bar
                var barBGGO = new GameObject("BarBG", typeof(RectTransform));
                barBGGO.transform.SetParent(colGO.transform, false);
                var barBGLE = barBGGO.AddComponent<LayoutElement>();
                barBGLE.preferredHeight = 4f;
                barBGLE.flexibleWidth   = 1f;
                barBGGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f);

                var barFillGO = new GameObject("BarFill", typeof(RectTransform));
                barFillGO.transform.SetParent(barBGGO.transform, false);
                var barFillRT = barFillGO.GetComponent<RectTransform>();
                barFillRT.anchorMin = Vector2.zero;
                barFillRT.anchorMax = new Vector2(0f, 1f);  // X updated per-frame
                barFillRT.offsetMin = Vector2.zero;
                barFillRT.offsetMax = Vector2.zero;
                var barFillImg = barFillGO.AddComponent<Image>();
                barFillImg.color = new Color(0.2f, 0.7f, 0.3f);

                var stateTxt = MakeLabel(colGO.transform, "Txt_State", "", 10, new Color(0.75f, 0.75f, 0.75f), 11f);
                stateTxt.alignment = TextAlignmentOptions.Center;

                _playerRows.Add((nameTxt, stateTxt, barFillImg));
            }

            for (int i = 0; i < players.Length && i < _playerRows.Count; i++)
            {
                var p   = players[i];
                var row = _playerRows[i];
                bool isMe = p.laneIndex == myLane;

                row.name.text  = isMe ? $"<b>{p.displayName}</b>" : p.displayName;
                row.name.color = isMe ? new Color(0.85f, 0.95f, 1f) : Color.white;

                string stateText;
                Color  stateColor;
                if (p.gameplayReady)
                {
                    stateText  = "Ready";
                    stateColor = new Color(0.3f, 0.9f, 0.4f);
                }
                else if (p.loadoutReady)
                {
                    stateText  = string.IsNullOrEmpty(p.contentState) ? "Downloading" : p.contentState;
                    stateColor = new Color(0.9f, 0.8f, 0.3f);
                }
                else
                {
                    stateText  = "Preparing...";
                    stateColor = new Color(0.6f, 0.6f, 0.6f);
                }

                row.state.text  = stateText;
                row.state.color = stateColor;

                if (row.bar != null)
                {
                    float pct = p.gameplayReady ? 1f : Mathf.Clamp01(p.contentPercent);
                    row.bar.rectTransform.anchorMax = new Vector2(pct, 1f);
                    row.bar.color = p.gameplayReady
                        ? new Color(0.2f, 0.7f, 0.3f)
                        : new Color(0.3f, 0.55f, 0.9f);
                }
            }
        }

        void RefreshFallbackPlayerPanel()
        {
            if (_playerPanelRoot == null) return;

            var nm = NetworkManager.Instance;
            if (nm == null) return;

            var authoritative = nm.LastPreparationState?.players;
            if (authoritative != null && authoritative.Length > 0) return;

            var laneAssignments = nm.LastMLMatchReady?.laneAssignments;
            if (laneAssignments == null || laneAssignments.Length == 0) return;

            int myLane = nm.MyLaneIndex;
            float aiPct = Mathf.Clamp01((Time.unscaledTime - _phaseStartTime) / 3f);
            var players = new MLPlayerPreparationState[laneAssignments.Length];
            for (int i = 0; i < laneAssignments.Length; i++)
            {
                var lane = laneAssignments[i];
                bool isMe = lane.laneIndex == myLane;
                bool loadoutReady = lane.isAI || _loadoutReadyEmitted;
                bool gameplayReady = lane.isAI && aiPct >= 0.99f;
                float pct;
                string state;

                if (lane.isAI)
                {
                    pct = gameplayReady ? 1f : Mathf.Max(0.12f, aiPct);
                    state = gameplayReady ? "Ready" : "Loading...";
                }
                else if (isMe)
                {
                    pct = _loadoutReadyEmitted ? 0.5f : 0.12f;
                    state = _loadoutReadyEmitted ? "Selecting units" : "Preparing...";
                }
                else if (_state == PhaseState.WaitingForMatch)
                {
                    pct = 0.5f;
                    state = "Waiting...";
                }
                else
                {
                    pct = 0.12f;
                    state = "Preparing...";
                }

                players[i] = new MLPlayerPreparationState
                {
                    laneIndex = lane.laneIndex,
                    displayName = string.IsNullOrWhiteSpace(lane.displayName) ? $"Lane {lane.laneIndex + 1}" : lane.displayName,
                    loadoutReady = loadoutReady,
                    gameplayReady = gameplayReady,
                    contentPercent = pct,
                    contentState = state
                };
            }

            UpdatePlayerPanel(players);
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI helpers

        static TMP_Text MakeLabel(Transform parent, string goName, string text, int fontSize, Color color, float preferredHeight = 24f)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = 1f;
            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.text      = text;
            txt.fontSize  = fontSize;
            txt.color     = color;
            txt.alignment = TextAlignmentOptions.Center;
            return txt;
        }

        static Button MakeButton(Transform parent, string goName, string label, float height)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.55f, 0.25f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var lblGO = new GameObject("Lbl");
            lblGO.transform.SetParent(go.transform, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
            var lbl = lblGO.AddComponent<TextMeshProUGUI>();
            lbl.text      = label;
            lbl.fontSize  = 16;
            lbl.color     = Color.white;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.fontStyle = FontStyles.Bold;
            return btn;
        }
    }
}
