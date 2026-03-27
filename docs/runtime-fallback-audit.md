# Runtime Fallback Audit

Date: 2026-03-24

This audit focuses on runtime fallback behavior that could hide broken game state by silently substituting data, visuals, routes, bindings, or UI state. The preferred direction is explicit failure with strong console context plus in-game surfacing where practical.

## Replaced In This Pass

| Category | File + Method | Previous fallback behavior | Why it hid bugs | Loud behavior now | Status |
| --- | --- | --- | --- | --- | --- |
| Catalog data | `unity-client/Assets/Scripts/Net/CatalogLoader.cs` `FetchAll` | Seeded local fallback unit and barracks data when remote catalog load failed. | Made broken catalog/addressable state look valid. | Logs a critical error, records failure state, aborts readiness, and does not inject fallback rows. | Replaced |
| Scene transition | `unity-client/Assets/Scripts/Net/LoadingScreen.cs` lobby-entry failure path | Continued into `Game_ML` with local content when remote preload failed. | Masked remote content and preload regressions. | Fails the transition loudly instead of continuing with guessed content. | Replaced |
| Unit prefab lookup | `unity-client/Assets/Scripts/Game/UnitPrefabRegistry.cs` `GetPrefab`, `GetPrefabForSkin` | Fell back from missing/broken remote skin or unit prefabs to local/base prefabs or a runtime placeholder. | Spawned the wrong unit while appearing to work. | Emits hard errors with unit/skin context and returns `null` so the caller can surface a broken path. | Replaced |
| Command bar loadout | `unity-client/Assets/Scripts/UI/CmdBar.cs` `Start`, `RequireAuthoritativeLoadout` | Derived send buttons from cached/global catalog data when per-match loadout was missing. | Let gameplay continue with the wrong roster. | Shows `WAITING LOADOUT` / `LOADOUT MISSING`, logs an error, and disables the send path until `ml_match_config` arrives. | Replaced |
| Tile placement loadout | `unity-client/Assets/Scripts/UI/TileMenuUI.cs` `EnsureInitialized`, `HandleMatchConfig`, `RequireAuthoritativeLoadout` | Derived placement buttons and costs from global catalog data. | Hid bad match config and could use wrong costs. | Shows explicit loadout error labels, logs invalid costs, and disables placement until authoritative loadout data exists. | Replaced |
| Tile menu binding | `unity-client/Assets/Scripts/Game/TileGrid.cs` `TryResolveTileMenu` | Bound the grid to `tileMenus[0]` when lane mapping failed. | Cross-wired lane input to the wrong UI. | Logs an error with lane/scene context, drops a failure marker, and leaves the menu unresolved. | Replaced |
| Tower prefab spawn | `unity-client/Assets/Scripts/Game/TileGrid.cs` `GetTowerPrefab` | Returned `WallPrefab` when tower lookup failed. | Rendered the wrong tower while hiding missing registry or key bugs. | Logs an error and returns `null`; tile update now drops an in-world failure marker for the broken tile. | Replaced |
| Branch remap | `unity-client/Assets/Scripts/Game/TileGrid.cs` `OnSnapshot` | Previously allowed bad branch identity to continue into lane rebuild logic. | A bad `branchId` could remap or misplace the board. | Logs an error with lane and branch id, marks the grid in-world, and aborts the rebuild. | Replaced |
| Wave route sampling | `unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs` `TryResolveWaveWorldPosition` | Fell back from `LanePathMarkers` to `TileGrid.NormProgressToWorld` and spawn-anchor guesses. | Spawned units onto a guessed route instead of exposing broken path markers. | Logs strong lane/unit errors, marks the lane in-world, and aborts the spawn/update path. | Replaced |
| Wave prefab spawn | `unity-client/Assets/Scripts/Game/WaveSnapshotRuntimeSpawner.cs` `CreateWaveView` | Warning-only skip when registry or prefab was missing. | Failed quietly and made it hard to see which unit/lane was broken. | Emits errors with lane, unit id, type, skin, and barracks source, plus in-world markers. | Replaced |
| Building visual catalog | `unity-client/Assets/Scripts/Game/SnapshotBuildingVisualBridge.cs` `EnsureVisualInstance` | Quietly did nothing when catalog, entry, prefab, or `TieredBuildingVisual` was missing. | Broken building visuals looked like normal unbuilt or empty state. | Logs contextual errors, hides the broken visual path, and adds an in-world failure marker on the building anchor. | Replaced |
| Building snapshot binding | `unity-client/Assets/Scripts/Game/SnapshotBuildingVisualBridge.cs` `RefreshFromSnapshot` | Quietly treated missing lane/site/pad snapshot bindings as unbuilt. | Misconfigured pad/site ids looked like valid unbuilt buildings. | Logs contextual errors, hides visuals, and marks the affected building anchor. | Replaced |
| Server loadout resolution | `server/game/loadoutHelpers.js` `resolveLoadout` | Fell back to the first 5 buildable units when inline selection or race-derived keys were incomplete. | Server would start a match with a plausible but wrong roster. | Throws a structured loadout-resolution error after logging the broken ids/keys. | Replaced |
| Match start error handling | `server/game/multilaneRuntime.js` AI and player loadout resolution blocks | Caught loadout resolution failures and continued starting the match. | Broken loadout config did not stop the match. | Logs failure details and cancels the match instead of proceeding with partial state. | Replaced |
| Prep-state UI | `unity-client/Assets/Scripts/UI/LoadoutPhaseManager.cs` `BuildPlayerPanel`, `RefreshFallbackPlayerPanel` | Simulated preparation progress and AI readiness without authoritative server state. | A missing `ml_match_preparation_state` looked like real progress. | Logs a missing-state error and renders `NO SERVER STATE` rows with zero progress. | Replaced |
| Tech tree default unit | `unity-client/Assets/Scripts/UI/LoadoutPhaseManager.cs` `GetDefaultUnit` | Auto-selected the first unit in the first lane when nothing was marked `StartsUnlocked`. | Misconfigured progression data still looked selectable. | Logs an error and returns `null` instead of choosing the first available unit. | Replaced |
| Tech tree stats | `unity-client/Assets/Scripts/UI/LoadoutPhaseManager.cs` `BuildUnitStatsLine`, `TryGetCatalogEntry` | Rendered placeholder stats when catalog entries were missing. | Bad catalog wiring looked like incomplete but usable content. | Logs an error and shows `CATALOG ERROR` explicitly. | Replaced |

## Added Runtime Surfacing

| Feature | File | Purpose |
| --- | --- | --- |
| Central diagnostics bus | `unity-client/Assets/Scripts/UI/RuntimeDiagnosticsService.cs` | Collects system/errors and chat with ring buffers and Unity log hook integration. |
| World failure marker | `unity-client/Assets/Scripts/UI/RuntimeFailureMarker.cs` | Displays obvious in-world error labels on broken objects or lanes. |
| Chat + diagnostics panel | `unity-client/Assets/Scripts/UI/RuntimeChatDebugPanelController.cs` | Adds draggable, collapsible runtime UI with `All Chat` and `System / Errors` tabs, filtering, unread badge, and copy action. |
| Draggable UI helper | `unity-client/Assets/Scripts/UI/DraggablePanel.cs` | Makes the panel and launcher draggable and position-persistent. |
| Network chat plumbing | `unity-client/Assets/Scripts/Net/NetworkManager.cs`, `unity-client/Assets/Scripts/Net/GameState.cs`, `server/socket/registerHandlers.js` | Adds runtime all-chat transport and routes chat into the shared diagnostics shell. |

## Remaining Manual Cleanup

These are still fallback-like behaviors or placeholder paths that were identified but not fully removed in this pass.

| Category | File + Method | Current behavior | Why it still matters | Suggested loud-fail follow-up |
| --- | --- | --- | --- | --- |
| Race selection fallback | `unity-client/Assets/Scripts/UI/RaceProgressionCatalog.cs` `GetOrDefault`, `ResolveAllowedRaceId` | Defaults race resolution to `DefaultRaceId` / first valid entry in viewer-side flows. | Can hide bad race payloads or invalid viewer launch state. | Replace with strict `TryResolve...` APIs and block the viewer with explicit config errors when race ids are invalid. |
| Building art substitution | `unity-client/Assets/Scripts/UI/LoadoutPhaseManager.cs` `GetBuildingIcon`, icon fallback text helpers | Still allows non-authoritative art fallback patterns in the progression UI. | Missing building art can still read as placeholder content rather than a broken asset pipeline. | Replace generic/icon fallback with explicit `ART ERROR` states and per-card logging. |
| Rejoin race defaulting | `server/socket/registerHandlers.js` reconnect `ml_loadout_phase_start` payload | Sends `getDefaultRaceId()` when a reconnecting player has no pending race id. | Rejoin UI can hide missing persisted race selection. | Surface missing pending race explicitly and ask the client to reselect instead of defaulting. |
| Player race defaulting | `server/game/multilaneRuntime.js` player race resolution | Uses `getDefaultRaceId()` when `pendingRaceId` is absent. | Human/manual selection bugs can still resolve to Humans silently in some flows. | Require explicit race selection for manual mode, or emit a clear server-side failure instead of defaulting. |

