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

test("legacy public queue formats normalize into the shared FFA survival bucket", () => {
  const normalized = matchQueueManager.normalizeQueueRequest({
    gameType: "line_wars",
    matchFormat: "2v2",
    ranked: true,
  });

  assert.equal(normalized.gameType, "line_wars");
  assert.equal(normalized.matchFormat, "ffa");
  assert.equal(normalized.ranked, true);
  assert.equal(matchQueueManager.makeBucketKey(normalized.gameType, normalized.matchFormat, normalized.ranked), "line_wars:ffa:1");
});

test("public matchmaking assembles solo entrants into one FFA survival match", () => {
  const originalSetInterval = global.setInterval;
  const originalClearInterval = global.clearInterval;
  let tick = null;

  global.setInterval = (fn) => {
    tick = fn;
    return { mocked: true };
  };
  global.clearInterval = () => {};

  const partiesById = new Map([
    ["solo-a", makeParty("solo-a", 1)],
    ["solo-b", makeParty("solo-b", 1)],
    ["solo-c", makeParty("solo-c", 1)],
  ]);
  const socketByPlayerId = new Map();
  const found = [];

  try {
    for (const partyId of partiesById.keys()) {
      matchQueueManager.addToQueue(partyId, {
        gameType: "line_wars",
        matchFormat: "ffa",
        ranked: false,
        partySize: 1,
        rating: 1200,
        queueEnteredAt: Date.now(),
        region: "global",
      });
    }

    matchQueueManager.startMatchmakingLoop({}, partiesById, socketByPlayerId, (...args) => found.push(args));
    assert.ok(typeof tick === "function");
    tick();

    assert.equal(found.length, 1);
    const [, , mode, teams, bucketKey] = found[0];
    assert.equal(mode, "line_wars:ffa:0");
    assert.equal(bucketKey, "line_wars:ffa:0");
    assert.deepEqual(
      teams.map((team) => team.map((ticket) => ticket.partyId)),
      [["solo-a"], ["solo-b"], ["solo-c"]]
    );
  } finally {
    matchQueueManager.stopMatchmakingLoop();
    for (const partyId of partiesById.keys())
      matchQueueManager.removeFromQueue(partyId);
    global.setInterval = originalSetInterval;
    global.clearInterval = originalClearInterval;
  }
});

test("private lobbies always expose the FFA survival ruleset and reject team assignment", () => {
  const lobby = matchQueueManager.createLobby({
    hostSocketId: "host-1",
    hostDisplayName: "Host",
    gameType: "line_wars",
    matchFormat: "2v2",
    pvpMode: "teams",
  });

  try {
    assert.equal(lobby.matchFormat, "ffa");
    assert.equal(lobby.pvpMode, "ffa");

    const updateErr = matchQueueManager.updateLobby("host-1", {
      gameType: "linewars",
      matchFormat: "duel",
      pvpMode: "teams",
    });
    assert.equal(updateErr, null);

    const updated = matchQueueManager.getLobby(lobby.lobbyId);
    assert.equal(updated.gameType, "line_wars");
    assert.equal(updated.matchFormat, "ffa");
    assert.equal(updated.pvpMode, "ffa");

    const snapshot = matchQueueManager.lobbySnapshot(updated);
    assert.equal(snapshot.matchFormat, "ffa");
    assert.equal(snapshot.pvpMode, "ffa");
    assert.equal(snapshot.members[0].team, null);

    assert.equal(
      matchQueueManager.assignTeam("host-1", "host-1", "red"),
      "Team assignment is no longer supported. Matches now use free-for-all survival lanes."
    );
  } finally {
    matchQueueManager.disbandLobby(lobby.lobbyId);
    matchQueueManager.cleanupSocket("host-1");
  }
});
