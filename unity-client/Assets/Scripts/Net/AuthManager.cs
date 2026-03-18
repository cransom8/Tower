// AuthManager.cs — JWT storage, decode, and expiry check.
// Singleton — persists across scenes.
//
// SETUP:
//   Add to the same scene as NetworkManager (Lobby scene).
//   Must Awake() before NetworkManager.Start() calls Connect() — place
//   AuthManager GameObject earlier in scene hierarchy, or use Script Execution Order.
//
// Auth flow (WebGL):
//   1. Player clicks Sign In → Application.OpenURL(serverUrl + "/auth/google")
//   2. Server OAuth callback → sets localStorage "castle_jwt" → redirects to app
//   3. On next load, JSIO_Connect reads localStorage and passes auth: { token }
//   4. AuthManager.LoadToken() reads PlayerPrefs (synced from localStorage by SaveToken)
//
// Auth flow (Editor / Standalone):
//   Token is read from PlayerPrefs only.
//   Use AuthManager.SaveToken(jwt) after any manual login flow.
//
// Guest path:
//   If no token, IsAuthenticated = false. Socket connects without auth.
//   Server allows guests for casual/private modes; ranked queue requires auth.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Newtonsoft.Json;

namespace CastleDefender.Net
{
    public class AuthManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static AuthManager Instance { get; private set; }

        // ── State ─────────────────────────────────────────────────────────────
        public static string Token           { get; private set; }
        public static string PlayerId        { get; private set; }
        public static string DisplayName     { get; private set; } = "Player";
        public static bool   IsAuthenticated => !string.IsNullOrEmpty(Token);

        const string PrefKey = "castle_jwt";

        // ── JSLib imports (WebGL only) ─────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string JSIO_GetJWT();
        [DllImport("__Internal")] static extern void   JSIO_SetJWT(string token);
        [DllImport("__Internal")] static extern void   JSIO_ClearJWT();
#endif

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadToken();
        }

        // ── Token management ─────────────────────────────────────────────────

        void LoadToken()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Try localStorage first (written by server after OAuth callback)
            try
            {
                string fromStorage = JSIO_GetJWT();
                if (!string.IsNullOrEmpty(fromStorage))
                {
                    Token = fromStorage;
                    PlayerPrefs.SetString(PrefKey, Token);
                    PlayerPrefs.Save();
                }
                else
                {
                    Token = PlayerPrefs.GetString(PrefKey, null);
                }
            }
            catch
            {
                Token = PlayerPrefs.GetString(PrefKey, null);
            }
#else
            Token = PlayerPrefs.GetString(PrefKey, null);
#endif
            if (!string.IsNullOrEmpty(Token))
                DecodeToken();
        }

        /// <summary>Call after a successful login (e.g. non-OAuth flow or token refresh).</summary>
        public static void SaveToken(string token)
        {
            Token = token;
            PlayerPrefs.SetString(PrefKey, token);
            PlayerPrefs.Save();
#if UNITY_WEBGL && !UNITY_EDITOR
            try { JSIO_SetJWT(token); } catch { }
#endif
            Instance?.DecodeToken();
        }

        /// <summary>Sign out — clears token everywhere.</summary>
        public static void ClearToken()
        {
            Token       = null;
            PlayerId    = null;
            DisplayName = "Player";
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
#if UNITY_WEBGL && !UNITY_EDITOR
            try { JSIO_ClearJWT(); } catch { }
#endif
            Debug.Log("[Auth] Signed out");
        }

        // ── JWT decode ────────────────────────────────────────────────────────

        void DecodeToken()
        {
            try
            {
                string[] parts = Token.Split('.');
                if (parts.Length < 2) { ClearToken(); return; }

                // Base64url → Base64
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');

                string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                var payload = JsonConvert.DeserializeObject<JwtPayload>(json);

                // Check expiry
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (payload.exp > 0 && payload.exp < now)
                {
                    Debug.Log("[Auth] Token expired — clearing");
                    ClearToken();
                    return;
                }

                PlayerId    = !string.IsNullOrEmpty(payload.sub) ? payload.sub : payload.id;
                DisplayName = !string.IsNullOrEmpty(payload.displayName)
                            ? payload.displayName
                            : (!string.IsNullOrEmpty(payload.name) ? payload.name : "Player");

                Debug.Log($"[Auth] Authenticated as '{DisplayName}' (id={PlayerId})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Auth] JWT decode failed: {ex.Message} — clearing token");
                ClearToken();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the Google SSO login page. After auth, the server sets localStorage
        /// "castle_jwt" and redirects back to the app. On reload, LoadToken() picks it up.
        /// </summary>
        public static void SignInWithGoogle(string serverUrl)
        {
            Application.OpenURL(serverUrl.TrimEnd('/') + "/auth/google");
        }

        // ─────────────────────────────────────────────────────────────────────
        [Serializable]
        class JwtPayload
        {
            public string sub;          // standard subject (player id)
            public string id;           // our fallback id field
            public string displayName;  // our custom display name
            public string name;         // standard name claim
            public long   exp;          // expiry (unix seconds)
            public long   iat;          // issued at
        }
    }
}
