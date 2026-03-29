# Warrior Pack Animation Integration Plan

Date: 2026-03-28

## Current Import State

- The real Warrior Pack Bundle 2 assets are now imported under `unity-client/Assets/ExplosiveLLC/`.
- The project now has reusable profile assets plus an editor tool that can reapply the mapping onto TT_RTS prefabs.
- The remaining work is gameplay polish, not basic asset hookup.

## What Was Implemented

- Added a reusable `UnitAnimationProfile` ScriptableObject type for:
  - state alias lists
  - attack-family-specific state groups
  - transition durations
  - optional runtime and portrait controller overrides
  - animator settings
- Added `UnitAnimationBinding` so individual prefabs can later opt into:
  - a concrete animation profile
  - controller overrides
  - attack-family overrides
  - per-prefab animator tuning
- Added `UnitAnimationResolver` so the project has one authoritative place for:
  - fallback animation state names
  - runtime intent selection for `attack`, `defend`, and `retreat`
  - TT_RTS and future pack role heuristics
  - controller override application
  - animator state lookup and clip-length resolution
- Refactored `WaveSnapshotRuntimeSpawner` to use the shared resolver instead of hardcoded local state tables.
- Refactored `UnitPortraitCamera` to use the same shared resolver and be ready for portrait-specific controller overrides later.
- Added `PrepareWarriorPackBundle2Profiles` editor tooling to create prepared profile assets and warn if the bundle import only contains link files.
- Expanded the shared animation defaults and resolver so imported controllers with nested state machines and `WalkRun` / `RangeAttack1` style state names can be found reliably at runtime.
- Added a repeatable editor mapping flow:
  - `Castle Defender/Animation/Prepare Warrior Pack Bundle 2 Profiles`
  - `Castle Defender/Animation/Apply Warrior Pack Bundle 2 TT_RTS Mapping`

## Why This Matches The Thesis

The presentation layer now has the same shape as the gameplay thesis:

- authoritative state lives in one place
- local presentation freedom is bounded
- per-unit improvisation is reduced
- future animation work can be added by profile and binding instead of one-off runtime hacks

That means we can keep units visually more expressive without making the integration brittle or turning every prefab into a special case.

## Applied Mapping

- `Archer` pack:
  - TT archer
  - TT crossbowman
  - TT mounted scout
- `Knight` pack:
  - TT heavy infantry
  - TT heavy swordman
  - TT heavy cavalry
  - TT paladin
  - TT mounted paladin
- `2Handed` pack:
  - TT peasant / militia
  - TT light infantry / footman
  - TT mounted knight
  - TT king
- `Mage` pack:
  - TT mounted priest
  - TT mage
  - TT priest
  - TT high priest
  - TT mounted mage
  - TT mounted king
  - TT commander / bishop

## Intentionally Left On Existing Controllers

- TT spearman
- TT halberdier
- TT light cavalry
- TT scout
- TT settler

Those units do not have a clean match in the imported bundle yet, so they stay on the TT_RTS controllers for now instead of being forced onto a misleading weapon style.

## Follow-Up Work

1. Verify the mapped units in play mode with current AI-driven movement and attack timing.
2. Tune per-family transition timings and animator speed multipliers once we see them in waves.
3. Add role-specific hit reacts, alternate attacks, and stronger defend / retreat posture usage.
4. Revisit polearm and scout units with a dedicated spear or cavalry animation source instead of forcing them into the current four packs.

## Guardrails

- Keep root motion disabled for server-authoritative gameplay units.
- Prefer profile-driven state alias resolution over hardcoded controller names in gameplay scripts.
- Do not replace the TT_RTS prefab set just to use the pack.
- Leave units on their TT_RTS controllers when the imported weapon family is visibly wrong.
