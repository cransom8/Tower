const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const { loadAuthoredEnvironmentLayout } = require("../authoredEnvironmentLayout");

const LIVE_PREFAB_PATH = path.resolve(
  __dirname,
  "../../unity-client/Assets/AddressableContent/Environment/GameEnvironment.prefab"
);

test("authored environment layout parses the current prefab and keeps widened fort spacing", () => {
  const layout = loadAuthoredEnvironmentLayout();

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
  const layout = loadAuthoredEnvironmentLayout();

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

function extractScalar(block, key) {
  const match = block.match(new RegExp(`\\r?\\n  ${escapeRegex(key)}: ([^\\r\\n]+)\\r?\\n`));
  return match ? String(match[1]).trim() : "";
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
