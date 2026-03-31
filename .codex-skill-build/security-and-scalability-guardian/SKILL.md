---
name: security-and-scalability-guardian
description: Enforce server-authoritative security, anti-cheat protections, and scalable architecture in Ransom Forge. Use when reviewing or changing gameplay systems, networking, snapshots, action validation, resource flow, spawn/combat/movement authority, or any code path that could introduce client trust, exploit vectors, denial-of-service risk, or poor scaling under higher unit counts.
---

# Security And Scalability Guardian

Protect one rule: the client is never trusted for gameplay-critical state.

Treat every player-facing action as hostile until the server validates it against game rules, ownership, timing, and lane/state constraints. Reject invalid or unverifiable actions instead of correcting them quietly.

## Review Workflow

1. Identify the gameplay-critical state in the system under review.
2. Map who owns that state, who may request changes, and who may finalize changes.
3. Trace every client input to the server validation path that constrains it.
4. Check whether invalid requests are rejected loudly instead of ignored or auto-corrected.
5. Check whether the code can scale under larger unit counts, higher message rates, or repeated malicious inputs.
6. List concrete exploit paths before recommending fixes.

Start every review with these questions:

- What can the client request here?
- What must the server verify before accepting it?
- What prevents repeated or impossible requests?
- What happens when the request is malformed, late, duplicated, or malicious?

## Server Authority Checks

Require server authority over:

- movement
- lane assignment
- combat resolution
- damage calculation
- spawning
- unit state transitions such as Attack, Defend, and Retreat
- formation anchors
- resource changes
- tech tree progression

Flag any system where the client decides, finalizes, or locally overrides gameplay-critical outcomes.

## Client Trust Violations

Flag any path where client input is accepted without validation, sanitization, or rule checks.

Common violations:

- client position accepted directly
- client damage applied directly
- client spawn request executed without server-side eligibility checks
- client-selected lane accepted without routing validation
- client cooldown, timer, or attack-rate values used as source of truth
- client-authored resource totals, unlocks, health, or unit stats reused by the server

Require the server to validate:

- ownership and permissions
- allowed action type
- timing and cooldown legality
- lane, route, and state-machine legality
- unit/building/resource prerequisites
- bounds, ranges, and stat-derived limits

## Anti-Cheat Checks

Look for protection against:

- speed hacks
- attack-rate manipulation
- cooldown bypass
- resource injection
- invalid or duplicate spawn requests
- invalid lane transitions
- impossible positioning
- combat actions issued during invalid states such as Retreat
- replayed, reordered, or spammed requests

Validate each action against:

- authoritative unit stats
- current lane and route membership
- unit state machine rules
- build, spawn, and progression prerequisites
- server time or tick ownership

## Fail Loudly

Do not permit invalid gameplay state to continue quietly.

Flag:

- silent correction of invalid requests
- fallback behavior that masks exploit attempts
- ignored errors
- log-only rejection paths that still let simulation continue

Require:

- explicit rejection of invalid actions
- clear logging with actor id, unit id, lane id, action, and violated rule
- hard failures when architecture invariants are broken

Prefer rejection over correction. Prefer server-owned truth over graceful degradation.

## Data Integrity Checks

Verify that:

- critical state is stored and finalized on the server
- duplicated state has a clear source of truth
- client copies are treated as presentation only
- health, stats, resources, unlocks, lane state, and formation state cannot be authored by the client
- desync-prone logic is not split across server and client in incompatible ways

Treat hidden state duplication as a security risk when it can drift or be exploited.

## Networking Safety

Flag:

- exposed debug or cheat endpoints
- insecure admin or developer commands
- unbounded RPC or message handlers
- missing authentication or session checks
- missing rate limiting or flood protection
- handlers that deserialize or apply arbitrary payload fields
- commands that trust caller-provided ids without ownership checks

Require layered validation at message boundaries before the request reaches gameplay logic.

## Scalability Checks

Assume the system must survive beyond local test scale.

Flag:

- per-unit expensive work every frame or tick
- O(n^2) combat, aggro, or proximity scans without hard bounds
- repeated full-entity searches
- memory growth without cleanup
- excessive instantiate/destroy churn instead of pooling
- tight coupling that forces unrelated systems to update together
- request handling that scales linearly with malicious spam

Prefer:

- batching
- pooling
- event-driven updates
- lane-based grouping
- bounded search radii
- cached lookups and indexed collections
- explicit rate caps and backpressure

## Exploit Surface Prompts

For each system, ask:

- How could a malicious player force an impossible state?
- What happens if the same request is sent many times?
- What happens if timing is manipulated or packets are replayed?
- What happens if ids, positions, resources, or lane choices are forged?
- What happens if required data is missing, stale, or intentionally malformed?
- What is the most expensive valid-looking request an attacker could spam?

List concrete exploit vectors, not vague suspicion.

## Ransom Forge Focus

Apply extra scrutiny to systems that can bypass:

- server-authoritative lane movement
- Attack, Defend, and Retreat enforcement
- formation anchor integrity
- reinforcement merging rules
- Town Core-based routing
- current tech tree gating

Any path that allows a unit to move, fight, spawn, unlock, or merge outside these rules is a security issue.

## Output Format

Use this structure when reviewing a system:

- System:
- Security Risk Level:
- Client Trust Issues:
- Server Authority Violations:
- Anti-Cheat Gaps:
- Data Integrity Risks:
- Networking Risks:
- Scalability Risks:
- Possible Exploits:
- Recommended Fix:
- Priority:
- Notes:

Keep findings concrete. Name exact files, code paths, messages, or state transitions whenever possible.

## Decision Rule

If the system can be cheated, fix it.

If the system cannot be validated, do not trust it.
