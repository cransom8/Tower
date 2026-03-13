using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class PostGameSceneController : MonoBehaviour
    {
        const string SceneName = "PostGame";

        Canvas _canvas;
        TMP_Text _resultText;
        TMP_Text _winnerText;
        TMP_Text _causeText;
        TMP_Text _summaryText;
        TMP_Text _rematchLabel;
        Button _rematchButton;
        Button _lobbyButton;
        Button _statsButton;
        PostGameStatsPanel _statsPanel;
        MLGameOverPayload _payload;
        bool _rematchRequested;
        bool _transitioningToLoadout;

        void Awake()
        {
            EnsureUi();
        }

        void OnEnable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnRematchStatus += HandleRematchStatus;
            nm.OnRematchStarting += HandleRematchStarting;
            nm.OnMLLoadoutPhaseStart += HandleLoadoutPhaseStart;
            nm.OnMLMatchReady += HandleMatchReady;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnRematchStatus -= HandleRematchStatus;
            nm.OnRematchStarting -= HandleRematchStarting;
            nm.OnMLLoadoutPhaseStart -= HandleLoadoutPhaseStart;
            nm.OnMLMatchReady -= HandleMatchReady;
        }

        void Start()
        {
            var nm = NetworkManager.Instance;
            _payload = nm != null ? nm.LastMLGameOver : null;
            ShowPayload(_payload);

            if (nm != null && nm.LastRematchStatus != null)
                HandleRematchStatus(nm.LastRematchStatus);

            if (nm != null && nm.PendingLoadoutPhase != null)
                BeginLoadoutTransition();
        }

        void ShowPayload(MLGameOverPayload payload)
        {
            if (_resultText == null) return;

            if (payload == null)
            {
                _resultText.text = "MATCH COMPLETE";
                _winnerText.text = "No post-game data available.";
                _causeText.text = "Return to lobby or start a new match.";
                _summaryText.text = string.Empty;
                return;
            }

            int myLaneIndex = NetworkManager.Instance != null ? NetworkManager.Instance.MyLaneIndex : -1;
            bool isWinner = payload.winnerLaneIndex == myLaneIndex;
            _resultText.text = isWinner ? "VICTORY" : "DEFEAT";
            _resultText.color = isWinner
                ? new Color(1f, 0.84f, 0.25f)
                : new Color(0.92f, 0.34f, 0.32f);

            _winnerText.text = !string.IsNullOrWhiteSpace(payload.winnerName)
                ? $"Winner: {payload.winnerName}"
                : (!string.IsNullOrWhiteSpace(payload.winningTeam) ? $"Winner: {payload.winningTeam}" : "Winner: Unknown");

            _causeText.text = $"{payload.causeLoss ?? "Lives reduced to 0"}  •  {payload.gameDuration / 60}m {payload.gameDuration % 60}s";

            var myStat = GetMyStat(payload, myLaneIndex);
            if (myStat != null)
            {
                _summaryText.text =
                    $"Income {myStat.income:F0}    Build {myStat.buildValue:F0}    Sends {myStat.totalSendSpend:F0}/{myStat.totalSendCount}    " +
                    $"Leaks {myStat.totalLeaksTaken}    Hold {myStat.longestHoldStreak}";
            }
            else
            {
                _summaryText.text = $"Final Wave {payload.finalRound}";
            }
        }

        void HandleRematchStatus(RematchStatusPayload payload)
        {
            if (_rematchLabel == null || _rematchButton == null) return;
            if (payload == null)
            {
                _rematchLabel.text = _rematchRequested ? "Waiting for opponent..." : "Ready for rematch?";
                _rematchButton.interactable = !_rematchRequested;
                return;
            }

            if (payload.allAccepted)
            {
                _rematchLabel.text = "Rematch accepted. Starting...";
                _rematchButton.interactable = false;
                return;
            }

            if (_rematchRequested)
            {
                _rematchLabel.text = $"Waiting for opponent... ({payload.count}/{payload.needed})";
                _rematchButton.interactable = false;
            }
            else if (payload.count > 0)
            {
                _rematchLabel.text = $"Opponent accepted rematch ({payload.count}/{payload.needed})";
                _rematchButton.interactable = true;
            }
            else
            {
                _rematchLabel.text = "Ready for rematch?";
                _rematchButton.interactable = true;
            }
        }

        void HandleRematchStarting(RematchStartingPayload _)
        {
            if (_rematchLabel != null) _rematchLabel.text = "Rematch starting...";
            if (_rematchButton != null) _rematchButton.interactable = false;
        }

        void HandleMatchReady(MLMatchReadyPayload _)
        {
            if (_rematchLabel != null) _rematchLabel.text = "Preparing rematch...";
        }

        void HandleLoadoutPhaseStart(MLLoadoutPhaseStartPayload _)
        {
            BeginLoadoutTransition();
        }

        void BeginLoadoutTransition()
        {
            if (_transitioningToLoadout) return;
            _transitioningToLoadout = true;
            NetworkManager.Instance?.ClearPostGameData();
            SceneManager.LoadScene("Loadout");
        }

        void OnRematch()
        {
            if (_rematchRequested) return;
            _rematchRequested = true;
            _rematchLabel.text = "Waiting for opponent...";
            _rematchButton.interactable = false;
            ActionSender.RequestRematch();
        }

        void OnLobby()
        {
            NetworkManager.Instance?.ClearPostGameData();
            SceneManager.LoadScene("Lobby");
        }

        void OnStats()
        {
            if (_statsPanel != null && _payload != null)
            {
                if (!_statsPanel.gameObject.activeSelf)
                    _statsPanel.gameObject.SetActive(true);
                _statsPanel.Show(_payload);
            }
        }

        MLFinalLaneStat GetMyStat(MLGameOverPayload payload, int myLaneIndex)
        {
            if (payload == null || payload.finalStats == null) return null;
            foreach (var stat in payload.finalStats)
            {
                if (stat != null && stat.laneIndex == myLaneIndex) return stat;
            }
            return null;
        }

        void EnsureUi()
        {
            _canvas = FindFirstObjectByType<Canvas>();
            if (_canvas == null)
            {
                var canvasGo = new GameObject("Canvas");
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            var backdrop = MakePanel(_canvas.transform, "Backdrop", new Color(0.04f, 0.03f, 0.05f, 1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var card = MakePanel(backdrop.transform, "Card", new Color(0.10f, 0.08f, 0.12f, 0.96f), new Vector2(0.16f, 0.12f), new Vector2(0.84f, 0.88f), Vector2.zero, Vector2.zero);

            _resultText = MakeText(card.transform, "TxtResult", "VICTORY", 42, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.94f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            _winnerText = MakeText(card.transform, "TxtWinner", "Winner", 22, new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.78f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            _causeText = MakeText(card.transform, "TxtCause", "Cause", 18, new Vector2(0.08f, 0.60f), new Vector2(0.92f, 0.68f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            _summaryText = MakeText(card.transform, "TxtSummary", "Summary", 16, new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.60f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _rematchLabel = MakeText(card.transform, "TxtRematchStatus", "Ready for rematch?", 16, new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.42f), FontStyles.Italic, TextAlignmentOptions.MidlineLeft);

            _rematchButton = MakeButton(card.transform, "BtnRematch", "Rematch", new Vector2(0.08f, 0.18f), new Vector2(0.32f, 0.28f), new Color(0.72f, 0.23f, 0.18f, 1f));
            _lobbyButton = MakeButton(card.transform, "BtnLobby", "Return To Lobby", new Vector2(0.36f, 0.18f), new Vector2(0.62f, 0.28f), new Color(0.23f, 0.25f, 0.30f, 1f));
            _statsButton = MakeButton(card.transform, "BtnStats", "View Report", new Vector2(0.66f, 0.18f), new Vector2(0.92f, 0.28f), new Color(0.18f, 0.38f, 0.56f, 1f));

            _rematchButton.onClick.AddListener(OnRematch);
            _lobbyButton.onClick.AddListener(OnLobby);
            _statsButton.onClick.AddListener(OnStats);

            _statsPanel = BuildStatsPanel(backdrop.transform);
        }

        PostGameStatsPanel BuildStatsPanel(Transform parent)
        {
            var root = MakePanel(parent, "PanelPostGameStats", new Color(0.04f, 0.04f, 0.10f, 0.98f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.SetActive(false);

            var panel = root.AddComponent<PostGameStatsPanel>();
            panel.PanelRoot = root;

            var header = MakePanel(root.transform, "PanelHeader", new Color(0.10f, 0.10f, 0.18f, 1f), new Vector2(0f, 0.92f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var layout = header.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            panel.Btn_Tab_Summary = MakeLayoutButton(header.transform, "BtnTabSummary", "Summary");
            panel.Btn_Tab_Economy = MakeLayoutButton(header.transform, "BtnTabEconomy", "Economy");
            panel.Btn_Tab_Build = MakeLayoutButton(header.transform, "BtnTabBuild", "Build");
            panel.Btn_Tab_Waves = MakeLayoutButton(header.transform, "BtnTabWaves", "Waves");
            panel.Btn_Close = MakeLayoutButton(header.transform, "BtnClose", "Close");

            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);
            var contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = new Vector2(1f, 0.92f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            panel.PanelSummary = MakePanel(content.transform, "PanelSummary", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var summaryLayout = panel.PanelSummary.AddComponent<VerticalLayoutGroup>();
            summaryLayout.padding = new RectOffset(24, 24, 24, 24);
            summaryLayout.spacing = 14;
            summaryLayout.childControlHeight = false;

            panel.SummaryRows = new TMP_Text[4];
            for (int i = 0; i < panel.SummaryRows.Length; i++)
            {
                var row = new GameObject($"SummaryRow_{i}");
                row.transform.SetParent(panel.PanelSummary.transform, false);
                row.AddComponent<LayoutElement>().preferredHeight = 42f;
                panel.SummaryRows[i] = row.AddComponent<TextMeshProUGUI>();
                panel.SummaryRows[i].fontSize = 16;
                panel.SummaryRows[i].color = Color.white;
                panel.SummaryRows[i].alignment = TextAlignmentOptions.MidlineLeft;
            }

            panel.PanelEconomy = MakePanel(content.transform, "PanelEconomy", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelEconomy.SetActive(false);
            panel.EconomyGraph = BuildGraph(panel.PanelEconomy.transform, "EconomyGraph");

            panel.PanelBuild = MakePanel(content.transform, "PanelBuild", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelBuild.SetActive(false);
            panel.BuildGraph = BuildGraph(panel.PanelBuild.transform, "BuildGraph");

            panel.PanelWaves = MakePanel(content.transform, "PanelWaves", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelWaves.SetActive(false);

            var scrollGo = new GameObject("ScrollRect");
            scrollGo.transform.SetParent(panel.PanelWaves.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            var scroll = scrollGo.AddComponent<ScrollRect>();

            var viewport = MakePanel(scrollGo.transform, "Viewport", new Color(0f, 0f, 0f, 0.01f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("WaveContent");
            contentGo.transform.SetParent(viewport.transform, false);
            var wavesRt = contentGo.AddComponent<RectTransform>();
            wavesRt.anchorMin = new Vector2(0f, 1f);
            wavesRt.anchorMax = new Vector2(1f, 1f);
            wavesRt.pivot = new Vector2(0.5f, 1f);
            wavesRt.offsetMin = new Vector2(0f, 0f);
            wavesRt.offsetMax = new Vector2(0f, 0f);
            var waveLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            waveLayout.padding = new RectOffset(12, 12, 12, 12);
            waveLayout.spacing = 6;
            waveLayout.childControlHeight = false;
            waveLayout.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = wavesRt;
            scroll.horizontal = false;
            scroll.vertical = true;

            panel.WaveRowContainer = wavesRt;
            panel.WaveRowPrefab = BuildWaveRowPrefab();

            return panel;
        }

        GameObject BuildWaveRowPrefab()
        {
            var go = new GameObject("WaveRowRuntime");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 48f);
            go.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.88f);
            go.AddComponent<LayoutElement>().preferredHeight = 48f;

            var text = new GameObject("Label");
            text.transform.SetParent(go.transform, false);
            var textRt = text.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 0f);
            textRt.offsetMax = new Vector2(-10f, 0f);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 13;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return go;
        }

        LineGraphUI BuildGraph(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.10f);
            rt.anchorMax = new Vector2(0.95f, 0.90f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go.AddComponent<LineGraphUI>();
        }

        GameObject MakePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            go.AddComponent<Image>().color = color;
            return go;
        }

        TMP_Text MakeText(Transform parent, string name, string text, float fontSize, Vector2 anchorMin, Vector2 anchorMax, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.alignment = alignment;
            return tmp;
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = MakePanel(parent, name, color, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            var button = go.AddComponent<Button>();
            var text = MakeText(go.transform, "Label", label, 18, Vector2.zero, Vector2.one, FontStyles.Bold, TextAlignmentOptions.Center);
            text.color = Color.white;
            return button;
        }

        Button MakeLayoutButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 38f;
            go.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.32f, 1f);
            var button = go.AddComponent<Button>();
            MakeText(go.transform, "Label", label, 14, Vector2.zero, Vector2.one, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }
    }
}
