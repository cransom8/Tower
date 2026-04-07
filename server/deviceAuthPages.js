"use strict";

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function renderPlatformRetiredPage({ appName = "Castle Defender" } = {}) {
  const safeAppName = escapeHtml(appName);
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${safeAppName} Platform Update</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #0e1117;
      --panel: #171b24;
      --edge: #2b3343;
      --text: #eef2f8;
      --muted: #9aa6bc;
      --accent: #d7a84a;
      --link: #7dc4ff;
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 24px;
      background:
        radial-gradient(circle at top, rgba(215, 168, 74, 0.14), transparent 32%),
        linear-gradient(180deg, #090b10 0%, var(--bg) 100%);
      color: var(--text);
      font: 16px/1.5 "Segoe UI", system-ui, sans-serif;
    }

    main {
      width: min(100%, 720px);
      padding: 28px;
      border: 1px solid var(--edge);
      border-radius: 18px;
      background: rgba(23, 27, 36, 0.94);
      box-shadow: 0 20px 70px rgba(0, 0, 0, 0.35);
    }

    h1 {
      margin: 0 0 12px;
      font-size: clamp(2rem, 4vw, 2.8rem);
      line-height: 1.05;
    }

    p {
      margin: 0 0 14px;
      color: var(--muted);
    }

    .banner {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 18px;
      padding: 8px 12px;
      border: 1px solid rgba(215, 168, 74, 0.35);
      border-radius: 999px;
      background: rgba(215, 168, 74, 0.12);
      color: var(--accent);
      font-size: 0.92rem;
      font-weight: 600;
      letter-spacing: 0.02em;
    }

    ul {
      margin: 20px 0 0;
      padding-left: 18px;
      color: var(--muted);
    }

    a {
      color: var(--link);
      text-decoration: none;
    }

    a:hover {
      text-decoration: underline;
    }
  </style>
</head>
<body>
  <main>
    <div class="banner">WebGL Retired</div>
    <h1>${safeAppName} now targets Android first and PC standalone second.</h1>
    <p>The browser player is no longer part of the active platform stack.</p>
    <p>This server still hosts gameplay APIs, remote content, account services, and a lightweight authorization page for device sign-in.</p>
    <ul>
      <li>Android is the primary validation platform.</li>
      <li>PC standalone is the debugging and scale-validation platform.</li>
      <li><a href="/authorize">Open device authorization</a> if you are finishing Google sign-in for the native client.</li>
      <li><a href="/privacy">Privacy policy</a></li>
    </ul>
  </main>
</body>
</html>`;
}

function renderDeviceAuthorizationPage({
  appName = "Castle Defender",
  googleClientId = "",
  code = "",
} = {}) {
  const safeAppName = escapeHtml(appName);
  const initialCode = JSON.stringify(String(code ?? ""));
  const initialGoogleClientId = JSON.stringify(String(googleClientId ?? ""));

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${safeAppName} Device Authorization</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #0d1016;
      --panel: #171c27;
      --edge: #2b3244;
      --text: #eef2f8;
      --muted: #98a4bc;
      --accent: #ddb45d;
      --accent-strong: #f0c777;
      --success: #74d48e;
      --error: #ff8b8b;
      --button: #223047;
      --button-hover: #2b3c59;
      --field: #0f1420;
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      min-height: 100vh;
      padding: 24px;
      display: grid;
      place-items: center;
      background:
        radial-gradient(circle at top, rgba(221, 180, 93, 0.18), transparent 30%),
        linear-gradient(180deg, #090b10 0%, var(--bg) 100%);
      color: var(--text);
      font: 16px/1.5 "Segoe UI", system-ui, sans-serif;
    }

    main {
      width: min(100%, 760px);
      padding: 30px;
      border: 1px solid var(--edge);
      border-radius: 20px;
      background: rgba(23, 28, 39, 0.95);
      box-shadow: 0 22px 70px rgba(0, 0, 0, 0.36);
    }

    .eyebrow {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 18px;
      padding: 8px 12px;
      border: 1px solid rgba(221, 180, 93, 0.35);
      border-radius: 999px;
      background: rgba(221, 180, 93, 0.12);
      color: var(--accent);
      font-size: 0.92rem;
      font-weight: 600;
      letter-spacing: 0.02em;
    }

    h1 {
      margin: 0 0 10px;
      font-size: clamp(2rem, 4vw, 2.9rem);
      line-height: 1.05;
    }

    p {
      margin: 0 0 14px;
      color: var(--muted);
    }

    .grid {
      display: grid;
      gap: 18px;
      margin-top: 24px;
    }

    .card {
      padding: 18px;
      border: 1px solid var(--edge);
      border-radius: 16px;
      background: rgba(15, 20, 32, 0.72);
    }

    .card h2 {
      margin: 0 0 8px;
      font-size: 1.1rem;
      color: var(--accent-strong);
    }

    .session {
      margin-top: 10px;
      padding: 12px 14px;
      border-radius: 12px;
      background: rgba(34, 48, 71, 0.48);
      color: var(--muted);
      font-size: 0.95rem;
    }

    label {
      display: block;
      margin-bottom: 8px;
      font-size: 0.95rem;
      font-weight: 600;
      color: var(--accent-strong);
    }

    input {
      width: 100%;
      padding: 14px 16px;
      border: 1px solid var(--edge);
      border-radius: 12px;
      background: var(--field);
      color: var(--text);
      font: 600 1.1rem/1.1 Consolas, "Courier New", monospace;
      letter-spacing: 0.2em;
      text-transform: uppercase;
    }

    button {
      appearance: none;
      width: 100%;
      margin-top: 14px;
      padding: 14px 16px;
      border: 1px solid rgba(221, 180, 93, 0.35);
      border-radius: 12px;
      background: var(--button);
      color: var(--text);
      font: 600 1rem/1 "Segoe UI", system-ui, sans-serif;
      cursor: pointer;
      transition: background 120ms ease, border-color 120ms ease, transform 120ms ease;
    }

    button:hover:enabled {
      background: var(--button-hover);
      border-color: rgba(240, 199, 119, 0.55);
      transform: translateY(-1px);
    }

    button:disabled {
      opacity: 0.45;
      cursor: not-allowed;
      transform: none;
    }

    .message {
      min-height: 1.6em;
      margin-top: 14px;
      font-size: 0.95rem;
      color: var(--muted);
    }

    .message.success { color: var(--success); }
    .message.error { color: var(--error); }

    .help {
      margin-top: 14px;
      font-size: 0.92rem;
    }

    .footer {
      margin-top: 24px;
      font-size: 0.9rem;
      color: var(--muted);
    }

    a {
      color: #8ac6ff;
      text-decoration: none;
    }

    a:hover {
      text-decoration: underline;
    }
  </style>
</head>
<body>
  <main>
    <div class="eyebrow">Device Authorization</div>
    <h1>Finish sign-in for the native client.</h1>
    <p>Use this page only when the Android or PC build asks you to authorize a device code for Google sign-in.</p>

    <div class="grid">
      <section class="card">
        <h2>1. Sign in on this page</h2>
        <p>If you already have an active session here, you can skip straight to the authorization step.</p>
        <div id="session-status" class="session">Checking whether you are already signed in…</div>
        <div id="google-signin" style="margin-top: 16px;"></div>
        <p id="google-help" class="help"></p>
      </section>

      <section class="card">
        <h2>2. Approve the device code</h2>
        <label for="device-code">Device code</label>
        <input id="device-code" name="device-code" type="text" inputmode="latin" autocomplete="one-time-code" maxlength="6" placeholder="ABC123">
        <button id="authorize-button" type="button" disabled>Authorize Device</button>
        <p id="message" class="message"></p>
      </section>
    </div>

    <p class="footer"><a href="/">Back to platform status</a></p>
  </main>

  <script src="https://accounts.google.com/gsi/client" async defer></script>
  <script>
    const googleClientId = ${initialGoogleClientId};
    const codeInput = document.getElementById("device-code");
    const authorizeButton = document.getElementById("authorize-button");
    const messageEl = document.getElementById("message");
    const sessionStatusEl = document.getElementById("session-status");
    const googleHelpEl = document.getElementById("google-help");
    const googleButtonHost = document.getElementById("google-signin");

    const initialCode = ${initialCode};
    let sessionReady = false;
    let authorizeInFlight = false;

    function normalizeCode(value) {
      return String(value || "").toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);
    }

    function setMessage(text, kind = "") {
      messageEl.textContent = text || "";
      messageEl.className = kind ? "message " + kind : "message";
    }

    function setSessionStatus(text, signedIn) {
      sessionStatusEl.textContent = text;
      sessionStatusEl.style.color = signedIn ? "var(--success)" : "var(--muted)";
    }

    function updateAuthorizeState() {
      const hasCode = normalizeCode(codeInput.value).length === 6;
      authorizeButton.disabled = authorizeInFlight || !sessionReady || !hasCode;
    }

    async function readJsonResponse(response, fallbackMessage) {
      const text = await response.text();
      if (!text) {
        return {};
      }

      try {
        return JSON.parse(text);
      } catch {
        throw new Error(fallbackMessage);
      }
    }

    function resolveErrorMessage(payload, fallbackMessage) {
      if (payload && typeof payload.error === "string" && payload.error.trim()) {
        return payload.error.trim();
      }
      return fallbackMessage;
    }

    async function refreshSession() {
      try {
        const response = await fetch("/auth/session", {
          credentials: "include",
          cache: "no-store",
        });
        const payload = await readJsonResponse(
          response,
          "The sign-in page received an unexpected session response."
        );
        if (!response.ok) {
          throw new Error(resolveErrorMessage(payload, "Unable to verify the current session."));
        }
        sessionReady = !!payload && payload.signedIn === true;
        if (sessionReady) {
          const name = payload.player && payload.player.displayName ? payload.player.displayName : "Player";
          setSessionStatus("Signed in as " + name + ". You can authorize the device now.", true);
          setMessage("Google sign-in is complete. Approve the device code when you are ready.", "success");
        } else {
          setSessionStatus("Sign in with Google on this page before authorizing the device.", false);
        }
      } catch (error) {
        sessionReady = false;
        setSessionStatus("Could not verify your sign-in state. Reload and try again.", false);
        setMessage(error && error.message ? error.message : "Unable to verify the current session.", "error");
      }

      updateAuthorizeState();
    }

    async function signInWithGoogleCredential(credential) {
      const response = await fetch("/auth/google", {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ credential }),
      });

      const payload = await readJsonResponse(
        response,
        "The server returned an unexpected response while finishing Google sign-in."
      );
      if (!response.ok) {
        throw new Error(resolveErrorMessage(payload, "Google sign-in failed."));
      }
    }

    async function authorizeDevice() {
      if (authorizeButton.disabled) {
        return;
      }

      authorizeInFlight = true;
      updateAuthorizeState();
      setMessage("Authorizing device…");

      try {
        const userCode = normalizeCode(codeInput.value);
        const response = await fetch("/auth/device/authorize", {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ userCode }),
        });

        const payload = await readJsonResponse(
          response,
          "The server returned an unexpected response while authorizing the device."
        );
        if (!response.ok) {
          throw new Error(resolveErrorMessage(payload, "Device authorization failed."));
        }

        setMessage("Device approved. You can return to the Android or PC client now.", "success");
      } catch (error) {
        setMessage(error && error.message ? error.message : "Device authorization failed.", "error");
      } finally {
        authorizeInFlight = false;
        updateAuthorizeState();
      }
    }

    function initializeGoogleButton() {
      if (!googleClientId) {
        googleHelpEl.textContent = "Google sign-in is not configured on this server.";
        return;
      }

      if (!window.google || !window.google.accounts || !window.google.accounts.id) {
        googleHelpEl.textContent = "Google sign-in could not finish loading. Refresh this page and try again.";
        return;
      }

      window.google.accounts.id.initialize({
        client_id: googleClientId,
        callback: async (response) => {
          if (!response || !response.credential) {
            setMessage("Google sign-in was cancelled or returned an empty credential.", "error");
            return;
          }

          setMessage("Signing in with Google…");
          try {
            await signInWithGoogleCredential(response.credential);
            await refreshSession();
          } catch (error) {
            setMessage(error && error.message ? error.message : "Google sign-in failed.", "error");
          }
        },
      });

      window.google.accounts.id.renderButton(googleButtonHost, {
        theme: "filled_black",
        size: "large",
        shape: "pill",
        text: "continue_with",
        width: 320,
      });
      googleHelpEl.textContent = "Google sign-in stays on this page and only exists to authorize the native client.";
    }

    codeInput.value = normalizeCode(initialCode);
    codeInput.addEventListener("input", () => {
      codeInput.value = normalizeCode(codeInput.value);
      updateAuthorizeState();
    });
    authorizeButton.addEventListener("click", authorizeDevice);

    refreshSession();

    if (document.readyState === "complete") {
      initializeGoogleButton();
    } else {
      window.addEventListener("load", initializeGoogleButton, { once: true });
    }
  </script>
</body>
</html>`;
}

module.exports = {
  renderDeviceAuthorizationPage,
  renderPlatformRetiredPage,
};
