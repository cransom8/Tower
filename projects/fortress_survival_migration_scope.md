# Fortress Survival Migration Scope

Date: 2026-03-26

## Current Game Source Of Truth

Castle Defender is now a fortress survival PvP game.

- Waves spawn in the center and travel toward each fortress.
- Players sabotage opponents by sending barracks units down side lanes into enemy fortresses.
- Matches support ranked, casual, bots, friends, solo continuation, and survival through wave 30.
- The immediate playable milestone is simple: survive as many waves as possible without the game crashing.

This means the repo should optimize for one shared combat system and one shared movement system. Any older system that still assumes maze building, classic tower placement, static Legion-TD-style setup, or separate classic runtime paths should be archived unless it is still required by the current fortress-survival loop.

## Keep Active

These files and systems align with the current game definition and should be treated as the protected path during cleanup.

### Server runtime

- `server/sim-multilane.js`
- `server/sim-multilane-serialization.js`
- `server/game/multilaneRuntime.js`
- `server/socket/registerHandlers.js`
- `server/socket/helpers.js`
- `server/index.js`
- `server/gameConfig.js`
- `server/gameDefaults.js`

### Server regression coverage for the active game

- `server/tests/town_core_combat.test.js`
- `server/tests/cooperative_wave_defense_flow.test.js`
- `server/tests/solo_continue_after_win.test.js`
- `server/tests/start_next_wave_gate.test.js`
- `server/tests/survival_wave_autostart.test.js`
- `server/tests/spawn_resolution.test.js`
- `server/tests/barracks_unlock_flow.test.js`
- `server/tests/route_graph_routing.test.js`
- `server/tests/fortress_pad_progression.test.js`
- `server/tests/market_progression_config.test.js`
- `server/tests/multilane_config_validation.test.js`
- `server/tests/multilane_runtime_lane_assignment.test.js`

### Unity runtime and UI tied to the active loop

- `unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs`
- `unity-client/Assets/Scripts/Game/BarracksAutoSpawner.cs`
- `unity-client/Assets/Scripts/Game/BarracksLanePath.cs`
- `unity-client/Assets/Scripts/Game/BarracksSiteView.cs`
- `unity-client/Assets/Scripts/Game/LaneSnapshotCombatant.cs`
- `unity-client/Assets/Scripts/Game/LanePathMarkers.cs`
- `unity-client/Assets/Scripts/Game/LanePathSpawnedUnit.cs`
- `unity-client/Assets/Scripts/Game/FortressPadRuntimeBinder.cs`
- `unity-client/Assets/Scripts/Game/SnapshotBuildingVisualBridge.cs`
- `unity-client/Assets/Scripts/Game/GameManager.cs`
- `unity-client/Assets/Scripts/UI/BarracksPanel.cs`
- `unity-client/Assets/Scripts/UI/MobileMatchHud.cs`
- `unity-client/Assets/Scripts/UI/InfoBar.cs`
- `unity-client/Assets/Scripts/UI/GameOverUI.cs`
- `unity-client/Assets/Scripts/UI/PostGameSceneController.cs`
- `unity-client/Assets/Scripts/Net/GameState.cs`
- `unity-client/Assets/Scripts/Net/NetworkManager.cs`
- `unity-client/Assets/Scripts/Net/SnapshotApplier.cs`

### Unity playmode coverage for the active loop

- `unity-client/Assets/Tests/PlayMode/WaveSnapshotRuntimeSpawnerRuntimeTests.cs`
- `unity-client/Assets/Tests/PlayMode/BarracksAutoSpawnerRuntimeTests.cs`
- `unity-client/Assets/Tests/PlayMode/LanePathMarkersRuntimeTests.cs`
- `unity-client/Assets/Tests/PlayMode/LaneSnapshotCombatantRuntimeTests.cs`
- `unity-client/Assets/Tests/PlayMode/BlacksmithUpgradeMaterialRuntimeTests.cs`

## Archive Candidates

These are the highest-confidence legacy paths that should move out of the main runtime path first.

### Classic tower / wall / maze AI

- `server/ai.js`
- `server/ai/actions.js`
- `server/ai/bot.js`
- `server/ai/observe.js`

Why: these files still refer to walls, towers, maze plans, tower targeting, and classic tile-grid defense behavior. That does not match the current fortress-survival design.

### Classic placement and room APIs still exposed in Unity

- `unity-client/Assets/Scripts/Game/ActionSender.cs`
- `unity-client/Assets/Scripts/UI/TileMenuUI.cs`

Why: these still expose classic room creation, classic tower actions, and placement semantics that can keep old gameplay paths alive by accident.

### Classic or duplicate content roots

- `server/client_backup_*`
- `admin-client/` if `server/admin-client/` is the real live copy
- `archive_pending_deletion/`

Why: these create ambiguity about what code or assets are authoritative.

## Generated Noise To Remove Or Ignore

These should not stay mixed into gameplay cleanup work.

- `.appdata/`
- `.dotnet/`
- `.dotnet-cli/`
- `.dotnet-cli-home/`
- `builds/`
- `unity-client/.utmp/`
- `unity-client/Assets/InitTestScene*.unity`
- `unity-client/Assets/_Recovery/`
- large generated `unity-client/ServerData/*` outputs that can be rebuilt

## Important Caution

Not everything with old words in it is actually legacy.

- `loadout` is still present in the current game as match setup / progression language.
- `tower_progression_config.test.js` still validates live fortress pad and turret hardpoint data, so it is not an automatic archive target.
- `BarracksAutoSpawner.cs` contains legacy prototype migration fields, but the file itself participates in the active barracks runtime and should stay.

Cleanup should follow behavior, not filenames.

## Current Validation Snapshot

Server survival-path tests were run on 2026-03-26 for the current design.

- 36 tests passed
- 2 tests failed

Current failures:

- `server/tests/spawn_resolution.test.js`

Observed mismatch:

- The runtime now labels scheduled wave spawns as `dungeon_wave`
- The test still expects `scheduled_wave`

This looks like an active-path naming mismatch, not proof that the fortress-survival loop is fundamentally broken.

## Recommended Cleanup Order

1. Protect the active fortress-survival runtime and tests listed above.
2. Quarantine the classic tower / maze AI files so they cannot affect live matchmaking logic.
3. Remove or gate classic client entry points in `ActionSender.cs` and `TileMenuUI.cs`.
4. Clear generated noise and backup directories out of the working tree.
5. Re-run the active survival regression suite after each cleanup pass.
