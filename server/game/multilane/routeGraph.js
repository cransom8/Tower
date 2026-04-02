"use strict";

const { FRONT_GATE_COMBAT_OFFSET } = require("./fortressSystem");
const { normalizeBarracksSiteId } = require("./barracksSystem");

const ROUTE_TYPES = Object.freeze({
  OUTER_LOOP: "OUTER_LOOP",
  CENTER_CROSS: "CENTER_CROSS",
  WAVE_LANE: "WAVE_LANE",
});

const ROUTE_NODE_IDS = Object.freeze(["A", "B", "C", "D"]);
const WAVE_SPAWN_NODE_IDS = Object.freeze(["WA", "WB", "WC", "WD"]);
const LANE_NODE_IDS = Object.freeze(["A", "B", "C", "D"]);
const RouteMineNode = "M";
const ROUTE_TANGENT_SAMPLE_DELTA = 0.003;

const ROUTE_GRAPH_CORE_NODE_POSITIONS = Object.freeze({
  M: Object.freeze({ x: 0, y: 0 }),
  A: Object.freeze({ x: -24, y: 24 }),
  B: Object.freeze({ x: 24, y: 24 }),
  C: Object.freeze({ x: 24, y: -24 }),
  D: Object.freeze({ x: -24, y: -24 }),
});

const LANE_COMBAT_AXES = Object.freeze([
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.A,
    lateral: Object.freeze({ x: 1, y: 0 }),
    forward: Object.freeze({ x: 0, y: -1 }),
  }),
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.B,
    lateral: Object.freeze({ x: -1, y: 0 }),
    forward: Object.freeze({ x: 0, y: -1 }),
  }),
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.C,
    lateral: Object.freeze({ x: -1, y: 0 }),
    forward: Object.freeze({ x: 0, y: 1 }),
  }),
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.D,
    lateral: Object.freeze({ x: 1, y: 0 }),
    forward: Object.freeze({ x: 0, y: 1 }),
  }),
]);

const BARRACKS_SITE_COMBAT_OFFSETS = Object.freeze({
  center: Object.freeze({ x: 0, y: 2 }),
  left: Object.freeze({ x: -4, y: 2 }),
  right: Object.freeze({ x: 4, y: 2 }),
});

const BARRACKS_ROUTE_NODE_SUFFIXES = Object.freeze({
  center: "CTR",
  left: "LFT",
  right: "RGT",
});

const MARKET_ROUTE_NODE_SUFFIXES = Object.freeze({
  market: "MKT",
  rearGate: "RGR",
  tradeOutpost: "BST",
});

const MARKET_ROUTE_NODE_OFFSETS = Object.freeze({
  market: Object.freeze({ x: 0, y: 5 }),
  rearGate: Object.freeze({ x: 0, y: -5 }),
  tradeOutpost: Object.freeze({ x: 0, y: -12 }),
});
const MARKET_ROUTE_CURVE_LATERAL_OFFSET = 3.2;

const ROUTE_GRAPH_NODE_POSITIONS = Object.freeze(buildBarracksRouteGraphNodePositions());

// Route graph polylines are simulation-space Battlefield Route segments.
// Client runtime projects these segment ids into rendered board-space anchors.
const ROUTE_SEGMENT_POLYLINES = Object.freeze({
  A_B: Object.freeze([
    Object.freeze({ x: -24, y: 24 }),
    Object.freeze({ x: 0, y: 28 }),
    Object.freeze({ x: 24, y: 24 }),
  ]),
  B_C: Object.freeze([
    Object.freeze({ x: 24, y: 24 }),
    Object.freeze({ x: 28, y: 0 }),
    Object.freeze({ x: 24, y: -24 }),
  ]),
  C_D: Object.freeze([
    Object.freeze({ x: 24, y: -24 }),
    Object.freeze({ x: 0, y: -28 }),
    Object.freeze({ x: -24, y: -24 }),
  ]),
  D_A: Object.freeze([
    Object.freeze({ x: -24, y: -24 }),
    Object.freeze({ x: -28, y: 0 }),
    Object.freeze({ x: -24, y: 24 }),
  ]),
  A_C: Object.freeze([
    Object.freeze({ x: -24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: -24 }),
  ]),
  C_A: Object.freeze([
    Object.freeze({ x: 24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: 24 }),
  ]),
  B_D: Object.freeze([
    Object.freeze({ x: 24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: -24 }),
  ]),
  D_B: Object.freeze([
    Object.freeze({ x: -24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: 24 }),
  ]),
  A_M: Object.freeze([
    Object.freeze({ x: -24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_A: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: 24 }),
  ]),
  B_M: Object.freeze([
    Object.freeze({ x: 24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_B: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: 24 }),
  ]),
  C_M: Object.freeze([
    Object.freeze({ x: 24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_C: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: -24 }),
  ]),
  D_M: Object.freeze([
    Object.freeze({ x: -24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_D: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: -24 }),
  ]),
  ...buildWaveLanePolylines(),
  ...buildBarracksRouteLinkPolylines(),
  ...buildMarketRouteLinkPolylines(),
});

const ROUTE_SEGMENT_LENGTHS = Object.freeze(Object.fromEntries(
  Object.entries(ROUTE_SEGMENT_POLYLINES).map(([segmentId, points]) => [segmentId, getPolylineLength(points)])
));

function getPolylineLength(points) {
  if (!Array.isArray(points) || points.length < 2) return 0;
  let total = 0;
  for (let i = 0; i < points.length - 1; i++) {
    total += pointDistance(points[i], points[i + 1]);
  }
  return total;
}

function pointDistance(a, b) {
  const dx = Number(b.x) - Number(a.x);
  const dy = Number(b.y) - Number(a.y);
  return Math.sqrt((dx * dx) + (dy * dy));
}

function samplePolyline(points, progress) {
  const clamped = Math.max(0, Math.min(1, Number(progress) || 0));
  const total = getPolylineLength(points);
  if (!Array.isArray(points) || points.length === 0)
    return { point: { x: 0, y: 0 }, tangent: { x: 0, y: 1 } };
  if (points.length === 1 || total <= 0)
    return { point: { x: Number(points[0].x) || 0, y: Number(points[0].y) || 0 }, tangent: { x: 0, y: 1 } };

  const target = total * clamped;
  let walked = 0;
  for (let i = 0; i < points.length - 1; i++) {
    const from = points[i];
    const to = points[i + 1];
    const segLen = pointDistance(from, to);
    if (segLen <= 0)
      continue;
    if (walked + segLen >= target) {
      const localT = (target - walked) / segLen;
      const tangent = normalize2D({
        x: Number(to.x) - Number(from.x),
        y: Number(to.y) - Number(from.y),
      });
      return {
        point: {
          x: lerp(Number(from.x), Number(to.x), localT),
          y: lerp(Number(from.y), Number(to.y), localT),
        },
        tangent,
      };
    }
    walked += segLen;
  }

  const last = points[points.length - 1];
  const prev = points[points.length - 2];
  return {
    point: { x: Number(last.x) || 0, y: Number(last.y) || 0 },
    tangent: normalize2D({
      x: Number(last.x) - Number(prev.x),
      y: Number(last.y) - Number(prev.y),
    }),
  };
}

function lerp(a, b, t) {
  return a + ((b - a) * t);
}

function normalize2D(vec) {
  const x = Number(vec && vec.x) || 0;
  const y = Number(vec && vec.y) || 0;
  const len = Math.sqrt((x * x) + (y * y));
  if (len <= 0.00001)
    return { x: 0, y: 0 };
  return { x: x / len, y: y / len };
}

function perpendicular2D(vec) {
  const safe = normalize2D(vec);
  return { x: -safe.y, y: safe.x };
}

function dot2D(a, b) {
  const ax = Number(a && a.x) || 0;
  const ay = Number(a && a.y) || 0;
  const bx = Number(b && b.x) || 0;
  const by = Number(b && b.y) || 0;
  return (ax * bx) + (ay * by);
}

function getLaneNodeId(laneIndex) {
  return Number.isInteger(laneIndex) && laneIndex >= 0 && laneIndex < LANE_NODE_IDS.length
    ? LANE_NODE_IDS[laneIndex]
    : null;
}

function getWaveSpawnNodeId(laneIndex) {
  return Number.isInteger(laneIndex) && laneIndex >= 0 && laneIndex < WAVE_SPAWN_NODE_IDS.length
    ? WAVE_SPAWN_NODE_IDS[laneIndex]
    : null;
}

function getNodeIndex(nodeId) {
  return ROUTE_NODE_IDS.indexOf(nodeId);
}

function getLaneCombatAxes(laneIndex) {
  return Number.isInteger(laneIndex) && laneIndex >= 0 && laneIndex < LANE_COMBAT_AXES.length
    ? LANE_COMBAT_AXES[laneIndex]
    : null;
}

function getBarracksRouteStartNodeId(laneIndex, barracksId) {
  const coreNodeId = getLaneNodeId(laneIndex);
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  const suffix = normalizedBarracksId ? BARRACKS_ROUTE_NODE_SUFFIXES[normalizedBarracksId] : null;
  if (!coreNodeId || !suffix)
    return null;
  return `${coreNodeId}${suffix}`;
}

function getMarketRouteNodeId(laneIndex) {
  const coreNodeId = getLaneNodeId(laneIndex);
  return coreNodeId ? `${coreNodeId}${MARKET_ROUTE_NODE_SUFFIXES.market}` : null;
}

function getRearGateRouteNodeId(laneIndex) {
  const coreNodeId = getLaneNodeId(laneIndex);
  return coreNodeId ? `${coreNodeId}${MARKET_ROUTE_NODE_SUFFIXES.rearGate}` : null;
}

function getTradeOutpostRouteNodeId(laneIndex) {
  const coreNodeId = getLaneNodeId(laneIndex);
  return coreNodeId ? `${coreNodeId}${MARKET_ROUTE_NODE_SUFFIXES.tradeOutpost}` : null;
}

function getLaneCoreNodeIdForRouteNode(nodeId) {
  const normalizedNodeId = String(nodeId || "").trim().toUpperCase();
  if (ROUTE_NODE_IDS.includes(normalizedNodeId))
    return normalizedNodeId;

  for (const coreNodeId of ROUTE_NODE_IDS) {
    if (normalizedNodeId.startsWith(coreNodeId)) {
      const suffix = normalizedNodeId.slice(coreNodeId.length);
      if (suffix === BARRACKS_ROUTE_NODE_SUFFIXES.center
          || suffix === BARRACKS_ROUTE_NODE_SUFFIXES.left
          || suffix === BARRACKS_ROUTE_NODE_SUFFIXES.right
          || suffix === MARKET_ROUTE_NODE_SUFFIXES.market
          || suffix === MARKET_ROUTE_NODE_SUFFIXES.rearGate
          || suffix === MARKET_ROUTE_NODE_SUFFIXES.tradeOutpost) {
        return coreNodeId;
      }
    }
  }

  return null;
}

function isBarracksRouteStartNode(nodeId) {
  const normalizedNodeId = String(nodeId || "").trim().toUpperCase();
  return normalizedNodeId.length > 1
    && getLaneCoreNodeIdForRouteNode(normalizedNodeId) !== normalizedNodeId
    && getLaneCoreNodeIdForRouteNode(normalizedNodeId) !== null;
}

function buildBarracksRouteGraphNodePositions() {
  const positions = {
    ...ROUTE_GRAPH_CORE_NODE_POSITIONS,
  };

  for (let laneIndex = 0; laneIndex < LANE_COMBAT_AXES.length; laneIndex += 1) {
    const axes = LANE_COMBAT_AXES[laneIndex];
    const coreNodeId = LANE_NODE_IDS[laneIndex];
    if (!axes || !coreNodeId)
      continue;

    for (const barracksId of Object.keys(BARRACKS_SITE_COMBAT_OFFSETS)) {
      const routeNodeId = getBarracksRouteStartNodeId(laneIndex, barracksId);
      const offset = BARRACKS_SITE_COMBAT_OFFSETS[barracksId];
      if (!routeNodeId || !offset)
        continue;

      positions[routeNodeId] = Object.freeze({
        x: axes.core.x + (axes.lateral.x * offset.x) + (axes.forward.x * offset.y),
        y: axes.core.y + (axes.lateral.y * offset.x) + (axes.forward.y * offset.y),
      });
    }

    for (const [nodeKey, offset] of Object.entries(MARKET_ROUTE_NODE_OFFSETS)) {
      const routeNodeId = nodeKey === "market"
        ? getMarketRouteNodeId(laneIndex)
        : nodeKey === "rearGate"
          ? getRearGateRouteNodeId(laneIndex)
          : getTradeOutpostRouteNodeId(laneIndex);
      if (!routeNodeId || !offset)
        continue;

      positions[routeNodeId] = Object.freeze({
        x: axes.core.x + (axes.lateral.x * offset.x) + (axes.forward.x * offset.y),
        y: axes.core.y + (axes.lateral.y * offset.x) + (axes.forward.y * offset.y),
      });
    }
  }

  return positions;
}

function buildBarracksRouteLinkPolylines() {
  const segments = {};
  for (let laneIndex = 0; laneIndex < LANE_NODE_IDS.length; laneIndex += 1) {
    const coreNodeId = LANE_NODE_IDS[laneIndex];
    const corePos = ROUTE_GRAPH_CORE_NODE_POSITIONS[coreNodeId];
    if (!coreNodeId || !corePos)
      continue;

    for (const barracksId of Object.keys(BARRACKS_SITE_COMBAT_OFFSETS)) {
      const routeNodeId = getBarracksRouteStartNodeId(laneIndex, barracksId);
      const routeNodePos = routeNodeId ? ROUTE_GRAPH_NODE_POSITIONS[routeNodeId] : null;
      if (!routeNodeId || !routeNodePos)
        continue;

      segments[`${routeNodeId}_${coreNodeId}`] = Object.freeze([
        Object.freeze({ x: routeNodePos.x, y: routeNodePos.y }),
        Object.freeze({ x: corePos.x, y: corePos.y }),
      ]);
    }
  }

  return segments;
}

function buildMarketRouteLinkPolylines() {
  const segments = {};
  for (let laneIndex = 0; laneIndex < LANE_NODE_IDS.length; laneIndex += 1) {
    const coreNodeId = getLaneNodeId(laneIndex);
    const marketNodeId = getMarketRouteNodeId(laneIndex);
    const rearGateNodeId = getRearGateRouteNodeId(laneIndex);
    const tradeOutpostNodeId = getTradeOutpostRouteNodeId(laneIndex);
    const coreNodePos = coreNodeId ? ROUTE_GRAPH_NODE_POSITIONS[coreNodeId] : null;
    const marketNodePos = marketNodeId ? ROUTE_GRAPH_NODE_POSITIONS[marketNodeId] : null;
    const rearGateNodePos = rearGateNodeId ? ROUTE_GRAPH_NODE_POSITIONS[rearGateNodeId] : null;
    const tradeOutpostNodePos = tradeOutpostNodeId ? ROUTE_GRAPH_NODE_POSITIONS[tradeOutpostNodeId] : null;
    if (!coreNodePos || !marketNodeId || !rearGateNodeId || !tradeOutpostNodeId
        || !marketNodePos || !rearGateNodePos || !tradeOutpostNodePos) {
      continue;
    }

    const rearLoopControlPoint = Object.freeze({
      x: Number(coreNodePos.x) - MARKET_ROUTE_CURVE_LATERAL_OFFSET,
      y: (Number(marketNodePos.y) + Number(rearGateNodePos.y)) * 0.5,
    });

    segments[`${marketNodeId}_${rearGateNodeId}`] = Object.freeze([
      Object.freeze({ x: marketNodePos.x, y: marketNodePos.y }),
      rearLoopControlPoint,
      Object.freeze({ x: rearGateNodePos.x, y: rearGateNodePos.y }),
    ]);
    segments[`${rearGateNodeId}_${tradeOutpostNodeId}`] = Object.freeze([
      Object.freeze({ x: rearGateNodePos.x, y: rearGateNodePos.y }),
      Object.freeze({ x: tradeOutpostNodePos.x, y: tradeOutpostNodePos.y }),
    ]);
    segments[`${tradeOutpostNodeId}_${rearGateNodeId}`] = Object.freeze([
      Object.freeze({ x: tradeOutpostNodePos.x, y: tradeOutpostNodePos.y }),
      Object.freeze({ x: rearGateNodePos.x, y: rearGateNodePos.y }),
    ]);
    segments[`${rearGateNodeId}_${marketNodeId}`] = Object.freeze([
      Object.freeze({ x: rearGateNodePos.x, y: rearGateNodePos.y }),
      rearLoopControlPoint,
      Object.freeze({ x: marketNodePos.x, y: marketNodePos.y }),
    ]);
  }

  return segments;
}

function buildWaveLanePolylines() {
  const segments = {};
  for (let laneIndex = 0; laneIndex < LANE_NODE_IDS.length; laneIndex += 1) {
    const waveNodeId = WAVE_SPAWN_NODE_IDS[laneIndex];
    const coreNodeId = LANE_NODE_IDS[laneIndex];
    const axes = getLaneCombatAxes(laneIndex);
    const corePos = coreNodeId ? ROUTE_GRAPH_CORE_NODE_POSITIONS[coreNodeId] : null;
    if (!waveNodeId || !coreNodeId || !axes || !corePos)
      continue;

    const frontGatePoint = Object.freeze({
      x: corePos.x + (Number(axes.forward.x) * FRONT_GATE_COMBAT_OFFSET),
      y: corePos.y + (Number(axes.forward.y) * FRONT_GATE_COMBAT_OFFSET),
    });
    segments[`${waveNodeId}_${coreNodeId}`] = Object.freeze([
      Object.freeze({ x: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.x, y: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.y }),
      frontGatePoint,
      Object.freeze({ x: corePos.x, y: corePos.y }),
    ]);
  }

  return segments;
}

function getWaveSpawnWorldPosition(laneIndex) {
  if (!Number.isInteger(laneIndex) || laneIndex < 0 || laneIndex >= LANE_NODE_IDS.length)
    return null;

  return {
    x: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.x,
    y: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.y,
  };
}

function getPadWorldPosition(laneIndex, gridX, gridY) {
  const axes = getLaneCombatAxes(laneIndex);
  if (!axes)
    return { x: 0, y: 0 };

  const lateralOffset = (Number(gridX) || 0) - 3;
  const forwardOffset = 25 - (Number(gridY) || 0);
  return {
    x: axes.core.x + (axes.lateral.x * lateralOffset) + (axes.forward.x * forwardOffset),
    y: axes.core.y + (axes.lateral.y * lateralOffset) + (axes.forward.y * forwardOffset),
  };
}

function getBarracksSiteWorldPosition(laneIndex, barracksId) {
  const axes = getLaneCombatAxes(laneIndex);
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  const offset = normalizedBarracksId ? BARRACKS_SITE_COMBAT_OFFSETS[normalizedBarracksId] : null;
  if (!axes || !offset)
    return null;

  return {
    x: axes.core.x + (axes.lateral.x * offset.x) + (axes.forward.x * offset.y),
    y: axes.core.y + (axes.lateral.y * offset.x) + (axes.forward.y * offset.y),
  };
}

function getMarketPadWorldPosition(laneIndex) {
  const routeNodeId = getMarketRouteNodeId(laneIndex);
  return routeNodeId && ROUTE_GRAPH_NODE_POSITIONS[routeNodeId]
    ? {
      x: ROUTE_GRAPH_NODE_POSITIONS[routeNodeId].x,
      y: ROUTE_GRAPH_NODE_POSITIONS[routeNodeId].y,
    }
    : null;
}

function buildMarketLoopRouteSegments(laneIndex) {
  const marketNodeId = getMarketRouteNodeId(laneIndex);
  const rearGateNodeId = getRearGateRouteNodeId(laneIndex);
  const tradeOutpostNodeId = getTradeOutpostRouteNodeId(laneIndex);
  if (!marketNodeId || !rearGateNodeId || !tradeOutpostNodeId)
    return null;

  return [
    `${marketNodeId}_${rearGateNodeId}`,
    `${rearGateNodeId}_${tradeOutpostNodeId}`,
    `${tradeOutpostNodeId}_${rearGateNodeId}`,
    `${rearGateNodeId}_${marketNodeId}`,
  ];
}

function buildRouteSegments(routeType, sourceNodeId, targetNodeId) {
  if (!routeType)
    return null;

  const routeSourceNodeId = String(sourceNodeId || "").trim().toUpperCase();
  const routeTargetNodeId = String(targetNodeId || "").trim().toUpperCase();
  const sourceCoreNodeId = getLaneCoreNodeIdForRouteNode(routeSourceNodeId);
  const prependBarracksLink = isBarracksRouteStartNode(routeSourceNodeId) && sourceCoreNodeId
    ? `${routeSourceNodeId}_${sourceCoreNodeId}`
    : null;

  if (prependBarracksLink && routeTargetNodeId && sourceCoreNodeId && routeTargetNodeId === sourceCoreNodeId)
    return [prependBarracksLink];

  if (routeType === ROUTE_TYPES.WAVE_LANE) {
    if (!routeSourceNodeId || !routeTargetNodeId)
      return null;
    return [`${routeSourceNodeId}_${routeTargetNodeId}`];
  }

  if (routeType === ROUTE_TYPES.CENTER_CROSS) {
    if (!routeSourceNodeId || !routeTargetNodeId || !sourceCoreNodeId)
      return null;
    const segments = [];
    if (prependBarracksLink)
      segments.push(prependBarracksLink);
    segments.push(`${sourceCoreNodeId}_${RouteMineNode}`);
    segments.push(`${RouteMineNode}_${routeTargetNodeId}`);
    return segments;
  }

  if (routeType !== ROUTE_TYPES.OUTER_LOOP)
    return null;

  if (!sourceCoreNodeId)
    return null;

  const sourceIndex = getNodeIndex(sourceCoreNodeId);
  if (sourceIndex < 0)
    return null;

  const segments = prependBarracksLink ? [prependBarracksLink] : [];
  for (let step = 0; step < ROUTE_NODE_IDS.length; step++) {
    const from = ROUTE_NODE_IDS[(sourceIndex + step) % ROUTE_NODE_IDS.length];
    const to = ROUTE_NODE_IDS[(sourceIndex + step + 1) % ROUTE_NODE_IDS.length];
    segments.push(`${from}_${to}`);
  }
  return segments;
}

function parseRouteSegmentId(segmentId) {
  const parts = String(segmentId || "").trim().toUpperCase().split("_");
  if (parts.length !== 2 || !parts[0] || !parts[1])
    return null;
  return {
    fromNode: parts[0],
    toNode: parts[1],
  };
}

function getRouteLength(routeSegments) {
  if (!Array.isArray(routeSegments))
    return 0;
  return routeSegments.reduce((sum, segmentId) => sum + (ROUTE_SEGMENT_LENGTHS[segmentId] || 0), 0);
}

function sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset = 0) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;

  const safeIndex = Math.max(0, Math.min(routeSegments.length - 1, Math.floor(Number(segmentIndex) || 0)));
  const segmentId = routeSegments[safeIndex];
  const points = ROUTE_SEGMENT_POLYLINES[segmentId];
  if (!Array.isArray(points) || points.length < 2)
    return null;
  const sample = samplePolyline(ROUTE_SEGMENT_POLYLINES[segmentId], segmentProgress);
  const lateral = perpendicular2D(sample.tangent);
  return {
    segmentId,
    point: {
      x: sample.point.x + (lateral.x * (Number(lateralOffset) || 0)),
      y: sample.point.y + (lateral.y * (Number(lateralOffset) || 0)),
    },
    tangent: sample.tangent,
  };
}

function advanceRouteState(unit, deltaDistance) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  let remaining = Number(deltaDistance) || 0;
  const isLooping = unit.routeType === ROUTE_TYPES.OUTER_LOOP;
  let advanced = false;

  while (Math.abs(remaining) > 0.0001) {
    const currentSegmentId = unit.routeSegments[unit.routeSegmentIndex];
    const segmentLength = Math.max(0.0001, ROUTE_SEGMENT_LENGTHS[currentSegmentId] || 0.0001);
    const distanceOnSegment = Math.max(0, Math.min(1, Number(unit.segmentProgress) || 0)) * segmentLength;

    if (remaining > 0) {
      const distanceToEnd = segmentLength - distanceOnSegment;
      if (remaining < distanceToEnd) {
        unit.segmentProgress = (distanceOnSegment + remaining) / segmentLength;
        remaining = 0;
        advanced = true;
        break;
      }

      remaining -= distanceToEnd;
      unit.segmentProgress = 1;
      advanced = true;

      if (unit.routeSegmentIndex >= unit.routeSegments.length - 1) {
        if (!isLooping) {
          remaining = 0;
          break;
        }
        unit.routeSegmentIndex = 0;
        unit.segmentProgress = 0;
        continue;
      }

      unit.routeSegmentIndex += 1;
      unit.segmentProgress = 0;
      continue;
    }

    const distanceToStart = distanceOnSegment;
    if (Math.abs(remaining) < distanceToStart) {
      unit.segmentProgress = (distanceOnSegment + remaining) / segmentLength;
      remaining = 0;
      advanced = true;
      break;
    }

    remaining += distanceToStart;
    unit.segmentProgress = 0;
    advanced = true;

    if (unit.routeSegmentIndex <= 0) {
      if (!isLooping) {
        remaining = 0;
        break;
      }
      unit.routeSegmentIndex = unit.routeSegments.length - 1;
      unit.segmentProgress = 1;
      continue;
    }

    unit.routeSegmentIndex -= 1;
    unit.segmentProgress = 1;
  }

  return advanced;
}

function computeUnitRoutePathIndex(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return 0;

  const totalLength = Math.max(0.0001, getRouteLength(unit.routeSegments));
  let distance = 0;
  for (let i = 0; i < unit.routeSegmentIndex; i++) {
    distance += ROUTE_SEGMENT_LENGTHS[unit.routeSegments[i]] || 0;
  }
  const currentSegmentId = unit.routeSegments[unit.routeSegmentIndex];
  const currentSegmentLength = ROUTE_SEGMENT_LENGTHS[currentSegmentId] || 0;
  distance += Math.max(0, Math.min(1, Number(unit.segmentProgress) || 0)) * currentSegmentLength;
  return distance / totalLength;
}

function sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset = 0, lateralOffset = 0) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;

  const clampedProgress = Math.max(0, Math.min(1, Number(routeProgress) || 0));
  const centerSample = sampleRouteByDistanceNorm(routeSegments, clampedProgress, 0);
  if (!centerSample)
    return null;

  const beforeSample = sampleRouteByDistanceNorm(
    routeSegments,
    Math.max(0, clampedProgress - ROUTE_TANGENT_SAMPLE_DELTA),
    0
  );
  const afterSample = sampleRouteByDistanceNorm(
    routeSegments,
    Math.min(1, clampedProgress + ROUTE_TANGENT_SAMPLE_DELTA),
    0
  );

  let tangent = centerSample.tangent;
  if (beforeSample && afterSample) {
    tangent = normalize2D({
      x: Number(afterSample.point.x) - Number(beforeSample.point.x),
      y: Number(afterSample.point.y) - Number(beforeSample.point.y),
    });
  }
  tangent = normalize2D(tangent);
  const lateral = perpendicular2D(tangent);

  return {
    segmentId: centerSample.segmentId,
    point: {
      x: Number(centerSample.point.x) + (tangent.x * (Number(longitudinalOffset) || 0)) + (lateral.x * (Number(lateralOffset) || 0)),
      y: Number(centerSample.point.y) + (tangent.y * (Number(longitudinalOffset) || 0)) + (lateral.y * (Number(lateralOffset) || 0)),
    },
    tangent,
  };
}

function buildSampledPathFromSegments(routeSegments, sampleCount = 28) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return [];

  const safeCount = Math.max(2, Math.floor(Number(sampleCount) || 28));
  const samples = [];
  for (let i = 0; i < safeCount; i++) {
    const distanceNorm = safeCount === 1 ? 0 : (i / (safeCount - 1));
    const sample = sampleRouteByDistanceNorm(routeSegments, distanceNorm, 0);
    if (!sample)
      return [];
    samples.push({
      x: Number(sample.point.x.toFixed(3)),
      y: Number(sample.point.y.toFixed(3)),
    });
  }
  return samples;
}

function sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset = 0) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;

  const totalLength = Math.max(0.0001, getRouteLength(routeSegments));
  let remainingDistance = Math.max(0, Math.min(1, Number(routeProgress) || 0)) * totalLength;

  for (let i = 0; i < routeSegments.length; i++) {
    const segmentId = routeSegments[i];
    const segmentLength = Math.max(0.0001, ROUTE_SEGMENT_LENGTHS[segmentId] || 0.0001);
    if (remainingDistance <= segmentLength || i === routeSegments.length - 1) {
      return sampleRoutePosition(routeSegments, i, remainingDistance / segmentLength, lateralOffset);
    }
    remainingDistance -= segmentLength;
  }

  return sampleRoutePosition(routeSegments, routeSegments.length - 1, 1, lateralOffset);
}

function projectPointOntoPolyline(points, targetPoint) {
  if (!Array.isArray(points) || points.length < 2 || !targetPoint)
    return null;

  const totalLength = Math.max(0.0001, getPolylineLength(points));
  let walkedLength = 0;
  let best = null;

  for (let i = 0; i < points.length - 1; i++) {
    const from = points[i];
    const to = points[i + 1];
    const segVec = {
      x: Number(to.x) - Number(from.x),
      y: Number(to.y) - Number(from.y),
    };
    const segLenSq = (segVec.x * segVec.x) + (segVec.y * segVec.y);
    const segLen = Math.sqrt(segLenSq);
    if (segLen <= 0.0001)
      continue;

    const toTarget = {
      x: Number(targetPoint.x) - Number(from.x),
      y: Number(targetPoint.y) - Number(from.y),
    };
    const localT = Math.max(0, Math.min(1, dot2D(toTarget, segVec) / segLenSq));
    const point = {
      x: lerp(Number(from.x), Number(to.x), localT),
      y: lerp(Number(from.y), Number(to.y), localT),
    };
    const delta = {
      x: Number(targetPoint.x) - point.x,
      y: Number(targetPoint.y) - point.y,
    };
    const distanceSq = (delta.x * delta.x) + (delta.y * delta.y);
    const candidate = {
      point,
      tangent: normalize2D(segVec),
      distanceSq,
      progress: (walkedLength + (segLen * localT)) / totalLength,
    };

    if (!best || candidate.distanceSq < best.distanceSq)
      best = candidate;

    walkedLength += segLen;
  }

  return best;
}

function projectPointOntoRouteSegments(routeSegments, targetPoint) {
  if (!Array.isArray(routeSegments) || routeSegments.length === 0 || !targetPoint)
    return null;

  const totalRouteLength = Math.max(0.0001, getRouteLength(routeSegments));
  let walkedRouteLength = 0;
  let best = null;

  for (let i = 0; i < routeSegments.length; i++) {
    const segmentId = routeSegments[i];
    const points = ROUTE_SEGMENT_POLYLINES[segmentId];
    const segmentLength = Math.max(0.0001, ROUTE_SEGMENT_LENGTHS[segmentId] || 0.0001);
    const projection = projectPointOntoPolyline(points, targetPoint);
    if (projection) {
      const candidate = {
        segmentIndex: i,
        segmentId,
        point: projection.point,
        tangent: projection.tangent,
        segmentProgress: Math.max(0, Math.min(1, Number(projection.progress) || 0)),
        routeProgress: (walkedRouteLength + (segmentLength * projection.progress)) / totalRouteLength,
        distanceSq: projection.distanceSq,
      };
      if (!best || candidate.distanceSq < best.distanceSq)
        best = candidate;
    }
    walkedRouteLength += segmentLength;
  }

  return best;
}

function buildRoutePathId(routeSegments) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;
  return routeSegments.join(">");
}

function resolveUnitNextWaypoint(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length <= 0)
    return null;

  const currentIndex = Math.max(0, Math.min(unit.routeSegments.length - 1, Math.floor(Number(unit.routeSegmentIndex) || 0)));
  const segmentId = unit.routeSegments[currentIndex];
  const parts = String(segmentId || "").split("_");
  return parts.length === 2 ? parts[1] : null;
}

module.exports = {
  ROUTE_TYPES,
  ROUTE_NODE_IDS,
  WAVE_SPAWN_NODE_IDS,
  LANE_NODE_IDS,
  RouteMineNode,
  ROUTE_GRAPH_CORE_NODE_POSITIONS,
  LANE_COMBAT_AXES,
  BARRACKS_SITE_COMBAT_OFFSETS,
  BARRACKS_ROUTE_NODE_SUFFIXES,
  MARKET_ROUTE_NODE_SUFFIXES,
  MARKET_ROUTE_NODE_OFFSETS,
  ROUTE_GRAPH_NODE_POSITIONS,
  ROUTE_SEGMENT_POLYLINES,
  ROUTE_SEGMENT_LENGTHS,
  getPolylineLength,
  pointDistance,
  samplePolyline,
  lerp,
  normalize2D,
  perpendicular2D,
  dot2D,
  getLaneNodeId,
  getWaveSpawnNodeId,
  getNodeIndex,
  getLaneCombatAxes,
  getBarracksRouteStartNodeId,
  getMarketRouteNodeId,
  getRearGateRouteNodeId,
  getTradeOutpostRouteNodeId,
  getLaneCoreNodeIdForRouteNode,
  isBarracksRouteStartNode,
  getWaveSpawnWorldPosition,
  getPadWorldPosition,
  getBarracksSiteWorldPosition,
  getMarketPadWorldPosition,
  buildMarketLoopRouteSegments,
  buildRouteSegments,
  parseRouteSegmentId,
  getRouteLength,
  sampleRoutePosition,
  advanceRouteState,
  computeUnitRoutePathIndex,
  sampleContinuousRoutePosition,
  buildSampledPathFromSegments,
  sampleRouteByDistanceNorm,
  projectPointOntoPolyline,
  projectPointOntoRouteSegments,
  buildRoutePathId,
  resolveUnitNextWaypoint,
};
