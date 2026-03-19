# Bootstrap-Only Remote Scenes Handoff

## Current Checkpoint

The Bootstrap-only remote scenes migration is runtime-validated deeper than before, with the remaining work now mostly around manual bundle-release verification and final WebGL smoke.

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
- `LoadingScreen` now preflights remote scene catalog lookup before `Addressables.LoadSceneAsync(...)` and distinguishes scene-catalog vs scene-bundle failures.
- Added explicit verification fault injection for remote scene catalog lookup and remote scene bundle download failures.
- `ActionSender` gameplay emits now use plain serializable objects instead of `JObject` payloads.
- `NetworkManager.Emit(...)` now serializes native/editor socket payloads through the same JSON path as WebGL, preventing gameplay action payload drift between platforms.
- `LoginUI` now waits for the initial `Bootstrap -> Login` transition to finish before requesting `Lobby`, fixing the authenticated auto-login stall at "Preparing your game session...".

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
- `Game_Classic` has now been retired from the bootstrap-only remote-scenes plan and should stay out of the remote scene sync list.
- Runtime happy-path validation succeeded for:
  - `Bootstrap -> Login`
  - `Login -> Lobby`
  - `Lobby -> Loadout`
  - `Loadout -> Game_ML`
  - `Game_ML -> PostGame`
  - `PostGame -> Lobby`
- For each validated transition above:
  - `Bootstrap` remained loaded as the persistent local scene
  - the active remote scene switched correctly
  - the previous remote scene was unloaded through `Addressables.UnloadSceneAsync(...)`
  - tracked scene handles collapsed back to only the current remote scene after unload
- Failure/retry UX was validated in-editor for:
  - Addressables init failure on first remote login load
  - remote scene catalog-miss / stale-catalog style failure
  - remote scene bundle download failure
- Reissuing the transition after each one-shot injected failure succeeded and advanced to the expected destination scene.
- The centralized transition guard menu item reports no forbidden direct `SceneManager.LoadScene*` usage outside `LoadingScreen` and tests.
- Fresh authenticated startup was validated after the login fix and now advances through `Login -> Lobby` instead of hanging on the login status text.

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

1. Rerun `Castle Defender -> Remote Content -> Setup Remote Scenes Addressables` after the retired `Game_Classic` removal so the group stays synced without the stale warning.

### Phase 3: Runtime Validation

Completed:

1. Boot flow:
   - `Bootstrap -> remote Login`
2. Transition flow:
   - `Login -> Lobby`
   - `Lobby -> Loadout`
   - `Loadout -> Game_ML`
   - `Game_ML -> PostGame`
   - `PostGame -> Lobby`
3. Handle-based unload verification:
   - previous remote scene unload logs now complete successfully after each validated transition
4. Failure/retry UX:
   - Addressables init failure
   - remote scene catalog missing / stale catalog style failure
   - remote scene bundle download failure

Still needed:

1. Verify reconnect/game-over driven transitions still land in the correct scene.
2. Verify previous-scene bundles are actually released at the bundle/memory level using:
   - Addressables Profiler
   - or a memory snapshot
3. Run the updated play mode tests from the Unity Test Runner once the MCP test harness / editor test state is healthy again.
4. Re-verify in normal gameplay that troop send/build actions no longer trigger `Bad action` after the serialization fix.

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
- `Game_Classic` is retired, so any stale docs/comments that still describe it as a remote scene should be cleaned up as they are encountered.
- The MCP play mode test runner failed to initialize cleanly during this session, so the updated tests were not re-run end-to-end even though the same flows were validated manually in Play Mode.
- The gameplay action serialization fix should be re-checked in live gameplay to confirm troop send/build actions no longer emit `Bad action`.
- Existing unrelated dirty worktree changes were intentionally left untouched.

## Fresh Terminal Prompt

Use this prompt in a new terminal session:

```text
We are continuing the Bootstrap-only remote scenes migration in c:\Users\Crans\RansomForge\castle-defender.

Read first:
- projects/bootstrap-only-remote-scenes-handoff.md

Current status:
- Runtime foundation is implemented and deeper runtime validation is complete.
- LoadingScreen now lives in Assets/Scripts/Net, is the centralized remote scene transition service, has the fixed unload-handle bug, and now distinguishes remote scene catalog failures from bundle-download failures.
- Happy-path runtime transitions have been verified for Bootstrap -> Login -> Lobby -> Loadout -> Game_ML -> PostGame -> Lobby.
- Previous remote scenes now unload successfully via Addressables in those validated transitions.
- BuildWebGL.cs builds Bootstrap only.
- SetupRemoteSceneAddressables.cs exists, the Remote Scenes group is synced, and retired Game_Classic has been removed from the remote scene list.
- ValidateCentralizedSceneTransitions.cs exists and currently reports no forbidden raw SceneManager.LoadScene* usage outside LoadingScreen/tests.
- Addressables init failure, stale/missing remote scene catalog failure, and remote scene bundle download failure have all been manually validated in-editor with retry UI appearing.
- ActionSender / NetworkManager gameplay emit serialization was just updated to fix a `Bad action` server response when trying to send/build troops.
- LoginUI auto-login was just updated to stop hanging on `Preparing your game session...` by waiting for the Bootstrap -> Login transition to finish before requesting Lobby.

Your task:
1. Review the current implementation and confirm there are no obvious regressions.
2. Re-test live gameplay actions in the Unity Editor:
   - sending troops from CmdBar
   - placing/upgrading/selling defenders from TileMenuUI
   - confirm the server no longer returns `error_message: Bad action`
3. Re-run the updated play mode tests from the Unity Test Runner if the editor test harness is healthy:
   - `BootstrapRemoteSceneFlowTests`
4. Verify previous-scene bundles are actually released after transition with the Addressables Profiler or a memory snapshot, or leave precise manual follow-up notes if you still cannot verify directly.
5. Keep the retired Game_Classic decision reflected in scene-sync tooling and stale docs/comments.
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
