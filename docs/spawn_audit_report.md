# Spawn Audit Report

## Summary

This audit found two distinct spawn layers:

- Server-authored logical spawns in [`server/sim-multilane.js`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js)
- Unity-authored world-position resolution in [`unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs) and [`unity-client/Assets/Scripts/Game/LanePathMarkers.cs`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/LanePathMarkers.cs)

The server does not choose a scene/world transform for wave units. It enqueues units into a lane-local spawn rectangle using `spawnIndex -> (posX,posY)`. Unity then resolves those lane-relative units onto a world path using `LanePathMarkers`.

The highest-risk bug was on the Unity side:

- `LanePathMarkers` silently overwrote duplicate lane registrations
- route lookup could resolve by lane only without using a deterministic branch/path key
- marker point construction had fallback behavior that could hide misconfiguration

That meant a bad or duplicate marker set could hijack a lane route and visually place server-authored units in the wrong area.

## Spawn Systems

| Spawn type | Source function(s) | Input key(s) | Lane / team dependency | Resolved marker / anchor | Resolved position | Fallback before fix | Authoring |
|---|---|---|---|---|---|---|---|
| Scheduled wave enemy | [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300), [`spawnScheduledWave`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3447) | `lane`, `waveDef.unit_type`, `spawnIndex` | target lane only | Server queue origin, then Unity `LanePathMarkers` using `branchId`/lane | Server logical `(posX,posY)` from `spawnIndex`; Unity world path sample | Unity route lookup could silently pick an ambiguous lane marker set | Hybrid |
| Barracks roster send | [`buildBarracksRosterSpawnEntries`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L1830), [`spawnBarracksRosterFormation`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L1907), [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300) | `sourceLaneIndex`, `sourceBarracksId`, `spawnIndex`, `unitType` | source lane/team and target opponent lane | Server queue origin, then Unity route markers for target lane | Server logical `(posX,posY)` from `spawnIndex`; Unity world path sample | Invalid source barracks metadata was not centrally audited; Unity lane markers could still misplace the unit | Hybrid |
| Barracks hero send | [`deployBarracksHero`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L2450), [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300) | `heroKey`, `sourceLaneIndex`, `sourceBarracksId` | source lane/team and target opponent lane | Server queue origin, then Unity route markers for target lane | Server logical `(posX,posY)` from `spawnIndex`; Unity world path sample | Same marker ambiguity risk as other wave-unit rendering | Hybrid |
| Spawn queue materialization | [`mlTick`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3501) | `spawnIndex` | target lane only | `server_spawn_queue_rect` | `x = spawnIndex % GRID_W`, `y = floor(spawnIndex / GRID_W)` | No world fallback; purely logical | Server |
| Upcoming wave preview | [`createLaneUpcomingWavePreview`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3114) | scheduled wave row, barracks/hero metadata | lane snapshot only | none | preview data only, no live spawn | none | Server |
| Unity wave render spawn | [`TryResolveWaveWorldPosition`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs#L392), [`CreateWaveView`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs#L297) | `lane.branchId`, `lane.slotKey`, `lane.laneIndex`, `unit.pathIdx`, `unit.gridX` | lane branch/path registry | `LanePathMarkers` registration | world path sample + lateral offset + ground projection | duplicate lane overwrite, lane-only lookup, implicit marker fallback | Client |

## What Was Wrong

- Server spawn metadata was not centrally validated before enqueue.
- Invalid `sourceBarracksId` could be difficult to diagnose because enqueue and materialization did not emit one structured audit line.
- Unity route resolution depended on `LanePathMarkers` global registration by lane index, with silent overwrite on duplicates.
- `LanePathMarkers` could still construct a route from implicit child transforms instead of requiring the explicit configured marker array.
- `WaveSnapshotRuntimeSpawner` sampled by lane index only, even though `branchId` is the deterministic route identity from the server snapshot.

## What Was Fixed

- Added centralized server spawn validation and audit logging in [`validateSpawnDefinition`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L728), [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300), and [`mlTick`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3501).
- Server now rejects invalid barracks-authored spawns instead of quietly queueing them.
- Added structured server audit logs:
  - `[SpawnAudit][ServerQueue] queued`
  - `[SpawnAudit][ServerQueue] rejected`
  - `[SpawnAudit][ServerLive] materialized`
- Reworked `LanePathMarkers` registry to track all active registrations and fail loudly on ambiguity instead of silently overwriting an existing lane/key registration.
- Removed hidden route construction fallback in `LanePathMarkers`; explicit `CenterMarker` and all 5 `MarkerTransforms` are now required.
- Updated `WaveSnapshotRuntimeSpawner` to resolve route markers by deterministic `branchId` first, then `slotKey`, and only then use lane index when no key is available.
- Added `[SpawnAudit][ClientWave]` logs for successful world-position resolution and richer failure context for route lookup failures.

## Regression Coverage

- Server:
  - [`server/tests/spawn_resolution.test.js`](/C:/Users/Crans/RansomForge/castle-defender/server/tests/spawn_resolution.test.js)
- Unity PlayMode:
  - [`unity-client/Assets/Tests/PlayMode/LanePathMarkersRuntimeTests.cs`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Tests/PlayMode/LanePathMarkersRuntimeTests.cs)

## Remaining Manual Verification Risk

- Existing scenes/prefabs must have exactly one valid `LanePathMarkers` route per active branch key / lane.
- Any scene still relying on implicit child marker discovery will now fail loudly until configured with explicit markers.
- Barracks-local Unity-only tools such as [`BarracksAutoSpawner`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/BarracksAutoSpawner.cs) use a separate `BarracksLanePath` system and were not changed here because they are not the authoritative multilane gameplay spawn path.
