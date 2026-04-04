"use strict";

function isSpawnAuditLoggingEnabled(deps = {}) {
  return deps.ENABLE_SPAWN_AUDIT_LOGS === true;
}

function logSpawnAuditInfo(deps = {}, msg, data) {
  const log = deps && deps.log;
  if (!isSpawnAuditLoggingEnabled(deps) || !log || typeof log.info !== "function")
    return;
  log.info(msg, data);
}

function logSpawnAuditLine(deps = {}, line) {
  if (!isSpawnAuditLoggingEnabled(deps))
    return;
  console.log(line);
}

module.exports = {
  isSpawnAuditLoggingEnabled,
  logSpawnAuditInfo,
  logSpawnAuditLine,
};
