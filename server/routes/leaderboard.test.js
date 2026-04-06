const test = require("node:test");
const assert = require("node:assert/strict");

const router = require("./leaderboard");

function getLeaderboardHandler() {
  const layer = router.stack.find((entry) => entry.route && entry.route.path === "/" && entry.route.methods.get);
  return layer.route.stack[0].handle;
}

function createResponse() {
  return {
    statusCode: 200,
    body: null,
    status(code) {
      this.statusCode = code;
      return this;
    },
    json(payload) {
      this.body = payload;
      return this;
    },
  };
}

test("leaderboard honors injected db, requested page size, and stable ranking order", async () => {
  const handler = getLeaderboardHandler();
  const queries = [];
  const db = {
    async query(sql, params) {
      queries.push({ sql, params });
      if (sql.includes("FROM seasons")) {
        return {
          rows: [
            {
              id: "season-7",
              name: "Season 7",
              start_date: "2026-04-01T00:00:00.000Z",
            },
          ],
        };
      }

      if (sql.includes("COUNT(*)::int AS total"))
        return { rows: [{ total: 3 }] };

      return {
        rows: [
          { id: "p2", display_name: "Bravo", region: "na", rating: 1550, wins: 8, losses: 3, rank: 3 },
          { id: "p3", display_name: "Charlie", region: "na", rating: 1520, wins: 7, losses: 4, rank: 4 },
        ],
      };
    },
  };

  const req = {
    query: {
      mode: "ffa_ranked",
      region: "na",
      page: "2",
      limit: "2",
    },
    app: {
      locals: {
        db,
      },
    },
  };
  const res = createResponse();

  await handler(req, res);

  assert.equal(res.statusCode, 200);
  assert.equal(res.body.page, 2);
  assert.equal(res.body.total, 3);
  assert.equal(res.body.pageSize, 2);
  assert.equal(res.body.season.name, "Season 7");
  assert.equal(res.body.entries.length, 2);
  assert.deepEqual(queries[1].params, ["ffa_ranked", 2, 2, "na"]);
  assert.match(queries[1].sql, /ORDER BY r\.rating DESC, r\.wins DESC, p\.display_name ASC, p\.id ASC/);
});
