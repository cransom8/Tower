const test = require("node:test");
const assert = require("node:assert/strict");

const { loadAuthoredEnvironmentLayout } = require("../authoredEnvironmentLayout");

test("authored environment layout parses the current prefab and keeps widened fort spacing", () => {
  const layout = loadAuthoredEnvironmentLayout();

  assert.ok(layout, "expected authored environment layout");
  assert.equal(layout.mineCenter.x, 0, "mine center should stay at authored origin x");
  assert.equal(layout.mineCenter.y, 0, "mine center should stay at authored origin y");

  for (const laneKey of ["red", "yellow", "blue", "green"]) {
    const lane = layout.lanes[laneKey];
    assert.ok(lane, `expected authored lane '${laneKey}'`);
    assert.ok(lane.pads.town_core_pad, `expected town core pad for '${laneKey}'`);
    assert.ok(lane.pads.gate_front_pad, `expected front gate pad for '${laneKey}'`);
    assert.ok(lane.barracks.center, `expected center barracks for '${laneKey}'`);
    assert.ok(lane.barracks.left, `expected left barracks for '${laneKey}'`);
    assert.ok(lane.barracks.right, `expected right barracks for '${laneKey}'`);
    assert.ok(lane.tradeOutpost, `expected trade outpost for '${laneKey}'`);
  }

  assert.ok(layout.lanes.red.pads.town_core_pad.x <= -245, "red town core should stay on the widened left footprint");
  assert.ok(layout.lanes.yellow.pads.town_core_pad.x >= 245, "yellow town core should stay on the widened right footprint");
  assert.ok(layout.lanes.blue.pads.town_core_pad.y <= -245, "blue town core should stay on the widened lower footprint");
  assert.ok(layout.lanes.green.pads.town_core_pad.y >= 245, "green town core should stay on the widened upper footprint");
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
