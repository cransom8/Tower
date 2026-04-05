// PostGameStatsPanel.cs - Player-facing post-game battle report panel.
//
// SETUP:
//   PanelPostGameStats (inactive by default, sibling of PanelGameOver)
//   ├── PanelHeader
//   │   ├── Btn_Tab_Summary, Btn_Tab_Economy, Btn_Tab_Build, Btn_Tab_Threat, Btn_Tab_Story, Btn_Tab_Waves
//   │   └── Btn_Close
//   ├── PanelSummary   (active by default)
//   ├── PanelEconomy   (inactive - economy curve)
//   ├── PanelBuild     (inactive - army curve)
//   ├── PanelThreat    (inactive - threat curve)
//   ├── PanelStory     (inactive - battle story)
//   └── PanelWaves     (inactive - advanced summary)
//
// Wire all inspector references in the Unity Editor.

using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class PostGameStatsPanel : MonoBehaviour
    {
        [Header("Root")]
        public GameObject PanelRoot;

        [Header("Tab buttons")]
        public Button Btn_Tab_Summary;
        public Button Btn_Tab_Economy;
        public Button Btn_Tab_Build;
        public Button Btn_Tab_Threat;
        public Button Btn_Tab_Story;
        public Button Btn_Tab_Waves;
        public Button Btn_Close;

        [Header("Tab panels")]
        public GameObject PanelSummary;
        public GameObject PanelEconomy;
        public GameObject PanelBuild;
        public GameObject PanelThreat;
        public GameObject PanelStory;
        public GameObject PanelWaves;

        [Header("Summary — one TMP_Text per lane (up to 4)")]
        public TMP_Text[] SummaryRows;   // populated programmatically
        public TMP_Text SummaryBodyText;

        [Header("Charts")]
        public LineGraphUI EconomyGraph;
        public LineGraphUI BuildGraph;
        public LineGraphUI ThreatGraph;

        [Header("Waves tab")]
        public Transform   WaveRowContainer;
        public GameObject  WaveRowPrefab;
        public TMP_Text    WavesBodyText;

        // ── State ─────────────────────────────────────────────────────────────

        private MLGameOverPayload _payload;
        private CommanderBattleReport _report;
        private bool _economyPopulated;
        private bool _armyPopulated;
        private bool _threatPopulated;
        private bool _storyPopulated;
        private bool _advancedPopulated;
        private TMP_Text _economyBodyText;
        private TMP_Text _armyBodyText;
        private TMP_Text _threatBodyText;
        private TMP_Text _storyBodyText;
        private bool _economyLayoutBuilt;
        private bool _armyLayoutBuilt;
        private bool _threatLayoutBuilt;
        private bool _storyLayoutBuilt;
        private ScrollRect _waveScroll;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            if (PanelRoot != null) PanelRoot.SetActive(false);

            if (Btn_Tab_Summary != null) Btn_Tab_Summary.onClick.AddListener(() => SwitchTab(0));
            if (Btn_Tab_Economy != null) Btn_Tab_Economy.onClick.AddListener(() => SwitchTab(1));
            if (Btn_Tab_Build != null) Btn_Tab_Build.onClick.AddListener(() => SwitchTab(2));
            if (Btn_Tab_Threat != null) Btn_Tab_Threat.onClick.AddListener(() => SwitchTab(3));
            if (Btn_Tab_Story != null) Btn_Tab_Story.onClick.AddListener(() => SwitchTab(4));
            if (Btn_Tab_Waves != null) Btn_Tab_Waves.onClick.AddListener(() => SwitchTab(5));
            if (Btn_Close != null) Btn_Close.onClick.AddListener(Hide);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show(MLGameOverPayload payload)
        {
            bool canAnimate = isActiveAndEnabled;
            _payload          = payload;
            _report           = ResolveCommanderReport(payload);
            _economyPopulated = false;
            _armyPopulated    = false;
            _threatPopulated  = false;
            _storyPopulated   = false;
            _advancedPopulated = false;
            SetButtonLabel(Btn_Tab_Summary, "Summary");
            SetButtonLabel(Btn_Tab_Economy, "Economy");
            SetButtonLabel(Btn_Tab_Build, "Army");
            SetButtonLabel(Btn_Tab_Threat, "Threat");
            SetButtonLabel(Btn_Tab_Story, "Story");
            SetButtonLabel(Btn_Tab_Waves, "Advanced");
            bool hasPlayerReport = _report != null;
            bool hasAdvanced = hasPlayerReport || (payload?.waveSnapshots != null && payload.waveSnapshots.Length > 0) || (payload?.balanceDiagnosisLines?.Length ?? 0) > 0;

            if (Btn_Tab_Summary != null) Btn_Tab_Summary.gameObject.SetActive(hasPlayerReport || payload?.finalStats != null);
            if (Btn_Tab_Economy != null) Btn_Tab_Economy.gameObject.SetActive(hasPlayerReport);
            if (Btn_Tab_Build != null) Btn_Tab_Build.gameObject.SetActive(hasPlayerReport);
            if (Btn_Tab_Threat != null) Btn_Tab_Threat.gameObject.SetActive(hasPlayerReport);
            if (Btn_Tab_Story != null) Btn_Tab_Story.gameObject.SetActive(hasPlayerReport);
            if (Btn_Tab_Waves != null) Btn_Tab_Waves.gameObject.SetActive(hasAdvanced);

            if (PanelRoot != null)
            {
                PanelRoot.SetActive(true);
                if (!canAnimate)
                    PanelRoot.transform.localScale = Vector3.one;
            }
            Debug.Log(
                $"[PostGameReport] Opening report panel. " +
                $"reportLanes={(payload?.playerBattleReport?.lanes?.Length ?? 0)} " +
                $"waveSnapshots={(payload?.waveSnapshots?.Length ?? 0)}.");
            SwitchTab(0);
            if (PanelRoot == null)
                return;

            if (!canAnimate)
            {
                PanelRoot.transform.localScale = Vector3.one;
                return;
            }

            StopAllCoroutines();
            StartCoroutine(ScaleIn(PanelRoot.transform, 0f, 1f, 0.3f));
        }

        public void Hide()
        {
            if (!isActiveAndEnabled || PanelRoot == null)
            {
                HideImmediate();
                return;
            }

            StopAllCoroutines();
            StartCoroutine(ScaleOut(PanelRoot.transform, 0.2f));
        }

        public void HideImmediate()
        {
            StopAllCoroutines();
            if (PanelRoot != null)
            {
                PanelRoot.transform.localScale = Vector3.zero;
                PanelRoot.SetActive(false);
            }
        }

        // ── Tab switching ─────────────────────────────────────────────────────

        void SwitchTab(int index)
        {
            if (PanelSummary != null) PanelSummary.SetActive(index == 0);
            if (PanelEconomy != null) PanelEconomy.SetActive(index == 1);
            if (PanelBuild != null) PanelBuild.SetActive(index == 2);
            if (PanelThreat != null) PanelThreat.SetActive(index == 3);
            if (PanelStory != null) PanelStory.SetActive(index == 4);
            if (PanelWaves != null) PanelWaves.SetActive(index == 5);

            switch (index)
            {
                case 0: PopulateSummary(); break;
                case 1: if (!_economyPopulated) { PopulateEconomy(); _economyPopulated = true; } break;
                case 2: if (!_armyPopulated) { PopulateArmy(); _armyPopulated = true; } break;
                case 3: if (!_threatPopulated) { PopulateThreat(); _threatPopulated = true; } break;
                case 4: if (!_storyPopulated) { PopulateStory(); _storyPopulated = true; } break;
                case 5: if (!_advancedPopulated) { PopulateAdvanced(); _advancedPopulated = true; } break;
            }
        }

        // ── Populate helpers ──────────────────────────────────────────────────

        void PopulateSummary()
        {
            if (SummaryBodyText == null)
                return;

            if (_report == null)
            {
                SummaryBodyText.text = "Battle report data is not available for this match.";
                return;
            }

            var header = _report.matchHeader ?? new MatchResultHeaderReport();
            var snapshot = _report.snapshot ?? new CorePerformanceSnapshotReport();
            var pressure = _report.fortressPressure ?? new FortressPressureReport();
            var army = _report.armySummary ?? new ArmySummaryReport();
            var builder = new System.Text.StringBuilder();

            builder.Append("<b>Match Result</b>").AppendLine();
            builder.Append($"{header.resultLabel}  |  Final Wave {header.finalWave}  |  {FormatDuration(header.durationSeconds)}");
            if (!string.IsNullOrWhiteSpace(header.modeLabel))
                builder.Append($"  |  {header.modeLabel}");
            if (!string.IsNullOrWhiteSpace(header.difficultyLabel))
                builder.Append($"  |  {header.difficultyLabel}");

            builder.AppendLine().AppendLine();
            builder.Append("<b>Core Performance Snapshot</b>").AppendLine();
            builder.Append($"Enemies Defeated: {snapshot.enemiesDefeated}    ");
            builder.Append($"Units Recruited: {snapshot.unitsRecruited}    ");
            builder.Append($"Units Lost: {snapshot.unitsLost}").AppendLine();
            builder.Append($"Buildings Constructed: {snapshot.buildingsConstructed}    ");
            builder.Append($"Upgrades Purchased: {snapshot.upgradesPurchased}").AppendLine();
            builder.Append($"Gold Earned: {snapshot.goldEarned:F0}    ");
            builder.Append($"Gold Spent: {snapshot.goldSpent:F0}").AppendLine();
            builder.Append($"Breaches Suffered: {snapshot.breachesSuffered}    ");
            builder.Append($"Core Health Remaining: {snapshot.coreHealthRemaining}    ");
            builder.Append($"Core Damage Taken: {snapshot.coreDamageTaken}");

            builder.AppendLine().AppendLine();
            builder.Append("<b>Fortress Pressure</b>").AppendLine();
            builder.Append($"{pressure.summary}").AppendLine();
            builder.Append($"Wall Damage: {pressure.wallDamageTaken}    ");
            builder.Append($"Tower Damage: {pressure.towerDamageTaken}    ");
            builder.Append($"Time Under Breach Pressure: {pressure.timeUnderBreachPressure:F1}s").AppendLine();
            builder.Append($"Fortress Entries: {pressure.fortressEntries}    ");
            builder.Append($"Time Without Frontline: {pressure.timeWithoutFrontline:F1}s    ");
            builder.Append($"Verdict: {pressure.pressureVerdict}");

            builder.AppendLine().AppendLine();
            builder.Append("<b>Army Summary</b>").AppendLine();
            builder.Append($"{army.summary}").AppendLine();
            builder.Append($"Most Recruited Unit: {army.mostRecruitedUnitType}    ");
            builder.Append($"Best Performer: {army.bestPerformingUnitType}").AppendLine();
            builder.Append($"Army Value Fielded: {army.armyValueFielded:F0}    ");
            builder.Append($"Best Support: {army.bestSupportType}").AppendLine();
            builder.Append(army.heroContribution);
            if (army.unitComposition != null && army.unitComposition.Length > 0)
            {
                builder.AppendLine();
                builder.Append("Composition: ");
                for (int index = 0; index < army.unitComposition.Length; index++)
                {
                    var entry = army.unitComposition[index];
                    if (entry == null)
                        continue;
                    if (index > 0)
                        builder.Append("  |  ");
                    builder.Append($"{entry.label} {entry.sharePercent:F0}%");
                }
            }

            if (_report.commanderNotes != null && _report.commanderNotes.Length > 0)
            {
                builder.AppendLine().AppendLine();
                builder.Append("<b>Commander Notes</b>").AppendLine();
                for (int noteIndex = 0; noteIndex < _report.commanderNotes.Length; noteIndex++)
                {
                    if (string.IsNullOrWhiteSpace(_report.commanderNotes[noteIndex]))
                        continue;
                    builder.Append("- ").Append(_report.commanderNotes[noteIndex]).AppendLine();
                }
            }

            SummaryBodyText.text = builder.ToString();
        }

        void PopulateEconomy()
        {
            EnsureCurveTabLayout(PanelEconomy, ref EconomyGraph, ref _economyBodyText, ref _economyLayoutBuilt, "EconomyCurve", "EconomyBody");
            if (_report == null)
            {
                if (_economyBodyText != null)
                    _economyBodyText.text = "No economy report was attached to this match.";
                return;
            }

            ApplyCurve(EconomyGraph, _report.economyCurve);

            if (_economyBodyText != null)
            {
                var strategy = _report.strategyComparison;
                var builder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(_report.economyCurve?.subtitle))
                    builder.Append(_report.economyCurve.subtitle).AppendLine();
                AppendCurveTakeaway(builder, _report.economyCurve);

                if (strategy != null)
                {
                    builder.AppendLine();
                    builder.Append("<b>Strategy Comparison</b>").AppendLine();
                    builder.Append(strategy.summary).AppendLine();
                    builder.AppendLine();
                    AppendStrategyBlock(builder, "You", strategy.player);
                    if (strategy.opponent != null)
                    {
                        builder.AppendLine();
                        AppendStrategyBlock(builder, strategy.opponent.commanderName, strategy.opponent);
                    }
                }
                else
                {
                    builder.Append("No strategy comparison was recorded.");
                }

                _economyBodyText.text = builder.ToString();
            }
        }

        void PopulateArmy()
        {
            EnsureCurveTabLayout(PanelBuild, ref BuildGraph, ref _armyBodyText, ref _armyLayoutBuilt, "ArmyCurve", "ArmyBody");
            if (_armyBodyText == null)
                return;

            if (_report == null)
            {
                _armyBodyText.text = "No army report was attached to this match.";
                return;
            }

            ApplyCurve(BuildGraph, _report.armyCurve);

            var army = _report.armySummary ?? new ArmySummaryReport();
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_report.armyCurve?.subtitle))
                builder.Append(_report.armyCurve.subtitle).AppendLine();
            AppendCurveTakeaway(builder, _report.armyCurve);
            builder.AppendLine();
            builder.Append("<b>What Carried The Run</b>").AppendLine();
            builder.Append(army.summary).AppendLine();
            builder.Append($"Most Recruited: {army.mostRecruitedUnitType}    ");
            builder.Append($"Best Performer: {army.bestPerformingUnitType}").AppendLine();
            builder.Append($"Army Value Fielded: {army.armyValueFielded:F0}    ");
            builder.Append($"Best Support: {army.bestSupportType}").AppendLine();
            if (!string.IsNullOrWhiteSpace(army.heroContribution))
                builder.Append(army.heroContribution).AppendLine();
            AppendShareLine(builder, "Unit Composition", army.unitComposition);
            _armyBodyText.text = builder.ToString();
        }

        void PopulateThreat()
        {
            EnsureCurveTabLayout(PanelThreat, ref ThreatGraph, ref _threatBodyText, ref _threatLayoutBuilt, "ThreatCurve", "ThreatBody");
            if (_threatBodyText == null)
                return;

            if (_report == null)
            {
                _threatBodyText.text = "No threat report was attached to this match.";
                return;
            }

            ApplyCurve(ThreatGraph, _report.threatCurve);

            var pressure = _report.fortressPressure ?? new FortressPressureReport();
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_report.threatCurve?.subtitle))
                builder.Append(_report.threatCurve.subtitle).AppendLine();
            AppendCurveTakeaway(builder, _report.threatCurve);
            builder.AppendLine();
            builder.Append("<b>Fortress Pressure</b>").AppendLine();
            builder.Append(pressure.summary).AppendLine();
            builder.Append($"Verdict: {pressure.pressureVerdict}    ");
            builder.Append($"Breaches: {pressure.breachCount}    ");
            builder.Append($"Core Damage: {pressure.coreDamageTaken}").AppendLine();
            builder.Append($"Time Under Pressure: {pressure.timeUnderBreachPressure:F1}s    ");
            builder.Append($"Time Without Frontline: {pressure.timeWithoutFrontline:F1}s");

            if (_report.battleStory?.highlights != null && _report.battleStory.highlights.Length > 0)
            {
                builder.AppendLine().AppendLine();
                builder.Append("<b>Turning Points</b>").AppendLine();
                foreach (var highlight in _report.battleStory.highlights)
                {
                    if (highlight == null || string.IsNullOrWhiteSpace(highlight.title))
                        continue;

                    if (highlight.title != "Hardest Wave" &&
                        highlight.title != "First Breach" &&
                        highlight.title != "Stabilized" &&
                        highlight.title != "Surpassed the Horde")
                    {
                        continue;
                    }

                    builder.Append("- ").Append(highlight.title);
                    if (highlight.wave > 0)
                        builder.Append($" (Wave {highlight.wave})");
                    builder.Append(": ").Append(highlight.detail).AppendLine();
                }
            }

            _threatBodyText.text = builder.ToString();
        }

        void PopulateStory()
        {
            EnsureStoryLayout();
            if (_storyBodyText == null)
                return;

            if (_report == null)
            {
                _storyBodyText.text = "No battle story was attached to this match.";
                return;
            }

            var builder = new System.Text.StringBuilder();
            builder.Append("<b>Turning Points</b>").AppendLine();
            if (_report.battleStory?.highlights != null)
            {
                foreach (var highlight in _report.battleStory.highlights)
                {
                    if (highlight == null)
                        continue;
                    builder.Append($"- <b>{highlight.title}</b>");
                    if (highlight.wave > 0)
                        builder.Append($"  (Wave {highlight.wave})");
                    builder.AppendLine();
                    builder.Append(highlight.detail).AppendLine().AppendLine();
                }
            }

            builder.Append("<b>Match Story</b>").AppendLine();
            if (_report.battleStory?.storyLines != null)
            {
                foreach (var line in _report.battleStory.storyLines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    builder.Append("- ").Append(line).AppendLine();
                }
            }

            builder.AppendLine().AppendLine();
            builder.Append("<b>Battle Honors</b>").AppendLine();
            if (_report.awards != null && _report.awards.Length > 0)
            {
                foreach (var award in _report.awards)
                {
                    if (award == null)
                        continue;
                    builder.Append($"- <b>{award.title}</b>  ").Append(award.detail).AppendLine();
                }
            }
            else
            {
                builder.Append("No honors were awarded this time.").AppendLine();
            }

            if (_report.battleStory?.recommendations != null && _report.battleStory.recommendations.Length > 0)
            {
                builder.AppendLine();
                builder.Append("<b>What To Try Next</b>").AppendLine();
                foreach (var recommendation in _report.battleStory.recommendations)
                {
                    if (string.IsNullOrWhiteSpace(recommendation))
                        continue;
                    builder.Append("- ").Append(recommendation).AppendLine();
                }
            }

            _storyBodyText.text = builder.ToString();
        }

        void PopulateAdvanced()
        {
            if (WavesBodyText == null)
                return;

            EnsureWavesTabLayout();

            if (WaveRowContainer != null)
            {
                foreach (Transform child in WaveRowContainer)
                    Destroy(child.gameObject);
            }

            var builder = new StringBuilder();
            if (_report == null)
            {
                builder.Append("No advanced battle report data was attached to this match.");
                WavesBodyText.text = builder.ToString();
                return;
            }

            builder.Append("<b>Advanced Stats</b>").AppendLine();
            if (_report.advanced?.breakdownLines != null)
            {
                foreach (var line in _report.advanced.breakdownLines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    builder.Append("- ").Append(line).AppendLine();
                }
            }

            builder.AppendLine();
            builder.Append("<b>Wave By Wave</b>").AppendLine();
            if (_report.advanced?.waveRows != null)
            {
                foreach (var row in _report.advanced.waveRows)
                {
                    if (row == null)
                        continue;
                    builder.Append($"- Wave {row.wave}  ");
                    builder.Append($"{row.state}  ");
                    builder.Append($"War Chest {row.bankedGold:F0}  ");
                    builder.Append($"Army {row.armyStrength:F0}  ");
                    builder.Append($"Threat {row.dungeonThreat:F0}  ");
                    builder.Append($"Breaches {row.breachCount}  ");
                    builder.Append($"Core Damage {row.coreDamage}");
                    if (row.opponentArmyStrength > 0f)
                        builder.Append($"  Rival Army {row.opponentArmyStrength:F0}");
                    builder.AppendLine();
                }
            }

            WavesBodyText.text = builder.ToString();
        }

        void AddWaveInfoRow(string _text)
        {
        }

        void EnsureWavesTabLayout()
        {
            if (PanelWaves == null)
                return;

            var image = PanelWaves.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.05f, 0.05f, 0.09f, 0.96f);
            if (_waveScroll == null)
                _waveScroll = PanelWaves.GetComponentInChildren<ScrollRect>(true);
        }

        void EnsureCurveTabLayout(
            GameObject panel,
            ref LineGraphUI graph,
            ref TMP_Text bodyText,
            ref bool layoutBuilt,
            string graphName,
            string bodyName)
        {
            if (layoutBuilt || panel == null)
                return;

            var image = panel.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.05f, 0.05f, 0.09f, 0.96f);

            ClearChildren(panel.transform);
            graph = CreateGraph(panel.transform, graphName, new Vector2(0.03f, 0.28f), new Vector2(0.97f, 0.96f));
            graph.BackgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.98f);
            graph.AnnotationColor = new Color(0.96f, 0.97f, 0.99f, 1f);
            graph.MaxXAxisLabels = 6;
            graph.LineThickness = 2.5f;
            bodyText = CreatePanelText(panel.transform, bodyName, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.22f), 13);
            layoutBuilt = true;
        }

        void EnsureStoryLayout()
        {
            if (_storyLayoutBuilt || PanelStory == null)
                return;

            var image = PanelStory.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.05f, 0.05f, 0.09f, 0.96f);

            ClearChildren(PanelStory.transform);
            _storyBodyText = CreatePanelText(PanelStory.transform, "StoryBody", new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f), 15);
            _storyLayoutBuilt = true;
        }

        CommanderBattleReport ResolveCommanderReport(MLGameOverPayload payload)
        {
            if (payload?.playerBattleReport?.lanes == null || payload.playerBattleReport.lanes.Length == 0)
                return null;

            int myLaneIndex = NetworkManager.Instance != null ? NetworkManager.Instance.MyLaneIndex : -1;
            foreach (var laneReport in payload.playerBattleReport.lanes)
            {
                if (laneReport != null && laneReport.laneIndex == myLaneIndex)
                    return laneReport;
            }

            foreach (var laneReport in payload.playerBattleReport.lanes)
            {
                if (laneReport != null && laneReport.laneIndex == payload.winnerLaneIndex)
                    return laneReport;
            }

            return payload.playerBattleReport.lanes[0];
        }

        void ApplyCurve(LineGraphUI graph, CurveReport curve)
        {
            if (graph == null || curve == null || curve.lines == null)
                return;

            int lineCount = curve.lines.Length;
            var series = new float[lineCount][];
            var labels = new string[lineCount];
            var colors = new Color[lineCount];
            for (int index = 0; index < lineCount; index++)
            {
                var line = curve.lines[index];
                series[index] = line?.values ?? new float[0];
                labels[index] = line?.label ?? $"Line {index + 1}";
                colors[index] = ResolveCurveToneColor(line?.tone, index);
            }

            graph.SetData(
                series,
                labels,
                chartTitle: curve.title,
                valueFormat: string.IsNullOrWhiteSpace(curve.valueFormat) ? "F0" : curve.valueFormat,
                xAxisLabels: curve.xLabels,
                lineColors: colors);
        }

        void AppendStrategyBlock(StringBuilder builder, string heading, StrategySpendBreakdownReport spending)
        {
            if (spending == null)
                return;

            builder.Append($"<b>{heading}</b>");
            if (!string.IsNullOrWhiteSpace(spending.styleLabel))
                builder.Append($"  |  {spending.styleLabel}");
            builder.AppendLine();
            builder.Append($"Units {spending.unitSpending:F0}    ");
            builder.Append($"Upgrades {spending.upgradeSpending:F0}    ");
            builder.Append($"Economy {spending.economySpending:F0}    ");
            builder.Append($"Defense {spending.defenseSpending:F0}    ");
            builder.Append($"Repairs {spending.repairs:F0}").AppendLine();
            if (spending.buildingPaths != null && spending.buildingPaths.Length > 0)
            {
                builder.Append("Major Paths: ");
                for (int index = 0; index < spending.buildingPaths.Length; index++)
                {
                    var path = spending.buildingPaths[index];
                    if (path == null)
                        continue;
                    if (index > 0)
                        builder.Append("  |  ");
                    builder.Append($"{path.label} {path.sharePercent:F0}%");
                }
                builder.AppendLine();
            }
            if (spending.highlights != null)
            {
                foreach (var highlight in spending.highlights)
                {
                    if (string.IsNullOrWhiteSpace(highlight))
                        continue;
                    builder.Append("- ").Append(highlight).AppendLine();
                }
            }
        }

        void AppendCurveTakeaway(StringBuilder builder, CurveReport curve)
        {
            if (curve == null || string.IsNullOrWhiteSpace(curve.takeaway))
                return;

            builder.Append("<b>Curve Read</b>").AppendLine();
            builder.Append("- ").Append(curve.takeaway).AppendLine();
        }

        void AppendShareLine(StringBuilder builder, string label, StrategyShareReport[] shares)
        {
            if (shares == null || shares.Length == 0)
                return;

            builder.AppendLine();
            builder.Append(label).Append(": ");
            bool wroteAny = false;
            for (int index = 0; index < shares.Length; index++)
            {
                var share = shares[index];
                if (share == null)
                    continue;

                if (wroteAny)
                    builder.Append("  |  ");
                builder.Append($"{share.label} {share.sharePercent:F0}%");
                wroteAny = true;
            }
        }

        // ── Utility ───────────────────────────────────────────────────────────

        string GetLaneLabel(int laneIndex)
        {
            if (_payload?.finalStats != null)
                foreach (var s in _payload.finalStats)
                    if (s.laneIndex == laneIndex) return s.displayName;
            return $"Lane {laneIndex}";
        }

        string[] BuildWaveAxisLabels()
        {
            if (_payload?.waveSnapshots == null || _payload.waveSnapshots.Length == 0)
                return null;

            var labels = new string[_payload.waveSnapshots.Length];
            for (int i = 0; i < _payload.waveSnapshots.Length; i++)
            {
                var snap = _payload.waveSnapshots[i];
                labels[i] = snap != null && snap.round > 0
                    ? $"W{snap.round}"
                    : $"P{i + 1}";
            }
            return labels;
        }

        Color[] GetLaneColors(int laneCount)
        {
            var colors = new Color[Mathf.Max(0, laneCount)];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = GetLaneColor(i);
            return colors;
        }

        void SetButtonLabel(Button button, string label)
        {
            if (button == null)
                return;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
                text.text = label;
        }

        string FormatDuration(int durationSeconds)
        {
            int safeSeconds = Mathf.Max(0, durationSeconds);
            return $"{safeSeconds / 60}m {safeSeconds % 60}s";
        }

        LineGraphUI CreateGraph(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var graphGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            graphGo.transform.SetParent(parent, false);
            var rect = graphGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return graphGo.AddComponent<LineGraphUI>();
        }

        TMP_Text CreatePanelText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float fontSize)
        {
            var textGo = new GameObject(name, typeof(RectTransform));
            textGo.transform.SetParent(parent, false);
            var rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = Mathf.Max(12f, fontSize);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        void ClearChildren(Transform parent)
        {
            if (parent == null)
                return;
            for (int childIndex = parent.childCount - 1; childIndex >= 0; childIndex--)
                Destroy(parent.GetChild(childIndex).gameObject);
        }

        Color ResolveCurveToneColor(string tone, int fallbackIndex)
        {
            switch (tone)
            {
                case "player": return new Color(0.93f, 0.76f, 0.25f);
                case "opponent": return new Color(0.86f, 0.30f, 0.28f);
                case "threat": return new Color(0.58f, 0.36f, 0.86f);
                case "support": return new Color(0.25f, 0.78f, 0.55f);
                case "spend": return new Color(0.34f, 0.72f, 0.95f);
                case "opponent_support": return new Color(0.84f, 0.50f, 0.30f);
                case "opponent_spend": return new Color(0.62f, 0.62f, 0.72f);
                default: return GetLaneColor(fallbackIndex);
            }
        }

        Color GetLaneColor(int laneIndex)
        {
            if (_payload?.finalStats != null)
            {
                foreach (var s in _payload.finalStats)
                {
                    if (s == null || s.laneIndex != laneIndex)
                        continue;

                    if (TryResolveReportColor(s.team, out var teamColor))
                        return teamColor;
                    if (TryResolveReportColor(s.side, out var sideColor))
                        return sideColor;
                }
            }

            return (laneIndex % 4) switch
            {
                0 => new Color(0.24f, 0.50f, 0.92f),
                1 => new Color(0.86f, 0.25f, 0.22f),
                2 => new Color(0.92f, 0.74f, 0.20f),
                _ => new Color(0.20f, 0.72f, 0.42f),
            };
        }

        static bool TryResolveReportColor(string key, out Color color)
        {
            return SnapshotApplier.TryResolveSlotColor(key, out color);
        }

        string ColorizeLaneName(string laneName, int laneIndex, bool bold = false)
        {
            string safeName = string.IsNullOrWhiteSpace(laneName) ? $"Lane {laneIndex}" : laneName;
            if (bold)
                safeName = $"<b>{safeName}</b>";
            string colorHex = ColorUtility.ToHtmlStringRGB(GetLaneColor(laneIndex));
            return $"<color=#{colorHex}>{safeName}</color>";
        }

        // ── Animation coroutines ──────────────────────────────────────────────

        static IEnumerator ScaleIn(Transform t, float from, float to, float dur)
        {
            t.localScale = Vector3.one * from;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                t.localScale = Vector3.one * Mathf.Lerp(from, to, n);
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }

        static IEnumerator ScaleOut(Transform t, float dur)
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
            t.gameObject.SetActive(false);
        }
    }
}
