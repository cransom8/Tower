# Bundle Budget Snapshot

Date: 2026-03-17
Source: `unity-client/Library/com.unity.addressables/buildlayout.json`

## Remote bundle sizes

- `Remote Units 02`: `76.58 MB`
- `Remote Units 01`: `63.01 MB`
- `Remote Units 07`: `59.35 MB`
- `Remote Units 03`: `46.97 MB`
- `Remote Environment`: `42.62 MB`
- `Remote Units 09`: `31.37 MB`
- `Remote Units 05`: `24.02 MB`
- `Remote Units 06`: `23.67 MB`
- `Remote Units 10`: `21.23 MB`
- `Remote Units 04`: `16.91 MB`
- `Remote Units 08`: `16.49 MB`
- `Remote Portraits`: `3.28 MB`

## Split pass 1 result

`Remote Units 03` was decomposed into:

- `Remote Units 03`: `46.97 MB`
- `Remote Units 07`: `59.35 MB`
- `Remote Units 08`: `16.49 MB`

Moved units:

- to `Remote Units 07`: `cyclops`, `demon_lord`, `ogre`
- to `Remote Units 08`: `troll`, `werewolf`

Remaining in `Remote Units 03`:

- `darkness_spider`
- `ghoul`
- `giant_rat`
- `goblin`
- `lizard_warrior`

Previous pre-split size for `Remote Units 03`: `122.88 MB`
Current post-split largest replacement bundle from that bucket: `59.35 MB`

## Split pass 2 result

`Remote Units 05` was decomposed into:

- `Remote Units 09`: `31.37 MB`
- `Remote Units 05`: `24.02 MB`
- `Remote Units 10`: `21.23 MB`

Moved units:

- to `Remote Units 09`: `evil_watcher`, `mountain_dragon`
- to `Remote Units 10`: `fantasy_wolf`, `wyvern`

Remaining in `Remote Units 05`:

- `kobold`
- `skeleton_knight`

Previous pre-split size for `Remote Units 05`: `76.63 MB`
Current post-split largest replacement bundle from that bucket: `31.37 MB`

## Read against plan guidance

The remediation plan's suggested starting ceilings were:

- `T1 before first match <= 80 MB compressed`
- `single remote bundle <= 30 MB uncompressed`

Current snapshot shows multiple bundles still far above that suggested single-bundle ceiling:

- `Remote Units 02`
- `Remote Units 01`
- `Remote Units 07`
- `Remote Units 03`
- `Remote Environment`
- `Remote Units 09`

## Immediate implication

The next slimming win is not another manifest or gating change.

It is bundle decomposition:

- break up oversized `Remote Units` groups
- continue with `Remote Units 02` next
- review whether `Remote Environment` can be split or trimmed further
- keep portraits as-is unless new evidence shows hidden duplication

## Notes

- `Remote Skins 01/02/03` currently build `0` bundles, so skins are not the present driver of bundle bloat.
- This snapshot is about remote bundle shape, not the total `server/client` folder size.
