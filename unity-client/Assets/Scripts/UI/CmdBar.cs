// CmdBar.cs — Bottom command bar: 5 unit send buttons (each with embedded AUTO strip)
//
// SETUP (Game_ML.unity):
//   Run "Castle Defender → Setup → Rebuild CmdBar Buttons" after any script change.
//
//   Canvas
//   └── CmdBar (RectTransform anchored left, VerticalLayoutGroup)
//       ├── Btn_Wall       (kept in hierarchy but hidden at runtime — no-op in wave defense)
//       ├── Btn_Unit0 … Btn_Unit4
//       │     ├── Label        (TMP_Text  — unit name + cost)
//       │     ├── QueueCount   (TMP_Text  — "×N" badge, top-right, hidden when 0)
//       │     └── AutoStrip    (Button + Image — tappable AUTO on/off at bottom of button)
//       │           └── AutoLabel (TMP_Text — "AUTO" / "AUTO ✓")
//       └── QueueDrainBar  (repurposed as build-phase countdown — fills during BUILD, hidden otherwise)
//
// Tap main button area → send one unit (spawn_unit, queued as pendingSend until combat starts).
// Tap AUTO strip       → toggle autosend on/off for that unit.
//   • Send buttons are interactive during BUILD and COMBAT phases.
//   • Autosend is globally enabled whenever ≥1 unit has AUTO on.
//   • Server fill priority = loadoutKeys order.

using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class CmdBar : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Unit Buttons (Runner … Golem)")]
        public Button[]   UnitButtons;       // main send button per unit

        [Header("Per-Unit AUTO Strip")]
        public Button[]   AutoToggleButtons; // small strip at bottom of each unit button
        public TMP_Text[] AutoToggleTxts;    // label inside each strip

        [Header("Queue Count Badges")]
        public TMP_Text[] QueueCountLabels;  // top-right badge "×N" per unit button

        [Header("Send Queue Drain Bar")]
        [Tooltip("Image (Filled/Horizontal) driven by queue_update.drainProgress.")]
        public Image QueueDrainBar;

        [Header("Lane Return")]
        public Button ReturnToLaneButton;

        [Header("Colors")]
        public Color ColorWallOn   = new Color(0.2f,  0.7f,  0.6f,  1f);
        public Color ColorAutoOn   = new Color(0.2f,  0.7f,  0.6f,  1f);
        public Color ColorAutoOff  = new Color(0.14f, 0.12f, 0.10f, 0.92f);
        public Color ColorPhaseOff = new Color(0.35f, 0.35f, 0.35f, 0.50f); // buttons during combat/transition
        public Color ColorReturnLane = new Color(0.10f, 0.58f, 0.52f, 0.98f);

        [Header("Responsive Layout")]
        [Range(0.10f, 0.35f)] public float WidthPercentOfScreen = 0.16f;
        public float MinExpandedWidth = 152f;
        public float MaxExpandedWidth = 228f;
        public float CollapsedWidth = 20f;
        public float CollapseToggleWidth = 26f;
        public float CollapseToggleHeight = 116f;
        public float CollapseToggleInset = 6f;
        public float CollapseAnimDuration = 0.18f;

        [Header("Mobile Layout")]
        [Range(0.08f, 0.30f)] public float MobileWidthPercentOfSafeArea = 0.13f;
        public float MobileMinExpandedWidth = 118f;
        public float MobileMaxExpandedWidth = 180f;
        public float MobileCollapsedWidth = 24f;
        public float MobileCollapseToggleWidth = 38f;
        public float MobileCollapseToggleHeight = 132f;
        public float MobileCollapseToggleInset = 4f;
        public bool RespectSafeArea = true;
        public float SmallScreenThreshold = 1100f;
        public float MobileMinButtonHeight = 80f;
        public float DesktopMinButtonHeight = 128f;
        public float MobileVerticalSpacing = 4f;
        public float DesktopVerticalSpacing = 12f;
        public int MobileTopBottomPadding = 4;
        public int DesktopTopBottomPadding = 10;
        [Range(0.60f, 1.00f)] public float MobileVerticalUsagePercent = 0.84f;
        [Range(0.60f, 1.00f)] public float DesktopVerticalUsagePercent = 0.98f;

        // ── Static state ──────────────────────────────────────────────────────

        [Header("3D Portraits")]
        [Tooltip("Optional portrait camera. If omitted, CmdBar creates a hidden runtime portrait studio.")]
        [SerializeField] UnitPortraitCamera PortraitCam;
        [Tooltip("Optional registry override for portrait captures. Falls back to LaneRenderer/TileGrid.")]
        [SerializeField] UnitPrefabRegistry PortraitRegistry;

        // ── Fallbacks ─────────────────────────────────────────────────────────
        static readonly string[] FallbackUnitKeys  = { "goblin", "kobold", "hobgoblin", "orc", "ogre" };
        static readonly int[]    FallbackUnitCosts = { 1, 1, 2, 3, 4 };
        static readonly Vector3 LeftSideAnchor = new Vector3(-43.5f, 1f, 0f);
        static readonly Vector3 RightSideAnchor = new Vector3(43.5f, 1f, 0f);

        // ── Catalog-driven state ──────────────────────────────────────────────
        string[] _unitKeys;
        int[]    _unitCosts;
        readonly Dictionary<string, Texture2D> _portraitCache = new();
        readonly HashSet<string> _capturePending = new();
        readonly Queue<string> _captureQueue = new();
        readonly List<RawImage> _buttonPortraits = new();
        RenderTexture _runtimePortraitTexture;
        GameObject _runtimePortraitRoot;
        bool _isCapturingPortraits;
        CameraController _cameraController;
        int _laneCycleStep;
        RectTransform _rectTransform;
        RectTransform _parentRect;
        Button _collapseButton;
        TMP_Text _collapseButtonLabel;
        Coroutine _collapseRoutine;
        Vector2 _lastParentSize = new(-1f, -1f);
        Rect _lastSafeArea = new(-1f, -1f, -1f, -1f);
        bool _isCollapsed;
        VerticalLayoutGroup _layoutGroup;
        readonly List<LayoutElement> _unitButtonLayouts = new();
        readonly List<float> _unitButtonBaseHeights = new();

        // ── Autosend state ────────────────────────────────────────────────────
        Dictionary<string, bool> _autoUnits = new();

        // Derived: true if any unit has auto enabled
        bool AnyAutoEnabled
        {
            get
            {
                foreach (var v in _autoUnits.Values)
                    if (v) return true;
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            _rectTransform = GetComponent<RectTransform>();
            _parentRect = transform.parent as RectTransform;
            _layoutGroup = GetComponent<VerticalLayoutGroup>();
            CacheButtonLayoutElements();
            EnsureViewportMask();
            EnsureCollapseToggle();
            ApplyResponsiveLayout(true);

            _cameraController = FindFirstObjectByType<CameraController>();
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnQueueUpdate   += HandleQueueUpdate;
                NetworkManager.Instance.OnMLMatchConfig += HandleMatchConfig;
            }

            // Apply cached loadout immediately — the per-player ml_match_config typically
            // arrives before Game_ML finishes loading, so OnMLMatchConfig fires with no
            // subscribers yet.  NetworkManager caches it since it's DontDestroyOnLoad.
            var cachedLoadout = NetworkManager.Instance?.LastMatchLoadout;
            if (cachedLoadout != null && cachedLoadout.Length > 0)
            {
                ApplyMatchLoadout(cachedLoadout);
            }
            else if (CatalogLoader.IsReady)
            {
                ApplyCatalog();
            }
            else
            {
                CatalogLoader.OnCatalogReady += ApplyCatalog;
            }

            HideLegacyUnitButtonIcons();
            EnsurePortraitSlots();
            RefreshButtonPortraits();

            // Wire unit buttons
            for (int i = 0; i < UnitButtons.Length; i++)
            {
                int idx = i;
                UnitButtons[i].onClick.AddListener(() => OnUnitClick(idx));

                // Per-unit AUTO strip
                if (AutoToggleButtons != null && idx < AutoToggleButtons.Length
                    && AutoToggleButtons[idx] != null)
                    AutoToggleButtons[idx].onClick.AddListener(() => OnAutoUnitToggle(idx));

                // Queue count label — hidden until units are queued
                if (QueueCountLabels != null && idx < QueueCountLabels.Length
                    && QueueCountLabels[idx] != null)
                {
                    QueueCountLabels[idx].text = "";
                    QueueCountLabels[idx].gameObject.SetActive(false);
                }
            }

            if (QueueDrainBar != null) QueueDrainBar.fillAmount = 0f;

            _laneCycleStep = 0;
            EnsureReturnToLaneButton();
            RefreshAllAutoStrips();
        }

        void OnDestroy()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnQueueUpdate   -= HandleQueueUpdate;
                NetworkManager.Instance.OnMLMatchConfig -= HandleMatchConfig;
            }
            if (_runtimePortraitRoot != null) Destroy(_runtimePortraitRoot);
            if (_runtimePortraitTexture != null)
            {
                _runtimePortraitTexture.Release();
                Destroy(_runtimePortraitTexture);
            }
            foreach (var tex in _portraitCache.Values)
            {
                if (tex != null) Destroy(tex);
            }
            _portraitCache.Clear();
        }

        // ── Queue update ──────────────────────────────────────────────────────
        void HandleQueueUpdate(QueueUpdatePayload p)
        {
            if (p == null || _unitKeys == null) return;

            var counts = p.queues ?? new Dictionary<string, int>();

            for (int i = 0; i < UnitButtons.Length && i < _unitKeys.Length; i++)
            {
                if (QueueCountLabels == null || i >= QueueCountLabels.Length
                    || QueueCountLabels[i] == null) continue;

                int count = counts.TryGetValue(_unitKeys[i], out int c) ? c : 0;
                if (count > 0)
                {
                    QueueCountLabels[i].text = $"×{count}";
                    QueueCountLabels[i].gameObject.SetActive(true);
                }
                else
                {
                    QueueCountLabels[i].text = "";
                    QueueCountLabels[i].gameObject.SetActive(false);
                }
            }

            if (QueueDrainBar != null)
                QueueDrainBar.fillAmount = Mathf.Clamp01(p.drainProgress);
        }

        // ── Match config / catalog ────────────────────────────────────────────
        void HandleMatchConfig(MLMatchConfig config)
        {
            if (config?.loadout == null || config.loadout.Length == 0) return;
            ApplyMatchLoadout(config.loadout);
        }

        void ApplyMatchLoadout(LoadoutEntry[] loadout)
        {
            int count = Mathf.Min(loadout.Length, UnitButtons.Length);
            _unitKeys  = new string[count];
            _unitCosts = new int[count];
            for (int i = 0; i < count; i++)
            {
                _unitKeys[i]  = loadout[i].key;
                _unitCosts[i] = loadout[i].send_cost;

                // Update visible label on the button (child TMP_Text named "Label")
                if (i < UnitButtons.Length && UnitButtons[i] != null)
                {
                    var lbl = UnitButtons[i].transform.Find("Label")?.GetComponent<TMPro.TMP_Text>();
                    if (lbl != null)
                        lbl.text = $"{loadout[i].name}\n{loadout[i].send_cost}g";
                }
            }
            RebuildAutoDict();
            ResetPortraitState();
            for (int i = 0; i < UnitButtons.Length; i++)
                UnitButtons[i].gameObject.SetActive(i < count);
            ApplyResponsiveLayout(true);
            Debug.Log($"[CmdBar] Match loadout applied — {count} units");
        }

        void ApplyCatalog()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;
            var units = CatalogLoader.Units.Count > 0 ? CatalogLoader.Units : null;
            int count = units != null ? Mathf.Min(units.Count, UnitButtons.Length) : FallbackUnitKeys.Length;
            _unitKeys  = new string[count];
            _unitCosts = new int[count];
            for (int i = 0; i < count; i++)
            {
                _unitKeys[i]  = units != null ? units[i].key       : FallbackUnitKeys[i];
                _unitCosts[i] = units != null ? units[i].send_cost : FallbackUnitCosts[i];
            }
            RebuildAutoDict();
            ResetPortraitState();
            for (int i = 0; i < UnitButtons.Length; i++)
                UnitButtons[i].gameObject.SetActive(i < count);
            ApplyResponsiveLayout(true);
            Debug.Log($"[CmdBar] Catalog applied — {count} unit types");
        }

        void RebuildAutoDict()
        {
            var newAuto = new Dictionary<string, bool>();
            foreach (var key in _unitKeys)
                newAuto[key] = _autoUnits.ContainsKey(key) && _autoUnits[key];
            _autoUnits = newAuto;
        }

        void ResetPortraitState()
        {
            _portraitCache.Clear();
            _capturePending.Clear();
            _captureQueue.Clear();

            if (_buttonPortraits.Count == 0) EnsurePortraitSlots();
            for (int i = 0; i < _buttonPortraits.Count; i++)
            {
                var raw = _buttonPortraits[i];
                if (raw != null)
                {
                    raw.texture = null;
                    raw.color = new Color(1f, 1f, 1f, 0f);
                }

                var icon = UnitButtons != null && i < UnitButtons.Length
                    ? UnitButtons[i].transform.Find("Icon")?.GetComponent<Image>()
                    : null;
                if (icon != null) icon.color = Color.white;
            }
        }

        void HideLegacyUnitButtonIcons()
        {
            if (UnitButtons == null) return;
            for (int i = 0; i < UnitButtons.Length; i++)
            {
                var icon = UnitButtons[i] != null
                    ? UnitButtons[i].transform.Find("Icon")?.GetComponent<Image>()
                    : null;
                if (icon == null) continue;

                icon.sprite = null;
                icon.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        void EnsurePortraitSlots()
        {
            _buttonPortraits.Clear();
            if (UnitButtons == null) return;
            for (int i = 0; i < UnitButtons.Length; i++)
                _buttonPortraits.Add(EnsurePortraitImage(UnitButtons[i]));
        }

        RawImage EnsurePortraitImage(Button btn)
        {
            if (btn == null) return null;
            var existing = btn.transform.Find("Portrait")?.GetComponent<RawImage>();
            if (existing != null) return existing;
            var existingInFrame = btn.transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>();
            if (existingInFrame != null) return existingInFrame;

            var frameRect = btn.transform.Find("IconBg") as RectTransform;
            var iconRect = btn.transform.Find("Icon") as RectTransform;
            var referenceRect = frameRect != null ? frameRect : iconRect;
            var frameGO = new GameObject("PortraitFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            frameGO.transform.SetParent(btn.transform, false);
            var frameRt = frameGO.GetComponent<RectTransform>();
            frameRt.pivot = new Vector2(0.5f, 0.5f);

            if (referenceRect != null)
            {
                frameRt.anchorMin = referenceRect.anchorMin;
                frameRt.anchorMax = referenceRect.anchorMax;
                frameRt.anchoredPosition = referenceRect.anchoredPosition;
                frameRt.sizeDelta = referenceRect.sizeDelta;
                frameRt.offsetMin = referenceRect.offsetMin + new Vector2(2f, 2f);
                frameRt.offsetMax = referenceRect.offsetMax + new Vector2(-2f, -2f);
                frameGO.transform.SetSiblingIndex(referenceRect.GetSiblingIndex() + 1);
            }
            else
            {
                frameRt.anchorMin = new Vector2(0.10f, 0.24f);
                frameRt.anchorMax = new Vector2(0.90f, 0.72f);
                frameRt.offsetMin = Vector2.zero;
                frameRt.offsetMax = Vector2.zero;
            }

            var frameImage = frameGO.GetComponent<Image>();
            frameImage.color = new Color(0.08f, 0.12f, 0.20f, 0.96f);
            var mask = frameGO.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            var portraitGO = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            portraitGO.transform.SetParent(frameGO.transform, false);
            var portraitRect = portraitGO.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(2f, 2f);
            portraitRect.offsetMax = new Vector2(-2f, -2f);
            portraitRect.pivot = new Vector2(0.5f, 0.5f);

            var raw = portraitGO.GetComponent<RawImage>();
            raw.color = new Color(1f, 1f, 1f, 0f);
            raw.raycastTarget = false;
            var fitter = portraitGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1f;
            return raw;
        }

        void RefreshButtonPortraits()
        {
            if (_buttonPortraits.Count == 0) EnsurePortraitSlots();
            for (int i = 0; i < _buttonPortraits.Count; i++)
            {
                var raw = _buttonPortraits[i];
                if (raw == null) continue;

                string key = (_unitKeys != null && i < _unitKeys.Length) ? _unitKeys[i] : null;
                if (string.IsNullOrEmpty(key))
                {
                    raw.texture = null;
                    raw.color = new Color(1f, 1f, 1f, 0f);
                    var emptyIcon = UnitButtons != null && i < UnitButtons.Length
                        ? UnitButtons[i].transform.Find("Icon")?.GetComponent<Image>()
                        : null;
                    if (emptyIcon != null) emptyIcon.color = Color.white;
                    continue;
                }

                if (_portraitCache.TryGetValue(key, out var tex) && tex != null)
                {
                    ApplyPortraitToButton(i, tex);
                    continue;
                }

                raw.texture = null;
                raw.color = new Color(1f, 1f, 1f, 0f);
                var icon = UnitButtons != null && i < UnitButtons.Length
                    ? UnitButtons[i].transform.Find("Icon")?.GetComponent<Image>()
                    : null;
                if (icon != null) icon.color = Color.white;
                StartPortraitCapture(key);
            }
        }

        void ApplyPortraitToButton(int index, Texture texture)
        {
            if (index < 0 || index >= _buttonPortraits.Count) return;
            var raw = _buttonPortraits[index];
            if (raw == null) return;

            raw.texture = texture;
            raw.color = Color.white;

            var icon = UnitButtons != null && index < UnitButtons.Length
                ? UnitButtons[index].transform.Find("Icon")?.GetComponent<Image>()
                : null;
            if (icon != null) icon.color = new Color(1f, 1f, 1f, 0f);
        }

        void StartPortraitCapture(string key)
        {
            if (string.IsNullOrEmpty(key) || _capturePending.Contains(key)) return;
            var portraitCam = EnsurePortraitCamera();
            if (portraitCam == null) return;

            _capturePending.Add(key);
            _captureQueue.Enqueue(key);
            if (!_isCapturingPortraits)
                StartCoroutine(ProcessPortraitQueue(portraitCam));
        }

        IEnumerator ProcessPortraitQueue(UnitPortraitCamera portraitCam)
        {
            _isCapturingPortraits = true;
            while (_captureQueue.Count > 0)
            {
                var key = _captureQueue.Dequeue();
                yield return EnsureRemotePortraitPrefabReady(key);

                bool done = false;
                Texture2D captured = null;
                portraitCam.StartIconCapture(key, tex =>
                {
                    captured = tex;
                    done = true;
                });
                while (!done) yield return null;

                _capturePending.Remove(key);
                if (captured == null || _unitKeys == null) continue;

                _portraitCache[key] = captured;
                for (int i = 0; i < _unitKeys.Length; i++)
                {
                    if (_unitKeys[i] == key) ApplyPortraitToButton(i, captured);
                }
            }
            _isCapturingPortraits = false;
        }

        IEnumerator EnsureRemotePortraitPrefabReady(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                yield break;

            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null)
                yield break;

            if (remoteContent.TryGetLoadedPrefabForUnit(key, out var loadedPrefab) && loadedPrefab != null)
                yield break;

            Debug.LogWarning($"[CmdBar] Unit prefab '{key}' was not preloaded before portrait capture. CmdBar will not trigger a first-time remote download from this UI path.");
        }

        UnitPortraitCamera EnsurePortraitCamera()
        {
            if (PortraitCam != null && PortraitCam.Registry != null) return PortraitCam;

            var registry = RuntimePortraitStudio.ResolveRegistry(PortraitRegistry);
            if (registry == null) return null;

            if (PortraitCam != null)
            {
                PortraitCam.Registry = registry;
                return PortraitCam;
            }

            if (_runtimePortraitRoot == null)
            {
                PortraitCam = RuntimePortraitStudio.Create("CmdBarPortraitStudio", registry, out _runtimePortraitRoot, out _runtimePortraitTexture);
            }

            PortraitCam.Registry = registry;
            PortraitCam.transform.position = new Vector3(0f, 0f, 50f);
            return PortraitCam;
        }

        void Update()
        {
            ApplyResponsiveLayout(false);

            var sa   = SnapshotApplier.Instance;
            var lane = sa?.MyLane;
            var snap = sa?.LatestML;
            RefreshButtonPortraits();
            RefreshReturnToLaneButton();

            if (lane == null || _unitCosts == null || UnitButtons.Length == 0) return;
            if (!UnitButtons[0].gameObject.activeSelf) return;

            // Send buttons are usable during build and combat phases.
            // When snap is null the match hasn't started — keep buttons disabled.
            bool canSend = snap != null && (snap.roundState == "build" || snap.roundState == "combat");

            for (int i = 0; i < UnitButtons.Length && i < _unitCosts.Length; i++)
            {
                bool canAfford = lane.gold >= _unitCosts[i];
                bool active    = !_isCollapsed && canSend && canAfford;
                UnitButtons[i].interactable  = active;
                UnitButtons[i].image.color   = active ? Color.white : (canSend ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : ColorPhaseOff);
            }

            for (int i = 0; i < (AutoToggleButtons?.Length ?? 0); i++)
            {
                if (AutoToggleButtons[i] != null)
                    AutoToggleButtons[i].interactable = !_isCollapsed;
            }

            // Repurpose QueueDrainBar as build-phase countdown (drains as time runs out)
            if (QueueDrainBar != null)
            {
                bool showBar = snap != null && snap.roundState == "build" && snap.buildPhaseTotal > 0;
                if (QueueDrainBar.gameObject.activeSelf != showBar)
                    QueueDrainBar.gameObject.SetActive(showBar);
                if (showBar)
                    QueueDrainBar.fillAmount = Mathf.Clamp01((float)snap.roundStateTicks / snap.buildPhaseTotal);
            }
        }

        // ── Click handlers ────────────────────────────────────────────────────

        void EnsureReturnToLaneButton()
        {
            if (ReturnToLaneButton != null)
            {
                ReturnToLaneButton.onClick.RemoveListener(OnReturnToLaneClicked);
                ReturnToLaneButton.onClick.AddListener(OnReturnToLaneClicked);
                return;
            }

            var go = new GameObject("Btn_ReturnToLane", typeof(RectTransform), typeof(Image), typeof(Button));
            var parent = transform.parent != null ? transform.parent : transform;
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(190f, 44f);
            rt.anchoredPosition = new Vector2(-14f, -14f);
            rt.SetAsLastSibling();

            var image = go.GetComponent<Image>();
            image.color = ColorReturnLane;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color(0.16f, 0.70f, 0.62f, 1f);
            colors.pressedColor = new Color(0.08f, 0.42f, 0.37f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.55f);
            button.colors = colors;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(OnReturnToLaneClicked);

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.10f, 0.10f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);

            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(8f, 4f);
            labelRt.offsetMax = new Vector2(-8f, -4f);

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "Cycle Lanes";
            tmp.fontSize = 22f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;

            ReturnToLaneButton = button;
        }

        void RefreshReturnToLaneButton()
        {
            if (ReturnToLaneButton == null)
                return;

            bool show = !_isCollapsed && HasAnyCycleTargets();
            ReturnToLaneButton.gameObject.SetActive(show);
            ReturnToLaneButton.interactable = show;
        }

        int GetMyBranchConfigIndex()
        {
            var sa = SnapshotApplier.Instance;
            if (sa == null)
                return Mathf.Clamp(sa != null ? sa.MyLaneIndex : 0, 0, 3);

            var assignment = sa.GetLaneAssignment(sa.MyLaneIndex);
            int branchCfg = TileGrid.GetBranchConfigIndex(assignment?.branchId);
            if (branchCfg >= 0)
                return branchCfg;

            var lane = sa.GetLane(sa.MyLaneIndex);
            branchCfg = TileGrid.GetBranchConfigIndex(lane?.branchId);
            if (branchCfg >= 0)
                return branchCfg;

            return Mathf.Clamp(sa.MyLaneIndex, 0, 3);
        }

        int GetLaneIndexForBranch(int branchCfg)
        {
            var sa = SnapshotApplier.Instance;
            if (sa == null)
                return -1;

            var assignments = sa.LatestMLMatchReady?.laneAssignments;
            if (assignments != null)
            {
                for (int i = 0; i < assignments.Length; i++)
                {
                    var assignment = assignments[i];
                    if (assignment != null && TileGrid.GetBranchConfigIndex(assignment.branchId) == branchCfg)
                        return assignment.laneIndex;
                }
            }

            var snap = sa.LatestML;
            if (snap?.lanes != null)
            {
                for (int i = 0; i < snap.lanes.Length; i++)
                {
                    var lane = snap.lanes[i];
                    if (lane != null && TileGrid.GetBranchConfigIndex(lane.branchId) == branchCfg)
                        return lane.laneIndex;
                }
            }

            return -1;
        }

        bool HasAnyCycleTargets()
        {
            return GetMyBranchConfigIndex() >= 0;
        }

        void FocusBranch(int branchCfg)
        {
            var sa = SnapshotApplier.Instance;
            if (_cameraController == null)
                _cameraController = FindFirstObjectByType<CameraController>();

            int laneIndex = GetLaneIndexForBranch(branchCfg);
            if (sa != null && laneIndex >= 0)
                sa.ViewingLane = laneIndex;

            if (_cameraController != null)
            {
                Vector3 p = TileGrid.TileToWorld(branchCfg, 5, 14);
                _cameraController.PanTo(new Vector3(p.x, _cameraController.CameraTarget.position.y, p.z));
            }
        }

        void FocusMergePoint(bool leftSide)
        {
            if (_cameraController == null)
                _cameraController = FindFirstObjectByType<CameraController>();
            if (_cameraController == null || _cameraController.CameraTarget == null)
                return;

            Vector3 p = leftSide ? LeftSideAnchor : RightSideAnchor;
            _cameraController.PanTo(new Vector3(p.x, _cameraController.CameraTarget.position.y, p.z));
        }

        void OnReturnToLaneClicked()
        {
            int myBranch = GetMyBranchConfigIndex();
            if (myBranch < 0)
                return;

            int opponentAcrossBranch = (myBranch + 2) % 4;
            bool mySideIsLeft = myBranch < 2;
            int totalLanes = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.TotalLanes : 0;

            if (totalLanes <= 2)
            {
                _laneCycleStep = (_laneCycleStep + 1) % 4;
                switch (_laneCycleStep)
                {
                    case 0:
                        FocusBranch(myBranch);
                        break;
                    case 1:
                        FocusMergePoint(mySideIsLeft);
                        break;
                    case 2:
                        FocusBranch(opponentAcrossBranch);
                        break;
                    default:
                        FocusMergePoint(!mySideIsLeft);
                        break;
                }
                return;
            }

            int teammateBranch = myBranch ^ 1;
            int oppositeTeammateBranch = (teammateBranch + 2) % 4;

            _laneCycleStep = (_laneCycleStep + 1) % 6;
            switch (_laneCycleStep)
            {
                case 0:
                    FocusBranch(myBranch);
                    break;
                case 1:
                    FocusMergePoint(mySideIsLeft);
                    break;
                case 2:
                    FocusBranch(teammateBranch);
                    break;
                case 3:
                    FocusBranch(oppositeTeammateBranch);
                    break;
                case 4:
                    FocusMergePoint(!mySideIsLeft);
                    break;
                default:
                    FocusBranch(opponentAcrossBranch);
                    break;
            }
        }

        void OnUnitClick(int idx)
        {
            if (_unitKeys == null || idx >= _unitKeys.Length) return;
            ActionSender.SpawnUnit(_unitKeys[idx]);
            StartCoroutine(PunchScale(UnitButtons[idx].transform, 0.9f, 0.07f));
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void OnAutoUnitToggle(int idx)
        {
            if (_unitKeys == null || idx >= _unitKeys.Length) return;
            string key = _unitKeys[idx];
            _autoUnits[key] = !_autoUnits[key];
            RefreshAutoStrip(idx);
            SyncAutosend();
            AudioManager.I?.Play(AudioManager.SFX.AutosendToggle);
        }

        // ── Visual refresh ────────────────────────────────────────────────────
        void RefreshAutoStrip(int idx)
        {
            if (AutoToggleButtons == null || idx >= AutoToggleButtons.Length
                || AutoToggleButtons[idx] == null) return;

            string key = (_unitKeys != null && idx < _unitKeys.Length) ? _unitKeys[idx] : "";
            bool on = _autoUnits.TryGetValue(key, out bool v) && v;

            AutoToggleButtons[idx].image.color = on ? ColorAutoOn : ColorAutoOff;

            if (AutoToggleTxts != null && idx < AutoToggleTxts.Length && AutoToggleTxts[idx] != null)
                AutoToggleTxts[idx].text = on ? "AUTO ✓" : "AUTO";
        }

        void RefreshAllAutoStrips()
        {
            for (int i = 0; i < (AutoToggleButtons?.Length ?? 0); i++)
                RefreshAutoStrip(i);
        }

        // ── Sync to server ────────────────────────────────────────────────────
        void SyncAutosend()
        {
            string[] keys = (_unitKeys != null && _unitKeys.Length > 0) ? _unitKeys : FallbackUnitKeys;
            ActionSender.SetAutosend(AnyAutoEnabled, _autoUnits, keys);
        }

        // ── Punch scale ───────────────────────────────────────────────────────
        static IEnumerator PunchScale(Transform t, float targetScale, float halfDur)
        {
            Vector3 orig  = t.localScale;
            Vector3 punch = orig * targetScale;
            float elapsed = 0f;
            while (elapsed < halfDur)
            {
                elapsed += Time.deltaTime;
                t.localScale = Vector3.Lerp(orig, punch, Mathf.Clamp01(elapsed / halfDur));
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < halfDur)
            {
                elapsed += Time.deltaTime;
                t.localScale = Vector3.Lerp(punch, orig, Mathf.Clamp01(elapsed / halfDur));
                yield return null;
            }
            t.localScale = orig;
        }

        void EnsureViewportMask()
        {
            if (GetComponent<RectMask2D>() == null)
                gameObject.AddComponent<RectMask2D>();
        }

        void EnsureCollapseToggle()
        {
            if (_collapseButton != null)
                return;

            var parent = _parentRect != null ? _parentRect : transform.parent as RectTransform;
            if (parent == null)
                return;

            var toggleGo = new GameObject("CmdBarCollapseToggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            toggleGo.transform.SetParent(parent, false);
            toggleGo.transform.SetAsLastSibling();

            var rt = toggleGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(CollapseToggleWidth, CollapseToggleHeight);

            var image = toggleGo.GetComponent<Image>();
            image.color = new Color(0.08f, 0.10f, 0.12f, 0.92f);

            _collapseButton = toggleGo.GetComponent<Button>();
            _collapseButton.targetGraphic = image;
            _collapseButton.onClick.AddListener(ToggleCollapsed);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(toggleGo.transform, false);

            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            _collapseButtonLabel = labelGo.GetComponent<TextMeshProUGUI>();
            _collapseButtonLabel.alignment = TextAlignmentOptions.Center;
            _collapseButtonLabel.fontSize = 26f;
            _collapseButtonLabel.fontStyle = FontStyles.Bold;
            _collapseButtonLabel.color = Color.white;
            _collapseButtonLabel.text = "<";
            if (TMP_Settings.defaultFontAsset != null)
                _collapseButtonLabel.font = TMP_Settings.defaultFontAsset;

            UpdateCollapseToggleVisual();
        }

        void ToggleCollapsed()
        {
            SetCollapsed(!_isCollapsed, false);
        }

        void SetCollapsed(bool collapsed, bool immediate)
        {
            _isCollapsed = collapsed;
            UpdateCollapseToggleVisual();
            RefreshReturnToLaneButton();

            if (_collapseRoutine != null)
                StopCoroutine(_collapseRoutine);

            float targetWidth = _isCollapsed ? CollapsedWidth : GetExpandedWidth();
            if (immediate || !isActiveAndEnabled)
            {
                SetBarWidth(targetWidth);
                UpdateCollapseTogglePosition();
                return;
            }

            _collapseRoutine = StartCoroutine(AnimateWidth(targetWidth));
        }

        IEnumerator AnimateWidth(float targetWidth)
        {
            float startWidth = _rectTransform != null ? _rectTransform.rect.width : targetWidth;
            float duration = Mathf.Max(0.01f, CollapseAnimDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetBarWidth(Mathf.Lerp(startWidth, targetWidth, t));
                UpdateCollapseTogglePosition();
                yield return null;
            }

            SetBarWidth(targetWidth);
            UpdateCollapseTogglePosition();
            _collapseRoutine = null;
        }

        void ApplyResponsiveLayout(bool force)
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();
            if (_parentRect == null)
                _parentRect = transform.parent as RectTransform;

            var parentSize = _parentRect != null ? _parentRect.rect.size : new Vector2(Screen.width, Screen.height);
            var safeArea = Screen.safeArea;
            if (!force && parentSize == _lastParentSize && safeArea == _lastSafeArea)
                return;

            _lastParentSize = parentSize;
            _lastSafeArea = safeArea;

            if (_rectTransform != null)
                _rectTransform.anchoredPosition = new Vector2(GetSafeAreaLeftInsetUnits(), 0f);

            if (_collapseRoutine == null)
                SetBarWidth(_isCollapsed ? GetCollapsedWidth() : GetExpandedWidth());

            ApplyVerticalFit();
            UpdateCollapseTogglePosition();
        }

        float GetExpandedWidth()
        {
            float availableWidth = GetSafeAreaWidthUnits();
            if (IsSmallScreenLayout())
            {
                float width = availableWidth * Mathf.Clamp(MobileWidthPercentOfSafeArea, 0.08f, 0.30f);
                return Mathf.Clamp(width, MobileMinExpandedWidth, MobileMaxExpandedWidth);
            }

            float standardWidth = availableWidth * Mathf.Clamp(WidthPercentOfScreen, 0.10f, 0.35f);
            return Mathf.Clamp(standardWidth, MinExpandedWidth, MaxExpandedWidth);
        }

        float GetCollapsedWidth()
        {
            return IsSmallScreenLayout() ? MobileCollapsedWidth : CollapsedWidth;
        }

        void SetBarWidth(float width)
        {
            if (_rectTransform == null)
                return;

            _rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, width));
        }

        void UpdateCollapseTogglePosition()
        {
            if (_collapseButton == null || _rectTransform == null)
                return;

            var toggleRect = _collapseButton.transform as RectTransform;
            if (toggleRect == null)
                return;

            float panelWidth = _rectTransform.rect.width;
            float inset = IsSmallScreenLayout() ? MobileCollapseToggleInset : CollapseToggleInset;
            float leftInset = GetSafeAreaLeftInsetUnits();
            toggleRect.sizeDelta = new Vector2(
                IsSmallScreenLayout() ? MobileCollapseToggleWidth : CollapseToggleWidth,
                IsSmallScreenLayout() ? MobileCollapseToggleHeight : CollapseToggleHeight);
            toggleRect.anchoredPosition = new Vector2(leftInset + panelWidth + inset, 0f);
        }

        void UpdateCollapseToggleVisual()
        {
            if (_collapseButtonLabel != null)
                _collapseButtonLabel.text = _isCollapsed ? ">" : "<";
        }

        bool IsSmallScreenLayout()
        {
            float shortestSide = Mathf.Min(Screen.width, Screen.height);
            return Application.isMobilePlatform || shortestSide <= SmallScreenThreshold;
        }

        float GetSafeAreaWidthUnits()
        {
            float parentWidth = _parentRect != null ? _parentRect.rect.width : Screen.width;
            if (!RespectSafeArea || Screen.width <= 0f)
                return parentWidth;

            float leftInset = Screen.safeArea.xMin;
            float rightInset = Screen.width - Screen.safeArea.xMax;
            float insetUnits = (leftInset + rightInset) * (parentWidth / Screen.width);
            return Mathf.Max(0f, parentWidth - insetUnits);
        }

        float GetSafeAreaLeftInsetUnits()
        {
            float parentWidth = _parentRect != null ? _parentRect.rect.width : Screen.width;
            if (!RespectSafeArea || Screen.width <= 0f)
                return 0f;

            return Screen.safeArea.xMin * (parentWidth / Screen.width);
        }

        float GetSafeAreaHeightUnits()
        {
            float parentHeight = _parentRect != null ? _parentRect.rect.height : Screen.height;
            if (!RespectSafeArea || Screen.height <= 0f)
                return parentHeight;

            return Screen.safeArea.height * (parentHeight / Screen.height);
        }

        void CacheButtonLayoutElements()
        {
            _unitButtonLayouts.Clear();
            _unitButtonBaseHeights.Clear();

            if (UnitButtons == null)
                return;

            for (int i = 0; i < UnitButtons.Length; i++)
            {
                var layout = UnitButtons[i] != null ? UnitButtons[i].GetComponent<LayoutElement>() : null;
                _unitButtonLayouts.Add(layout);
                _unitButtonBaseHeights.Add(layout != null && layout.preferredHeight > 0f ? layout.preferredHeight : 188f);
            }
        }

        void ApplyVerticalFit()
        {
            if (_isCollapsed)
                return;

            if (_layoutGroup == null)
                _layoutGroup = GetComponent<VerticalLayoutGroup>();
            if (_layoutGroup == null || _rectTransform == null)
                return;

            if (_unitButtonLayouts.Count == 0)
                CacheButtonLayoutElements();

            bool mobile = IsSmallScreenLayout();
            _layoutGroup.spacing = mobile ? MobileVerticalSpacing : DesktopVerticalSpacing;
            _layoutGroup.padding.top = mobile ? MobileTopBottomPadding : DesktopTopBottomPadding;
            _layoutGroup.padding.bottom = mobile ? MobileTopBottomPadding : DesktopTopBottomPadding;

            int activeCount = 0;
            for (int i = 0; i < UnitButtons.Length; i++)
            {
                if (UnitButtons[i] != null && UnitButtons[i].gameObject.activeSelf)
                    activeCount++;
            }

            if (activeCount <= 0)
                return;

            float verticalUsagePercent = mobile ? MobileVerticalUsagePercent : DesktopVerticalUsagePercent;
            float layoutHeight = Mathf.Min(_rectTransform.rect.height, GetSafeAreaHeightUnits() * verticalUsagePercent);
            float availableHeight = layoutHeight - _layoutGroup.padding.top - _layoutGroup.padding.bottom;
            float spacingTotal = Mathf.Max(0, activeCount - 1) * _layoutGroup.spacing;
            float heightForButtons = Mathf.Max(0f, availableHeight - spacingTotal);
            float fittedHeight = heightForButtons / activeCount;
            float minHeight = mobile ? MobileMinButtonHeight : DesktopMinButtonHeight;
            float maxHeight = GetMaxBaseButtonHeight();
            float targetHeight = Mathf.Clamp(fittedHeight, minHeight, maxHeight);

            if (fittedHeight < minHeight)
                targetHeight = Mathf.Max(72f, fittedHeight);

            for (int i = 0; i < _unitButtonLayouts.Count; i++)
            {
                var layout = _unitButtonLayouts[i];
                if (layout == null)
                    continue;

                layout.minHeight = targetHeight;
                layout.preferredHeight = targetHeight;
                layout.flexibleHeight = 0f;
            }

            LayoutRebuilder.MarkLayoutForRebuild(_rectTransform);
        }

        float GetMaxBaseButtonHeight()
        {
            float maxHeight = 188f;
            for (int i = 0; i < _unitButtonBaseHeights.Count; i++)
                maxHeight = Mathf.Max(maxHeight, _unitButtonBaseHeights[i]);
            return maxHeight;
        }
    }
}

