# Bootstrap-Only Remote Scenes Handoff

## Current Checkpoint

The Bootstrap-only remote scenes migration is partially runtime-validated and has one important unload regression fixed.

Implemented:

- Centralized scene entry points behind `LoadingScreen`.
- `BootstrapManager` now routes first-scene load through `LoadingScreen`.
- `NetworkManager`, `PostGameSceneController`, and `LoadoutPhaseManager` no longer directly call `SceneManager.LoadScene(...)`.
- `LoadingScreen` is now a persistent bootstrap-owned loading overlay and transition service.
- `LoadingScreen` explicitly initializes Addressables before the first remote scene load.
- `LoadingScreen` uses `Addressables.LoadSceneAsync(...)` for scene loads.
- `LoadingScreen` tracks `AsyncOperationHandle<SceneInstance>` per loaded scene.
- `LoadingScreen` uses handle-based `Addressables.UnloadSceneAsync(...)` for remote scene unloading.
- `RemoteContentManager` now exposes `EnsureAddressablesReady(...)` and `AreAddressablesInitialized`.
- `BuildWebGL.cs` now builds `Bootstrap` only.
- Added `SetupRemoteSceneAddressables.cs` to create/sync a `Remote Scenes` Addressables group.
- Added `RemoteSceneValidationTools.cs` to drive in-editor transition checks and inspect runtime scene/handle state.
- Added `ValidateCentralizedSceneTransitions.cs` to block future raw `SceneManager.LoadScene(...)` usage outside the transition service.
- `LoadingScreen` was moved into `Assets/Scripts/Net` / `CastleDefender.Net` so bootstrap/network callers in the `CastleDefender.Net` asmdef can reference it safely.
- Fixed a real Addressables unload bug in `LoadingScreen`: the previous implementation auto-released the unload handle before reading its status, which caused `Attempting to use an invalid operation handle` during `Login -> Lobby`.
- Updated loading-overlay timing in `LoadingScreen` to use unscaled time so scene transitions do not stall if gameplay time scale is paused or zero.

Validated:

- `BuildWebGL.cs` still builds `Bootstrap` only.
- `SetupRemoteSceneAddressables.cs` creates/syncs the `Remote Scenes` group and keeps `Bootstrap` as the only enabled build scene.
- `Remote Scenes` group exists and uses remote build/load profile IDs with remote bundle caching/CRC enabled.
- The synced group currently contains:
  - `Login`
  - `Lobby`
  - `Loading`
  - `Loadout`
  - `Game_ML`
  - `PostGame`
- `Game_Classic.unity` is currently missing from `Assets/Scenes`, so sync logs a warning and does not add it.
- Runtime happy-path validation succeeded for:
  - `Bootstrap -> Login`
  - `Login -> Lobby`
  - `Lobby -> Loadout`
  - `Loadout -> Game_ML`
- For each validated transition above:
  - `Bootstrap` remained loaded as the persistent local scene
  - the active remote scene switched correctly
  - the previous remote scene was unloaded through `Addressables.UnloadSceneAsync(...)`
  - tracked scene handles collapsed back to only the current remote scene after unload
- The centralized transition guard menu item reports no forbidden direct `SceneManager.LoadScene*` usage outside `LoadingScreen` and tests.

Still not fully smoke tested in Unity runtime or WebGL deployment.

## Key Files

- `unity-client/Assets/Scripts/Net/LoadingScreen.cs`
- `unity-client/Assets/Scripts/Net/RemoteContentManager.cs`
- `unity-client/Assets/Scripts/Net/BootstrapManager.cs`
- `unity-client/Assets/Scripts/Net/NetworkManager.cs`
- `unity-client/Assets/Scripts/UI/PostGameSceneController.cs`
- `unity-client/Assets/Scripts/UI/LoadoutPhaseManager.cs`
- `unity-client/Assets/Scripts/Editor/BuildWebGL.cs`
- `unity-client/Assets/Scripts/Editor/SetupRemoteSceneAddressables.cs`
- `unity-client/Assets/Scripts/Editor/RemoteSceneValidationTools.cs`
- `unity-client/Assets/Scripts/Editor/ValidateCentralizedSceneTransitions.cs`

## Remaining Plan

### Phase 2: Editor And Addressables State

Phase 2 is mostly complete.

Remaining editor/addressables follow-up:

1. Decide what to do about missing `Assets/Scenes/Game_Classic.unity`.
2. If `Game_Classic` should still exist, restore it and rerun `Castle Defender -> Remote Content -> Setup Remote Scenes Addressables`.
3. If `Game_Classic` is retired, update the scene sync list and any stale docs/comments so the warning goes away intentionally.

### Phase 3: Runtime Validation

Completed:

1. Boot flow:
   - `Bootstrap -> remote Login`
2. Transition flow:
   - `Login -> Lobby`
   - `Lobby -> Loadout`
   - `Loadout -> Game_ML`
3. Handle-based unload verification:
   - previous remote scene unload logs now complete successfully after each validated transition

Still needed:

1. Finish transition flow:
   - `Game_ML -> PostGame`
   - `PostGame -> Lobby`
2. Test retry/failure UX when:
   - Addressables init fails
   - remote catalog is stale/missing
   - scene bundle download fails
3. Verify reconnect/game-over driven transitions still land in the correct scene.
4. Verify previous-scene bundles are actually released at the bundle/memory level using:
   - Addressables Profiler
   - or a memory snapshot
5. Re-check a bootstrap timing oddity:
   - in-editor, `BootstrapManager.Start()` sometimes logs its first frame immediately but the first transition request may not visibly complete until another runtime interaction occurs
   - once `LoadingScreen` is running, the centralized remote transitions themselves do work

### Phase 4: Build And Deploy

1. Run `Castle Defender -> Remote Content -> Build Addressables Content`.
2. Upload bundles and updated catalog to GCS.
3. Run `Castle Defender -> Build -> Build WebGL Release`.
4. Deploy the bootstrap-only WebGL player.
5. Test in:
   - fresh browser session with empty cache
   - warm cache session to confirm bundle reuse

### Phase 5: Cleanup

1. Update stale comments/docs that still describe the old local additive loading flow.
2. Decide whether `Loading.unity` should remain an addressable scene temporarily or be retired entirely.
3. Add a guard/check to prevent future direct `SceneManager.LoadScene(...)` usage outside the transition service.

## Known Risks / Things To Watch

- A failed transition may still expose edge cases around audio listeners or overlay state.
- `Loading.unity` is still listed in the remote scenes sync helper even though runtime no longer depends on it.
- Runtime logs now confirm previous remote scenes are unloaded via Addressables after validated transitions, but actual bundle/memory release still needs profiler verification.
- `Game_Classic.unity` is missing from `Assets/Scenes`, so the sync helper warns until that is intentionally resolved one way or the other.
- The bootstrap-entry timing oddity still needs manual confirmation in a normal editor run and in WebGL.
- Existing unrelated dirty worktree changes were intentionally left untouched.

## Fresh Terminal Prompt

Use this prompt in a new terminal session:

```text
We are continuing the Bootstrap-only remote scenes migration in c:\Users\Crans\RansomForge\castle-defender.

Read first:
- projects/bootstrap-only-remote-scenes-handoff.md

Current status:
- Runtime foundation is implemented and partial runtime validation is complete.
- LoadingScreen now lives in Assets/Scripts/Net, is the centralized remote scene transition service, and has a fixed unload-handle bug.
- Happy-path runtime transitions have been verified for Bootstrap -> Login -> Lobby -> Loadout -> Game_ML.
- Previous remote scenes now unload successfully via Addressables in those validated transitions.
- BuildWebGL.cs builds Bootstrap only.
- SetupRemoteSceneAddressables.cs exists and the Remote Scenes group is synced, but Game_Classic.unity is currently missing from Assets/Scenes.
- ValidateCentralizedSceneTransitions.cs exists and currently reports no forbidden raw SceneManager.LoadScene* usage outside LoadingScreen/tests.

Your task:
1. Review the current implementation and confirm there are no obvious regressions.
2. Continue Phase 3 from the current checkpoint, starting with Game_ML -> PostGame -> Lobby runtime validation.
3. Test the failure/retry UX paths:
   - Addressables init failure
   - stale/missing catalog
   - scene bundle download failure
4. Verify previous-scene bundles are actually released after transition with the Addressables Profiler or a memory snapshot, or leave precise manual follow-up notes if you still cannot verify directly.
5. Resolve or explicitly document the missing Game_Classic scene decision.
6. Do not revert unrelated user changes in the worktree.

Important constraints:
- Bootstrap.unity must remain the only local WebGL scene.
- Prefer keeping all scene transitions centralized through LoadingScreen.
- Do not reintroduce raw SceneManager.LoadScene(...) calls outside the transition service.
- Be careful with Addressables scene unload semantics; bundle leaks are a primary risk.

At the end, summarize:
- what was verified
- what was changed
- what still needs manual Unity Editor / WebGL smoke testing
```
