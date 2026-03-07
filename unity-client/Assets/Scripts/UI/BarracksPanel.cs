// BarracksPanel.cs — Modal panel for upgrading barracks (Lv 1-4).
//
// SETUP (Game_ML.unity):
//   Canvas
//   └── PanelBarracks (inactive by default)
//       ├── Txt_Title       "Barracks — Lv 2"
//       ├── Txt_Benefits    "HP ×1.15  DMG ×1.10  Speed ×1.00  IncBonus +0.5g"
//       ├── Txt_Cost        "Upgrade cost: 100 gold  (Need 8g/s income)"
//       ├── Txt_Affordance  "Affordable" / "Need more gold" / "Need more income"
//       ├── Btn_Confirm
//       └── Btn_Cancel
//
// Server barracks costs (from BARRACKS_LEVELS):
//   Lv 1→2: 100g, reqIncome  8
//   Lv 2→3: 220g, reqIncome 18
//   Lv 3→4: 400g, reqIncome 35

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Game;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class BarracksPanel : MonoBehaviour
    {
        public GameObject PanelBarracks;
        public TMP_Text   TxtTitle;
        public TMP_Text   TxtBenefits;
        public TMP_Text   TxtCost;
        public TMP_Text   TxtAffordance;
        public Button     BtnConfirm;
        public Button     BtnCancel;

        // Hardcoded fallback (used when CatalogLoader barracks levels not available)
        static readonly BarracksLevelEntry[] FallbackLevels = {
            new BarracksLevelEntry { level=2, upgrade_cost=100, req_income= 8, hp_multiplier=1.15f, dmg_multiplier=1.10f },
            new BarracksLevelEntry { level=3, upgrade_cost=220, req_income=18, hp_multiplier=1.30f, dmg_multiplier=1.20f },
            new BarracksLevelEntry { level=4, upgrade_cost=400, req_income=35, hp_multiplier=1.45f, dmg_multiplier=1.30f },
        };

        System.Collections.Generic.IReadOnlyList<BarracksLevelEntry> Levels =>
            CatalogLoader.BarracksLevels.Count > 0
                ? CatalogLoader.BarracksLevels
                : FallbackLevels;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            PanelBarracks.SetActive(false);
            BtnConfirm.onClick.AddListener(OnConfirm);
            BtnCancel.onClick.AddListener(Hide);
        }

        // ─────────────────────────────────────────────────────────────────────
        public void Show(int currentLevel, float gold, float income)
        {
            if (currentLevel >= 4)
            {
                TxtTitle.text      = "Barracks — Lv 4 (MAX)";
                TxtBenefits.text   = "HP ×1.45  DMG ×1.30  Speed ×1.08  IncBonus +2.0g";
                TxtCost.text       = "Maximum level reached.";
                TxtAffordance.text = "";
                BtnConfirm.interactable = false;
                OpenPanel();
                return;
            }

            int idx = currentLevel - 1;
            if (idx < 0 || idx >= Levels.Count) return;

            var d         = Levels[idx];
            int nextLevel = currentLevel + 1;

            TxtTitle.text    = $"Barracks — Lv {currentLevel} → Lv {nextLevel}";
            TxtBenefits.text = $"HP ×{d.hp_multiplier:0.00}  DMG ×{d.dmg_multiplier:0.00}";
            TxtCost.text     = $"Upgrade cost: {d.upgrade_cost} gold  (Need {d.req_income}g/s income)";

            bool canAfford = gold >= d.upgrade_cost;
            bool hasIncome = income >= d.req_income;

            if (canAfford && hasIncome)
            {
                TxtAffordance.text  = "+ Affordable";
                TxtAffordance.color = new Color(0.3f, 0.9f, 0.4f);
            }
            else if (!canAfford)
            {
                TxtAffordance.text  = $"✗ Need {d.upgrade_cost - Mathf.FloorToInt(gold)} more gold";
                TxtAffordance.color = new Color(0.9f, 0.3f, 0.3f);
            }
            else
            {
                TxtAffordance.text  = $"✗ Need {d.req_income}g/s income (have {income:0.0}g/s)";
                TxtAffordance.color = new Color(0.9f, 0.3f, 0.3f);
            }

            BtnConfirm.interactable = canAfford && hasIncome;
            OpenPanel();
        }

        public void Hide()
        {
            StartCoroutine(ScaleOut(PanelBarracks.transform, 0.15f));
        }

        void OpenPanel()
        {
            PanelBarracks.SetActive(true);
            PanelBarracks.transform.localScale = Vector3.zero;
            StartCoroutine(ScaleIn(PanelBarracks.transform, 0.2f));
        }

        void OnConfirm()
        {
            ActionSender.UpgradeBarracks();
            Hide();
        }

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
            PanelBarracks.SetActive(false);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float tm1 = t - 1f;
            return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        }
    }
}
