"use strict";

const { performance } = require("node:perf_hooks");

const { canUseInLoadout } = require("../unitUsage");
const {
  getAvailableRaces,
  getDefaultRaceId,
  isValidRaceId,
} = require("./raceProgressionCatalog");
const { getLockedWavePlan } = require("../waveDefenseSpec");

async function loadDefaultWaveConfig(db) {
  const fallbackPlan = getLockedWavePlan();
  if (!db) return fallbackPlan;
  try {
    const cr = await db.query(
      `SELECT id FROM ml_wave_configs WHERE is_default=TRUE LIMIT 1`,
      { label: "ml_wave_configs:load_default_config" }
    );
    if (!cr.rows[0]) return fallbackPlan;
    const wr = await db.query(
      `SELECT wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult
       FROM ml_waves WHERE config_id=$1 ORDER BY wave_number`,
      [cr.rows[0].id],
      { label: "ml_wave_configs:load_default_waves" }
    );
    return wr.rows.length > 0 ? wr.rows : fallbackPlan;
  } catch {
    return fallbackPlan;
  }
}

function createMultilaneRuntime({
  aiRuntime,
  db,
  getAllUnitTypes,
  disconnectGrace,
  generateCode,
  gamesByRoomId,
  io,
  issueReconnectToken,
  log,
  logMatchEnd,
  logMatchStart,
  mlRoomsByCode,
  normalizeMatchSettings,
  resolveLoadout,
  sanitizeDisplayName,
  sessionBySocketId,
  simMl,
  socketByPlayerId,
  stringToSeed,
  RECONNECT_GRACE_MS,
}) {
  function createMLRoom(hostSocketId, displayName, settings) {
    let code;
    do code = generateCode(6);
    while (mlRoomsByCode.has(code));

    const roomId = `mlroom_${code}`;
    const room = {
      roomId,
      players: [hostSocketId],
      laneBySocketId: new Map([[hostSocketId, 0]]),
      playerTeamsBySocketId: new Map([[hostSocketId, ffaTeamForLane(0)]]),
      readySet: new Set(),
      playerNames: new Map([[hostSocketId, sanitizeDisplayName(displayName)]]),
      createdAt: Date.now(),
      mode: "multilane",
      hostSocketId,
      aiPlayers: [],
      settings: normalizeMatchSettings(settings),
      pvpMode: "ffa",
    };
    mlRoomsByCode.set(code, room);
    return { code, roomId };
  }

  function countMlTeams(room) {
    const counts = {};
    for (const sid of room.players || []) {
      const team = room.playerTeamsBySocketId?.get(sid) || ffaTeamForLane(room.laneBySocketId.get(sid) || 0);
      counts[team] = (counts[team] || 0) + 1;
    }
    for (const ai of room.aiPlayers || []) {
      const team = ai.team || ffaTeamForLane(ai.laneIndex || 0);
      counts[team] = (counts[team] || 0) + 1;
    }
    return counts;
  }

  function pickBalancedMlTeam(room) {
    const nextLaneIndex = (room.players?.length || 0) + (room.aiPlayers?.length || 0);
    return ffaTeamForLane(nextLaneIndex);
  }

  const FFA_TEAMS = ["red", "yellow", "blue", "green"];

  function ffaTeamForLane(laneIndex) {
    return FFA_TEAMS[laneIndex] || `p${laneIndex}`;
  }

  function validateMlTeamSetup(room) {
    return { ok: true };
  }

  function capFirst(value) {
    return String(value || "").charAt(0).toUpperCase() + String(value || "").slice(1);
  }

  function roundDurationMs(value) {
    return Number.isFinite(value) ? Number(value.toFixed(1)) : null;
  }

  function summarizeLiveMatchLoad(game) {
    let liveUnitCount = 0;
    let projectileCount = 0;
    const laneUnitCounts = [];
    for (const lane of Array.isArray(game && game.lanes) ? game.lanes : []) {
      let laneLiveUnits = 0;
      if (Array.isArray(lane && lane.units)) {
        for (const unit of lane.units) {
          if (unit && unit.hp > 0)
            laneLiveUnits += 1;
        }
      }

      liveUnitCount += laneLiveUnits;
      laneUnitCounts.push(laneLiveUnits);
      if (Array.isArray(lane && lane.projectiles))
        projectileCount += lane.projectiles.length;
    }

    return { liveUnitCount, projectileCount, laneUnitCounts };
  }

  function shouldEmitPerfWarning(entry, kind, nowMs, cooldownMs = 5000) {
    if (!entry)
      return false;
    if (!entry.perfLogAtByKind)
      entry.perfLogAtByKind = Object.create(null);

    const lastAt = Number(entry.perfLogAtByKind[kind]) || 0;
    if ((nowMs - lastAt) < cooldownMs)
      return false;

    entry.perfLogAtByKind[kind] = nowMs;
    return true;
  }

  function logTickPerfIfNeeded(entry, roomId, code, localTick, perf) {
    if (!entry || !entry.game || !perf)
      return;

    const tickBudgetMs = Math.max(1, Number(simMl && simMl.TICK_MS) || 50);
    const shouldLog = perf.callbackMs > tickBudgetMs
      || perf.simTickMs > tickBudgetMs
      || perf.schedulerDelayMs > Math.max(15, tickBudgetMs * 0.35)
      || perf.snapshotBuildMs > Math.max(8, tickBudgetMs * 0.25);
    if (!shouldLog)
      return;

    const nowMs = Date.now();
    if (!shouldEmitPerfWarning(entry, "tick_over_budget", nowMs))
      return;

    const load = summarizeLiveMatchLoad(entry.game);
    const tickBreakdown = entry.game && entry.game._lastTickPerfBreakdown
      ? entry.game._lastTickPerfBreakdown
      : null;
    log.warn("[ml-game][perf] tick over budget", {
      roomId,
      code,
      localTick,
      simTick: entry.game.tick,
      phase: entry.game.phase,
      budgetMs: roundDurationMs(tickBudgetMs),
      callbackMs: roundDurationMs(perf.callbackMs),
      simTickMs: roundDurationMs(perf.simTickMs),
      snapshotBuildMs: roundDurationMs(perf.snapshotBuildMs),
      schedulerDelayMs: roundDurationMs(perf.schedulerDelayMs),
      snapshotBuilt: !!perf.snapshotBuilt,
      snapshotEveryNTicks: entry.snapshotEveryNTicks,
      liveUnitCount: load.liveUnitCount,
      projectileCount: load.projectileCount,
      laneUnitCounts: load.laneUnitCounts,
      tickBreakdown: tickBreakdown ? {
        incomeMs: tickBreakdown.incomeMs,
        scheduledWavesMs: tickBreakdown.scheduledWavesMs,
        buildingConstructionMs: tickBreakdown.buildingConstructionMs,
        barracksSendsMs: tickBreakdown.barracksSendsMs,
        syncLaneCommandsMs: tickBreakdown.syncLaneCommandsMs,
        lanesMs: tickBreakdown.lanesMs,
        balanceTickMs: tickBreakdown.balanceTickMs,
        finalizeMs: tickBreakdown.finalizeMs,
        slowestLanes: tickBreakdown.slowestLanes,
      } : null,
    });
  }

  function buildRematchStatus(room) {
    const votes = room && room.rematchVotes ? Array.from(room.rematchVotes) : [];
    const acceptedLaneIndices = votes
      .map((sid) => room.laneBySocketId.get(sid))
      .filter((laneIndex) => Number.isInteger(laneIndex))
      .sort((a, b) => a - b);
    const acceptedDisplayNames = acceptedLaneIndices.map((laneIndex) => {
      const sid = room.players.find((playerSid) => room.laneBySocketId.get(playerSid) === laneIndex);
      return sid ? (room.playerNames.get(sid) || "Player") : `Lane ${laneIndex + 1}`;
    });
    const needed = room && Array.isArray(room.players) ? room.players.length : 0;
    return {
      count: acceptedLaneIndices.length,
      needed,
      acceptedLaneIndices,
      acceptedDisplayNames,
      allAccepted: needed > 0 && acceptedLaneIndices.length >= needed,
    };
  }

  function mlLobbyUpdatePayload(code, room) {
    const humanPlayers = room.players.map((sid) => ({
      laneIndex: room.laneBySocketId.get(sid),
      displayName: room.playerNames.get(sid) || "Player",
      ready: room.readySet.has(sid),
      isAI: false,
      team: room.playerTeamsBySocketId?.get(sid) || ffaTeamForLane(room.laneBySocketId.get(sid) || 0),
    }));
    const aiPlayers = (room.aiPlayers || []).map((ai) => ({
      laneIndex: ai.laneIndex,
      displayName: ai.displayName || `CPU (${capFirst(ai.difficulty)})`,
      ready: true,
      isAI: true,
      difficulty: ai.difficulty,
      team: ai.team || ffaTeamForLane(ai.laneIndex || 0),
      takeover: !!ai.takeover,
    }));
    const players = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);
    return {
      code,
      players,
      hostLaneIndex: 0,
      settings: normalizeMatchSettings(room.settings),
      pvpMode: "ffa",
    };
  }

  function buildMlMatchSeed(roomId, code, laneAssignments) {
    const parts = laneAssignments
      .slice()
      .sort((a, b) => a.laneIndex - b.laneIndex)
      .map((assignment) => `${assignment.laneIndex}:${assignment.team}:${assignment.isAI ? "ai" : "human"}`);
    return `${roomId}:${code}:${parts.join("|")}`;
  }

  function getSlotDefinition(simMl, playerCount, laneIndex, laneTeams) {
    const config = simMl.createMLPublicConfig({ playerCount, laneTeams });
    const defs = config && Array.isArray(config.slotDefinitions) ? config.slotDefinitions : [];
    return defs.find((slot) => Number(slot.laneIndex) === Number(laneIndex)) || null;
  }

  function getLaneDisplayName(room, laneIndex) {
    const sid = room.players.find((socketId) => room.laneBySocketId.get(socketId) === laneIndex);
    if (sid) return room.playerNames.get(sid) || "Player";
    const ai = (room.aiPlayers || []).find((entry) => entry.laneIndex === laneIndex);
    if (ai) return ai.displayName || `CPU (${capFirst(ai.difficulty)})`;
    return `Lane ${Number(laneIndex) + 1}`;
  }

  function buildFinalStats(room, game) {
    return game.lanes.map((lane) => ({
      laneIndex: lane.laneIndex,
      displayName: getLaneDisplayName(room, lane.laneIndex),
      team: lane.team,
      side: lane.side,
      income: lane.income,
      buildValue: simMl.getLaneBuildValue(lane),
      gold: Math.floor(lane.gold),
      totalSendSpend: lane.totalSendSpend,
      totalSendCount: lane.totalSendCount || 0,
      totalBuildSpend: lane.totalBuildSpend || 0,
      totalLeaksTaken: lane.totalLeaksTaken,
      biggestLeakTaken: lane.biggestLeakTaken || 0,
      wavesHeld: lane.wavesHeld || 0,
      wavesLeaked: lane.wavesLeaked || 0,
      longestHoldStreak: lane.longestHoldStreak || 0,
      lives: lane.lives,
      teamHp: lane.lives,
      eliminated: lane.eliminated,
    }));
  }

  function buildOutcomePayload(room, entry) {
    const game = entry.game;
    const balanceData = simMl && typeof simMl.getFinalizedMatchBalance === "function"
      ? simMl.getFinalizedMatchBalance(game)
      : { summary: null, flags: [], diagnosis: null };
    const winnerLane = Number.isInteger(game.officialWinnerLane)
      ? game.officialWinnerLane
      : (Number.isInteger(game.winner) ? game.winner : null);
    const totalDurationSec = Number.isInteger(game.tick)
      ? Math.round(game.tick / (simMl.TICK_HZ || 20))
      : Math.round((Date.now() - (game.startedAt || Date.now())) / 1000);
    const survivalDurationSec = Number.isInteger(game.survivalStartedAtTick)
      ? Math.max(0, Math.round((game.tick - game.survivalStartedAtTick) / (simMl.TICK_HZ || 20)))
      : totalDurationSec;
    const survivalExtraRounds = Number.isInteger(game.survivalStartRound)
      ? Math.max(0, game.roundNumber - game.survivalStartRound)
      : Math.max(0, game.roundNumber - 1);

    return {
      winnerLaneIndex: winnerLane ?? -1,
      winnerName: winnerLane !== null && winnerLane !== undefined ? getLaneDisplayName(room, winnerLane) : "",
      winningTeam: winnerLane !== null && winnerLane !== undefined ? (game.lanes[winnerLane]?.team || null) : null,
      winningSide: winnerLane !== null && winnerLane !== undefined ? (game.lanes[winnerLane]?.side || null) : null,
      losingTeam: null,
      losingSide: null,
      finalRound: game.roundNumber,
      gameDuration: totalDurationSec,
      causeLoss: `Survival ended on Wave ${game.roundNumber}`,
      finalStats: buildFinalStats(room, game),
      waveSnapshots: game.roundSnapshots,
      balanceSummary: balanceData.summary,
      balanceFlags: balanceData.flags,
      balanceDiagnosis: balanceData.diagnosis,
      matchState: game.matchState || (game.phase === "ended" ? "final_game_over" : "active_survival"),
      continuedIntoSurvival: game.continuedIntoSurvival !== false,
      survivalDuration: survivalDurationSec,
      survivalExtraRounds,
      pvpWinnerLaneIndex: winnerLane ?? -1,
      finalGameOverReason: game.finalGameOverReason || null,
      finalGameOverDebug: game.finalGameOverDebug || null,
    };
  }

  function getWinningHumanLaneIndices(room, game) {
    if (!room || !game || !Number.isInteger(game.officialWinnerLane)) return [];
    return room.players
      .map((sid) => room.laneBySocketId.get(sid))
      .filter((laneIndex) => laneIndex === game.officialWinnerLane);
  }

  function handlePostWinDecision(roomId, code, decision, sid) {
    const room = mlRoomsByCode.get(code);
    const entry = gamesByRoomId.get(roomId);
    if (!room || !entry || !entry.game) return { ok: false, reason: "Game not active" };
    const game = entry.game;
    const laneIndex = room.laneBySocketId.get(sid);
    const lane = Number.isInteger(laneIndex) ? game.lanes[laneIndex] : null;
    if (!lane || lane.eliminated)
      return { ok: false, reason: "You have been eliminated." };

    if (decision === "continue") {
      game.awaitingPostWinDecision = false;
      game.matchState = "active_survival";
      game.continuedIntoSurvival = true;
      if (!Number.isInteger(game.survivalStartedAtTick))
        game.survivalStartedAtTick = 0;
      if (!Number.isInteger(game.survivalStartRound))
        game.survivalStartRound = 1;

      const currentWaveComplete = simMl && typeof simMl.countRemainingWaveMobs === "function"
        ? simMl.countRemainingWaveMobs(game) <= 0
        : !(simMl && typeof simMl.isCurrentWaveComplete === "function") || simMl.isCurrentWaveComplete(game);
      const startedImmediately = currentWaveComplete
        && !!(simMl && typeof simMl.startNextWaveNow === "function" && simMl.startNextWaveNow(game));
      log.info("[ml-game] survival continue acknowledged", {
        roomId,
        code,
        laneIndex,
        startedImmediately,
        round: game.roundNumber,
      });
      io.to(roomId).emit("ml_survival_continuation_started", {
        winnerLaneIndex: -1,
        winningTeam: null,
        winningSide: null,
      });
      return { ok: true, startedImmediately };
    }

    if (decision === "end_now") {
      return { ok: false, reason: "Manual end-now flow is no longer supported in free-for-all survival." };
    }

    return { ok: false, reason: "Unknown post-win decision" };
  }

  function applyMultilaneAction(entry, laneIndex, action) {
    const result = simMl.applyMLAction(entry.game, laneIndex, action);
    const tracker = entry.botController && entry.botController.runtimeTracker;
    if (tracker && !result.ok)
      tracker.recordInvalidAction(laneIndex);
    return result;
  }

  function addRuntimeBot(entry, botConfig) {
    if (!entry || !botConfig || !Number.isInteger(botConfig.laneIndex)) return null;
    if (!entry.botController) {
      entry.botController = aiRuntime.createBotController({
        game: entry.game,
        seed: `${entry.game.matchSeed || "match"}:runtime`,
        tickMs: simMl.TICK_MS,
        botTickMs: 500,
        runtimeOptions: { waveTickInterval: simMl.INCOME_INTERVAL_TICKS || 240 },
        botConfigs: [botConfig],
      });
      return entry.botController;
    }
    if (typeof entry.botController.addBot === "function") {
      entry.botController.addBot(botConfig);
    }
    return entry.botController;
  }

  function removeRuntimeBot(entry, laneIndex) {
    if (!entry || !entry.botController || !Number.isInteger(laneIndex)) return null;
    if (typeof entry.botController.removeBot === "function") {
      return entry.botController.removeBot(laneIndex);
    }
    return null;
  }

  function attachTakeoverBot(room, entry, laneIndex, options) {
    if (!room || !entry || !Number.isInteger(laneIndex)) return null;
    const lane = entry.game && entry.game.lanes && entry.game.lanes[laneIndex];
    if (!lane || lane.eliminated) return null;
    if (!room.aiPlayers) room.aiPlayers = [];
    const existing = room.aiPlayers.find((ai) => ai.laneIndex === laneIndex);
    if (existing) return existing;
    const opt = options && typeof options === "object" ? options : {};
    const aiEntry = {
      laneIndex,
      difficulty: opt.difficulty || "hard",
      team: opt.team || lane.team || "red",
      displayName: opt.displayName || `Takeover AI (${capFirst(opt.difficulty || "hard")})`,
      takeover: true,
      humanDisplayName: opt.humanDisplayName || opt.displayName || "Player",
      playerId: opt.playerId || null,
    };
    room.aiPlayers.push(aiEntry);
    addRuntimeBot(entry, {
      laneIndex,
      difficulty: aiEntry.difficulty,
      seedSuffix: `takeover:${laneIndex}`,
    });
    return aiEntry;
  }

  function detachTakeoverBot(room, entry, laneIndex) {
    if (!room || !Number.isInteger(laneIndex)) return null;
    const idx = (room.aiPlayers || []).findIndex((ai) => ai.laneIndex === laneIndex && ai.takeover);
    if (idx === -1) return null;
    const [removed] = room.aiPlayers.splice(idx, 1);
    removeRuntimeBot(entry, laneIndex);
    return removed;
  }

  function _bucketToDbMode(queueMode) {
    if (!queueMode) return "multilane";
    if (queueMode === "ranked_2v2") return "ffa_ranked";
    const parts = queueMode.split(':');
    if (parts.length !== 3 || parts[0] !== "line_wars") return "multilane";
    const [, matchFormat, rankedFlag] = parts;
    const isRanked = rankedFlag === "1";
    if (matchFormat === "1v1" || matchFormat === "2v2" || matchFormat === "ffa")
      return isRanked ? "ffa_ranked" : "ffa_casual";
    return "multilane";
  }

  // ── Loadout-phase helpers ────────────────────────────────────────────────

  // Returns the buildable loadout catalog used by ml_loadout_confirm validation.
  function buildAvailableUnits() {
    return getAllUnitTypes()
      .filter((ut) => canUseInLoadout(ut))
      .map(ut => ({
        id:            ut.id,
        key:           ut.key,
        name:          ut.name,
        send_cost:     Number(ut.send_cost)     || 0,
        build_cost:    Number(ut.build_cost)    || 0,
        hp:            Number(ut.hp)            || 0,
        attack_damage: Number(ut.attack_damage) || 0,
        attack_speed:  Number(ut.attack_speed)  || 1,
        range:         Number(ut.range)         || 0,
        path_speed:    Number(ut.path_speed)    || 0,
        income:        Number(ut.income)        || 0,
        damage_type:   ut.damage_type  || "NORMAL",
        armor_type:    ut.armor_type   || "UNARMORED",
        usage_scope:   ut.usage_scope  || "both",
      }));
  }

  function getHumanLaneIndices(room) {
    return (room?.players || [])
      .map((sid) => room.laneBySocketId.get(sid))
      .filter((laneIndex) => Number.isInteger(laneIndex))
      .sort((a, b) => a - b);
  }

  function areAllHumanLanesReady(room, readyByLane) {
    const humanLaneIndices = getHumanLaneIndices(room);
    if (humanLaneIndices.length === 0) return false;
    return humanLaneIndices.every((laneIndex) => readyByLane && readyByLane.has(laneIndex));
  }

  // Waits for all human players to emit ml_loadout_confirm, or until timeoutMs.
  // Stores confirmed unit-type-ID arrays on sock.pendingUnitTypeIds so the
  // existing resolveLoadout() call picks them up as inline selections.
  // Emits ml_loadout_phase_end to the room when done.
  function waitForLoadoutConfirms(room, roomId, timeoutMs) {
    const humanSockets = room.players
      .map(sid => io.sockets.sockets.get(sid))
      .filter(Boolean);
    if (humanSockets.length === 0) return Promise.resolve();

    const hasConfirmedSelection = (sock) => {
      if (!sock) return false;
      if (isValidRaceId(sock.pendingRaceId)) return true;
      return Array.isArray(sock.pendingUnitTypeIds) && sock.pendingUnitTypeIds.length === 5;
    };

    return new Promise(resolve => {
      let resolved = false;
      const handlers = new Map(); // sock → listener fn

      const done = (reason) => {
        if (resolved) return;
        resolved = true;
        clearTimeout(timeoutHandle);
        room._loadoutPhaseResolve  = null;
        room._loadoutPhaseDeadline = null;
        for (const [sock, fn] of handlers) sock.removeListener("ml_loadout_confirm", fn);
        io.to(roomId).emit("ml_loadout_phase_end", {
          code:   room.code || "",
          reason: reason || "all_confirmed",
        });
        resolve();
      };

      room._loadoutPhaseResolve  = () => done("all_confirmed");
      room._loadoutPhaseDeadline = Date.now() + timeoutMs;

      for (const sock of humanSockets) {
        const fn = () => {
          if (!hasConfirmedSelection(sock)) return;
          const allDone = humanSockets.every(hasConfirmedSelection);
          if (allDone) done("all_confirmed");
        };
        handlers.set(sock, fn);
        sock.on("ml_loadout_confirm", fn);
      }

      if (humanSockets.every(hasConfirmedSelection))
        done("all_confirmed");

      const timeoutHandle = setTimeout(() => done("timeout"), timeoutMs);
    });
  }

  function getUpcomingWaveNumber(game) {
    if (simMl && typeof simMl.getUpcomingWaveNumber === "function")
      return simMl.getUpcomingWaveNumber(game);

    const currentRound = Math.max(1, Math.floor(Number(game && game.roundNumber) || 1));
    return game && game.hasSpawnedWave
      ? currentRound + 1
      : currentRound;
  }

  function getEligibleWaveReadyLaneIndices(room, game) {
    if (!room || !game) return [];
    return room.players
      .map((sid) => room.laneBySocketId.get(sid))
      .filter((laneIndex) => {
        if (!Number.isInteger(laneIndex)) return false;
        const lane = game.lanes[laneIndex];
        return !!lane && !lane.eliminated;
      })
      .sort((a, b) => a - b);
  }

  function buildWaveReadyState(room, game, waveReadyVotes) {
    const eligibleLaneIndices = getEligibleWaveReadyLaneIndices(room, game);
    const readyLaneIndices = eligibleLaneIndices
      .filter((laneIndex) => waveReadyVotes && waveReadyVotes.has(laneIndex))
      .sort((a, b) => a - b);
    const remainingWaveMobCount = !!(simMl && typeof simMl.countRemainingWaveMobs === "function")
      ? simMl.countRemainingWaveMobs(game)
      : 0;

    return {
      upcomingWaveNumber: getUpcomingWaveNumber(game),
      requiredReadyCount: eligibleLaneIndices.length,
      eligibleLaneIndices,
      readyLaneIndices,
      remainingWaveMobCount,
      currentWaveComplete: remainingWaveMobCount <= 0,
      allReady: eligibleLaneIndices.length > 0 && readyLaneIndices.length >= eligibleLaneIndices.length,
    };
  }

  function emitWaveReadyState(roomId, room, entry) {
    if (!roomId || !room || !entry || !entry.game)
      return;

    io.to(roomId).emit("ml_wave_ready_state", buildWaveReadyState(room, entry.game, entry.waveReadyVotes));
  }

  function resetWaveReadyState(roomId, room, entry) {
    if (!entry)
      return;

    entry.waveReadyVotes = new Set();
    entry.waveReadyWaveNumber = getUpcomingWaveNumber(entry.game);
    emitWaveReadyState(roomId, room, entry);
  }

  function submitWaveReadyVote(roomId, code, laneIndex) {
    const room = mlRoomsByCode.get(code);
    const entry = gamesByRoomId.get(roomId);
    if (!room || !entry || !entry.game) {
      log.warn("[WaveStart][Server] rejected", {
        roomId,
        code,
        laneIndex,
        reason: "Game not active",
      });
      return { ok: false, reason: "Game not active" };
    }

    const game = entry.game;
    if (game.phase !== "playing") {
      log.warn("[WaveStart][Server] rejected", {
        roomId,
        code,
        laneIndex,
        reason: "Game not active",
        phase: game.phase,
      });
      return { ok: false, reason: "Game not active" };
    }

    const lane = game.lanes[laneIndex];
    if (!lane || lane.eliminated) {
      log.warn("[WaveStart][Server] rejected", {
        roomId,
        code,
        laneIndex,
        reason: "You have been eliminated.",
      });
      return { ok: false, reason: "You have been eliminated." };
    }

    const readyStateBeforeVote = buildWaveReadyState(room, game, entry.waveReadyVotes);
    if (!readyStateBeforeVote.currentWaveComplete) {
      log.warn("[WaveStart][Server] blocked", {
        roomId,
        code,
        laneIndex,
        reason: "Finish the current wave before starting the next one.",
        remainingWaveMobCount: readyStateBeforeVote.remainingWaveMobCount,
        upcomingWaveNumber: readyStateBeforeVote.upcomingWaveNumber,
      });
      return { ok: false, reason: "Finish the current wave before starting the next one.", readyState: readyStateBeforeVote };
    }

    const upcomingWaveNumber = getUpcomingWaveNumber(game);
    if (entry.waveReadyWaveNumber !== upcomingWaveNumber) {
      entry.waveReadyVotes = new Set();
      entry.waveReadyWaveNumber = upcomingWaveNumber;
    }

    if (!entry.waveReadyVotes)
      entry.waveReadyVotes = new Set();

    entry.waveReadyVotes.add(laneIndex);
    log.info("[WaveStart][Server] vote recorded", {
      roomId,
      code,
      laneIndex,
      upcomingWaveNumber,
      readyVotes: Array.from(entry.waveReadyVotes.values()).sort((a, b) => a - b),
    });
    emitWaveReadyState(roomId, room, entry);

    const readyState = buildWaveReadyState(room, game, entry.waveReadyVotes);
    if (!readyState.allReady)
      return { ok: true, startedImmediately: false, readyState };

    const startedImmediately = !!(simMl && typeof simMl.startNextWaveNow === "function" && simMl.startNextWaveNow(game));
    if (!startedImmediately) {
      log.warn("[WaveStart][Server] blocked", {
        roomId,
        code,
        laneIndex,
        reason: "Unable to start the next wave yet.",
        upcomingWaveNumber,
      });
      emitWaveReadyState(roomId, room, entry);
      return { ok: false, reason: "Unable to start the next wave yet." };
    }

    log.info("[WaveStart][Server] started", {
      roomId,
      code,
      laneIndex,
      upcomingWaveNumber,
      roundNumber: game.roundNumber,
    });
    resetWaveReadyState(roomId, room, entry);
    return { ok: true, startedImmediately: true };
  }

  function waitForGameplaySceneReady(room, roomId, timeoutMs) {
    const humanSockets = room.players
      .map((sid) => io.sockets.sockets.get(sid))
      .filter(Boolean);
    if (humanSockets.length === 0) return Promise.resolve("no_players");

    return new Promise((resolve) => {
      let resolved = false;
      const handlers = new Map();
      const ready = new Set();

      const done = (reason) => {
        if (resolved) return;
        resolved = true;
        clearTimeout(timeoutHandle);
        for (const [sock, fn] of handlers) sock.removeListener("ml_game_scene_ready", fn);
        resolve(reason || "all_ready");
      };

      for (const sock of humanSockets) {
        const fn = () => {
          ready.add(sock.id);
          if (ready.size >= humanSockets.length)
            done("all_ready");
        };
        handlers.set(sock, fn);
        sock.on("ml_game_scene_ready", fn);
      }

      const timeoutHandle = setTimeout(() => done("timeout"), timeoutMs);
    });
  }

  // ── Phase-based readiness helpers ────────────────────────────────────────

  // Broadcasts current per-player preparation state to all room members.
  function broadcastPreparationState(room, roomId) {
    const humanPlayers = room.players.map(sid => {
      const sock = io.sockets.sockets.get(sid);
      const laneIndex = room.laneBySocketId.get(sid);
      const progress = room.contentProgressByLane?.get(laneIndex) || sock?._contentProgress || null;
      return {
        laneIndex,
        displayName:    room.playerNames?.get(sid) || "Player",
        loadoutReady:   !!(room.loadoutReadyByLane?.has(laneIndex) || (sock && sock._loadoutReady)),
        gameplayReady:  !!(room.gameplayReadyByLane?.has(laneIndex) || (sock && sock._gameplayReady)),
        contentPercent: progress?.percent || 0,
        contentState:   progress?.state || "Preparing",
      };
    });
    // AI players simulate a brief 3-second "load" so their bars animate in real-time.
    // _aiReadyAt is set when the match prep starts; before that, we ramp 0→1 over 3s.
    const now = Date.now();
    const aiReady = room._aiReadyAt ? now >= room._aiReadyAt : false;
    const aiElapsed = room._aiReadyAt ? Math.min(1, (now - (room._aiReadyAt - 3000)) / 3000) : 0;
    const aiPlayers = (room.aiPlayers || []).map(ai => ({
      laneIndex:      ai.laneIndex,
      displayName:    ai.displayName || "CPU",
      loadoutReady:   true,
      gameplayReady:  aiReady,
      contentPercent: aiReady ? 1.0 : aiElapsed,
      contentState:   aiReady ? "Ready" : "Loading...",
    }));
    const players = [...humanPlayers, ...aiPlayers];
    io.to(roomId).emit("ml_match_preparation_state", { players });
  }

  // Cancels a match in progress, cleaning up the game entry and notifying clients.
  function cancelMatch(roomId, code, reason) {
    log.warn("[ml-game] match cancelled", { roomId, code, reason });
    const entry = gamesByRoomId.get(roomId);
    if (entry) {
      if (entry.tickHandle) clearInterval(entry.tickHandle);
      gamesByRoomId.delete(roomId);
    }
    io.to(roomId).emit("ml_match_cancelled", {
      code,
      reason,
      message: "A player did not finish downloading in time.",
    });
  }

  // Waits for all human players to signal ml_loadout_ready.
  // The flag socket._loadoutReady is set by the registerHandlers ml_loadout_ready handler,
  // which also calls room._loadoutReadyResolve() when all players are ready.
  // Resolves with { reason: "all_ready" | "no_players" | "timeout" }.
  function waitForLoadoutReady(room, roomId, timeoutMs) {
    const humanLaneIndices = getHumanLaneIndices(room);
    if (humanLaneIndices.length === 0) return Promise.resolve({ reason: "no_players" });

    // Fast path: all already signalled before we started waiting
    if (areAllHumanLanesReady(room, room.loadoutReadyByLane)) {
      broadcastPreparationState(room, roomId);
      return Promise.resolve({ reason: "all_ready" });
    }

    return new Promise(resolve => {
      let resolved = false;

      const done = (reason) => {
        if (resolved) return;
        resolved = true;
        clearInterval(broadcastInterval);
        clearTimeout(timeoutHandle);
        room._loadoutReadyResolve = null;
        resolve({ reason });
      };

      room._loadoutReadyResolve = () => done("all_ready");

      const broadcastInterval = setInterval(() => broadcastPreparationState(room, roomId), 1000);
      const timeoutHandle = setTimeout(() => done("timeout"), timeoutMs);
    });
  }

  // Waits for all human players to signal ml_gameplay_ready.
  // The flag socket._gameplayReady is set by the registerHandlers ml_gameplay_ready handler,
  // which also calls room._gameplayReadyResolve() when all players are ready.
  // Resolves with { reason: "all_ready" | "no_players" | "timeout" }.
  function waitForGameplayReady(room, roomId, timeoutMs) {
    const humanLaneIndices = getHumanLaneIndices(room);
    if (humanLaneIndices.length === 0) return Promise.resolve({ reason: "no_players" });

    // Fast path: all already signalled before we started waiting
    if (areAllHumanLanesReady(room, room.gameplayReadyByLane)) {
      broadcastPreparationState(room, roomId);
      return Promise.resolve({ reason: "all_ready" });
    }

    return new Promise(resolve => {
      let resolved = false;

      const done = (reason) => {
        if (resolved) return;
        resolved = true;
        clearInterval(broadcastInterval);
        clearTimeout(timeoutHandle);
        room._gameplayReadyResolve = null;
        resolve({ reason });
      };

      room._gameplayReadyResolve = () => done("all_ready");

      const broadcastInterval = setInterval(() => broadcastPreparationState(room, roomId), 1000);
      const timeoutHandle = setTimeout(() => done("timeout"), timeoutMs);
    });
  }
  // ────────────────────────────────────────────────────────────────────────

  async function startMLGame(roomId, code) {
    if (gamesByRoomId.has(roomId)) return;
    const room = mlRoomsByCode.get(code);
    if (!room) return;

    const aiList = room.aiPlayers || [];
    const playerCount = room.players.length + aiList.length;
    const laneAssignments = [
      ...room.players.map((sid) => ({
        laneIndex: room.laneBySocketId.get(sid),
        displayName: room.playerNames.get(sid) || "Player",
        isAI: false,
        team: ffaTeamForLane(room.laneBySocketId.get(sid) || 0),
      })),
      ...aiList.map((ai) => ({
        laneIndex: ai.laneIndex,
        displayName: ai.displayName || `CPU (${capFirst(ai.difficulty)})`,
        isAI: true,
        team: ffaTeamForLane(ai.laneIndex || 0),
      })),
    ];

    const laneTeams = new Array(playerCount).fill(null);
    for (const assignment of laneAssignments) {
      if (
        Number.isInteger(assignment.laneIndex) &&
        assignment.laneIndex >= 0 &&
        assignment.laneIndex < playerCount
      ) {
        laneTeams[assignment.laneIndex] = assignment.team || ffaTeamForLane(assignment.laneIndex);
      }
    }

    for (const sid of room.players)
      room.playerTeamsBySocketId.set(sid, ffaTeamForLane(room.laneBySocketId.get(sid) || 0));
    for (const ai of aiList)
      ai.team = ffaTeamForLane(ai.laneIndex || 0);

    for (const assignment of laneAssignments) {
      const slot = getSlotDefinition(simMl, playerCount, assignment.laneIndex, laneTeams);
      if (!slot) continue;
      assignment.side = slot.side;
      assignment.slotKey = slot.slotKey;
      assignment.slotColor = slot.slotColor;
      assignment.branchId = slot.branchId;
      assignment.branchLabel = slot.branchLabel;
      assignment.castleSide = slot.castleSide;
    }

    const matchSeedStr = buildMlMatchSeed(roomId, code, laneAssignments);
    const matchSeedNum = stringToSeed(matchSeedStr);
    const publicConfig = simMl.createMLPublicConfig({ ...room.settings, playerCount, laneTeams });
    const game = simMl.createMLGame(playerCount, {
      ...room.settings,
      laneTeams,
      matchSeed: matchSeedNum,
      startingCombatMilitiaCount: 0,
    });

    try {
      const waves = await loadDefaultWaveConfig(db);
      game.waveConfig = waves;
      const usingLockedWaveFallback = Array.isArray(waves) && waves.length > 0 && Object.prototype.hasOwnProperty.call(waves[0], "note");
      log.info(`[ml-game] wave config loaded: ${waves.length} waves`, {
        roomId,
        source: usingLockedWaveFallback ? "locked_fallback" : "database",
      });
    } catch (err) {
      game.waveConfig = [];
      log.error("[ml-game] failed to load wave config", { err: err.message });
    }

    room.loadoutReadyByLane = new Set();
    room.gameplayReadyByLane = new Set();
    room.contentProgressByLane = new Map();

    // Reset readiness before notifying clients so an immediate ml_loadout_ready
    // from the lobby is not erased by a later stale-state clear.
    for (const sid of room.players) {
      const sock = io.sockets.sockets.get(sid);
      if (sock) {
        sock._loadoutReady = false;
        sock._gameplayReady = false;
        sock.pendingUnitTypeIds = null;
        sock.pendingRaceId = null;
        sock._contentProgress = null;
      }
    }

    io.to(roomId).emit("ml_match_ready", {
      code,
      playerCount,
      laneAssignments,
      battlefieldTopology: publicConfig.battlefieldTopology,
      aiEngine: "new_bot_controller_v1",
    });
    log.info("[multilane] emitting ml_match_config", {
      roomId,
      code,
      playerCount,
      loadoutEntries: Array.isArray(publicConfig.loadout) ? publicConfig.loadout.length : 0,
      layoutId: publicConfig?.battlefieldLayout?.layoutId || null,
      layoutHash: publicConfig?.battlefieldLayout?.contentHash || null,
      layoutLanes: Array.isArray(publicConfig?.battlefieldLayout?.lanes) ? publicConfig.battlefieldLayout.lanes.length : 0,
    });
    io.to(roomId).emit("ml_match_config", publicConfig);

    // ── EARLY GAME ENTRY: register before loadout so rejoin_game works during scene transition ──
    const playerSnapshot = room.players
      .map((sid) => {
        const sock = io.sockets.sockets.get(sid);
        return sock?.playerId ? { playerId: sock.playerId, laneIndex: room.laneBySocketId.get(sid) } : null;
      })
      .filter(Boolean);
    const dbMode = _bucketToDbMode(room.queueMode);
    const eliminatedNotified = new Set();
    const snapshotEveryNTicks = 2;
    const gameEntry = {
      game,
      tickHandle: null,
      mode: "multilane",
      snapshotEveryNTicks,
      eliminatedNotified,
      botController: null,
      matchIdPromise: null,
      playerSnapshot,
      dbMode,
      partyASize: room.partyASize || 1,
      waveReadyVotes: new Set(),
      waveReadyWaveNumber: getUpcomingWaveNumber(game),
      perfLogAtByKind: Object.create(null),
    };
    gamesByRoomId.set(roomId, gameEntry);
    for (const sid of room.players) {
      const sock = io.sockets.sockets.get(sid);
      if (!sock) continue;
      const laneIdx = room.laneBySocketId.get(sid);
      const token = issueReconnectToken(roomId, code, "multilane", String(laneIdx), sock);
      sock.emit("reconnect_token", { token, gracePeriodMs: RECONNECT_GRACE_MS });
    }
    // ─────────────────────────────────────────────────────────────────────

    // ── LOADOUT SELECTION PHASE ──────────────────────────────────────────
    const selectionMode = (room.settings && room.settings.selectionMode) || "manual";
    const loadoutTimeoutMs = 60_000;
    room.loadoutConfirms = new Map();
    if (!room.loadoutByLane) room.loadoutByLane = [];
    if (!room.raceByLane) room.raceByLane = [];
    try {
      await Promise.all(
        aiList.map(async (ai) => {
          const raceId = getDefaultRaceId();
          const loadout = await resolveLoadout(null, null, raceId);
          const laneIdx = ai.laneIndex;
          room.loadoutByLane[laneIdx] = loadout;
          room.raceByLane[laneIdx] = raceId;
          if (game.lanes[laneIdx]) {
            const keys = loadout.map((ut) => ut.key);
            game.lanes[laneIdx].loadoutKeys = keys;
          }
        })
      );
    } catch (err) {
      log.error("[loadout] ai pre-resolve error", { err: err.message, details: err.details || null });
      cancelMatch(roomId, code, "ai_loadout_resolution_failed");
      return;
    }

    // ── Clear per-match socket state to prevent stale flags from prior matches ──
    // ─────────────────────────────────────────────────────────────────────

    // ── BARRIER 1: All players must have loadout-critical assets before loadout starts ──
    log.info("[ml-game] waiting for loadout ready", { roomId, code });
    const loadoutReadyResult = await waitForLoadoutReady(room, roomId, 120_000);
    log.info("[ml-game] loadout ready wait complete", { roomId, code, reason: loadoutReadyResult.reason });
    if (loadoutReadyResult.reason === "timeout") {
      cancelMatch(roomId, code, "loadout_ready_timeout");
      return;
    }
    // ─────────────────────────────────────────────────────────────────────

    io.to(roomId).emit("ml_loadout_phase_start", {
      code,
      timeoutSeconds: Math.ceil(loadoutTimeoutMs / 1000),
      selectionMode,
      defaultRaceId: getDefaultRaceId(),
      selectedRaceId: getDefaultRaceId(),
      availableRaceIds: getAvailableRaces().map((race) => race.id),
      availableUnits: buildAvailableUnits(),
    });

    // AI players animate 0→100% over 3 seconds from loadout phase start.
    room._aiReadyAt = Date.now() + 3000;

    // Broadcast initial preparation state immediately so clients can populate
    // the player panel, then keep broadcasting every 1.5s during unit selection.
    broadcastPreparationState(room, roomId);
    const loadoutPhaseInterval = setInterval(() => broadcastPreparationState(room, roomId), 1500);

    if (selectionMode !== "random") {
      await waitForLoadoutConfirms(room, roomId, loadoutTimeoutMs);
    }
    clearInterval(loadoutPhaseInterval);
    // ─────────────────────────────────────────────────────────────────────

    // ── BARRIER 2: All players must have gameplay-critical assets before match starts ──
    // Start waiting now so the timer runs in parallel with per-player loadout resolution
    // (clients begin downloading gameplay content as soon as they receive ml_match_config).
    const gameplayReadyPromise = waitForGameplayReady(room, roomId, 300_000);

    try {
      await Promise.all([
        ...room.players.map(async (sid) => {
          const sock = io.sockets.sockets.get(sid);
          if (!sock) return;
          const laneIdx = room.laneBySocketId.get(sid);
          const raceId = isValidRaceId(sock.pendingRaceId)
            ? sock.pendingRaceId
            : getDefaultRaceId();
          const loadout = await resolveLoadout(sock.playerId || null, sock.pendingUnitTypeIds, raceId);
          sock.pendingUnitTypeIds = null;
          sock.pendingRaceId = null;
          room.loadoutByLane[laneIdx] = loadout;
          room.raceByLane[laneIdx] = raceId;
          if (game.lanes[laneIdx]) {
            const keys = loadout.map((ut) => ut.key);
            game.lanes[laneIdx].loadoutKeys = keys;
            // Load equipped skins for this player
            if (db && sock.playerId) {
              try {
                const skinRows = await db.query(
                  `SELECT pse.unit_type, pse.skin_key
                     FROM player_skin_equip pse
                     JOIN skin_catalog s ON s.skin_key = pse.skin_key
                    WHERE pse.player_id = $1
                      AND s.enabled = true`,
                  [sock.playerId],
                  { label: "ml:load_equipped_skins" }
                );
                game.lanes[laneIdx].equippedSkins = Object.fromEntries(
                  skinRows.rows.map(r => [r.unit_type, r.skin_key])
                );
              } catch (err) {
                log.error('[skins] failed to load equipped skins', { err: err.message });
              }
            }
          }
          const resolvedConfig = simMl.createMLPublicConfig({ ...room.settings, playerCount, laneTeams });
          resolvedConfig.loadout = loadout;
          resolvedConfig.raceId = raceId;
          log.info("[multilane] emitting resolved ml_match_config", {
            roomId,
            code,
            laneIndex: laneIdx,
            loadoutEntries: Array.isArray(resolvedConfig.loadout) ? resolvedConfig.loadout.length : 0,
            layoutId: resolvedConfig?.battlefieldLayout?.layoutId || null,
            layoutHash: resolvedConfig?.battlefieldLayout?.contentHash || null,
            layoutLanes: Array.isArray(resolvedConfig?.battlefieldLayout?.lanes)
              ? resolvedConfig.battlefieldLayout.lanes.length
              : 0,
          });
          sock.emit("ml_match_config", resolvedConfig);
        }),
      ]);
    } catch (err) {
      log.error("[loadout] resolve error", { err: err.message, details: err.details || null });
      cancelMatch(roomId, code, "loadout_resolution_failed");
      return;
    }

    const gameplayReadyResult = await gameplayReadyPromise;
    log.info("[ml-game] gameplay ready wait complete", {
      roomId,
      code,
      reason: gameplayReadyResult.reason,
    });
    if (gameplayReadyResult.reason === "timeout") {
      cancelMatch(roomId, code, "gameplay_ready_timeout");
      return;
    }
    if (gameplayReadyResult.reason === "no_players") {
      cancelMatch(roomId, code, "no_players");
      return;
    }
    // ticks begin only after all players are gameplay_ready

    const botController = aiList.length > 0
      ? aiRuntime.createBotController({
          game,
          seed: matchSeedStr,
          tickMs: simMl.TICK_MS,
          botTickMs: 500,
          runtimeOptions: { waveTickInterval: simMl.INCOME_INTERVAL_TICKS || 240 },
          botConfigs: aiList.map((ai) => ({
            laneIndex: ai.laneIndex,
            difficulty: ai.difficulty,
          })),
        })
      : null;

    if (aiList.length > 0) {
      log.info("ml ai engine active", {
        roomId,
        code,
        aiEngine: aiRuntime.__engine || "new_bot_controller_v1",
        botCount: botController ? botController.getBotCount() : 0,
      });
    }

    gameEntry.botController = botController;
    gameEntry.matchIdPromise = logMatchStart(roomId, dbMode);
    const matchIdPromise = gameEntry.matchIdPromise;
    let localTick = 0;
    let lastTickStartedAt = null;
    const tickBudgetMs = Math.max(1, Number(simMl.TICK_MS) || 50);

    const tickHandle = setInterval(() => {
      const entry = gamesByRoomId.get(roomId);
      if (!entry) return;
      const tickStartedAt = performance.now();
      const schedulerDelayMs = lastTickStartedAt == null
        ? 0
        : Math.max(0, tickStartedAt - lastTickStartedAt - tickBudgetMs);
      lastTickStartedAt = tickStartedAt;
      let simTickMs = 0;
      let snapshotBuildMs = 0;
      let snapshotBuilt = false;
      let eliminationStateChanged = false;

      const prevLite = entry.botController ? aiRuntime.captureStateLite(entry.game) : null;
      if (entry.botController) entry.botController.onBeforeSimTick(entry.game);
      const simTickStartedAt = performance.now();
      simMl.mlTick(entry.game);
      simTickMs = performance.now() - simTickStartedAt;
      if (entry.botController && prevLite) {
        const nextLite = aiRuntime.captureStateLite(entry.game);
        entry.botController.onAfterSimTick(prevLite, nextLite);
      }

      const queuedReadyState = entry.waveReadyVotes && entry.waveReadyVotes.size > 0
        ? buildWaveReadyState(room, entry.game, entry.waveReadyVotes)
        : null;
      if (queuedReadyState && queuedReadyState.currentWaveComplete && queuedReadyState.allReady) {
        const startedFromQueuedReady = !!(simMl && typeof simMl.startNextWaveNow === "function" && simMl.startNextWaveNow(entry.game));
        if (startedFromQueuedReady)
          resetWaveReadyState(roomId, room, entry);
      }

      localTick += 1;

      for (const lane of entry.game.lanes) {
        if (lane.eliminated && !entry.eliminatedNotified.has(lane.laneIndex)) {
          eliminationStateChanged = true;
          entry.eliminatedNotified.add(lane.laneIndex);
          const eliminatedSid = room.players.find((sid) => room.laneBySocketId.get(sid) === lane.laneIndex);
          const aiEntry = (room.aiPlayers || []).find((ai) => ai.laneIndex === lane.laneIndex);
          const displayName = eliminatedSid
            ? room.playerNames.get(eliminatedSid) || "Player"
            : aiEntry
              ? (aiEntry.displayName || `CPU (${capFirst(aiEntry.difficulty)})`)
              : "Player";
          io.to(roomId).emit("ml_player_eliminated", { laneIndex: lane.laneIndex, displayName });
          if (eliminatedSid) {
            io.to(eliminatedSid).emit("ml_spectator_join", { laneIndex: lane.laneIndex });
          }
        }
      }

      // Drain pending round events from sim
      const pendingEvents = entry.game._pendingEvents || [];
      entry.game._pendingEvents = [];
      for (const evt of pendingEvents) {
        io.to(roomId).emit(evt.type, evt);
      }
      const waveStarted = pendingEvents.some((evt) => evt && evt.type === "ml_wave_start");
      if (waveStarted || eliminationStateChanged)
        resetWaveReadyState(roomId, room, entry);

      // Drain combat log events (buffered by combatLog.js when COMBAT_LOG=true)
      if (entry.game._combatLog && entry.game._combatLog.length > 0) {
        if (!entry.combatEvents) entry.combatEvents = [];
        entry.combatEvents.push(...entry.game._combatLog);
        entry.game._combatLog = [];
      }

      if (localTick % snapshotEveryNTicks === 0) {
        snapshotBuilt = true;
        const snapshotBuildStartedAt = performance.now();
        const snapshot = simMl.createMLSnapshot(entry.game);
        snapshotBuildMs = performance.now() - snapshotBuildStartedAt;
        io.to(roomId).emit("ml_state_snapshot", snapshot);
      }

      logTickPerfIfNeeded(entry, roomId, code, localTick, {
        callbackMs: performance.now() - tickStartedAt,
        simTickMs,
        snapshotBuildMs,
        schedulerDelayMs,
        snapshotBuilt,
      });

      if (entry.game.phase === "ended") {
        const balanceData = simMl && typeof simMl.finalizeMatchBalance === "function"
          ? simMl.finalizeMatchBalance(entry.game, {
            finalResult: "completed",
            defeat: entry.game.finalGameOverReason === "all_town_cores_destroyed",
          })
          : { waveReports: [], summary: null, flags: [] };
        const finalPayload = buildOutcomePayload(room, entry);
        log.info("[ml-game] final game over", {
          roomId,
          code,
          winnerLaneIndex: finalPayload.winnerLaneIndex,
          matchState: finalPayload.matchState,
          continuedIntoSurvival: finalPayload.continuedIntoSurvival,
          finalRound: finalPayload.finalRound,
          survivalDuration: finalPayload.survivalDuration,
          finalGameOverReason: finalPayload.finalGameOverReason,
          finalGameOverDebug: finalPayload.finalGameOverDebug,
        });
        io.to(roomId).emit("ml_game_over", finalPayload);
        const winnerLane = finalPayload.winnerLaneIndex;
        const hasWinnerLane = Number.isInteger(winnerLane) && winnerLane >= 0;
        const snapshots = entry.playerSnapshot.map((player) => ({
          ...player,
          result: hasWinnerLane && player.laneIndex === winnerLane ? "win" : "loss",
        }));

        logMatchEnd(entry.matchIdPromise, winnerLane, snapshots, {
          mode: entry.dbMode,
          partyASize: entry.partyASize,
          combatLog: entry.combatEvents,
          waveStats: balanceData.waveReports,
          balanceSummary: balanceData.summary,
          balanceFlags: balanceData.flags,
        })
          .then(({ ratingUpdates }) => {
            for (const update of ratingUpdates || []) {
              const sid = socketByPlayerId.get(update.playerId);
              if (!sid) continue;
              const totalMatches = (update.wins || 0) + (update.losses || 0);
              const isPlacement = totalMatches < 10;
              io.to(sid).emit("rating_update", {
                mode: entry.dbMode,
                newRating: Math.round(update.newRating),
                delta: Math.round(update.delta),
                isPlacement,
                placementProgress: isPlacement ? `${totalMatches}/10` : null,
              });
            }
          })
          .catch((err) => log.error("[match] finalization error:", { err: err.message }));

        stopMLGame(roomId, code);
      }
    }, simMl.TICK_MS);

    gameEntry.tickHandle = tickHandle;
    emitWaveReadyState(roomId, room, gameEntry);
    log.info(`[ml-game] started ${roomId}`);
  }

  function stopMLGame(roomId, code) {
    const entry = gamesByRoomId.get(roomId);
    if (!entry) return;
    clearInterval(entry.tickHandle);
    if (entry.botController && typeof entry.botController.stop === "function") {
      try {
        entry.botController.stop();
      } catch {
        // Ignore AI shutdown failures during cleanup.
      }
    }
    gamesByRoomId.delete(roomId);
    log.info(`[ml-game] stopped ${roomId}`);
    const handle = setTimeout(() => {
      mlRoomsByCode.delete(code);
      log.info(`[ml-room] cleaned up ${code}`);
    }, 60_000);
    const room = mlRoomsByCode.get(code);
    if (room) room._cleanupHandle = handle;
  }
  return {
    applyMultilaneAction,
    attachTakeoverBot,
    buildAvailableUnits,
    buildRematchStatus,
    capFirst,
    countMlTeams,
    createMLRoom,
    detachTakeoverBot,
    ffaTeamForLane,
    handlePostWinDecision,
    mlLobbyUpdatePayload,
    pickBalancedMlTeam,
    submitWaveReadyVote,
    startMLGame,
    stopMLGame,
    validateMlTeamSetup,
  };
}

module.exports = { createMultilaneRuntime };
