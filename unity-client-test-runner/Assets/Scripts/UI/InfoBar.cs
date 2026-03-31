// InfoBar.cs — Top HUD bar: Town Core HP | gold | income ring | barracks button
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
        const string RuntimeBarracksButtonName = "RuntimeBarracksButton";
        const string RuntimeBarracksPanelHostName = "RuntimeBarracksPanelHost";

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
        bool _barracksButtonHooked;
        Button _hookedBarracksButton;

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

            EnsureBarracksUiBootstrapped();

            // Prevent layout overlap between income text and barracks controls.
            AutoFixBarracksLayout();
        }

        void Update()
        {
            EnsureBarracksUiBootstrapped();
            var snapshotApplier = SnapshotApplier.Instance;
            var snap = snapshotApplier?.LatestML;
            var lane = snapshotApplier?.MyLane;
            if (lane == null) return;

            if (snapshotApplier?.LatestMLMatchConfig != null && snapshotApplier.LatestMLMatchConfig.incomeIntervalTicks > 0)
                IncomePeriodTicks = snapshotApplier.LatestMLMatchConfig.incomeIntervalTicks;

            int displayHp = ResolveDisplayedHp(snap, lane);
            int displayHpMax = ResolveDisplayedHpMax(snap, lane);

            // HP
            if (displayHp != _prevLives)
            {
                string hpText = displayHpMax > 0 ? $"Core HP: {displayHp}/{displayHpMax}" : $"Core HP: {displayHp}";
                if (TxtLives != null) TxtLives.text = hpText;
                if (TxtTeamHpLeft != null && string.IsNullOrEmpty(TxtTeamHpLeft.text))
                    TxtTeamHpLeft.text = hpText;
                if (_prevLives >= 0)
                {
                    int hpLost = Mathf.Max(0, _prevLives - displayHp);
                    if (hpLost > 0 && TxtLives != null)
                    {
                        if (_livesFlash != null) StopCoroutine(_livesFlash);
                        _livesFlash = StartCoroutine(FlashTmpColor(TxtLives, TxtLives.color, Color.red, 0.15f, 2));
                    }
                    if (hpLost > 0)
                    {
                        AudioManager.I?.Play(AudioManager.SFX.LifeLost);
                        PostProcessController.I?.ImpactFlash();
                        if (FloatTextAnchor != null)
                            FloatingText.Spawn($"-{hpLost} Core HP",
                                FloatTextAnchor.position + Vector3.up * 0.5f,
                                FloatingText.Kind.LifeLoss);
                    }
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
            EnsureBarracksUiBootstrapped();
            var lane = SnapshotApplier.Instance?.MyLane;
            if (lane == null) return;
            BarracksPanel?.Show();
        }

        void EnsureBarracksUiBootstrapped()
        {
            if (BarracksPanel == null)
                BarracksPanel = FindOrCreateRuntimeBarracksPanel();

            if (BtnBarracks == null || TxtBarracksLv == null)
                EnsureRuntimeBarracksButton();

            if (BtnBarracks == null)
                return;

            BtnBarracks.gameObject.SetActive(true);
            BtnBarracks.interactable = true;
            if (_hookedBarracksButton != BtnBarracks)
            {
                if (_hookedBarracksButton != null)
                    _hookedBarracksButton.onClick.RemoveListener(OnBarracksClick);

                _hookedBarracksButton = BtnBarracks;
                _barracksButtonHooked = false;
            }

            bool shouldOwnBarracksClick = string.Equals(BtnBarracks.name, RuntimeBarracksButtonName);
            if (!shouldOwnBarracksClick)
            {
                BtnBarracks.onClick.RemoveListener(OnBarracksClick);
                _barracksButtonHooked = false;
                return;
            }

            if (!_barracksButtonHooked)
            {
                BtnBarracks.onClick.RemoveListener(OnBarracksClick);
                BtnBarracks.onClick.AddListener(OnBarracksClick);
                _barracksButtonHooked = true;
            }
        }

        BarracksPanel FindOrCreateRuntimeBarracksPanel()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return null;

            var existing = canvas.transform.Find(RuntimeBarracksPanelHostName);
            GameObject host;
            if (existing != null)
            {
                host = existing.gameObject;
            }
            else
            {
                host = new GameObject(RuntimeBarracksPanelHostName, typeof(RectTransform));
                host.transform.SetParent(canvas.transform, false);
                var rect = host.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }

            var panel = host.GetComponent<BarracksPanel>();
            if (panel == null)
                panel = host.AddComponent<BarracksPanel>();
            return panel;
        }

        void EnsureRuntimeBarracksButton()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return;

            Transform existing = canvas.transform.Find(RuntimeBarracksButtonName);
            Button button;
            if (existing != null)
            {
                button = existing.GetComponent<Button>();
            }
            else
            {
                var buttonGo = new GameObject(RuntimeBarracksButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
                buttonGo.transform.SetParent(canvas.transform, false);
                var rect = buttonGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(196f, 52f);
                rect.anchoredPosition = new Vector2(0f, -86f);

                var image = buttonGo.GetComponent<Image>();
                image.color = new Color(0.18f, 0.30f, 0.22f, 0.94f);
                button = buttonGo.GetComponent<Button>();

                var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
                titleGo.transform.SetParent(buttonGo.transform, false);
                var titleRect = titleGo.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0f, 0.45f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.offsetMin = new Vector2(10f, -4f);
                titleRect.offsetMax = new Vector2(-10f, 0f);
                var title = titleGo.GetComponent<TextMeshProUGUI>();
                title.font = TMP_Settings.defaultFontAsset;
                title.fontSize = 20f;
                title.fontStyle = FontStyles.Bold;
                title.alignment = TextAlignmentOptions.Center;
                title.color = Color.white;
                title.text = "Progression";

                var levelGo = new GameObject("Level", typeof(RectTransform), typeof(TextMeshProUGUI));
                levelGo.transform.SetParent(buttonGo.transform, false);
                var levelRect = levelGo.GetComponent<RectTransform>();
                levelRect.anchorMin = new Vector2(0f, 0f);
                levelRect.anchorMax = new Vector2(1f, 0.5f);
                levelRect.offsetMin = new Vector2(10f, 4f);
                levelRect.offsetMax = new Vector2(-10f, 0f);
                TxtBarracksLv = levelGo.GetComponent<TextMeshProUGUI>();
                TxtBarracksLv.font = TMP_Settings.defaultFontAsset;
                TxtBarracksLv.fontSize = 15f;
                TxtBarracksLv.alignment = TextAlignmentOptions.Center;
                TxtBarracksLv.color = new Color(0.96f, 0.91f, 0.60f, 0.98f);
                TxtBarracksLv.text = "Tech Tree";
            }

            BtnBarracks = button;
            BtnBarracks.transform.SetAsLastSibling();
            if (TxtBarracksLv == null)
            {
                var level = BtnBarracks.transform.Find("Level");
                if (level != null)
                    TxtBarracksLv = level.GetComponent<TextMeshProUGUI>();
            }

            var titleLabel = BtnBarracks.transform.Find("Title");
            if (titleLabel != null && titleLabel.TryGetComponent<TextMeshProUGUI>(out var titleText))
                titleText.text = "Progression";
            if (TxtBarracksLv != null)
                TxtBarracksLv.text = "Tech Tree";

            _barracksButtonHooked = false;
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
                var snapshotApplier = SnapshotApplier.Instance;
                if (snapshotApplier != null)
                {
                    var myLane = snapshotApplier.MyLane;
                    if (myLane != null)
                        _playerSide = myLane.side;
                }
            }

            if (TxtWave != null)
                TxtWave.text = $"Wave {snap.roundNumber}";

            var snapshotApplierInstance = SnapshotApplier.Instance;
            var lane = snapshotApplierInstance != null ? snapshotApplierInstance.MyLane : null;
            int waveSeconds = snapshotApplierInstance != null ? snapshotApplierInstance.GetWaveTimerSecondsRemaining() : 0;
            int sendSeconds = lane != null && snapshotApplierInstance != null ? snapshotApplierInstance.GetBarracksSendSecondsRemaining(lane.laneIndex) : 0;

            if (TxtPhase != null)
                TxtPhase.text = "LIVE";

            if (TxtCountdown != null)
                TxtCountdown.text = lane != null
                    ? $"Wave {waveSeconds}s  Send {sendSeconds}s"
                    : $"Wave {waveSeconds}s";

            var hp = snap.teamHp;
            if (hp == null) return;

            if (TxtTeamHpLeft != null)
            {
                TxtTeamHpLeft.text = $"Left Side {hp.left}";
                TxtTeamHpLeft.color = _playerSide == "left"
                    ? new Color(1f, 0.92f, 0.2f)
                    : new Color(1f, 1f, 1f, 0.75f);
            }

            if (TxtTeamHpRight != null)
            {
                TxtTeamHpRight.text = $"Right Side {hp.right}";
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

            if (SnapshotApplier.Instance != null
                && SnapshotApplier.Instance.TryGetTownCoreHp(lane.laneIndex, out int currentHp, out _))
                return currentHp;

            return lane.lives;
        }

        int ResolveDisplayedHpMax(MLSnapshot snap, MLLaneSnap lane)
        {
            if (lane != null
                && SnapshotApplier.Instance != null
                && SnapshotApplier.Instance.TryGetTownCoreHp(lane.laneIndex, out _, out int maxHp)
                && maxHp > 0)
                return maxHp;

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
