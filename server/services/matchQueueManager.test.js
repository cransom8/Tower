const test = require("node:test");
const assert = require("node:assert/strict");

const matchQueueManager = require("./matchQueueManager");

function makeParty(id, size) {
  return {
    id,
    members: Array.from({ length: size }, (_, idx) => ({
      playerId: `${id}-p${idx}`,
      socketId: `${id}-s${idx}`,
      displayName: `${id}-${idx}`,
    })),
  };
}

test("2v2 matcher can skip an incompatible duo and use a later compatible duo with solos", () => {
  const originalSetInterval = global.setInterval;
  const originalClearInterval = global.clearInterval;
  let tick = null;

  global.setInterval = (fn) => {
    tick = fn;
    return { mocked: true };
  };
  global.clearInterval = () => {};

  const partiesById = new Map([
    ["duo-high", makeParty("duo-high", 2)],
    ["duo-fit", makeParty("duo-fit", 2)],
    ["solo-a", makeParty("solo-a", 1)],
    ["solo-b", makeParty("solo-b", 1)],
  ]);
  const socketByPlayerId = new Map();
  const found = [];

  try {
    matchQueueManager.addToQueue("duo-high", {
      gameType: "line_wars",
      matchFormat: "2v2",
      ranked: false,
      partySize: 2,
      rating: 1800,
      queueEnteredAt: Date.now(),
      region: "global",
    });
    matchQueueManager.addToQueue("duo-fit", {
      gameType: "line_wars",
      matchFormat: "2v2",
      ranked: false,
      partySize: 2,
      rating: 1200,
      queueEnteredAt: Date.now(),
      region: "global",
    });
    matchQueueManager.addToQueue("solo-a", {
      gameType: "line_wars",
      matchFormat: "2v2",
      ranked: false,
      partySize: 1,
      rating: 1180,
      queueEnteredAt: Date.now(),
      region: "global",
    });
    matchQueueManager.addToQueue("solo-b", {
      gameType: "line_wars",
      matchFormat: "2v2",
      ranked: false,
      partySize: 1,
      rating: 1220,
      queueEnteredAt: Date.now(),
      region: "global",
    });

    matchQueueManager.startMatchmakingLoop({}, partiesById, socketByPlayerId, (...args) => found.push(args));
    assert.ok(typeof tick === "function");
    tick();

    assert.equal(found.length, 1);
    const [, , mode, teams, bucketKey] = found[0];
    assert.equal(mode, "line_wars:2v2:0");
    assert.equal(bucketKey, "line_wars:2v2:0");
    assert.deepEqual(
      teams.map((team) => team.map((ticket) => ticket.partyId)),
      [["duo-fit"], ["solo-a", "solo-b"]]
    );
    assert.equal(matchQueueManager.getQueueEntry("duo-high")?.partyId, "duo-high");
    assert.equal(matchQueueManager.getQueueEntry("duo-fit"), null);
    assert.equal(matchQueueManager.getQueueEntry("solo-a"), null);
    assert.equal(matchQueueManager.getQueueEntry("solo-b"), null);
  } finally {
    matchQueueManager.stopMatchmakingLoop();
    ["duo-high", "duo-fit", "solo-a", "solo-b"].forEach((partyId) => matchQueueManager.removeFromQueue(partyId));
    global.setInterval = originalSetInterval;
    global.clearInterval = originalClearInterval;
  }
});

test("lobby updates normalize aliased match formats", () => {
  const lobby = matchQueueManager.createLobby({
    hostSocketId: "host-1",
    hostDisplayName: "Host",
    gameType: "line_wars",
    matchFormat: "2v2",
    pvpMode: "teams",
  });

  try {
    const err = matchQueueManager.updateLobby("host-1", {
      gameType: "linewars",
      matchFormat: "duel",
    });

    assert.equal(err, null);
    const updated = matchQueueManager.getLobby(lobby.lobbyId);
    assert.equal(updated.gameType, "line_wars");
    assert.equal(updated.matchFormat, "1v1");
    assert.equal(updated.pvpMode, "1v1");
  } finally {
    matchQueueManager.disbandLobby(lobby.lobbyId);
    matchQueueManager.cleanupSocket("host-1");
  }
});
