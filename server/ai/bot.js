"use strict";

const simMl = require("../sim-multilane");
const { AI_ACTION_TYPE, BOT_DIFFICULTY, DIFFICULTY_KNOBS, COUNT_BUCKETS } = require("./types");
const { createRng, hashSeed } = require("./rng");
const { getPersonalityProfile, choosePersonality } = require("./personalities");
const { COMMAND_STATES, validateActionAgainstGame, createDoNothingAction } = require("./actions");
const { buildObservation, summarizeLaneForAi } = require("./observe");
const targeting = require("./targeting");

const ROLE_KEYS = Object.freeze(["melee", "ranged", "support"]);
const BRANCH_ROLE_MAP = Object.freeze({
  blacksmith: "melee",
  archery_tower: "ranged",
  wizard_tower: "ranged",
  temple: "support",
});

function getLane(game, laneIndex) {
  return game && Array.isArray(game.lanes) ? game.lanes[laneIndex] || null : null;
}

function getRoundStage(roundNumber) {
  const round = Math.max(1, Math.floor(Number(roundNumber) || 1));
  if (round <= 5) return "early";
  if (round <= 14) return "mid";
  return "late";
}

function sumRecentLeaks(runtime, laneIndex, waves = 3) {
  const history = runtime && runtime.laneLeakHistory && runtime.laneLeakHistory[laneIndex];
  if (!Array.isArray(history) || history.length === 0) return 0;
  const count = Math.max(1, Number(waves) || 3);
  let total = 0;
  for (let i = Math.max(0, history.length - count); i < history.length; i += 1)
    total += Number(history[i]) || 0;
  return total;
}

function getPreferredIndex(list, value) {
  if (!Array.isArray(list) || list.length === 0) return 99;
  const index = list.indexOf(value);
  return index >= 0 ? index : 99;
}

function getActionKey(action) {
  if (!action || typeof action !== "object") return AI_ACTION_TYPE.DO_NOTHING;
  return JSON.stringify(action);
}

function getPadsByType(laneSummary, buildingType) {
  if (!laneSummary || !laneSummary.padsByType) return [];
  return Array.isArray(laneSummary.padsByType[buildingType]) ? laneSummary.padsByType[buildingType] : [];
}

function findBuildablePad(laneSummary, buildingType) {
  return getPadsByType(laneSummary, buildingType)
    .filter((pad) => pad && pad.canBuild)
    .sort((a, b) => ((Number(a.gridY) || 0) - (Number(b.gridY) || 0)) || String(a.padId || "").localeCompare(String(b.padId || "")))[0] || null;
}

function findUpgradablePad(laneSummary, buildingType) {
  return getPadsByType(laneSummary, buildingType)
    .filter((pad) => pad && pad.canUpgrade)
    .sort((a, b) => ((Number(a.tier) || 0) - (Number(b.tier) || 0)) || ((Number(a.gridY) || 0) - (Number(b.gridY) || 0)) || String(a.padId || "").localeCompare(String(b.padId || "")))[0] || null;
}

function countBuiltPads(laneSummary, buildingType) {
  return getPadsByType(laneSummary, buildingType).filter((pad) => pad && pad.isBuilt).length;
}

function getSiteSnapshot(laneSummary, barracksId) {
  return (laneSummary && Array.isArray(laneSummary.barracksSites) ? laneSummary.barracksSites : [])
    .find((site) => site && site.barracksId === barracksId) || null;
}

function getRosterEntry(siteSnapshot, rosterKey) {
  return siteSnapshot && Array.isArray(siteSnapshot.roster)
    ? siteSnapshot.roster.find((entry) => entry && entry.rosterKey === rosterKey) || null
    : null;
}

function getBuiltSitesOrdered(laneSummary, profile) {
  const order = Array.isArray(profile && profile.barracksSiteOrder) ? profile.barracksSiteOrder : [];
  return (laneSummary && Array.isArray(laneSummary.barracksSites) ? laneSummary.barracksSites : [])
    .filter((site) => site && site.isBuilt)
    .slice()
    .sort((a, b) => {
      const orderDiff = getPreferredIndex(order, a.barracksId) - getPreferredIndex(order, b.barracksId);
      if (orderDiff !== 0) return orderDiff;
      return (Number(a.sortIndex) || 0) - (Number(b.sortIndex) || 0);
    });
}

function getUnlockedRoleCount(laneSummary, role) {
  return Math.max(
    0,
    Number(
      laneSummary &&
      laneSummary.barracksRosterSummary &&
      laneSummary.barracksRosterSummary.unlockedByRole &&
      laneSummary.barracksRosterSummary.unlockedByRole[role]
    ) || 0
  );
}

function getOwnedRoleCount(laneSummary, role) {
  return Math.max(
    0,
    Number(
      laneSummary &&
      laneSummary.barracksRosterSummary &&
      laneSummary.barracksRosterSummary.byRole &&
      laneSummary.barracksRosterSummary.byRole[role]
    ) || 0
  );
}

function chooseTechSaveTarget(laneSummary, context, profile) {
  if (!laneSummary || !context || !profile)
    return null;

  const targets = [];
  const openingGoals = Array.isArray(profile.openingGoals) ? profile.openingGoals : [];
  const branchPriority = Array.isArray(profile.branchPriority) ? profile.branchPriority : [];

  const addTarget = (label, cost, urgency) => {
    const normalizedCost = Math.max(0, Number(cost) || 0);
    if (normalizedCost <= 0) return;
    targets.push({
      label,
      cost: normalizedCost,
      urgency: Math.max(0, Number(urgency) || 0),
    });
  };

  if (laneSummary.builtBarracksSites <= 0) {
    const centerSite = getSiteSnapshot(laneSummary, "center");
    if (centerSite && centerSite.canBuild)
      addTarget("center_barracks", centerSite.buildCost, 8.5);
  }

  const desiredLumberTier = context.roundStage === "early" ? 1 : context.roundStage === "mid" ? 2 : 3;
  const lumberTier = Number(laneSummary.padTiers.lumber_mill) || 0;
  if (lumberTier <= 0) {
    const lumberPad = findBuildablePad(laneSummary, "lumber_mill");
    if (lumberPad)
      addTarget("lumber_mill", lumberPad.buildCost, 7.8 + profile.ecoWeight * 0.5);
  } else if (lumberTier < desiredLumberTier) {
    const lumberUpgrade = findUpgradablePad(laneSummary, "lumber_mill");
    if (lumberUpgrade && context.ecoWindow)
      addTarget("lumber_mill_upgrade", lumberUpgrade.upgradeCost, 4.2 + profile.ecoWeight * 0.35);
  }

  for (let i = 0; i < openingGoals.length; i += 1) {
    const goal = openingGoals[i];
    if (goal === "center_barracks")
      continue;
    const priorityBonus = Math.max(0, 1.8 - i * 0.28);
    const buildable = findBuildablePad(laneSummary, goal);
    if (!buildable)
      continue;

    const role = BRANCH_ROLE_MAP[goal] || null;
    const roleNeed = role ? Math.max(0, Number(context.roleNeeds[role]) || 0) : 0;
    const unlockedCount = role ? getUnlockedRoleCount(laneSummary, role) : 0;
    const scarcityBonus = !role ? 0 : unlockedCount <= 0 ? 1.9 : unlockedCount === 1 ? 1.0 : 0;
    addTarget(goal, buildable.buildCost, 5.5 + priorityBonus + roleNeed * 1.25 + scarcityBonus);
  }

  for (let i = 0; i < branchPriority.length; i += 1) {
    const buildingType = branchPriority[i];
    if ((Number(laneSummary.padTiers[buildingType]) || 0) > 0)
      continue;
    const buildable = findBuildablePad(laneSummary, buildingType);
    if (!buildable)
      continue;
    const role = BRANCH_ROLE_MAP[buildingType] || null;
    const roleNeed = role ? Math.max(0, Number(context.roleNeeds[role]) || 0) : 0;
    const unlockedCount = role ? getUnlockedRoleCount(laneSummary, role) : 0;
    const scarcityBonus = !role ? 0 : unlockedCount <= 0 ? 1.6 : unlockedCount === 1 ? 0.8 : 0;
    const priorityBonus = Math.max(0, 1.4 - i * 0.22);
    addTarget(buildingType, buildable.buildCost, 4 + priorityBonus + roleNeed + scarcityBonus);
  }

  const townCorePad = findUpgradablePad(laneSummary, "town_core");
  if (townCorePad) {
    const unlockNeed = Math.max(
      0,
      (laneSummary.padTiers.temple <= 0 ? 0.25 : 0) +
      (laneSummary.padTiers.wizard_tower <= 0 ? 0.25 : 0) +
      (laneSummary.heroReady <= 0 ? 0.3 : 0)
    );
    addTarget("town_core_upgrade", townCorePad.upgradeCost, 3 + profile.townCoreUpgradeBias + unlockNeed);
  }

  const centerUpgrade = getSiteSnapshot(laneSummary, "center");
  if (centerUpgrade && centerUpgrade.canUpgrade) {
    addTarget(
      "center_barracks_upgrade",
      centerUpgrade.upgradeCost,
      2.6 + Math.max(0, (laneSummary.barracksRosterSummary.ownedTotal || 0) - 4) * 0.08
    );
  }

  targets.sort((a, b) => {
    if (b.urgency !== a.urgency) return b.urgency - a.urgency;
    if (a.cost !== b.cost) return a.cost - b.cost;
    return String(a.label).localeCompare(String(b.label));
  });
  return targets[0] || null;
}

function chooseCountBucket(maxAffordable, preferredCount) {
  const affordable = Math.max(0, Math.floor(Number(maxAffordable) || 0));
  if (affordable <= 0) return 0;
  const target = Math.max(1, Math.floor(Number(preferredCount) || 1));
  let best = 0;
  for (const bucket of COUNT_BUCKETS) {
    if (bucket <= affordable && bucket <= target)
      best = bucket;
  }
  if (best > 0) return best;
  for (let i = COUNT_BUCKETS.length - 1; i >= 0; i -= 1) {
    if (COUNT_BUCKETS[i] <= affordable)
      return COUNT_BUCKETS[i];
  }
  return 0;
}

class BotBrain {
  constructor(config) {
    const cfg = config && typeof config === "object" ? config : {};
    this.laneIndex = Number(cfg.laneIndex) || 0;
    this.difficulty = String(cfg.difficulty || BOT_DIFFICULTY.MEDIUM).toLowerCase();
    if (!DIFFICULTY_KNOBS[this.difficulty]) this.difficulty = BOT_DIFFICULTY.MEDIUM;

    const seedBase = `${cfg.seed || "bot"}:${this.laneIndex}:${this.difficulty}`;
    this.seed = hashSeed(seedBase);
    this.rng = createRng(this.seed);

    this.tickMs = Math.max(1, Number(cfg.tickMs) || 50);
    this.knobs = DIFFICULTY_KNOBS[this.difficulty];
    this.reactionDelayMs = this.rng.nextInt(this.knobs.reactionDelayMinMs, this.knobs.reactionDelayMaxMs);
    this.reactionDelayTicks = Math.max(1, Math.round(this.reactionDelayMs / this.tickMs));
    this.mistakeRate = Number(this.knobs.mistakeRate) || 0;

    this.personality = choosePersonality(this.rng, cfg.personality);
    this.personalityProfile = getPersonalityProfile(this.personality);
    this.unitDefs = (cfg.unitDefMap && Object.keys(cfg.unitDefMap).length > 0)
      ? cfg.unitDefMap
      : (typeof simMl.getMovingUnitDefMap === "function" ? simMl.getMovingUnitDefMap() : {});

    this.memory = {
      nextThinkTick: 0,
      invalidStreak: 0,
      lastActionTick: -1,
      currentTargetLaneIndex: null,
      targetHoldUntilTick: 0,
      laneSwitches: 0,
      lastTargetLaneIndex: null,
      lastTargetSwitchTick: -1,
      observationDim: 0,
    };
  }

  updateMemory(game, runtime) {
    const obs = buildObservation(game, this.laneIndex, runtime, this.unitDefs);
    this.memory.observationDim = Array.isArray(obs.vector) ? obs.vector.length : 0;
  }

  chooseTarget(game, runtime) {
    const target = targeting.chooseTargetOpponent(game, this.laneIndex, runtime, this.memory, this.rng, this.unitDefs);
    if (target !== this.memory.lastTargetLaneIndex) {
      this.memory.laneSwitches += 1;
      this.memory.lastTargetLaneIndex = target;
      this.memory.lastTargetSwitchTick = Number(game && game.tick) || 0;
    }
    if (runtime && runtime.currentTargetByLane)
      runtime.currentTargetByLane[this.laneIndex] = target;
    return target;
  }

  buildStrategicContext(game, runtime, laneSummary, targetSummary) {
    const roundStage = getRoundStage(game && game.roundNumber);
    const recentLeaks = sumRecentLeaks(runtime, this.laneIndex, 3);
    const currentLifeLoss = Number(runtime && runtime.recentLifeLossByLane && runtime.recentLifeLossByLane[this.laneIndex]) || 0;
    const threatGap = (Number(laneSummary && laneSummary.threat) || 0) - (Number(laneSummary && laneSummary.defense) || 0);
    const targetPressureGap = targetSummary ? (Number(targetSummary.threat) || 0) - (Number(targetSummary.defense) || 0) : 0;
    const crisis = !!laneSummary && (
      laneSummary.coreHpRatio <= 0.28 ||
      currentLifeLoss >= 2 ||
      recentLeaks >= 4 ||
      threatGap >= 6 ||
      laneSummary.hostileUnits >= laneSummary.laneControlledUnits + 6
    );
    const danger = !!laneSummary && (
      crisis ||
      laneSummary.coreHpRatio <= 0.55 ||
      currentLifeLoss > 0 ||
      recentLeaks >= 2 ||
      threatGap >= 2.2 ||
      laneSummary.hostileUnits >= laneSummary.laneControlledUnits + 2
    );
    const pressureWindow = !!laneSummary && !!targetSummary && !danger && (
      targetSummary.coreHpRatio <= 0.82 ||
      targetSummary.recentLeaks > 0 ||
      targetPressureGap >= 1.4 ||
      laneSummary.defense >= laneSummary.threat * 1.12 + 1 ||
      laneSummary.barracksRosterSummary.ownedTotal >= Math.max(4, laneSummary.builtBarracksSites * 3)
    );
    const ecoWindow = !!laneSummary && !danger && !pressureWindow && (
      laneSummary.padTiers.lumber_mill < (roundStage === "early" ? 1 : roundStage === "mid" ? 2 : 3) ||
      laneSummary.income < (roundStage === "early" ? 40 : roundStage === "mid" ? 90 : 150)
    );
    let reserveGold = crisis ? 0 : danger ? 10 : pressureWindow ? 18 : 28;
    reserveGold += roundStage === "late" ? 8 : 0;

    const builtSites = Math.max(0, Number(laneSummary && laneSummary.builtBarracksSites) || 0);
    const currentRosterByRole = laneSummary && laneSummary.barracksRosterSummary
      ? laneSummary.barracksRosterSummary.byRole
      : { melee: 0, ranged: 0, support: 0 };
    const desiredTotalRoster = Math.max(
      3,
      builtSites * (roundStage === "early" ? 3 : roundStage === "mid" ? 5 : 7) +
      (pressureWindow ? 2 : 0) +
      (danger ? 2 : 0)
    );
    const roleNeeds = {};
    for (const role of ROLE_KEYS) {
      let desired = desiredTotalRoster * (Number(this.personalityProfile.roleTargets[role]) || 0);
      if (danger && role === "melee") desired += 1.2;
      if (danger && role === "support") desired += 0.7;
      if (pressureWindow && role === "ranged") desired += 0.8;
      roleNeeds[role] = Math.max(0, desired - (Number(currentRosterByRole[role]) || 0));
    }

    const minimumRosterFloor = danger
      ? 0
      : roundStage === "early"
        ? (builtSites > 0 ? 1 : 0)
        : roundStage === "mid"
          ? 5
          : 8;
    const techSaveTarget = chooseTechSaveTarget(laneSummary, {
      roundStage,
      ecoWindow,
      roleNeeds,
    }, this.personalityProfile);

    return {
      roundStage,
      recentLeaks,
      currentLifeLoss,
      crisis,
      danger,
      pressureWindow,
      ecoWindow,
      reserveGold,
      roleNeeds,
      minimumRosterFloor,
      saveForTechCost: techSaveTarget ? techSaveTarget.cost : 0,
      saveForTechLabel: techSaveTarget ? techSaveTarget.label : null,
      saveForTechUrgency: techSaveTarget ? techSaveTarget.urgency : 0,
    };
  }

  estimateSpend(laneSummary, action) {
    if (!action || typeof action !== "object") return 0;
    if (action.type === AI_ACTION_TYPE.BUILD_PAD || action.type === AI_ACTION_TYPE.UPGRADE_PAD) {
      const pad = (laneSummary && Array.isArray(laneSummary.padSnapshots) ? laneSummary.padSnapshots : [])
        .find((entry) => entry && entry.padId === action.padId);
      if (!pad) return 0;
      return action.type === AI_ACTION_TYPE.BUILD_PAD
        ? Math.max(0, Number(pad.buildCost) || 0)
        : Math.max(0, Number(pad.upgradeCost) || 0);
    }
    if (action.type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE || action.type === AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE) {
      const site = getSiteSnapshot(laneSummary, action.barracksId);
      if (!site) return 0;
      return action.type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE
        ? Math.max(0, Number(site.buildCost) || 0)
        : Math.max(0, Number(site.upgradeCost) || 0);
    }
    if (action.type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT) {
      const site = getSiteSnapshot(laneSummary, action.barracksId);
      const entry = getRosterEntry(site, action.rosterKey);
      return entry ? Math.max(0, Number(entry.buyCost) || 0) * Math.max(1, Number(action.count) || 1) : 0;
    }
    if (action.type === AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO) {
      const hero = (laneSummary && Array.isArray(laneSummary.heroRoster) ? laneSummary.heroRoster : [])
        .find((entry) => entry && entry.heroKey === action.heroKey);
      return hero ? Math.max(0, Number(hero.summonCost) || 0) : 0;
    }
    return 0;
  }

  addCandidate(candidateMap, game, laneSummary, action, score, options = null) {
    const checked = validateActionAgainstGame(game, this.laneIndex, action);
    if (!checked.ok) return;
    const normalized = checked.normalized;
    const key = getActionKey(normalized);
    const existing = candidateMap.get(key);
    const candidate = {
      action: normalized,
      key,
      score: Number(score) || 0,
      spend: this.estimateSpend(laneSummary, normalized),
      critical: !!(options && options.critical),
      defensive: !!(options && options.defensive),
      reserveBreak: !!(options && options.reserveBreak),
    };
    if (!existing || candidate.score > existing.score)
      candidateMap.set(key, candidate);
  }

  rankCandidates(candidates) {
    return (candidates || []).slice().sort((a, b) => {
      if (b.score !== a.score) return b.score - a.score;
      if (a.spend !== b.spend) return a.spend - b.spend;
      return a.key.localeCompare(b.key);
    });
  }

  maybeInjectMistake(sortedCandidates) {
    if (!Array.isArray(sortedCandidates) || sortedCandidates.length === 0) return null;
    if (sortedCandidates.length === 1 || this.rng.next() >= this.mistakeRate)
      return sortedCandidates[0];
    const window = sortedCandidates.slice(1, Math.min(4, sortedCandidates.length));
    return window.length > 0 ? window[this.rng.nextInt(0, window.length - 1)] : sortedCandidates[0];
  }

  canAffordCandidate(laneSummary, candidate, context) {
    const gold = Math.max(0, Number(laneSummary && laneSummary.gold) || 0);
    if (candidate.spend <= 0) return true;
    if (gold < candidate.spend) return false;
    if (candidate.critical || candidate.reserveBreak) return true;
    const reserveFloor = candidate.defensive ? Math.floor(context.reserveGold * 0.4) : context.reserveGold;
    return gold - candidate.spend >= reserveFloor;
  }

  maybeAddLaneCommandCandidate(candidateMap, game, laneSummary, targetLaneIndex, context) {
    if (!laneSummary) return;

    let desiredState = COMMAND_STATES.ATTACK;
    let desiredProgress = 1;
    let score = 2.1 + this.personalityProfile.pressureWeight * 0.6;

    if (context.crisis) {
      desiredState = COMMAND_STATES.RETREAT;
      desiredProgress = this.personalityProfile.retreatProgress;
      score = 10.5 + this.personalityProfile.defenseWeight;
    } else if (context.danger) {
      desiredState = COMMAND_STATES.DEFEND;
      desiredProgress = Math.min(0.72, this.personalityProfile.defendProgress + Math.max(0, laneSummary.pressureGap) * 0.035);
      score = 9.6 + this.personalityProfile.defenseWeight;
    } else if (context.pressureWindow) {
      score = 4.8 + this.personalityProfile.pressureWeight;
    }

    const sameTarget = targetLaneIndex === null || laneSummary.commandTargetLaneIndex === targetLaneIndex;
    const progressDelta = Math.abs((Number(laneSummary.commandAnchorProgress) || 0) - desiredProgress);
    if (laneSummary.commandState === desiredState && sameTarget && progressDelta < 0.06)
      return;

    this.addCandidate(candidateMap, game, laneSummary, {
      type: AI_ACTION_TYPE.SET_LANE_COMMAND,
      commandState: desiredState,
      targetLaneIndex,
      progress: desiredProgress,
    }, score, {
      critical: context.danger,
      defensive: desiredState !== COMMAND_STATES.ATTACK,
      reserveBreak: true,
    });
  }

  maybeAddOpeningCandidates(candidateMap, game, laneSummary, context) {
    if (!laneSummary) return;
    const openingGoals = Array.isArray(this.personalityProfile.openingGoals) ? this.personalityProfile.openingGoals : [];
    for (let i = 0; i < openingGoals.length; i += 1) {
      const goal = openingGoals[i];
      const orderBonus = Math.max(0, 2.8 - i * 0.45);

      if (goal === "center_barracks") {
        const site = getSiteSnapshot(laneSummary, "center");
        if (site && site.canBuild) {
          this.addCandidate(candidateMap, game, laneSummary, {
            type: AI_ACTION_TYPE.BUILD_BARRACKS_SITE,
            barracksId: "center",
          }, 5.2 + orderBonus + this.personalityProfile.pressureWeight * 0.5 + (context.danger ? 0.8 : 0), {
            critical: laneSummary.builtBarracksSites <= 0,
            defensive: context.danger,
          });
        }
        continue;
      }

      const tier = Number(laneSummary.padTiers[goal]) || 0;
      const buildable = findBuildablePad(laneSummary, goal);
      if (tier <= 0 && buildable) {
        const branchRole = BRANCH_ROLE_MAP[goal];
        const roleBonus = branchRole ? Math.max(0, Number(context.roleNeeds[branchRole]) || 0) * 0.9 : 0;
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_PAD,
          padId: buildable.padId,
        }, 4.4 + orderBonus + roleBonus + (goal === "lumber_mill" ? this.personalityProfile.ecoWeight * 0.7 : 0), {
          critical: goal === "lumber_mill" && laneSummary.padTiers.lumber_mill <= 0,
          defensive: goal === "temple" && context.danger,
        });
      }
    }
  }

  maybeAddLumberMillCandidate(candidateMap, game, laneSummary, context) {
    const currentTier = Number(laneSummary && laneSummary.padTiers && laneSummary.padTiers.lumber_mill) || 0;
    const desiredTier = context.roundStage === "early" ? 1 : context.roundStage === "mid" ? 2 : 3;
    if (currentTier <= 0) {
      const buildable = findBuildablePad(laneSummary, "lumber_mill");
      if (buildable) {
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_PAD,
          padId: buildable.padId,
        }, 4.9 + this.personalityProfile.ecoWeight * 0.9 + (context.danger ? 0.3 : 0), {
          critical: true,
        });
      }
      return;
    }

    if (currentTier < desiredTier) {
      const upgradable = findUpgradablePad(laneSummary, "lumber_mill");
      if (upgradable) {
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.UPGRADE_PAD,
          padId: upgradable.padId,
        }, 2.9 + this.personalityProfile.ecoWeight * 0.8 + (context.roundStage === "late" ? 0.4 : 0), {
          reserveBreak: context.ecoWindow && laneSummary.gold > this.personalityProfile.floatGoldSoftCap + 40,
        });
      }
    }
  }

  maybeAddTownCoreCandidate(candidateMap, game, laneSummary, context) {
    const townCorePad = findUpgradablePad(laneSummary, "town_core");
    if (!townCorePad) return;

    let desiredTier = 1;
    if (context.roundStage === "early")
      desiredTier = laneSummary.padTiers.blacksmith > 0 || laneSummary.builtBarracksSites > 0 ? 2 : 1;
    else if (context.roundStage === "mid")
      desiredTier = 3;
    else
      desiredTier = 4;

    const unlockNeed = Math.max(
      0,
      (laneSummary.padTiers.temple <= 0 ? 0.15 : 0) +
      (laneSummary.padTiers.wizard_tower <= 0 ? 0.15 : 0) +
      (laneSummary.heroReady <= 0 ? 0.25 : 0)
    );

    if ((Number(townCorePad.tier) || 0) < desiredTier) {
      this.addCandidate(candidateMap, game, laneSummary, {
        type: AI_ACTION_TYPE.UPGRADE_PAD,
        padId: townCorePad.padId,
      }, 2.1 + this.personalityProfile.townCoreUpgradeBias + unlockNeed + (context.ecoWindow ? 0.5 : 0) + (context.roundStage === "late" ? 0.8 : 0), {
        reserveBreak: context.roundStage === "late" && laneSummary.gold > this.personalityProfile.floatGoldSoftCap + 120,
      });
    }
  }

  maybeAddBranchCandidates(candidateMap, game, laneSummary, context) {
    const branchPriority = Array.isArray(this.personalityProfile.branchPriority) ? this.personalityProfile.branchPriority : [];
    for (let i = 0; i < branchPriority.length; i += 1) {
      const buildingType = branchPriority[i];
      const currentTier = Number(laneSummary.padTiers[buildingType]) || 0;
      const branchRole = BRANCH_ROLE_MAP[buildingType] || null;
      const roleNeed = branchRole ? Math.max(0, Number(context.roleNeeds[branchRole]) || 0) : 0;
      const unlockedCount = branchRole ? getUnlockedRoleCount(laneSummary, branchRole) : 0;
      const scarcityBonus = !branchRole ? 0 : unlockedCount <= 0 ? 1.45 : unlockedCount === 1 ? 0.75 : 0;
      const desiredTier = context.roundStage === "early" ? 1 : context.roundStage === "mid" ? 2 : 3;
      const priorityBonus = Math.max(0, 1.6 - i * 0.28);

      if (currentTier <= 0) {
        const buildable = findBuildablePad(laneSummary, buildingType);
        if (!buildable) continue;
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_PAD,
          padId: buildable.padId,
        }, 2.8 + priorityBonus + roleNeed + scarcityBonus + (context.pressureWindow && branchRole === "ranged" ? 0.6 : 0) + (context.danger && branchRole === "support" ? 0.6 : 0), {
          reserveBreak: context.saveForTechLabel === buildingType,
        });
        continue;
      }

      if (currentTier < desiredTier) {
        const upgradable = findUpgradablePad(laneSummary, buildingType);
        if (!upgradable) continue;
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.UPGRADE_PAD,
          padId: upgradable.padId,
        }, 2 + priorityBonus + roleNeed * 0.8 + (context.roundStage === "late" ? 0.5 : 0), {});
      }
    }
  }

  maybeAddDefenseCandidates(candidateMap, game, laneSummary, context) {
    if (!laneSummary) return;

    const desiredGateCount = context.crisis ? 2 : context.danger ? 1 : 0;
    const desiredWallCount = context.crisis ? 5 : context.danger ? 3 : context.roundStage === "late" ? 2 : 0;
    const desiredTurretCount = context.danger ? 2 : context.pressureWindow ? 1 : 0;

    const gateCount = countBuiltPads(laneSummary, "gate");
    const wallCount = countBuiltPads(laneSummary, "wall");
    const turretCount = countBuiltPads(laneSummary, "turret");

    if (gateCount < desiredGateCount) {
      const gate = findBuildablePad(laneSummary, "gate");
      if (gate) {
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_PAD,
          padId: gate.padId,
        }, 4.8 + (desiredGateCount - gateCount) * 0.5 + this.personalityProfile.defenseWeight * 0.9, {
          critical: context.danger,
          defensive: true,
          reserveBreak: context.danger,
        });
      }
    }

    if (wallCount < desiredWallCount) {
      const wall = findBuildablePad(laneSummary, "wall");
      if (wall) {
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_PAD,
          padId: wall.padId,
        }, 4.1 + (desiredWallCount - wallCount) * 0.35 + this.personalityProfile.defenseWeight * 0.7, {
          defensive: true,
          reserveBreak: context.crisis,
        });
      }
    }

    if (turretCount < desiredTurretCount) {
      const turret = findBuildablePad(laneSummary, "turret");
      if (turret) {
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_PAD,
          padId: turret.padId,
        }, 3.7 + (desiredTurretCount - turretCount) * 0.45 + (context.pressureWindow ? 0.5 : 0), {
          defensive: context.danger,
        });
      }
    }

    if (context.danger) {
      for (const buildingType of ["gate", "wall", "turret"]) {
        const upgradable = findUpgradablePad(laneSummary, buildingType);
        if (!upgradable) continue;
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.UPGRADE_PAD,
          padId: upgradable.padId,
        }, 3 + this.personalityProfile.defenseWeight * 0.7 + Math.max(0, laneSummary.pressureGap) * 0.1, {
          defensive: true,
          reserveBreak: context.crisis,
        });
      }
    }
  }

  maybeAddBarracksCandidates(candidateMap, game, laneSummary, context) {
    const siteOrder = Array.isArray(this.personalityProfile.barracksSiteOrder)
      ? this.personalityProfile.barracksSiteOrder
      : ["center", "left", "right"];

    for (let i = 0; i < siteOrder.length; i += 1) {
      const barracksId = siteOrder[i];
      const site = getSiteSnapshot(laneSummary, barracksId);
      if (!site) continue;

      if (site.canBuild) {
        const score = (barracksId === "center" ? 4.7 : 2.4)
          + Math.max(0, 1.4 - i * 0.35)
          + this.personalityProfile.pressureWeight * 0.35
          + (laneSummary.builtBarracksSites <= 0 && barracksId === "center" ? 0.9 : 0);
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUILD_BARRACKS_SITE,
          barracksId,
        }, score, {
          critical: laneSummary.builtBarracksSites <= 0 && barracksId === "center",
          defensive: context.danger && barracksId === "center",
        });
      }

      if (site.canUpgrade && site.isBuilt) {
        const rosterWeight = laneSummary.barracksRosterSummary.ownedTotal >= laneSummary.builtBarracksSites * 3 ? 0.8 : 0.2;
        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE,
          barracksId,
        }, 2.3 + rosterWeight + (context.pressureWindow ? 0.4 : 0) + (context.roundStage === "late" ? 0.5 : 0), {});
      }
    }
  }

  maybeAddRosterCandidates(candidateMap, game, laneSummary, context) {
    const builtSites = getBuiltSitesOrdered(laneSummary, this.personalityProfile);
    if (builtSites.length === 0) return;
    if (Math.max(0, laneSummary.gold - context.reserveGold) <= 0 && !context.danger) return;

    const totalOwned = Math.max(0, Number(laneSummary.barracksRosterSummary.ownedTotal) || 0);
    const shouldSaveForTech = !context.danger &&
      context.saveForTechCost > 0 &&
      laneSummary.gold < context.saveForTechCost &&
      totalOwned >= context.minimumRosterFloor;
    if (shouldSaveForTech)
      return;

    for (let siteIndex = 0; siteIndex < builtSites.length; siteIndex += 1) {
      const site = builtSites[siteIndex];
      const sitePriority = Math.max(0, 0.7 - siteIndex * 0.18);

      for (const entry of site.roster || []) {
        if (!entry || !entry.unlocked || !entry.buyCost) continue;
        const roleNeed = Math.max(0, Number(context.roleNeeds[entry.role]) || 0);
        const unlockedRoleCount = getUnlockedRoleCount(laneSummary, entry.role);
        const ownedRoleCount = getOwnedRoleCount(laneSummary, entry.role);
        const preferredIndex = getPreferredIndex(this.personalityProfile.preferredRoster, entry.rosterKey);
        const preferredBonus = preferredIndex < 99 ? Math.max(0, 1.3 - preferredIndex * 0.12) : 0;

        let desiredCount = 1;
        if (context.danger) desiredCount = 3;
        if (context.pressureWindow) desiredCount = Math.max(desiredCount, 3);
        if (laneSummary.gold > this.personalityProfile.floatGoldSoftCap + 120) desiredCount = Math.max(desiredCount, 5);
        if (laneSummary.gold > this.personalityProfile.floatGoldSoftCap + 240) desiredCount = 10;
        if (roleNeed >= 2.5) desiredCount = Math.max(desiredCount, 5);

        const spendableGold = Math.max(0, laneSummary.gold - (context.danger ? 0 : context.reserveGold));
        const maxAffordable = Math.floor(spendableGold / Math.max(1, Number(entry.buyCost) || 1));
        const count = chooseCountBucket(maxAffordable, desiredCount);
        if (count <= 0) continue;

        let score = 1.6 + sitePriority + preferredBonus * 0.8 + roleNeed * 1.4;
        score += Math.max(0, Number(entry.tier) || 1) * 0.18;
        score += entry.ownedCount <= 0 ? 0.18 : 0;
        if (context.danger && entry.role === "melee") score += 1.2;
        if (context.danger && entry.role === "support") score += 0.9;
        if (context.pressureWindow && entry.role === "ranged") score += 0.95;
        if (context.pressureWindow && entry.role === "melee") score += 0.55;
        if (laneSummary.gold > this.personalityProfile.floatGoldSoftCap) score += 0.25;
        if (count >= 5) score += 0.3;
        if (!context.danger && !context.pressureWindow && roleNeed < 0.5)
          score -= 0.9;
        if (!context.danger && laneSummary.barracksRosterSummary.ownedTotal >= Math.max(8, laneSummary.builtBarracksSites * 10))
          score -= 0.6;
        if (!context.danger && unlockedRoleCount <= 1 && ownedRoleCount >= Math.max(4, context.minimumRosterFloor))
          score -= 1.5;
        if (!context.danger &&
            context.saveForTechCost > 0 &&
            laneSummary.gold < context.saveForTechCost + Number(entry.buyCost || 0) &&
            totalOwned >= context.minimumRosterFloor) {
          score -= 1.35;
        }
        if (!context.danger &&
            context.roundStage === "early" &&
            entry.productionBuildingType === "blacksmith" &&
            laneSummary.padTiers.temple <= 0 &&
            laneSummary.padTiers.archery_tower <= 0 &&
            laneSummary.padTiers.wizard_tower <= 0) {
          score -= 1.8;
        }

        this.addCandidate(candidateMap, game, laneSummary, {
          type: AI_ACTION_TYPE.BUY_BARRACKS_UNIT,
          barracksId: site.barracksId,
          rosterKey: entry.rosterKey,
          count,
        }, score, {
          defensive: context.danger && (entry.role === "melee" || entry.role === "support"),
          reserveBreak: context.danger && entry.role === "melee" && count >= 3,
        });
      }
    }
  }

  maybeAddHeroCandidates(candidateMap, game, laneSummary, context) {
    if (!laneSummary || !Array.isArray(laneSummary.heroRoster) || laneSummary.heroRoster.length === 0)
      return;

    const preferredSite = getBuiltSitesOrdered(laneSummary, this.personalityProfile)[0];
    if (!preferredSite) return;

    for (const hero of laneSummary.heroRoster) {
      if (!hero || !hero.canSummon) continue;

      let score = 2.6 + this.personalityProfile.pressureWeight * 0.5 + (context.roundStage === "late" ? 0.8 : 0);
      if (context.danger && hero.heroKey === "bishop") score += 1.4;
      if (context.danger && hero.heroKey === "paladin") score += 1.1;
      if (context.pressureWindow && hero.heroKey === "king") score += 1.2;
      if (context.pressureWindow && hero.heroKey === "paladin") score += 0.8;
      if (laneSummary.gold > this.personalityProfile.floatGoldSoftCap + 90) score += 0.5;

      this.addCandidate(candidateMap, game, laneSummary, {
        type: AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO,
        barracksId: preferredSite.barracksId,
        heroKey: hero.heroKey,
      }, score, {
        defensive: context.danger,
        reserveBreak: context.pressureWindow || context.danger,
      });
    }
  }

  tick(context) {
    const game = context && context.game;
    const runtime = (context && context.runtime) || {};
    const lane = getLane(game, this.laneIndex);
    if (!game || !lane || lane.eliminated || game.phase !== "playing")
      return createDoNothingAction();

    this.updateMemory(game, runtime);

    const currentTick = Number(game.tick) || 0;
    if (currentTick < this.memory.nextThinkTick)
      return createDoNothingAction();
    this.memory.nextThinkTick = currentTick + this.reactionDelayTicks;

    const targetLaneIndex = this.chooseTarget(game, runtime);
    const observation = buildObservation(game, this.laneIndex, runtime, this.unitDefs);
    const laneSummary = observation.laneSummary || summarizeLaneForAi(game, this.laneIndex, runtime, this.unitDefs);
    const targetSummary = Number.isInteger(targetLaneIndex)
      ? (observation.targetLaneIndex === targetLaneIndex && observation.targetSummary
        ? observation.targetSummary
        : summarizeLaneForAi(game, targetLaneIndex, runtime, this.unitDefs))
      : null;

    if (!laneSummary)
      return createDoNothingAction();

    const strategy = this.buildStrategicContext(game, runtime, laneSummary, targetSummary);
    const candidateMap = new Map();
    this.maybeAddLaneCommandCandidate(candidateMap, game, laneSummary, targetLaneIndex, strategy);
    this.maybeAddOpeningCandidates(candidateMap, game, laneSummary, strategy);
    this.maybeAddLumberMillCandidate(candidateMap, game, laneSummary, strategy);
    this.maybeAddTownCoreCandidate(candidateMap, game, laneSummary, strategy);
    this.maybeAddBranchCandidates(candidateMap, game, laneSummary, strategy);
    this.maybeAddDefenseCandidates(candidateMap, game, laneSummary, strategy);
    this.maybeAddBarracksCandidates(candidateMap, game, laneSummary, strategy);
    this.maybeAddRosterCandidates(candidateMap, game, laneSummary, strategy);
    this.maybeAddHeroCandidates(candidateMap, game, laneSummary, strategy);

    const ranked = this.rankCandidates(Array.from(candidateMap.values()))
      .filter((candidate) => this.canAffordCandidate(laneSummary, candidate, strategy));
    const fallback = ranked[0] || null;
    const selected = this.maybeInjectMistake(ranked) || fallback;
    if (!selected) return createDoNothingAction();

    const legal = validateActionAgainstGame(game, this.laneIndex, selected.action);
    if (!legal.ok) {
      this.memory.invalidStreak += 1;
      const safeFallback = fallback && fallback !== selected
        ? validateActionAgainstGame(game, this.laneIndex, fallback.action)
        : null;
      if (safeFallback && safeFallback.ok) {
        this.memory.invalidStreak = 0;
        this.memory.lastActionTick = currentTick;
        return safeFallback.normalized;
      }
      return createDoNothingAction();
    }

    this.memory.invalidStreak = 0;
    this.memory.lastActionTick = currentTick;
    return legal.normalized;
  }
}

module.exports = {
  BotBrain,
};
