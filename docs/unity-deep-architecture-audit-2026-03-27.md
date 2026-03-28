# Unity Deep Architecture Audit and Safe Migration Plan

Date: 2026-03-27

Scope reviewed:
- `unity-client/Assets/Scripts/{Game,Net,UI,FX,Audio,Utils}`
- Build scene wiring from `unity-client/ProjectSettings/EditorBuildSettings.asset`
- Live scene/prefab references in `unity-client/Assets/Scenes` and key prefabs/assets
- Server authority contract in `server/sim-multilane.js`
- Prior battlefield note: `BATTLEFIELD_REMODEL_HANDOFF.md`

Out of scope:
- `unity-client/Assets/Scripts/Editor/*`
- Pure art content without runtime script behavior

## Executive Verdict

The live game direction is already clear on the server:
- server-authoritative snapshots
- lane-driven movement and combat
- `ATTACK`, `DEFEND`, `RETREAT` lane command states
- Town Core / fortress routing and reinforcement joins
- authored battlefield topology and path contracts

The Unity client is only partially aligned.

The current Unity runtime is split into three islands:

1. A correct snapshot-driven island:
   `NetworkManager -> SnapshotApplier -> WaveSnapshotRuntimeSpawner / FortressPad* / Barracks* / current HUD`

2. A transitional compatibility island that still keeps wrong assumptions alive:
   `TileGrid`, `LaneRenderer`, `FortressPadRuntimeBinder`, name/color inference, legacy fallback visuals

3. A dead or mostly dead legacy island:
   classic mode, old tile menu flows, deprecated loadout persistence, authored prototype path helpers

The biggest architectural problem is not "old scripts exist." The problem is that some live systems still route core gameplay behavior through compatibility hosts built for the wrong model:
- `TileGrid` still acts like a lane-space oracle, tile interaction layer, and old build surface
- `LaneRenderer` still exists as a runtime dependency anchor even though live unit spawning moved elsewhere
- `FortressPadRuntimeBinder` still reconstructs authored fortress identity from scene object names instead of explicit authored data
- `ActionSender`, `NetworkManager`, and parts of `GameState` still expose classic and tile-era surfaces beside the live ML model

The safe migration target is:
- make Unity presentation consume the server route graph and authored anchor data directly
- isolate and replace transitional geometry/identity adapters
- then archive the invalid architecture instead of continuing to patch around it

## Evidence Summary

Authoritative runtime contract already exists on the server:
- `server/sim-multilane.js` defines `LANE_COMMAND_STATES`
- it also defines `PATH_CONTRACT_TYPES` for wave, barracks, guard, intercept, and retreat routing
- it models fixed battlefield topology instead of a straight-lane prototype

Live scene evidence:
- `Bootstrap` is enabled in build settings
- `Game_ML` is scene-wired and contains the live gameplay stack
- `Game_Classic` is disabled in build settings

Key active scene wiring:
- `Game_ML` directly references `GameManager`, `LaneRenderer`, `TileGrid`, `SnapshotApplier`, `ProjectileSystem`, `FortressSelectionController`, `BarracksPanel`, `CmdBar`, `InfoBar`, `MobileMatchHud`, `MyStatsHudWidget`, `TileMenuUI`
- `Bootstrap` directly references `AuthManager`, `BootstrapManager`, `CatalogLoader`, `LoadoutManager`, `NetworkManager`, `AudioManager`
- `GameEnvironment.prefab` contains `BarracksAutoSpawner`, `BarracksLanePath`, `BarracksSiteView`, `FortressPadAnchor`, `PathMarkerVisual`, `SnapshotBuildingVisualBridge`, `WaveSpawnAnchor`

Key architectural findings:
- `WaveSnapshotRuntimeSpawner` is the real live unit presentation path
- `LaneRenderer` is now mostly a compatibility host, not a real unit simulation owner
- `TileGrid` still owns too much wrong architecture and should not be patched forward
- `FortressPadRuntimeBinder` is a runtime name-matching shim and should be replaced by explicit authored anchor data
- `LoadoutManager` is explicitly marked deprecated and only retained as a bootstrap compatibility shim
- `PostWinFlowUI` runtime-bootstraps itself but no current caller invokes `ShowWinnerPrompt` or `ShowLoserPrompt`

## Classification Legend

- `Keep`: belongs in the architecture and is safe to keep
- `Keep but Refactor`: conceptually correct, but structure still carries transitional or legacy assumptions
- `Replace then Archive`: wrong architecture, but still depended on; replacement must land first
- `Archive`: does not belong and appears safe to retire from active use
- `Investigate Further`: evidence is incomplete enough that removal should wait for one more targeted trace

## Priority Migration Risks

### 1. Tile-grid island

Highest-risk files:
- `TileGrid`
- `TileMenuUI`
- `ITileMenu`
- `LaneRenderer`
- `ProjectileSystem`
- `CameraController`
- `LaneViewBar`
- `RuntimePortraitStudio`

Why this is the wrong architecture:
- these systems still encode battlefield space through tile-era helpers
- live ML fortress flow already blocks the legacy tile menu at runtime
- the correct source of truth is server route metadata plus authored anchors, not inferred tile lanes

### 2. Runtime identity shims

Highest-risk files:
- `FortressPadRuntimeBinder`
- `FortressLaneResolver`
- parts of `BarracksSiteView`
- parts of `WaveSnapshotRuntimeSpawner`

Why this is the wrong architecture:
- fortress and barracks identity should be explicit authoring, not reconstructed from scene names and slot colors
- scene naming should not be a gameplay dependency

### 3. Classic and deprecated compatibility surfaces

Highest-risk files:
- `ClassicGameManager`
- `LoadoutManager`
- classic branches inside `ActionSender`
- classic branches inside `NetworkManager`
- classic types inside `GameState`
- `GameOverUI`

Why this is the wrong architecture:
- classic mode is disabled in build settings
- these surfaces keep wrong assumptions alive in core orchestration

## Safe Migration Order

1. Freeze the architectural contract.
   Unity gameplay presentation should consume only snapshot state, route/path contract metadata, authored anchors, and the current tech tree.

2. Remove dead code that has no valid future.
   Start with `ClassicGameManager`, `LoadoutManager`, `BarracksAutoSpawner`, `BarracksLanePath`, `PathMarkerVisual`, `BuildingSpaceResolver`, `LaneViewBar` after final scene/prefab detachment.

3. Replace runtime identity shims.
   Author explicit fortress pad, barracks site, wave spawn, and Town Core anchors in environment content, then retire `FortressPadRuntimeBinder` and reduce `FortressLaneResolver` to explicit ID lookup only.

4. Replace the tile-grid island.
   Introduce a dedicated battlefield-space mapper and build-surface presenter, migrate `ProjectileSystem`, camera framing, unit presentation helpers, and portrait registry resolution off `TileGrid` and `LaneRenderer`, then archive `TileGrid`, `TileMenuUI`, `ITileMenu`, and `LaneRenderer`.

5. Split orchestration and command surfaces.
   Separate live ML networking and commands from classic/deprecated APIs in `ActionSender`, `NetworkManager`, `GameState`, `SnapshotApplier`, and `GameManager`.

6. Move tech tree authority to the active source of truth.
   Reduce hardcoded tech-tree duplication across `RaceProgressionCatalog`, `FortUnitIdentityCatalog`, `BarracksPanel`, and `LoadoutPhaseManager`.

7. Replace stale postgame branches.
   Consolidate postgame and continuation flow into the active ML postgame stack, then retire `GameOverUI` and `PostWinFlowUI`.

## Per-Script Audit

### Audio

| Script/System | Current purpose | Actively used | Fits current architecture | Dependency risk | Classification | Why | What breaks if removed | Correct replacement / migration order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AudioManager` | Global SFX and ambient manager | Yes, scene-wired in `Bootstrap` and called across gameplay/UI | Yes | Medium | Keep | Support layer only; does not own gameplay rules | UI feedback, combat SFX, ambient loop | Keep as shared presentation service; no migration needed beyond cleanup of dead SFX enums later |

### FX

| Script/System | Current purpose | Actively used | Fits current architecture | Dependency risk | Classification | Why | What breaks if removed | Correct replacement / migration order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `BillboardY` | Faces world-space visuals to the camera | Likely yes via prefab usage | Yes | Low | Keep | Pure view helper; no gameplay logic | World-space markers and labels may face incorrectly | Keep |
| `CannonSplash` | Pooled splash impact effect | Yes via `ProjectileSystem` and `GameManager` pool warmup | Yes | Low | Keep | Presentation-only and snapshot-triggered | Cannon impact visuals/audio timing feel worse | Keep |
| `FloatingText` | Floating world-space text feedback | Yes via `InfoBar` and `GameManager` prefab registration | Yes | Low | Keep | Presentation-only and decoupled from authority | Core HP loss popups disappear | Keep |
| `GoldPop` | UI gold payout popup | Yes via `InfoBar` and `GameManager` | Yes | Low | Keep | Presentation-only and independent of gameplay rules | Gold feedback disappears | Keep |
| `HitEffect` | Pooled non-splash hit FX | Yes via `ProjectileSystem` and `GameManager` | Yes | Low | Keep | Presentation-only, server does not depend on it | Hit impacts disappear | Keep |
| `PostProcessController` | Runtime quality and impact-flash controller | Yes in `Lobby` and `Game_ML` scene flow | Yes | Low | Keep | Pure rendering support | Quality settings and impact flash break | Keep |
| `ToonShaderBridge` | Runtime material property block bridge for toon shader | Unclear | Partial | Low | Investigate Further | Concept is valid, but live usage is not proven from scene/runtime evidence gathered here | Only visual tint/debuff presentation if still prefab-wired | Confirm prefab GUID usage; keep if live, archive if truly unused |

### Game

| Script/System | Current purpose | Actively used | Fits current architecture | Dependency risk | Classification | Why | What breaks if removed | Correct replacement / migration order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `ActionSender` | Central client command emitter | Yes | Partial | High | Keep but Refactor | Live ML actions belong; classic and tile-era actions do not | Build, upgrade, barracks buy, lane-state commands, rematch, loadout flow | Split into ML gameplay commands vs scene/meta commands; remove classic and tile-era sends after consumers migrate |
| `BarracksAutoSpawner` | Legacy barracks prototype data holder | No meaningful live behavior; prefab residue only | No | Low | Archive | Explicitly marked legacy prototype compatibility | Only old serialized prefab fields | Remove after prefab cleanup |
| `BarracksLanePath` | Legacy waypoint path authoring helper | No live integration found | No | Low | Archive | Prototype path authoring conflicts with route-graph authority | Only old prefab gizmo data | Remove with prefab cleanup |
| `BarracksSiteView` | Live authored barracks site presenter and selector | Yes | Partial | Medium | Keep but Refactor | Core concept belongs, but lane identity still leans on inference and compatibility hosts | Barracks selection, site visuals, health bars | Keep the concept; migrate to explicit authored branch/site IDs and direct anchor services |
| `BarracksSpawnCombatProfile` | Maps server/catalog stats to client combat presentation values | Yes | Partial | Medium | Keep but Refactor | Valid adapter, but still heuristic-heavy | Unit card and presentation stats may become inconsistent | Move to cleaner snapshot/catalog-driven profile mapping after route/build refactor |
| `BattleTeam` | Team/lane color identity helper | Yes | Partial | Low | Keep but Refactor | Used for color identity, but `AreFriendly(a,b)` is too naive for multi-lane alliance semantics | Color lookup and some friendly/enemy presentation logic | Replace with side/team-key aware identity helper sourced from snapshot lane assignment data |
| `BuildingSpaceResolver` | Collider-based building spacing helper | Appears unused | No | Low | Archive | No active callers found and wrong place for authoritative build-space logic | Likely nothing in live runtime | Archive after final prefab check |
| `BuildingVisualCatalog` | Loads generated building visual mappings | Yes | Yes | Medium | Keep | Needed by snapshot-driven building visuals | Building tiers/variants stop resolving | Keep |
| `CameraController` | Live gameplay camera controller | Yes | Partial | High | Keep but Refactor | Camera is needed, but it still has `TileGrid`-based fallback framing | Camera pan, framing, zoom control | Move camera framing to battlefield anchor and route-bounds service before removing `TileGrid` |
| `CenterBarracksOnboardingController` | Guided onboarding for center barracks flow | Yes, injected by `GameManager` | Partial | Low | Keep but Refactor | Current flow matches game direction, but it should not be hardwired through scene/runtime injection forever | New-player barracks onboarding | Keep behavior; move ownership to a dedicated onboarding layer after core migration |
| `ClassicGameManager` | Old classic 2P scene manager | No for current build | No | Low | Archive | Build-disabled classic scene; wrong game mode | Only disabled classic flow | Detach scene references, then archive immediately |
| `EnvironmentLoader` | Loads critical remote game environment | Yes | Yes | High | Keep | Essential to live environment/content boot | `Game_ML` environment fails to load | Keep |
| `FortressLaneResolver` | Resolves lane/branch/site identity from snapshot and scene hints | Yes | Partial | High | Keep but Refactor | Needed today, but too much identity is inferred from names/colors | Pad and barracks identity matching | Replace heuristics with explicit authored branch/lane/site IDs, then shrink this into a thin lookup service |
| `FortressPadAnchor` | Authored fortress pad identity and anchor data | Yes | Yes | High | Keep | Correct authored-anchor concept | Pad selection, building placement visuals, routing anchors | Keep and make it the explicit identity source |
| `FortressPadHealthView` | Snapshot-driven fortress pad HP bar/view | Yes | Yes | Medium | Keep | Correct presentation of authoritative state | Pad HP visuals disappear | Keep |
| `FortressPadRuntimeBinder` | Runtime name-matching shim that binds pad anchors/bridges | Yes, via runtime bootstrap | No | High | Replace then Archive | Wrong architecture: scene naming is being used as gameplay identity | Fortress pad components fail to bind on current map if removed now | First author explicit anchors in environment content, then remove binder bootstrap |
| `FortressSelectionController` | Live fortress/barracks selection owner | Yes | Yes | High | Keep | This is the correct selection surface for the fortress build model | Pad/site selection and barracks panel entry | Keep |
| `FortUnitIdentityCatalog` | Hardcoded unit presentation identity mapping | Yes | Partial | Medium | Keep but Refactor | Valid need, but hardcoded roster can drift from live tech tree/catalog | Unit labels, portraits, archetype identity | Move toward server/catalog-driven identity data after core space migration |
| `GameManager` | Scene init and runtime orchestration hub | Yes | Partial | High | Keep but Refactor | Belongs as bootstrap/orchestration, but owns too many unrelated responsibilities and legacy fallbacks | Pools, onboarding injection, camera setup, runtime sync helpers | Split into gameplay bootstrap, FX bootstrap, and camera/bootstrap coordinators before larger removals |
| `HpBarVisuals` | Shared HP bar UI helper | Yes | Yes | Low | Keep | Pure presentation helper | HP bars render incorrectly | Keep |
| `ITileMenu` | Interface between `TileGrid` and `TileMenuUI` | Yes only because legacy flow still exists | No | Medium | Archive | Exists solely to preserve the wrong interaction model | Old tile menu interaction path | Remove after `TileGrid` and `TileMenuUI` are replaced/archived |
| `LaneRenderer` | Legacy lane runtime host now reduced to registry/HP bar anchor | Yes, scene-wired and queried by live systems | No in current form | High | Replace then Archive | Live unit materialization already moved to `WaveSnapshotRuntimeSpawner`; remaining role is compatibility hosting | Unit registry discovery, HP bar prefab discovery, some runtime lookups | Create a dedicated `GameplayPresentationRegistry`/config host, migrate dependents, then archive |
| `LaneSnapshotCombatant` | Snapshot-owned unit presentation component | Yes | Yes | Medium | Keep | Correct server-authoritative presentation boundary | Unit visuals lose snapshot sync behaviors | Keep |
| `MLUnitPresentationIdentityResolver` | Resolves presentation identity from unit payload/catalog | Yes | Yes | Medium | Keep | Correct adapter between snapshot IDs and visuals | Unit prefab/portrait/name resolution breaks | Keep |
| `OptionalEnvironmentLoader` | Loads non-critical dressing environment | Yes | Yes | Medium | Keep | Support-only loader, does not distort architecture | Optional set dressing disappears | Keep |
| `PathMarkerVisual` | Legacy authored route helper arrow | No meaningful live use | No | Low | Archive | File explicitly says retired helper arrows | Only hidden legacy arrows | Archive with prefab cleanup |
| `ProjectileSystem` | Snapshot projectile visualizer | Yes | Partial | High | Keep but Refactor | Correct idea, but world positions still come from `TileGrid.TileToWorld` | Projectile arcs, hit FX, splash sync | Migrate to route-node / anchor-based world mapping before `TileGrid` removal |
| `SnapshotBuildingVisualBridge` | Applies snapshot building state to authored/generated visuals | Yes | Partial | Medium | Keep but Refactor | Correct concept, but still carries legacy renderer fallback behavior | Fortress/barracks visuals stop updating | Keep bridge concept; remove legacy renderer fallback after authored anchors are explicit |
| `TeamColorMaterialProfile` | Material override profile for team/lane colors | Yes | Yes | Low | Keep | Pure presentation data | Team tinting breaks | Keep |
| `TieredBuildingVisual` | Swaps building tier visual layers | Yes | Yes | Low | Keep | Correct local presentation primitive | Building tier visuals break | Keep |
| `TileGrid` | Transitional battlefield grid, lane-space mapper, legacy build UI owner | Yes | No in current form | High | Replace then Archive | Biggest wrong-architecture file; still mixes build cells, tile selection, branch mapping, and legacy visuals | Camera bounds, projectile positions, old tile menu path, some world-space helpers, portrait registry fallback | Replace with `BattlefieldSpaceMapper`, explicit build-surface presenter, and anchor-driven camera/projectile helpers; then archive |
| `UnitPrefabRegistry` | Core unit prefab and skin registry | Yes | Yes | High | Keep | Required for unit presentation | Units and portraits stop resolving | Keep |
| `WaveSnapshotRuntimeSpawner` | Live unit presentation spawner for ML snapshots | Yes | Partial | High | Keep but Refactor | This is the right center of gravity, but it still depends on `LaneRenderer`, `TileGrid`, and some inferred anchors | Live units stop appearing or animating correctly | Preserve as the main unit presenter; migrate all geometry/anchor dependencies away from compatibility hosts |
| `WaveSpawnAnchor` | Authored wave spawn and anchor marker | Yes | Yes | Medium | Keep | Correct authored world anchor | Spawn anchors and lane visual roots break | Keep |

### Networking and Data

| Script/System | Current purpose | Actively used | Fits current architecture | Dependency risk | Classification | Why | What breaks if removed | Correct replacement / migration order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AuthManager` | Authentication/session manager | Yes, bootstrap scene | Yes | Medium | Keep | Support-only and needed for login/bootstrap | Auth/session flow breaks | Keep |
| `BootstrapManager` | Startup scene flow coordinator | Yes | Yes | High | Keep | Needed to get into live scenes safely | Scene boot flow breaks | Keep |
| `CatalogLoader` | Loads authoritative unit/barracks catalogs | Yes | Yes | High | Keep | Correct source for runtime content data | Loadout, portraits, unit metadata break | Keep |
| `GameState` | Payload model layer for network state | Yes | Partial | High | Keep but Refactor | Core ML payloads belong, but file still carries classic and legacy compatibility fields | Network deserialization breaks | Split ML data models from classic/deprecated payloads once live consumers are isolated |
| `LoadingScreen` | Central scene transition/loading gate | Yes | Yes | Medium | Keep | Infrastructure only | Scene transitions and loading UX break | Keep |
| `LoadoutManager` | Deprecated saved-loadout singleton shim | Yes only as compatibility scene reference | No | Medium | Archive | Explicitly marked deprecated and obsolete | Only old bootstrap/scene references and lingering callers | Remove bootstrap/loadout scene references first, then archive |
| `NetworkManager` | Central socket layer and event hub | Yes | Partial | High | Keep but Refactor | Live ML flow belongs, but classic, rematch, lobby, chat, and scene concerns are over-concentrated | All networked gameplay and meta flow | Split ML gameplay channel from lobby/postgame/debug/classic surfaces in stages |
| `RemoteAddressablesRuntimePath` | Resolves remote content base path | Yes | Yes | Low | Keep | Infrastructure only | Remote content path resolution breaks | Keep |
| `RemoteContentManager` | Remote content loading and validation manager | Yes | Partial | High | Keep but Refactor | Needed, but still contains legacy fallback logic and broad responsibility | Remote environments, skins, registry-backed content fail | Keep core manager; reduce legacy fallback branches after registry and anchor migration |
| `RemoteContentVerification` | Runtime remote-content diagnostics and verification | Yes | Yes | Low | Keep | Safe support tooling | Verification/debugging weakens | Keep |
| `SnapshotApplier` | Stores latest authoritative snapshot and exposes runtime getters/events | Yes | Partial | High | Keep but Refactor | This is the correct data spine, but it still carries classic snapshot support and too many compatibility getters | Almost every live gameplay view breaks | Keep as authoritative client snapshot cache; split classic and compatibility APIs out after dependents migrate |

### UI

| Script/System | Current purpose | Actively used | Fits current architecture | Dependency risk | Classification | Why | What breaks if removed | Correct replacement / migration order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `BarracksPanel` | Live fortress/barracks build and progression panel | Yes | Partial | High | Keep but Refactor | Core UI belongs, but it still carries legacy naming and too much local tech-tree knowledge | Building, barracks buy, upgrade flow break | Keep; move data authority toward snapshot/catalog and explicit pad/site IDs |
| `CmdBar` | Lane-state command and barracks activity UI | Yes | Partial | Medium | Keep but Refactor | Current lane-state controls are correct, but file still hides legacy widgets and compatibility structure | Attack/Defend/Retreat controls and some quick actions break | Keep behavior, simplify around lane-command architecture only |
| `CollapsibleHudCard` | Expand/collapse HUD card helper | Yes via runtime-built HUD | Yes | Low | Keep | Pure UI utility | HUD card collapse behavior breaks | Keep |
| `DraggableHudPanel` | Drag/collapse helper for HUD panels | Yes | Yes | Low | Keep | Pure UI utility | Runtime HUD panels lose drag/collapse behavior | Keep |
| `DraggablePanel` | Drag helper for generic panels | Yes, runtime debug panel uses it | Yes | Low | Keep | Pure UI utility | Runtime debug/settings panels lose dragging | Keep |
| `FloatingSettingsPanel` | Animated settings flyout panel | Yes | Yes | Low | Keep | Pure UI behavior helper | Settings panel UX breaks | Keep |
| `GameOverUI` | Legacy game-over/rematch panel for ML and classic | No active scene wiring found; still subscribes to both ML and classic events | No in current form | Medium | Replace then Archive | Superseded by dedicated postgame flow, still entangled with classic events | If any hidden scene still uses it, rematch/lobby buttons would disappear | Consolidate ML-only postgame into `PostGameSceneController`/active HUD, then archive |
| `InfoBar` | Top HUD bar for resources/core HP and match info | Yes | Partial | Medium | Keep but Refactor | Still valuable, but it still reflects older barracks/tower-era assumptions in places | Gold, income, core HP, event feedback break | Keep; trim tower-era leftovers while retaining authoritative stat display |
| `LaneTabs` | Active lane-view switching tabs | Yes in runtime flow | Yes | Medium | Keep | Matches multi-lane architecture and uses snapshot lane alliances | Lane viewing control breaks | Keep |
| `LaneViewBar` | Old lane pan button strip using `TileGrid.TileToWorld` | No active scene wiring found | No | Low | Archive | Wrong camera-space model and not scene-wired | Likely nothing in live runtime | Archive after final prefab/scene confirmation |
| `LineGraphUI` | Postgame graph renderer | Yes via `PostGameStatsPanel` | Yes | Low | Keep | Pure postgame presentation helper | Postgame graphs break | Keep |
| `LoadoutPhaseManager` | Live pre-match loadout/progression phase UI | Yes | Partial | High | Keep but Refactor | Correct live phase, but very large and still compensates for bootstrap/event-system issues | Pre-match loadout and readiness flow break | Keep; split into smaller controllers after network/data cleanup |
| `LobbyUI` | Lobby scene controller | Yes | Yes | Medium | Keep | Current flow support | Lobby flow breaks | Keep |
| `LoginUI` | Login scene controller | Yes | Yes | Medium | Keep | Current flow support | Login flow breaks | Keep |
| `MobileMatchHud` | Main runtime match HUD and widget builder | Yes | Partial | High | Keep but Refactor | Live and important, but still carries legacy panel/button compatibility and too much construction logic | Match HUD, settings, wave widgets, barracks access break | Keep; split into focused HUD modules after tile-grid island is replaced |
| `MyStatsHudWidget` | Draggable personal stats widget | Yes | Yes | Low | Keep | Pure UI widget | Personal stats display breaks | Keep |
| `PostGameSceneController` | Active postgame scene owner | Yes | Yes | High | Keep | This is the right postgame home | Postgame scene flow breaks | Keep |
| `PostGameStatsPanel` | Detailed postgame stats tabs | Yes | Yes | Medium | Keep | Fits current game and consumes authoritative postgame payloads | Postgame stats detail breaks | Keep |
| `PostWinFlowUI` | Runtime-bootstrapped continue/spectate prompt | Partially; bootstrapped but prompt methods appear uncalled | No in current form | Medium | Replace then Archive | Live object may exist, but current behavior is stale and disconnected from active flow | If intended continuation UX still matters, winners/losers lose that prompt | Rebuild continuation inside active ML postgame flow, then archive this bootstrap UI |
| `ProgressionViewerLaunchContext` | Static launch context for progression viewer | Yes | Yes | Low | Keep | Small support context holder | Viewer launch context breaks | Keep |
| `RaceProgressionCatalog` | Hardcoded current race tech tree/progression data | Yes | Partial | High | Keep but Refactor | Reflects the current game, but hardcoded client authority will drift from the real tech tree over time | Loadout/progression UI breaks | Move toward server/catalog-authored tech tree; keep only view-friendly projection client-side |
| `RuntimeChatDebugPanelController` | Runtime chat/system debug overlay | Yes via runtime bootstrap | Yes for support, not core gameplay | Low | Keep | Good support/debug surface and isolated from gameplay rules | Runtime diagnostics/chat overlay disappears | Keep, optionally move behind debug gating later |
| `RuntimeDiagnosticsService` | Captures runtime logs and chat for diagnostics | Yes via runtime bootstrap | Yes for support | Low | Keep | Support-only and useful during migration | Runtime diagnostics history disappears | Keep |
| `RuntimeFailureMarker` | World-space "broken" marker for failed bindings/content | Likely yes as fallback/debug support | Yes for support | Low | Keep | Useful during migration and does not distort authority | Visual failure markers disappear | Keep until migration is complete |
| `RuntimePortraitStudio` | Runtime portrait render stage creation | Yes via portrait systems | Partial | Medium | Keep but Refactor | Needed, but registry resolution still falls back through `LaneRenderer` and `TileGrid` | Portrait rendering may fail | Repoint registry/anchor discovery to explicit services before archiving compatibility hosts |
| `TileMenuUI` | Legacy world-space tile placement/upgrade/sell menu | Yes, scene-wired, but fortress flow actively blocks it | No | High | Replace then Archive | Sends wrong actions and keeps the old build model alive | Old tile build popup and tower actions disappear; any lingering tile-only build flow breaks | Replace with fortress-pad/build-surface UI only, then archive |
| `UnitPortraitCamera` | Camera/controller for portrait render studio | Yes | Yes | Low | Keep | Pure presentation support | Portrait rendering breaks | Keep |
| `WaveStatusHudWidget` | Draggable wave-status widget | Yes | Yes | Low | Keep | Pure UI widget | Wave summary widget breaks | Keep |

### Utilities

| Script/System | Current purpose | Actively used | Fits current architecture | Dependency risk | Classification | Why | What breaks if removed | Correct replacement / migration order |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `SceneEventSystemUtility` | Finds a valid scene event system | Yes | Yes | Low | Keep | Safe scene/UI helper | UI bootstrap code becomes more brittle | Keep |
| `SingleEventSystem` | Prevents duplicate event systems | Yes | Yes | Low | Keep | Needed because several runtime-built UI surfaces self-heal event systems | UI focus/input bugs increase | Keep until UI bootstraps are simplified |

## Immediate Archive Candidates

These can move first once final scene/prefab detachment is confirmed:
- `ClassicGameManager`
- `LoadoutManager`
- `BarracksAutoSpawner`
- `BarracksLanePath`
- `PathMarkerVisual`
- `BuildingSpaceResolver`
- `LaneViewBar`

Rationale:
- they either serve disabled game modes, explicit deprecated shims, or retired prototype visuals
- none should be patched forward

## Replace-Then-Archive Queue

These are the systems to actively migrate away from, not fix in place:

### A. Build-space and lane-space compatibility

Replace then archive:
- `TileGrid`
- `TileMenuUI`
- `ITileMenu`
- `LaneRenderer`

Replacement architecture:
- `BattlefieldSpaceMapper`
  - authoritative client-space adapter from snapshot branch/path data to world-space points
  - no tile interaction ownership
- `FortressBuildSurfacePresenter`
  - authored buildable pad/site visuals only
  - no tile-era upgrade/sell menu
- `GameplayPresentationRegistry`
  - holds prefab registries, HP bar prefabs, and shared presentation references currently hanging off `LaneRenderer`

Migration order:
1. Introduce `BattlefieldSpaceMapper`
2. Migrate `ProjectileSystem`, `CameraController`, `WaveSnapshotRuntimeSpawner`, `RuntimePortraitStudio`
3. Replace fortress interaction completely with pad/site UI
4. Remove `TileMenuUI` and `ITileMenu`
5. Remove `TileGrid`
6. Remove `LaneRenderer`

### B. Runtime name-based identity binding

Replace then archive:
- `FortressPadRuntimeBinder`

Replacement architecture:
- explicit authored `FortressPadAnchor`, `BarracksSiteView`, `WaveSpawnAnchor`, Town Core anchors in the environment prefab or scene
- direct branch/lane/site IDs serialized into components

Migration order:
1. Update environment content to carry explicit IDs
2. Update `FortressLaneResolver` and consumers to prefer explicit IDs only
3. Run validation for missing/mismatched anchors
4. Remove binder bootstrap

### C. Stale postgame/legacy overlay flow

Replace then archive:
- `GameOverUI`
- `PostWinFlowUI`

Replacement architecture:
- a single ML-only continuation/postgame flow owned by the active postgame scene/HUD stack

Migration order:
1. Decide whether continuation prompt still belongs in the live product
2. Implement it inside the active ML postgame flow if yes
3. Remove `PostWinFlowUI`
4. Remove `GameOverUI`

## Recommended Refactors That Belong in the Architecture

These systems should survive, but in a cleaner form:

### Data and authority spine

- `NetworkManager`
- `SnapshotApplier`
- `GameState`
- `CatalogLoader`

Refactor goal:
- ML gameplay events and data in one surface
- lobby/postgame/debug support separated
- classic payloads and compatibility branches isolated for deletion

### Unit and building presentation

- `WaveSnapshotRuntimeSpawner`
- `LaneSnapshotCombatant`
- `BarracksSiteView`
- `SnapshotBuildingVisualBridge`
- `ProjectileSystem`
- `CameraController`

Refactor goal:
- consume route/path and anchor data directly
- stop depending on `TileGrid` and `LaneRenderer`
- stop inferring world identity from names/colors

### Tech tree and player-facing progression

- `RaceProgressionCatalog`
- `FortUnitIdentityCatalog`
- `BarracksPanel`
- `LoadoutPhaseManager`

Refactor goal:
- the active tech tree should exist in one authoritative place
- Unity should project and present that tree, not redefine it in multiple files

## Scene and Prefab Wiring Actions

### Scenes

`Bootstrap`
- remove `LoadoutManager` after callers are cleaned up

`Game_ML`
- stop scene-wiring `TileMenuUI` as an active gameplay dependency
- stop keeping `LaneRenderer` just to host registries and HP bar prefabs
- keep `FortressSelectionController`, `SnapshotApplier`, `ProjectileSystem`, `GameManager`, `MobileMatchHud`, `BarracksPanel`

`Game_Classic`
- keep disabled during migration
- remove entirely after archive pass if no longer needed for reference

### Prefabs and authored content

`GameEnvironment.prefab`
- remove `BarracksAutoSpawner`
- remove `BarracksLanePath`
- remove `PathMarkerVisual`
- keep and expand explicit authored anchors instead

## Validation Gates Before Removal

Before archiving any runtime system:
- verify no active scene still contains the script by GUID/component reference
- verify no prefab or ScriptableObject still serializes it
- verify no runtime bootstrap recreates it implicitly
- verify no inspector field type depends on it
- verify play mode starts from `Bootstrap` and reaches `Game_ML`
- verify snapshot application, unit spawn, pad selection, barracks purchase, lane-state commands, and postgame flow still work

Minimum migration test path:
1. Boot from `Bootstrap`
2. Reach login/lobby/loadout flow
3. Enter `Game_ML`
4. Receive `MLMatchReady`
5. Apply snapshots
6. Select fortress pad and barracks site
7. Build or upgrade a fortress structure
8. Buy barracks units
9. Change lane state between `ATTACK`, `DEFEND`, `RETREAT`
10. Observe unit spawn, routing, and reinforcement behavior
11. Complete a match and verify postgame flow

## Final Decision

The correct strategy is not to keep the current Unity client alive by patching transitional systems forever.

The safe strategy is:
- keep the snapshot-driven ML spine
- replace the tile-grid and runtime-binding compatibility islands
- archive dead classic/prototype systems
- move tech-tree and presentation identity toward explicit, authoritative data

That path preserves game stability while actually converging the Unity client on the current architecture, instead of teaching new code to depend on the wrong one.
