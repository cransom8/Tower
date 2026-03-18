"use strict";

function createSurvivalRuntime({
  db,
  generateCode,
  gamesByRoomId,
  io,
  log,
  logMatchEnd,
  logMatchStart,
  mlRoomsByCode,
  sanitizeDisplayName,
  simMl,
  simSurvival,
  stringToSeed,
  survivalRoomsByCode,
}) {
  async function loadActiveWaveSet() {
    if (!db) return null;
    try {
      const setRes = await db.query("SELECT * FROM survival_wave_sets WHERE is_active = true LIMIT 1");
      if (!setRes.rows[0]) return null;
      const waveSet = setRes.rows[0];
      const wavesRes = await db.query(
        `SELECT w.*, COALESCE(
           json_agg(sg ORDER BY sg.start_delay_ms, sg.id) FILTER (WHERE sg.id IS NOT NULL), '[]'
         ) AS spawn_groups
         FROM survival_waves w
         LEFT JOIN survival_spawn_groups sg ON sg.wave_id = w.id
         WHERE w.wave_set_id = $1
         GROUP BY w.id
         ORDER BY w.wave_number`,
        [waveSet.id]
      );
      waveSet.waves = wavesRes.rows;
      return waveSet;
    } catch (err) {
      log.error("[survival] loadActiveWaveSet error", { err: err.message });
      return null;
    }
  }

  function createSurvivalRoom(hostSocketId, displayName, coopMode) {
    let code;
    do code = generateCode(6);
    while (mlRoomsByCode.has(code) || survivalRoomsByCode.has(code));

    const roomId = `svroom_${code}`;
    const room = {
      roomId,
      players: [hostSocketId],
      laneBySocketId: new Map([[hostSocketId, 0]]),
      playerNames: new Map([[hostSocketId, sanitizeDisplayName(displayName)]]),
      readySet: new Set(),
      hostSocketId,
      coopMode: !!coopMode,
      createdAt: Date.now(),
      mode: "survival",
    };
    survivalRoomsByCode.set(code, room);
    return { code, roomId };
  }

  async function startSurvivalGame(roomId, code) {
    if (gamesByRoomId.has(roomId)) return;
    const room = survivalRoomsByCode.get(code);
    if (!room) return;

    let waveSet = await loadActiveWaveSet();
    if (!waveSet) {
      waveSet = {
        id: null,
        name: "Default",
        description: "",
        auto_scale: true,
        starting_gold: 150,
        starting_lives: 20,
        waves: [],
      };
    }

    const playerCount = room.players.length;
    const survivalSeedNum = stringToSeed(`${roomId}:${code}:survival`);
    const game = simSurvival.createSurvivalGame(playerCount, waveSet, survivalSeedNum);
    const playerSnapshot = room.players
      .map((sid) => {
        const sock = io.sockets.sockets.get(sid);
        return sock?.playerId ? { playerId: sock.playerId, laneIndex: room.laneBySocketId.get(sid) } : null;
      })
      .filter(Boolean);

    const matchIdPromise = logMatchStart(roomId, "survival");
    const laneAssignments = room.players.map((sid) => ({
      laneIndex: room.laneBySocketId.get(sid),
      displayName: room.playerNames.get(sid) || "Player",
    }));

    io.to(roomId).emit("survival_match_ready", {
      code,
      playerCount,
      laneAssignments,
      waveSetName: waveSet.name,
    });
    io.to(roomId).emit("ml_match_config", simMl.createMLPublicConfig({}));

    let localTick = 0;
    const snapshotEveryNTicks = 2; // 10 Hz at 20 Hz tick rate (was 10 = 2 Hz, too laggy)

    const tickHandle = setInterval(() => {
      const entry = gamesByRoomId.get(roomId);
      if (!entry) return;

      simSurvival.tickSurvival(entry.game);
      localTick += 1;

      if (entry.game.wavePhase === "SPAWNING" && entry._lastWavePhase !== "SPAWNING") {
        const waveConfig = (entry.game.config?.waves || []).find((wave) => wave.wave_number === entry.game.waveNumber);
        io.to(roomId).emit("survival_wave_start", {
          waveNumber: entry.game.waveNumber,
          isBoss: waveConfig?.is_boss || false,
          isRush: waveConfig?.is_rush || false,
          isElite: waveConfig?.is_elite || false,
        });
      }

      entry._lastWavePhase = entry.game.wavePhase;

      if (localTick % snapshotEveryNTicks === 0) {
        io.to(roomId).emit("survival_state_snapshot", simSurvival.createSurvivalSnapshot(entry.game));
      }

      if (entry.game.phase === "ended") {
        const snap = simSurvival.createSurvivalSnapshot(entry.game);
        io.to(roomId).emit("survival_ended", {
          wavesCleared: snap.totalWavesCleared,
          killCount: snap.killCount,
          timeSurvived: snap.timeSurvived,
          goldEarned: snap.goldEarned,
          wavePhase: snap.wavePhase,
        });
        logMatchEnd(entry.matchIdPromise, null, entry.playerSnapshot);
        stopSurvivalGame(roomId, code);
      }
    }, simSurvival.TICK_MS);

    gamesByRoomId.set(roomId, {
      game,
      tickHandle,
      mode: "survival",
      snapshotEveryNTicks,
      matchIdPromise,
      playerSnapshot,
      _lastWavePhase: "PREP",
    });

    log.info("[survival] game started", { roomId, code, playerCount });
  }

  function stopSurvivalGame(roomId, code) {
    const entry = gamesByRoomId.get(roomId);
    if (!entry) return;
    clearInterval(entry.tickHandle);
    gamesByRoomId.delete(roomId);
    log.info("[survival] game stopped", { roomId });
    setTimeout(() => {
      survivalRoomsByCode.delete(code);
      log.info("[survival] room cleaned up", { code });
    }, 60_000);
  }

  return {
    createSurvivalRoom,
    loadActiveWaveSet,
    startSurvivalGame,
    stopSurvivalGame,
  };
}

module.exports = { createSurvivalRuntime };
