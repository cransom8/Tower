"use strict";

const BUILDING_LIFECYCLE_STATES = Object.freeze({
  beingBuilt: "being_built",
  active: "active",
  destroyed: "destroyed",
  underRepair: "under_repair",
});

function normalizeBuildingLifecycleState(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case BUILDING_LIFECYCLE_STATES.beingBuilt:
    case BUILDING_LIFECYCLE_STATES.active:
    case BUILDING_LIFECYCLE_STATES.destroyed:
    case BUILDING_LIFECYCLE_STATES.underRepair:
      return normalized;
    default:
      return null;
  }
}

function resolveBuildingLifecycleState(options = {}) {
  const explicitState = normalizeBuildingLifecycleState(options.lifecycleState);
  const built = !!options.built;
  const constructionInProgress = !!options.constructionInProgress;
  const hp = Math.max(0, Math.floor(Number(options.hp) || 0));
  const maxHp = Math.max(0, Math.floor(Number(options.maxHp) || 0));

  if (!built)
    return constructionInProgress ? BUILDING_LIFECYCLE_STATES.beingBuilt : null;

  if (hp <= 0)
    return BUILDING_LIFECYCLE_STATES.destroyed;

  if (explicitState === BUILDING_LIFECYCLE_STATES.underRepair && hp < maxHp)
    return BUILDING_LIFECYCLE_STATES.underRepair;

  return BUILDING_LIFECYCLE_STATES.active;
}

function resolveLifecycleStateAfterDamage(currentState, options = {}) {
  return resolveBuildingLifecycleState({
    lifecycleState: normalizeBuildingLifecycleState(currentState),
    built: !!options.built,
    constructionInProgress: !!options.constructionInProgress,
    hp: options.hp,
    maxHp: options.maxHp,
  });
}

function resolveLifecycleStateAfterRepair(options = {}) {
  return resolveBuildingLifecycleState({
    lifecycleState: BUILDING_LIFECYCLE_STATES.underRepair,
    built: !!options.built,
    constructionInProgress: !!options.constructionInProgress,
    hp: options.hp,
    maxHp: options.maxHp,
  });
}

function resolveLifecycleStateAfterConstructionStart(alreadyBuilt = false, currentState = null) {
  return alreadyBuilt
    ? normalizeBuildingLifecycleState(currentState) || BUILDING_LIFECYCLE_STATES.active
    : BUILDING_LIFECYCLE_STATES.beingBuilt;
}

function resolveLifecycleStateAfterConstructionComplete() {
  return BUILDING_LIFECYCLE_STATES.active;
}

module.exports = {
  BUILDING_LIFECYCLE_STATES,
  normalizeBuildingLifecycleState,
  resolveBuildingLifecycleState,
  resolveLifecycleStateAfterDamage,
  resolveLifecycleStateAfterRepair,
  resolveLifecycleStateAfterConstructionStart,
  resolveLifecycleStateAfterConstructionComplete,
};
