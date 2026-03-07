// CmdBar.cs — Bottom command bar: Wall | 5 unit buttons | Autosend toggle + rate
//
// SETUP (Game_ML.unity):
//   Canvas
//   └── CmdBar (RectTransform anchored bottom, height 72)
//       ├── Btn_Wall
//       ├── Btn_Runner .. Btn_Golem  (5 unit buttons, each has child Image "AutoBadge")
//       ├── Btn_AutoToggle
//       ├── Btn_Slow / Btn_Normal / Btn_Fast
//
// Unit button long-press (≥0.5s) toggles that unit's autosend membership.
// Short click spawns a unit.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;
using System.Linq;

namespace CastleDefender.UI
{
    public class CmdBar : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Wall")]
        public Button BtnWall;

        [Header("Unit Buttons (Runner, Footman, Ironclad, Warlock, Golem)")]
        public Button[] UnitButtons;
        public Image[]  AutoBadges;     // one per unit button

        [Header("Autosend")]
        public Button   BtnAutoToggle;
        public TMP_Text TxtAutoToggle;
        public Button   BtnSlow;
        public Button   BtnNormal;
        public Button   BtnFast;

        [Header("Colors")]
        public Color ColorActive   = new Color(0.2f, 0.7f, 0.6f);
        public Color ColorInactive = new Color(0.15f, 0.15f, 0.2f);
        public Color ColorAutoOn   = new Color(0.2f, 0.7f, 0.6f);
        public Color ColorAutoOff  = new Color(0.25f, 0.25f, 0.3f);

        // ── Static state (TileGrid reads WallModeActive) ──────────────────────
        public static bool WallModeActive { get; private set; }
        public static event System.Action<bool> OnWallModeChanged;

        // ── Fallback constants (used until catalog loads) ──────────────────────
        static readonly string[] FallbackUnitKeys  = { "runner", "footman", "ironclad", "warlock", "golem" };
        static readonly int[]    FallbackUnitCosts  = { 8, 10, 16, 18, 25 };
        static readonly string[] UnitIconPaths =
        {
            "Icons/units/runner_send_icon",
            "Icons/units/footman_send_icon",
            "Icons/units/ironclad_send_icon",
            "Icons/units/warlock_send_icon",
            "Icons/units/golem_send_icon"
        };

        // ── Catalog-driven state (populated in ApplyCatalog) ──────────────────
        string[] _unitKeys;
        int[]    _unitCosts;

        // ── State ─────────────────────────────────────────────────────────────
        bool   _autoEnabled = false;
        string _autoRate    = "normal";
        Dictionary<string, bool> _autoUnits = new()
        {
            ["runner"]   = false,
            ["footman"]  = false,
            ["ironclad"] = false,
            ["warlock"]  = false,
            ["golem"]    = false,
        };

        Coroutine[] _longPressCoroutines;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            // Subscribe to match config — if it arrives it overrides the catalog (Phase U7)
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnMLMatchConfig += HandleMatchConfig;

            // Apply catalog as fallback; match config will override once match starts
            if (CatalogLoader.IsReady)
                ApplyCatalog();
            else
                CatalogLoader.OnCatalogReady += ApplyCatalog;

            ApplyUnitButtonIcons();
            BtnWall.onClick.AddListener(OnWallClick);

            _longPressCoroutines = new Coroutine[UnitButtons.Length];
            for (int i = 0; i < UnitButtons.Length; i++)
            {
                int idx = i;

                UnitButtons[i].onClick.AddListener(() => OnUnitClick(idx));

                var et = UnitButtons[i].gameObject.AddComponent<EventTrigger>();

                var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                down.callback.AddListener(_ => _longPressCoroutines[idx] =
                    StartCoroutine(LongPressRoutine(idx)));
                et.triggers.Add(down);

                var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
                up.callback.AddListener(_ => CancelLongPress(idx));
                et.triggers.Add(up);

                if (AutoBadges != null && idx < AutoBadges.Length)
                    AutoBadges[idx].gameObject.SetActive(false);
            }

            BtnAutoToggle.onClick.AddListener(OnAutoToggle);
            BtnSlow.onClick.AddListener(()   => SetRate("slow"));
            BtnNormal.onClick.AddListener(() => SetRate("normal"));
            BtnFast.onClick.AddListener(()   => SetRate("fast"));

            RefreshRateButtons();
            RefreshAutoToggle();
        }

        void OnDestroy()
        {
            CatalogLoader.OnCatalogReady -= ApplyCatalog;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnMLMatchConfig -= HandleMatchConfig;
        }

        // ── Match config loadout override (Phase U7) ──────────────────────────
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
            }

            // Rebuild autosend dict with new keys
            var newAuto = new Dictionary<string, bool>();
            foreach (var key in _unitKeys)
                newAuto[key] = _autoUnits.ContainsKey(key) && _autoUnits[key];
            _autoUnits = newAuto;

            for (int i = 0; i < UnitButtons.Length; i++)
                UnitButtons[i].gameObject.SetActive(i < count);

            Debug.Log($"[CmdBar] Match loadout applied — {count} units from ml_match_config");
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

            // Rebuild autosend dict with new keys
            var newAuto = new Dictionary<string, bool>();
            foreach (var key in _unitKeys)
                newAuto[key] = _autoUnits.ContainsKey(key) && _autoUnits[key];
            _autoUnits = newAuto;

            // Show/hide buttons based on catalog count
            for (int i = 0; i < UnitButtons.Length; i++)
                UnitButtons[i].gameObject.SetActive(i < count);

            Debug.Log($"[CmdBar] Catalog applied — {count} unit types");
        }

        void ApplyUnitButtonIcons()
        {
            if (UnitButtons == null) return;
            for (int i = 0; i < UnitButtons.Length && i < UnitIconPaths.Length; i++)
                ApplyIcon(UnitButtons[i], UnitIconPaths[i]);
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
            var lane = SnapshotApplier.Instance?.MyLane;
            if (lane == null || _unitCosts == null) return;

            for (int i = 0; i < UnitButtons.Length && i < _unitCosts.Length; i++)
            {
                bool canAfford = lane.gold >= _unitCosts[i];
                UnitButtons[i].image.color = canAfford
                    ? Color.white
                    : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            }
        }

        // ── Handlers ─────────────────────────────────────────────────────────
        void OnWallClick()
        {
            WallModeActive = !WallModeActive;
            BtnWall.image.color = WallModeActive ? ColorActive : Color.white;
            OnWallModeChanged?.Invoke(WallModeActive);
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void OnUnitClick(int idx)
        {
            if (_unitKeys == null || idx >= _unitKeys.Length) return;
            ActionSender.SpawnUnit(_unitKeys[idx]);
            StartCoroutine(PunchScale(UnitButtons[idx].transform, 0.9f, 0.07f));
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void OnAutoToggle()
        {
            _autoEnabled = !_autoEnabled;
            RefreshAutoToggle();
            SyncAutosend();
            AudioManager.I?.Play(AudioManager.SFX.AutosendToggle);
        }

        void SetRate(string rate)
        {
            _autoRate = rate;
            RefreshRateButtons();
            SyncAutosend();
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        // ── Long press ────────────────────────────────────────────────────────
        IEnumerator LongPressRoutine(int idx)
        {
            yield return new WaitForSeconds(0.5f);
            ToggleAutoUnit(idx);
            _longPressCoroutines[idx] = null;
        }

        void CancelLongPress(int idx)
        {
            if (_longPressCoroutines[idx] != null)
            {
                StopCoroutine(_longPressCoroutines[idx]);
                _longPressCoroutines[idx] = null;
            }
        }

        public void ToggleAutoUnit(int idx)
        {
            if (_unitKeys == null || idx >= _unitKeys.Length) return;
            string type = _unitKeys[idx];
            _autoUnits[type] = !_autoUnits[type];
            if (AutoBadges != null && idx < AutoBadges.Length)
                AutoBadges[idx].gameObject.SetActive(_autoUnits[type]);
            SyncAutosend();
            AudioManager.I?.Play(AudioManager.SFX.AutosendToggle);

            if (AutoBadges != null && idx < AutoBadges.Length)
                StartCoroutine(PunchScale(AutoBadges[idx].transform, 1.3f, 0.1f));
        }

        void SyncAutosend()
            => ActionSender.SetAutosend(_autoEnabled, _autoUnits, _autoRate);

        // ── Visual refresh ────────────────────────────────────────────────────
        void RefreshRateButtons()
        {
            BtnSlow.image.color   = _autoRate == "slow"   ? ColorActive : ColorInactive;
            BtnNormal.image.color = _autoRate == "normal" ? ColorActive : ColorInactive;
            BtnFast.image.color   = _autoRate == "fast"   ? ColorActive : ColorInactive;
        }

        void RefreshAutoToggle()
        {
            BtnAutoToggle.image.color = _autoEnabled ? ColorAutoOn : ColorAutoOff;
            TxtAutoToggle.text        = _autoEnabled ? "AUTO ON" : "AUTO";
        }

        // ── Coroutine helpers ─────────────────────────────────────────────────
        // Scale to target and back (yoyo, one round trip)
        static IEnumerator PunchScale(Transform t, float targetScale, float halfDur)
        {
            Vector3 orig  = t.localScale;
            Vector3 punch = orig * targetScale;

            // Scale toward target
            float elapsed = 0f;
            while (elapsed < halfDur)
            {
                elapsed += Time.deltaTime;
                t.localScale = Vector3.Lerp(orig, punch, Mathf.Clamp01(elapsed / halfDur));
                yield return null;
            }

            // Scale back
            elapsed = 0f;
            while (elapsed < halfDur)
            {
                elapsed += Time.deltaTime;
                t.localScale = Vector3.Lerp(punch, orig, Mathf.Clamp01(elapsed / halfDur));
                yield return null;
            }
            t.localScale = orig;
        }
    }
}
