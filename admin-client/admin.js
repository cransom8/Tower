/* admin.js — Castle Defender Admin Dashboard SPA — Phase 2 */
'use strict';

// ── State ──────────────────────────────────────────────────────────────────
// NOTE: Secrets and JWTs are NOT stored in sessionStorage (XSS risk).
// The admin JWT is stored server-side as an HttpOnly cookie.
// Only non-secret metadata (role, email, displayName) is kept in sessionStorage.
const S = {
  // Auth
  authMode:         sessionStorage.getItem('adminAuthMode') || 'jwt',
  adminRole:        sessionStorage.getItem('adminRole') || '',
  adminEmail:       sessionStorage.getItem('adminEmail') || '',
  adminDisplayName: sessionStorage.getItem('adminDisplayName') || '',
  adminResetToken:  '',
  // Navigation
  tab:        'dashboard',
  // Tab sub-state
  players:    { q: '', offset: 0, total: 0, rows: [] },
  matches:    { tab: 'history', status: '', mode: '', offset: 0, total: 0, rows: [], live: [] },
  audit:      { offset: 0, total: 0, rows: [] },
  units:      { view: 'kanban', displayFields: [] },
  liveHandle: null,
  branding:   null,
};

// RBAC permission map (mirrors server)
const ROLE_PERMS = {
  viewer:    new Set([]),
  support:   new Set(['player.ban', 'player.revoke']),
  moderator: new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write']),
  editor:    new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write',
                      'config.write', 'season.write', 'rating.adjust']),
  engineer:  new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write',
                      'config.write', 'season.write', 'rating.adjust', 'admin.write']),
  owner:     new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write',
                      'config.write', 'season.write', 'rating.adjust', 'admin.write']),
};

function can(perm) {
  const role = S.adminRole || 'viewer';
  if (role === 'owner') return true;
  return ROLE_PERMS[role]?.has(perm) ?? false;
}

// ── Auth headers ───────────────────────────────────────────────────────────
// JWT is stored as an HttpOnly cookie (cd_admin) — no Authorization header needed.
function authHeaders() {
  return { 'Content-Type': 'application/json' };
}

// ── API ────────────────────────────────────────────────────────────────────
async function api(method, path, body) {
  const opts = { method, headers: authHeaders(), credentials: 'include' };
  if (body !== undefined) opts.body = JSON.stringify(body);
  const res = await fetch(path, opts);
  if (!res.ok) {
    const txt = await res.text().catch(() => res.statusText);
    let msg = txt;
    try { msg = JSON.parse(txt).error || txt; } catch { /* ok */ }
    throw new Error(msg);
  }
  return res.json();
}

// ── Toast ──────────────────────────────────────────────────────────────────
function toast(msg, type = 'ok') {
  const el = document.createElement('div');
  el.className = `toast-msg ${type}`;
  el.textContent = msg;
  document.getElementById('toast').appendChild(el);
  setTimeout(() => el.remove(), 3500);
}

// ── Modal ──────────────────────────────────────────────────────────────────
function openModal(title, bodyHtml, footerHtml = '') {
  document.getElementById('modal-title').textContent = title;
  document.getElementById('modal-body').innerHTML = bodyHtml;
  document.getElementById('modal-footer').innerHTML = footerHtml;
  applyFieldDescriptions(document.getElementById('modal-body'));
  document.getElementById('modal-overlay').classList.remove('hidden');
}
function closeModal(e) {
  if (e && e.target !== document.getElementById('modal-overlay')) return;
  document.getElementById('modal-overlay').classList.add('hidden');
}
window.closeModal = closeModal;

// ── Navigation ─────────────────────────────────────────────────────────────
function navigate(tab) {
  S.tab = tab;
  document.querySelectorAll('nav a').forEach(a => {
    a.classList.toggle('active', a.dataset.tab === tab);
  });
  loadTab(tab);
}

document.querySelectorAll('nav a').forEach(a => {
  a.addEventListener('click', e => {
    e.preventDefault();
    navigate(a.dataset.tab);
  });
});

// ── Login mode ─────────────────────────────────────────────────────────────
function setLoginMode(_mode) {
  // Secret Key mode removed — always use email/Google/2FA
  renderGoogleBtn();
}
window.setLoginMode = setLoginMode;

// ── Google Sign-In ─────────────────────────────────────────────────────────
function renderGoogleBtn() {
  const container = document.getElementById('admin-google-btn-container');
  if (!container || container.dataset.rendered) return;
  const clientId = window.__GOOGLE_CLIENT_ID__;
  if (!clientId || typeof google === 'undefined') return;
  google.accounts.id.initialize({
    client_id: clientId,
    callback: handleAdminGoogleCredential,
    ux_mode: 'popup',
  });
  google.accounts.id.renderButton(container, {
    type: 'standard', shape: 'rectangular', theme: 'filled_black',
    text: 'signin_with', size: 'large', width: 280,
  });
  container.dataset.rendered = '1';
}

async function handleAdminGoogleCredential(response) {
  const errEl = document.getElementById('login-err');
  errEl.textContent = '';
  try {
    if (!response?.credential) {
      errEl.textContent = 'Google did not return a credential. Please try again.';
      return;
    }

    const res = await fetch('/admin/auth/google', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ credential: response.credential }),
      credentials: 'include',
    });
    const raw = await res.text();
    let d = {};
    try { d = raw ? JSON.parse(raw) : {}; } catch { d = {}; }
    if (!res.ok) {
      const msg = d.error || raw || 'Google login failed.';
      errEl.textContent = msg;
      console.error('[admin] Google SSO failed', { status: res.status, body: raw });
      return;
    }
    if (d.twoFaRequired) {
      S._pendingTicket = d.ticket;
      show2faStep();
      return;
    }

    // Normal success path
    if (d.role || d.email) {
      finishLogin(d);
      return;
    }

    // Fallback: cookie was set but response body was empty/partial.
    const me = await api('GET', '/admin/me');
    finishLogin({
      role: me?.user?.role || 'viewer',
      email: me?.user?.email || '',
      displayName: me?.user?.display_name || me?.user?.email || 'Admin',
    });
  } catch (err) {
    errEl.textContent = err.message || 'Google login failed.';
  }
}
window.handleAdminGoogleCredential = handleAdminGoogleCredential;

// ── 2FA step helpers ───────────────────────────────────────────────────────
function show2faStep() {
  setLoginSection('2fa');
  document.getElementById('login-2fa-code')?.focus();
}

function cancelLogin2fa() {
  S._pendingTicket = null;
  setLoginSection('password');
  document.getElementById('login-2fa-code').value = '';
  document.getElementById('login-err').textContent = '';
  document.getElementById('login-msg').textContent = '';
}
window.cancelLogin2fa = cancelLogin2fa;

async function doLogin2fa() {
  const errEl = document.getElementById('login-err');
  const code   = document.getElementById('login-2fa-code')?.value.trim();
  if (!code) { errEl.textContent = 'Enter your 2FA code.'; return; }
  try {
    const res = await fetch('/admin/login/2fa', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ticket: S._pendingTicket, code }),
      credentials: 'include',
    });
    const d = await res.json();
    if (!res.ok) { errEl.textContent = d.error || '2FA failed.'; return; }
    S._pendingTicket = null;
    finishLogin(d);
  } catch (err) {
    errEl.textContent = err.message || '2FA failed.';
  }
}
window.doLogin2fa = doLogin2fa;

function setLoginSection(section) {
  const ids = ['login-pass-section', 'login-2fa-section', 'login-forgot-section', 'login-reset-section'];
  ids.forEach(id => document.getElementById(id)?.classList.add('hidden'));
  if (section === '2fa') document.getElementById('login-2fa-section')?.classList.remove('hidden');
  else if (section === 'forgot') document.getElementById('login-forgot-section')?.classList.remove('hidden');
  else if (section === 'reset') document.getElementById('login-reset-section')?.classList.remove('hidden');
  else document.getElementById('login-pass-section')?.classList.remove('hidden');
}

function showForgotPassword() {
  document.getElementById('login-err').textContent = '';
  document.getElementById('login-msg').textContent = '';
  document.getElementById('forgot-email').value = document.getElementById('login-email')?.value.trim() || '';
  setLoginSection('forgot');
  document.getElementById('forgot-email')?.focus();
}
window.showForgotPassword = showForgotPassword;

function backToLogin() {
  document.getElementById('login-err').textContent = '';
  document.getElementById('login-msg').textContent = '';
  setLoginSection('password');
}
window.backToLogin = backToLogin;

async function submitForgotPassword() {
  const errEl = document.getElementById('login-err');
  const msgEl = document.getElementById('login-msg');
  errEl.textContent = '';
  msgEl.textContent = '';
  const email = document.getElementById('forgot-email')?.value.trim();
  if (!email) { errEl.textContent = 'Enter your admin email.'; return; }
  try {
    const res = await fetch('/admin/forgot-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email }),
      credentials: 'include',
    });
    if (!res.ok) {
      const d = await res.json().catch(() => ({}));
      errEl.textContent = d.error || 'Could not send reset link.';
      return;
    }
    msgEl.textContent = 'If this email exists, a reset link has been sent.';
  } catch (err) {
    errEl.textContent = err.message || 'Could not send reset link.';
  }
}
window.submitForgotPassword = submitForgotPassword;

async function submitResetPassword() {
  const errEl = document.getElementById('login-err');
  const msgEl = document.getElementById('login-msg');
  errEl.textContent = '';
  msgEl.textContent = '';
  if (!S.adminResetToken) { errEl.textContent = 'Missing reset token.'; return; }

  const password = document.getElementById('reset-password')?.value || '';
  const confirm  = document.getElementById('reset-password-confirm')?.value || '';
  if (!password || password.length < 8) { errEl.textContent = 'Password must be at least 8 characters.'; return; }
  if (password !== confirm) { errEl.textContent = 'Passwords do not match.'; return; }

  try {
    const res = await fetch('/admin/reset-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: S.adminResetToken, password }),
      credentials: 'include',
    });
    const d = await res.json().catch(() => ({}));
    if (!res.ok) {
      errEl.textContent = d.error || 'Password reset failed.';
      return;
    }

    S.adminResetToken = '';
    const url = new URL(window.location.href);
    url.searchParams.delete('admin_reset');
    url.searchParams.delete('reset');
    window.history.replaceState({}, '', url.pathname + (url.search || ''));
    document.getElementById('reset-password').value = '';
    document.getElementById('reset-password-confirm').value = '';
    msgEl.textContent = 'Password reset complete. You can sign in now.';
    setLoginSection('password');
  } catch (err) {
    errEl.textContent = err.message || 'Password reset failed.';
  }
}
window.submitResetPassword = submitResetPassword;

function finishLogin(d) {
  S.adminRole        = d.role;
  S.adminEmail       = d.email;
  S.adminDisplayName = d.displayName || d.email;
  S.authMode         = 'jwt';
  sessionStorage.setItem('adminRole',        d.role);
  sessionStorage.setItem('adminEmail',       d.email);
  sessionStorage.setItem('adminDisplayName', d.displayName || d.email);
  sessionStorage.setItem('adminAuthMode',    'jwt');
  enterApp();
}

// ── Login / Logout ─────────────────────────────────────────────────────────
async function doLogin() {
  const errEl = document.getElementById('login-err');
  errEl.textContent = '';
  const email    = document.getElementById('login-email')?.value.trim();
  const password = document.getElementById('login-password')?.value;
  if (!email || !password) { errEl.textContent = 'Enter email and password.'; return; }
  try {
    const res = await fetch('/admin/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
      credentials: 'include',
    });
    if (!res.ok) {
      const d = await res.json().catch(() => ({}));
      errEl.textContent = d.error || 'Login failed.';
      return;
    }
    const d = await res.json();
    if (d.twoFaRequired) {
      S._pendingTicket = d.ticket;
      show2faStep();
      return;
    }
    finishLogin(d);
  } catch (err) {
    errEl.textContent = err.message || 'Login failed.';
  }
}
window.doLogin = doLogin;

function enterApp() {
  document.getElementById('login-screen').style.display = 'none';
  document.getElementById('app').classList.remove('hidden');
  const identity = document.getElementById('admin-identity');
  if (identity) {
    const roleBadge = S.adminRole ? ` <span class="badge badge-blue" style="font-size:10px">${esc(S.adminRole)}</span>` : '';
    identity.innerHTML = esc(S.adminDisplayName) + roleBadge;
  }
  const btnMyAccount = document.getElementById('btn-my-account');
  if (btnMyAccount) btnMyAccount.style.display = '';
  if (can('admin.write')) {
    document.getElementById('nav-team')?.classList.remove('hidden');
  }
  startLivePoll();
  navigate('dashboard');
}

function doLogout() {
  fetch('/admin/logout', { method: 'POST', credentials: 'include' }).catch(() => {});
  ['adminRole','adminEmail','adminDisplayName','adminAuthMode']
    .forEach(k => sessionStorage.removeItem(k));
  S.adminRole = S.adminEmail = S.adminDisplayName = '';
  S.authMode = 'jwt';
  S.adminResetToken = '';
  stopLivePoll();
  document.getElementById('app').classList.add('hidden');
  document.getElementById('login-screen').style.display = '';
  setLoginSection('password');
}
window.doLogout = doLogout;

// ── Live poll ──────────────────────────────────────────────────────────────
function startLivePoll() {
  stopLivePoll();
  updateLiveHeader();
  S.liveHandle = setInterval(updateLiveHeader, 10_000);
}
function stopLivePoll() {
  if (S.liveHandle) { clearInterval(S.liveHandle); S.liveHandle = null; }
}
async function updateLiveHeader() {
  try {
    const d = await api('GET', '/admin/stats/live');
    document.getElementById('live-label').textContent =
      `${d.connectedSockets} connected · ${d.activeGames} active`;
  } catch { /* ignore */ }
}

function refreshCurrent() { loadTab(S.tab); }
window.refreshCurrent = refreshCurrent;

// ── Tab loader ─────────────────────────────────────────────────────────────
function loadTab(tab) {
  const m = {
    dashboard: loadDashboard,
    players:   loadPlayers,
    towers:    loadTowers,
    anticheat: loadAnticheat,
    matches:   loadMatches,
    analytics: loadAnalytics,
    branding:  loadBranding,
    config:    loadForgeWarsConfig,
    ops:       loadOps,
    audit:     loadAudit,
    team:      loadTeam,
    survival:  loadSurvival,
    units:     loadUnits,
    assets:    loadAssets,
  };
  (m[tab] || loadDashboard)();
}

// ── Helpers ────────────────────────────────────────────────────────────────
function setContent(html) {
  document.getElementById('main-content').innerHTML = html;
  applyFieldDescriptions(document.getElementById('main-content'));
}

function defaultBranding() {
  return {
    publicTitle: 'Forge Wars',
    publicSubtitle: 'Team lane defense',
    publicBrowserTitle: 'Forge Wars - Multiplayer',
    adminTitle: 'Forge Wars Admin',
    adminLoginTitle: 'Forge Wars Control',
    loadoutTitle: 'Loadout',
    loadoutHint: 'Choose five units for this match',
    loadoutBuilderTitle: 'Loadout Builder',
  };
}

function currentBranding() {
  return Object.assign(defaultBranding(), S.branding || {});
}

function applyAdminBranding(brandingInput) {
  S.branding = Object.assign(defaultBranding(), brandingInput || {});
  const branding = currentBranding();
  const pageTitleEl = document.getElementById('admin-page-title');
  if (pageTitleEl) pageTitleEl.textContent = branding.adminTitle;
  else document.title = branding.adminTitle;
  const loginTitleEl = document.getElementById('admin-login-title');
  if (loginTitleEl) loginTitleEl.textContent = branding.adminLoginTitle;
  const headerTitleEl = document.getElementById('admin-header-title');
  if (headerTitleEl) headerTitleEl.textContent = branding.adminTitle;
}

async function loadForgeWarsConfig() {
  setContent('<p class="load">Loading Forge Wars config...</p>');
  try {
    const [brandingRes, unitRes, waveRes, barracksRes, assetRes, cfgRes, histRes] = await Promise.all([
      api('GET', '/admin/branding').catch(() => ({ branding: currentBranding() })),
      api('GET', '/admin/unit-types').catch(() => ({ unitTypes: [] })),
      api('GET', '/admin/ml-waves/configs').catch(() => ({ configs: [] })),
      api('GET', '/admin/barracks-levels').catch(() => ({ levels: [] })),
      api('GET', '/admin/asset-packs').catch(() => ([])),
      api('GET', '/admin/game-config').catch(() => ({ multilane: { globalParams: {} } })),
      api('GET', '/admin/game-config/history?mode=multilane').catch(() => ({ versions: [] })),
    ]);

    const branding = Object.assign(defaultBranding(), brandingRes.branding || {});
    const unitTypes = unitRes.unitTypes || [];
    const waves = waveRes.configs || [];
    const barracksLevels = barracksRes.levels || [];
    const assetPacks = Array.isArray(assetRes) ? assetRes : [];
    configData = cfgRes;
    configHistory = histRes.versions || [];

    const enabledUnits = unitTypes.filter((ut) => ut.enabled);
    const visibleUnits = enabledUnits.filter((ut) => ut.display_to_players !== false);
    const attackers = enabledUnits.filter((ut) => ut.behavior_mode === 'moving' || ut.behavior_mode === 'both');
    const defenders = enabledUnits.filter((ut) => ut.build_cost > 0 && (ut.behavior_mode === 'fixed' || ut.behavior_mode === 'both'));
    const defaultWave = waves.find((cfg) => cfg.is_default) || null;
    const latestBarracks = barracksLevels.length ? barracksLevels[barracksLevels.length - 1] : null;
    const enabledAssetPacks = assetPacks.filter((pack) => pack.enabled);
    const gp = configData?.multilane?.globalParams || {};
    const canWrite = can('config.write');
    const historyHtml = configHistory.slice(0, 5).map((v) => `
      <tr>
        <td class="mono text-muted" style="font-size:11px">${esc(String(v.id).slice(0, 8))}</td>
        <td>${esc(v.label || ('v' + v.version))}</td>
        <td class="text-muted">${reltime(v.published_at)}</td>
        <td class="text-muted">${esc(v.published_by || '-')}</td>
      </tr>
    `).join('');

    setContent(`
      <h2>Forge Wars Runtime Config</h2>
      <p class="text-muted" style="font-size:12px;margin-bottom:18px">
        This is the current config map for the Unity Forge Wars client. Use it as the entry point for the systems that actually drive the live game.
      </p>
      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;margin-bottom:18px">
        <div class="section">
          <div class="section-header"><h3>Branding</h3></div>
          <div class="section-body">
            <div><strong>${esc(branding.publicTitle)}</strong></div>
            <div class="text-muted" style="font-size:12px;margin-top:4px">${esc(branding.publicSubtitle)}</div>
            <div class="text-muted" style="font-size:12px;margin-top:8px">Admin shell: ${esc(branding.adminTitle)}</div>
            <button class="btn-sm" style="margin-top:12px" onclick="navigate('branding')">Open Branding</button>
          </div>
        </div>
        <div class="section">
          <div class="section-header"><h3>Unit Catalog</h3></div>
          <div class="section-body">
            <div><strong>${enabledUnits.length}</strong> enabled unit types</div>
            <div class="text-muted" style="font-size:12px;margin-top:4px">${attackers.length} attackers/loadout units</div>
            <div class="text-muted" style="font-size:12px;margin-top:4px">${defenders.length} buildable defenders · ${visibleUnits.length} shown to players</div>
            <button class="btn-sm" style="margin-top:12px" onclick="navigate('units')">Open Unit Catalog</button>
          </div>
        </div>
        <div class="section">
          <div class="section-header"><h3>Barracks</h3></div>
          <div class="section-body">
            <div><strong>${barracksLevels.length}</strong> configured levels</div>
            <div class="text-muted" style="font-size:12px;margin-top:4px">
              ${latestBarracks
                ? `Highest level: Lv ${esc(String(latestBarracks.level))} · Multiplier ${Number(latestBarracks.multiplier || 1).toFixed(2)}x`
                : 'No barracks levels configured'}
            </div>
            <div class="text-muted" style="font-size:12px;margin-top:4px">Unity consumes these live from <code>/api/barracks-levels</code>.</div>
            <button class="btn-sm" style="margin-top:12px" onclick="navigate('units')">Edit Barracks</button>
          </div>
        </div>
        <div class="section">
          <div class="section-header"><h3>Wave Defense</h3></div>
          <div class="section-body">
            <div><strong>${waves.length}</strong> wave configs</div>
            <div class="text-muted" style="font-size:12px;margin-top:4px">
              ${defaultWave
                ? `Default: ${esc(defaultWave.name)} · ${esc(String(defaultWave.wave_count || 0))} waves`
                : 'No default wave config selected'}
            </div>
            <button class="btn-sm" style="margin-top:12px" onclick="navigate('survival')">Open Wave Config</button>
          </div>
        </div>
      </div>
      <div class="section" style="margin-bottom:18px">
        <div class="section-header"><h3>Match Start Settings</h3></div>
        <div class="section-body">
          <p class="text-muted" style="font-size:12px;margin-bottom:12px">
            These values are now used by newly created Forge Wars matches and are sent to Unity in <code>ml_match_config</code> / match snapshots.
          </p>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px">
            <div class="form-group">
              <label>Start Gold</label>
              <input id="fw-start-gold" type="number" min="0" step="1" value="${esc(String(gp.startGold ?? 70))}" ${canWrite ? '' : 'disabled'}>
            </div>
            <div class="form-group">
              <label>Start Income</label>
              <input id="fw-start-income" type="number" min="0" step="0.1" value="${esc(String(gp.startIncome ?? 10))}" ${canWrite ? '' : 'disabled'}>
            </div>
            <div class="form-group">
              <label>Player Lives</label>
              <input id="fw-lives-start" type="number" min="1" step="1" value="${esc(String(gp.livesStart ?? 20))}" ${canWrite ? '' : 'disabled'}>
            </div>
            <div class="form-group">
              <label>Team HP</label>
              <input id="fw-team-hp-start" type="number" min="1" step="1" value="${esc(String(gp.teamHpStart ?? 20))}" ${canWrite ? '' : 'disabled'}>
            </div>
          </div>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px;margin-top:10px">
            <div class="form-group">
              <label>Build Phase Ticks</label>
              <input id="fw-build-phase-ticks" type="number" min="20" step="1" value="${esc(String(gp.buildPhaseTicks ?? 600))}" ${canWrite ? '' : 'disabled'}>
            </div>
            <div class="form-group">
              <label>Transition Ticks</label>
              <input id="fw-transition-phase-ticks" type="number" min="20" step="1" value="${esc(String(gp.transitionPhaseTicks ?? 200))}" ${canWrite ? '' : 'disabled'}>
            </div>
          </div>
          ${canWrite ? `
            <div style="display:flex;gap:8px;align-items:center;margin-top:12px;flex-wrap:wrap">
              <button class="btn-primary btn-sm" onclick="saveForgeWarsSettings()">Save Start Settings</button>
              <span class="text-muted" style="font-size:12px">Saves as a new multilane config version and becomes active immediately for new matches.</span>
            </div>
          ` : '<p class="text-muted" style="font-size:12px;margin-top:12px">Read-only. Editor role or higher required to save.</p>'}
          <div class="tbl-wrap" style="margin-top:14px"><table>
            <thead><tr><th>ID</th><th>Label</th><th>Published</th><th>By</th></tr></thead>
            <tbody>${historyHtml || '<tr><td colspan="4" class="text-muted" style="padding:12px;text-align:center">No multilane config versions yet</td></tr>'}</tbody>
          </table></div>
        </div>
      </div>

      <div class="section" style="margin-bottom:18px">
        <div class="section-header"><h3>Unity Client Wiring</h3></div>
        <div class="section-body">
          <div class="text-muted" style="font-size:12px;line-height:1.6">
            <div>Game type: <code>line_wars</code> (Forge Wars)</div>
            <div>Unity project: <code>C:\\Users\\Crans\\CastleDefenderClient</code></div>
            <div>Catalog endpoints: <code>/api/unit-types</code>, <code>/api/barracks-levels</code></div>
            <div>Match loadout source: <code>ml_match_config.loadout</code></div>
            <div>Wave source: default config from <code>/admin/ml-waves</code> at match start</div>
          </div>
        </div>
      </div>
      <div class="section" style="margin-bottom:18px">
        <div class="section-header"><h3>Admin Editing Map</h3></div>
        <div class="section-body">
          <div class="tbl-wrap"><table>
            <thead><tr><th>Runtime Feature</th><th>Where To Edit</th><th>Notes</th></tr></thead>
            <tbody>
              <tr><td>Player-facing copy and admin labels</td><td><button class="btn-sm" onclick="navigate('branding')">Branding</button></td><td class="text-muted">Lobby, loadout, browser title, and admin shell text</td></tr>
              <tr><td>Loadout units, defender stats, sprites, sounds</td><td><button class="btn-sm" onclick="navigate('units')">Unit Catalog</button></td><td class="text-muted">Drives Unity command bar, tile menu, and match loadout visuals</td></tr>
              <tr><td>Barracks upgrade values</td><td><button class="btn-sm" onclick="navigate('units')">Unit Catalog</button></td><td class="text-muted">Edit in the Barracks Levels section inside Unit Catalog</td></tr>
              <tr><td>Default wave progression</td><td><button class="btn-sm" onclick="navigate('survival')">Wave Config</button></td><td class="text-muted">Loaded when a Forge Wars match starts</td></tr>
              <tr><td>Optional asset override packs</td><td><button class="btn-sm" onclick="navigate('assets')">Asset Overrides</button></td><td class="text-muted">${enabledAssetPacks.length} enabled packs</td></tr>
            </tbody>
          </table></div>
        </div>
      </div>
      <div class="section">
        <div class="section-header"><h3>Legacy Balance Data</h3></div>
        <div class="section-body">
          <p class="text-muted" style="font-size:12px;margin:0">
            The older versioned <code>/admin/game-config</code> editor still exists for legacy server balance flows, but the current Forge Wars Unity client is primarily driven by the systems listed above.
          </p>
        </div>
      </div>
    `);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

async function saveForgeWarsSettings() {
  if (!configData?.multilane) {
    toast('Current Forge Wars config is not loaded yet.', 'err');
    return;
  }

  const num = (id, fallback = 0) => {
    const v = parseFloat(document.getElementById(id)?.value);
    return Number.isFinite(v) ? v : fallback;
  };

  const nextConfig = JSON.parse(JSON.stringify(configData.multilane));
  nextConfig.globalParams = Object.assign({}, nextConfig.globalParams || {}, {
    startGold: num('fw-start-gold', 70),
    startIncome: num('fw-start-income', 10),
    livesStart: Math.max(1, Math.floor(num('fw-lives-start', 20))),
    teamHpStart: Math.max(1, Math.floor(num('fw-team-hp-start', 20))),
    buildPhaseTicks: Math.max(20, Math.floor(num('fw-build-phase-ticks', 600))),
    transitionPhaseTicks: Math.max(20, Math.floor(num('fw-transition-phase-ticks', 200))),
  });

  try {
    await api('POST', '/admin/game-config', {
      mode: 'multilane',
      config: nextConfig,
      label: `Forge Wars settings ${new Date().toLocaleDateString()}`,
      notes: 'Updated from Forge Wars admin start settings panel.',
      activate: true,
    });
    toast('Forge Wars start settings saved.', 'ok');
    await loadForgeWarsConfig();
  } catch (err) {
    toast(`Save failed: ${err.message}`, 'err');
  }
}
window.saveForgeWarsSettings = saveForgeWarsSettings;

const FIELD_HELP_BY_ID = {
  'login-email': 'Use the email assigned to your admin account.',
  'login-password': 'Enter your current admin password.',
  'forgot-email': 'We send a reset link only if this admin email exists.',
  'reset-password': 'Use at least 8 characters. Prefer a unique passphrase.',
  'reset-password-confirm': 'Re-enter the new password to confirm.',
  'login-2fa-code': 'Enter the 6-digit code currently shown in your authenticator app.',
};

function fieldHelpFromLabel(labelText, control) {
  const l = (labelText || '').toLowerCase();
  if (l.includes('email')) return 'Use a valid email address for this admin account.';
  if (l.includes('password')) return 'Use a strong password and keep it private.';
  if (l.includes('2fa') || l.includes('two-factor') || l.includes('confirmation code')) return 'Enter the current one-time code from your authenticator app.';
  if (l.includes('display name')) return 'Friendly name shown in admin activity and account views.';
  if (l === 'role' || l.includes(' role')) return 'Controls which admin permissions this user has.';
  if (l.includes('status') || l.includes('enabled')) return 'Toggle whether this item is active and usable.';
  if (l.includes('key')) return 'Stable identifier used by the game/config APIs.';
  if (l === 'name' || l.includes('name *')) return 'Human-readable name shown in admin and UI lists.';
  if (l.includes('description')) return 'Short summary of purpose or behavior.';
  if (l.includes('notes')) return 'Internal notes for operators and future edits.';
  if (l.includes('id')) return 'Internal identifier (read-only unless creating new data).';
  if (l.includes('url')) return 'Absolute or app-served URL to the asset/file to use.';
  if (l.includes('icon')) return 'Small image used in cards, selectors, and summaries.';
  if (l.includes('sprite')) return 'Static image used for this unit/tower orientation.';
  if (l.includes('animation')) return 'Sprite sheet or animated image used during movement/attacks.';
  if (l.includes('damage')) return 'Base damage dealt when an attack connects.';
  if (l.includes('speed')) return 'Higher values increase action or movement rate.';
  if (l.includes('range')) return 'Effective attack distance in game units.';
  if (l.includes('cost')) return 'Resource cost required for this action or upgrade.';
  if (l.includes('income')) return 'Income granted when this unit/action is used.';
  if (l.includes('hp')) return 'Base health points before modifiers are applied.';
  if (l.includes('multiplier')) return 'Scaling factor applied to base values.';
  if (l.includes('wave')) return 'Wave index or range this setting applies to.';
  if (l.includes('gold')) return 'Starting or bonus gold for this mode/config.';
  if (l.includes('lives')) return 'Starting lives for the wave set.';
  if (l.includes('duration')) return 'Duration in milliseconds; leave blank for no expiry when supported.';
  if (l.includes('type')) return 'Select the category/type used for game logic.';
  if (l.includes('behavior mode')) return 'Controls whether this unit moves, stays fixed, or supports both.';

  if (control.type === 'checkbox') return 'Enable this option to turn the setting on.';
  if (control.type === 'file') return 'Upload a file to populate or replace this asset field.';
  if (control.type === 'number') return 'Numeric value used by game balance/config.';
  if (control.tagName === 'SELECT') return 'Choose one value from the available options.';
  return 'Update this field to control how the related admin setting behaves.';
}

function getLabelTextForControl(root, control) {
  if (control.id) {
    const forLabel = root.querySelector(`label[for="${control.id}"]`);
    if (forLabel) return forLabel.textContent.replace(/\s+/g, ' ').trim();
  }
  const wrappedLabel = control.closest('label');
  if (!wrappedLabel) return '';
  const copy = wrappedLabel.cloneNode(true);
  copy.querySelectorAll('input,select,textarea,button').forEach(el => el.remove());
  return copy.textContent.replace(/\s+/g, ' ').trim();
}

function findHelpContainer(control) {
  const group = control.closest('.form-group');
  if (group) return group;
  const label = control.closest('label');
  if (label?.parentElement) return label.parentElement;
  return control.parentElement;
}

function applyFieldDescriptions(root = document) {
  if (!root) return;
  const controls = root.querySelectorAll('input:not([type="hidden"]):not([type="button"]):not([type="submit"]), select, textarea');
  controls.forEach(control => {
    const id = control.id || '';
    const container = findHelpContainer(control);
    if (!container) return;
    if (control.dataset.helpApplied === '1') return;
    if (id && container.querySelector(`.field-help[data-help-for="${id}"]`)) return;

    const labelText = getLabelTextForControl(root, control);
    const text = FIELD_HELP_BY_ID[id] || fieldHelpFromLabel(labelText, control);
    if (!text) return;

    const help = document.createElement('div');
    help.className = 'field-help text-muted';
    if (id) help.dataset.helpFor = id;
    help.textContent = text;
    container.appendChild(help);
    control.dataset.helpApplied = '1';
  });
}
function fmt(n) { return Number(n || 0).toLocaleString(); }
function dur(secs) {
  const s = Math.round(secs || 0);
  if (s < 60) return `${s}s`;
  return `${Math.floor(s / 60)}m ${s % 60}s`;
}
function reltime(ts) {
  if (!ts) return '—';
  const diff = Date.now() - new Date(ts).getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return new Date(ts).toLocaleDateString();
}
function esc(s) {
  // Escape all HTML special chars including single-quote to prevent XSS in
  // both innerHTML contexts and JS onclick="...esc(userValue)..." attributes.
  return String(s || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
function statusBadge(status) {
  const map = {
    active: 'badge-green', suspended: 'badge-red',
    verified: 'badge-green', unverified: 'badge-amber',
    completed: 'badge-blue', in_progress: 'badge-amber',
    abandoned: 'badge-gray', pending: 'badge-gray',
    win: 'badge-green', loss: 'badge-red', draw: 'badge-gray',
    viewer: 'badge-gray', support: 'badge-blue', moderator: 'badge-amber',
    editor: 'badge-green', engineer: 'badge-blue', owner: 'badge-red',
  };
  return `<span class="badge ${map[status] || 'badge-gray'}">${esc(status)}</span>`;
}

// ═══════════════════════════════════════════════════════════════════════
// DASHBOARD
// ═══════════════════════════════════════════════════════════════════════
function fmtWait(ms) {
  if (!ms) return '—';
  const s = Math.floor(ms / 1000);
  if (s < 60) return `${s}s`;
  return `${Math.floor(s / 60)}m ${s % 60}s`;
}

async function loadDashboard() {
  setContent('<p class="load">Loading…</p>');
  try {
    const [live, daily, queueData] = await Promise.all([
      api('GET', '/admin/stats/live'),
      api('GET', '/admin/stats/daily'),
      api('GET', '/admin/queue').catch(() => ({ queues: {}, privateLobbies: [], soloGames: [] })),
    ]);

    const q = queueData.queues || {};
    const ranked  = q['line_wars:2v2:1'] || { entries: 0, players: 0, oldestMs: null };
    const casual  = q['line_wars:2v2:0'] || { entries: 0, players: 0, oldestMs: null };
    const privates = queueData.privateLobbies || [];
    const soloGames = queueData.soloGames || [];

    const privateRows = privates.length
      ? privates.map(p => `
          <tr>
            <td><code>${esc(p.code)}</code></td>
            <td>${fmt(p.playerCount)} human · ${fmt(p.aiCount)} AI</td>
            <td>${esc(p.pvpMode)}</td>
            <td>${fmtWait(p.waitMs)}</td>
          </tr>`).join('')
      : '<tr><td colspan="4" class="load">No private lobbies waiting</td></tr>';

    setContent(`
      <h2>Dashboard</h2>
      <div class="kpi-grid">
        <div class="kpi-card blue">
          <div class="label">Connected</div>
          <div class="value">${fmt(live.connectedSockets)}</div>
          <div class="sub">sockets right now</div>
        </div>
        <div class="kpi-card green">
          <div class="label">Active Matches</div>
          <div class="value">${fmt(live.activeGames)}</div>
          <div class="sub">${fmt(live.classicGames)} classic · ${fmt(live.mlGames - live.soloGames)} pvp · ${fmt(live.soloGames)} solo</div>
        </div>
        <div class="kpi-card amber">
          <div class="label">Lobby Rooms</div>
          <div class="value">${fmt(live.lobbyRooms)}</div>
          <div class="sub">${fmt(live.privateLobbies)} private waiting</div>
        </div>
        <div class="kpi-card blue">
          <div class="label">Ranked Queue</div>
          <div class="value">${fmt(ranked.players)}</div>
          <div class="sub">${fmt(ranked.entries)} parties · longest ${fmtWait(ranked.oldestMs)}</div>
        </div>
        <div class="kpi-card">
          <div class="label">Casual Queue</div>
          <div class="value">${fmt(casual.players)}</div>
          <div class="sub">${fmt(casual.entries)} parties · longest ${fmtWait(casual.oldestMs)}</div>
        </div>
        <div class="kpi-card">
          <div class="label">Total Players</div>
          <div class="value">${fmt(daily.totalPlayers)}</div>
          <div class="sub">all-time registered</div>
        </div>
        <div class="kpi-card">
          <div class="label">Matches Today</div>
          <div class="value">${fmt(daily.matchesToday)}</div>
          <div class="sub">last 24 hours</div>
        </div>
        <div class="kpi-card">
          <div class="label">New Players</div>
          <div class="value">${fmt(daily.newPlayersToday)}</div>
          <div class="sub">last 24 hours</div>
        </div>
        <div class="kpi-card">
          <div class="label">Avg Match Length</div>
          <div class="value">${dur(daily.avgMatchLengthSeconds)}</div>
          <div class="sub">last 7 days, completed</div>
        </div>
      </div>

      <div class="section">
        <div class="section-header">
          <h3>Live Queues</h3>
          <button onclick="loadDashboard()">Refresh</button>
        </div>
        <div class="section-body">
          <table>
            <thead><tr><th>Queue</th><th>Players</th><th>Parties</th><th>Longest Wait</th></tr></thead>
            <tbody>
              <tr>
                <td><strong>Ranked 2v2</strong></td>
                <td>${fmt(ranked.players)}</td>
                <td>${fmt(ranked.entries)}</td>
                <td>${fmtWait(ranked.oldestMs)}</td>
              </tr>
              <tr>
                <td><strong>Casual 2v2</strong></td>
                <td>${fmt(casual.players)}</td>
                <td>${fmt(casual.entries)}</td>
                <td>${fmtWait(casual.oldestMs)}</td>
              </tr>
              <tr>
                <td><strong>Solo (active)</strong></td>
                <td>${fmt(soloGames.length)}</td>
                <td>—</td>
                <td>—</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="section">
        <div class="section-header"><h3>Private Lobbies Waiting</h3></div>
        <div class="section-body">
          <table>
            <thead><tr><th>Code</th><th>Players</th><th>Mode</th><th>Waiting</th></tr></thead>
            <tbody>${privateRows}</tbody>
          </table>
        </div>
      </div>

      <div class="section">
        <div class="section-header"><h3>Quick Actions</h3></div>
        <div class="section-body">
          <div style="display:flex;gap:10px;flex-wrap:wrap">
            <button onclick="navigate('ops')">&#x2699; Feature Flags</button>
            <button onclick="navigate('players')">&#x1F464; Player Search</button>
            <button onclick="navigate('matches')">&#x2694; Live Matches</button>
            <button onclick="navigate('analytics')">&#x1F4C8; Analytics</button>
            <button onclick="navigate('anticheat')">&#x1F6E1; Anti-cheat</button>
            <button onclick="navigate('audit')">&#x1F4CB; Audit Log</button>
          </div>
        </div>
      </div>
    `);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

// ═══════════════════════════════════════════════════════════════════════
// PLAYERS
// ═══════════════════════════════════════════════════════════════════════
async function loadPlayers() {
  setContent(`
    <h2>Players</h2>
    <div class="search-row">
      <input type="text" id="player-search" placeholder="Search by name or ID…"
        value="${esc(S.players.q)}" onkeydown="if(event.key==='Enter')searchPlayers()">
      <button onclick="searchPlayers()">Search</button>
    </div>
    <div id="players-table-wrap"><p class="load">Loading…</p></div>
  `);
  await fetchPlayers();
}

async function searchPlayers() {
  S.players.q = document.getElementById('player-search')?.value?.trim() || '';
  S.players.offset = 0;
  await fetchPlayers();
}
window.searchPlayers = searchPlayers;

async function fetchPlayers() {
  const wrap = document.getElementById('players-table-wrap');
  if (!wrap) return;
  wrap.innerHTML = '<p class="load">Loading…</p>';
  try {
    const d = await api('GET', `/admin/players?q=${encodeURIComponent(S.players.q)}&limit=25&offset=${S.players.offset}`);
    S.players.total = d.total;
    S.players.rows  = d.players;
    renderPlayersTable(wrap);
  } catch (err) {
    wrap.innerHTML = `<p class="load text-danger">Error: ${esc(err.message)}</p>`;
  }
}

function renderPlayersTable(wrap) {
  const { rows, total, offset } = S.players;
  if (!rows.length) { wrap.innerHTML = '<p class="load">No players found.</p>'; return; }

  const banBtn = can('player.ban');
  const trs = rows.map(p => `
    <tr>
      <td><a href="#" onclick="openPlayerModal('${p.id}');return false">${esc(p.display_name)}</a></td>
      <td class="mono text-muted" style="font-size:11px">${p.id.slice(0,8)}…</td>
      <td class="text-muted">${esc(p.email || '—')}</td>
      <td>${statusBadge(p.email_verified ? 'verified' : 'unverified')}</td>
      <td>${statusBadge(p.status)}</td>
      <td>${p.rating != null ? Math.round(p.rating) : '—'}</td>
      <td>${p.wins ?? 0} / ${p.losses ?? 0}</td>
      <td class="text-muted">${reltime(p.created_at)}</td>
      <td>
        <button class="btn-sm" onclick="openPlayerModal('${p.id}')">View</button>
        ${banBtn ? (p.status === 'active'
          ? `<button class="btn-sm btn-danger" onclick="banPlayer('${p.id}','${esc(p.display_name)}')">Ban</button>`
          : `<button class="btn-sm btn-success" onclick="unbanPlayer('${p.id}')">Unban</button>`) : ''}
      </td>
    </tr>`).join('');

  wrap.innerHTML = `
    <div class="tbl-wrap">
      <table>
        <thead><tr>
          <th>Display Name</th><th>ID</th><th>Email</th><th>Verification</th><th>Status</th>
          <th>Rating</th><th>W/L</th><th>Joined</th><th>Actions</th>
        </tr></thead>
        <tbody>${trs}</tbody>
      </table>
    </div>
    <div class="pager">
      <span>${offset + 1}–${Math.min(offset + rows.length, total)} of ${fmt(total)}</span>
      ${offset > 0 ? `<button class="btn-sm" onclick="pagePlayers(-1)">← Prev</button>` : ''}
      ${offset + rows.length < total ? `<button class="btn-sm" onclick="pagePlayers(1)">Next →</button>` : ''}
    </div>`;
}

window.pagePlayers = function(dir) {
  S.players.offset = Math.max(0, S.players.offset + dir * 25);
  fetchPlayers();
};

async function openPlayerModal(id) {
  openModal('Player Profile', '<p class="load">Loading…</p>');
  try {
    const d = await api('GET', `/admin/players/${id}`);
    const p = d.player;
    const ratingsHtml = d.ratings.map(r => `
      <div class="stat-pill">
        <strong>${r.rating != null ? Math.round(r.rating) : '—'}</strong>
        <span> ${r.mode} · ${r.wins}W ${r.losses}L</span>
      </div>`).join('');

    const matchRows = d.recentMatches.map(m => `
      <tr>
        <td class="mono text-muted" style="font-size:11px">${m.id.slice(0,8)}</td>
        <td>${esc(m.mode)}</td>
        <td>${statusBadge(m.result)}</td>
        <td class="text-muted">${reltime(m.started_at)}</td>
        <td>${m.ended_at ? dur((new Date(m.ended_at) - new Date(m.started_at)) / 1000) : '—'}</td>
      </tr>`).join('');

    const banBtn    = can('player.ban');
    const revokeBtn = can('player.revoke');

    document.getElementById('modal-body').innerHTML = `
      <div class="two-col" style="margin-bottom:16px">
        <div>
          <div class="form-group">
            <label>Display Name</label>
            <div style="font-size:16px;font-weight:600">${esc(p.display_name)}</div>
          </div>
          <div class="form-group">
            <label>ID</label>
            <div class="mono" style="font-size:12px">${p.id}</div>
          </div>
          <div class="form-group">
            <label>Email</label>
            <div>${esc(p.email || '—')}</div>
          </div>
          <div class="form-group">
            <label>Verification</label>
            <div>${statusBadge(p.email_verified ? 'verified' : 'unverified')}</div>
          </div>
          <div class="form-group">
            <label>Auth Methods</label>
            <div class="text-muted">${p.has_google_auth ? 'Google' : ''}${p.has_google_auth && p.has_password_auth ? ' + ' : ''}${p.has_password_auth ? 'Password' : ''}${!p.has_google_auth && !p.has_password_auth ? 'Unknown' : ''}</div>
          </div>
          <div class="form-group">
            <label>Status</label>
            <div>${statusBadge(p.status)}</div>
          </div>
          ${p.ban_reason ? `<div class="form-group"><label>Ban Reason</label><div class="text-danger">${esc(p.ban_reason)}</div></div>` : ''}
          <div class="form-group">
            <label>Joined</label>
            <div>${new Date(p.created_at).toLocaleString()}</div>
          </div>
        </div>
        <div>
          <label>Ratings</label>
          <div class="stat-row mt8">${ratingsHtml || '<span class="text-muted">No ratings yet</span>'}</div>
        </div>
      </div>
      <h3 class="mt16">Recent Matches</h3>
      <div class="tbl-wrap">
        <table>
          <thead><tr><th>ID</th><th>Mode</th><th>Result</th><th>When</th><th>Duration</th></tr></thead>
          <tbody>${matchRows || '<tr><td colspan="5" class="text-muted">No matches</td></tr>'}</tbody>
        </table>
      </div>`;

    document.getElementById('modal-footer').innerHTML = `
      ${revokeBtn ? `<button onclick="revokeTokens('${p.id}')">🔒 Revoke Tokens</button>` : ''}
      ${banBtn ? (p.status === 'active'
        ? `<button class="btn-danger" onclick="banPlayer('${p.id}','${esc(p.display_name)}');closeModal()">Ban Player</button>`
        : `<button class="btn-success" onclick="unbanPlayer('${p.id}')">Unban Player</button>`) : ''}
      <button onclick="closeModal()">Close</button>`;
  } catch (err) {
    document.getElementById('modal-body').innerHTML = `<p class="text-danger">Error: ${esc(err.message)}</p>`;
  }
}
window.openPlayerModal = openPlayerModal;

async function banPlayer(id, name) {
  const reason = prompt(`Ban reason for ${name}?`, 'Violation of terms');
  if (reason === null) return;
  try {
    await api('POST', `/admin/players/${id}/ban`, { reason: reason || 'Banned by admin' });
    toast(`Banned ${name}`, 'ok');
    fetchPlayers();
  } catch (err) { toast(`Ban failed: ${err.message}`, 'err'); }
}
window.banPlayer = banPlayer;

async function unbanPlayer(id) {
  try {
    await api('DELETE', `/admin/players/${id}/ban`);
    toast('Player unbanned', 'ok');
    fetchPlayers();
  } catch (err) { toast(`Unban failed: ${err.message}`, 'err'); }
}
window.unbanPlayer = unbanPlayer;

async function revokeTokens(id) {
  if (!confirm('Revoke all refresh tokens for this player? They will need to log in again.')) return;
  try {
    const d = await api('POST', `/admin/players/${id}/revoke-tokens`);
    toast(`Revoked ${d.revoked} token(s)`, 'ok');
  } catch (err) { toast(`Revoke failed: ${err.message}`, 'err'); }
}
window.revokeTokens = revokeTokens;

// ═══════════════════════════════════════════════════════════════════════
// ANTI-CHEAT
// ═══════════════════════════════════════════════════════════════════════
async function loadAnticheat() {
  setContent('<p class="load">Loading anti-cheat data…</p>');
  try {
    const d = await api('GET', '/admin/anticheat');
    const flags = d.flags || [];

    const SIGNAL_LABELS = {
      new_high_wr: { label: 'New Account / High Win Rate', cls: 'badge-amber' },
      extreme_wr:  { label: 'Extreme Win Rate (≥90%)',     cls: 'badge-red'   },
      burst_new:   { label: 'Burst Activity (New Account)', cls: 'badge-red'  },
    };

    if (!flags.length) {
      setContent(`
        <h2>Anti-cheat</h2>
        <div class="section">
          <div class="section-body" style="text-align:center;padding:40px">
            <div style="font-size:32px;margin-bottom:12px">✅</div>
            <p class="text-muted">No suspicious accounts detected.</p>
          </div>
        </div>
        ${renderAnticheatRules()}`);
      return;
    }

    const banBtn = can('player.ban');
    const trs = flags.map(f => {
      const sig = SIGNAL_LABELS[f.signal] || { label: esc(f.signal), cls: 'badge-gray' };
      const wr  = f.win_rate != null ? (+f.win_rate * 100).toFixed(1) + '%' : '—';
      return `<tr>
        <td><a href="#" onclick="openPlayerModal('${f.player_id}');return false">${esc(f.display_name)}</a></td>
        <td><span class="badge ${sig.cls}">${sig.label}</span></td>
        <td>${wr}</td>
        <td>${fmt(f.total_games)}</td>
        <td class="text-muted">${f.account_age_days != null ? Math.round(f.account_age_days) + 'd' : '—'}</td>
        <td>
          <button class="btn-sm" onclick="openPlayerModal('${f.player_id}')">View</button>
          ${banBtn ? `<button class="btn-sm btn-danger" onclick="banPlayer('${f.player_id}','${esc(f.display_name)}')">Ban</button>` : ''}
        </td>
      </tr>`;
    }).join('');

    setContent(`
      <h2>Anti-cheat <span class="badge badge-red">${flags.length} flagged</span></h2>
      <p class="text-muted" style="margin-bottom:20px;font-size:12px">
        Accounts flagged automatically by statistical anomaly detection. Review each case before acting.
      </p>
      <div class="section" style="margin-bottom:20px">
        <div class="section-header">
          <h3>Flagged Accounts</h3>
          <button class="btn-sm" onclick="loadAnticheat()">↻ Refresh</button>
        </div>
        <div class="section-body" style="padding:0">
          <div class="tbl-wrap"><table>
            <thead><tr>
              <th>Player</th><th>Signal</th><th>Win Rate</th><th>Games</th><th>Account Age</th><th>Actions</th>
            </tr></thead>
            <tbody>${trs}</tbody>
          </table></div>
        </div>
      </div>
      ${renderAnticheatRules()}`);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

function renderAnticheatRules() {
  return `
    <div class="section">
      <div class="section-header"><h3>Detection Rules</h3></div>
      <div class="section-body" style="padding:0">
        <table>
          <thead><tr><th>Rule</th><th>Condition</th></tr></thead>
          <tbody>
            <tr><td><span class="badge badge-amber">New Account / High WR</span></td><td class="text-muted">Account &lt;30 days · ≥5 games · win rate ≥75%</td></tr>
            <tr><td><span class="badge badge-red">Extreme Win Rate</span></td><td class="text-muted">≥10 games · win rate ≥90% (any age)</td></tr>
            <tr><td><span class="badge badge-red">Burst Activity</span></td><td class="text-muted">Account &lt;7 days · ≥20 games</td></tr>
          </tbody>
        </table>
      </div>
    </div>`;
}

// ═══════════════════════════════════════════════════════════════════════
// MATCHES
// ═══════════════════════════════════════════════════════════════════════
async function loadMatches() {
  setContent(`
    <h2>Matches</h2>
    <div style="display:flex;gap:8px;margin-bottom:16px">
      <button id="mtab-live" class="${S.matches.tab==='live'?'btn-primary':''}" onclick="switchMatchTab('live')">● Live</button>
      <button id="mtab-history" class="${S.matches.tab==='history'?'btn-primary':''}" onclick="switchMatchTab('history')">History</button>
    </div>
    <div id="matches-content"><p class="load">Loading…</p></div>
  `);
  await loadMatchSubtab();
}

function switchMatchTab(tab) {
  S.matches.tab = tab;
  document.getElementById('mtab-live')?.classList.toggle('btn-primary', tab==='live');
  document.getElementById('mtab-history')?.classList.toggle('btn-primary', tab==='history');
  loadMatchSubtab();
}
window.switchMatchTab = switchMatchTab;

async function loadMatchSubtab() {
  if (S.matches.tab === 'live') loadLiveMatches();
  else loadMatchHistory();
}

async function loadLiveMatches() {
  const wrap = document.getElementById('matches-content');
  if (!wrap) return;
  wrap.innerHTML = '<p class="load">Loading…</p>';
  try {
    const d = await api('GET', '/admin/matches/live');
    if (!d.live.length) { wrap.innerHTML = '<p class="load">No active matches.</p>'; return; }
    const terminateBtn = can('match.terminate');
    const trs = d.live.map(m => `
      <tr>
        <td class="mono text-muted" style="font-size:11px">${esc(m.roomId)}</td>
        <td>${esc(m.code || '—')}</td>
        <td>${statusBadge(m.mode)}</td>
        <td>${m.playerNames.map(n => esc(n)).join(', ')}</td>
        <td>${statusBadge(m.phase)}</td>
        <td>
          ${terminateBtn
            ? `<button class="btn-sm btn-danger" onclick="terminateMatch('${esc(m.roomId)}')">Force End</button>`
            : '—'}
        </td>
      </tr>`).join('');
    wrap.innerHTML = `
      <div class="tbl-wrap"><table>
        <thead><tr><th>Room ID</th><th>Code</th><th>Mode</th><th>Players</th><th>Phase</th><th>Actions</th></tr></thead>
        <tbody>${trs}</tbody>
      </table></div>`;
  } catch (err) {
    wrap.innerHTML = `<p class="load text-danger">Error: ${esc(err.message)}</p>`;
  }
}

async function terminateMatch(roomId) {
  if (!confirm(`Force-end match ${roomId}? This cannot be undone.`)) return;
  try {
    const listD = await api('GET', `/admin/matches?limit=5`);
    const found = listD.matches?.find(m => m.room_id === roomId);
    if (found) {
      await api('POST', `/admin/matches/${found.id}/terminate`);
    } else {
      throw new Error('Match not found in DB (may still be logging). Try again in a moment.');
    }
    toast('Match terminated', 'ok');
    loadLiveMatches();
  } catch (err) { toast(`Terminate failed: ${err.message}`, 'err'); }
}
window.terminateMatch = terminateMatch;

async function loadMatchHistory() {
  const wrap = document.getElementById('matches-content');
  if (!wrap) return;
  wrap.innerHTML = `
    <div style="display:flex;gap:8px;margin-bottom:14px;flex-wrap:wrap">
      <select id="mf-status" onchange="filterMatches()" style="width:140px">
        <option value="">All statuses</option>
        <option value="completed" ${S.matches.status==='completed'?'selected':''}>Completed</option>
        <option value="in_progress" ${S.matches.status==='in_progress'?'selected':''}>In Progress</option>
        <option value="abandoned" ${S.matches.status==='abandoned'?'selected':''}>Abandoned</option>
      </select>
      <select id="mf-mode" onchange="filterMatches()" style="width:140px">
        <option value="">All modes</option>
        <option value="classic" ${S.matches.mode==='classic'?'selected':''}>Classic</option>
        <option value="multilane" ${S.matches.mode==='multilane'?'selected':''}>Multilane</option>
        <option value="2v2_ranked" ${S.matches.mode==='2v2_ranked'?'selected':''}>Ranked</option>
      </select>
    </div>
    <div id="matches-table-wrap"><p class="load">Loading…</p></div>`;
  await fetchMatchHistory();
}

async function filterMatches() {
  S.matches.status = document.getElementById('mf-status')?.value || '';
  S.matches.mode   = document.getElementById('mf-mode')?.value   || '';
  S.matches.offset = 0;
  fetchMatchHistory();
}
window.filterMatches = filterMatches;

async function fetchMatchHistory() {
  const wrap = document.getElementById('matches-table-wrap');
  if (!wrap) return;
  wrap.innerHTML = '<p class="load">Loading…</p>';
  try {
    const params = new URLSearchParams({
      limit: 25, offset: S.matches.offset,
      ...(S.matches.status && { status: S.matches.status }),
      ...(S.matches.mode   && { mode:   S.matches.mode   }),
    });
    const d = await api('GET', `/admin/matches?${params}`);
    S.matches.total = d.total;
    S.matches.rows  = d.matches;

    if (!d.matches.length) { wrap.innerHTML = '<p class="load">No matches found.</p>'; return; }

    const trs = d.matches.map(m => `
      <tr>
        <td><a href="#" onclick="openMatchModal('${m.id}');return false" class="mono" style="font-size:11px">${m.id.slice(0,8)}</a></td>
        <td>${esc(m.mode)}</td>
        <td>${statusBadge(m.status)}</td>
        <td class="text-muted">${m.players?.filter(Boolean).length || 0}</td>
        <td>${m.duration_secs ? dur(m.duration_secs) : '—'}</td>
        <td class="text-muted">${reltime(m.started_at)}</td>
      </tr>`).join('');

    wrap.innerHTML = `
      <div class="tbl-wrap"><table>
        <thead><tr><th>ID</th><th>Mode</th><th>Status</th><th>Players</th><th>Duration</th><th>Started</th></tr></thead>
        <tbody>${trs}</tbody>
      </table></div>
      <div class="pager">
        <span>${S.matches.offset + 1}–${Math.min(S.matches.offset + d.matches.length, d.total)} of ${fmt(d.total)}</span>
        ${S.matches.offset > 0 ? `<button class="btn-sm" onclick="pageMatches(-1)">← Prev</button>` : ''}
        ${S.matches.offset + d.matches.length < d.total ? `<button class="btn-sm" onclick="pageMatches(1)">Next →</button>` : ''}
      </div>`;
  } catch (err) {
    wrap.innerHTML = `<p class="load text-danger">Error: ${esc(err.message)}</p>`;
  }
}

window.pageMatches = function(dir) {
  S.matches.offset = Math.max(0, S.matches.offset + dir * 25);
  fetchMatchHistory();
};

async function openMatchModal(id) {
  openModal('Match Detail', '<p class="load">Loading…</p>');
  try {
    const [d, cl] = await Promise.all([
      api('GET', `/admin/matches/${id}`),
      api('GET', `/admin/matches/${id}/combat-log`).catch(() => ({ events: [] })),
    ]);
    const m = d.match;
    const playerRows = d.players.map(p => `
      <tr>
        <td>${p.lane_index}</td>
        <td><a href="#" onclick="closeModal();openPlayerModal('${p.player_id}');return false">${esc(p.display_name)}</a></td>
        <td>${statusBadge(p.result)}</td>
        <td>${statusBadge(p.player_status)}</td>
      </tr>`).join('');

    const events = cl.events || [];
    let combatHtml = '';
    if (events.length === 0) {
      combatHtml = '<p class="text-muted" style="font-size:13px">No combat log (enable COMBAT_LOG=true on the server to record events).</p>';
    } else {
      const typeIcon = { defender_killed: '🏚️', wave_unit_killed: '💀', leak: '🌊' };
      const rows = events.map(e => `
        <tr>
          <td class="mono" style="font-size:11px">R${e.round} T${e.tick}</td>
          <td>${typeIcon[e.type] || ''} ${esc(e.type)}</td>
          <td>${e.type === 'defender_killed'
            ? `${esc(e.defenderType)} @ (${e.x},${e.y}) by ${esc(e.killedByType)}`
            : e.type === 'wave_unit_killed'
            ? `${esc(e.unitType)} (${esc(e.unitId)})`
            : e.type === 'leak'
            ? `${esc(e.unitType)} (${esc(e.unitId)}) lane ${e.lane}`
            : JSON.stringify(e)}</td>
        </tr>`).join('');
      combatHtml = `
        <div class="tbl-wrap" style="max-height:280px;overflow-y:auto">
          <table>
            <thead><tr><th>Time</th><th>Event</th><th>Detail</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
        </div>`;
    }

    document.getElementById('modal-body').innerHTML = `
      <div class="stat-row">
        <div class="stat-pill"><span>Mode </span><strong>${esc(m.mode)}</strong></div>
        <div class="stat-pill"><span>Status </span><strong>${esc(m.status)}</strong></div>
        <div class="stat-pill"><span>Duration </span><strong>${m.ended_at ? dur((new Date(m.ended_at)-new Date(m.started_at))/1000) : 'ongoing'}</strong></div>
        <div class="stat-pill"><span>Started </span><strong>${new Date(m.started_at).toLocaleString()}</strong></div>
      </div>
      <div class="form-group"><label>Match ID</label><div class="mono" style="font-size:12px">${m.id}</div></div>
      <div class="form-group"><label>Room ID</label><div class="mono" style="font-size:12px">${m.room_id}</div></div>
      <h3 class="mt16">Players</h3>
      <div class="tbl-wrap"><table>
        <thead><tr><th>Lane</th><th>Player</th><th>Result</th><th>Status</th></tr></thead>
        <tbody>${playerRows || '<tr><td colspan="4" class="text-muted">No players logged</td></tr>'}</tbody>
      </table></div>
      <h3 class="mt16">Combat Log <span class="text-muted" style="font-size:12px;font-weight:normal">${events.length} events</span></h3>
      ${combatHtml}`;
    document.getElementById('modal-footer').innerHTML = `<button onclick="closeModal()">Close</button>`;
  } catch (err) {
    document.getElementById('modal-body').innerHTML = `<p class="text-danger">Error: ${esc(err.message)}</p>`;
  }
}
window.openMatchModal = openMatchModal;

// ═══════════════════════════════════════════════════════════════════════
// ANALYTICS
// ═══════════════════════════════════════════════════════════════════════
async function loadAnalytics() {
  setContent('<p class="load">Loading analytics…</p>');
  try {
    const d = await api('GET', '/admin/analytics');
    const s = d.matchStats || {};

    const buckets = d.ratingBuckets || [];
    const maxCnt  = Math.max(1, ...buckets.map(b => parseInt(b.cnt, 10)));
    const chartHtml = buckets.length ? buckets.map(b => {
      const pct = Math.round(parseInt(b.cnt, 10) / maxCnt * 100);
      return `<div class="chart-bar-col">
        <div class="chart-bar" style="height:${pct}%" title="${b.bucket}–${+b.bucket+99}: ${b.cnt} players"></div>
        <div class="chart-lbl">${b.bucket}</div>
      </div>`;
    }).join('') : '<span class="text-muted">No rating data</span>';

    const topHtml = (d.topPlayers || []).map((p, i) => `
      <tr>
        <td>${i + 1}</td>
        <td><a href="#" onclick="openPlayerModal('${p.id}');return false">${esc(p.display_name)}</a></td>
        <td>${Math.round(p.rating)}</td>
        <td>${p.wins} / ${p.losses}</td>
        <td>${p.win_pct != null ? p.win_pct + '%' : '—'}</td>
      </tr>`).join('');

    const daily7 = d.last7d || [];
    const max7   = Math.max(1, ...daily7.map(r => parseInt(r.cnt, 10)));
    const chart7 = daily7.map(row => {
      const pct = Math.round(parseInt(row.cnt, 10) / max7 * 100);
      const lbl = new Date(row.day).toLocaleDateString(undefined, { weekday:'short' });
      return `<div class="chart-bar-col">
        <div class="chart-bar" style="height:${pct}%;background:var(--success)" title="${lbl}: ${row.cnt}"></div>
        <div class="chart-lbl">${lbl}</div>
      </div>`;
    }).join('');

    const modeHtml = (d.modeBreakdown || []).map(m =>
      `<div class="stat-pill"><span>${esc(m.mode)} </span><strong>${fmt(m.cnt)}</strong></div>`
    ).join('');

    setContent(`
      <h2>Analytics</h2>
      <div class="kpi-grid">
        <div class="kpi-card blue"><div class="label">Total Matches</div><div class="value">${fmt(s.total)}</div></div>
        <div class="kpi-card green"><div class="label">Completed</div><div class="value">${fmt(s.completed)}</div></div>
        <div class="kpi-card amber"><div class="label">Abandoned</div><div class="value">${fmt(s.abandoned)}</div></div>
        <div class="kpi-card"><div class="label">Avg Length</div><div class="value">${dur(s.avg_secs)}</div><div class="sub">completed matches</div></div>
        <div class="kpi-card"><div class="label">Last 24h</div><div class="value">${fmt(s.today)}</div></div>
        <div class="kpi-card"><div class="label">Last 7 days</div><div class="value">${fmt(s.last_7d)}</div></div>
      </div>

      <div class="two-col">
        <div class="section">
          <div class="section-header"><h3>Matches per Day (7d)</h3></div>
          <div class="section-body">
            <div class="chart-wrap">${chart7 || '<span class="text-muted">No data</span>'}</div>
          </div>
        </div>
        <div class="section">
          <div class="section-header"><h3>Mode Breakdown</h3></div>
          <div class="section-body">
            <div class="stat-row">${modeHtml || '<span class="text-muted">No data</span>'}</div>
          </div>
        </div>
      </div>

      <div class="section" style="margin-bottom:20px">
        <div class="section-header"><h3>Rating Distribution (Ranked)</h3></div>
        <div class="section-body">
          <div class="chart-wrap">${chartHtml}</div>
          <p class="text-muted" style="font-size:11px;margin-top:4px">Each bar = 100-point rating bucket. Hover for count.</p>
        </div>
      </div>

      <div class="section">
        <div class="section-header"><h3>Top Players (Ranked)</h3></div>
        <div class="section-body">
          <div class="tbl-wrap"><table>
            <thead><tr><th>#</th><th>Player</th><th>Rating</th><th>W/L</th><th>Win%</th></tr></thead>
            <tbody>${topHtml || '<tr><td colspan="5" class="text-muted">No data</td></tr>'}</tbody>
          </table></div>
        </div>
      </div>
    `);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

// ═══════════════════════════════════════════════════════════════════════
// CONFIG (editable, versioned)
// ═══════════════════════════════════════════════════════════════════════
async function loadBranding() {
  setContent('<p class="load">Loading branding...</p>');
  try {
    const data = await api('GET', '/admin/branding');
    applyAdminBranding(data.branding);
    renderBranding();
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

function renderBranding() {
  const branding = currentBranding();
  const canWrite = can('config.write');
  const previewUpdated = branding.updatedAt
    ? `${reltime(branding.updatedAt)} by ${esc(branding.updatedBy || 'unknown')}`
    : 'Using defaults';

  setContent(`
    <h2>Branding ${!canWrite ? '<span class="badge badge-gray">Read-only</span>' : ''}</h2>
    <p class="text-muted" style="font-size:12px;margin-bottom:16px">
      Update the shared copy used by the public lobby, loadout flow, and admin shell. Changes apply immediately after saving.
    </p>

    <div class="section" style="margin-bottom:16px">
      <div class="section-header">
        <h3>Shared Identity</h3>
        ${canWrite ? '<button class="btn-primary btn-sm" onclick="saveBranding()">Save Branding</button>' : ''}
      </div>
      <div class="section-body">
        <div class="two-col">
          <div class="form-group">
            <label>Public Title</label>
            <input id="branding-public-title" value="${esc(branding.publicTitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
          <div class="form-group">
            <label>Public Browser Title</label>
            <input id="branding-public-browser-title" value="${esc(branding.publicBrowserTitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
          <div class="form-group" style="grid-column:1/-1">
            <label>Public Subtitle</label>
            <input id="branding-public-subtitle" value="${esc(branding.publicSubtitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
          <div class="form-group">
            <label>Admin Header Title</label>
            <input id="branding-admin-title" value="${esc(branding.adminTitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
          <div class="form-group">
            <label>Admin Login Title</label>
            <input id="branding-admin-login-title" value="${esc(branding.adminLoginTitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
        </div>
      </div>
    </div>

    <div class="section" style="margin-bottom:16px">
      <div class="section-header"><h3>Loadout Copy</h3></div>
      <div class="section-body">
        <div class="two-col">
          <div class="form-group">
            <label>Loadout Section Title</label>
            <input id="branding-loadout-title" value="${esc(branding.loadoutTitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
          <div class="form-group">
            <label>Loadout Helper Text</label>
            <input id="branding-loadout-hint" value="${esc(branding.loadoutHint)}" ${canWrite ? '' : 'disabled'}>
          </div>
          <div class="form-group" style="grid-column:1/-1">
            <label>Loadout Builder Title</label>
            <input id="branding-loadout-builder-title" value="${esc(branding.loadoutBuilderTitle)}" ${canWrite ? '' : 'disabled'}>
          </div>
        </div>
      </div>
    </div>

    <div class="section">
      <div class="section-header"><h3>Preview</h3></div>
      <div class="section-body">
        <div class="card" style="padding:16px;margin-bottom:12px">
          <div style="font-size:24px;font-weight:700;margin-bottom:4px">${esc(branding.publicTitle)}</div>
          <div class="text-muted" style="margin-bottom:12px">${esc(branding.publicSubtitle)}</div>
          <div style="display:flex;gap:8px;flex-wrap:wrap">
            <span class="badge badge-blue">${esc(branding.loadoutTitle)}</span>
            <span class="badge badge-gray">${esc(branding.loadoutHint)}</span>
            <span class="badge badge-amber">${esc(branding.loadoutBuilderTitle)}</span>
          </div>
        </div>
        <p class="text-muted" style="font-size:12px">Last updated: ${previewUpdated}</p>
      </div>
    </div>
  `);
}

async function saveBranding() {
  const branding = {
    publicTitle: document.getElementById('branding-public-title')?.value || '',
    publicBrowserTitle: document.getElementById('branding-public-browser-title')?.value || '',
    publicSubtitle: document.getElementById('branding-public-subtitle')?.value || '',
    adminTitle: document.getElementById('branding-admin-title')?.value || '',
    adminLoginTitle: document.getElementById('branding-admin-login-title')?.value || '',
    loadoutTitle: document.getElementById('branding-loadout-title')?.value || '',
    loadoutHint: document.getElementById('branding-loadout-hint')?.value || '',
    loadoutBuilderTitle: document.getElementById('branding-loadout-builder-title')?.value || '',
  };
  try {
    const data = await api('PUT', '/admin/branding', { branding });
    applyAdminBranding(data.branding);
    renderBranding();
    toast('Branding saved');
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.saveBranding = saveBranding;

let configData    = null;
let configMode    = 'classic';
let configDraft   = null;
let configHistory = [];

async function loadConfig() {
  setContent('<p class="load">Loading config…</p>');
  try {
    const [cfg, hist] = await Promise.all([
      api('GET', '/admin/game-config'),
      api('GET', `/admin/game-config/history?mode=${configMode}`).catch(() => ({ versions: [] })),
    ]);
    configData    = cfg;
    configHistory = hist.versions || [];
    configDraft   = null;
    renderConfig();
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

function renderConfig() {
  const cfg      = configDraft?.[configMode] || configData?.[configMode];
  const canWrite = can('config.write');
  const isDraft  = !!configDraft;

  if (!cfg) { setContent('<p class="load text-danger">No config data available.</p>'); return; }

  const globalRows = Object.entries(cfg.globalParams || {}).map(([k, v]) => `
    <tr>
      <td class="mono">${esc(k)}</td>
      <td>${canWrite && isDraft
        ? `<input type="number" class="cfg-input" data-section="globalParams" data-key="${esc(k)}" value="${v}" step="any" style="width:120px">`
        : v}
      </td>
    </tr>`).join('');

  const unitCols = ['cost','hp','dmg','atkCdTicks','income','bounty'];
  const unitRows = Object.entries(cfg.unitDefs || {}).map(([name, u]) => `
    <tr>
      <td><strong>${esc(name)}</strong></td>
      ${unitCols.map(f =>
        `<td>${canWrite && isDraft
          ? `<input type="number" class="cfg-input" data-section="unitDefs" data-unit="${esc(name)}" data-key="${f}" value="${u[f] ?? ''}" step="any" style="width:68px">`
          : (u[f] ?? '—')}</td>`
      ).join('')}
      <td>${esc(u.armorType)}</td>
      <td>${esc(u.damageType)}</td>
      <td class="text-muted">${esc(u.special || '—')}</td>
    </tr>`).join('');

  const towerCols = ['cost','range','dmg','atkCdTicks'];
  const towerRows = Object.entries(cfg.towerDefs || {}).map(([name, t]) => `
    <tr>
      <td><strong>${esc(name)}</strong></td>
      ${towerCols.map(f =>
        `<td>${canWrite && isDraft
          ? `<input type="number" class="cfg-input" data-section="towerDefs" data-tower="${esc(name)}" data-key="${f}" value="${t[f] ?? ''}" step="any" style="width:68px">`
          : (t[f] ?? '—')}</td>`
      ).join('')}
      <td>${esc(t.damageType)}</td>
      <td>${t.splash != null ? (t.splash ? 'Yes' : 'No') : '—'}</td>
    </tr>`).join('');

  // Damage vs Armor multiplier matrix
  const dmgMults = cfg.damageMultipliers || {};
  const dmgTypes   = Object.keys(dmgMults);
  const armorTypes = dmgTypes.length ? Object.keys(dmgMults[dmgTypes[0]]) : [];

  function multCell(dmgType, armorType, val) {
    const color = val > 1.0 ? '#1b5e20' : val < 1.0 ? '#b71c1c' : '#1a2430';
    const textColor = val > 1.0 ? '#a5d6a7' : val < 1.0 ? '#ef9a9a' : '#90a4ae';
    if (canWrite && isDraft) {
      return `<td style="padding:2px 4px">
        <input type="number" class="cfg-input" data-section="damageMultipliers"
          data-dmgtype="${esc(dmgType)}" data-armortype="${esc(armorType)}"
          value="${val}" step="0.01" min="0" max="5"
          style="width:58px;background:${color};color:${textColor};border-color:#444;text-align:center">
      </td>`;
    }
    return `<td style="background:${color};color:${textColor};text-align:center;font-size:12px;padding:4px 8px">${val.toFixed(2)}</td>`;
  }

  const dmgMatrixRows = dmgTypes.map(dt => `
    <tr>
      <td><strong>${esc(dt)}</strong></td>
      ${armorTypes.map(at => multCell(dt, at, dmgMults[dt]?.[at] ?? 1)).join('')}
    </tr>`).join('');

  const histHtml = configHistory.slice(0, 8).map(v => `
    <tr>
      <td class="mono text-muted" style="font-size:11px">${v.id.slice(0,8)}</td>
      <td>${esc(v.label || 'v' + v.version)}</td>
      <td>${v.is_active ? '<span class="badge badge-green">active</span>' : ''}</td>
      <td class="text-muted">${reltime(v.published_at)}</td>
      <td class="text-muted">${esc(v.published_by || '—')}</td>
      <td>${canWrite && !v.is_active ? `<button class="btn-sm" onclick="activateConfigVersion('${v.id}')">Rollback</button>` : ''}</td>
    </tr>`).join('');

  document.getElementById('main-content').innerHTML = `
    <h2>Game Config
      ${isDraft ? '<span class="badge badge-amber">Unsaved Draft</span>' : ''}
      ${!canWrite ? '<span class="badge badge-gray">Read-only</span>' : ''}
    </h2>
    <p class="text-muted" style="font-size:12px;margin-bottom:12px">
      Config is applied at server startup. Publish a version then restart the server to activate it.
    </p>

    <div style="display:flex;align-items:center;gap:12px;margin-bottom:16px;flex-wrap:wrap">
      <div class="config-tabs" style="margin:0">
        <button class="config-tab ${configMode==='classic'?'active':''}" onclick="switchConfigMode('classic')">Classic</button>
        <button class="config-tab ${configMode==='multilane'?'active':''}" onclick="switchConfigMode('multilane')">Multilane</button>
      </div>
      ${canWrite ? (isDraft ? `
        <button class="btn-primary btn-sm" onclick="publishConfigDraft()">📤 Publish Draft</button>
        <button class="btn-sm" onclick="discardConfigDraft()">✕ Discard</button>
      ` : `
        <button class="btn-sm" onclick="startConfigEdit()">✏ Edit Config</button>
      `) : ''}
    </div>

    <div class="section" style="margin-bottom:16px">
      <div class="section-header"><h3>Global Parameters</h3></div>
      <div class="section-body">
        <div class="tbl-wrap"><table>
          <thead><tr><th>Parameter</th><th>Value</th></tr></thead>
          <tbody>${globalRows}</tbody>
        </table></div>
      </div>
    </div>

    <div class="section" style="margin-bottom:16px">
      <div class="section-header"><h3>Unit Definitions</h3></div>
      <div class="section-body">
        <div class="tbl-wrap"><table>
          <thead><tr><th>Unit</th><th>Cost</th><th>HP</th><th>DMG</th><th>ATK CD</th><th>Income</th><th>Bounty</th><th>Armor</th><th>Dmg Type</th><th>Special</th></tr></thead>
          <tbody>${unitRows}</tbody>
        </table></div>
      </div>
    </div>

    <div class="section" style="margin-bottom:20px">
      <div class="section-header"><h3>Tower Definitions</h3></div>
      <div class="section-body">
        <div class="tbl-wrap"><table>
          <thead><tr><th>Tower</th><th>Cost</th><th>Range</th><th>DMG</th><th>ATK CD</th><th>Dmg Type</th><th>Splash</th></tr></thead>
          <tbody>${towerRows}</tbody>
        </table></div>
      </div>
    </div>

    <div class="section" style="margin-bottom:16px">
      <div class="section-header"><h3>Damage vs Armor Multipliers</h3></div>
      <div class="section-body" style="padding:0">
        <div class="tbl-wrap">
          ${dmgTypes.length ? `<table>
            <thead><tr>
              <th>Dmg Type ↓ / Armor →</th>
              ${armorTypes.map(a => `<th>${esc(a)}</th>`).join('')}
            </tr></thead>
            <tbody>${dmgMatrixRows}</tbody>
          </table>` : '<p class="text-muted" style="padding:12px">No multiplier data.</p>'}
        </div>
      </div>
    </div>

    <div class="section">
      <div class="section-header"><h3>Version History</h3></div>
      <div class="section-body" style="padding:0">
        <div class="tbl-wrap"><table>
          <thead><tr><th>ID</th><th>Label</th><th>Status</th><th>Published</th><th>By</th><th>Action</th></tr></thead>
          <tbody>${histHtml || '<tr><td colspan="6" class="text-muted" style="padding:16px;text-align:center">No versions published yet</td></tr>'}</tbody>
        </table></div>
      </div>
    </div>
  `;
}

function switchConfigMode(mode) {
  if (configDraft) collectDraftEdits();
  configMode = mode;
  renderConfig();
}
window.switchConfigMode = switchConfigMode;

function startConfigEdit() {
  configDraft = JSON.parse(JSON.stringify(configData));
  renderConfig();
}
window.startConfigEdit = startConfigEdit;

function discardConfigDraft() {
  configDraft = null;
  renderConfig();
}
window.discardConfigDraft = discardConfigDraft;

function collectDraftEdits() {
  if (!configDraft) return;
  document.querySelectorAll('.cfg-input').forEach(inp => {
    const val = parseFloat(inp.value);
    if (isNaN(val)) return;
    const { section, key } = inp.dataset;
    if (section === 'globalParams') {
      configDraft[configMode].globalParams[key] = val;
    } else if (section === 'unitDefs') {
      configDraft[configMode].unitDefs[inp.dataset.unit][key] = val;
    } else if (section === 'towerDefs') {
      configDraft[configMode].towerDefs[inp.dataset.tower][key] = val;
    } else if (section === 'damageMultipliers') {
      const { dmgtype, armortype } = inp.dataset;
      if (!configDraft[configMode].damageMultipliers[dmgtype]) return;
      configDraft[configMode].damageMultipliers[dmgtype][armortype] = val;
    }
  });
}

async function publishConfigDraft() {
  collectDraftEdits();
  const label = prompt('Version label (optional):', `Balance patch ${new Date().toLocaleDateString()}`);
  if (label === null) return;
  const notes = prompt('Notes (optional):', '') || '';
  try {
    await api('POST', '/admin/game-config', {
      mode:   configMode,
      config: configDraft[configMode],
      label:  label || undefined,
      notes:  notes || undefined,
    });
    toast('Config published! Restart server to apply.', 'ok');
    configDraft = null;
    const [cfg, hist] = await Promise.all([
      api('GET', '/admin/game-config'),
      api('GET', `/admin/game-config/history?mode=${configMode}`).catch(() => ({ versions: [] })),
    ]);
    configData    = cfg;
    configHistory = hist.versions || [];
    renderConfig();
  } catch (err) { toast(`Publish failed: ${err.message}`, 'err'); }
}
window.publishConfigDraft = publishConfigDraft;

async function activateConfigVersion(versionId) {
  if (!confirm('Roll back to this config version? Active on next server restart.')) return;
  try {
    await api('POST', `/admin/game-config/${versionId}/activate`);
    toast('Version activated. Restart server to apply.', 'ok');
    const hist = await api('GET', `/admin/game-config/history?mode=${configMode}`).catch(() => ({ versions: [] }));
    configHistory = hist.versions || [];
    renderConfig();
  } catch (err) { toast(`Rollback failed: ${err.message}`, 'err'); }
}
window.activateConfigVersion = activateConfigVersion;

// ═══════════════════════════════════════════════════════════════════════
// OPS (Feature flags + Seasons)
// ═══════════════════════════════════════════════════════════════════════
async function loadOps() {
  setContent('<p class="load">Loading…</p>');
  try {
    const [flagsD, seasonsD] = await Promise.all([
      api('GET', '/admin/flags'),
      api('GET', '/admin/seasons').catch(() => ({ seasons: [] })),
    ]);
    renderOps(flagsD.flags, seasonsD.seasons);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

function renderOps(flags, seasons) {
  const canFlags   = can('flag.write');
  const canSeasons = can('season.write');

  const flagRows = (flags || []).map(f => `
    <div class="flag-row">
      <span class="flag-name">${esc(f.name)}</span>
      <span class="flag-notes">${esc(f.notes || '')}</span>
      ${canFlags ? `
        <label class="toggle">
          <input type="checkbox" ${f.enabled ? 'checked' : ''}
            onchange="setFlag('${esc(f.name)}',this.checked)">
          <span class="slider"></span>
        </label>` : ''}
      <span class="badge ${f.enabled ? 'badge-green' : 'badge-gray'}" style="min-width:40px">
        ${f.enabled ? 'ON' : 'OFF'}
      </span>
    </div>`).join('');

  const activeSeason = seasons.find(s => s.is_active);
  const seasonRows = (seasons || []).map(s => `
    <tr>
      <td>${esc(s.name)}</td>
      <td>${statusBadge(s.is_active ? 'active' : 'closed')}</td>
      <td class="text-muted">${new Date(s.start_date).toLocaleDateString()}</td>
      <td class="text-muted">${s.end_date ? new Date(s.end_date).toLocaleDateString() : '—'}</td>
      <td>
        ${canSeasons && s.is_active
          ? `<button class="btn-sm btn-danger" onclick="closeSeason('${s.id}','${esc(s.name)}')">Close Season</button>`
          : ''}
      </td>
    </tr>`).join('');

  document.getElementById('main-content').innerHTML = `
    <h2>Operations</h2>

    <div class="section" style="margin-bottom:20px">
      <div class="section-header">
        <h3>Feature Flags</h3>
        ${!canFlags ? '<span class="badge badge-gray">Read-only</span>' : ''}
      </div>
      <div class="section-body">
        ${flagRows || '<p class="text-muted">No flags configured</p>'}
      </div>
    </div>

    <div class="section">
      <div class="section-header">
        <h3>Season Management</h3>
        ${canSeasons && !activeSeason
          ? `<button class="btn-success btn-sm" onclick="createSeason()">+ New Season</button>`
          : (activeSeason ? `<span class="badge badge-green">Active: ${esc(activeSeason.name)}</span>` : '')}
      </div>
      <div class="section-body">
        <div class="tbl-wrap"><table>
          <thead><tr><th>Name</th><th>Status</th><th>Start</th><th>End</th><th>Actions</th></tr></thead>
          <tbody>${seasonRows || '<tr><td colspan="5" class="text-muted">No seasons</td></tr>'}</tbody>
        </table></div>
      </div>
    </div>
  `;
}

async function setFlag(name, enabled) {
  try {
    await api('PATCH', `/admin/flags/${name}`, { enabled });
    toast(`${name} → ${enabled ? 'ON' : 'OFF'}`, 'ok');
    const row = document.querySelector(`.flag-row input[onchange*="${name}"]`)?.closest('.flag-row');
    if (row) {
      const badge = row.querySelector('.badge');
      if (badge) { badge.textContent = enabled ? 'ON' : 'OFF'; badge.className = `badge ${enabled ? 'badge-green' : 'badge-gray'}`; }
    }
  } catch (err) { toast(`Flag update failed: ${err.message}`, 'err'); loadOps(); }
}
window.setFlag = setFlag;

async function createSeason() {
  const name = prompt('Season name?', `Season ${new Date().getFullYear()}`);
  if (!name) return;
  try {
    await api('POST', '/admin/seasons', { name });
    toast(`Season "${name}" created`, 'ok');
    loadOps();
  } catch (err) { toast(`Create failed: ${err.message}`, 'err'); }
}
window.createSeason = createSeason;

async function closeSeason(id, name) {
  if (!confirm(`Close season "${name}"? This will snapshot ratings and apply a soft reset.`)) return;
  try {
    const d = await api('POST', `/admin/seasons/${id}/close`);
    toast(`Season closed. ${d.snapshotCount} snapshots, ${d.resetCount} resets.`, 'ok');
    loadOps();
  } catch (err) { toast(`Close failed: ${err.message}`, 'err'); }
}
window.closeSeason = closeSeason;

// ═══════════════════════════════════════════════════════════════════════
// AUDIT LOG
// ═══════════════════════════════════════════════════════════════════════
async function loadAudit() {
  setContent(`
    <h2>Audit Log</h2>
    <div id="audit-wrap"><p class="load">Loading…</p></div>
  `);
  await fetchAuditLog();
}

async function fetchAuditLog() {
  const wrap = document.getElementById('audit-wrap');
  if (!wrap) return;
  wrap.innerHTML = '<p class="load">Loading…</p>';
  try {
    const d = await api('GET', `/admin/audit-log?limit=50&offset=${S.audit.offset}`);
    S.audit.total = d.total;
    S.audit.rows  = d.entries;

    if (!d.entries.length && S.audit.offset === 0) {
      wrap.innerHTML = '<p class="load">No audit entries yet.</p>';
      return;
    }

    const trs = (d.entries || []).map(e => `
      <tr>
        <td class="text-muted" style="font-size:11px;white-space:nowrap">${new Date(e.created_at).toLocaleString()}</td>
        <td class="text-muted" style="font-size:12px">${esc(e.admin_email || '—')}</td>
        <td><span class="badge badge-blue">${esc(e.action)}</span></td>
        <td class="text-muted">${esc(e.target_type || '—')}</td>
        <td class="mono text-muted" style="font-size:11px">${esc(String(e.target_id || '—').slice(0,8))}</td>
        <td class="text-muted" style="font-size:11px">${esc(e.admin_ip || '—')}</td>
        <td>${e.payload
          ? `<button class="btn-sm" onclick="showAuditPayload(${esc(JSON.stringify(JSON.stringify(e.payload)))})">Details</button>`
          : '—'}</td>
      </tr>`).join('');

    wrap.innerHTML = `
      <div class="tbl-wrap"><table>
        <thead><tr>
          <th>Time</th><th>Admin</th><th>Action</th><th>Target Type</th><th>Target ID</th><th>IP</th><th>Payload</th>
        </tr></thead>
        <tbody>${trs}</tbody>
      </table></div>
      <div class="pager">
        <span>${S.audit.offset + 1}–${Math.min(S.audit.offset + d.entries.length, d.total)} of ${fmt(d.total)}</span>
        ${S.audit.offset > 0 ? `<button class="btn-sm" onclick="pageAudit(-1)">← Prev</button>` : ''}
        ${S.audit.offset + d.entries.length < d.total ? `<button class="btn-sm" onclick="pageAudit(1)">Next →</button>` : ''}
      </div>`;
  } catch (err) {
    wrap.innerHTML = `<p class="load text-danger">Error: ${esc(err.message)}</p>`;
  }
}

window.pageAudit = function(dir) {
  S.audit.offset = Math.max(0, S.audit.offset + dir * 50);
  fetchAuditLog();
};

window.showAuditPayload = function(jsonStr) {
  let display;
  try { display = JSON.stringify(JSON.parse(jsonStr), null, 2); } catch { display = jsonStr; }
  openModal('Audit Payload',
    `<pre style="white-space:pre-wrap;font-size:12px;font-family:monospace;color:var(--text)">${esc(display)}</pre>`,
    `<button onclick="closeModal()">Close</button>`);
};

// ═══════════════════════════════════════════════════════════════════════
// ADMIN TEAM
// ═══════════════════════════════════════════════════════════════════════
async function loadTeam() {
  if (!can('admin.write')) {
    setContent('<p class="load text-muted">Access denied. Engineer or Owner role required.</p>');
    return;
  }
  setContent('<p class="load">Loading admin team…</p>');
  try {
    const d     = await api('GET', '/admin/users');
    const users = d.users || [];

    const trs = users.map(u => `
      <tr>
        <td><strong>${esc(u.display_name)}</strong></td>
        <td class="text-muted">${esc(u.email)}</td>
        <td>${statusBadge(u.role)}</td>
        <td>${statusBadge(u.active ? 'active' : 'suspended')}</td>
        <td class="text-muted">${reltime(u.last_login)}</td>
        <td class="text-muted">${reltime(u.created_at)}</td>
        <td>
          <button class="btn-sm" onclick="editAdminUser('${esc(u.id)}','${esc(u.email)}','${esc(u.display_name)}','${esc(u.role)}',${!!u.active})">Edit</button>
        </td>
      </tr>`).join('');

    document.getElementById('main-content').innerHTML = `
      <h2>Admin Team</h2>
      <p class="text-muted" style="font-size:12px;margin-bottom:16px">
        Manage admin users and roles. Use <code>POST /admin/setup</code> with your ADMIN_SECRET to bootstrap the first owner.
      </p>
      <div class="section" style="margin-bottom:20px">
        <div class="section-header">
          <h3>Admin Users (${users.length})</h3>
          <button class="btn-success btn-sm" onclick="createAdminUserModal()">+ Add Admin</button>
        </div>
        <div class="section-body" style="padding:0">
          <div class="tbl-wrap"><table>
            <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Last Login</th><th>Created</th><th>Actions</th></tr></thead>
            <tbody>${trs || '<tr><td colspan="7" class="text-muted" style="padding:16px;text-align:center">No admin users yet</td></tr>'}</tbody>
          </table></div>
        </div>
      </div>

      <div class="section">
        <div class="section-header"><h3>Role Permissions</h3></div>
        <div class="section-body" style="padding:0">
          <table>
            <thead><tr><th>Role</th><th>Permissions</th></tr></thead>
            <tbody>
              <tr><td>${statusBadge('viewer')}</td><td class="text-muted">Read-only access to all data</td></tr>
              <tr><td>${statusBadge('support')}</td><td class="text-muted">+ Ban/unban players, revoke tokens</td></tr>
              <tr><td>${statusBadge('moderator')}</td><td class="text-muted">+ Terminate matches, toggle feature flags</td></tr>
              <tr><td>${statusBadge('editor')}</td><td class="text-muted">+ Publish game config, manage seasons, adjust ratings</td></tr>
              <tr><td>${statusBadge('engineer')}</td><td class="text-muted">+ Create and modify admin users</td></tr>
              <tr><td>${statusBadge('owner')}</td><td class="text-muted">Full access (all permissions)</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    `;
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

function createAdminUserModal() {
  openModal('Create Admin User', `
    <div class="form-group">
      <label>Display Name</label>
      <input type="text" id="new-admin-name" placeholder="Jane Smith">
    </div>
    <div class="form-group">
      <label>Email</label>
      <input type="email" id="new-admin-email" placeholder="jane@example.com">
    </div>
    <div class="form-group">
      <label>Password</label>
      <input type="password" id="new-admin-pass" placeholder="Minimum 8 characters">
    </div>
    <div class="form-group">
      <label>Role</label>
      <select id="new-admin-role">
        <option value="viewer">viewer</option>
        <option value="support">support</option>
        <option value="moderator">moderator</option>
        <option value="editor">editor</option>
        <option value="engineer">engineer</option>
        <option value="owner">owner</option>
      </select>
    </div>
  `, `
    <button class="btn-primary" onclick="submitCreateAdmin()">Create</button>
    <button onclick="closeModal()">Cancel</button>
  `);
}
window.createAdminUserModal = createAdminUserModal;

async function submitCreateAdmin() {
  const name  = document.getElementById('new-admin-name')?.value.trim();
  const email = document.getElementById('new-admin-email')?.value.trim();
  const pass  = document.getElementById('new-admin-pass')?.value;
  const role  = document.getElementById('new-admin-role')?.value;
  if (!name || !email || !pass) { toast('Fill all fields', 'err'); return; }
  if (pass.length < 8) { toast('Password must be at least 8 characters', 'err'); return; }
  try {
    await api('POST', '/admin/users', { displayName: name, email, password: pass, role });
    toast(`Admin "${name}" created`, 'ok');
    closeModal();
    loadTeam();
  } catch (err) { toast(`Create failed: ${err.message}`, 'err'); }
}
window.submitCreateAdmin = submitCreateAdmin;

function editAdminUser(id, email, displayName, role, active) {
  openModal(`Edit Admin: ${displayName}`, `
    <div class="form-group">
      <label>Email</label>
      <div class="text-muted" style="padding:8px 0">${esc(email)}</div>
    </div>
    <div class="form-group">
      <label>Role</label>
      <select id="edit-admin-role">
        ${['viewer','support','moderator','editor','engineer','owner'].map(r =>
          `<option value="${r}" ${r === role ? 'selected' : ''}>${r}</option>`
        ).join('')}
      </select>
    </div>
    <div class="form-group">
      <label>Status</label>
      <select id="edit-admin-active">
        <option value="true" ${active ? 'selected' : ''}>Active</option>
        <option value="false" ${!active ? 'selected' : ''}>Suspended</option>
      </select>
    </div>
    <div class="form-group">
      <label>New Password <span class="text-muted">(leave blank to keep current)</span></label>
      <input type="password" id="edit-admin-pass" placeholder="Min 8 characters, or leave blank">
    </div>
  `, `
    <button class="btn-primary" onclick="submitEditAdmin('${id}')">Save</button>
    <button onclick="closeModal()">Cancel</button>
  `);
}
window.editAdminUser = editAdminUser;

async function submitEditAdmin(id) {
  const role    = document.getElementById('edit-admin-role')?.value;
  const active  = document.getElementById('edit-admin-active')?.value === 'true';
  const pass    = document.getElementById('edit-admin-pass')?.value;
  const payload = { role, active };
  if (pass) {
    if (pass.length < 8) { toast('Password must be at least 8 characters', 'err'); return; }
    payload.password = pass;
  }
  try {
    await api('PATCH', `/admin/users/${id}`, payload);
    toast('Admin user updated', 'ok');
    closeModal();
    loadTeam();
  } catch (err) { toast(`Update failed: ${err.message}`, 'err'); }
}
window.submitEditAdmin = submitEditAdmin;

// ═══════════════════════════════════════════════════════════════════════
// MY ACCOUNT (self-management: password, 2FA, linked auth)
// ═══════════════════════════════════════════════════════════════════════
async function myAccountModal() {
  let me;
  try { me = (await api('GET', '/admin/me')).user; }
  catch (err) { toast(`Load failed: ${err.message}`, 'err'); return; }

  const has2fa   = !!me.totp_enabled;
  const hasGoogle = !!me.has_google;

  openModal('My Account', `
    <div class="section-header" style="margin-bottom:12px">
      <div>
        <strong>${esc(me.display_name)}</strong>
        <span class="badge badge-blue" style="font-size:10px;margin-left:6px">${esc(me.role)}</span>
      </div>
      <div class="text-muted" style="font-size:12px">${esc(me.email)}</div>
    </div>

    <div class="form-group" style="border-top:1px solid #333;padding-top:16px">
      <label style="font-weight:600">Change Password</label>
      <input type="password" id="me-cur-pass" placeholder="Current password (leave blank if none set)" style="margin-bottom:6px">
      <input type="password" id="me-new-pass" placeholder="New password (min 8 chars)">
    </div>
    <div style="margin-bottom:20px">
      <button class="btn-primary btn-sm" onclick="submitMyPassword()">Update Password</button>
    </div>

    <div style="border-top:1px solid #333;padding-top:16px">
      <label style="font-weight:600;display:block;margin-bottom:8px">Two-Factor Authentication</label>
      ${has2fa
        ? `<p class="text-muted" style="font-size:12px;margin-bottom:8px">2FA is <strong style="color:#4caf50">enabled</strong>. Enter your current code to disable it.</p>
           <input type="text" id="me-2fa-disable-code" placeholder="6-digit code" maxlength="6" inputmode="numeric" style="margin-bottom:6px">
           <button class="btn-sm" style="background:#c62828" onclick="submitDisable2fa()">Disable 2FA</button>`
        : `<p class="text-muted" style="font-size:12px;margin-bottom:8px">2FA is <strong style="color:#888">not enabled</strong>.</p>
           <button class="btn-sm btn-primary" onclick="setup2faFlow()">Set up 2FA</button>`
      }
    </div>

    <div style="border-top:1px solid #333;padding-top:16px;margin-top:16px">
      <label style="font-weight:600;display:block;margin-bottom:4px">Linked Auth</label>
      <p class="text-muted" style="font-size:12px">
        Password login: ${me.password_hash !== undefined ? 'set' : (hasGoogle ? 'none (SSO-only)' : 'set')}&nbsp;&nbsp;
        Google SSO: ${hasGoogle ? '<span style="color:#4caf50">linked</span>' : '<span style="color:#888">not linked</span>'}
      </p>
    </div>
  `, '');
}
window.myAccountModal = myAccountModal;

async function submitMyPassword() {
  const cur  = document.getElementById('me-cur-pass')?.value;
  const next = document.getElementById('me-new-pass')?.value;
  if (!next || next.length < 8) { toast('New password must be at least 8 chars', 'err'); return; }
  try {
    await api('POST', '/admin/me/password', { currentPassword: cur || undefined, newPassword: next });
    toast('Password updated', 'ok');
    document.getElementById('me-cur-pass').value = '';
    document.getElementById('me-new-pass').value = '';
  } catch (err) { toast(`Failed: ${err.message}`, 'err'); }
}
window.submitMyPassword = submitMyPassword;

async function setup2faFlow() {
  let data;
  try { data = await api('POST', '/admin/me/2fa/setup'); }
  catch (err) { toast(`Setup failed: ${err.message}`, 'err'); return; }

  openModal('Set up 2FA', `
    <p style="font-size:13px;margin-bottom:12px">
      Scan this QR code with your authenticator app (Google Authenticator, Authy, etc.), then enter the 6-digit code to confirm.
    </p>
    <div style="text-align:center;margin-bottom:12px">
      <img src="${data.qrCodeDataUrl}" alt="QR Code" style="max-width:200px;border-radius:4px">
    </div>
    <p class="text-muted" style="font-size:11px;text-align:center;margin-bottom:12px">
      Manual key: <code>${esc(data.secret)}</code>
    </p>
    <div class="form-group">
      <label>Confirmation Code</label>
      <input type="text" id="me-2fa-enable-code" placeholder="000000" maxlength="6" inputmode="numeric"
        onkeydown="if(event.key==='Enter')submitEnable2fa()">
    </div>
  `, `
    <button class="btn-primary" onclick="submitEnable2fa()">Enable 2FA</button>
    <button onclick="closeModal()">Cancel</button>
  `);
}
window.setup2faFlow = setup2faFlow;

async function submitEnable2fa() {
  const code = document.getElementById('me-2fa-enable-code')?.value.trim();
  if (!code) { toast('Enter the 6-digit code', 'err'); return; }
  try {
    await api('POST', '/admin/me/2fa/enable', { code });
    toast('2FA enabled — you will need your authenticator on next login', 'ok');
    closeModal();
  } catch (err) { toast(`Failed: ${err.message}`, 'err'); }
}
window.submitEnable2fa = submitEnable2fa;

async function submitDisable2fa() {
  const code = document.getElementById('me-2fa-disable-code')?.value.trim();
  if (!code) { toast('Enter your current 2FA code', 'err'); return; }
  try {
    await api('POST', '/admin/me/2fa/disable', { code });
    toast('2FA disabled', 'ok');
    closeModal();
  } catch (err) { toast(`Failed: ${err.message}`, 'err'); }
}
window.submitDisable2fa = submitDisable2fa;

// ═══════════════════════════════════════════════════════════════════════
// TOWERS
// ═══════════════════════════════════════════════════════════════════════

const TOWER_CATEGORIES = ['damage','slow_control','aura_support','economy','special'];
const DAMAGE_TYPES     = ['NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE'];
const TARGETING_OPTS   = ['first','last','strongest','weakest','closest'];

const CATEGORY_LABELS = {
  damage: 'Damage', slow_control: 'Slow / Control',
  aura_support: 'Aura / Support', economy: 'Economy', special: 'Special',
};
const TOWER_KANBAN_TYPES = ['NORMAL', 'SIEGE', 'SPLASH', 'PIERCE', 'MAGIC'];
const DTYPE_COLORS = {
  NORMAL: '#aaa', PIERCE: '#4fc3f7', SPLASH: '#ff7043',
  SIEGE:  '#ab47bc', MAGIC: '#7e57c2', PHYSICAL: '#ff8a65', TRUE: '#ef5350',
};

async function loadTowers() {
  setContent('<p class="load">Loading mobs…</p>');
  try {
    const d = await api('GET', '/admin/towers');
    const towers = d.towers || [];
    renderTowerList(towers);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}

function renderTowerList(towers) {
  const canEdit = can('config.write');
  const byType = new Map(TOWER_KANBAN_TYPES.map((k) => [k, []]));
  const other = [];
  for (const t of towers) {
    const key = String(t.damage_type || '').toUpperCase();
    if (byType.has(key)) byType.get(key).push(t);
    else other.push(t);
  }
  const columns = TOWER_KANBAN_TYPES.map((dtype) => {
    const list = byType.get(dtype) || [];
    const cards = list.map((t) => towerCard(t, canEdit)).join('');
    return `
      <section class="tower-kanban-col">
        <header class="tower-kanban-col-head">
          <span class="tower-dtype-badge" style="background:${(DTYPE_COLORS[dtype] || '#aaa')}22;color:${DTYPE_COLORS[dtype] || '#aaa'};border:1px solid ${(DTYPE_COLORS[dtype] || '#aaa')}44">
            ${dtype}
          </span>
          <span class="text-muted" style="font-size:11px">${list.length}</span>
        </header>
        <div class="tower-kanban-col-body">
          ${cards || '<div class="text-muted" style="font-size:12px">No towers</div>'}
        </div>
      </section>
    `;
  }).join('');
  const otherCol = other.length
    ? `<section class="tower-kanban-col">
         <header class="tower-kanban-col-head">
           <span class="tower-dtype-badge" style="background:#8882;color:#bbb;border:1px solid #6664">OTHER</span>
           <span class="text-muted" style="font-size:11px">${other.length}</span>
         </header>
         <div class="tower-kanban-col-body">${other.map((t) => towerCard(t, canEdit)).join('')}</div>
       </section>`
    : '';
  setContent(`
    <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:20px">
      <div>
        <h2 style="margin:0">Mobs</h2>
        <p class="text-muted" style="font-size:12px;margin:4px 0 0">
          ${towers.length} mob${towers.length !== 1 ? 's' : ''} — data-driven, no deployment required
        </p>
      </div>
      ${canEdit ? '<button class="btn-success" onclick="openCreateTowerModal()">+ New Mob</button>' : ''}
    </div>

    ${towers.length === 0
      ? '<div class="section"><div class="section-body" style="padding:32px;text-align:center;color:var(--muted)">No mobs yet. Click <strong>+ New Mob</strong> to create one.</div></div>'
      : `<div class="tower-kanban">${columns}${otherCol}</div>`}
  `);
}

function towerCard(t, canEdit) {
  const icon = t.icon_url
    ? `<img src="${esc(t.icon_url)}" alt="${esc(t.name)}" class="tower-card-icon">`
    : `<div class="tower-card-icon-placeholder">${esc(t.name[0] || '?')}</div>`;

  const dtColor = DTYPE_COLORS[t.damage_type] || '#aaa';
  const abilities = (t.abilities || []);
  const abilityBadges = abilities.map(a =>
    `<span class="ability-badge">${esc(a.ability_key.replace(/_/g,' '))}</span>`
  ).join('') || '<span class="text-muted" style="font-size:11px">No abilities</span>';

  return `
    <div class="tower-card ${t.enabled ? '' : 'tower-card--disabled'}" onclick="openTowerDetailModal(${t.id})">
      ${t.sprite_url || t.icon_url ? `<img src="${esc(t.sprite_url || t.icon_url)}" alt="${esc(t.name)}" class="tower-card-preview">` : ''}
      <div class="tower-card-header">
        ${icon}
        <div class="tower-card-title">
          <strong>${esc(t.name)}</strong>
          <span class="badge ${t.enabled ? 'badge-green' : 'badge-gray'}" style="font-size:10px">
            ${t.enabled ? 'Enabled' : 'Disabled'}
          </span>
        </div>
        <span class="tower-dtype-badge" style="background:${dtColor}22;color:${dtColor};border:1px solid ${dtColor}44">
          ${esc(t.damage_type)}
        </span>
      </div>

      <p class="tower-card-desc text-muted">${esc(t.description || '—')}</p>

      <div class="tower-stat-grid">
        <div class="tower-stat"><span class="tower-stat-label">Damage</span><span class="tower-stat-val">${(+t.attack_damage).toFixed(1)}</span></div>
        <div class="tower-stat"><span class="tower-stat-label">Atk Speed</span><span class="tower-stat-val">${(+t.attack_speed).toFixed(2)}/s</span></div>
        <div class="tower-stat"><span class="tower-stat-label">Range</span><span class="tower-stat-val">${(+t.range).toFixed(3)}</span></div>
        <div class="tower-stat"><span class="tower-stat-label">Cost</span><span class="tower-stat-val">${t.base_cost}g</span></div>
        ${t.splash_radius ? `<div class="tower-stat"><span class="tower-stat-label">Splash R</span><span class="tower-stat-val">${(+t.splash_radius).toFixed(3)}</span></div>` : ''}
        <div class="tower-stat"><span class="tower-stat-label">Category</span><span class="tower-stat-val">${esc(CATEGORY_LABELS[t.category] || t.category)}</span></div>
      </div>

      <div class="tower-card-abilities">${abilityBadges}</div>

      <div class="tower-card-actions">
        ${canEdit ? `
          <button class="btn-sm btn-primary" onclick="event.stopPropagation();openEditTowerModal(${t.id})">Edit</button>
          <button class="btn-sm" onclick="event.stopPropagation();openAbilityModal(${t.id},'${esc(t.name)}')">Abilities</button>
          <button class="btn-sm" onclick="event.stopPropagation();toggleTower(${t.id},${!t.enabled})" style="${t.enabled ? 'background:#c62828' : ''}">
            ${t.enabled ? 'Disable' : 'Enable'}
          </button>
          <button class="btn-sm" style="background:#b71c1c" onclick="event.stopPropagation();deleteTower(${t.id},'${esc(t.name)}')">Delete</button>
        ` : ''}
      </div>
    </div>`;
}

async function openTowerDetailModal(id) {
  try {
    const d = await api('GET', `/admin/towers/${id}`);
    const t = d.tower;
    const canEdit = can('config.write');
    const abilities = (t.abilities || [])
      .map(a => `<span class="ability-badge">${esc(a.ability_key.replace(/_/g, ' '))}</span>`)
      .join('') || '<span class="text-muted" style="font-size:12px">No abilities</span>';
    const icon = t.icon_url
      ? `<img src="${esc(t.icon_url)}" alt="${esc(t.name)}" class="tower-card-icon">`
      : `<div class="tower-card-icon-placeholder">${esc(t.name[0] || '?')}</div>`;
    openModal(
      `Mob Details: ${t.name}`,
      `
        <div style="display:flex;align-items:center;gap:10px;margin-bottom:10px">
          ${icon}
          <div>
            <div><strong>${esc(t.name)}</strong></div>
            <div class="text-muted" style="font-size:12px">${esc(t.description || '—')}</div>
          </div>
        </div>
        ${t.sprite_url || t.icon_url ? `<div style="margin-bottom:10px"><img src="${esc(t.sprite_url || t.icon_url)}" alt="${esc(t.name)}" style="width:100%;max-height:180px;object-fit:contain;background:#111;border:1px solid var(--border);border-radius:6px"></div>` : ''}
        <div class="tower-stat-grid" style="margin-bottom:10px">
          <div class="tower-stat"><span class="tower-stat-label">Type</span><span class="tower-stat-val">${esc(t.damage_type)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Category</span><span class="tower-stat-val">${esc(CATEGORY_LABELS[t.category] || t.category)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Status</span><span class="tower-stat-val">${t.enabled ? 'Enabled' : 'Disabled'}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Damage</span><span class="tower-stat-val">${(+t.attack_damage).toFixed(1)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Atk Speed</span><span class="tower-stat-val">${(+t.attack_speed).toFixed(2)}/s</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Range</span><span class="tower-stat-val">${(+t.range).toFixed(3)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Cost</span><span class="tower-stat-val">${t.base_cost}g</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Projectile Speed</span><span class="tower-stat-val">${t.projectile_speed == null ? '—' : (+t.projectile_speed).toFixed(3)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Splash Radius</span><span class="tower-stat-val">${t.splash_radius == null ? '—' : (+t.splash_radius).toFixed(3)}</span></div>
        </div>
        <div class="tower-stat-grid" style="margin-bottom:10px">
          <div class="tower-stat"><span class="tower-stat-label">Upgrade Cost Mult</span><span class="tower-stat-val">${(+t.upgrade_cost_mult).toFixed(2)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Damage Scaling</span><span class="tower-stat-val">${(+t.damage_scaling).toFixed(3)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Range Scaling</span><span class="tower-stat-val">${(+t.range_scaling).toFixed(3)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Atk Speed Scaling</span><span class="tower-stat-val">${(+t.attack_speed_scaling).toFixed(3)}</span></div>
          <div class="tower-stat"><span class="tower-stat-label">Targeting</span><span class="tower-stat-val">${esc(t.targeting_options || 'first')}</span></div>
        </div>
        <div style="margin:8px 0 10px">
          <div class="text-muted" style="font-size:11px">Assets</div>
          <div style="font-size:12px;line-height:1.5">
            <div>Icon: ${t.icon_url ? esc(t.icon_url) : '—'}</div>
            <div>Sprite: ${t.sprite_url ? esc(t.sprite_url) : '—'}</div>
            <div>Projectile: ${t.projectile_url ? esc(t.projectile_url) : '—'}</div>
            <div>Animation: ${t.animation_url ? esc(t.animation_url) : '—'}</div>
          </div>
        </div>
        <div>${abilities}</div>
      `,
      `
        ${canEdit ? `<button class="btn-primary" onclick="closeModal();openEditTowerModal(${t.id})">Edit Mob</button>` : ''}
        <button onclick="closeModal()">Close</button>
      `
    );
  } catch (err) {
    toast(`Load failed: ${err.message}`, 'err');
  }
}
window.openTowerDetailModal = openTowerDetailModal;

// ── Tower create/edit modal ──────────────────────────────────────────────────

function towerFormHtml(t) {
  const targeting = (t.targeting_options || 'first').split(',').map(s => s.trim());
  return `
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
      <div class="form-group" style="grid-column:1/-1">
        <label>Mob Name *</label>
        <input type="text" id="tf-name" value="${esc(t.name || '')}" placeholder="e.g. Ice Archer">
      </div>
      <div class="form-group" style="grid-column:1/-1">
        <label>Description</label>
        <textarea id="tf-desc" rows="2" style="resize:vertical">${esc(t.description || '')}</textarea>
      </div>

      <div class="form-group">
        <label>Category</label>
        <select id="tf-category">
          ${TOWER_CATEGORIES.map(c =>
            `<option value="${c}" ${(t.category||'damage')===c?'selected':''}>${esc(CATEGORY_LABELS[c])}</option>`
          ).join('')}
        </select>
      </div>
      <div class="form-group">
        <label>Damage Type</label>
        <select id="tf-dtype">
          ${DAMAGE_TYPES.map(d =>
            `<option value="${d}" ${(t.damage_type||'NORMAL')===d?'selected':''}>${d}</option>`
          ).join('')}
        </select>
      </div>

      <div class="form-group">
        <label>Attack Damage *</label>
        <input type="number" id="tf-dmg" value="${t.attack_damage||10}" step="0.1" min="0.1">
      </div>
      <div class="form-group">
        <label>Attack Speed (attacks/s)</label>
        <input type="number" id="tf-aspd" value="${t.attack_speed||1}" step="0.05" min="0.01">
      </div>
      <div class="form-group">
        <label>Range *</label>
        <input type="number" id="tf-range" value="${t.range||0.35}" step="0.01" min="0.01">
      </div>
      <div class="form-group">
        <label>Base Cost (gold) *</label>
        <input type="number" id="tf-cost" value="${t.base_cost||10}" step="1" min="1">
      </div>
      <div class="form-group">
        <label>Splash Radius <span class="text-muted">(blank = none)</span></label>
        <input type="number" id="tf-splash" value="${t.splash_radius||''}" step="0.01" min="0" placeholder="e.g. 0.08">
      </div>
      <div class="form-group">
        <label>Projectile Speed <span class="text-muted">(blank = instant)</span></label>
        <input type="number" id="tf-pspeed" value="${t.projectile_speed||''}" step="0.001" min="0" placeholder="e.g. 0.02">
      </div>
    </div>

    <details style="margin:12px 0">
      <summary style="cursor:pointer;font-size:12px;color:var(--muted);margin-bottom:8px">Upgrade Scaling</summary>
      <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;padding:8px 0">
        <div class="form-group">
          <label style="font-size:11px">Upgrade Cost Mult</label>
          <input type="number" id="tf-ucm" value="${t.upgrade_cost_mult||1}" step="0.05" min="0.1">
        </div>
        <div class="form-group">
          <label style="font-size:11px">Damage Scaling/lvl</label>
          <input type="number" id="tf-dscale" value="${t.damage_scaling||0.12}" step="0.01" min="0">
        </div>
        <div class="form-group">
          <label style="font-size:11px">Range Scaling/lvl</label>
          <input type="number" id="tf-rscale" value="${t.range_scaling||0.015}" step="0.001" min="0">
        </div>
        <div class="form-group">
          <label style="font-size:11px">Atk Speed Scaling/lvl</label>
          <input type="number" id="tf-ascale" value="${t.attack_speed_scaling||0.015}" step="0.001" min="0">
        </div>
      </div>
    </details>

    <details style="margin:12px 0">
      <summary style="cursor:pointer;font-size:12px;color:var(--muted);margin-bottom:8px">Targeting Options</summary>
      <div style="display:flex;gap:12px;flex-wrap:wrap;padding:8px 0">
        ${TARGETING_OPTS.map(o => `
          <label style="display:flex;align-items:center;gap:6px;font-size:13px;cursor:pointer">
            <input type="checkbox" class="tf-target" value="${o}" ${targeting.includes(o)?'checked':''}> ${o}
          </label>`).join('')}
      </div>
    </details>    <details style="margin:12px 0">
      <summary style="cursor:pointer;font-size:12px;color:var(--muted);margin-bottom:8px">Visual Assets (File Upload)</summary>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;padding:8px 0">
        <div class="form-group">
          <label style="font-size:11px">Icon Image</label>
          <input type="file" id="tf-icon-file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml">
          <input type="text" id="tf-icon" value="${esc(t.icon_url||'')}" placeholder="No icon uploaded" readonly style="margin-top:6px">
          <button class="btn-sm" type="button" style="margin-top:6px" onclick="clearTowerAssetUrl('tf-icon','tf-icon-file')">Clear</button>
        </div>
        <div class="form-group">
          <label style="font-size:11px">Sprite Image</label>
          <input type="file" id="tf-sprite-file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml">
          <input type="text" id="tf-sprite" value="${esc(t.sprite_url||'')}" placeholder="No sprite uploaded" readonly style="margin-top:6px">
          <button class="btn-sm" type="button" style="margin-top:6px" onclick="clearTowerAssetUrl('tf-sprite','tf-sprite-file')">Clear</button>
        </div>
        <div class="form-group">
          <label style="font-size:11px">Projectile Image</label>
          <input type="file" id="tf-proj-file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml">
          <input type="text" id="tf-proj" value="${esc(t.projectile_url||'')}" placeholder="No projectile image uploaded" readonly style="margin-top:6px">
          <button class="btn-sm" type="button" style="margin-top:6px" onclick="clearTowerAssetUrl('tf-proj','tf-proj-file')">Clear</button>
        </div>
        <div class="form-group">
          <label style="font-size:11px">Animation Image</label>
          <input type="file" id="tf-anim-file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml">
          <input type="text" id="tf-anim" value="${esc(t.animation_url||'')}" placeholder="No animation image uploaded" readonly style="margin-top:6px">
          <button class="btn-sm" type="button" style="margin-top:6px" onclick="clearTowerAssetUrl('tf-anim','tf-anim-file')">Clear</button>
        </div>
      </div>
    </details>

    <div class="form-group">
      <label>
        <input type="checkbox" id="tf-enabled" ${(t.enabled !== false) ? 'checked' : ''}> Enabled in game
      </label>
    </div>
  `;
}

function gatherTowerForm() {
  const targets = [...document.querySelectorAll('.tf-target:checked')].map(c => c.value);
  return {
    name:                 document.getElementById('tf-name')?.value.trim(),
    description:          document.getElementById('tf-desc')?.value.trim(),
    category:             document.getElementById('tf-category')?.value,
    damage_type:          document.getElementById('tf-dtype')?.value,
    attack_damage:        +document.getElementById('tf-dmg')?.value,
    attack_speed:         +document.getElementById('tf-aspd')?.value,
    range:                +document.getElementById('tf-range')?.value,
    base_cost:            +document.getElementById('tf-cost')?.value,
    splash_radius:        document.getElementById('tf-splash')?.value ? +document.getElementById('tf-splash')?.value : null,
    projectile_speed:     document.getElementById('tf-pspeed')?.value ? +document.getElementById('tf-pspeed')?.value : null,
    upgrade_cost_mult:    +document.getElementById('tf-ucm')?.value,
    damage_scaling:       +document.getElementById('tf-dscale')?.value,
    range_scaling:        +document.getElementById('tf-rscale')?.value,
    attack_speed_scaling: +document.getElementById('tf-ascale')?.value,
    targeting_options:    targets.join(',') || 'first',
    icon_url:             document.getElementById('tf-icon')?.value.trim() || null,
    sprite_url:           document.getElementById('tf-sprite')?.value.trim() || null,
    projectile_url:       document.getElementById('tf-proj')?.value.trim() || null,
    animation_url:        document.getElementById('tf-anim')?.value.trim() || null,
    enabled:              document.getElementById('tf-enabled')?.checked,
  };
}

function clearTowerAssetUrl(urlInputId, fileInputId) {
  const urlEl = document.getElementById(urlInputId);
  const fileEl = document.getElementById(fileInputId);
  if (urlEl) urlEl.value = '';
  if (fileEl) fileEl.value = '';
}
window.clearTowerAssetUrl = clearTowerAssetUrl;

function fileToDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ''));
    reader.onerror = () => reject(new Error('Failed to read file'));
    reader.readAsDataURL(file);
  });
}

function loadImageFromDataUrl(dataUrl) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('Failed to load image'));
    img.src = dataUrl;
  });
}

function canvasToDataUrl(canvas, mimeType, quality) {
  if (mimeType === 'image/jpeg' || mimeType === 'image/webp') {
    return canvas.toDataURL(mimeType, quality);
  }
  return canvas.toDataURL(mimeType);
}

async function normalizeAssetUpload(file, options = {}) {
  const { maxWidth = 0, maxHeight = 0, outputMimeType = '' } = options;
  const isBitmap = /^image\/(png|jpeg|webp)$/i.test(file.type);
  if (!isBitmap || (!maxWidth && !maxHeight)) {
    return {
      fileName: file.name,
      mimeType: file.type,
      dataBase64: await fileToDataUrl(file),
    };
  }

  const srcDataUrl = await fileToDataUrl(file);
  const img = await loadImageFromDataUrl(srcDataUrl);
  const widthLimit = maxWidth || img.naturalWidth;
  const heightLimit = maxHeight || img.naturalHeight;
  const scale = Math.min(
    1,
    widthLimit / Math.max(1, img.naturalWidth),
    heightLimit / Math.max(1, img.naturalHeight)
  );

  if (scale >= 1) {
    return {
      fileName: file.name,
      mimeType: file.type,
      dataBase64: srcDataUrl,
    };
  }

  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(img.naturalWidth * scale));
  canvas.height = Math.max(1, Math.round(img.naturalHeight * scale));
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas is unavailable for image resize');
  ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

  const mimeType = outputMimeType || file.type || 'image/png';
  const safeName = file.name.replace(/\.[^.]+$/, '');
  return {
    fileName: `${safeName}.${mimeType === 'image/jpeg' ? 'jpg' : mimeType === 'image/webp' ? 'webp' : 'png'}`,
    mimeType,
    dataBase64: canvasToDataUrl(canvas, mimeType, 0.92),
  };
}

async function uploadTowerAssetFile(file, options = {}) {
  const payload = await normalizeAssetUpload(file, options);
  const res = await fetch('/admin/assets/upload-image', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      fileName: payload.fileName,
      mimeType: payload.mimeType,
      dataBase64: payload.dataBase64,
    }),
    credentials: 'include',
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(body.error || 'Asset upload failed');
  if (!body.url) throw new Error('Asset upload did not return a URL');
  return body.url;
}

async function uploadSelectedTowerAssets() {
  const assetFields = [
    { fileId: 'tf-icon-file', urlId: 'tf-icon', label: 'icon', uploadOptions: { maxWidth: 256, maxHeight: 256, outputMimeType: 'image/png' } },
    { fileId: 'tf-sprite-file', urlId: 'tf-sprite', label: 'sprite' },
    { fileId: 'tf-proj-file', urlId: 'tf-proj', label: 'projectile' },
    { fileId: 'tf-anim-file', urlId: 'tf-anim', label: 'animation' },
  ];

  const uploaded = [];
  for (const asset of assetFields) {
    const input = document.getElementById(asset.fileId);
    const file = input?.files?.[0];
    if (!file) continue;
    const url = await uploadTowerAssetFile(file, asset.uploadOptions || {});
    const urlEl = document.getElementById(asset.urlId);
    if (urlEl) urlEl.value = url;
    uploaded.push(asset.label);
  }
  return uploaded;
}

function openCreateTowerModal() {
  openModal('New Mob', towerFormHtml({}), `
    <button class="btn-primary" onclick="submitCreateTower()">Create Mob</button>
    <button onclick="closeModal()">Cancel</button>
  `);
}
window.openCreateTowerModal = openCreateTowerModal;

async function submitCreateTower() {
  try {
    const uploaded = await uploadSelectedTowerAssets();
    const data = gatherTowerForm();
    if (!data.name) { toast('Mob Name is required', 'err'); return; }
    if (!data.attack_damage || data.attack_damage <= 0) { toast('Attack Damage must be > 0', 'err'); return; }
    if (!data.range || data.range <= 0) { toast('Range must be > 0', 'err'); return; }
    if (!data.base_cost || data.base_cost <= 0) { toast('Base Cost must be > 0', 'err'); return; }
    await api('POST', '/admin/towers', data);
    if (uploaded.length) toast(`Uploaded: ${uploaded.join(', ')}`, 'ok');
    toast(`Mob "${data.name}" created`, 'ok');
    closeModal();
    loadTowers();
  } catch (err) { toast(`Create failed: ${err.message}`, 'err'); }
}
window.submitCreateTower = submitCreateTower;

async function openEditTowerModal(id) {
  try {
    const d = await api('GET', `/admin/towers/${id}`);
    const t = d.tower;
    openModal(`Edit Mob: ${t.name}`, towerFormHtml(t), `
      <button class="btn-primary" onclick="submitEditTower(${id})">Save Changes</button>
      <button onclick="closeModal()">Cancel</button>
    `);
  } catch (err) { toast(`Load failed: ${err.message}`, 'err'); }
}
window.openEditTowerModal = openEditTowerModal;

async function submitEditTower(id) {
  try {
    const uploaded = await uploadSelectedTowerAssets();
    const data = gatherTowerForm();
    if (!data.name) { toast('Mob Name is required', 'err'); return; }
    await api('PATCH', `/admin/towers/${id}`, data);
    if (uploaded.length) toast(`Uploaded: ${uploaded.join(', ')}`, 'ok');
    toast('Mob updated', 'ok');
    closeModal();
    loadTowers();
  } catch (err) { toast(`Update failed: ${err.message}`, 'err'); }
}
window.submitEditTower = submitEditTower;

async function toggleTower(id, enable) {
  try {
    await api('PATCH', `/admin/towers/${id}`, { enabled: enable });
    toast(`Mob ${enable ? 'enabled' : 'disabled'}`, 'ok');
    loadTowers();
  } catch (err) { toast(`Failed: ${err.message}`, 'err'); }
}
window.toggleTower = toggleTower;

async function deleteTower(id, name) {
  if (!confirm(`Delete mob "${name}"? This cannot be undone.`)) return;
  try {
    await api('DELETE', `/admin/towers/${id}`);
    toast(`Mob "${name}" deleted`, 'ok');
    loadTowers();
  } catch (err) { toast(`Delete failed: ${err.message}`, 'err'); }
}
window.deleteTower = deleteTower;

// ── Ability management modal ─────────────────────────────────────────────────

async function openAbilityModal(towerId, towerName) {
  let towerData, abilityCatalog;
  try {
    [towerData, abilityCatalog] = await Promise.all([
      api('GET', `/admin/towers/${towerId}`),
      api('GET', '/admin/abilities'),
    ]);
  } catch (err) { toast(`Load failed: ${err.message}`, 'err'); return; }

  const tower     = towerData.tower;
  const catalog   = abilityCatalog.abilities || [];
  const assigned  = new Map((tower.abilities || []).map(a => [a.ability_key, a.params]));

  const catalogByCat = {};
  for (const ab of catalog) {
    (catalogByCat[ab.category] = catalogByCat[ab.category] || []).push(ab);
  }

  const catLabels = { damage: 'Damage', status_effect: 'Status Effects', utility: 'Utility', aura: 'Aura Effects' };

  let html = `<p class="text-muted" style="font-size:12px;margin-bottom:16px">
    Attach modular abilities to <strong>${esc(towerName)}</strong>. Each ability can have configurable parameters.
  </p>`;

  for (const [cat, abs] of Object.entries(catalogByCat)) {
    html += `<h4 style="font-size:12px;text-transform:uppercase;letter-spacing:1px;color:var(--muted);margin:16px 0 8px">${catLabels[cat]||cat}</h4>`;
    for (const ab of abs) {
      const isAssigned = assigned.has(ab.key);
      const params     = isAssigned ? assigned.get(ab.key) : {};
      const schema     = ab.param_schema || {};
      const paramEntries = Object.entries(schema);

      html += `
        <div class="ability-row ${isAssigned ? 'ability-row--active' : ''}" id="ab-row-${ab.key.replace(/[^a-z0-9]/g,'_')}">
          <div style="display:flex;align-items:center;gap:10px">
            <input type="checkbox" class="ab-check" id="ab-${ab.key}" data-key="${ab.key}"
              ${isAssigned ? 'checked' : ''} onchange="toggleAbilityRow('${ab.key}',${towerId})">
            <div>
              <label for="ab-${ab.key}" style="font-weight:600;font-size:13px;cursor:pointer">${esc(ab.name)}</label>
              <p class="text-muted" style="font-size:11px;margin:2px 0 0">${esc(ab.description)}</p>
            </div>
          </div>
          ${paramEntries.length > 0 ? `
            <div class="ability-params ${isAssigned ? '' : 'hidden'}" id="ab-params-${ab.key.replace(/[^a-z0-9]/g,'_')}">
              ${paramEntries.map(([pkey, pdef]) => `
                <div style="display:flex;align-items:center;gap:8px;margin-top:8px">
                  <label style="font-size:11px;color:var(--muted);min-width:120px">${esc(pdef.label||pkey)}</label>
                  <input type="number" class="ab-param" data-ability="${ab.key}" data-param="${pkey}"
                    value="${params[pkey] !== undefined ? params[pkey] : pdef.default}"
                    step="${pdef.type==='integer'?1:0.1}"
                    min="${pdef.min!==undefined?pdef.min:''}"
                    max="${pdef.max!==undefined?pdef.max:''}"
                    style="width:80px"
                    onchange="saveAbilityParam(${towerId},'${ab.key}')">
                </div>`).join('')}
            </div>` : ''}
        </div>`;
    }
  }

  openModal(`Abilities: ${towerName}`, html, `<button onclick="closeModal();loadTowers()">Done</button>`);
}
window.openAbilityModal = openAbilityModal;

function toggleAbilityRow(abilityKey, towerId) {
  const cb       = document.getElementById(`ab-${abilityKey}`);
  const safeKey  = abilityKey.replace(/[^a-z0-9]/g,'_');
  const paramDiv = document.getElementById(`ab-params-${safeKey}`);
  const row      = document.getElementById(`ab-row-${safeKey}`);

  if (cb.checked) {
    paramDiv?.classList.remove('hidden');
    row?.classList.add('ability-row--active');
    saveAbilityParam(towerId, abilityKey);
  } else {
    paramDiv?.classList.add('hidden');
    row?.classList.remove('ability-row--active');
    api('DELETE', `/admin/towers/${towerId}/abilities/${abilityKey}`)
      .then(() => toast(`Ability removed`, 'ok'))
      .catch(err => toast(`Remove failed: ${err.message}`, 'err'));
  }
}
window.toggleAbilityRow = toggleAbilityRow;

function saveAbilityParam(towerId, abilityKey) {
  const inputs = document.querySelectorAll(`.ab-param[data-ability="${abilityKey}"]`);
  const params = {};
  inputs.forEach(inp => { params[inp.dataset.param] = +inp.value; });
  api('POST', `/admin/towers/${towerId}/abilities`, { ability_key: abilityKey, params })
    .catch(err => toast(`Save failed: ${err.message}`, 'err'));
}
window.saveAbilityParam = saveAbilityParam;

// ═══════════════════════════════════════════════════════════════════════
// ML WAVE CONFIG TAB (replaces old Survival tab)
// ═══════════════════════════════════════════════════════════════════════

const mlWaveState = {
  configs: [],
  editingId: null,
  editingWaves: [],   // [{wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult}]
  unitTypes: [],      // populated from /api/units on first load
};

async function loadSurvival() {
  setContent('<p class="load">Loading wave configs…</p>');
  try {
    const [configsData, unitsData] = await Promise.all([
      api('GET', '/admin/ml-waves/configs'),
      api('GET', '/api/units').catch(() => ({ units: [] })),
    ]);
    mlWaveState.configs = configsData.configs || [];
    mlWaveState.unitTypes = (unitsData.units || [])
      .filter(u => u.behavior_mode === 'moving' || u.behavior_mode === 'both')
      .map(u => u.key);
    mlWaveState.editingId = null;
    renderMLWaveConfigList();
  } catch (err) {
    setContent(`<p class="err">Failed to load: ${err.message}</p>`);
  }
}


function renderMLWaveConfigList() {
  const canWrite = can('config.write');
  const configs = mlWaveState.configs;
  const cards = configs.map(c => `
    <div class="card" style="margin-bottom:12px">
      <div style="display:flex;justify-content:space-between;align-items:center">
        <div>
          <strong>${esc(c.name)}</strong>
          ${c.is_default ? '<span style="color:#4ade80;font-size:12px;margin-left:6px">[DEFAULT]</span>' : ''}
          <br><small style="color:#94a3b8">${esc(c.description || '')} &nbsp;·&nbsp; ${c.wave_count || 0} waves</small>
        </div>
        <div style="display:flex;gap:6px;flex-wrap:wrap;justify-content:flex-end">
          ${canWrite && !c.is_default ? `<button style="font-size:11px;padding:3px 8px" onclick="mlWaveSetDefault(${c.id})">Set Default</button>` : ''}
          <button style="font-size:11px;padding:3px 8px" onclick="mlWaveOpenEditor(${c.id})">Edit Waves</button>
          ${canWrite ? `<button style="font-size:11px;padding:3px 8px;background:#ef4444" onclick="mlWaveDeleteConfig(${c.id})">Delete</button>` : ''}
        </div>
      </div>
    </div>
  `).join('');

  const createForm = canWrite ? `
    <div class="card" style="margin-top:16px">
      <strong>New Wave Config</strong>
      <div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap">
        <input id="new-wave-cfg-name" class="inp" placeholder="Config name" style="flex:1;min-width:150px">
        <input id="new-wave-cfg-desc" class="inp" placeholder="Description (optional)" style="flex:2;min-width:150px">
        <button onclick="mlWaveCreateConfig()">Create</button>
      </div>
    </div>
  ` : '';

  setContent(`
    <h2>ML Wave Configs</h2>
    <p style="color:#94a3b8;font-size:13px">Manage wave configs for Forge Wars wave defense mode. The default config is loaded at match start.</p>
    ${configs.length ? cards : '<p style="color:#94a3b8">No configs yet.</p>'}
    ${createForm}
  `);
}

async function mlWaveCreateConfig() {
  const name = document.getElementById('new-wave-cfg-name')?.value?.trim();
  if (!name) return toast('Name required', 'err');
  const description = document.getElementById('new-wave-cfg-desc')?.value?.trim() || '';
  try {
    await api('POST', '/admin/ml-waves/configs', { name, description });
    toast('Config created');
    await loadSurvival();
  } catch (err) {
    toast(`Error: ${err.message}`, 'err');
  }
}
window.mlWaveCreateConfig = mlWaveCreateConfig;

async function mlWaveSetDefault(id) {
  try {
    await api('POST', `/admin/ml-waves/configs/${id}/set-default`);
    toast('Default updated');
    await loadSurvival();
  } catch (err) {
    toast(`Error: ${err.message}`, 'err');
  }
}
window.mlWaveSetDefault = mlWaveSetDefault;

async function mlWaveDeleteConfig(id) {
  if (!confirm('Delete this wave config?')) return;
  try {
    await api('DELETE', `/admin/ml-waves/configs/${id}`);
    toast('Config deleted');
    await loadSurvival();
  } catch (err) {
    toast(`Error: ${err.message}`, 'err');
  }
}
window.mlWaveDeleteConfig = mlWaveDeleteConfig;

async function mlWaveOpenEditor(id) {
  try {
    const data = await api('GET', `/admin/ml-waves/configs/${id}`);
    mlWaveState.editingId = id;
    mlWaveState.editingWaves = (data.waves || []).map(w => ({ ...w }));
    renderMLWaveEditor(data.config);
  } catch (err) {
    toast(`Error: ${err.message}`, 'err');
  }
}
window.mlWaveOpenEditor = mlWaveOpenEditor;

function renderMLWaveEditor(config) {
  const canWrite = can('config.write');
  const waves = mlWaveState.editingWaves;
  const unitTypes = mlWaveState.unitTypes;
  const unitOpts = unitTypes.map(u => `<option value="${esc(u)}">${esc(u)}</option>`).join('');

  const rows = waves.map((w, i) => `
    <tr>
      <td style="text-align:center;color:#94a3b8;font-size:12px">${w.wave_number}</td>
      <td>
        <select class="inp" style="width:100%;font-size:12px" onchange="mlWaveRowChange(${i},'unit_type',this.value)" ${canWrite ? '' : 'disabled'}>
          ${unitTypes.map(u => `<option value="${esc(u)}" ${u === w.unit_type ? 'selected' : ''}>${esc(u)}</option>`).join('')}
        </select>
      </td>
      <td><input type="number" class="inp" style="width:60px;font-size:12px" value="${w.spawn_qty}" min="1" max="200"
        onchange="mlWaveRowChange(${i},'spawn_qty',this.value)" ${canWrite ? '' : 'disabled'}></td>
      <td><input type="number" class="inp" style="width:60px;font-size:12px" value="${w.hp_mult}" min="0.1" max="100" step="0.05"
        onchange="mlWaveRowChange(${i},'hp_mult',this.value)" ${canWrite ? '' : 'disabled'}></td>
      <td><input type="number" class="inp" style="width:60px;font-size:12px" value="${w.dmg_mult}" min="0.1" max="100" step="0.05"
        onchange="mlWaveRowChange(${i},'dmg_mult',this.value)" ${canWrite ? '' : 'disabled'}></td>
      <td><input type="number" class="inp" style="width:60px;font-size:12px" value="${w.speed_mult}" min="0.1" max="10" step="0.05"
        onchange="mlWaveRowChange(${i},'speed_mult',this.value)" ${canWrite ? '' : 'disabled'}></td>
      ${canWrite ? `<td><button style="font-size:11px;padding:2px 6px;background:#ef4444" onclick="mlWaveRemoveRow(${i})">×</button></td>` : '<td></td>'}
    </tr>
  `).join('');

  const isDefault = config && config.is_default;
  setContent(`
    <div style="display:flex;align-items:center;gap:12px;margin-bottom:12px">
      <button onclick="mlWaveBackToList()">← Back</button>
      <h2 style="margin:0">${esc(config ? config.name : 'Wave Editor')}</h2>
      ${isDefault ? '<span style="color:#4ade80;font-size:12px">[DEFAULT]</span>' : ''}
    </div>
    <p style="color:#94a3b8;font-size:13px;margin-top:0">
      Each row = one round. Wave enemies are spawned in <strong>spawn_qty</strong> batches with the given stat multipliers.<br>
      After the last configured wave, enemies repeat the last wave with +10% HP/DMG per extra round.
    </p>
    <div style="overflow-x:auto">
      <table style="width:100%;font-size:13px;border-collapse:collapse">
        <thead>
          <tr style="color:#94a3b8;text-align:left">
            <th style="padding:6px 8px">#</th>
            <th style="padding:6px 8px">Unit Type</th>
            <th style="padding:6px 8px">Qty</th>
            <th style="padding:6px 8px">HP×</th>
            <th style="padding:6px 8px">DMG×</th>
            <th style="padding:6px 8px">Speed×</th>
            <th style="padding:6px 8px"></th>
          </tr>
        </thead>
        <tbody id="ml-wave-rows">
          ${rows || '<tr><td colspan="7" style="color:#94a3b8;padding:12px">No waves yet. Add one below.</td></tr>'}
        </tbody>
      </table>
    </div>
    ${canWrite ? `
    <div style="display:flex;gap:8px;margin-top:12px;flex-wrap:wrap">
      <button onclick="mlWaveAddRow()">+ Add Wave</button>
      <button onclick="mlWaveSaveAll()" style="background:rgba(40,208,96,0.15);border-color:rgba(40,208,96,0.4);color:#28d060">Save All</button>
    </div>
    ` : ''}
  `);
}

function mlWaveRowChange(i, field, value) {
  const w = mlWaveState.editingWaves[i];
  if (!w) return;
  if (field === 'unit_type') {
    w.unit_type = value;
  } else {
    w[field] = Number(value);
  }
}
window.mlWaveRowChange = mlWaveRowChange;

function mlWaveAddRow() {
  const waves = mlWaveState.editingWaves;
  const lastNum = waves.length > 0 ? waves[waves.length - 1].wave_number : 0;
  const lastWave = waves[waves.length - 1];
  waves.push({
    wave_number: lastNum + 1,
    unit_type: (lastWave && lastWave.unit_type) || (mlWaveState.unitTypes[0] || ''),
    spawn_qty: (lastWave && lastWave.spawn_qty) || 8,
    hp_mult: (lastWave && lastWave.hp_mult) || 1.0,
    dmg_mult: (lastWave && lastWave.dmg_mult) || 1.0,
    speed_mult: (lastWave && lastWave.speed_mult) || 1.0,
  });
  // Re-render editor (we need the config name — fetch it from configs list)
  const config = mlWaveState.configs.find(c => c.id === mlWaveState.editingId) || null;
  renderMLWaveEditor(config);
}
window.mlWaveAddRow = mlWaveAddRow;

function mlWaveRemoveRow(i) {
  mlWaveState.editingWaves.splice(i, 1);
  // Renumber
  mlWaveState.editingWaves.forEach((w, idx) => { w.wave_number = idx + 1; });
  const config = mlWaveState.configs.find(c => c.id === mlWaveState.editingId) || null;
  renderMLWaveEditor(config);
}
window.mlWaveRemoveRow = mlWaveRemoveRow;

async function mlWaveSaveAll() {
  const id = mlWaveState.editingId;
  if (!id) return;
  try {
    await api('PUT', `/admin/ml-waves/configs/${id}/waves`, { waves: mlWaveState.editingWaves });
    toast('Waves saved');
    // Refresh configs list in background
    api('GET', '/admin/ml-waves/configs').then(d => { mlWaveState.configs = d.configs || []; }).catch(() => {});
  } catch (err) {
    toast(`Save failed: ${err.message}`, 'err');
  }
}
window.mlWaveSaveAll = mlWaveSaveAll;

function mlWaveBackToList() {
  mlWaveState.editingId = null;
  mlWaveState.editingWaves = [];
  renderMLWaveConfigList();
}
window.mlWaveBackToList = mlWaveBackToList;


// ═══════════════════════════════════════════════════════════════════════
// UNITS TAB (Phase A — Core Reuse Architecture)
// ═══════════════════════════════════════════════════════════════════════

const BEHAVIOR_BADGE_COLOR = { moving: '#42a5f5', fixed: '#66bb6a', both: '#ffa726' };
const UNIT_DTYPE_COLORS = {
  NORMAL: '#90a4ae', PIERCE: '#42a5f5', SPLASH: '#ffa726',
  SIEGE: '#ab47bc',  MAGIC:  '#7e57c2', PHYSICAL: '#ff8a65', TRUE: '#ef5350',
};
const UNIT_DTYPE_ORDER = ['NORMAL', 'PHYSICAL', 'PIERCE', 'SPLASH', 'SIEGE', 'MAGIC', 'TRUE'];
const UNIT_ARMOR_COLORS = {
  UNARMORED: '#90a4ae', LIGHT: '#42a5f5', MEDIUM: '#ffa726', HEAVY: '#ef5350', MAGIC: '#7e57c2',
};

async function loadUnits() {
  setContent('<p class="load">Loading unit types…</p>');
  try {
    const [utRes, dfRes] = await Promise.all([
      api('GET', '/admin/unit-types'),
      api('GET', '/admin/unit-display-fields').catch(() => ({ fields: [] })),
    ]);
    S.units.displayFields = dfRes.fields || [];
    renderUnitsTab(utRes.unitTypes || [], S.units.displayFields);
  } catch (err) {
    setContent(`<p class="load text-danger">Error: ${esc(err.message)}</p>`);
  }
}
window.loadUnits = loadUnits;

function switchUnitsView(view) {
  S.units.view = view;
  document.getElementById('units-view-list')?.classList.toggle('active', view === 'list');
  document.getElementById('units-view-kanban')?.classList.toggle('active', view === 'kanban');
  document.getElementById('units-list-wrap')  && (document.getElementById('units-list-wrap').style.display   = view === 'list'   ? '' : 'none');
  document.getElementById('units-kanban-wrap') && (document.getElementById('units-kanban-wrap').style.display = view === 'kanban' ? '' : 'none');
}
window.switchUnitsView = switchUnitsView;

function _utPlaceholderSymbol(ut) {
  return { moving: '⚔', fixed: '🏰', both: '🛡' }[ut.behavior_mode] || '?';
}

function _utPlaceholderHtml(ut, size = 60) {
  return `<div class="ukc-icon-placeholder" style="width:${size}px;height:${size}px;font-size:${Math.round(size*0.45)}px">${_utPlaceholderSymbol(ut)}</div>`;
}

function _utBuiltinIconSrc(ut) {
  const images = window.RenderAssets?.manifest?.images || {};
  return images[`unit_icon_${ut.key}`] || '';
}

function adminUnitIconFallback(imgEl) {
  if (!imgEl) return;
  const fallbackSrc = imgEl.dataset.fallbackSrc || '';
  if (fallbackSrc && imgEl.src !== fallbackSrc) {
    imgEl.dataset.fallbackSrc = '';
    imgEl.src = fallbackSrc;
    return;
  }
  const placeholder = document.createElement('div');
  placeholder.className = 'ukc-icon-placeholder';
  placeholder.style.width = imgEl.style.width || '60px';
  placeholder.style.height = imgEl.style.height || '60px';
  placeholder.style.fontSize = `${Math.round((parseInt(imgEl.dataset.size, 10) || 60) * 0.45)}px`;
  placeholder.textContent = imgEl.dataset.placeholder || '?';
  imgEl.replaceWith(placeholder);
}
window.adminUnitIconFallback = adminUnitIconFallback;

function _utBadge(label, color) {
  return `<span style="display:inline-block;padding:1px 6px;border-radius:3px;font-size:10px;background:${color}22;color:${color};border:1px solid ${color}44">${label}</span>`;
}

function _utIconHtml(ut, size = 60, cssClass = 'ukc-icon') {
  if (ut.icon_url) return `<img src="${esc(ut.icon_url)}" class="${cssClass}" style="width:${size}px;height:${size}px" alt="${esc(ut.name)}">`;
  const emoji = { moving: '⚔', fixed: '🏰', both: '🛡' }[ut.behavior_mode] || '?';
  return `<div class="ukc-icon-placeholder" style="width:${size}px;height:${size}px;font-size:${Math.round(size*0.45)}px">${emoji}</div>`;
}

function _utIconHtml(ut, size = 60, cssClass = 'ukc-icon') {
  const fallbackSrc = _utBuiltinIconSrc(ut);
  const primarySrc = ut.icon_url || fallbackSrc;
  if (primarySrc) {
    const fallbackAttr = fallbackSrc && ut.icon_url ? ` data-fallback-src="${esc(fallbackSrc)}"` : '';
    return `<img src="${esc(primarySrc)}" class="${cssClass}" style="width:${size}px;height:${size}px" alt="${esc(ut.name)}" data-size="${size}" data-placeholder="${_utPlaceholderSymbol(ut)}"${fallbackAttr} onerror="adminUnitIconFallback(this)">`;
  }
  return _utPlaceholderHtml(ut, size);
}

function renderUnitsTab(unitTypes, displayFields) {
  const canWrite = can('config.write');

  // ── List view rows
  const listRows = unitTypes.map(ut => {
    const bColor = BEHAVIOR_BADGE_COLOR[ut.behavior_mode] || '#aaa';
    const dColor = UNIT_DTYPE_COLORS[ut.damage_type] || '#aaa';
    const aColor = UNIT_ARMOR_COLORS[ut.armor_type]  || '#aaa';
    return `<tr>
      <td><code style="font-size:11px">${esc(ut.key)}</code></td>
      <td>${esc(ut.name)}</td>
      <td>${_utBadge(ut.behavior_mode, bColor)}</td>
      <td>${_utBadge(ut.damage_type, dColor)}</td>
      <td>${_utBadge(ut.armor_type, aColor)}</td>
      <td class="text-right">${ut.hp}</td>
      <td class="text-right">${ut.send_cost}</td>
      <td class="text-right">${ut.build_cost}</td>
      <td>${ut.enabled ? '<span style="color:#66bb6a;font-size:11px">on</span>' : '<span style="color:#ef5350;font-size:11px">off</span>'}</td>
      <td>
        <span title="${ut.display_to_players ? 'Shown to players' : 'Hidden from players'}"
          style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${ut.display_to_players ? '#66bb6a' : '#ef5350'};margin-right:4px"></span>
        ${canWrite ? `
          <button class="btn-sm" onclick="openUnitTypeModal(${ut.id})">Edit</button>
          <button class="btn-sm" onclick="toggleUnitTypeEnabled(${ut.id},${ut.enabled})">${ut.enabled ? 'Disable' : 'Enable'}</button>
          <button class="btn-sm" onclick="toggleUnitDisplayToPlayers(${ut.id},${ut.display_to_players})">${ut.display_to_players ? 'Hide' : 'Show'} Players</button>
          <button class="btn-sm" style="color:#ef5350" onclick="deleteUnitType(${ut.id},'${esc(ut.key)}')">Del</button>
        ` : ''}
      </td>
    </tr>`;
  }).join('');

  // ── Kanban cards grouped by damage type
  const unitsByDamageType = new Map();
  unitTypes.forEach((ut) => {
    const key = String(ut.damage_type || 'OTHER').toUpperCase();
    if (!unitsByDamageType.has(key)) unitsByDamageType.set(key, []);
    unitsByDamageType.get(key).push(ut);
  });

  const kanbanCard = (ut) => {
    const bColor = BEHAVIOR_BADGE_COLOR[ut.behavior_mode] || '#aaa';
    const dColor = UNIT_DTYPE_COLORS[ut.damage_type] || '#aaa';
    const aColor = UNIT_ARMOR_COLORS[ut.armor_type]  || '#aaa';
    return `<div class="unit-kanban-card${ut.enabled ? '' : ' ukc-disabled'}" onclick="openUnitTypeModal(${ut.id})">
      <span class="ukc-display-dot ${ut.display_to_players ? 'on' : 'off'}"
        title="${ut.display_to_players ? 'Shown to players' : 'Hidden from players'}"></span>
      ${_utIconHtml(ut, 84)}
      <div class="ukc-name">${esc(ut.name)}</div>
      <div class="ukc-desc">${esc(ut.description || '')}</div>
      <div class="ukc-badges">
        ${_utBadge(ut.behavior_mode, bColor)}
        ${_utBadge(ut.damage_type, dColor)}
        ${_utBadge(ut.armor_type, aColor)}
      </div>
      <div class="ukc-stats">
        <span class="ukc-stat-label">HP</span><span class="ukc-stat-val">${ut.hp}</span>
        <span class="ukc-stat-label">ATK</span><span class="ukc-stat-val">${ut.attack_damage}</span>
        <span class="ukc-stat-label">Send$</span><span class="ukc-stat-val">${ut.send_cost}</span>
        <span class="ukc-stat-label">Build$</span><span class="ukc-stat-val">${ut.build_cost}</span>
      </div>
      ${canWrite ? `<div class="ukc-actions" onclick="event.stopPropagation()">
        <button class="btn-sm" onclick="toggleUnitTypeEnabled(${ut.id},${ut.enabled})">${ut.enabled ? 'Disable' : 'Enable'}</button>
        <button class="btn-sm" onclick="toggleUnitDisplayToPlayers(${ut.id},${ut.display_to_players})">${ut.display_to_players ? 'Hide' : 'Show'}</button>
        <button class="btn-sm" style="color:#ef5350" onclick="deleteUnitType(${ut.id},'${esc(ut.key)}')">Del</button>
      </div>` : ''}
    </div>`;
  };

  const damageColumns = UNIT_DTYPE_ORDER
    .filter((dtype) => unitsByDamageType.has(dtype))
    .concat(Array.from(unitsByDamageType.keys()).filter((dtype) => !UNIT_DTYPE_ORDER.includes(dtype)).sort())
    .map((dtype) => {
      const units = unitsByDamageType.get(dtype) || [];
      const color = UNIT_DTYPE_COLORS[dtype] || '#aaa';
      const cards = units
        .slice()
        .sort((a, b) => String(a.name || '').localeCompare(String(b.name || '')))
        .map(kanbanCard)
        .join('');
      return `<section class="tower-kanban-col">
        <header class="tower-kanban-col-head">
          <div>${_utBadge(dtype, color)}</div>
          <span class="text-muted" style="font-size:11px">${units.length}</span>
        </header>
        <div class="tower-kanban-col-body">
          ${cards || '<p class="text-muted" style="padding:12px">No units</p>'}
        </div>
      </section>`;
    }).join('');

  setContent(`
    <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:20px">
      <div>
        <h2 style="margin:0">Unit Types <span style="font-size:14px;color:var(--muted)">${unitTypes.length}</span></h2>
        <p class="text-muted" style="font-size:12px;margin:4px 0 0">
          Attackers (moving), Defenders (fixed), and Special units. Green dot = shown to players.
        </p>
      </div>
      <div style="display:flex;gap:8px;align-items:center">
        <div class="units-view-toggle">
          <button class="units-view-btn ${S.units.view === 'list' ? 'active' : ''}" id="units-view-list" onclick="switchUnitsView('list')">≡ List</button>
          <button class="units-view-btn ${S.units.view === 'kanban' ? 'active' : ''}" id="units-view-kanban" onclick="switchUnitsView('kanban')">⊞ Kanban</button>
        </div>
        ${canWrite ? `
          <button class="btn-sm" onclick="reloadUnitTypeCache()">↻ Reload Cache</button>
          <button class="btn-success" onclick="openUnitTypeModal(null)">+ New Unit Type</button>
        ` : ''}
      </div>
    </div>

    <div id="units-list-wrap" class="section" style="${S.units.view === 'list' ? '' : 'display:none'};overflow-x:auto">
      <table style="width:100%">
        <thead><tr>
          <th>Key</th><th>Name</th><th>Mode</th><th>Dmg Type</th>
          <th>Armor</th><th>HP</th><th>Send$</th><th>Build$</th><th>On</th>
          <th>Actions</th>
        </tr></thead>
        <tbody>
          ${listRows || '<tr><td colspan="10" class="text-muted" style="padding:20px;text-align:center">No unit types found.</td></tr>'}
        </tbody>
      </table>
    </div>

    <div id="units-kanban-wrap" style="${S.units.view === 'kanban' ? '' : 'display:none'}">
      <div class="tower-kanban">
        ${damageColumns || '<p class="text-muted" style="padding:20px">No unit types found.</p>'}
      </div>
    </div>

    <div class="display-settings-section">
      <h3 style="margin:0 0 4px">Display Settings</h3>
      <p class="text-muted" style="font-size:12px;margin-bottom:12px">
        Control which stat fields appear on player-facing unit cards. The green/red dot on each unit overrides visibility for that unit only.
      </p>
      <div id="display-settings-wrap"></div>
    </div>

    <div style="margin-top:32px">
      <h3 style="margin:0 0 12px">Barracks Levels</h3>
      <div id="barracks-levels-section"><p class="load">Loading…</p></div>
    </div>
  `);

  renderDisplaySettings(displayFields, unitTypes);
  loadBarracksLevels();
}

async function toggleUnitDisplayToPlayers(id, current) {
  try {
    await api('PATCH', `/admin/unit-types/${id}`, { display_to_players: !current });
    toast(`Unit ${current ? 'hidden from' : 'shown to'} players`);
    loadUnits();
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.toggleUnitDisplayToPlayers = toggleUnitDisplayToPlayers;

function renderDisplaySettings(displayFields, unitTypes) {
  const canWrite = can('config.write');
  const wrap = document.getElementById('display-settings-wrap');
  if (!wrap) return;

  const fieldRows = (displayFields.length ? displayFields : []).map(f => `
    <div class="display-field-row">
      <input type="checkbox" id="df-${esc(f.field_key)}"
        ${f.visible_to_players ? 'checked' : ''}
        ${canWrite ? `onchange="saveDisplayFieldVisibility('${esc(f.field_key)}', this.checked)"` : 'disabled'}>
      <label for="df-${esc(f.field_key)}">${esc(f.label)}</label>
    </div>
  `).join('');

  const unitOpts = (unitTypes || []).map(ut =>
    `<option value="${ut.id}">${esc(ut.name)}</option>`
  ).join('');

  wrap.innerHTML = `
    <div class="display-fields-grid">${fieldRows || '<p class="text-muted">No display fields configured (run migration 026).</p>'}</div>
    ${displayFields.length ? `
      <div class="display-preview-wrap">
        <div>
          <label style="font-size:12px;color:var(--muted)">Preview unit:</label><br>
          <select id="display-preview-unit" style="margin-top:4px;padding:4px 8px;background:var(--card);border:1px solid var(--border);color:var(--text);border-radius:4px;font-size:12px"
            onchange="refreshDisplayPreview()">
            ${unitOpts}
          </select>
        </div>
        <div id="display-preview-card-wrap"></div>
      </div>
    ` : ''}
  `;

  refreshDisplayPreview();
}

function refreshDisplayPreview() {
  const sel = document.getElementById('display-preview-unit');
  const wrap = document.getElementById('display-preview-card-wrap');
  if (!sel || !wrap) return;
  const unitId = parseInt(sel.value, 10);
  // Find unit from current tab data — stored in S.units implicitly via the rendered list
  // We'll re-fetch from a quick pass of the displayed cards
  const cardEl = document.querySelector(`.unit-kanban-card[onclick*="openUnitTypeModal(${unitId})"]`);
  if (!cardEl) { wrap.innerHTML = ''; return; }

  const dFields = S.units.displayFields;
  if (!dFields.length) { wrap.innerHTML = ''; return; }

  // Reconstruct stats from the card's data (use DOM to find relevant info),
  // but simpler: just show a mock card using the checked fields
  const visibleFields = dFields.filter(f => {
    const cb = document.getElementById(`df-${f.field_key}`);
    return cb ? cb.checked : f.visible_to_players;
  });

  const namEl = cardEl.querySelector('.ukc-name');
  const name  = namEl ? namEl.textContent : 'Unit';
  const iconEl = cardEl.querySelector('.ukc-icon, .ukc-icon-placeholder');
  const iconHtml = iconEl ? iconEl.outerHTML : '';

  const statsHtml = visibleFields.map(f => {
    // We can read existing stat labels from the card's ukc-stats
    return `<span class="ukc-stat-label">${esc(f.label)}</span><span class="ukc-stat-val">—</span>`;
  }).join('');

  wrap.innerHTML = `
    <div class="display-preview-card">
      ${iconHtml}
      <div class="ukc-name">${esc(name)}</div>
      <div class="ukc-stats">${statsHtml || '<span class="text-muted" style="grid-column:1/-1">No fields visible</span>'}</div>
    </div>
    <p class="display-preview-label">Player card preview<br><span style="color:var(--muted)">${visibleFields.length} field${visibleFields.length !== 1 ? 's' : ''} visible</span></p>
  `;
}
window.refreshDisplayPreview = refreshDisplayPreview;

async function saveDisplayFieldVisibility(fieldKey, visible) {
  try {
    await api('PUT', `/admin/unit-display-fields/${fieldKey}`, { visible_to_players: visible });
    toast('Display setting saved');
    const f = S.units.displayFields.find(df => df.field_key === fieldKey);
    if (f) f.visible_to_players = visible;
    refreshDisplayPreview();
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.saveDisplayFieldVisibility = saveDisplayFieldVisibility;

async function toggleUnitTypeEnabled(id, currentEnabled) {
  try {
    await api('PATCH', `/admin/unit-types/${id}`, { enabled: !currentEnabled });
    toast(`Unit type ${currentEnabled ? 'disabled' : 'enabled'}`);
    loadUnits();
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.toggleUnitTypeEnabled = toggleUnitTypeEnabled;

async function deleteUnitType(id, key) {
  if (!confirm(`Delete unit type "${key}"? This cannot be undone.`)) return;
  try {
    await api('DELETE', `/admin/unit-types/${id}`);
    toast('Unit type deleted');
    loadUnits();
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.deleteUnitType = deleteUnitType;

async function reloadUnitTypeCache() {
  try {
    const r = await api('POST', '/admin/unit-types/reload');
    toast(`Cache reloaded — ${r.count} unit types`);
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.reloadUnitTypeCache = reloadUnitTypeCache;

// ── Unit Type modal (create + edit) ────────────────────────────────────────────

async function openUnitTypeModal(id) {
  let ut = null;
  let abilities = [];
  if (id != null) {
    try {
      const d = await api('GET', `/admin/unit-types/${id}`);
      ut = d.unitType;
      abilities = ut.abilities || [];
    } catch (err) {
      toast(err.message, 'err');
      return;
    }
  }

  const v = (f, def = '') => ut ? (ut[f] ?? def) : def;
  const chk = (f) => ut ? (ut[f] ? 'checked' : '') : '';

  const sel = (f, opts) => `<select id="ut-${f}" style="width:100%">${
    opts.map(o => `<option value="${o}" ${v(f) === o ? 'selected' : ''}>${o}</option>`).join('')
  }</select>`;

  const inp = (f, type = 'text', step = '') =>
    `<input id="ut-${f}" type="${type}" ${step ? `step="${step}"` : ''} value="${esc(String(v(f)))}" style="width:100%">`;

  const abilityRows = abilities.map(a =>
    `<div style="display:flex;align-items:center;gap:6px;margin-bottom:4px">
       <code style="font-size:11px;flex:1">${esc(a.ability_key)}</code>
       ${can('config.write') && id != null
         ? `<button class="btn-sm" style="color:#ef5350" onclick="detachUnitAbility(${id},'${esc(a.ability_key)}')">✕</button>`
         : ''}
     </div>`
  ).join('') || '<p class="text-muted" style="font-size:12px">No abilities attached.</p>';

  const attachSection = can('config.write') && id != null ? `
    <div style="margin-top:8px;display:flex;gap:6px">
      <input id="ut-new-ability-key" type="text" placeholder="ability_key" style="flex:1">
      <button class="btn-sm" onclick="attachUnitAbility(${id})">Attach</button>
    </div>
  ` : '';

  openModal(id != null ? `Edit Unit Type: ${esc(ut.key)}` : 'New Unit Type', `
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
      <div class="form-group" style="grid-column:1/2">
        <label>Key</label>${inp('key')}
      </div>
      <div class="form-group" style="grid-column:2/3">
        <label>Name</label>${inp('name')}
      </div>
      <div class="form-group" style="grid-column:1/-1">
        <label>Description</label><input id="ut-description" type="text" value="${esc(String(v('description')))}" style="width:100%">
      </div>
      <div class="form-group">
        <label>Behavior Mode</label>${sel('behavior_mode', ['moving','fixed','both'])}
      </div>
      <div class="form-group" style="display:flex;align-items:center;gap:8px;padding-top:20px">
        <input type="checkbox" id="ut-enabled" ${chk('enabled') || (!ut ? 'checked' : '')}>
        <label for="ut-enabled" style="margin:0">Enabled</label>
      </div>
    </div>

    <h4 style="margin:16px 0 8px">Combat Stats</h4>
    <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:8px">
      <div class="form-group"><label>HP</label>${inp('hp','number','0.01')}</div>
      <div class="form-group"><label>Attack Damage</label>${inp('attack_damage','number','0.01')}</div>
      <div class="form-group"><label>Attack Speed</label>${inp('attack_speed','number','0.01')}</div>
      <div class="form-group"><label>Range</label>${inp('range','number','0.001')}</div>
      <div class="form-group"><label>Path Speed</label>${inp('path_speed','number','0.001')}</div>
    </div>

    <h4 style="margin:16px 0 8px">Damage &amp; Armor</h4>
    <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:8px">
      <div class="form-group"><label>Damage Type</label>${sel('damage_type',['NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE'])}</div>
      <div class="form-group"><label>Armor Type</label>${sel('armor_type',['UNARMORED','LIGHT','MEDIUM','HEAVY','MAGIC'])}</div>
      <div class="form-group"><label>Dmg Reduction % (0–80)</label>${inp('damage_reduction_pct','number','1')}</div>
    </div>

    <h4 style="margin:16px 0 8px">Economy</h4>
    <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:8px">
      <div class="form-group"><label>Send Cost</label>${inp('send_cost','number','1')}</div>
      <div class="form-group"><label>Build Cost</label>${inp('build_cost','number','1')}</div>
      <div class="form-group"><label>Income</label>${inp('income','number','1')}</div>
      <div class="form-group"><label>Refund % (0–100)</label>${inp('refund_pct','number','1')}</div>
    </div>

    <h4 style="margin:16px 0 8px">Barracks Scaling</h4>
    <div style="display:flex;gap:24px">
      <label><input type="checkbox" id="ut-barracks_scales_hp" ${chk('barracks_scales_hp')}> Scale HP</label>
      <label><input type="checkbox" id="ut-barracks_scales_dmg" ${chk('barracks_scales_dmg')}> Scale Damage</label>
    </div>

    <h4 style="margin:16px 0 8px">Assets</h4>
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px">
      <div class="form-group">
        <label>Icon URL</label>
        <input id="ut-icon_url-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('icon_url')}
      </div>
      <div class="form-group"><label>Sprite URL (Legacy/Fallback)</label>${inp('sprite_url')}</div>
      <div class="form-group"><label>Animation URL (Legacy/Fallback)</label>${inp('animation_url')}</div>
      <div class="form-group">
        <label>Front Sprite (moving down screen)</label>
        <input id="ut-sprite_url_front-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('sprite_url_front')}
      </div>
      <div class="form-group">
        <label>Back Sprite (moving up screen)</label>
        <input id="ut-sprite_url_back-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('sprite_url_back')}
      </div>
      <div class="form-group">
        <label>Front Animation Sheet (moving down)</label>
        <input id="ut-animation_url_front-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('animation_url_front')}
      </div>
      <div class="form-group">
        <label>Back Animation Sheet (moving up)</label>
        <input id="ut-animation_url_back-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('animation_url_back')}
      </div>
      <div class="form-group">
        <label>Idle Sprite (Legacy/Fallback)</label>
        <input id="ut-idle_sprite_url-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('idle_sprite_url')}
      </div>
      <div class="form-group">
        <label>Front Idle Sprite (moving down)</label>
        <input id="ut-idle_sprite_url_front-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('idle_sprite_url_front')}
      </div>
      <div class="form-group">
        <label>Back Idle Sprite (moving up)</label>
        <input id="ut-idle_sprite_url_back-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/svg+xml" style="width:100%;margin-bottom:6px">
        ${inp('idle_sprite_url_back')}
      </div>
    </div>

    <h4 style="margin:16px 0 8px">Sound Cues</h4>
    <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:8px">
      <div class="form-group"><label>Spawn</label>${inp('sound_spawn')}</div>
      <div class="form-group"><label>Attack</label>${inp('sound_attack')}</div>
      <div class="form-group"><label>Hit</label>${inp('sound_hit')}</div>
      <div class="form-group"><label>Death</label>${inp('sound_death')}</div>
    </div>

    ${id != null ? `
      <h4 style="margin:16px 0 8px">Abilities</h4>
      <div>${abilityRows}</div>
      ${attachSection}
    ` : ''}
  `, can('config.write') ? `<button class="btn-primary" onclick="saveUnitType(${id ?? 'null'})">Save</button>` : '');

  syncUnitCostFields();
}
window.openUnitTypeModal = openUnitTypeModal;

function syncUnitCostFields() {
  const sendCostEl = document.getElementById('ut-send_cost');
  const buildCostEl = document.getElementById('ut-build_cost');
  if (!sendCostEl || !buildCostEl) return;

  let sendCostCustomized = false;

  if (buildCostEl.value !== '') {
    sendCostEl.value = buildCostEl.value;
  } else if (sendCostEl.value !== '') {
    buildCostEl.value = sendCostEl.value;
  }

  sendCostEl.addEventListener('input', () => {
    sendCostCustomized = true;
  });

  buildCostEl.addEventListener('input', () => {
    if (!sendCostCustomized) {
      sendCostEl.value = buildCostEl.value;
    }
  });
}

function readUnitTypeForm() {
  const g = (id) => document.getElementById(id);
  const num = (id) => { const v = parseFloat(g(id)?.value); return Number.isFinite(v) ? v : 0; };
  const int = (id) => { const v = parseInt(g(id)?.value, 10); return Number.isFinite(v) ? v : 0; };
  return {
    key:                  g('ut-key')?.value.trim(),
    name:                 g('ut-name')?.value.trim(),
    description:          g('ut-description')?.value.trim(),
    behavior_mode:        g('ut-behavior_mode')?.value,
    enabled:              g('ut-enabled')?.checked ?? true,
    hp:                   num('ut-hp'),
    attack_damage:        num('ut-attack_damage'),
    attack_speed:         num('ut-attack_speed'),
    range:                num('ut-range'),
    path_speed:           num('ut-path_speed'),
    damage_type:          g('ut-damage_type')?.value,
    armor_type:           g('ut-armor_type')?.value,
    damage_reduction_pct: int('ut-damage_reduction_pct'),
    send_cost:            int('ut-send_cost'),
    build_cost:           int('ut-build_cost'),
    income:               int('ut-income'),
    refund_pct:           int('ut-refund_pct'),
    barracks_scales_hp:   g('ut-barracks_scales_hp')?.checked ?? false,
    barracks_scales_dmg:  g('ut-barracks_scales_dmg')?.checked ?? false,
    icon_url:             g('ut-icon_url')?.value.trim() || null,
    sprite_url:           g('ut-sprite_url')?.value.trim() || null,
    animation_url:        g('ut-animation_url')?.value.trim() || null,
    sprite_url_front:     g('ut-sprite_url_front')?.value.trim() || null,
    sprite_url_back:      g('ut-sprite_url_back')?.value.trim() || null,
    animation_url_front:  g('ut-animation_url_front')?.value.trim() || null,
    animation_url_back:   g('ut-animation_url_back')?.value.trim() || null,
    idle_sprite_url:      g('ut-idle_sprite_url')?.value.trim() || null,
    idle_sprite_url_front:g('ut-idle_sprite_url_front')?.value.trim() || null,
    idle_sprite_url_back: g('ut-idle_sprite_url_back')?.value.trim() || null,
    sound_spawn:          g('ut-sound_spawn')?.value.trim() || null,
    sound_attack:         g('ut-sound_attack')?.value.trim() || null,
    sound_hit:            g('ut-sound_hit')?.value.trim() || null,
    sound_death:          g('ut-sound_death')?.value.trim() || null,
  };
}

async function uploadSelectedUnitAssets() {
  const assetFields = [
    { fileId: 'ut-icon_url-file', urlId: 'ut-icon_url', uploadOptions: { maxWidth: 256, maxHeight: 256, outputMimeType: 'image/png' } },
    { fileId: 'ut-sprite_url_front-file', urlId: 'ut-sprite_url_front' },
    { fileId: 'ut-sprite_url_back-file', urlId: 'ut-sprite_url_back' },
    { fileId: 'ut-animation_url_front-file', urlId: 'ut-animation_url_front' },
    { fileId: 'ut-animation_url_back-file', urlId: 'ut-animation_url_back' },
    { fileId: 'ut-idle_sprite_url-file', urlId: 'ut-idle_sprite_url' },
    { fileId: 'ut-idle_sprite_url_front-file', urlId: 'ut-idle_sprite_url_front' },
    { fileId: 'ut-idle_sprite_url_back-file', urlId: 'ut-idle_sprite_url_back' },
  ];
  for (const asset of assetFields) {
    const input = document.getElementById(asset.fileId);
    const file = input?.files?.[0];
    if (!file) continue;
    const url = await uploadTowerAssetFile(file, asset.uploadOptions || {});
    const urlEl = document.getElementById(asset.urlId);
    if (urlEl) urlEl.value = url;
  }
}

async function saveUnitType(id) {
  try {
    await uploadSelectedUnitAssets();
    const body = readUnitTypeForm();
    if (id != null) {
      await api('PATCH', `/admin/unit-types/${id}`, body);
      toast('Unit type updated');
    } else {
      await api('POST', '/admin/unit-types', body);
      toast('Unit type created');
    }
    document.getElementById('modal-overlay').classList.add('hidden');
    loadUnits();
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.saveUnitType = saveUnitType;

async function attachUnitAbility(unitTypeId) {
  const key = document.getElementById('ut-new-ability-key')?.value.trim();
  if (!key) { toast('Enter an ability key', 'err'); return; }
  try {
    await api('POST', `/admin/unit-types/${unitTypeId}/abilities`, { ability_key: key });
    toast('Ability attached');
    openUnitTypeModal(unitTypeId);
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.attachUnitAbility = attachUnitAbility;

async function detachUnitAbility(unitTypeId, abilityKey) {
  if (!confirm(`Remove ability "${abilityKey}" from this unit type?`)) return;
  try {
    await api('DELETE', `/admin/unit-types/${unitTypeId}/abilities/${encodeURIComponent(abilityKey)}`);
    toast('Ability removed');
    openUnitTypeModal(unitTypeId);
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.detachUnitAbility = detachUnitAbility;

// ── Barracks Levels ────────────────────────────────────────────────────────────

async function loadBarracksLevels() {
  const el = document.getElementById('barracks-levels-section');
  if (!el) return;
  try {
    const { levels } = await api('GET', '/admin/barracks-levels');
    renderBarracksLevels(levels || []);
  } catch (err) {
    el.innerHTML = `<p class="text-danger">${esc(err.message)}</p>`;
  }
}
window.loadBarracksLevels = loadBarracksLevels;

function renderBarracksLevels(levels) {
  const el = document.getElementById('barracks-levels-section');
  if (!el) return;
  const canWrite = can('config.write');

  const rows = levels.map(lv => `<tr>
    <td>${lv.level}</td>
    <td>${parseFloat(lv.multiplier).toFixed(2)}×</td>
    <td>${lv.upgrade_cost}</td>
    <td>${esc(lv.notes)}</td>
    <td>${canWrite ? `<button class="btn-sm" onclick="openBarracksLevelModal(${lv.level},${lv.multiplier},${lv.upgrade_cost},'${esc(lv.notes)}')">Edit</button>` : ''}</td>
  </tr>`).join('');

  el.innerHTML = `
    <div class="section" style="overflow-x:auto">
      <table style="width:100%">
        <thead><tr><th>Level</th><th>Multiplier</th><th>Upgrade Cost</th><th>Notes</th>${canWrite ? '<th></th>' : ''}</tr></thead>
        <tbody>${rows || '<tr><td colspan="5" class="text-muted" style="padding:16px">No levels configured.</td></tr>'}</tbody>
      </table>
      ${canWrite ? '<button class="btn-sm" style="margin-top:8px" onclick="openBarracksLevelModal(null,1.0,0,\'\')">+ Add Level</button>' : ''}
    </div>
  `;
}

function openBarracksLevelModal(level, multiplier, upgradeCost, notes) {
  const isNew = level == null;
  openModal(isNew ? 'Add Barracks Level' : `Edit Barracks Level ${level}`, `
    <div class="form-group">
      <label>Level (1–99)</label>
      <input id="bl-level" type="number" min="1" max="99" value="${isNew ? '' : level}" style="width:100%" ${isNew ? '' : 'readonly'}>
    </div>
    <div class="form-group">
      <label>Multiplier</label>
      <input id="bl-multiplier" type="number" step="0.01" min="0.1" max="100" value="${multiplier}" style="width:100%">
    </div>
    <div class="form-group">
      <label>Upgrade Cost</label>
      <input id="bl-upgrade_cost" type="number" min="0" value="${upgradeCost}" style="width:100%">
    </div>
    <div class="form-group">
      <label>Notes</label>
      <input id="bl-notes" type="text" value="${esc(notes)}" style="width:100%">
    </div>
  `, `<button class="btn-primary" onclick="saveBarracksLevel()">Save</button>`);
}
window.openBarracksLevelModal = openBarracksLevelModal;

async function saveBarracksLevel() {
  const level = parseInt(document.getElementById('bl-level')?.value, 10);
  const multiplier = parseFloat(document.getElementById('bl-multiplier')?.value);
  const upgrade_cost = parseInt(document.getElementById('bl-upgrade_cost')?.value, 10) || 0;
  const notes = document.getElementById('bl-notes')?.value.trim() || '';
  if (!Number.isFinite(level) || level < 1) { toast('Invalid level', 'err'); return; }
  if (!Number.isFinite(multiplier)) { toast('Invalid multiplier', 'err'); return; }
  try {
    await api('PUT', `/admin/barracks-levels/${level}`, { multiplier, upgrade_cost, notes });
    toast('Barracks level saved');
    document.getElementById('modal-overlay').classList.add('hidden');
    loadBarracksLevels();
  } catch (err) {
    toast(err.message, 'err');
  }
}
window.saveBarracksLevel = saveBarracksLevel;

// ═══════════════════════════════════════════════════════════════════════
// INIT
// ═══════════════════════════════════════════════════════════════════════
async function init() {
  applyFieldDescriptions(document.getElementById('login-screen'));

  // Fetch public config (Google client ID etc.)
  try {
    const cfg = await fetch('/config').then(r => r.json());
    if (cfg.googleClientId) window.__GOOGLE_CLIENT_ID__ = cfg.googleClientId;
    applyAdminBranding(cfg.branding);
  } catch {
    applyAdminBranding(defaultBranding());
  }

  // Render Google button (GSI may not be loaded yet)
  if (typeof google !== 'undefined') renderGoogleBtn();
  else window.addEventListener('load', renderGoogleBtn, { once: true });

  const params = new URLSearchParams(window.location.search);
  const resetToken = params.get('admin_reset') || params.get('reset');
  if (resetToken) {
    S.adminResetToken = resetToken;
    setLoginSection('reset');
    document.getElementById('reset-password')?.focus();
    return;
  }

  if (S.authMode !== 'jwt') return;
  const hasStoredAdminIdentity = !!(S.adminEmail || S.adminRole || S.adminDisplayName);
  if (!hasStoredAdminIdentity) return;

  try {
    const me = await api('GET', '/admin/me');
    if (me?.user) {
      S.adminRole = me.user.role || S.adminRole;
      S.adminEmail = me.user.email || S.adminEmail;
      S.adminDisplayName = me.user.display_name || S.adminDisplayName || me.user.email || 'Admin';
      sessionStorage.setItem('adminRole', S.adminRole);
      sessionStorage.setItem('adminEmail', S.adminEmail);
      sessionStorage.setItem('adminDisplayName', S.adminDisplayName);
    }
    enterApp();
  } catch {
    doLogout();
  }
}

init();

// ═══════════════════════════════════════════════════════════════════════
// ASSET PACKS  (Phase H)
// ═══════════════════════════════════════════════════════════════════════
async function loadAssets() {
  setContent('<p class="load">Loading asset packs…</p>');
  try {
    const packs = await api('GET', '/admin/asset-packs');
    renderAssetPackList(packs);
  } catch (err) {
    setContent(`<p class="error">Error: ${esc(err.message)}</p>`);
  }
}

function renderAssetPackList(packs) {
  const canWrite = can('config.write');
  const rows = packs.map(p => `
    <tr>
      <td>${esc(p.key)}</td>
      <td>${esc(p.name)}</td>
      <td>${esc(p.description)}</td>
      <td><span class="badge ${p.enabled ? 'badge-green' : 'badge-red'}">${p.enabled ? 'enabled' : 'disabled'}</span></td>
      <td>${(p.items || []).length}</td>
      <td>
        <button onclick="openAssetPackItems(${p.id},${JSON.stringify(esc(p.name))})">Items</button>
        ${canWrite ? `
        <button onclick="editAssetPack(${p.id},${JSON.stringify(esc(p.key))},${JSON.stringify(esc(p.name))},${JSON.stringify(esc(p.description))},${p.enabled})">Edit</button>
        <button class="danger" onclick="deleteAssetPack(${p.id})">Delete</button>` : ''}
      </td>
    </tr>`).join('');

  setContent(`
    <div class="section-header">
      <h2>Asset Packs</h2>
      ${canWrite ? '<button onclick="createAssetPackModal()">+ New Pack</button>' : ''}
    </div>
    <table>
      <thead><tr><th>Key</th><th>Name</th><th>Description</th><th>Status</th><th>Items</th><th>Actions</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6">No asset packs yet.</td></tr>'}</tbody>
    </table>`);
}

window.createAssetPackModal = function () {
  openModal('New Asset Pack', `
    <div class="form-group"><label>Key (unique slug)</label><input id="ap-key" placeholder="e.g. fantasy_reskin"></div>
    <div class="form-group"><label>Name</label><input id="ap-name" placeholder="Fantasy Reskin"></div>
    <div class="form-group"><label>Description</label><input id="ap-desc"></div>
    <div class="form-group"><label><input type="checkbox" id="ap-enabled" checked> Enabled</label></div>`,
    `<button onclick="submitCreateAssetPack()">Create</button>`);
};

window.submitCreateAssetPack = async function () {
  const key = document.getElementById('ap-key')?.value.trim();
  const name = document.getElementById('ap-name')?.value.trim();
  const description = document.getElementById('ap-desc')?.value.trim();
  const enabled = document.getElementById('ap-enabled')?.checked ?? true;
  if (!key || !name) return toast('Key and name required', 'err');
  try {
    await api('POST', '/admin/asset-packs', { key, name, description, enabled });
    toast('Asset pack created');
    closeModal();
    loadAssets();
  } catch (err) { toast(err.message, 'err'); }
};

window.editAssetPack = function (id, key, name, desc, enabled) {
  openModal(`Edit Pack: ${key}`, `
    <div class="form-group"><label>Name</label><input id="ep-name" value="${esc(name)}"></div>
    <div class="form-group"><label>Description</label><input id="ep-desc" value="${esc(desc)}"></div>
    <div class="form-group"><label><input type="checkbox" id="ep-enabled" ${enabled ? 'checked' : ''}> Enabled</label></div>`,
    `<button onclick="submitEditAssetPack(${id})">Save</button>`);
};

window.submitEditAssetPack = async function (id) {
  const name = document.getElementById('ep-name')?.value.trim();
  const description = document.getElementById('ep-desc')?.value.trim();
  const enabled = document.getElementById('ep-enabled')?.checked ?? true;
  try {
    await api('PATCH', `/admin/asset-packs/${id}`, { name, description, enabled });
    toast('Saved');
    closeModal();
    loadAssets();
  } catch (err) { toast(err.message, 'err'); }
};

window.deleteAssetPack = async function (id) {
  if (!confirm('Delete this asset pack? This removes all its items.')) return;
  try {
    await api('DELETE', `/admin/asset-packs/${id}`);
    toast('Deleted');
    loadAssets();
  } catch (err) { toast(err.message, 'err'); }
};

window.openAssetPackItems = function (packId, packName) {
  api('GET', '/admin/asset-packs').then(packs => {
    const pack = packs.find(p => p.id === packId);
    if (!pack) return toast('Pack not found', 'err');
    renderAssetPackItemsModal(pack);
  });
};

function renderAssetPackItemsModal(pack) {
  const canWrite = can('config.write');
  const rows = (pack.items || []).map(item => `
    <tr>
      <td>${esc(item.unit_type_key)}</td>
      <td>${esc(item.asset_slot)}</td>
      <td style="max-width:260px;overflow:hidden;text-overflow:ellipsis" title="${esc(item.url)}">${esc(item.url)}</td>
      <td>${canWrite ? `<button class="danger" onclick="deletePackItem(${pack.id},${item.id})">Remove</button>` : ''}</td>
    </tr>`).join('');

  const addForm = canWrite ? `
    <hr>
    <h4>Add / Update Item</h4>
    <div class="form-row">
      <input id="pi-key" placeholder="unit_type_key e.g. runner">
      <select id="pi-slot"><option value="icon">icon</option><option value="sprite">sprite</option><option value="animation">animation</option></select>
      <input id="pi-url" placeholder="https://…/runner_override.png" style="flex:2">
      <button onclick="submitAddPackItem(${pack.id})">Add</button>
    </div>` : '';

  openModal(`Items — ${esc(pack.name)}`, `
    <table>
      <thead><tr><th>Unit Type</th><th>Slot</th><th>URL</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="4">No items.</td></tr>'}</tbody>
    </table>
    ${addForm}`);
}

window.submitAddPackItem = async function (packId) {
  const unit_type_key = document.getElementById('pi-key')?.value.trim();
  const asset_slot = document.getElementById('pi-slot')?.value;
  const url = document.getElementById('pi-url')?.value.trim();
  if (!unit_type_key || !url) return toast('unit_type_key and url required', 'err');
  try {
    await api('POST', `/admin/asset-packs/${packId}/items`, { unit_type_key, asset_slot, url });
    toast('Item saved');
    // Refresh modal
    const packs = await api('GET', '/admin/asset-packs');
    const pack = packs.find(p => p.id === packId);
    if (pack) renderAssetPackItemsModal(pack);
  } catch (err) { toast(err.message, 'err'); }
};

window.deletePackItem = async function (packId, itemId) {
  try {
    await api('DELETE', `/admin/asset-packs/${packId}/items/${itemId}`);
    toast('Removed');
    const packs = await api('GET', '/admin/asset-packs');
    const pack = packs.find(p => p.id === packId);
    if (pack) renderAssetPackItemsModal(pack);
  } catch (err) { toast(err.message, 'err'); }
};
