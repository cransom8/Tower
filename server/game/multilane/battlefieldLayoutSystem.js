"use strict";

const crypto = require("crypto");
const { FRONT_GATE_COMBAT_OFFSET } = require("./fortressSystem");
const { loadAuthoredEnvironmentLayout } = require("../../authoredEnvironmentLayout");

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`battlefieldLayoutSystem requires deps.${name}`);
  return fn;
}

function requireDepArray(deps, name) {
  const value = deps && deps[name];
  if (!Array.isArray(value))
    throw new Error(`battlefieldLayoutSystem requires array deps.${name}`);
  return value;
}

function requireDepObject(deps, name) {
  const value = deps && deps[name];
  if (!value || typeof value !== "object")
    throw new Error(`battlefieldLayoutSystem requires object deps.${name}`);
  return value;
}

function createWorldPoint(x, y, contextLabel = "world point") {
  const numericX = Number(x);
  const numericY = Number(y);
  if (!Number.isFinite(numericX) || !Number.isFinite(numericY)) {
    throw new Error(
      `[battlefieldLayoutSystem] Missing finite coordinates for ${contextLabel}.`
    );
  }

  return {
    x: numericX,
    y: numericY,
  };
}

function createFinitePointFromEntry(entry, contextLabel) {
  if (!entry || !Number.isFinite(Number(entry.x)) || !Number.isFinite(Number(entry.y))) {
    throw new Error(
      `[battlefieldLayoutSystem] Missing finite point for ${contextLabel}.`
    );
  }

  return createWorldPoint(entry.x, entry.y, contextLabel);
}

function normalizeLaneLayoutKey(slot, deps = {}) {
  const normalizeAllegianceKey = requireDepFunction(deps, "normalizeAllegianceKey");
  return normalizeAllegianceKey(slot && (slot.slotColor || slot.team || slot.side))
    || (slot && (slot.slotColor || slot.team))
    || "";
}

function resolvePerimeterControlPoint(fromWorld, toWorld, mineCenterWorld) {
  const alignedCornerCandidates = [
    createWorldPoint(Number(fromWorld.x), Number(toWorld.y)),
    createWorldPoint(Number(toWorld.x), Number(fromWorld.y)),
  ];
  const xDelta = Math.abs(Number(fromWorld.x) - Number(toWorld.x));
  const yDelta = Math.abs(Number(fromWorld.y) - Number(toWorld.y));
  if (xDelta > 0.001 && yDelta > 0.001) {
    const rankedCandidates = alignedCornerCandidates
      .map((candidate) => ({
        candidate,
        distanceToMine: Math.hypot(
          Number(candidate.x) - Number(mineCenterWorld.x),
          Number(candidate.y) - Number(mineCenterWorld.y)
        ),
      }))
      .sort((left, right) => right.distanceToMine - left.distanceToMine);
    if (rankedCandidates.length > 0)
      return rankedCandidates[0].candidate;
  }

  const midpoint = {
    x: (Number(fromWorld.x) + Number(toWorld.x)) * 0.5,
    y: (Number(fromWorld.y) + Number(toWorld.y)) * 0.5,
  };
  let outward = {
    x: midpoint.x - Number(mineCenterWorld.x),
    y: midpoint.y - Number(mineCenterWorld.y),
  };
  let outwardLength = Math.hypot(outward.x, outward.y);
  if (outwardLength <= 0.0001) {
    const edge = {
      x: Number(toWorld.x) - Number(fromWorld.x),
      y: Number(toWorld.y) - Number(fromWorld.y),
    };
    outward = {
      x: -edge.y,
      y: edge.x,
    };
    outwardLength = Math.hypot(outward.x, outward.y);
  }

  if (outwardLength <= 0.0001) {
    outward = { x: 0, y: 1 };
    outwardLength = 1;
  }

  const controlDistance = Math.max(
    2,
    Math.hypot(
      Number(toWorld.x) - Number(fromWorld.x),
      Number(toWorld.y) - Number(fromWorld.y)
    ) * 0.28
  );
  return createWorldPoint(
    midpoint.x + ((outward.x / outwardLength) * controlDistance),
    midpoint.y + ((outward.y / outwardLength) * controlDistance)
  );
}

function normalize2D(vec, contextLabel = "2D vector") {
  const x = Number(vec && vec.x);
  const y = Number(vec && vec.y);
  const length = Math.hypot(x, y);
  if (!Number.isFinite(length) || length <= 0.0001) {
    throw new Error(
      `[battlefieldLayoutSystem] Missing valid ${contextLabel} for normalization.`
    );
  }

  return {
    x: x / length,
    y: y / length,
  };
}

function dot2D(a, b) {
  return (Number(a && a.x) * Number(b && b.x))
    + (Number(a && a.y) * Number(b && b.y));
}

function projectRouteSpacePointToLaneWorld(
  routeSpacePoint,
  laneIndex,
  townCoreWorld,
  frontGateWorld,
  deps = {}
) {
  const getLaneCombatAxes = requireDepFunction(deps, "getLaneCombatAxes");
  const axes = getLaneCombatAxes(laneIndex);
  if (!axes) {
    throw new Error(
      `[battlefieldLayoutSystem] Missing combat axes for lane ${laneIndex}.`
    );
  }

  const simPoint = createFinitePointFromEntry(
    routeSpacePoint,
    `route-space control point for lane ${laneIndex}`
  );
  const simCore = createFinitePointFromEntry(
    axes.core,
    `combat-space core point for lane ${laneIndex}`
  );
  const simLateral = normalize2D(
    axes.lateral,
    `combat-space lateral axis for lane ${laneIndex}`
  );
  const simForward = normalize2D(
    axes.forward,
    `combat-space forward axis for lane ${laneIndex}`
  );

  const outwardForward = normalize2D(
    {
      x: Number(frontGateWorld.x) - Number(townCoreWorld.x),
      y: Number(frontGateWorld.y) - Number(townCoreWorld.y),
    },
    `authored world forward axis for lane ${laneIndex}`
  );
  const frontGateDistance = Math.hypot(
    Number(frontGateWorld.x) - Number(townCoreWorld.x),
    Number(frontGateWorld.y) - Number(townCoreWorld.y)
  );
  if (!Number.isFinite(frontGateDistance) || frontGateDistance <= 0.0001) {
    throw new Error(
      `[battlefieldLayoutSystem] Missing authored front gate distance for lane ${laneIndex}.`
    );
  }

  const worldUnitsPerSimUnit = frontGateDistance / FRONT_GATE_COMBAT_OFFSET;
  const lateralWorldSign = laneIndex === 1 || laneIndex === 2 ? -1 : 1;
  const lateralWorld = {
    x: (-outwardForward.y) * lateralWorldSign,
    y: outwardForward.x * lateralWorldSign,
  };
  const simDelta = {
    x: Number(simPoint.x) - Number(simCore.x),
    y: Number(simPoint.y) - Number(simCore.y),
  };
  const lateralOffset = dot2D(simDelta, simLateral);
  const forwardOffset = dot2D(simDelta, simForward);

  return createWorldPoint(
    Number(townCoreWorld.x) + (lateralWorld.x * lateralOffset * worldUnitsPerSimUnit)
      + (outwardForward.x * forwardOffset * worldUnitsPerSimUnit),
    Number(townCoreWorld.y) + (lateralWorld.y * lateralOffset * worldUnitsPerSimUnit)
      + (outwardForward.y * forwardOffset * worldUnitsPerSimUnit),
    `projected route-space point for lane ${laneIndex}`
  );
}

function buildAuthoredSegmentWorldPoints(
  segmentId,
  segmentPoints,
  fromNodeId,
  toNodeId,
  laneIndex,
  fromWorld,
  toWorld,
  townCoreWorld,
  frontGatePoint,
  mineWorldPoint,
  waveNodeIds,
  coreNodeIds,
  deps = {}
) {
  const routePointCount = Array.isArray(segmentPoints) ? segmentPoints.length : 0;
  if (routePointCount < 2) {
    throw new Error(
      `[battlefieldLayoutSystem] Route segment '${segmentId}' is missing route-space points.`
    );
  }

  if (routePointCount === 2) {
    return [
      createWorldPoint(fromWorld.x, fromWorld.y),
      createWorldPoint(toWorld.x, toWorld.y),
    ];
  }

  if (routePointCount !== 3) {
    throw new Error(
      `[battlefieldLayoutSystem] Route segment '${segmentId}' uses unsupported route-space point count ${routePointCount}.`
    );
  }

  if (waveNodeIds.has(fromNodeId)) {
    if (!frontGatePoint) {
      throw new Error(
        `[battlefieldLayoutSystem] Wave route segment '${segmentId}' is missing its authored front gate control point.`
      );
    }

    return [
      createWorldPoint(fromWorld.x, fromWorld.y),
      createWorldPoint(frontGatePoint.x, frontGatePoint.y),
      createWorldPoint(toWorld.x, toWorld.y),
    ];
  }

  const fromIsMine = fromNodeId === "M";
  const toIsMine = toNodeId === "M";
  if (fromIsMine || toIsMine) {
    return [
      createWorldPoint(fromWorld.x, fromWorld.y),
      createWorldPoint(toWorld.x, toWorld.y),
    ];
  }

  if (coreNodeIds.has(fromNodeId) && coreNodeIds.has(toNodeId)) {
    const controlPoint = resolvePerimeterControlPoint(fromWorld, toWorld, mineWorldPoint);
    return [
      createWorldPoint(fromWorld.x, fromWorld.y),
      controlPoint,
      createWorldPoint(toWorld.x, toWorld.y),
    ];
  }

  if (Number.isInteger(laneIndex) && laneIndex >= 0 && townCoreWorld && frontGatePoint) {
    const controlPoint = projectRouteSpacePointToLaneWorld(
      segmentPoints[1],
      laneIndex,
      townCoreWorld,
      frontGatePoint,
      deps
    );
    return [
      createWorldPoint(fromWorld.x, fromWorld.y),
      controlPoint,
      createWorldPoint(toWorld.x, toWorld.y),
    ];
  }

  throw new Error(
    `[battlefieldLayoutSystem] Route segment '${segmentId}' could not resolve authored world control points.`
  );
}

function buildBattlefieldLayoutForSlotDefinitions(slotDefinitionsInput, opt, deps = {}) {
  const getLaneNodeId = requireDepFunction(deps, "getLaneNodeId");
  const getWaveSpawnNodeId = requireDepFunction(deps, "getWaveSpawnNodeId");
  const getLaneCombatAxes = requireDepFunction(deps, "getLaneCombatAxes");
  const getBarracksRouteStartNodeId = requireDepFunction(deps, "getBarracksRouteStartNodeId");
  const getMarketRouteNodeId = requireDepFunction(deps, "getMarketRouteNodeId");
  const getRearGateRouteNodeId = requireDepFunction(deps, "getRearGateRouteNodeId");
  const getTradeOutpostRouteNodeId = requireDepFunction(deps, "getTradeOutpostRouteNodeId");
  const fortressPadDefs = requireDepArray(deps, "FORTRESS_PAD_DEFS");
  const barracksSiteDefs = requireDepArray(deps, "BARRACKS_SITE_DEFS");
  const routeSegmentPolylines = requireDepObject(deps, "ROUTE_SEGMENT_POLYLINES");
  const authoredEnvironmentLayout = loadAuthoredEnvironmentLayout();
  const slotDefinitions = Array.isArray(slotDefinitionsInput) ? slotDefinitionsInput : [];
  const laneCount = slotDefinitions.length;
  const routeNodes = [];
  const routeSegments = [];
  const lanes = [];
  const slotByLaneIndex = new Map();
  const coreNodeIds = new Set();
  const nodeLaneKeyByNodeId = new Map();
  const nodeLaneIndexByNodeId = new Map();
  const nodeWorldByNodeId = new Map();
  const laneFrontGateByLaneKey = new Map();
  const waveNodeIds = new Set();

  for (let laneIndex = 0; laneIndex < slotDefinitions.length; laneIndex += 1) {
    const slot = slotDefinitions[laneIndex];
    if (!slot)
      continue;
    slotByLaneIndex.set(laneIndex, slot);
  }

  const mineWorld = createFinitePointFromEntry(
    authoredEnvironmentLayout.mineCenter,
    "WaveSpawnAnchor 'mine_center'"
  );
  routeNodes.push({
    nodeId: "M",
    laneIndex: -1,
    laneKey: "mine_center",
    world: createWorldPoint(mineWorld.x, mineWorld.y),
  });
  nodeWorldByNodeId.set("M", createWorldPoint(mineWorld.x, mineWorld.y));
  nodeLaneKeyByNodeId.set("M", "mine_center");
  nodeLaneIndexByNodeId.set("M", -1);

  for (let laneIndex = 0; laneIndex < laneCount; laneIndex += 1) {
    const slot = slotByLaneIndex.get(laneIndex);
    if (!slot)
      continue;

    const laneKey = normalizeLaneLayoutKey(slot, deps) || `lane_${laneIndex}`;
    const authoredLane = authoredEnvironmentLayout.lanes[laneKey];
    if (!authoredLane) {
      throw new Error(
        `[battlefieldLayoutSystem] Missing authored environment lane '${laneKey}' while building battlefield layout.`
      );
    }

    const coreNodeId = getLaneNodeId(laneIndex);
    const waveNodeId = getWaveSpawnNodeId(laneIndex);
    const fortressPads = fortressPadDefs.map((pad) => {
      const authoredPad = authoredLane.pads[pad.padId];
      if (!authoredPad) {
        throw new Error(
          `[battlefieldLayoutSystem] Authored environment lane '${laneKey}' is missing fortress pad '${pad.padId}'.`
        );
      }

      const authoredPadWorld = createFinitePointFromEntry(
        authoredPad,
        `fortress pad '${pad.padId}' on lane '${laneKey}'`
      );
      return {
        padId: pad.padId,
        buildingType: pad.buildingType,
        displayName: pad.displayName,
        gridX: pad.gridX,
        gridY: pad.gridY,
        world: authoredPadWorld,
        combatWorld: createWorldPoint(authoredPadWorld.x, authoredPadWorld.y),
      };
    });

    const townCorePad = fortressPads.find((entry) => entry && entry.padId === "town_core_pad") || null;
    if (!townCorePad) {
      throw new Error(
        `[battlefieldLayoutSystem] Authored environment lane '${laneKey}' is missing required pad 'town_core_pad'.`
      );
    }

    const frontGatePad = fortressPads.find((entry) => entry && entry.padId === "gate_front_pad") || null;
    if (!frontGatePad) {
      throw new Error(
        `[battlefieldLayoutSystem] Authored environment lane '${laneKey}' is missing required pad 'gate_front_pad'.`
      );
    }
    const marketPad = fortressPads.find((entry) => entry && entry.padId === "market_pad") || null;
    const rearGatePad = fortressPads.find((entry) => entry && entry.padId === "gate_rear_pad") || null;
    const tradeOutpost = authoredLane.tradeOutpost
      ? createFinitePointFromEntry(authoredLane.tradeOutpost, `trade outpost on lane '${laneKey}'`)
      : null;
    const marketNodeId = getMarketRouteNodeId(laneIndex);
    const rearGateNodeId = getRearGateRouteNodeId(laneIndex);
    const tradeOutpostNodeId = getTradeOutpostRouteNodeId(laneIndex);

    const coreWorld = createWorldPoint(townCorePad.world.x, townCorePad.world.y);
    const waveWorld = createWorldPoint(mineWorld.x, mineWorld.y);
    const frontGateWorld = createWorldPoint(frontGatePad.world.x, frontGatePad.world.y);

    routeNodes.push({
      nodeId: coreNodeId,
      laneIndex,
      laneKey,
      world: createWorldPoint(coreWorld.x, coreWorld.y),
    });
    coreNodeIds.add(coreNodeId);
    routeNodes.push({
      nodeId: waveNodeId,
      laneIndex,
      laneKey,
      world: createWorldPoint(waveWorld.x, waveWorld.y),
    });
    nodeWorldByNodeId.set(coreNodeId, createWorldPoint(coreWorld.x, coreWorld.y));
    nodeLaneKeyByNodeId.set(coreNodeId, laneKey);
    nodeLaneIndexByNodeId.set(coreNodeId, laneIndex);
    nodeWorldByNodeId.set(waveNodeId, createWorldPoint(waveWorld.x, waveWorld.y));
    nodeLaneKeyByNodeId.set(waveNodeId, laneKey);
    nodeLaneIndexByNodeId.set(waveNodeId, laneIndex);
    laneFrontGateByLaneKey.set(laneKey, createWorldPoint(frontGateWorld.x, frontGateWorld.y));
    waveNodeIds.add(waveNodeId);
    getLaneCombatAxes(laneIndex);

    if (marketPad && marketNodeId) {
      routeNodes.push({
        nodeId: marketNodeId,
        laneIndex,
        laneKey,
        world: createWorldPoint(marketPad.world.x, marketPad.world.y),
      });
      nodeWorldByNodeId.set(marketNodeId, createWorldPoint(marketPad.world.x, marketPad.world.y));
      nodeLaneKeyByNodeId.set(marketNodeId, laneKey);
      nodeLaneIndexByNodeId.set(marketNodeId, laneIndex);
    }

    if (rearGatePad && rearGateNodeId) {
      routeNodes.push({
        nodeId: rearGateNodeId,
        laneIndex,
        laneKey,
        world: createWorldPoint(rearGatePad.world.x, rearGatePad.world.y),
      });
      nodeWorldByNodeId.set(rearGateNodeId, createWorldPoint(rearGatePad.world.x, rearGatePad.world.y));
      nodeLaneKeyByNodeId.set(rearGateNodeId, laneKey);
      nodeLaneIndexByNodeId.set(rearGateNodeId, laneIndex);
    }

    if (tradeOutpost && tradeOutpostNodeId) {
      routeNodes.push({
        nodeId: tradeOutpostNodeId,
        laneIndex,
        laneKey,
        world: createWorldPoint(tradeOutpost.x, tradeOutpost.y),
      });
      nodeWorldByNodeId.set(tradeOutpostNodeId, createWorldPoint(tradeOutpost.x, tradeOutpost.y));
      nodeLaneKeyByNodeId.set(tradeOutpostNodeId, laneKey);
      nodeLaneIndexByNodeId.set(tradeOutpostNodeId, laneIndex);
    }

    const barracksSites = barracksSiteDefs.map((siteDef) => {
      const authoredBarracks = authoredLane.barracks[siteDef.barracksId];
      if (!authoredBarracks) {
        throw new Error(
          `[battlefieldLayoutSystem] Authored environment lane '${laneKey}' is missing barracks '${siteDef.barracksId}'.`
        );
      }

      const world = createFinitePointFromEntry(
        authoredBarracks,
        `barracks '${siteDef.barracksId}' on lane '${laneKey}'`
      );
      const routeNodeId = getBarracksRouteStartNodeId(laneIndex, siteDef.barracksId);
      routeNodes.push({
        nodeId: routeNodeId,
        laneIndex,
        laneKey,
        world: createWorldPoint(world.x, world.y),
      });
      nodeWorldByNodeId.set(routeNodeId, createWorldPoint(world.x, world.y));
      nodeLaneKeyByNodeId.set(routeNodeId, laneKey);
      nodeLaneIndexByNodeId.set(routeNodeId, laneIndex);

      return {
        barracksId: siteDef.barracksId,
        displayName: siteDef.displayName,
        slot: siteDef.slot,
        sortIndex: siteDef.sortIndex,
        world: createWorldPoint(world.x, world.y),
        routeNodeId,
      };
    });

    lanes.push({
      laneIndex,
      laneKey,
      slotColor: slot.slotColor,
      slotKey: slot.slotKey,
      branchId: slot.branchId,
      townCore: createWorldPoint(coreWorld.x, coreWorld.y),
      frontGate: frontGateWorld,
      market: marketPad ? createWorldPoint(marketPad.world.x, marketPad.world.y) : null,
      rearGate: rearGatePad ? createWorldPoint(rearGatePad.world.x, rearGatePad.world.y) : null,
      tradeOutpost: tradeOutpost ? createWorldPoint(tradeOutpost.x, tradeOutpost.y) : null,
      waveSpawn: createWorldPoint(waveWorld.x, waveWorld.y),
      fortressPads,
      barracksSites,
    });
  }

  for (const [segmentId, segmentPoints] of Object.entries(routeSegmentPolylines)) {
    if (!Array.isArray(segmentPoints) || segmentPoints.length < 2)
      continue;

    const splitIndex = segmentId.indexOf("_");
    if (splitIndex <= 0 || splitIndex >= segmentId.length - 1)
      continue;

    const fromNodeId = segmentId.slice(0, splitIndex);
    const toNodeId = segmentId.slice(splitIndex + 1);
    const fromWorld = nodeWorldByNodeId.get(fromNodeId);
    const toWorld = nodeWorldByNodeId.get(toNodeId);
    if (!fromWorld || !toWorld)
      continue;

    const fromLaneKey = nodeLaneKeyByNodeId.get(fromNodeId) || null;
    const toLaneKey = nodeLaneKeyByNodeId.get(toNodeId) || null;
    const fromLaneIndex = nodeLaneIndexByNodeId.get(fromNodeId);
    const toLaneIndex = nodeLaneIndexByNodeId.get(toNodeId);
    const segmentLaneKey = (fromLaneKey && fromLaneKey !== "mine_center")
      ? fromLaneKey
      : ((toLaneKey && toLaneKey !== "mine_center") ? toLaneKey : null);
    const segmentLaneIndex = Number.isInteger(fromLaneIndex) && fromLaneIndex >= 0
      ? fromLaneIndex
      : (Number.isInteger(toLaneIndex) && toLaneIndex >= 0 ? toLaneIndex : null);
    const frontGatePoint = segmentLaneKey
      ? laneFrontGateByLaneKey.get(segmentLaneKey) || null
      : null;
    const townCoreWorld = Number.isInteger(segmentLaneIndex)
      ? nodeWorldByNodeId.get(getLaneNodeId(segmentLaneIndex)) || null
      : null;
    const authoredSegmentPoints = buildAuthoredSegmentWorldPoints(
      segmentId,
      segmentPoints,
      fromNodeId,
      toNodeId,
      segmentLaneIndex,
      fromWorld,
      toWorld,
      townCoreWorld,
      frontGatePoint,
      mineWorld,
      waveNodeIds,
      coreNodeIds,
      deps
    );
    const routeSpacePoints = [];
    for (let pointIndex = 0; pointIndex < segmentPoints.length; pointIndex += 1) {
      routeSpacePoints.push(
        createFinitePointFromEntry(
          segmentPoints[pointIndex],
          `route-space point ${pointIndex} for segment '${segmentId}'`
        )
      );
    }

    routeSegments.push({
      segmentId,
      fromNodeId,
      toNodeId,
      laneKey: segmentLaneKey,
      points: authoredSegmentPoints,
      routeSpacePoints,
    });
  }

  return {
    mapType: opt && opt.battlefieldTopology && opt.battlefieldTopology.mapType
      ? opt.battlefieldTopology.mapType
      : "unknown",
    playerCount: laneCount,
    lanes,
    routeNodes,
    routeSegments,
  };
}

function mergeBattlefieldLayoutEntries(primaryEntries, fallbackEntries, idKey) {
  const merged = [];
  const seen = new Set();
  for (const entries of [primaryEntries, fallbackEntries]) {
    if (!Array.isArray(entries))
      continue;
    for (const entry of entries) {
      const id = entry && entry[idKey];
      if (!id || seen.has(id))
        continue;
      seen.add(id);
      merged.push(entry);
    }
  }
  return merged;
}

function buildBattlefieldLayout(opt, deps = {}) {
  const getDefaultSlotDefinitions = requireDepFunction(deps, "getDefaultSlotDefinitions");
  const activeSlotDefinitions = Array.isArray(opt && opt.battlefieldTopology && opt.battlefieldTopology.slotDefinitions)
    ? opt.battlefieldTopology.slotDefinitions
    : [];
  const canonicalLayout = buildBattlefieldLayoutForSlotDefinitions(activeSlotDefinitions, opt, deps);

  const authoredEnvironmentPlayerCount = Number.isFinite(Number(deps.defaultEnvironmentPlayerCount))
    ? Math.max(1, Math.floor(Number(deps.defaultEnvironmentPlayerCount)))
    : Math.max(1, activeSlotDefinitions.length);
  const authoredSlotDefinitions = getDefaultSlotDefinitions(authoredEnvironmentPlayerCount, []);
  const environmentLayout = buildBattlefieldLayoutForSlotDefinitions(
    Array.isArray(authoredSlotDefinitions) && authoredSlotDefinitions.length > 0
      ? authoredSlotDefinitions
      : activeSlotDefinitions,
    opt,
    deps
  );

  const contentHash = crypto
    .createHash("sha256")
    .update(JSON.stringify(environmentLayout))
    .digest("hex");

  return {
    layoutId: `${canonicalLayout.mapType}:server_v2`,
    mapType: canonicalLayout.mapType,
    playerCount: canonicalLayout.playerCount,
    contentHash,
    lanes: canonicalLayout.lanes,
    // Always publish the full authored route graph so clients can project
    // legal server segments even when the active match uses a subset of lanes.
    routeNodes: mergeBattlefieldLayoutEntries(canonicalLayout.routeNodes, environmentLayout.routeNodes, "nodeId"),
    routeSegments: mergeBattlefieldLayoutEntries(canonicalLayout.routeSegments, environmentLayout.routeSegments, "segmentId"),
  };
}

module.exports = {
  buildBattlefieldLayout,
};
