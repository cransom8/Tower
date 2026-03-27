---
name: design-rts-combat-architecture
description: Design and review Unity RTS and tower-defense combat systems with a focus on visual quality, frame-time discipline, battlefield readability, snapshot-driven networking, and durable backend data architecture. Use when Codex needs to plan or critique combat feel, rendering and optimization budgets, lane or battlefield topology, unit and tower presentation, server-authoritative RTS simulation, Socket.IO snapshot flows, Postgres schema choices for match and config data, or phased implementation roadmaps for the Castle Defender project.
---

# Design RTS Combat Architecture

Use this skill to make concrete design and implementation decisions for Castle Defender's combat presentation and RTS architecture. Anchor recommendations in the current Unity client, server simulation, and migration history instead of giving generic game-dev advice.

## Start

- Identify the primary request shape: design review, implementation plan, performance diagnosis, combat-feel pass, battlefield-topology rewrite, or backend schema change.
- Extract or infer the operating constraints before proposing changes:
  - target platform
  - target frame rate
  - expected concurrent unit, projectile, and VFX counts
  - camera height and readability needs
  - whether the request changes authoritative simulation, client presentation, or durable storage
- State assumptions when the user does not provide them.

## Load What Matters

- Read [references/project-map.md](references/project-map.md) first to find the live authority for the change.
- Read [references/unity-visual-performance.md](references/unity-visual-performance.md) when the task touches rendering quality, frame time, VFX, lighting, camera, materials, or scene readability.
- Read [references/combat-feel.md](references/combat-feel.md) when the task touches attack cadence, projectile readability, hit response, death feedback, unit identity, audio, or "make combat feel real."
- Read [references/server-data-architecture.md](references/server-data-architecture.md) when the task touches match persistence, live state ownership, snapshots, event logs, schema design, analytics, or Postgres performance.

## Workflow

1. Locate the authoritative layer before changing anything.
   - Treat `server/sim-multilane.js` as combat and pathing truth.
   - Treat `server/game/multilaneRuntime.js` and `unity-client/Assets/Scripts/Net/*.cs` as snapshot and match-flow wiring.
   - Treat `server/migrations/*.sql`, `server/db.js`, and `server/game/matchPersistence.js` as durable-data guidance.
2. Set budgets before proposing polish.
   - Define what "looks better" means in measurable terms: unit readability, castle impact, cleaner lane contrast, lower overdraw, fewer draw calls, lower snapshot size, or faster queries.
   - Refuse "maximize visuals" advice that has no frame-time or scale target.
3. Prefer readable spectacle over expensive noise.
   - Spend budget on silhouettes, path readability, lane color language, attack timing, impact signatures, and a few high-value hero moments.
   - Push expensive post-processing, realtime lights, and heavy transparency behind explicit quality tiers.
4. Keep simulation authoritative and presentation elastic.
   - Do not move combat truth into client-only effects.
   - Use client-side smoothing, anticipation, trails, flashes, audio, and screen feedback to sell impact without changing outcomes.
5. Keep the database durable, not chatty.
   - Do not persist per-tick unit positions, attack rolls, or projectile updates.
   - Persist match boundaries, config versions, compact summaries, rewards, analytics aggregates, and debug logs that are worth querying later.
6. Tie every recommendation to the existing codebase.
   - Name the file or migration that should change.
   - Call out whether the safest first move is server, client, schema, or tooling.
   - Propose phased work when the request crosses multiple layers.

## Project Guardrails

- Use `BATTLEFIELD_REMODEL_HANDOFF.md` before changing battlefield topology, shared funnels, slot identity, lane ownership, or camera framing for the shared map.
- Reuse the existing migration style under `server/migrations/` instead of inventing ad hoc SQL entry points.
- Favor config-driven content over hard-coded one-off rules when the change affects units, projectiles, waves, or balance tuning.
- Treat `combat_log` and `wave_stats` as compact summaries, not as an excuse to write every hit to Postgres.
- When a request spans visuals and optimization, fix readability first, then spend any remaining budget on atmosphere.
- When a request spans simulation and persistence, keep the hot path in memory and move expensive writes to match end or phase boundaries.

## Response Shape

- Return a short design thesis first.
- Then provide:
  - the critical constraints
  - the authoritative files to touch
  - the recommended architecture or visual direction
  - a phased implementation plan
  - the validation steps and metrics
- If the request is a review, lead with risks and regressions before suggestions.
- If the request is implementation, inspect the referenced files before editing and keep the write set as small as possible.

## Example Invocations

- "Use $design-rts-combat-architecture to review this battlefield rewrite for readability, performance, and server authority."
- "Use $design-rts-combat-architecture to plan a database model for match summaries, wave stats, and replay-safe combat logs."
- "Use $design-rts-combat-architecture to make this Unity combat look better without hurting WebGL or mobile performance."

Only load the reference files you need for the current request.
