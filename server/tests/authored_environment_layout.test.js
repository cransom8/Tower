const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const LIVE_PREFAB_PATH = path.resolve(
  __dirname,
  "../../unity-client/Assets/AddressableContent/Environment/GameEnvironment.prefab"
);
const SNAPSHOT_PATH = path.resolve(
  __dirname,
  "../assets/authoredEnvironmentLayout.json"
);
const SOURCE_OVERRIDE_ENV = "AUTHORED_ENVIRONMENT_LAYOUT_SOURCE";
const SOURCE_PREFAB_ASSET_PATH =
  "Assets/AddressableContent/Environment/GameEnvironment.prefab";

test("authored environment layout parses the current prefab and keeps widened fort spacing", () => {
  const layout = loadLayoutFromSource("live_prefab");

  assert.ok(layout, "expected authored environment layout");
  assert.equal(typeof layout.mineCenter.x, "number", "mine center should expose an authored world x");
  assert.equal(typeof layout.mineCenter.y, "number", "mine center should expose an authored world y");

  for (const laneKey of ["red", "yellow", "blue", "green"]) {
    const lane = layout.lanes[laneKey];
    assert.ok(lane, `expected authored lane '${laneKey}'`);
    assert.ok(lane.pads.town_core_pad, `expected town core pad for '${laneKey}'`);
    assert.ok(lane.pads.gate_front_pad, `expected front gate pad for '${laneKey}'`);
    assert.ok(lane.barracks.center, `expected center barracks for '${laneKey}'`);
    assert.ok(lane.barracks.left, `expected left barracks for '${laneKey}'`);
    assert.ok(lane.barracks.right, `expected right barracks for '${laneKey}'`);
  }

  const redOffsetX = layout.lanes.red.pads.town_core_pad.x - layout.mineCenter.x;
  const yellowOffsetX = layout.lanes.yellow.pads.town_core_pad.x - layout.mineCenter.x;
  const blueOffsetY = layout.lanes.blue.pads.town_core_pad.y - layout.mineCenter.y;
  const greenOffsetY = layout.lanes.green.pads.town_core_pad.y - layout.mineCenter.y;

  assert.ok(redOffsetX <= -245, "red town core should stay on the widened left footprint relative to mine center");
  assert.ok(yellowOffsetX >= 245, "yellow town core should stay on the widened right footprint relative to mine center");
  assert.ok(blueOffsetY <= -245, "blue town core should stay on the widened lower footprint relative to mine center");
  assert.ok(greenOffsetY >= 245, "green town core should stay on the widened upper footprint relative to mine center");
});

test("authored Town Core and center Barracks remain separate committed slots", () => {
  const layout = loadLayoutFromSource("live_prefab");

  for (const laneKey of ["red", "yellow", "blue", "green"]) {
    const lane = layout.lanes[laneKey];
    const townCorePad = lane?.pads?.town_core_pad;
    const barracksPad = lane?.pads?.barracks_pad;
    const centerBarracks = lane?.barracks?.center;

    assert.ok(townCorePad, `expected town core pad for '${laneKey}'`);
    assert.ok(barracksPad, `expected barracks pad for '${laneKey}'`);
    assert.ok(centerBarracks, `expected center barracks site for '${laneKey}'`);
    assert.equal(townCorePad.buildingType, "town_core", `expected town core building type for '${laneKey}'`);
    assert.equal(barracksPad.buildingType, "barracks", `expected barracks building type for '${laneKey}'`);
    assert.deepEqual(
      { x: barracksPad.x, y: barracksPad.y },
      { x: centerBarracks.x, y: centerBarracks.y },
      `expected center barracks site to stay aligned with barracks_pad for '${laneKey}'`
    );
    assert.notDeepEqual(
      { x: townCorePad.x, y: townCorePad.y },
      { x: barracksPad.x, y: barracksPad.y },
      `expected Town Core and Barracks to remain distinct slots for '${laneKey}'`
    );
  }
});

test("packaged authored environment snapshot matches the live prefab layout", () => {
  const liveLayout = loadLayoutFromSource("live_prefab");
  const packagedLayout = loadLayoutFromSource("packaged_snapshot");
  const snapshotPayload = JSON.parse(fs.readFileSync(SNAPSHOT_PATH, "utf8"));

  assert.equal(snapshotPayload.schemaVersion, 1, "expected packaged snapshot schema version 1");
  assert.equal(
    snapshotPayload.sourcePrefabAssetPath,
    SOURCE_PREFAB_ASSET_PATH,
    "expected packaged snapshot to record its live prefab source asset path"
  );
  assert.equal(
    packagedLayout.sourceKind,
    "packaged_snapshot",
    "expected packaged snapshot override to load the packaged server asset"
  );
  assert.deepEqual(
    {
      mineCenter: packagedLayout.mineCenter,
      lanes: packagedLayout.lanes,
    },
    {
      mineCenter: liveLayout.mineCenter,
      lanes: liveLayout.lanes,
    },
    "expected packaged snapshot runtime layout to stay byte-for-byte aligned with the live prefab layout"
  );
  assert.deepEqual(
    snapshotPayload,
    {
      schemaVersion: 1,
      sourcePrefabAssetPath: SOURCE_PREFAB_ASSET_PATH,
      mineCenter: liveLayout.mineCenter,
      lanes: liveLayout.lanes,
    },
    "expected packaged authored environment snapshot JSON to match the live prefab layout exactly"
  );
});

test("production runtime defaults authored environment layout to the packaged snapshot", () => {
  const layout = loadLayoutFromSource(null, "production");

  assert.equal(
    layout.sourceKind,
    "packaged_snapshot",
    "expected production runtime to load the packaged authored environment snapshot by default"
  );
  assert.equal(
    path.resolve(layout.sourcePath),
    SNAPSHOT_PATH,
    "expected production runtime to resolve the packaged authored environment snapshot path"
  );
});

test("live environment prefab keeps one shared-defense anchor per lane and pad id", () => {
  const prefabText = fs.readFileSync(LIVE_PREFAB_PATH, "utf8");
  const blocks = prefabText.split(/^--- /m).slice(1);
  const counts = new Map();

  for (const rawBlock of blocks) {
    const block = `--- ${rawBlock}`;
    if (!/Assembly-CSharp::CastleDefender\.Game\.FortressPadAnchor/m.test(block))
      continue;

    const buildingType = extractScalar(block, "buildingType").toLowerCase();
    if (!["wall", "gate", "turret"].includes(buildingType))
      continue;

    const laneColor = extractScalar(block, "laneColor");
    const padId = extractScalar(block, "padId");
    if (!laneColor || !padId)
      continue;

    const key = `${laneColor}::${padId}`;
    counts.set(key, (counts.get(key) || 0) + 1);
  }

  const duplicates = Array.from(counts.entries())
    .filter(([, count]) => count > 1)
    .map(([key, count]) => `${key} x${count}`);

  assert.deepEqual(duplicates, [], "expected the live environment prefab to avoid duplicate shared-defense anchors");
});

test("live environment prefab keeps every direct defense child under wall parents bridge-controlled", () => {
  const prefabText = fs.readFileSync(LIVE_PREFAB_PATH, "utf8");
  const prefab = parsePrefabHierarchy(prefabText);

  for (const team of ["RED", "YELLOW", "BLUE", "GREEN"]) {
    for (const side of ["FRONT", "LEFT", "RIGHT", "REAR"]) {
      const wallParentPath = `GameEnvironment/${capitalize(team.toLowerCase())} Fort/Walls/${team}_${side}_WALL`;
      const wallParentTransformId = prefab.transformIdByPath.get(wallParentPath);
      assert.ok(wallParentTransformId, `expected wall parent '${wallParentPath}'`);

      const directChildren = prefab.childTransformsByParent.get(wallParentTransformId) || [];
      const defenseChildren = directChildren
        .map((transformId) => ({
          transformId,
          gameObjectId: prefab.transformToGameObject.get(transformId),
          name: prefab.gameObjectNames.get(prefab.transformToGameObject.get(transformId)) || "",
        }))
        .filter((entry) => /_(Wall|Tower|Gate)_/i.test(entry.name));

      assert.ok(defenseChildren.length > 0, `expected direct defense children under '${wallParentPath}'`);

      for (const child of defenseChildren) {
        const componentSet = prefab.componentsByGameObject.get(child.gameObjectId) || new Set();
        assert.ok(
          componentSet.has("CastleDefender.Game.FortressPadAnchor"),
          `expected '${child.name}' under '${wallParentPath}' to keep a FortressPadAnchor`
        );
        assert.ok(
          componentSet.has("CastleDefender.Game.SnapshotBuildingVisualBridge"),
          `expected '${child.name}' under '${wallParentPath}' to keep a SnapshotBuildingVisualBridge`
        );
      }
    }
  }
});

function extractScalar(block, key) {
  const match = block.match(new RegExp(`\\r?\\n  ${escapeRegex(key)}: ([^\\r\\n]+)\\r?\\n`));
  return match ? String(match[1]).trim() : "";
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function parsePrefabHierarchy(prefabText) {
  const blocks = prefabText.split(/^--- /m).slice(1).map((raw) => `--- ${raw}`);
  const gameObjectNames = new Map();
  const transformToGameObject = new Map();
  const gameObjectToTransform = new Map();
  const transformParents = new Map();
  const componentsByGameObject = new Map();

  for (const block of blocks) {
    const gameObjectHeader = block.match(/^--- !u!1 &(\d+)/m);
    if (gameObjectHeader) {
      const gameObjectId = gameObjectHeader[1];
      const name = extractScalar(block, "m_Name");
      gameObjectNames.set(gameObjectId, name);
      continue;
    }

    const transformHeader = block.match(/^--- !u!4 &(\d+)/m);
    if (transformHeader) {
      const transformId = transformHeader[1];
      const gameObjectId = extractFileId(block, "m_GameObject");
      const parentTransformId = extractFileId(block, "m_Father") || "0";
      if (gameObjectId) {
        transformToGameObject.set(transformId, gameObjectId);
        gameObjectToTransform.set(gameObjectId, transformId);
      }
      transformParents.set(transformId, parentTransformId);
      continue;
    }

    const monoBehaviourHeader = block.match(/^--- !u!114 &(\d+)/m);
    if (monoBehaviourHeader) {
      const gameObjectId = extractFileId(block, "m_GameObject");
      const classIdentifier = extractScalar(block, "m_EditorClassIdentifier");
      if (!gameObjectId || !classIdentifier)
        continue;

      if (!componentsByGameObject.has(gameObjectId))
        componentsByGameObject.set(gameObjectId, new Set());
      componentsByGameObject.get(gameObjectId).add(normalizeClassIdentifier(classIdentifier));
    }
  }

  const childTransformsByParent = new Map();
  for (const [transformId, parentTransformId] of transformParents.entries()) {
    if (!childTransformsByParent.has(parentTransformId))
      childTransformsByParent.set(parentTransformId, []);
    childTransformsByParent.get(parentTransformId).push(transformId);
  }

  const transformIdByPath = new Map();
  for (const [transformId] of transformParents.entries()) {
    const path = buildTransformPath(transformId, transformParents, transformToGameObject, gameObjectNames);
    if (path)
      transformIdByPath.set(path, transformId);
  }

  return {
    gameObjectNames,
    transformToGameObject,
    childTransformsByParent,
    componentsByGameObject,
    transformIdByPath,
  };
}

function buildTransformPath(transformId, transformParents, transformToGameObject, gameObjectNames) {
  const parts = [];
  let currentTransformId = transformId;

  while (currentTransformId && currentTransformId !== "0") {
    const gameObjectId = transformToGameObject.get(currentTransformId);
    if (!gameObjectId)
      return "";

    parts.push(gameObjectNames.get(gameObjectId) || "");
    currentTransformId = transformParents.get(currentTransformId) || "0";
  }

  return parts.reverse().join("/");
}

function extractFileId(block, key) {
  const match = block.match(new RegExp(`\\r?\\n  ${escapeRegex(key)}: \\{fileID: ([^\\r\\n}]+)\\}`));
  return match ? String(match[1]).trim() : "";
}

function normalizeClassIdentifier(classIdentifier) {
  const normalized = String(classIdentifier || "").trim();
  const separatorIndex = normalized.indexOf("::");
  return separatorIndex >= 0 ? normalized.slice(separatorIndex + 2) : normalized;
}

function capitalize(value) {
  return value ? value[0].toUpperCase() + value.slice(1) : value;
}

function loadLayoutFromSource(sourceKind, nodeEnv) {
  const modulePath = require.resolve("../authoredEnvironmentLayout");
  const previousOverride = process.env[SOURCE_OVERRIDE_ENV];
  const previousNodeEnv = process.env.NODE_ENV;

  if (sourceKind)
    process.env[SOURCE_OVERRIDE_ENV] = sourceKind;
  else
    delete process.env[SOURCE_OVERRIDE_ENV];

  if (nodeEnv == null)
    delete process.env.NODE_ENV;
  else
    process.env.NODE_ENV = nodeEnv;

  delete require.cache[modulePath];

  try {
    const { loadAuthoredEnvironmentLayout } = require("../authoredEnvironmentLayout");
    return loadAuthoredEnvironmentLayout();
  } finally {
    delete require.cache[modulePath];
    if (previousOverride == null)
      delete process.env[SOURCE_OVERRIDE_ENV];
    else
      process.env[SOURCE_OVERRIDE_ENV] = previousOverride;

    if (previousNodeEnv == null)
      delete process.env.NODE_ENV;
    else
      process.env.NODE_ENV = previousNodeEnv;
  }
}
