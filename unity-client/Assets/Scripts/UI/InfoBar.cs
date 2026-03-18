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
        public TMP_Text TxtGoldTop;
        public TMP_Text TxtIncomeTop;
        public TMP_Text TxtWave;
        public TMP_Text TxtPhase;
        public TMP_Text TxtCountdown;
        public TMP_Text TxtTeamHpLeft;
        public TMP_Text TxtTeamHpRight;

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
        string   _playerSide;
        Coroutine _goldFlash;
        Coroutine _livesFlash;
        Coroutine _barracksFlash;

        const int TickHz = 20;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            // Wire FloatTextAnchor from GameManager's castle tile (avoids Game→UI dependency).
            if (FloatTextAnchor == null)
            {
                var gm = FindFirstObjectByType<CastleDefender.Game.GameManager>();
                if (gm != null) FloatTextAnchor = gm.CastleTileTransform;
            }

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

            int displayHp = ResolveDisplayedHp(snap, lane);
            int displayHpMax = ResolveDisplayedHpMax(snap, lane);

            // HP
            if (displayHp != _prevLives)
            {
                string hpText = displayHpMax > 0 ? $"HP: {displayHp}/{displayHpMax}" : $"HP: {displayHp}";
                if (TxtLives != null) TxtLives.text = hpText;
                if (TxtTeamHpLeft != null && string.IsNullOrEmpty(TxtTeamHpLeft.text))
                    TxtTeamHpLeft.text = hpText;
                if (_prevLives >= 0)
                {
                    if (TxtLives != null)
                    {
                        if (_livesFlash != null) StopCoroutine(_livesFlash);
                        _livesFlash = StartCoroutine(FlashTmpColor(TxtLives, TxtLives.color, Color.red, 0.15f, 2));
                    }
                    AudioManager.I?.Play(AudioManager.SFX.LifeLost);
                    PostProcessController.I?.ImpactFlash();
                    if (FloatTextAnchor != null)
                        FloatingText.Spawn("-1 Life",
                            FloatTextAnchor.position + Vector3.up * 0.5f,
                            FloatingText.Kind.LifeLoss);
                }
                _prevLives = displayHp;
            }

            // Gold (flash border on change)
            if (Mathf.Abs(lane.gold - _prevGold) > 0.01f)
            {
                if (TxtGold != null) TxtGold.text = $"Gold: {Mathf.FloorToInt(lane.gold)}";
                if (TxtGoldTop != null) TxtGoldTop.text = $"Gold {Mathf.FloorToInt(lane.gold)}";
                if (_prevGold >= 0f)
                {
                    float delta = lane.gold - _prevGold;
                    if (GoldBorder != null)
                    {
                        if (_goldFlash != null) StopCoroutine(_goldFlash);
                        _goldFlash = StartCoroutine(FlashGraphicColor(
                            GoldBorder, GoldNormalColor, GoldFlashColor, 0.12f, 2));
                    }

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
            if (TxtIncome != null) TxtIncome.text = $"Inc: {lane.income:0.0}";
            if (TxtIncomeTop != null) TxtIncomeTop.text = $"Inc {lane.income:0.0}";

            // Income ring
            if (ImgIncomeRing != null && snap != null)
                ImgIncomeRing.fillAmount =
                    1f - (float)snap.incomeTicksRemaining / IncomePeriodTicks;

            if (snap != null)
                UpdateMatchStats(snap);

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
                if (TxtBarracksLv != null)
                {
                    TxtBarracksLv.text = $"Lv{lane.barracksLevel}";
                    if (_prevBarracks >= 0)
                    {
                        if (_barracksFlash != null) StopCoroutine(_barracksFlash);
                        _barracksFlash = StartCoroutine(
                            FlashTmpColor(TxtBarracksLv, TxtBarracksLv.color,
                                          new Color(1f, 0.85f, 0.2f), 0.2f, 4));
                    }
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

        void UpdateMatchStats(MLSnapshot snap)
        {
            if (_playerSide == null)
            {
                var sa = SnapshotApplier.Instance;
                if (sa != null)
                {
                    var myLane = sa.MyLane;
                    if (myLane != null)
                        _playerSide = myLane.side;
                }
            }

            if (TxtWave != null)
                TxtWave.text = $"Wave {snap.roundNumber}";

            if (TxtPhase != null)
            {
                TxtPhase.text = snap.roundState switch
                {
                    "build"      => "BUILD",
                    "combat"     => "COMBAT",
                    "transition" => "NEXT WAVE",
                    _            => snap.roundState?.ToUpper() ?? ""
                };
            }

            if (TxtCountdown != null)
            {
                int ticksLeft = snap.roundState == "build" && snap.buildPhaseTotal > 0
                    ? (snap.buildPhaseTotal - snap.roundStateTicks)
                    : snap.roundStateTicks;
                int secs = Mathf.CeilToInt((float)ticksLeft / TickHz);
                TxtCountdown.text = secs > 0 ? $"{secs}s" : "--";
            }

            var hp = snap.teamHp;
            if (hp == null) return;

            if (TxtTeamHpLeft != null)
            {
                TxtTeamHpLeft.text = $"Left Team {hp.left}";
                TxtTeamHpLeft.color = _playerSide == "left"
                    ? new Color(1f, 0.92f, 0.2f)
                    : new Color(1f, 1f, 1f, 0.75f);
            }

            if (TxtTeamHpRight != null)
            {
                TxtTeamHpRight.text = $"Right Team {hp.right}";
                TxtTeamHpRight.color = _playerSide == "right"
                    ? new Color(1f, 0.92f, 0.2f)
                    : new Color(1f, 1f, 1f, 0.75f);
            }
        }

        int ResolveDisplayedHp(MLSnapshot snap, MLLaneSnap lane)
        {
            if (lane == null) return 0;
            if (_playerSide == null && !string.IsNullOrWhiteSpace(lane.side))
                _playerSide = lane.side;

            var hp = snap?.teamHp;
            if (hp != null)
            {
                if (_playerSide == "right") return hp.right;
                if (_playerSide == "left") return hp.left;
            }
            return lane.lives;
        }

        int ResolveDisplayedHpMax(MLSnapshot snap, MLLaneSnap lane)
        {
            if (snap != null && snap.teamHpMax > 0) return snap.teamHpMax;
            return lane != null ? Mathf.Max(lane.lives, 0) : 0;
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
