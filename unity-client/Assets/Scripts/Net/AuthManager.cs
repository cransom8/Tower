// AuthManager.cs - access token storage, session restore, and logout.
// Singleton - persists across scenes.
//
// SETUP:
//   Add to the same scene as NetworkManager (Lobby scene).
//   Must Awake() before NetworkManager.Start() calls Connect() - place
//   AuthManager GameObject earlier in scene hierarchy, or use Script Execution Order.
//
// Auth flow:
//   1. Login endpoints return a short-lived access token plus a long-lived refresh token.
//   2. The client stores the access token and refresh token locally on supported
//      native/editor platforms.
//   3. On the next launch, the client silently restores the session through /auth/refresh.
//
// Guest path:
//   If no token, IsAuthenticated = false. Socket connects without auth.
//   Server allows guests for casual/private modes; ranked queue requires auth.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace CastleDefender.Net
{
    public class AuthManager : MonoBehaviour
    {
        public static AuthManager Instance { get; private set; }
        public static event Action AuthStateChanged;

        public static string Token { get; private set; }
        public static string RefreshToken { get; private set; }
        public static string PlayerId { get; private set; }
        public static string DisplayName { get; private set; } = "Player";
        public static bool IsAuthenticated => !string.IsNullOrEmpty(Token);
        public static bool HasStoredSessionHint => PlayerPrefs.GetInt(SessionHintPrefKey, 0) == 1;

        const string AccessPrefKey = "castle_jwt";
        const string RefreshPrefKey = "castle_refresh_token";
        const string SessionHintPrefKey = "castle_has_saved_session";

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPersistedAuthState();
        }

        void LoadPersistedAuthState()
        {
            LoadRefreshToken();
            Token = PlayerPrefs.GetString(AccessPrefKey, null);

            if (!string.IsNullOrEmpty(Token))
                TryDecodeAccessToken();

            NotifyAuthStateChanged();
        }

        static void LoadRefreshToken()
        {
            RefreshToken = PlayerPrefs.GetString(RefreshPrefKey, null);
        }

        public static void SaveToken(string token)
        {
            StoreAccessToken(token);
            Instance?.TryDecodeAccessToken();
            NotifyAuthStateChanged();
        }

        public static void SaveSession(string accessToken, string refreshToken = null)
        {
            StoreAccessToken(accessToken);
            if (!string.IsNullOrWhiteSpace(refreshToken))
                StoreRefreshToken(refreshToken);

            SetSessionHint(true);
            Instance?.TryDecodeAccessToken();
            NotifyAuthStateChanged();
        }

        public static void ClearToken()
        {
            ClearStoredSession("Signed out.");
            NotifyAuthStateChanged();
        }

        public static void BeginLogout(string baseUrl)
        {
            string capturedRefreshToken = RefreshToken;
            ClearToken();

            if (Instance != null)
                Instance.StartCoroutine(Instance.SendLogoutRequest(baseUrl, capturedRefreshToken));
        }

        public IEnumerator RestoreSessionRoutine(string baseUrl, Action<bool, string> onComplete = null)
        {
            if (!HasStoredSessionHint)
            {
                onComplete?.Invoke(false, "No saved session was found.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                onComplete?.Invoke(false, "No auth server URL was available.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(RefreshToken))
            {
                Debug.LogWarning("[Auth] Session hint exists, but no refresh token was stored.");
                ClearStoredSession("Saved session metadata was incomplete.");
                NotifyAuthStateChanged();
                onComplete?.Invoke(false, "Saved session metadata was incomplete.");
                yield break;
            }

            string url = baseUrl.TrimEnd('/') + "/auth/refresh";
            using var req = CreateJsonPostRequest(url, BuildRefreshRequestBody());
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string error = ParseErrorBody(req.downloadHandler?.text, req.error);
                if (IsSessionRejected(req.responseCode))
                {
                    Debug.Log($"[Auth] Saved session rejected ({req.responseCode}); clearing saved credentials.");
                    ClearStoredSession("Saved session rejected by auth service.");
                    NotifyAuthStateChanged();
                }
                else
                {
                    Debug.LogWarning($"[Auth] Session restore failed: {error}");
                }

                onComplete?.Invoke(false, error);
                yield break;
            }

            RefreshResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<RefreshResponse>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Auth] Session restore parse failed: {ex.Message}");
                onComplete?.Invoke(false, "Session restore returned an unreadable response.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(response?.accessToken))
            {
                Debug.LogWarning("[Auth] Session restore succeeded without returning an access token.");
                onComplete?.Invoke(false, "Session restore did not return an access token.");
                yield break;
            }

            SaveSession(response.accessToken, response.refreshToken);
            Debug.Log("[Auth] Restored saved session.");
            onComplete?.Invoke(true, null);
        }

        IEnumerator SendLogoutRequest(string baseUrl, string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                yield break;

            string url = baseUrl.TrimEnd('/') + "/auth/logout";
            using var req = CreateJsonPostRequest(url, BuildLogoutRequestBody(refreshToken));
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Auth] Logout revocation request failed: {req.error}");
            }
        }

        bool TryDecodeAccessToken()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Token))
                {
                    ResetIdentity();
                    return false;
                }

                string[] parts = Token.Split('.');
                if (parts.Length < 2)
                {
                    Debug.LogWarning("[Auth] Stored access token was malformed; clearing cached access token.");
                    ClearCachedAccessToken("Stored access token was malformed.");
                    return false;
                }

                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');

                string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                var payload = JsonConvert.DeserializeObject<JwtPayload>(json);
                if (payload == null)
                {
                    Debug.LogWarning("[Auth] Stored access token payload was empty; clearing cached access token.");
                    ClearCachedAccessToken("Stored access token payload was empty.");
                    return false;
                }

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (payload.exp > 0 && payload.exp < now)
                {
                    Debug.Log("[Auth] Access token expired; keeping refresh session for silent restore.");
                    ClearCachedAccessToken("Access token expired.");
                    return false;
                }

                PlayerId = !string.IsNullOrEmpty(payload.sub) ? payload.sub : payload.id;
                DisplayName = !string.IsNullOrEmpty(payload.displayName)
                    ? payload.displayName
                    : (!string.IsNullOrEmpty(payload.name) ? payload.name : "Player");

                Debug.Log($"[Auth] Authenticated as '{DisplayName}' (id={PlayerId})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Auth] Access token decode failed: {ex.Message}; clearing cached access token.");
                ClearCachedAccessToken("Stored access token was unreadable.");
                return false;
            }
        }

        static void StoreAccessToken(string token)
        {
            Token = string.IsNullOrWhiteSpace(token) ? null : token;
            ResetIdentity();

            if (string.IsNullOrWhiteSpace(Token))
                PlayerPrefs.DeleteKey(AccessPrefKey);
            else
                PlayerPrefs.SetString(AccessPrefKey, Token);
            PlayerPrefs.Save();
        }

        static void StoreRefreshToken(string refreshToken)
        {
            RefreshToken = string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken;
            if (string.IsNullOrWhiteSpace(RefreshToken))
                PlayerPrefs.DeleteKey(RefreshPrefKey);
            else
                PlayerPrefs.SetString(RefreshPrefKey, RefreshToken);
            PlayerPrefs.Save();
        }

        static void ClearCachedAccessToken(string reason)
        {
            Token = null;
            ResetIdentity();
            PlayerPrefs.DeleteKey(AccessPrefKey);
            PlayerPrefs.Save();

            if (!string.IsNullOrWhiteSpace(reason))
                Debug.Log($"[Auth] Cleared cached access token ({reason})");
        }

        static void ClearStoredSession(string reason)
        {
            ClearCachedAccessToken(reason);
            StoreRefreshToken(null);
            SetSessionHint(false);
        }

        static void SetSessionHint(bool enabled)
        {
            if (enabled)
                PlayerPrefs.SetInt(SessionHintPrefKey, 1);
            else
                PlayerPrefs.DeleteKey(SessionHintPrefKey);
            PlayerPrefs.Save();
        }

        static void ResetIdentity()
        {
            PlayerId = null;
            DisplayName = "Player";
        }

        static bool IsSessionRejected(long responseCode)
        {
            return responseCode == 400 || responseCode == 401 || responseCode == 403;
        }

        static UnityWebRequest CreateJsonPostRequest(string url, string body)
        {
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(body) ? "{}" : body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        static string BuildRefreshRequestBody()
        {
            return JsonConvert.SerializeObject(new RefreshRequest { refreshToken = RefreshToken });
        }

        static string BuildLogoutRequestBody(string refreshToken)
        {
            return string.IsNullOrWhiteSpace(refreshToken)
                ? "{}"
                : JsonConvert.SerializeObject(new RefreshRequest { refreshToken = refreshToken });
        }

        static string ParseErrorBody(string body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body))
                return fallback;

            try
            {
                var parsed = JsonConvert.DeserializeObject<ErrorBody>(body);
                return !string.IsNullOrWhiteSpace(parsed?.error) ? parsed.error : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static void SignInWithGoogle(string serverUrl)
        {
            Application.OpenURL(serverUrl.TrimEnd('/') + "/auth/google");
        }

        static void NotifyAuthStateChanged()
        {
            AuthStateChanged?.Invoke();
            UserPreferencesManager.NotifyAuthenticationChanged();
        }

        [Serializable]
        sealed class RefreshRequest
        {
            public string refreshToken;
        }

        [Serializable]
        sealed class RefreshResponse
        {
            public string accessToken;
            public string refreshToken;
        }

        [Serializable]
        sealed class ErrorBody
        {
            public string error;
        }

        [Serializable]
        sealed class JwtPayload
        {
            public string sub;
            public string id;
            public string displayName;
            public string name;
            public long exp;
            public long iat;
        }
    }
}
