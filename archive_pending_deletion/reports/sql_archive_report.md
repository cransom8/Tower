# SQL Archive Report

Date: 2026-03-18

## Scope

Scanned the repository for SQL-related artifacts and adjacent database files, prioritizing:

- `.sql`, schema, migration, seed, backup, export, dump, and similarly named files
- files whose names suggest old or deprecated status
- references from setup docs, runtime code, admin tooling, and migration bootstrap code

Key constraint used during review:

- `server/migrate.js` loads every `.sql` file in `server/migrations` in sorted order, so files in that directory are part of the current canonical bootstrap path unless proven otherwise.

## Files Moved

No files were moved.

Reason:

- No high-confidence orphaned, duplicate, backup, or superseded SQL files were found outside the active migration chain.
- Moving any current file from `server/migrations` would change fresh environment setup and deployment behavior.

## Review Needed / Not Moved

These files looked potentially archival by name or behavior, but were left in place because they still appear to affect setup, deployment, or current runtime data shape.

| filename | original path | new archive path | reason it appears unused or outdated | confidence | might still affect setup or deployment |
|---|---|---|---|---|---|
| `044_dedup_wave_configs.sql` | `server/migrations/044_dedup_wave_configs.sql` | `not moved` | One-off cleanup migration for duplicate `ml_wave_configs`, but the current app still uses `ml_wave_configs`/`ml_waves`, and this migration is part of the bootstrap chain. | Medium | Yes |
| `048_seed_corey_admin.sql` | `server/migrations/048_seed_corey_admin.sql` | `not moved` | Environment-specific admin seed by name/content, but it is still executed by the canonical migration loader and may be required for admin bootstrap. | Medium | Yes |
| `049_remove_classic_towers_and_wall.sql` | `server/migrations/049_remove_classic_towers_and_wall.sql` | `not moved` | Looks like legacy cleanup for retired tower rows and `wall_placeholder`, but removing it would change the final seeded DB state for fresh installs. | High | Yes |
| `050_disable_tt_rts_skins_for_launch.sql` | `server/migrations/050_disable_tt_rts_skins_for_launch.sql` | `not moved` | Launch-specific feature gating by name, but still part of the current migration chain and referenced by project notes. | Medium | Yes |
| `051_restore_giant_rat_and_fantasy_wolf.sql` | `server/migrations/051_restore_giant_rat_and_fantasy_wolf.sql` | `not moved` | Looks like a corrective one-off restore patch, but it realigns `unit_types` and `unit_content_metadata` with current content expectations. | Medium | Yes |
| `importRemoteContentSeed.js` | `server/scripts/importRemoteContentSeed.js` | `not moved` | Seed/import utility rather than a SQL file; related to DB content seeding, but still targets active tables and should be reviewed manually before any archive move. | Low | Possibly |

## Summary

- Total files moved: `0`
- Files skipped due to uncertainty: `6`
- Possible duplicate table definitions found: `none confirmed`
- Possible obsolete SQL tables found:
  - `config_versions` created in `server/migrations/020_unit_types.sql` appears unused in current code search. This is a table-level review item only; no file was moved.
  - Legacy data rows, not tables, appear intentionally retired by migrations:
    - classic tower rows in `towers`
    - classic defender/unit rows in `unit_types`
    - `wall_placeholder` row in `unit_types`
- Recommended next review order:
  1. Review whether `config_versions` is still needed anywhere operationally.
  2. Review whether `048_seed_corey_admin.sql` should remain in shared bootstrap or move to a separate environment/bootstrap process.
  3. Review whether `050_disable_tt_rts_skins_for_launch.sql` should stay as a permanent migration or be documented as historical launch state.
  4. Review whether `044_dedup_wave_configs.sql` and `051_restore_giant_rat_and_fantasy_wolf.sql` should remain as permanent history or be folded into a future baseline snapshot.

## Possible Duplicate Table Definitions Found

None confirmed.

Notes:

- `017_towers.sql` and `020_unit_types.sql` both seed gameplay entities, but they define different tables.
- `018_survival_mode.sql` and `039_ml_wave_config.sql` represent different wave systems rather than duplicate table definitions.

## Possible Obsolete SQL Table Groups

No fully obsolete table group was confirmed strongly enough to justify archiving any migration file.

Lower-confidence review targets:

- `config_versions`
  - No current code reference was found during repo search.
- Historical row sets inside active tables
  - Classic tower rows seeded in `017_towers.sql` are later removed/disabled by `049_remove_classic_towers_and_wall.sql`.
  - Classic attacker rows seeded in `020_unit_types.sql` are later disabled by `036_retire_classic_units.sql`.

## Risk Notes

- The highest-risk archive area is `server/migrations`. The repo currently treats that folder as the authoritative migration history for fresh setup.
- The current docs and runtime references indicate the migration chain is still live, not merely historical.
- A safer future cleanup path would be creating a reviewed baseline schema/bootstrap process first, then archiving historical migrations only after the bootstrap mechanism changes accordingly.
