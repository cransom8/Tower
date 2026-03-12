"use strict";

const crypto = require("crypto");

function registerSocketHandlers({
  authService,
  checkActionRateLimit,
  checkLobbyRateLimit,
  createMLRoom,
  db,
  disconnectGrace,
  ffaTeamForLane,
  gamesByRoomId,
  generateCode,
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
  validateLoadoutSelection,
  validateMlTeamSetup,
  verifyReconnectToken,
  applyMultilaneAction,
  RECONNECT_GRACE_MS,
  isEnabled,
}) {
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
    const teamAssignments = [];
    const ffaColors = ["red", "blue", "green", "orange"];

    teams.forEach((team, teamIdx) => {
      team.forEach((ticket) => {
        const party = partiesById.get(ticket.partyId);
        if (!party) return;
        party.members.forEach((member) => {
          const sid = socketByPlayerId.get(member.playerId) || member.socketId;
          if (!sid) return;
          allSocketIds.push({ sid, name: member.displayName, partyId: ticket.partyId });
          teamAssignments.push(
            matchFormat === "ffa"
              ? ffaColors[teamIdx % ffaColors.length]
              : teamIdx === 0
                ? "red"
                : "blue"
          );
        });
      });
    });

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
      room.pvpMode = matchFormat === "ffa" ? "ffa" : "2v2";

      sessionBySocketId.set(host.sid, { code, roomId, laneIndex: 0, mode: "multilane" });
      room.playerTeamsBySocketId.set(host.sid, teamAssignments[0]);
      const hostSock = io.sockets.sockets.get(host.sid);
      if (hostSock) hostSock.join(roomId);

      for (let i = 1; i < allSocketIds.length; i += 1) {
        const { sid, name } = allSocketIds[i];
        room.players.push(sid);
        room.laneBySocketId.set(sid, i);
        room.playerNames.set(sid, sanitizeDisplayName(name));
        room.playerTeamsBySocketId.set(sid, teamAssignments[i]);
        sessionBySocketId.set(sid, { code, roomId, laneIndex: i, mode: "multilane" });
        const sock = io.sockets.sockets.get(sid);
        if (sock) sock.join(roomId);
      }

      io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

      allSocketIds.forEach(({ sid }, laneIndex) => {
        const myTeam = allSocketIds
          .filter((_, idx) => teamAssignments[idx] === teamAssignments[laneIndex])
          .map((entry) => entry.name);
        const enemies = allSocketIds
          .filter((_, idx) => teamAssignments[idx] !== teamAssignments[laneIndex])
          .map((entry) => entry.name);
        io.to(sid).emit("match_found", {
          roomCode: code,
          laneIndex,
          teammates: myTeam,
          opponents: enemies,
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

    const hostSocketId = socketByPlayerId.get(allMembers[0].playerId) || allMembers[0].socketId;
    const hostDisplayName = sanitizeDisplayName(allMembers[0].displayName);
    const { code, roomId } = createMLRoom(hostSocketId, hostDisplayName, {});
    const room = mlRoomsByCode.get(code);
    room.queueMode = mode;
    room.partyASize = partyA.members.length;

    sessionBySocketId.set(hostSocketId, { code, roomId, laneIndex: 0, mode: "multilane" });
    room.playerTeamsBySocketId.set(hostSocketId, "red");
    const hostSocket = io.sockets.sockets.get(hostSocketId);
    if (hostSocket) hostSocket.join(roomId);

    let laneIndex = 1;
    for (let i = 1; i < allMembers.length; i += 1) {
      const member = allMembers[i];
      const sid = socketByPlayerId.get(member.playerId) || member.socketId;
      if (!sid) {
        laneIndex += 1;
        continue;
      }
      room.players.push(sid);
      room.laneBySocketId.set(sid, laneIndex);
      room.playerNames.set(sid, sanitizeDisplayName(member.displayName));
      room.playerTeamsBySocketId.set(sid, i < partyA.members.length ? "red" : "blue");
      sessionBySocketId.set(sid, { code, roomId, laneIndex, mode: "multilane" });
      const memberSocket = io.sockets.sockets.get(sid);
      if (memberSocket) memberSocket.join(roomId);
      laneIndex += 1;
    }

    io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

    const partyANames = partyA.members.map((member) => member.displayName);
    const partyBNames = partyB.members.map((member) => member.displayName);

    partyA.members.forEach((member, idx) => {
      const sid = socketByPlayerId.get(member.playerId) || member.socketId;
      if (!sid) return;
      io.to(sid).emit("match_found", {
        roomCode: code,
        laneIndex: idx,
        teammates: partyANames,
        opponents: partyBNames,
      });
    });

    partyB.members.forEach((member, idx) => {
      const sid = socketByPlayerId.get(member.playerId) || member.socketId;
      if (!sid) return;
      io.to(sid).emit("match_found", {
        roomCode: code,
        laneIndex: partyA.members.length + idx,
        teammates: partyBNames,
        opponents: partyANames,
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

        const grace = disconnectGrace.get(graceKey);
        if (grace) {
          clearTimeout(grace.graceHandle);
          disconnectGrace.delete(graceKey);
        }

        const playerName = grace?.playerName || socket.playerDisplayName || "Player";
        if (!room.players.includes(socket.id)) room.players.push(socket.id);
        room.laneBySocketId.set(socket.id, laneIndex);
        if (room.playerTeamsBySocketId && !room.playerTeamsBySocketId.has(socket.id)) {
          room.playerTeamsBySocketId.set(socket.id, pickBalancedMlTeam(room));
        }
        room.playerNames.set(socket.id, playerName);
        socket.join(roomId);
        sessionBySocketId.set(socket.id, { code, roomId, laneIndex, mode: "multilane" });

        const humanPlayers = room.players.map((sid) => ({
          laneIndex: room.laneBySocketId.get(sid),
          displayName: room.playerNames.get(sid) || "Player",
          ready: true,
          isAI: false,
          team: room.playerTeamsBySocketId?.get(sid) || "red",
        }));
        const aiPlayers = (room.aiPlayers || []).map((ai) => ({
          laneIndex: ai.laneIndex,
          displayName: `CPU (${String(ai.difficulty).charAt(0).toUpperCase()}${String(ai.difficulty).slice(1)})`,
          ready: true,
          isAI: true,
          team: ai.team || "red",
        }));
        const laneAssignments = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);

        socket.emit("rejoin_ack", { success: true, mode: "multilane", laneIndex, code });
        socket.emit("ml_match_ready", { code, playerCount: laneAssignments.length, laneAssignments });
        const reconnectConfig = simMl.createMLPublicConfig(room.settings);
        if (room.loadoutByLane && room.loadoutByLane[laneIndex]) {
          reconnectConfig.loadout = room.loadoutByLane[laneIndex];
        }
        socket.emit("ml_match_config", reconnectConfig);
        socket.emit("ml_state_snapshot", simMl.createMLSnapshot(entry.game));
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
        const normalizedSettings = normalizeMatchSettings(settings);
        socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
        socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
        const { code, roomId } = createMLRoom(socket.id, playerDisplayName, normalizedSettings);
        const room = mlRoomsByCode.get(code);
        sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
        socket.join(roomId);
        const botCfgs = Array.isArray(filters.botConfigs) && filters.botConfigs.length > 0
          ? filters.botConfigs
          : [{ difficulty: "medium" }];
        const validDiffs = ["easy", "medium", "hard"];
        for (const bot of botCfgs) {
          const difficulty = validDiffs.includes(bot.difficulty) ? bot.difficulty : "medium";
          const totalPlayers = room.players.length + (room.aiPlayers || []).length;
          if (totalPlayers >= 4) break;
          if (!room.aiPlayers) room.aiPlayers = [];
          room.aiPlayers.push({ laneIndex: totalPlayers, difficulty, team: pickBalancedMlTeam(room) });
        }
        socket.emit("match_found", {
          roomCode: code,
          laneIndex: 0,
          teammates: [playerDisplayName],
          opponents: (room.aiPlayers || []).map((ai) => `CPU (${ai.difficulty})`),
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
          const normalizedSettings = normalizeMatchSettings(settings);
          const { code, roomId } = createMLRoom(socket.id, playerDisplayName, normalizedSettings);
          const room = mlRoomsByCode.get(code);
          room.pvpMode = pvpMode === "ffa" ? "ffa" : "2v2";
          socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
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
          const assignedTeam = room.pvpMode === "ffa" ? ffaTeamForLane(laneIndex) : pickBalancedMlTeam(room);
          room.players.push(socket.id);
          room.laneBySocketId.set(socket.id, laneIndex);
          room.playerTeamsBySocketId.set(socket.id, assignedTeam);
          const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
          room.playerNames.set(socket.id, playerDisplayName);
          if (room.aiPlayers) room.aiPlayers.forEach((ai, idx) => { ai.laneIndex = room.players.length + idx; });
          socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
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
        const validModes = new Set(["ranked_2v2", "casual_2v2"]);
        if (!validModes.has(mode)) {
          return socket.emit("error_message", { message: "This public queue mode is not available." });
        }
        const isRanked = mode === "ranked_2v2";
        const queueMode = matchmaker.makeBucketKey("line_wars", "2v2", isRanked);
        const dbMode = isRanked ? "2v2_ranked" : "2v2_casual";

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
              "SELECT COALESCE(wins + losses, 0) AS matches FROM ratings WHERE player_id = $1 AND mode = '2v2_casual'",
              [socket.playerId]
            ).then((result) => Number(result.rows[0]?.matches) || 0).catch(() => 0),
            db.query(
              "SELECT rating, wins + losses AS matches FROM ratings WHERE player_id = $1 AND mode = '2v2_ranked'",
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

          party.status = "queued";
          party.queueMode = queueMode;
          party.queueEnteredAt = Date.now();
          socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
          socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

          matchmaker.addToQueue(partyId, {
            gameType: "line_wars",
            matchFormat: "2v2",
            ranked: isRanked,
            rating,
            queueEnteredAt: party.queueEnteredAt,
            region: party.region,
            isPlacement: isRanked && rankedMatches < 10,
          });

          const queueSize = matchmaker.getQueuePlayerCount(queueMode, partiesById);
          io.to(`party:${partyId}`).emit("queue_status", { status: "queued", mode: queueMode, elapsed: 0, queueSize });
          log.info("queue entered", { partyId, mode: queueMode, isPlacement: isRanked && rankedMatches < 10 });
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
      const { GAME_TYPES, MATCH_FORMATS, validatePublicPartySize } = matchmaker;
      if (!GAME_TYPES.has(gameType)) return socket.emit("error_message", { message: "Invalid game type." });
      if (!MATCH_FORMATS.has(matchFormat)) return socket.emit("error_message", { message: "Invalid match format." });

      const isRankedReq = !!ranked;
      if (isRankedReq && !isEnabled("ranked_queue_enabled"))
        return socket.emit("error_message", { message: "Ranked queue is currently disabled." });
      if (!isRankedReq && !isEnabled("casual_queue_enabled"))
        return socket.emit("error_message", { message: "Casual queue is currently disabled." });
      if (matchFormat === "ffa" && !isEnabled("public_ffa_enabled"))
        return socket.emit("error_message", { message: "Public FFA matchmaking is currently disabled." });
      if (matchFormat === "1v1" && isRankedReq && !isEnabled("1v1_ranked_enabled"))
        return socket.emit("error_message", { message: "Ranked 1v1 is not yet enabled." });

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

          party.status = "queued";
          party.queueMode = matchmaker.makeBucketKey(gameType, matchFormat, isRanked);
          party.queueEnteredAt = Date.now();
          socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
          socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

          matchmaker.addToQueue(partyId, {
            gameType,
            matchFormat,
            ranked: isRanked,
            rating,
            partySize: party.members.length,
            queueEnteredAt: party.queueEnteredAt,
            region: party.region || "global",
            isPlacement: isRanked && rankedMatches < 10,
          });

          const bucketKey = matchmaker.makeBucketKey(gameType, matchFormat, isRanked);
          const queueSize = matchmaker.getQueuePlayerCount(bucketKey, partiesById);
          io.to(`party:${partyId}`).emit("queue_status", { status: "queued", mode: bucketKey, elapsed: 0, queueSize });
          log.info("queue:enter_v2", { partyId, gameType, matchFormat, ranked: isRanked });
        });
      });
    });

    socket.on("lobby:create", ({ gameType, matchFormat, pvpMode, displayName, settings, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      if (!isEnabled("private_match_enabled"))
        return socket.emit("error_message", { message: "Private lobbies are currently disabled." });
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const lobbyName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
      socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;

      const existingLobby = matchmaker.getLobbyForSocket(socket.id);
      if (existingLobby) {
        const { lobby, disbanded } = matchmaker.leaveLobby(socket.id);
        if (lobby && !disbanded) {
          socket.leave(`lobby:${lobby.lobbyId}`);
          io.to(`lobby:${lobby.lobbyId}`).emit("lobby_update", { lobby: matchmaker.lobbySnapshot(lobby) });
        }
      }

      const lobby = matchmaker.createLobby({
        hostSocketId: socket.id,
        hostDisplayName: lobbyName,
        gameType: gameType || "line_wars",
        matchFormat: matchFormat || "2v2",
        pvpMode: pvpMode || matchFormat || "2v2",
        settings: settings ? normalizeMatchSettings(settings) : {},
      });
      socket.join(`lobby:${lobby.lobbyId}`);
      socket.emit("lobby_created", { lobbyId: lobby.lobbyId, code: lobby.code, lobby: matchmaker.lobbySnapshot(lobby) });
      log.info("lobby created", { lobbyId: lobby.lobbyId, code: lobby.code });
    });

    socket.on("lobby:join", ({ code, displayName, loadoutSlot, unitTypeIds } = {}) => {
      if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
        return socket.emit("error_message", { message: "Too many requests. Please wait." });
      }
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const normalizedCode = String(code || "").trim().toUpperCase();
      if (normalizedCode.length !== 6) return socket.emit("lobby_error", { message: "Invalid lobby code." });
      const lobbyName = sanitizeDisplayName(displayName || socket.playerDisplayName || "Player");
      socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
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
      const updateErr = matchmaker.updateLobby(socket.id, {
        gameType,
        matchFormat,
        pvpMode,
        settings: settings ? normalizeMatchSettings(settings) : undefined,
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
      socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : socket.pendingLoadoutSlot;
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : socket.pendingUnitTypeIds;

      const hostName = sanitizeDisplayName(launchLobby.members.get(socket.id)?.name || "Player");


      const { code, roomId } = createMLRoom(socket.id, hostName, normalizeMatchSettings(launchLobby.settings));
      const room = mlRoomsByCode.get(code);
      room.pvpMode = launchLobby.pvpMode === "ffa" ? "ffa" : (launchLobby.matchFormat || "2v2");

      sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
      const hostDefaultTeam = room.pvpMode === "ffa" ? "red" : "red";
      room.playerTeamsBySocketId.set(socket.id, launchLobby.teamAssignments.get(socket.id) || hostDefaultTeam);
      socket.join(roomId);

      let laneIndex = 1;
      for (const [memberSid, metadata] of launchLobby.members) {
        if (memberSid === socket.id) continue;
        room.players.push(memberSid);
        room.laneBySocketId.set(memberSid, laneIndex);
        room.playerNames.set(memberSid, sanitizeDisplayName(metadata.name));
        const defaultTeam = room.pvpMode === "ffa"
          ? ["red", "blue", "green", "orange"][laneIndex % 4]
          : (laneIndex < 2 ? "red" : "blue");
        room.playerTeamsBySocketId.set(memberSid, launchLobby.teamAssignments.get(memberSid) || defaultTeam);
        sessionBySocketId.set(memberSid, { code, roomId, laneIndex, mode: "multilane" });
        const memberSock = io.sockets.sockets.get(memberSid);
        if (memberSock) memberSock.join(roomId);
        laneIndex += 1;
      }

      if (!room.aiPlayers) room.aiPlayers = [];
      for (const bot of launchLobby.botSlots) {
        const total = room.players.length + room.aiPlayers.length;
        if (total >= 4) break;
        room.aiPlayers.push({ laneIndex: total, difficulty: bot.difficulty, team: pickBalancedMlTeam(room) });
      }

      io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

      const allNames = [...launchLobby.members.values()].map((member) => member.name);
      const botNames = launchLobby.botSlots.map((bot) => `CPU (${bot.difficulty})`);
      let idx = 0;
      for (const [memberSid] of launchLobby.members) {
        io.to(memberSid).emit("match_found", {
          roomCode: code,
          laneIndex: idx,
          teammates: allNames,
          opponents: botNames,
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
      const normalizedSettings = normalizeMatchSettings(settings);
      const { code, roomId } = createMLRoom(socket.id, displayName, normalizedSettings);
      socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
      sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
      socket.join(roomId);
      const room = mlRoomsByCode.get(code);
      room.pvpMode = pvpMode === "ffa" ? "ffa" : "2v2";
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
      const assignedTeam = room.pvpMode === "ffa" ? ffaTeamForLane(laneIndex) : pickBalancedMlTeam(room);
      room.players.push(socket.id);
      room.laneBySocketId.set(socket.id, laneIndex);
      room.playerTeamsBySocketId.set(socket.id, assignedTeam);
      room.playerNames.set(socket.id, sanitizeDisplayName(displayName));
      if (room.aiPlayers) room.aiPlayers.forEach((ai, idx) => { ai.laneIndex = room.players.length + idx; });
      socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
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
      if (room.pvpMode !== "ffa" && totalPlayers >= 2 && room.readySet.size === room.players.length) {
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
      if (totalPlayers < 2) {
        return socket.emit("error_message", { message: "Need at least 2 players (add AI or invite another)." });
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
      room.settings = normalizeMatchSettings(settings);
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
      const validDifficulties = ["easy", "medium", "hard"];
      const diff = validDifficulties.includes(String(difficulty)) ? String(difficulty) : "easy";
      if (!room.aiPlayers) room.aiPlayers = [];
      const aiTeam = room.pvpMode === "ffa" ? ffaTeamForLane(totalPlayers) : pickBalancedMlTeam(room);
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
      const room = mlRoomsByCode.get(session.code);
      if (!room || room.hostSocketId !== socket.id || gamesByRoomId.has(room.roomId) || room.pvpMode === "ffa") return;

      const normalizedTeam = team === "blue" ? "blue" : "red";
      const targetLane = Number(laneIndex);
      const total = room.players.length + (room.aiPlayers || []).length;
      if (!Number.isInteger(targetLane) || targetLane < 0 || targetLane >= total) return;

      let currentTeam = null;
      let targetSid = null;
      let targetAi = null;

      for (const sid of room.players) {
        if (room.laneBySocketId.get(sid) === targetLane) {
          targetSid = sid;
          currentTeam = room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red";
          break;
        }
      }

      if (!targetSid) {
        targetAi = (room.aiPlayers || []).find((ai) => ai.laneIndex === targetLane) || null;
        if (targetAi) currentTeam = targetAi.team === "blue" ? "blue" : "red";
      }
      if (!currentTeam || currentTeam === normalizedTeam) return;

      const counts = { red: 0, blue: 0 };
      for (const sid of room.players) {
        const assigned = room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red";
        counts[assigned] += 1;
      }
      for (const ai of room.aiPlayers || []) {
        counts[ai.team === "blue" ? "blue" : "red"] += 1;
      }

      counts[currentTeam] -= 1;
      counts[normalizedTeam] += 1;
      const maxTeamSize = Math.ceil(total / 2);
      if (counts.red > maxTeamSize || counts.blue > maxTeamSize) {
        return socket.emit("error_message", { message: `Too many players on one team (max ${maxTeamSize}).` });
      }

      if (targetSid) {
        room.playerTeamsBySocketId.set(targetSid, normalizedTeam);
      } else if (targetAi) {
        targetAi.team = normalizedTeam;
      }

      io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
    });

    socket.on("player_action", ({ type, data }) => {
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
        if (!result.ok) return socket.emit("error_message", { message: result.reason || "Action rejected" });
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
      if (!room || gamesByRoomId.has(room.roomId)) return;
      if (!room.rematchVotes) room.rematchVotes = new Set();
      if (room.rematchVotes.has(socket.id)) return;
      room.rematchVotes.add(socket.id);
      const needed = room.players.length;
      io.to(room.roomId).emit("rematch_vote", { count: room.rematchVotes.size, needed });
      if (room.rematchVotes.size >= needed) {
        room.rematchVotes = null;
        room.readySet = new Set();
        if (room._cleanupHandle) {
          clearTimeout(room._cleanupHandle);
          room._cleanupHandle = null;
        }
        startMLGame(room.roomId, session.code);
      }
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
        if (entry && laneIndex !== undefined && !entry.eliminatedNotified.has(laneIndex)) {
          entry.eliminatedNotified.add(laneIndex);
          io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "quit" });
        }

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

        if (room.players.length === 0) {
          stopMLGame(room.roomId, session.code);
          mlRoomsByCode.delete(session.code);
        } else if (!entry) {
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
            if (entry && !entry.eliminatedNotified.has(laneIndex)) {
              entry.eliminatedNotified.add(laneIndex);
              io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "forfeit" });
              io.to(socket.id).emit("ml_spectator_join", { laneIndex });
            }
            const staleRoom = mlRoomsByCode.get(code);
            if (staleRoom) {
              const staleIdx = staleRoom.players.indexOf(socket.id);
              if (staleIdx !== -1) staleRoom.players.splice(staleIdx, 1);
              staleRoom.laneBySocketId.delete(socket.id);
              if (staleRoom.playerTeamsBySocketId) staleRoom.playerTeamsBySocketId.delete(socket.id);
              staleRoom.playerNames.delete(socket.id);
              staleRoom.readySet.delete(socket.id);
              if (staleRoom.players.length === 0) {
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
          io.to(room.roomId).emit("player_left", { code });
          if (room.players.length === 0) {
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
