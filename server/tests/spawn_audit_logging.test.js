"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const {
  isSpawnAuditLoggingEnabled,
  logSpawnAuditInfo,
  logSpawnAuditLine,
} = require("../game/multilane/spawnAuditLogging");

test("spawn audit logging stays disabled by default", () => {
  const calls = [];
  logSpawnAuditInfo({
    log: {
      info(msg, data) {
        calls.push({ msg, data });
      },
    },
  }, "[SpawnAudit][ServerQueue] queued", { unitId: "wu1" });

  assert.equal(isSpawnAuditLoggingEnabled({}), false);
  assert.deepEqual(calls, []);
});

test("spawn audit logging becomes available when explicitly enabled", () => {
  const calls = [];
  const deps = {
    ENABLE_SPAWN_AUDIT_LOGS: true,
    log: {
      info(msg, data) {
        calls.push({ msg, data });
      },
    },
  };

  logSpawnAuditInfo(deps, "[SpawnAudit][ServerRoute] assigned", { unitId: "wu2" });

  assert.equal(isSpawnAuditLoggingEnabled(deps), true);
  assert.deepEqual(calls, [
    {
      msg: "[SpawnAudit][ServerRoute] assigned",
      data: { unitId: "wu2" },
    },
  ]);
});

test("line-based trace logging also stays opt-in", () => {
  const originalConsoleLog = console.log;
  const lines = [];
  console.log = (line) => lines.push(line);
  try {
    logSpawnAuditLine({}, "[BarracksTrace][ServerTimer] muted");
    logSpawnAuditLine({ ENABLE_SPAWN_AUDIT_LOGS: true }, "[BarracksTrace][ServerTimer] enabled");
  } finally {
    console.log = originalConsoleLog;
  }

  assert.deepEqual(lines, ["[BarracksTrace][ServerTimer] enabled"]);
});
