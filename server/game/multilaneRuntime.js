"use strict";

async function loadDefaultWaveConfig(db) {
  if (!db) return [];
  try {
    const cr = await db.query(`SELECT id FROM ml_wave_configs WHERE is_default=TRUE LIMIT 1`);
    if (!cr.rows[0]) return [];
    const wr = await db.query(
      `SELECT wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult
       FROM ml_waves WHERE config_id=$1 ORDER BY wave_number`,
      [cr.rows[0].id]
    );
    return wr.rows;
  } catch {
    return [];
  }
}

function createMultilaneRuntime({
  aiRuntime,
  db,
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
  ratingService,
  resolveLoadout,
  sanitizeDisplayName,
  seasonService,
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
      playerTeamsBySocketId: new Map([[hostSocketId, "red"]]),
      readySet: new Set(),
      playerNames: new Map([[hostSocketId, sanitizeDisplayName(displayName)]]),
      createdAt: Date.now(),
      mode: "multilane",
      hostSocketId,
      aiPlayers: [],
      settings: normalizeMatchSettings(settings),
    };
    mlRoomsByCode.set(code, room);
    return { code, roomId };
  }

  function countMlTeams(room) {
    const counts = { red: 0, blue: 0 };
    for (const sid of room.players || []) {
      const team = room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red";
      counts[team] += 1;
    }
    for (const ai of room.aiPlayers || []) {
      const team = ai.team === "blue" ? "blue" : "red";
      counts[team] += 1;
    }
    return counts;
  }

  function pickBalancedMlTeam(room) {
    const counts = countMlTeams(room);
    return counts.red <= counts.blue ? "red" : "blue";
  }

  const FFA_TEAMS = ["red", "blue", "green", "purple"];

  function ffaTeamForLane(laneIndex) {
    return FFA_TEAMS[laneIndex] || `p${laneIndex}`;
  }

  function validateMlTeamSetup(room) {
    if (room.pvpMode === "ffa") return { ok: true };
    const total = (room.players?.length || 0) + (room.aiPlayers?.length || 0);
    if (total !== 4) return { ok: true };
    const counts = countMlTeams(room);
    if (counts.red !== 2 || counts.blue !== 2) {
      return { ok: false, reason: "For 4-player matches, assign exactly 2 Red and 2 Blue before starting." };
    }
    return { ok: true };
  }

  function capFirst(value) {
    return String(value || "").charAt(0).toUpperCase() + String(value || "").slice(1);
  }

  function mlLobbyUpdatePayload(code, room) {
    const humanPlayers = room.players.map((sid) => ({
      laneIndex: room.laneBySocketId.get(sid),
      displayName: room.playerNames.get(sid) || "Player",
      ready: room.readySet.has(sid),
      isAI: false,
      team: room.playerTeamsBySocketId?.get(sid) || "red",
    }));
    const aiPlayers = (room.aiPlayers || []).map((ai) => ({
      laneIndex: ai.laneIndex,
      displayName: `CPU (${capFirst(ai.difficulty)})`,
      ready: true,
      isAI: true,
      difficulty: ai.difficulty,
      team: ai.team || "red",
    }));
    const players = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);
    return {
      code,
      players,
      hostLaneIndex: 0,
      settings: normalizeMatchSettings(room.settings),
      pvpMode: room.pvpMode || "2v2",
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

  function resolveDefaultMultilaneTarget(game, sourceLaneIndex) {
    if (!game || !Array.isArray(game.lanes) || game.lanes.length <= 1) return null;
    const sourceLane = game.lanes[sourceLaneIndex];
    if (!sourceLane) return null;
    const total = Math.min(Number(game.playerCount) || game.lanes.length, game.lanes.length);
    for (let step = 1; step < total; step += 1) {
      const idx = (sourceLaneIndex + step) % total;
      const lane = game.lanes[idx];
      if (!lane || lane.eliminated) continue;
      if (lane.team === sourceLane.team) continue;
      return idx;
    }
    return null;
  }

  function applyMultilaneAction(entry, laneIndex, action) {
    const result = simMl.applyMLAction(entry.game, laneIndex, action);
    const tracker = entry.botController && entry.botController.runtimeTracker;
    if (tracker) {
      if (!result.ok) {
        tracker.recordInvalidAction(laneIndex);
      } else if (action && action.type === "spawn_unit") {
        const requestedTarget = Number(action.data && action.data.targetLaneIndex);
        const targetLaneIndex = Number.isInteger(requestedTarget)
          ? requestedTarget
          : resolveDefaultMultilaneTarget(entry.game, laneIndex);
        tracker.recordSendEvent(
          laneIndex,
          targetLaneIndex,
          action.data && action.data.unitType,
          1,
          1,
          entry.game.tick
        );
      }
    }
    return result;
  }

  function _bucketToDbMode(queueMode) {
    if (!queueMode) return 'multilane';
    if (queueMode === 'ranked_2v2') return '2v2_ranked'; // legacy
    const parts = queueMode.split(':');
    if (parts.length !== 3 || parts[0] !== 'line_wars') return 'multilane';
    const [, matchFormat, rankedFlag] = parts;
    const isRanked = rankedFlag === '1';
    if (matchFormat === '2v2') return isRanked ? '2v2_ranked' : '2v2_casual';
    if (matchFormat === '1v1') return isRanked ? '1v1_ranked' : '1v1_casual';
    if (matchFormat === 'ffa') return isRanked ? 'ffa_ranked' : 'ffa_casual';
    return 'multilane';
  }

  function startMLGame(roomId, code) {
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
        team: room.playerTeamsBySocketId?.get(sid) || "red",
      })),
      ...aiList.map((ai) => ({
        laneIndex: ai.laneIndex,
        displayName: `CPU (${capFirst(ai.difficulty)})`,
        isAI: true,
        team: ai.team || "red",
      })),
    ];

    const laneTeams = new Array(playerCount).fill("red");
    for (const assignment of laneAssignments) {
      if (
        Number.isInteger(assignment.laneIndex) &&
        assignment.laneIndex >= 0 &&
        assignment.laneIndex < playerCount
      ) {
        laneTeams[assignment.laneIndex] = assignment.team || "red";
      }
    }

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
    });

    // Load wave config from DB (async; game starts with empty config then gets populated)
    loadDefaultWaveConfig(db).then(waves => {
      game.waveConfig = waves;
      log.info(`[ml-game] wave config loaded: ${waves.length} waves`, { roomId });
    }).catch(err => {
      log.error('[ml-game] failed to load wave config', { err: err.message });
    });

    io.to(roomId).emit("ml_match_ready", {
      code,
      playerCount,
      laneAssignments,
      battlefieldTopology: publicConfig.battlefieldTopology,
      aiEngine: "new_bot_controller_v1",
    });
    io.to(roomId).emit("ml_match_config", publicConfig);

    if (!room.loadoutByLane) room.loadoutByLane = [];
    Promise.all([
      ...room.players.map(async (sid) => {
        const sock = io.sockets.sockets.get(sid);
        if (!sock) return;
        const laneIdx = room.laneBySocketId.get(sid);
        const loadout = await resolveLoadout(sock.playerId || null, sock.pendingLoadoutSlot, sock.pendingUnitTypeIds);
        sock.pendingLoadoutSlot = null;
        sock.pendingUnitTypeIds = null;
        room.loadoutByLane[laneIdx] = loadout;
        if (game.lanes[laneIdx]) {
          const keys = loadout.map((ut) => ut.key);
          game.lanes[laneIdx].autosend.loadoutKeys = keys;
          game.lanes[laneIdx].autosend.enabledUnits = Object.fromEntries(keys.map(k => [k, false]));
          // Load equipped skins for this player
          if (db && sock.playerId) {
            try {
              const skinRows = await db.query(
                'SELECT unit_type, skin_key FROM player_skin_equip WHERE player_id = $1',
                [sock.playerId]
              );
              game.lanes[laneIdx].equippedSkins = Object.fromEntries(
                skinRows.rows.map(r => [r.unit_type, r.skin_key])
              );
            } catch (err) {
              log.error('[skins] failed to load equipped skins', { err: err.message });
            }
          }
        }
        sock.emit("ml_match_config", { loadout });
      }),
      ...aiList.map(async (ai) => {
        const loadout = await resolveLoadout(null, null, null);
        const laneIdx = ai.laneIndex;
        room.loadoutByLane[laneIdx] = loadout;
        if (game.lanes[laneIdx]) {
          const keys = loadout.map((ut) => ut.key);
          game.lanes[laneIdx].autosend.loadoutKeys = keys;
          game.lanes[laneIdx].autosend.enabledUnits = Object.fromEntries(keys.map(k => [k, false]));
        }
      }),
    ]).catch((err) => log.error("[loadout] resolve error", { err: err.message }));

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

    const playerSnapshot = room.players
      .map((sid) => {
        const sock = io.sockets.sockets.get(sid);
        return sock?.playerId ? { playerId: sock.playerId, laneIndex: room.laneBySocketId.get(sid) } : null;
      })
      .filter(Boolean);

    const dbMode = _bucketToDbMode(room.queueMode);
    const matchIdPromise = logMatchStart(roomId, dbMode);
    const eliminatedNotified = new Set();
    let localTick = 0;
    const snapshotEveryNTicks = 2; // 10 Hz at 20 Hz tick rate (was 10 = 2 Hz, too laggy)

    const tickHandle = setInterval(() => {
      const entry = gamesByRoomId.get(roomId);
      if (!entry) return;

      const prevLite = entry.botController ? aiRuntime.captureStateLite(entry.game) : null;
      if (entry.botController) entry.botController.onBeforeSimTick(entry.game);
      simMl.mlTick(entry.game);
      if (entry.botController && prevLite) {
        const nextLite = aiRuntime.captureStateLite(entry.game);
        entry.botController.onAfterSimTick(prevLite, nextLite);
      }

      localTick += 1;

      for (const lane of entry.game.lanes) {
        if (lane.eliminated && !entry.eliminatedNotified.has(lane.laneIndex)) {
          entry.eliminatedNotified.add(lane.laneIndex);
          const eliminatedSid = room.players.find((sid) => room.laneBySocketId.get(sid) === lane.laneIndex);
          const aiEntry = (room.aiPlayers || []).find((ai) => ai.laneIndex === lane.laneIndex);
          const displayName = eliminatedSid
            ? room.playerNames.get(eliminatedSid) || "Player"
            : aiEntry
              ? `CPU (${capFirst(aiEntry.difficulty)})`
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

      if (localTick % snapshotEveryNTicks === 0) {
        io.to(roomId).emit("ml_state_snapshot", simMl.createMLSnapshot(entry.game));
      }

      if (entry.game.phase === "ended") {
        const winnerLane = entry.game.winner;
        let winnerName = "Unknown";
        if (winnerLane !== null && winnerLane !== undefined) {
          const winnerSid = room.players.find((sid) => room.laneBySocketId.get(sid) === winnerLane);
          if (winnerSid) {
            winnerName = room.playerNames.get(winnerSid) || "Player";
          } else {
            const winnerAi = (room.aiPlayers || []).find((ai) => ai.laneIndex === winnerLane);
            if (winnerAi) winnerName = `CPU (${capFirst(winnerAi.difficulty)})`;
          }
        }

        io.to(roomId).emit("ml_game_over", { winnerLaneIndex: winnerLane, winnerName });
        const snapshots = entry.playerSnapshot.map((player) => ({
          ...player,
          result: winnerLane === null
            ? "draw"
            : entry.game.lanes[player.laneIndex]?.team === entry.game.lanes[winnerLane]?.team
              ? "win"
              : "loss",
        }));

        const endPromise = logMatchEnd(entry.matchIdPromise, winnerLane, snapshots);
        if (ratingService && db && entry.dbMode && entry.dbMode.endsWith("_ranked")) {
          Promise.all([endPromise, entry.matchIdPromise])
            .then(([, matchId]) =>
              ratingService.updateRatings(db, matchId, entry.dbMode, snapshots, entry.partyASize)
            )
            .then((updates) => {
              for (const update of updates) {
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

              if (seasonService && updates.length > 0) {
                seasonService
                  .getActiveSeason(db)
                  .then((season) => {
                    if (!season) return;
                    for (const update of updates) {
                      seasonService.updatePeakRating(db, season.id, update.playerId, entry.dbMode, update.newRating);
                    }
                  })
                  .catch(() => {});
              }
            })
            .catch((err) => log.error("[rating] error:", { err: err.message }));
        }

        stopMLGame(roomId, code);
      }
    }, simMl.TICK_MS);

    gamesByRoomId.set(roomId, {
      game,
      tickHandle,
      mode: "multilane",
      snapshotEveryNTicks,
      eliminatedNotified,
      botController,
      matchIdPromise,
      playerSnapshot,
      dbMode,
      partyASize: room.partyASize || 1,
    });

    for (const sid of room.players) {
      const sock = io.sockets.sockets.get(sid);
      if (!sock) continue;
      const laneIdx = room.laneBySocketId.get(sid);
      const token = issueReconnectToken(roomId, code, "multilane", String(laneIdx), sock);
      sock.emit("reconnect_token", { token, gracePeriodMs: RECONNECT_GRACE_MS });
    }

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
    capFirst,
    countMlTeams,
    createMLRoom,
    ffaTeamForLane,
    mlLobbyUpdatePayload,
    pickBalancedMlTeam,
    startMLGame,
    stopMLGame,
    validateMlTeamSetup,
  };
}

module.exports = { createMultilaneRuntime };
