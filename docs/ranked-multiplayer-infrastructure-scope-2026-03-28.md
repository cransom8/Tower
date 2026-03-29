# Ranked Multiplayer Infrastructure Scope

## Goal

Define the smallest backend and hosting scope required to support:

- authoritative live multiplayer matches
- ranked matchmaking
- reconnect and disconnect grace
- match result persistence
- rating updates
- a clean path from today's single-process runtime to disposable match workers later

This scope is based on the current server code as of 2026-03-28.

## Executive Read

You are not starting from zero.

The repo already contains most of the gameplay-backend foundation needed for a ranked alpha:

- auth and JWT issuance in `server/auth.js`
- REST and Socket.IO bootstrap in `server/index.js`
- in-memory runtime ownership in `server/state/runtimeState.js`
- authoritative match simulation in `server/sim-multilane.js`
- live match lifecycle in `server/game/multilaneRuntime.js`
- matchmaking queue services in `server/services/matchmaker.js` and `server/services/matchQueueManager.js`
- reconnect tokens and reconnect validation in `server/socket/helpers.js` and `server/socket/registerHandlers.js`
- match persistence in `server/game/matchPersistence.js`
- rating updates in `server/services/rating.js`
- backend tests covering core runtime and match logic in `server/tests/` and `server/ai/tests/`

The main thing missing is infrastructure shape, not core gameplay backend logic.

Right now, one Node process is acting as:

- web API
- socket gateway
- lobby service
- queue service
- match coordinator
- live authoritative match host
- reconnect authority

That is acceptable for development and small closed testing, but it is not the final runtime boundary for scalable ranked play.

## Current Readiness

### What Already Exists

- Account auth and JWT-based identity.
- Ratings table, rating history, and Glicko-style rating updates.
- Match records and match player records.
- Queue bucket logic with ranked and casual buckets.
- Reconnect tokens and reconnect seat reclamation.
- Disconnect grace with forfeit or AI takeover handling.
- Match start and match end persistence boundaries.
- Admin visibility into queue and match state.

### What Is Still Coupled

- Queue state is in process memory.
- Live match state is in process memory.
- Reconnect grace state is in process memory.
- Socket ownership and session ownership assume a single process.
- Queue formation directly launches matches in the same server runtime.
- API traffic and live match ticks compete for the same process budget.

## Honest Distance Estimate

### Ranked Alpha On One Region

Estimated distance: close.

If the goal is:

- one region
- modest concurrency
- one backend deployment
- one Postgres instance
- reconnect grace
- ranked queue and rating updates

then you are roughly 60-75% of the way there on backend functionality.

What is left for a serious ranked alpha is mostly:

- deployment packaging
- environment hardening
- process role separation
- idempotent match finalization review
- reconnect flow hardening
- operational visibility

Estimated effort:

- 3 to 5 focused days for a rough but usable single-region hosted alpha
- 1 to 2 focused weeks for a cleaner ranked alpha with good observability and failure handling

### Disposable Match Worker Architecture

If the goal is:

- one service for platform/API/lobby/matchmaking
- separate disposable gameplay match workers
- reconnect routed through a stable control service
- cleaner scale path for many concurrent matches

then you are not blocked by gameplay code, but you do need an architectural split.

Estimated effort:

- 2 to 4 weeks for a first solid version if we keep the current Node authoritative sim
- longer if you also want autoscaling, multi-region, or managed fleet orchestration in the same pass

## Recommended Target Architecture

Build toward two runtime roles, not three independent products.

### 1. Platform Service

Responsibilities:

- auth
- parties
- private lobbies
- public queue
- match formation
- rank/MMR reads and writes
- player profile and progression APIs
- reconnect routing metadata
- match server allocation

Suggested stack:

- Node/Express/Socket.IO
- Postgres for durable data
- Redis for ephemeral queue and reconnect state once you leave single-process mode

### 2. Match Worker

Responsibilities:

- host one or more authoritative live matches
- own in-memory sim state for active matches
- emit snapshots
- enforce disconnect grace during the match
- produce authoritative final result
- report result back to the platform service

Suggested stack:

- Node process running the existing authoritative sim
- no direct client-facing business APIs beyond match socket traffic

### 3. Durable Storage

Responsibilities:

- players
- refresh tokens
- ratings
- match records
- match players
- season data
- loadouts
- bans

Suggested stack:

- Postgres

### 4. Ephemeral Coordination

Responsibilities:

- queue tickets
- party presence
- reconnect routing metadata
- short-lived match allocation records
- optional rate-limit counters if multi-node

Suggested stack:

- Redis or Valkey

## What Not To Build Yet

Do not build all of this right now:

- microservices for every concern
- Kafka or a large event bus
- multi-region match placement
- replay infrastructure
- distributed simulation
- database-backed real-time gameplay

Those are future scale tools, not today's unblockers.

## Recommended Delivery Phases

## Phase 0: Ship-Ready Single Process Alpha

Goal:

Use the existing server shape, but harden it enough to run a real ranked alpha in one region.

Scope:

- keep one Node process for API, sockets, queue, and match hosting
- keep Postgres as durable storage
- keep in-memory queue and reconnect state for now
- add deployment, health checks, and structured operational logging
- verify match finalization and rating updates are idempotent
- verify reconnect edge cases around duplicate sockets, expired grace, and stale seat ownership
- define resource budgets for max simultaneous matches per process

Deliverables:

- deployable backend container
- production env var contract
- `/health` and readiness endpoints
- documented single-region deployment
- test pass for ranked queue, reconnect, match end, and rating update flows

Exit criteria:

- players can queue into ranked
- players can disconnect and reconnect during grace
- completed matches always persist results once
- ratings update once per completed ranked match

Estimated effort:

- 3 to 5 days

## Phase 1: Split Platform Service From Match Worker

Goal:

Stop letting one process be both website/backend and gameplay host.

Scope:

- introduce a platform service process
- introduce a match worker process using the current sim runtime
- define a match allocation contract
- move queue ownership to the platform service
- move live match ownership to workers
- move reconnect metadata out of local memory into Redis
- persist enough routing data for platform-assisted reconnect

Deliverables:

- `platform` runtime role
- `match-worker` runtime role
- worker registration and capacity reporting
- allocation record for `matchId -> worker endpoint`
- reconnect routing record for `playerId -> active match assignment`

Exit criteria:

- queue service can launch matches on workers
- active matches survive API restarts
- reconnect no longer depends on the original API process holding local memory

Estimated effort:

- 1 to 2 weeks after Phase 0

## Phase 2: Managed Match Hosting

Goal:

Make workers disposable and easier to scale.

Scope options:

- container workers on ECS or similar
- GameLift managed game server hosting
- Agones if you want Kubernetes-style control

Deliverables:

- automated worker deployment
- worker health reporting
- match placement into available worker capacity
- graceful drain for deploys

Exit criteria:

- new matches allocate without manual intervention
- deploys do not kill active matches
- worker failure handling is explicit and observable

Estimated effort:

- 1 to 2 additional weeks for a first clean version

## Gaps To Close In The Current Codebase

### 1. Process Boundary Assumptions

Current owner files:

- `server/index.js`
- `server/state/runtimeState.js`
- `server/socket/registerHandlers.js`

Gap:

These files assume queue state, socket state, reconnect state, and live match state all live in the same process.

Needed change:

Introduce explicit ownership boundaries:

- platform owns queue and party state
- worker owns live match state

### 2. Reconnect Depends On Local Memory

Current owner files:

- `server/socket/helpers.js`
- `server/socket/registerHandlers.js`
- `server/game/multilaneRuntime.js`

Gap:

Reconnect is well-designed for a single process, but the authoritative grace state is stored in `disconnectGrace` memory.

Needed change:

Move reconnect routing metadata to Redis once platform and worker are split.

### 3. Queue Formation Is In-Process

Current owner files:

- `server/services/matchQueueManager.js`
- `server/socket/registerHandlers.js`

Gap:

Queue formation directly triggers room creation in the same runtime.

Needed change:

Replace direct launch with allocation:

- queue forms a match
- platform asks for worker capacity
- platform assigns players to worker
- players receive worker connection info

### 4. Match Finalization Needs Explicit Idempotency Review

Current owner files:

- `server/game/matchPersistence.js`
- `server/services/rating.js`
- `server/game/multilaneRuntime.js`

Gap:

The current transaction boundaries are good, but match completion should be explicitly protected against duplicate end processing across retries or restarts.

Needed change:

- add a finalization guard keyed by match id
- ensure ratings and rewards cannot double-apply

### 5. No Declared Capacity Model

Current owner files:

- `server/index.js`
- `server/game/multilaneRuntime.js`
- `server/sim-multilane.js`

Gap:

The repo does not yet define how many active matches or sockets one process is expected to hold.

Needed change:

- set target budget per worker
- measure CPU and memory by match count
- enforce match capacity caps

## Minimum Scope I Recommend Right Now

If the goal is to make this happen soon without overbuilding, do this:

1. Ship Phase 0 first.
2. Keep one region.
3. Keep one Postgres instance.
4. Add Redis only when splitting platform and worker.
5. Split platform and match workers before trying fancy autoscaling.

That gives you the fastest route to:

- ranked matches
- reconnect rules
- persistent ratings
- scalable-enough architecture for the next stage

## Suggested Work Breakdown

### Workstream A: Runtime Hardening

- add health and readiness endpoints
- add shutdown and drain behavior
- add process-level match capacity limits
- make match finalization explicitly idempotent

### Workstream B: Queue And Ranked Lifecycle

- document queue buckets and matchmaking rules
- verify placement/rating edge cases
- confirm abandoned, forfeited, and disconnected match outcomes
- ensure admin and analytics visibility into queue and live match states

### Workstream C: Reconnect Hardening

- test reconnect token expiry
- test stale socket seat reclaim
- test duplicate reconnect attempts
- define whether reconnect returns to the same socket gateway or is platform-routed

### Workstream D: Deployment

- containerize backend cleanly
- define staging and production env sets
- deploy Postgres-backed staging
- add log aggregation and basic metrics

### Workstream E: Worker Split

- extract worker startup entrypoint
- define worker registration heartbeat
- define allocation contract
- externalize reconnect routing metadata

## Recommended Order

1. Harden the current single-process backend.
2. Prove ranked alpha in one region.
3. Split platform and match workers.
4. Move ephemeral coordination to Redis.
5. Add managed worker hosting.

## Bottom Line

You are close to a ranked multiplayer alpha.

You are not close to zero.

The shortest practical route is:

- keep the current Node authoritative sim
- harden the current process for a single-region alpha
- then split it into platform service plus match workers

Do not rewrite the sim.
Do not move gameplay into the database.
Do not jump to a giant microservice architecture.

The next real milestone is not "buy more tech."

It is:

"turn the current backend into a shippable ranked alpha, then separate match hosting from platform responsibilities."
