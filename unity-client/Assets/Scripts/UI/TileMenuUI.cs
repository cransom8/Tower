// TileMenuUI.cs — World-space popup menu for wall→tower conversion and tower upgrades.
//
// SETUP (Game_ML.unity):
//   World Space Canvas prefab as a child of TileGrid (or a dedicated parent).
//   Canvas
//   └── PanelTileMenu (inactive by default)
//       ├── Txt_TileInfo
//       ├── HLayout_TowerButtons
//       │   ├── Btn_Archer / Fighter / Mage / Ballista / Cannon
//       ├── Btn_Upgrade
//       ├── Btn_Remove
//       └── Btn_Close

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class TileMenuUI : MonoBehaviour
    {
        public GameObject PanelTileMenu;
        public TMP_Text   TxtTileInfo;

        [Header("Wall: tower-type conversion buttons")]
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

        // Fallback tower data (used until catalog loads)
        static readonly string[] FallbackTowerKeys  = { "archer", "fighter", "mage", "ballista", "cannon" };
        static readonly int[]    FallbackTowerCosts  = {      10,        12,      24,         20,        30 };
        static readonly int[]    UpgradeCostPerLevel = { 12, 18, 26, 36, 50, 65, 82, 100, 120 };
        static readonly string[] TowerIconPaths =
        {
            "Icons/towers/archer_icon",
            "Icons/towers/fighter_icon",
            "Icons/towers/mage_icon",
            "Icons/towers/ballista_icon",
            "Icons/towers/cannon_icon"
        };

        // Catalog-driven (populated in ApplyCatalog)
        string[] _towerKeys;
        int[]    _towerCosts;

        int    _col, _row;
        string _tileType;
        string _towerType;
        bool   _initialized;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            EnsureInitialized();
        }

        void OnDestroy()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;
        }

        void ApplyCatalog()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;

            var towers = CatalogLoader.Towers.Count > 0 ? CatalogLoader.Towers : null;
            int count  = towers != null ? Mathf.Min(towers.Count, 5) : FallbackTowerKeys.Length;

            _towerKeys  = new string[count];
            _towerCosts = new int[count];
            for (int i = 0; i < count; i++)
            {
                _towerKeys[i]  = towers != null ? towers[i].key        : FallbackTowerKeys[i];
                _towerCosts[i] = towers != null ? towers[i].build_cost : FallbackTowerCosts[i];
            }

            // Re-wire button listeners to use catalog keys
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].onClick.RemoveAllListeners();
                if (i < count)
                {
                    int captured = i;
                    buttons[i].onClick.AddListener(() => OnConvert(_towerKeys[captured]));
                    buttons[i].gameObject.SetActive(true);
                }
                else
                {
                    buttons[i].gameObject.SetActive(false);
                }
            }
            Debug.Log($"[TileMenuUI] Catalog applied — {count} tower types");
        }

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Seed with fallback data immediately; catalog will override when ready
            _towerKeys  = (string[])FallbackTowerKeys.Clone();
            _towerCosts = (int[])FallbackTowerCosts.Clone();

            if (CatalogLoader.IsReady)
                ApplyCatalog();
            else
                CatalogLoader.OnCatalogReady += ApplyCatalog;

            if (PanelTileMenu != null)
                PanelTileMenu.SetActive(false);

            ApplyTowerButtonIcons();

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
        }

        void ApplyTowerButtonIcons()
        {
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length && i < TowerIconPaths.Length; i++)
                ApplyIcon(buttons[i], TowerIconPaths[i]);
        }

        static void ApplyIcon(Button button, string resourcePath)
        {
            if (button == null || button.image == null || string.IsNullOrEmpty(resourcePath))
                return;

            Sprite sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(resourcePath);
                if (tex != null)
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                           new Vector2(0.5f, 0.5f), 100f);
            }

            if (sprite == null) return;
            button.image.sprite = sprite;
            button.image.preserveAspect = true;
            button.image.type = Image.Type.Simple;
        }

        void Update()
        {
            if (!PanelTileMenu.activeSelf) return;

            float gold = SnapshotApplier.Instance?.MyLane?.gold ?? 0f;

            if (_towerCosts == null) return;
            var buttons = new[] { BtnArcher, BtnFighter, BtnMage, BtnBallista, BtnCannon };
            for (int i = 0; i < buttons.Length && i < _towerCosts.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].interactable = gold >= _towerCosts[i];
            }

            if (_tileType == "tower")
                BtnUpgrade.interactable = gold >= UpgradeCostEstimate();
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

            TxtTileInfo.text = tileType == "tower"
                ? $"{Capitalize(towerType)} tower at ({col},{row})"
                : $"Wall at ({col},{row})";

            bool isWall  = tileType == "wall";
            bool isTower = tileType == "tower";

            HLayoutTowerButtons.SetActive(isWall);
            BtnUpgrade.gameObject.SetActive(isTower);
            BtnRemove.gameObject.SetActive(true);

            if (isTower)
                TxtUpgradeCost.text = $"↑ Upgrade ({UpgradeCostEstimate()}g)";

            PanelTileMenu.SetActive(true);
            if (isActiveAndEnabled)
            {
                PanelTileMenu.transform.localScale = Vector3.zero;
                StartCoroutine(ScaleIn(PanelTileMenu.transform, 0.15f));
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
                StartCoroutine(ScaleOut(PanelTileMenu.transform, 0.1f));
            else if (PanelTileMenu != null)
                PanelTileMenu.SetActive(false);
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
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransform canvasRt = canvas.transform as RectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen, cam, out Vector2 local))
                return;

            local += new Vector2(0f, 70f);

            Vector2 halfCanvas = canvasRt.rect.size * 0.5f;
            Vector2 halfMenu = rt.rect.size * 0.5f;
            const float margin = 12f;
            local.x = Mathf.Clamp(local.x, -halfCanvas.x + halfMenu.x + margin, halfCanvas.x - halfMenu.x - margin);
            local.y = Mathf.Clamp(local.y, -halfCanvas.y + halfMenu.y + margin, halfCanvas.y - halfMenu.y - margin);

            rt.anchoredPosition = local;
        }

        // ── Handlers ─────────────────────────────────────────────────────────
        void OnConvert(string towerType) { ActionSender.UpgradeWall(_col, _row, towerType); Close(); }
        void OnUpgrade()                 { ActionSender.UpgradeTower(_col, _row, _towerType); Close(); }
        void OnRemove()                  { ActionSender.RemoveWall(_col, _row);              Close(); }

        // ── Helpers ───────────────────────────────────────────────────────────
        int UpgradeCostEstimate()
        {
            var lane = SnapshotApplier.Instance?.ViewedLane;
            if (lane?.towerCells == null) return 15;
            foreach (var t in lane.towerCells)
                if (t.X == _col && t.Y == _row)
                {
                    int lvl = Mathf.Clamp(t.level - 1, 0, UpgradeCostPerLevel.Length - 1);
                    return UpgradeCostPerLevel[lvl];
                }
            return 15;
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
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float tm1 = t - 1f;
            return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        }
    }
}
