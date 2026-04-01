"use strict";

const routeGraph = require("./routeGraph");
const laneCommandSystem = require("./laneCommandSystem");

function normalize2D(vec) {
  return routeGraph.normalize2D(vec);
}

function perpendicular2D(vec) {
  return routeGraph.perpendicular2D(vec);
}

function dot2D(a, b) {
  return routeGraph.dot2D(a, b);
}

function getLaneNodeId(laneIndex) {
  return routeGraph.getLaneNodeId(laneIndex);
}

function getWaveSpawnNodeId(laneIndex) {
  return routeGraph.getWaveSpawnNodeId(laneIndex);
}

function getLaneCombatAxes(laneIndex) {
  return routeGraph.getLaneCombatAxes(laneIndex);
}

function getBarracksRouteStartNodeId(laneIndex, barracksId) {
  return routeGraph.getBarracksRouteStartNodeId(laneIndex, barracksId);
}

function getLaneCoreNodeIdForRouteNode(nodeId) {
  return routeGraph.getLaneCoreNodeIdForRouteNode(nodeId);
}

function isBarracksRouteStartNode(nodeId) {
  return routeGraph.isBarracksRouteStartNode(nodeId);
}

function getWaveSpawnWorldPosition(laneIndex) {
  return routeGraph.getWaveSpawnWorldPosition(laneIndex);
}

function getPadWorldPosition(laneIndex, gridX, gridY) {
  return routeGraph.getPadWorldPosition(laneIndex, gridX, gridY);
}

function getBarracksSiteWorldPosition(laneIndex, barracksId) {
  return routeGraph.getBarracksSiteWorldPosition(laneIndex, barracksId);
}

function buildRouteSegments(routeType, sourceNodeId, targetNodeId) {
  return routeGraph.buildRouteSegments(routeType, sourceNodeId, targetNodeId);
}

function parseRouteSegmentId(segmentId) {
  return routeGraph.parseRouteSegmentId(segmentId);
}

function getRouteLength(routeSegments) {
  return routeGraph.getRouteLength(routeSegments);
}

function sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset = 0) {
  return routeGraph.sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset);
}

function advanceRouteState(unit, deltaDistance) {
  return routeGraph.advanceRouteState(unit, deltaDistance);
}

function relaxUnitRouteOffsets(unit, speed, deps = {}) {
  return laneCommandSystem.relaxUnitRouteOffsets(unit, speed, deps);
}

function setUnitRouteSnapshotState(unit, deps = {}) {
  return laneCommandSystem.setUnitRouteSnapshotState(unit, deps);
}

function computeUnitRoutePathIndex(unit) {
  return routeGraph.computeUnitRoutePathIndex(unit);
}

function sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset = 0, lateralOffset = 0) {
  return routeGraph.sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset, lateralOffset);
}

function resolveSpawnOriginForUnit(unit, targetLane, deps = {}) {
  return laneCommandSystem.resolveSpawnOriginForUnit(unit, targetLane, deps);
}

function resolveRouteContractForUnit(game, targetLane, unit, deps = {}) {
  return laneCommandSystem.resolveRouteContractForUnit(game, targetLane, unit, deps);
}

function resolveRedirectRouteContractForExistingLaneControlledUnit(game, currentLane, targetLane, unit, deps = {}) {
  return laneCommandSystem.resolveRedirectRouteContractForExistingLaneControlledUnit(
    game,
    currentLane,
    targetLane,
    unit,
    deps
  );
}

function initializeMovingUnitRouteState(game, targetLane, unit, spawnLogicalPos, deps = {}) {
  return laneCommandSystem.initializeMovingUnitRouteState(game, targetLane, unit, spawnLogicalPos, deps);
}

function applyRouteContractToExistingUnit(unit, routeContract, currentPosition = null, deps = {}) {
  return laneCommandSystem.applyRouteContractToExistingUnit(unit, routeContract, currentPosition, deps);
}

function getUnitForwardDirection(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return { x: 0, y: 1 };
  const sample = sampleContinuousRoutePosition(
    unit.routeSegments,
    computeUnitRoutePathIndex(unit),
    0,
    0
  );
  return sample ? sample.tangent : { x: 0, y: 1 };
}

function buildSampledPathFromSegments(routeSegments, sampleCount = 28) {
  return routeGraph.buildSampledPathFromSegments(routeSegments, sampleCount);
}

function sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset = 0) {
  return routeGraph.sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset);
}

function projectPointOntoPolyline(points, targetPoint) {
  return routeGraph.projectPointOntoPolyline(points, targetPoint);
}

function projectPointOntoRouteSegments(routeSegments, targetPoint) {
  return routeGraph.projectPointOntoRouteSegments(routeSegments, targetPoint);
}

function syncUnitRouteStateToWorldPosition(unit, worldPosition = null, deps = {}) {
  return laneCommandSystem.syncUnitRouteStateToWorldPosition(unit, worldPosition, deps);
}

function syncMovedUnitPathState(unit, deps = {}) {
  return laneCommandSystem.syncMovedUnitPathState(unit, deps);
}

function buildRoutePathId(routeSegments) {
  return routeGraph.buildRoutePathId(routeSegments);
}

function resolveUnitNextWaypoint(unit) {
  return routeGraph.resolveUnitNextWaypoint(unit);
}

module.exports = {
  normalize2D,
  perpendicular2D,
  dot2D,
  getLaneNodeId,
  getWaveSpawnNodeId,
  getLaneCombatAxes,
  getBarracksRouteStartNodeId,
  getLaneCoreNodeIdForRouteNode,
  isBarracksRouteStartNode,
  getWaveSpawnWorldPosition,
  getPadWorldPosition,
  getBarracksSiteWorldPosition,
  buildRouteSegments,
  parseRouteSegmentId,
  getRouteLength,
  sampleRoutePosition,
  advanceRouteState,
  relaxUnitRouteOffsets,
  setUnitRouteSnapshotState,
  computeUnitRoutePathIndex,
  sampleContinuousRoutePosition,
  resolveSpawnOriginForUnit,
  resolveRouteContractForUnit,
  resolveRedirectRouteContractForExistingLaneControlledUnit,
  initializeMovingUnitRouteState,
  applyRouteContractToExistingUnit,
  getUnitForwardDirection,
  buildSampledPathFromSegments,
  sampleRouteByDistanceNorm,
  projectPointOntoPolyline,
  projectPointOntoRouteSegments,
  syncUnitRouteStateToWorldPosition,
  syncMovedUnitPathState,
  buildRoutePathId,
  resolveUnitNextWaypoint,
};
