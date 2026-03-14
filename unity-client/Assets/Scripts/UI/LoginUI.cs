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
using UnityEngine.Scripting;
using TMPro;
using Newtonsoft.Json;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class LoginUI : MonoBehaviour
    {
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

        // ── WebGL jslib imports ───────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void JSIO_GoogleSignIn(string clientId, string gameObjectName);
#endif

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            // Already signed in — skip straight to lobby
            if (AuthManager.IsAuthenticated)
            {
                LoadingScreen.LoadScene("Lobby");
                return;
            }

            // Hide Google button until config says it is available
            if (Btn_Google  != null) Btn_Google.gameObject.SetActive(false);
            if (Obj_Divider != null) Obj_Divider.SetActive(false);

            // Wire buttons
            if (Btn_TabSignIn)   Btn_TabSignIn.onClick.AddListener(()  => SetTab(false));
            if (Btn_TabRegister) Btn_TabRegister.onClick.AddListener(() => SetTab(true));
            if (Btn_Submit)      Btn_Submit.onClick.AddListener(OnSubmit);
            if (Btn_Google)      Btn_Google.onClick.AddListener(OnGoogleSignIn);
            if (Btn_Browser)     Btn_Browser.onClick.AddListener(OnForgotPassword);
            if (Btn_DeviceCancel)Btn_DeviceCancel.onClick.AddListener(CancelDeviceFlow);
            if (Obj_DevicePanel) Obj_DevicePanel.SetActive(false);
            ConfigureAuxiliaryAction();

            SetTab(false);
            SetError("");
            SetStatus("");

            StartCoroutine(FetchConfig());
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
                    if (Btn_Browser     != null) Btn_Browser.gameObject.SetActive(false);
                    SetStatus("Use Google to sign in.");
                }
                else if (Btn_Browser != null)
                {
                    Btn_Browser.gameObject.SetActive(true);
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

            // Highlight active tab
            if (Btn_TabSignIn != null)
            {
                var txt = Btn_TabSignIn.GetComponentInChildren<TMP_Text>();
                if (txt != null) txt.color = register ? ColorTabInactive : ColorTabActive;
            }
            if (Btn_TabRegister != null)
            {
                var txt = Btn_TabRegister.GetComponentInChildren<TMP_Text>();
                if (txt != null) txt.color = register ? ColorTabActive : ColorTabInactive;
            }

            if (Btn_Browser != null)
                Btn_Browser.gameObject.SetActive(_passwordEnabled && !_isRegisterTab);

            SetError("");
            SetStatus("");
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
            // Small delay so the player sees the welcome message
            StartCoroutine(DelayedSceneLoad("Lobby", 0.6f));
        }

        IEnumerator DelayedSceneLoad(string sceneName, float delay)
        {
            yield return new WaitForSeconds(delay);
            LoadingScreen.LoadScene(sceneName);
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
