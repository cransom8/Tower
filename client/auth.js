'use strict';

// ── Castle Defender Auth Module ───────────────────────────────────────────────
// Handles Google Sign-In, password auth, JWT storage, token refresh, and profile state.

const Auth = (() => {
  const KEY_ACCESS  = 'cd_access';
  const KEY_REFRESH = 'cd_refresh';
  const KEY_PLAYER  = 'cd_player';

  let _player       = null;
  let _onAuthChange = null;

  // ── Storage helpers ──────────────────────────────────────────────────────────

  function getAccessToken()  { return localStorage.getItem(KEY_ACCESS); }
  function getRefreshToken() { return localStorage.getItem(KEY_REFRESH); }
  function getPlayer()       { return _player; }
  function isSignedIn()      { return !!_player; }

  function _store(accessToken, refreshToken, player) {
    localStorage.setItem(KEY_ACCESS,  accessToken);
    localStorage.setItem(KEY_REFRESH, refreshToken);
    localStorage.setItem(KEY_PLAYER,  JSON.stringify(player));
    _player = player;
    if (_onAuthChange) _onAuthChange(_player);
  }

  function _clear() {
    localStorage.removeItem(KEY_ACCESS);
    localStorage.removeItem(KEY_REFRESH);
    localStorage.removeItem(KEY_PLAYER);
    _player = null;
    if (_onAuthChange) _onAuthChange(null);
  }

  // ── Token management ─────────────────────────────────────────────────────────

  async function _refresh() {
    const rt = getRefreshToken();
    if (!rt) throw new Error('No refresh token');
    const res = await fetch('/auth/refresh', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ refreshToken: rt }),
    });
    if (!res.ok) { _clear(); throw new Error('Session expired — please sign in again'); }
    const { accessToken, refreshToken } = await res.json();
    localStorage.setItem(KEY_ACCESS,  accessToken);
    localStorage.setItem(KEY_REFRESH, refreshToken);
    return accessToken;
  }

  // Authenticated fetch — auto-refreshes on 401
  async function apiFetch(url, options = {}) {
    let token = getAccessToken();
    const go  = (t) => fetch(url, {
      ...options,
      headers: { ...options.headers, Authorization: `Bearer ${t}` },
    });
    let res = await go(token);
    if (res.status === 401) {
      try { token = await _refresh(); res = await go(token); } catch { /* expired */ }
    }
    return res;
  }

  // ── Google Sign-In ───────────────────────────────────────────────────────────

  async function handleGoogleCredential(response) {
    const res = await fetch('/auth/google', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ idToken: response.credential }),
    });
    if (!res.ok) throw new Error('Sign-in failed — please try again');
    const data = await res.json();
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  // ── Password auth ────────────────────────────────────────────────────────────

  async function loginWithPassword(email, password) {
    const res = await fetch('/auth/login', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ email, password }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Login failed');
    if (data.requiresMfa) return data; // caller handles MFA step
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function loginWithMfa(mfaToken, code) {
    const res = await fetch('/auth/login/mfa', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ mfaToken, code }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'MFA verification failed');
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function register(email, displayName, password) {
    const res = await fetch('/auth/register', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ email, displayName, password }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Registration failed');
    if (data.requiresVerification) return data; // caller shows verify modal
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function verifyEmail(token) {
    const res = await fetch('/auth/verify-email', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ token }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Verification failed');
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function resendVerification(email) {
    await fetch('/auth/resend-verification', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ email }),
    });
  }

  // ── 2FA management (called from settings panel) ───────────────────────────

  async function setup2fa() {
    const res = await apiFetch('/auth/2fa/setup', { method: 'POST', headers: { 'Content-Type': 'application/json' } });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Setup failed');
    return data; // { secret, qrCodeDataUrl }
  }

  async function enable2fa(code) {
    const res = await apiFetch('/auth/2fa/enable', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ code }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Enable failed');
    return data;
  }

  async function disable2fa(code) {
    const res = await apiFetch('/auth/2fa/disable', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ code }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Disable failed');
    return data;
  }

  // ── Logout ───────────────────────────────────────────────────────────────────

  async function logout() {
    try {
      const rt = getRefreshToken();
      if (rt) {
        await fetch('/auth/logout', {
          method:  'POST',
          headers: { 'Content-Type': 'application/json' },
          body:    JSON.stringify({ refreshToken: rt }),
        });
      }
    } catch { /* best-effort */ }
    _clear();
    // Reload to reset socket connection with cleared credentials
    window.location.reload();
  }

  // ── Init ─────────────────────────────────────────────────────────────────────

  async function init(onAuthChange) {
    _onAuthChange = onAuthChange;

    // Restore player from localStorage immediately (optimistic)
    const stored = localStorage.getItem(KEY_PLAYER);
    if (stored) {
      try { _player = JSON.parse(stored); } catch { _clear(); }
    }

    // Silently validate / refresh stored tokens
    if (getRefreshToken()) {
      try {
        await _refresh();
      } catch {
        _clear();
      }
    }

    if (_onAuthChange) _onAuthChange(_player);
  }

  return {
    init,
    handleGoogleCredential,
    loginWithPassword,
    loginWithMfa,
    register,
    verifyEmail,
    resendVerification,
    setup2fa,
    enable2fa,
    disable2fa,
    logout,
    getAccessToken,
    getPlayer,
    isSignedIn,
    apiFetch,
  };
})();
