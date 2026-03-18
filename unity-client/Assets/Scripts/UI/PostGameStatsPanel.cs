// PostGameStatsPanel.cs — Tabbed post-game stats panel (Summary / Economy / Build / Waves).
//
// SETUP:
//   PanelPostGameStats (inactive by default, sibling of PanelGameOver)
//   ├── PanelHeader
//   │   ├── Btn_Tab_Summary, Btn_Tab_Economy, Btn_Tab_Build, Btn_Tab_Waves
//   │   └── Btn_Close
//   ├── PanelSummary   (active by default — TMP_Text rows wired below)
//   ├── PanelEconomy   (inactive — LineGraphUI wired to EconomyGraph)
//   ├── PanelBuild     (inactive — LineGraphUI wired to BuildGraph)
//   └── PanelWaves     (inactive — ScrollRect wired, WaveRowPrefab assigned)
//
// Wire all inspector references in the Unity Editor.

using System.Collections;
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
        public Button Btn_Tab_Waves;
        public Button Btn_Close;

        [Header("Tab panels")]
        public GameObject PanelSummary;
        public GameObject PanelEconomy;
        public GameObject PanelBuild;
        public GameObject PanelWaves;

        [Header("Summary — one TMP_Text per lane (up to 4)")]
        public TMP_Text[] SummaryRows;   // populated programmatically

        [Header("Charts")]
        public LineGraphUI EconomyGraph;
        public LineGraphUI BuildGraph;

        [Header("Waves tab")]
        public Transform   WaveRowContainer;
        public GameObject  WaveRowPrefab;

        // ── State ─────────────────────────────────────────────────────────────

        private MLGameOverPayload _payload;
        private bool _economyPopulated;
        private bool _buildPopulated;
        private bool _wavesPopulated;
        private LineGraphUI _leakGraph;
        private TMP_Text _leakGraphTitle;
        private ScrollRect _waveScroll;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            if (PanelRoot != null) PanelRoot.SetActive(false);

            if (Btn_Tab_Summary != null) Btn_Tab_Summary.onClick.AddListener(() => SwitchTab(0));
            if (Btn_Tab_Economy != null) Btn_Tab_Economy.onClick.AddListener(() => SwitchTab(1));
            if (Btn_Tab_Build   != null) Btn_Tab_Build  .onClick.AddListener(() => SwitchTab(2));
            if (Btn_Tab_Waves   != null) Btn_Tab_Waves  .onClick.AddListener(() => SwitchTab(3));
            if (Btn_Close       != null) Btn_Close      .onClick.AddListener(Hide);
            EnsureWavesTabLayout();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show(MLGameOverPayload payload)
        {
            _payload          = payload;
            _economyPopulated = false;
            _buildPopulated   = false;
            _wavesPopulated   = false;

            if (PanelRoot != null) PanelRoot.SetActive(true);
            SwitchTab(0);
            StartCoroutine(ScaleIn(PanelRoot.transform, 0f, 1f, 0.3f));
        }

        public void Hide()
        {
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
            if (PanelBuild   != null) PanelBuild  .SetActive(index == 2);
            if (PanelWaves   != null) PanelWaves  .SetActive(index == 3);

            switch (index)
            {
                case 0: PopulateSummary(); break;
                case 1: if (!_economyPopulated) { PopulateEconomy(); _economyPopulated = true; } break;
                case 2: if (!_buildPopulated)   { PopulateBuild();   _buildPopulated   = true; } break;
                case 3: if (!_wavesPopulated)   { PopulateWaves();   _wavesPopulated   = true; } break;
            }
        }

        // ── Populate helpers ──────────────────────────────────────────────────

        void PopulateSummary()
        {
            if (_payload?.finalStats == null) return;
            var stats = _payload.finalStats;

            if (SummaryRows != null)
            {
                for (int i = 0; i < SummaryRows.Length && i < stats.Length; i++)
                {
                    if (SummaryRows[i] == null) continue;
                    var s = stats[i];
                    bool isWinner = !string.IsNullOrEmpty(_payload.winningTeam)
                        ? s.team == _payload.winningTeam
                        : s.laneIndex == _payload.winnerLaneIndex;
                    SummaryRows[i].text =
                        $"<b>{s.displayName}</b>  {(isWinner ? "WIN" : "LOSS")}  " +
                        $"Income:{s.income:F0}  " +
                        $"Build:{s.buildValue:F0}  " +
                        $"BuildSpend:{s.totalBuildSpend:F0}  " +
                        $"Sends:{s.totalSendSpend:F0}/{s.totalSendCount}  " +
                        $"Leaks:{s.totalLeaksTaken}  " +
                        $"Hold:{s.longestHoldStreak}  " +
                        $"BigLeak:{s.biggestLeakTaken}";
                }

                for (int i = stats.Length; i < SummaryRows.Length; i++)
                {
                    if (SummaryRows[i] != null) SummaryRows[i].text = string.Empty;
                }
            }
        }

        void PopulateEconomy()
        {
            if (EconomyGraph == null || _payload?.waveSnapshots == null) return;
            var snaps  = _payload.waveSnapshots;
            int nLanes = snaps.Length > 0 && snaps[0].lanes != null ? snaps[0].lanes.Length : 0;
            if (nLanes == 0) return;

            var series = new float[nLanes][];
            var labels = new string[nLanes];
            for (int li = 0; li < nLanes; li++)
            {
                series[li] = new float[snaps.Length];
                for (int wi = 0; wi < snaps.Length; wi++)
                {
                    var wl = snaps[wi].lanes;
                    series[li][wi] = (wl != null && li < wl.Length) ? wl[li].income : 0f;
                }
                labels[li] = GetLaneLabel(li);
            }
            EconomyGraph.SetData(series, labels);
        }

        void PopulateBuild()
        {
            if (BuildGraph == null || _payload?.waveSnapshots == null) return;
            var snaps  = _payload.waveSnapshots;
            int nLanes = snaps.Length > 0 && snaps[0].lanes != null ? snaps[0].lanes.Length : 0;
            if (nLanes == 0) return;

            var series = new float[nLanes][];
            var labels = new string[nLanes];
            for (int li = 0; li < nLanes; li++)
            {
                series[li] = new float[snaps.Length];
                for (int wi = 0; wi < snaps.Length; wi++)
                {
                    var wl = snaps[wi].lanes;
                    series[li][wi] = (wl != null && li < wl.Length) ? wl[li].buildValue : 0f;
                }
                labels[li] = GetLaneLabel(li);
            }
            BuildGraph.SetData(series, labels);
        }

        void PopulateWaves()
        {
            if (WaveRowContainer == null || WaveRowPrefab == null || _payload?.waveSnapshots == null)
                return;

            EnsureWavesTabLayout();
            PopulateLeakGraph();

            // Clear existing rows
            foreach (Transform child in WaveRowContainer)
                Destroy(child.gameObject);

            foreach (var snap in _payload.waveSnapshots)
            {
                var row = Instantiate(WaveRowPrefab, WaveRowContainer);
                var lbl = row.GetComponentInChildren<TMP_Text>();
                if (lbl == null) continue;

                // Build a compact row: Wave N | per-lane snapshot values
                var sb = new System.Text.StringBuilder();
                string waveLabel = snap.terminal ? "Final" : $"Wave {snap.round}";
                sb.Append($"<b>{waveLabel}</b>");
                if (snap.elapsedSeconds > 0)
                    sb.Append($"  t={snap.elapsedSeconds / 60}m {snap.elapsedSeconds % 60}s");

                if (snap.lanes != null)
                {
                    foreach (var l in snap.lanes)
                    {
                        sb.Append($"  [{GetLaneLabel(l.laneIndex)}]" +
                                  $" Inc:{l.income:F0}" +
                                  $" Build:{l.buildValue:F0}" +
                                  $" BuildSpend:{l.buildSpend:F0}" +
                                  $" Sends:{l.sendSpend:F0}/{l.sendCount}" +
                                  $" Leak:{l.leaksTaken}" +
                                  $" LifeLoss:{l.leakDamage}" +
                                  $" Result:{l.holdResult}" +
                                  $" HP:{l.teamHp}");
                    }
                }
                lbl.text = sb.ToString();
            }
        }

        void EnsureWavesTabLayout()
        {
            if (PanelWaves == null) return;

            if (_waveScroll == null)
                _waveScroll = PanelWaves.GetComponentInChildren<ScrollRect>(true);

            if (_leakGraphTitle == null)
            {
                var existingTitle = PanelWaves.transform.Find("LeakGraphTitle");
                if (existingTitle != null)
                {
                    _leakGraphTitle = existingTitle.GetComponent<TMP_Text>();
                }
                else
                {
                    var titleGO = new GameObject("LeakGraphTitle");
                    titleGO.transform.SetParent(PanelWaves.transform, false);
                    var rt = titleGO.AddComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.04f, 0.88f);
                    rt.anchorMax = new Vector2(0.96f, 0.98f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _leakGraphTitle = titleGO.AddComponent<TextMeshProUGUI>();
                    _leakGraphTitle.text = "Leaks By Wave";
                    _leakGraphTitle.fontSize = 16;
                    _leakGraphTitle.alignment = TextAlignmentOptions.MidlineLeft;
                    _leakGraphTitle.color = Color.white;
                }
            }

            if (_leakGraph == null)
            {
                var existingGraph = PanelWaves.transform.Find("LeakGraph");
                if (existingGraph != null)
                {
                    _leakGraph = existingGraph.GetComponent<LineGraphUI>();
                }
                else
                {
                    var graphGO = new GameObject("LeakGraph");
                    graphGO.transform.SetParent(PanelWaves.transform, false);
                    var rt = graphGO.AddComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.04f, 0.52f);
                    rt.anchorMax = new Vector2(0.96f, 0.86f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _leakGraph = graphGO.AddComponent<LineGraphUI>();
                }
            }

            if (_waveScroll != null && _waveScroll.viewport != null)
            {
                var viewportRt = _waveScroll.viewport;
                viewportRt.anchorMin = new Vector2(0f, 0f);
                viewportRt.anchorMax = new Vector2(1f, 0.48f);
                viewportRt.offsetMin = Vector2.zero;
                viewportRt.offsetMax = Vector2.zero;
            }
        }

        void PopulateLeakGraph()
        {
            if (_leakGraph == null || _payload?.waveSnapshots == null) return;
            var snaps = _payload.waveSnapshots;
            int nLanes = snaps.Length > 0 && snaps[0].lanes != null ? snaps[0].lanes.Length : 0;
            if (nLanes == 0) return;

            var series = new float[nLanes][];
            var labels = new string[nLanes];
            for (int li = 0; li < nLanes; li++)
            {
                series[li] = new float[snaps.Length];
                for (int wi = 0; wi < snaps.Length; wi++)
                {
                    var wl = snaps[wi].lanes;
                    series[li][wi] = (wl != null && li < wl.Length)
                        ? Mathf.Max(wl[li].leakDamage, wl[li].leaksTaken)
                        : 0f;
                }
                labels[li] = GetLaneLabel(li);
            }
            _leakGraph.SetData(series, labels);
        }

        // ── Utility ───────────────────────────────────────────────────────────

        string GetLaneLabel(int laneIndex)
        {
            if (_payload?.finalStats != null)
                foreach (var s in _payload.finalStats)
                    if (s.laneIndex == laneIndex) return s.displayName;
            return $"Lane {laneIndex}";
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
