"use strict";

const fs = require("fs");
const path = require("path");

const PREFAB_PATH = path.resolve(
  __dirname,
  "../unity-client/Assets/AddressableContent/Environment/GameEnvironment.prefab"
);

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

  if (!fs.existsSync(PREFAB_PATH)) {
    throw new Error(
      `[authoredEnvironmentLayout] Required environment prefab is missing at '${PREFAB_PATH}'.`
    );
  }

  const prefabText = fs.readFileSync(PREFAB_PATH, "utf8");
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

  s_cachedAuthoredEnvironmentLayout = Object.freeze({
    prefabPath: PREFAB_PATH,
    mineCenter: Object.freeze({
      x: roundWorldAxis(mineCenterWorld.x),
      y: roundWorldAxis(mineCenterWorld.z),
    }),
    lanes: Object.freeze({
      red: freezeLaneLayout(lanes.red),
      yellow: freezeLaneLayout(lanes.yellow),
      blue: freezeLaneLayout(lanes.blue),
      green: freezeLaneLayout(lanes.green),
    }),
  });

  return s_cachedAuthoredEnvironmentLayout;
}

function extractScalar(block, key) {
  const match = block.match(new RegExp(`\\n  ${escapeRegex(key)}: (.+)\\n`));
  return match ? String(match[1]).trim() : "";
}

function extractFileId(block, key) {
  const match = block.match(new RegExp(`\\n  ${escapeRegex(key)}: \\{fileID: ([0-9]+)\\}`));
  return match ? String(match[1]) : "";
}

function extractVector3(block, key) {
  const match = block.match(
    new RegExp(`\\n  ${escapeRegex(key)}: \\{x: ([^,]+), y: ([^,]+), z: ([^}]+)\\}`)
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
    new RegExp(`\\n  ${escapeRegex(key)}: \\{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\\}`)
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

    if (!lane.tradeOutpost) {
      throw new Error(
        `[authoredEnvironmentLayout] Lane '${laneKey}' is missing required authored trade outpost 'BeastLair'.`
      );
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
