# Project Map

## Stack Snapshot

- Client: Unity 6 project in `unity-client/` using URP, VFX Graph, Addressables, and Socket.IO Unity integration.
- Server: Node 22 in `server/` using Express, Socket.IO, and Postgres through `pg`.
- Match model: server-authoritative multilane RTS and tower-defense hybrid with Unity rendering snapshots from network payloads.

## Authoritative Server Files

- `server/sim-multilane.js`
  - Core combat and pathing authority.
  - Defines the 20 Hz sim tick, lane grid constants, shared route suffix, fixed slot layout, battlefield topology, abilities, and combat rules.
- `server/game/multilaneRuntime.js`
  - Owns room lifecycle, lobby flow, match start and end, wave config loading, outcome payloads, and player-facing state transitions.
- `server/game/matchPersistence.js`
  - Persists match start and end boundaries plus player outcomes.
  - Use it as the baseline for "durable summary write, not per-tick write."
- `server/db.js`
  - Wraps the Postgres pool and logs slow queries over 300 ms.

## Client Runtime Files

- `unity-client/Assets/Scripts/Net/NetworkManager.cs`
  - Registers Socket.IO events, stores lane and loadout state, and bridges match flow across scene loads.
- `unity-client/Assets/Scripts/Net/SnapshotApplier.cs`
  - Caches the latest authoritative snapshots and exposes helpers for lane assignment, team checks, and slot colors.
- `unity-client/Assets/Scripts/Game/TileGrid.cs`
  - Maps branch-local tile coordinates into battlefield world space.
  - Also defines shared path polylines and fallback lane colors.
- `unity-client/Assets/Scripts/Game/LaneRenderer.cs`
  - Primary unit and tower presentation layer.
  - Read it when the task changes how combat is seen, not only how it is simulated.
- `unity-client/Assets/Scripts/Game/ProjectileSystem.cs`
  - Builds snapshot-driven projectile visuals and impact effects from server payloads.
- `unity-client/Assets/Scripts/FX/PostProcessController.cs`
  - Useful when the request touches URP post-processing, mood, bloom, or quality tiers.

## Data and Migrations

- `server/migrations/004_matches.sql`
  - Base durable match and match-player tables.
- `server/migrations/020_unit_types.sql`
  - Unit reference data.
- `server/migrations/023_projectile_defs.sql`
  - Projectile reference data.
- `server/migrations/039_ml_wave_config.sql`
  - Multilane wave configuration tables.
- `server/migrations/043_combat_log.sql`
  - Adds `combat_log JSONB` for compact per-match history.
- `server/migrations/045_match_wave_stats.sql`
  - Adds `wave_stats JSONB` for summarized wave analytics.
- `server/migrations/047_remote_content_metadata.sql`
  - Useful when the request touches content versioning or remote definitions.

## Battlefield Remodel Context

- `BATTLEFIELD_REMODEL_HANDOFF.md`
  - Read this before changing battlefield topology, camera framing, slot identity, or the transition from isolated lanes to a shared lava-lake funnel map.
  - It documents what is already done, what is still straight-lane legacy, and which layer should change first.

## Fast Search Patterns

- `rg -n "TICK_HZ|SHARED_SUFFIX_LENGTH|FIXED_SLOT_LAYOUT|BATTLEFIELD_TOPOLOGY" server/sim-multilane.js`
- `rg -n "ml_match_ready|ml_match_config|OnMLStateSnapshot|OnMLGameOver" unity-client/Assets/Scripts/Net`
- `rg -n "TileToWorld|NormProgressToWorld|SuffixProgressToWorld" unity-client/Assets/Scripts/Game`
- `rg -n "combat_log|wave_stats|unit_types|projectile_defs|game_configs" server/migrations`

## Use This Map

- Start here when a request spans more than one layer.
- Use it to name the real authority before proposing a change.
- Keep recommendations tied to these files instead of generic "engine best practices."
