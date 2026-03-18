# Castle Defender Addressables Group Audit

Date: 2026-03-17

## Current Scene Loading State

Scenes are not currently loaded through Addressables.

- `LoadingScreen.LoadSceneWithRemoteContentGate(...)` still transitions with `SceneManager.LoadSceneAsync(...)`
- `Game_ML` is still a built scene, not an Addressables scene
- Remote content currently covers:
  - T1 unit prefabs
  - T1 portrait textures
- The environment extraction step from the remediation plan is not complete yet, so the visual match environment is still embedded in `Game_ML`

Relevant code:

- `unity-client/Assets/Scripts/UI/LoadingScreen.cs`
- `unity-client/Assets/Scripts/Net/RemoteContentManager.cs`

## Current Bundle Snapshot

From `unity-client/Library/com.unity.addressables/buildlayout.json`:

| Group | Bundle Count | Size (MB) |
|---|---:|---:|
| Remote Portraits | 1 | 3.28 |
| Remote Units 01 | 1 | 63.01 |
| Remote Units 02 | 1 | 76.58 |
| Remote Units 03 | 1 | 122.88 |
| Remote Units 04 | 1 | 16.91 |
| Remote Units 05 | 1 | 76.63 |
| Remote Units 06 | 1 | 23.67 |

## Current Addressable Unit Membership

### Remote Units 01

- `units/chimera`
- `units/dragonide`
- `units/griffin`
- `units/mummy`

### Remote Units 02

- `units/hobgoblin`
- `units/hydra`
- `units/ice_golem`
- `units/manticora`
- `units/oak_tree_ent`
- `units/undead_warrior`

### Remote Units 03

- `units/cyclops`
- `units/darkness_spider`
- `units/demon_lord`
- `units/ghoul`
- `units/giant_rat`
- `units/goblin`
- `units/lizard_warrior`
- `units/ogre`
- `units/troll`
- `units/werewolf`

### Remote Units 04

- `units/giant_viper`
- `units/orc`

### Remote Units 05

- `units/evil_watcher`
- `units/fantasy_wolf`
- `units/kobold`
- `units/mountain_dragon`
- `units/skeleton_knight`
- `units/wyvern`

### Remote Units 06

- `units/harpy`
- `units/vampire`

## Current Addressable Portrait Membership

Portraits are present for the currently audited T1 unit set, including `giant_rat` and `fantasy_wolf`.

## Immediate Findings

1. `Remote Units 03` is the main outlier at `122.88 MB` and should be audited first for shared dependencies or accidental bloat.
2. `Remote Units 02` and `Remote Units 05` are also large enough to justify dependency review.
3. There is no remote environment group yet, so Step 5 from the remediation plan remains open.
4. The current runtime model is:
   - built scenes via `SceneManager`
   - remote gameplay assets via Addressables
   - remote portraits via Addressables

## Shared Dependency Pass

The current `buildlayout.json` does not show meaningful non-builtin cross-unit dependency sharing.

Observed result:

- The only repeated `InternalReferencedOtherAssets` RID across all `units/*` prefabs was `Resources/unity_builtin_extra`
- No production art/material/texture dependency appeared as a repeated cross-unit shared asset in this pass

Implication:

- A large shared gameplay dependency group is not currently justified by the build layout evidence
- The oversized remote unit bundles appear to come more from the units' own direct asset footprints than from obvious duplicated cross-unit dependencies
- The biggest remaining source of player-build bloat is more likely scene-embedded content, especially the `Game_ML` environment, than missing shared-unit extraction

This means the next highest-value Addressables pass is still the remote environment extraction workstream, while keeping an eye on whether any specific unit groups need texture/material optimization later.

## Next Addressables Work

1. Extract the pure-visual `Game_ML` environment into `environment/game_ml`.
2. Rebuild Addressables and verify the remote bundle outputs again after regrouping.
3. Re-measure player build size after environment extraction.
4. Only create a shared gameplay dependency group if a later audit shows concrete non-builtin reuse worth splitting.
