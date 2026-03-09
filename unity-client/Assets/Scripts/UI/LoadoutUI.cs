// LoadoutUI.cs — Unit picker for match loadout.
// Shows all catalog units as a compact grid of cards (no scroll).
// Player selects up to 5. Hover shows 3D preview via UnitPortraitCamera.
// LobbyUI subscribes: OnConfirmed(int[] unitTypeIds) — null means use server defaults.
//
// SCENE SETUP (built by SetupLoadoutPanel + SetupPortraitStudio):
//   Panel_Loadout  (LoadoutUI component)
//   ├── Txt_Title
//   ├── Panel_Body  (HorizontalLayoutGroup)
//   │   ├── Panel_Catalog  (ScrollRect)
//   │   │   └── Viewport / Content  (RectTransform + GridLayoutGroup — cards spawned at runtime)
//   │   └── Panel_Preview
//   │       ├── Img_Portrait  (RawImage — shows RenderTexture)
//   │       ├── Txt_PreviewName
//   │       └── Txt_PreviewStats
//   ├── Panel_Slots  (HorizontalLayoutGroup — 5 slot buttons)
//   │   └── Btn_Slot0..4
//   └── Panel_Buttons
//       ├── Btn_Confirm
//       └── Btn_Back

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class LoadoutUI : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Portrait Camera")]
        public UnitPortraitCamera PortraitCam;
        public RawImage           Img_Portrait;

        [Header("Preview Info")]
        public TMP_Text Txt_PreviewName;
        public TMP_Text Txt_PreviewStats;

        [Header("Catalog Scroll View")]
        public RectTransform CatalogContent;   // Content RectTransform inside ScrollRect

        [Header("Selected Slots (5)")]
        public Button[]   SlotButtons;         // 5 buttons
        public TMP_Text[] SlotLabels;          // label inside each slot button

        [Header("Action Buttons")]
        public Button Btn_Confirm;
        public Button Btn_Back;

        [Header("Colors")]
        public Color ColorSelected   = new Color(0.20f, 0.70f, 0.30f);
        public Color ColorUnselected = new Color(0.22f, 0.22f, 0.28f);
        public Color ColorEmpty      = new Color(0.13f, 0.13f, 0.18f);

        [Header("Card Grid Layout")]
        public float CardWidth   = 130f;
        public float CardHeight  = 72f;
        public float CardSpacing = 6f;
        public int   CardColumns = 3;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<int[]> OnConfirmed;   // null = use server defaults
        public event Action        OnBack;

        // ── State ─────────────────────────────────────────────────────────────
        int[]                    _selected = new int[5] { -1, -1, -1, -1, -1 };
        List<UnitCatalogEntry>   _units    = new();
        readonly List<GameObject> _cards   = new();

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            if (Btn_Confirm != null) Btn_Confirm.onClick.AddListener(OnConfirmClick);
            if (Btn_Back    != null) Btn_Back.onClick.AddListener(OnBackClick);

            for (int i = 0; i < (SlotButtons?.Length ?? 0); i++)
            {
                int idx = i;
                if (SlotButtons[i] != null)
                    SlotButtons[i].onClick.AddListener(() => ClearSlot(idx));
            }
        }

        void OnEnable()
        {
            _selected = new int[5] { -1, -1, -1, -1, -1 };

            if (CatalogLoader.IsReady)
                BuildCatalog();
            else
                CatalogLoader.OnCatalogReady += BuildCatalog;

            RefreshSlotDisplay();
        }

        void OnDisable()
        {
            CatalogLoader.OnCatalogReady -= BuildCatalog;
            PortraitCam?.Clear();
        }

        // ── Public ────────────────────────────────────────────────────────────
        public void Refresh()
        {
            _selected = new int[5] { -1, -1, -1, -1, -1 };
            BuildCatalog();
            RefreshSlotDisplay();
        }

        // ── Catalog building ──────────────────────────────────────────────────
        void BuildCatalog()
        {
            CatalogLoader.OnCatalogReady -= BuildCatalog;

            foreach (var c in _cards) if (c != null) Destroy(c);
            _cards.Clear();

            if (CatalogContent == null) return;

            _units = new List<UnitCatalogEntry>(CatalogLoader.Units);
            var enabled = _units.FindAll(u => u.enabled);
            if (enabled.Count > 0) _units = enabled;

            // Ensure GridLayoutGroup exists on the content transform
            var grid = CatalogContent.GetComponent<GridLayoutGroup>();
            if (grid == null) grid = CatalogContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize         = new Vector2(CardWidth, CardHeight);
            grid.spacing          = new Vector2(CardSpacing, CardSpacing);
            grid.padding          = new RectOffset(4, 4, 4, 4);
            grid.constraint       = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount  = CardColumns;
            grid.childAlignment   = TextAnchor.UpperLeft;

            // ContentSizeFitter so the content rect grows to fit all cards
            var csf = CatalogContent.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = CatalogContent.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var unit in _units)
            {
                var card = CreateCard(unit);
                card.transform.SetParent(CatalogContent, false);
                _cards.Add(card);
            }

            RefreshCardColors();

            if (PortraitCam != null)
                StartCoroutine(FillCardIcons());
        }

        IEnumerator FillCardIcons()
        {
            // Give layout one frame to settle before rendering portraits
            yield return null;

            for (int i = 0; i < _cards.Count && i < _units.Count; i++)
            {
                var card = _cards[i];
                if (card == null) continue;
                var raw = card.transform.Find("Icon")?.GetComponent<RawImage>();
                if (raw == null) continue;

                string key = _units[i].key;
                bool done = false;
                PortraitCam.StartIconCapture(key, tex =>
                {
                    if (tex != null && raw != null)
                    {
                        raw.texture = tex;
                        raw.color   = Color.white;
                    }
                    done = true;
                });
                while (!done) yield return null;
            }
        }

        GameObject CreateCard(UnitCatalogEntry unit)
        {
            var go = new GameObject(unit.key);
            go.AddComponent<RectTransform>();

            // Rounded-corner look via a slightly darker border image behind
            var border = go.AddComponent<Image>();
            border.color = new Color(0.10f, 0.10f, 0.14f);

            var btn = go.AddComponent<Button>();
            // Disable the default button color transition so we control color manually
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.95f);
            colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f);
            colors.fadeDuration     = 0.05f;
            btn.colors = colors;

            string key = unit.key;
            int    id  = unit.id;
            btn.onClick.AddListener(() => { SelectUnit(key, id); });

            // Hover → update large preview
            var trig = go.AddComponent<EventTrigger>();
            var hoverEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hoverEntry.callback.AddListener(_ => PreviewUnit(key));
            trig.triggers.Add(hoverEntry);

            // Inner fill (color changes with selection)
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(go.transform, false);
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = new Vector2(2f, 2f);
            fillRT.offsetMax = new Vector2(-2f, -2f);
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.color = ColorUnselected;
            fillImg.raycastTarget = false;

            // Icon — left 38% of card, filled via UnitPortraitCamera capture coroutine
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            var iconRT = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = new Vector2(0.38f, 1f);
            iconRT.offsetMin = new Vector2(4f, 4f);
            iconRT.offsetMax = new Vector2(-2f, -4f);
            var iconImg = iconGO.AddComponent<RawImage>();
            iconImg.color        = new Color(0.3f, 0.3f, 0.4f, 0.5f); // placeholder until portrait is captured
            iconImg.raycastTarget = false;

            // Unit name (top-right portion of card)
            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(go.transform, false);
            var nameRT = nameGO.AddComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0.38f, 0.45f);
            nameRT.anchorMax = new Vector2(1f, 1f);
            nameRT.offsetMin = new Vector2(4f, 0f);
            nameRT.offsetMax = new Vector2(-6f, -4f);
            var nameTxt = nameGO.AddComponent<TextMeshProUGUI>();
            nameTxt.text         = unit.name;
            nameTxt.fontSize     = 13f;
            nameTxt.fontStyle    = FontStyles.Bold;
            nameTxt.color        = Color.white;
            nameTxt.alignment    = TextAlignmentOptions.MidlineLeft;
            nameTxt.overflowMode = TextOverflowModes.Ellipsis;
            nameTxt.raycastTarget = false;

            // Stats (bottom-right portion)
            var statsGO = new GameObject("Stats");
            statsGO.transform.SetParent(go.transform, false);
            var statsRT = statsGO.AddComponent<RectTransform>();
            statsRT.anchorMin = new Vector2(0.38f, 0f);
            statsRT.anchorMax = new Vector2(1f, 0.48f);
            statsRT.offsetMin = new Vector2(4f, 2f);
            statsRT.offsetMax = new Vector2(-6f, 0f);
            var statsTxt = statsGO.AddComponent<TextMeshProUGUI>();
            statsTxt.text      = $"HP {unit.hp:0}  {unit.send_cost}g";
            statsTxt.fontSize  = 10f;
            statsTxt.color     = new Color(0.75f, 0.75f, 0.75f);
            statsTxt.alignment = TextAlignmentOptions.MidlineLeft;
            statsTxt.raycastTarget = false;

            // Selected checkmark (top-right corner, over everything)
            var selGO = new GameObject("SelMark");
            selGO.transform.SetParent(go.transform, false);
            var selRT = selGO.AddComponent<RectTransform>();
            selRT.anchorMin = new Vector2(0.7f, 0.55f);
            selRT.anchorMax = new Vector2(1f, 1f);
            selRT.offsetMin = Vector2.zero;
            selRT.offsetMax = new Vector2(-3f, -3f);
            var selTxt = selGO.AddComponent<TextMeshProUGUI>();
            selTxt.name          = "SelMark";
            selTxt.text          = "";
            selTxt.fontSize      = 16f;
            selTxt.color         = ColorSelected;
            selTxt.alignment     = TextAlignmentOptions.TopRight;
            selTxt.raycastTarget = false;

            return go;
        }

        // ── Selection logic ───────────────────────────────────────────────────
        void SelectUnit(string key, int id)
        {
            // Toggle off if already selected
            for (int i = 0; i < _selected.Length; i++)
            {
                if (_selected[i] == id) { ClearSlot(i); return; }
            }
            // Find first empty slot
            for (int i = 0; i < _selected.Length; i++)
            {
                if (_selected[i] == -1)
                {
                    _selected[i] = id;
                    RefreshSlotDisplay();
                    RefreshCardColors();
                    PreviewUnit(key);
                    AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
                    return;
                }
            }
            // All full — replace last slot
            _selected[4] = id;
            RefreshSlotDisplay();
            RefreshCardColors();
            PreviewUnit(key);
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void ClearSlot(int idx)
        {
            if (idx < 0 || idx >= _selected.Length) return;
            _selected[idx] = -1;
            // Compact: shift left
            for (int i = idx; i < _selected.Length - 1; i++)
                _selected[i] = _selected[i + 1];
            _selected[_selected.Length - 1] = -1;
            RefreshSlotDisplay();
            RefreshCardColors();
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        // ── Display ───────────────────────────────────────────────────────────
        void RefreshSlotDisplay()
        {
            for (int i = 0; i < (SlotButtons?.Length ?? 0); i++)
            {
                if (SlotButtons[i] == null) continue;
                bool filled = _selected[i] != -1;

                if (SlotLabels != null && i < SlotLabels.Length && SlotLabels[i] != null)
                {
                    if (filled)
                    {
                        var u = _units?.Find(x => x.id == _selected[i]);
                        SlotLabels[i].text = u != null ? u.name : "?";
                    }
                    else
                    {
                        SlotLabels[i].text = $"Slot {i + 1}";
                    }
                }

                SlotButtons[i].image.color = filled ? ColorSelected : ColorEmpty;
            }
        }

        void RefreshCardColors()
        {
            var selSet = new HashSet<int>(_selected);
            for (int i = 0; i < _cards.Count && i < _units.Count; i++)
            {
                if (_cards[i] == null) continue;
                bool selected = selSet.Contains(_units[i].id);

                // Color the inner fill child
                var fill = _cards[i].transform.Find("Fill")?.GetComponent<Image>();
                if (fill != null)
                    fill.color = selected ? ColorSelected : ColorUnselected;

                // Fallback: color root image if no Fill child
                else
                {
                    var img = _cards[i].GetComponent<Image>();
                    if (img != null) img.color = selected ? ColorSelected : ColorUnselected;
                }

                // Update tick mark
                var selMark = _cards[i].transform.Find("SelMark")?.GetComponent<TMP_Text>();
                if (selMark != null)
                    selMark.text = selected ? "✓" : "";
            }
        }

        void PreviewUnit(string key)
        {
            PortraitCam?.ShowUnit(key);
            if (!CatalogLoader.UnitByKey.TryGetValue(key, out var u)) return;
            if (Txt_PreviewName  != null) Txt_PreviewName.text  = u.name;
            if (Txt_PreviewStats != null)
            {
                Txt_PreviewStats.text =
                    $"<b>HP:</b> {u.hp:0}\n" +
                    $"<b>Send Cost:</b> {u.send_cost}\n" +
                    $"<b>Build Cost:</b> {u.build_cost}\n" +
                    $"<b>Speed:</b> {u.path_speed * 100f:0.0}";
            }
        }

        // ── Confirm / Back ────────────────────────────────────────────────────
        void OnConfirmClick()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            bool full = true;
            foreach (int id in _selected) if (id == -1) { full = false; break; }
            OnConfirmed?.Invoke(full ? (int[])_selected.Clone() : null);
        }

        void OnBackClick()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            OnBack?.Invoke();
        }
    }
}
