'use strict';

const SERVER_URL = window.location.hostname === 'localhost'
  ? 'http://localhost:3000'
  : window.location.origin;

const socket = io(SERVER_URL, {
  autoConnect:          true,
  reconnectionAttempts: 5,
  withCredentials:      true, // send HttpOnly auth cookie automatically
});

const UNIT_TYPES = ['runner', 'footman', 'ironclad', 'warlock', 'golem'];
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

const MARCH_SPEED = 0.00129375; // shared march speed

const DEFAULT_UNIT_META = {
  runner:   { label: 'Runner',   cost: 8,  income: 0.5, dmg: 7,  hp: 60,  bounty: 2, armorType: 'UNARMORED', damageType: 'NORMAL', speedPerTick: 0.00215625,  atkCdTicks: 7,  range: 0.045, special: '-25% splash dmg' },
  footman:  { label: 'Footman',  cost: 10, income: 1,   dmg: 8,  hp: 90,  bounty: 3, armorType: 'MEDIUM',    damageType: 'NORMAL', speedPerTick: MARCH_SPEED,  atkCdTicks: 8,  range: 0.045 },
  ironclad: { label: 'Ironclad', cost: 16, income: 2,   dmg: 9,  hp: 160, bounty: 4, armorType: 'HEAVY',     damageType: 'NORMAL', speedPerTick: MARCH_SPEED,  atkCdTicks: 10, range: 0.045, special: '-30% pierce dmg' },
  warlock:  { label: 'Warlock',  cost: 18, income: 2,   dmg: 12, hp: 80,  bounty: 5, armorType: 'MAGIC',     damageType: 'MAGIC',  speedPerTick: MARCH_SPEED,  atkCdTicks: 11, range: 0.045, special: 'Tower -25% dmg 3s' },
  golem:    { label: 'Golem',    cost: 25, income: 3,   dmg: 14, hp: 240, bounty: 6, armorType: 'HEAVY',     damageType: 'NORMAL', speedPerTick: 0.00090563,   atkCdTicks: 13, range: 0.045, special: '+25% gate dmg' },
};
const DEFAULT_TOWER_META = {
  archer: { label: 'Archer', cost: 10, range: 0.30, dmg: 6.6, atkCdTicks: 12, damageType: 'PIERCE' },
  fighter: { label: 'Fighter', cost: 12, range: 0.18, dmg: 8.8, atkCdTicks: 11, damageType: 'NORMAL' },
  ballista: { label: 'Ballista', cost: 20, range: 0.35, dmg: 12.1, atkCdTicks: 14, damageType: 'SIEGE' },
  cannon: { label: 'Cannon', cost: 30, range: 0.288, dmg: 8, atkCdTicks: 16, damageType: 'SPLASH' },
  mage: { label: 'Mage', cost: 24, range: 0.30, dmg: 13.2, atkCdTicks: 13, damageType: 'MAGIC' },
};

const SNAPSHOT_HZ = 10;
const SNAPSHOT_MS = Math.floor(1000 / SNAPSHOT_HZ);

let unitMeta = Object.assign({}, DEFAULT_UNIT_META);
let towerMeta = Object.assign({}, DEFAULT_TOWER_META);
let tickHz = 20;
let livesStart = 20;

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
let rematchVoted = false;
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
let isClassicHost = false;
let _soloMode = false;
let _soloAiDiff = 'medium';
let _gameType  = 't2t';   // 't2t' | 'td'
let _pvpMode   = '1v1';   // '1v1' | 'ffa' | '2v2'
let _matchType = 'ranked'; // 'ranked' | 'unranked' | 'private' | 'solo'
const DEFAULT_MATCH_SETTINGS = Object.freeze({ startIncome: 10 });

// ML grid constants (mirrors server/sim-multilane.js)
const ML_GRID_W = 11;
const ML_GRID_H = 28;
const ML_SPAWN_X = 5;
const ML_SPAWN_YG = 0;
const ML_CASTLE_X = 5;
const ML_CASTLE_YG = 27;

let viewingLaneIndex = null;    // which lane is shown full-screen in ML mode
let mlBarracksDefs = [];        // from ml_match_config.barracksLevels
let mlMyBarracksIncomeBonus = 0; // income bonus from current barracks level
let mlMyBarracksHpMult = 1;
let mlMyBarracksDmgMult = 1;
let mlMyBarracksSpeedMult = 1;
let mlMyBarracksUnitCostMult = 1;
let mlMyBarracksUnitIncomeMult = 1;
let mlBarracksInfinite = false;
let mlBarracksCostBase = 100;
let mlBarracksReqIncomeBase = 8;
let mlTileMenuJustOpened = false;
let mlActiveTile = null; // { gx, gy } of the currently-open tower upgrade menu
let mlSelectedTiles = []; // [{ gx, gy }]
let mlSelectionKind = null; // "wall" | "tower" | null
let mlSelectionTowerType = null;
let mlWallCost = 2;
let mlMaxWalls = null;

// ── Auto-send state ───────────────────────────────────────────────────────────
let autosendEnabled = {};       // { runner: bool, footman: bool, ... }
let autosendRate = 'normal';
UNIT_TYPES.forEach(t => { autosendEnabled[t] = false; });
let autoBarracksEnabled = false;
let autoBarracksLastAttemptMs = 0;

// ── ML mobile UI state ────────────────────────────────────────────────────────
const ML_INCOME_PERIOD = 240;   // ticks between income payouts
let mlGlobalAutoEnabled = false;
let floatingTexts = [];         // [{x,y,text,color,alpha,age}]
let mlLastIncomeTicksRemaining = -1;
let mlDragPlacing = false;
let mlDragPlacedSet = new Set();
let mlDragSelecting = false;
let mlDragSelectAnchor = null; // { gx, gy, kind, towerType }
let mlDragSelectCurrent = null; // { gx, gy }
let mlWasDrag = false;
let mlHoverTile = null;         // {gx,gy} or null
let mlDragAnchorTile = null;    // first tile in current wall drag
let mlDragAxis = null;          // 'x' (horizontal) or 'y' (vertical)
let mlPrevLives = -1;
let mlPrevBarracksLevel = 1;
let _lastMibGold = -1;

function canPreviewWallAt(lane, gx, gy) {
  if (!lane) return false;
  const isWall  = lane.walls      && lane.walls.some(w => w.x === gx && w.y === gy);
  const isTower = lane.towerCells && lane.towerCells.some(t => t.x === gx && t.y === gy);
  return !isWall && !isTower;
}

function getMLGridTileFromClient(clientX, clientY) {
  const rect = canvas.getBoundingClientRect();
  const cx = (clientX - rect.left) * (canvas.width / rect.width);
  const cy = (clientY - rect.top) * (canvas.height / rect.height);
  const { tileSize, offsetX, offsetY } = getMLGridLayout();
  const gx = Math.floor((cx - offsetX) / tileSize);
  const gy = Math.floor((cy - offsetY) / tileSize);
  if (gx < 0 || gx >= ML_GRID_W || gy < 0 || gy >= ML_GRID_H) return null;
  return { gx, gy };
}

function getAffordableTowerUpgrades(selectedTowers, towerType, gold) {
  const out = [];
  let remainingGold = Number(gold) || 0;
  let totalUpgradeable = 0;
  for (const t of selectedTowers) {
    const level = Number(t.level) || 1;
    if (level >= 10) continue;
    totalUpgradeable += 1;
    const cost = getTowerUpgradeCost(towerType, level + 1);
    if (remainingGold >= cost) {
      out.push({ x: t.x, y: t.y, level, cost });
      remainingGold -= cost;
    }
  }
  const totalCost = out.reduce((sum, it) => sum + it.cost, 0);
  return { affordable: out, totalUpgradeable, totalCost };
}

function commitDragPreviewWalls() {
  if (!mlDragPlacedSet || mlDragPlacedSet.size === 0) return false;
  if (gameMode !== 'multilane' || viewingLaneIndex !== myLaneIndex || isSpectator) {
    mlDragPlacedSet.clear();
    return false;
  }
  for (const key of mlDragPlacedSet) {
    const parts = key.split(',');
    const gx = Number(parts[0]);
    const gy = Number(parts[1]);
    if (!Number.isInteger(gx) || !Number.isInteger(gy)) continue;
    sendAction('place_wall', { gridX: gx, gridY: gy });
  }
  mlDragPlacedSet.clear();
  return true;
}

function updateStraightWallDragPreview(gx, gy) {
  if (!mlDragPlacing || viewingLaneIndex !== myLaneIndex || isSpectator) return;
  if (!mlCurrentState) return;
  const lane = mlCurrentState.lanes[myLaneIndex];
  if (!lane) return;

  if (!mlDragAnchorTile) {
    if (!canPreviewWallAt(lane, gx, gy)) return;
    mlDragAnchorTile = { gx, gy };
    mlDragAxis = null;
    mlDragPlacedSet = new Set([gx + ',' + gy]);
    return;
  }

  const anchorX = mlDragAnchorTile.gx;
  const anchorY = mlDragAnchorTile.gy;
  const dx = gx - anchorX;
  const dy = gy - anchorY;

  if (!mlDragAxis) {
    if (dx === 0 && dy === 0) {
      mlDragPlacedSet = new Set([anchorX + ',' + anchorY]);
      return;
    }
    mlDragAxis = Math.abs(dx) >= Math.abs(dy) ? 'x' : 'y';
  }

  const nextSet = new Set();
  if (mlDragAxis === 'x') {
    const endX = gx;
    const step = endX >= anchorX ? 1 : -1;
    for (let x = anchorX; ; x += step) {
      if (!canPreviewWallAt(lane, x, anchorY)) break;
      nextSet.add(x + ',' + anchorY);
      if (x === endX) break;
    }
  } else {
    const endY = gy;
    const step = endY >= anchorY ? 1 : -1;
    for (let y = anchorY; ; y += step) {
      if (!canPreviewWallAt(lane, anchorX, y)) break;
      nextSet.add(anchorX + ',' + y);
      if (y === endY) break;
    }
  }
  mlDragPlacedSet = nextSet;
}

const lobbyEl = document.getElementById('lobby');
const btnCreate = document.getElementById('btn-create');
const btnJoin = document.getElementById('btn-join');
const btnCopyCode = document.getElementById('btn-copy-code');
const btnCopyLink = document.getElementById('btn-copy-link');
const joinInput = document.getElementById('join-code-input');
const joinNameInput = document.getElementById('join-name-input');
const classicStartIncomeInput = document.getElementById('classic-start-income');
const roomCodeRow = document.getElementById('room-code-row');
const roomCodeDisplay = document.getElementById('room-code-display');
const lobbyStatus = document.getElementById('lobby-status');
const canvasWrap = document.getElementById('game-canvas-wrap');
const canvas = document.getElementById('game-canvas');
const ctx = canvas.getContext('2d');
const gameUi = document.getElementById('game-ui');
const leftRail = document.getElementById('left-rail');
const rightRail = document.getElementById('right-rail');
const btnLeftRailToggle = document.getElementById('btn-left-rail-toggle');
const btnRightRailToggle = document.getElementById('btn-right-rail-toggle');
const authBar = document.getElementById('auth-bar');
const hudGold = document.getElementById('hud-gold');
const hudIncome = document.getElementById('hud-income');
const hudIncomeTimer = document.getElementById('hud-income-timer');
const gameOverBanner = document.getElementById('game-over-banner');
const actionFeedback = document.getElementById('action-feedback');
const disconnectNotice = document.getElementById('disconnect-notice');
const towerPopout = document.getElementById('tower-popout');
const matchupPanel = document.getElementById('matchup-panel');
const incomeLeaderboardPanel = document.getElementById('income-leaderboard');
const statLeaderboardPanel = document.getElementById('stat-leaderboard');
const authBarHomeParent = authBar ? authBar.parentElement : null;
const authBarHomeNextSibling = authBar ? authBar.nextSibling : null;
const sendButtons = Array.from(document.querySelectorAll('.send-btn'));
const defenseButtons = Array.from(document.querySelectorAll('.defense-btn[data-tower-type]'));
const towerSlots = Array.from(document.querySelectorAll('.tower-slot'));

const sendBtnByType = {};
const sendAutoBtnByType = {};
const sendAutoProgressByType = {};
const sendAutoProgressFillByType = {};
sendButtons.forEach(btn => {
  const type = btn.getAttribute('data-unit-type');
  sendBtnByType[type] = btn;

  const label = document.createElement('span');
  label.className = 'send-label';
  label.textContent = btn.textContent || '';
  btn.textContent = '';
  btn.appendChild(label);

  const autoBtn = document.createElement('button');
  autoBtn.type = 'button';
  autoBtn.className = 'send-auto-corner';
  autoBtn.setAttribute('data-unit-type', type);
  autoBtn.textContent = 'AUTO';
  autoBtn.style.display = 'none';
  btn.appendChild(autoBtn);
  sendAutoBtnByType[type] = autoBtn;

  const prog = document.createElement('span');
  prog.className = 'send-auto-progress';
  prog.style.display = 'none';
  const progFill = document.createElement('span');
  progFill.className = 'send-auto-progress-fill';
  prog.appendChild(progFill);
  btn.appendChild(prog);
  sendAutoProgressByType[type] = prog;
  sendAutoProgressFillByType[type] = progFill;
});

// ── Multi-lane DOM refs ───────────────────────────────────────────────────────
const mlLobbyPanel = document.getElementById('ml-lobby-panel');
const mlPlayerList = document.getElementById('ml-player-list');
const mlTeamSetupStatus = document.getElementById('ml-team-setup-status');
const btnCreateMl = document.getElementById('btn-create-ml');
const btnJoinMl = document.getElementById('btn-join-ml');
const mlJoinInput = document.getElementById('ml-join-code-input');
const mlNameInput = document.getElementById('ml-name-input');
const btnReadyMl = document.getElementById('btn-ready-ml');
const btnForceStart = document.getElementById('btn-force-start-ml');
const spectatorBadge = document.getElementById('spectator-badge');
const lobbyQueueSection    = document.getElementById('lobby-queue-section');
const lobbyPrivateSection  = document.getElementById('lobby-private-section');
const lobbySoloSection     = document.getElementById('lobby-solo-section');
const lobbyMlSection       = document.getElementById('lobby-ml-section');
const btnTabRanked   = document.getElementById('btn-tab-ranked');
const btnTabUnranked = document.getElementById('btn-tab-unranked');
const btnTabPrivate  = document.getElementById('btn-tab-private');
const btnTabSolo     = document.getElementById('btn-tab-solo');
const btnGameT2t     = document.getElementById('btn-game-t2t');
const btnGameTd      = document.getElementById('btn-game-td');
const pvpModeSeparator = document.getElementById('pvp-mode-separator');
const pvpModeBar     = document.getElementById('pvp-mode-bar');
const btnPvpFfa      = document.getElementById('btn-pvp-ffa');
const btnPvpTwoV2    = document.getElementById('btn-pvp-2v2');
const queueModeDesc  = document.getElementById('queue-mode-desc');
const btnQueueFind   = document.getElementById('btn-queue-find');
const privateTtUI    = document.getElementById('private-t2t-ui');
const privateTdUI    = document.getElementById('private-td-ui');
const soloTtUI       = document.getElementById('solo-t2t-ui');
const soloTdUI       = document.getElementById('solo-td-ui');
const mlRoomCodeRow = document.getElementById('ml-room-code-row');
const mlRoomCodeDisplay = document.getElementById('ml-room-code-display');
const btnCopyMlCode = document.getElementById('btn-copy-ml-code');
const btnCopyMlLink = document.getElementById('btn-copy-ml-link');
const mlAiControls = document.getElementById('ml-ai-controls');
const mlStartIncomeInput = document.getElementById('ml-start-income');
const btnAddAiEasy = document.getElementById('btn-add-ai-easy');
const btnAddAiMedium = document.getElementById('btn-add-ai-medium');
const btnAddAiHard = document.getElementById('btn-add-ai-hard');

// ── ML grid UI DOM refs ───────────────────────────────────────────────────────
const mlBarracksHud = document.getElementById('ml-barracks-hud');
const mlBarracksLevel = document.getElementById('ml-barracks-level');
const btnBarracksUpgrade = document.getElementById('btn-ml-barracks-upgrade');
const btnBarracksAuto = document.getElementById('btn-ml-barracks-auto');
const mlLaneNav = document.getElementById('ml-lane-nav');
const mlViewingLabel = document.getElementById('ml-viewing-label');
const btnPrevLane = document.getElementById('btn-ml-prev-lane');
const btnNextLane = document.getElementById('btn-ml-next-lane');
const mlTileMenu = document.getElementById('ml-tile-menu');

// ── Auto-send DOM refs ────────────────────────────────────────────────────────
const autosendBar = document.getElementById('autosend-bar');
const autosendToggles = Array.from(document.querySelectorAll('.autosend-toggle'));
const autosendRateSelect = document.getElementById('autosend-rate');

// ── ML mobile UI DOM refs ─────────────────────────────────────────────────────
const hudBar          = document.getElementById('hud-bar');
const mlInfoBar       = document.getElementById('ml-info-bar');
const mibLives        = document.getElementById('mib-lives');
const mibGold         = document.getElementById('mib-gold');
const mibIncome       = document.getElementById('mib-income');
const mibTimer        = document.getElementById('mib-timer');
const mtrFill         = document.getElementById('mtr-fill');
const mibBarracksBtn  = document.getElementById('mib-barracks-btn');
const mibBarracksLv   = document.getElementById('mib-barracks-lv');
const mlLaneTabs      = document.getElementById('ml-lane-tabs');
const mlCmdBar        = document.getElementById('ml-cmd-bar');
const cmdWallBtn      = document.getElementById('cmd-wall-btn');
const cmdWallCost     = document.getElementById('cmd-wall-cost');
const cmdUnitsZone    = document.getElementById('cmd-units-zone');
const cmdUnitBtns     = Array.from(document.querySelectorAll('.cmd-unit-btn'));
const cmdAutoIndicators = Array.from(document.querySelectorAll('.cub-auto'));
const cmdGlobalAuto   = document.getElementById('cmd-global-auto');
const cmdRateBtns     = Array.from(document.querySelectorAll('.cmd-rate-btn'));
const enemyLaneTint   = document.getElementById('enemy-lane-tint');
const mlSpectateNotice = document.getElementById('ml-spectate-notice');
const mlSpectateName  = document.getElementById('ml-spectate-name');
const sideWallBtn     = document.getElementById('side-wall-btn');
const mlMidNextBtn    = document.getElementById('ml-mid-next');

function normalizeMatchSettingsClient(settings) {
  const src = settings && typeof settings === 'object' ? settings : {};
  const rawIncome = Number(src.startIncome);
  const startIncome = Number.isFinite(rawIncome) ? Math.max(0, Math.min(1000, rawIncome)) : DEFAULT_MATCH_SETTINGS.startIncome;
  return { startIncome };
}

function readClassicSettingsFromUi() {
  return normalizeMatchSettingsClient({
    startIncome: classicStartIncomeInput ? classicStartIncomeInput.value : DEFAULT_MATCH_SETTINGS.startIncome,
  });
}

function readMlSettingsFromUi() {
  return normalizeMatchSettingsClient({
    startIncome: mlStartIncomeInput ? mlStartIncomeInput.value : DEFAULT_MATCH_SETTINGS.startIncome,
  });
}

function applyClassicSettingsToUi(settings) {
  const s = normalizeMatchSettingsClient(settings);
  if (classicStartIncomeInput) classicStartIncomeInput.value = String(s.startIncome);
}

function applyMlSettingsToUi(settings) {
  const s = normalizeMatchSettingsClient(settings);
  if (mlStartIncomeInput) mlStartIncomeInput.value = String(s.startIncome);
}

function setClassicSettingsEditable(editable) {
  if (classicStartIncomeInput) classicStartIncomeInput.disabled = !editable;
}

function setMlSettingsEditable(editable) {
  if (mlStartIncomeInput) mlStartIncomeInput.disabled = !editable;
}

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

  // Keep top-right account/party controls uncluttered during gameplay.
  const signedIn = !!Auth.getPlayer();
  const party = document.getElementById('party-panel');
  if (party) party.style.display = (signedIn && state !== 'playing') ? '' : 'none';

  const authBtn = document.getElementById('btn-auth-signout');
  if (authBtn) authBtn.textContent = state === 'playing' ? 'Quit Game' : 'Sign out';
  syncAuthBarPlacement();
  syncFriendsPanelPlacement();
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
  refreshSideRailsForViewport(true);
}

function hideGameUi() {
  canvasWrap.style.display = 'none';
  gameUi.style.display = 'none';
}

function syncAuthBarPlacement() {
  if (!authBar) return;
  const shouldDockInRail = lobbyState === 'playing' && !!rightRail;
  if (shouldDockInRail) {
    if (authBar.parentElement !== rightRail) {
      if (hudBar && hudBar.parentElement === rightRail) rightRail.insertBefore(authBar, hudBar);
      else rightRail.appendChild(authBar);
    }
    authBar.classList.add('auth-in-rail');
    return;
  }

  authBar.classList.remove('auth-in-rail');
  if (!authBarHomeParent || authBar.parentElement === authBarHomeParent) return;
  if (authBarHomeNextSibling && authBarHomeNextSibling.parentNode === authBarHomeParent) {
    authBarHomeParent.insertBefore(authBar, authBarHomeNextSibling);
  } else {
    authBarHomeParent.appendChild(authBar);
  }
}

function isCompactSideRailMode() {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false;
  return (
    window.matchMedia('(max-width: 760px)').matches
    || window.matchMedia('(max-height: 760px)').matches
    || window.matchMedia('(pointer: coarse)').matches
  );
}

function syncRailToggleButtons() {
  if (btnLeftRailToggle && leftRail) {
    btnLeftRailToggle.innerHTML = leftRail.classList.contains('rail-open') ? '&#x25C0;' : '&#x25B6;';
  }
  if (btnRightRailToggle && rightRail) {
    btnRightRailToggle.innerHTML = rightRail.classList.contains('rail-open') ? '&#x25B6;' : '&#x25C0;';
  }
}

function setRailOpen(side, open) {
  const rail = side === 'left' ? leftRail : rightRail;
  if (!rail) return;
  rail.classList.toggle('rail-open', !!open);
  syncRailToggleButtons();
}

function refreshSideRailsForViewport(forceCompactDefault) {
  if (!leftRail || !rightRail) return;
  const compact = isCompactSideRailMode();
  if (!compact) {
    leftRail.classList.add('rail-open');
    rightRail.classList.add('rail-open');
  } else if (forceCompactDefault) {
    leftRail.classList.remove('rail-open');
    rightRail.classList.remove('rail-open');
  }
  syncRailToggleButtons();
}

if (btnLeftRailToggle) {
  btnLeftRailToggle.addEventListener('click', (e) => {
    e.stopPropagation();
    const willOpen = !(leftRail && leftRail.classList.contains('rail-open'));
    setRailOpen('left', willOpen);
    if (willOpen && isCompactSideRailMode()) setRailOpen('right', false);
  });
}

if (btnRightRailToggle) {
  btnRightRailToggle.addEventListener('click', (e) => {
    e.stopPropagation();
    const willOpen = !(rightRail && rightRail.classList.contains('rail-open'));
    setRailOpen('right', willOpen);
    if (willOpen && isCompactSideRailMode()) setRailOpen('left', false);
  });
}

window.addEventListener('resize', () => refreshSideRailsForViewport(false));
if (window.visualViewport) {
  window.visualViewport.addEventListener('resize', () => refreshSideRailsForViewport(false));
}
refreshSideRailsForViewport(false);
syncAuthBarPlacement();

function initCanvas() {
  function resize() {
    const vw = Math.max(1, Math.floor((window.visualViewport && window.visualViewport.width) || window.innerWidth));
    const vh = Math.max(1, Math.floor((window.visualViewport && window.visualViewport.height) || window.innerHeight));
    canvasWrap.style.width = vw + 'px';
    canvasWrap.style.height = vh + 'px';
    canvas.width = canvasWrap.clientWidth || vw;
    canvas.height = canvasWrap.clientHeight || vh;
    if (!hasFirstSnapshot) drawWaiting();
  }
  window.addEventListener('resize', resize);
  if (window.visualViewport) window.visualViewport.addEventListener('resize', resize);
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
  if (Number.isFinite(config.livesStart)) livesStart = config.livesStart;

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
  if (typeof config.barracksInfinite === 'boolean') {
    mlBarracksInfinite = config.barracksInfinite;
  }
  if (Number.isFinite(config.barracksCostBase)) mlBarracksCostBase = config.barracksCostBase;
  if (Number.isFinite(config.barracksReqIncomeBase)) mlBarracksReqIncomeBase = config.barracksReqIncomeBase;
  if (Number.isFinite(config.wallCost)) mlWallCost = config.wallCost;
  mlMaxWalls = Number.isFinite(config.maxWalls) ? config.maxWalls : null;
  updateActionLabels();
}

function updateActionLabels() {
  sendButtons.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const m = unitMeta[type] || DEFAULT_UNIT_META[type];
    if (!m) return;
    const hot = UNIT_TYPES.indexOf(type) + 1;
    const aps = m.atkCdTicks ? (tickHz / m.atkCdTicks) : 0;
    const laneSpeed = m.speedPerTick ? (m.speedPerTick * tickHz * 100) : 0;
    const hpMult = Number.isFinite(mlMyBarracksHpMult) ? mlMyBarracksHpMult : 1;
    const dmgMult = Number.isFinite(mlMyBarracksDmgMult) ? mlMyBarracksDmgMult : 1;
    const speedMult = Number.isFinite(mlMyBarracksSpeedMult) ? mlMyBarracksSpeedMult : 1;
    const unitCost = getScaledUnitCost(m.cost);
    const totalIncome = (function () {
      const v = getScaledUnitIncome(m.income);
      return Number.isInteger(v) ? String(v) : v.toFixed(1);
    }());
    const rows = [
      ['HP', formatScaledStat(m.hp || 0, hpMult, 0)],
      ['DMG', formatScaledStat(m.dmg || 0, dmgMult, 0) + ' ' + (m.damageType || 'NORMAL')],
      ['ARM', String(m.armorType || '-')],
      ['ATK', aps.toFixed(2) + '/s'],
      ['MOVE', formatScaledStat(laneSpeed, speedMult, 2) + '%/s'],
      ['INC', '+' + totalIncome + 'g'],
      ['BNTY', '+' + String(m.bounty || m.income || 0) + 'g'],
    ];
    if (gameMode !== 'multilane') rows.splice(5, 0, ['RNG', String(Math.round((m.range || 0) * 100)) + '%']);
    const labelEl = btn.querySelector('.send-label');
    if (labelEl) {
      const rowsHtml = rows.map(([k, v]) =>
        '<div class="send-card-row"><span class="send-card-key">' + escHtml(k) + '</span><span class="send-card-val">' + escHtml(v) + '</span></div>'
      ).join('');
      labelEl.innerHTML =
        '<div class="send-card-head">'
        + '<span class="send-card-hot">' + escHtml(String(hot)) + '</span>'
        + '<span class="send-card-name">' + escHtml(m.label) + '</span>'
        + '<span class="send-card-cost">' + escHtml(formatScaledStat(m.cost, mlMyBarracksUnitCostMult, 0)) + 'g</span>'
        + '</div>'
        + '<div class="send-card-table">' + rowsHtml + '</div>'
        + (m.special ? '<div class="send-card-note">' + escHtml(m.special) + '</div>' : '');
    } else {
      btn.textContent =
        hot + ' ' + m.label + ' | ' + unitCost + 'g'
        + ' | HP ' + formatScaledStat(m.hp || 0, hpMult, 0)
        + ' | DMG ' + formatScaledStat(m.dmg || 0, dmgMult, 0);
    }
  });
  defenseButtons.forEach(btn => {
    const type = btn.getAttribute('data-tower-type');
    const m = towerMeta[type] || DEFAULT_TOWER_META[type];
    if (!m) return;
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

function getMLBarracksLevelDef(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  if (mlBarracksInfinite) {
    if (lvl === 1) {
      return {
        hpMult: 1, dmgMult: 1, speedMult: 1, structMult: 1,
        incomeBonus: 0, cost: 0, reqIncome: 0,
      };
    }
    const statMult = Math.pow(2, lvl - 1);
    const gateMult = Math.pow(2, lvl - 2);
    return {
      hpMult: statMult,
      dmgMult: statMult,
      speedMult: 1,
      structMult: statMult,
      incomeBonus: 0,
      cost: Math.ceil(mlBarracksCostBase * gateMult),
      reqIncome: Math.ceil(mlBarracksReqIncomeBase * gateMult),
    };
  }
  if (!Array.isArray(mlBarracksDefs) || mlBarracksDefs.length === 0) {
    return {
      hpMult: 1, dmgMult: 1, speedMult: 1, structMult: 1,
      incomeBonus: 0, cost: 0, reqIncome: 0,
    };
  }
  const idx = Math.max(0, Math.min(mlBarracksDefs.length - 1, lvl - 1));
  return mlBarracksDefs[idx] || mlBarracksDefs[0];
}

function formatScaledStat(baseValue, mult, decimals) {
  const base = Number(baseValue) || 0;
  const safeMult = Number.isFinite(mult) ? mult : 1;
  const scaled = base * safeMult;
  const baseText = decimals > 0 ? base.toFixed(decimals) : String(Math.round(base));
  if (Math.abs(safeMult - 1) < 1e-9) return baseText;
  const scaledText = decimals > 0 ? scaled.toFixed(decimals) : String(Math.round(scaled));
  const multText = Number.isInteger(safeMult)
    ? String(safeMult)
    : Number(safeMult.toFixed(2)).toString();
  return baseText + ' -> ' + scaledText + ' (x' + multText + ')';
}

function getScaledUnitCost(baseCost) {
  const base = Math.max(0, Number(baseCost) || 0);
  const mult = Number.isFinite(mlMyBarracksUnitCostMult) ? mlMyBarracksUnitCostMult : 1;
  return Math.ceil(base * Math.max(0, mult));
}

function getScaledUnitIncome(baseIncome) {
  const base = Number(baseIncome) || 0;
  const mult = Number.isFinite(mlMyBarracksUnitIncomeMult) ? mlMyBarracksUnitIncomeMult : 1;
  return (base * Math.max(0, mult)) + mlMyBarracksIncomeBonus;
}

function syncBarracksStatMultipliers(level) {
  const def = getMLBarracksLevelDef(level) || {};
  const nextHp = Number.isFinite(def.hpMult) ? Number(def.hpMult) : 1;
  const nextDmg = Number.isFinite(def.dmgMult) ? Number(def.dmgMult) : 1;
  const nextSpeed = 1;
  const nextUnitCost = Number.isFinite(def.unitCostMult)
    ? Number(def.unitCostMult)
    : (Number.isFinite(def.hpMult) ? Number(def.hpMult) : 1);
  const nextUnitIncome = Number.isFinite(def.unitIncomeMult)
    ? Number(def.unitIncomeMult)
    : (Number.isFinite(def.hpMult) ? Number(def.hpMult) : 1);
  const changed =
    nextHp !== mlMyBarracksHpMult
    || nextDmg !== mlMyBarracksDmgMult
    || nextSpeed !== mlMyBarracksSpeedMult
    || nextUnitCost !== mlMyBarracksUnitCostMult
    || nextUnitIncome !== mlMyBarracksUnitIncomeMult;
  mlMyBarracksHpMult = nextHp;
  mlMyBarracksDmgMult = nextDmg;
  mlMyBarracksSpeedMult = nextSpeed;
  mlMyBarracksUnitCostMult = nextUnitCost;
  mlMyBarracksUnitIncomeMult = nextUnitIncome;
  return changed;
}

function shouldUseCompactRightRail() {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false;
  return (
    window.matchMedia('(max-width: 760px)').matches
    || window.matchMedia('(max-height: 760px)').matches
    || window.matchMedia('(pointer: coarse)').matches
  );
}

function updateStatsPanel() {
  const panel = document.getElementById('stats-panel');
  if (!panel) return;
  const isML = (gameMode === 'multilane');
  const wasExpanded = panel.classList.contains('expanded');
  const hadInit = panel.dataset.init === '1';
  const compactRightRail = shouldUseCompactRightRail();
  let bodyHtml = '';

  if (isML) {
    bodyHtml += '<div class="stats-section-hdr">Wall</div>';
    bodyHtml += '<div class="stats-wall-row">'
      + '<span class="stats-name">Wall</span>'
      + '<span class="stats-cost">' + mlWallCost + 'g</span>'
      + '<span class="stats-max">max ' + (Number.isFinite(mlMaxWalls) ? mlMaxWalls : 'Unlimited') + '</span>'
      + '</div>';
    bodyHtml += '<div class="stats-divider"></div>';
  }

  bodyHtml += '<div class="stats-section-hdr">Towers</div>';
  TOWER_TYPES.forEach(type => {
    const m = towerMeta[type];
    if (!m) return;
    const rangeStr = isML
      ? m.range.toFixed(1) + 't'
      : Math.round(m.range * 100) + '%';
    bodyHtml += '<div class="stats-row">'
      + '<span class="stats-name">' + m.label + '</span>'
      + '<span class="stats-cost">' + m.cost + 'g</span>'
      + '<span class="stats-type">' + m.damageType + '</span>'
      + '<span class="stats-dmg">' + Number(m.dmg).toFixed(1) + '</span>'
      + '<span class="stats-range">' + rangeStr + '</span>'
      + '</div>';
  });

  panel.innerHTML =
    '<button class="stats-toggle-btn" type="button">'
    + '<span class="stats-toggle-arrow">&#x25B6;</span>'
    + ' Towers:'
    + '</button>'
    + '<div class="stats-body">' + bodyHtml + '</div>';

  if ((hadInit && wasExpanded) || (!hadInit && !compactRightRail)) panel.classList.add('expanded');
  panel.dataset.init = '1';

  const toggleBtn = panel.querySelector('.stats-toggle-btn');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      const willExpand = !panel.classList.contains('expanded');
      if (willExpand && shouldUseCompactRightRail() && matchupPanel) {
        matchupPanel.classList.remove('expanded');
      }
      panel.classList.toggle('expanded');
    });
  }
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
  const hadInit = matchupPanel.dataset.init === '1';
  const compactRightRail = shouldUseCompactRightRail();

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

  if ((hadInit && wasExpanded) || (!hadInit && !compactRightRail)) matchupPanel.classList.add('expanded');
  matchupPanel.dataset.init = '1';

  // Re-attach toggle click listener after innerHTML replacement
  const toggleBtn = matchupPanel.querySelector('.matchup-toggle-btn');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      const willExpand = !matchupPanel.classList.contains('expanded');
      if (willExpand && shouldUseCompactRightRail()) {
        const statsPanelEl = document.getElementById('stats-panel');
        if (statsPanelEl) statsPanelEl.classList.remove('expanded');
      }
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
      if (!entry) return { type: null, level: 0, targetMode: 'first' };
      if (typeof entry === 'string') return { type: entry || null, level: entry ? 1 : 0, targetMode: 'first' };
      return {
        type: entry.type || null,
        level: Number(entry.level) || (entry.type ? 1 : 0),
        targetMode: String(entry.targetMode || 'first'),
      };
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
      lives: Number(meRaw.lives) || livesStart,
      barracksLevel: Math.max(1, Number(meRaw.barracksLevel) || 1),
      autosend: meRaw.autosend && typeof meRaw.autosend === 'object'
        ? {
            enabled: !!meRaw.autosend.enabled,
            enabledUnits: Object.assign({}, meRaw.autosend.enabledUnits || {}),
            rate: String(meRaw.autosend.rate || 'normal'),
            tickCounter: Number(meRaw.autosend.tickCounter) || 0,
          }
        : { enabled: false, enabledUnits: {}, rate: 'normal', tickCounter: 0 },
      towers: localizeTowers(meRaw),
    },
    enemy: {
      gold: Number(enRaw.gold) || 0,
      income: Number(enRaw.income) || 0,
      lives: Number(enRaw.lives) || livesStart,
      barracksLevel: Math.max(1, Number(enRaw.barracksLevel) || 1),
      autosend: enRaw.autosend && typeof enRaw.autosend === 'object'
        ? {
            enabled: !!enRaw.autosend.enabled,
            enabledUnits: Object.assign({}, enRaw.autosend.enabledUnits || {}),
            rate: String(enRaw.autosend.rate || 'normal'),
            tickCounter: Number(enRaw.autosend.tickCounter) || 0,
          }
        : { enabled: false, enabledUnits: {}, rate: 'normal', tickCounter: 0 },
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
  updateClassicBarracksHud(local);
  updateSendAutoProgressBars(local.me);
  updateLeaderboards(local, null);
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
  drawCastleGateStructure(local.enemy.lives, livesStart, laneX, laneW, w, h * 0.06, '#ff3a3a', true);
  drawCastleGateStructure(local.me.lives,    livesStart, laneX, laneW, w, h * 0.94, '#28c0b0', false);

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

function drawUnitFireAura(u, front) {
  const now = Date.now() * 0.006;
  const unitSeed = (u && u.id != null) ? Number(String(u.id).length) : 0;
  const flicker = 1 + Math.sin(now + unitSeed) * 0.08;
  const auraW = 15 * flicker;
  const auraH = 20 * flicker;

  const aura = ctx.createRadialGradient(0, -2, 1, 0, -2, auraH);
  aura.addColorStop(0, 'rgba(255,245,190,0.34)');
  aura.addColorStop(0.35, 'rgba(255,170,60,0.28)');
  aura.addColorStop(0.7, 'rgba(255,88,20,0.16)');
  aura.addColorStop(1, 'rgba(255,40,0,0)');
  ctx.fillStyle = aura;
  ctx.beginPath();
  ctx.ellipse(0, -2, auraW, auraH, 0, 0, Math.PI * 2);
  ctx.fill();

  // Flame tongue above the unit silhouette.
  ctx.globalAlpha = 0.7;
  ctx.fillStyle = 'rgba(255,130,30,0.36)';
  ctx.beginPath();
  ctx.moveTo(-3.5, -9);
  ctx.quadraticCurveTo(-5.5, -14, -2, -19);
  ctx.quadraticCurveTo(0.5, -16, 2, -20);
  ctx.quadraticCurveTo(5.5, -14, 3.5, -8);
  ctx.closePath();
  ctx.fill();
  ctx.globalAlpha = 1;

  // Small trailing embers.
  for (let i = 0; i < 3; i++) {
    const phase = now * (1.25 + i * 0.18) + (unitSeed * 0.17) + i * 1.8;
    const ex = Math.sin(phase * 1.3) * (5 + i * 1.6);
    const ey = front * (4 + i * 2.2) + Math.cos(phase) * 1.5;
    const er = 0.9 + (Math.sin(phase * 1.9) + 1) * 0.45;
    ctx.fillStyle = i === 0 ? 'rgba(255,245,170,0.85)' : 'rgba(255,130,30,0.75)';
    ctx.beginPath();
    ctx.arc(ex, ey, er, 0, Math.PI * 2);
    ctx.fill();
  }
}

function drawUnitShape(u, x, y) {
  const friendly = u.side === 'bottom';
  const teamCol = friendly ? '#ff9b2f' : '#ff5f2e';
  const hi = '#ffe8bb';
  const shd = '#4a140b';
  const front = friendly ? -1 : 1; // -1=upward advance, +1=downward advance
  const t = Date.now() * 0.01;
  const pulse = 0.5 + Math.sin(t + ((u && u.id) ? String(u.id).length : 0)) * 0.5;
  ctx.save();
  ctx.translate(x, y);
  drawUnitFireAura(u, front);
  ctx.fillStyle = 'rgba(6,2,1,0.45)';
  ctx.beginPath();
  ctx.ellipse(0, 10, 8.5, 2.8, 0, 0, Math.PI * 2);
  ctx.fill();

  if (u.type === 'footman') {
    // Knight with red shield + sword.
    ctx.fillStyle = '#616b78';
    roundRect(-8, -3, 16, 14, 3); ctx.fill();
    ctx.fillStyle = '#7f8998';
    roundRect(-6, -13, 12, 9, 3); ctx.fill();
    ctx.fillStyle = 'rgba(255,130,70,0.9)';
    ctx.fillRect(-2.2, -10.2, 4.4, 1.4);
    ctx.fillStyle = 'rgba(255,235,180,0.8)';
    ctx.fillRect(-0.8, -9.8, 1.6, 0.8);
    ctx.fillStyle = '#aeb7c5';
    ctx.fillRect(4.5, -1, 2.2, 10);
    ctx.strokeStyle = '#d4dce7'; ctx.lineWidth = 1.2;
    ctx.beginPath();
    ctx.moveTo(5.6, -8); ctx.lineTo(5.6, 3); ctx.stroke();
    ctx.fillStyle = '#d4dce7';
    ctx.beginPath(); ctx.moveTo(4.3, -8); ctx.lineTo(6.9, -8); ctx.lineTo(5.6, -11); ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#862020';
    ctx.beginPath();
    ctx.moveTo(-9, -7); ctx.lineTo(-2.5, -9); ctx.lineTo(-1.5, 6); ctx.lineTo(-9, 8);
    ctx.closePath(); ctx.fill();
    ctx.strokeStyle = '#ba4a4a'; ctx.lineWidth = 1;
    ctx.stroke();
    ctx.strokeStyle = '#e1e6ee'; ctx.lineWidth = 0.9;
    ctx.beginPath(); ctx.moveTo(-8, 0); ctx.lineTo(-3, -1.5); ctx.stroke();

  } else if (u.type === 'golem') {
    // Crystal-rock golem.
    ctx.fillStyle = '#5a5047';
    ctx.beginPath();
    ctx.moveTo(-10, 10); ctx.lineTo(-12, -1); ctx.lineTo(-6, -7); ctx.lineTo(0, -9);
    ctx.lineTo(7, -6); ctx.lineTo(12, -1); ctx.lineTo(10, 10); ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#6f6459';
    ctx.beginPath(); ctx.moveTo(-11, -2); ctx.lineTo(-16, 2); ctx.lineTo(-13, 9); ctx.lineTo(-8, 6); ctx.closePath(); ctx.fill();
    ctx.beginPath(); ctx.moveTo(11, -2); ctx.lineTo(16, 2); ctx.lineTo(13, 9); ctx.lineTo(8, 6); ctx.closePath(); ctx.fill();
    const core = ctx.createRadialGradient(0, 1, 0, 0, 1, 6.5);
    core.addColorStop(0, 'rgba(180,245,255,0.95)');
    core.addColorStop(0.45, 'rgba(60,190,255,0.9)');
    core.addColorStop(1, 'rgba(10,80,120,0)');
    ctx.fillStyle = core;
    ctx.beginPath(); ctx.arc(0, 1, 6.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#84e9ff';
    ctx.beginPath(); ctx.arc(-2.8, -5, 1.1, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(2.8, -5, 1.1, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 0.5 + pulse * 0.25;
    ctx.strokeStyle = '#7ce8ff'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(-4, 3); ctx.lineTo(4, 1); ctx.stroke();
    ctx.globalAlpha = 1;

  } else if (u.type === 'ironclad') {
    // Heavy molten knight with axe.
    ctx.fillStyle = '#505764';
    roundRect(-9, -3, 18, 14, 3); ctx.fill();
    ctx.fillStyle = '#3f4651';
    roundRect(-7, -14, 14, 10, 3); ctx.fill();
    ctx.fillStyle = '#353b46';
    roundRect(-12, -5, 6, 9, 2); ctx.fill();
    roundRect(6, -5, 6, 9, 2); ctx.fill();
    ctx.fillStyle = '#ff822d';
    ctx.fillRect(-3.6, -10.2, 7.2, 1.5);
    ctx.fillStyle = '#ffd697';
    ctx.fillRect(-1.5, -9.9, 3, 0.9);
    ctx.strokeStyle = '#7a4b2d'; ctx.lineWidth = 1.4;
    ctx.beginPath(); ctx.moveTo(7, 8); ctx.lineTo(13, -2); ctx.stroke();
    ctx.fillStyle = '#c9d0d9';
    ctx.beginPath();
    ctx.moveTo(10, -7); ctx.lineTo(15.5, -2); ctx.lineTo(11, 3); ctx.lineTo(6.5, -1);
    ctx.closePath(); ctx.fill();
    ctx.strokeStyle = '#6e7681'; ctx.lineWidth = 1;
    ctx.stroke();

  } else if (u.type === 'runner') {
    // Hooded rogue with twin daggers.
    ctx.fillStyle = '#353230';
    ctx.beginPath();
    ctx.moveTo(-8, 8); ctx.lineTo(-5, -2); ctx.lineTo(0, -10); ctx.lineTo(6, -1); ctx.lineTo(8, 8);
    ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#4d4a46';
    ctx.beginPath();
    ctx.moveTo(-6, -2); ctx.lineTo(0, -14); ctx.lineTo(6, -2); ctx.lineTo(2, 1); ctx.lineTo(-2, 1);
    ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#3a2b20';
    ctx.beginPath(); ctx.arc(0, -5, 2.4, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#ffcb8c';
    ctx.fillRect(-1.8, -5.8, 3.6, 0.9);
    ctx.strokeStyle = '#d8dde5'; ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.moveTo(-6, 0); ctx.lineTo(-13, -4 * front); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(6, 1); ctx.lineTo(13, -2 * front); ctx.stroke();
    ctx.fillStyle = '#d8dde5';
    ctx.beginPath(); ctx.moveTo(-13, -4 * front); ctx.lineTo(-15, -5 * front); ctx.lineTo(-12, -6 * front); ctx.closePath(); ctx.fill();
    ctx.beginPath(); ctx.moveTo(13, -2 * front); ctx.lineTo(15, -3 * front); ctx.lineTo(12, -4 * front); ctx.closePath(); ctx.fill();

  } else if (u.type === 'warlock') {
    // Purple mage with skull staff.
    ctx.strokeStyle = '#d9d2ff'; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.moveTo(8, 8); ctx.lineTo(8, -10); ctx.stroke();
    const orb = ctx.createRadialGradient(8, -11, 0, 8, -11, 4.5);
    orb.addColorStop(0, '#f2dbff');
    orb.addColorStop(0.45, '#b66dff');
    orb.addColorStop(1, 'rgba(120,40,180,0)');
    ctx.fillStyle = orb;
    ctx.beginPath(); ctx.arc(8, -11, 4.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#d6b8ff';
    ctx.beginPath(); ctx.arc(8, -11.5, 1.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#352044';
    ctx.beginPath();
    ctx.moveTo(-7, 8); ctx.lineTo(-5, -2); ctx.lineTo(0, -11); ctx.lineTo(5, -2); ctx.lineTo(7, 8);
    ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#6d3fb3';
    ctx.beginPath();
    ctx.moveTo(-5, 8); ctx.lineTo(-3, 0); ctx.lineTo(0, -8); ctx.lineTo(3, 0); ctx.lineTo(5, 8);
    ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#ffddb8';
    ctx.beginPath(); ctx.arc(0, -2, 2.2, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#c18cff';
    for (let i = 0; i < 3; i++) {
      const a = t * 0.3 + i * 2.1;
      ctx.beginPath();
      ctx.arc(Math.cos(a) * (5 + i), -6 + Math.sin(a) * 2, 0.9, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  // Charred silhouette pass to pull units away from "clean soldier" look.
  ctx.globalAlpha = 0.2;
  ctx.fillStyle = '#160702';
  ctx.beginPath();
  ctx.ellipse(0, 1, 7.5, 10.5, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.globalAlpha = 1;

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

  if (projectileType === 'archer' || damageType === 'PIERCE') {
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
  if (type === 'warlock') return '#c9a8ff';
  return towerColor(type);
}

function getTowerUpgradeCost(type, nextLevel) {
  const m = towerMeta[type] || DEFAULT_TOWER_META[type];
  const base = Number(m.cost) || 0;
  const scale = 0.75 + (0.25 * nextLevel);
  return Math.ceil(base * scale);
}

function getTowerSellValue(type, level) {
  const m = towerMeta[type] || DEFAULT_TOWER_META[type];
  const base = Number(m.cost) || 0;
  let total = mlWallCost + base;
  for (let lvl = 2; lvl <= level; lvl++) {
    total += getTowerUpgradeCost(type, lvl);
  }
  return Math.floor(total * 0.7);
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
  const canAct = (lobbyState === 'playing');
  sendButtons.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const m = unitMeta[type] || DEFAULT_UNIT_META[type];
    const unitCost = getScaledUnitCost(m.cost);
    btn.disabled = !canAct;
    btn.classList.toggle('locked', canAct && gold < unitCost);
  });
}

function renderBarracksUpgradeLabel(level, nextDef) {
  const nextLevel = level + 1;
  if (!nextDef) {
    return '<span class="send-label">'
      + '<span class="send-card-head">'
      + '<span class="send-card-hot">B</span>'
      + '<span class="send-card-name">Barracks MAX</span>'
      + '<span class="send-card-cost">--</span>'
      + '</span>'
      + '<span class="send-card-table">'
      + '<span class="send-card-row"><span class="send-card-key">Need</span><span class="send-card-val">MAX</span></span>'
      + '</span>'
      + '</span>';
  }
  return '<span class="send-label">'
    + '<span class="send-card-head">'
    + '<span class="send-card-hot">B</span>'
    + '<span class="send-card-name">Barracks Lv' + nextLevel + '</span>'
    + '<span class="send-card-cost">' + nextDef.cost + 'g</span>'
    + '</span>'
    + '<span class="send-card-table">'
    + '<span class="send-card-row"><span class="send-card-key">Need</span><span class="send-card-val">' + nextDef.reqIncome + 'g/inc</span></span>'
    + '</span>'
    + '</span>';
}

function updateClassicBarracksHud(local) {
  if (!local || !mlBarracksHud) return;
  const level = Math.max(1, Number(local.me.barracksLevel) || 1);
  if (syncBarracksStatMultipliers(level)) {
    updateActionLabels();
    updateSendButtons(local.me.gold);
  }
  if (mlBarracksLevel) mlBarracksLevel.textContent = String(level);
  const nextDef = getMLBarracksLevelDef(level + 1);
  if (btnBarracksUpgrade) {
    btnBarracksUpgrade.innerHTML = renderBarracksUpgradeLabel(level, nextDef);
    btnBarracksUpgrade.disabled = !nextDef || local.me.gold < nextDef.cost || local.me.income < nextDef.reqIncome;
  }
  refreshBarracksAutoControl();
  maybeAutoUpgradeBarracks(local.me.gold, local.me.income, level);
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
    const t = towers[slot] || { type: null, level: 0, targetMode: 'first' };
    const wasOccupied = slotEl.classList.contains('occupied');
    const nowOccupied = !!t.type;
    slotEl.textContent = '';
    slotEl.dataset.towerType = t.type || '';
    slotEl.dataset.towerLevel = String(t.level || 0);
    slotEl.dataset.towerTargetMode = String(t.targetMode || 'first');
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

function showTowerPopout(slotEl, towerType, towerLevel, targetMode) {
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
  const mode = (targetMode === 'last' || targetMode === 'weakest' || targetMode === 'strongest' || targetMode === 'first') ? targetMode : 'first';
  // XSS note (fix #1): towerType comes from slotEl.dataset.towerType which is set from server state
  // unitType strings validated server-side against known keys — not arbitrary user input.
  // activePopSlot comes from slotEl.getAttribute('data-slot') which is a known SLOT_NAMES constant.
  // stats.damageType is derived from server-validated towerMeta. All numeric values are coerced
  // through Number/toFixed/Math.round before insertion. Risk is low but noted for future audits.
  towerPopout.innerHTML = ''
    + '<span class="tower-pop-line">' + cap(towerType) + ' Lv' + level + '</span>'
    + '<span class="tower-pop-line">' + stats.damageType + '  dmg ' + stats.dmg.toFixed(1) + '  rng ' + Math.round(stats.range * 100) + '%</span>'
    + '<span class="tower-pop-line">Target: ' + cap(mode) + '</span>'
    + '<div class="tower-target-row">'
    + '<button class="tower-target-btn' + (mode === 'first' ? ' active' : '') + '" data-target-slot="' + activePopSlot + '" data-target-mode="first">First</button>'
    + '<button class="tower-target-btn' + (mode === 'last' ? ' active' : '') + '" data-target-slot="' + activePopSlot + '" data-target-mode="last">Last</button>'
    + '<button class="tower-target-btn' + (mode === 'weakest' ? ' active' : '') + '" data-target-slot="' + activePopSlot + '" data-target-mode="weakest">Weakest</button>'
    + '<button class="tower-target-btn' + (mode === 'strongest' ? ' active' : '') + '" data-target-slot="' + activePopSlot + '" data-target-mode="strongest">Strongest</button>'
    + '</div>'
    + '<button class="tower-upgrade-btn" data-upgrade-slot="' + activePopSlot + '"' + (canUpgrade ? '' : ' disabled') + '>'
    + (canLevel ? ('Upgrade to Lv' + nextLevel + ' (' + upgradeCost + 'g)') : 'MAXED')
    + '</button>';
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
  towerPopout.querySelectorAll('.tower-target-btn').forEach(btn => {
    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      const slot = btn.getAttribute('data-target-slot');
      const nextMode = btn.getAttribute('data-target-mode');
      trySetTowerTarget(slot, nextMode);
      showTowerPopout(slotEl, towerType, level, nextMode);
    });
  });
}

function refreshClassicOpenTowerPopout(local) {
  if (gameMode === 'multilane') return;
  if (!towerPopout || towerPopout.style.display === 'none') return;
  if (!activePopSlot) return;

  const upBtn = towerPopout.querySelector('.tower-upgrade-btn');
  if (!upBtn) return;

  const tower = local && local.me && local.me.towers ? local.me.towers[activePopSlot] : null;
  if (!tower || !tower.type) {
    towerPopout.style.display = 'none';
    activePopSlot = null;
    return;
  }

  const nextLevel = (Number(tower.level) || 1) + 1;
  if (nextLevel > 10) {
    upBtn.textContent = 'MAXED';
    upBtn.disabled = true;
    return;
  }

  const cost = getTowerUpgradeCost(tower.type, nextLevel);
  const gold = local.me && Number.isFinite(local.me.gold) ? local.me.gold : 0;
  upBtn.textContent = 'Upgrade to Lv' + nextLevel + ' (' + cost + 'g)';
  upBtn.disabled = gold < cost;
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

function trySetTowerTarget(slot, targetMode) {
  if (!slot) return;
  sendAction('set_tower_target', { slot, targetMode });
}

function showGameOverBanner(message) {
  rematchVoted = false;
  const isVictory = message === 'VICTORY';
  gameOverBanner.innerHTML =
    '<span class="banner-main">' + message + '</span>'
    + '<div class="banner-actions">'
    + '<button class="banner-btn rematch-btn" id="banner-rematch-btn">&#9876; Rematch</button>'
    + '<button class="banner-btn lobby-btn" id="banner-lobby-btn">Lobby</button>'
    + '</div>';
  gameOverBanner.classList.remove('banner-victory', 'banner-defeat');
  gameOverBanner.classList.add(isVictory ? 'banner-victory' : 'banner-defeat');
  gameOverBanner.style.display = 'block';
  document.getElementById('banner-rematch-btn').addEventListener('click', function (e) {
    e.stopPropagation();
    if (rematchVoted) return;
    rematchVoted = true;
    socket.emit('request_rematch');
    this.textContent = 'Waiting...';
    this.disabled = true;
  });
  document.getElementById('banner-lobby-btn').addEventListener('click', function (e) {
    e.stopPropagation();
    _lastHudGold = -1;
    resetToLobby('Game over. Start a new match!');
  });
}

function hideGameOverBanner() {
  gameOverBanner.style.display = 'none';
  gameOverBanner.innerHTML = '';
  gameOverBanner.classList.remove('banner-victory', 'banner-defeat');
}

function resetToLobby(msg) {
  rematchVoted = false;
  myCode = null;
  mySide = null;
  isClassicHost = false;
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

  // Restore default tab
  _soloMode  = false;
  _gameType  = 't2t';
  _pvpMode   = '1v1';
  _matchType = 'ranked';
  activateGameType('t2t');
  activateLobbyTab('ranked');
  hideMLLobbyPanel();
  if (mlRoomCodeRow) mlRoomCodeRow.style.display = 'none';
  if (btnForceStart) btnForceStart.style.display = 'none';
  if (btnReadyMl) btnReadyMl.classList.remove('is-ready');
  if (spectatorBadge) spectatorBadge.style.display = 'none';

  // Restore tower slots for classic mode
  towerSlots.forEach(slotEl => { slotEl.style.display = ''; });
  const defenseBarEl = document.getElementById('defense-bar');
  if (defenseBarEl) defenseBarEl.style.display = 'none';

  // Hide ML-specific UI
  if (mlBarracksHud) mlBarracksHud.style.display = 'none';
  if (mlLaneNav) mlLaneNav.style.display = 'none';
  if (btnPrevLane) btnPrevLane.style.display = 'none';
  if (btnNextLane) btnNextLane.style.display = 'none';
  if (mlTileMenu) mlTileMenu.style.display = 'none';
  if (autosendBar) autosendBar.style.display = 'none';
  if (sideWallBtn) sideWallBtn.style.display = 'none';
  if (mlMidNextBtn) mlMidNextBtn.style.display = 'none';
  viewingLaneIndex = null;

  // Hide new ML UI
  if (mlInfoBar) mlInfoBar.style.display = 'none';
  if (mlCmdBar) mlCmdBar.style.display = 'none';
  if (mlLaneTabs) mlLaneTabs.style.display = 'none';
  if (enemyLaneTint) enemyLaneTint.style.display = 'none';
  if (mlSpectateNotice) mlSpectateNotice.style.display = 'none';

  // Restore classic hud-bar
  if (hudBar) hudBar.style.display = '';

  // Reset new ML state
  floatingTexts = [];
  mlLastIncomeTicksRemaining = -1;
  mlGlobalAutoEnabled = false;
  mlPrevLives = -1;
  mlPrevBarracksLevel = 1;
  _lastMibGold = -1;
  mlHoverTile = null;
  mlDragPlacing = false;
  mlDragPlacedSet.clear();

  // Reset barracks income bonus
  mlMyBarracksIncomeBonus = 0;
  mlMyBarracksHpMult = 1;
  mlMyBarracksDmgMult = 1;
  mlMyBarracksSpeedMult = 1;
  mlMyBarracksUnitCostMult = 1;
  mlMyBarracksUnitIncomeMult = 1;

  // Reset auto-send state
  UNIT_TYPES.forEach(t => { autosendEnabled[t] = false; });
  autosendRate = 'normal';
  if (autosendRateSelect) autosendRateSelect.value = 'normal';
  if (autosendRateSelect) autosendRateSelect.disabled = false;
  autosendToggles.forEach(btn => { btn.disabled = false; });
  autosendToggles.forEach(btn => btn.classList.remove('autosend-active'));
  refreshAutosendControls();
  autoBarracksEnabled = false;
  autoBarracksLastAttemptMs = 0;
  refreshBarracksAutoControl();

  hideGameUi();
  hideGameOverBanner();
  showLobby();
  setLobbyState('idle');
  setStatus(msg, 'error');
}

function leaveCurrentGame() {
  if (lobbyState !== 'playing') return;
  if (!window.confirm('Are you sure you want to quit?')) return;
  socket.emit('leave_game');
  resetToLobby('You left the match.');
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

function escHtml(s) {
  return String(s || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function countClassicTowers(towersObj) {
  let n = 0;
  SLOT_NAMES.forEach(slot => {
    if (towersObj && towersObj[slot] && towersObj[slot].type) n += 1;
  });
  return n;
}

function calcPowerScore(entry) {
  const walls = Number(entry.walls) || 0;
  return Math.round(
    (Number(entry.income) || 0) * 20
    + (Number(entry.gold) || 0)
    + (Number(entry.lives) || 0) * 25
    + (Number(entry.army) || 0) * 8
    + (Number(entry.towers) || 0) * 14
    + (Number(entry.barracks) || 1) * 30
    + walls
  );
}

function buildClassicLeaderboardEntries(local) {
  if (!local || !local.me || !local.enemy) return [];
  const myArmy = (local.units || []).filter(u => u.side === 'bottom').length;
  const enArmy = (local.units || []).filter(u => u.side === 'top').length;
  return [
    {
      laneIndex: 0,
      name: 'You',
      income: Number(local.me.income) || 0,
      gold: Number(local.me.gold) || 0,
      lives: Number(local.me.lives) || 0,
      barracks: Number(local.me.barracksLevel) || 1,
      army: myArmy,
      towers: countClassicTowers(local.me.towers),
      walls: 0,
      eliminated: false,
    },
    {
      laneIndex: 1,
      name: 'Enemy',
      income: Number(local.enemy.income) || 0,
      gold: Number(local.enemy.gold) || 0,
      lives: Number(local.enemy.lives) || 0,
      barracks: Number(local.enemy.barracksLevel) || 1,
      army: enArmy,
      towers: countClassicTowers(local.enemy.towers),
      walls: 0,
      eliminated: false,
    },
  ];
}

function buildMLLeaderboardEntries(local) {
  if (!local || !Array.isArray(local.lanes)) return [];
  return local.lanes.map(lane => {
    const laneIndex = Number(lane.laneIndex);
    const assign = mlLaneAssignments.find(a => a.laneIndex === laneIndex);
    const laneName = assign ? assign.displayName : ('Lane ' + laneIndex);
    const ownerArmy = local.lanes.reduce((acc, ln) => {
      const units = ln.units || [];
      return acc + units.filter(u => Number(u.ownerLane) === laneIndex).length;
    }, 0);
    return {
      laneIndex,
      name: (laneIndex === myLaneIndex ? '\u2605 ' : '') + laneName,
      income: Number(lane.income) || 0,
      gold: Number(lane.gold) || 0,
      lives: Number(lane.lives) || 0,
      barracks: Number(lane.barracksLevel) || 1,
      army: ownerArmy,
      towers: Array.isArray(lane.towerCells) ? lane.towerCells.length : 0,
      walls: Array.isArray(lane.walls) ? lane.walls.length : 0,
      eliminated: !!lane.eliminated,
    };
  });
}

function renderLeaderboardPanels(entries) {
  if (!incomeLeaderboardPanel || !statLeaderboardPanel) return;
  if (!entries || entries.length === 0) {
    incomeLeaderboardPanel.innerHTML = '';
    statLeaderboardPanel.innerHTML = '';
    return;
  }

  const incomeRows = entries
    .slice()
    .sort((a, b) => (b.income - a.income) || (b.gold - a.gold) || (b.lives - a.lives));
  const statRows = entries
    .map(e => Object.assign({}, e, { score: calcPowerScore(e) }))
    .sort((a, b) => (b.score - a.score) || (b.income - a.income) || (b.gold - a.gold));

  let incomeHtml = '<div class="lb-title">Income Leaderboard</div>';
  incomeRows.forEach((r, i) => {
    incomeHtml += '<div class="lb-row">'
      + '<span class="lb-rank">' + (i + 1) + '</span>'
      + '<span class="lb-name' + (r.eliminated ? ' lb-elim' : '') + '">' + escHtml(r.name) + '</span>'
      + '<span class="lb-main">+' + (Number.isInteger(r.income) ? r.income : r.income.toFixed(1)) + '</span>'
      + '<span class="lb-sub">' + r.gold + 'g</span>'
      + '</div>';
  });
  incomeLeaderboardPanel.innerHTML = incomeHtml;

  let statHtml = '<div class="lb-title">Stat Leaderboard</div>'
    + '<div class="lb-eqn">Score = income*20 + gold + lives*25 + army*8 + towers*14 + barracks*30 + walls</div>';
  statRows.forEach((r, i) => {
    const sub = r.army + 'u/' + r.towers + 't';
    statHtml += '<div class="lb-row">'
      + '<span class="lb-rank">' + (i + 1) + '</span>'
      + '<span class="lb-name' + (r.eliminated ? ' lb-elim' : '') + '">' + escHtml(r.name) + '</span>'
      + '<span class="lb-main">' + r.score + '</span>'
      + '<span class="lb-sub">' + sub + '</span>'
      + '</div>';
  });
  statLeaderboardPanel.innerHTML = statHtml;
}

function updateLeaderboards(classicLocal, mlLocal) {
  if (lobbyState !== 'playing') return renderLeaderboardPanels([]);
  if (gameMode === 'multilane') return renderLeaderboardPanels(buildMLLeaderboardEntries(mlLocal));
  return renderLeaderboardPanels(buildClassicLeaderboardEntries(classicLocal));
}

// ── Auto-send helpers ─────────────────────────────────────────────────────────

function syncAutosend() {
  const anyEnabled = Object.values(autosendEnabled).some(v => v);
  sendAction('set_autosend', {
    enabled: anyEnabled,
    enabledUnits: Object.assign({}, autosendEnabled),
    rate: autosendRate,
  });
}

function refreshAutosendControls() {
  mlGlobalAutoEnabled = UNIT_TYPES.every(t => !!autosendEnabled[t]);
  const canToggle = lobbyState === 'playing' && (gameMode !== 'multilane' || !isSpectator);
  autosendToggles.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const on = !!autosendEnabled[type];
    btn.classList.toggle('autosend-active', on);
    btn.textContent = on ? 'AUTO ON' : 'AUTO';
    btn.disabled = !canToggle;
  });
  Object.keys(sendAutoBtnByType).forEach(type => {
    const btn = sendAutoBtnByType[type];
    if (!btn) return;
    const on = !!autosendEnabled[type];
    btn.classList.toggle('send-auto-on', on);
    btn.textContent = on ? 'AUTO ON' : 'AUTO';
    btn.disabled = !canToggle;
    btn.style.display = (lobbyState === 'playing') ? 'flex' : 'none';
  });
  updateSendAutoProgressBars();
}

function updateSendAutoProgressBars(sourceArg) {
  let as = null;
  if (gameMode === 'multilane') {
    const myLane = sourceArg || (mlCurrentState && mlCurrentState.lanes && mlCurrentState.lanes[myLaneIndex]);
    as = myLane && myLane.autosend ? myLane.autosend : null;
  } else {
    const meState = sourceArg || (currentState && currentState.me);
    as = meState && meState.autosend ? meState.autosend : null;
  }
  const ratio = 1;

  UNIT_TYPES.forEach(type => {
    const wrap = sendAutoProgressByType[type];
    const fill = sendAutoProgressFillByType[type];
    if (!wrap || !fill) return;
    const enabled = as
      ? !!(as.enabled && as.enabledUnits && as.enabledUnits[type])
      : !!autosendEnabled[type];
    const show = (lobbyState === 'playing') && (gameMode !== 'multilane' || !isSpectator) && enabled;
    wrap.style.display = show ? 'block' : 'none';
    fill.style.width = Math.round(ratio * 100) + '%';
  });
}

autosendToggles.forEach(btn => {
  btn.addEventListener('click', () => {
    if (lobbyState !== 'playing' || (gameMode === 'multilane' && isSpectator)) return;
    const type = btn.getAttribute('data-unit-type');
    autosendEnabled[type] = !autosendEnabled[type];
    refreshAutosendControls();
    syncAutosend();
  });
});

Object.keys(sendAutoBtnByType).forEach(type => {
  const autoBtn = sendAutoBtnByType[type];
  autoBtn.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (lobbyState !== 'playing' || (gameMode === 'multilane' && isSpectator)) return;
    autosendEnabled[type] = !autosendEnabled[type];
    refreshAutosendControls();
    syncAutosend();
  });
});

if (autosendRateSelect) {
  autosendRateSelect.addEventListener('change', () => {
    autosendRate = autosendRateSelect.value;
    if (lobbyState === 'playing' && (gameMode !== 'multilane' || !isSpectator)) syncAutosend();
  });
}

updateActionLabels();
refreshAutosendControls();
refreshBarracksAutoControl();
applyClassicSettingsToUi(DEFAULT_MATCH_SETTINGS);
applyMlSettingsToUi(DEFAULT_MATCH_SETTINGS);
setClassicSettingsEditable(true);
setMlSettingsEditable(false);

// ── Tab helpers and event handlers ────────────────────────────────────────────

function updateLobbyContent() {
  // Queue description + button label
  if (_matchType === 'ranked' || _matchType === 'unranked') {
    const r = _matchType === 'ranked';
    const modeStr = _gameType === 't2t' ? '1v1' : (_pvpMode === '2v2' ? '2v2' : 'FFA');
    if (queueModeDesc) queueModeDesc.textContent = r
      ? `Rated ${modeStr} — requires account.`
      : `Casual ${modeStr} — no rating at stake.`;
    if (btnQueueFind) btnQueueFind.textContent = r ? `⚔ Find Ranked ${modeStr}` : `🛡 Find ${modeStr} Match`;
  }
  // Private sub-panel
  if (privateTtUI) privateTtUI.style.display = _gameType === 'td' ? 'none' : '';
  if (privateTdUI) privateTdUI.style.display = _gameType === 'td' ? '' : 'none';
  // Solo sub-panel
  if (soloTtUI) soloTtUI.style.display = _gameType === 'td' ? 'none' : '';
  if (soloTdUI) soloTdUI.style.display = _gameType === 'td' ? '' : 'none';
}

function setPvpModeUiVisibility(show) {
  if (pvpModeBar) pvpModeBar.style.display = show ? '' : 'none';
  if (pvpModeSeparator) pvpModeSeparator.style.display = show ? '' : 'none';
}

function activateLobbyTab(tab) {
  _matchType = tab;
  // Show/hide pvp bar: visible for TD + not solo
  setPvpModeUiVisibility(_gameType === 'td' && tab !== 'solo');
  // Show/hide content sections
  if (lobbyQueueSection)   lobbyQueueSection.style.display   = (tab === 'ranked' || tab === 'unranked') ? '' : 'none';
  if (lobbyPrivateSection) lobbyPrivateSection.style.display = tab === 'private' ? '' : 'none';
  if (lobbySoloSection)    lobbySoloSection.style.display    = tab === 'solo'    ? '' : 'none';
  // Active tab state
  [btnTabRanked, btnTabUnranked, btnTabPrivate, btnTabSolo].forEach(b => b && b.classList.remove('active'));
  const tabEl = { ranked: btnTabRanked, unranked: btnTabUnranked, private: btnTabPrivate, solo: btnTabSolo }[tab];
  if (tabEl) tabEl.classList.add('active');
  updateLobbyContent();
}

function activateGameType(type) {
  _gameType = type;
  if (type === 't2t') _pvpMode = '1v1';
  [btnGameT2t, btnGameTd].forEach(b => b && b.classList.remove('active'));
  const gameEl = { t2t: btnGameT2t, td: btnGameTd }[type];
  if (gameEl) gameEl.classList.add('active');
  setPvpModeUiVisibility(type === 'td' && _matchType !== 'solo');
  updateLobbyContent();
}

function activatePvpMode(mode) {
  _pvpMode = mode;
  [btnPvpFfa, btnPvpTwoV2].forEach(b => b && b.classList.remove('active'));
  const pvpEl = { ffa: btnPvpFfa, '2v2': btnPvpTwoV2 }[mode];
  if (pvpEl) pvpEl.classList.add('active');
  updateLobbyContent();
}

if (btnGameT2t) btnGameT2t.addEventListener('click', () => {
  if (lobbyState === 'playing') return;
  setLobbyState('idle');
  setStatus('', '');
  gameMode = 'classic';
  activateGameType('t2t');
  hideMLLobbyPanel();
});

if (btnGameTd) btnGameTd.addEventListener('click', () => {
  if (lobbyState === 'playing') return;
  setLobbyState('idle');
  setStatus('', '');
  gameMode = 'multilane';
  activateGameType('td');
  hideMLLobbyPanel();
});

if (btnPvpFfa) btnPvpFfa.addEventListener('click', () => {
  activatePvpMode('ffa');
});

if (btnPvpTwoV2) btnPvpTwoV2.addEventListener('click', () => {
  activatePvpMode('2v2');
});

btnTabRanked.addEventListener('click', () => {
  if (lobbyState === 'playing') return;
  setLobbyState('idle');
  setStatus('', '');
  activateLobbyTab('ranked');
  hideMLLobbyPanel();
});

btnTabUnranked.addEventListener('click', () => {
  if (lobbyState === 'playing') return;
  setLobbyState('idle');
  setStatus('', '');
  activateLobbyTab('unranked');
  hideMLLobbyPanel();
});

btnTabPrivate.addEventListener('click', () => {
  if (lobbyState === 'playing') return;
  setLobbyState('idle');
  setStatus('', '');
  gameMode = _gameType === 'td' ? 'multilane' : 'classic';
  activateLobbyTab('private');
  hideMLLobbyPanel();
  setClassicSettingsEditable(isClassicHost || myCode === null);
});

btnTabSolo.addEventListener('click', () => {
  if (lobbyState === 'playing') return;
  setLobbyState('idle');
  setStatus('', '');
  gameMode = 'multilane';
  activateLobbyTab('solo');
  hideMLLobbyPanel();
});

// Initialize queue description on page load
updateLobbyContent();

btnCreateMl.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  const name = Auth.getPlayer()?.displayName || mlNameInput.value.trim() || 'Player';
  const settings = readMlSettingsFromUi();
  setLobbyState('creating');
  setStatus('Creating ML room...', 'wait');
  socket.emit('create_ml_room', { displayName: name, settings });
});

btnJoinMl.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  const code = mlJoinInput.value.trim().toUpperCase();
  if (code.length < 6) return setStatus('Enter the full 6-character room code.', 'error');
  const name = Auth.getPlayer()?.displayName || mlNameInput.value.trim() || 'Player';
  setLobbyState('joining');
  setStatus('Joining ML room...', 'wait');
  socket.emit('join_ml_room', { code, displayName: name });
});

mlJoinInput.addEventListener('keydown', e => {
  if (e.key === 'Enter') btnJoinMl.click();
});

// ── Lobby queue entry button ──────────────────────────────────────────────────

if (btnQueueFind) {
  btnQueueFind.addEventListener('click', () => {
    const modeMap = {
      't2t-ranked':      'ranked_1v1',
      't2t-unranked':    'casual_1v1',
      'td-ffa-ranked':   'ranked_ffa',
      'td-ffa-unranked': 'casual_ffa',
      'td-2v2-ranked':   'ranked_2v2',
      'td-2v2-unranked': 'casual_2v2',
    };
    const key = _gameType === 't2t'
      ? `${_gameType}-${_matchType}`
      : `td-${_pvpMode}-${_matchType}`;
    socket.emit('queue:enter', { mode: modeMap[key] });
  });
}

// ── Solo mode ─────────────────────────────────────────────────────────────────

const btnSoloPlay   = document.getElementById('btn-solo-play');
const soloNameInput = document.getElementById('solo-name-input');

document.querySelectorAll('.solo-diff-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.solo-diff-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    _soloAiDiff = btn.dataset.diff;
  });
});

if (btnSoloPlay) {
  btnSoloPlay.addEventListener('click', () => {
    if (lobbyState !== 'idle') return;
    const name = Auth.getPlayer()?.displayName || (soloNameInput ? soloNameInput.value.trim() : '') || 'Player';
    _soloMode = true;
    setLobbyState('creating');
    setStatus('Setting up solo match\u2026', 'wait');
    socket.emit('create_ml_room', { displayName: name, settings: readMlSettingsFromUi() });
  });
}

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

// ── AI add/remove buttons (host only) ─────────────────────────────────────────
['easy', 'medium', 'hard'].forEach(diff => {
  const btn = document.getElementById('btn-add-ai-' + diff);
  if (btn) {
    btn.addEventListener('click', () => {
      if (!mlMyCode) return;
      socket.emit('add_ai_to_ml_room', { difficulty: diff });
    });
  }
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
  const settings = readClassicSettingsFromUi();
  setLobbyState('creating');
  setStatus('Creating room...', 'wait');
  socket.emit('create_room', { settings });
});

btnJoin.addEventListener('click', () => {
  if (lobbyState !== 'idle') return;
  const code = joinInput.value.trim().toUpperCase();
  if (code.length < 6) return setStatus('Enter the full 6-character room code.', 'error');
  const displayName = Auth.getPlayer()?.displayName || joinNameInput.value.trim() || 'Player';
  setLobbyState('joining');
  setStatus('Joining room...', 'wait');
  socket.emit('join_room', { code, displayName });
});

joinInput.addEventListener('keydown', e => {
  if (e.key === 'Enter') btnJoin.click();
});

if (classicStartIncomeInput) {
  classicStartIncomeInput.addEventListener('change', () => {
    applyClassicSettingsToUi(readClassicSettingsFromUi());
    if (isClassicHost && myCode) {
      socket.emit('update_room_settings', { settings: readClassicSettingsFromUi() });
    }
  });
}
if (mlStartIncomeInput) {
  mlStartIncomeInput.addEventListener('change', () => {
    applyMlSettingsToUi(readMlSettingsFromUi());
    if (mlIsHost && mlMyCode && lobbyState !== 'playing') {
      socket.emit('update_ml_room_settings', { settings: readMlSettingsFromUi() });
    }
  });
}

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
  btn.addEventListener('click', (e) => {
    if (e.target && e.target.closest('.send-auto-corner')) return;
    trySpawn(btn.getAttribute('data-unit-type'));
  });
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
    const existingTargetMode = String(slotEl.dataset.towerTargetMode || 'first');
    if (existing) {
      showTowerPopout(slotEl, existing, existingLevel, existingTargetMode);
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
  isClassicHost = true;
  applyClassicSettingsToUi(data.settings || readClassicSettingsFromUi());
  setClassicSettingsEditable(true);
  setLobbyState('waiting');
  roomCodeDisplay.textContent = data.code;
  roomCodeRow.style.display = 'block';
  setStatus('Share this code - waiting for opponent...', 'wait');
});

socket.on('room_joined', data => {
  myCode = data.code;
  mySide = data.side;
  isClassicHost = false;
  setClassicSettingsEditable(false);
  setLobbyState('playing');
  setStatus('Match starting...', 'ok');
});

socket.on('room_settings_update', data => {
  if (!data || !data.settings) return;
  applyClassicSettingsToUi(data.settings);
});

socket.on('match_ready', () => {
  hasFirstSnapshot = false;
  previousState = null;
  currentState = null;
  _lastHudGold = -1;
  setLobbyState('playing');
  hideLobby();
  showGameUi();
  initCanvas();
  hideGameOverBanner();
  startRenderLoop();

  if (autosendBar) autosendBar.style.display = 'none';
  if (mlBarracksHud) mlBarracksHud.style.display = 'grid';
  if (mlInfoBar) mlInfoBar.style.display = 'none';
  if (mlLaneTabs) mlLaneTabs.style.display = 'none';
  if (mlCmdBar) mlCmdBar.style.display = 'none';
  if (sideWallBtn) sideWallBtn.style.display = 'none';
  const defenseBarEl = document.getElementById('defense-bar');
  if (defenseBarEl) defenseBarEl.style.display = 'none';
  if (mlMidNextBtn) mlMidNextBtn.style.display = 'none';
  if (mlLaneNav) mlLaneNav.style.display = 'none';
  if (btnPrevLane) btnPrevLane.style.display = 'none';
  if (btnNextLane) btnNextLane.style.display = 'none';
  if (hudBar) hudBar.style.display = '';
  refreshAutosendControls();
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

socket.on('left_game_ack', () => {
  if (lobbyState === 'playing') resetToLobby('You left the match.');
});

socket.on('state_snapshot', state => {
  const local = localizeState(state);
  if (!local) return;
  hasFirstSnapshot = true;
  previousState = currentState || local;
  currentState = local;
  currentStateReceivedAt = performance.now();
  refreshClassicOpenTowerPopout(local);
  if (local.me && local.me.autosend) {
    UNIT_TYPES.forEach(type => {
      autosendEnabled[type] = !!(local.me.autosend.enabled && local.me.autosend.enabledUnits && local.me.autosend.enabledUnits[type]);
    });
    autosendRate = String(local.me.autosend.rate || autosendRate);
    if (autosendRateSelect) autosendRateSelect.value = autosendRate;
    refreshAutosendControls();
  }
});

socket.on('game_over', payload => {
  if (!payload || !payload.winner) return;
  showGameOverBanner(payload.winner === mySide ? 'VICTORY' : 'DEFEAT');
});

socket.on('rematch_vote', data => {
  const btn = document.getElementById('banner-rematch-btn');
  if (btn) {
    btn.textContent = rematchVoted
      ? ('Waiting... (' + data.count + '/' + data.needed + ')')
      : ('Rematch (' + data.count + '/' + data.needed + ')');
    btn.disabled = rematchVoted;
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
      // Switch to Tower Defence > Private tab
      activateGameType('td');
      activateLobbyTab('private');
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

// ── Disconnect / reconnect helpers ───────────────────────────────────────────
let _disconnectCountdownHandle = null;

function showDisconnectNotice(html) {
  if (!disconnectNotice) return;
  disconnectNotice.innerHTML = html;
  disconnectNotice.style.display = '';
}

function hideDisconnectNotice() {
  if (!disconnectNotice) return;
  disconnectNotice.style.display = 'none';
  disconnectNotice.innerHTML = '';
  if (_disconnectCountdownHandle) { clearInterval(_disconnectCountdownHandle); _disconnectCountdownHandle = null; }
}

function startGraceCountdown(gracePeriodMs) {
  if (!disconnectNotice) return;
  let remaining = Math.ceil(gracePeriodMs / 1000);
  const update = () => {
    showDisconnectNotice(`Opponent disconnected — forfeiting in <b>${remaining}s</b> if not back`);
    remaining--;
    if (remaining < 0) { hideDisconnectNotice(); }
  };
  update();
  _disconnectCountdownHandle = setInterval(update, 1000);
}

socket.on('reconnect_token', ({ token } = {}) => {
  if (token) localStorage.setItem('cd_reconnect', token);
});

socket.on('connect', () => {
  console.log('[socket] connected:', socket.id);
  if (lobbyState !== 'playing') setStatus('', '');
  const token = localStorage.getItem('cd_reconnect');
  if (token) {
    console.log('[reconnect] attempting rejoin…');
    socket.emit('rejoin_game', { token });
  }
});

socket.on('rejoin_ack', ({ success, mode, side, laneIndex, code } = {}) => {
  if (!success) return;
  console.log('[reconnect] rejoin ack', mode, side ?? laneIndex);
  localStorage.removeItem('cd_reconnect');
  hideDisconnectNotice();

  if (mode === 'classic') {
    myCode = code;
    mySide = side;
    gameMode = 'classic';
    hasFirstSnapshot = false;
    previousState = null;
    currentState = null;
    _lastHudGold = -1;
    setLobbyState('playing');
    hideLobby();
    showGameUi();
    initCanvas();
    hideGameOverBanner();
    startRenderLoop();
    if (autosendBar) autosendBar.style.display = 'none';
    if (mlBarracksHud) mlBarracksHud.style.display = 'grid';
    if (mlInfoBar) mlInfoBar.style.display = 'none';
    if (mlLaneTabs) mlLaneTabs.style.display = 'none';
    if (mlCmdBar) mlCmdBar.style.display = 'none';
    if (hudBar) hudBar.style.display = '';
    refreshAutosendControls();
    showActionFeedback('Reconnected!', true);
  } else {
    // ML: set vars now; ml_match_ready (sent just before this) has already fired and set up UI
    mlMyCode = code;
    myLaneIndex = laneIndex;
    gameMode = 'multilane';
    showActionFeedback('Reconnected!', true);
  }
});

socket.on('rejoin_fail', ({ reason } = {}) => {
  console.log('[reconnect] rejoin fail:', reason);
  localStorage.removeItem('cd_reconnect');
  hideDisconnectNotice();
  if (lobbyState === 'playing') resetToLobby('Could not reconnect. Game may be over.');
});

socket.on('opponent_disconnected', ({ side, gracePeriodMs } = {}) => {
  if (lobbyState !== 'playing') return;
  startGraceCountdown(gracePeriodMs || 120000);
});

socket.on('player_disconnected', ({ laneIndex, displayName, gracePeriodMs } = {}) => {
  if (lobbyState !== 'playing') return;
  showDisconnectNotice(`${displayName || 'A player'} disconnected — ${Math.ceil((gracePeriodMs || 120000) / 1000)}s to reconnect`);
  if (_disconnectCountdownHandle) clearInterval(_disconnectCountdownHandle);
  let remaining = Math.ceil((gracePeriodMs || 120000) / 1000);
  _disconnectCountdownHandle = setInterval(() => {
    remaining--;
    if (remaining <= 0) { hideDisconnectNotice(); return; }
    showDisconnectNotice(`${displayName || 'A player'} disconnected — ${remaining}s to reconnect`);
  }, 1000);
});

socket.on('player_reconnected', ({ displayName, mode: _m } = {}) => {
  hideDisconnectNotice();
  if (lobbyState === 'playing') showActionFeedback((displayName || 'Player') + ' reconnected', true);
});

socket.on('disconnect', reason => {
  console.log('[socket] disconnected:', reason);
  // Expected during auth cookie refresh / manual reconnect.
  if (reason === 'io client disconnect') return;
  if (lobbyState === 'playing') {
    if (localStorage.getItem('cd_reconnect')) {
      showDisconnectNotice('Reconnecting…');
    } else {
      resetToLobby('Connection lost. Game ended.');
    }
  } else {
    setStatus('Connection lost. Reload to retry.', 'error');
  }
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
    const walls = Array.isArray(lane.walls) ? lane.walls : (Array.isArray(prevLane.walls) ? prevLane.walls : []);
    return Object.assign({}, lane, { units, walls });
  });
  return result;
}

// ── ML grid rendering ─────────────────────────────────────────────────────────

// Returns square-tile layout centered in the middle pane between side rails.
// Prefer full-height lanes; if width is constrained, shrink to fit center pane.
function getMLGridLayout() {
  const w = canvas.width;
  const h = canvas.height;

  const leftRailRect = document.getElementById('left-rail')?.getBoundingClientRect();
  const rightRailRect = document.getElementById('right-rail')?.getBoundingClientRect();

  const leftPaneX = leftRailRect ? Math.max(0, Math.ceil(leftRailRect.right + 8)) : 0;
  const rightPaneX = rightRailRect ? Math.min(w, Math.floor(rightRailRect.left - 8)) : w;
  const availW = Math.max(40, rightPaneX - leftPaneX);
  const availH = h;

  const tileByHeight = availH / ML_GRID_H;
  const tileByWidth = availW / ML_GRID_W;
  const tileSize = Math.max(4, Math.min(tileByHeight, tileByWidth));

  const gridW = tileSize * ML_GRID_W;
  const gridH = tileSize * ML_GRID_H;
  const paneCenterX = leftPaneX + (availW / 2);
  const offsetX = Math.floor(paneCenterX - (gridW / 2));
  const offsetY = Math.floor((h - gridH) / 2);
  return { tileSize, offsetX, offsetY, gridW, gridH };
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

function getMLUnitRenderJitter(unitId, tileSize) {
  const key = String(unitId || "");
  let hash = 2166136261;
  for (let i = 0; i < key.length; i++) {
    hash ^= key.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  const xBucket = (hash >>> 0) % 5;         // 0..4
  const yBucket = ((hash >>> 3) % 3);       // 0..2
  return {
    x: (xBucket - 2) * Math.max(1, tileSize * 0.09),
    y: (yBucket - 1) * Math.max(0.5, tileSize * 0.05),
  };
}

function drawMLGridLane(lane, local) {
  if (!lane) return;
  const w = canvas.width;
  const h = canvas.height;
  const { tileSize, offsetX, offsetY, gridW, gridH } = getMLGridLayout();
  normalizeMLSelectionForLane(lane);

  // Full-canvas background — gutters get stone tile pattern, grid gets dark fill
  ctx.clearRect(0, 0, w, h);

  // Stone tile pattern for gutters (areas outside the centered grid rect)
  const stoneSz = 24;
  for (let gsy = 0; gsy < h; gsy += stoneSz) {
    for (let gsx = 0; gsx < w; gsx += stoneSz) {
      if (gsx + stoneSz > offsetX && gsx < offsetX + gridW &&
          gsy + stoneSz > offsetY && gsy < offsetY + gridH) continue;
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
  ctx.fillRect(offsetX, offsetY, gridW, gridH);

  // Zone tints — spawn zone (teal, top) / castle zone (amber, bottom)
  const zoneRows = Math.floor(ML_GRID_H * 0.28);
  const topZoneGrad = ctx.createLinearGradient(0, offsetY, 0, offsetY + zoneRows * tileSize);
  topZoneGrad.addColorStop(0, 'rgba(40,192,176,0.13)');
  topZoneGrad.addColorStop(1, 'rgba(40,192,176,0)');
  ctx.fillStyle = topZoneGrad;
  ctx.fillRect(offsetX, offsetY, gridW, zoneRows * tileSize);

  const botZoneAbsY = offsetY + (ML_GRID_H - zoneRows) * tileSize;
  const botZoneGrad = ctx.createLinearGradient(0, botZoneAbsY, 0, offsetY + gridH);
  botZoneGrad.addColorStop(0, 'rgba(200,120,40,0)');
  botZoneGrad.addColorStop(1, 'rgba(200,120,40,0.13)');
  ctx.fillStyle = botZoneGrad;
  ctx.fillRect(offsetX, botZoneAbsY, gridW, offsetY + gridH - botZoneAbsY);

  // Subtle glow strips on left and right grid edges
  const lgGrad = ctx.createLinearGradient(offsetX, 0, offsetX + 14, 0);
  lgGrad.addColorStop(0, 'rgba(40,192,176,0.22)');
  lgGrad.addColorStop(1, 'rgba(40,192,176,0)');
  ctx.fillStyle = lgGrad;
  ctx.fillRect(offsetX, offsetY, 14, gridH);
  const rgGrad = ctx.createLinearGradient(offsetX + gridW - 14, 0, offsetX + gridW, 0);
  rgGrad.addColorStop(0, 'rgba(40,192,176,0)');
  rgGrad.addColorStop(1, 'rgba(40,192,176,0.22)');
  ctx.fillStyle = rgGrad;
  ctx.fillRect(offsetX + gridW - 14, offsetY, 14, gridH);

  // All grid-relative drawing is offset by (offsetX, offsetY) so tiles are square and centered.
  ctx.save();
  ctx.translate(offsetX, offsetY);

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
    for (const { x, y, type, level, debuffed } of lane.towerCells) {
      const col = debuffed ? '#c060ff' : towerColor(type);
      // Dark tile background + colored border
      ctx.fillStyle = debuffed ? '#1a1430' : '#1a2230';
      ctx.fillRect(x * tileSize + 1, y * tileSize + 1, tileSize - 2, tileSize - 2);
      ctx.strokeStyle = col; ctx.lineWidth = debuffed ? 2 : 1.5;
      ctx.strokeRect(x * tileSize + 1, y * tileSize + 1, tileSize - 2, tileSize - 2);
      // Debuff overlay pulsing
      if (debuffed) {
        ctx.fillStyle = 'rgba(176,96,255,0.14)';
        ctx.fillRect(x * tileSize + 1, y * tileSize + 1, tileSize - 2, tileSize - 2);
      }
      // Scaled tower art centered in tile
      const tcx = (x + 0.5) * tileSize;
      const tcy = (y + 0.5) * tileSize;
      drawTowerArt(type, tcx, tcy, tileSize / 22);
      // Level badge at tile bottom
      const fs = Math.max(5, Math.floor(tileSize * 0.22));
      ctx.fillStyle = 'rgba(6,13,24,0.7)';
      ctx.fillRect(tcx - fs, (y + 1) * tileSize - fs - 2, fs * 2, fs + 2);
      ctx.fillStyle = debuffed ? '#c060ff' : '#e8a828';
      ctx.font = `bold ${fs}px "Share Tech Mono", monospace`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
      ctx.fillText(String(level || 1), tcx, (y + 1) * tileSize - 1);
    }
  }


  // Selected tiles highlight (walls or same-type towers).
  if (viewingLaneIndex === myLaneIndex && mlSelectedTiles.length > 0) {
    const selColor = mlSelectionKind === 'wall' ? 'rgba(64, 220, 168, 0.92)' : 'rgba(90, 170, 255, 0.95)';
    const selFill = mlSelectionKind === 'wall' ? 'rgba(64, 220, 168, 0.20)' : 'rgba(90, 170, 255, 0.18)';
    ctx.save();
    for (const t of mlSelectedTiles) {
      const px = t.gx * tileSize + 1;
      const py = t.gy * tileSize + 1;
      const ps = tileSize - 2;
      ctx.fillStyle = selFill;
      ctx.fillRect(px, py, ps, ps);
      ctx.strokeStyle = selColor;
      ctx.lineWidth = (mlActiveTile && mlActiveTile.gx === t.gx && mlActiveTile.gy === t.gy) ? 2 : 1.2;
      ctx.strokeRect(px + 0.5, py + 0.5, ps - 1, ps - 1);
    }
    ctx.restore();
  }

  if (viewingLaneIndex === myLaneIndex && mlDragSelecting && mlDragSelectAnchor && mlDragSelectCurrent) {
    const minX = Math.min(mlDragSelectAnchor.gx, mlDragSelectCurrent.gx);
    const maxX = Math.max(mlDragSelectAnchor.gx, mlDragSelectCurrent.gx);
    const minY = Math.min(mlDragSelectAnchor.gy, mlDragSelectCurrent.gy);
    const maxY = Math.max(mlDragSelectAnchor.gy, mlDragSelectCurrent.gy);
    const rx = minX * tileSize + 1;
    const ry = minY * tileSize + 1;
    const rw = (maxX - minX + 1) * tileSize - 2;
    const rh = (maxY - minY + 1) * tileSize - 2;
    const col = mlDragSelectAnchor.kind === 'wall' ? 'rgba(64, 220, 168, 0.95)' : 'rgba(90, 170, 255, 0.95)';
    ctx.save();
    ctx.fillStyle = mlDragSelectAnchor.kind === 'wall' ? 'rgba(64, 220, 168, 0.10)' : 'rgba(90, 170, 255, 0.10)';
    ctx.strokeStyle = col;
    ctx.lineWidth = 1.6;
    ctx.setLineDash([4, 3]);
    ctx.fillRect(rx, ry, rw, rh);
    ctx.strokeRect(rx + 0.5, ry + 0.5, rw - 1, rh - 1);
    ctx.restore();
  }

  // Spawn tile - downward arrow icon (units enter here)
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
    const jitter = getMLUnitRenderJitter(u.id, tileSize);
    const drawX = pos.x + jitter.x;
    const drawY = pos.y + jitter.y;
    const myLane = local.lanes && local.lanes[myLaneIndex];
    const ownerLane = local.lanes && local.lanes[u.ownerLane];
    const friendly = !!myLane && !!ownerLane && myLane.team === ownerLane.team;
    drawUnitShape({ type: u.type, side: friendly ? 'bottom' : 'top' }, drawX, drawY);
    const hpRatio = Math.max(0, Math.min(1, u.hp / u.maxHp));
    ctx.fillStyle = '#252d38';
    ctx.fillRect(drawX - 9, drawY - 12, 18, 3);
    ctx.fillStyle = friendly ? '#28c0b0' : '#ff3a3a';
    ctx.fillRect(drawX - 9, drawY - 12, 18 * hpRatio, 3);
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

  // Tower range preview on hover (inside translated context)
  if (mlHoverTile && viewingLaneIndex === myLaneIndex) {
    const { gx: hx, gy: hy } = mlHoverTile;
    const towerCell = lane.towerCells && lane.towerCells.find(t => t.x === hx && t.y === hy);
    if (towerCell) {
      const m = towerMeta[towerCell.type] || DEFAULT_TOWER_META[towerCell.type];
      const range = m ? m.range : 3;
      const col = towerColor(towerCell.type);
      ctx.save();
      ctx.globalAlpha = 0.18;
      ctx.fillStyle = col;
      ctx.beginPath();
      ctx.arc((hx + 0.5) * tileSize, (hy + 0.5) * tileSize, range * tileSize, 0, Math.PI * 2);
      ctx.fill();
      ctx.globalAlpha = 0.5;
      ctx.strokeStyle = col;
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.restore();
    }
  }

  // Wall drag preview (pending placements before release)
  if (mlDragPlacedSet && mlDragPlacedSet.size > 0 && viewingLaneIndex === myLaneIndex) {
    ctx.save();
    ctx.globalAlpha = 0.42;
    ctx.fillStyle = 'rgba(40, 192, 176, 0.55)';
    ctx.strokeStyle = 'rgba(40, 192, 176, 0.9)';
    ctx.lineWidth = 1;
    for (const key of mlDragPlacedSet) {
      const parts = key.split(',');
      const gx = Number(parts[0]);
      const gy = Number(parts[1]);
      if (!Number.isInteger(gx) || !Number.isInteger(gy)) continue;
      const px = gx * tileSize + 1;
      const py = gy * tileSize + 1;
      const ps = tileSize - 2;
      ctx.fillRect(px, py, ps, ps);
      ctx.strokeRect(px + 0.5, py + 0.5, ps - 1, ps - 1);
    }
    ctx.restore();
  }

  ctx.restore(); // end translate(offsetX, offsetY)

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

  // Danger indicator — enemy units near castle
  const pathLen = lane.path ? lane.path.length : ML_GRID_H;
  const viewedLane = local.lanes && local.lanes[viewingLaneIndex];
  const dangerUnits = (lane.units || []).filter(u =>
    viewedLane && local.lanes && local.lanes[u.ownerLane]
      ? local.lanes[u.ownerLane].team !== viewedLane.team && u.pathIdx >= pathLen - 7
      : u.ownerLane !== viewingLaneIndex && u.pathIdx >= pathLen - 7
  );
  if (dangerUnits.length > 0) {
    const pulse = 0.3 + Math.sin(Date.now() * 0.006) * 0.2;
    ctx.strokeStyle = 'rgba(255,60,60,' + pulse.toFixed(2) + ')';
    ctx.lineWidth = 4;
    ctx.strokeRect(offsetX + 2, offsetY + 2, gridW - 4, gridH - 4);
    ctx.fillStyle = 'rgba(255,60,60,' + (pulse * 0.7).toFixed(2) + ')';
    ctx.font = 'bold 10px "Share Tech Mono", monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText('\u26A0 LEAK INCOMING', offsetX + gridW / 2, offsetY + gridH - 4);
  }

  // Debug tick counter (grid top-right)
  ctx.fillStyle = 'rgba(140,180,200,0.3)';
  ctx.font = '11px "Share Tech Mono", monospace';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'top';
  ctx.fillText('T:' + (mlCurrentState ? mlCurrentState.tick : 0), offsetX + gridW - 2, offsetY + 2);

  // Spawn queue label (grid top-left)
  if (lane.spawnQueueLength > 0) {
    ctx.textAlign = 'left';
    ctx.fillText('Q:' + lane.spawnQueueLength, offsetX + 4, offsetY + 20);
  }

  // Floating texts
  updateAndDrawFloatingTexts();
}

// ── ML floating text animations ───────────────────────────────────────────────

function spawnFloatingText(x, y, text, color) {
  floatingTexts.push({ x, y, text, color, alpha: 1, age: 0 });
}

function updateAndDrawFloatingTexts() {
  const toRemove = [];
  floatingTexts.forEach((ft, i) => {
    ft.age++;
    ft.y -= 0.75;
    ft.alpha = Math.max(0, 1 - ft.age / 55);
    if (ft.alpha <= 0) { toRemove.push(i); return; }
    ctx.save();
    ctx.globalAlpha = ft.alpha;
    ctx.fillStyle = ft.color;
    ctx.font = 'bold 14px "Share Tech Mono", monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText(ft.text, ft.x, ft.y);
    ctx.restore();
  });
  for (let i = toRemove.length - 1; i >= 0; i--) {
    floatingTexts.splice(toRemove[i], 1);
  }
}

// ── ML Info Bar update ────────────────────────────────────────────────────────

function updateMLInfoBar(myLane, state) {
  if (!myLane) return;

  // Lives — detect loss to spawn floating text
  const lives = Math.max(0, myLane.lives);
  if (mibLives) mibLives.textContent = String(lives);
  if (mlPrevLives !== -1 && lives < mlPrevLives) {
    const { offsetX, offsetY, tileSize } = getMLGridLayout();
    spawnFloatingText(
      offsetX + (ML_CASTLE_X + 0.5) * tileSize,
      offsetY + ML_CASTLE_YG * tileSize,
      '-' + (mlPrevLives - lives), '#ff5050'
    );
  }
  mlPrevLives = lives;

  // Gold with flash animation
  const goldVal = myLane.gold;
  if (goldVal !== _lastMibGold) {
    if (_lastMibGold !== -1 && mibGold) {
      mibGold.classList.remove('mib-gold-flash');
      void mibGold.offsetWidth;
      mibGold.classList.add('mib-gold-flash');
      mibGold.addEventListener('animationend', function onEnd() {
        mibGold.classList.remove('mib-gold-flash');
        mibGold.removeEventListener('animationend', onEnd);
      });
    }
    _lastMibGold = goldVal;
  }
  if (mibGold) mibGold.textContent = String(goldVal);

  // Income label
  if (mibIncome) mibIncome.textContent = '+' + myLane.income + 'g';

  // Timer ring — detect income payout (ticksRemaining resets from low to high)
  const curRemaining = state ? (Number.isFinite(state.incomeTicksRemaining) ? state.incomeTicksRemaining : 0) : 0;
  if (mlLastIncomeTicksRemaining !== -1 &&
      curRemaining > mlLastIncomeTicksRemaining + ML_INCOME_PERIOD / 2) {
    const { offsetX, offsetY, gridW } = getMLGridLayout();
    spawnFloatingText(offsetX + gridW / 2, offsetY + 30, '+' + myLane.income + 'g', '#28d060');
  }
  mlLastIncomeTicksRemaining = curRemaining;

  const ratio = Math.max(0, Math.min(1, curRemaining / ML_INCOME_PERIOD));
  const seconds = Math.max(0, Math.ceil(curRemaining / tickHz));
  if (mibTimer) mibTimer.textContent = seconds + 's';
  if (mtrFill) {
    const r = 10;
    const circumference = 2 * Math.PI * r;
    mtrFill.style.strokeDasharray = circumference.toFixed(2);
    mtrFill.style.strokeDashoffset = (circumference * (1 - ratio)).toFixed(2);
  }
}

// ── ML Lane Tabs ──────────────────────────────────────────────────────────────

function initMLLaneTabs() {
  if (!mlLaneTabs) return;
  mlLaneTabs.innerHTML = '';
  const myAssign = mlLaneAssignments.find(a => a.laneIndex === myLaneIndex);
  const myTeam = myAssign && myAssign.team ? myAssign.team : null;
  mlLaneAssignments.forEach(a => {
    const btn = document.createElement('button');
    const isMine = a.laneIndex === myLaneIndex;
    const isTeammate = !isMine && myTeam && a.team === myTeam;
    btn.className = 'lane-tab ' + (isMine || isTeammate ? 'lane-tab-mine' : 'lane-tab-enemy');
    btn.dataset.laneIndex = String(a.laneIndex);
    const star = isMine ? '\u2605 ' : '';
    btn.innerHTML = star + a.displayName + ' <span class="lane-tab-lives" id="ltab-lives-' + a.laneIndex + '">\u2665?</span>';
    btn.addEventListener('click', () => switchViewingLane(a.laneIndex));
    mlLaneTabs.appendChild(btn);
  });
}

function updateLaneTabs(state) {
  if (!mlLaneTabs) return;
  Array.from(mlLaneTabs.querySelectorAll('.lane-tab')).forEach(btn => {
    const li = parseInt(btn.dataset.laneIndex);
    btn.classList.toggle('lane-tab-active', li === viewingLaneIndex);
    const lane = state && state.lanes && state.lanes[li];
    const livesSpan = btn.querySelector('.lane-tab-lives');
    if (livesSpan && lane) {
      livesSpan.textContent = '\u2665' + Math.max(0, lane.lives);
    }
  });
}

function switchViewingLane(index) {
  viewingLaneIndex = index;
  const myAssign = mlLaneAssignments.find(a => a.laneIndex === myLaneIndex);
  const viewedAssign = mlLaneAssignments.find(a => a.laneIndex === index);
  const isEnemy = index !== myLaneIndex && (!myAssign || !viewedAssign || myAssign.team !== viewedAssign.team);
  if (enemyLaneTint) enemyLaneTint.style.display = isEnemy ? 'block' : 'none';
  if (mlSpectateNotice) {
    if (isEnemy) {
      const assign = mlLaneAssignments.find(a => a.laneIndex === index);
      const name = assign ? assign.displayName : ('Lane ' + index);
      if (mlSpectateName) mlSpectateName.textContent = name;
      mlSpectateNotice.style.display = 'block';
    } else {
      mlSpectateNotice.style.display = 'none';
    }
  }
  updateLaneNavLabel();
  closeMLTileMenu();
}

// ── ML Command Bar update ─────────────────────────────────────────────────────

function updateCmdBar(myLane) {
  if (!myLane) return;
  const gold = myLane.gold;
  const canAct = lobbyState === 'playing' && !isSpectator && viewingLaneIndex === myLaneIndex;

  // Wall button
  if (cmdWallBtn) {
    cmdWallBtn.disabled = !canAct;
    if (cmdWallCost) cmdWallCost.textContent = mlWallCost + 'g';
  }
  if (sideWallBtn) {
    sideWallBtn.disabled = !canAct || gold < mlWallCost;
    sideWallBtn.textContent = 'Wall\n'
      + 'Cost ' + mlWallCost + 'g\n'
      + 'Place on grid\n'
      + 'Remove for refund';
  }

  // Unit buttons
  // NOTE: disabled is only set based on canAct (not gold) so the .cub-auto badge
  // inside the button remains clickable — disabled buttons block child events in Chrome.
  cmdUnitBtns.forEach(btn => {
    const type = btn.getAttribute('data-unit-type');
    const m = unitMeta[type] || DEFAULT_UNIT_META[type];
    const unitCost = getScaledUnitCost(m.cost);
    btn.disabled = !canAct;
    btn.classList.toggle('cmd-unit-nogold', canAct && gold < unitCost);
    const costEl = btn.querySelector('.cub-cost');
    const incomeEl = btn.querySelector('.cub-income');
    if (costEl) costEl.textContent = unitCost + 'g';
    if (incomeEl) {
      const totalInc = getScaledUnitIncome(m.income);
      incomeEl.textContent = '+' + (Number.isInteger(totalInc) ? totalInc : totalInc.toFixed(1));
    }
    const badge = btn.querySelector('.cub-auto');
    if (badge) badge.classList.toggle('cub-auto-on', !!autosendEnabled[type]);
  });

  // Global auto button
  if (cmdGlobalAuto) {
    cmdGlobalAuto.classList.toggle('cmd-auto-on', mlGlobalAutoEnabled);
    cmdGlobalAuto.innerHTML = 'AUTO<br>' + (mlGlobalAutoEnabled ? 'ON' : 'OFF');
  }

  // Rate buttons
  cmdRateBtns.forEach(btn => {
    btn.classList.toggle('cmd-rate-on', btn.getAttribute('data-rate') === autosendRate);
  });
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
    const canAct = (lobbyState === 'playing' && !isSpectator);
    btn.disabled = !canAct;
    if (!m || !Number.isFinite(m.cost)) {
      btn.classList.add('locked');
      return;
    }
    btn.classList.toggle('locked', canAct && gold < getScaledUnitCost(m.cost));
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
  if (data && data.settings) applyMlSettingsToUi(data.settings);
  setMlSettingsEditable(mlIsHost && lobbyState !== 'playing');
  mlLobbyPlayers = data.players || [];
  if (mlTeamSetupStatus) {
    const redCount = mlLobbyPlayers.filter(p => (p.team || 'red') === 'red').length;
    const blueCount = mlLobbyPlayers.filter(p => p.team === 'blue').length;
    const total = mlLobbyPlayers.length;
    const isFourPlayerReady = total === 4 && redCount === 2 && blueCount === 2;
    mlTeamSetupStatus.textContent = `Team Setup: Red ${redCount} | Blue ${blueCount}`;
    mlTeamSetupStatus.classList.remove('ok', 'warn');
    if (total < 4) {
      mlTeamSetupStatus.classList.add('warn');
      mlTeamSetupStatus.textContent += ' (waiting for players)';
    } else if (isFourPlayerReady) {
      mlTeamSetupStatus.classList.add('ok');
      mlTeamSetupStatus.textContent += ' (ready)';
    } else {
      mlTeamSetupStatus.classList.add('warn');
      mlTeamSetupStatus.textContent += ' (need 2v2 split)';
    }
  }
  mlPlayerList.innerHTML = '';
  mlLobbyPlayers.forEach(p => {
    const li = document.createElement('li');

    const laneSpan = document.createElement('span');
    laneSpan.className = 'player-lane-num';
    laneSpan.textContent = 'L' + p.laneIndex;

    const teamBadge = document.createElement('span');
    teamBadge.className = 'ml-team-badge ml-team-' + (p.team === 'blue' ? 'blue' : 'red');
    teamBadge.textContent = p.team === 'blue' ? 'BLUE' : 'RED';

    const nameSpan = document.createElement('span');
    nameSpan.style.flex = '1';
    nameSpan.style.marginLeft = '4px';
    nameSpan.textContent = p.displayName;
    if (p.isAI) {
      const aiBadge = document.createElement('span');
      aiBadge.className = 'ai-badge';
      aiBadge.textContent = 'AI';
      nameSpan.appendChild(aiBadge);
    }

    const badge = document.createElement('span');
    badge.className = p.ready ? 'player-ready-badge' : 'player-waiting-badge';
    badge.textContent = p.ready ? 'READY' : 'WAITING';

    li.appendChild(laneSpan);
    li.appendChild(teamBadge);
    li.appendChild(nameSpan);
    li.appendChild(badge);

    // Host: reorder buttons (all slots) + remove button (AI only)
    if (mlIsHost) {
      const totalSlots = mlLobbyPlayers.length;

      const upBtn = document.createElement('button');
      upBtn.className = 'ml-swap-btn';
      upBtn.title = 'Move up';
      upBtn.textContent = '\u25B2';
      upBtn.disabled = p.laneIndex === 0;
      upBtn.addEventListener('click', () => {
        socket.emit('swap_ml_lanes', { laneA: p.laneIndex, laneB: p.laneIndex - 1 });
      });

      const downBtn = document.createElement('button');
      downBtn.className = 'ml-swap-btn';
      downBtn.title = 'Move down';
      downBtn.textContent = '\u25BC';
      downBtn.disabled = p.laneIndex === totalSlots - 1;
      downBtn.addEventListener('click', () => {
        socket.emit('swap_ml_lanes', { laneA: p.laneIndex, laneB: p.laneIndex + 1 });
      });

      li.appendChild(upBtn);
      li.appendChild(downBtn);

      const teamBtn = document.createElement('button');
      teamBtn.className = 'ml-team-toggle ml-team-' + (p.team === 'blue' ? 'blue' : 'red');
      teamBtn.title = 'Switch team';
      teamBtn.textContent = p.team === 'blue' ? 'Blue' : 'Red';
      teamBtn.addEventListener('click', () => {
        const nextTeam = p.team === 'blue' ? 'red' : 'blue';
        socket.emit('set_ml_team', { laneIndex: p.laneIndex, team: nextTeam });
      });
      li.appendChild(teamBtn);

      if (p.isAI) {
        const removeBtn = document.createElement('button');
        removeBtn.className = 'ml-remove-ai-btn';
        removeBtn.title = 'Remove AI';
        removeBtn.textContent = '\u2715';
        removeBtn.addEventListener('click', () => {
          socket.emit('remove_ai_from_ml_room', { laneIndex: p.laneIndex });
        });
        li.appendChild(removeBtn);
      }
    }

    mlPlayerList.appendChild(li);
  });
}

function renderFrameML() {
  if (!mlHasFirstSnapshot) return drawWaiting();
  const local = buildMLInterpolatedState(performance.now());
  if (!local) return drawWaiting();

  if (viewingLaneIndex === null) viewingLaneIndex = myLaneIndex;

  const viewingLane = local.lanes && local.lanes[viewingLaneIndex];
  if (viewingLane) drawMLGridLane(viewingLane, local);

  const myLane = local.lanes && local.lanes[myLaneIndex];
  if (myLane) {
    updateMLInfoBar(myLane, local);
    updateSendButtonsML(myLane);
    updateSendAutoProgressBars(myLane);
    updateCmdBar(myLane);
    updateLaneTabs(local);
    updateBarracksHud(myLane);
  }
  updateLeaderboards(null, local);
}

// ── ML socket listeners ───────────────────────────────────────────────────────

socket.on('ml_room_created', data => {
  gameMode = 'multilane';
  mlMyCode = data.code;
  myLaneIndex = data.laneIndex;
  mlIsHost = true;
  applyMlSettingsToUi(data.settings || readMlSettingsFromUi());
  setMlSettingsEditable(true);

  if (_soloMode) {
    _soloMode = false;
    setLobbyState('waiting');
    setStatus('Starting solo match\u2026', 'wait');
    socket.emit('add_ai_to_ml_room', { difficulty: _soloAiDiff });
    socket.emit('ml_force_start', { code: data.code });
    return;
  }

  setLobbyState('waiting');
  setStatus('Share code \u2014 waiting for players...', 'wait');
  lobbyMlSection.style.display = 'none';
  showMLLobbyPanel();
  if (mlRoomCodeRow) mlRoomCodeRow.style.display = 'block';
  if (mlRoomCodeDisplay) mlRoomCodeDisplay.textContent = data.code;
  if (btnForceStart) btnForceStart.style.display = '';
  if (mlAiControls) mlAiControls.style.display = '';
});

socket.on('ml_room_joined', data => {
  gameMode = 'multilane';
  mlMyCode = data.code;
  myLaneIndex = data.laneIndex;
  mlIsHost = false;
  setMlSettingsEditable(false);
  setLobbyState('waiting');
  setStatus('Joined! Click Ready when set.', 'ok');
  lobbyMlSection.style.display = 'none';
  showMLLobbyPanel();
  if (mlRoomCodeRow) mlRoomCodeRow.style.display = 'block';
  if (mlRoomCodeDisplay) mlRoomCodeDisplay.textContent = data.code;
  if (btnForceStart) btnForceStart.style.display = 'none';
  if (mlAiControls) mlAiControls.style.display = 'none';
});

socket.on('ml_lobby_update', data => {
  renderMLLobbyPanel(data);
});

socket.on('ml_lane_reassigned', data => {
  myLaneIndex = data.laneIndex;
});

socket.on('ml_match_ready', data => {
  mlPlayerCount = data.playerCount || 2;
  mlLaneAssignments = data.laneAssignments || [];
  mlCurrentState = null;
  mlPreviousState = null;
  mlHasFirstSnapshot = false;
  isSpectator = false;
  _lastHudGold = -1;
  if (spectatorBadge) spectatorBadge.style.display = 'none';
  setLobbyState('playing');
  hideLobby();
  showGameUi();
  initCanvas();
  hideGameOverBanner();
  startRenderLoop();
  hideMLLobbyPanel();
  if (mlAiControls) mlAiControls.style.display = 'none';

  // Reset new ML UI state
  mlGlobalAutoEnabled = false;
  floatingTexts = [];
  mlLastIncomeTicksRemaining = -1;
  mlPrevLives = -1;
  mlPrevBarracksLevel = 1;
  _lastMibGold = -1;
  mlDragPlacedSet.clear();
  mlDragPlacing = false;
  mlDragSelecting = false;
  mlDragSelectAnchor = null;
  mlDragSelectCurrent = null;

  // Initialize ML grid UI
  viewingLaneIndex = myLaneIndex;

  // Keep right-side tower list visible in ML as a reference panel
  const defBar = document.getElementById('defense-bar');
  if (defBar) defBar.style.display = 'flex';
  defenseButtons.forEach(btn => { btn.disabled = true; });
  towerSlots.forEach(slotEl => { slotEl.style.display = 'none'; });

  // Hide classic hud-bar — ml-info-bar replaces it
  if (hudBar) hudBar.style.display = 'none';

  // Show new ML UI elements
  if (mlInfoBar) mlInfoBar.style.display = 'flex';
  if (mlCmdBar) mlCmdBar.style.display = 'none';
  if (mlLaneTabs) { mlLaneTabs.style.display = 'none'; initMLLaneTabs(); }
  if (enemyLaneTint) enemyLaneTint.style.display = 'none';
  if (mlSpectateNotice) mlSpectateNotice.style.display = 'none';
  if (cmdWallCost) cmdWallCost.textContent = mlWallCost + 'g';

  // Side-rail controls for ML mode
  if (mlBarracksHud) mlBarracksHud.style.display = 'grid';
  if (autosendBar) autosendBar.style.display = 'none';
  if (sideWallBtn) sideWallBtn.style.display = 'block';
  if (mlMidNextBtn) mlMidNextBtn.style.display = 'none';
  if (mlLaneNav) mlLaneNav.style.display = (mlPlayerCount > 1 ? 'flex' : 'none');
  refreshAutosendControls();

  updateLaneNavLabel();
});

socket.on('ml_match_config', config => applyMatchConfig(config));

socket.on('ml_state_snapshot', state => {
  if (!state) return;
  mlHasFirstSnapshot = true;
  mlPreviousState = mlCurrentState || state;
  mlCurrentState = state;
  mlCurrentStateReceivedAt = performance.now();

  const lane = state.lanes && state.lanes[myLaneIndex];
  normalizeMLSelectionForLane(lane);
  if (mlActiveTile && mlTileMenu && mlTileMenu.style.display !== 'none' && lane) {
    if (mlSelectionKind === 'wall') {
      const isWall = lane.walls && lane.walls.some(w => w.x === mlActiveTile.gx && w.y === mlActiveTile.gy);
      if (!isWall) {
        closeMLTileMenu();
      } else {
        const selectedWalls = getSelectionWallTiles(lane, mlActiveTile.gx, mlActiveTile.gy);
        const selectedCount = selectedWalls.length;
        const selectedWallsEl = mlTileMenu.querySelector('[data-selected-walls]');
        if (selectedWallsEl) selectedWallsEl.textContent = `Selected walls: ${selectedCount}`;
        mlTileMenu.querySelectorAll('[data-tower]').forEach(btn => {
          const towerType = String(btn.getAttribute('data-tower') || '');
          const m = towerMeta[towerType] || DEFAULT_TOWER_META[towerType];
          const unitCost = m ? m.cost : 0;
          const totalCost = unitCost * selectedCount;
          btn.disabled = !m || lane.gold < totalCost;
          btn.textContent = selectedCount > 1
            ? `${cap(towerType)} x${selectedCount} (${totalCost}g)`
            : `${cap(towerType)} (${unitCost}g)`;
        });
      }
    } else {
      const tc = lane.towerCells && lane.towerCells.find(t =>
        t.x === mlActiveTile.gx
        && t.y === mlActiveTile.gy
        && (!mlSelectionTowerType || t.type === mlSelectionTowerType)
      );
      if (!tc) {
        closeMLTileMenu();
      } else {
        const mode = tc.targetMode || 'first';
        if (tc.level !== mlActiveTile.level || mode !== (mlActiveTile.targetMode || 'first')) {
          showMLTowerUpgradeMenu(mlActiveTile.gx, mlActiveTile.gy, tc.type, tc.level, mode);
        } else {
          const nextLevel = tc.level + 1;
          const upgradeBtn = mlTileMenu.querySelector('[data-upgrade]');
          if (upgradeBtn) {
            if (nextLevel > 10) {
              upgradeBtn.textContent = 'MAXED';
              upgradeBtn.disabled = true;
            } else {
              const cost = getTowerUpgradeCost(tc.type, nextLevel);
              upgradeBtn.textContent = `Upgrade -> Lv${nextLevel} (${cost}g)`;
              upgradeBtn.disabled = lane.gold < cost;
            }
          }

          const bulkBtn = mlTileMenu.querySelector('[data-bulk-upgrade]');
          if (bulkBtn) {
            const selection = getSelectionTowerContext(lane, tc.type, tc.level, mode);
            const selected = (selection && selection.selected) ? selection.selected : [tc];
            const affordableBulk = getAffordableTowerUpgrades(selected, tc.type, lane.gold);
            const label = affordableBulk.totalUpgradeable === 0
              ? 'All selected maxed'
              : (affordableBulk.affordable.length > 0
                ? `Upgrade selected (${affordableBulk.affordable.length}/${affordableBulk.totalUpgradeable}) (${affordableBulk.totalCost}g)`
                : 'Need more gold');
            bulkBtn.textContent = label;
            bulkBtn.disabled = affordableBulk.affordable.length === 0;
          }
        }
      }
    }
  }
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
  autosendToggles.forEach(btn => { btn.disabled = true; });
  if (autosendRateSelect) autosendRateSelect.disabled = true;
  if (sideWallBtn) sideWallBtn.disabled = true;
  refreshAutosendControls();
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

function mlTileKey(gx, gy) {
  return gx + ',' + gy;
}

function clearMLSelection() {
  mlSelectedTiles = [];
  mlSelectionKind = null;
  mlSelectionTowerType = null;
}

function hasMLSelectionTile(gx, gy) {
  const key = mlTileKey(gx, gy);
  return mlSelectedTiles.some(t => mlTileKey(t.gx, t.gy) === key);
}

function setMLSingleSelection(kind, gx, gy, towerType) {
  mlSelectionKind = kind;
  mlSelectionTowerType = kind === 'tower' ? towerType : null;
  mlSelectedTiles = [{ gx, gy }];
}

function toggleMLSelectionTile(gx, gy) {
  const key = mlTileKey(gx, gy);
  const idx = mlSelectedTiles.findIndex(t => mlTileKey(t.gx, t.gy) === key);
  if (idx >= 0) {
    if (mlSelectedTiles.length > 1) mlSelectedTiles.splice(idx, 1);
    return;
  }
  mlSelectedTiles.push({ gx, gy });
}

function getSelectionTowerContext(lane, fallbackType, fallbackLevel, fallbackMode) {
  if (!lane) return null;
  const sameType = (mlSelectionKind === 'tower' && mlSelectionTowerType === fallbackType);
  const selected = [];
  if (sameType) {
    for (const t of mlSelectedTiles) {
      const cell = lane.towerCells && lane.towerCells.find(tc => tc.x === t.gx && tc.y === t.gy && tc.type === fallbackType);
      if (cell) selected.push(cell);
    }
  }
  if (selected.length === 0) {
    setMLSingleSelection('tower', mlActiveTile?.gx ?? 0, mlActiveTile?.gy ?? 0, fallbackType);
    return {
      selected: [{ x: mlActiveTile?.gx ?? 0, y: mlActiveTile?.gy ?? 0, type: fallbackType, level: fallbackLevel || 1, targetMode: fallbackMode || 'first' }],
      active: { x: mlActiveTile?.gx ?? 0, y: mlActiveTile?.gy ?? 0, type: fallbackType, level: fallbackLevel || 1, targetMode: fallbackMode || 'first' },
      isMixedLevels: false,
    };
  }
  const active = selected.find(tc => tc.x === mlActiveTile?.gx && tc.y === mlActiveTile?.gy) || selected[0];
  const firstLevel = Number(selected[0].level) || 1;
  const isMixedLevels = selected.some(tc => (Number(tc.level) || 1) !== firstLevel);
  return { selected, active, isMixedLevels };
}

function getSelectionWallTiles(lane, fallbackGx, fallbackGy) {
  if (!lane) return [];
  const sameKind = mlSelectionKind === 'wall';
  const out = [];
  if (sameKind) {
    for (const t of mlSelectedTiles) {
      const isWall = lane.walls && lane.walls.some(w => w.x === t.gx && w.y === t.gy);
      if (isWall) out.push({ x: t.gx, y: t.gy });
    }
  }
  if (out.length === 0) {
    setMLSingleSelection('wall', fallbackGx, fallbackGy, null);
    return [{ x: fallbackGx, y: fallbackGy }];
  }
  return out;
}

function normalizeMLSelectionForLane(lane) {
  if (!lane || mlSelectedTiles.length === 0 || !mlSelectionKind) {
    if (!lane) clearMLSelection();
    return;
  }
  const valid = [];
  if (mlSelectionKind === 'wall') {
    for (const t of mlSelectedTiles) {
      const ok = lane.walls && lane.walls.some(w => w.x === t.gx && w.y === t.gy);
      if (ok) valid.push(t);
    }
  } else if (mlSelectionKind === 'tower') {
    for (const t of mlSelectedTiles) {
      const ok = lane.towerCells && lane.towerCells.some(tc => tc.x === t.gx && tc.y === t.gy && tc.type === mlSelectionTowerType);
      if (ok) valid.push(t);
    }
  }
  mlSelectedTiles = valid;
  if (mlSelectedTiles.length === 0) {
    clearMLSelection();
    return;
  }
  if (mlActiveTile && !hasMLSelectionTile(mlActiveTile.gx, mlActiveTile.gy)) {
    mlActiveTile.gx = mlSelectedTiles[0].gx;
    mlActiveTile.gy = mlSelectedTiles[0].gy;
  }
}

// ── ML grid interaction ───────────────────────────────────────────────────────

function applyMLDragSelection() {
  if (!mlCurrentState || !mlDragSelectAnchor || !mlDragSelectCurrent) return false;
  const lane = mlCurrentState.lanes && mlCurrentState.lanes[myLaneIndex];
  if (!lane) return false;

  const minX = Math.min(mlDragSelectAnchor.gx, mlDragSelectCurrent.gx);
  const maxX = Math.max(mlDragSelectAnchor.gx, mlDragSelectCurrent.gx);
  const minY = Math.min(mlDragSelectAnchor.gy, mlDragSelectCurrent.gy);
  const maxY = Math.max(mlDragSelectAnchor.gy, mlDragSelectCurrent.gy);
  const selected = [];

  for (let x = minX; x <= maxX; x++) {
    for (let y = minY; y <= maxY; y++) {
      if (mlDragSelectAnchor.kind === 'wall') {
        const isWall = lane.walls && lane.walls.some(w => w.x === x && w.y === y);
        if (isWall) selected.push({ gx: x, gy: y });
      } else if (mlDragSelectAnchor.kind === 'tower') {
        const tower = lane.towerCells && lane.towerCells.find(t =>
          t.x === x && t.y === y && t.type === mlDragSelectAnchor.towerType
        );
        if (tower) selected.push({ gx: x, gy: y });
      }
    }
  }

  if (selected.length === 0) return false;
  mlSelectedTiles = selected;
  mlSelectionKind = mlDragSelectAnchor.kind;
  mlSelectionTowerType = mlDragSelectAnchor.kind === 'tower' ? mlDragSelectAnchor.towerType : null;

  const anchorInSelection = selected.find(s => s.gx === mlDragSelectAnchor.gx && s.gy === mlDragSelectAnchor.gy);
  const active = anchorInSelection || selected[0];
  mlActiveTile = { gx: active.gx, gy: active.gy };

  if (mlSelectionKind === 'tower') {
    const tower = lane.towerCells && lane.towerCells.find(t => t.x === active.gx && t.y === active.gy);
    if (tower) {
      showMLTowerUpgradeMenu(active.gx, active.gy, tower.type, tower.level, tower.targetMode);
      return true;
    }
  } else if (mlSelectionKind === 'wall') {
    showMLWallConvertMenu(active.gx, active.gy);
    return true;
  }
  return false;
}
function handleMLCanvasClick(clientX, clientY) {
  if (gameMode !== 'multilane') return;
  if (viewingLaneIndex !== myLaneIndex || isSpectator) return;
  if (!mlCurrentState) return;
  if (gameOverBanner.style.display !== 'none') return;

  const rect = canvas.getBoundingClientRect();
  const cx = (clientX - rect.left) * (canvas.width / rect.width);
  const cy = (clientY - rect.top) * (canvas.height / rect.height);
  const { tileSize, offsetX, offsetY } = getMLGridLayout();
  const gx = Math.floor((cx - offsetX) / tileSize);
  const gy = Math.floor((cy - offsetY) / tileSize);

  if (gx < 0 || gx >= ML_GRID_W || gy < 0 || gy >= ML_GRID_H) return;

  const lane = mlCurrentState.lanes[myLaneIndex];
  if (!lane) return;

  const isWall = lane.walls && lane.walls.some(w => w.x === gx && w.y === gy);
  const towerCell = lane.towerCells && lane.towerCells.find(t => t.x === gx && t.y === gy);

  if (!towerCell && !isWall) {
    clearMLSelection();
    closeMLTileMenu();
    sendAction('place_wall', { gridX: gx, gridY: gy });
    return;
  }

  if (towerCell) {
    const sameSelection = mlSelectionKind === 'tower' && mlSelectionTowerType === towerCell.type;
    if (!sameSelection) {
      setMLSingleSelection('tower', gx, gy, towerCell.type);
    } else {
      toggleMLSelectionTile(gx, gy);
    }
    mlActiveTile = { gx, gy, level: Number(towerCell.level) || 1, targetMode: towerCell.targetMode || 'first' };
    showMLTowerUpgradeMenu(gx, gy, towerCell.type, towerCell.level, towerCell.targetMode);
    return;
  }

  const sameSelection = mlSelectionKind === 'wall';
  if (!sameSelection) {
    setMLSingleSelection('wall', gx, gy, null);
  } else {
    toggleMLSelectionTile(gx, gy);
  }
  mlActiveTile = { gx, gy };
  showMLWallConvertMenu(gx, gy);
}
function positionMLTileMenu(gx, gy) {
  if (!mlTileMenu) return;
  const { tileSize, offsetX, offsetY } = getMLGridLayout();
  const scaleX = canvas.getBoundingClientRect().width / canvas.width;
  const scaleY = canvas.getBoundingClientRect().height / canvas.height;
  const canvasRect = canvas.getBoundingClientRect();
  const uiRect = gameUi.getBoundingClientRect();

  // Center of the clicked tile in CSS pixels relative to game-ui
  const tileCenterX = canvasRect.left - uiRect.left + (offsetX + (gx + 0.5) * tileSize) * scaleX;
  const tileCenterY = canvasRect.top - uiRect.top + (offsetY + (gy + 0.5) * tileSize) * scaleY;

  const menuW = 170;
  const menuH = 225;
  const tileHalf = (tileSize * scaleX) * 0.5;
  let mx = tileCenterX + tileHalf + 8; // prefer right of tile so we do not cover selection
  let my = tileCenterY - menuH / 2;

  if (mx + menuW > uiRect.width - 4) {
    mx = tileCenterX - tileHalf - menuW - 8; // fallback to left of tile
  }
  if (mx < 4) {
    mx = tileCenterX - menuW / 2; // fallback above/below
    my = tileCenterY - menuH - 8;
    if (my < 4) my = tileCenterY + tileSize * scaleY + 4;
  }

  // Clamp to UI bounds
  mx = Math.max(4, Math.min(uiRect.width - menuW - 4, mx));
  my = Math.max(4, Math.min(uiRect.height - menuH - 4, my));

  mlTileMenu.style.left = mx + 'px';
  mlTileMenu.style.top = my + 'px';
}

function showMLWallConvertMenu(gx, gy) {
  if (!mlTileMenu) return;
  const lane = mlCurrentState && mlCurrentState.lanes[myLaneIndex];
  if (!lane) return;
  normalizeMLSelectionForLane(lane);
  mlActiveTile = { gx, gy };

  const gold = lane.gold;
  const selectedWalls = getSelectionWallTiles(lane, gx, gy);
  const selectedCount = selectedWalls.length;

  let html = '<div class="ml-menu-title">Wall -> Tower</div>';
  if (selectedCount > 1) {
    html += `<div class="ml-menu-stat" data-selected-walls>Selected walls: ${selectedCount}</div>`;
  }
  const towerTypes = ['archer', 'fighter', 'mage', 'ballista', 'cannon'];
  for (const t of towerTypes) {
    const m = towerMeta[t] || DEFAULT_TOWER_META[t];
    const unitCost = m ? m.cost : 0;
    const totalCost = unitCost * selectedCount;
    const disabled = (!m || gold < totalCost) ? ' disabled' : '';
    const label = selectedCount > 1 ? `${cap(t)} x${selectedCount} (${totalCost}g)` : `${cap(t)} (${unitCost}g)`;
    html += `<button class="ml-tile-btn" data-gx="${gx}" data-gy="${gy}" data-tower="${t}"${disabled}>${label}</button>`;
  }
  html += `<button class="ml-tile-btn danger" data-gx="${gx}" data-gy="${gy}" data-remove-wall>Remove Wall (+${mlWallCost}g)</button>`;
  html += '<button class="ml-tile-btn secondary" data-close>Cancel</button>';
  mlTileMenu.innerHTML = html;

  positionMLTileMenu(gx, gy);
  mlTileMenu.style.display = 'block';
  mlTileMenuJustOpened = true;
  requestAnimationFrame(() => { mlTileMenuJustOpened = false; });

  mlTileMenu.querySelectorAll('[data-tower]').forEach(btn => {
    btn.addEventListener('click', () => {
      const towerType = btn.getAttribute('data-tower');
      if (!towerType) return;
      btn.disabled = true;
      btn.textContent = 'Applying...';
      if (selectedCount > 1) {
        sendAction('bulk_convert_walls', {
          towerType,
          tiles: selectedWalls.map(t => ({ gridX: t.x, gridY: t.y })),
        });
      } else {
        sendAction('convert_wall', { gridX: gx, gridY: gy, towerType });
      }
      closeMLTileMenu();
    });
  });
  const removeWallBtn = mlTileMenu.querySelector('[data-remove-wall]');
  if (removeWallBtn) {
    removeWallBtn.addEventListener('click', () => {
      const bx = Number(removeWallBtn.getAttribute('data-gx'));
      const by = Number(removeWallBtn.getAttribute('data-gy'));
      sendAction('remove_wall', { gridX: bx, gridY: by });
      closeMLTileMenu();
    });
  }
  const closeBtn = mlTileMenu.querySelector('[data-close]');
  if (closeBtn) closeBtn.addEventListener('click', closeMLTileMenu);
}

function showMLTowerUpgradeMenu(gx, gy, towerType, level, targetMode) {
  if (!mlTileMenu) return;
  const lane = mlCurrentState && mlCurrentState.lanes[myLaneIndex];
  if (!lane) return;
  normalizeMLSelectionForLane(lane);

  const lvl = level || 1;
  const mode = (targetMode === 'last' || targetMode === 'weakest' || targetMode === 'strongest' || targetMode === 'first') ? targetMode : 'first';
  mlActiveTile = { gx, gy, level: lvl, targetMode: mode };

  const selection = getSelectionTowerContext(lane, towerType, lvl, mode);
  if (!selection) return;
  const selectedTowers = selection.selected;
  const activeTower = selection.active;
  const selectedCount = selectedTowers.length;

  const activeLevel = Number(activeTower.level) || lvl;
  const activeMode = activeTower.targetMode || mode;
  const gold = lane.gold;

  const nextLevel = activeLevel + 1;
  const canUpgrade = nextLevel <= 10;
  const cost = canUpgrade ? getTowerUpgradeCost(towerType, nextLevel) : 0;
  const canAfford = canUpgrade && gold >= cost;
  const stats = getTowerStatsAtLevel(towerType, activeLevel);

  const affordableBulk = getAffordableTowerUpgrades(selectedTowers, towerType, gold);
  const canBulkUpgrade = affordableBulk.affordable.length > 0;

  let html = `<div class="ml-menu-title">${cap(towerType)} Lv${activeLevel}</div>`;
  if (selectedCount > 1) {
    html += `<div class="ml-menu-stat">Selected: ${selectedCount}</div>`;
    if (selection.isMixedLevels) html += '<div class="ml-menu-stat">Mixed levels</div>';
  }
  html += `<div class="ml-menu-stat">DMG ${stats.dmg.toFixed(1)} | RNG ${stats.range.toFixed(1)} tiles</div>`;
  html += `<div class="ml-menu-stat">${stats.damageType}</div>`;
  html += `<div class="ml-menu-stat">Target: ${cap(activeMode)}</div>`;
  html += `<div class="ml-target-row">`
    + `<button class="ml-tile-btn secondary${activeMode === 'first' ? ' active' : ''}" data-gx="${activeTower.x}" data-gy="${activeTower.y}" data-target-mode="first">First</button>`
    + `<button class="ml-tile-btn secondary${activeMode === 'last' ? ' active' : ''}" data-gx="${activeTower.x}" data-gy="${activeTower.y}" data-target-mode="last">Last</button>`
    + `<button class="ml-tile-btn secondary${activeMode === 'weakest' ? ' active' : ''}" data-gx="${activeTower.x}" data-gy="${activeTower.y}" data-target-mode="weakest">Weakest</button>`
    + `<button class="ml-tile-btn secondary${activeMode === 'strongest' ? ' active' : ''}" data-gx="${activeTower.x}" data-gy="${activeTower.y}" data-target-mode="strongest">Strongest</button>`
    + `</div>`;

  html += `<button class="ml-tile-btn" data-gx="${activeTower.x}" data-gy="${activeTower.y}" data-upgrade${canAfford ? '' : ' disabled'}>${canUpgrade ? `Upgrade -> Lv${nextLevel} (${cost}g)` : 'MAXED'}</button>`;
  if (selectedCount > 1) {
    const label = affordableBulk.totalUpgradeable === 0
      ? 'All selected maxed'
      : (affordableBulk.affordable.length > 0
        ? `Upgrade selected (${affordableBulk.affordable.length}/${affordableBulk.totalUpgradeable}) (${affordableBulk.totalCost}g)`
        : 'Need more gold');
    html += `<button class="ml-tile-btn" data-bulk-upgrade${canBulkUpgrade ? '' : ' disabled'}>${label}</button>`;
  }

  const sellValue = getTowerSellValue(towerType, activeLevel);
  html += `<button class="ml-tile-btn danger" data-gx="${activeTower.x}" data-gy="${activeTower.y}" data-sell>Sell (${sellValue}g)</button>`;
  html += '<button class="ml-tile-btn secondary" data-close>Close</button>';
  mlTileMenu.innerHTML = html;

  positionMLTileMenu(activeTower.x, activeTower.y);
  mlTileMenu.style.display = 'block';
  mlTileMenuJustOpened = true;
  requestAnimationFrame(() => { mlTileMenuJustOpened = false; });

  const upgradeBtn = mlTileMenu.querySelector('[data-upgrade]');
  if (upgradeBtn) {
    upgradeBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      const bx = Number(upgradeBtn.getAttribute('data-gx'));
      const by = Number(upgradeBtn.getAttribute('data-gy'));
      sendAction('upgrade_tower', { gridX: bx, gridY: by });
      upgradeBtn.disabled = true;
      upgradeBtn.textContent = 'Upgrading...';
      showMLTowerUpgradeMenu(bx, by, towerType, activeLevel + 1, activeMode);
    });
  }

  const bulkBtn = mlTileMenu.querySelector('[data-bulk-upgrade]');
  if (bulkBtn) {
    bulkBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      bulkBtn.disabled = true;
      bulkBtn.textContent = 'Upgrading...';
      sendAction('bulk_upgrade_towers', {
        tiles: selectedTowers.map(t => ({ gridX: t.x, gridY: t.y })),
      });
    });
  }

  mlTileMenu.querySelectorAll('[data-target-mode]').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const bx = Number(btn.getAttribute('data-gx'));
      const by = Number(btn.getAttribute('data-gy'));
      const nextMode = String(btn.getAttribute('data-target-mode') || 'first');
      sendAction('set_tower_target', { gridX: bx, gridY: by, targetMode: nextMode });
      showMLTowerUpgradeMenu(bx, by, towerType, activeLevel, nextMode);
    });
  });

  const sellBtn = mlTileMenu.querySelector('[data-sell]');
  if (sellBtn) {
    sellBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      const bx = Number(sellBtn.getAttribute('data-gx'));
      const by = Number(sellBtn.getAttribute('data-gy'));
      sendAction('sell_tower', { gridX: bx, gridY: by });
      closeMLTileMenu();
    });
  }

  const closeBtn = mlTileMenu.querySelector('[data-close]');
  if (closeBtn) closeBtn.addEventListener('click', closeMLTileMenu);
}

function closeMLTileMenu(clearSelection = true) {
  if (mlTileMenu) mlTileMenu.style.display = 'none';
  mlActiveTile = null;
  if (clearSelection) clearMLSelection();
}
function updateBarracksHud(lane) {
  if (!lane) return;
  const level = lane.barracksLevel || 1;
  if (mlBarracksLevel) mlBarracksLevel.textContent = String(level);
  if (mibBarracksLv) mibBarracksLv.textContent = String(level);

  // Detect barracks level increase → screen flash + feedback
  if (mlPrevBarracksLevel !== -1 && level > mlPrevBarracksLevel) {
    canvasWrap.classList.remove('barracks-upgraded');
    void canvasWrap.offsetWidth;
    canvasWrap.classList.add('barracks-upgraded');
    canvasWrap.addEventListener('animationend', function onEnd() {
      canvasWrap.classList.remove('barracks-upgraded');
      canvasWrap.removeEventListener('animationend', onEnd);
    });
    showActionFeedback('Barracks upgraded to Lv' + level + '!', true);
    const { offsetX, offsetY, tileSize, gridW } = getMLGridLayout();
    spawnFloatingText(offsetX + gridW / 2, offsetY + 60, 'Barracks Lv' + level, '#e8a828');
  }
  mlPrevBarracksLevel = level;

  // Refresh send-button income labels if barracks bonus changed
  const curDef = getMLBarracksLevelDef(level);
  const bonus = curDef ? (curDef.incomeBonus || 0) : 0;
  const bonusChanged = bonus !== mlMyBarracksIncomeBonus;
  if (bonusChanged) {
    mlMyBarracksIncomeBonus = bonus;
  }
  const statsChanged = syncBarracksStatMultipliers(level);
  if (bonusChanged || statsChanged) {
    updateActionLabels();
    updateSendButtonsML(lane);
  }

  const nextDef = getMLBarracksLevelDef(level + 1);
  if (btnBarracksUpgrade) {
    btnBarracksUpgrade.innerHTML = renderBarracksUpgradeLabel(level, nextDef);
    btnBarracksUpgrade.disabled = !nextDef || lane.gold < nextDef.cost || lane.income < nextDef.reqIncome;
  }
  refreshBarracksAutoControl();
  maybeAutoUpgradeBarracks(lane.gold, lane.income, level);

  // Update mib barracks button
  if (mibBarracksBtn) {
    const cost = nextDef ? nextDef.cost : '?';
    mibBarracksBtn.innerHTML = '&#x1F3F0; Lv' + level + '\u2192' + (level + 1) + ' (' + cost + 'g)';
    mibBarracksBtn.disabled = !nextDef || lane.gold < nextDef.cost || lane.income < nextDef.reqIncome;
  }
}

function refreshBarracksAutoControl() {
  if (!btnBarracksAuto) return;
  const canToggle = lobbyState === 'playing' && (gameMode !== 'multilane' || !isSpectator);
  btnBarracksAuto.disabled = !canToggle;
  btnBarracksAuto.classList.toggle('barracks-auto-on', autoBarracksEnabled);
  btnBarracksAuto.textContent = autoBarracksEnabled ? 'AUTO ON' : 'AUTO';
}

function maybeAutoUpgradeBarracks(gold, income, level) {
  if (!autoBarracksEnabled) return;
  const canAct = lobbyState === 'playing' && (gameMode !== 'multilane' || !isSpectator);
  if (!canAct) return;
  const now = Date.now();
  if ((now - autoBarracksLastAttemptMs) < 600) return;
  const nextDef = getMLBarracksLevelDef((Number(level) || 1) + 1);
  if (!nextDef) return;
  if (gold < nextDef.cost || income < nextDef.reqIncome) return;
  autoBarracksLastAttemptMs = now;
  sendAction('upgrade_barracks', {});
}

function updateLaneNavLabel() {
  const showNav = (gameMode === 'multilane' && mlPlayerCount > 1);
  if (btnPrevLane) btnPrevLane.style.display = showNav ? 'flex' : 'none';
  if (btnNextLane) btnNextLane.style.display = showNav ? 'flex' : 'none';
  if (!mlViewingLabel) return;
  if (mlMidNextBtn) mlMidNextBtn.style.display = 'none';
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
  if (mlWasDrag) { mlWasDrag = false; return; }
  handleMLCanvasClick(e.clientX, e.clientY);
});

canvas.addEventListener('touchend', function (e) {
  if (gameMode !== 'multilane') return;
  e.preventDefault();
  const didCommit = commitDragPreviewWalls();
  mlDragPlacing = false;
  mlDragSelecting = false;
  mlDragSelectAnchor = null;
  mlDragSelectCurrent = null;
  if (didCommit) {
    mlWasDrag = true;
    return;
  }
  if (!didCommit) {
    const t = e.changedTouches[0];
    if (t) handleMLCanvasClick(t.clientX, t.clientY);
  }
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

if (mlMidNextBtn) {
  mlMidNextBtn.addEventListener('click', () => {
    if (gameMode !== 'multilane' || mlPlayerCount <= 1) return;
    viewingLaneIndex = ((viewingLaneIndex || 0) + 1) % mlPlayerCount;
    updateLaneNavLabel();
    closeMLTileMenu();
  });
}

// ── ML Command Bar event listeners ────────────────────────────────────────────

// Global AUTO toggle
if (cmdGlobalAuto) {
  cmdGlobalAuto.addEventListener('click', () => {
    if (lobbyState !== 'playing' || gameMode !== 'multilane' || isSpectator) return;
    mlGlobalAutoEnabled = !mlGlobalAutoEnabled;
    UNIT_TYPES.forEach(t => { autosendEnabled[t] = mlGlobalAutoEnabled; });
    refreshAutosendControls();
    syncAutosend();
  });
}

// Per-unit auto badge (A button inside each unit button)
cmdAutoIndicators.forEach(badge => {
  badge.addEventListener('click', (e) => {
    e.stopPropagation(); // don't trigger parent unit button
    if (lobbyState !== 'playing' || gameMode !== 'multilane' || isSpectator) return;
    const type = badge.getAttribute('data-unit-type');
    autosendEnabled[type] = !autosendEnabled[type];
    refreshAutosendControls();
    mlGlobalAutoEnabled = UNIT_TYPES.every(t => autosendEnabled[t]);
    syncAutosend();
  });
});

// Unit buttons — spawn on click (skip if auto badge was clicked, skip if not enough gold)
cmdUnitBtns.forEach(btn => {
  btn.addEventListener('click', (e) => {
    if (e.target.classList.contains('cub-auto')) return;
    if (btn.classList.contains('cmd-unit-nogold')) return;
    trySpawn(btn.getAttribute('data-unit-type'));
  });
});

// Rate buttons
cmdRateBtns.forEach(btn => {
  btn.addEventListener('click', () => {
    autosendRate = btn.getAttribute('data-rate');
    if (autosendRateSelect) autosendRateSelect.value = autosendRate;
    if (lobbyState === 'playing' && gameMode === 'multilane' && !isSpectator) syncAutosend();
  });
});

// Mib barracks upgrade button
if (mibBarracksBtn) {
  mibBarracksBtn.addEventListener('click', () => {
    sendAction('upgrade_barracks', {});
  });
}

if (btnBarracksAuto) {
  btnBarracksAuto.addEventListener('click', () => {
    if (lobbyState !== 'playing' || (gameMode === 'multilane' && isSpectator)) return;
    autoBarracksEnabled = !autoBarracksEnabled;
    autoBarracksLastAttemptMs = 0;
    refreshBarracksAutoControl();
  });
}

// Wall button — show placement hint
if (cmdWallBtn) {
  cmdWallBtn.addEventListener('click', () => {
    if (gameMode !== 'multilane' || viewingLaneIndex !== myLaneIndex || isSpectator) return;
    showActionFeedback('Click an empty grid tile to place a wall (' + mlWallCost + 'g)', true);
  });
}

if (sideWallBtn) {
  sideWallBtn.addEventListener('click', () => {
    if (gameMode !== 'multilane' || viewingLaneIndex !== myLaneIndex || isSpectator) return;
    showActionFeedback('Click an empty grid tile to place a wall (' + mlWallCost + 'g)', true);
  });
}

// ── Drag-to-place walls ───────────────────────────────────────────────────────

canvas.addEventListener('mousedown', (e) => {
  if (gameMode !== 'multilane') return;
  if (viewingLaneIndex !== myLaneIndex || isSpectator) return;
  const tile = getMLGridTileFromClient(e.clientX, e.clientY);
  if (!tile || !mlCurrentState) return;
  const lane = mlCurrentState.lanes && mlCurrentState.lanes[myLaneIndex];
  if (!lane) return;

  const towerCell = lane.towerCells && lane.towerCells.find(t => t.x === tile.gx && t.y === tile.gy);
  const isWall = lane.walls && lane.walls.some(w => w.x === tile.gx && w.y === tile.gy);
  if (towerCell || isWall) {
    mlDragSelecting = true;
    mlDragSelectAnchor = {
      gx: tile.gx,
      gy: tile.gy,
      kind: towerCell ? 'tower' : 'wall',
      towerType: towerCell ? towerCell.type : null,
    };
    mlDragSelectCurrent = { gx: tile.gx, gy: tile.gy };
    mlDragPlacing = false;
    mlDragPlacedSet.clear();
    mlDragAnchorTile = null;
    mlDragAxis = null;
    mlWasDrag = false;
    return;
  }

  mlDragPlacing = true;
  mlDragPlacedSet = new Set();
  mlDragAnchorTile = null;
  mlDragAxis = null;
  mlDragSelecting = false;
  mlDragSelectAnchor = null;
  mlDragSelectCurrent = null;
  mlWasDrag = false;
});

canvas.addEventListener('mousemove', (e) => {
  if (gameMode !== 'multilane') return;
  const tile = getMLGridTileFromClient(e.clientX, e.clientY);
  mlHoverTile = tile ? { gx: tile.gx, gy: tile.gy } : null;

  if (mlDragSelecting) {
    if (tile) mlDragSelectCurrent = { gx: tile.gx, gy: tile.gy };
    return;
  }
  if (!mlDragPlacing || !tile) return;
  updateStraightWallDragPreview(tile.gx, tile.gy);
});

canvas.addEventListener('mouseup', () => {
  if (gameMode === 'multilane') {
    if (mlDragSelecting) {
      const didSelect = applyMLDragSelection();
      if (didSelect) mlWasDrag = true;
    } else {
      const didCommit = commitDragPreviewWalls();
      if (didCommit) mlWasDrag = true;
    }
  }
  mlDragPlacing = false;
  mlDragSelecting = false;
  mlDragSelectAnchor = null;
  mlDragSelectCurrent = null;
  mlDragAnchorTile = null;
  mlDragAxis = null;
});

canvas.addEventListener('mouseleave', () => {
  mlHoverTile = null;
  mlDragPlacing = false;
  mlDragSelecting = false;
  mlDragSelectAnchor = null;
  mlDragSelectCurrent = null;
  mlDragPlacedSet.clear();
  mlDragAnchorTile = null;
  mlDragAxis = null;
});

// Touch drag-to-place
canvas.addEventListener('touchstart', () => {
  if (gameMode !== 'multilane') return;
  mlDragPlacing = true;
  mlDragPlacedSet = new Set();
  mlDragSelecting = false;
  mlDragSelectAnchor = null;
  mlDragSelectCurrent = null;
  mlDragAnchorTile = null;
  mlDragAxis = null;
  mlWasDrag = false;
}, { passive: true });

canvas.addEventListener('touchmove', (e) => {
  if (gameMode !== 'multilane' || !mlDragPlacing) return;
  e.preventDefault();
  const touch = e.touches[0];
  if (!touch) return;
  const rect = canvas.getBoundingClientRect();
  const cx = (touch.clientX - rect.left) * (canvas.width / rect.width);
  const cy = (touch.clientY - rect.top) * (canvas.height / rect.height);
  const { tileSize, offsetX, offsetY } = getMLGridLayout();
  const gx = Math.floor((cx - offsetX) / tileSize);
  const gy = Math.floor((cy - offsetY) / tileSize);
  if (gx < 0 || gx >= ML_GRID_W || gy < 0 || gy >= ML_GRID_H) return;
  updateStraightWallDragPreview(gx, gy);
}, { passive: false });

// ── Auth bar ──────────────────────────────────────────────────────────────────

const authSigninArea  = document.getElementById('auth-signin-area');
const authProfileArea = document.getElementById('auth-profile-area');
const authDisplayName = document.getElementById('auth-display-name');
const btnAuthSignout  = document.getElementById('btn-auth-signout');
const btnAuth2fa      = document.getElementById('btn-auth-2fa');

function onAuthStateChange(player) {
  if (player) {
    authSigninArea.style.display  = 'none';
    authProfileArea.style.display = 'flex';
    authDisplayName.textContent   = player.displayName;
    // Reconnect socket so the server picks up the new HttpOnly auth cookie
    if (socket.connected) socket.disconnect();
    socket.connect();
    // Show friends panel when signed in; party panel stays hidden during gameplay.
    document.getElementById('party-panel').style.display = lobbyState === 'playing' ? 'none' : '';
    document.getElementById('friends-panel').style.display = '';
    syncFriendsPanelPlacement();
    btnAuthSignout.textContent = lobbyState === 'playing' ? 'Quit Game' : 'Sign out';
    renderPartyPanel(null);
    renderFriendsPanel([]);
    // Pre-fill and hide name inputs — username is used automatically
    [joinNameInput, mlNameInput, soloNameInput].forEach(inp => {
      if (!inp) return;
      inp.value = player.displayName;
      inp.style.display = 'none';
      const label = document.querySelector(`label[for="${inp.id}"]`);
      if (label) label.style.display = 'none';
    });
  } else {
    authSigninArea.style.display  = 'flex';
    authProfileArea.style.display = 'none';
    document.getElementById('party-panel').style.display = 'none';
    document.getElementById('friends-panel').style.display = 'none';
    syncFriendsPanelPlacement();
    _friendsList = [];
    _pendingPartyInvite = null;
    _resetPwForm();
    // Restore name inputs for guests
    [joinNameInput, mlNameInput, soloNameInput].forEach(inp => {
      if (!inp) return;
      inp.value = '';
      inp.style.display = '';
      const label = document.querySelector(`label[for="${inp.id}"]`);
      if (label) label.style.display = '';
    });
  }
  syncAuthBarPlacement();
}

btnAuthSignout.addEventListener('click', () => {
  if (lobbyState === 'playing') {
    leaveCurrentGame();
    return;
  }
  Auth.logout();
});

// ── Password auth form logic ──────────────────────────────────────────────────

const tabLogin      = document.getElementById('tab-login');
const tabRegister   = document.getElementById('tab-register');
const authEmail     = document.getElementById('auth-email');
const authDname     = document.getElementById('auth-displayname');
const authPassword  = document.getElementById('auth-password');
const authMfaCode   = document.getElementById('auth-mfa-code');
const authPwError   = document.getElementById('auth-pw-error');
const btnPwSubmit   = document.getElementById('btn-auth-pw-submit');

let _pwMode    = 'login'; // 'login' | 'register'
let _mfaToken  = null;    // set when server returns requiresMfa

function _resetPwForm() {
  _pwMode = 'login';
  _mfaToken = null;
  tabLogin.classList.add('active');
  tabRegister.classList.remove('active');
  authDname.style.display    = 'none';
  authMfaCode.style.display  = 'none';
  authEmail.style.display    = '';
  authPassword.style.display = '';
  btnPwSubmit.textContent    = 'Sign In';
  authPwError.style.display  = 'none';
  authPwError.textContent    = '';
  authEmail.value = authDname.value = authPassword.value = authMfaCode.value = '';
}

function _showPwError(msg) {
  authPwError.textContent   = msg;
  authPwError.style.display = '';
}

tabLogin.addEventListener('click', () => {
  if (_pwMode === 'login') return;
  _pwMode = 'login';
  tabLogin.classList.add('active');
  tabRegister.classList.remove('active');
  authDname.style.display = 'none';
  btnPwSubmit.textContent = 'Sign In';
  authPwError.style.display = 'none';
});

tabRegister.addEventListener('click', () => {
  if (_pwMode === 'register') return;
  _pwMode = 'register';
  tabRegister.classList.add('active');
  tabLogin.classList.remove('active');
  authDname.style.display = '';
  btnPwSubmit.textContent = 'Register';
  authPwError.style.display = 'none';
});

btnPwSubmit.addEventListener('click', async () => {
  authPwError.style.display = 'none';
  btnPwSubmit.disabled = true;
  try {
    // MFA step
    if (_mfaToken) {
      const code = authMfaCode.value.trim();
      if (!code) { _showPwError('Enter the 6-digit code'); return; }
      await Auth.loginWithMfa(_mfaToken, code);
      return;
    }
    if (_pwMode === 'login') {
      const result = await Auth.loginWithPassword(authEmail.value.trim(), authPassword.value);
      if (result && result.requiresMfa) {
        // Switch form to MFA code entry
        _mfaToken = result.mfaToken;
        authEmail.style.display    = 'none';
        authPassword.style.display = 'none';
        authMfaCode.style.display  = '';
        btnPwSubmit.textContent    = 'Verify';
        authMfaCode.focus();
      }
    } else {
      const result = await Auth.register(authEmail.value.trim(), authDname.value.trim(), authPassword.value);
      if (result && result.requiresVerification) {
        // Close auth panel and show verify modal
        document.getElementById('auth-pw-panel').style.display = 'none';
        authEmail.value    = '';
        authDname.value    = '';
        authPassword.value = '';
        _showVerifyModal(result.email);
      }
    }
  } catch (err) {
    if (err.message === 'email_not_verified') {
      _showPwError('Please verify your email before signing in. Check your inbox or use Resend below.');
      _showVerifyModal(authEmail.value.trim());
    } else {
      _showPwError(err.message);
    }
  } finally {
    btnPwSubmit.disabled = false;
  }
});

// Allow Enter key to submit
[authEmail, authDname, authPassword, authMfaCode].forEach(el => {
  el.addEventListener('keydown', e => { if (e.key === 'Enter') btnPwSubmit.click(); });
});

// ── 2FA settings modal ────────────────────────────────────────────────────────

const tfaModal          = document.getElementById('tfa-modal');
const tfaStateDisabled  = document.getElementById('tfa-state-disabled');
const tfaStateSetup     = document.getElementById('tfa-state-setup');
const tfaStateEnabled   = document.getElementById('tfa-state-enabled');
const tfaQrImg          = document.getElementById('tfa-qr-img');
const tfaSetupCode      = document.getElementById('tfa-setup-code');
const tfaSetupError     = document.getElementById('tfa-setup-error');
const tfaDisableCode    = document.getElementById('tfa-disable-code');
const tfaDisableError   = document.getElementById('tfa-disable-error');

function _showTfaState(state) {
  tfaStateDisabled.style.display = state === 'disabled' ? '' : 'none';
  tfaStateSetup.style.display    = state === 'setup'    ? '' : 'none';
  tfaStateEnabled.style.display  = state === 'enabled'  ? '' : 'none';
}

btnAuth2fa.addEventListener('click', async () => {
  tfaModal.style.display = '';
  tfaSetupError.style.display   = 'none';
  tfaDisableError.style.display = 'none';
  tfaSetupCode.value = '';
  tfaDisableCode.value = '';
  // Determine current 2FA state via a quick /players/me check could be added,
  // but for simplicity show disabled state by default; user can see enabled state after enabling.
  // A real implementation would fetch /players/me to check totp_enabled.
  _showTfaState('disabled');
});

document.getElementById('tfa-close-btn').addEventListener('click', () => {
  tfaModal.style.display = 'none';
});

document.getElementById('btn-tfa-start-setup').addEventListener('click', async () => {
  try {
    const data = await Auth.setup2fa();
    tfaQrImg.src = data.qrCodeDataUrl;
    tfaSetupCode.value = '';
    tfaSetupError.style.display = 'none';
    _showTfaState('setup');
  } catch (err) {
    alert('Error: ' + err.message);
  }
});

document.getElementById('btn-tfa-cancel-setup').addEventListener('click', () => {
  _showTfaState('disabled');
});

document.getElementById('btn-tfa-confirm-enable').addEventListener('click', async () => {
  tfaSetupError.style.display = 'none';
  const btn = document.getElementById('btn-tfa-confirm-enable');
  btn.disabled = true;
  try {
    await Auth.enable2fa(tfaSetupCode.value.trim());
    _showTfaState('enabled');
  } catch (err) {
    tfaSetupError.textContent   = err.message;
    tfaSetupError.style.display = '';
  } finally {
    btn.disabled = false;
  }
});

document.getElementById('btn-tfa-confirm-disable').addEventListener('click', async () => {
  tfaDisableError.style.display = 'none';
  const btn = document.getElementById('btn-tfa-confirm-disable');
  btn.disabled = true;
  try {
    await Auth.disable2fa(tfaDisableCode.value.trim());
    _showTfaState('disabled');
  } catch (err) {
    tfaDisableError.textContent   = err.message;
    tfaDisableError.style.display = '';
  } finally {
    btn.disabled = false;
  }
});

// ── Forgot password ───────────────────────────────────────────────────────────

const forgotForm      = document.getElementById('auth-forgot-form');
const authPwForm      = document.getElementById('auth-pw-form');
const btnForgotPw     = document.getElementById('btn-forgot-pw');
const btnForgotBack   = document.getElementById('btn-forgot-back');
const forgotEmail     = document.getElementById('auth-forgot-email');
const forgotError     = document.getElementById('auth-forgot-error');
const forgotSuccess   = document.getElementById('auth-forgot-success');
const btnForgotSubmit = document.getElementById('btn-forgot-submit');

btnForgotPw.addEventListener('click', () => {
  authPwForm.style.display  = 'none';
  forgotForm.style.display  = '';
  forgotError.style.display   = 'none';
  forgotSuccess.style.display = 'none';
  forgotEmail.value = '';
});

btnForgotBack.addEventListener('click', () => {
  forgotForm.style.display = 'none';
  authPwForm.style.display = '';
});

btnForgotSubmit.addEventListener('click', async () => {
  forgotError.style.display   = 'none';
  forgotSuccess.style.display = 'none';
  const email = forgotEmail.value.trim();
  if (!email) { forgotError.textContent = 'Enter your email'; forgotError.style.display = ''; return; }
  btnForgotSubmit.disabled = true;
  try {
    const res  = await fetch('/auth/forgot-password', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ email }),
    });
    if (!res.ok) {
      const d = await res.json();
      forgotError.textContent   = d.error || 'Request failed';
      forgotError.style.display = '';
    } else {
      forgotSuccess.style.display = '';
      btnForgotSubmit.disabled    = true; // prevent re-send
    }
  } catch {
    forgotError.textContent   = 'Network error — please try again';
    forgotError.style.display = '';
    btnForgotSubmit.disabled  = false;
  }
});

forgotEmail.addEventListener('keydown', e => { if (e.key === 'Enter') btnForgotSubmit.click(); });

// ── Email verification modal ──────────────────────────────────────────────────

let _pendingVerifyEmail = '';

const verifyModal        = document.getElementById('verify-modal');
const verifyEmailDisplay = document.getElementById('verify-email-display');
const verifyError        = document.getElementById('verify-error');
const verifySuccess      = document.getElementById('verify-success');

function _showVerifyModal(email) {
  _pendingVerifyEmail = email || '';
  verifyEmailDisplay.textContent = _pendingVerifyEmail;
  verifyError.style.display   = 'none';
  verifySuccess.style.display = 'none';
  verifyModal.style.display   = 'flex';
}

document.getElementById('btn-verify-resend').addEventListener('click', async () => {
  verifyError.style.display   = 'none';
  verifySuccess.style.display = 'none';
  try {
    await Auth.resendVerification(_pendingVerifyEmail);
    verifySuccess.style.display = '';
  } catch {
    verifyError.textContent   = 'Failed to resend — please try again';
    verifyError.style.display = '';
  }
});

document.getElementById('btn-verify-close').addEventListener('click', () => {
  verifyModal.style.display = 'none';
});

// Handle ?verify=<token> in URL (user clicked email link)
(function checkVerifyToken() {
  const params = new URLSearchParams(window.location.search);
  const token  = params.get('verify');
  if (!token) return;
  history.replaceState(null, '', window.location.pathname);
  Auth.verifyEmail(token).catch(err => {
    // Show error in verify modal with a message
    verifyEmailDisplay.textContent = '';
    document.getElementById('verify-msg').textContent = 'Verification failed: ' + (err.message || 'Invalid or expired link');
    verifyError.style.display = 'none';
    verifyModal.style.display = 'flex';
  });
})();

// ── Reset password modal (handles ?reset=<token> in URL) ─────────────────────

const resetModal    = document.getElementById('reset-modal');
const resetPassword = document.getElementById('reset-password');
const resetPassword2= document.getElementById('reset-password2');
const resetError    = document.getElementById('reset-error');
const resetSuccess  = document.getElementById('reset-success');
const btnResetSubmit= document.getElementById('btn-reset-submit');

(function checkResetToken() {
  const params = new URLSearchParams(window.location.search);
  const token  = params.get('reset');
  if (!token) return;
  // Remove token from URL bar without reloading
  history.replaceState(null, '', window.location.pathname);
  resetModal.style.display = '';
  resetModal._token = token;
})();

btnResetSubmit.addEventListener('click', async () => {
  resetError.style.display   = 'none';
  resetSuccess.style.display = 'none';
  const pw  = resetPassword.value;
  const pw2 = resetPassword2.value;
  if (pw.length < 8) {
    resetError.textContent   = 'Password must be at least 8 characters';
    resetError.style.display = '';
    return;
  }
  if (pw !== pw2) {
    resetError.textContent   = 'Passwords do not match';
    resetError.style.display = '';
    return;
  }
  btnResetSubmit.disabled = true;
  try {
    const res = await fetch('/auth/reset-password', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ token: resetModal._token, password: pw }),
    });
    const data = await res.json();
    if (!res.ok) {
      resetError.textContent   = data.error || 'Reset failed';
      resetError.style.display = '';
      btnResetSubmit.disabled  = false;
    } else {
      resetSuccess.style.display = '';
      resetPassword.style.display  = 'none';
      resetPassword2.style.display = 'none';
      btnResetSubmit.style.display = 'none';
      // Close modal after a short delay
      setTimeout(() => { resetModal.style.display = 'none'; }, 2500);
    }
  } catch {
    resetError.textContent   = 'Network error — please try again';
    resetError.style.display = '';
    btnResetSubmit.disabled  = false;
  }
});

// Global callback invoked by Google GSI after credential selection
window.onGoogleSignIn = async (response) => {
  try {
    await Auth.handleGoogleCredential(response);
  } catch (err) {
    console.error('[auth]', err.message);
  }
};

// Boot auth: fetch public config, then init
(async () => {
  try {
    const cfgRes = await fetch('/config');
    const cfg    = await cfgRes.json();
    if (cfg.googleClientId) {
      document.getElementById('g_id_onload')
        .setAttribute('data-client_id', cfg.googleClientId);
      // Prompt GSI to render the button now that client_id is set
      if (window.google?.accounts?.id) {
        google.accounts.id.initialize({
          client_id: cfg.googleClientId,
          callback:  window.onGoogleSignIn,
        });
        google.accounts.id.renderButton(
          document.getElementById('gsi-button-host'),
          { type: 'standard', theme: 'filled_black', size: 'small', text: 'sign_in_with', shape: 'pill', logo_alignment: 'left' }
        );
      }
      document.getElementById('auth-google-wrap').style.display = '';
    }
    // Show auth bar when DB is present (password auth) or Google is configured
    if (cfg.passwordAuthEnabled || cfg.googleClientId) {
      document.getElementById('auth-bar').style.display = 'block';
      syncAuthBarPlacement();
    }
  } catch { /* no DB / no Google config — auth bar stays hidden */ }

  await Auth.init(onAuthStateChange);
})();

// ── Party panel ───────────────────────────────────────────────────────────────

let _currentParty   = null; // local party state
let _queueStatus    = 'idle'; // 'idle' | 'queued' | 'matched'
let _queueMode      = null;
let _queueElapsed   = 0;
let _queueSize      = 0;
let _queueInterval  = null; // local elapsed timer

// ── Friends panel state ────────────────────────────────────────────────────────
let _friendsList       = [];
let _pendingPartyInvite = null;
let _friendsMsgTimer   = null;

const partyPanel        = document.getElementById('party-panel');
const friendsPanel      = document.getElementById('friends-panel');
const friendsPanelHomeParent = friendsPanel ? friendsPanel.parentElement : null;
const friendsPanelHomeNextSibling = friendsPanel ? friendsPanel.nextSibling : null;
const partyCodeDisplay  = document.getElementById('party-code-display');
const partyMemberList   = document.getElementById('party-member-list');
const partyActions      = document.getElementById('party-actions');
const btnPartyCreate    = document.getElementById('btn-party-create');
const btnPartyShowJoin  = document.getElementById('btn-party-show-join');
const partyJoinRow      = document.getElementById('party-join-row');
const partyJoinInput    = document.getElementById('party-join-input');
const btnPartyJoin      = document.getElementById('btn-party-join');
const btnPartyLeave     = document.getElementById('btn-party-leave');
const queueArea         = document.getElementById('queue-area');
const queueStatusText   = document.getElementById('queue-status-text');
const btnQueueRanked    = document.getElementById('btn-queue-enter-ranked');
const btnQueueCasual    = document.getElementById('btn-queue-enter-casual');
const btnQueueLeave     = document.getElementById('btn-queue-leave');

function renderPartyPanel(party) {
  _currentParty = party || null;
  const inParty = !!_currentParty;
  const myPlayerId = Auth.getPlayer()?.id;

  // Member list
  partyMemberList.innerHTML = '';
  if (inParty) {
    for (const m of _currentParty.members) {
      const li = document.createElement('li');
      if (m.playerId === _currentParty.leaderId) li.classList.add('leader');
      li.textContent = m.displayName || 'Player';
      partyMemberList.appendChild(li);
    }
    partyCodeDisplay.textContent = 'Code: ' + _currentParty.code;
  } else {
    partyCodeDisplay.textContent = '';
  }

  // Buttons
  btnPartyCreate.style.display   = inParty ? 'none' : '';
  btnPartyShowJoin.style.display = inParty ? 'none' : '';
  partyJoinRow.style.display     = 'none'; // always reset; show-join toggles it
  btnPartyLeave.style.display    = inParty ? '' : 'none';

  // Queue area: always show when signed in (solo players can queue without a party)
  queueArea.style.display = '';
  const isLeader = inParty ? (_currentParty.leaderId === myPlayerId) : true;
  const isQueued = _queueStatus === 'queued';
  btnQueueRanked.style.display = (!isQueued && isLeader) ? '' : 'none';
  btnQueueCasual.style.display = (!isQueued && isLeader) ? '' : 'none';
  btnQueueLeave.style.display  = (isQueued && isLeader) ? '' : 'none';

  if (_queueStatus === 'idle') {
    queueStatusText.textContent = (inParty && !isLeader) ? 'Waiting for leader' : 'Ready to queue';
    queueStatusText.className   = '';
  } else if (_queueStatus === 'queued') {
    const modeLabel = _queueMode === 'ranked_2v2' ? 'Ranked 2v2' : 'Casual 2v2';
    const sizeStr   = _queueSize > 0 ? ` · ${_queueSize} in queue` : '';
    queueStatusText.textContent = `Searching ${modeLabel}… ${_queueElapsed}s${sizeStr}`;
    queueStatusText.className   = 'queued';
  } else if (_queueStatus === 'matched') {
    queueStatusText.textContent = 'Match found!';
    queueStatusText.className   = 'matched';
  }
  // Keep friends panel content in sync with party state changes.
  renderFriendsPanel(_friendsList);
}

// ── Friends panel rendering ────────────────────────────────────────────────────

function _showFriendsMsg(msg, durationMs = 4000) {
  const el = document.getElementById('friends-msg');
  if (!el) return;
  el.textContent = msg;
  el.style.display = '';
  if (_friendsMsgTimer) clearTimeout(_friendsMsgTimer);
  _friendsMsgTimer = setTimeout(() => {
    el.style.display = 'none';
    _friendsMsgTimer = null;
  }, durationMs);
}

function renderFriendsPanel(friends) {
  _friendsList = friends || [];
  const panel = document.getElementById('friends-panel');
  if (!panel || panel.style.display === 'none') return;

  const myPlayerId = Auth.getPlayer()?.id;
  const myParty    = _currentParty;
  const amLeader   = myParty && myParty.leaderId === myPlayerId;

  // Invite banner
  const banner     = document.getElementById('friends-invite-banner');
  const inviteText = document.getElementById('friends-invite-text');
  if (_pendingPartyInvite) {
    inviteText.textContent = `${_pendingPartyInvite.fromDisplayName} invited you to their party!`;
    banner.style.display = '';
  } else {
    banner.style.display = 'none';
  }

  // Online count
  const onlineCount = document.getElementById('friends-online-count');
  const acceptedOnline = _friendsList.filter(f => f.status === 'accepted' && f.online).length;
  const acceptedTotal  = _friendsList.filter(f => f.status === 'accepted').length;
  onlineCount.textContent = acceptedTotal > 0 ? `${acceptedOnline}/${acceptedTotal} online` : '';

  // Sort: online accepted → offline accepted → pending_received → pending_sent
  const ORDER = { accepted_online: 0, accepted_offline: 1, pending_received: 2, pending_sent: 3 };
  const sorted = [..._friendsList].sort((a, b) => {
    const keyA = a.status === 'accepted' ? (a.online ? 'accepted_online' : 'accepted_offline') : a.status;
    const keyB = b.status === 'accepted' ? (b.online ? 'accepted_online' : 'accepted_offline') : b.status;
    return (ORDER[keyA] ?? 9) - (ORDER[keyB] ?? 9);
  });

  const list = document.getElementById('friends-list');
  list.innerHTML = '';
  for (const f of sorted) {
    const li = document.createElement('li');
    li.className = 'friends-item';

    const dot = document.createElement('span');
    dot.className = 'friends-dot ' + (f.online ? 'online' : 'offline');
    li.appendChild(dot);

    const name = document.createElement('span');
    name.className = 'friends-name';
    name.textContent = f.displayName;
    li.appendChild(name);

    const status = document.createElement('span');
    status.className = 'friends-status';
    if (f.status === 'pending_received') status.textContent = 'Pending';
    else if (f.status === 'pending_sent') status.textContent = 'Sent';
    li.appendChild(status);

    const actions = document.createElement('span');
    actions.className = 'friends-actions';

    if (f.status === 'accepted') {
      if (amLeader && f.online) {
        const invBtn = document.createElement('button');
        invBtn.className = 'party-btn small';
        invBtn.textContent = 'Invite';
        invBtn.addEventListener('click', () => socket.emit('party:invite', { targetPlayerId: f.playerId }));
        actions.appendChild(invBtn);
      }
      const rmBtn = document.createElement('button');
      rmBtn.className = 'party-btn small danger';
      rmBtn.textContent = 'Remove';
      rmBtn.addEventListener('click', () => socket.emit('friend:remove', { playerId: f.playerId }));
      actions.appendChild(rmBtn);
    } else if (f.status === 'pending_received') {
      const accBtn = document.createElement('button');
      accBtn.className = 'party-btn small primary';
      accBtn.textContent = 'Accept';
      accBtn.addEventListener('click', () => socket.emit('friend:accept', { playerId: f.playerId }));
      actions.appendChild(accBtn);
      const decBtn = document.createElement('button');
      decBtn.className = 'party-btn small danger';
      decBtn.textContent = 'Decline';
      decBtn.addEventListener('click', () => socket.emit('friend:decline', { playerId: f.playerId }));
      actions.appendChild(decBtn);
    } else if (f.status === 'pending_sent') {
      const canBtn = document.createElement('button');
      canBtn.className = 'party-btn small';
      canBtn.textContent = 'Cancel';
      canBtn.addEventListener('click', () => socket.emit('friend:decline', { playerId: f.playerId }));
      actions.appendChild(canBtn);
    }

    li.appendChild(actions);
    list.appendChild(li);
  }

  // Keep friends panel stacked directly below party panel.
  _repositionFriendsPanel();
}

function syncFriendsPanelPlacement() {
  if (!friendsPanel) return;
  const shouldDockInRail = lobbyState === 'playing' && !!rightRail;
  if (shouldDockInRail) {
    if (friendsPanel.parentElement !== rightRail) {
      const anchor = (authBar && authBar.parentElement === rightRail) ? authBar : null;
      if (anchor && anchor.nextSibling) rightRail.insertBefore(friendsPanel, anchor.nextSibling);
      else if (anchor) rightRail.appendChild(friendsPanel);
      else rightRail.appendChild(friendsPanel);
    }
    friendsPanel.classList.add('friends-in-rail');
    friendsPanel.style.top = '';
    return;
  }

  friendsPanel.classList.remove('friends-in-rail');
  if (!friendsPanelHomeParent || friendsPanel.parentElement === friendsPanelHomeParent) return;
  if (friendsPanelHomeNextSibling && friendsPanelHomeNextSibling.parentNode === friendsPanelHomeParent) {
    friendsPanelHomeParent.insertBefore(friendsPanel, friendsPanelHomeNextSibling);
  } else {
    friendsPanelHomeParent.appendChild(friendsPanel);
  }
  _repositionFriendsPanel();
}

function _repositionFriendsPanel() {
  const pp = document.getElementById('party-panel');
  const fp = document.getElementById('friends-panel');
  if (!pp || !fp || fp.style.display === 'none') return;
  if (fp.parentElement === rightRail || lobbyState === 'playing') {
    fp.style.top = '';
    return;
  }
  const rect = pp.getBoundingClientRect();
  fp.style.top = (rect.bottom + 6) + 'px';
}

function _startElapsedTimer() {
  if (_queueInterval) return;
  _queueInterval = setInterval(() => {
    if (_queueStatus !== 'queued') { _stopElapsedTimer(); return; }
    _queueElapsed++;
    renderPartyPanel(_currentParty);
  }, 1000);
}

function _stopElapsedTimer() {
  clearInterval(_queueInterval);
  _queueInterval = null;
}

btnPartyCreate.addEventListener('click', () => {
  socket.emit('party:create');
});

btnPartyShowJoin.addEventListener('click', () => {
  btnPartyShowJoin.style.display = 'none';
  partyJoinRow.style.display = '';
  partyJoinInput.focus();
});

btnPartyJoin.addEventListener('click', () => {
  const code = partyJoinInput.value.trim().toUpperCase();
  if (code.length !== 6) return;
  socket.emit('party:join', { code });
  partyJoinInput.value = '';
  partyJoinRow.style.display = 'none';
});

partyJoinInput.addEventListener('keydown', e => {
  if (e.key === 'Enter') btnPartyJoin.click();
});

btnPartyLeave.addEventListener('click', () => {
  socket.emit('party:leave');
});

btnQueueRanked.addEventListener('click', () => {
  socket.emit('queue:enter', { mode: 'ranked_2v2' });
});

btnQueueCasual.addEventListener('click', () => {
  socket.emit('queue:enter', { mode: 'casual_2v2' });
});

btnQueueLeave.addEventListener('click', () => {
  socket.emit('queue:leave');
});

// ── Party / queue socket listeners ────────────────────────────────────────────

socket.on('party_update', ({ party }) => {
  _currentParty = party;
  if (_queueStatus === 'queued' && party.status !== 'queued') {
    _queueStatus = 'idle';
    _stopElapsedTimer();
  }
  renderPartyPanel(party);
});

socket.on('queue_status', ({ status, mode, elapsed, queueSize }) => {
  if (status === 'queued') {
    if (_queueStatus !== 'queued') {
      _queueStatus = 'queued';
      _queueMode   = mode || _queueMode;
      _queueElapsed = typeof elapsed === 'number' ? elapsed : 0;
      _startElapsedTimer();
    } else {
      // Sync elapsed from server heartbeat
      if (typeof elapsed === 'number') _queueElapsed = elapsed;
    }
    if (typeof queueSize === 'number') _queueSize = queueSize;
  } else {
    _queueStatus = 'idle';
    _queueSize   = 0;
    _stopElapsedTimer();
  }
  renderPartyPanel(_currentParty);
});

socket.on('match_found', ({ roomCode, laneIndex, teammates, opponents }) => {
  _queueStatus = 'matched';
  _stopElapsedTimer();
  renderPartyPanel(_currentParty);

  // Auto-join the ML room
  const displayName = Auth.getPlayer()?.displayName || 'Player';
  console.log(`[party] match found! roomCode=${roomCode} laneIndex=${laneIndex}`);

  // Simulate clicking into the ML room as if we called join_ml_room
  gameMode   = 'multilane';
  mlMyCode   = roomCode;
  myLaneIndex = laneIndex;
  mlIsHost   = (laneIndex === 0);

  setMlSettingsEditable(false);
  setLobbyState('waiting');
  setStatus('Match found! Joining…', 'ok');

  // Hide all sections so the ML lobby panel shows correctly
  activateGameType('td');
  activateLobbyTab('ranked');
  showMLLobbyPanel();

  if (mlRoomCodeRow) { mlRoomCodeRow.style.display = 'block'; }
  if (mlRoomCodeDisplay) { mlRoomCodeDisplay.textContent = roomCode; }
  if (btnForceStart) btnForceStart.style.display = mlIsHost ? '' : 'none';
  if (mlAiControls)  mlAiControls.style.display  = mlIsHost ? '' : 'none';

  // Notify teammates/opponents in status
  const tmStr  = teammates.join(', ');
  const oppStr = opponents.join(', ');
  setStatus(`Match found! Team: ${tmStr} | vs: ${oppStr}`, 'ok');
});

// ── Friends panel events & socket listeners ────────────────────────────────────

window.addEventListener('resize', () => _repositionFriendsPanel());
if (window.visualViewport) {
  window.visualViewport.addEventListener('resize', () => _repositionFriendsPanel());
}

document.getElementById('btn-friends-add').addEventListener('click', () => {
  const input = document.getElementById('friends-add-input');
  const name = (input.value || '').trim();
  if (!name) return;
  socket.emit('friend:add', { displayName: name });
  input.value = '';
});

document.getElementById('friends-add-input').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') document.getElementById('btn-friends-add').click();
});

document.getElementById('btn-invite-accept').addEventListener('click', () => {
  if (!_pendingPartyInvite) return;
  socket.emit('party:join', { code: _pendingPartyInvite.partyCode });
  _pendingPartyInvite = null;
  renderFriendsPanel(_friendsList);
});

document.getElementById('btn-invite-dismiss').addEventListener('click', () => {
  _pendingPartyInvite = null;
  renderFriendsPanel(_friendsList);
});

socket.on('friends_list', (friends) => {
  _friendsList = friends || [];
  renderFriendsPanel(_friendsList);
});

socket.on('friend_request', ({ playerId, displayName }) => {
  _friendsList = _friendsList.filter(f => f.playerId !== playerId);
  _friendsList.push({ playerId, displayName, status: 'pending_received', online: true });
  renderFriendsPanel(_friendsList);
  _showFriendsMsg(`${displayName} sent you a friend request.`);
});

socket.on('friend_accepted', ({ playerId, displayName }) => {
  const idx = _friendsList.findIndex(f => f.playerId === playerId);
  if (idx !== -1) _friendsList[idx].status = 'accepted';
  renderFriendsPanel(_friendsList);
  _showFriendsMsg(`${displayName} accepted your friend request!`);
});

socket.on('friend_online', ({ playerId, displayName }) => {
  const idx = _friendsList.findIndex(f => f.playerId === playerId);
  if (idx !== -1) _friendsList[idx].online = true;
  renderFriendsPanel(_friendsList);
});

socket.on('friend_offline', ({ playerId }) => {
  const idx = _friendsList.findIndex(f => f.playerId === playerId);
  if (idx !== -1) _friendsList[idx].online = false;
  renderFriendsPanel(_friendsList);
});

socket.on('friend_removed', ({ playerId }) => {
  _friendsList = _friendsList.filter(f => f.playerId !== playerId);
  renderFriendsPanel(_friendsList);
});

socket.on('friend_error', ({ message }) => {
  _showFriendsMsg(message);
});

socket.on('party_invite', (invite) => {
  _pendingPartyInvite = invite;
  renderFriendsPanel(_friendsList);
});

// ── Leaderboard ───────────────────────────────────────────────────────────────

(function () {
  const overlay   = document.getElementById('lb-overlay');
  const openBtn   = document.getElementById('btn-open-leaderboard');
  const closeBtn  = document.getElementById('lb-close-btn');
  const modeEl    = document.getElementById('lb-mode-select');
  const regionEl  = document.getElementById('lb-region-select');
  const seasonBar = document.getElementById('lb-season-bar');
  const tbody     = document.getElementById('lb-tbody');
  const emptyEl   = document.getElementById('lb-empty');
  const loadingEl = document.getElementById('lb-loading');
  const prevBtn   = document.getElementById('lb-prev-btn');
  const nextBtn   = document.getElementById('lb-next-btn');
  const pageInfo  = document.getElementById('lb-page-info');

  if (!overlay || !openBtn) return;

  let currentPage = 1;
  let totalEntries = 0;
  const PAGE_SIZE = 50;
  const MEDALS = ['🥇', '🥈', '🥉'];

  function myPlayerId() {
    if (typeof Auth !== 'undefined' && Auth.isSignedIn()) {
      const p = Auth.getPlayer();
      return p ? p.id : null;
    }
    return null;
  }

  async function fetchLeaderboard(page) {
    loadingEl.style.display = 'block';
    emptyEl.style.display   = 'none';
    tbody.innerHTML = '';
    prevBtn.disabled = true;
    nextBtn.disabled = true;

    const mode   = modeEl.value;
    const region = regionEl.value;
    const url    = `/leaderboard?mode=${encodeURIComponent(mode)}&region=${encodeURIComponent(region)}&page=${page}`;

    try {
      const res  = await fetch(url);
      const data = await res.json();

      loadingEl.style.display = 'none';

      // Season banner
      if (data.season) {
        const sd = new Date(data.season.start_date).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
        seasonBar.textContent = `Season: ${data.season.name}  ·  Started ${sd}`;
      } else {
        seasonBar.textContent = 'Off-season';
      }

      totalEntries = data.total || 0;
      currentPage  = data.page  || 1;
      const myId   = myPlayerId();

      if (!data.entries || data.entries.length === 0) {
        emptyEl.style.display = 'block';
      } else {
        const frag = document.createDocumentFragment();
        for (const entry of data.entries) {
          const tr = document.createElement('tr');
          if (myId && entry.id === myId) tr.classList.add('lb-me');

          const rankNum = (currentPage - 1) * PAGE_SIZE + entry.rank;
          const rankDisp = rankNum <= 3
            ? `<span class="lb-rank-medal">${MEDALS[rankNum - 1]}</span>`
            : `${rankNum}`;

          tr.innerHTML = `
            <td class="lb-col-rank">${rankDisp}</td>
            <td class="lb-col-name">${escapeHtml(entry.display_name)}</td>
            <td class="lb-col-region">${escapeHtml(entry.region || '')}</td>
            <td class="lb-col-rating">${Math.round(entry.rating)}</td>
            <td class="lb-col-record">${entry.wins}W / ${entry.losses}L</td>
          `;
          frag.appendChild(tr);
        }
        tbody.appendChild(frag);
      }

      // Pagination
      const totalPages = Math.ceil(totalEntries / PAGE_SIZE) || 1;
      pageInfo.textContent = `Page ${currentPage} of ${totalPages}`;
      prevBtn.disabled = currentPage <= 1;
      nextBtn.disabled = currentPage >= totalPages;
    } catch (err) {
      loadingEl.style.display = 'none';
      emptyEl.style.display   = 'block';
      emptyEl.textContent     = 'Failed to load leaderboard.';
      console.error('[leaderboard]', err);
    }
  }

  function escapeHtml(s) {
    return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
  }

  function open() {
    overlay.style.display = 'flex';
    fetchLeaderboard(1);
  }

  function close() {
    overlay.style.display = 'none';
  }

  openBtn.addEventListener('click', open);
  closeBtn.addEventListener('click', close);
  overlay.addEventListener('click', e => { if (e.target === overlay) close(); });
  modeEl.addEventListener('change',   () => fetchLeaderboard(1));
  regionEl.addEventListener('change', () => fetchLeaderboard(1));
  prevBtn.addEventListener('click', () => { if (currentPage > 1) fetchLeaderboard(currentPage - 1); });
  nextBtn.addEventListener('click', () => {
    const totalPages = Math.ceil(totalEntries / PAGE_SIZE) || 1;
    if (currentPage < totalPages) fetchLeaderboard(currentPage + 1);
  });
})();
