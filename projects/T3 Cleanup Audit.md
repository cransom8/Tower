# T3 Cleanup Audit

Date: 2026-03-17

## Confirmed Current State

The two explicit T3 asset targets named in the remediation plan are currently absent from the Unity project:

- `unity-client/Assets/Resources/PerformanceTestRunInfo.json`
- `unity-client/Assets/Resources/PerformanceTestRunSettings.json`

The legacy portrait `Resources` folder is also no longer carrying active portrait files:

- `unity-client/Assets/Resources/UnitPortraits`

Portrait delivery has already moved to:

- `unity-client/Assets/AddressableContent/UnitPortraits`

## T3 Notes

- The plan-specific test JSON files do not currently need removal because they are already not present.
- Deprecated saved-loadout code paths still exist in runtime/server compatibility code, but that is not part of the current Step 3 environment extraction work.
- Any later T3 cleanup pass should still review backup build folders and old generated WebGL output, but those are outside the active Unity asset path.

## Current Recommendation

Proceed with Step 3 environment extraction work while keeping T3 cleanup tracked separately for the later manifest and cleanup phase.
