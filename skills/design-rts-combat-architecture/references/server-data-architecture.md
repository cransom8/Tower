# Server Data Architecture

## Core Thesis

Keep live RTS combat in memory and over the socket. Persist only the data that is durable, queryable, replay-relevant, or operationally valuable after the match ends.

## Current Repo Direction

- `server/db.js` uses a shared Postgres pool and logs slow queries.
- `server/game/matchPersistence.js` persists match start and end boundaries plus player outcomes.
- `server/migrations/043_combat_log.sql` adds `combat_log JSONB`.
- `server/migrations/045_match_wave_stats.sql` adds `wave_stats JSONB`.
- `server/sim-multilane.js` is already the hot authoritative simulation loop. Keep database activity off that path.

## What Belongs In Postgres

- Durable account and progression data:
  - players
  - ratings
  - parties
  - bans
  - loadouts
- Reference and config data:
  - unit definitions
  - projectile definitions
  - wave configurations
  - game configs
  - remote content metadata
- Match summaries and outcomes:
  - match start and end
  - winners and losers
  - compact combat logs
  - wave summaries
  - rewards and season updates

## What Does Not Belong In Postgres

- Per-tick unit positions
- Individual attack resolution writes
- Projectile travel state
- Every damage event in a crowded fight
- Client-only presentation state

## Relational Vs JSONB

- Use relational tables for entities you need to join, filter, aggregate, or enforce with foreign keys.
- Use JSONB for flexible per-match summaries, evolving diagnostics, and payloads that are useful to inspect later but do not belong in the hot relational model.
- Do not put a hot gameplay stream into JSONB just because it is easy to append.

## Patterns To Prefer

- Store config version or content version identifiers with each match so results remain explainable.
- Persist summary rows at clear boundaries:
  - match start
  - phase transition
  - match end
  - post-match reward settlement
- Use transactions for operations that must remain consistent across tables.
- Batch noncritical analytics writes or move them to match end.
- Add indexes only for real query patterns.

## Guidance For RTS Scaling

- Reduce payload size before raising tick rate.
- Prefer snapshots plus client smoothing over database-backed live state.
- If replay or forensic debugging matters, store:
  - seed
  - version ids
  - compact event summaries
  - outcome snapshots
- If a proposed schema change is really for debugging only, consider JSONB, object storage, or offline logs before adding hot relational tables.

## Repo-Specific Review Questions

- Can this data be reconstructed from the authoritative sim plus versioned config?
- Does this write happen on the hot path inside or near the sim tick?
- Will product or operations actually query this field later?
- Should this be a relational entity, a compact JSONB summary, or not persisted at all?
- Can this write wait until a phase boundary or match end?

## Validation

- Check query plans for new admin or analytics endpoints.
- Watch `server/db.js` slow-query logging after adding indexes or new write paths.
- Confirm that match-finalization writes stay transactional and idempotent.
- Favor the smallest durable schema that still supports replay, analytics, moderation, and live operations.
