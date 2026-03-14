// TileMenuUI.cs — World-space popup menu for unit placement and tower upgrades/selling.
//
// SETUP (Game_ML.unity):
//   World Space Canvas prefab as a child of TileGrid (or a dedicated parent).
//   Canvas
//   └── PanelTileMenu (inactive by default)
//       ├── Txt_TileInfo
//       ├── HLayout_TowerButtons   (unit placement picker — shown on empty tiles)
//       │   ├── Btn_Archer / Fighter / Mage / Ballista / Cannon
//       ├── Btn_Upgrade            (shown on tower tiles)
//       ├── Btn_Remove             (shown on tower tiles — sells defender back)
//       └── Btn_Close

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class TileMenuUI : MonoBehaviour, CastleDefender.Game.ITileMenu
    {
        public GameObject PanelTileMenu;
        public TMP_Text   TxtTileInfo;

        [Header("Empty tile: unit placement picker buttons")]
        public GameObject HLayoutTowerButtons;
        public Button     BtnArcher;
        public Button     BtnFighter;
        public Button     BtnMage;
        public Button     BtnBallista;
        public Button     BtnCannon;

        [Header("Tower: upgrade + remove")]
        public Button   BtnUpgrade;
        public TMP_Text TxtUpgradeCost;

        [Header("Shared")]
        public Button BtnRemove;
        public Button BtnClose;

        [Header("Tower Icons (assign in Inspector)")]
        [SerializeField] Sprite[] TowerIcons;

        // Fallback tower data (used until match config / catalog loads)
        static readonly string[] FallbackTowerKeys  = { "goblin", "orc", "troll", "vampire", "wyvern" };
        static readonly int[]    FallbackTowerCosts  = {       8,    14,      16,        20,       22 };

        // Catalog-driven (populated in ApplyCatalog)
        string[] _towerKeys;
        int[]    _towerCosts;

        int    _col, _row;
        string _tileType;
        string _towerType;
        bool   _initialized;
        Coroutine _scaleCoroutine;
        int _shownFrame = -1;
        readonly List<RawImage> _placementPortraits = new();

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            EnsureInitialized();
        }

        void OnDestroy()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnMLMatchConfig -= HandleMatchConfig;
        }

        // ── Match config (authoritative loadout for this match) ───────────────
        void HandleMatchConfig(MLMatchConfig config)
        {
            if (config?.loadout == null || config.loadout.Length == 0) return;

            int count = Mathf.Min(config.loadout.Length, 5);
            _towerKeys  = new string[count];
            _towerCosts = new int[count];
            for (int i = 0; i < count; i++)
            {
                var entry = config.loadout[i];
                _towerKeys[i]  = entry.key;
                _towerCosts[i] = entry.build_cost > 0
                    ? entry.build_cost
                    : GetKnownTowerCost(entry.key);
            }

            RefreshPlacementButtons(config.loadout);
            ApplyTowerButtonIcons();
            Debug.Log($"[TileMenuUI] Match loadout applied — {count} tower types");
        }

        void ApplyCatalog()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;
            var cachedLoadout = NetworkManager.Instance?.LastMatchLoadout;
            if (cachedLoadout != null && cachedLoadout.Length > 0)
            {
                BackfillTowerCostsFromCatalog(cachedLoadout);
                RefreshPlacementButtons(cachedLoadout);
                return;
            }

            // Use unit-types catalog (behavior_mode='both' HF units) as the pre-match
            // fallback — NOT the old towers catalog which has retired classic keys.
            var units = CatalogLoader.Units.FindAll(u => u.build_cost > 0);
            int count = Mathf.Min(units.Count > 0 ? units.Count : FallbackTowerKeys.Length, 5);

            _towerKeys  = new string[count];
            _towerCosts = new int[count];
            for (int i = 0; i < count; i++)
            {
                _towerKeys[i]  = units.Count > 0 ? units[i].key        : FallbackTowerKeys[i];
                _towerCosts[i] = units.Count > 0 ? units[i].build_cost : FallbackTowerCosts[i];
            }

            RefreshPlacementButtons(units.Count > 0 ? units.ToArray() : null);
            ApplyTowerButtonIcons();
            Debug.Log($"[TileMenuUI] Catalog applied — {count} unit types (pre-match fallback)");
        }

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            EnsureEventSystem();
            EnsureCanvasRaycaster();
            EnsurePanelRaycastState();

            // Seed with fallback data immediately; catalog will override when ready
            _towerKeys  = (string[])FallbackTowerKeys.Clone();
            _towerCosts = (int[])FallbackTowerCosts.Clone();

            if (CatalogLoader.IsReady)
                ApplyCatalog();
            else
                CatalogLoader.OnCatalogReady += ApplyCatalog;

            // Also listen to match config so we can re-populate buttons with the
            // actual player loadout (which the server enforces for upgrade_wall).
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnMLMatchConfig += HandleMatchConfig;

            if (PanelTileMenu != null)
                PanelTileMenu.SetActive(false);

            EnsurePlacementPortraitSlots();
            ApplyTowerButtonIcons();

            // Initial wiring — uses whatever _towerKeys is set to (catalog or fallback).
            // HandleMatchConfig (below) will RemoveAllListeners + re-add with loadout keys.
            BtnArcher?.onClick.AddListener(()   => OnConvert(_towerKeys.Length > 0 ? _towerKeys[0] : "archer"));
            BtnFighter?.onClick.AddListener(()  => OnConvert(_towerKeys.Length > 1 ? _towerKeys[1] : "fighter"));
            BtnMage?.onClick.AddListener(()     => OnConvert(_towerKeys.Length > 2 ? _towerKeys[2] : "mage"));
            BtnBallista?.onClick.AddListener(() => OnConvert(_towerKeys.Length > 3 ? _towerKeys[3] : "ballista"));
            BtnCannon?.onClick.AddListener(()   => OnConvert(_towerKeys.Length > 4 ? _towerKeys[4] : "cannon"));

            BtnUpgrade?.onClick.AddListener(OnUpgrade);
            BtnRemove?.onClick.AddListener(OnRemove);
            BtnClose?.onClick.AddListener(Close);

            // Avoid missing-glyph warnings on fonts that don't include the Unicode close icon.
            var closeLabel = BtnClose != null ? BtnClose.GetComponentInChildren<TMP_Text>(true) : null;
            if (closeLabel != null) closeLabel.text = "X";

            // Apply cached loadout LAST so HandleMatchConfig can safely remove the
            // initial listeners above and re-wire with the correct loadout keys.
            var cachedLoadout = NetworkManager.Instance?.LastMatchLoadout;
            if (cachedLoadout != null && cachedLoadout.Length > 0)
                HandleMatchConfig(new MLMatchConfig { loadout = cachedLoadout });
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
                var go = new GameObject("GameplayEventSystem");
                existing = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                Debug.Log("[TileMenuUI] Created fallback EventSystem for gameplay UI.");
                return;
            }

            if (existing.GetComponent<BaseInputModule>() == null)
            {
                existing.gameObject.AddComponent<StandaloneInputModule>();
                Debug.Log("[TileMenuUI] Added missing StandaloneInputModule to existing EventSystem.");
            }
        }

        void EnsureCanvasRaycaster()
        {
            var canvas = GetComponentInParent<Canvas>(true);
            if (canvas == null) return;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log("[TileMenuUI] Added missing GraphicRaycaster to parent Canvas.");
            }
        }

        void EnsurePanelRaycastState()
        {
            if (PanelTileMenu == null) return;

            var panelImage = PanelTileMenu.GetComponent<Image>();
            if (panelImage != null) panelImage.raycastTarget = true;

            var canvasGroup = PanelTileMenu.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = PanelTileMenu.AddComponent<CanvasGroup>();
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        void ApplyTowerButtonIcons()
        {
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length; i++)
                ApplyButtonVisual(buttons[i], i);
        }

        void ApplyButtonVisual(Button button, int index)
        {
            if (button == null) return;
            var target = button.transform.Find("Icon")?.GetComponent<Image>() ?? button.image;
            var portrait = index >= 0 && index < _placementPortraits.Count ? _placementPortraits[index] : null;
            var portraitTexture = ResolvePlacementPortraitTexture(index);
            var sprite = ResolvePlacementSprite(index);

            if (portrait != null)
            {
                portrait.texture = portraitTexture;
                portrait.color = portraitTexture != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            }

            if (target == null) return;
            target.sprite = sprite;
            target.preserveAspect = true;
            target.type = Image.Type.Simple;
            target.color = portraitTexture != null ? new Color(1f, 1f, 1f, 0f) : Color.white;
        }

        void EnsurePlacementPortraitSlots()
        {
            _placementPortraits.Clear();
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length; i++)
                _placementPortraits.Add(EnsurePlacementPortraitImage(buttons[i]));
        }

        static RawImage EnsurePlacementPortraitImage(Button btn)
        {
            if (btn == null) return null;
            var existing = btn.transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>();
            if (existing != null) return existing;

            var referenceRect = btn.transform.Find("IconBg") as RectTransform
                ?? btn.transform.Find("Icon") as RectTransform;

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
                frameRt.anchorMin = new Vector2(0.10f, 0.20f);
                frameRt.anchorMax = new Vector2(0.90f, 0.78f);
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

            var raw = portraitGO.GetComponent<RawImage>();
            raw.color = new Color(1f, 1f, 1f, 0f);
            raw.raycastTarget = false;
            var fitter = portraitGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1f;
            return raw;
        }

        Texture ResolvePlacementPortraitTexture(int index)
        {
            var cmdBar = FindFirstObjectByType<CastleDefender.UI.CmdBar>(FindObjectsInactive.Include);
            if (cmdBar == null || cmdBar.UnitButtons == null || index < 0 || index >= cmdBar.UnitButtons.Length)
                return null;

            var raw = cmdBar.UnitButtons[index].transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>();
            return raw != null ? raw.texture : null;
        }

        Sprite ResolvePlacementSprite(int index)
        {
            var cmdBar = FindFirstObjectByType<CastleDefender.UI.CmdBar>(FindObjectsInactive.Include);
            if (cmdBar != null)
            {
                if (cmdBar.UnitButtons != null && index < cmdBar.UnitButtons.Length && cmdBar.UnitButtons[index] != null)
                {
                    var liveIcon = cmdBar.UnitButtons[index].transform.Find("Icon")?.GetComponent<Image>();
                    if (liveIcon != null && liveIcon.sprite != null) return liveIcon.sprite;
                    if (cmdBar.UnitButtons[index].image != null && cmdBar.UnitButtons[index].image.sprite != null)
                        return cmdBar.UnitButtons[index].image.sprite;
                }
            }

            return TowerIcons != null && index < TowerIcons.Length ? TowerIcons[index] : null;
        }

        void RefreshPlacementButtons(UnitCatalogEntry[] catalogEntries)
        {
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].onClick.RemoveAllListeners();
                if (_towerKeys == null || i >= _towerKeys.Length)
                {
                    buttons[i].gameObject.SetActive(false);
                    continue;
                }

                string key = _towerKeys[i];
                string name = key;
                int cost = i < _towerCosts.Length ? _towerCosts[i] : 0;

                if (catalogEntries != null && i < catalogEntries.Length && catalogEntries[i] != null)
                {
                    name = catalogEntries[i].name;
                    if (cost <= 0) cost = catalogEntries[i].build_cost;
                }
                else if (CatalogLoader.UnitByKey.TryGetValue(key, out var catalogUnit))
                {
                    name = catalogUnit.name;
                    if (cost <= 0) cost = catalogUnit.build_cost;
                }

                _towerCosts[i] = cost;

                var lbl = buttons[i].GetComponentInChildren<TMP_Text>(true);
                if (lbl != null)
                    lbl.text = $"{name}\n{cost}g";

                int captured = i;
                buttons[i].onClick.AddListener(() => OnConvert(_towerKeys[captured]));
                buttons[i].gameObject.SetActive(true);
            }
        }

        void RefreshPlacementButtons(LoadoutEntry[] loadoutEntries)
        {
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].onClick.RemoveAllListeners();
                if (_towerKeys == null || i >= _towerKeys.Length || loadoutEntries == null || i >= loadoutEntries.Length)
                {
                    buttons[i].gameObject.SetActive(false);
                    continue;
                }

                int cost = i < _towerCosts.Length ? _towerCosts[i] : 0;
                if (cost <= 0 && CatalogLoader.UnitByKey.TryGetValue(loadoutEntries[i].key, out var catalogUnit))
                    cost = catalogUnit.build_cost;

                _towerCosts[i] = cost;

                var lbl = buttons[i].GetComponentInChildren<TMP_Text>(true);
                if (lbl != null)
                    lbl.text = $"{loadoutEntries[i].name}\n{cost}g";

                int captured = i;
                buttons[i].onClick.AddListener(() => OnConvert(_towerKeys[captured]));
                buttons[i].gameObject.SetActive(true);
            }
        }

        void BackfillTowerCostsFromCatalog(LoadoutEntry[] loadoutEntries)
        {
            if (loadoutEntries == null || _towerKeys == null || _towerCosts == null) return;
            for (int i = 0; i < loadoutEntries.Length && i < _towerKeys.Length && i < _towerCosts.Length; i++)
            {
                if (_towerCosts[i] > 0) continue;
                if (loadoutEntries[i].build_cost > 0)
                {
                    _towerCosts[i] = loadoutEntries[i].build_cost;
                    continue;
                }
                if (CatalogLoader.UnitByKey.TryGetValue(loadoutEntries[i].key, out var unit))
                    _towerCosts[i] = unit.build_cost;
            }
        }

        void Update()
        {
            if (!PanelTileMenu.activeSelf) return;

            ApplyTowerButtonIcons();

            float gold = SnapshotApplier.Instance?.MyLane?.gold ?? 0f;

            if (_towerCosts == null) return;
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length && i < _towerCosts.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].interactable = gold >= _towerCosts[i];
            }

            if (_tileType == "tower")
            {
                int lvl  = GetTowerLevel(_col, _row);
                int cost = UpgradeCostEstimate(lvl);
                BtnUpgrade.interactable = gold >= cost;
                if (TxtUpgradeCost != null)
                    TxtUpgradeCost.text = BuildUpgradeLabel(lvl, cost);
            }

            HandlePointerFallback();
        }

        // ─────────────────────────────────────────────────────────────────────
        public void Show(int col, int row, string tileType, string towerType)
        {
            // Some scenes keep this GO inactive in hierarchy.
            // Ensure it is active and wired before displaying.
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            EnsureInitialized();

            _col      = col;
            _row      = row;
            _tileType = tileType;
            _towerType = towerType;
            PositionMenuNearTile(col, row);

            bool isEmpty = tileType == "empty";
            bool isTower = tileType == "tower";

            if (!isEmpty && !isTower)
            {
                // Unexpected tile type — close immediately rather than show a broken menu
                Debug.LogWarning($"[TileMenuUI] Show called with unexpected tileType '{tileType}' at ({col},{row}) — ignoring.");
                return;
            }

            if (isTower)
            {
                int lvl  = GetTowerLevel(col, row);
                int cost = UpgradeCostEstimate(lvl);
                TxtTileInfo.text = $"{Capitalize(towerType)}  Lv {lvl}  [{col},{row}]";
                if (TxtUpgradeCost != null)
                    TxtUpgradeCost.text = BuildUpgradeLabel(lvl, cost);
            }
            else
            {
                TxtTileInfo.text = $"Place unit  [{col},{row}]";
            }

            HLayoutTowerButtons.SetActive(isEmpty);
            BtnUpgrade.gameObject.SetActive(isTower);
            BtnRemove.gameObject.SetActive(isTower);

            PanelTileMenu.SetActive(true);
            _shownFrame = Time.frameCount;
            if (isActiveAndEnabled)
            {
                if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
                PanelTileMenu.transform.localScale = Vector3.zero;
                _scaleCoroutine = StartCoroutine(ScaleIn(PanelTileMenu.transform, 0.15f));
            }
            else
            {
                // If this component/GO is inactive, coroutines cannot run; open instantly.
                PanelTileMenu.transform.localScale = Vector3.one;
            }
        }

        public void Close()
        {
            if (isActiveAndEnabled)
            {
                if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
                _scaleCoroutine = StartCoroutine(ScaleOut(PanelTileMenu.transform, 0.1f));
            }
            else if (PanelTileMenu != null)
            {
                PanelTileMenu.SetActive(false);
            }
        }

        void HandlePointerFallback()
        {
            if (Time.frameCount == _shownFrame) return;

            if (Input.GetMouseButtonUp(0))
            {
                if (TryHandlePointerAt(Input.mousePosition)) return;
            }

            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Ended)
                    {
                        if (TryHandlePointerAt(touch.position)) return;
                    }
                }
            }
        }

        bool TryHandlePointerAt(Vector2 screenPos)
        {
            Camera eventCam = GetEventCamera();

            if (_tileType == "tower" && BtnUpgrade != null && BtnUpgrade.gameObject.activeInHierarchy
                && RectContainsScreenPoint(BtnUpgrade.transform as RectTransform, screenPos, eventCam))
            {
                if (BtnUpgrade.interactable)
                {
                    Debug.Log($"[TileMenuUI] Fallback upgrade click at ({_col},{_row}) tower={_towerType}");
                    OnUpgrade();
                }
                return true;
            }

            if (_tileType == "tower" && BtnRemove != null && BtnRemove.gameObject.activeInHierarchy
                && RectContainsScreenPoint(BtnRemove.transform as RectTransform, screenPos, eventCam))
            {
                if (BtnRemove.interactable) OnRemove();
                return true;
            }

            if (BtnClose != null && BtnClose.gameObject.activeInHierarchy
                && RectContainsScreenPoint(BtnClose.transform as RectTransform, screenPos, eventCam))
            {
                Close();
                return true;
            }

            return false;
        }

        Camera GetEventCamera()
        {
            var canvas = GetComponentInParent<Canvas>(true);
            if (canvas == null) return Camera.main;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        static bool RectContainsScreenPoint(RectTransform rt, Vector2 screenPos, Camera eventCam)
        {
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, eventCam);
        }

        void PositionMenuNearTile(int col, int row)
        {
            var rt = transform as RectTransform;
            var canvas = GetComponentInParent<Canvas>();
            if (rt == null || canvas == null)
            {
                transform.position = TileGrid.TileToWorld(SnapshotApplier.Instance?.MyLaneIndex ?? 0, col, row) + Vector3.up * 0.5f;
                return;
            }

            Vector3 world = TileGrid.TileToWorld(SnapshotApplier.Instance?.MyLaneIndex ?? 0, col, row) + Vector3.up * 0.5f;

            // For ScreenSpaceCamera with no worldCamera assigned, Unity falls back to
            // rendering as ScreenSpaceOverlay — so use null for the conversion camera.
            // For ScreenSpaceCamera with a worldCamera assigned, use that camera.
            Camera projCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.worldCamera == null
                ? null
                : canvas.worldCamera;

            // Always use Camera.main (or the canvas cam) to project world→screen.
            Camera screenCam = Camera.main != null ? Camera.main : projCam;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(screenCam, world);

            RectTransform canvasRt = canvas.transform as RectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen, projCam, out Vector2 local))
            {
                // Fallback: place at center-screen offset so menu is at least visible
                local = Vector2.zero;
            }

            local += new Vector2(0f, 70f);

            Vector2 halfCanvas = canvasRt.rect.size * 0.5f;
            Vector2 halfMenu = rt.rect.size * 0.5f;
            const float margin = 12f;
            local.x = Mathf.Clamp(local.x, -halfCanvas.x + halfMenu.x + margin, halfCanvas.x - halfMenu.x - margin);
            local.y = Mathf.Clamp(local.y, -halfCanvas.y + halfMenu.y + margin, halfCanvas.y - halfMenu.y - margin);

            rt.anchoredPosition = local;
        }

        // ── Handlers ─────────────────────────────────────────────────────────
        void OnConvert(string unitTypeKey) { ActionSender.PlaceUnit(_col, _row, unitTypeKey); Close(); }
        void OnUpgrade()                   { ActionSender.UpgradeTower(_col, _row, _towerType); Close(); }
        void OnRemove()                    { ActionSender.SellTower(_col, _row);              Close(); }

        // ── Helpers ───────────────────────────────────────────────────────────
        int GetTowerLevel(int col, int row)
        {
            var lane = SnapshotApplier.Instance?.ViewedLane;
            if (lane == null) return 1;
            if (lane.towerCells != null)
                foreach (var t in lane.towerCells)
                    if (t.X == col && t.Y == row) return Mathf.Max(1, t.level);
            // Defender may be mobilized during combat — check mobilizedCells too
            if (lane.mobilizedCells != null)
                foreach (var t in lane.mobilizedCells)
                    if (t.X == col && t.Y == row) return Mathf.Max(1, t.level);
            return 1;
        }

        // Mirrors server: Math.ceil(baseCost * (0.75 + 0.25 * nextLevel))
        int UpgradeCostEstimate(int currentLevel)
        {
            int baseCost = GetTowerBaseCost(_towerType);
            if (baseCost <= 0) return 0;
            return Mathf.CeilToInt(baseCost * (0.75f + 0.25f * (currentLevel + 1)));
        }

        int GetTowerBaseCost(string towerType)
        {
            if (string.IsNullOrEmpty(towerType)) return 0;
            if (CatalogLoader.UnitByKey.TryGetValue(towerType, out var u)) return u.build_cost;
            if (_towerKeys != null)
                for (int i = 0; i < _towerKeys.Length && i < _towerCosts.Length; i++)
                    if (_towerKeys[i] == towerType) return _towerCosts[i];
            return 0;
        }

        int GetKnownTowerCost(string towerType)
        {
            if (string.IsNullOrEmpty(towerType)) return 0;
            if (CatalogLoader.UnitByKey.TryGetValue(towerType, out var u) && u.build_cost > 0) return u.build_cost;
            if (_towerKeys != null)
                for (int i = 0; i < _towerKeys.Length && i < _towerCosts.Length; i++)
                    if (_towerKeys[i] == towerType && _towerCosts[i] > 0) return _towerCosts[i];
            return 0;
        }

        string BuildUpgradeLabel(int lvl, int cost)
        {
            string line = $"↑  Lv {lvl} → {lvl + 1}   ({cost}g)";
            string preview = NextLevelDmgPreview(lvl);
            return preview.Length > 0 ? $"{line}\n{preview}" : line;
        }

        string NextLevelDmgPreview(int currentLevel)
        {
            float baseDmg = 0f;
            var loadout = NetworkManager.Instance?.LastMatchLoadout;
            if (loadout != null)
                foreach (var e in loadout)
                    if (e.key == _towerType) { baseDmg = e.attack_damage; break; }
            if (baseDmg <= 0) return "";
            float curDmg  = baseDmg * (1f + 0.12f * (currentLevel - 1));
            float nextDmg = baseDmg * (1f + 0.12f * currentLevel);
            return $"DMG  {curDmg:0} → {nextDmg:0}";
        }

        static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        // ── Coroutine helpers ─────────────────────────────────────────────────
        static IEnumerator ScaleIn(Transform t, float dur)
        {
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                t.localScale = Vector3.one * EaseOutBack(n);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        IEnumerator ScaleOut(Transform t, float dur)
        {
            Vector3 start = t.localScale;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                t.localScale = Vector3.Lerp(start, Vector3.zero, n * n);
                yield return null;
            }
            t.localScale = Vector3.zero;
            PanelTileMenu.SetActive(false);
            // Deactivate the whole container so the outer Image doesn't linger on screen.
            gameObject.SetActive(false);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float tm1 = t - 1f;
            return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        }
    }
}

