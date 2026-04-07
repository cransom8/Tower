"use strict";

const fs = require("fs");
const path = require("path");

const LIVE_PREFAB_PATH = path.resolve(
  __dirname,
  "../unity-client/Assets/AddressableContent/Environment/GameEnvironment.prefab"
);
const PACKAGED_SNAPSHOT_PATH = path.resolve(
  __dirname,
  "assets/authoredEnvironmentLayout.json"
);
const SNAPSHOT_SCHEMA_VERSION = 1;
const SOURCE_OVERRIDE_ENV = "AUTHORED_ENVIRONMENT_LAYOUT_SOURCE";
const SOURCE_KIND = Object.freeze({
  LIVE_PREFAB: "live_prefab",
  PACKAGED_SNAPSHOT: "packaged_snapshot",
});
const SOURCE_PREFAB_ASSET_PATH =
  "Assets/AddressableContent/Environment/GameEnvironment.prefab";

const LANE_COLOR_TO_KEY = Object.freeze({
  1: "red",
  2: "yellow",
  3: "blue",
  4: "green",
});

const LANE_NAME_TO_KEY = Object.freeze({
  red: "red",
  yellow: "yellow",
  blue: "blue",
  green: "green",
});

let s_cachedAuthoredEnvironmentLayout = null;

function loadAuthoredEnvironmentLayout() {
  if (s_cachedAuthoredEnvironmentLayout)
    return s_cachedAuthoredEnvironmentLayout;

  const source = resolveAuthoredEnvironmentLayoutSource();
  const layout =
    source.kind === SOURCE_KIND.PACKAGED_SNAPSHOT
      ? loadPackagedAuthoredEnvironmentLayoutSnapshot(source.path)
      : loadAuthoredEnvironmentLayoutFromPrefab(source.path);

  s_cachedAuthoredEnvironmentLayout = freezeAuthoredEnvironmentLayout(layout, source);
  return s_cachedAuthoredEnvironmentLayout;
}

function loadAuthoredEnvironmentLayoutFromPrefab(prefabPath) {
  const prefabText = fs.readFileSync(prefabPath, "utf8");
  const blocks = prefabText.split(/^--- /m).slice(1);
  const transforms = new Map();
  const gameObjects = new Map();
  const padComponents = [];
  const barracksComponents = [];
  const waveSpawnComponents = [];

  for (const rawBlock of blocks) {
    const block = `--- ${rawBlock}`;
    const headerMatch = block.match(/^--- !u!\d+ &([0-9]+)/m);
    if (!headerMatch)
      continue;

    const fileId = String(headerMatch[1]);
    if (/^GameObject:/m.test(block)) {
      gameObjects.set(fileId, {
        fileId,
        name: extractScalar(block, "m_Name"),
      });
      continue;
    }

    if (/^Transform:/m.test(block)) {
      const gameObjectId = extractFileId(block, "m_GameObject");
      const parentTransformId = extractFileId(block, "m_Father") || "0";
      const localPosition = extractVector3(block, "m_LocalPosition");
      const localRotation = extractQuaternion(block, "m_LocalRotation");
      transforms.set(fileId, {
        fileId,
        gameObjectId,
        parentTransformId,
        localPosition,
        localRotation,
      });
      continue;
    }

    if (/Assembly-CSharp::CastleDefender\.Game\.FortressPadAnchor/m.test(block)) {
      padComponents.push({
        gameObjectId: extractFileId(block, "m_GameObject"),
        padId: extractScalar(block, "padId"),
        buildingType: extractScalar(block, "buildingType"),
        laneColor: Number.parseInt(extractScalar(block, "laneColor"), 10) || 0,
      });
      continue;
    }

    if (/Assembly-CSharp::CastleDefender\.Game\.BarracksSiteView/m.test(block)) {
      barracksComponents.push({
        gameObjectId: extractFileId(block, "m_GameObject"),
        barracksId: extractScalar(block, "barracksId"),
        laneColor: Number.parseInt(extractScalar(block, "laneColor"), 10) || 0,
      });
      continue;
    }

    if (/Assembly-CSharp::CastleDefender\.Game\.WaveSpawnAnchor/m.test(block)) {
      waveSpawnComponents.push({
        gameObjectId: extractFileId(block, "m_GameObject"),
        anchorId: extractScalar(block, "anchorId"),
      });
    }
  }

  const transformByGameObjectId = new Map();
  for (const transform of transforms.values()) {
    if (transform && transform.gameObjectId)
      transformByGameObjectId.set(String(transform.gameObjectId), transform);
  }

  const worldCache = new Map();
  const lanes = {
    red: { pads: {}, barracks: {}, tradeOutpost: null },
    yellow: { pads: {}, barracks: {}, tradeOutpost: null },
    blue: { pads: {}, barracks: {}, tradeOutpost: null },
    green: { pads: {}, barracks: {}, tradeOutpost: null },
  };

  for (const pad of padComponents) {
    const laneKey = LANE_COLOR_TO_KEY[pad.laneColor];
    if (!laneKey)
      continue;

    const worldPosition = resolveWorldPositionForGameObject(
      pad.gameObjectId,
      transforms,
      transformByGameObjectId,
      worldCache
    );
    if (!worldPosition) {
      throw new Error(
        `[authoredEnvironmentLayout] FortressPadAnchor '${pad.padId || "<null>"}' on lane '${laneKey}' is missing a resolvable Transform.`
      );
    }

    lanes[laneKey].pads[pad.padId] = {
      x: roundWorldAxis(worldPosition.x),
      y: roundWorldAxis(worldPosition.z),
      buildingType: pad.buildingType || null,
    };
  }

  for (const site of barracksComponents) {
    const laneKey = LANE_COLOR_TO_KEY[site.laneColor];
    if (!laneKey)
      continue;

    const worldPosition = resolveWorldPositionForGameObject(
      site.gameObjectId,
      transforms,
      transformByGameObjectId,
      worldCache
    );
    if (!worldPosition) {
      throw new Error(
        `[authoredEnvironmentLayout] BarracksSiteView '${site.barracksId || "<null>"}' on lane '${laneKey}' is missing a resolvable Transform.`
      );
    }

    lanes[laneKey].barracks[site.barracksId] = {
      x: roundWorldAxis(worldPosition.x),
      y: roundWorldAxis(worldPosition.z),
    };
  }

  for (const gameObject of gameObjects.values()) {
    const match = String(gameObject && gameObject.name || "").trim().match(/^(Red|Yellow|Blue|Green)_BeastLair$/i);
    if (!match)
      continue;

    const laneKey = LANE_NAME_TO_KEY[String(match[1] || "").trim().toLowerCase()];
    if (!laneKey || !lanes[laneKey])
      continue;

    const worldPosition = resolveWorldPositionForGameObject(
      gameObject.fileId,
      transforms,
      transformByGameObjectId,
      worldCache
    );
    if (!worldPosition) {
      throw new Error(
        `[authoredEnvironmentLayout] Trade outpost '${gameObject.name || "<null>"}' on lane '${laneKey}' is missing a resolvable Transform.`
      );
    }

    lanes[laneKey].tradeOutpost = {
      x: roundWorldAxis(worldPosition.x),
      y: roundWorldAxis(worldPosition.z),
      name: "Beast Lair",
    };
  }

  const mineCenterAnchor = waveSpawnComponents.find(
    (entry) => entry && String(entry.anchorId || "").trim().toLowerCase() === "mine_center"
  );
  if (!mineCenterAnchor) {
    throw new Error(
      "[authoredEnvironmentLayout] Required WaveSpawnAnchor 'mine_center' is missing from the authored environment."
    );
  }

  const mineCenterWorld = resolveWorldPositionForGameObject(
    mineCenterAnchor.gameObjectId,
    transforms,
    transformByGameObjectId,
    worldCache
  );
  if (!mineCenterWorld) {
    throw new Error(
      "[authoredEnvironmentLayout] WaveSpawnAnchor 'mine_center' is missing a resolvable Transform."
    );
  }

  validateRequiredLaneAnchors(lanes);

  return {
    mineCenter: {
      x: roundWorldAxis(mineCenterWorld.x),
      y: roundWorldAxis(mineCenterWorld.z),
    },
    lanes,
  };
}

function loadPackagedAuthoredEnvironmentLayoutSnapshot(snapshotPath) {
  let snapshot = null;
  try {
    snapshot = JSON.parse(fs.readFileSync(snapshotPath, "utf8"));
  } catch (error) {
    const details = error && error.message ? error.message : String(error);
    throw new Error(
      `[authoredEnvironmentLayout] Failed to parse packaged authored environment snapshot at '${snapshotPath}': ${details}.`
    );
  }

  if (!snapshot || typeof snapshot !== "object" || Array.isArray(snapshot)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot at '${snapshotPath}' must contain an object payload.`
    );
  }

  if (snapshot.schemaVersion !== SNAPSHOT_SCHEMA_VERSION) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot at '${snapshotPath}' has schemaVersion '${snapshot.schemaVersion}', expected '${SNAPSHOT_SCHEMA_VERSION}'.`
    );
  }

  if (snapshot.sourcePrefabAssetPath !== SOURCE_PREFAB_ASSET_PATH) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot at '${snapshotPath}' has sourcePrefabAssetPath '${snapshot.sourcePrefabAssetPath}', expected '${SOURCE_PREFAB_ASSET_PATH}'.`
    );
  }

  const rawLanes = snapshot.lanes;
  if (!rawLanes || typeof rawLanes !== "object" || Array.isArray(rawLanes)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot at '${snapshotPath}' is missing a valid 'lanes' object.`
    );
  }

  const lanes = {
    red: normalizeSnapshotLaneLayout(rawLanes.red, "red"),
    yellow: normalizeSnapshotLaneLayout(rawLanes.yellow, "yellow"),
    blue: normalizeSnapshotLaneLayout(rawLanes.blue, "blue"),
    green: normalizeSnapshotLaneLayout(rawLanes.green, "green"),
  };

  validateRequiredLaneAnchors(lanes);

  return {
    mineCenter: normalizeSnapshotPoint(snapshot.mineCenter, "mineCenter"),
    lanes,
  };
}

function resolveAuthoredEnvironmentLayoutSource() {
  const override = normalizeSourceOverride(process.env[SOURCE_OVERRIDE_ENV]);
  if (override)
    return requireAuthoredEnvironmentLayoutSource(override);

  if (isProductionRuntime())
    return requireAuthoredEnvironmentLayoutSource(SOURCE_KIND.PACKAGED_SNAPSHOT);

  if (fs.existsSync(LIVE_PREFAB_PATH)) {
    return {
      kind: SOURCE_KIND.LIVE_PREFAB,
      path: LIVE_PREFAB_PATH,
    };
  }

  if (fs.existsSync(PACKAGED_SNAPSHOT_PATH)) {
    return {
      kind: SOURCE_KIND.PACKAGED_SNAPSHOT,
      path: PACKAGED_SNAPSHOT_PATH,
    };
  }

  throw buildMissingAuthoredEnvironmentLayoutError();
}

function requireAuthoredEnvironmentLayoutSource(kind) {
  const resolvedPath =
    kind === SOURCE_KIND.PACKAGED_SNAPSHOT
      ? PACKAGED_SNAPSHOT_PATH
      : LIVE_PREFAB_PATH;

  if (!fs.existsSync(resolvedPath)) {
    if (kind === SOURCE_KIND.PACKAGED_SNAPSHOT && isProductionRuntime()) {
      throw new Error(
        `[authoredEnvironmentLayout] Production server requires packaged authored environment snapshot at '${PACKAGED_SNAPSHOT_PATH}'. Live Unity prefab '${LIVE_PREFAB_PATH}' is not a valid production runtime dependency.`
      );
    }

    throw new Error(
      `[authoredEnvironmentLayout] Requested authored environment source '${kind}' via ${SOURCE_OVERRIDE_ENV} but required file is missing at '${resolvedPath}'.`
    );
  }

  return {
    kind,
    path: resolvedPath,
  };
}

function normalizeSourceOverride(rawValue) {
  const normalized = String(rawValue || "").trim().toLowerCase();
  if (!normalized)
    return "";

  if (normalized === SOURCE_KIND.LIVE_PREFAB || normalized === SOURCE_KIND.PACKAGED_SNAPSHOT)
    return normalized;

  throw new Error(
    `[authoredEnvironmentLayout] Invalid ${SOURCE_OVERRIDE_ENV}='${rawValue}'. Expected '${SOURCE_KIND.LIVE_PREFAB}' or '${SOURCE_KIND.PACKAGED_SNAPSHOT}'.`
  );
}

function isProductionRuntime() {
  return String(process.env.NODE_ENV || "").trim().toLowerCase() === "production";
}

function buildMissingAuthoredEnvironmentLayoutError() {
  return new Error(
    `[authoredEnvironmentLayout] Missing authored environment data. Checked live prefab '${LIVE_PREFAB_PATH}' and packaged snapshot '${PACKAGED_SNAPSHOT_PATH}'.`
  );
}

function freezeAuthoredEnvironmentLayout(layout, source) {
  return Object.freeze({
    sourceKind: source.kind,
    sourcePath: source.path,
    prefabPath: source.kind === SOURCE_KIND.LIVE_PREFAB ? source.path : null,
    mineCenter: Object.freeze({
      x: roundWorldAxis(layout.mineCenter.x),
      y: roundWorldAxis(layout.mineCenter.y),
    }),
    lanes: Object.freeze({
      red: freezeLaneLayout(layout.lanes.red),
      yellow: freezeLaneLayout(layout.lanes.yellow),
      blue: freezeLaneLayout(layout.lanes.blue),
      green: freezeLaneLayout(layout.lanes.green),
    }),
  });
}

function normalizeSnapshotLaneLayout(rawLane, laneKey) {
  if (!rawLane || typeof rawLane !== "object" || Array.isArray(rawLane)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot is missing a valid lane object for '${laneKey}'.`
    );
  }

  return {
    pads: normalizeSnapshotPads(rawLane.pads, laneKey),
    barracks: normalizeSnapshotBarracks(rawLane.barracks, laneKey),
    tradeOutpost: normalizeSnapshotTradeOutpost(rawLane.tradeOutpost, laneKey),
  };
}

function normalizeSnapshotPads(rawPads, laneKey) {
  if (!rawPads || typeof rawPads !== "object" || Array.isArray(rawPads)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' is missing a valid 'pads' object.`
    );
  }

  const pads = {};
  for (const [padId, entry] of Object.entries(rawPads)) {
    if (!padId) {
      throw new Error(
        `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' contains a pad with an empty id.`
      );
    }

    pads[padId] = normalizeSnapshotPadEntry(entry, laneKey, padId);
  }

  return pads;
}

function normalizeSnapshotPadEntry(entry, laneKey, padId) {
  if (!entry || typeof entry !== "object" || Array.isArray(entry)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' pad '${padId}' must be an object.`
    );
  }

  return {
    x: normalizeSnapshotAxis(entry.x, `lane '${laneKey}' pad '${padId}' x`),
    y: normalizeSnapshotAxis(entry.y, `lane '${laneKey}' pad '${padId}' y`),
    buildingType: normalizeSnapshotBuildingType(entry.buildingType, laneKey, padId),
  };
}

function normalizeSnapshotBuildingType(buildingType, laneKey, padId) {
  if (buildingType == null)
    return null;

  if (typeof buildingType !== "string" || !String(buildingType).trim()) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' pad '${padId}' has invalid buildingType '${buildingType}'.`
    );
  }

  return String(buildingType).trim();
}

function normalizeSnapshotBarracks(rawBarracks, laneKey) {
  if (!rawBarracks || typeof rawBarracks !== "object" || Array.isArray(rawBarracks)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' is missing a valid 'barracks' object.`
    );
  }

  const barracks = {};
  for (const [barracksId, entry] of Object.entries(rawBarracks)) {
    if (!barracksId) {
      throw new Error(
        `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' contains a barracks entry with an empty id.`
      );
    }

    barracks[barracksId] = normalizeSnapshotPoint(
      entry,
      `lane '${laneKey}' barracks '${barracksId}'`
    );
  }

  return barracks;
}

function normalizeSnapshotTradeOutpost(entry, laneKey) {
  if (entry == null)
    return null;

  if (typeof entry !== "object" || Array.isArray(entry)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' tradeOutpost must be an object or null.`
    );
  }

  if (typeof entry.name !== "string" || !String(entry.name).trim()) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot lane '${laneKey}' tradeOutpost is missing a valid name.`
    );
  }

  return {
    ...normalizeSnapshotPoint(entry, `lane '${laneKey}' tradeOutpost`),
    name: String(entry.name).trim(),
  };
}

function normalizeSnapshotPoint(entry, label) {
  if (!entry || typeof entry !== "object" || Array.isArray(entry)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot ${label} must be an object.`
    );
  }

  return {
    x: normalizeSnapshotAxis(entry.x, `${label} x`),
    y: normalizeSnapshotAxis(entry.y, `${label} y`),
  };
}

function normalizeSnapshotAxis(value, label) {
  const numeric = Number.parseFloat(value);
  if (!Number.isFinite(numeric)) {
    throw new Error(
      `[authoredEnvironmentLayout] Packaged authored environment snapshot ${label} must be a finite number, received '${value}'.`
    );
  }

  return roundWorldAxis(numeric);
}

function extractScalar(block, key) {
  const match = block.match(new RegExp(`\\r?\\n  ${escapeRegex(key)}: ([^\\r\\n]+)\\r?\\n`));
  return match ? String(match[1]).trim() : "";
}

function extractFileId(block, key) {
  const match = block.match(new RegExp(`\\r?\\n  ${escapeRegex(key)}: \\{fileID: ([0-9-]+)\\}`));
  return match ? String(match[1]) : "";
}

function extractVector3(block, key) {
  const match = block.match(
    new RegExp(`\\r?\\n  ${escapeRegex(key)}: \\{x: ([^,]+), y: ([^,]+), z: ([^}]+)\\}`)
  );
  return match
    ? {
        x: parseNumberOrDefault(match[1], 0),
        y: parseNumberOrDefault(match[2], 0),
        z: parseNumberOrDefault(match[3], 0),
      }
    : { x: 0, y: 0, z: 0 };
}

function extractQuaternion(block, key) {
  const match = block.match(
    new RegExp(`\\r?\\n  ${escapeRegex(key)}: \\{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\\}`)
  );
  return match
    ? {
        x: parseNumberOrDefault(match[1], 0),
        y: parseNumberOrDefault(match[2], 0),
        z: parseNumberOrDefault(match[3], 0),
        w: parseNumberOrDefault(match[4], 1),
      }
    : { x: 0, y: 0, z: 0, w: 1 };
}

function resolveWorldPositionForGameObject(
  gameObjectId,
  transforms,
  transformByGameObjectId,
  worldCache
) {
  const transform = transformByGameObjectId.get(String(gameObjectId || ""));
  if (!transform)
    return null;

  return resolveWorldTransform(transform.fileId, transforms, worldCache).position;
}

function resolveWorldTransform(transformFileId, transforms, worldCache) {
  const normalizedId = String(transformFileId || "");
  if (!normalizedId || normalizedId === "0") {
    return {
      position: { x: 0, y: 0, z: 0 },
      rotation: { x: 0, y: 0, z: 0, w: 1 },
    };
  }

  if (worldCache.has(normalizedId))
    return worldCache.get(normalizedId);

  const transform = transforms.get(normalizedId);
  if (!transform) {
    throw new Error(
      `[authoredEnvironmentLayout] Missing Transform '${normalizedId}' while composing authored environment world positions.`
    );
  }

  const parentWorld = resolveWorldTransform(transform.parentTransformId, transforms, worldCache);
  const rotatedLocalPosition = rotateVectorByQuaternion(
    parentWorld.rotation,
    transform.localPosition
  );
  const worldTransform = {
    position: {
      x: parentWorld.position.x + rotatedLocalPosition.x,
      y: parentWorld.position.y + rotatedLocalPosition.y,
      z: parentWorld.position.z + rotatedLocalPosition.z,
    },
    rotation: multiplyQuaternions(parentWorld.rotation, transform.localRotation),
  };

  worldCache.set(normalizedId, worldTransform);
  return worldTransform;
}

function multiplyQuaternions(left, right) {
  return {
    w: (left.w * right.w) - (left.x * right.x) - (left.y * right.y) - (left.z * right.z),
    x: (left.w * right.x) + (left.x * right.w) + (left.y * right.z) - (left.z * right.y),
    y: (left.w * right.y) - (left.x * right.z) + (left.y * right.w) + (left.z * right.x),
    z: (left.w * right.z) + (left.x * right.y) - (left.y * right.x) + (left.z * right.w),
  };
}

function rotateVectorByQuaternion(rotation, vector) {
  const vectorQuaternion = {
    w: 0,
    x: vector.x,
    y: vector.y,
    z: vector.z,
  };
  const rotated = multiplyQuaternions(
    multiplyQuaternions(rotation, vectorQuaternion),
    conjugateQuaternion(rotation)
  );
  return {
    x: rotated.x,
    y: rotated.y,
    z: rotated.z,
  };
}

function conjugateQuaternion(rotation) {
  return {
    w: rotation.w,
    x: -rotation.x,
    y: -rotation.y,
    z: -rotation.z,
  };
}

function validateRequiredLaneAnchors(lanes) {
  const requiredPadIds = ["town_core_pad", "gate_front_pad"];
  const requiredBarracksIds = ["center", "left", "right"];
  for (const laneKey of Object.keys(lanes)) {
    const lane = lanes[laneKey];
    for (const padId of requiredPadIds) {
      if (!lane.pads[padId]) {
        throw new Error(
          `[authoredEnvironmentLayout] Lane '${laneKey}' is missing required authored pad '${padId}'.`
        );
      }
    }

    for (const barracksId of requiredBarracksIds) {
      if (!lane.barracks[barracksId]) {
        throw new Error(
          `[authoredEnvironmentLayout] Lane '${laneKey}' is missing required authored barracks '${barracksId}'.`
        );
      }
    }

  }
}

function freezeLaneLayout(lane) {
  return Object.freeze({
    pads: Object.freeze(cloneObject(lane.pads)),
    barracks: Object.freeze(cloneObject(lane.barracks)),
    tradeOutpost: lane.tradeOutpost ? Object.freeze({ ...lane.tradeOutpost }) : null,
  });
}

function cloneObject(value) {
  const clone = {};
  for (const [key, entry] of Object.entries(value || {}))
    clone[key] = Object.freeze({ ...entry });
  return clone;
}

function roundWorldAxis(value) {
  const numeric = Number.parseFloat(value);
  if (!Number.isFinite(numeric))
    return 0;
  return Math.round(numeric * 1000) / 1000;
}

function parseNumberOrDefault(value, fallback) {
  const numeric = Number.parseFloat(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function escapeRegex(value) {
  return String(value || "").replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

module.exports = {
  loadAuthoredEnvironmentLayout,
};
