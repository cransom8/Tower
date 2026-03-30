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
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
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
        public Color ColorTabActive   = new Color(0.9f, 0.75f, 0.2f);
        public Color ColorTabInactive = new Color(0.5f, 0.5f, 0.5f);

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
                SetStatus("Preparing your game session...");
                StartCoroutine(CompleteLoginAndEnterLobby(0f));
            }
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
                    if (Input_Email     != null) Input_Email.gameObject.SetActive(false);
                    if (Input_Password  != null) Input_Password.gameObject.SetActive(false);
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
                Input_DisplayName.gameObject.SetActive(register);

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
                rootImage.color = ClassicRpgUiRuntime.BackdropColor;
                ClassicRpgUiRuntime.ApplyPanel(rootImage, ClassicRpgPanelSkin.DarkSpell, false, new Color(1f, 1f, 1f, 0.24f));
            }

            DestroyGeneratedChild(root, "PremiumLoginBackdrop");
            DestroyGeneratedChild(root, "PremiumLoginSafeArea");
            DestroyGeneratedChild(root, "PremiumLoginStage");
            DisableLegacyCard(root);

            BuildPremiumBackdrop(root, compact);

            var safeArea = CreateUiRect("PremiumLoginSafeArea", root);
            ClassicRpgUiRuntime.ApplySafeArea(
                safeArea,
                canvasRect,
                compact ? 20f : 48f,
                compact ? 18f : 38f,
                compact ? 18f : 34f);

            var stage = CreateUiRect("PremiumLoginStage", safeArea);
            ClassicRpgUiRuntime.Stretch(stage);

            RectTransform brandColumn;
            RectTransform cardColumn;
            if (compact)
                BuildCompactLoginStage(stage, out brandColumn, out cardColumn);
            else
                BuildWideLoginStage(stage, out brandColumn, out cardColumn);

            var brandGroup = brandColumn.gameObject.AddComponent<CanvasGroup>();
            var cardGroup = cardColumn.gameObject.AddComponent<CanvasGroup>();
            BuildBrandColumn(brandColumn, compact);
            var cardContent = BuildLoginCardShell(cardColumn, compact);
            PopulateLoginCard(cardContent, compact);
            StyleDevicePanel(root, canvasRect, compact);

            StartCoroutine(AnimatePremiumPresentation(brandGroup, brandColumn, cardGroup, cardColumn));
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
            panelRect.sizeDelta = compact ? new Vector2(460f, 320f) : new Vector2(560f, 340f);
            panelRect.anchoredPosition = Vector2.zero;

            var panelImage = Obj_DevicePanel.GetComponent<Image>();
            if (panelImage != null)
            {
                ClassicRpgUiRuntime.ApplyPanel(panelImage, ClassicRpgPanelSkin.Frame, true, Color.white);
            }

            if (Txt_DeviceCode != null)
                ClassicRpgUiRuntime.ApplyText(Txt_DeviceCode, ClassicRpgTextTone.Title, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold);

            if (Txt_DeviceStatus != null)
            {
                Txt_DeviceStatus.textWrappingMode = TextWrappingModes.Normal;
                ClassicRpgUiRuntime.ApplyText(Txt_DeviceStatus, ClassicRpgTextTone.Body, TextAlignmentOptions.Center, ClassicRpgUiRuntime.BrightText);
            }

            if (Btn_DeviceCancel != null)
            {
                PrepareButton(Btn_DeviceCancel, 46f, compact ? 0f : 200f);
                ClassicRpgUiRuntime.ApplyButton(Btn_DeviceCancel, ClassicRpgButtonSkin.MiniBrown);
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
            ClassicRpgUiRuntime.ApplyButton(button, active ? ClassicRpgButtonSkin.MiniGold : ClassicRpgButtonSkin.MiniBrown, text);
            if (text != null)
                text.color = active ? ColorTabActive : ClassicRpgUiRuntime.BrightText;
        }

        void StyleTabButton(Button button, float preferredWidth)
        {
            if (button == null)
                return;

            PrepareButton(button, 44f, preferredWidth);
            ClassicRpgUiRuntime.ApplyButton(button, ClassicRpgButtonSkin.MiniBrown);
        }

        void PrepareField(TMP_InputField field, float preferredHeight, string placeholder)
        {
            if (field == null)
                return;

            var rect = field.transform as RectTransform;
            if (rect == null)
                return;

            PrepareForLayout(rect, preferredHeight);
            ClassicRpgUiRuntime.StyleInputField(field, placeholder);
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
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;
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
            var current = SceneEventSystemUtility.FindBest(loginUi);

            if (current == null)
            {
                var go = new GameObject("LoginEventSystem");
                current = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                Debug.Log("[LoginUI] Created fallback EventSystem for login UI.");
            }

            if (!current.gameObject.activeSelf)
                current.gameObject.SetActive(true);

            if (current.GetComponent<BaseInputModule>() == null)
                current.gameObject.AddComponent<StandaloneInputModule>();

            if (current.GetComponent<SingleEventSystem>() == null)
                current.gameObject.AddComponent<SingleEventSystem>();
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
