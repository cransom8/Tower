# Battlefield Remodel Handoff

## Goal

Remodel multiplayer from `4 isolated straight lanes` into a `shared lava-lake battlefield` with:

- `1` center spawn island
- `4` fixed-color player branches
- `2` side-based allied teams
- `2` merge landmasses
- `2` final castle bridges
- `1` castle per side

Players still build on their own branch, but units spawn from the center and funnel through a shared route toward the opposing castle.

## Fixed Slot Model

The current agreed default slot layout is:

- `lane 0`: `Red Branch`, `left` side, attacks `right` castle
- `lane 1`: `Gold Branch`, `left` side, attacks `right` castle
- `lane 2`: `Blue Branch`, `right` side, attacks `left` castle
- `lane 3`: `Green Branch`, `right` side, attacks `left` castle

Players should be randomized into these fixed slots. Colors stay attached to the map, not regenerated per match.

## What Is Already Done

### Server

In [server/sim-multilane.js](/c:/Users/Crans/castle-defender/server/sim-multilane.js):

- Added fixed slot metadata:
  - `slotKey`
  - `side`
  - `slotColor`
  - `branchId`
  - `branchLabel`
  - `castleSide`
- Added battlefield topology metadata:
  - `mapType`
  - `centerIslandId`
  - `castles`
  - `mergeZones`
  - `buildZones`
  - `sharedZonesBuildable`
  - `slotDefinitions`
- Added topology/slot metadata to:
  - `createMLGame()`
  - `createMLSnapshot()`
  - `createMLPublicConfig()`

In [server/game/multilaneRuntime.js](/c:/Users/Crans/castle-defender/server/game/multilaneRuntime.js):

- Added slot metadata to `laneAssignments`
- Added `battlefieldTopology` to `ml_match_ready`
- Normalized `ml_match_config` to use the same slot/topology-aware config

### Unity payload models

In [unity-client/Assets/Scripts/Net/GameState.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Net/GameState.cs):

- Added slot/topology payload types:
  - `MLBattlefieldTopology`
  - `MLSlotDefinition`
  - `MLCastleDef`
  - `MLMergeZoneDef`
  - `MLBuildZoneDef`
- Extended:
  - `MLMatchReadyPayload`
  - `MLLaneAssignment`
  - `MLMatchConfig`
  - `MLSnapshot`
  - `MLLaneSnap`

### Unity consumption

In [unity-client/Assets/Scripts/Net/SnapshotApplier.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Net/SnapshotApplier.cs):

- Stores latest `MLMatchReady` and `MLMatchConfig`
- Exposes current battlefield topology
- Adds helper methods:
  - `GetLaneAssignment()`
  - `AreLanesAllied()`
  - `GetLaneColor()`
  - `TryResolveSlotColor()`

In [unity-client/Assets/Scripts/Game/LaneRenderer.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/LaneRenderer.cs):

- Units now tint by fixed slot color
- Ally/enemy rim coloring now respects side/team relationship

In [unity-client/Assets/Scripts/UI/LaneTabs.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/UI/LaneTabs.cs):

- Tabs use branch labels from payloads
- Tab colors use fixed slot colors
- Enemy overlay now respects alliance instead of `viewed lane != my lane`

## What Is Not Done Yet

The actual battlefield is still functionally an old straight-lane model.

Not yet implemented:

- Shared center-island spawn geometry
- Branch-plus-funnel pathing
- Merge landmass routing
- Final castle bridge routing
- Side-based world layout in Unity scene
- Branch-local build zones vs shared non-build zones in world coordinates
- Camera framing for full battlefield
- AI send/path evaluation for funnel map

## Remaining Plan

### Phase 1: Transitional Unity Presentation

Goal: make the Unity scene visually acknowledge branch identity and side ownership before the full geometry rewrite.

Target files:

- [unity-client/Assets/Scripts/Game/TileGrid.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/TileGrid.cs)
- [unity-client/Assets/Scripts/Game/GameManager.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/GameManager.cs)
- [unity-client/Assets/Scripts/Net/SnapshotApplier.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Net/SnapshotApplier.cs)

Tasks:

- Tint or decorate the viewed branch with its fixed slot color
- Surface side/castle metadata in HUD if useful
- Prepare camera anchoring for wider battlefield framing
- Keep build interaction branch-local for now

### Phase 2: Server Pathing Refactor

Goal: replace `one private lane path` with `branch + merge + final bridge + castle`.

Primary file:

- [server/sim-multilane.js](/c:/Users/Crans/castle-defender/server/sim-multilane.js)

Tasks:

- Replace single spawn/castle assumptions:
  - `SPAWN_X`
  - `SPAWN_YG`
  - `CASTLE_X`
  - `CASTLE_YG`
- Replace private lane BFS assumption with stitched/shared path segments
- Keep player ownership on units after they enter shared routes
- Preserve private build grids in first pass
- Update endpoint damage to hit side castle, not private lane end

### Phase 3: Unity Battlefield Geometry

Goal: move Unity from one-lane board rendering to battlefield rendering.

Primary files:

- [unity-client/Assets/Scripts/Game/TileGrid.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/TileGrid.cs)
- [unity-client/Assets/Scripts/Game/LaneRenderer.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/LaneRenderer.cs)
- [unity-client/Assets/Scripts/Game/ProjectileSystem.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/ProjectileSystem.cs)
- [unity-client/Assets/Scripts/Game/GameManager.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/GameManager.cs)

Tasks:

- Replace single rectangular lane assumptions in world positioning
- Add battlefield coordinate mapping for:
  - center island
  - 4 branches
  - 2 merge zones
  - 2 castle bridges
  - 2 castles
- Render shared funnel space as non-buildable
- Keep branch build zones interactable
- Rework camera framing around full battlefield instead of one lane

### Phase 4: Match/UX Cleanup

Primary files:

- [unity-client/Assets/Scripts/UI/InfoBar.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/UI/InfoBar.cs)
- [unity-client/Assets/Scripts/UI/CmdBar.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/UI/CmdBar.cs)
- [unity-client/Assets/Scripts/UI/LobbyUI.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/UI/LobbyUI.cs)
- [client/game.js](/c:/Users/Crans/castle-defender/client/game.js)

Tasks:

- Show side/team + fixed branch identity cleanly
- Remove ambiguous generic lane naming where possible
- Ensure lobby and spectator views respect fixed slot layout

### Phase 5: AI and Rules

Primary files:

- [server/ai.js](/c:/Users/Crans/castle-defender/server/ai.js)
- [server/ai/targeting.js](/c:/Users/Crans/castle-defender/server/ai/targeting.js)

Tasks:

- Replace round-robin outgoing target assumptions
- Target opposing side/castle route instead of private lane endpoint
- Evaluate pressure on merged funnels
- Keep tower building branch-local

## Recommended Next Step For Claude

The best next implementation step is:

1. Rewrite server pathing in [server/sim-multilane.js](/c:/Users/Crans/castle-defender/server/sim-multilane.js) around branch-plus-funnel routing.
2. Then rewrite Unity world positioning in [unity-client/Assets/Scripts/Game/TileGrid.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/TileGrid.cs) and [unity-client/Assets/Scripts/Game/LaneRenderer.cs](/c:/Users/Crans/castle-defender/unity-client/Assets/Scripts/Game/LaneRenderer.cs).

Trying to do Unity geometry first without server path changes will create fake visuals on top of old straight-lane simulation.

## Risks

- The current codebase still assumes `viewed lane == self-contained board` in several places.
- The full battlefield rewrite will likely require replacing `TileGrid.TileToWorld()` as the single source of lane coordinate mapping.
- Build-zone validation should remain branch-local until shared route movement is stable.

## Minimal Definition Of Done

- Units spawn from the center island by branch
- Branches merge on each side
- Units traverse shared final bridge to castle
- Each player can only build on their own branch
- Unity shows four fixed branch colors in-world
- Teaming is side-based, not ad hoc lane coloring
