// InfoBar.cs — Top HUD bar: lives | gold | income ring | barracks button
//
// SETUP (Game_ML.unity):
//   Canvas (Screen Space Overlay)
//   └── InfoBar (RectTransform anchored top, height 44)
//       ├── Txt_Lives
//       ├── Txt_Gold
//       ├── Txt_Income
//       ├── Img_IncomeRing   (Image, Filled, Radial360)
//       ├── Txt_BarracksLv
//       └── Btn_Barracks     → opens BarracksPanel

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class InfoBar : MonoBehaviour
    {
        [Header("Text fields")]
        public TMP_Text TxtLives;
        public TMP_Text TxtGold;
        public TMP_Text TxtIncome;
        public TMP_Text TxtBarracksLv;

        [Header("Income ring")]
        public Image ImgIncomeRing;
        public int IncomePeriodTicks = 240;

        [Header("Barracks button")]
        public Button        BtnBarracks;
        public BarracksPanel BarracksPanel;

        [Header("Gold flash")]
        public Graphic GoldBorder;
        public Color   GoldFlashColor  = new Color(1f, 0.9f, 0.2f, 1f);
        public Color   GoldNormalColor = new Color(0.6f, 0.5f, 0.1f, 0.6f);

        [Header("Branch identity (optional — Phase 4)")]
        [Tooltip("If assigned, shows the player's branch name and defending side (e.g. 'Red Branch  [left]').")]
        public TMP_Text TxtBranchLabel;

        [Header("FX anchors (canvas space)")]
        public RectTransform GoldPopAnchor;
        public Transform     FloatTextAnchor;

        // ── State ─────────────────────────────────────────────────────────────
        float    _prevGold     = -1f;
        int      _prevLives    = -1;
        int      _prevBarracks = -1;
        bool     _branchLabelSet;
        Coroutine _goldFlash;
        Coroutine _livesFlash;
        Coroutine _barracksFlash;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            if (BtnBarracks != null)
            {
                // Defensive: scene wiring can drift; keep barracks button usable.
                BtnBarracks.gameObject.SetActive(true);
                BtnBarracks.interactable = true;
                BtnBarracks.onClick.AddListener(OnBarracksClick);
            }

            // Prevent layout overlap between income text and barracks controls.
            AutoFixBarracksLayout();
        }

        void Update()
        {
            var snap = SnapshotApplier.Instance?.LatestML;
            var lane = SnapshotApplier.Instance?.MyLane;
            if (lane == null) return;

            // Lives
            if (lane.lives != _prevLives)
            {
                TxtLives.text = $"HP: {lane.lives}";
                if (_prevLives >= 0)
                {
                    if (_livesFlash != null) StopCoroutine(_livesFlash);
                    _livesFlash = StartCoroutine(FlashTmpColor(TxtLives, TxtLives.color, Color.red, 0.15f, 2));
                    AudioManager.I?.Play(AudioManager.SFX.LifeLost);
                    PostProcessController.I?.ImpactFlash();
                    if (FloatTextAnchor != null)
                        FloatingText.Spawn("-1 Life",
                            FloatTextAnchor.position + Vector3.up * 0.5f,
                            FloatingText.Kind.LifeLoss);
                }
                _prevLives = lane.lives;
            }

            // Gold (flash border on change)
            if (Mathf.Abs(lane.gold - _prevGold) > 0.01f)
            {
                TxtGold.text = $"G: {Mathf.FloorToInt(lane.gold)}";
                if (_prevGold >= 0f)
                {
                    float delta = lane.gold - _prevGold;
                    if (_goldFlash != null) StopCoroutine(_goldFlash);
                    _goldFlash = StartCoroutine(FlashGraphicColor(
                        GoldBorder, GoldNormalColor, GoldFlashColor, 0.12f, 2));

                    if (delta > 0f)
                    {
                        AudioManager.I?.Play(AudioManager.SFX.GoldGain, 0.7f);
                        if (GoldPopAnchor != null)
                            GoldPop.Spawn(GoldPopAnchor.parent, GoldPopAnchor.anchoredPosition, delta);
                    }
                    else
                    {
                        AudioManager.I?.Play(AudioManager.SFX.GoldSpend, 0.5f);
                    }
                }
                _prevGold = lane.gold;
            }

            // Income
            TxtIncome.text = $"Inc: {lane.income:0.0}";

            // Income ring
            if (ImgIncomeRing != null && snap != null)
                ImgIncomeRing.fillAmount =
                    1f - (float)snap.incomeTicksRemaining / IncomePeriodTicks;

            // Branch identity — set once when assignment data first arrives
            if (TxtBranchLabel != null && !_branchLabelSet)
            {
                var sa     = SnapshotApplier.Instance;
                var assign = sa?.GetLaneAssignment(sa.MyLaneIndex);
                if (assign != null)
                {
                    string lbl  = !string.IsNullOrWhiteSpace(assign.branchLabel)
                                  ? assign.branchLabel
                                  : $"Lane {sa.MyLaneIndex + 1}";
                    string side = !string.IsNullOrWhiteSpace(assign.castleSide)
                                  ? assign.castleSide
                                  : (assign.side ?? "");
                    TxtBranchLabel.text = string.IsNullOrWhiteSpace(side)
                                         ? lbl
                                         : $"{lbl}  [{side}]";
                    if (SnapshotApplier.TryResolveSlotColor(assign.slotColor, out var slotCol))
                        TxtBranchLabel.color = slotCol;
                    _branchLabelSet = true;
                }
            }

            // Barracks level
            if (lane.barracksLevel != _prevBarracks)
            {
                TxtBarracksLv.text = $"Barracks Lv {lane.barracksLevel}";
                if (_prevBarracks >= 0)
                {
                    if (_barracksFlash != null) StopCoroutine(_barracksFlash);
                    _barracksFlash = StartCoroutine(
                        FlashTmpColor(TxtBarracksLv, TxtBarracksLv.color,
                                      new Color(1f, 0.85f, 0.2f), 0.2f, 4));
                }
                _prevBarracks = lane.barracksLevel;
            }
        }

        void OnBarracksClick()
        {
            var lane = SnapshotApplier.Instance?.MyLane;
            if (lane == null) return;
            BarracksPanel?.Show(lane.barracksLevel, lane.gold, lane.income);
        }

        void AutoFixBarracksLayout()
        {
            if (TxtIncome == null || BtnBarracks == null) return;

            var incomeRt   = TxtIncome.rectTransform;
            var barracksRt = BtnBarracks.GetComponent<RectTransform>();
            if (incomeRt == null || barracksRt == null) return;

            float minSpacing = 140f;
            float dx = Mathf.Abs(barracksRt.anchoredPosition.x - incomeRt.anchoredPosition.x);
            if (dx >= minSpacing) return;

            float targetX = incomeRt.anchoredPosition.x + 200f;
            barracksRt.anchoredPosition = new Vector2(targetX, barracksRt.anchoredPosition.y);

            if (TxtBarracksLv != null)
            {
                var lvRt = TxtBarracksLv.rectTransform;
                lvRt.anchoredPosition = new Vector2(targetX - 95f, lvRt.anchoredPosition.y);
            }
        }

        // ── Coroutine helpers ─────────────────────────────────────────────────

        // Yoyo between normalColor and flashColor for `halfLoops` half-steps
        static IEnumerator FlashTmpColor(TMP_Text label, Color normalColor, Color flashColor,
                                          float halfDur, int halfLoops)
        {
            for (int i = 0; i < halfLoops; i++)
            {
                Color from = (i % 2 == 0) ? normalColor : flashColor;
                Color to   = (i % 2 == 0) ? flashColor  : normalColor;
                float t = 0f;
                while (t < halfDur)
                {
                    t += Time.deltaTime;
                    label.color = Color.Lerp(from, to, Mathf.Clamp01(t / halfDur));
                    yield return null;
                }
                label.color = to;
            }
            label.color = normalColor;
        }

        static IEnumerator FlashGraphicColor(Graphic g, Color normalColor, Color flashColor,
                                              float halfDur, int halfLoops)
        {
            if (g == null) yield break;
            for (int i = 0; i < halfLoops; i++)
            {
                Color from = (i % 2 == 0) ? normalColor : flashColor;
                Color to   = (i % 2 == 0) ? flashColor  : normalColor;
                float t = 0f;
                while (t < halfDur)
                {
                    t += Time.deltaTime;
                    g.color = Color.Lerp(from, to, Mathf.Clamp01(t / halfDur));
                    yield return null;
                }
                g.color = to;
            }
            g.color = normalColor;
        }
    }
}
