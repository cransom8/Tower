# T2 / T3 Slimming Audit

Date: 2026-03-17

## Summary

- `T2` skin content is currently not inflating `T0` or `T1`.
- `T3` performance test JSON files appear absent from `Assets/Resources`.
- The remaining size pressure is not coming from skins. It is concentrated in a few large `T1` remote bundles and the remote environment bundle.

## T2 Status

### Addressables groups

From `unity-client/Library/com.unity.addressables/buildlayout.json`:

- `Remote Skins 01`: `0` bundles
- `Remote Skins 02`: `0` bundles
- `Remote Skins 03`: `0` bundles

This is good for the current slimming phase:

- no skin bundles are being built right now
- skins are not present in the current `T0` / `T1` remote outputs

### Server/runtime status

Relevant files:

- `server/routes/skins.js`
- `server/index.js`
- `server/remoteContent.js`
- `server/migrations/050_disable_tt_rts_skins_for_launch.sql`

Current state:

- skins API still exists
- content-manifest still supports skin rows if enabled in DB
- migration `050_disable_tt_rts_skins_for_launch.sql` disables `tt_*` RTS skins for launch

Implication:

- skins are functionally in a deferred/on-demand posture today
- Step 8 is not fully productized yet because there are no active skin bundles to verify against, but skins are also not polluting the critical path

## T3 Status

### Performance test assets

Plan-marked removable assets:

- `Assets/Resources/PerformanceTestRunInfo.json`
- `Assets/Resources/PerformanceTestRunSettings.json`

Current audit result:

- no live matches were found for those files under `unity-client/Assets`
- `.gitignore` now explicitly ignores both files and their `.meta` siblings to reduce accidental reintroduction

### Cosmetic/dead content

The audit did not find active skin bundles in Addressables, which reduces immediate `T3` risk in critical bundles.

Remaining open `T3` task:

- audit database skin rows and remote mappings for dead or broken entries
- decide per skin whether it belongs in `T2 deferred` or `T3 remove`

## Key Finding

The next meaningful slimming gains are not from skins.

They are from oversized `T1` bundles:

- `Remote Units 02`: `76.58 MB`
- `Remote Units 01`: `63.01 MB`
- `Remote Units 07`: `59.35 MB`
- `Remote Units 03`: `46.97 MB`
- `Remote Environment`: `42.62 MB`
- `Remote Units 09`: `31.37 MB`

Those numbers are far more important than the currently empty skin groups.

`Remote Units 03` was already split once in this pass and dropped from `122.88 MB` to:

- `Remote Units 03`: `46.97 MB`
- `Remote Units 07`: `59.35 MB`
- `Remote Units 08`: `16.49 MB`

`Remote Units 05` was then split again and dropped from `76.63 MB` to:

- `Remote Units 09`: `31.37 MB`
- `Remote Units 05`: `24.02 MB`
- `Remote Units 10`: `21.23 MB`

## Recommended Next Step

Start the next slimming pass by splitting oversized `T1` bundles to meet plan budget intent:

1. split `Remote Units 02`
2. review whether `Remote Units 01` should be decomposed next or whether `Remote Units 07` is the higher-value follow-up
3. review whether `Remote Environment` can be split into lower-risk visual chunks
4. keep skins deferred unless they begin producing real bundles again
