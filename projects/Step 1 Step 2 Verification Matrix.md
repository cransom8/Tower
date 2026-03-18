# Castle Defender Step 1 / Step 2 Verification Matrix

Authority: [Web GL Remote Content Remediation plan](C:\Users\Crans\RansomForge\castle-defender\projects\Web GL Remote Content Remediation plan.md)

This runbook verifies plan Step 1 and Step 2 before any further regrouping work. It is intentionally biased toward visible failure behavior, retry behavior, and centralized orchestration evidence.

## Preconditions

- Use the current repo state at `C:\Users\Crans\RansomForge\castle-defender`.
- Open the Unity project at `unity-client`.
- Start each scenario by running `Castle Defender/Remote Content Verification/Clear All Overrides`.
- After each scenario, run `Castle Defender/Remote Content Verification/Dump Evidence To Console`.
- For WebGL-specific confirmation, prefer a clean browser cache or an incognito session.

## Verification Helpers

Editor menu path: `Castle Defender/Remote Content Verification`

Available overrides:

- `Fail Manifest Download Once`
- `Fail Addressables Init Once`
- `Fail T1 Gameplay Download Once`
- `Fail Portrait Download Once`
- `Clear All Overrides`
- `Dump Evidence To Console`

Evidence report expectations:

- `Owner requests` shows which centralized subsets were asked to run.
- `waitedOnExisting` greater than `0` proves a repeated request waited on in-flight work instead of starting duplicate orchestration.
- `Reuse hits` proves cached or already-in-flight content was reused.
- `Fault injections consumed` proves the forced failure actually fired during the scenario.

## Matrix

| Area | Setup | Action | Pass Criteria | Evidence |
|---|---|---|---|---|
| T0 manifest failure before lobby | Clear overrides, then set `Fail Manifest Download Once` | Launch from login into lobby using the normal loading flow | Loading screen shows visible blocking error and retry button before lobby opens | Evidence report contains `fault` for manifest download and `owner subset=t0.lobby_entry` |
| T0 catalog/init failure before lobby | Clear overrides, then set `Fail Addressables Init Once` | Launch from login into lobby | Loading screen shows visible blocking error and retry button before lobby opens | Evidence report contains `fault` for Addressables initialization and `owner subset=t0.lobby_entry` |
| Retry without restart | Use either T0 failure setup above | Click retry on the loading screen without restarting Play Mode or the client | The same loading screen retries and reaches lobby if the fail-once override has been consumed | Evidence timeline shows one fault event followed by a successful retry path in the same session |
| Lobby entry succeeds while T1 is unavailable | Clear overrides, enter lobby, then set `Fail T1 Gameplay Download Once` before starting loadout flow | Stay in lobby and let lobby warmup run, then try to open loadout | Lobby remains usable even though T1 warmup fails; loadout gate later blocks with visible retry UI | Evidence shows `owner subset=t1.gameplay` from lobby warmup and later from the loadout gate |
| Portrait failure blocks loadout | Clear overrides, set `Fail Portrait Download Once` | Trigger a loadout transition | Loadout scene does not open until portraits are ready; loading screen shows visible blocking error and retry button | Evidence shows `owner subset=t1.portraits` and a portrait fault event |
| Portrait retry works | Use portrait failure setup above | Click retry on the blocking loading screen | Portrait prep retries in-place and loadout opens after the override is consumed | Evidence shows a portrait fault followed by reuse or successful completion in the same session |
| T1 gameplay failure blocks loadout | Clear overrides, set `Fail T1 Gameplay Download Once` | Trigger a loadout transition | Loading screen blocks before loadout opens and shows visible retry UI | Evidence shows `owner subset=t1.gameplay` and the T1 gameplay fault |
| Loadout does not open before required T1 is ready | Clear overrides, optionally use either `Fail T1 Gameplay Download Once` or `Fail Portrait Download Once` | Trigger a loadout transition | Loadout scene is not entered until the blocking gate succeeds | Scene remains on loading screen during failure state; evidence shows the required owner requests before success |
| Rematch uses same gate | Complete one match, then from postgame trigger rematch-to-loadout with `Fail T1 Gameplay Download Once` or `Fail Portrait Download Once` armed beforehand | Enter rematch flow | Postgame rematch transition shows the same blocking behavior and retry UI as lobby-to-loadout | Evidence shows the same `t1.gameplay` and `t1.portraits` owner subsets during postgame path |
| Match start blocks when T1 is incomplete | Clear overrides, let loadout open, then force the relevant T1 gate to fail before match start if needed | Start the match from loadout | Match scene does not open until required T1 gameplay content is ready | Evidence shows `owner subset=t1.gameplay requester=LoadingScreen.SceneGate:Game_ML` |
| Missing remote unit prefab is never silent null | Run with a bundle or asset key intentionally missing for a required unit, or use a known bad mapping | Spawn or render the affected unit | Unit path resolves to assigned fallback prefab or runtime placeholder; console shows deterministic warning or error, never silent null behavior | Console contains `UnitPrefabRegistry` fallback log and gameplay continues with visible placeholder or explicit error |
| No legacy UI-owned first-time downloads remain | No override needed | Exercise lobby, loadout, portrait capture, rematch, and command bar flows | UI paths do not call first-time Addressables downloads directly | Code audit command below returns only `RemoteContentManager` as the first-time download owner |
| Repeated requests do not duplicate T1 downloads | Clear overrides, then trigger the same loadout gate twice quickly or let lobby warmup overlap with loadout gating | Exercise repeated T1 requests in one session | Second request waits on existing work or reuses cache instead of starting duplicate first-time work | Evidence `Owner requests` summary shows `waitedOnExisting` above `0` and `Reuse hits` for cached or in-flight portrait/gameplay content |
| Central orchestration owns first-time T1 | No override needed | Run lobby warmup, loadout gate, and rematch gate | Only `RemoteContentManager` starts first-time T1 downloads; dependent systems only await readiness | Code audit and evidence report both show centralized subset ownership |

## Code Audit Commands

Run from repo root:

```powershell
rg -n "EnsureUnitPrefabLoaded\\(|DownloadDependenciesAsync\\(|Addressables\\.LoadAssetAsync|Addressables\\.InstantiateAsync" unity-client/Assets/Scripts/UI unity-client/Assets/Scripts/Game
```

Expected result:

- No first-time download orchestration in UI code.
- Any remaining Addressables first-time download calls live inside `unity-client/Assets/Scripts/Net/RemoteContentManager.cs`.

Run from repo root:

```powershell
rg -n "PrepareLobbyEntryContentForSession\\(|PreloadCriticalContentForSession\\(|EnsurePortraitsReady\\(" unity-client/Assets/Scripts
```

Expected result:

- Dependent systems call centralized readiness/orchestration entry points.
- `RemoteContentManager` remains the only first-time T1 owner.

## Sign-Off Notes

- Step 1 is not complete until every missing-prefab path is proven to fall back visibly or fail visibly.
- Step 2 is not complete until manifest, Addressables init, portrait, and gameplay-bundle failures all show visible retry behavior and successful in-session retry.
- Do not start Addressables regrouping work from plan Step 3 until this matrix is executed and recorded.
