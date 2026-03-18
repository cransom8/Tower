# Castle Defender - WebGL Remote Content Remediation Plan v3

## 1. Objective

Reduce the login-to-lobby remote download to only the content required to safely enter the lobby screen. Nothing else.

Defer first-match-critical content to the earliest point it is actually needed - before loadout or before match start - not before lobby unless a specific, named UI or gameplay dependency requires it earlier.

Secondary objective: ensure no missing-prefab failure can ever be silent or produce a null GameObject at runtime.

## 2. Non-Negotiable Rules

### R1 - Definition of critical

An asset is critical if and only if: without it, the game fails to start, crashes, produces a missing model/pink material, or becomes unplayable before the first match ends. "Convenient to preload" is not critical. "Might be needed" is not critical.

### R2 - Bloat is a failure mode

Putting a non-critical asset into a critical group is a bug, the same as a null-ref crash. It will be caught by the acceptance matrix before merge.

### R3 - No silent nulls

A missing remote prefab must never silently return null to calling code. Every load path must have a real, tested fallback prefab or a hard failure with visible error state. Silent null = shipping broken.

### R4 - Runtime safety before optimization

Fallback safety and retry/error UX must be complete and tested before any Addressables group is split or any asset is moved. Do not regroup assets on top of an unsafe runtime.

### R5 - On-demand loading is not a loophole

Do not use on-demand loading to justify moving core gameplay units out of preload. In this phase, on-demand is only for optional skins and cosmetics. Core units must be preloaded before first match.

### R6 - Groups do not drift

Every asset added to a critical group must have a written justification stating which rule in R1 it satisfies. No justification = it does not belong in the group.

### R7 - Budget ownership is mandatory

Every deliverable must be compared against the size budgets defined in section 4. A build that ships over budget without an approved exception is a broken build.

### R8 - T1 content does not belong in the login-to-lobby preload by default

Any T1 asset that is pulled into the login-to-lobby preload must name the exact UI screen or gameplay system that requires it before the lobby can be safely entered. "It might be needed soon" is not a valid reason. No name = it does not block lobby entry.

### R9 - No broad catch-all T0/T1 downloads

Do not use broad group-level, catch-all label, or "download all dependencies" preload logic for T0/T1 unless the exact asset list behind that request is explicitly audited, frozen, and justified.

### R10 - Centralized T1 orchestration only

All T1 downloads must be initiated, tracked, deduplicated, and reported by one centralized system, such as RemoteContentManager or an equivalent replacement. Individual systems may await readiness, but they must not independently trigger first-time downloads.

### R11 - T1 download requests must be idempotent

Re-requesting an asset that is already downloaded or already being downloaded must not start a duplicate download or produce conflicting state.

## 3. Revised Workstreams

### 3A - Asset Criticality Tiers

Every asset in the project must be assigned exactly one tier before grouping work begins. No asset lives in "unclear."

Tier: T0 - Required before lobby
Definition: The lobby screen cannot be safely entered without it
When it loads: Blocking, login-to-lobby
Examples: Addressables catalog, remote settings, core lobby UI prefabs

Tier: T1 - Required before loadout or first match
Definition: Gameplay fails, crashes, or visually breaks without it - but only at or after loadout, not before lobby
When it loads: During lobby, blocks loadout or match start
Examples: currently shippable base unit prefabs that can appear in the first playable match in production, environment prefab, portrait textures

Tier: T2 - Optional, deferred
Definition: Game runs correctly without it until the moment it is explicitly requested
When it loads: On-demand at point of use
Examples: cosmetic skins, seasonal variants, future unit DLC

Tier: T3 - Deprecated or removable
Definition: Not used, not referenced, or test-only
When it loads: Remove from project
Examples: PerformanceTestRunInfo.json, unused variant bundles

T1 is not part of the login-to-lobby preload. T1 content loads during the lobby - after the player has entered - at the latest point that still allows it to be ready before loadout or match start. The lobby is the buffer window for T1 preloading, not the login screen.

### 3B - Gameplay Core Units Group

Scope: T1 only.

Contains:
- The currently shippable base unit prefabs that can appear in the first playable match in production
- Their direct mesh, material, texture, and animator controller dependencies - only what Unity's dependency resolver pulls in for those prefabs
- Nothing else

Core units are T1. They must be fully preloaded before the loadout screen opens or before match start, whichever comes first in the current flow.

They are not part of the login-to-lobby download. The login-to-lobby preload does not touch this group unless a specific named system requires a unit asset before the lobby screen can render. No such system currently exists.

This group must not become a junk drawer.
Before finalizing the group, run a dependency audit and list every asset Unity will include when building this group. If anything appears that is not a direct dependency of the production-shippable base prefabs, it does not belong here. Remove it or justify it in writing against R1.

Cosmetic skin variants are T2. They do not go here under any circumstances.

### 3C - Shared Gameplay Dependencies Group

Scope: assets that are direct dependencies of multiple T1 units and cannot be cleanly assigned to one unit's bundle.

Rules:
- Must be referenced by at least two T1 unit prefabs to qualify as "shared"
- Must be a render, mesh, animation, or physics dependency - not a cosmetic
- Must not be miscellaneous assets that are hard to classify
- Optional skins and cosmetics never qualify, regardless of how many units share them

Verification gate before merge:
- Dump the full asset list for this group
- Check its uncompressed size
- If the shared group is larger than a single unit group, investigate before proceeding

This group exists to prevent duplication, not to become a second junk drawer.

### 3D - Remote Environment Group

Scope: the Winter and Christmas Pack pure-visual assets currently embedded in Game_ML.unity.

Approach:
- Identify every pure-visual GameObject in Game_ML (MeshRenderer and no custom gameplay scripts)
- Extract to Assets/AddressableContent/Environment/GameEnvironment.prefab
- Register in new Remote Environment group using Remote.BuildPath / Remote.LoadPath
- Address: environment/game_ml
- Remove extracted GameObjects from scene and replace with EnvironmentLoader MonoBehaviour

Before extracting, manually verify these stay in the scene:
- enemy path waypoints
- tower placement zones and trigger volumes
- spawn points
- any GameObject referenced by name or tag from gameplay scripts
- any GameObject with a custom script

Timing:
- Environment is T1
- It must be present before the first match starts
- It does not need to block lobby entry unless the current UX renders the game scene before or during lobby, which it currently does not

Loading sequence:
- Environment bundle is not part of the login-to-lobby preload
- Environment download begins during the lobby window, under centralized T1 orchestration
- EnvironmentLoader may await readiness or instantiate once content is ready, but it must not own first-time download orchestration
- Environment instantiation must complete before the match start gate is released
- If the download is still in progress when the player attempts to start a match, block match start with a loading indicator
- If load fails, hard-fail with a visible error; never silently continue with missing geometry

Risks to verify:
- waypoints and spawn points must not be extracted by mistake
- lightmaps may break when moving environment objects into a runtime-instantiated prefab
- test environment lighting after instantiation; rebake or switch lighting strategy if required

### 3E - Remote Portraits Group

Portraits are T1. They are required before the earliest UI that depends on them.

If loadout happens after lobby, portraits are not part of the login-to-lobby preload and must not block lobby entry.

Requirements:
- Rebuild Addressables content
- Deploy new bundles to server
- Rebuild player
- Verify portrait bundle is absent from StreamingAssets/aa/WebGL/

Loading sequence:
- Portrait bundle download begins during the lobby window under centralized T1 orchestration
- Must complete before the loadout screen opens
- If download is still in progress when loadout is triggered, block loadout entry with a loading indicator
- LoadoutPhaseManager can continue to use async Addressables for portrait usage, but the bundle must already be available before BuildPanel() is called

### 3F - Fallback and Missing-Prefab Safety

This workstream completes before any group split.

Requirements:
- Every code path that calls Addressables.LoadAssetAsync or Addressables.InstantiateAsync for a unit or environment prefab must handle AsyncOperationStatus.Failed explicitly
- Failed load must produce one of:
  - instantiate a real fallback or placeholder prefab
  - show a visible error UI
  - abort the match with a clear error message
- Never return null silently
- UnitPrefabRegistry.GetPrefab() fallback path must be tested with the remote bundle absent; confirm it returns placeholder or hard-fails visibly, never null
- EnvironmentLoader must hard-fail visibly, not continue silently with no geometry
- fallbackPrefab or placeholder_key resolution must be real and tested, not just present in metadata

### 3G - Retry UI and Failure States

Completes before any group split.

Requirements:
- Manifest or catalog load failure: show retry prompt, do not silently hang
- Bundle download failure during preload: show retry prompt with error details
- Current blocking behavior before lobby for incomplete T0 content must be preserved
- Retry must re-attempt the specific failed operation, not restart the whole client
- RemoteContentManager must distinguish:
  - manifest failure
  - catalog failure
  - bundle failure
  - prefab load failure

### 3H - Cleanup: Deprecated and Test Assets

Remove from the project:
- Assets/Resources/PerformanceTestRunInfo.json and its .meta
- Assets/Resources/PerformanceTestRunSettings.json and its .meta

Add both paths to .gitignore if appropriate.
Verify neither path appears in the next build log's Used Assets section.

Also:
- clean stale or broken skin mappings
- audit all TT skins and decide whether each belongs in T2 deferred, or T3 remove
- remove any dead cosmetic, deprecated asset, or incorrect remote mapping from the critical path

### 3I - Config and Versioning

Do not build a more complex system than needed for this phase.

For this phase:
- Keep same-origin /addressables delivery
- Do not introduce new CDN abstraction, catalog URL routing, or environment-switching logic until grouping and preload scope are stable
- If metadata fields exist in the runtime model, each field must be documented as either:
  - active: read and acted on at runtime
  - admin-only: written by tooling, never read at runtime
- Remove admin-only fields from the runtime data model
- A runtime model carrying inert metadata is misleading and must be simplified before the next major content release

### 3J - Centralized T1 Download Orchestration

A single centralized system must own all T1 download orchestration.

Responsibilities:
- start T1 downloads immediately upon entering the lobby, or the earliest safe post-login point
- track progress centrally
- deduplicate requests
- expose readiness per subset:
  - portraits ready
  - units ready
  - environment ready
- expose overall T1 progress UI if needed
- allow dependent systems to gate on their required subset

Dependent systems:
- LoadoutPhaseManager
- EnvironmentLoader
- match-start gate
- unit render/spawn path

These systems may request or await readiness, but they must not trigger independent first-time downloads.

## 4. Anti-Bloat Controls

### 4A - Size Budgets

The implementer must propose exact numbers and get them approved before the group split lands. Budget ownership is mandatory.

Required budgets:
- Base WebGL player build compressed size ceiling
- Login-to-lobby T0 compressed remote download ceiling
- T1 compressed remote download ceiling before first match
- Maximum single remote bundle uncompressed size ceiling

Suggested starting point for discussion:
- base build <= 35 MB compressed
- login-to-lobby T0 <= 10 MB compressed
- T1 before first match <= 80 MB compressed
- single remote bundle <= 30 MB uncompressed

These are proposals, not final. Actual approved numbers must be written down and enforced.

### 4B - Group Membership Verification

Before any group is merged:
- dump the full uncompressed asset list for every modified group
- verify no T2 or T3 asset appears in T0 or T1 groups
- verify optional skins are absent from all critical groups
- verify shared dependency group is not oversized
- verify portrait bundle is absent from StreamingAssets after rebuild
- verify bundle contents, not just size
- confirm no bundle mixes T1 gameplay units with T2 cosmetics or unrelated assets

### 4C - Manifest Discipline

Every release that modifies critical_content or its equivalent preload manifest must include:
- a full list of what is in T0
- a full list of what is in T1
- a diff of what changed from the previous release
- a one-line reason for each newly added item citing the specific failure it prevents under R1

No item enters the critical set without written reason.

### 4D - No Convenience-Based Preload Expansion

Prohibited patterns:
- "download all dependencies" against broad mixed labels
- preload entire groups when only a subset is required
- broad label downloads whose backing asset lists are not audited and frozen
- silently adding cosmetics or deprecated assets because they are bundled nearby

## 5. Revised Execution Order

Steps must be completed in order. Do not start a step until the previous step is verified.

Step 1
Fallback plus missing-prefab safety
Gate: every load path tested with bundle absent; no silent nulls confirmed

Step 2
Retry UI plus failure states
Gate: manifest and bundle failure paths tested end-to-end

Step 3
Addressables group split:
- Remote Environment
- tighten Gameplay Core Units
- tighten Shared Gameplay Dependencies
Gate: all groups have correct Remote schema; no local groups accidentally created

Step 4
Shared dependency cleanup - audit and trim
Gate: shared group size verified reasonable

Step 5
Define and document critical preload policy - assign every asset a tier
Gate: every asset has a tier; no unclassified assets

Step 6
Manifest and deprecated asset cleanup
Gate: test assets removed; metadata fields documented as active or admin-only

Step 7
Rebuild Addressables, rebuild player, verify actual bundle outputs
Gate: build output and Used Assets logs confirm intended assets are remote and critical bundles are correctly split

Step 8
Optional skins on-demand loading
Gate: skins load correctly on-demand; confirmed absent from T0/T1 preload

Step 9
Config and version simplification
Gate: inert metadata removed from runtime model

Step 10
WebGL delivery tuning: compression and cache headers
Gate: measured against size budgets

Step 11
Pipeline hardening - automate budget and manifest checks
Gate: budget checks and manifest diff checks run on every build

Step 12
Acceptance matrix validation
Gate: all criteria met and signed off

## 6. Acceptance Criteria

AC1
Critical login-to-lobby preload contains only T0 assets
How to verify: dump T0 asset list and network trace on clean cache before lobby appears

AC2
Optional skins and cosmetics are absent from all T0/T1 groups
How to verify: inspect group asset lists and bundle contents

AC3
Missing critical bundle fails visibly and safely
How to verify: pull a critical bundle from server mid-test; confirm visible error, no null crash

AC4
No silent null prefab cases remain
How to verify: test every LoadAssetAsync / InstantiateAsync path with missing bundle

AC5
No single giant convenience bundle remains
How to verify: inspect each bundle's asset contents and uncompressed size; confirm no bundle mixes T1 gameplay units with T2 cosmetics or unrelated assets

AC6
Repeat sessions reuse cache
How to verify: reload after first session; confirm no re-download of unchanged bundles in browser network tab

AC7
First match has no missing models, pink materials, broken animators, or mid-match critical downloads
How to verify: play a full match on clean cache and watch rendering, console, and network

AC8
Critical manifest contents are reviewable and justified
How to verify: release diff exists; every T0/T1 asset has written justification

AC9
Size budgets are met
How to verify: measure compressed build and remote download sizes against approved budgets

AC10
Base WebGL player no longer contains assets that were intentionally moved remote
How to verify: inspect build output and Used Assets logs for removed remote targets

AC11
Portrait bundle absent from StreamingAssets
How to verify: inspect StreamingAssets/aa/WebGL/ in build output

AC12
Registry stripping or remote reference stripping is confirmed
How to verify: build log contains expected strip confirmation and runtime tests prove remote-only references are functioning

AC13
Login-to-lobby preload contains only T0 content
How to verify: network tab on clean cache confirms no unit, environment, or portrait bundle is downloaded before lobby appears

AC14
T1 content loads after lobby entry, before earliest dependent screen
How to verify: observe download sequence; units, environment, and portraits begin after lobby entry and complete before loadout or match start as required

AC15
Lobby can be entered without downloading environment, unit, or portrait bundles unless a specific named dependency requires otherwise
How to verify: artificially block T1 bundle server; confirm lobby entry still succeeds and only T0 assets were requested

AC16
Central orchestration owns all T1 downloads
How to verify: code audit plus runtime logs confirm only centralized manager starts first-time T1 downloads; dependent systems only await readiness

AC17
T1 requests are idempotent
How to verify: trigger repeated requests for same asset subset; confirm no duplicate downloads or conflicting state

## 7. Risks and Watchouts

Waypoints and spawn points in environment extraction
If a path node or spawn point is accidentally moved into the environment prefab, gameplay/pathing will break. Any GameObject with gameplay logic or custom script stays in scene.

Lightmaps
Moving scene environment art into a runtime prefab may break baked lightmap lookup. Test lighting after instantiation. Rework lighting if needed.

Shared dependency group becoming a junk drawer
This is a common Addressables failure mode. Enforce the two-unit minimum rule and keep this group small and justified.

Schema copy errors
When creating remote groups, verify the actual Addressables profile/schema resolves to Remote.BuildPath / Remote.LoadPath. Wrong IDs can silently create local groups.

Building player before rebuilding Addressables
This will create stale catalogs and missing bundle errors. Always rebuild Addressables first, deploy remote content, then build player.

On-demand loading used to dodge preload discipline
If core gameplay units get moved to on-demand under the excuse that they load fast enough, that violates the plan and reintroduces runtime risk.

Budget gates without enforcement
Budgets without automated enforcement are theater. Pipeline checks must reject over-budget output unless there is an explicit override path.

Multiple download owners
If units, portraits, and environment each start their own first-time downloads, race conditions and duplicated work will creep in. Keep orchestration centralized.

Fake runtime configurability
If runtime models still carry unused content metadata, debugging gets harder and the system appears more flexible than it really is. Remove or clearly label inert fields.
