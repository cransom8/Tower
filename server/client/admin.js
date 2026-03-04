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
  // Navigation
  tab:        'dashboard',
  // Tab sub-state
  players:    { q: '', offset: 0, total: 0, rows: [] },
  matches:    { tab: 'history', status: '', mode: '', offset: 0, total: 0, rows: [], live: [] },
  audit:      { offset: 0, total: 0, rows: [] },
  liveHandle: null,
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
    const res = await fetch('/admin/auth/google', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ credential: response.credential }),
      credentials: 'include',
    });
    const d = await res.json();
    if (!res.ok) { errEl.textContent = d.error || 'Google login failed.'; return; }
    if (d.twoFaRequired) {
      S._pendingTicket = d.ticket;
      show2faStep();
      return;
    }
    finishLogin(d);
  } catch (err) {
    errEl.textContent = err.message || 'Google login failed.';
  }
}
window.handleAdminGoogleCredential = handleAdminGoogleCredential;

// ── 2FA step helpers ───────────────────────────────────────────────────────
function show2faStep() {
  document.getElementById('login-pass-section')?.classList.add('hidden');
  document.getElementById('login-2fa-section')?.classList.remove('hidden');
  document.getElementById('login-2fa-code')?.focus();
}

function cancelLogin2fa() {
  S._pendingTicket = null;
  document.getElementById('login-pass-section')?.classList.remove('hidden');
  document.getElementById('login-2fa-section')?.classList.add('hidden');
  document.getElementById('login-2fa-code').value = '';
  document.getElementById('login-err').textContent = '';
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
  stopLivePoll();
  document.getElementById('app').classList.add('hidden');
  document.getElementById('login-screen').style.display = '';
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
    config:    loadConfig,
    ops:       loadOps,
    audit:     loadAudit,
    team:      loadTeam,
  };
  (m[tab] || loadDashboard)();
}

// ── Helpers ────────────────────────────────────────────────────────────────
function setContent(html) {
  document.getElementById('main-content').innerHTML = html;
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
  return String(s || '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
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
async function loadDashboard() {
  setContent('<p class="load">Loading…</p>');
  try {
    const [live, daily] = await Promise.all([
      api('GET', '/admin/stats/live'),
      api('GET', '/admin/stats/daily'),
    ]);
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
          <div class="sub">${fmt(live.classicGames)} classic · ${fmt(live.mlGames)} multilane</div>
        </div>
        <div class="kpi-card amber">
          <div class="label">Lobby Rooms</div>
          <div class="value">${fmt(live.lobbyRooms)}</div>
          <div class="sub">waiting for players</div>
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
        <div class="section-header"><h3>Quick Actions</h3></div>
        <div class="section-body">
          <div style="display:flex;gap:10px;flex-wrap:wrap">
            <button onclick="navigate('ops')">⚙ Feature Flags</button>
            <button onclick="navigate('players')">👤 Player Search</button>
            <button onclick="navigate('matches')">⚔ Live Matches</button>
            <button onclick="navigate('analytics')">📈 Analytics</button>
            <button onclick="navigate('anticheat')">🛡 Anti-cheat</button>
            <button onclick="navigate('audit')">📋 Audit Log</button>
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
    const d = await api('GET', `/admin/matches/${id}`);
    const m = d.match;
    const playerRows = d.players.map(p => `
      <tr>
        <td>${p.lane_index}</td>
        <td><a href="#" onclick="closeModal();openPlayerModal('${p.player_id}');return false">${esc(p.display_name)}</a></td>
        <td>${statusBadge(p.result)}</td>
        <td>${statusBadge(p.player_status)}</td>
      </tr>`).join('');

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
      </table></div>`;
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
const DTYPE_COLORS = {
  NORMAL: '#aaa', PIERCE: '#4fc3f7', SPLASH: '#ff7043',
  SIEGE:  '#ab47bc', MAGIC: '#7e57c2', PHYSICAL: '#ff8a65', TRUE: '#ef5350',
};

async function loadTowers() {
  setContent('<p class="load">Loading towers…</p>');
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
  const cards = towers.map(t => towerCard(t, canEdit)).join('');
  setContent(`
    <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:20px">
      <div>
        <h2 style="margin:0">Towers</h2>
        <p class="text-muted" style="font-size:12px;margin:4px 0 0">
          ${towers.length} tower${towers.length !== 1 ? 's' : ''} — data-driven, no deployment required
        </p>
      </div>
      ${canEdit ? '<button class="btn-success" onclick="openCreateTowerModal()">+ New Tower</button>' : ''}
    </div>

    ${towers.length === 0
      ? '<div class="section"><div class="section-body" style="padding:32px;text-align:center;color:var(--muted)">No towers yet. Click <strong>+ New Tower</strong> to create one.</div></div>'
      : `<div class="tower-grid">${cards}</div>`}
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
    <div class="tower-card ${t.enabled ? '' : 'tower-card--disabled'}">
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
          <button class="btn-sm btn-primary" onclick="openEditTowerModal(${t.id})">Edit</button>
          <button class="btn-sm" onclick="openAbilityModal(${t.id},'${esc(t.name)}')">Abilities</button>
          <button class="btn-sm" onclick="toggleTower(${t.id},${!t.enabled})" style="${t.enabled ? 'background:#c62828' : ''}">
            ${t.enabled ? 'Disable' : 'Enable'}
          </button>
          <button class="btn-sm" style="background:#b71c1c" onclick="deleteTower(${t.id},'${esc(t.name)}')">Delete</button>
        ` : ''}
      </div>
    </div>`;
}

// ── Tower create/edit modal ──────────────────────────────────────────────────

function towerFormHtml(t) {
  const targeting = (t.targeting_options || 'first').split(',').map(s => s.trim());
  return `
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
      <div class="form-group" style="grid-column:1/-1">
        <label>Tower Name *</label>
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
    </details>

    <details style="margin:12px 0">
      <summary style="cursor:pointer;font-size:12px;color:var(--muted);margin-bottom:8px">Visual Assets (URL)</summary>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;padding:8px 0">
        <div class="form-group">
          <label style="font-size:11px">Icon URL</label>
          <input type="url" id="tf-icon" value="${esc(t.icon_url||'')}" placeholder="https://…">
        </div>
        <div class="form-group">
          <label style="font-size:11px">Sprite URL</label>
          <input type="url" id="tf-sprite" value="${esc(t.sprite_url||'')}" placeholder="https://…">
        </div>
        <div class="form-group">
          <label style="font-size:11px">Projectile URL</label>
          <input type="url" id="tf-proj" value="${esc(t.projectile_url||'')}" placeholder="https://…">
        </div>
        <div class="form-group">
          <label style="font-size:11px">Animation URL</label>
          <input type="url" id="tf-anim" value="${esc(t.animation_url||'')}" placeholder="https://…">
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

function openCreateTowerModal() {
  openModal('New Tower', towerFormHtml({}), `
    <button class="btn-primary" onclick="submitCreateTower()">Create Tower</button>
    <button onclick="closeModal()">Cancel</button>
  `);
}
window.openCreateTowerModal = openCreateTowerModal;

async function submitCreateTower() {
  const data = gatherTowerForm();
  if (!data.name) { toast('Tower Name is required', 'err'); return; }
  if (!data.attack_damage || data.attack_damage <= 0) { toast('Attack Damage must be > 0', 'err'); return; }
  if (!data.range || data.range <= 0) { toast('Range must be > 0', 'err'); return; }
  if (!data.base_cost || data.base_cost <= 0) { toast('Base Cost must be > 0', 'err'); return; }
  try {
    await api('POST', '/admin/towers', data);
    toast(`Tower "${data.name}" created`, 'ok');
    closeModal();
    loadTowers();
  } catch (err) { toast(`Create failed: ${err.message}`, 'err'); }
}
window.submitCreateTower = submitCreateTower;

async function openEditTowerModal(id) {
  try {
    const d = await api('GET', `/admin/towers/${id}`);
    const t = d.tower;
    openModal(`Edit Tower: ${t.name}`, towerFormHtml(t), `
      <button class="btn-primary" onclick="submitEditTower(${id})">Save Changes</button>
      <button onclick="closeModal()">Cancel</button>
    `);
  } catch (err) { toast(`Load failed: ${err.message}`, 'err'); }
}
window.openEditTowerModal = openEditTowerModal;

async function submitEditTower(id) {
  const data = gatherTowerForm();
  if (!data.name) { toast('Tower Name is required', 'err'); return; }
  try {
    await api('PATCH', `/admin/towers/${id}`, data);
    toast('Tower updated', 'ok');
    closeModal();
    loadTowers();
  } catch (err) { toast(`Update failed: ${err.message}`, 'err'); }
}
window.submitEditTower = submitEditTower;

async function toggleTower(id, enable) {
  try {
    await api('PATCH', `/admin/towers/${id}`, { enabled: enable });
    toast(`Tower ${enable ? 'enabled' : 'disabled'}`, 'ok');
    loadTowers();
  } catch (err) { toast(`Failed: ${err.message}`, 'err'); }
}
window.toggleTower = toggleTower;

async function deleteTower(id, name) {
  if (!confirm(`Delete tower "${name}"? This cannot be undone.`)) return;
  try {
    await api('DELETE', `/admin/towers/${id}`);
    toast(`Tower "${name}" deleted`, 'ok');
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
// INIT
// ═══════════════════════════════════════════════════════════════════════
async function init() {
  // Fetch public config (Google client ID etc.)
  try {
    const cfg = await fetch('/config').then(r => r.json());
    if (cfg.googleClientId) window.__GOOGLE_CLIENT_ID__ = cfg.googleClientId;
  } catch { /* non-fatal */ }

  // Render Google button (GSI may not be loaded yet)
  if (typeof google !== 'undefined') renderGoogleBtn();
  else window.addEventListener('load', renderGoogleBtn, { once: true });

  if (S.authMode !== 'jwt') return;

  try {
    await api('GET', '/admin/stats/daily');
    enterApp();
  } catch {
    doLogout();
  }
}

init();
