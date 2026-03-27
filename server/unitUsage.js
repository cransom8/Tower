"use strict";

const VALID_UNIT_USAGE_SCOPES = Object.freeze([
  "wave_only",
  "loadout_only",
  "both",
  "disabled",
]);

function normalizeUnitUsageScope(scope) {
  const value = String(scope || "").trim().toLowerCase();
  if (VALID_UNIT_USAGE_SCOPES.includes(value)) return value;
  return "both";
}

function canUseInLoadout(unitType) {
  if (!unitType || !unitType.enabled) return false;
  const scope = normalizeUnitUsageScope(unitType.usage_scope);
  if (scope !== "loadout_only" && scope !== "both") return false;
  return Number(unitType.build_cost) > 0 && Number(unitType.range) > 0;
}

function canUseInWaves(unitType) {
  if (!unitType || !unitType.enabled) return false;
  const scope = normalizeUnitUsageScope(unitType.usage_scope);
  return scope === "wave_only" || scope === "both";
}

function publicUnitRole(unitType) {
  const scope = normalizeUnitUsageScope(unitType?.usage_scope);
  if (scope === "both") return "hybrid";
  if (scope === "wave_only") return "attacker";
  if (scope === "loadout_only") return "defender";
  return "disabled";
}

module.exports = {
  VALID_UNIT_USAGE_SCOPES,
  normalizeUnitUsageScope,
  canUseInLoadout,
  canUseInWaves,
  publicUnitRole,
};
