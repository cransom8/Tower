// LoginUI.cs — Mandatory sign-in screen. Shown before the Lobby.
// If AuthManager already has a valid token, skips directly to Lobby.
// Supports email/password, forgot-password, and Google SSO (WebGL only).
//
// SCENE SETUP (Login.unity):
//   -- Place AuthManager + NetworkManager GameObjects here (they DontDestroyOnLoad)
//   Canvas
//   └── Panel_Login
//       ├── Txt_Title            — game title or "Sign In"
//       ├── Btn_Google           — Google SSO button (hidden until config loaded)
//       ├── Obj_Divider          — "or" row (hidden when only one method)
//       ├── Row_Tabs
//       │   ├── Btn_TabSignIn    — switches to sign-in fields
//       │   └── Btn_TabRegister  — switches to register fields
//       ├── Input_Email
//       ├── Input_DisplayName    — register only, hidden on Sign In tab
//       ├── Input_Password
//       ├── Txt_Error            — inline error message
//       ├── Btn_Submit           — "Sign In" or "Register"
//       └── Txt_Status           — secondary status (e.g. "Check your email")

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.Video;
using TMPro;
using Newtonsoft.Json;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class LoginUI : MonoBehaviour
    {
        GameObject _runtimeAudioListener;

        [Preserve]
        sealed class LoginRequest
        {
            public string email;
            public string password;
        }

        [Preserve]
        sealed class RegisterRequest
        {
            public string email;
            public string displayName;
            public string password;
        }

        [Preserve]
        sealed class ForgotPasswordRequest
        {
            public string email;
        }

        [Preserve]
        sealed class GoogleAuthRequest
        {
            public string idToken;
        }
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Panels")]
        public GameObject PanelLogin;

        [Header("Google SSO")]
        public Button     Btn_Google;
        public GameObject Obj_Divider;

        [Header("Tabs")]
        public Button     Btn_TabSignIn;
        public Button     Btn_TabRegister;

        [Header("Inputs")]
        public TMP_InputField Input_Email;
        public TMP_InputField Input_DisplayName;   // register only
        public TMP_InputField Input_Password;

        [Header("Actions")]
        public Button   Btn_Submit;
        public TMP_Text TxtSubmitBtn;

        [Header("Feedback")]
        public TMP_Text Txt_Error;
        public TMP_Text Txt_Status;

        [Header("Auxiliary Action")]
        public Button     Btn_Browser;        // repurposed as "Forgot Password?"
        public GameObject Obj_DevicePanel;    // shown while waiting for browser auth
        public TMP_Text   Txt_DeviceCode;     // the 6-char code to enter on the web
        public TMP_Text   Txt_DeviceStatus;   // "Waiting for browser..."
        public Button     Btn_DeviceCancel;

        [Header("Tab Colors")]
        public Color ColorTabActive   = new Color(0.41f, 0.64f, 0.94f, 1f);
        public Color ColorTabInactive = new Color(0.58f, 0.66f, 0.76f, 1f);

        [Header("Cinematics")]
        public bool EnableLoginCinematics = true;
        public string LoginCinematicFolder = "LoginCinematics";
        public string[] IntroClipFileNames =
        {
            "Blacksmith_First Slide.mp4",
            "Sword Quinch_Second Slide.mp4",
            "Long Goodbyes_Third Slide.mp4",
            "They are Coming_Last Slide.mp4",
        };
        public string LoginLoopClipFileName = "";
        public float FinalIntroClipRevealTimeSeconds = 7f;
        public float CinematicVideoVolume = 1f;
        public bool AllowIntroSkip = true;
        public float IntroTitleFadeInDuration = 0.55f;
        public float IntroTitleHoldDuration = 1.35f;
        public float IntroTitleFadeOutDuration = 0.65f;
        public float LoginRevealDuration = 0.45f;
        public bool HoldLoginBackgroundOnFinalFrame = true;
        public float LoginBackgroundFreezePaddingSeconds = 0.12f;
        public int LoginBackgroundFreezeFramePadding = 2;
        public float EndTitleRevealDelay = 0.12f;
        public float IntroTaglineDelay = 0.32f;
        public float IntroStampDuration = 0.16f;
        public float IntroStampSettleDuration = 0.20f;

        // ── State ─────────────────────────────────────────────────────────────
        bool   _isRegisterTab   = false;
        bool   _passwordEnabled = false;
        string _googleClientId  = "";
        bool   _busy            = false;
        string _deviceCode      = "";
        bool   _polling         = false;
        bool   _completingLogin = false;
        bool   _retryingCriticalContent = false;
        bool   _showingPreloadRetry = false;
        bool   _loginPresentationVisible = true;
        bool   _introSequenceActive = false;
        bool   _allowTapToSkipIntro = false;
        bool   _backgroundLoopActive = false;
        bool   _cinematicAssetsResolved = false;
        bool   _freezeLoginBackgroundOnPrepare = false;
        bool   _loginBackgroundFrozen = false;
        string _activeVideoPath = "";
        string _loopVideoPath = "";
        readonly Queue<string> _pendingIntroVideoPaths = new();
        readonly List<string> _resolvedIntroVideoPaths = new();

        CanvasGroup _loginPresentationGroup;
        CanvasGroup _introTitleGroup;
        CanvasGroup _introTitleCanvasGroup;
        CanvasGroup _introTaglineCanvasGroup;
        CanvasGroup _loginReadabilityShadeGroup;
        Button _skipIntroButton;
        TMP_Text _introTitleText;
        TMP_Text _introTaglineText;
        Image _introStampFlash;
        Vector2 _introTitleBasePosition;
        Vector2 _introTaglineBasePosition;
        RawImage _cinematicBackgroundImage;
        AspectRatioFitter _cinematicBackgroundAspect;
        VideoPlayer _cinematicVideoPlayer;
        RenderTexture _cinematicVideoTexture;
        Coroutine _introTitleRoutine;
        Coroutine _loginRevealRoutine;
        Coroutine _loginBackgroundFreezeRoutine;
        Coroutine _introTimedStopRoutine;

        public bool IsReadyForFinalRuntimeScreenshot
        {
            get
            {
                bool backgroundReady =
                    !_backgroundLoopActive ||
                    !HoldLoginBackgroundOnFinalFrame ||
                    string.IsNullOrWhiteSpace(_loopVideoPath) ||
                    _loginBackgroundFrozen;
                bool loginReady = _loginPresentationGroup == null || _loginPresentationGroup.alpha >= 0.99f;
                bool shadeReady = _loginReadabilityShadeGroup == null || _loginReadabilityShadeGroup.alpha >= 0.99f;
                bool titleReady = _introTitleGroup == null || _introTitleGroup.alpha >= 0.99f;
                bool titleTextReady = _introTitleCanvasGroup == null || _introTitleCanvasGroup.alpha >= 0.99f;
                bool taglineReady = _introTaglineCanvasGroup == null || _introTaglineCanvasGroup.alpha >= 0.99f;

                return
                    !_introSequenceActive &&
                    _loginPresentationVisible &&
                    backgroundReady &&
                    loginReady &&
                    shadeReady &&
                    titleReady &&
                    titleTextReady &&
                    taglineReady;
            }
        }

        public string FinalRuntimeScreenshotState
        {
            get
            {
                float loginAlpha = _loginPresentationGroup != null ? _loginPresentationGroup.alpha : 1f;
                float shadeAlpha = _loginReadabilityShadeGroup != null ? _loginReadabilityShadeGroup.alpha : 1f;
                float titleAlpha = _introTitleGroup != null ? _introTitleGroup.alpha : 1f;
                float titleTextAlpha = _introTitleCanvasGroup != null ? _introTitleCanvasGroup.alpha : 1f;
                float taglineAlpha = _introTaglineCanvasGroup != null ? _introTaglineCanvasGroup.alpha : 1f;
                string activeClip = string.IsNullOrWhiteSpace(_activeVideoPath) ? "(none)" : Path.GetFileName(_activeVideoPath);

                return
                    $"clip={activeClip}, " +
                    $"introSequence={_introSequenceActive}, " +
                    $"loginVisible={_loginPresentationVisible}, " +
                    $"backgroundFrozen={_loginBackgroundFrozen}, " +
                    $"backgroundLoop={_backgroundLoopActive}, " +
                    $"loginAlpha={loginAlpha:0.00}, " +
                    $"shadeAlpha={shadeAlpha:0.00}, " +
                    $"titleAlpha={titleAlpha:0.00}, " +
                    $"titleTextAlpha={titleTextAlpha:0.00}, " +
                    $"taglineAlpha={taglineAlpha:0.00}";
            }
        }

        public void SkipIntroForAutomation()
        {
            if (_loginPresentationVisible)
                return;

            SkipLoginIntro();
        }

        // ── WebGL jslib imports ───────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void JSIO_GoogleSignIn(string clientId, string gameObjectName);
#endif

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            EnsureLoginAudioListener();
        }

        void Start()
        {
            EnsurePersistentEventSystem();
            ApplyPremiumPresentation();

            // Hide Google button until config says it is available
            if (Btn_Google  != null) Btn_Google.gameObject.SetActive(false);
            if (Obj_Divider != null) Obj_Divider.SetActive(false);

            // Wire buttons
            if (Btn_TabSignIn)   Btn_TabSignIn.onClick.AddListener(()  => SetTab(false));
            if (Btn_TabRegister) Btn_TabRegister.onClick.AddListener(() => SetTab(true));
            if (Btn_Submit)      Btn_Submit.onClick.AddListener(OnSubmit);
            if (Btn_Google)      Btn_Google.onClick.AddListener(OnGoogleSignIn);
            if (Btn_Browser)     Btn_Browser.onClick.AddListener(OnAuxiliaryAction);
            if (Btn_DeviceCancel)Btn_DeviceCancel.onClick.AddListener(CancelDeviceFlow);
            if (Obj_DevicePanel) Obj_DevicePanel.SetActive(false);
            ConfigureAuxiliaryAction();

            SetTab(false);
            SetError("");
            SetStatus("");

            StartCoroutine(FetchConfig());

            if (AuthManager.IsAuthenticated)
            {
                ShowLoginPresentation(true);
                SetStatus("Preparing your game session...");
                StartCoroutine(CompleteLoginAndEnterLobby(0f));
            }
            else
            {
                StartLoginCinematicSequence();
            }
        }

        void Update()
        {
            if (!_allowTapToSkipIntro || _loginPresentationVisible)
                return;

            if (Input.GetMouseButtonDown(0) || Input.touchCount > 0 || Input.anyKeyDown)
                SkipLoginIntro();
        }

        void OnDestroy()
        {
            if (_cinematicVideoPlayer != null)
            {
                _cinematicVideoPlayer.prepareCompleted -= HandleCinematicVideoPrepared;
                _cinematicVideoPlayer.loopPointReached -= HandleCinematicVideoLoopPointReached;
                _cinematicVideoPlayer.errorReceived -= HandleCinematicVideoError;
                _cinematicVideoPlayer.Stop();
            }

            if (_loginBackgroundFreezeRoutine != null)
                StopCoroutine(_loginBackgroundFreezeRoutine);

            if (_introTimedStopRoutine != null)
                StopCoroutine(_introTimedStopRoutine);

            if (_cinematicVideoTexture != null)
            {
                if (_cinematicVideoTexture.IsCreated())
                    _cinematicVideoTexture.Release();
                Destroy(_cinematicVideoTexture);
                _cinematicVideoTexture = null;
            }

            if (_runtimeAudioListener != null)
                Destroy(_runtimeAudioListener);
        }

        // ── Config fetch ──────────────────────────────────────────────────────

        IEnumerator FetchConfig()
        {
            string url = ResolvedBaseUrl + "/config";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 8;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[LoginUI] FetchConfig failed — result:{req.result} error:'{req.error}' url:{url} body:'{req.downloadHandler?.text}'");
                SetStatus("Could not reach server. Check connection.");
                yield break;
            }

            try
            {
                var cfg = JsonConvert.DeserializeObject<ConfigResponse>(req.downloadHandler.text);
                _passwordEnabled = cfg.passwordAuthEnabled;
                _googleClientId  = cfg.googleClientId ?? "";

                bool hasGoogle   = !string.IsNullOrEmpty(_googleClientId);
                bool hasPassword = _passwordEnabled;

                if (Btn_Google != null)
                    Btn_Google.gameObject.SetActive(hasGoogle);

                // Show divider only when both methods are available
                if (Obj_Divider != null)
                    Obj_Divider.SetActive(hasGoogle && hasPassword);

                // Hide tabs + password form if only Google is configured
                if (!hasPassword)
                {
                    if (Btn_TabSignIn   != null) Btn_TabSignIn.gameObject.SetActive(false);
                    if (Btn_TabRegister != null) Btn_TabRegister.gameObject.SetActive(false);
                    if (Input_Email     != null) SetFieldContainerActive(Input_Email, false);
                    if (Input_Password  != null) SetFieldContainerActive(Input_Password, false);
                    if (Input_DisplayName != null) SetFieldContainerActive(Input_DisplayName, false);
                    if (Btn_Submit      != null) Btn_Submit.gameObject.SetActive(false);
                    ShowContentRetryAction(false);
                    SetStatus("Use Google to sign in.");
                }
                else
                {
                    RefreshAuxiliaryAction();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Login] Config parse error: {ex.Message}");
            }
        }

        // ── Tab switching ─────────────────────────────────────────────────────

        void SetTab(bool register)
        {
            _isRegisterTab = register;

            if (Input_DisplayName != null)
                SetFieldContainerActive(Input_DisplayName, register);

            if (TxtSubmitBtn != null)
                TxtSubmitBtn.text = register ? "Register" : "Sign In";

            RefreshTabPresentation();

            RefreshAuxiliaryAction();

            SetError("");
            SetStatus("");
        }

        void ApplyPremiumPresentation()
        {
            if (PanelLogin == null)
                return;

            var canvas = PanelLogin.GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            if (canvas != null)
                ClassicRpgUiRuntime.ApplyCanvasScaler(canvas.GetComponent<CanvasScaler>(), ClassicRpgUiRuntime.ReferenceResolution);

            var root = PanelLogin.transform as RectTransform;
            if (root == null)
                return;

            bool compact = ClassicRpgUiRuntime.IsCompactLayout(canvasRect);

            var rootImage = PanelLogin.GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.sprite = null;
                rootImage.type = Image.Type.Simple;
                rootImage.color = new Color(0.02f, 0.03f, 0.05f, 1f);
            }

            DestroyGeneratedChild(root, "PremiumLoginBackdrop");
            DestroyGeneratedChild(root, "PremiumLoginSafeArea");
            DestroyGeneratedChild(root, "PremiumLoginStage");
            DestroyGeneratedChild(root, "PremiumLoginIntroOverlay");
            DisableLegacyCard(root);

            BuildCinematicBackdrop(root, compact);

            var safeArea = CreateUiRect("PremiumLoginSafeArea", root);
            ClassicRpgUiRuntime.ApplySafeArea(
                safeArea,
                canvasRect,
                compact ? 20f : 48f,
                compact ? 18f : 38f,
                compact ? 18f : 34f);

            var stage = CreateUiRect("PremiumLoginStage", safeArea);
            ClassicRpgUiRuntime.Stretch(stage);
            var cardColumn = BuildModernLoginStage(stage, compact);
            var cardContent = BuildModernLoginShell(cardColumn, compact);
            PopulateModernLoginCard(cardContent, compact);
            StyleDevicePanel(root, canvasRect, compact);
            BuildIntroOverlay(root, compact);
            ShowLoginPresentation(false, true);
        }

        void BuildCinematicBackdrop(RectTransform root, bool compact)
        {
            var backdrop = CreateUiRect("PremiumLoginBackdrop", root);
            Stretch(backdrop);
            backdrop.SetSiblingIndex(0);

            var videoRoot = CreateUiRect("VideoRoot", backdrop);
            Stretch(videoRoot);

            var videoGo = new GameObject("BackgroundVideo", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            videoGo.transform.SetParent(videoRoot, false);
            _cinematicBackgroundImage = videoGo.GetComponent<RawImage>();
            _cinematicBackgroundImage.raycastTarget = false;
            _cinematicBackgroundImage.color = Color.white;
            _cinematicBackgroundAspect = videoGo.GetComponent<AspectRatioFitter>();
            _cinematicBackgroundAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _cinematicBackgroundAspect.aspectRatio = 16f / 9f;
            Stretch(videoGo.GetComponent<RectTransform>());

            var tint = CreatePlainImage("BaseTint", backdrop, new Color(0f, 0f, 0f, 0.18f));
            Stretch(tint.rectTransform);

            var vignette = CreatePlainImage("Vignette", backdrop, new Color(0f, 0f, 0f, 0.10f));
            Stretch(vignette.rectTransform);

            var readabilityShade = CreatePlainImage(
                "ReadabilityShade",
                backdrop,
                compact ? new Color(0f, 0f, 0f, 0.20f) : new Color(0f, 0f, 0f, 0.14f));
            _loginReadabilityShadeGroup = readabilityShade.gameObject.AddComponent<CanvasGroup>();
            _loginReadabilityShadeGroup.alpha = 0f;
            readabilityShade.rectTransform.anchorMin = Vector2.zero;
            readabilityShade.rectTransform.anchorMax = Vector2.one;
            readabilityShade.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            readabilityShade.rectTransform.offsetMin = Vector2.zero;
            readabilityShade.rectTransform.offsetMax = Vector2.zero;

            EnsureCinematicVideoPlayer(backdrop);
        }

        RectTransform BuildModernLoginStage(RectTransform stage, bool compact)
        {
            var cardColumn = CreateUiRect("CardColumn", stage);
            Stretch(cardColumn);
            _loginPresentationGroup = cardColumn.gameObject.AddComponent<CanvasGroup>();

            return cardColumn;
        }

        RectTransform BuildModernLoginShell(RectTransform parent, bool compact)
        {
            var shell = CreateUiRect("LoginShell", parent);
            shell.anchorMin = new Vector2(0.5f, 1f);
            shell.anchorMax = new Vector2(0.5f, 1f);
            shell.pivot = new Vector2(0.5f, 1f);
            shell.anchoredPosition = compact ? new Vector2(0f, -528f) : new Vector2(0f, -364f);
            shell.sizeDelta = compact ? new Vector2(360f, 0f) : new Vector2(480f, 0f);

            var fitter = shell.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = shell.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 10f : 12f;
            layout.padding = compact ? new RectOffset(10, 10, 0, 0) : new RectOffset(12, 12, 0, 0);

            return shell;
        }

        void PopulateModernLoginCard(RectTransform content, bool compact)
        {
            var title = FindDescendant(PanelLogin.transform, "Txt_Title") as RectTransform;
            var rowTabs = FindDescendant(PanelLogin.transform, "Row_Tabs") as RectTransform;

            if (title != null)
            {
                title.gameObject.SetActive(false);
            }

            if (rowTabs != null)
            {
                rowTabs.SetParent(content, false);
                PrepareForLayout(rowTabs, compact ? 24f : 26f);
                var tabsLayout = rowTabs.GetComponent<HorizontalLayoutGroup>() ?? rowTabs.gameObject.AddComponent<HorizontalLayoutGroup>();
                tabsLayout.childAlignment = TextAnchor.MiddleCenter;
                tabsLayout.childControlWidth = false;
                tabsLayout.childControlHeight = true;
                tabsLayout.childForceExpandWidth = false;
                tabsLayout.childForceExpandHeight = true;
                tabsLayout.spacing = compact ? 22f : 28f;
                tabsLayout.padding = new RectOffset(0, 0, 0, 0);
            }

            StyleTabButton(Btn_TabSignIn, compact ? 0f : 96f);
            StyleTabButton(Btn_TabRegister, compact ? 0f : 96f);

            CreateLabeledFieldRow(content, "Email", Input_Email, compact);
            CreateLabeledFieldRow(content, "Display Name", Input_DisplayName, compact);
            CreateLabeledFieldRow(content, "Password", Input_Password, compact);

            if (Btn_Submit != null)
            {
                Btn_Submit.transform.SetParent(content, false);
                PrepareButton(Btn_Submit, compact ? 44f : 48f, compact ? 340f : 460f);
                if (TxtSubmitBtn != null)
                    TxtSubmitBtn.fontSize = compact ? 15f : 17f;
                ApplyModernButtonStyle(
                    Btn_Submit,
                    new Color(0.03f, 0.04f, 0.05f, 0.54f),
                    new Color(0.05f, 0.06f, 0.08f, 0.62f),
                    new Color(0.02f, 0.03f, 0.04f, 0.72f),
                    Color.white,
                    TxtSubmitBtn);
            }

            if (Btn_Browser != null)
            {
                Btn_Browser.transform.SetParent(content, false);
                PrepareButton(Btn_Browser, compact ? 20f : 22f, compact ? 340f : 460f);
                ApplyMinimalLinkButtonStyle(Btn_Browser, new Color(0.90f, 0.92f, 0.95f, 0.96f));
            }

            if (Obj_Divider != null)
                Obj_Divider.SetActive(false);

            if (Btn_Google != null)
            {
                Btn_Google.transform.SetParent(content, false);
                PrepareButton(Btn_Google, compact ? 42f : 46f, compact ? 340f : 460f);
                ApplyModernButtonStyle(
                    Btn_Google,
                    new Color(0.03f, 0.04f, 0.05f, 0.46f),
                    new Color(0.05f, 0.06f, 0.08f, 0.56f),
                    new Color(0.02f, 0.03f, 0.04f, 0.68f),
                    new Color(0.96f, 0.97f, 0.99f, 0.98f));
                var googleText = Btn_Google.GetComponentInChildren<TMP_Text>(true);
                if (googleText != null)
                {
                    googleText.fontSize = compact ? 14f : 16f;
                    googleText.text = "Sign in with Google";
                }
            }

            if (Txt_Error != null)
            {
                Txt_Error.transform.SetParent(content, false);
                PrepareForLayout(Txt_Error.rectTransform, 32f);
                Txt_Error.textWrappingMode = TextWrappingModes.Normal;
                ApplyModernTextStyle(Txt_Error, 12f, new Color(0.92f, 0.43f, 0.40f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
            }

            if (Txt_Status != null)
            {
                Txt_Status.transform.SetParent(content, false);
                PrepareForLayout(Txt_Status.rectTransform, 34f);
                Txt_Status.textWrappingMode = TextWrappingModes.Normal;
                ApplyModernTextStyle(Txt_Status, 12f, new Color(0.68f, 0.77f, 0.86f, 0.94f), FontStyles.Normal, TextAlignmentOptions.Center);
            }
        }

        void BuildIntroOverlay(RectTransform root, bool compact)
        {
            var overlay = CreateUiRect("PremiumLoginIntroOverlay", root);
            Stretch(overlay);

            var titleGroupRoot = CreateUiRect("TitleGroup", overlay);
            Stretch(titleGroupRoot);
            _introTitleGroup = titleGroupRoot.gameObject.AddComponent<CanvasGroup>();
            _introTitleGroup.alpha = 0f;

            _introTitleText = CreateModernText(
                "IntroTitle",
                titleGroupRoot,
                "RansomForge",
                compact ? 62f : 90f,
                new Color(0.98f, 0.97f, 0.95f, 1f),
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            _introTitleText.overflowMode = TextOverflowModes.Overflow;
            _introTitleBasePosition = compact ? new Vector2(0f, -138f) : new Vector2(0f, -96f);
            ConfigureOverlayTextRect(_introTitleText.rectTransform, _introTitleBasePosition, compact ? new Vector2(900f, 84f) : new Vector2(1500f, 112f));
            _introTitleCanvasGroup = _introTitleText.gameObject.AddComponent<CanvasGroup>();
            ApplyIntroTitleTreatment(_introTitleText);

            _introStampFlash = CreatePlainImage("StampFlash", _introTitleText.transform, new Color(1f, 0.66f, 0.24f, 0f));
            _introStampFlash.transform.SetAsFirstSibling();
            Stretch(
                _introStampFlash.rectTransform,
                compact ? new Vector2(-160f, 20f) : new Vector2(-240f, 28f),
                compact ? new Vector2(160f, -20f) : new Vector2(240f, -28f));

            _introTaglineText = CreateModernText(
                "IntroTagline",
                titleGroupRoot,
                "Forged for War",
                compact ? 20f : 24f,
                new Color(0.86f, 0.89f, 0.94f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            _introTaglineText.overflowMode = TextOverflowModes.Overflow;
            _introTaglineBasePosition = compact ? new Vector2(0f, -214f) : new Vector2(0f, -194f);
            ConfigureOverlayTextRect(_introTaglineText.rectTransform, _introTaglineBasePosition, compact ? new Vector2(720f, 28f) : new Vector2(960f, 32f));
            _introTaglineCanvasGroup = _introTaglineText.gameObject.AddComponent<CanvasGroup>();
            ApplyIntroTaglineTreatment(_introTaglineText);

            ResetIntroOverlayVisualState();

            if (AllowIntroSkip)
            {
                _skipIntroButton = CreateModernButton("SkipIntro", overlay, "Skip");
                var skipRect = _skipIntroButton.transform as RectTransform;
                if (skipRect != null)
                {
                    skipRect.anchorMin = new Vector2(1f, 1f);
                    skipRect.anchorMax = new Vector2(1f, 1f);
                    skipRect.pivot = new Vector2(1f, 1f);
                    skipRect.anchoredPosition = compact ? new Vector2(-12f, -12f) : new Vector2(-4f, -4f);
                    skipRect.sizeDelta = new Vector2(120f, 38f);
                }

                ApplyModernButtonStyle(
                    _skipIntroButton,
                    new Color(0.04f, 0.07f, 0.11f, 0.34f),
                    new Color(0.07f, 0.11f, 0.16f, 0.44f),
                    new Color(0.03f, 0.05f, 0.08f, 0.32f),
                    new Color(0.86f, 0.90f, 0.96f, 0.98f));
                _skipIntroButton.onClick.AddListener(SkipLoginIntro);
            }
        }

        void StartLoginCinematicSequence()
        {
            ResolveLoginCinematicPaths();

            if (!EnableLoginCinematics || (_resolvedIntroVideoPaths.Count == 0 && string.IsNullOrWhiteSpace(_loopVideoPath)))
            {
                ShowPersistentBrandImmediate();
                ShowLoginPresentation(true);
                return;
            }

            _allowTapToSkipIntro = AllowIntroSkip;
            _backgroundLoopActive = false;
            _pendingIntroVideoPaths.Clear();
            foreach (var path in _resolvedIntroVideoPaths)
                _pendingIntroVideoPaths.Enqueue(path);

            if (_pendingIntroVideoPaths.Count == 0)
            {
                StartLoopBackground();
                ShowPersistentBrandImmediate();
                ShowLoginPresentation(true);
                return;
            }

            _introSequenceActive = true;
            UpdateSkipIntroVisibility(true);
            StartNextIntroClip();
        }

        void ResolveLoginCinematicPaths()
        {
            if (_cinematicAssetsResolved)
                return;

            _cinematicAssetsResolved = true;
            _resolvedIntroVideoPaths.Clear();

            foreach (var clipName in IntroClipFileNames)
            {
                string path = BuildStreamingAssetUrl(clipName);
                if (!string.IsNullOrWhiteSpace(path))
                    _resolvedIntroVideoPaths.Add(path);
            }

            _loopVideoPath = BuildStreamingAssetUrl(LoginLoopClipFileName);
        }

        void StartNextIntroClip()
        {
            if (_pendingIntroVideoPaths.Count == 0)
            {
                BeginLoginLoopAndReveal();
                return;
            }

            PlayCinematicClip(_pendingIntroVideoPaths.Dequeue(), false);
        }

        void StartLoopBackground()
        {
            _backgroundLoopActive = !string.IsNullOrWhiteSpace(_loopVideoPath);
            if (_backgroundLoopActive)
                PlayCinematicClip(_loopVideoPath, !HoldLoginBackgroundOnFinalFrame);
        }

        void BeginLoginLoopAndReveal(bool skipBrandReveal = false)
        {
            _introSequenceActive = false;
            bool hasLoopBackground = !string.IsNullOrWhiteSpace(_loopVideoPath);
            if (hasLoopBackground)
                StartLoopBackground();
            else
                FreezeCurrentCinematicFrame();

            if (_introTitleRoutine != null)
            {
                StopCoroutine(_introTitleRoutine);
                _introTitleRoutine = null;
            }

            if (skipBrandReveal || !hasLoopBackground)
            {
                _allowTapToSkipIntro = false;
                UpdateSkipIntroVisibility(false);
                ShowPersistentBrandImmediate();
                ShowLoginPresentation(true);
                return;
            }

            _allowTapToSkipIntro = AllowIntroSkip;
            UpdateSkipIntroVisibility(_allowTapToSkipIntro);
            _introTitleRoutine = StartCoroutine(RevealBrandThenShowLogin());
        }

        void SkipLoginIntro()
        {
            if (_loginPresentationVisible)
                return;

            _pendingIntroVideoPaths.Clear();
            BeginLoginLoopAndReveal(true);
        }

        void ShowLoginPresentation(bool show, bool immediate = false)
        {
            _loginPresentationVisible = show;
            if (_loginRevealRoutine != null)
                StopCoroutine(_loginRevealRoutine);

            if (immediate)
            {
                SetCanvasGroupState(_loginPresentationGroup, show ? 1f : 0f, show);
                SetCanvasGroupState(_loginReadabilityShadeGroup, show ? 1f : 0f, false);
                return;
            }

            _loginRevealRoutine = StartCoroutine(AnimateLoginPresentation(show));
        }

        IEnumerator AnimateIntroTitle()
        {
            if (_introTitleGroup == null || _introTitleCanvasGroup == null || _introTaglineCanvasGroup == null)
                yield break;

            ResetIntroOverlayVisualState();
            _introTitleGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, EndTitleRevealDelay));
            yield return StampIntroElement(_introTitleCanvasGroup, _introTitleText.rectTransform, _introTitleBasePosition, new Vector2(0f, -22f), 1.22f, 0.93f);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, IntroTaglineDelay));
            yield return StampIntroElement(_introTaglineCanvasGroup, _introTaglineText.rectTransform, _introTaglineBasePosition, new Vector2(0f, -10f), 1.14f, 0.96f);
            yield return new WaitForSecondsRealtime(IntroTitleHoldDuration);
        }

        IEnumerator RevealBrandThenShowLogin()
        {
            if (HoldLoginBackgroundOnFinalFrame && !string.IsNullOrWhiteSpace(_loopVideoPath))
            {
                float elapsed = 0f;
                while (!_loginBackgroundFrozen && elapsed < 8f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            yield return AnimateIntroTitle();

            _allowTapToSkipIntro = false;
            UpdateSkipIntroVisibility(false);
            ShowLoginPresentation(true);
            _introTitleRoutine = null;
        }

        IEnumerator AnimateLoginPresentation(bool show)
        {
            float loginTarget = show ? 1f : 0f;
            float shadeTarget = show ? 1f : 0f;
            float loginStart = _loginPresentationGroup != null ? _loginPresentationGroup.alpha : loginTarget;
            float shadeStart = _loginReadabilityShadeGroup != null ? _loginReadabilityShadeGroup.alpha : shadeTarget;

            if (_loginPresentationGroup != null)
            {
                _loginPresentationGroup.interactable = show;
                _loginPresentationGroup.blocksRaycasts = show;
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, LoginRevealDuration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);

                if (_loginPresentationGroup != null)
                    _loginPresentationGroup.alpha = Mathf.Lerp(loginStart, loginTarget, eased);

                if (_loginReadabilityShadeGroup != null)
                    _loginReadabilityShadeGroup.alpha = Mathf.Lerp(shadeStart, shadeTarget, eased);

                yield return null;
            }

            SetCanvasGroupState(_loginPresentationGroup, loginTarget, show);
            SetCanvasGroupState(_loginReadabilityShadeGroup, shadeTarget, false);
        }

        IEnumerator FadeCanvasGroup(CanvasGroup group, float targetAlpha, float duration)
        {
            if (group == null)
                yield break;

            float startAlpha = group.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            group.alpha = targetAlpha;
        }

        IEnumerator StampIntroElement(CanvasGroup group, RectTransform rect, Vector2 settledPosition, Vector2 impactOffset, float startScale, float impactScale)
        {
            if (group == null || rect == null)
                yield break;

            Vector2 startPosition = settledPosition + impactOffset;
            Vector2 impactPosition = settledPosition + new Vector2(0f, 3f);
            Vector3 startVector = Vector3.one * startScale;
            Vector3 impactVector = Vector3.one * impactScale;

            group.alpha = 0f;
            rect.anchoredPosition = startPosition;
            rect.localScale = startVector;

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, IntroStampDuration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 4f);
                group.alpha = eased;
                rect.anchoredPosition = Vector2.Lerp(startPosition, impactPosition, eased);
                rect.localScale = Vector3.Lerp(startVector, impactVector, eased);

                if (_introStampFlash != null)
                    _introStampFlash.color = new Color(1f, 0.66f, 0.24f, Mathf.Lerp(0.28f, 0f, t));

                yield return null;
            }

            elapsed = 0f;
            duration = Mathf.Max(0.01f, IntroStampSettleDuration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                rect.anchoredPosition = Vector2.Lerp(impactPosition, settledPosition, eased);
                rect.localScale = Vector3.Lerp(impactVector, Vector3.one, eased);

                if (_introStampFlash != null)
                    _introStampFlash.color = new Color(1f, 0.66f, 0.24f, Mathf.Lerp(0.12f, 0f, eased));

                yield return null;
            }

            group.alpha = 1f;
            rect.anchoredPosition = settledPosition;
            rect.localScale = Vector3.one;

            if (_introStampFlash != null)
                _introStampFlash.color = new Color(1f, 0.66f, 0.24f, 0f);
        }

        void EnsureCinematicVideoPlayer(Transform parent)
        {
            if (_cinematicVideoPlayer != null)
                return;

            var videoPlayerGo = new GameObject("CinematicVideoPlayer", typeof(VideoPlayer));
            videoPlayerGo.transform.SetParent(parent, false);
            _cinematicVideoPlayer = videoPlayerGo.GetComponent<VideoPlayer>();
            _cinematicVideoPlayer.source = VideoSource.Url;
            _cinematicVideoPlayer.playOnAwake = false;
            _cinematicVideoPlayer.waitForFirstFrame = true;
            _cinematicVideoPlayer.skipOnDrop = true;
            // Login cinematics can keep their embedded audio while loop music still comes from AudioManager.
            _cinematicVideoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _cinematicVideoPlayer.renderMode = VideoRenderMode.RenderTexture;

            _cinematicVideoTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            _cinematicVideoTexture.name = "LoginCinematicVideo";
            _cinematicVideoTexture.Create();
            _cinematicVideoPlayer.targetTexture = _cinematicVideoTexture;

            if (_cinematicBackgroundImage != null)
                _cinematicBackgroundImage.texture = _cinematicVideoTexture;

            _cinematicVideoPlayer.prepareCompleted += HandleCinematicVideoPrepared;
            _cinematicVideoPlayer.loopPointReached += HandleCinematicVideoLoopPointReached;
            _cinematicVideoPlayer.errorReceived += HandleCinematicVideoError;
        }

        void PlayCinematicClip(string path, bool loop)
        {
            if (_cinematicVideoPlayer == null || string.IsNullOrWhiteSpace(path))
                return;

            if (_loginBackgroundFreezeRoutine != null)
            {
                StopCoroutine(_loginBackgroundFreezeRoutine);
                _loginBackgroundFreezeRoutine = null;
            }

            if (_introTimedStopRoutine != null)
            {
                StopCoroutine(_introTimedStopRoutine);
                _introTimedStopRoutine = null;
            }

            _activeVideoPath = path;
            _freezeLoginBackgroundOnPrepare =
                HoldLoginBackgroundOnFinalFrame &&
                !string.IsNullOrWhiteSpace(_loopVideoPath) &&
                string.Equals(path, _loopVideoPath, StringComparison.OrdinalIgnoreCase);
            _loginBackgroundFrozen = false;
            _cinematicVideoPlayer.Stop();
            _cinematicVideoPlayer.isLooping = loop;
            _cinematicVideoPlayer.url = path;
            _cinematicVideoPlayer.Prepare();
        }

        void HandleCinematicVideoPrepared(VideoPlayer source)
        {
            if (_cinematicBackgroundAspect != null && source.width > 0 && source.height > 0)
                _cinematicBackgroundAspect.aspectRatio = (float)source.width / source.height;

            if (_cinematicBackgroundImage != null && source.targetTexture != null)
                _cinematicBackgroundImage.texture = source.targetTexture;

            for (ushort trackIndex = 0; trackIndex < source.audioTrackCount; trackIndex++)
            {
                source.EnableAudioTrack(trackIndex, true);
                source.SetDirectAudioMute(trackIndex, false);
                if (source.canSetDirectAudioVolume)
                    source.SetDirectAudioVolume(trackIndex, Mathf.Max(0f, CinematicVideoVolume));
            }

            source.Play();

            if (_freezeLoginBackgroundOnPrepare)
                _loginBackgroundFreezeRoutine = StartCoroutine(HoldVideoOnFinalFrame(source));
            else if (ShouldStopFinalIntroClipEarly(source))
                _introTimedStopRoutine = StartCoroutine(StopFinalIntroClipAtConfiguredTime(source));
        }

        bool ShouldStopFinalIntroClipEarly(VideoPlayer source)
        {
            return
                source != null &&
                _introSequenceActive &&
                _resolvedIntroVideoPaths.Count > 0 &&
                !string.IsNullOrWhiteSpace(_activeVideoPath) &&
                string.Equals(_activeVideoPath, _resolvedIntroVideoPaths[_resolvedIntroVideoPaths.Count - 1], StringComparison.OrdinalIgnoreCase) &&
                FinalIntroClipRevealTimeSeconds > 0f;
        }

        void HandleCinematicVideoLoopPointReached(VideoPlayer source)
        {
            if (_freezeLoginBackgroundOnPrepare &&
                !string.IsNullOrWhiteSpace(_loopVideoPath) &&
                string.Equals(_activeVideoPath, _loopVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                source.Pause();
                _loginBackgroundFrozen = true;
                return;
            }

            if (_backgroundLoopActive && source.isLooping)
                return;

            if (_introSequenceActive)
            {
                StartNextIntroClip();
                return;
            }

            if (!_loginPresentationVisible)
                BeginLoginLoopAndReveal();
        }

        void HandleCinematicVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"[LoginUI] Cinematic video error for '{_activeVideoPath}': {message}");

            if (_introSequenceActive)
            {
                StartNextIntroClip();
                return;
            }

            ShowLoginPresentation(true);
        }

        IEnumerator HoldVideoOnFinalFrame(VideoPlayer source)
        {
            if (source == null)
                yield break;

            while (source != null && source.isPrepared)
            {
                if (!source.isPlaying)
                {
                    yield return null;
                    continue;
                }

                bool reachedFreezeFrame = false;
                if (source.frameCount > 0)
                {
                    long paddingFrames = Math.Max(1, LoginBackgroundFreezeFramePadding);
                    long freezeFrame = Math.Max(0L, (long)source.frameCount - paddingFrames);
                    reachedFreezeFrame = source.frame >= freezeFrame;
                }

                bool reachedFreezeTime = false;
                if (source.length > 0d)
                {
                    double freezeTime = Math.Max(0d, source.length - Math.Max(0.01f, LoginBackgroundFreezePaddingSeconds));
                    reachedFreezeTime = source.time >= freezeTime;
                }

                if (reachedFreezeFrame || reachedFreezeTime)
                {
                    source.Pause();
                    _loginBackgroundFrozen = true;
                    _loginBackgroundFreezeRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _loginBackgroundFreezeRoutine = null;
        }

        IEnumerator StopFinalIntroClipAtConfiguredTime(VideoPlayer source)
        {
            if (source == null)
                yield break;

            double revealTime = Math.Max(0.05d, FinalIntroClipRevealTimeSeconds);
            if (source.length > 0d)
                revealTime = Math.Min(revealTime, Math.Max(0.05d, source.length));

            while (source != null && source.isPrepared)
            {
                if (!source.isPlaying)
                {
                    yield return null;
                    continue;
                }

                if (source.time >= revealTime)
                {
                    source.Pause();
                    _loginBackgroundFrozen = true;
                    _introTimedStopRoutine = null;
                    BeginLoginLoopAndReveal();
                    yield break;
                }

                yield return null;
            }

            _introTimedStopRoutine = null;
        }

        void FreezeCurrentCinematicFrame()
        {
            if (_cinematicVideoPlayer == null || !_cinematicVideoPlayer.isPrepared)
                return;

            _cinematicVideoPlayer.Pause();
            _loginBackgroundFrozen = true;
        }

        void UpdateSkipIntroVisibility(bool show)
        {
            if (_skipIntroButton == null)
                return;

            _skipIntroButton.gameObject.SetActive(show);
        }

        string BuildStreamingAssetUrl(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            string basePath = Application.streamingAssetsPath?.TrimEnd('/', '\\') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(basePath))
                return string.Empty;

            string normalizedFolder = (LoginCinematicFolder ?? string.Empty).Trim().Trim('/', '\\');
            string normalizedFile = fileName.Trim().Trim('/', '\\');
            if (string.IsNullOrWhiteSpace(normalizedFile))
                return string.Empty;

            if (basePath.Contains("://", StringComparison.Ordinal))
            {
                string url = string.IsNullOrWhiteSpace(normalizedFolder)
                    ? $"{basePath}/{normalizedFile}"
                    : $"{basePath}/{normalizedFolder}/{normalizedFile}";
                return url.Replace("\\", "/");
            }

            string path = string.IsNullOrWhiteSpace(normalizedFolder)
                ? Path.Combine(basePath, normalizedFile)
                : Path.Combine(basePath, normalizedFolder, normalizedFile);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[LoginUI] Login cinematic clip not found at '{path}'.");
                return string.Empty;
            }

            return path;
        }

        static void SetCanvasGroupState(CanvasGroup group, float alpha, bool interactive)
        {
            if (group == null)
                return;

            group.alpha = alpha;
            group.interactable = interactive;
            group.blocksRaycasts = interactive;
        }

        void BuildWideLoginStage(RectTransform stage, out RectTransform brandColumn, out RectTransform cardColumn)
        {
            var layout = stage.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.spacing = 28f;
            layout.padding = new RectOffset(10, 10, 10, 10);

            brandColumn = CreateUiRect("BrandColumn", stage);
            var brandLayout = brandColumn.gameObject.AddComponent<LayoutElement>();
            brandLayout.flexibleWidth = 1f;
            brandLayout.flexibleHeight = 1f;
            brandLayout.minWidth = 0f;

            cardColumn = CreateUiRect("CardColumn", stage);
            var cardLayout = cardColumn.gameObject.AddComponent<LayoutElement>();
            cardLayout.preferredWidth = 560f;
            cardLayout.minWidth = 520f;
            cardLayout.flexibleHeight = 1f;
        }

        void BuildCompactLoginStage(RectTransform stage, out RectTransform brandColumn, out RectTransform cardColumn)
        {
            var layout = stage.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 16f;
            layout.padding = new RectOffset(0, 0, 0, 0);

            brandColumn = CreateUiRect("BrandColumn", stage);
            var brandLayout = brandColumn.gameObject.AddComponent<LayoutElement>();
            brandLayout.preferredHeight = 264f;
            brandLayout.flexibleWidth = 1f;

            cardColumn = CreateUiRect("CardColumn", stage);
            var cardLayout = cardColumn.gameObject.AddComponent<LayoutElement>();
            cardLayout.flexibleWidth = 1f;
            cardLayout.flexibleHeight = 1f;
            cardLayout.minHeight = 456f;
        }

        void BuildPremiumBackdrop(RectTransform root, bool compact)
        {
            var backdrop = CreateUiRect("PremiumLoginBackdrop", root);
            ClassicRpgUiRuntime.Stretch(backdrop);
            backdrop.SetSiblingIndex(0);

            var shadow = backdrop.gameObject.AddComponent<Image>();
            ClassicRpgUiRuntime.ApplyPanel(shadow, ClassicRpgPanelSkin.Shadow, false, new Color(1f, 1f, 1f, 0.30f));

            CreateBackdropBar(backdrop, "TopBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, compact ? -36f : -46f), new Vector2(compact ? 760f : 1040f, compact ? 82f : 104f), new Color(0.46f, 0.34f, 0.17f, 0.58f));
            CreateBackdropBar(backdrop, "BottomBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, compact ? 34f : 48f), new Vector2(compact ? 760f : 1040f, compact ? 78f : 96f), new Color(0.20f, 0.16f, 0.11f, 0.44f));

            if (compact)
            {
                CreateBackdropFlag(backdrop, "TopFlag", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -138f), new Vector2(180f, 240f), new Color(1f, 1f, 1f, 0.16f), false);
            }
            else
            {
                CreateBackdropFlag(backdrop, "LeftFlag", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(130f, -140f), new Vector2(260f, 340f), new Color(1f, 1f, 1f, 0.20f), false);
                CreateBackdropFlag(backdrop, "RightFlag", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-130f, 150f), new Vector2(240f, 320f), new Color(1f, 1f, 1f, 0.15f), true);
            }
        }

        void BuildBrandColumn(RectTransform parent, bool compact)
        {
            var layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = compact ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 12f : 16f;
            layout.padding = compact ? new RectOffset(12, 12, 12, 12) : new RectOffset(28, 20, 54, 54);

            var crest = CreateUiImage("Crest", parent, ClassicRpgPanelSkin.FlagClassic, new Color(1f, 1f, 1f, 0.95f), false);
            var crestLayout = crest.gameObject.AddComponent<LayoutElement>();
            crestLayout.preferredWidth = compact ? 190f : 280f;
            crestLayout.preferredHeight = compact ? 86f : 114f;
            crest.rectTransform.pivot = compact ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
            crest.gameObject.AddComponent<UiAmbientMotion>();

            var titlePlate = CreateUiImage("TitlePlate", parent, ClassicRpgPanelSkin.TitleLong, Color.white, false);
            var titlePlateLayout = titlePlate.gameObject.AddComponent<LayoutElement>();
            titlePlateLayout.preferredWidth = compact ? 420f : 520f;
            titlePlateLayout.preferredHeight = compact ? 90f : 108f;

            var titleText = CreateUiText("BrandTitle", titlePlate.transform, "RANSOMFORGE\nCASTLE DEFENDER", compact ? 27f : 34f, ClassicRpgTextTone.Title, ClassicRpgUiRuntime.WarmGold);
            Stretch(titleText.rectTransform, new Vector2(30f, 18f), new Vector2(-30f, -20f));
            titleText.textWrappingMode = TextWrappingModes.Normal;
            titleText.lineSpacing = compact ? -10f : -18f;
            titleText.alignment = TextAlignmentOptions.Center;

            var subtitle = CreateUiText(
                "BrandSubtitle",
                parent,
                compact
                    ? "A clearer front door for the fortress, built for phones and framed like the RPG kit."
                    : "First impressions matter. Command the fortress with confidence from the very first screen.",
                compact ? 17f : 23f,
                ClassicRpgTextTone.Heading,
                ClassicRpgUiRuntime.BrightText);
            subtitle.alignment = compact ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            subtitle.textWrappingMode = TextWrappingModes.Normal;
            var subtitleLayout = subtitle.gameObject.AddComponent<LayoutElement>();
            subtitleLayout.preferredHeight = compact ? 58f : 86f;

            var bodyCard = CreateUiImage("BodyCard", parent, ClassicRpgPanelSkin.PaperMedium, new Color(0.16f, 0.14f, 0.10f, 0.95f), true);
            var bodyCardLayout = bodyCard.gameObject.AddComponent<LayoutElement>();
            bodyCardLayout.preferredHeight = compact ? 112f : 244f;
            var bodyCardGroup = bodyCard.gameObject.AddComponent<VerticalLayoutGroup>();
            bodyCardGroup.childAlignment = compact ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            bodyCardGroup.childControlWidth = true;
            bodyCardGroup.childControlHeight = false;
            bodyCardGroup.childForceExpandWidth = true;
            bodyCardGroup.childForceExpandHeight = false;
            bodyCardGroup.spacing = compact ? 4f : 8f;
            bodyCardGroup.padding = compact ? new RectOffset(18, 18, 16, 16) : new RectOffset(28, 28, 24, 24);

            var bodyHeader = CreateUiText("BodyHeader", bodyCard.transform, compact ? "WAR ROOM NOTES" : "WAR ROOM BRIEF", compact ? 17f : 20f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            bodyHeader.alignment = compact ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            bodyHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            AddBrandBullet(bodyCard.transform, "Premium presentation with classic fantasy craftsmanship.", compact);
            AddBrandBullet(bodyCard.transform, compact ? "Clear hierarchy and larger mobile targets." : "Clear hierarchy, larger targets, and stronger motion for confidence.", compact);
            if (!compact)
                AddBrandBullet(bodyCard.transform, "A front door that feels intentional instead of placeholder.", compact);
        }

        void AddBrandBullet(Transform parent, string copy, bool compact)
        {
            var row = CreateUiRect("Bullet", parent);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = compact ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = compact ? 8f : 10f;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = compact ? 28f : 42f;

            var icon = CreateUiImage("Icon", row, ClassicRpgPanelSkin.FlagClassic, new Color(1f, 1f, 1f, 0.8f), false);
            icon.rectTransform.sizeDelta = compact ? new Vector2(14f, 14f) : new Vector2(18f, 18f);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var text = CreateUiText("Copy", row, copy, compact ? 13f : 16f, ClassicRpgTextTone.Body, new Color(0.92f, 0.89f, 0.82f, 1f));
            text.alignment = compact ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.gameObject.AddComponent<LayoutElement>().preferredWidth = 0f;
        }

        RectTransform BuildLoginCardShell(RectTransform parent, bool compact)
        {
            var shell = CreateUiRect("LoginShell", parent);
            ClassicRpgUiRuntime.Stretch(shell);

            var shadow = CreateUiImage("Shadow", shell, ClassicRpgPanelSkin.Shadow, new Color(1f, 1f, 1f, 0.28f), false);
            Stretch(shadow.rectTransform, new Vector2(-18f, -22f), new Vector2(24f, 24f));

            var outerFrame = CreateUiImage("OuterFrame", shell, ClassicRpgPanelSkin.Frame, Color.white, true);
            Stretch(outerFrame.rectTransform);

            var innerPanel = CreateUiImage("InnerPanel", shell, ClassicRpgPanelSkin.PaperMedium, new Color(0.15f, 0.13f, 0.09f, 0.96f), true);
            Stretch(innerPanel.rectTransform, new Vector2(20f, 20f), new Vector2(-20f, -20f));

            var content = CreateUiRect("Content", innerPanel.transform);
            Stretch(content, compact ? new Vector2(24f, 24f) : new Vector2(34f, 32f), compact ? new Vector2(-24f, -26f) : new Vector2(-34f, -32f));
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = compact ? 12f : 14f;

            return content;
        }

        void PopulateLoginCard(RectTransform content, bool compact)
        {
            var title = FindDescendant(PanelLogin.transform, "Txt_Title") as RectTransform;
            var rowTabs = FindDescendant(PanelLogin.transform, "Row_Tabs") as RectTransform;

            var overline = CreateUiText("Overline", content, "ENTER THE WAR ROOM", 18f, ClassicRpgTextTone.Accent, ClassicRpgUiRuntime.SoftGold);
            overline.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            if (title != null)
            {
                title.SetParent(content, false);
                PrepareForLayout(title, 76f);
                var titleText = title.GetComponent<TMP_Text>();
                if (titleText != null)
                {
                    titleText.text = "Sign In";
                    titleText.fontSize = compact ? 28f : 34f;
                    titleText.fontStyle = FontStyles.Bold;
                    ClassicRpgUiRuntime.ApplyText(titleText, ClassicRpgTextTone.Title, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold);
                }
            }

            if (rowTabs != null)
            {
                rowTabs.SetParent(content, false);
                PrepareForLayout(rowTabs, 44f);
                var tabsLayout = rowTabs.GetComponent<HorizontalLayoutGroup>() ?? rowTabs.gameObject.AddComponent<HorizontalLayoutGroup>();
                tabsLayout.childAlignment = TextAnchor.MiddleCenter;
                tabsLayout.childControlWidth = true;
                tabsLayout.childControlHeight = true;
                tabsLayout.childForceExpandWidth = true;
                tabsLayout.childForceExpandHeight = true;
                tabsLayout.spacing = 12f;
                tabsLayout.padding = new RectOffset(0, 0, 0, 0);
            }

            StyleTabButton(Btn_TabSignIn, compact ? 0f : 210f);
            StyleTabButton(Btn_TabRegister, compact ? 0f : 210f);

            if (Input_Email != null) Input_Email.transform.SetParent(content, false);
            if (Input_DisplayName != null) Input_DisplayName.transform.SetParent(content, false);
            if (Input_Password != null) Input_Password.transform.SetParent(content, false);

            PrepareField(Input_Email, compact ? 60f : 64f, "Email address");
            PrepareField(Input_DisplayName, compact ? 60f : 64f, "Display name");
            PrepareField(Input_Password, compact ? 60f : 64f, "Password");

            if (Btn_Submit != null)
            {
                PrepareButton(Btn_Submit, compact ? 60f : 64f, 0f);
                Btn_Submit.transform.SetParent(content, false);
                ClassicRpgUiRuntime.ApplyButton(Btn_Submit, ClassicRpgButtonSkin.LongGold, TxtSubmitBtn);
            }

            if (Obj_Divider != null)
            {
                var dividerRect = Obj_Divider.transform as RectTransform;
                if (dividerRect != null)
                {
                    dividerRect.SetParent(content, false);
                    PrepareForLayout(dividerRect, 24f);
                }

                var dividerText = Obj_Divider.GetComponentInChildren<TMP_Text>(true);
                if (dividerText != null)
                    ClassicRpgUiRuntime.ApplyText(dividerText, ClassicRpgTextTone.Muted, TextAlignmentOptions.Center, ClassicRpgUiRuntime.MutedText);
            }

            if (Btn_Google != null)
            {
                PrepareButton(Btn_Google, compact ? 54f : 58f, 0f);
                Btn_Google.transform.SetParent(content, false);
                ClassicRpgUiRuntime.ApplyButton(Btn_Google, ClassicRpgButtonSkin.MediumGold);
                var googleText = Btn_Google.GetComponentInChildren<TMP_Text>(true);
                if (googleText != null)
                    googleText.text = "Continue With Google";
            }

            if (Btn_Browser != null)
            {
                PrepareButton(Btn_Browser, 44f, compact ? 0f : 220f);
                Btn_Browser.transform.SetParent(content, false);
                ClassicRpgUiRuntime.ApplyButton(Btn_Browser, ClassicRpgButtonSkin.MiniBrown);
            }

            if (Txt_Error != null)
            {
                Txt_Error.transform.SetParent(content, false);
                PrepareForLayout(Txt_Error.rectTransform, 42f);
                Txt_Error.textWrappingMode = TextWrappingModes.Normal;
                ClassicRpgUiRuntime.ApplyText(Txt_Error, ClassicRpgTextTone.Error, TextAlignmentOptions.Center, ClassicRpgUiRuntime.ErrorText);
            }

            if (Txt_Status != null)
            {
                Txt_Status.transform.SetParent(content, false);
                PrepareForLayout(Txt_Status.rectTransform, 58f);
                Txt_Status.textWrappingMode = TextWrappingModes.Normal;
                ClassicRpgUiRuntime.ApplyText(Txt_Status, ClassicRpgTextTone.Muted, TextAlignmentOptions.Center, ClassicRpgUiRuntime.MutedText);
            }
        }

        void StyleDevicePanel(RectTransform root, RectTransform canvasRect, bool compact)
        {
            if (Obj_DevicePanel == null)
                return;

            var panelRect = Obj_DevicePanel.transform as RectTransform;
            if (panelRect == null)
                return;

            panelRect.SetParent(root, false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = compact ? new Vector2(420f, 270f) : new Vector2(520f, 300f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.SetAsLastSibling();

            var panelImage = Obj_DevicePanel.GetComponent<Image>();
            if (panelImage != null)
            {
                panelImage.sprite = null;
                panelImage.type = Image.Type.Simple;
                panelImage.color = new Color(0.04f, 0.07f, 0.11f, 0.92f);
                panelImage.raycastTarget = true;
            }

            var outline = Obj_DevicePanel.GetComponent<Outline>() ?? Obj_DevicePanel.AddComponent<Outline>();
            outline.effectColor = new Color(0.36f, 0.49f, 0.65f, 0.34f);
            outline.effectDistance = new Vector2(1f, -1f);

            var shadow = Obj_DevicePanel.GetComponent<Shadow>() ?? Obj_DevicePanel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
            shadow.effectDistance = new Vector2(0f, -10f);

            if (Txt_DeviceCode != null)
            {
                Txt_DeviceCode.textWrappingMode = TextWrappingModes.NoWrap;
                ApplyModernTextStyle(Txt_DeviceCode, compact ? 28f : 34f, new Color(0.94f, 0.97f, 1f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
                Txt_DeviceCode.characterSpacing = 4f;
            }

            if (Txt_DeviceStatus != null)
            {
                Txt_DeviceStatus.textWrappingMode = TextWrappingModes.Normal;
                ApplyModernTextStyle(Txt_DeviceStatus, compact ? 14f : 15f, new Color(0.71f, 0.79f, 0.88f, 0.96f), FontStyles.Normal, TextAlignmentOptions.Center);
            }

            if (Btn_DeviceCancel != null)
            {
                PrepareButton(Btn_DeviceCancel, 46f, compact ? 0f : 200f);
                ApplyModernButtonStyle(
                    Btn_DeviceCancel,
                    new Color(0.09f, 0.13f, 0.19f, 0.92f),
                    new Color(0.12f, 0.17f, 0.24f, 0.98f),
                    new Color(0.07f, 0.10f, 0.15f, 1f),
                    new Color(0.88f, 0.92f, 0.98f, 0.98f));
            }
        }

        void RefreshTabPresentation()
        {
            StyleActiveTab(Btn_TabSignIn, !_isRegisterTab);
            StyleActiveTab(Btn_TabRegister, _isRegisterTab);
        }

        void StyleActiveTab(Button button, bool active)
        {
            if (button == null)
                return;

            var text = button.GetComponentInChildren<TMP_Text>(true);
            ApplyMinimalLinkButtonStyle(button, active ? Color.white : new Color(0.82f, 0.85f, 0.90f, 0.72f));

            if (text != null)
            {
                text.characterSpacing = 2.5f;
                text.fontStyle = FontStyles.Bold;
                text.color = active ? Color.white : new Color(0.82f, 0.85f, 0.90f, 0.72f);
            }
        }

        void StyleTabButton(Button button, float preferredWidth)
        {
            if (button == null)
                return;

            PrepareButton(button, 44f, preferredWidth);
            var text = button.GetComponentInChildren<TMP_Text>(true);
            ApplyMinimalLinkButtonStyle(button, new Color(0.82f, 0.85f, 0.90f, 0.72f));
            if (text != null)
                text.characterSpacing = 2.5f;
        }

        void PrepareField(TMP_InputField field, float preferredHeight, string placeholder, float preferredWidth = 0f)
        {
            if (field == null)
                return;

            var rect = field.transform as RectTransform;
            if (rect == null)
                return;

            PrepareForLayout(rect, preferredHeight);
            if (preferredWidth > 0f)
            {
                var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = preferredWidth;
                layoutElement.minWidth = preferredWidth;
                layoutElement.flexibleWidth = 0f;
            }
            ApplyModernInputFieldStyle(field, placeholder);
        }

        void CreateLabeledFieldRow(RectTransform parent, string label, TMP_InputField field, bool compact)
        {
            if (parent == null || field == null)
                return;

            float fieldWidth = compact ? 340f : 460f;
            float fieldHeight = compact ? 38f : 42f;
            float groupHeight = compact ? 62f : 70f;

            var row = CreateUiRect($"{field.name}_Row", parent);
            var rowLayoutElement = row.gameObject.AddComponent<LayoutElement>();
            rowLayoutElement.preferredWidth = fieldWidth;
            rowLayoutElement.minWidth = fieldWidth;
            rowLayoutElement.preferredHeight = groupHeight;
            rowLayoutElement.flexibleWidth = 0f;

            var rowLayout = row.gameObject.AddComponent<VerticalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.UpperCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = compact ? 6f : 8f;
            rowLayout.padding = new RectOffset(0, 0, 0, 0);

            var labelText = CreateModernText(
                $"{field.name}_Label",
                row,
                label,
                compact ? 13f : 15f,
                new Color(0.92f, 0.94f, 0.97f, 0.96f),
                FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft);
            labelText.characterSpacing = 0.8f;
            PrepareForLayout(labelText.rectTransform, compact ? 18f : 20f);
            var labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredHeight = compact ? 18f : 20f;
            labelLayout.flexibleWidth = 1f;

            var fieldRect = field.transform as RectTransform;
            if (fieldRect == null)
                return;

            fieldRect.SetParent(row, false);
            fieldRect.anchorMin = new Vector2(0f, 1f);
            fieldRect.anchorMax = new Vector2(1f, 1f);
            fieldRect.pivot = new Vector2(0.5f, 1f);
            fieldRect.anchoredPosition = Vector2.zero;
            fieldRect.sizeDelta = new Vector2(0f, fieldHeight);

            PrepareField(field, fieldHeight, string.Empty, fieldWidth);
        }

        void PrepareButton(Button button, float preferredHeight, float preferredWidth)
        {
            if (button == null)
                return;

            var rect = button.transform as RectTransform;
            if (rect == null)
                return;

            PrepareForLayout(rect, preferredHeight);
            var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = preferredWidth > 0f ? preferredWidth : -1f;
            layoutElement.minWidth = preferredWidth > 0f ? preferredWidth : 0f;
            layoutElement.flexibleWidth = 0f;
        }

        static void SetFieldContainerActive(TMP_InputField field, bool active)
        {
            if (field == null)
                return;

            Transform row = field.transform.parent;
            if (row != null && row.name.EndsWith("_Row", StringComparison.Ordinal))
            {
                row.gameObject.SetActive(active);
                return;
            }

            field.gameObject.SetActive(active);
        }

        static void PrepareForLayout(RectTransform rect, float preferredHeight)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, preferredHeight);
            var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleWidth = 1f;
        }

        IEnumerator AnimatePremiumPresentation(CanvasGroup brandGroup, RectTransform brandColumn, CanvasGroup cardGroup, RectTransform cardColumn)
        {
            if (brandGroup != null)
            {
                brandGroup.alpha = 0f;
                brandColumn.localScale = Vector3.one * 0.965f;
            }

            if (cardGroup != null)
            {
                cardGroup.alpha = 0f;
                cardColumn.localScale = Vector3.one * 0.96f;
            }

            float elapsed = 0f;
            const float duration = 0.55f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);

                if (brandGroup != null)
                {
                    brandGroup.alpha = eased;
                    brandColumn.localScale = Vector3.Lerp(Vector3.one * 0.965f, Vector3.one, eased);
                }

                if (cardGroup != null)
                {
                    float delayed = Mathf.Clamp01((elapsed - 0.08f) / duration);
                    float delayedEase = 1f - Mathf.Pow(1f - delayed, 3f);
                    cardGroup.alpha = delayedEase;
                    cardColumn.localScale = Vector3.Lerp(Vector3.one * 0.96f, Vector3.one, delayedEase);
                }

                yield return null;
            }
        }

        void CreateBackdropBar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var image = CreateUiImage(name, parent, ClassicRpgPanelSkin.MainMenuBar, color, false);
            image.rectTransform.anchorMin = anchorMin;
            image.rectTransform.anchorMax = anchorMax;
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = anchoredPosition;
            image.rectTransform.sizeDelta = size;
            image.gameObject.AddComponent<UiAmbientMotion>();
        }

        void CreateBackdropFlag(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color, bool mirror)
        {
            var image = CreateUiImage(name, parent, ClassicRpgPanelSkin.FlagClassic, color, false);
            image.rectTransform.anchorMin = anchorMin;
            image.rectTransform.anchorMax = anchorMax;
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = anchoredPosition;
            image.rectTransform.sizeDelta = size;
            if (mirror)
                image.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            image.gameObject.AddComponent<UiAmbientMotion>();
        }

        static void DestroyGeneratedChild(RectTransform parent, string childName)
        {
            if (parent == null)
                return;

            var child = parent.Find(childName);
            if (child != null)
                Destroy(child.gameObject);
        }

        static void DisableLegacyCard(RectTransform root)
        {
            if (root == null)
                return;

            var card = root.Find("Card");
            if (card == null)
                return;

            var cardImage = card.GetComponent<Image>();
            if (cardImage != null)
                cardImage.raycastTarget = false;

            card.gameObject.SetActive(false);
        }

        static RectTransform CreateUiRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Image CreateUiImage(string name, Transform parent, ClassicRpgPanelSkin skin, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            ClassicRpgUiRuntime.ApplyPanel(image, skin, sliced, color);
            return image;
        }

        static TMP_Text CreateUiText(string name, Transform parent, string value, float fontSize, ClassicRpgTextTone tone, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.textWrappingMode = TextWrappingModes.Normal;
            ClassicRpgUiRuntime.ApplyText(text, tone, TextAlignmentOptions.Center, color);
            return text;
        }

        static Image CreatePlainImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void ConfigureOverlayTextRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        void ResetIntroOverlayVisualState()
        {
            if (_introTitleGroup != null)
                _introTitleGroup.alpha = 0f;

            if (_introTitleCanvasGroup != null)
                _introTitleCanvasGroup.alpha = 0f;

            if (_introTaglineCanvasGroup != null)
                _introTaglineCanvasGroup.alpha = 0f;

            if (_introTitleText != null)
            {
                _introTitleText.rectTransform.anchoredPosition = _introTitleBasePosition;
                _introTitleText.rectTransform.localScale = Vector3.one;
            }

            if (_introTaglineText != null)
            {
                _introTaglineText.rectTransform.anchoredPosition = _introTaglineBasePosition;
                _introTaglineText.rectTransform.localScale = Vector3.one;
            }

            if (_introStampFlash != null)
                _introStampFlash.color = new Color(1f, 0.66f, 0.24f, 0f);
        }

        void ShowPersistentBrandImmediate()
        {
            if (_introTitleGroup == null)
                return;

            if (_introTitleText != null)
            {
                _introTitleText.rectTransform.anchoredPosition = _introTitleBasePosition;
                _introTitleText.rectTransform.localScale = Vector3.one;
            }

            if (_introTaglineText != null)
            {
                _introTaglineText.rectTransform.anchoredPosition = _introTaglineBasePosition;
                _introTaglineText.rectTransform.localScale = Vector3.one;
            }

            _introTitleGroup.alpha = 1f;
            if (_introTitleCanvasGroup != null)
                _introTitleCanvasGroup.alpha = 1f;
            if (_introTaglineCanvasGroup != null)
                _introTaglineCanvasGroup.alpha = 1f;
            if (_introStampFlash != null)
                _introStampFlash.color = new Color(1f, 0.66f, 0.24f, 0f);
        }

        static void ApplyIntroTitleTreatment(TMP_Text text)
        {
            if (text == null)
                return;

            ClassicRpgUiRuntime.ApplyTextStyle(
                text,
                ClassicRpgTextStyle.Title,
                TextAlignmentOptions.Center,
                new Color(0.96f, 0.94f, 0.90f, 1f),
                allowWrap: false);
            text.characterSpacing = 1.1f;
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(
                new Color(1.00f, 0.98f, 0.92f, 1f),
                new Color(0.96f, 0.94f, 0.88f, 1f),
                new Color(0.56f, 0.60f, 0.68f, 1f),
                new Color(0.34f, 0.37f, 0.44f, 1f));
            text.outlineColor = new Color(0.06f, 0.05f, 0.04f, 0.98f);
            text.outlineWidth = 0.24f;

            var shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.92f, 0.46f, 0.12f, 0.26f);
            shadow.effectDistance = new Vector2(0f, -6f);
        }

        static void ApplyIntroTaglineTreatment(TMP_Text text)
        {
            if (text == null)
                return;

            text.characterSpacing = 4.2f;
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(
                new Color(0.94f, 0.96f, 0.99f, 1f),
                new Color(0.94f, 0.96f, 0.99f, 1f),
                new Color(0.70f, 0.76f, 0.84f, 1f),
                new Color(0.70f, 0.76f, 0.84f, 1f));

            var shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.30f);
            shadow.effectDistance = new Vector2(0f, -4f);
        }

        static TMP_Text CreateModernText(string name, Transform parent, string value, float fontSize, Color color, FontStyles fontStyle, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.raycastTarget = false;
            ApplyModernTextStyle(text, fontSize, color, fontStyle, alignment);
            return text;
        }

        static Button CreateModernButton(string name, Transform parent, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = new Color(0.08f, 0.12f, 0.17f, 0.88f);
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var labelText = CreateModernText(
                "Label",
                go.transform,
                label,
                13f,
                new Color(0.90f, 0.94f, 0.99f, 0.98f),
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform, new Vector2(12f, 8f), new Vector2(-12f, -8f));

            return button;
        }

        static void ApplyModernTextStyle(TMP_Text text, float fontSize, Color color, FontStyles fontStyle, TextAlignmentOptions alignment)
        {
            if (text == null)
                return;

            if (text.font == null && TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;

            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
        }

        static void ApplyModernButtonStyle(Button button, Color baseColor, Color hoverColor, Color pressedColor, Color textColor, TMP_Text labelOverride = null)
        {
            if (button == null)
                return;

            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = baseColor;
            image.raycastTarget = true;
            ClearLegacyGraphics(button.transform, image);

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.92f, 0.94f, 0.98f, 0.14f);
            outline.effectDistance = new Vector2(1f, -1f);

            var shadow = button.GetComponent<Shadow>() ?? button.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.16f);
            shadow.effectDistance = new Vector2(0f, -1f);

            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = hoverColor;
            colors.selectedColor = hoverColor;
            colors.pressedColor = pressedColor;
            colors.disabledColor = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, Mathf.Clamp01(baseColor.a * 0.6f));
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = labelOverride ?? button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                var fontSize = Mathf.Max(label.fontSize, 15f);
                ApplyModernTextStyle(label, fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
                label.characterSpacing = 0.7f;
            }
        }

        static void ApplyMinimalLinkButtonStyle(Button button, Color textColor)
        {
            if (button == null)
                return;

            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            ClearLegacyGraphics(button.transform, image);

            var outline = button.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = Color.clear;
                outline.effectDistance = Vector2.zero;
            }

            var shadow = button.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.effectColor = Color.clear;
                shadow.effectDistance = Vector2.zero;
            }

            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.normalColor = new Color(0f, 0f, 0f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.06f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.12f);
            colors.disabledColor = new Color(0f, 0f, 0f, 0f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                ApplyModernTextStyle(label, 14f, textColor, FontStyles.Normal, TextAlignmentOptions.Center);
                label.characterSpacing = 0.4f;
            }
        }

        static void ApplyModernInputFieldStyle(TMP_InputField field, string placeholder)
        {
            if (field == null)
                return;

            var background = field.GetComponent<Image>() ?? field.gameObject.AddComponent<Image>();
            background.sprite = null;
            background.type = Image.Type.Simple;
            background.color = new Color(0.02f, 0.03f, 0.04f, 0.42f);
            background.raycastTarget = true;
            field.targetGraphic = background;

            var outline = field.GetComponent<Outline>() ?? field.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.94f, 0.96f, 0.98f, 0.14f);
            outline.effectDistance = new Vector2(1f, -1f);

            var shadow = field.GetComponent<Shadow>() ?? field.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.10f);
            shadow.effectDistance = new Vector2(0f, -1f);

            field.customCaretColor = true;
            field.caretColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            field.selectionColor = new Color(1f, 1f, 1f, 0.18f);

            if (field.textComponent != null)
            {
                ApplyModernTextStyle(field.textComponent, 13f, new Color(0.98f, 0.98f, 0.98f, 1f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                field.textComponent.margin = new Vector4(14f, 0f, 14f, 2f);
            }

            if (field.placeholder is TMP_Text placeholderText)
            {
                placeholderText.text = placeholder;
                ApplyModernTextStyle(placeholderText, 13f, new Color(0.86f, 0.89f, 0.93f, 0.84f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                placeholderText.margin = new Vector4(14f, 0f, 14f, 2f);
            }

            var underline = field.transform.Find("Underline") as RectTransform;
            if (underline == null)
            {
                var underlineImage = CreatePlainImage("Underline", field.transform, new Color(1f, 1f, 1f, 0.16f));
                underline = underlineImage.rectTransform;
            }

            var underlineGraphic = underline.GetComponent<Graphic>();
            ClearLegacyGraphics(field.transform, background, underlineGraphic);

            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(1f, 0f);
            underline.pivot = new Vector2(0.5f, 0f);
            underline.anchoredPosition = Vector2.zero;
            underline.sizeDelta = new Vector2(-6f, 1f);
        }

        static void ClearLegacyGraphics(Transform root, params Graphic[] keepGraphics)
        {
            if (root == null)
                return;

            var keep = new HashSet<Graphic>(keepGraphics);
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || keep.Contains(graphic) || graphic is TMP_Text)
                    continue;

                if (graphic is Image image)
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                }

                graphic.color = Color.clear;
                graphic.raycastTarget = false;
            }
        }

        static Transform FindDescendant(Transform parent, string name)
        {
            if (parent == null)
                return null;

            if (parent.name == name)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindDescendant(parent.GetChild(i), name);
                if (result != null)
                    return result;
            }

            return null;
        }

        static void Stretch(RectTransform rect, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        // ── Submit ────────────────────────────────────────────────────────────

        void OnSubmit()
        {
            if (_busy) return;
            SetError("");

            string email    = Input_Email?.text.Trim() ?? "";
            string password = Input_Password?.text ?? "";

            if (string.IsNullOrEmpty(email))    { SetError("Email is required."); return; }
            if (string.IsNullOrEmpty(password)) { SetError("Password is required."); return; }

            if (_isRegisterTab)
            {
                string displayName = Input_DisplayName?.text.Trim() ?? "";
                if (string.IsNullOrEmpty(displayName)) { SetError("Display name is required."); return; }
                StartCoroutine(DoRegister(email, displayName, password));
            }
            else
            {
                StartCoroutine(DoLogin(email, password));
            }
        }

        // ── Password sign-in ──────────────────────────────────────────────────

        IEnumerator DoLogin(string email, string password)
        {
            _busy = true;
            SetStatus("Signing in...");
            SetBusy(true);

            string url  = ResolvedBaseUrl + "/auth/login";
            using var req = CreateJsonPostRequest(url, new LoginRequest
            {
                email = email,
                password = password,
            });
            req.timeout = 15;
            yield return req.SendWebRequest();

            _busy = false;
            SetBusy(false);

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = ParseErrorBody(req.downloadHandler.text, req.error);
                SetError(err);
                SetStatus("");
                yield break;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<AuthResponse>(req.downloadHandler.text);
                if (resp?.requiresMfa == true)
                {
                    SetError("Multi-factor authentication is not supported in the app. Use the web client.");
                    SetStatus("");
                    yield break;
                }
                if (string.IsNullOrEmpty(resp?.accessToken))
                {
                    SetError("Login failed — no token received.");
                    yield break;
                }
                OnLoginSuccess(resp.accessToken);
            }
            catch (Exception ex)
            {
                SetError($"Unexpected response: {ex.Message}");
            }
        }

        // ── Registration ──────────────────────────────────────────────────────

        IEnumerator DoRegister(string email, string displayName, string password)
        {
            _busy = true;
            SetStatus("Creating account...");
            SetBusy(true);

            string url  = ResolvedBaseUrl + "/auth/register";
            using var req = CreateJsonPostRequest(url, new RegisterRequest
            {
                email = email,
                displayName = displayName,
                password = password,
            });
            req.timeout = 15;
            yield return req.SendWebRequest();

            _busy = false;
            SetBusy(false);

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = ParseErrorBody(req.downloadHandler.text, req.error);
                SetError(err);
                SetStatus("");
                yield break;
            }

            // Registration sends a verification email — switch to sign-in tab
            SetError("");
            SetStatus("Account created! Check your email for a verification link, then sign in.");
            SetTab(false);
        }

        // ── Google SSO ────────────────────────────────────────────────────────

        void ConfigureAuxiliaryAction()
        {
            if (Btn_Browser == null) return;
            var txt = Btn_Browser.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = "Forgot Password?";
            Btn_Browser.gameObject.SetActive(false);
        }

        void ShowContentRetryAction(bool show)
        {
            _showingPreloadRetry = show;
            if (Btn_Browser == null) return;

            var txt = Btn_Browser.GetComponentInChildren<TMP_Text>();
            if (txt != null)
                txt.text = show ? "Retry Content Download" : "Forgot Password?";

            bool shouldShow = show || (_passwordEnabled && !_isRegisterTab);
            Btn_Browser.gameObject.SetActive(shouldShow);
            Btn_Browser.interactable = !_busy;
        }

        void RefreshAuxiliaryAction()
        {
            ShowContentRetryAction(_showingPreloadRetry);
        }

        void OnAuxiliaryAction()
        {
            if (_showingPreloadRetry)
            {
                RetryCriticalContentPreparation();
                return;
            }

            OnForgotPassword();
        }

        void RetryCriticalContentPreparation()
        {
            if (_busy || !AuthManager.IsAuthenticated || _retryingCriticalContent)
                return;

            SetError("");
            SetStatus("Retrying required content download...");
            StartCoroutine(CompleteLoginAndEnterLobby(0f, true));
        }

        void OnForgotPassword()
        {
            if (_busy) return;

            string email = Input_Email?.text.Trim() ?? "";
            if (string.IsNullOrEmpty(email))
            {
                SetError("Enter your email first.");
                SetStatus("");
                return;
            }

            StartCoroutine(DoForgotPassword(email));
        }

        IEnumerator DoForgotPassword(string email)
        {
            _busy = true;
            SetStatus("Sending reset email...");
            SetBusy(true);
            SetError("");

            string url  = ResolvedBaseUrl + "/auth/forgot-password";
            using var req = CreateJsonPostRequest(url, new ForgotPasswordRequest
            {
                email = email,
            });
            req.timeout = 15;
            yield return req.SendWebRequest();

            _busy = false;
            SetBusy(false);

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = ParseErrorBody(req.downloadHandler.text, req.error);
                SetError(err);
                SetStatus("");
                yield break;
            }

            SetError("");
            SetStatus("If that email exists, a reset link has been sent.");
        }

        void OnGoogleSignIn()
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_googleClientId)) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            _busy = true;
            SetBusy(true);
            SetError("");
            SetStatus("Opening Google sign-in...");
            JSIO_GoogleSignIn(_googleClientId, gameObject.name);
#else
            SetStatus("Google sign-in is only available in the browser build.");
#endif
        }

        // Called by SocketIOBridge.jslib via SendMessage when Google returns a credential
        public void OnGoogleCredential(string credential)
        {
            if (string.IsNullOrEmpty(credential))
            {
                _busy = false;
                SetBusy(false);
                SetError("Google sign-in was cancelled or not available.");
                SetStatus("");
                return;
            }
            StartCoroutine(DoGoogleAuth(credential));
        }

        IEnumerator DoGoogleAuth(string idToken)
        {
            _busy = true;
            SetStatus("Verifying with Google...");
            SetBusy(true);

            string url  = ResolvedBaseUrl + "/auth/google";
            using var req = CreateJsonPostRequest(url, new GoogleAuthRequest
            {
                idToken = idToken,
            });
            req.timeout = 15;
            yield return req.SendWebRequest();

            _busy = false;
            SetBusy(false);

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = ParseErrorBody(req.downloadHandler.text, req.error);
                SetError(err);
                SetStatus("");
                yield break;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<AuthResponse>(req.downloadHandler.text);
                if (string.IsNullOrEmpty(resp?.accessToken))
                {
                    SetError("Google login failed — no token received.");
                    yield break;
                }
                OnLoginSuccess(resp.accessToken);
            }
            catch (Exception ex)
            {
                SetError($"Unexpected response: {ex.Message}");
            }
        }

        // ── Post-login ────────────────────────────────────────────────────────

        UnityWebRequest CreateJsonPostRequest(string url, object payload)
        {
            string body = JsonConvert.SerializeObject(payload);
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        void OnLoginSuccess(string accessToken)
        {
            AuthManager.SaveToken(accessToken);
            NetworkManager.Instance?.ReconnectForCurrentAuth("login success");
            Debug.Log($"[Login] Signed in as {AuthManager.DisplayName}");
            SetStatus($"Welcome, {AuthManager.DisplayName}!");
            ShowContentRetryAction(false);
            StartCoroutine(CompleteLoginAndEnterLobby(0.6f));
        }

        IEnumerator CompleteLoginAndEnterLobby(float delay, bool forceRefreshManifest = false)
        {
            if (_completingLogin) yield break;
            _completingLogin = true;
            _retryingCriticalContent = forceRefreshManifest;
            _busy = true;
            SetBusy(true);
            ShowContentRetryAction(false);
            SetError("");

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!LoadingScreen.IsTransitionInProgress
                    && string.Equals(SceneManager.GetActiveScene().name, "Login", StringComparison.Ordinal))
                {
                    break;
                }

                yield return null;
            }

            ReleaseLoginAudioListener();
            EnsurePersistentEventSystem();
            ShowContentRetryAction(false);
            LoadingScreen.LoadSceneWithCriticalContentPreload("Lobby");
        }

        void EnsureLoginAudioListener()
        {
            if (_runtimeAudioListener != null)
                return;

            if (FindFirstObjectByType<AudioListener>(FindObjectsInactive.Include) != null)
                return;

            _runtimeAudioListener = new GameObject("LoginAudioListener");
            _runtimeAudioListener.AddComponent<AudioListener>();
            Debug.Log("[LoginUI] Created temporary AudioListener for the login flow.");
        }

        void ReleaseLoginAudioListener()
        {
            if (_runtimeAudioListener == null)
                return;

            Destroy(_runtimeAudioListener);
            _runtimeAudioListener = null;
        }

        static void EnsurePersistentEventSystem()
        {
            var loginUi = FindFirstObjectByType<LoginUI>(FindObjectsInactive.Include);
            SceneEventSystemUtility.EnsureSceneLocal(loginUi, "LoginEventSystem", "LoginUI");
        }

        // ── Browser / Device-code flow (Editor + Standalone) ─────────────────

        void OnBrowserSignIn()
        {
            if (_busy) return;
            StartCoroutine(DoDeviceCodeFlow());
        }

        IEnumerator DoDeviceCodeFlow()
        {
            _busy = true;
            SetBusy(true);
            SetError("");
            SetStatus("Requesting code...");

            // Step 1 — get a device code from the server
            string url  = ResolvedBaseUrl + "/auth/device";
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler   = new UploadHandlerRaw(new byte[0]);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetError("Could not reach server.");
                SetStatus("");
                SetBusy(false);
                _busy = false;
                yield break;
            }

            DeviceCodeResponse dcr;
            try   { dcr = JsonConvert.DeserializeObject<DeviceCodeResponse>(req.downloadHandler.text); }
            catch { SetError("Unexpected server response."); SetBusy(false); _busy = false; yield break; }

            _deviceCode = dcr.deviceCode;
            _polling    = true;

            // Step 2 — show the code panel and open the browser
            if (Obj_DevicePanel != null) Obj_DevicePanel.SetActive(true);
            if (Txt_DeviceCode  != null) Txt_DeviceCode.text = dcr.userCode;
            if (Txt_DeviceStatus!= null) Txt_DeviceStatus.text = "Waiting for browser authorization...";

            string authorizeUrl = ResolvedBaseUrl + "/?authorize=" + dcr.userCode;
            Application.OpenURL(authorizeUrl);
            SetStatus("");
            SetBusy(false);     // re-enable form while waiting

            // Step 3 — poll until authorized, expired, or cancelled
            float elapsed    = 0f;
            float expiresSec = dcr.expiresIn > 0 ? dcr.expiresIn : 600f;

            while (_polling && elapsed < expiresSec)
            {
                yield return new WaitForSeconds(2.5f);
                elapsed += 2.5f;

                if (!_polling) yield break;

                var pollReq = UnityWebRequest.Get(ResolvedBaseUrl + "/auth/device/poll?code=" + _deviceCode);
                pollReq.timeout = 8;
                yield return pollReq.SendWebRequest();

                if (!_polling) yield break;

                if (pollReq.result != UnityWebRequest.Result.Success) continue;

                DevicePollResponse poll;
                try   { poll = JsonConvert.DeserializeObject<DevicePollResponse>(pollReq.downloadHandler.text); }
                catch { continue; }

                if (poll.status == "authorized" && !string.IsNullOrEmpty(poll.accessToken))
                {
                    _polling = false;
                    _busy    = false;
                    if (Obj_DevicePanel != null) Obj_DevicePanel.SetActive(false);
                    OnLoginSuccess(poll.accessToken);
                    yield break;
                }
                if (poll.status == "expired")
                {
                    _polling = false;
                    _busy    = false;
                    if (Obj_DevicePanel != null) Obj_DevicePanel.SetActive(false);
                    SetError("Code expired. Try again.");
                    yield break;
                }
                // status == "pending" — keep waiting
                if (Txt_DeviceStatus != null)
                    Txt_DeviceStatus.text = $"Waiting... ({Mathf.CeilToInt(expiresSec - elapsed)}s)";
            }

            // Timed out
            _polling = false;
            _busy    = false;
            if (Obj_DevicePanel != null) Obj_DevicePanel.SetActive(false);
            SetError("Authorization timed out. Try again.");
        }

        void CancelDeviceFlow()
        {
            _polling = false;
            _busy    = false;
            if (Obj_DevicePanel != null) Obj_DevicePanel.SetActive(false);
            SetError("");
            SetStatus("");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void SetError(string msg)
        {
            if (Txt_Error != null)
            {
                Txt_Error.text = msg;
                Txt_Error.gameObject.SetActive(!string.IsNullOrEmpty(msg));
            }
        }

        void SetStatus(string msg)
        {
            if (Txt_Status != null) Txt_Status.text = msg;
        }

        void SetBusy(bool busy)
        {
            if (Btn_Submit != null) Btn_Submit.interactable = !busy;
            if (Btn_Google != null) Btn_Google.interactable = !busy;
            if (Btn_Browser != null) Btn_Browser.interactable = !busy;
        }

        static string BuildCriticalContentFailureTitle(RemoteContentManager remoteContent)
        {
            return remoteContent.LastFailureStage switch
            {
                RemoteContentManager.CriticalPreloadFailureStage.ManifestDownload => "Couldn't download required content metadata.",
                RemoteContentManager.CriticalPreloadFailureStage.ManifestParse => "Required content metadata was invalid.",
                RemoteContentManager.CriticalPreloadFailureStage.ManifestValidation => "Required content metadata is incomplete.",
                RemoteContentManager.CriticalPreloadFailureStage.AddressablesInitialization => "The remote content system failed to initialize.",
                RemoteContentManager.CriticalPreloadFailureStage.DownloadSizing => "Couldn't estimate required content download size.",
                RemoteContentManager.CriticalPreloadFailureStage.ContentDownload => "Couldn't download required gameplay content.",
                RemoteContentManager.CriticalPreloadFailureStage.AssetLoad => "Required gameplay prefabs failed to load.",
                _ => "Unable to prepare required gameplay content before entering the lobby.",
            };
        }

        static string BuildCriticalContentFailureStatus(RemoteContentManager remoteContent)
        {
            string detail = string.IsNullOrWhiteSpace(remoteContent.LastError)
                ? "Retry the content download or check your connection."
                : remoteContent.LastError;

            return remoteContent.LastFailureStage == RemoteContentManager.CriticalPreloadFailureStage.ManifestDownload && remoteContent.HasManifest
                ? $"Live manifest download failed, but a cached manifest is available. {detail}"
                : detail;
        }

        static string ParseErrorBody(string body, string fallback)
        {
            if (string.IsNullOrEmpty(body)) return fallback;
            try
            {
                var obj = JsonConvert.DeserializeObject<ErrorBody>(body);
                return !string.IsNullOrEmpty(obj?.error) ? obj.error : fallback;
            }
            catch { return fallback; }
        }

        string ResolvedBaseUrl
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var page = new Uri(Application.absoluteURL);
                bool standard = (page.Scheme == "https" && page.Port == 443)
                             || (page.Scheme == "http"  && page.Port == 80)
                             || page.Port < 0;
                return standard
                    ? $"{page.Scheme}://{page.Host}"
                    : $"{page.Scheme}://{page.Host}:{page.Port}";
#else
                return NetworkManager.Instance != null
                    ? NetworkManager.Instance.ResolvedServerUrl
                    : "http://127.0.0.1:3000";
#endif
            }
        }

        // ── JSON models ───────────────────────────────────────────────────────

        [Serializable] class ConfigResponse
        {
            public bool   passwordAuthEnabled;
            public string googleClientId;
        }

        [Serializable] class AuthResponse
        {
            public string accessToken;
            public string refreshToken;
            public bool   requiresMfa;
        }

        [Serializable] class ErrorBody
        {
            public string error;
        }

        [Serializable] class DeviceCodeResponse
        {
            public string deviceCode;
            public string userCode;
            public int    expiresIn;
        }

        [Serializable] class DevicePollResponse
        {
            public string status;        // "pending" | "authorized" | "expired"
            public string accessToken;
        }
    }
}
