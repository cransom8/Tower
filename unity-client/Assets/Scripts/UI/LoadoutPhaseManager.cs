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
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

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

        [Header("3D Portraits")]
        [Tooltip("Optional registry override for portrait captures in the Loadout scene.")]
        [SerializeField] UnitPrefabRegistry PortraitRegistry;

        // ── Phase state ───────────────────────────────────────────────────────
        enum PhaseState { Idle, Active, Confirming, Done }
        PhaseState _state = PhaseState.Idle;

        // ── Panel references (created at runtime) ─────────────────────────────
        GameObject     _panelRoot;
        TMP_Text       _txtTimer;
        TMP_Text       _txtStatus;
        Button         _btnConfirm;
        TMP_Text       _txtConfirmLabel;
        Transform      _gridParent;

        // ── Data ──────────────────────────────────────────────────────────────
        LoadoutEntry[]           _available;
        readonly int[]           _selected  = new int[5] { -1, -1, -1, -1, -1 };
        readonly string[]        _selNames  = new string[5];
        readonly List<GameObject> _cards    = new();
        readonly Dictionary<string, Texture2D> _portraitCache = new();
        readonly HashSet<string> _runtimePortraitKeys = new();
        readonly HashSet<string> _capturePending = new();
        readonly Queue<string> _captureQueue = new();
        UnitPortraitCamera _portraitCam;
        GameObject _portraitRoot;
        RenderTexture _portraitTexture;
        bool _isCapturingPortraits;
        float _timerRemaining;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()
        {
            EnsureEventSystem();
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLLoadoutPhaseStart += HandlePhaseStart;
            nm.OnMLLoadoutPhaseEnd   += HandlePhaseEnd;
            nm.OnMLMatchConfig       += HandleMatchConfig;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLLoadoutPhaseStart -= HandlePhaseStart;
            nm.OnMLLoadoutPhaseEnd   -= HandlePhaseEnd;
            nm.OnMLMatchConfig       -= HandleMatchConfig;
            if (_portraitRoot != null) Destroy(_portraitRoot);
            if (_portraitTexture != null)
            {
                _portraitTexture.Release();
                Destroy(_portraitTexture);
            }
            foreach (var key in _runtimePortraitKeys)
                if (_portraitCache.TryGetValue(key, out var tex) && tex != null) Destroy(tex);
            _portraitCache.Clear();
            _runtimePortraitKeys.Clear();
            _capturePending.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            // ml_loadout_phase_start may have fired before this scene finished
            // loading (race condition).  NetworkManager caches it so we can pick
            // it up here on the first frame after scene init.
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            var pending = nm.PendingLoadoutPhase;
            if (pending != null && _state == PhaseState.Idle)
                HandlePhaseStart(pending);
        }

        void HandlePhaseStart(MLLoadoutPhaseStartPayload payload)
        {
            _available = payload.availableUnits ?? Array.Empty<LoadoutEntry>();
            _timerRemaining = Mathf.Max(1f, payload.timeoutSeconds);

            Array.Fill(_selected, -1);
            Array.Fill(_selNames, null);
            _cards.Clear();

            if (_panelRoot != null) Destroy(_panelRoot);
            BuildPanel();

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
            // Loadout resolved — leave the Loadout scene and enter the game
            _state = PhaseState.Done;
            if (_txtStatus != null) _txtStatus.text = "Loading game...";
            if (_btnConfirm != null) _btnConfirm.interactable = false;
            LoadingScreen.LoadScene("Game_ML");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Timer
        void Update()
        {
            if (_state != PhaseState.Active) return;
            _timerRemaining -= Time.deltaTime;
            if (_txtTimer != null)
            {
                int secs = Mathf.CeilToInt(_timerRemaining);
                _txtTimer.text  = secs > 0 ? $"{secs}s" : "0s";
                _txtTimer.color = _timerRemaining < 5f ? ColorTimerUrgent : ColorTimerNormal;
            }
            if (_timerRemaining <= 0f) SubmitConfirm();
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
                Debug.Log("[LoadoutPhaseManager] Created fallback EventSystem for loadout UI.");
                return;
            }

            if (existing.GetComponent<BaseInputModule>() == null)
            {
                existing.gameObject.AddComponent<StandaloneInputModule>();
                Debug.Log("[LoadoutPhaseManager] Added missing StandaloneInputModule to existing EventSystem.");
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
            EnsurePortraitCamera();
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
            if (_portraitCam == null || target == null || string.IsNullOrEmpty(key)) return;
            if (_portraitCache.TryGetValue(key, out var cached) && cached != null)
            {
                target.texture = cached;
                target.color = Color.white;
                return;
            }

            if (UnitPortraitResources.TryLoad(key, out var baked))
            {
                _portraitCache[key] = baked;
                target.texture = baked;
                target.color = Color.white;
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return;
#endif

            target.texture = null;
            target.color = new Color(1f, 1f, 1f, 0f);

            if (_capturePending.Contains(key)) return;
            _capturePending.Add(key);
            _captureQueue.Enqueue(key);
            if (!_isCapturingPortraits)
                StartCoroutine(ProcessPortraitQueue());
        }

        IEnumerator ProcessPortraitQueue()
        {
            _isCapturingPortraits = true;
            while (_captureQueue.Count > 0)
            {
                var key = _captureQueue.Dequeue();
                bool done = false;
                Texture2D captured = null;
                _portraitCam.StartIconCapture(key, tex =>
                {
                    captured = tex;
                    done = true;
                });
                while (!done) yield return null;

                _capturePending.Remove(key);
                if (captured == null) continue;
                _portraitCache[key] = captured;
                _runtimePortraitKeys.Add(key);
                ApplyPortraitToCards(key, captured);
            }
            _isCapturingPortraits = false;
        }

        void ApplyPortraitToCards(string key, Texture2D texture)
        {
            foreach (var card in _cards)
            {
                if (card == null || !card.name.EndsWith(key, StringComparison.Ordinal)) continue;
                var raw = card.transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>();
                if (raw == null) continue;
                raw.texture = texture;
                raw.color = Color.white;
            }
        }

        void EnsurePortraitCamera()
        {
            if (_portraitCam != null && _portraitCam.Registry != null) return;
            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry == null) return;
            if (_portraitCam == null)
                _portraitCam = RuntimePortraitStudio.Create("LoadoutPhasePortraitStudio", registry, out _portraitRoot, out _portraitTexture);
            else
                _portraitCam.Registry = registry;
        }

        float TotalCardHeight => CardHeight + PortraitFrameHeight + CardTextExtraHeight;

        // ─────────────────────────────────────────────────────────────────────
        // UI helpers

        static TMP_Text MakeLabel(Transform parent, string goName, string text, int fontSize, Color color, float preferredHeight = 24f)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
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
