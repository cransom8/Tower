// LoadoutUI.cs — Loadout slot selection panel + edit modal.
// Place this component on Panel_Loadout in the Lobby scene.
// LobbyUI references it and wires OnConfirmed / OnBack.
//
// SCENE SETUP (Panel_Loadout):
//   Panel_Loadout  (has LoadoutUI component)
//   ├── Btn_Default          — "Default" card (always visible)
//   ├── Btn_Slot0..3         — one per saved slot (hidden if slot doesn't exist)
//   ├── Txt_SlotName0..3     — TMP_Text label inside each slot button
//   ├── Btn_Edit0..3         — pencil icon per slot (opens edit modal)
//   ├── Btn_Confirm          — confirm selection and proceed
//   ├── Btn_Back             — go back to Type step
//   └── Panel_Edit           — edit modal (inactive by default)
//       ├── Input_LoadoutName — TMP_InputField for slot name
//       ├── Dropdown_Unit0..4 — TMP_Dropdown for each of 5 unit slots
//       ├── Btn_SaveEdit
//       └── Btn_CancelEdit

using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class LoadoutUI : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Slot Buttons (0-3)")]
        public Button[]   Btn_Slots;        // 4 buttons, show/hide based on Slots.Count
        public TMP_Text[] Txt_SlotNames;    // label inside each slot button
        public Button[]   Btn_EditSlots;    // pencil icon per slot

        [Header("Default + Confirm + Back")]
        public Button Btn_Default;
        public Button Btn_Confirm;
        public Button Btn_Back;

        [Header("Edit Modal (optional)")]
        public GameObject     Panel_Edit;
        public TMP_InputField Input_LoadoutName;
        public TMP_Dropdown[] Dropdown_Units;   // 5 dropdowns, one per unit slot
        public Button         Btn_SaveEdit;
        public Button         Btn_CancelEdit;

        [Header("Colors")]
        public Color ColorSelected   = new Color(0.2f, 0.7f, 0.3f);
        public Color ColorUnselected = new Color(0.25f, 0.25f, 0.3f);

        // ── Events (LobbyUI subscribes) ───────────────────────────────────────
        public event Action<int> OnConfirmed;   // -1 = default, 0-3 = slot
        public event Action      OnBack;

        // ── State ─────────────────────────────────────────────────────────────
        int _selected  = -1;    // -1 = default
        int _editSlot  = -1;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            Btn_Default.onClick.AddListener(() => SelectSlot(-1));
            Btn_Confirm.onClick.AddListener(OnConfirmClick);
            Btn_Back.onClick.AddListener(OnBackClick);

            for (int i = 0; i < Btn_Slots.Length; i++)
            {
                int idx = i;
                if (Btn_Slots[i] != null)
                    Btn_Slots[i].onClick.AddListener(() => SelectSlot(idx));
                if (Btn_EditSlots != null && idx < Btn_EditSlots.Length && Btn_EditSlots[idx] != null)
                    Btn_EditSlots[idx].onClick.AddListener(() => OpenEdit(idx));
            }

            if (Btn_SaveEdit   != null) Btn_SaveEdit.onClick.AddListener(OnSaveEdit);
            if (Btn_CancelEdit != null) Btn_CancelEdit.onClick.AddListener(CloseEdit);

            if (Panel_Edit != null) Panel_Edit.SetActive(false);
        }

        void OnEnable()
        {
            _selected = LoadoutManager.SelectedSlot;
            RefreshSlots();
        }

        // ── Public ────────────────────────────────────────────────────────────

        /// <summary>Call from LobbyUI when showing this panel to refresh slot labels.</summary>
        public void Refresh()
        {
            _selected = LoadoutManager.SelectedSlot;
            RefreshSlots();
        }

        // ── Slot selection ────────────────────────────────────────────────────
        void SelectSlot(int idx)
        {
            _selected = idx;
            LoadoutManager.SelectedSlot = idx;
            RefreshSlots();
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void RefreshSlots()
        {
            // Show/hide slot buttons based on how many slots are saved
            for (int i = 0; i < Btn_Slots.Length; i++)
            {
                if (Btn_Slots[i] == null) continue;
                bool exists = i < LoadoutManager.Slots.Count;
                Btn_Slots[i].gameObject.SetActive(exists);

                if (exists && Txt_SlotNames != null && i < Txt_SlotNames.Length && Txt_SlotNames[i] != null)
                {
                    var slot = LoadoutManager.Slots[i];
                    Txt_SlotNames[i].text = string.IsNullOrEmpty(slot.name) ? $"Slot {i + 1}" : slot.name;
                }

                if (Btn_EditSlots != null && i < Btn_EditSlots.Length && Btn_EditSlots[i] != null)
                    Btn_EditSlots[i].gameObject.SetActive(exists);

                Btn_Slots[i].image.color = (_selected == i) ? ColorSelected : ColorUnselected;
            }

            if (Btn_Default != null)
                Btn_Default.image.color = (_selected == -1) ? ColorSelected : ColorUnselected;
        }

        // ── Confirm / Back ────────────────────────────────────────────────────
        void OnConfirmClick()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            OnConfirmed?.Invoke(_selected);
        }

        void OnBackClick()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            OnBack?.Invoke();
        }

        // ── Edit modal ────────────────────────────────────────────────────────
        void OpenEdit(int slotIdx)
        {
            if (!AuthManager.IsAuthenticated) return;
            if (Panel_Edit == null) return;

            _editSlot = slotIdx;
            var slot = slotIdx < LoadoutManager.Slots.Count ? LoadoutManager.Slots[slotIdx] : null;

            if (Input_LoadoutName != null)
                Input_LoadoutName.text = slot?.name ?? $"Slot {slotIdx + 1}";

            // Populate unit dropdowns from catalog
            if (Dropdown_Units != null && CatalogLoader.Units.Count > 0)
            {
                var options = CatalogLoader.Units.Select(u => u.name).ToList();
                for (int d = 0; d < Dropdown_Units.Length; d++)
                {
                    if (Dropdown_Units[d] == null) continue;
                    Dropdown_Units[d].ClearOptions();
                    Dropdown_Units[d].AddOptions(options);

                    // Select current unit for this dropdown position
                    int value = 0;
                    if (slot?.unit_type_ids != null && d < slot.unit_type_ids.Length)
                    {
                        // Find index in catalog by id
                        int id = slot.unit_type_ids[d];
                        // Unit catalog entries don't have an id field yet; default to 0
                        value = 0;
                    }
                    Dropdown_Units[d].value = value;
                }
            }

            Panel_Edit.SetActive(true);
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
        }

        void OnSaveEdit()
        {
            if (_editSlot < 0 || Panel_Edit == null) return;

            string name = Input_LoadoutName != null ? Input_LoadoutName.text.Trim() : $"Slot {_editSlot + 1}";
            if (string.IsNullOrEmpty(name)) name = $"Slot {_editSlot + 1}";

            // Build unit_type_ids from dropdown selections
            // (catalog-based ordering; real ids would need catalog id field — use index for now)
            var ids = new int[5];
            if (Dropdown_Units != null)
            {
                for (int d = 0; d < 5 && d < Dropdown_Units.Length; d++)
                    ids[d] = Dropdown_Units[d] != null ? Dropdown_Units[d].value : 0;
            }

            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            StartCoroutine(LoadoutManager.SaveSlot(_editSlot, name, ids, ok =>
            {
                if (ok) RefreshSlots();
                CloseEdit();
            }));
        }

        void CloseEdit()
        {
            if (Panel_Edit != null) Panel_Edit.SetActive(false);
            _editSlot = -1;
        }
    }
}
