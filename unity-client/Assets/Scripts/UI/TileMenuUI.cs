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

using System;
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

        const float MissingLoadoutErrorDelaySeconds = 1.5f;

        // Catalog-driven (populated in ApplyCatalog)
        string[] _towerKeys;
        int[]    _towerCosts;

        int    _col, _row;
        string _tileType;
        string _towerType;
        string _baseTileInfoText;
        string _statusText;
        bool   _initialized;
        bool   _networkHooksRegistered;
        bool   _loadoutMissingLogged;
        Coroutine _scaleCoroutine;
        int _shownFrame = -1;
        readonly List<RawImage> _placementPortraits = new();
        PendingAction _pendingAction = PendingAction.None;
        string _lastLegacyBlockSignature;

        enum PendingAction
        {
            None,
            PlaceUnit,
            UpgradeTower,
            SellTower,
        }

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            EnsureInitialized();
        }

        void OnDestroy()
        {
            UnregisterNetworkCallbacks();
        }

        // ── Match config (authoritative loadout for this match) ───────────────
        void HandleMatchConfig(MLMatchConfig config)
        {
            if (config?.loadout == null || config.loadout.Length == 0) return;

            _loadoutMissingLogged = false;
            int count = Mathf.Min(config.loadout.Length, 5);
            _towerKeys  = new string[count];
            _towerCosts = new int[count];
            for (int i = 0; i < count; i++)
            {
                var entry = config.loadout[i];
                _towerKeys[i]  = entry.key;
                _towerCosts[i] = entry.build_cost;
                if (_towerCosts[i] <= 0)
                {
                    Debug.LogError(
                        $"[TileMenuUI] Match loadout entry '{entry.key}' has invalid build_cost={entry.build_cost}. " +
                        "Runtime will not guess a replacement cost.");
                }
            }

            RefreshPlacementButtons(config.loadout);
            ApplyTowerButtonIcons();
            Debug.Log($"[TileMenuUI] Match loadout applied — {count} tower types");
        }

        void ApplyCatalog()
        {
            SetPlacementButtonsUnavailable("LOADOUT\nERROR");
            Debug.LogError(
                "[TileMenuUI] Refusing to derive placement buttons from the global catalog. " +
                "Game_ML requires the authoritative per-match loadout from ml_match_config.");
        }

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            EnsureEventSystem();
            EnsureCanvasRaycaster();
            EnsurePanelRaycastState();

            _towerKeys = Array.Empty<string>();
            _towerCosts = Array.Empty<int>();

            // Also listen to match config so we can re-populate buttons with the
            // actual player loadout (which the server enforces for upgrade_wall).
            RegisterNetworkCallbacks();

            if (PanelTileMenu != null)
                PanelTileMenu.SetActive(false);

            EnsurePlacementPortraitSlots();
            SetPlacementButtonsUnavailable("WAITING\nLOADOUT");
            ApplyTowerButtonIcons();

            // Initial wiring targets the eventual authoritative loadout entries.
            BtnArcher?.onClick.AddListener(()   => TryOnConvertIndex(0));
            BtnFighter?.onClick.AddListener(()  => TryOnConvertIndex(1));
            BtnMage?.onClick.AddListener(()     => TryOnConvertIndex(2));
            BtnBallista?.onClick.AddListener(() => TryOnConvertIndex(3));
            BtnCannon?.onClick.AddListener(()   => TryOnConvertIndex(4));

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
            else
                StartCoroutine(RequireAuthoritativeLoadout());
        }

        IEnumerator RequireAuthoritativeLoadout()
        {
            yield return new WaitForSeconds(MissingLoadoutErrorDelaySeconds);
            if (_towerKeys != null && _towerKeys.Length > 0)
                yield break;

            if (_loadoutMissingLogged)
                yield break;

            _loadoutMissingLogged = true;
            SetPlacementButtonsUnavailable("LOADOUT\nMISSING");
            Debug.LogError(
                $"[TileMenuUI] No authoritative match loadout arrived for '{name}' in scene '{gameObject.scene.name}'. " +
                "Placement buttons are disabled until ml_match_config provides the per-match roster.");
        }

        void SetPlacementButtonsUnavailable(string label)
        {
            _towerKeys = Array.Empty<string>();
            _towerCosts = Array.Empty<int>();

            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null)
                    continue;

                button.interactable = false;
                var labelText = button.GetComponentInChildren<TMP_Text>(true);
                if (labelText != null)
                    labelText.text = label;
            }
        }

        void TryOnConvertIndex(int index)
        {
            if (_towerKeys == null || index < 0 || index >= _towerKeys.Length)
            {
                Debug.LogError(
                    $"[TileMenuUI] Placement button index={index} was pressed before an authoritative loadout was available on '{name}'.");
                return;
            }

            OnConvert(_towerKeys[index]);
        }

        static void EnsureEventSystem()
        {
            var tileMenu = FindFirstObjectByType<TileMenuUI>(FindObjectsInactive.Include);
            var existing = SceneEventSystemUtility.FindBest(tileMenu);

            if (existing == null)
            {
                var go = new GameObject("GameplayEventSystem");
                existing = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                go.AddComponent<SingleEventSystem>();
                Debug.LogError("[TileMenuUI] Runtime created a missing EventSystem for gameplay UI. Scene wiring should provide one explicitly.");
                return;
            }

            if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
                Debug.LogError("[TileMenuUI] Reactivated an inactive EventSystem for gameplay UI. Scene wiring is broken.");
            }

            if (existing.GetComponent<BaseInputModule>() == null)
            {
                existing.gameObject.AddComponent<StandaloneInputModule>();
                Debug.LogError("[TileMenuUI] Added a missing StandaloneInputModule to the EventSystem at runtime. Scene wiring is broken.");
            }

            if (existing.GetComponent<SingleEventSystem>() == null)
            {
                existing.gameObject.AddComponent<SingleEventSystem>();
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
                if (cost <= 0)
                {
                    Debug.LogError(
                        $"[TileMenuUI] Loadout entry '{loadoutEntries[i].key}' is missing a valid build_cost. " +
                        "Placement button will remain disabled instead of guessing from catalog data.");
                }

                _towerCosts[i] = cost;

                var lbl = buttons[i].GetComponentInChildren<TMP_Text>(true);
                if (lbl != null)
                    lbl.text = cost > 0
                        ? $"{loadoutEntries[i].name}\n{cost}g"
                        : $"{loadoutEntries[i].name}\nCOST ERR";

                int captured = i;
                buttons[i].onClick.AddListener(() => OnConvert(_towerKeys[captured]));
                buttons[i].gameObject.SetActive(true);
            }
        }

        void Update()
        {
            if (!PanelTileMenu.activeSelf) return;
            if (TryGetLegacyTileGridDisableReason(out string reason))
            {
                PanelTileMenu.SetActive(false);
                LogLegacyTileMenuBlocked("UpdateHide", reason);
                return;
            }

            ApplyTowerButtonIcons();
            float gold = GetMyGold();
            bool isBuildPhase = IsBuildPhase();

            if (_towerCosts == null) return;
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length && i < _towerCosts.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].interactable = _pendingAction == PendingAction.None
                        && _tileType == "empty"
                        && isBuildPhase
                        && gold >= _towerCosts[i];
            }

            if (_tileType == "tower")
            {
                int lvl  = GetTowerLevel(_col, _row);
                int cost = UpgradeCostEstimate(lvl);
                if (BtnUpgrade != null)
                    BtnUpgrade.interactable = _pendingAction == PendingAction.None
                        && lvl < 10
                        && gold >= cost;
                if (BtnRemove != null)
                    BtnRemove.interactable = _pendingAction == PendingAction.None;
                if (TxtUpgradeCost != null)
                    TxtUpgradeCost.text = BuildUpgradeLabel(lvl, cost);
            }

            HandlePointerFallback();
        }

        // ─────────────────────────────────────────────────────────────────────
        public void Show(int col, int row, string tileType, string towerType)
        {
            if (TryGetLegacyTileGridDisableReason(out string reason))
            {
                if (PanelTileMenu != null)
                    PanelTileMenu.SetActive(false);
                LogLegacyTileMenuBlocked("Show", reason);
                return;
            }

            // Some scenes keep this GO inactive in hierarchy.
            // Ensure it is active and wired before displaying.
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            EnsureInitialized();

            _col      = col;
            _row      = row;
            _tileType = tileType;
            _towerType = towerType;
            _pendingAction = PendingAction.None;
            _statusText = null;
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
                _baseTileInfoText = $"{Capitalize(towerType)}  Lv {lvl}  [{col},{row}]";
                if (TxtUpgradeCost != null)
                    TxtUpgradeCost.text = BuildUpgradeLabel(lvl, cost);
            }
            else
            {
                _baseTileInfoText = $"Place unit  [{col},{row}]";
            }

            RefreshTileInfo();

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

            if (_tileType == "empty" && _towerKeys != null)
            {
                var placementButtons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
                for (int i = 0; i < placementButtons.Length && i < _towerKeys.Length; i++)
                {
                    var button = placementButtons[i];
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    if (!RectContainsScreenPoint(button.transform as RectTransform, screenPos, eventCam)) continue;

                    if (button.interactable)
                    {
                        Debug.Log($"[TileMenuUI] Fallback placement click at ({_col},{_row}) unit={_towerKeys[i]}");
                        OnConvert(_towerKeys[i]);
                    }
                    return true;
                }
            }

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
                transform.position = BattlefieldSpaceMapper.TileToWorld(SnapshotApplier.Instance?.MyLaneIndex ?? 0, col, row) + Vector3.up * 0.5f;
                return;
            }

            Vector3 world = BattlefieldSpaceMapper.TileToWorld(SnapshotApplier.Instance?.MyLaneIndex ?? 0, col, row) + Vector3.up * 0.5f;

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
        void OnConvert(string unitTypeKey)
        {
            Debug.Log($"[TileMenuUI] OnConvert col={_col} row={_row} unitTypeKey={unitTypeKey} tileType={_tileType}");
            BeginPendingAction(PendingAction.PlaceUnit, unitTypeKey);
            ActionSender.PlaceUnit(_col, _row, unitTypeKey);
        }
        void OnUpgrade()                   { BeginPendingAction(PendingAction.UpgradeTower, _towerType); ActionSender.UpgradeTower(_col, _row, _towerType); }
        void OnRemove()                    { BeginPendingAction(PendingAction.SellTower, _towerType); ActionSender.SellTower(_col, _row); }

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
            if (_towerKeys != null)
                for (int i = 0; i < _towerKeys.Length && i < _towerCosts.Length; i++)
                    if (_towerKeys[i] == towerType) return _towerCosts[i];
            return 0;
        }

        string BuildUpgradeLabel(int lvl, int cost)
        {
            string line = $"↑  Lv {lvl} → {lvl + 1}   ({cost}g)";
            string preview = NextLevelDmgPreview(lvl);
            return preview.Length > 0 ? $"{line}\n{preview}" : line;
        }

        void RegisterNetworkCallbacks()
        {
            if (_networkHooksRegistered || NetworkManager.Instance == null) return;
            NetworkManager.Instance.OnMLMatchConfig += HandleMatchConfig;
            NetworkManager.Instance.OnActionApplied += HandleActionApplied;
            NetworkManager.Instance.OnErrorMsg += HandleErrorMessage;
            _networkHooksRegistered = true;
        }

        void UnregisterNetworkCallbacks()
        {
            if (!_networkHooksRegistered || NetworkManager.Instance == null) return;
            NetworkManager.Instance.OnMLMatchConfig -= HandleMatchConfig;
            NetworkManager.Instance.OnActionApplied -= HandleActionApplied;
            NetworkManager.Instance.OnErrorMsg -= HandleErrorMessage;
            _networkHooksRegistered = false;
        }

        void BeginPendingAction(PendingAction action, string _unitTypeKey)
        {
            _pendingAction = action;
            _statusText = "Sending...";
            RefreshTileInfo();
        }

        void HandleActionApplied(ActionAppliedPayload payload)
        {
            if (payload == null || _pendingAction == PendingAction.None) return;

            bool matches = false;
            switch (_pendingAction)
            {
                case PendingAction.PlaceUnit:
                    matches = payload.type == "place_unit";
                    break;
                case PendingAction.UpgradeTower:
                    matches = payload.type == "upgrade_tower";
                    break;
                case PendingAction.SellTower:
                    matches = payload.type == "sell_tower";
                    break;
            }

            if (!matches) return;

            _pendingAction = PendingAction.None;
            _statusText = null;
            Close();
        }

        void HandleErrorMessage(ErrorPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.message)) return;
            if (_pendingAction == PendingAction.None) return;

            _pendingAction = PendingAction.None;
            _statusText = payload.message;
            RefreshTileInfo();
        }

        void RefreshTileInfo()
        {
            if (TxtTileInfo == null) return;
            TxtTileInfo.text = string.IsNullOrWhiteSpace(_statusText)
                ? _baseTileInfoText
                : $"{_baseTileInfoText}\n{_statusText}";
        }

        float GetMyGold()
        {
            var lane = SnapshotApplier.Instance?.MyLane;
            return lane != null ? lane.gold : 0f;
        }

        bool IsBuildPhase()
        {
            return SnapshotApplier.Instance?.LatestML?.roundState == "build";
        }

        bool TryGetLegacyTileGridDisableReason(out string reason)
        {
            return FortressSelectionController.ShouldBlockLegacyTileMenu(ResolveBlockingLaneIndex(), out reason);
        }

        int ResolveBlockingLaneIndex()
        {
            var parentGrid = GetComponentInParent<TileGrid>(true);
            if (parentGrid != null)
                return parentGrid.LaneIndex;

            var snapshotApplier = SnapshotApplier.Instance;
            return snapshotApplier != null ? snapshotApplier.MyLaneIndex : -1;
        }

        void LogLegacyTileMenuBlocked(string source, string reason)
        {
            string signature = $"{source}|{reason}";
            if (_lastLegacyBlockSignature == signature)
                return;

            _lastLegacyBlockSignature = signature;
            Debug.LogError($"[FortressSelection] Blocked TileMenuUI source='{source}' on '{name}'. {reason}");
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

