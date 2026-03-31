using CastleDefender.Game;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    public class PostWinFlowUI : MonoBehaviour
    {
        Canvas _canvas;
        GameObject _panel;
        TMP_Text _title;
        TMP_Text _body;
        Button _primaryButton;
        TMP_Text _primaryLabel;
        Button _secondaryButton;
        TMP_Text _secondaryLabel;
        Button _tertiaryButton;
        TMP_Text _tertiaryLabel;

        bool _winnerDecisionSubmitted;
        bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!Application.isPlaying)
                return;
            EnsureInstance();
            SceneManager.sceneLoaded += (_, __) => EnsureInstance();
        }

        public static void EnsureInstance()
        {
            if (!Application.isPlaying)
                return;
            if (FindFirstObjectByType<PostWinFlowUI>() != null) return;
            var go = new GameObject(nameof(PostWinFlowUI));
            DontDestroyOnLoad(go);
            go.AddComponent<PostWinFlowUI>();
        }

        void Awake()
        {
            if (!Application.isPlaying)
                return;
            DontDestroyOnLoad(gameObject);
            BuildUi();
            HidePanel();
        }

        void OnEnable()
        {
            TrySubscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Update()
        {
            if (!_subscribed)
                TrySubscribe();
        }

        void TrySubscribe()
        {
            if (_subscribed) return;
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLPvPResolved -= HandlePvPResolved;
            nm.OnMLSurvivalContinuationStarted -= HandleSurvivalStarted;
            nm.OnMLGameOver -= HandleGameOver;
            nm.OnMLMatchReady -= HandleMatchReady;
            nm.OnMLGameOver += HandleGameOver;
            nm.OnMLMatchReady += HandleMatchReady;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLPvPResolved -= HandlePvPResolved;
            nm.OnMLSurvivalContinuationStarted -= HandleSurvivalStarted;
            nm.OnMLGameOver -= HandleGameOver;
            nm.OnMLMatchReady -= HandleMatchReady;
            _subscribed = false;
        }

        void BuildUi()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;
            gameObject.AddComponent<GraphicRaycaster>();
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panelGo = new GameObject("Panel", typeof(Image));
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo;
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(720f, 340f);
            var panelImage = panelGo.GetComponent<Image>();
            panelImage.color = new Color(0.08f, 0.10f, 0.14f, 0.95f);

            _title = CreateText("Title", panelGo.transform, 34, FontStyles.Bold, TextAlignmentOptions.Center);
            var titleRt = _title.rectTransform;
            titleRt.anchorMin = new Vector2(0.08f, 0.72f);
            titleRt.anchorMax = new Vector2(0.92f, 0.92f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            _body = CreateText("Body", panelGo.transform, 24, FontStyles.Normal, TextAlignmentOptions.Center);
            var bodyRt = _body.rectTransform;
            bodyRt.anchorMin = new Vector2(0.10f, 0.40f);
            bodyRt.anchorMax = new Vector2(0.90f, 0.70f);
            bodyRt.offsetMin = Vector2.zero;
            bodyRt.offsetMax = Vector2.zero;

            _primaryButton = CreateButton("Primary", panelGo.transform, out _primaryLabel);
            SetButtonRect(_primaryButton.GetComponent<RectTransform>(), new Vector2(0.18f, 0.10f), new Vector2(0.46f, 0.28f));

            _secondaryButton = CreateButton("Secondary", panelGo.transform, out _secondaryLabel);
            SetButtonRect(_secondaryButton.GetComponent<RectTransform>(), new Vector2(0.54f, 0.10f), new Vector2(0.82f, 0.28f));

            _tertiaryButton = CreateButton("Tertiary", panelGo.transform, out _tertiaryLabel);
            SetButtonRect(_tertiaryButton.GetComponent<RectTransform>(), new Vector2(0.36f, 0.10f), new Vector2(0.64f, 0.28f));
            _tertiaryButton.gameObject.SetActive(false);
        }

        void HandlePvPResolved(MLPvPResolvedPayload payload)
        {
            HidePanel();
        }

        void HandleSurvivalStarted(MLSurvivalContinuationStartedPayload _)
        {
            HidePanel();
        }

        void HandleGameOver(MLGameOverPayload _)
        {
            HidePanel();
        }

        void HandleMatchReady(MLMatchReadyPayload _)
        {
            _winnerDecisionSubmitted = false;
            HidePanel();
        }

        void ShowWinnerPrompt()
        {
            _panel.SetActive(true);
            _title.text = "You won the match";
            _body.text = "Continue in survival mode?";
            ConfigureButton(_primaryButton, _primaryLabel, "Continue", OnContinueClicked);
            ConfigureButton(_secondaryButton, _secondaryLabel, "End Game Now", OnEndNowClicked);
            _tertiaryButton.gameObject.SetActive(false);
        }

        void ShowLoserPrompt()
        {
            _panel.SetActive(true);
            _title.text = "Defeated - Spectating";
            _body.text = "You can stay and watch the winners finish their survival run, or leave the match safely.";
            ConfigureButton(_primaryButton, _primaryLabel, "Spectate", HidePanel);
            ConfigureButton(_secondaryButton, _secondaryLabel, "Leave Match", OnLeaveMatchClicked);
            _tertiaryButton.gameObject.SetActive(false);
        }

        void OnContinueClicked()
        {
            if (_winnerDecisionSubmitted) return;
            _winnerDecisionSubmitted = true;
            ActionSender.ContinueAfterWin();
            _body.text = "Waiting for survival mode...";
            SetButtonsInteractable(false);
        }

        void OnEndNowClicked()
        {
            if (_winnerDecisionSubmitted) return;
            _winnerDecisionSubmitted = true;
            ActionSender.EndGameNow();
            _body.text = "Ending match...";
            SetButtonsInteractable(false);
        }

        void OnLeaveMatchClicked()
        {
            NetworkManager.Instance?.Emit("leave_game", null);
            HidePanel();
            LoadingScreen.LoadScene("Lobby");
        }

        void HidePanel()
        {
            if (_panel != null) _panel.SetActive(false);
            SetButtonsInteractable(true);
        }

        void SetButtonsInteractable(bool interactable)
        {
            if (_primaryButton != null) _primaryButton.interactable = interactable;
            if (_secondaryButton != null) _secondaryButton.interactable = interactable;
            if (_tertiaryButton != null) _tertiaryButton.interactable = interactable;
        }

        void ConfigureButton(Button button, TMP_Text label, string text, UnityEngine.Events.UnityAction onClick)
        {
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
            button.interactable = true;
            label.text = text;
        }

        static void SetButtonRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static TMP_Text CreateText(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        static Button CreateButton(string name, Transform parent, out TMP_Text label)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.19f, 0.31f, 0.47f, 0.98f);

            var button = go.GetComponent<Button>();
            var labelGo = new GameObject("Label", typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            label = labelGo.GetComponent<TextMeshProUGUI>();
            label.fontSize = 22;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            return button;
        }
    }
}
