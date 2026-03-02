'use strict';

const SERVER_URL = window.location.hostname === 'localhost'
  ? 'http://localhost:3000'
  : window.location.origin;

const socket = io(SERVER_URL, { autoConnect: true, reconnectionAttempts: 5 });

const UNIT_TYPES = ['footman', 'bowman', 'ironclad', 'runner', 'warlock'];
const TOWER_TYPES = ['archer', 'fighter', 'ballista', 'cannon', 'mage'];
const SLOT_NAMES = ['left_outer', 'left_mid', 'left_inner', 'right_inner', 'right_mid', 'right_outer'];
const DAMAGE_TYPES = ['PIERCE', 'NORMAL', 'SPLASH', 'SIEGE', 'MAGIC'];
const ARMOR_TYPES = ['UNARMORED', 'LIGHT', 'MEDIUM', 'HEAVY', 'MAGIC'];
const DAMAGE_MULTIPLIERS = {
  PIERCE: { UNARMORED: 1.35, LIGHT: 1.20, MEDIUM: 1.00, HEAVY: 0.75, MAGIC: 0.85 },
  NORMAL: { UNARMORED: 1.10, LIGHT: 1.00, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 0.90 },
  SPLASH: { UNARMORED: 1.10, LIGHT: 1.25, MEDIUM: 1.20, HEAVY: 0.80, MAGIC: 0.85 },
  SIEGE: { UNARMORED: 0.90, LIGHT: 0.90, MEDIUM: 1.00, HEAVY: 1.35, MAGIC: 0.80 },
  MAGIC: { UNARMORED: 1.00, LIGHT: 1.05, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 1.40 },
};

const MARCH_SPEED = 0.00129375; // shared march speed — only runner differs

const DEFAULT_UNIT_META = {
  footman:  { label: 'Footman',  cost: 10, income: 2, dmg: 9,  hp: 90,  bounty: 2, armorType: 'MEDIUM',    damageType: 'NORMAL', speedPerTick: MARCH_SPEED,  atkCdTicks: 8,  range: 0.045 },
  bowman:   { label: 'Bowman',   cost: 12, income: 2, dmg: 9,  hp: 50,  bounty: 2, armorType: 'LIGHT',     damageType: 'PIERCE', speedPerTick: MARCH_SPEED,  atkCdTicks: 9,  range: 0.22  },
  ironclad: { label: 'Ironclad', cost: 15, income: 3, dmg: 11, hp: 140, bounty: 3, armorType: 'HEAVY',     damageType: 'NORMAL', speedPerTick: MARCH_SPEED,  atkCdTicks: 10, range: 0.045 },
  runner:   { label: 'Runner',   cost: 8,  income: 1, dmg: 7,  hp: 55,  bounty: 1, armorType: 'UNARMORED', damageType: 'NORMAL', speedPerTick: 0.00215625,   atkCdTicks: 7,  range: 0.045 },
  warlock:  { label: 'Warlock',  cost: 15, income: 3, dmg: 14, hp: 80,  bounty: 3, armorType: 'MAGIC',     damageType: 'MAGIC',  speedPerTick: MARCH_SPEED,  atkCdTicks: 11, range: 0.18  },
};
const DEFAULT_TOWER_META = {
  archer: { label: 'Archer', cost: 10, range: 0.25, dmg: 6.6, atkCdTicks: 12, damageType: 'PIERCE' },
  fighter: { label: 'Fighter', cost: 12, range: 0.18, dmg: 8.8, atkCdTicks: 11, damageType: 'NORMAL' },
  ballista: { label: 'Ballista', cost: 20, range: 0.35, dmg: 12.1, atkCdTicks: 14, damageType: 'SIEGE' },
  cannon: { label: 'Cannon', cost: 30, range: 0.45, dmg: 19.8, atkCdTicks: 16, damageType: 'SPLASH' },
  mage: { label: 'Mage', cost: 24, range: 0.30, dmg: 13.2, atkCdTicks: 13, damageType: 'MAGIC' },
};

const SNAPSHOT_HZ = 10;
const SNAPSHOT_MS = Math.floor(1000 / SNAPSHOT_HZ);

let unitMeta = Object.assign({}, DEFAULT_UNIT_META);
let towerMeta = Object.assign({}, DEFAULT_TOWER_META);
let tickHz = 20;
let gateHpStart = 100;

let lobbyState = 'idle';
let myCode = null;
let mySide = null;
let hasFirstSnapshot = false;
let previousState = null;
let currentState = null;
let currentStateReceivedAt = 0;
let renderLoopStarted = false;
let lastActionAck = '';
let feedbackTimeout = null;
let draggingTowerType = null;
let activePopSlot = null;

// ── Multi-lane state ──────────────────────────────────────────────────────────
let gameMode = 'classic';       // 'classic' | 'multilane'
let myLaneIndex = null;
let mlPlayerCount = 0;
let mlLaneAssignments = [];     // [{laneIndex, displayName}]
let mlCurrentState = null;
let mlPreviousState = null;
let mlCurrentStateReceivedAt = 0;
let mlHasFirstSnapshot = false;
let isSpectator = false;
let mlLobbyPlayers = [];
let mlIsHost = false;
let mlMyCode = null;

// ML grid constants (mirrors server/sim-multilane.js)
const ML_GRID_W = 10;
const ML_GRID_H = 28;
const ML_SPAWN_X = 4;
const ML_SPAWN_YG = 0;
const ML_CASTLE_X = 4;
const ML_CASTLE_YG = 27;

let viewingLaneIndex = null;    // which lane is shown full-screen in ML mode
let mlBarracksDefs = [];        // from ml_match_config.barracksLevels
let mlTileMenuJustOpened = false;
let mlWallCost = 5;
let mlMaxWalls = 100;

const lobbyEl = document.getElementById('lobby');
const btnCreate = document.getElementById('btn-create');
const btnJoin = document.getElementById('btn-join');
const btnCopyCode = document.getElementById('btn-copy-code');
const btnCopyLink = document.getElementById('btn-copy-link');
const joinInput = document.getElementById('join-code-input');
const joinNameInput = document.getElementById('join-name-input');
const roomCodeRow = document.getElementById('room-code-row');
const roomCodeDisplay = document.getElementById('room-code-display');
const lobbyStatus = document.getElementById('lobby-status');
const canvasWrap = document.getElementById('game-canvas-wrap');
const canvas = document.getElementById('game-canvas');
const ctx = canvas.getContext('2d');
const gameUi = document.getElementById('game-ui');
const hudGold = document.getElementById('hud-gold');
const hudIncome = document.getElementById('hud-income');
const hudIncomeTimer = document.getElementById('hud-income-timer');
const gameOverBanner = document.getElementById('game-over-banner');
const actionFeedback = document.getElementById('action-feedback');
const towerPopout = document.getElementById('tower-popout');
const matchupPanel = document.getElementById('matchup-panel');
const sendButtons = Array.from(document.querySelectorAll('.send-btn'));
const defenseButtons = Array.from(document.querySelectorAll('.defense-btn'));
const towerSlots = Array.from(document.querySelectorAll('.tower-slot'));

const sendBtnByType = {};
sendButtons.forEach(btn => { sendBtnByType[btn.getAttribute('data-unit-type')] = btn; });

// ── Multi-lane DOM refs ───────────────────────────────────────────────────────
const mlLobbyPanel = document.getElementById('ml-lobby-panel');
const mlPlayerList = document.getElementById('ml-player-list');
const btnCreateMl = document.getElementById('btn-create-ml');
const btnJoinMl = document.getElementById('btn-join-ml');
const mlJoinInput = document.getElementById('ml-join-code-input');
const mlNameInput = document.getElementById('ml-name-input');
const btnReadyMl = document.getElementById('btn-ready-ml');
const btnForceStart = document.getElementById('btn-force-start-ml');
const spectatorBadge = document.getElementById('spectator-badge');
const lobbyClassicSection = document.getElementById('lobby-classic-section');
const lobbyMlSection = document.getElementById('lobby-ml-section');
const btnTabClassic = document.getElementById('btn-tab-classic');
const btnTabMl = document.getElementById('btn-tab-multilane');
const mlRoomCodeRow = document.getElementById('ml-room-code-row');
const mlRoomCodeDisplay = document.getElementById('ml-room-code-display');
const btnCopyMlCode = document.getElementById('btn-copy-ml-code');
const btnCopyMlLink = document.getElementById('btn-copy-ml-link');

// ── ML grid UI DOM refs ───────────────────────────────────────────────────────
const mlBarracksHud = document.getElementById('ml-barracks-hud');
const mlBarracksLevel = document.getElementById('ml-barracks-level');
const btnBarracksUpgrade = document.getElementById('btn-ml-barracks-upgrade');
const mlLaneNav = document.getElementById('ml-lane-nav');
const mlViewingLabel = document.getElementById('ml-viewing-label');
const btnPrevLane = document.getElementById('btn-ml-prev-lane');
const btnNextLane = document.getElementById('btn-ml-next-lane');
const mlTileMenu = document.getElementById('ml-tile-menu');

function setStatus(msg, type) {
  lobbyStatus.textContent = msg;
  lobbyStatus.className = type || '';
}

function setLobbyState(state) {
  lobbyState = state;
  const inFlight = state === 'creating' || state === 'joining';
  btnCreate.disabled = inFlight;
  btnJoin.disabled = inFlight;
  joinInput.disabled = inFlight;
  joinNameInput.disabled = inFlight;
}

function hideLobby() {
  lobbyEl.classList.add('hidden');
  setTimeout(() => { lobbyEl.style.display = 'none'; }, 420);
}

function showLobby() {
  lobbyEl.style.display = 'flex';
  requestAnimationFrame(() => { lobbyEl.classList.remove('hidden'); });
}

function showGameUi() {
  canvasWrap.style.display = 'block';
  gameUi.style.display = 'block';
}

function hideGameUi() {
  canvasWrap.style.display = 'none';
  gameUi.style.display = 'none';
}

function initCanvas() {
  function resize() {
    canvas.width = canvasWrap.clientWidth || window.innerWidth;
    canvas.height = canvasWrap.clientHeight || window.innerHeight;
    if (!hasFirstSnapshot) drawWaiting();
  }
  window.addEventListener('resize', resize);
  resize();
}

function startRenderLoop() {
  if (renderLoopStarted) return;
  renderLoopStarted = true;
  function frame() {
    renderFrame();
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
}

function drawWaiting() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#07090e';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#445868';
  ctx.font = '14px "Share Tech Mono", monospace';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText('Waiting for state...', canvas.width / 2, canvas.height / 2);
}

function applyMatchConfig(config) {
  if (!config || typeof config !== 'object') return;
  if (Number.isFinite(config.tickHz)) tickHz = config.tickHz;
  if (Number.isFinite(config.gateHpStart)) gateHpStart = config.gateHpStart;

  if (config.unitDefs) {
    const next = {};
    Object.keys(config.unitDefs).forEach(k => {
      const d = config.unitDefs[k];
      next[k] = {
        label: cap(k),
        cost: Number(d.cost) || 0,
        income: Number(d.income) || 0,
        dmg: Number(d.dmg) || 0,
        hp: Number(d.hp) || 0,
        bounty: Number(d.bounty) || 0,
        armorType: String(d.armorType || ''),
        damageType: String(d.damageType || 'NORMAL'),
        speedPerTick: Number(d.speedPerTick) || (Number(d.pathSpeed) / ML_GRID_H) || 0,
        atkCdTicks: Number(d.atkCdTicks) || 0,
        range: Number(d.range) || 0,
      };
    });
    unitMeta = Object.assign({}, unitMeta, next);
  }
  if (config.towerDefs) {
    const next = {};
    Object.keys(config.towerDefs).forEach(k => {
      const d = config.towerDefs[k];
      next[k] = {
        label: cap(k),
        cost: Number(d.cost) || 0,
        range: Number(d.range) || 0,
        dmg: Number(d.dmg) || 0,
        atkCdTicks: Number(d.atkCdTicks) || 0,
        damageType: String(d.damageType || 'NORMAL'),
      };
    });
    towerMeta = Object.assign({}, towerMeta, next);
  }
  if (config.barracksLevels && Array.isArray(config.barracksLevels)) {
    mlBarracksDefs = config.barracksLevels;
  }
  if (Number.isFinite(config.wallCost)) mlWallCost = config.wallCost;
  if (Number.isFinite(config.maxWalls)) mlMaxWalls = config.maxWalls;
  updateActionLabels();
}

function updateActionLabels() {
  sendButtons.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const m = unitMeta[type] || DEFAULT_UNIT_META[type];
    const hot = UNIT_TYPES.indexOf(type) + 1;
    const aps = m.atkCdTicks ? (tickHz / m.atkCdTicks) : 0;
    const laneSpeed = m.speedPerTick ? (m.speedPerTick * tickHz * 100) : 0;
    btn.textContent =
      hot + ' ' + m.label + '\n'
      + 'Cost ' + m.cost + 'g\n'
      + 'HP ' + Math.round(m.hp || 0) + '\n'
      + 'DMG ' + Math.round(m.dmg || 0) + ' (' + (m.damageType || 'NORMAL') + ')\n'
      + 'Armor ' + (m.armorType || '-') + '\n'
      + 'Atk ' + aps.toFixed(2) + '/s\n'
      + 'Move ' + laneSpeed.toFixed(2) + '%/s\n'
      + (gameMode !== 'multilane' ? 'Range ' + Math.round((m.range || 0) * 100) + '%\n' : '')
      + 'Income +' + m.income + 'g\n'
      + 'Bounty +' + (m.bounty || m.income || 0) + 'g';
  });
  defenseButtons.forEach(btn => {
    const type = btn.getAttribute('data-tower-type');
    const m = towerMeta[type] || DEFAULT_TOWER_META[type];
    const aps = m.atkCdTicks ? (tickHz / m.atkCdTicks) : 0;
    btn.textContent =
      m.label + '\n'
      + 'Cost ' + m.cost + 'g\n'
      + 'DMG ' + Number(m.dmg || 0).toFixed(1) + ' (' + (m.damageType || 'NORMAL') + ')\n'
      + 'Atk ' + aps.toFixed(2) + '/s\n'
      + 'Range ' + Math.round((m.range || 0) * 100) + '%';
  });
  renderMatchupPanel();
  updateStatsPanel();
}

function updateStatsPanel() {
  const panel = document.getElementById('stats-panel');
  if (!panel) return;
  const isML = (gameMode === 'multilane');
  let html = '';

  if (isML) {
    html += '<div class="stats-section-hdr">Wall</div>';
    html += '<div class="stats-wall-row">'
      + '<span class="stats-name">Wall</span>'
      + '<span class="stats-cost">' + mlWallCost + 'g</span>'
      + '<span class="stats-max">max ' + mlMaxWalls + '</span>'
      + '</div>';
    html += '<div class="stats-divider"></div>';
  }

  html += '<div class="stats-section-hdr">Towers</div>';
  TOWER_TYPES.forEach(type => {
    const m = towerMeta[type];
    if (!m) return;
    const rangeStr = isML
      ? m.range.toFixed(1) + 't'
      : Math.round(m.range * 100) + '%';
    html += '<div class="stats-row">'
      + '<span class="stats-name">' + m.label + '</span>'
      + '<span class="stats-cost">' + m.cost + 'g</span>'
      + '<span class="stats-type">' + m.damageType + '</span>'
      + '<span class="stats-dmg">' + Number(m.dmg).toFixed(1) + '</span>'
      + '<span class="stats-range">' + rangeStr + '</span>'
      + '</div>';
  });

  panel.innerHTML = html;
}

function renderMatchupPanel() {
  if (!matchupPanel) return;

  const getStrongWeakForDamage = function (damageType) {
    const row = DAMAGE_MULTIPLIERS[damageType] || {};
    let strong = { armor: '-', value: -Infinity };
    let weak = { armor: '-', value: Infinity };
    ARMOR_TYPES.forEach(a => {
      const v = Number(row[a]);
      if (!Number.isFinite(v)) return;
      if (v > strong.value) strong = { armor: a, value: v };
      if (v < weak.value) weak = { armor: a, value: v };
    });
    return { strong, weak };
  };

  const getStrongWeakForArmor = function (armorType) {
    let weakTo = { damage: '-', value: -Infinity }; // takes most
    let strongVs = { damage: '-', value: Infinity }; // takes least
    DAMAGE_TYPES.forEach(d => {
      const v = Number((DAMAGE_MULTIPLIERS[d] || {})[armorType]);
      if (!Number.isFinite(v)) return;
      if (v > weakTo.value) weakTo = { damage: d, value: v };
      if (v < strongVs.value) strongVs = { damage: d, value: v };
    });
    return { weakTo, strongVs };
  };

  // Preserve expanded state across re-renders
  const wasExpanded = matchupPanel.classList.contains('expanded');

  // All values come exclusively from DAMAGE_MULTIPLIERS/DAMAGE_TYPES/ARMOR_TYPES constants
  let bodyHtml = '<div class="matchup-title">Damage vs Armor</div>';
  bodyHtml += '<table><thead><tr><th>DMG \\ ARM</th>';
  ARMOR_TYPES.forEach(a => { bodyHtml += '<th>' + a + '</th>'; });
  bodyHtml += '</tr></thead><tbody>';
  DAMAGE_TYPES.forEach(d => {
    bodyHtml += '<tr><th>' + d + '</th>';
    ARMOR_TYPES.forEach(a => {
      const v = Number((DAMAGE_MULTIPLIERS[d] || {})[a]);
      const cls = v > 1 ? 'strong' : (v < 1 ? 'weak' : '');
      bodyHtml += '<td class="' + cls + '">' + (Number.isFinite(v) ? v.toFixed(2) : '-') + '</td>';
    });
    bodyHtml += '</tr>';
  });
  bodyHtml += '</tbody></table>';

  bodyHtml += '<div class="matchup-title">Damage Type Summary</div>';
  DAMAGE_TYPES.forEach(d => {
    const sw = getStrongWeakForDamage(d);
    bodyHtml += '<div class="matchup-note">' + d + ': strong vs ' + sw.strong.armor + ' (' + sw.strong.value.toFixed(2) + '), weak vs ' + sw.weak.armor + ' (' + sw.weak.value.toFixed(2) + ')</div>';
  });

  bodyHtml += '<div class="matchup-title" style="margin-top:6px">Armor Type Summary</div>';
  ARMOR_TYPES.forEach(a => {
    const sw = getStrongWeakForArmor(a);
    bodyHtml += '<div class="matchup-note">' + a + ': strong vs ' + sw.strongVs.damage + ' (' + sw.strongVs.value.toFixed(2) + '), weak vs ' + sw.weakTo.damage + ' (' + sw.weakTo.value.toFixed(2) + ')</div>';
  });

  matchupPanel.innerHTML =
    '<button class="matchup-toggle-btn" type="button">'
    + '<span class="matchup-toggle-arrow">&#x25B6;</span>'
    + ' Matchup Table'
    + '</button>'
    + '<div class="matchup-body">' + bodyHtml + '</div>';

  if (wasExpanded) matchupPanel.classList.add('expanded');

  // Re-attach toggle click listener after innerHTML replacement
  const toggleBtn = matchupPanel.querySelector('.matchup-toggle-btn');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      matchupPanel.classList.toggle('expanded');
    });
  }
}

function localizeState(state) {
  if (!mySide || !state || !state.players) return null;
  const enemySide = mySide === 'bottom' ? 'top' : 'bottom';
  const meRaw = state.players[mySide] || {};
  const enRaw = state.players[enemySide] || {};
  const localizeTowers = function (raw) {
    const t = raw && raw.towers ? raw.towers : {};
    const norm = function (entry) {
      if (!entry) return { type: null, level: 0 };
      if (typeof entry === 'string') return { type: entry || null, level: entry ? 1 : 0 };
      return { type: entry.type || null, level: Number(entry.level) || (entry.type ? 1 : 0) };
    };
    return {
      left_outer: norm(t.left_outer),
      left_mid: norm(t.left_mid),
      left_inner: norm(t.left_inner),
      right_inner: norm(t.right_inner),
      right_mid: norm(t.right_mid),
      right_outer: norm(t.right_outer),
    };
  };

  const units = (state.units || []).map(u => {
    if (mySide === 'bottom') return { id: u.id, side: u.side, type: u.type, y: u.y, hp: u.hp, maxHp: u.maxHp };
    return { id: u.id, side: u.side === 'top' ? 'bottom' : 'top', type: u.type, y: 1 - u.y, hp: u.hp, maxHp: u.maxHp };
  });

  const projectiles = (state.projectiles || []).map(p => {
    if (mySide === 'bottom') return p;
    return {
      id: p.id,
      side: p.side === 'top' ? 'bottom' : 'top',
      slot: p.slot,
      projectileType: p.projectileType,
      damageType: p.damageType,
      sourceKind: p.sourceKind,
      x: p.x,
      y: 1 - p.y,
    };
  });

  return {
    tick: Number(state.tick) || 0,
    incomeTicksRemaining: Number.isFinite(state.incomeTicksRemaining) ? state.incomeTicksRemaining : (30 * tickHz),
    me: {
      gold: Number(meRaw.gold) || 0,
      income: Number(meRaw.income) || 0,
      gateHp: Number(meRaw.gateHp) || gateHpStart,
      towers: localizeTowers(meRaw),
    },
    enemy: {
      gold: Number(enRaw.gold) || 0,
      income: Number(enRaw.income) || 0,
      gateHp: Number(enRaw.gateHp) || gateHpStart,
      towers: localizeTowers(enRaw),
    },
    units,
    projectiles,
  };
}

function lerp(a, b, t) { return a + (b - a) * t; }

function buildInterpolatedState(nowMs) {
  if (!currentState) return null;
  if (!previousState) return currentState;
  const alpha = Math.max(0, Math.min(1, (nowMs - currentStateReceivedAt) / SNAPSHOT_MS));
  const prevById = new Map();
  previousState.units.forEach(u => prevById.set(u.id, u));
  const units = currentState.units.map(u => {
    const prev = prevById.get(u.id);
    if (!prev) return u;
    return { id: u.id, side: u.side, type: u.type, y: lerp(prev.y, u.y, alpha), hp: lerp(prev.hp, u.hp, alpha), maxHp: u.maxHp };
  });
  return {
    tick: currentState.tick,
    incomeTicksRemaining: currentState.incomeTicksRemaining,
    me: currentState.me,
    enemy: currentState.enemy,
    units,
    projectiles: currentState.projectiles,
  };
}

function renderFrame() {
  if (gameMode === 'multilane') return renderFrameML();
  if (!hasFirstSnapshot || !mySide) return drawWaiting();
  const local = buildInterpolatedState(performance.now());
  if (!local) return drawWaiting();

  drawBattlefield(local);
  drawUnits(local.units);
  drawProjectiles(local.projectiles);
  updateHud(local);
  updateSendButtons(local.me.gold);
  updateDefenseButtons(local.me.gold, local.me.towers);
  updateTowerSlots(local.me.towers);
}

function drawBattlefield(local) {
  const w = canvas.width;
  const h = canvas.height;
  const laneX = w * 0.5;
  const laneW = Math.max(80, Math.min(180, w * 0.18));

  ctx.clearRect(0, 0, w, h);

  // ── Background gradient ───────────────────────────────────
  const bg = ctx.createLinearGradient(0, 0, 0, h);
  bg.addColorStop(0,   '#0c0f14');
  bg.addColorStop(0.5, '#111e13');
  bg.addColorStop(1,   '#0f0d0a');
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, w, h);

  // ── Territory zone tints ──────────────────────────────────
  const eZone = ctx.createLinearGradient(0, 0, 0, h * 0.44);
  eZone.addColorStop(0, 'rgba(160,28,28,0.16)');
  eZone.addColorStop(1, 'rgba(160,28,28,0)');
  ctx.fillStyle = eZone; ctx.fillRect(0, 0, w, h * 0.44);
  const mZone = ctx.createLinearGradient(0, h * 0.56, 0, h);
  mZone.addColorStop(0, 'rgba(18,140,120,0)');
  mZone.addColorStop(1, 'rgba(18,140,120,0.16)');
  ctx.fillStyle = mZone; ctx.fillRect(0, h * 0.56, w, h * 0.44);

  // ── Side terrain: castle walls + trees + torches ──────────
  drawBattlefieldSides(w, h, laneX, laneW);

  // ── Lane: cobblestone path ────────────────────────────────
  ctx.fillStyle = '#12221a';
  ctx.fillRect(laneX - laneW / 2, 0, laneW, h);
  drawLaneCobblestones(laneX, laneW, h);

  // Lane edge glow + border
  ctx.save();
  const lg1 = ctx.createLinearGradient(laneX - laneW / 2, 0, laneX - laneW / 2 + 14, 0);
  lg1.addColorStop(0, 'rgba(40,192,100,0.14)'); lg1.addColorStop(1, 'transparent');
  ctx.fillStyle = lg1; ctx.fillRect(laneX - laneW / 2 + 1, 0, 14, h);
  const lg2 = ctx.createLinearGradient(laneX + laneW / 2 - 14, 0, laneX + laneW / 2, 0);
  lg2.addColorStop(0, 'transparent'); lg2.addColorStop(1, 'rgba(40,192,100,0.14)');
  ctx.fillStyle = lg2; ctx.fillRect(laneX + laneW / 2 - 13, 0, 14, h);
  ctx.strokeStyle = '#284030'; ctx.lineWidth = 2;
  ctx.strokeRect(laneX - laneW / 2, 0, laneW, h);
  ctx.restore();

  // ── Castle gate structures ────────────────────────────────
  drawCastleGateStructure(local.enemy.gateHp, gateHpStart, laneX, laneW, w, h * 0.06, '#ff3a3a', true);
  drawCastleGateStructure(local.me.gateHp,    gateHpStart, laneX, laneW, w, h * 0.94, '#28c0b0', false);

  // ── Enemy tower icons + my tower slot positions ───────────
  drawTowerIcons(local.enemy.towers, false, laneX, laneW, h);
  updateTowerSlotPositions(laneX, laneW, h);

  // ── Debug overlay ─────────────────────────────────────────
  ctx.fillStyle = 'rgba(140,180,200,0.3)';
  ctx.font = '11px "Share Tech Mono", monospace';
  ctx.textAlign = 'right'; ctx.textBaseline = 'top';
  ctx.fillText('T:' + local.tick + '  U:' + local.units.length, w - 10, 10);
  if (lastActionAck) ctx.fillText(lastActionAck, w - 10, 26);
}

function drawLaneCobblestones(laneX, laneW, h) {
  ctx.save();
  const stoneH = 20;
  const cols = Math.max(3, Math.floor(laneW / 18));
  const stoneW = laneW / cols;
  const lx0 = laneX - laneW / 2;
  // Subtle stone-shade variation per block
  for (let row = 0; row * stoneH < h; row++) {
    const sy = row * stoneH;
    const offsetX = (row % 2) * (stoneW / 2);
    for (let col = -1; col <= cols + 1; col++) {
      const sx = lx0 + col * stoneW + offsetX;
      if (sx + stoneW < lx0 || sx > lx0 + laneW) continue;
      const shade = (row * 7 + col * 13 + row % 2) % 3;
      if (shade > 0) {
        ctx.fillStyle = shade === 1 ? 'rgba(30,70,40,0.09)' : 'rgba(8,28,16,0.07)';
        const rx = Math.max(sx + 1, lx0 + 1);
        const rw = Math.min(sx + stoneW - 2, lx0 + laneW - 1) - rx;
        if (rw > 0) ctx.fillRect(rx, sy + 1, rw, Math.min(stoneH - 2, h - sy - 1));
      }
    }
  }
  // Mortar joint lines — horizontal
  ctx.strokeStyle = 'rgba(8,26,16,0.6)';
  ctx.lineWidth = 1;
  for (let row = 1; row * stoneH < h; row++) {
    ctx.beginPath();
    ctx.moveTo(lx0, row * stoneH);
    ctx.lineTo(lx0 + laneW, row * stoneH);
    ctx.stroke();
  }
  // Mortar joint lines — vertical (offset alternating rows)
  for (let row = 0; row * stoneH < h; row++) {
    const offsetX = (row % 2) * (stoneW / 2);
    for (let col = 1; col <= cols; col++) {
      const sx = lx0 + col * stoneW + offsetX;
      if (sx <= lx0 || sx >= lx0 + laneW) continue;
      ctx.beginPath();
      ctx.moveTo(sx, row * stoneH);
      ctx.lineTo(sx, Math.min((row + 1) * stoneH, h));
      ctx.stroke();
    }
  }
  ctx.restore();
}

// Castle banner flag (called by drawCastleGateStructure; x,y = flag base point)
function drawBattleFlag(x, y, color) {
  ctx.save();
  // Pole
  ctx.strokeStyle = 'rgba(180,160,100,0.85)';
  ctx.lineWidth = 1.5;
  ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x, y - 30); ctx.stroke();
  // Crossbar
  ctx.beginPath(); ctx.moveTo(x, y - 30); ctx.lineTo(x + 16, y - 30); ctx.stroke();
  // Banner
  ctx.fillStyle = color; ctx.globalAlpha = 0.92;
  ctx.beginPath();
  ctx.moveTo(x, y - 30);
  ctx.lineTo(x + 16, y - 24);
  ctx.lineTo(x + 16, y - 14);
  ctx.lineTo(x, y - 20);
  ctx.closePath(); ctx.fill();
  // Diamond emblem
  ctx.fillStyle = 'rgba(255,255,255,0.55)'; ctx.globalAlpha = 0.55;
  ctx.beginPath();
  ctx.moveTo(x + 8, y - 27); ctx.lineTo(x + 12, y - 22);
  ctx.lineTo(x + 8, y - 17); ctx.lineTo(x + 4, y - 22);
  ctx.closePath(); ctx.fill();
  ctx.globalAlpha = 1;
  ctx.restore();
}

// Battlefield sides: stone wall strips + trees + ruin rocks + torches
function drawBattlefieldSides(w, h, laneX, laneW) {
  const leftEdge = laneX - laneW / 2;
  const rightEdge = laneX + laneW / 2;
  const sideW = leftEdge;

  // ── Narrow stone wall strips along canvas edges ───────────
  const wallW = 18;
  const stone = '#252f3c'; const mortar = '#141e2c';
  ctx.fillStyle = stone;
  ctx.fillRect(0, 0, wallW, h);
  ctx.fillRect(w - wallW, 0, wallW, h);
  ctx.strokeStyle = mortar; ctx.lineWidth = 1;
  for (let sy = 0; sy < h; sy += 24) {
    const row = Math.floor(sy / 24);
    ctx.beginPath(); ctx.moveTo(0, sy); ctx.lineTo(wallW, sy); ctx.stroke();
    if (row % 2 === 0) { ctx.beginPath(); ctx.moveTo(wallW / 2, sy); ctx.lineTo(wallW / 2, Math.min(sy + 24, h)); ctx.stroke(); }
    ctx.beginPath(); ctx.moveTo(w - wallW, sy); ctx.lineTo(w, sy); ctx.stroke();
    if (row % 2 === 1) { ctx.beginPath(); ctx.moveTo(w - wallW / 2, sy); ctx.lineTo(w - wallW / 2, Math.min(sy + 24, h)); ctx.stroke(); }
  }
  // Inner-edge merlons on wall strips (facing the battlefield)
  const mw = 8, mh = 12, mg = 6;
  ctx.fillStyle = '#2c3848';
  for (let my = 6; my < h; my += mw + mg) {
    ctx.fillRect(wallW - 2, my, 6, mh); ctx.strokeStyle = mortar; ctx.lineWidth = 1; ctx.strokeRect(wallW - 2, my, 6, mh);
    ctx.fillRect(w - wallW - 4, my, 6, mh); ctx.strokeRect(w - wallW - 4, my, 6, mh);
  }

  // ── Trees (deterministic positions) ───────────────────────
  const treeDefs = [
    { rx: -0.38, ry: 0.11, s: 1.0 }, { rx: -0.22, ry: 0.26, s: 0.8 },
    { rx: -0.45, ry: 0.37, s: 1.1 }, { rx: -0.16, ry: 0.52, s: 0.9 },
    { rx: -0.40, ry: 0.63, s: 1.0 }, { rx: -0.24, ry: 0.75, s: 0.85 },
    { rx: -0.42, ry: 0.87, s: 0.95 },
    { rx:  0.34, ry: 0.14, s: 0.9 }, { rx:  0.20, ry: 0.30, s: 1.1 },
    { rx:  0.41, ry: 0.43, s: 0.85 }, { rx:  0.17, ry: 0.57, s: 1.0 },
    { rx:  0.37, ry: 0.69, s: 0.9 }, { rx:  0.19, ry: 0.81, s: 0.95 },
    { rx:  0.39, ry: 0.92, s: 1.0 },
  ];
  treeDefs.forEach(t => drawTree(laneX + t.rx * (sideW + wallW) * 1.5, t.ry * h, t.s));

  // ── Ruin rocks ────────────────────────────────────────────
  const rockDefs = [
    { rx: -0.14, ry: 0.20 }, { rx: -0.30, ry: 0.47 }, { rx: -0.12, ry: 0.71 }, { rx: -0.27, ry: 0.84 },
    { rx:  0.13, ry: 0.23 }, { rx:  0.26, ry: 0.53 }, { rx:  0.11, ry: 0.74 }, { rx:  0.29, ry: 0.90 },
  ];
  rockDefs.forEach(r => drawRuinRock(laneX + r.rx * (sideW + wallW) * 1.4, r.ry * h));

  // ── Torches along lane edges ───────────────────────────────
  [0.17, 0.38, 0.60, 0.81].forEach(ty => {
    drawTorch(leftEdge - 5, ty * h);
    drawTorch(rightEdge + 5, ty * h);
  });
}

function drawTree(x, y, scale) {
  ctx.save(); ctx.translate(x, y);
  ctx.fillStyle = '#29180a';
  ctx.fillRect(-2 * scale, 0, 4 * scale, 18 * scale);
  ctx.fillStyle = '#0e2208';
  ctx.beginPath(); ctx.moveTo(-13 * scale, 4 * scale); ctx.lineTo(0, -22 * scale); ctx.lineTo(13 * scale, 4 * scale); ctx.closePath(); ctx.fill();
  ctx.fillStyle = '#142a0d';
  ctx.beginPath(); ctx.moveTo(-9 * scale, -10 * scale); ctx.lineTo(0, -35 * scale); ctx.lineTo(9 * scale, -10 * scale); ctx.closePath(); ctx.fill();
  ctx.fillStyle = 'rgba(40,80,20,0.22)';
  ctx.beginPath(); ctx.moveTo(-3 * scale, -20 * scale); ctx.lineTo(0, -35 * scale); ctx.lineTo(3 * scale, -20 * scale); ctx.closePath(); ctx.fill();
  ctx.restore();
}

function drawRuinRock(x, y) {
  ctx.save(); ctx.translate(x, y);
  ctx.fillStyle = '#283042'; ctx.strokeStyle = '#192030'; ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(-10, 4); ctx.lineTo(-9, -5); ctx.lineTo(-2, -8); ctx.lineTo(6, -4); ctx.lineTo(9, 1); ctx.lineTo(7, 5); ctx.lineTo(-8, 6); ctx.closePath(); ctx.fill(); ctx.stroke();
  ctx.fillStyle = '#323c50';
  ctx.beginPath(); ctx.moveTo(7, 0); ctx.lineTo(9, -3); ctx.lineTo(13, -1); ctx.lineTo(11, 3); ctx.lineTo(8, 4); ctx.closePath(); ctx.fill(); ctx.stroke();
  ctx.strokeStyle = 'rgba(255,255,255,0.05)'; ctx.beginPath(); ctx.moveTo(-6, -3); ctx.lineTo(-2, 1); ctx.stroke();
  ctx.restore();
}

function drawTorch(x, y) {
  ctx.save(); ctx.translate(x, y);
  ctx.fillStyle = '#3c4858'; ctx.fillRect(-4, -22, 8, 5);
  ctx.fillStyle = '#2a1808'; ctx.fillRect(-1.5, -18, 3, 18);
  ctx.globalAlpha = 0.08; ctx.fillStyle = '#ff8800';
  ctx.beginPath(); ctx.arc(0, -24, 16, 0, Math.PI * 2); ctx.fill();
  ctx.globalAlpha = 0.22; ctx.fillStyle = '#ffaa00';
  ctx.beginPath(); ctx.arc(0, -25, 8, 0, Math.PI * 2); ctx.fill();
  ctx.globalAlpha = 1;
  ctx.fillStyle = '#ff9000';
  ctx.beginPath(); ctx.moveTo(-3, -20); ctx.quadraticCurveTo(-4, -28, 0, -33); ctx.quadraticCurveTo(4, -28, 3, -20); ctx.closePath(); ctx.fill();
  ctx.fillStyle = '#fff080';
  ctx.beginPath(); ctx.moveTo(-1.5, -20); ctx.quadraticCurveTo(-2, -26, 0, -30); ctx.quadraticCurveTo(2, -26, 1.5, -20); ctx.closePath(); ctx.fill();
  ctx.restore();
}

function drawWallSegment(x, y, w, h) {
  ctx.fillStyle = '#252e3a';
  ctx.fillRect(x, y, w, h);
  ctx.strokeStyle = '#1a2230'; ctx.lineWidth = 1;
  ctx.strokeRect(x, y, w, h);
  ctx.beginPath(); ctx.moveTo(x, y + h / 2); ctx.lineTo(x + w, y + h / 2); ctx.stroke();
  ctx.fillStyle = 'rgba(255,255,255,0.04)'; ctx.fillRect(x, y, w, 2);
  // Battlements
  const mw = 8, mh = 8;
  ctx.fillStyle = '#2e3a4a';
  for (let mx = x + 2; mx + mw <= x + w; mx += mw + 5) {
    ctx.fillRect(mx, y - mh + 1, mw, mh);
    ctx.strokeStyle = '#1a2230'; ctx.lineWidth = 1; ctx.strokeRect(mx, y - mh + 1, mw, mh);
  }
}

function drawCastleGateStructure(hp, maxHp, laneX, laneW, canvasW, centerY, color, isEnemy) {
  const ratio = Math.max(0, Math.min(1, hp / maxHp));
  const gateH = 48, towerW = 30, merlonH = 13, merlonW = 9, merlonGap = 6;
  const stone = '#25303e', stoneDark = '#1e2836', stoneLight = '#2e3c50', mortar = '#131d2b';
  const gateTop = isEnemy ? centerY - 6 : centerY - gateH + 6;

  // ── Full-width wall (canvas-edge to lane) ─────────────────
  const wallH = Math.floor(gateH * 0.52), wallTop = gateTop + (gateH - wallH) / 2;
  ctx.fillStyle = stoneDark;
  ctx.fillRect(0, wallTop, laneX - laneW / 2 - towerW, wallH);
  ctx.strokeStyle = mortar; ctx.lineWidth = 1;
  ctx.strokeRect(0, wallTop, laneX - laneW / 2 - towerW, wallH);
  ctx.fillRect(laneX + laneW / 2 + towerW, wallTop, canvasW - laneX - laneW / 2 - towerW, wallH);
  ctx.strokeRect(laneX + laneW / 2 + towerW, wallTop, canvasW - laneX - laneW / 2 - towerW, wallH);
  // Wall battlements
  ctx.fillStyle = stone;
  const bY = wallTop - merlonH + 2;
  for (let mx = 4; mx + merlonW < laneX - laneW / 2 - towerW; mx += merlonW + merlonGap) {
    ctx.fillRect(mx, bY, merlonW, merlonH); ctx.strokeStyle = mortar; ctx.lineWidth = 1; ctx.strokeRect(mx, bY, merlonW, merlonH);
  }
  for (let mx = laneX + laneW / 2 + towerW + 4; mx + merlonW < canvasW; mx += merlonW + merlonGap) {
    ctx.fillRect(mx, bY, merlonW, merlonH); ctx.strokeStyle = mortar; ctx.lineWidth = 1; ctx.strokeRect(mx, bY, merlonW, merlonH);
  }

  // ── Flanking towers ───────────────────────────────────────
  const lTX = laneX - laneW / 2 - towerW, rTX = laneX + laneW / 2;
  [lTX, rTX].forEach(tx => {
    ctx.fillStyle = stone;
    ctx.fillRect(tx, gateTop, towerW, gateH);
    ctx.strokeStyle = mortar; ctx.lineWidth = 1; ctx.strokeRect(tx, gateTop, towerW, gateH);
    // Horizontal stone joints
    ctx.beginPath(); ctx.moveTo(tx, gateTop + gateH / 3); ctx.lineTo(tx + towerW, gateTop + gateH / 3); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(tx, gateTop + gateH * 2 / 3); ctx.lineTo(tx + towerW, gateTop + gateH * 2 / 3); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(tx + towerW / 2, gateTop); ctx.lineTo(tx + towerW / 2, gateTop + gateH / 3); ctx.stroke();
    // Highlight
    ctx.fillStyle = 'rgba(255,255,255,0.04)'; ctx.fillRect(tx, gateTop, 2, gateH);
    // Arrow slit
    ctx.fillStyle = '#0a1018';
    ctx.fillRect(tx + towerW / 2 - 1, gateTop + 6, 2, 9);
    ctx.fillRect(tx + towerW / 2 - 3, gateTop + 10, 6, 2);
    // Tower battlements
    ctx.fillStyle = stoneLight;
    for (let mx = tx + 2; mx + merlonW <= tx + towerW; mx += merlonW + merlonGap) {
      ctx.fillRect(mx, gateTop - merlonH + 1, merlonW, merlonH);
      ctx.strokeStyle = mortar; ctx.lineWidth = 1; ctx.strokeRect(mx, gateTop - merlonH + 1, merlonW, merlonH);
    }
  });

  // ── Gate portcullis ───────────────────────────────────────
  ctx.fillStyle = '#090e16';
  ctx.fillRect(laneX - laneW / 2, gateTop, laneW, gateH);
  // HP fill tint
  if (ratio > 0) {
    const hpH = gateH * ratio, hpY = isEnemy ? gateTop : gateTop + gateH - hpH;
    ctx.fillStyle = color; ctx.globalAlpha = 0.2;
    ctx.fillRect(laneX - laneW / 2, hpY, laneW, hpH); ctx.globalAlpha = 1;
  }
  // Portcullis bars (vertical)
  const barSpacing = Math.max(8, Math.floor(laneW / 8));
  ctx.strokeStyle = 'rgba(75,95,115,0.75)'; ctx.lineWidth = 2;
  for (let bx = laneX - laneW / 2 + barSpacing; bx < laneX + laneW / 2; bx += barSpacing) {
    ctx.beginPath(); ctx.moveTo(bx, gateTop); ctx.lineTo(bx, gateTop + gateH); ctx.stroke();
  }
  // Portcullis crossbars (horizontal)
  ctx.lineWidth = 1.5;
  for (let j = 1; j <= 3; j++) {
    const by = gateTop + j * gateH / 4;
    ctx.beginPath(); ctx.moveTo(laneX - laneW / 2, by); ctx.lineTo(laneX + laneW / 2, by); ctx.stroke();
  }
  // Gate frame (colored border)
  ctx.strokeStyle = color; ctx.globalAlpha = 0.5; ctx.lineWidth = 2;
  ctx.strokeRect(laneX - laneW / 2, gateTop, laneW, gateH); ctx.globalAlpha = 1;

  // ── Banner flags on tower peaks ───────────────────────────
  drawBattleFlag(lTX + towerW / 2, gateTop - merlonH + 1, color);
  drawBattleFlag(rTX + towerW / 2, gateTop - merlonH + 1, color);

  // ── HP bar ────────────────────────────────────────────────
  const hpBarH = 7, hpBarY = isEnemy ? gateTop + gateH + 5 : gateTop - hpBarH - 5;
  ctx.fillStyle = '#0c1820'; ctx.fillRect(laneX - laneW / 2, hpBarY, laneW, hpBarH);
  ctx.fillStyle = color; ctx.fillRect(laneX - laneW / 2, hpBarY, laneW * ratio, hpBarH);
  ctx.strokeStyle = color; ctx.globalAlpha = 0.3; ctx.lineWidth = 1;
  ctx.strokeRect(laneX - laneW / 2, hpBarY, laneW, hpBarH); ctx.globalAlpha = 1;

  // ── Label ─────────────────────────────────────────────────
  ctx.fillStyle = color; ctx.font = 'bold 10px "Cinzel", serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = isEnemy ? 'bottom' : 'top';
  const labelY = isEnemy ? gateTop - merlonH - 4 : gateTop + gateH + merlonH / 2 + 8;
  ctx.fillText((isEnemy ? 'ENEMY GATE' : 'YOUR GATE') + '  ♥ ' + Math.max(0, Math.floor(hp)), laneX, labelY);
}

function drawTowerIcons(towers, isMine, laneX, laneW, h) {
  const y = isMine ? h * 0.90 : h * 0.04;
  if (isMine) return;
  drawTowerIcon(towers.left_outer, laneX - laneW / 2 - 84, y);
  drawTowerIcon(towers.left_mid, laneX - laneW / 2 - 54, y);
  drawTowerIcon(towers.left_inner, laneX - laneW / 2 - 24, y);
  drawTowerIcon(towers.right_inner, laneX + laneW / 2 + 24, y);
  drawTowerIcon(towers.right_mid, laneX + laneW / 2 + 54, y);
  drawTowerIcon(towers.right_outer, laneX + laneW / 2 + 84, y);
}

// Draws tower art centered at (cx, cy) scaled by `scale`. No background.
function drawTowerArt(type, cx, cy, scale) {
  const col = towerColor(type);
  const stone = '#2a3344';
  const dark = '#060d18';
  const hi = 'rgba(255,255,255,0.2)';
  ctx.save();
  ctx.translate(cx, cy);
  if (scale && scale !== 1) ctx.scale(scale, scale);
  if (type === 'archer') {
    // Stone base + shaft
    ctx.fillStyle = stone;
    ctx.fillRect(-5, 4, 10, 6);
    ctx.fillRect(-3, -4, 6, 9);
    // Three merlons (battlements)
    ctx.fillStyle = col;
    ctx.fillRect(-5, -10, 3, 7);
    ctx.fillRect(-1, -10, 2, 5);
    ctx.fillRect(3, -10, 3, 7);
    // Arrow slit cross
    ctx.fillStyle = dark;
    ctx.fillRect(-0.5, -3, 1, 5);
    ctx.fillRect(-2.5, -1, 5, 1.5);
    // Stone highlight
    ctx.fillStyle = hi;
    ctx.fillRect(-3, -4, 1, 9);
    ctx.fillRect(-2, -10, 1, 7);
  } else if (type === 'fighter') {
    // Wide fort base + body
    ctx.fillStyle = stone;
    ctx.fillRect(-7, 4, 14, 6);
    ctx.fillRect(-5, -3, 10, 8);
    // Two large merlons
    ctx.fillStyle = col;
    ctx.fillRect(-7, -10, 5, 8);
    ctx.fillRect(2, -10, 5, 8);
    // Embrasure gap (dark)
    ctx.fillStyle = dark;
    ctx.fillRect(-2, -9, 4, 7);
    // Sword emblem in body
    ctx.strokeStyle = col;
    ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.moveTo(0, -1); ctx.lineTo(0, 5); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(-3, 1.5); ctx.lineTo(3, 1.5); ctx.stroke();
    ctx.fillStyle = col;
    ctx.beginPath(); ctx.arc(0, -2, 1.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = hi;
    ctx.fillRect(-5, -3, 1, 8);
  } else if (type === 'ballista') {
    // Platform
    ctx.fillStyle = stone;
    ctx.fillRect(-6, 3, 12, 7);
    ctx.fillRect(-4, -2, 8, 6);
    // Ballista arms (V-spread pointing up)
    ctx.strokeStyle = col;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(-7, -8); ctx.lineTo(0, -1);
    ctx.moveTo(7, -8); ctx.lineTo(0, -1);
    ctx.stroke();
    // Bowstring
    ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-7, -8); ctx.lineTo(7, -8); ctx.stroke();
    // Bolt loaded
    ctx.fillStyle = col;
    ctx.fillRect(-1, -11, 2, 4);
    ctx.beginPath();
    ctx.moveTo(-2.5, -11); ctx.lineTo(0, -13); ctx.lineTo(2.5, -11);
    ctx.closePath(); ctx.fill();
  } else if (type === 'cannon') {
    // Round stone fort
    ctx.fillStyle = stone;
    ctx.beginPath(); ctx.arc(0, 0, 6, 0, Math.PI * 2); ctx.fill();
    ctx.fillRect(-6, 4, 12, 6);
    // Embrasure (hole the barrel exits)
    ctx.fillStyle = dark;
    ctx.fillRect(3, -1.5, 4, 3);
    // Barrel
    ctx.fillStyle = col;
    ctx.fillRect(5, -2, 7, 4);
    ctx.strokeStyle = stone; ctx.lineWidth = 1;
    ctx.strokeRect(5, -2, 7, 4);
    // Iron cannonball tip
    ctx.fillStyle = '#2a2a2a';
    ctx.beginPath(); ctx.arc(10.5, 0, 1.8, 0, Math.PI * 2); ctx.fill();
    // Stone highlight
    ctx.fillStyle = hi;
    ctx.beginPath(); ctx.arc(-2, -2, 1.5, Math.PI * 1.2, Math.PI * 1.8); ctx.fill();
  } else if (type === 'mage') {
    // Tapered arcane spire
    ctx.fillStyle = stone;
    ctx.beginPath();
    ctx.moveTo(-4, 8); ctx.lineTo(4, 8);
    ctx.lineTo(2.5, 0); ctx.lineTo(1.5, -6);
    ctx.lineTo(-1.5, -6); ctx.lineTo(-2.5, 0);
    ctx.closePath(); ctx.fill();
    ctx.fillRect(-6, 6, 12, 4);
    // Glowing window slit
    ctx.fillStyle = col; ctx.globalAlpha = 0.65;
    ctx.fillRect(-1, -3, 2, 4);
    ctx.globalAlpha = 1;
    // Magic orb atop spire
    ctx.fillStyle = col;
    ctx.beginPath(); ctx.arc(0, -9, 2.5, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = col; ctx.lineWidth = 1.5; ctx.globalAlpha = 0.35;
    ctx.beginPath(); ctx.arc(0, -9, 5, 0, Math.PI * 2); ctx.stroke();
    ctx.globalAlpha = 1;
    ctx.fillStyle = '#fff'; ctx.globalAlpha = 0.55;
    ctx.beginPath(); ctx.arc(-0.8, -9.8, 0.9, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1;
    // Spire highlight
    ctx.fillStyle = hi;
    ctx.fillRect(-1.5, -6, 0.8, 13);
  }
  ctx.restore();
}

function drawTowerIcon(towerEntry, x, y) {
  const type = towerEntry && towerEntry.type ? towerEntry.type : null;
  const level = towerEntry && Number(towerEntry.level) ? Number(towerEntry.level) : 0;
  // Background box
  ctx.fillStyle = '#1d2732';
  ctx.fillRect(x - 10, y, 20, 22);
  ctx.strokeStyle = type ? towerColor(type) : '#3f4b58';
  ctx.lineWidth = 1;
  ctx.strokeRect(x - 10, y, 20, 22);
  if (!type) return;
  drawTowerArt(type, x, y + 11, 1);
  // Level badge overlay at bottom of icon
  ctx.fillStyle = 'rgba(6,13,24,0.65)';
  ctx.fillRect(x - 5, y + 15, 10, 7);
  ctx.fillStyle = '#e8a828';
  ctx.font = '7px "Share Tech Mono", monospace';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText(String(level || 1), x, y + 16);
}

function towerColor(type) {
  if (type === 'archer') return '#d8c28f';
  if (type === 'fighter') return '#c2d7a2';
  if (type === 'ballista') return '#9fb4d8';
  if (type === 'cannon') return '#d89f9f';
  if (type === 'mage') return '#cba7ff';
  return '#ffffff';
}

function drawUnits(units) {
  const laneX = canvas.width * 0.5;
  const offsetById = {};
  const sortedBottom = units.filter(u => u.side === 'bottom').sort((a, b) => b.y - a.y);
  const sortedTop = units.filter(u => u.side === 'top').sort((a, b) => a.y - b.y);
  [sortedBottom, sortedTop].forEach(arr => {
    for (let i = 0; i < arr.length; i++) offsetById[arr[i].id] = ((i % 5) - 2) * 5;
  });
  units.forEach(u => {
    const x = laneX + (offsetById[u.id] || 0);
    const y = u.y * canvas.height;
    drawUnitShape(u, x, y);
    drawUnitHp(u, x, y);
  });
}

function drawUnitHp(u, x, y) {
  const ratio = Math.max(0, Math.min(1, u.hp / u.maxHp));
  ctx.fillStyle = '#252d38';
  ctx.fillRect(x - 9, y - 12, 18, 3);
  ctx.fillStyle = u.side === 'bottom' ? '#28c0b0' : '#ff3a3a';
  ctx.fillRect(x - 9, y - 12, 18 * ratio, 3);
}

function drawUnitShape(u, x, y) {
  const friendly = u.side === 'bottom';
  const col = friendly ? '#28c0b0' : '#ff3a3a';
  const hi = '#f4f7fb';
  const shd = friendly ? '#0d5548' : '#6b1010';
  const dir = friendly ? -1 : 1; // direction of travel: -1=up, +1=down
  ctx.save();
  ctx.translate(x, y);

  if (u.type === 'footman') {
    // Shield on leading side (toward enemy)
    ctx.fillStyle = shd;
    ctx.fillRect(dir * 2, dir * -8, 5, 9);
    ctx.fillStyle = col;
    ctx.fillRect(dir * 3, dir * -7, 4, 8);
    ctx.strokeStyle = hi; ctx.lineWidth = 1;
    ctx.strokeRect(dir * 3, dir * -7, 4, 8);
    ctx.beginPath(); // shield cross
    ctx.moveTo(dir * 5, dir * -7); ctx.lineTo(dir * 5, dir * 1);
    ctx.moveTo(dir * 3, dir * -3); ctx.lineTo(dir * 7, dir * -3);
    ctx.stroke();
    // Armored body
    ctx.fillStyle = col;
    roundRect(-5, -4, 10, 10, 2); ctx.fill();
    ctx.strokeStyle = shd; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-5, 1); ctx.lineTo(5, 1); ctx.stroke();
    // Helmet
    ctx.fillStyle = hi;
    ctx.beginPath(); ctx.arc(0, -6, 4, Math.PI, 0); ctx.closePath(); ctx.fill();
    ctx.fillStyle = shd; ctx.fillRect(-2, -7, 4, 2); // visor slit
    // Spear on trailing side
    ctx.strokeStyle = hi; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.moveTo(-dir * 5, 2); ctx.lineTo(-dir * 9, dir * 8); ctx.stroke();
    ctx.beginPath();
    ctx.moveTo(-dir * 8, dir * 6); ctx.lineTo(-dir * 10, dir * 8); ctx.lineTo(-dir * 8, dir * 10);
    ctx.stroke();

  } else if (u.type === 'bowman') {
    // Slim body
    ctx.fillStyle = col;
    roundRect(-4, -3, 8, 9, 2); ctx.fill();
    // Head with hood
    ctx.fillStyle = hi;
    ctx.beginPath(); ctx.arc(0, -5, 3.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = col;
    ctx.beginPath(); ctx.arc(0, -5, 3.5, Math.PI * 0.1, Math.PI * 0.9); ctx.closePath(); ctx.fill();
    // Quiver on back
    ctx.fillStyle = shd;
    ctx.fillRect(-dir * 5, -2, 2, 5);
    // Bow arc
    ctx.strokeStyle = hi; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(dir * 7, 1, 5, -Math.PI * 0.55, Math.PI * 0.55); ctx.stroke();
    // Bowstring
    ctx.strokeStyle = col; ctx.lineWidth = 1;
    const bsY1 = 1 - 5 * Math.sin(Math.PI * 0.55);
    const bsY2 = 1 + 5 * Math.sin(Math.PI * 0.55);
    ctx.beginPath();
    ctx.moveTo(dir * 7, bsY1); ctx.lineTo(dir * 5, 1); ctx.lineTo(dir * 7, bsY2);
    ctx.stroke();
    // Arrow nocked
    ctx.strokeStyle = hi; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-dir, 0); ctx.lineTo(dir * 8, 0); ctx.stroke();

  } else if (u.type === 'ironclad') {
    // Pauldrons (shoulder plates)
    ctx.fillStyle = col;
    ctx.fillRect(-9, -5, 4, 5);
    ctx.fillRect(5, -5, 4, 5);
    ctx.strokeStyle = shd; ctx.lineWidth = 1;
    ctx.strokeRect(-9, -5, 4, 5);
    ctx.strokeRect(5, -5, 4, 5);
    // Heavy body plate
    ctx.fillStyle = col;
    roundRect(-6, -3, 12, 13, 2); ctx.fill();
    ctx.strokeStyle = shd; ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(-6, 2); ctx.lineTo(6, 2);
    ctx.moveTo(-6, 6); ctx.lineTo(6, 6);
    ctx.stroke();
    // Bucket helmet
    ctx.fillStyle = hi;
    roundRect(-6, -10, 12, 8, 2); ctx.fill();
    ctx.fillStyle = col;
    ctx.fillRect(-6, -7, 12, 3); // neck cover
    ctx.fillStyle = shd;
    ctx.fillRect(-4, -9, 8, 2); // visor slit
    // Glowing eye slits
    ctx.fillStyle = col; ctx.globalAlpha = 0.5;
    ctx.fillRect(-3, -9, 2, 2);
    ctx.fillRect(1, -9, 2, 2);
    ctx.globalAlpha = 1;

  } else if (u.type === 'runner') {
    // Speed lines trailing behind
    ctx.strokeStyle = col; ctx.globalAlpha = 0.35; ctx.lineWidth = 1;
    for (let i = 1; i <= 3; i++) {
      ctx.beginPath();
      ctx.moveTo(dir * (4 + i * 3), -i);
      ctx.lineTo(dir * (6 + i * 5), -i);
      ctx.stroke();
    }
    ctx.globalAlpha = 1;
    // Lean body (slight forward tilt)
    ctx.save(); ctx.rotate(dir * 0.2);
    ctx.fillStyle = col;
    ctx.beginPath(); ctx.ellipse(0, 2, 4, 7, 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = hi;
    ctx.beginPath(); ctx.arc(0, -5, 3, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = col;
    ctx.beginPath(); ctx.arc(0, -5, 3, Math.PI * 0.1, Math.PI * 0.9); ctx.closePath(); ctx.fill();
    ctx.restore();

  } else if (u.type === 'warlock') {
    // Staff on one side
    ctx.strokeStyle = hi; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.moveTo(dir * 6, 8); ctx.lineTo(dir * 6, -7); ctx.stroke();
    // Magic orb atop staff
    const og = ctx.createRadialGradient(dir * 6, -8, 0, dir * 6, -8, 3);
    og.addColorStop(0, hi); og.addColorStop(0.5, col); og.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = og;
    ctx.beginPath(); ctx.arc(dir * 6, -8, 3, 0, Math.PI * 2); ctx.fill();
    // Pointed robe
    ctx.fillStyle = col;
    ctx.beginPath();
    ctx.moveTo(-1, -8); ctx.lineTo(-3, -3);
    ctx.lineTo(-6, 8); ctx.lineTo(6, 8);
    ctx.lineTo(3, -3); ctx.closePath(); ctx.fill();
    // Robe hem detail
    ctx.strokeStyle = shd; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-6, 4); ctx.lineTo(6, 4); ctx.stroke();
    // Face
    ctx.fillStyle = hi; ctx.globalAlpha = 0.8;
    ctx.beginPath(); ctx.arc(0, -1, 2.5, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1;
    ctx.fillStyle = col;
    ctx.beginPath(); ctx.arc(-1, -1, 0.7, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(1, -1, 0.7, 0, Math.PI * 2); ctx.fill();
  }

  ctx.restore();
}

function drawProjectiles(projectiles) {
  for (const p of projectiles) {
    const px = p.x * canvas.width;
    const py = p.y * canvas.height;
    // bottom-side units travel up (-π/2), top-side travel down (+π/2)
    const angle = (p.side === 'bottom') ? -Math.PI / 2 : Math.PI / 2;
    drawProjectileAt(p.projectileType, p.damageType || '', px, py, angle);
  }
}

// Draws a single projectile centered at (px, py) traveling in direction travelAngle
// (canvas convention: 0=right, π/2=down, -π/2=up).
function drawProjectileAt(projectileType, damageType, px, py, travelAngle) {
  const c = projectileColor(projectileType);
  ctx.save();
  ctx.translate(px, py);

  if (projectileType === 'archer' || projectileType === 'bowman' || damageType === 'PIERCE') {
    // Arrow — shape drawn pointing up, rotated to travel direction
    ctx.rotate(travelAngle + Math.PI / 2);
    ctx.strokeStyle = c; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.moveTo(0, 7); ctx.lineTo(0, -5); ctx.stroke();
    ctx.fillStyle = c;
    ctx.beginPath(); ctx.moveTo(-2.5, -3); ctx.lineTo(0, -8); ctx.lineTo(2.5, -3); ctx.closePath(); ctx.fill();
    ctx.strokeStyle = c; ctx.globalAlpha = 0.5; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-2, 7); ctx.lineTo(0, 4); ctx.moveTo(2, 7); ctx.lineTo(0, 4); ctx.stroke();
    ctx.globalAlpha = 1;

  } else if (projectileType === 'fighter' || damageType === 'NORMAL') {
    // Spinning blade (thrown sword)
    ctx.rotate((Date.now() * 0.008) % (Math.PI * 2));
    ctx.fillStyle = c;
    ctx.beginPath(); ctx.moveTo(0, -6); ctx.lineTo(2, 0); ctx.lineTo(0, 6); ctx.lineTo(-2, 0); ctx.closePath(); ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,0.45)'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-5, 0); ctx.lineTo(5, 0); ctx.stroke();

  } else if (projectileType === 'ballista' || damageType === 'SIEGE') {
    // Thick crossbow bolt
    ctx.rotate(travelAngle + Math.PI / 2);
    ctx.fillStyle = c;
    ctx.fillRect(-2, -9, 4, 14);
    ctx.beginPath(); ctx.moveTo(-3, -9); ctx.lineTo(0, -13); ctx.lineTo(3, -9); ctx.closePath(); ctx.fill();
    ctx.globalAlpha = 0.6;
    ctx.beginPath(); ctx.moveTo(-4, 4); ctx.lineTo(0, 2); ctx.lineTo(-4, 0); ctx.closePath(); ctx.fill();
    ctx.beginPath(); ctx.moveTo(4, 4); ctx.lineTo(0, 2); ctx.lineTo(4, 0); ctx.closePath(); ctx.fill();
    ctx.globalAlpha = 1;

  } else if (projectileType === 'cannon' || damageType === 'SPLASH') {
    // Cannonball with fire trail behind it
    const tdx = Math.cos(travelAngle - Math.PI) * 10;
    const tdy = Math.sin(travelAngle - Math.PI) * 10;
    const tg = ctx.createRadialGradient(tdx * 0.7, tdy * 0.7, 0, tdx * 0.7, tdy * 0.7, 9);
    tg.addColorStop(0, 'rgba(255,140,0,0.8)');
    tg.addColorStop(1, 'rgba(255,40,0,0)');
    ctx.fillStyle = tg;
    ctx.beginPath(); ctx.arc(tdx * 0.5, tdy * 0.5, 7, 0, Math.PI * 2); ctx.fill();
    const bg = ctx.createRadialGradient(-1.5, -1.5, 0, 0, 0, 5);
    bg.addColorStop(0, '#aaa'); bg.addColorStop(1, '#111');
    ctx.fillStyle = bg;
    ctx.beginPath(); ctx.arc(0, 0, 5, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#555'; ctx.lineWidth = 1; ctx.stroke();

  } else {
    // Magic orb — mage / warlock / MAGIC type
    const gg = ctx.createRadialGradient(0, 0, 0, 0, 0, 6);
    gg.addColorStop(0, '#fff'); gg.addColorStop(0.4, c); gg.addColorStop(1, 'rgba(80,0,180,0)');
    ctx.fillStyle = gg;
    ctx.beginPath(); ctx.arc(0, 0, 6, 0, Math.PI * 2); ctx.fill();
    const t = Date.now() * 0.003;
    for (let i = 0; i < 4; i++) {
      const sa = t + i * Math.PI / 2;
      const sr = 7 + Math.sin(t * 1.3 + i) * 1.5;
      ctx.globalAlpha = 0.5 + Math.sin(t * 0.7 + i) * 0.3;
      ctx.fillStyle = c;
      ctx.beginPath(); ctx.arc(Math.cos(sa) * sr, Math.sin(sa) * sr, 1, 0, Math.PI * 2); ctx.fill();
    }
    ctx.globalAlpha = 1;
  }

  ctx.restore();
}

function projectileColor(type) {
  if (type === 'bowman') return '#8fd8b4';
  if (type === 'warlock') return '#c9a8ff';
  return towerColor(type);
}

function getTowerUpgradeCost(type, nextLevel) {
  const m = towerMeta[type] || DEFAULT_TOWER_META[type];
  const base = Number(m.cost) || 0;
  const scale = 0.75 + (0.25 * nextLevel);
  return Math.ceil(base * scale);
}

function getTowerStatsAtLevel(type, level) {
  const m = towerMeta[type] || DEFAULT_TOWER_META[type];
  const lvl = Math.max(1, Math.min(10, level));
  const scale = lvl - 1;
  return {
    dmg: Number(m.dmg) * (1 + 0.12 * scale),
    range: Number(m.range) * (1 + 0.015 * scale),
    damageType: m.damageType || 'NORMAL',
  };
}

function roundRect(x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y); ctx.lineTo(x + w - r, y); ctx.quadraticCurveTo(x + w, y, x + w, y + r);
  ctx.lineTo(x + w, y + h - r); ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h); ctx.quadraticCurveTo(x, y + h, x, y + h - r);
  ctx.lineTo(x, y + r); ctx.quadraticCurveTo(x, y, x + r, y); ctx.closePath();
}

let _lastHudGold = -1;
function updateHud(local) {
  const goldVal = local.me.gold;
  if (goldVal !== _lastHudGold) {
    if (_lastHudGold !== -1) {
      hudGold.classList.remove('gold-flash');
      // Force reflow so animation restarts cleanly if already applied
      void hudGold.offsetWidth;
      hudGold.classList.add('gold-flash');
      hudGold.addEventListener('animationend', function onGoldFlashEnd() {
        hudGold.classList.remove('gold-flash');
        hudGold.removeEventListener('animationend', onGoldFlashEnd);
      });
    }
    _lastHudGold = goldVal;
  }
  hudGold.textContent = String(goldVal);
  hudIncome.textContent = String(local.me.income) + 'g';
  hudIncomeTimer.textContent = Math.max(0, Math.ceil(local.incomeTicksRemaining / tickHz)) + 's';
}

function updateSendButtons(gold) {
  sendButtons.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const m = unitMeta[type] || DEFAULT_UNIT_META[type];
    btn.disabled = !(lobbyState === 'playing' && gold >= m.cost);
  });
}

function updateDefenseButtons(gold, towers) {
  defenseButtons.forEach(btn => {
    const type = btn.getAttribute('data-tower-type');
    const m = towerMeta[type] || DEFAULT_TOWER_META[type];
    btn.disabled = !(lobbyState === 'playing' && gold >= m.cost);
  });

  const occupied = SLOT_NAMES.every(function (slot) { return !!(towers[slot] && towers[slot].type); });
  if (occupied) {
    defenseButtons.forEach(btn => { btn.disabled = true; });
  }
}

function updateTowerSlots(towers) {
  towerSlots.forEach(slotEl => {
    const slot = slotEl.getAttribute('data-slot');
    const t = towers[slot] || { type: null, level: 0 };
    const wasOccupied = slotEl.classList.contains('occupied');
    const nowOccupied = !!t.type;
    slotEl.textContent = '';
    slotEl.dataset.towerType = t.type || '';
    slotEl.dataset.towerLevel = String(t.level || 0);
    slotEl.dataset.towerShort = t.type ? shortTower(t.type) : '';
    slotEl.classList.toggle('occupied', nowOccupied);
    // Trigger build animation when a tower is first placed
    if (!wasOccupied && nowOccupied) {
      slotEl.classList.remove('just-built');
      void slotEl.offsetWidth;
      slotEl.classList.add('just-built');
      setTimeout(function () { slotEl.classList.remove('just-built'); }, 400);
    }
  });
}

function updateTowerSlotPositions(laneX, laneW, h) {
  const y = h * 0.905;
  const xBySlot = {
    left_outer: canvas.width * 0.10,
    left_mid: canvas.width * 0.25,
    left_inner: canvas.width * 0.40,
    right_inner: canvas.width * 0.60,
    right_mid: canvas.width * 0.75,
    right_outer: canvas.width * 0.90,
  };
  towerSlots.forEach(slotEl => {
    const slot = slotEl.getAttribute('data-slot');
    slotEl.style.left = xBySlot[slot] + 'px';
    slotEl.style.top = y + 'px';
  });
}

function showActionFeedback(message, isInfo) {
  if (!actionFeedback) return;
  // Reset animation so slideUp replays on each call
  actionFeedback.classList.remove('visible');
  void actionFeedback.offsetWidth;
  actionFeedback.textContent = message;
  actionFeedback.classList.toggle('feedback-info', !!isInfo);
  actionFeedback.classList.add('visible');
  if (feedbackTimeout) clearTimeout(feedbackTimeout);
  feedbackTimeout = setTimeout(function () { actionFeedback.classList.remove('visible'); }, 1200);
}

function showBuildPopout(slotEl) {
  const slot = slotEl.getAttribute('data-slot');
  activePopSlot = slot;
  const myGold = (currentState && currentState.me && Number.isFinite(currentState.me.gold))
    ? currentState.me.gold : 0;
  let html = '<span class="tower-pop-line">Build Tower</span>';
  TOWER_TYPES.forEach(type => {
    const m = towerMeta[type] || DEFAULT_TOWER_META[type];
    const aps = m.atkCdTicks ? (tickHz / m.atkCdTicks) : 0;
    const canAfford = myGold >= m.cost;
    html += '<button class="tower-build-btn"'
      + ' data-build-type="' + type + '"'
      + ' data-build-slot="' + slot + '"'
      + (canAfford ? '' : ' disabled') + '>'
      + m.label + ' — ' + m.cost + 'g'
      + ' | ' + m.damageType + ' ' + Number(m.dmg || 0).toFixed(1) + 'dmg ' + aps.toFixed(1) + '/s'
      + '</button>';
  });
  towerPopout.innerHTML = html;
  towerPopout.style.display = 'block';

  const uiRect = gameUi.getBoundingClientRect();
  const slotRect = slotEl.getBoundingClientRect();
  const x = slotRect.left - uiRect.left + (slotRect.width / 2);
  const y = slotRect.top - uiRect.top - 8;
  towerPopout.style.left = x + 'px';
  towerPopout.style.top = y + 'px';

  towerPopout.querySelectorAll('.tower-build-btn').forEach(btn => {
    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      tryBuildTower(btn.getAttribute('data-build-type'), btn.getAttribute('data-build-slot'));
      towerPopout.style.display = 'none';
      activePopSlot = null;
    });
  });
}

function showTowerPopout(slotEl, towerType, towerLevel) {
  if (!towerType) {
    towerPopout.style.display = 'none';
    return;
  }
  // In ML mode use the ML slot key stored in data-ml-slot; else use the DOM data-slot name
  activePopSlot = (gameMode === 'multilane' && slotEl.dataset.mlSlot)
    ? slotEl.dataset.mlSlot
    : slotEl.getAttribute('data-slot');
  const level = Math.max(1, Number(towerLevel) || 1);
  const stats = getTowerStatsAtLevel(towerType, level);
  const nextLevel = level + 1;
  const canLevel = nextLevel <= 10;
  const upgradeCost = canLevel ? getTowerUpgradeCost(towerType, nextLevel) : 0;
  let myGold = 0;
  if (gameMode === 'multilane') {
    const mlLane = mlCurrentState && mlCurrentState.lanes && mlCurrentState.lanes[myLaneIndex];
    myGold = (mlLane && Number.isFinite(mlLane.gold)) ? mlLane.gold : 0;
  } else {
    myGold = (currentState && currentState.me && Number.isFinite(currentState.me.gold)) ? currentState.me.gold : 0;
  }
  const canUpgrade = canLevel && myGold >= upgradeCost;
  // XSS note (fix #1): towerType comes from slotEl.dataset.towerType which is set from server state
  // unitType strings validated server-side against known keys — not arbitrary user input.
  // activePopSlot comes from slotEl.getAttribute('data-slot') which is a known SLOT_NAMES constant.
  // stats.damageType is derived from server-validated towerMeta. All numeric values are coerced
  // through Number/toFixed/Math.round before insertion. Risk is low but noted for future audits.
  towerPopout.innerHTML = ''
    + '<span class="tower-pop-line">' + cap(towerType) + ' Lv' + level + '</span>'
    + '<span class="tower-pop-line">' + stats.damageType + '  dmg ' + stats.dmg.toFixed(1) + '  rng ' + Math.round(stats.range * 100) + '%</span>'
    + (canLevel
      ? '<button class="tower-upgrade-btn" data-upgrade-slot="' + activePopSlot + '"' + (canUpgrade ? '' : ' disabled') + '>Upgrade to Lv' + nextLevel + ' (' + upgradeCost + 'g)</button>'
      : '<span class="tower-pop-line">Max level</span>');
  towerPopout.style.display = 'block';

  const uiRect = gameUi.getBoundingClientRect();
  const slotRect = slotEl.getBoundingClientRect();
  const x = slotRect.left - uiRect.left + (slotRect.width / 2);
  const y = slotRect.top - uiRect.top - 8;
  towerPopout.style.left = x + 'px';
  towerPopout.style.top = y + 'px';

  const upBtn = towerPopout.querySelector('.tower-upgrade-btn');
  if (upBtn) {
    upBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      const slot = upBtn.getAttribute('data-upgrade-slot');
      tryUpgradeTower(slot);
    });
  }
}

function trySpawn(type) {
  sendAction('spawn_unit', { unitType: type });
}

function tryBuildTower(type, slot) {
  if (!slot) return;
  sendAction('build_tower', { towerType: type, slot: slot });
}

function tryUpgradeTower(slot) {
  if (!slot) return;
  sendAction('upgrade_tower', { slot: slot });
}

function showGameOverBanner(message) {
  const isVictory = message === 'VICTORY';
  gameOverBanner.innerHTML =
    '<span class="banner-main">' + message + '</span>'
    + '<span class="banner-sub">Click anywhere to return to lobby</span>';
  gameOverBanner.classList.remove('banner-victory', 'banner-defeat');
  gameOverBanner.classList.add(isVictory ? 'banner-victory' : 'banner-defeat');
  gameOverBanner.style.display = 'block';
}

function hideGameOverBanner() {
  gameOverBanner.style.display = 'none';
  gameOverBanner.innerHTML = '';
  gameOverBanner.classList.remove('banner-victory', 'banner-defeat');
}

function resetToLobby(msg) {
  myCode = null;
  mySide = null;
  hasFirstSnapshot = false;
  previousState = null;
  currentState = null;
  roomCodeRow.style.display = 'none';
  roomCodeDisplay.textContent = '';

  // Reset ML state
  gameMode = 'classic';
  myLaneIndex = null;
  mlPlayerCount = 0;
  mlLaneAssignments = [];
  mlCurrentState = null;
  mlPreviousState = null;
  mlHasFirstSnapshot = false;
  isSpectator = false;
  mlLobbyPlayers = [];
  mlIsHost = false;
  mlMyCode = null;

  // Restore classic tab visibility
  btnTabClassic.classList.add('active');
  btnTabMl.classList.remove('active');
  lobbyClassicSection.style.display = '';
  lobbyMlSection.style.display = 'none';
  hideMLLobbyPanel();
  if (mlRoomCodeRow) mlRoomCodeRow.style.display = 'none';
  if (btnForceStart) btnForceStart.style.display = 'none';
  if (btnReadyMl) btnReadyMl.classList.remove('is-ready');
  if (spectatorBadge) spectatorBadge.style.display = 'none';

  // Restore tower slots and defense bar for classic mode
  towerSlots.forEach(slotEl => { slotEl.style.display = ''; });
  const defenseBarEl = document.getElementById('defense-bar');
  if (defenseBarEl) defenseBarEl.style.display = '';

  // Hide ML-specific UI
  if (mlBarracksHud) mlBarracksHud.style.display = 'none';
  if (mlLaneNav) mlLaneNav.style.display = 'none';
  if (mlTileMenu) mlTileMenu.style.display = 'none';
  viewingLaneIndex = null;

  hideGameUi();
  hideGameOverBanner();
  showLobby();
  setLobbyState('idle');
  setStatus(msg, 'error');
}

function sendAction(type, data) {
  if (lobbyState !== 'playing') return;
  if (gameMode === 'multilane') {
    if (!mlMyCode || myLaneIndex === null) return;
    socket.emit('player_action', { code: mlMyCode, laneIndex: myLaneIndex, type, data: data || {} });
    return;
  }
  if (!myCode || !mySide) return;
  socket.emit('player_action', { code: myCode, side: mySide, type, data: data || {} });
}

function cap(s) {
  return String(s || '').charAt(0).toUpperCase() + String(s || '').slice(1);
}

updateActionLabels();

// ── Tab event handlers ────────────────────────────────────────────────────────

btnTabClassic.addEventListener('click', () => {
  if (gameMode === 'multilane' && lobbyState !== 'idle') return;
  btnTabClassic.classList.add('active');
  btnTabMl.classList.remove('active');
  lobbyClassicSection.style.display = '';
  lobbyMlSection.style.display = 'none';
  hideMLLobbyPanel();
});

btnTabMl.addEventListener('click', () => {
  if (gameMode === 'classic' && lobbyState !== 'idle') return;
  btnTabMl.classList.add('active');
  btnTabClassic.classList.remove('active');
  lobbyMlSection.style.display = '';
  lobbyClassicSection.style.display = 'none';
  hideMLLobbyPanel();
});

btnCreateMl.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  const name = mlNameInput.value.trim() || 'Player';
  setLobbyState('creating');
  setStatus('Creating ML room...', 'wait');
  socket.emit('create_ml_room', { displayName: name });
});

btnJoinMl.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  const code = mlJoinInput.value.trim().toUpperCase();
  if (code.length < 6) return setStatus('Enter the full 6-character room code.', 'error');
  const name = mlNameInput.value.trim() || 'Player';
  setLobbyState('joining');
  setStatus('Joining ML room...', 'wait');
  socket.emit('join_ml_room', { code, displayName: name });
});

mlJoinInput.addEventListener('keydown', e => {
  if (e.key === 'Enter') btnJoinMl.click();
});

btnReadyMl.addEventListener('click', () => {
  if (!mlMyCode) return;
  const isReady = btnReadyMl.classList.toggle('is-ready');
  btnReadyMl.textContent = isReady ? '\u2714 Ready (click to unready)' : '\u2714 Ready';
  if (isReady) socket.emit('ml_player_ready', { code: mlMyCode });
});

btnForceStart.addEventListener('click', () => {
  if (!mlMyCode) return;
  socket.emit('ml_force_start', { code: mlMyCode });
});

if (btnCopyMlCode) {
  btnCopyMlCode.addEventListener('click', async () => {
    if (!mlMyCode) return;
    try {
      await copyToClipboard(mlMyCode);
      setStatus('Room code copied!', 'ok');
    } catch (_) {
      setStatus('Copy failed.', 'error');
    }
  });
}

if (btnCopyMlLink) {
  btnCopyMlLink.addEventListener('click', async () => {
    if (!mlMyCode) return;
    try {
      await copyToClipboard(makeShareUrl(mlMyCode, 'multilane'));
      setStatus('Invite link copied!', 'ok');
    } catch (_) {
      setStatus('Copy failed.', 'error');
    }
  });
}

btnCreate.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  setLobbyState('creating');
  setStatus('Creating room...', 'wait');
  socket.emit('create_room');
});

btnJoin.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  const code = joinInput.value.trim().toUpperCase();
  if (code.length < 6) return setStatus('Enter the full 6-character room code.', 'error');
  const displayName = joinNameInput.value.trim() || 'Player';
  setLobbyState('joining');
  setStatus('Joining room...', 'wait');
  socket.emit('join_room', { code, displayName });
});

joinInput.addEventListener('keydown', e => {
  if (e.key === 'Enter') btnJoin.click();
});

function makeShareUrl(code, mode) {
  const base = window.location.origin + window.location.pathname;
  const param = mode === 'multilane' ? 'mljoin' : 'join';
  return base + '?' + param + '=' + encodeURIComponent(code);
}

async function copyToClipboard(text) {
  if (navigator.clipboard && window.isSecureContext) {
    await navigator.clipboard.writeText(text);
    return;
  }
  const el = document.createElement('input');
  el.value = text;
  el.style.cssText = 'position:fixed;top:-999px;left:-999px;opacity:0';
  document.body.appendChild(el);
  el.select();
  el.setSelectionRange(0, el.value.length);
  const ok = document.execCommand('copy');
  document.body.removeChild(el);
  if (!ok) throw new Error('execCommand failed');
}

btnCopyCode.addEventListener('click', async () => {
  if (!myCode) return;
  try {
    await copyToClipboard(myCode);
    setStatus('Room code copied to clipboard.', 'ok');
  } catch (_err) {
    setStatus('Copy failed. Copy manually.', 'error');
  }
});

btnCopyLink.addEventListener('click', async () => {
  if (!myCode) return;
  try {
    await copyToClipboard(makeShareUrl(myCode, 'classic'));
    setStatus('Invite link copied!', 'ok');
  } catch (_err) {
    setStatus('Copy failed. Copy manually.', 'error');
  }
});

sendButtons.forEach(btn => {
  btn.addEventListener('click', () => trySpawn(btn.getAttribute('data-unit-type')));
});

  defenseButtons.forEach(btn => {
  const t = btn.getAttribute('data-tower-type');
  btn.addEventListener('click', () => {
    draggingTowerType = t;
    showActionFeedback('Selected ' + cap(t) + ' - drop on wall slot', true);
  });
  btn.addEventListener('dragstart', e => {
    draggingTowerType = t;
    e.dataTransfer.setData('text/plain', t);
  });
});

towerSlots.forEach(slotEl => {
  slotEl.addEventListener('dragover', e => {
    e.preventDefault();
    slotEl.classList.add('drag-over');
  });
  slotEl.addEventListener('dragleave', () => {
    slotEl.classList.remove('drag-over');
  });
  slotEl.addEventListener('drop', e => {
    e.preventDefault();
    slotEl.classList.remove('drag-over');
    const fromDnd = e.dataTransfer.getData('text/plain');
    const towerType = fromDnd || draggingTowerType;
    const slot = slotEl.getAttribute('data-slot');
    if (towerType) tryBuildTower(towerType, slot);
  });
  slotEl.addEventListener('click', () => {
    const existing = slotEl.dataset.towerType || '';
    const existingLevel = Number(slotEl.dataset.towerLevel || '0');
    if (existing) {
      showTowerPopout(slotEl, existing, existingLevel);
      return;
    }
    if (draggingTowerType) {
      tryBuildTower(draggingTowerType, slotEl.getAttribute('data-slot'));
    } else {
      showBuildPopout(slotEl);
    }
  });
});

window.addEventListener('click', function (e) {
  // Close ML tile menu when clicking outside it (but not if just opened this cycle)
  if (mlTileMenu && mlTileMenu.style.display !== 'none' && !mlTileMenuJustOpened) {
    if (!e.target || !e.target.closest('#ml-tile-menu')) {
      closeMLTileMenu();
    }
  }
  // Classic tower popout
  if (!(e.target && (e.target.closest('.tower-slot') || e.target.closest('#tower-popout')))) {
    towerPopout.style.display = 'none';
    activePopSlot = null;
  }
});

window.addEventListener('keydown', e => {
  if (lobbyState !== 'playing') return;
  if (document.activeElement === joinInput) return;
  if (e.key === '1') trySpawn(UNIT_TYPES[0]);
  if (e.key === '2') trySpawn(UNIT_TYPES[1]);
  if (e.key === '3') trySpawn(UNIT_TYPES[2]);
  if (e.key === '4') trySpawn(UNIT_TYPES[3]);
  if (e.key === '5') trySpawn(UNIT_TYPES[4]);
});

socket.on('room_created', data => {
  myCode = data.code;
  mySide = data.side;
  setLobbyState('waiting');
  roomCodeDisplay.textContent = data.code;
  roomCodeRow.style.display = 'block';
  setStatus('Share this code - waiting for opponent...', 'wait');
});

socket.on('room_joined', data => {
  myCode = data.code;
  mySide = data.side;
  setLobbyState('playing');
  setStatus('Match starting...', 'ok');
});

socket.on('match_ready', () => {
  setLobbyState('playing');
  hideLobby();
  showGameUi();
  initCanvas();
  hideGameOverBanner();
  startRenderLoop();
});

socket.on('match_config', config => applyMatchConfig(config));

socket.on('action_applied', data => {
  if (!data) return;
  lastActionAck = 'ACK ' + data.type + ' t' + data.tick + ' u' + data.units + ' g' + data.gold;
  if (data.type === 'upgrade_tower') towerPopout.style.display = 'none';
});

socket.on('error_message', data => {
  const msg = (data && data.message) ? data.message : 'Action rejected';
  if (lobbyState === 'playing') {
    showActionFeedback(msg);
    return;
  }
  setLobbyState('idle');
  setStatus(msg, 'error');
});

socket.on('player_left', () => {
  if (lobbyState === 'playing') resetToLobby('Opponent disconnected. Game ended.');
  else {
    setLobbyState('idle');
    setStatus('Opponent disconnected. Room closed.', 'error');
  }
});

socket.on('state_snapshot', state => {
  const local = localizeState(state);
  if (!local) return;
  hasFirstSnapshot = true;
  previousState = currentState || local;
  currentState = local;
  currentStateReceivedAt = performance.now();
});

socket.on('game_over', payload => {
  if (!payload || !payload.winner) return;
  showGameOverBanner(payload.winner === mySide ? 'VICTORY' : 'DEFEAT');
});

// Click anywhere on canvas/UI after game over to return to lobby
gameUi.addEventListener('click', function () {
  if (gameOverBanner.style.display !== 'none' && gameOverBanner.style.display !== '') {
    resetToLobby('Game over. Start a new match!');
    _lastHudGold = -1;
  }
});

// ── Share-link auto-join ──────────────────────────────────────────────────────
(function handleShareLink() {
  const params = new URLSearchParams(window.location.search);
  const classicCode = params.get('join');
  const mlCode = params.get('mljoin');
  if (!classicCode && !mlCode) return;

  // Strip the param from the address bar immediately
  history.replaceState(null, '', window.location.pathname);

  function doPreFill() {
    if (classicCode) {
      const code = classicCode.toUpperCase().slice(0, 6);
      if (code.length !== 6) return setStatus('Invalid invite link.', 'error');
      joinInput.value = code;
      setStatus("You've been invited! Enter your name, then click Join Room.", 'wait');
      joinNameInput.focus();
    } else {
      const code = mlCode.toUpperCase().slice(0, 6);
      if (code.length !== 6) return setStatus('Invalid invite link.', 'error');
      // Switch to ML tab visually
      btnTabClassic.classList.remove('active');
      btnTabMl.classList.add('active');
      lobbyClassicSection.style.display = 'none';
      lobbyMlSection.style.display = '';
      mlJoinInput.value = code;
      setStatus("You've been invited! Enter your name, then click Join ML Room.", 'wait');
      mlNameInput.focus();
    }
  }

  if (socket.connected) {
    doPreFill();
  } else {
    socket.once('connect', doPreFill);
  }
}());

socket.on('connect', () => console.log('[socket] connected:', socket.id));
socket.on('disconnect', reason => {
  console.log('[socket] disconnected:', reason);
  if (lobbyState === 'playing') resetToLobby('Connection lost. Game ended.');
  else setStatus('Connection lost. Reload to retry.', 'error');
});
socket.on('connect_error', err => {
  console.error('[socket] connect_error:', err.message);
  setStatus('Cannot reach server - is it running?', 'error');
});

// fix #7: removed unnecessary window.sendAction global exposure

// ── Multi-lane render helpers ─────────────────────────────────────────────────

function buildMLInterpolatedState(nowMs) {
  if (!mlCurrentState) return null;
  if (!mlPreviousState) return mlCurrentState;
  const alpha = Math.max(0, Math.min(1, (nowMs - mlCurrentStateReceivedAt) / SNAPSHOT_MS));
  const result = {
    tick: mlCurrentState.tick,
    phase: mlCurrentState.phase,
    winner: mlCurrentState.winner,
    incomeTicksRemaining: mlCurrentState.incomeTicksRemaining,
  };
  result.lanes = mlCurrentState.lanes.map((lane, li) => {
    const prevLane = mlPreviousState.lanes && mlPreviousState.lanes[li];
    if (!prevLane) return lane;
    const prevById = new Map();
    prevLane.units.forEach(u => prevById.set(u.id, u));
    const units = lane.units.map(u => {
      const prev = prevById.get(u.id);
      if (!prev) return u;
      // Interpolate pathIdx for smooth tile-based movement
      return Object.assign({}, u, {
        pathIdx: lerp(prev.pathIdx, u.pathIdx, alpha),
        hp: lerp(prev.hp, u.hp, alpha),
      });
    });
    return Object.assign({}, lane, { units });
  });
  return result;
}

// ── ML grid rendering ─────────────────────────────────────────────────────────

// Returns square-tile layout so tiles are always the same width and height.
// Tiles fit the canvas height (minus the 16px gate bar); grid is centered horizontally.
function getMLGridLayout() {
  const w = canvas.width;
  const h = canvas.height;
  const tileSize = Math.max(4, Math.min(
    Math.floor(w / ML_GRID_W),
    Math.floor((h - 16) / ML_GRID_H)
  ));
  const gridW = tileSize * ML_GRID_W;
  const gridH = tileSize * ML_GRID_H;
  const offsetX = Math.floor((w - gridW) / 2);
  return { tileSize, offsetX, gridW, gridH };
}

// Compute smooth screen position for a unit from its interpolated pathIdx and path
function getUnitPixelPos(pathIdx, path, tileW, tileH) {
  if (!path || path.length === 0) return { x: canvas.width / 2, y: canvas.height / 2 };
  const clamped = Math.max(0, Math.min(pathIdx, path.length - 1));
  const lower = Math.floor(clamped);
  const upper = Math.min(lower + 1, path.length - 1);
  const frac = clamped - lower;
  const ax = path[lower].x, ay = path[lower].y;
  const bx = path[upper].x, by = path[upper].y;
  return {
    x: (ax + (bx - ax) * frac + 0.5) * tileW,
    y: (ay + (by - ay) * frac + 0.5) * tileH,
  };
}

function drawMLGridLane(lane) {
  if (!lane) return;
  const w = canvas.width;
  const h = canvas.height;
  const { tileSize, offsetX, gridW, gridH } = getMLGridLayout();

  // Full-canvas background — gutters get stone tile pattern, grid gets dark fill
  ctx.clearRect(0, 0, w, h);

  // Stone tile pattern for gutters (canvas areas left/right of the centered grid)
  const stoneSz = 24;
  for (let gsy = 0; gsy < h; gsy += stoneSz) {
    for (let gsx = 0; gsx < w; gsx += stoneSz) {
      if (gsx + stoneSz > offsetX && gsx < offsetX + gridW && gsy < gridH) continue;
      const shade = ((Math.floor(gsx / stoneSz) * 3 + Math.floor(gsy / stoneSz) * 7) & 3);
      const stoneColors = ['#0d1118', '#0b0f15', '#0e1220', '#0c1019'];
      ctx.fillStyle = stoneColors[shade];
      ctx.fillRect(gsx, gsy, stoneSz, stoneSz);
      ctx.strokeStyle = '#050810';
      ctx.lineWidth = 0.5;
      ctx.strokeRect(gsx + 0.5, gsy + 0.5, stoneSz - 1, stoneSz - 1);
    }
  }

  // Grid area base fill
  ctx.fillStyle = '#080c14';
  ctx.fillRect(offsetX, 0, gridW, gridH);

  // Zone tints — spawn zone (teal, top) / castle zone (amber, bottom)
  const zoneRows = Math.floor(ML_GRID_H * 0.28);
  const topZoneGrad = ctx.createLinearGradient(0, 0, 0, zoneRows * tileSize);
  topZoneGrad.addColorStop(0, 'rgba(40,192,176,0.13)');
  topZoneGrad.addColorStop(1, 'rgba(40,192,176,0)');
  ctx.fillStyle = topZoneGrad;
  ctx.fillRect(offsetX, 0, gridW, zoneRows * tileSize);

  const botZoneY = (ML_GRID_H - zoneRows) * tileSize;
  const botZoneGrad = ctx.createLinearGradient(0, botZoneY, 0, gridH);
  botZoneGrad.addColorStop(0, 'rgba(200,120,40,0)');
  botZoneGrad.addColorStop(1, 'rgba(200,120,40,0.13)');
  ctx.fillStyle = botZoneGrad;
  ctx.fillRect(offsetX, botZoneY, gridW, gridH - botZoneY);

  // Subtle glow strips on left and right grid edges
  const lgGrad = ctx.createLinearGradient(offsetX, 0, offsetX + 14, 0);
  lgGrad.addColorStop(0, 'rgba(40,192,176,0.22)');
  lgGrad.addColorStop(1, 'rgba(40,192,176,0)');
  ctx.fillStyle = lgGrad;
  ctx.fillRect(offsetX, 0, 14, gridH);
  const rgGrad = ctx.createLinearGradient(offsetX + gridW - 14, 0, offsetX + gridW, 0);
  rgGrad.addColorStop(0, 'rgba(40,192,176,0)');
  rgGrad.addColorStop(1, 'rgba(40,192,176,0.22)');
  ctx.fillStyle = rgGrad;
  ctx.fillRect(offsetX + gridW - 14, 0, 14, gridH);

  // All grid-relative drawing is offset by (offsetX, 0) so tiles are square and centered.
  ctx.save();
  ctx.translate(offsetX, 0);

  // Path tiles — warm earth/dirt fill with pebble texture dots
  if (lane.path && lane.path.length > 0) {
    for (const { x, y } of lane.path) {
      const ptx = x * tileSize, pty = y * tileSize;
      const dirtShade = ((x * 5 + y * 3) & 3);
      const dirtColors = ['#1e1a0e', '#221e10', '#1a1608', '#201c0c'];
      ctx.fillStyle = dirtColors[dirtShade];
      ctx.fillRect(ptx, pty, tileSize, tileSize);
      ctx.fillStyle = 'rgba(100,90,60,0.38)';
      ctx.fillRect(ptx + Math.floor(tileSize * 0.2), pty + Math.floor(tileSize * 0.3), 2, 2);
      ctx.fillRect(ptx + Math.floor(tileSize * 0.6), pty + Math.floor(tileSize * 0.65), 2, 2);
    }
  }

  // Grid mortar joints
  ctx.strokeStyle = '#0a1018';
  ctx.lineWidth = 0.5;
  for (let gx = 0; gx <= ML_GRID_W; gx++) {
    ctx.beginPath(); ctx.moveTo(gx * tileSize, 0); ctx.lineTo(gx * tileSize, gridH); ctx.stroke();
  }
  for (let gy = 0; gy <= ML_GRID_H; gy++) {
    ctx.beginPath(); ctx.moveTo(0, gy * tileSize); ctx.lineTo(gridW, gy * tileSize); ctx.stroke();
  }

  // Wall tiles — 3D stone block with bevel lighting
  if (lane.walls && lane.walls.length > 0) {
    for (const { x, y } of lane.walls) {
      const wx = x * tileSize + 1, wy = y * tileSize + 1, ws = tileSize - 2;
      const wallShade = ((x * 11 + y * 7) & 3);
      const wallColors = ['#3a4250', '#3c4454', '#384050', '#40485a'];
      ctx.fillStyle = wallColors[wallShade];
      ctx.fillRect(wx, wy, ws, ws);
      // Top-left highlight bevel
      ctx.fillStyle = 'rgba(255,255,255,0.13)';
      ctx.fillRect(wx, wy, ws, 2);
      ctx.fillRect(wx, wy, 2, ws);
      // Bottom-right shadow bevel
      ctx.fillStyle = 'rgba(0,0,0,0.42)';
      ctx.fillRect(wx, wy + ws - 2, ws, 2);
      ctx.fillRect(wx + ws - 2, wy, 2, ws);
      // Mortar outline
      ctx.strokeStyle = '#181e2c';
      ctx.lineWidth = 0.5;
      ctx.strokeRect(wx + 0.5, wy + 0.5, ws - 1, ws - 1);
    }
  }

  // Tower tiles
  if (lane.towerCells && lane.towerCells.length > 0) {
    for (const { x, y, type, level } of lane.towerCells) {
      const col = towerColor(type);
      // Dark tile background + colored border
      ctx.fillStyle = '#1a2230';
      ctx.fillRect(x * tileSize + 1, y * tileSize + 1, tileSize - 2, tileSize - 2);
      ctx.strokeStyle = col; ctx.lineWidth = 1.5;
      ctx.strokeRect(x * tileSize + 1, y * tileSize + 1, tileSize - 2, tileSize - 2);
      // Scaled tower art centered in tile
      const tcx = (x + 0.5) * tileSize;
      const tcy = (y + 0.5) * tileSize;
      drawTowerArt(type, tcx, tcy, tileSize / 22);
      // Level badge at tile bottom
      const fs = Math.max(5, Math.floor(tileSize * 0.22));
      ctx.fillStyle = 'rgba(6,13,24,0.7)';
      ctx.fillRect(tcx - fs, (y + 1) * tileSize - fs - 2, fs * 2, fs + 2);
      ctx.fillStyle = '#e8a828';
      ctx.font = `bold ${fs}px "Share Tech Mono", monospace`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
      ctx.fillText(String(level || 1), tcx, (y + 1) * tileSize - 1);
    }
  }

  // Spawn tile — downward arrow icon (units enter here)
  {
    const spx = ML_SPAWN_X * tileSize, spy = ML_SPAWN_YG * tileSize;
    const spcx = spx + tileSize / 2, spcy = spy + tileSize / 2;
    ctx.fillStyle = 'rgba(20,160,60,0.52)';
    ctx.fillRect(spx + 1, spy + 1, tileSize - 2, tileSize - 2);
    ctx.strokeStyle = '#28d060';
    ctx.lineWidth = 1.5;
    ctx.strokeRect(spx + 2, spy + 2, tileSize - 4, tileSize - 4);
    const aW = Math.max(4, tileSize * 0.28), aH = Math.max(5, tileSize * 0.44);
    ctx.fillStyle = '#28d060';
    ctx.beginPath();
    ctx.moveTo(spcx - aW * 0.5, spcy - aH * 0.45);
    ctx.lineTo(spcx + aW * 0.5, spcy - aH * 0.45);
    ctx.lineTo(spcx + aW * 0.5, spcy + aH * 0.05);
    ctx.lineTo(spcx + aW, spcy + aH * 0.05);
    ctx.lineTo(spcx, spcy + aH * 0.55);
    ctx.lineTo(spcx - aW, spcy + aH * 0.05);
    ctx.lineTo(spcx - aW * 0.5, spcy + aH * 0.05);
    ctx.closePath();
    ctx.fill();
  }

  // Castle tile — battlement silhouette (defend this!)
  {
    const cstx = ML_CASTLE_X * tileSize, csty = ML_CASTLE_YG * tileSize;
    const cstcx = cstx + tileSize / 2;
    ctx.fillStyle = 'rgba(200,120,20,0.52)';
    ctx.fillRect(cstx + 1, csty + 1, tileSize - 2, tileSize - 2);
    ctx.strokeStyle = '#e8a828';
    ctx.lineWidth = 1.5;
    ctx.strokeRect(cstx + 2, csty + 2, tileSize - 4, tileSize - 4);
    const bW = Math.max(8, tileSize * 0.62), bH = Math.max(4, tileSize * 0.38);
    const bx = cstcx - bW / 2, by = csty + tileSize * 0.35;
    const mW = bW / 5;
    ctx.fillStyle = '#e8a828';
    // Wall body
    ctx.fillRect(bx, by + bH * 0.38, bW, bH * 0.62);
    // Three merlons
    for (let mi = 0; mi < 3; mi++) {
      ctx.fillRect(bx + mW * mi * 2 + mW * 0.1, by, mW * 1.1, bH * 0.75);
    }
  }

  // Units — smooth movement along path (coordinates are relative to translated origin)
  for (const u of (lane.units || [])) {
    const pos = getUnitPixelPos(u.pathIdx, lane.path, tileSize, tileSize);
    const friendly = (u.ownerLane === myLaneIndex);
    drawUnitShape({ type: u.type, side: friendly ? 'bottom' : 'top' }, pos.x, pos.y);
    const hpRatio = Math.max(0, Math.min(1, u.hp / u.maxHp));
    ctx.fillStyle = '#252d38';
    ctx.fillRect(pos.x - 9, pos.y - 12, 18, 3);
    ctx.fillStyle = friendly ? '#28c0b0' : '#ff3a3a';
    ctx.fillRect(pos.x - 9, pos.y - 12, 18 * hpRatio, 3);
  }

  // Projectiles
  for (const p of (lane.projectiles || [])) {
    const fx = (p.fromX + 0.5) * tileSize;
    const fy = (p.fromY + 0.5) * tileSize;
    const tx = (p.toX + 0.5) * tileSize;
    const ty = (p.toY + 0.5) * tileSize;
    const px = fx + (tx - fx) * p.progress;
    const py = fy + (ty - fy) * p.progress;
    const mlAngle = Math.atan2(ty - fy, tx - fx);
    drawProjectileAt(p.projectileType, p.damageType || '', px, py, mlAngle);
  }

  ctx.restore(); // end translate(offsetX, 0)

  // Gate HP bar — full width at bottom
  const isMine = (viewingLaneIndex === myLaneIndex);
  const gateRatio = Math.max(0, Math.min(1, lane.gateHp / gateHpStart));
  const barY = h - 16;
  ctx.fillStyle = '#1a2030';
  ctx.fillRect(0, barY, w, 16);
  ctx.fillStyle = isMine ? '#28c0b0' : '#ff3a3a';
  ctx.fillRect(0, barY, w * gateRatio, 16);
  ctx.fillStyle = '#dce7ef';
  ctx.font = '11px "Share Tech Mono", monospace';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText('Gate HP: ' + Math.max(0, Math.floor(lane.gateHp)) + ' / ' + gateHpStart, w / 2, barY + 8);

  // Eliminated overlay
  if (lane.eliminated) {
    ctx.fillStyle = 'rgba(255,40,40,0.22)';
    ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = '#ff5050';
    ctx.font = 'bold 48px "Cinzel", serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('ELIMINATED', w / 2, h / 2);
  }

  // Player name & debug info
  const assignment = mlLaneAssignments.find(a => a.laneIndex === lane.laneIndex);
  const playerName = assignment ? assignment.displayName : ('Lane ' + (lane.laneIndex !== undefined ? lane.laneIndex : viewingLaneIndex));
  ctx.fillStyle = isMine ? '#28c0b0' : '#ff8888';
  ctx.font = 'bold 13px "Share Tech Mono", monospace';
  ctx.textAlign = 'left';
  ctx.textBaseline = 'top';
  ctx.fillText(playerName + (isMine ? ' (you)' : ''), 6, 4);

  ctx.fillStyle = '#bcd0e0';
  ctx.font = '11px "Share Tech Mono", monospace';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'top';
  ctx.fillText('T:' + (mlCurrentState ? mlCurrentState.tick : 0), w - 6, 4);

  if (lane.spawnQueueLength > 0) {
    ctx.textAlign = 'left';
    ctx.fillText('Q:' + lane.spawnQueueLength, 6, 20);
  }

  // Hint for own lane
  if (isMine && !isSpectator && !lane.eliminated) {
    ctx.fillStyle = 'rgba(40,192,176,0.35)';
    ctx.font = '9px "Share Tech Mono", monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText('Click empty tile: place wall (5g) | Click wall: convert to tower | Click tower: upgrade', w / 2, barY - 2);
  }
}

// (old multi-view lane drawing replaced by drawMLGridLane above)

function updateHudML(myLane) {
  if (!myLane) return;
  const goldVal = myLane.gold;
  if (goldVal !== _lastHudGold) {
    if (_lastHudGold !== -1) {
      hudGold.classList.remove('gold-flash');
      void hudGold.offsetWidth;
      hudGold.classList.add('gold-flash');
      hudGold.addEventListener('animationend', function onEnd() {
        hudGold.classList.remove('gold-flash');
        hudGold.removeEventListener('animationend', onEnd);
      });
    }
    _lastHudGold = goldVal;
  }
  hudGold.textContent = String(goldVal);
  hudIncome.textContent = String(myLane.income) + 'g';
  hudIncomeTimer.textContent = Math.max(0, Math.ceil((mlCurrentState ? mlCurrentState.incomeTicksRemaining : 0) / tickHz)) + 's';
}

function updateSendButtonsML(myLane) {
  const gold = myLane ? myLane.gold : 0;
  sendButtons.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const m = unitMeta[type] || DEFAULT_UNIT_META[type];
    btn.disabled = !(lobbyState === 'playing' && !isSpectator && gold >= m.cost);
  });
}

// Tower slots are canvas-drawn in ML mode; these DOM functions are no-ops
function updateMLTowerSlots() {}
function updateMLTowerSlotPositions() {}

function showMLLobbyPanel() {
  if (mlLobbyPanel) mlLobbyPanel.style.display = 'block';
}

function hideMLLobbyPanel() {
  if (mlLobbyPanel) mlLobbyPanel.style.display = 'none';
}

function renderMLLobbyPanel(data) {
  if (!mlPlayerList) return;
  mlLobbyPlayers = data.players || [];
  mlPlayerList.innerHTML = '';
  mlLobbyPlayers.forEach(p => {
    const li = document.createElement('li');
    const laneSpan = document.createElement('span');
    laneSpan.className = 'player-lane-num';
    laneSpan.textContent = 'L' + p.laneIndex;
    const nameSpan = document.createElement('span');
    nameSpan.textContent = p.displayName;
    nameSpan.style.flex = '1';
    nameSpan.style.marginLeft = '4px';
    const badge = document.createElement('span');
    badge.className = p.ready ? 'player-ready-badge' : 'player-waiting-badge';
    badge.textContent = p.ready ? 'READY' : 'WAITING';
    li.appendChild(laneSpan);
    li.appendChild(nameSpan);
    li.appendChild(badge);
    mlPlayerList.appendChild(li);
  });
}

function renderFrameML() {
  if (!mlHasFirstSnapshot) return drawWaiting();
  const local = buildMLInterpolatedState(performance.now());
  if (!local) return drawWaiting();

  if (viewingLaneIndex === null) viewingLaneIndex = myLaneIndex;

  const viewingLane = local.lanes && local.lanes[viewingLaneIndex];
  if (viewingLane) drawMLGridLane(viewingLane);

  const myLane = local.lanes && local.lanes[myLaneIndex];
  if (myLane) {
    updateHudML(myLane);
    updateSendButtonsML(myLane);
    updateBarracksHud(myLane);
  }
}

// ── ML socket listeners ───────────────────────────────────────────────────────

socket.on('ml_room_created', data => {
  gameMode = 'multilane';
  mlMyCode = data.code;
  myLaneIndex = data.laneIndex;
  mlIsHost = true;
  setLobbyState('waiting');
  setStatus('Share code \u2014 waiting for players...', 'wait');
  lobbyMlSection.style.display = 'none';
  showMLLobbyPanel();
  if (mlRoomCodeRow) mlRoomCodeRow.style.display = 'block';
  if (mlRoomCodeDisplay) mlRoomCodeDisplay.textContent = data.code;
  if (btnForceStart) btnForceStart.style.display = '';
});

socket.on('ml_room_joined', data => {
  gameMode = 'multilane';
  mlMyCode = data.code;
  myLaneIndex = data.laneIndex;
  mlIsHost = false;
  setLobbyState('waiting');
  setStatus('Joined! Click Ready when set.', 'ok');
  lobbyMlSection.style.display = 'none';
  showMLLobbyPanel();
  if (mlRoomCodeRow) mlRoomCodeRow.style.display = 'block';
  if (mlRoomCodeDisplay) mlRoomCodeDisplay.textContent = data.code;
  if (btnForceStart) btnForceStart.style.display = 'none';
});

socket.on('ml_lobby_update', data => {
  renderMLLobbyPanel(data);
});

socket.on('ml_match_ready', data => {
  mlPlayerCount = data.playerCount || 2;
  mlLaneAssignments = data.laneAssignments || [];
  setLobbyState('playing');
  hideLobby();
  showGameUi();
  initCanvas();
  hideGameOverBanner();
  startRenderLoop();
  hideMLLobbyPanel();

  // Initialize ML grid UI
  viewingLaneIndex = myLaneIndex;

  // Hide classic defense elements
  const defBar = document.getElementById('defense-bar');
  if (defBar) defBar.style.display = 'none';
  towerSlots.forEach(slotEl => { slotEl.style.display = 'none'; });

  // Show ML elements
  if (mlBarracksHud) mlBarracksHud.style.display = 'flex';
  if (mlLaneNav) mlLaneNav.style.display = (mlPlayerCount > 1) ? 'flex' : 'none';
  updateLaneNavLabel();
});

socket.on('ml_match_config', config => applyMatchConfig(config));

socket.on('ml_state_snapshot', state => {
  if (!state) return;
  mlHasFirstSnapshot = true;
  mlPreviousState = mlCurrentState || state;
  mlCurrentState = state;
  mlCurrentStateReceivedAt = performance.now();
});

socket.on('ml_player_eliminated', data => {
  const name = data.displayName || ('Lane ' + data.laneIndex);
  showActionFeedback(name + ' has been eliminated!', true);
});

socket.on('ml_spectator_join', () => {
  isSpectator = true;
  if (spectatorBadge) spectatorBadge.style.display = 'block';
  sendButtons.forEach(btn => { btn.disabled = true; });
  defenseButtons.forEach(btn => { btn.disabled = true; });
  showActionFeedback('You have been eliminated. Spectating...', false);
});

socket.on('ml_game_over', data => {
  if (data.winnerLaneIndex === myLaneIndex) {
    showGameOverBanner('VICTORY');
  } else if (data.winnerLaneIndex === null || data.winnerLaneIndex === undefined) {
    showGameOverBanner('DRAW');
  } else {
    showGameOverBanner((data.winnerName || 'Opponent') + ' WINS');
  }
});

function shortTower(type) {
  if (type === 'archer') return 'AR';
  if (type === 'fighter') return 'FI';
  if (type === 'ballista') return 'BA';
  if (type === 'cannon') return 'CA';
  if (type === 'mage') return 'MG';
  return '';
}

// ── ML grid interaction ───────────────────────────────────────────────────────

function handleMLCanvasClick(clientX, clientY) {
  if (gameMode !== 'multilane') return;
  if (viewingLaneIndex !== myLaneIndex || isSpectator) return;
  if (!mlCurrentState) return;
  if (gameOverBanner.style.display !== 'none') return;

  const rect = canvas.getBoundingClientRect();
  const cx = (clientX - rect.left) * (canvas.width / rect.width);
  const cy = (clientY - rect.top) * (canvas.height / rect.height);
  const { tileSize, offsetX } = getMLGridLayout();
  const gx = Math.floor((cx - offsetX) / tileSize);
  const gy = Math.floor(cy / tileSize);

  if (gx < 0 || gx >= ML_GRID_W || gy < 0 || gy >= ML_GRID_H) return;

  const lane = mlCurrentState.lanes[myLaneIndex];
  if (!lane) return;

  const isWall = lane.walls && lane.walls.some(w => w.x === gx && w.y === gy);
  const towerCell = lane.towerCells && lane.towerCells.find(t => t.x === gx && t.y === gy);

  if (towerCell) {
    showMLTowerUpgradeMenu(gx, gy, towerCell.type, towerCell.level);
  } else if (isWall) {
    showMLWallConvertMenu(gx, gy);
  } else {
    // Empty tile — try to place wall
    closeMLTileMenu();
    sendAction('place_wall', { gridX: gx, gridY: gy });
  }
}

function positionMLTileMenu(gx, gy) {
  if (!mlTileMenu) return;
  const { tileSize, offsetX } = getMLGridLayout();
  const scaleX = canvas.getBoundingClientRect().width / canvas.width;
  const scaleY = canvas.getBoundingClientRect().height / canvas.height;
  const canvasRect = canvas.getBoundingClientRect();
  const uiRect = gameUi.getBoundingClientRect();

  // Center of the clicked tile in CSS pixels relative to game-ui
  const tileCenterX = canvasRect.left - uiRect.left + (offsetX + (gx + 0.5) * tileSize) * scaleX;
  const tileCenterY = canvasRect.top - uiRect.top + (gy + 0.5) * tileSize * scaleY;

  const menuW = 155;
  const menuH = 200;
  let mx = tileCenterX - menuW / 2;
  let my = tileCenterY - menuH - 8; // above tile by default

  // If too close to top, show below instead
  if (my < 4) my = tileCenterY + tileSize * scaleY + 4;

  // Clamp to UI bounds
  mx = Math.max(4, Math.min(uiRect.width - menuW - 4, mx));
  my = Math.max(4, Math.min(uiRect.height - menuH - 4, my));

  mlTileMenu.style.left = mx + 'px';
  mlTileMenu.style.top = my + 'px';
}

function showMLWallConvertMenu(gx, gy) {
  if (!mlTileMenu) return;
  const lane = mlCurrentState && mlCurrentState.lanes[myLaneIndex];
  const gold = lane ? lane.gold : 0;

  let html = '<div class="ml-menu-title">Wall → Tower</div>';
  const towerTypes = ['archer', 'fighter', 'mage', 'ballista', 'cannon'];
  for (const t of towerTypes) {
    const m = towerMeta[t] || DEFAULT_TOWER_META[t];
    const cost = m ? m.cost : '?';
    const disabled = (!m || gold < m.cost) ? ' disabled' : '';
    html += `<button class="ml-tile-btn" data-gx="${gx}" data-gy="${gy}" data-tower="${t}"${disabled}>${cap(t)} (${cost}g)</button>`;
  }
  html += '<button class="ml-tile-btn secondary" data-close>Cancel</button>';
  mlTileMenu.innerHTML = html;

  positionMLTileMenu(gx, gy);
  mlTileMenu.style.display = 'block';
  mlTileMenuJustOpened = true;
  requestAnimationFrame(() => { mlTileMenuJustOpened = false; });

  mlTileMenu.querySelectorAll('[data-tower]').forEach(btn => {
    btn.addEventListener('click', () => {
      const towerType = btn.getAttribute('data-tower');
      const bx = Number(btn.getAttribute('data-gx'));
      const by = Number(btn.getAttribute('data-gy'));
      sendAction('convert_wall', { gridX: bx, gridY: by, towerType });
      closeMLTileMenu();
    });
  });
  const closeBtn = mlTileMenu.querySelector('[data-close]');
  if (closeBtn) closeBtn.addEventListener('click', closeMLTileMenu);
}

function showMLTowerUpgradeMenu(gx, gy, towerType, level) {
  if (!mlTileMenu) return;
  const lane = mlCurrentState && mlCurrentState.lanes[myLaneIndex];
  const gold = lane ? lane.gold : 0;

  const lvl = level || 1;
  const nextLevel = lvl + 1;
  const canUpgrade = nextLevel <= 10;
  const cost = canUpgrade ? getTowerUpgradeCost(towerType, nextLevel) : 0;
  const canAfford = canUpgrade && gold >= cost;
  const stats = getTowerStatsAtLevel(towerType, lvl);

  let html = `<div class="ml-menu-title">${cap(towerType)} Lv${lvl}</div>`;
  html += `<div class="ml-menu-stat">DMG ${stats.dmg.toFixed(1)} | RNG ${stats.range.toFixed(1)} tiles</div>`;
  html += `<div class="ml-menu-stat">${stats.damageType}</div>`;
  if (canUpgrade) {
    html += `<button class="ml-tile-btn" data-gx="${gx}" data-gy="${gy}" data-upgrade${canAfford ? '' : ' disabled'}>Upgrade → Lv${nextLevel} (${cost}g)</button>`;
  } else {
    html += `<div class="ml-menu-stat" style="color:var(--gold);margin-top:4px">MAX LEVEL</div>`;
  }
  html += '<button class="ml-tile-btn secondary" data-close>Close</button>';
  mlTileMenu.innerHTML = html;

  positionMLTileMenu(gx, gy);
  mlTileMenu.style.display = 'block';
  mlTileMenuJustOpened = true;
  requestAnimationFrame(() => { mlTileMenuJustOpened = false; });

  const upgradeBtn = mlTileMenu.querySelector('[data-upgrade]');
  if (upgradeBtn) {
    upgradeBtn.addEventListener('click', () => {
      const bx = Number(upgradeBtn.getAttribute('data-gx'));
      const by = Number(upgradeBtn.getAttribute('data-gy'));
      sendAction('upgrade_tower', { gridX: bx, gridY: by });
      closeMLTileMenu();
    });
  }
  const closeBtn = mlTileMenu.querySelector('[data-close]');
  if (closeBtn) closeBtn.addEventListener('click', closeMLTileMenu);
}

function closeMLTileMenu() {
  if (mlTileMenu) mlTileMenu.style.display = 'none';
}

function updateBarracksHud(lane) {
  if (!mlBarracksHud || !lane) return;
  const level = lane.barracksLevel || 1;
  if (mlBarracksLevel) mlBarracksLevel.textContent = String(level);

  if (btnBarracksUpgrade) {
    const maxLevel = 4;
    if (level >= maxLevel) {
      btnBarracksUpgrade.textContent = 'Barracks Max';
      btnBarracksUpgrade.disabled = true;
    } else {
      // mlBarracksDefs is 0-indexed where [0]=level1, [1]=level2, etc.
      const nextDef = mlBarracksDefs[level]; // next level's def
      const cost = nextDef ? nextDef.cost : '?';
      const reqIncome = nextDef ? nextDef.reqIncome : 0;
      btnBarracksUpgrade.textContent = `Barracks Lv${level + 1} (${cost}g, ${reqIncome}g/inc)`;
      btnBarracksUpgrade.disabled = !nextDef || lane.gold < nextDef.cost || lane.income < nextDef.reqIncome;
    }
  }
}

function updateLaneNavLabel() {
  if (!mlViewingLabel) return;
  if (viewingLaneIndex === myLaneIndex) {
    mlViewingLabel.textContent = 'My Lane';
  } else {
    const assign = mlLaneAssignments.find(a => a.laneIndex === viewingLaneIndex);
    mlViewingLabel.textContent = assign ? assign.displayName : ('Lane ' + viewingLaneIndex);
  }
}

// ── ML grid event listeners ───────────────────────────────────────────────────

canvas.addEventListener('click', function (e) {
  if (gameMode !== 'multilane') return;
  handleMLCanvasClick(e.clientX, e.clientY);
});

canvas.addEventListener('touchend', function (e) {
  if (gameMode !== 'multilane') return;
  e.preventDefault();
  const t = e.changedTouches[0];
  if (t) handleMLCanvasClick(t.clientX, t.clientY);
}, { passive: false });

if (btnBarracksUpgrade) {
  btnBarracksUpgrade.addEventListener('click', () => {
    sendAction('upgrade_barracks', {});
  });
}

if (btnPrevLane) {
  btnPrevLane.addEventListener('click', () => {
    if (mlPlayerCount <= 1) return;
    viewingLaneIndex = ((viewingLaneIndex || 0) - 1 + mlPlayerCount) % mlPlayerCount;
    updateLaneNavLabel();
    closeMLTileMenu();
  });
}

if (btnNextLane) {
  btnNextLane.addEventListener('click', () => {
    if (mlPlayerCount <= 1) return;
    viewingLaneIndex = ((viewingLaneIndex || 0) + 1) % mlPlayerCount;
    updateLaneNavLabel();
    closeMLTileMenu();
  });
}
