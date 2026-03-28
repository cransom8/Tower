# Spawn Audit Report

## Summary

This audit now refers to the live server-authoritative route graph plus Unity anchor projection path.

- Server-authored logical spawns in [`server/sim-multilane.js`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js)
- Unity-authored world-position resolution in [`unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs)

The server chooses the authoritative route state for moving units. It enqueues units into a lane-local spawn rectangle, builds route-graph segments, and serializes `routeType`, `currentSegment`, `segmentProgress`, and `routeWorldX/Y`. Unity then projects that authoritative state onto the authored battlefield anchors.

Historical note: this document originally covered a `LanePathMarkers`-based client path sampler. That legacy pathing has now been retired and removed from the project because it was no longer the live authority and was a source of confusion during debugging.

The highest-risk remaining bugs are now route-node or anchor mismatches inside `WaveSnapshotRuntimeSpawner`, not duplicate legacy marker registrations.

## Spawn Systems

| Spawn type | Source function(s) | Input key(s) | Lane / team dependency | Resolved marker / anchor | Resolved position | Fallback before fix | Authoring |
|---|---|---|---|---|---|---|---|
| Scheduled wave enemy | [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300), [`spawnScheduledWave`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3447) | `lane`, `waveDef.unit_type`, `spawnIndex` | target lane only | Server queue origin, then Unity battlefield route anchors | Server logical `(posX,posY)` from `spawnIndex`; Unity projects the authoritative route graph state into world space | Legacy client-side marker route sampling used to be a risk here; removed | Hybrid |
| Barracks roster send | [`buildBarracksRosterSpawnEntries`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L1830), [`spawnBarracksRosterFormation`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L1907), [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300) | `sourceLaneIndex`, `sourceBarracksId`, `spawnIndex`, `unitType` | source lane/team and target opponent lane | Server queue origin, then Unity route markers for target lane | Server logical `(posX,posY)` from `spawnIndex`; Unity world path sample | Invalid source barracks metadata was not centrally audited; Unity lane markers could still misplace the unit | Hybrid |
| Barracks hero send | [`deployBarracksHero`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L2450), [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300) | `heroKey`, `sourceLaneIndex`, `sourceBarracksId` | source lane/team and target opponent lane | Server queue origin, then Unity route markers for target lane | Server logical `(posX,posY)` from `spawnIndex`; Unity world path sample | Same marker ambiguity risk as other wave-unit rendering | Hybrid |
| Spawn queue materialization | [`mlTick`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3501) | `spawnIndex` | target lane only | `server_spawn_queue_rect` | `x = spawnIndex % GRID_W`, `y = floor(spawnIndex / GRID_W)` | No world fallback; purely logical | Server |
| Upcoming wave preview | [`createLaneUpcomingWavePreview`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3114) | scheduled wave row, barracks/hero metadata | lane snapshot only | none | preview data only, no live spawn | none | Server |
| Unity wave render spawn | [`TryResolveSnapshotWorldPosition`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs#L1205), [`TryResolveBattlefieldRouteWorldPosition`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs#L1373) | `routeType`, `currentSegment`, `segmentProgress`, `routeWorldX`, `routeWorldY`, `routeTargetNode` | server-authored route graph plus scene anchor registry | `WaveSpawnAnchor`, front gates, town core anchors, barracks site anchors | route graph projection + ground projection | anchor mismatch or route-node mapping issues | Client |

## What Was Wrong

- Server spawn metadata was not centrally validated before enqueue.
- Invalid `sourceBarracksId` could be difficult to diagnose because enqueue and materialization did not emit one structured audit line.
- Unity route resolution can still fail if battlefield anchors are missing or bound to the wrong lane.
- Live combat-space projection can visually snap if a unit transitions into combat with bad route-node or lane-key mapping.
- Historical `LanePathMarkers` sampling is no longer part of the runtime path.

## What Was Fixed

- Added centralized server spawn validation and audit logging in [`validateSpawnDefinition`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L728), [`_spawnWaveUnit`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3300), and [`mlTick`](/C:/Users/Crans/RansomForge/castle-defender/server/sim-multilane.js#L3501).
- Server now rejects invalid barracks-authored spawns instead of quietly queueing them.
- Added structured server audit logs:
  - `[SpawnAudit][ServerQueue] queued`
  - `[SpawnAudit][ServerQueue] rejected`
  - `[SpawnAudit][ServerLive] materialized`
- Kept authoritative spawn validation and route-state logging on the server.
- Moved Unity wave world-position resolution fully onto the live battlefield route/anchor projection path in [`unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs).
- Retired the unused `LanePathMarkers` path-sampling system and removed its authored components from the environment prefabs.
- Added and retained `[SpawnAudit]` logging around client route projection failures so anchor or route-node issues fail loudly.

## Regression Coverage

- Server:
  - [`server/tests/spawn_resolution.test.js`](/C:/Users/Crans/RansomForge/castle-defender/server/tests/spawn_resolution.test.js)

## Remaining Manual Verification Risk

- Existing scenes/prefabs must still provide the named battlefield anchors and barracks/town-core bindings that `WaveSnapshotRuntimeSpawner` expects.
- Any scene with incorrect lane-key naming or misplaced anchors can still project units onto the wrong visual route even though the server route graph is authoritative.
- Barracks-local Unity-only tools such as [`BarracksAutoSpawner`](/C:/Users/Crans/RansomForge/castle-defender/unity-client/Assets/Scripts/Game/BarracksAutoSpawner.cs) use a separate `BarracksLanePath` system and were not changed here because they are not the authoritative multilane gameplay spawn path.
