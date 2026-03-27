"use strict";

const crypto = require("crypto");
const {
  getAvailableRaces,
  getDefaultRaceId,
  isValidRaceId,
  normalizeRaceId,
} = require("../game/raceProgressionCatalog");

function registerSocketHandlers({
  attachTakeoverBot,
  authService,
  buildAvailableUnits,
  buildRematchStatus,
  checkActionRateLimit,
  checkLobbyRateLimit,
  createMLRoom,
  detachTakeoverBot,
  db,
  disconnectGrace,
  ffaTeamForLane,
  gamesByRoomId,
  generateCode,
  handlePostWinDecision,
  hasValidInlineLoadoutIds,
  io,
  log,
  matchmaker,
  mlLobbyUpdatePayload,
  mlRoomsByCode,
  normalizeMatchSettings,
  pickBalancedMlTeam,
  partiesByCode,
  partiesById,
  partyByPlayerId,
  requireAuthSocket,
  sanitizeDisplayName,
  sessionBySocketId,
  simMl,
  socketByPlayerId,
  startMLGame,
  stopMLGame,
  submitWaveReadyVote,
  validateLoadoutSelection,
  validateMlTeamSetup,
  verifyReconnectToken,
  applyMultilaneAction,
  RECONNECT_GRACE_MS,
  isEnabled,
}) {
  const VALID_AI_DIFFICULTIES = ["easy", "medium", "hard", "insane"];
  const DEFAULT_TAKEOVER_DIFFICULTY = "hard";

  function getPartyForPlayer(playerId) {
    const partyId = partyByPlayerId.get(playerId);
    if (!partyId) return null;
    return partiesById.get(partyId) || null;
  }

  function getQueueConflictMessage(playerId) {
    const party = getPartyForPlayer(playerId);
    if (!party) return null;
    if (party.status === "queued") return "Leave public queue before joining a private lobby.";
    if (party.status === "in_match") return "You are already in a match.";
    return null;
  }

  function canFinalizeQueueJoin(partyId, leaderId) {
    const latestParty = partiesById.get(partyId);
    if (!latestParty) return { ok: false, reason: "Party no longer exists." };
    if (latestParty.leaderId !== leaderId) return { ok: false, reason: "Party leader changed." };
    if (latestParty.status !== "idle") return { ok: false, reason: "Party is no longer available for queue." };
    if (matchmaker.getLobbyForSocket(socketByPlayerId.get(leaderId) || "")) {
      return { ok: false, reason: "Leave the private lobby before entering public queue." };
    }
    return { ok: true, party: latestParty };
  }

  function restoreQueueTickets(teams, fallbackBucketKey = null) {
    const restoredParties = new Set();
    teams.flat().forEach((ticket) => {
      if (restoredParties.has(ticket.partyId)) return;
      restoredParties.add(ticket.partyId);
      const party = partiesById.get(ticket.partyId);
      if (!party || party.status !== "queued") return;
      matchmaker.addToQueue(ticket.partyId, {
        ...ticket,
        partySize: party.members.length,
        region: party.region || ticket.region || "global",
        queueEnteredAt: party.queueEnteredAt || ticket.queueEnteredAt || Date.now(),
      });
    });

    restoredParties.forEach((partyId) => {
      const party = partiesById.get(partyId);
      if (!party) return;
      const mode = party.queueMode || fallbackBucketKey || matchmaker.makeBucketKey("line_wars", "ffa", false);
      const queueSize = matchmaker.getQueuePlayerCount(mode, partiesById);
      io.to(`party:${partyId}`).emit("queue_status", {
        status: "queued",
        mode,
        elapsed: Math.floor(Math.max(0, Date.now() - (party.queueEnteredAt || Date.now())) / 1000),
        queueSize,
      });
      io.to(`party:${partyId}`).emit("party_update", { party });
    });
  }

  async function getFriendsList(playerId) {
    if (!db) return [];
    const result = await db.query(
      `SELECT
         CASE WHEN f.requester_id = $1 THEN f.addressee_id ELSE f.requester_id END AS friend_id,
         CASE WHEN f.requester_id = $1 THEN pa.display_name ELSE pr.display_name END AS display_name,
         CASE
           WHEN f.status = 'accepted' THEN 'accepted'
           WHEN f.requester_id = $1   THEN 'pending_sent'
           ELSE 'pending_received'
         END AS status
       FROM friends f
       JOIN players pr ON pr.id = f.requester_id
       JOIN players pa ON pa.id = f.addressee_id
       WHERE f.requester_id = $1 OR f.addressee_id = $1`,
      [playerId]
    );
    return result.rows.map((row) => ({
      playerId: row.friend_id,
      displayName: row.display_name,
      status: row.status,
      online: socketByPlayerId.has(row.friend_id),
    }));
  }

  async function pushFriendsList(playerId) {
    const sid = socketByPlayerId.get(playerId);
    if (!sid) return;
    const sock = io.sockets.sockets.get(sid);
    if (!sock) return;
    sock.emit("friends_list", await getFriendsList(playerId));
  }

  function generatePartyCode() {
    return generateCode(6);
  }

  function normalizeMatchSettingsOrReply(socket, settings, errorEvent = "error_message") {
    try {
      return normalizeMatchSettings(settings || {});
    } catch (err) {
      const message = err && err.message ? err.message : "Invalid match settings.";
      socket.emit(errorEvent, { message });
      return null;
    }
  }

  function _leaveParty(socket, partyId) {
    const party = partiesById.get(partyId);
    if (!party) return;

    party.members = party.members.filter((member) => member.playerId !== socket.playerId);
    partyByPlayerId.delete(socket.playerId);
    socket.leave(`party:${partyId}`);

    if (party.members.length === 0) {
      if (party.status === "queued") matchmaker.removeFromQueue(partyId);
      partiesById.delete(partyId);
      partiesByCode.delete(party.code);
      log.info(`[party] disbanded ${partyId}`);
      return;
    }

    if (party.leaderId === socket.playerId) {
      const newLeader = party.members.find((member) => socketByPlayerId.has(member.playerId));
      if (newLeader) {
        party.leaderId = newLeader.playerId;
      } else {
        if (party.status === "queued") matchmaker.removeFromQueue(partyId);
        partiesById.delete(partyId);
        partiesByCode.delete(party.code);
        log.info(`[party] disbanded ${partyId} (no connected leader candidate)`);
        return;
      }
    }

    if (party.status === "queued" && party.members.length === 0) {
      matchmaker.removeFromQueue(partyId);
      party.status = "idle";
      party.queueMode = null;
      party.queueEnteredAt = null;
    }

    io.to(`party:${partyId}`).emit("party_update", { party });
    if (party.status === "queued") {
      const elapsed = Math.floor((Date.now() - party.queueEnteredAt) / 1000);
      io.to(`party:${partyId}`).emit("queue_status", { status: "queued", mode: party.queueMode, elapsed });
    }
    log.info(`[party] ${socket.playerId} left ${partyId}`);
  }

  function onMatchFoundNew(teams, bucketKey) {
    const [gameType, matchFormat] = bucketKey.split(":");
    log.info("[match] new-format found", { bucketKey, teams: teams.length });

    const allSocketIds = [];
    let missingQueuedMember = false;

    teams.forEach((team) => {
      team.forEach((ticket) => {
        const party = partiesById.get(ticket.partyId);
        if (!party) return;
        party.members.forEach((member) => {
          const sid = socketByPlayerId.get(member.playerId) || member.socketId;
          if (!sid) {
            missingQueuedMember = true;
            return;
          }
          allSocketIds.push({ sid, name: member.displayName, partyId: ticket.partyId });
        });
      });
    });

    if (missingQueuedMember) {
      log.warn("[match] aborted launch due to missing queued member", { bucketKey });
      restoreQueueTickets(teams, bucketKey);
      return;
    }

    if (allSocketIds.length === 0) return;

    if (gameType === "line_wars") {
      const host = allSocketIds[0];
      const { code, roomId } = createMLRoom(host.sid, sanitizeDisplayName(host.name), {});
      const room = mlRoomsByCode.get(code);
      room.queueMode = bucketKey;
      room.partyASize = teams[0].reduce((count, ticket) => {
        const party = partiesById.get(ticket.partyId);
        return count + (party ? party.members.length : 1);
      }, 0);
      room.pvpMode = "ffa";

      sessionBySocketId.set(host.sid, { code, roomId, laneIndex: 0, mode: "multilane" });
      room.playerTeamsBySocketId.set(host.sid, ffaTeamForLane(0));
      const hostSock = io.sockets.sockets.get(host.sid);
      if (hostSock) hostSock.join(roomId);

      for (let i = 1; i < allSocketIds.length; i += 1) {
        const { sid, name } = allSocketIds[i];
        room.players.push(sid);
        room.laneBySocketId.set(sid, i);
        room.playerNames.set(sid, sanitizeDisplayName(name));
        room.playerTeamsBySocketId.set(sid, ffaTeamForLane(i));
        sessionBySocketId.set(sid, { code, roomId, laneIndex: i, mode: "multilane" });
        const sock = io.sockets.sockets.get(sid);
        if (sock) sock.join(roomId);
      }

      io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

      allSocketIds.forEach(({ sid }, laneIndex) => {
        const enemies = allSocketIds
          .filter((_, idx) => idx !== laneIndex)
          .map((entry) => entry.name);
        io.to(sid).emit("match_found", {
          roomCode: code,
          laneIndex,
          teammates: [allSocketIds[laneIndex].name],
          opponents: enemies,
          matchFormat: "ffa",
        });
      });
    }

    const seenParties = new Set();
    teams.flat().forEach((ticket) => {
      if (seenParties.has(ticket.partyId)) return;
      seenParties.add(ticket.partyId);
      const party = partiesById.get(ticket.partyId);
      if (!party) return;
      party.status = "in_match";
      party.queueMode = null;
      party.queueEnteredAt = null;
      io.to(`party:${party.id}`).emit("party_update", { party });
    });
  }

  function onMatchFound(partyA, partyB, mode, teams, bucketKey) {
    if (partyA === null && teams) {
      onMatchFoundNew(teams, bucketKey);
      return;
    }

    log.info(`[match] found: party ${partyA.id} vs party ${partyB.id} mode=${mode}`);
    const allMembers = [...partyA.members, ...partyB.members];
    if (allMembers.length === 0) return;

    const resolvedMembers = allMembers.map((member) => ({
      member,
      sid: socketByPlayerId.get(member.playerId) || member.socketId,
    }));
    if (resolvedMembers.some(({ sid }) => !sid)) {
      log.warn("[match] aborted legacy launch due to missing queued member", {
        mode,
        partyA: partyA.id,
        partyB: partyB.id,
      });
      restoreQueueTickets(teams || [], bucketKey || partyA.queueMode || partyB.queueMode || mode);
      return;
    }

    const hostSocketId = resolvedMembers[0].sid;
    const hostDisplayName = sanitizeDisplayName(resolvedMembers[0].member.displayName);
    const { code, roomId } = createMLRoom(hostSocketId, hostDisplayName, {});
    const room = mlRoomsByCode.get(code);
    room.queueMode = mode;
    room.partyASize = partyA.members.length;
    room.pvpMode = "ffa";

    sessionBySocketId.set(hostSocketId, { code, roomId, laneIndex: 0, mode: "multilane" });
    room.playerTeamsBySocketId.set(hostSocketId, ffaTeamForLane(0));
    const hostSocket = io.sockets.sockets.get(hostSocketId);
    if (hostSocket) hostSocket.join(roomId);

    let laneIndex = 1;
    for (let i = 1; i < resolvedMembers.length; i += 1) {
      const { member, sid } = resolvedMembers[i];
      room.players.push(sid);
      room.laneBySocketId.set(sid, laneIndex);
      room.playerNames.set(sid, sanitizeDisplayName(member.displayName));
      room.playerTeamsBySocketId.set(sid, ffaTeamForLane(laneIndex));
      sessionBySocketId.set(sid, { code, roomId, laneIndex, mode: "multilane" });
      const memberSocket = io.sockets.sockets.get(sid);
      if (memberSocket) memberSocket.join(roomId);
      laneIndex += 1;
    }

    io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

    const allNames = resolvedMembers.map(({ member }) => member.displayName);

    resolvedMembers.forEach(({ member, sid }, idx) => {
      if (!sid) return;
      io.to(sid).emit("match_found", {
        roomCode: code,
        laneIndex: idx,
        teammates: [member.displayName],
        opponents: allNames.filter((name, nameIdx) => nameIdx !== idx),
        matchFormat: "ffa",
      });
    });

    partyA.status = "in_match";
    partyA.queueMode = null;
    partyA.queueEnteredAt = null;
    partyB.status = "in_match";
    partyB.queueMode = null;
    partyB.queueEnteredAt = null;

    io.to(`party:${partyA.id}`).emit("party_update", { party: partyA });
    io.to(`party:${partyB.id}`).emit("party_update", { party: partyB });
  }

  io.on("connection", (socket) => {
    log.info("socket connected");
    socket.guestId = crypto.randomUUID();

    if (socket.playerId) {
      socketByPlayerId.set(socket.playerId, socket.id);
      if (db) {
        getFriendsList(socket.playerId)
          .then((friends) => {
            socket.emit("friends_list", friends);
            for (const friend of friends) {
              if (friend.status !== "accepted") continue;
              const sid = socketByPlayerId.get(friend.playerId);
              if (!sid) continue;
              const friendSock = io.sockets.sockets.get(sid);
              if (friendSock) {
                friendSock.emit("friend_online", {
                  playerId: socket.playerId,
                  displayName: socket.playerDisplayName,
                });
              }
            }
          })
          .catch(() => {});
      }
    }

    socket.on("authenticate", ({ token } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
      if (!token) return;
      try {
        const payload = authService.verifyAccessToken(token);
        socket.playerId = payload.sub;
        socket.playerDisplayName = payload.displayName;
        socketByPlayerId.set(socket.playerId, socket.id);
        socket.emit("authenticated", { playerId: socket.playerId, displayName: socket.playerDisplayName });
        if (db) {
          getFriendsList(socket.playerId)
            .then((friends) => {
              socket.emit("friends_list", friends);
              for (const friend of friends) {
                if (friend.status !== "accepted") continue;
                const sid = socketByPlayerId.get(friend.playerId);
                if (!sid) continue;
                const friendSock = io.sockets.sockets.get(sid);
                if (friendSock) {
                  friendSock.emit("friend_online", {
                    playerId: socket.playerId,
                    displayName: socket.playerDisplayName,
                  });
                }
              }
            })
            .catch(() => {});
        }
      } catch {
        socket.emit("auth_error", { message: "Invalid or expired token" });
      }
    });

    socket.on("rejoin_game", ({ token } = {}) => {
      if (!token || typeof token !== "string") {
        return socket.emit("rejoin_fail", { reason: "no_token" });
      }

      let payload;
      try {
        payload = verifyReconnectToken(token);
      } catch {
        return socket.emit("rejoin_fail", { reason: "invalid_token" });
      }

      if (payload.type !== "reconnect") {
        return socket.emit("rejoin_fail", { reason: "wrong_type" });
      }

      const { roomId, code, mode, seatKey, playerId } = payload;
      if (playerId && socket.playerId && socket.playerId !== playerId) {
        return socket.emit("rejoin_fail", { reason: "identity_mismatch" });
      }

      const entry = gamesByRoomId.get(roomId);
      if (!entry) {
        return socket.emit("rejoin_fail", { reason: "game_over" });
      }

      const graceKey = `${roomId}:${seatKey}`;
      if (mode === "multilane") {
        const room = mlRoomsByCode.get(code);
        if (!room) return socket.emit("rejoin_fail", { reason: "room_gone" });
        const laneIndex = parseInt(seatKey, 10);

        for (const [sid, assignedLane] of room.laneBySocketId.entries()) {
          if (assignedLane !== laneIndex || sid === socket.id) continue;
          if (io.sockets.sockets.has(sid)) {
            return socket.emit("rejoin_fail", { reason: "seat_taken" });
          }
          const staleTeam = room.playerTeamsBySocketId?.get(sid);
          room.players = room.players.filter((playerSid) => playerSid !== sid);
          room.laneBySocketId.delete(sid);
          if (room.playerTeamsBySocketId) room.playerTeamsBySocketId.delete(sid);
          room.playerNames.delete(sid);
          room.readySet.delete(sid);
          sessionBySocketId.delete(sid);
          if (staleTeam && room.playerTeamsBySocketId) room.playerTeamsBySocketId.set(socket.id, staleTeam);
          break;
        }

        const reclaimedTakeover = detachTakeoverBot ? detachTakeoverBot(room, entry, laneIndex) : null;

        const grace = disconnectGrace.get(graceKey);
        if (grace) {
          clearTimeout(grace.graceHandle);
          disconnectGrace.delete(graceKey);
        }

        const playerName = grace?.playerName || socket.playerDisplayName || "Player";
        if (!room.players.includes(socket.id)) room.players.push(socket.id);
        room.laneBySocketId.set(socket.id, laneIndex);
        if (room.playerTeamsBySocketId && !room.playerTeamsBySocketId.has(socket.id)) {
          room.playerTeamsBySocketId.set(socket.id, ffaTeamForLane(laneIndex));
        }
        room.playerNames.set(socket.id, playerName);
        socket.join(roomId);
        sessionBySocketId.set(socket.id, { code, roomId, laneIndex, mode: "multilane" });
        socket._loadoutReady = !!room.loadoutReadyByLane?.has(laneIndex);
        socket._gameplayReady = !!room.gameplayReadyByLane?.has(laneIndex);
        socket._contentProgress = room.contentProgressByLane?.get(laneIndex) || null;

        const humanPlayers = room.players.map((sid) => ({
          laneIndex: room.laneBySocketId.get(sid),
          displayName: room.playerNames.get(sid) || "Player",
          ready: true,
          isAI: false,
          team: room.playerTeamsBySocketId?.get(sid) || ffaTeamForLane(room.laneBySocketId.get(sid) || 0),
        }));
        const aiPlayers = (room.aiPlayers || []).map((ai) => ({
          laneIndex: ai.laneIndex,
          displayName: `CPU (${String(ai.difficulty).charAt(0).toUpperCase()}${String(ai.difficulty).slice(1)})`,
          ready: true,
          isAI: true,
          team: ai.team || ffaTeamForLane(ai.laneIndex || 0),
        }));
        const laneAssignments = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);

        socket.emit("rejoin_ack", { success: true, mode: "multilane", laneIndex, code });
        socket.emit("ml_match_ready", { code, playerCount: laneAssignments.length, laneAssignments });
        const reconnectConfig = simMl.createMLPublicConfig(room.settings);
        if (room.loadoutByLane && room.loadoutByLane[laneIndex]) {
          reconnectConfig.loadout = room.loadoutByLane[laneIndex];
        }
        if (room.raceByLane && room.raceByLane[laneIndex]) {
          reconnectConfig.raceId = room.raceByLane[laneIndex];
        }
        socket.emit("ml_match_config", reconnectConfig);
        // Re-emit loadout phase if still active (player reconnected during selection window)
        if (room._loadoutPhaseResolve && room._loadoutPhaseDeadline) {
          const remainingMs = room._loadoutPhaseDeadline - Date.now();
          if (remainingMs > 0) {
            socket.emit("ml_loadout_phase_start", {
              code,
              timeoutSeconds: Math.max(1, Math.ceil(remainingMs / 1000)),
              selectionMode: (room.settings && room.settings.selectionMode) || "manual",
              defaultRaceId: getDefaultRaceId(),
              selectedRaceId: normalizeRaceId(socket.pendingRaceId) || getDefaultRaceId(),
              availableRaceIds: getAvailableRaces().map((race) => race.id),
              availableUnits: buildAvailableUnits(),
            });
          }
        }
        // Only send snapshot if ticks have started (null tickHandle = still in loadout phase)
        if (entry.tickHandle !== null) {
          socket.emit("ml_state_snapshot", simMl.createMLSnapshot(entry.game));
        }
        if (reclaimedTakeover) {
          io.to(roomId).emit("ml_ai_takeover_ended", {
            laneIndex,
            displayName: playerName,
          });
        }
        io.to(roomId).emit("player_reconnected", { laneIndex, displayName: playerName, mode: "multilane" });
        log.info(`[reconnect] ml lane ${laneIndex} in ${roomId}`);
      } else {
        socket.emit("rejoin_fail", { reason: "mode_unsupported" });
      }
    });

    socket.on("party:create", () => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      requireAuthSocket(socket, () => {
        const existingPartyId = partyByPlayerId.get(socket.playerId);
        if (existingPartyId) _leaveParty(socket, existingPartyId);

        let code;
        do code = generatePartyCode();
        while (partiesByCode.has(code));

        const partyId = crypto.randomUUID();
        const party = {
          id: partyId,
          code,
          leaderId: socket.playerId,
          members: [{ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id }],
          status: "idle",
          queueMode: null,
          queueEnteredAt: null,
          region: "global",
        };
        partiesById.set(partyId, party);
        partiesByCode.set(code, partyId);
        partyByPlayerId.set(socket.playerId, partyId);
        socket.join(`party:${partyId}`);
        io.to(`party:${partyId}`).emit("party_update", { party });
        log.info(`[party] created ${partyId} code=${code} by ${socket.playerId}`);
      });
    });

    socket.on("party:join", ({ code } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      requireAuthSocket(socket, () => {
        const normalized = String(code || "").trim().toUpperCase();
        if (normalized.length !== 6) {
          return socket.emit("error_message", { message: "Invalid party code." });
        }
        const party = partiesById.get(partiesByCode.get(normalized));
        if (!party) return socket.emit("error_message", { message: "Party not found." });
        if (party.members.length >= 4) return socket.emit("error_message", { message: "Party is full." });
        if (party.status !== "idle") return socket.emit("error_message", { message: "Party is already in queue or match." });
        if (party.members.some((member) => member.playerId === socket.playerId)) {
          return socket.emit("error_message", { message: "Already in this party." });
        }

        const existingPartyId = partyByPlayerId.get(socket.playerId);
        if (existingPartyId) _leaveParty(socket, existingPartyId);

        party.members.push({ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id });
        partyByPlayerId.set(socket.playerId, party.id);
        socket.join(`party:${party.id}`);
        io.to(`party:${party.id}`).emit("party_update", { party });
        log.info(`[party] ${socket.playerId} joined ${party.id}`);
      });
    });

    socket.on("party:leave", () => {
      requireAuthSocket(socket, () => {
        const partyId = partyByPlayerId.get(socket.playerId);
        if (!partyId) return;
        _leaveParty(socket, partyId);
      });
    });

    socket.on("friend:list", () => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address) || !db) return;
      requireAuthSocket(socket, () => {
        getFriendsList(socket.playerId)
          .then((friends) => socket.emit("friends_list", friends))
          .catch(() => {});
      });
    });

    socket.on("friend:add", ({ displayName } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
      if (!db) return socket.emit("friend_error", { message: "Friends require an account." });
      if (!displayName || typeof displayName !== "string") return;
      const name = displayName.trim().slice(0, 20);
      if (!name) return;
      requireAuthSocket(socket, () => {
        db.query("SELECT id, display_name FROM players WHERE display_name = $1 AND id <> $2 LIMIT 1", [name, socket.playerId])
          .then(async ({ rows }) => {
            if (!rows.length) {
              return socket.emit("friend_error", { message: `No player named "${name}".` });
            }
            const target = rows[0];
            await db.query(
              `INSERT INTO friends (requester_id, addressee_id)
               VALUES ($1, $2)
               ON CONFLICT DO NOTHING`,
              [socket.playerId, target.id]
            );
            const sid = socketByPlayerId.get(target.id);
            if (sid) {
              const targetSock = io.sockets.sockets.get(sid);
              if (targetSock) {
                targetSock.emit("friend_request", {
                  playerId: socket.playerId,
                  displayName: socket.playerDisplayName,
                });
              }
              pushFriendsList(target.id).catch(() => {});
            }
            pushFriendsList(socket.playerId).catch(() => {});
          })
          .catch(() => socket.emit("friend_error", { message: "Could not send request." }));
      });
    });

    socket.on("friend:accept", ({ playerId: requesterId } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address) || !db) return;
      if (!requesterId || typeof requesterId !== "string") return;
      requireAuthSocket(socket, () => {
        db.query(
          `UPDATE friends SET status = 'accepted', updated_at = NOW()
           WHERE requester_id = $1 AND addressee_id = $2 AND status = 'pending'`,
          [requesterId, socket.playerId]
        )
          .then(() => {
            const sid = socketByPlayerId.get(requesterId);
            if (sid) {
              const requesterSock = io.sockets.sockets.get(sid);
              if (requesterSock) {
                requesterSock.emit("friend_accepted", {
                  playerId: socket.playerId,
                  displayName: socket.playerDisplayName,
                });
              }
              pushFriendsList(requesterId).catch(() => {});
            }
            pushFriendsList(socket.playerId).catch(() => {});
          })
          .catch(() => {});
      });
    });

    socket.on("friend:decline", ({ playerId: otherId } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address) || !db) return;
      if (!otherId || typeof otherId !== "string") return;
      requireAuthSocket(socket, () => {
        db.query(
          `DELETE FROM friends
           WHERE status = 'pending'
             AND ((requester_id = $1 AND addressee_id = $2)
               OR (requester_id = $2 AND addressee_id = $1))`,
          [socket.playerId, otherId]
        )
          .then(() => {
            pushFriendsList(socket.playerId).catch(() => {});
            pushFriendsList(otherId).catch(() => {});
          })
          .catch(() => {});
      });
    });

    socket.on("friend:remove", ({ playerId: otherId } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address) || !db) return;
      if (!otherId || typeof otherId !== "string") return;
      requireAuthSocket(socket, () => {
        db.query(
          `DELETE FROM friends
           WHERE (requester_id = $1 AND addressee_id = $2)
              OR (requester_id = $2 AND addressee_id = $1)`,
          [socket.playerId, otherId]
        )
          .then(() => {
            const sid = socketByPlayerId.get(otherId);
            if (sid) {
              const otherSock = io.sockets.sockets.get(sid);
              if (otherSock) otherSock.emit("friend_removed", { playerId: socket.playerId });
              pushFriendsList(otherId).catch(() => {});
            }
            pushFriendsList(socket.playerId).catch(() => {});
          })
          .catch(() => {});
      });
    });

    socket.on("party:invite", ({ targetPlayerId } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address) || !db) return;
      if (!targetPlayerId || typeof targetPlayerId !== "string") return;
      requireAuthSocket(socket, () => {
        const partyId = partyByPlayerId.get(socket.playerId);
        if (!partyId) return socket.emit("error_message", { message: "You must be in a party to invite." });
        const party = partiesById.get(partyId);
        if (!party || party.leaderId !== socket.playerId) {
          return socket.emit("error_message", { message: "Only the party leader can invite." });
        }
        if (party.status !== "idle") {
          return socket.emit("error_message", { message: "Party must be idle to send invites." });
        }
        if (party.members.length >= 4) {
          return socket.emit("error_message", { message: "Party is full." });
        }
        if (party.members.some((member) => member.playerId === targetPlayerId)) {
          return socket.emit("error_message", { message: "That player is already in your party." });
        }

        const targetSid = socketByPlayerId.get(targetPlayerId);
        if (!targetSid) return socket.emit("error_message", { message: "That player is not online." });

        db.query(
          `SELECT 1 FROM friends
           WHERE status = 'accepted'
             AND ((requester_id = $1 AND addressee_id = $2)
               OR (requester_id = $2 AND addressee_id = $1))
           LIMIT 1`,
          [socket.playerId, targetPlayerId]
        )
          .then(({ rows }) => {
            if (!rows.length) return socket.emit("error_message", { message: "That player is not your friend." });
            const targetSock = io.sockets.sockets.get(targetSid);
            if (!targetSock) return socket.emit("error_message", { message: "That player is not online." });
            targetSock.emit("party_invite", {
              partyId,
              partyCode: party.code,
              fromPlayerId: socket.playerId,
              fromDisplayName: socket.playerDisplayName,
            });
            socket.emit("party_invite_sent", {
              playerId: targetPlayerId,
              displayName: targetSock.playerDisplayName || "Player",
            });
          })
          .catch(() => {});
      });
    });

    // DEPRECATED — use queue:enter_v2 for public ranked/casual queue.
    // solo_td / private_td paths remain here until a dedicated solo handler is added.
    socket.on("queue:enter", ({ mode, loadoutSlot, unitTypeIds, filters = {}, displayName, settings, pvpMode } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }

      if (mode === "solo_td" || mode === "solo_t2t") {
        if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
        const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
        const normalizedSettings = normalizeMatchSettingsOrReply(socket, settings);
        if (!normalizedSettings) return;
        socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
        const { code, roomId } = createMLRoom(socket.id, playerDisplayName, normalizedSettings);
        const room = mlRoomsByCode.get(code);
        sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
        socket.join(roomId);
        const botCfgs = Array.isArray(filters.botConfigs) && filters.botConfigs.length > 0
          ? filters.botConfigs
          : [{ difficulty: "medium" }];
        const validDiffs = VALID_AI_DIFFICULTIES;
        for (const bot of botCfgs) {
          const difficulty = validDiffs.includes(bot.difficulty) ? bot.difficulty : "medium";
          const totalPlayers = room.players.length + (room.aiPlayers || []).length;
          if (totalPlayers >= 4) break;
          if (!room.aiPlayers) room.aiPlayers = [];
          room.aiPlayers.push({ laneIndex: totalPlayers, difficulty, team: ffaTeamForLane(totalPlayers) });
        }
        socket.emit("match_found", {
          roomCode: code,
          laneIndex: 0,
          teammates: [playerDisplayName],
          opponents: (room.aiPlayers || []).map((ai) => `CPU (${ai.difficulty})`),
          matchFormat: "ffa",
          autoStart: true,
        });
        startMLGame(roomId, code);
        log.info("solo match started via queue:enter", { code, bots: (room.aiPlayers || []).length });
        return;
      }

      if (mode === "private_td" || mode === "private_t2t") {
        if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
        const privateCode = typeof filters.privateCode === "string" ? filters.privateCode.trim().toUpperCase() : null;
        if (!privateCode) {
          const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
          const normalizedSettings = normalizeMatchSettingsOrReply(socket, settings);
          if (!normalizedSettings) return;
          const { code, roomId } = createMLRoom(socket.id, playerDisplayName, normalizedSettings);
          const room = mlRoomsByCode.get(code);
          room.pvpMode = "ffa";
          socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
          sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
          socket.join(roomId);
          socket.emit("ml_room_created", { code, laneIndex: 0, displayName: playerDisplayName, settings: room.settings });
          io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));
          log.info("private room created via queue:enter", { code });
        } else {
          const room = mlRoomsByCode.get(privateCode);
          if (!room) return socket.emit("error_message", { message: "Room not found." });
          const totalInRoom = room.players.length + (room.aiPlayers || []).length;
          if (totalInRoom >= 4) return socket.emit("error_message", { message: "Room is full (max 4)." });
          if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });
          const laneIndex = room.players.length;
          const assignedTeam = ffaTeamForLane(laneIndex);
          room.players.push(socket.id);
          room.laneBySocketId.set(socket.id, laneIndex);
          room.playerTeamsBySocketId.set(socket.id, assignedTeam);
          const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
          room.playerNames.set(socket.id, playerDisplayName);
          if (room.aiPlayers) room.aiPlayers.forEach((ai, idx) => { ai.laneIndex = room.players.length + idx; });
          socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
          sessionBySocketId.set(socket.id, { code: privateCode, roomId: room.roomId, laneIndex, mode: "multilane" });
          socket.join(room.roomId);
          socket.emit("ml_room_joined", { code: privateCode, laneIndex, displayName: playerDisplayName });
          io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(privateCode, room));
          log.info("joined private room via queue:enter", { code: privateCode });
        }
        return;
      }

      requireAuthSocket(socket, () => {
        if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
        const validModes = new Set(["ranked_2v2", "casual_2v2", "public_queue", "casual_queue"]);
        if (!validModes.has(mode)) {
          return socket.emit("error_message", { message: "This public queue mode is not available." });
        }
        const isRanked = mode === "ranked_2v2" || mode === "public_queue";
        const queueMode = matchmaker.makeBucketKey("line_wars", "ffa", isRanked);
        const dbMode = isRanked ? "ffa_ranked" : "ffa_casual";

        let partyId = partyByPlayerId.get(socket.playerId);
        if (!partyId) {
          let code;
          do code = generatePartyCode();
          while (partiesByCode.has(code));
          partyId = crypto.randomUUID();
          const soloParty = {
            id: partyId,
            code,
            leaderId: socket.playerId,
            members: [{ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id }],
            status: "idle",
            queueMode: null,
            queueEnteredAt: null,
            region: "global",
          };
          partiesById.set(partyId, soloParty);
          partiesByCode.set(code, partyId);
          partyByPlayerId.set(socket.playerId, partyId);
          socket.join(`party:${partyId}`);
        }

        const party = partiesById.get(partyId);
        if (!party) return socket.emit("error_message", { message: "Party not found." });
        if (party.leaderId !== socket.playerId) {
          return socket.emit("error_message", { message: "Only the party leader can enter queue." });
        }
        if (party.status !== "idle") {
          return socket.emit("error_message", { message: "Party is already queued or in a match." });
        }

        let dataPromise;
        if (db && isRanked) {
          dataPromise = Promise.all([
            db.query(
              "SELECT COALESCE(wins + losses, 0) AS matches FROM ratings WHERE player_id = $1 AND mode = 'ffa_casual'",
              [socket.playerId]
            ).then((result) => Number(result.rows[0]?.matches) || 0).catch(() => 0),
            db.query(
              "SELECT rating, wins + losses AS matches FROM ratings WHERE player_id = $1 AND mode = 'ffa_ranked'",
              [socket.playerId]
            ).then((result) =>
              result.rows[0]
                ? { rating: Number(result.rows[0].rating), rankedMatches: Number(result.rows[0].matches) }
                : { rating: 1200, rankedMatches: 0 }
            ).catch(() => ({ rating: 1200, rankedMatches: 0 })),
          ]).then(([casualMatches, ranked]) => ({ casualMatches, ...ranked }));
        } else {
          dataPromise = (db
            ? db.query("SELECT rating FROM ratings WHERE player_id = $1 AND mode = $2", [socket.playerId, dbMode])
                .then((result) => Number(result.rows[0]?.rating) || 1200)
                .catch(() => 1200)
            : Promise.resolve(1200)
          ).then((rating) => ({ rating, casualMatches: 99, rankedMatches: 99 }));
        }

        dataPromise.then(({ rating, casualMatches, rankedMatches }) => {
          if (isRanked && casualMatches < 5) {
            return socket.emit("error_message", {
              message: `Complete ${5 - casualMatches} more casual match(es) to unlock ranked queue.`,
            });
          }

          const finalize = canFinalizeQueueJoin(partyId, socket.playerId);
          if (!finalize.ok) {
            return socket.emit("error_message", { message: finalize.reason });
          }
          const latestParty = finalize.party;
          latestParty.status = "queued";
          latestParty.queueMode = queueMode;
          latestParty.queueEnteredAt = Date.now();
          socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

          matchmaker.addToQueue(partyId, {
            gameType: "line_wars",
            matchFormat: "ffa",
            ranked: isRanked,
            rating,
            partySize: latestParty.members.length,
            queueEnteredAt: latestParty.queueEnteredAt,
            region: latestParty.region,
            isPlacement: isRanked && rankedMatches < 10,
          });

          const queueSize = matchmaker.getQueuePlayerCount(queueMode, partiesById);
          io.to(`party:${partyId}`).emit("queue_status", { status: "queued", mode: queueMode, elapsed: 0, queueSize });
          io.to(`party:${partyId}`).emit("party_update", { party: latestParty });
          log.info("queue entered", { partyId, mode: queueMode, isPlacement: isRanked && rankedMatches < 10 });
        }).catch((err) => {
          log.warn("queue:enter legacy failed", { partyId, playerId: socket.playerId, err: err?.message || String(err) });
          socket.emit("error_message", { message: "Unable to enter queue right now." });
        });
      });
    });

    socket.on("queue:leave", () => {
      requireAuthSocket(socket, () => {
        const partyId = partyByPlayerId.get(socket.playerId);
        if (!partyId) return;
        const party = partiesById.get(partyId);
        if (!party) return;
        if (party.leaderId !== socket.playerId) {
          return socket.emit("error_message", { message: "Only the party leader can leave queue." });
        }
        if (party.status !== "queued") return;
        matchmaker.removeFromQueue(partyId);
        party.status = "idle";
        party.queueMode = null;
        party.queueEnteredAt = null;
        io.to(`party:${partyId}`).emit("queue_status", { status: "idle" });
        io.to(`party:${partyId}`).emit("party_update", { party });
        log.info("queue left", { partyId });
      });
    });

    socket.on("queue:enter_v2", ({ gameType, matchFormat, ranked, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      const { normalizeQueueRequest, validatePublicPartySize } = matchmaker;
      const normalizedQueue = normalizeQueueRequest({ gameType, matchFormat, ranked });
      if (!normalizedQueue.gameType) return socket.emit("error_message", { message: "Invalid game type." });
      if (!normalizedQueue.matchFormat) return socket.emit("error_message", { message: "Invalid match format." });
      gameType = normalizedQueue.gameType;
      matchFormat = normalizedQueue.matchFormat;
      ranked = normalizedQueue.ranked;

      const isRankedReq = !!ranked;
      if (isRankedReq && !isEnabled("ranked_queue_enabled"))
        return socket.emit("error_message", { message: "Ranked queue is currently disabled." });
      if (!isRankedReq && !isEnabled("casual_queue_enabled"))
        return socket.emit("error_message", { message: "Casual queue is currently disabled." });
      if (matchFormat === "ffa" && !isEnabled("public_ffa_enabled"))
        return socket.emit("error_message", { message: "Public FFA matchmaking is currently disabled." });
      requireAuthSocket(socket, () => {
        if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;

        let partyId = partyByPlayerId.get(socket.playerId);
        if (!partyId) {
          let code;
          do code = generatePartyCode();
          while (partiesByCode.has(code));
          partyId = crypto.randomUUID();
          const soloParty = {
            id: partyId,
            code,
            leaderId: socket.playerId,
            members: [{ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id }],
            status: "idle",
            queueMode: null,
            queueEnteredAt: null,
            region: "global",
          };
          partiesById.set(partyId, soloParty);
          partiesByCode.set(code, partyId);
          partyByPlayerId.set(socket.playerId, partyId);
          socket.join(`party:${partyId}`);
        }

        const party = partiesById.get(partyId);
        if (!party) return socket.emit("error_message", { message: "Party not found." });
        if (party.leaderId !== socket.playerId) return socket.emit("error_message", { message: "Only the party leader can enter queue." });
        if (party.status !== "idle") return socket.emit("error_message", { message: "Party is already queued or in a match." });

        const sizeErr = validatePublicPartySize(matchFormat, party.members.length);
        if (sizeErr) return socket.emit("error_message", { message: sizeErr });

        const isRanked = !!ranked;
        const dbMode = gameType === "line_wars"
          ? (isRanked ? `${matchFormat}_ranked` : `${matchFormat}_casual`)
          : null;

        const ratingPromise = db && dbMode
          ? db.query("SELECT rating, wins+losses AS matches FROM ratings WHERE player_id=$1 AND mode=$2", [socket.playerId, dbMode])
              .then((result) => result.rows[0]
                ? { rating: Number(result.rows[0].rating) || 1200, rankedMatches: Number(result.rows[0].matches) || 0 }
                : { rating: 1200, rankedMatches: 0 })
              .catch(() => ({ rating: 1200, rankedMatches: 0 }))
          : Promise.resolve({ rating: 1200, rankedMatches: 0 });

        const casualDbMode = isRanked && dbMode ? dbMode.replace("_ranked", "_casual") : null;
        const smurfPromise = isRanked && db && casualDbMode
          ? db.query("SELECT COALESCE(wins+losses,0) AS m FROM ratings WHERE player_id=$1 AND mode=$2", [socket.playerId, casualDbMode])
              .then((result) => Number(result.rows[0]?.m) || 0)
              .catch(() => 0)
          : Promise.resolve(99);

        Promise.all([ratingPromise, smurfPromise]).then(([{ rating, rankedMatches }, casualMatches]) => {
          if (isRanked && casualMatches < 5) {
            return socket.emit("error_message", {
              message: `Complete ${5 - casualMatches} more casual match(es) to unlock ranked queue.`,
            });
          }

          const finalize = canFinalizeQueueJoin(partyId, socket.playerId);
          if (!finalize.ok) {
            return socket.emit("error_message", { message: finalize.reason });
          }
          const latestParty = finalize.party;
          latestParty.status = "queued";
          latestParty.queueMode = matchmaker.makeBucketKey(gameType, matchFormat, isRanked);
          latestParty.queueEnteredAt = Date.now();
          socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

          matchmaker.addToQueue(partyId, {
            gameType,
            matchFormat,
            ranked: isRanked,
            rating,
            partySize: latestParty.members.length,
            queueEnteredAt: latestParty.queueEnteredAt,
            region: latestParty.region || "global",
            isPlacement: isRanked && rankedMatches < 10,
          });

          const bucketKey = matchmaker.makeBucketKey(gameType, matchFormat, isRanked);
          const queueSize = matchmaker.getQueuePlayerCount(bucketKey, partiesById);
          io.to(`party:${partyId}`).emit("queue_status", { status: "queued", mode: bucketKey, elapsed: 0, queueSize });
          io.to(`party:${partyId}`).emit("party_update", { party: latestParty });
          log.info("queue:enter_v2", { partyId, gameType, matchFormat, ranked: isRanked });
        }).catch((err) => {
          log.warn("queue:enter_v2 failed", { partyId, playerId: socket.playerId, err: err?.message || String(err) });
          socket.emit("error_message", { message: "Unable to enter queue right now." });
        });
      });
    });

    socket.on("lobby:create", ({ gameType, matchFormat, pvpMode, displayName, settings, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      if (!isEnabled("private_match_enabled"))
        return socket.emit("error_message", { message: "Private lobbies are currently disabled." });
      const queueConflict = getQueueConflictMessage(socket.playerId);
      if (queueConflict) return socket.emit("lobby_error", { message: queueConflict });
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const lobbyName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

      const existingLobby = matchmaker.getLobbyForSocket(socket.id);
      if (existingLobby) {
        const { lobby, disbanded } = matchmaker.leaveLobby(socket.id);
        if (lobby && !disbanded) {
          socket.leave(`lobby:${lobby.lobbyId}`);
          io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
        }
      }

      const normalizedSettings = normalizeMatchSettingsOrReply(socket, settings, "lobby_error");
      if (!normalizedSettings) return;
      const lobby = matchmaker.createLobby({
        hostSocketId: socket.id,
        hostDisplayName: lobbyName,
        gameType: gameType || "line_wars",
        matchFormat: "ffa",
        pvpMode: "ffa",
        settings: normalizedSettings,
      });
      if (!lobby) return;
      socket.join(`lobby:${lobby.lobbyId}`);
      socket.emit("lobby_created", { lobbyId: lobby.lobbyId, code: lobby.code, lobby: matchmaker.lobbySnapshot(lobby) });
      log.info("lobby created", { lobbyId: lobby.lobbyId, code: lobby.code });
    });

    socket.on("lobby:join", ({ code, displayName, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      const queueConflict = getQueueConflictMessage(socket.playerId);
      if (queueConflict) return socket.emit("lobby_error", { message: queueConflict });
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const normalizedCode = String(code || "").trim().toUpperCase();
      if (normalizedCode.length !== 6) return socket.emit("lobby_error", { message: "Invalid lobby code." });
      const lobbyName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

      const existingLobby = matchmaker.getLobbyForSocket(socket.id);
      if (existingLobby) {
        const { lobby, disbanded } = matchmaker.leaveLobby(socket.id);
        if (lobby && !disbanded) {
          socket.leave(`lobby:${lobby.lobbyId}`);
          io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
        }
      }

      const joinErr = matchmaker.joinLobby(normalizedCode, socket.id, lobbyName);
      if (joinErr) return socket.emit("lobby_error", { message: joinErr });

      const joinedLobby = matchmaker.getLobbyByCode(normalizedCode);
      socket.join(`lobby:${joinedLobby.lobbyId}`);
      socket.emit("lobby_joined", {
        lobbyId: joinedLobby.lobbyId,
        code: joinedLobby.code,
        lobby: matchmaker.lobbySnapshot(joinedLobby),
      });
      io.to(`lobby:${joinedLobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(joinedLobby) });
      log.info("lobby joined", { lobbyId: joinedLobby.lobbyId });
    });

    socket.on("lobby:leave", () => {
      const { lobby, disbanded } = matchmaker.leaveLobby(socket.id);
      if (!lobby) return;
      socket.leave(`lobby:${lobby.lobbyId}`);
      socket.emit("lobby_left", { lobbyId: lobby.lobbyId });
      if (!disbanded) {
        io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
      }
      log.info("lobby left", { lobbyId: lobby.lobbyId });
    });

    socket.on("lobby:invite", ({ targetPlayerId } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address) || !db) return;
      if (!targetPlayerId || typeof targetPlayerId !== "string") return;
      requireAuthSocket(socket, () => {
        const lobby = matchmaker.getLobbyForSocket(socket.id);
        if (!lobby) return socket.emit("error_message", { message: "You are not in a lobby." });
        if (lobby.hostSocketId !== socket.id) return socket.emit("error_message", { message: "Only the host can invite players." });
        if (lobby.status !== "open") return socket.emit("error_message", { message: "Lobby is not open for invites." });

        const targetSid = socketByPlayerId.get(targetPlayerId);
        if (!targetSid) return socket.emit("error_message", { message: "That player is not online." });

        db.query(
          `SELECT 1 FROM friends
           WHERE status = 'accepted'
             AND ((requester_id = $1 AND addressee_id = $2)
               OR (requester_id = $2 AND addressee_id = $1))
           LIMIT 1`,
          [socket.playerId, targetPlayerId]
        )
          .then(({ rows }) => {
            if (!rows.length) return socket.emit("error_message", { message: "That player is not your friend." });
            const targetSock = io.sockets.sockets.get(targetSid);
            if (!targetSock) return socket.emit("error_message", { message: "That player is not online." });
            targetSock.emit("lobby_invite", {
              lobbyId: lobby.lobbyId,
              code: lobby.code,
              fromPlayerId: socket.playerId,
              fromDisplayName: socket.playerDisplayName,
            });
            socket.emit("lobby_invite_sent", {
              playerId: targetPlayerId,
              displayName: targetSock.playerDisplayName || "Player",
            });
          })
          .catch(() => {});
      });
    });

    socket.on("lobby:update", ({ gameType, matchFormat, pvpMode, settings } = {}) => {
      const normalizedSettings = settings === undefined
        ? undefined
        : normalizeMatchSettingsOrReply(socket, settings, "lobby_error");
      if (settings !== undefined && !normalizedSettings) return;
      const updateErr = matchmaker.updateLobby(socket.id, {
        gameType,
        matchFormat,
        pvpMode,
        settings: normalizedSettings,
      });
      if (updateErr) return socket.emit("lobby_error", { message: updateErr });
      const lobby = matchmaker.getLobbyForSocket(socket.id);
      io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
    });

    socket.on("lobby:add_bot", ({ difficulty } = {}) => {
      const addErr = matchmaker.addBotToLobby(socket.id, difficulty || "medium");
      if (addErr) return socket.emit("lobby_error", { message: addErr });
      const lobby = matchmaker.getLobbyForSocket(socket.id);
      io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
    });

    socket.on("lobby:remove_bot", ({ index } = {}) => {
      const removeErr = matchmaker.removeBotFromLobby(socket.id, typeof index === "number" ? index : 0);
      if (removeErr) return socket.emit("lobby_error", { message: removeErr });
      const lobby = matchmaker.getLobbyForSocket(socket.id);
      io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
    });

    socket.on("lobby:ready", ({ ready } = {}) => {
      const { lobby, error } = matchmaker.setMemberReady(socket.id, !!ready);
      if (error) return socket.emit("lobby_error", { message: error });
      io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
    });

    socket.on("lobby:assign_team", ({ targetSocketId, team } = {}) => {
      const err = matchmaker.assignTeam(socket.id, String(targetSocketId || ""), team ? String(team) : "");
      if (err) return socket.emit("lobby_error", { message: err });
      const lobby = matchmaker.getLobbyForSocket(socket.id);
      io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
    });

    socket.on("lobby:launch", ({ loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      const launchLobby = matchmaker.getLobbyForSocket(socket.id);
      if (!launchLobby) return socket.emit("lobby_error", { message: "Not in a lobby." });
      if (launchLobby.hostSocketId !== socket.id) return socket.emit("lobby_error", { message: "Only the host can launch." });
      if (launchLobby.status !== "open") return socket.emit("lobby_error", { message: "Lobby already launched." });

      const launchErr = matchmaker.validateLaunch(launchLobby);
      if (launchErr) return socket.emit("lobby_error", { message: launchErr });

      launchLobby.status = "starting";
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : socket.pendingUnitTypeIds;

      const hostName = sanitizeDisplayName(launchLobby.members.get(socket.id)?.name || "Player");

      const normalizedLaunchSettings = normalizeMatchSettingsOrReply(socket, launchLobby.settings, "lobby_error");
      if (!normalizedLaunchSettings) return;
      const { code, roomId } = createMLRoom(socket.id, hostName, normalizedLaunchSettings);
      const room = mlRoomsByCode.get(code);
      room.pvpMode = "ffa";

      sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
      room.playerTeamsBySocketId.set(socket.id, ffaTeamForLane(0));
      socket.join(roomId);

      let laneIndex = 1;
      for (const [memberSid, metadata] of launchLobby.members) {
        if (memberSid === socket.id) continue;
        room.players.push(memberSid);
        room.laneBySocketId.set(memberSid, laneIndex);
        room.playerNames.set(memberSid, sanitizeDisplayName(metadata.name));
        room.playerTeamsBySocketId.set(memberSid, ffaTeamForLane(laneIndex));
        sessionBySocketId.set(memberSid, { code, roomId, laneIndex, mode: "multilane" });
        const memberSock = io.sockets.sockets.get(memberSid);
        if (memberSock) memberSock.join(roomId);
        laneIndex += 1;
      }

      if (!room.aiPlayers) room.aiPlayers = [];
      for (const bot of launchLobby.botSlots) {
        const total = room.players.length + room.aiPlayers.length;
        if (total >= 4) break;
        room.aiPlayers.push({ laneIndex: total, difficulty: bot.difficulty, team: ffaTeamForLane(total) });
      }

      io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

      const humanNames = [...launchLobby.members.values()].map((member) => member.name);
      const botNames = launchLobby.botSlots.map((bot) => `CPU (${bot.difficulty})`);
      const allNames = [...humanNames, ...botNames];
      let idx = 0;
      for (const [memberSid, memberMeta] of launchLobby.members) {
        io.to(memberSid).emit("match_found", {
          roomCode: code,
          laneIndex: idx,
          teammates: [memberMeta.name],
          opponents: allNames.filter((name) => name !== memberMeta.name),
          matchFormat: "ffa",
          autoStart: true,
        });
        idx += 1;
      }

      startMLGame(roomId, code);
      log.info("lobby launched", {
        lobbyId: launchLobby.lobbyId,
        code,
        humans: launchLobby.members.size,
        bots: launchLobby.botSlots.length,
      });
      matchmaker.disbandLobby(launchLobby.lobbyId);
    });

    socket.on("create_ml_room", ({ displayName, settings, pvpMode, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const normalizedSettings = normalizeMatchSettingsOrReply(socket, settings);
      if (!normalizedSettings) return;
      const { code, roomId } = createMLRoom(socket.id, displayName, normalizedSettings);
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
      sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
      socket.join(roomId);
      const room = mlRoomsByCode.get(code);
      room.pvpMode = "ffa";
      socket.emit("ml_room_created", {
        code,
        laneIndex: 0,
        displayName: String(displayName || "Player").slice(0, 20),
        settings: room.settings,
      });
      io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));
    });

    socket.on("join_ml_room", ({ code, displayName, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const normalized = String(code || "").trim().toUpperCase();
      if (normalized.length !== 6) return socket.emit("error_message", { message: "Invalid room code." });
      const room = mlRoomsByCode.get(normalized);
      if (!room) return socket.emit("error_message", { message: "Room not found." });
      const totalInRoom = room.players.length + (room.aiPlayers || []).length;
      if (totalInRoom >= 4) return socket.emit("error_message", { message: "Room is full (max 4)." });
      if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });

      const laneIndex = room.players.length;
      const assignedTeam = ffaTeamForLane(laneIndex);
      room.players.push(socket.id);
      room.laneBySocketId.set(socket.id, laneIndex);
      room.playerTeamsBySocketId.set(socket.id, assignedTeam);
      room.playerNames.set(socket.id, sanitizeDisplayName(displayName));
      if (room.aiPlayers) room.aiPlayers.forEach((ai, idx) => { ai.laneIndex = room.players.length + idx; });
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
      sessionBySocketId.set(socket.id, { code: normalized, roomId: room.roomId, laneIndex, mode: "multilane" });
      socket.join(room.roomId);
      socket.emit("ml_room_joined", { code: normalized, laneIndex, displayName: room.playerNames.get(socket.id) });
      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(normalized, room));
    });

    socket.on("ml_player_ready", ({ code } = {}) => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const normalized = String(code || session.code || "").trim().toUpperCase();
      const room = mlRoomsByCode.get(normalized);
      if (!room || !room.players.includes(socket.id)) return;
      room.readySet.add(socket.id);
      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(normalized, room));

      const totalPlayers = room.players.length + (room.aiPlayers || []).length;
      const teamCheck = validateMlTeamSetup(room);
      if (!teamCheck.ok) {
        return socket.emit("error_message", { message: teamCheck.reason });
      }
      if (totalPlayers >= 1 && room.readySet.size === room.players.length) {
        startMLGame(room.roomId, normalized);
      }
    });

    socket.on("ml_force_start", ({ code } = {}) => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const normalized = String(code || session.code || "").trim().toUpperCase();
      const room = mlRoomsByCode.get(normalized);
      if (!room) return;
      if (room.hostSocketId !== socket.id) {
        return socket.emit("error_message", { message: "Only the host can force start." });
      }
      const totalPlayers = room.players.length + (room.aiPlayers || []).length;
      if (totalPlayers < 1) {
        return socket.emit("error_message", { message: "Need at least 1 player or bot to start." });
      }
      const teamCheck = validateMlTeamSetup(room);
      if (!teamCheck.ok) {
        return socket.emit("error_message", { message: teamCheck.reason });
      }
      if (!gamesByRoomId.has(room.roomId)) {
        startMLGame(room.roomId, normalized);
      }
    });

    socket.on("update_ml_room_settings", ({ settings } = {}) => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room || room.hostSocketId !== socket.id || gamesByRoomId.has(room.roomId)) return;
      const normalizedSettings = normalizeMatchSettingsOrReply(socket, settings);
      if (!normalizedSettings) return;
      room.settings = normalizedSettings;
      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
    });

    socket.on("add_ai_to_ml_room", ({ difficulty } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room) return;
      if (room.hostSocketId !== socket.id) {
        return socket.emit("error_message", { message: "Only the host can add AI players." });
      }
      if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });
      const totalPlayers = room.players.length + (room.aiPlayers || []).length;
      if (totalPlayers >= 4) return socket.emit("error_message", { message: "Room is full (max 4 players)." });
      const diff = VALID_AI_DIFFICULTIES.includes(String(difficulty)) ? String(difficulty) : "easy";
      if (!room.aiPlayers) room.aiPlayers = [];
      const aiTeam = ffaTeamForLane(totalPlayers);
      room.aiPlayers.push({ laneIndex: totalPlayers, difficulty: diff, team: aiTeam });
      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
    });

    socket.on("remove_ai_from_ml_room", ({ laneIndex } = {}) => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room || room.hostSocketId !== socket.id || gamesByRoomId.has(room.roomId) || !room.aiPlayers) return;
      const idx = room.aiPlayers.findIndex((ai) => ai.laneIndex === Number(laneIndex));
      if (idx === -1) return;
      room.aiPlayers.splice(idx, 1);
      room.aiPlayers.forEach((ai, index) => { ai.laneIndex = room.players.length + index; });
      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
    });

    socket.on("swap_ml_lanes", ({ laneA, laneB } = {}) => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room || room.hostSocketId !== socket.id || gamesByRoomId.has(room.roomId)) return;
      const a = Number(laneA);
      const b = Number(laneB);
      const total = room.players.length + (room.aiPlayers || []).length;
      if (!Number.isInteger(a) || !Number.isInteger(b) || a === b) return;
      if (a < 0 || a >= total || b < 0 || b >= total) return;

      const getOwner = (laneIdx) => {
        const humanSid = room.players.find((sid) => room.laneBySocketId.get(sid) === laneIdx);
        if (humanSid) return { type: "human", sid: humanSid };
        const aiEntry = (room.aiPlayers || []).find((ai) => ai.laneIndex === laneIdx);
        if (aiEntry) return { type: "ai", entry: aiEntry };
        return null;
      };

      const ownerA = getOwner(a);
      const ownerB = getOwner(b);
      if (!ownerA || !ownerB) return;

      if (ownerA.type === "human") {
        room.laneBySocketId.set(ownerA.sid, b);
        const ownerSession = sessionBySocketId.get(ownerA.sid);
        if (ownerSession) ownerSession.laneIndex = b;
        io.to(ownerA.sid).emit("ml_lane_reassigned", { laneIndex: b });
      } else {
        ownerA.entry.laneIndex = b;
      }

      if (ownerB.type === "human") {
        room.laneBySocketId.set(ownerB.sid, a);
        const ownerSession = sessionBySocketId.get(ownerB.sid);
        if (ownerSession) ownerSession.laneIndex = a;
        io.to(ownerB.sid).emit("ml_lane_reassigned", { laneIndex: a });
      } else {
        ownerB.entry.laneIndex = a;
      }

      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
    });

    socket.on("set_ml_team", ({ laneIndex, team } = {}) => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      socket.emit("error_message", { message: "Team assignment is no longer supported. Matches now use free-for-all survival lanes." });
    });

    // ── Loadout selection phase (in-game) ─────────────────────────────────
    socket.on("ml_loadout_confirm", (rawPayload) => {
      let payload = rawPayload;
      if (typeof payload === "string") {
        try {
          payload = JSON.parse(payload);
        } catch {
          payload = null;
        }
      }
      const unitTypeIds = Array.isArray(payload?.unitTypeIds) ? payload.unitTypeIds : null;
      const raceId = normalizeRaceId(payload?.raceId);
      if (!checkActionRateLimit(socket.id)) return;
      const hasLegacyIds = hasValidInlineLoadoutIds(unitTypeIds);
      const hasRaceSelection = isValidRaceId(raceId);
      if (!hasLegacyIds && !hasRaceSelection) {
        log.warn("[loadout] invalid selection", {
          socketId: socket.id,
          playerId: socket.playerId || null,
          mode: sessionBySocketId.get(socket.id)?.mode || null,
          rawType: typeof rawPayload,
          rawPayload,
          raceId,
          unitTypeIds,
        });
        return socket.emit("error_message", { message: "Invalid race selection." });
      }
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room) return;
      socket.pendingRaceId = hasRaceSelection ? raceId : null;
      socket.pendingUnitTypeIds = hasLegacyIds ? unitTypeIds.map(Number) : null;
      if (room.loadoutConfirms) {
        room.loadoutConfirms.set(socket.id, {
          raceId: socket.pendingRaceId,
          unitTypeIds: socket.pendingUnitTypeIds,
        });
      }
      // Unblock waitForLoadoutConfirms if every human has now confirmed
      if (room._loadoutPhaseResolve) {
        const allDone = room.players.every(sid => {
          const s = io.sockets.sockets.get(sid);
          if (!s) return false;
          if (isValidRaceId(s.pendingRaceId)) return true;
          return Array.isArray(s.pendingUnitTypeIds) && s.pendingUnitTypeIds.length === 5;
        });
        if (allDone) room._loadoutPhaseResolve();
      }
      log.info("[loadout] player confirmed", {
        code: session.code,
        laneIndex: session.laneIndex,
        raceId: socket.pendingRaceId || null,
      });
    });
    // ─────────────────────────────────────────────────────────────────────

    socket.on("ml_start_wave_vote", () => {
      if (!checkActionRateLimit(socket.id)) return;
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane" || !submitWaveReadyVote) return;

      log.info("[WaveStart][Server] client vote received", {
        roomId: session.roomId,
        code: session.code,
        laneIndex: session.laneIndex,
        socketId: socket.id,
      });

      const result = submitWaveReadyVote(session.roomId, session.code, session.laneIndex);
      log.info("[WaveStart][Server] vote result", {
        roomId: session.roomId,
        code: session.code,
        laneIndex: session.laneIndex,
        ok: !!(result && result.ok),
        startedImmediately: !!(result && result.startedImmediately),
        reason: result && result.reason ? result.reason : null,
      });
      if (!result.ok)
        socket.emit("error_message", { message: result.reason || "Unable to start the next wave." });
    });

    socket.on("ml_game_scene_ready", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      log.info("[ml-game] client reported gameplay scene ready (legacy)", {
        code: session.code,
        laneIndex: session.laneIndex,
        socketId: socket.id,
      });
    });

    socket.on("ml_loadout_ready", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      socket._loadoutReady = true;
      const room = mlRoomsByCode.get(session.code);
      if (room) {
        if (!room.loadoutReadyByLane) room.loadoutReadyByLane = new Set();
        room.loadoutReadyByLane.add(session.laneIndex);
      }
      log.info("[ml-game] client loadout ready", {
        code: session.code,
        laneIndex: session.laneIndex,
        socketId: socket.id,
      });
      if (!room || !room._loadoutReadyResolve) return;
      const allReady = room.players.every((sid) => {
        const laneIndex = room.laneBySocketId.get(sid);
        return Number.isInteger(laneIndex) && room.loadoutReadyByLane?.has(laneIndex);
      });
      if (allReady) room._loadoutReadyResolve();
    });

    socket.on("ml_gameplay_ready", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      socket._gameplayReady = true;
      const room = mlRoomsByCode.get(session.code);
      if (room) {
        if (!room.gameplayReadyByLane) room.gameplayReadyByLane = new Set();
        room.gameplayReadyByLane.add(session.laneIndex);
      }
      log.info("[ml-game] client gameplay ready", {
        code: session.code,
        laneIndex: session.laneIndex,
        socketId: socket.id,
      });
      if (!room || !room._gameplayReadyResolve) return;
      const allReady = room.players.every((sid) => {
        const laneIndex = room.laneBySocketId.get(sid);
        return Number.isInteger(laneIndex) && room.gameplayReadyByLane?.has(laneIndex);
      });
      if (allReady) room._gameplayReadyResolve();
    });

    socket.on("ml_content_progress", (rawPayload) => {
      let payload = rawPayload;
      if (typeof payload === "string") {
        try { payload = JSON.parse(payload); } catch { payload = null; }
      }
      const percent = typeof payload?.percent === "number"
        ? Math.max(0, Math.min(1, payload.percent)) : 0;
      const state = typeof payload?.state === "string" ? payload.state : "Preparing";
      socket._contentProgress = { percent, state };
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room) return;
      if (!room.contentProgressByLane) room.contentProgressByLane = new Map();
      room.contentProgressByLane.set(session.laneIndex, socket._contentProgress);
    });

    socket.on("ml_chat_message", (rawPayload) => {
      if (!checkActionRateLimit(socket.id)) {
        socket.emit("error_message", { message: "Chat rate limit exceeded." });
        return;
      }

      let payload = rawPayload;
      if (typeof payload === "string") {
        try {
          payload = JSON.parse(payload);
        } catch {
          payload = null;
        }
      }

      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") {
        socket.emit("error_message", { message: "Cannot send chat without an active multilane session." });
        return;
      }

      const room = mlRoomsByCode.get(session.code);
      if (!room) {
        socket.emit("error_message", { message: "Multilane room not found for chat broadcast." });
        return;
      }

      const message = typeof payload?.message === "string" ? payload.message.trim() : "";
      if (!message) {
        socket.emit("error_message", { message: "Cannot send an empty chat message." });
        return;
      }

      const trimmedMessage = message.slice(0, 240);
      const chatPayload = {
        laneIndex: session.laneIndex,
        displayName: room.playerNames.get(socket.id) || socket.playerDisplayName || "Player",
        message: trimmedMessage,
        timestampUtc: new Date().toISOString(),
        team: room.playerTeamsBySocketId?.get(socket.id) || null,
      };

      io.to(room.roomId).emit("ml_chat_message", chatPayload);
      log.info("[ml-chat] message", {
        code: session.code,
        laneIndex: session.laneIndex,
        displayName: chatPayload.displayName,
        length: trimmedMessage.length,
      });
    });

    socket.on("player_action", (rawPayload) => {
      let payload = rawPayload;
      if (typeof payload === "string") {
        try {
          payload = JSON.parse(payload);
        } catch {
          payload = null;
        }
      }

      const type = typeof payload?.type === "string" ? payload.type : null;
      const data =
        payload && typeof payload === "object"
          ? (payload.data && typeof payload.data === "object" ? payload.data : payload)
          : null;

      if (typeof type !== "string") {
        let preview = null;
        try {
          preview = typeof rawPayload === "string"
            ? rawPayload
            : JSON.stringify(rawPayload);
        } catch {
          preview = "[unserializable]";
        }
        log.warn("[player_action] invalid payload shape", {
          socketId: socket.id,
          mode: sessionBySocketId.get(socket.id)?.mode || null,
          rawType: typeof rawPayload,
          normalizedType: type,
          preview,
        });
      }

      if (!checkActionRateLimit(socket.id)) {
        socket.emit("error_message", { message: "Action rate limit exceeded." });
        return;
      }
      const session = sessionBySocketId.get(socket.id);
      if (!session) {
        socket.emit("error_message", { message: "No session for socket" });
        return;
      }


      if (session.mode === "multilane") {
        const entry = gamesByRoomId.get(session.roomId);
        if (!entry) return socket.emit("error_message", { message: "Game not started" });
        const lane = entry.game.lanes[session.laneIndex];
        if (!lane) return socket.emit("error_message", { message: "Lane not found" });
        if (lane.eliminated) return socket.emit("error_message", { message: "You have been eliminated." });
        const result = applyMultilaneAction(entry, session.laneIndex, { type, data });
        if (!result.ok) {
          log.warn("[player_action] rejected", {
            socketId: socket.id,
            laneIndex: session.laneIndex,
            type,
            reason: result.reason || "Action rejected",
            data,
          });
          return socket.emit("error_message", { message: result.reason || "Action rejected" });
        }
        socket.emit("action_applied", {
          type,
          laneIndex: session.laneIndex,
          tick: entry.game.tick,
          gold: lane.gold,
          income: lane.income,
        });
      }
    });

    socket.on("request_rematch", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room) return;
      const activeEntry = gamesByRoomId.get(room.roomId);
      if (activeEntry) {
        if (activeEntry.game && activeEntry.game.phase === "ended") {
          stopMLGame(room.roomId, session.code);
        } else {
          return;
        }
      }
      if (!room.rematchVotes) room.rematchVotes = new Set();
      if (room.rematchVotes.has(socket.id)) return;
      room.rematchVotes.add(socket.id);
      const status = buildRematchStatus ? buildRematchStatus(room) : { count: room.rematchVotes.size, needed: room.players.length };
      io.to(room.roomId).emit("rematch_vote", { count: status.count, needed: status.needed });
      io.to(room.roomId).emit("rematch_status", status);
      if (status.allAccepted) {
        room.rematchVotes = null;
        room.readySet = new Set();
        if (room._cleanupHandle) {
          clearTimeout(room._cleanupHandle);
          room._cleanupHandle = null;
        }
        io.to(room.roomId).emit("rematch_starting", { countdownSeconds: 0 });
        startMLGame(room.roomId, session.code);
      }
    });

    socket.on("ml_continue_after_win", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane" || !handlePostWinDecision) return;
      const result = handlePostWinDecision(session.roomId, session.code, "continue", socket.id);
      if (!result.ok) socket.emit("error_message", { message: result.reason || "Unable to continue" });
    });

    socket.on("ml_end_game_now", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane" || !handlePostWinDecision) return;
      const result = handlePostWinDecision(session.roomId, session.code, "end_now", socket.id);
      if (!result.ok) socket.emit("error_message", { message: result.reason || "Unable to end match" });
    });

    socket.on("cancel_rematch", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session || session.mode !== "multilane") return;
      const room = mlRoomsByCode.get(session.code);
      if (!room || !room.rematchVotes || gamesByRoomId.has(room.roomId)) return;
      if (!room.rematchVotes.delete(socket.id)) return;
      const status = buildRematchStatus ? buildRematchStatus(room) : { count: room.rematchVotes.size, needed: room.players.length };
      io.to(room.roomId).emit("rematch_vote", { count: status.count, needed: status.needed });
      io.to(room.roomId).emit("rematch_status", status);
    });

    socket.on("leave_game", () => {
      const session = sessionBySocketId.get(socket.id);
      if (!session) return;

      if (session.mode === "multilane") {
        const room = mlRoomsByCode.get(session.code);
        if (!room) return;
        const laneIndex = room.laneBySocketId.get(socket.id);
        const entry = gamesByRoomId.get(room.roomId);
        const playerName = room.playerNames.get(socket.id) || socket.playerDisplayName || "Player";
        const assignedTeam = room.playerTeamsBySocketId?.get(socket.id);
        const lane = entry && laneIndex !== undefined ? entry.game.lanes[laneIndex] : null;
        const isSpectatorOnly = !!(lane && lane.eliminated);

        const idx = room.players.indexOf(socket.id);
        if (idx !== -1) room.players.splice(idx, 1);
        room.laneBySocketId.delete(socket.id);
        if (room.playerTeamsBySocketId) room.playerTeamsBySocketId.delete(socket.id);
        room.playerNames.delete(socket.id);
        room.readySet.delete(socket.id);
        if (room.rematchVotes) room.rematchVotes.delete(socket.id);
        sessionBySocketId.delete(socket.id);
        socket.leave(room.roomId);
        socket.emit("left_game_ack");

        if (entry && laneIndex !== undefined && !isSpectatorOnly && attachTakeoverBot) {
          const takeover = attachTakeoverBot(room, entry, laneIndex, {
            difficulty: DEFAULT_TAKEOVER_DIFFICULTY,
            displayName: `Takeover AI (${DEFAULT_TAKEOVER_DIFFICULTY})`,
            humanDisplayName: playerName,
            playerId: socket.playerId || null,
            team: assignedTeam,
          });
          if (takeover) {
            io.to(room.roomId).emit("ml_ai_takeover_started", {
              laneIndex,
              displayName: playerName,
              difficulty: takeover.difficulty,
            });
          } else if (!entry.eliminatedNotified.has(laneIndex)) {
            entry.eliminatedNotified.add(laneIndex);
            io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "quit" });
          }
        }

        if (room.players.length === 0 && (room.aiPlayers || []).length === 0) {
          stopMLGame(room.roomId, session.code);
          mlRoomsByCode.delete(session.code);
        } else if (!entry) {
          if (room.rematchVotes) {
            const status = buildRematchStatus ? buildRematchStatus(room) : { count: room.rematchVotes.size, needed: room.players.length };
            io.to(room.roomId).emit("rematch_vote", { count: status.count, needed: status.needed });
            io.to(room.roomId).emit("rematch_status", status);
          }
          io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
        }
        return;
      }


      sessionBySocketId.delete(socket.id);
      socket.emit("left_game_ack");
    });

    socket.on("disconnect", () => {
      const currentSocketForPlayer = socket.playerId ? socketByPlayerId.get(socket.playerId) : null;
      sessionBySocketId.delete(socket.id);

      const lobby = matchmaker.getLobbyForSocket(socket.id);
      if (lobby) {
        const { lobby: leftLobby, disbanded } = matchmaker.leaveLobby(socket.id);
        if (leftLobby && !disbanded) {
          io.to(`lobby:${leftLobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(leftLobby) });
        }
      }
      matchmaker.cleanupSocket(socket.id);

      if (socket.playerId) {
        if (db) {
          const offlineId = socket.playerId;
          const offlineName = socket.playerDisplayName;
          getFriendsList(offlineId)
            .then((friends) => {
              for (const friend of friends) {
                if (friend.status !== "accepted") continue;
                const sid = socketByPlayerId.get(friend.playerId);
                if (!sid) continue;
                const friendSock = io.sockets.sockets.get(sid);
                if (friendSock) friendSock.emit("friend_offline", { playerId: offlineId, displayName: offlineName });
              }
            })
            .catch(() => {});
        }

        if (currentSocketForPlayer === socket.id) {
          socketByPlayerId.delete(socket.playerId);
        }

        const partyId = partyByPlayerId.get(socket.playerId);
        if (partyId) {
          _leaveParty(socket, partyId);
        }
      }

      for (const [code, room] of mlRoomsByCode.entries()) {
        const idx = room.players.indexOf(socket.id);
        if (idx === -1) continue;

        const laneIndex = room.laneBySocketId.get(socket.id);
        const isActive = gamesByRoomId.has(room.roomId);
        if (isActive && laneIndex !== undefined) {
          const graceKey = `${room.roomId}:${laneIndex}`;
          const playerName = room.playerNames.get(socket.id) || socket.playerDisplayName || "Player";
          io.to(room.roomId).emit("player_disconnected", {
            laneIndex,
            displayName: playerName,
            gracePeriodMs: RECONNECT_GRACE_MS,
          });

          const graceHandle = setTimeout(() => {
            disconnectGrace.delete(graceKey);
            const entry = gamesByRoomId.get(room.roomId);
            const staleRoom = mlRoomsByCode.get(code);
            if (staleRoom) {
              const assignedTeam = staleRoom.playerTeamsBySocketId?.get(socket.id);
              const staleIdx = staleRoom.players.indexOf(socket.id);
              if (staleIdx !== -1) staleRoom.players.splice(staleIdx, 1);
              staleRoom.laneBySocketId.delete(socket.id);
              if (staleRoom.playerTeamsBySocketId) staleRoom.playerTeamsBySocketId.delete(socket.id);
              staleRoom.playerNames.delete(socket.id);
              staleRoom.readySet.delete(socket.id);
              if (Number.isInteger(laneIndex)) {
                staleRoom.loadoutReadyByLane?.delete(laneIndex);
                staleRoom.gameplayReadyByLane?.delete(laneIndex);
                staleRoom.contentProgressByLane?.delete(laneIndex);
              }
              const remainingHumanLaneIndices = staleRoom.players
                .map((sid) => staleRoom.laneBySocketId.get(sid))
                .filter((value) => Number.isInteger(value));
              if (staleRoom._loadoutReadyResolve) {
                const allLoadoutReady = remainingHumanLaneIndices.length === 0
                  || remainingHumanLaneIndices.every((value) => staleRoom.loadoutReadyByLane?.has(value));
                if (allLoadoutReady)
                  staleRoom._loadoutReadyResolve();
              }
              if (staleRoom._gameplayReadyResolve) {
                const allGameplayReady = remainingHumanLaneIndices.length === 0
                  || remainingHumanLaneIndices.every((value) => staleRoom.gameplayReadyByLane?.has(value));
                if (allGameplayReady)
                  staleRoom._gameplayReadyResolve();
              }
              if (entry && attachTakeoverBot && laneIndex !== undefined) {
                const takeover = attachTakeoverBot(staleRoom, entry, laneIndex, {
                  difficulty: DEFAULT_TAKEOVER_DIFFICULTY,
                  displayName: `Takeover AI (${DEFAULT_TAKEOVER_DIFFICULTY})`,
                  humanDisplayName: playerName,
                  playerId: socket.playerId || null,
                  team: assignedTeam,
                });
                if (takeover) {
                  io.to(room.roomId).emit("ml_ai_takeover_started", {
                    laneIndex,
                    displayName: playerName,
                    difficulty: takeover.difficulty,
                  });
                } else if (entry && !entry.eliminatedNotified.has(laneIndex)) {
                  entry.eliminatedNotified.add(laneIndex);
                  io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "forfeit" });
                }
              } else if (entry && !entry.eliminatedNotified.has(laneIndex)) {
                entry.eliminatedNotified.add(laneIndex);
                io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "forfeit" });
              }
              if (staleRoom.players.length === 0 && (staleRoom.aiPlayers || []).length === 0) {
                stopMLGame(room.roomId, code);
                mlRoomsByCode.delete(code);
              }
            }
          }, RECONNECT_GRACE_MS);

          disconnectGrace.set(graceKey, {
            graceHandle,
            expiresAt: Date.now() + RECONNECT_GRACE_MS,
            playerId: socket.playerId || null,
            guestId: socket.guestId,
            playerName,
            code,
            mode: "multilane",
            seatKey: String(laneIndex),
            prevSocketId: socket.id,
          });
        } else {
          room.players.splice(idx, 1);
          room.laneBySocketId.delete(socket.id);
          if (room.playerTeamsBySocketId) room.playerTeamsBySocketId.delete(socket.id);
          room.playerNames.delete(socket.id);
          room.readySet.delete(socket.id);
          if (Number.isInteger(laneIndex)) {
            room.loadoutReadyByLane?.delete(laneIndex);
            room.gameplayReadyByLane?.delete(laneIndex);
            room.contentProgressByLane?.delete(laneIndex);
          }
          const remainingHumanLaneIndices = room.players
            .map((sid) => room.laneBySocketId.get(sid))
            .filter((value) => Number.isInteger(value));
          if (room._loadoutReadyResolve) {
            const allLoadoutReady = remainingHumanLaneIndices.length === 0
              || remainingHumanLaneIndices.every((value) => room.loadoutReadyByLane?.has(value));
            if (allLoadoutReady)
              room._loadoutReadyResolve();
          }
          if (room._gameplayReadyResolve) {
            const allGameplayReady = remainingHumanLaneIndices.length === 0
              || remainingHumanLaneIndices.every((value) => room.gameplayReadyByLane?.has(value));
            if (allGameplayReady)
              room._gameplayReadyResolve();
          }
          io.to(room.roomId).emit("player_left", { code });
          if (room.players.length === 0 && (room.aiPlayers || []).length === 0) {
            stopMLGame(room.roomId, code);
            mlRoomsByCode.delete(code);
          }
        }
        break;
      }
    });
  });

  return { onMatchFound };
}

module.exports = { registerSocketHandlers };
