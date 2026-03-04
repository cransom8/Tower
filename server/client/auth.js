'use strict';

// ── Ransom Forge Auth Module ───────────────────────────────────────────────
// Handles Google Sign-In, password auth, JWT storage, token refresh, and profile state.

const Auth = (() => {
  const KEY_PLAYER = 'cd_player'; // Only non-secret player profile stored in localStorage

  let _player       = null;
  let _onAuthChange = null;

  // ── Storage helpers ──────────────────────────────────────────────────────────

  function getPlayer()  { return _player; }
  function isSignedIn() { return !!_player; }

  function _store(_accessToken, _refreshToken, player) {
    // Tokens are stored server-side as HttpOnly cookies — not in localStorage
    localStorage.setItem(KEY_PLAYER, JSON.stringify(player));
    _player = player;
    if (_onAuthChange) _onAuthChange(_player);
  }

  function _clear() {
    localStorage.removeItem(KEY_PLAYER);
    _player = null;
    if (_onAuthChange) _onAuthChange(null);
  }

  // ── Token management ─────────────────────────────────────────────────────────

  async function _refresh() {
    // Token is sent automatically via HttpOnly cookie
    const res = await fetch('/auth/refresh', {
      method:      'POST',
      credentials: 'include',
      headers:     { 'Content-Type': 'application/json' },
    });
    if (!res.ok) { _clear(); throw new Error('Session expired — please sign in again'); }
    return true;
  }

  // Authenticated fetch — tokens sent via HttpOnly cookie, auto-refreshes on 401
  async function apiFetch(url, options = {}) {
    const go = () => fetch(url, { ...options, credentials: 'include' });
    let res = await go();
    if (res.status === 401) {
      try { await _refresh(); res = await go(); } catch { /* expired */ }
    }
    return res;
  }

  // ── Google Sign-In ───────────────────────────────────────────────────────────

  async function handleGoogleCredential(response) {
    const res = await fetch('/auth/google', {
      method:      'POST',
      credentials: 'include',
      headers:     { 'Content-Type': 'application/json' },
      body:        JSON.stringify({ idToken: response.credential }),
    });
    if (!res.ok) throw new Error('Sign-in failed — please try again');
    const data = await res.json();
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  // ── Password auth ────────────────────────────────────────────────────────────

  async function loginWithPassword(email, password) {
    const res = await fetch('/auth/login', {
      method:      'POST',
      credentials: 'include',
      headers:     { 'Content-Type': 'application/json' },
      body:        JSON.stringify({ email, password }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Login failed');
    if (data.requiresMfa) return data; // caller handles MFA step
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function loginWithMfa(mfaToken, code) {
    const res = await fetch('/auth/login/mfa', {
      method:      'POST',
      credentials: 'include',
      headers:     { 'Content-Type': 'application/json' },
      body:        JSON.stringify({ mfaToken, code }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'MFA verification failed');
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function register(email, displayName, password) {
    const res = await fetch('/auth/register', {
      method:      'POST',
      credentials: 'include',
      headers:     { 'Content-Type': 'application/json' },
      body:        JSON.stringify({ email, displayName, password }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Registration failed');
    if (data.requiresVerification) return data; // caller shows verify modal
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function verifyEmail(token) {
    const res = await fetch('/auth/verify-email', {
      method:      'POST',
      credentials: 'include',
      headers:     { 'Content-Type': 'application/json' },
      body:        JSON.stringify({ token }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Verification failed');
    _store(data.accessToken, data.refreshToken, data.player);
    return data.player;
  }

  async function resendVerification(email) {
    const res  = await fetch('/auth/resend-verification', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ email }),
    });
    const data = await res.json().catch(() => ({}));
    return data;
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
      // Token is revoked server-side and cookie is cleared
      await fetch('/auth/logout', {
        method:      'POST',
        credentials: 'include',
        headers:     { 'Content-Type': 'application/json' },
      });
    } catch { /* best-effort */ }
    _clear();
    // Reload to reset socket connection with cleared credentials
    window.location.reload();
  }

  // ── Init ─────────────────────────────────────────────────────────────────────

  async function init(onAuthChange) {
    _onAuthChange = onAuthChange;

    // Restore player profile from localStorage immediately (optimistic)
    const stored = localStorage.getItem(KEY_PLAYER);
    if (stored) {
      try { _player = JSON.parse(stored); } catch { _clear(); }
    }

    // Silently validate session via cookie — clears local state if session is gone
    if (_player) {
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
    getPlayer,
    isSignedIn,
    apiFetch,
  };
})();
