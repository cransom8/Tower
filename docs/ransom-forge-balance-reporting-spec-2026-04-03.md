# Ransom Forge Balance Reporting Spec

## Goal

Define the telemetry, derived metrics, report outputs, and durable data contract required to balance Ransom Forge from repeated playtests using evidence instead of feel.

This spec is for the live repo shape as of 2026-04-03:

- authoritative simulation lives in `server/sim-multilane.js` plus `server/game/multilane/*`
- match lifecycle lives in `server/game/multilaneRuntime.js`
- match persistence lives in `server/game/matchPersistence.js`
- durable config versions live in `server/migrations/013_game_configs.sql`
- compact match analytics already have a storage seam in `matches.combat_log` and `matches.wave_stats`

The reporting framework must answer, after every test match:

- Was the early game too hard?
- At what point did the player stabilize?
- At what point did the player begin outscaling the dungeon?
- Was the player short on resources early and overflowing with resources later?
- Were units too cheap, too expensive, or correctly priced?
- Were upgrades overtaking wave scaling too quickly?
- Which systems created the largest power spikes?
- Was the challenge curve smooth, or did it collapse into an easy snowball?

## Design Thesis

The per-wave report is the canonical balance dataset.

Everything else must derive from it:

- match AAR summaries
- balance diagnosis
- auto-detected flags
- economy and army deep dives
- timeline and power-curve views
- cross-match and cross-patch comparisons

The system must stay faithful to the current server-authoritative architecture:

- aggregate telemetry in server memory during the match
- close wave records at wave boundaries
- close match summaries at match end
- persist compact summaries and derived diagnostics
- do not write per-tick or per-hit telemetry to Postgres

## Reporting Grains

Ransom Forge is a lane-based server-authoritative game, so reports must exist at distinct grains:

1. Event grain
   Raw in-memory telemetry counters and boundary events used to assemble reports.
2. Unit lifetime grain
   Per-unit rollups for damage, healing, survival, kills, and gold efficiency.
3. Lane-wave grain
   The authoritative balancing grain.
4. Match grain
   Team aggregate plus by-lane breakdown when multiple human lanes exist.
5. Multi-match grain
   Aggregated rows grouped by build, map, config version, patch, and date window.

If a mode has multiple player-controlled lanes, every report must support:

- `teamAggregate`
- `byLane`

Single-player playtests may surface only the aggregate view, but the schema must still allow lane breakdowns.

## Non-Negotiable Architecture Rules

- Do not put telemetry writes on the sim tick hot path.
- Do not depend on the database to answer live gameplay questions.
- Do not persist every combat action, projectile update, or unit position.
- Do persist wave, phase, and match boundary summaries.
- Do version every report with both gameplay config version and telemetry schema version.
- Do keep all formulas and thresholds explainable and stable enough to compare across patches.

## Required Match Identity and Versioning

Every durable report object must carry:

- `matchId`
- `roomId`
- `mode`
- `mapId` or authored environment id
- `playerCount`
- `humanPlayerCount`
- `aiConfiguration`
- `laneCount`
- `startedAt`
- `endedAt`
- `matchDurationSeconds`
- `serverBuildId`
- `clientBuildId`
- `contentManifestHash` or equivalent content version id
- `gameConfigId`
- `gameConfigVersion`
- `gameConfigLabel`
- `telemetrySchemaVersion`
- `balanceModelVersion`
- `seed` when the mode uses deterministic authored seeds

Without these fields, patch comparison and multi-match balance analysis will be ambiguous.

## Required Authored Balance Metadata

Some questions in this spec cannot be answered purely from raw outcomes. The active gameplay config must therefore provide authored balance metadata alongside wave and roster data.

Required metadata:

- `phaseBands`
  Example: opening, mid, late boundaries by wave number.
- `waveDifficultyTargets`
  Per-wave intended power, pressure band, and target clear time.
- `economyTargets`
  Expected gold scarcity and overflow bands by phase.
- `minimumSurvivalBasket`
  The minimum opener package considered fair for the mode.
- `flagThresholds`
  Config-driven thresholds for auto-detected flags.
- `powerModelWeights`
  Weights used by the effective-power calculation.
- `metricWeights`
  Weights used by struggle, pressure, stabilization, and efficiency scores.

If the config does not explicitly supply these values, the system may use documented defaults, but the report must mark the output as using fallback balance thresholds.

## Canonical Classification Layers

### Unit Families

The reporting layer should normalize unit data into stable balance buckets using current fort catalog families and user-facing aliases:

- `infantry`
- `shield`
- `spear`
- `archer`
- `mage`
- `priest_healer`
- `cavalry`
- `economy`
- `hero`
- `elite_special`

Repo-specific mapping guidance:

- `family=infantry` -> `infantry`
- `family=shield` -> `shield`
- `family=polearm` -> `spear`
- `family=ranged` -> `archer`
- `family=arcane` -> `mage`
- `family=support` -> `priest_healer`
- mounted combat units or stable-produced units -> `cavalry`
- `family=economy` -> `economy`
- `family=hero` or authored elite markers -> `hero` or `elite_special`

### Building and Upgrade Paths

The reporting layer must normalize building paths by canonical internal key and player-facing label:

- `town_core` -> Town Core
- `barracks` -> Barracks
- `blacksmith` -> Blacksmith
- `archery_tower` -> Archery
- `wizard_tower` -> Mage Tower
- `temple` -> Temple
- `market` -> Market
- `stable` -> Stables
- `workshop` -> Workshop when enabled later

Reports must preserve the internal key for machine-readable comparison and show the player-facing label in human-readable output.

## Required Telemetry Capture

The following events or counters must exist in memory during the match so reports can be assembled without reconstructing behavior from guesswork.

### Wave Lifecycle

- wave scheduled
- wave start
- first enemy contact
- first breach
- first core-range threat
- wave cleared
- defeat or match termination

### Economy Events

- gold gained with source classification
- gold spent with sink classification
- peak held gold
- trough held gold
- affordability snapshots at wave start and wave end

### Unit Lifecycle Events

- unit purchased
- unit spawned
- unit died
- unit leaked past frontline
- unit damage dealt
- unit damage taken
- unit healing done
- unit mitigation or shielding provided
- unit time alive
- unit arrival time after purchase or spawn

### Structure and Pressure Events

- wall damage by segment and aggregate
- gate damage
- tower damage
- town core damage
- breach pressure start and end
- enemy inside fortress bounds start and end
- frontline empty start and end

### Building and Upgrade Events

- building constructed
- building upgraded
- upgrade purchased
- hero unlocked or summoned
- market contract or economy unit purchased

## Durable Data Contract

The minimum durable reporting contract should be:

- `matches.wave_stats`
  Canonical array of per-wave report objects.
- `matches.combat_log`
  Compact event timeline for explainability, not raw replay.
- match-level balance summary
  Either a new JSONB field on `matches` or a derived/cached object built at match finalization.
- multi-match trend exports
  Flat datasets derived from durable per-wave and per-match rows.

Recommended durable additions:

- `matches.game_config_id`
- `matches.game_config_version`
- `matches.server_build_id`
- `matches.client_build_id`
- `matches.content_manifest_hash`
- `matches.balance_summary`
- `matches.balance_flags`

The exact storage form can vary, but the outputs defined below are required.

## Report Layers

## 1. Per-Wave Report

The per-wave report is the source of truth for balance tuning.

Each wave report must capture:

- the exact state at wave start
- what changed during the wave
- the exact state at wave end
- the intended target band for that wave
- the derived balance readings for that wave

### Required Per-Wave Fields

#### Wave Identity

- wave number
- match id
- lane id or team aggregate id
- phase band
- elapsed match time at wave start
- elapsed match time at wave end
- total wave duration
- time to first contact
- time to first breach, if any
- time to clear the wave
- whether the wave was fully cleared or ended in defeat
- authored intended wave power
- authored intended pressure band
- authored target clear-time band

#### Economy During the Wave

- gold at wave start
- gold earned during the wave
- gold spent during the wave
- gold at wave end
- highest gold held during the wave
- lowest gold held during the wave
- income source breakdown
  - passive income
  - kill rewards
  - market or trader income
  - bonus or event income
  - refunds if any
- spending breakdown
  - units purchased
  - buildings purchased
  - upgrades purchased
  - hero summons
  - repairs
  - other spending
- affordable units at wave start by family and cheapest unlocked combat option
- affordable upgrades at wave start by building path
- survival tradeoff status
  - could buy minimum survival basket
  - could buy a long-term upgrade instead
  - could buy both

#### Army State During the Wave

- units alive at wave start
- units purchased during the wave
- units spawned during the wave
- units lost during the wave
- units alive at wave end
- total alive army value at wave start
- total alive army value at wave end
- total army value purchased during the wave
- total army value lost during the wave
- peak unit count during the wave
- peak alive army value during the wave

#### Army Composition During the Wave

- alive shield units at start and end
- alive infantry or sword units at start and end
- alive spear units at start and end
- alive archers at start and end
- alive mages at start and end
- alive priests or healers at start and end
- alive cavalry or stable units at start and end
- alive economy units at start and end
- alive hero or elite categories at start and end
- alive share of army value by family
- damage share by family
- survivability share by family

#### Combat Results During the Wave

- enemies spawned
- enemies killed
- enemies leaked past frontline
- max enemies alive at once
- total player damage dealt
- total enemy damage dealt
- wall damage taken
- gate damage taken
- tower damage taken
- town core damage taken
- healing done by player units
- shielding or mitigation done
- total kills by player unit family

#### Pressure and Survivability

- whether walls were breached
- whether any enemy reached core range
- whether the player entered near-loss state
- seconds under breach pressure
- seconds enemies were inside fortress bounds
- seconds no frontline units were alive
- seconds core perimeter was contested
- frontline survival duration
- backline survival duration
- recovery after breach duration

#### Purchasing and Response Behavior

- exact purchase list during the wave
- purchase timestamp relative to breach pressure
- proactive or reactive spending classification
- unspent gold at wave end
- response time from breach to emergency purchase
- response time from frontline collapse to replacement arrival

#### Derived Wave Metrics

- struggle score
- economic efficiency score
- army efficiency score
- unit loss ratio
- gold float score
- pressure score
- stabilization score
- recovery score
- player effective power
- enemy wave effective power
- player-to-enemy power ratio
- overperformed or underperformed versus intended difficulty

### Per-Wave Report Requirements

- Every field must be reproducible from authoritative server-owned counters plus authored config metadata.
- The same wave object must support both human-readable and machine-readable outputs.
- No later aggregation step may invent values that were not captured or derived from this wave object.

## 2. Match Summary / After Action Report

Every completed match must generate one readable AAR plus one machine-readable match summary object.

### Match Identity

- match id
- mode
- map
- player count
- AI or enemy configuration
- final result: win or loss
- final wave reached
- total match duration
- config version and build ids

### Economy Summary

- total gold earned
- total gold spent
- total gold unspent at match end
- peak floating gold during the match
- average gold earned per wave
- average gold spent per wave
- percentage of earned gold actually spent
- percentage of waves ending above configured excess-gold thresholds
- first wave where gold scarcity ends
- first wave where overflow becomes consistent

### Army Summary

- total units purchased
- total units spawned
- total units lost
- peak unit count
- average unit count across the match
- peak alive army value
- average alive army value
- total army value lost
- first wave where losses stop materially slowing recovery

### Combat Summary

- average wave clear time
- slowest clear time
- fastest clear time
- total wall damage taken
- total gate damage taken
- total tower damage taken
- total town core damage taken
- number of waves with breach
- number of waves with near-loss pressure
- number of waves with zero meaningful pressure

### Upgrade Summary

- total upgrade gold spent
- upgrade spending by building
- first purchase time and wave for each building path
- number of purchases in each upgrade category
- estimated power gained from upgrades
- observed challenge reduction after upgrades
- dominant upgrade tree

### Balance Summary Conclusions

- early game difficulty rating
- mid game difficulty rating
- late game difficulty rating
- snowball risk rating
- economy overflow rating
- upgrade pacing rating
- unit affordability rating
- enemy scaling adequacy rating
- stabilization wave
- snowball onset wave
- strongest power spike source
- top likely tuning targets

## 3. Auto-Detected Balance Flags

The system must emit explicit, queryable balance flags so repeated matches can be triaged without hand-reading every log.

Each flag row must include:

- `flagType`
- `severity`
- `matchId`
- `waveStart`
- `waveEnd`
- `evidence`
- `triggerValues`
- `configVersion`
- `telemetrySchemaVersion`

### Required Flags and Default Detection Rules

| Flag | Default detection rule |
| --- | --- |
| Early game too hard | Opening phase average struggle score >= 65 and at least one of: 2 or more near-loss waves, opening wall/core damage above target band, or minimum survival basket not affordable on wave 1 |
| Early game too easy | Opening phase average struggle score <= 25, no breach pressure, low loss ratio, and end-of-wave gold already above opening overflow target for 2 or more opening waves |
| Mid game collapse | After a previously pressured phase, a 3-wave window drops to very low pressure and fast clears while player power ratio rises sharply |
| Snowball detected | 3 consecutive waves with rising player-to-enemy power ratio, falling struggle, and rising gold float |
| Economy overflow | 3 consecutive waves ending above excess-gold threshold while struggle and pressure stay low |
| Unit spam enabled | Unit count or army value per 100 gold spent grows above target band and threat does not keep up |
| Upgrade spike too strong | A single upgrade purchase is followed by a sustained 3-wave struggle drop larger than the configured spike threshold |
| Wave scaling too weak | Enemy effective power keeps rising slower than player effective power over a sustained window and actual pressure stays flat or drops |
| Wave scaling too sharp | A wave's pressure or struggle exceeds both adjacent waves by more than the discontinuity threshold |
| No meaningful decision pressure | For several consecutive waves the player can afford both minimum survival spending and at least one long-term upgrade with low pressure and low losses |
| Forced starvation | Opening waves repeatedly leave the player unable to afford the minimum survival basket or the minimum recovery basket |
| Recovery impossible | After a setback wave, the player fails to restore stable frontline, army value, or affordability within the configured recovery window |
| Recovery too easy | After a major setback, the player returns to pre-loss power and low pressure within the same wave or immediately next wave with minimal economic penalty |

Flags should use config-driven thresholds, but the defaults above must exist.

## 4. Economy Reports

Economy reports exist at both per-wave and per-match scope.

### Starting Economy Report

- starting gold
- gold available before first wave
- spending options available from starting gold
- number of units affordable at start
- number of frontline units affordable at start
- whether starting gold supports the minimum survival basket
- whether starting gold forces a narrow opener

### Income Ramp Report

- gold earned by wave
- income growth rate by wave
- cumulative gold earned by wave
- economy power compared to enemy power by wave
- market or trader contribution by wave

### Gold Usage Report

- total gold spent on units
- total gold spent on buildings
- total gold spent on upgrades
- total gold spent on repairs
- total gold spent on optional purchases
- percentage allocation by category

### Gold Float Report

- unspent gold per wave
- max unspent gold in match
- average unspent gold
- waves exceeding configured float thresholds
- early starvation to late flood transition wave

### Affordability Pressure Report

- how many units were affordable at the start of each wave
- how many upgrades were affordable at the start of each wave
- whether the player had to choose between survival spending and long-term scaling
- whether the player could buy both freely

## 5. Army Composition and Performance Reports

### Army Composition Report

For every wave and for match totals:

- units alive by type
- units purchased by type
- units lost by type
- share of total army value by type
- share of total damage by type
- share of total survivability by type

### Unit Efficiency Report

Per unit type:

- total gold invested
- total units fielded
- total lifetime damage dealt
- total lifetime damage absorbed
- total healing performed
- total mitigation or shielding provided
- total kills
- average survival time
- gold efficiency score
- combat efficiency score

The report must make it obvious whether:

- cheap units are overperforming
- expensive units are underperforming
- ranged units are too safe
- melee units are too disposable
- support units are mandatory or ignorable

### Army Scaling Report

- army value by wave
- army count by wave
- army value gained per 100 gold spent
- army value lost per wave
- ratio of permanent scaling versus expendable scaling

## 6. Combat Pressure and Survivability Reports

### Pressure Report

Per wave and per match:

- breach occurrence
- seconds under breach pressure
- enemies inside wall perimeter
- enemies reaching core perimeter
- time spent without sufficient frontline
- total structural damage taken

### Survivability Report

- frontline survival duration
- backline survival duration
- average unit lifetime by class
- wall survival contribution
- heal throughput
- mitigation throughput
- recovery after breach

### Threat Response Report

- time from enemy contact to first player unit death
- time from breach to player response purchase
- time from frontline collapse to replacement units arriving
- whether reinforcements arrived in time to matter

## 7. Upgrade Pacing Reports

### Upgrade Timing Report

For each upgrade category:

- first purchase wave
- first purchase match time
- total number of purchases
- total gold invested
- average interval between purchases

### Upgrade Power Spike Report

After each upgrade purchase:

- player power before purchase
- player power after purchase
- next three wave struggle scores
- next three wave clear times
- next three wave loss ratios
- next three wave pressure scores
- whether the upgrade correlates with a large drop in challenge

The report must store both:

- `estimatedPowerGain`
- `observedPowerGain`

### Upgrade Dominance Report

- which building upgrade trees received most investment
- which upgrade trees produced best results
- whether one tree dominates all others
- whether a neglected path is underpowered

### Unlock Pacing Report

- wave number when each building became available
- wave number when each building was built
- wave number when each upgrade category first mattered
- whether unlock timing came too early, too late, or never mattered

## 8. Wave Difficulty Reports

### Wave Strength Report

For each wave:

- total enemy count
- total enemy HP pool
- total enemy damage output potential
- total enemy armor or mitigation
- movement pressure
- siege pressure
- special unit pressure
- ranged pressure
- healing or support pressure
- intended wave power

### Wave Performance Report

- actual time to defeat wave
- actual player losses against the wave
- actual structural damage caused by the wave
- whether the wave overperformed or underperformed relative to intended difficulty

### Wave Curve Report

- expected wave power by wave number
- actual perceived pressure by wave number
- discontinuity warnings when one wave spikes or dips too sharply compared to adjacent waves

## 9. Timeline / Power Curve Reports

### Power Curve Report

Track by wave:

- player army value
- player upgrade value
- player total effective power
- enemy wave effective power
- player-to-enemy power ratio

### Stabilization Report

Track:

- first wave where player consistently survives without meaningful wall damage
- first wave where clear times begin dropping instead of rising
- first wave where player ends with excess gold consistently
- first wave where losses stop mattering

This marks the moment the player turns the corner.

### Snowball Onset Report

Track:

- first wave where player power ratio exceeds the snowball threshold
- first wave where losses trend downward for consecutive waves
- first wave where unspent gold trends upward for consecutive waves
- first wave where breach risk effectively disappears

This marks the onset of runaway advantage.

## 10. Comparative Multi-Match Reports

Single-match reports are useful but not sufficient for tuning.

### Multi-Match Summary Report

Across a selected sample window:

- average final wave reached
- win rate
- average stabilization wave
- average snowball onset wave
- average early-game loss rate
- average wall damage in opening waves
- average peak gold float
- average unit count by wave

### Build Comparison Report

Compare:

- unit-heavy builds
- upgrade-heavy builds
- balanced builds
- branch-specialist builds
- economy-heavy builds

Measure:

- survival consistency
- clear speed
- gold efficiency
- pressure resistance
- late-game dominance

### Patch Comparison Report

Compare old patch versus new patch on:

- early-game survival
- stabilization timing
- economy overflow
- snowball rate
- average difficulty curve
- dominant upgrade tree
- unit affordability

Every comparison must be filterable by:

- config version
- build id
- content hash
- map
- date range
- player count
- build classification

## Required Derived Metrics

All derived metrics should be stored on a 0-100 scale unless otherwise noted.

The exact weights must be config-driven and versioned in `balanceModelVersion`.

### Struggle Score

Represents how hard the player had to work to survive a wave.

Required inputs:

- unit loss ratio
- wall and core damage ratio
- breach duration ratio
- clear time versus target
- near-loss state

Behavior:

- 0 means trivial
- 100 means extreme survival stress or likely failure

### Gold Float Score

Represents excess unspent economy.

Required inputs:

- end-of-wave gold
- peak held gold
- affordability counts
- number of meaningful sinks currently available

Behavior:

- low when the player is cash-poor
- high when the player has more gold than useful decisions

### Army Efficiency Score

Represents combat value returned from army spend.

Required inputs:

- total army value
- lifetime damage
- survival time
- kills
- mitigation
- healing

Behavior:

- higher means army investment is returning too much combat value
- very low means units are overpriced or ineffective

### Upgrade Efficiency Score

Represents challenge reduction gained per gold spent on upgrades.

Required inputs:

- gold spent on upgrade
- observed struggle delta across next three waves
- observed pressure delta
- observed clear-time delta
- estimated power gain

### Pressure Score

Represents how threatening a wave actually was in practice.

Required inputs:

- breach occurrence
- breach duration
- enemies inside fortress
- frontline downtime
- structural damage
- core threat presence

### Stabilization Score

Represents how secure the player has become over time.

Required inputs:

- pressure score trend
- struggle trend
- clear-time trend
- gold overflow trend
- loss ratio trend

Behavior:

- low when the run is unstable
- high when the run is comfortably under control

### Power Ratio

Represents player total effective power versus enemy wave power.

Formula:

- `power_ratio = player_total_effective_power / max(enemy_wave_effective_power, epsilon)`

Required outputs:

- raw ratio
- normalized band
- moving average over 3 waves

### Recovery Score

Represents whether the player recovers from losses in a healthy way.

Required inputs:

- setback severity
- waves to restore frontline
- waves to restore affordability
- waves to restore army value
- post-setback struggle trend

Behavior:

- low when recovery is impossible
- mid or high when recovery is achievable but not free
- separate flags should catch "too easy" and "impossible"

## Effective Power Model

The effective-power model must be shared across player and enemy reporting so the power race is interpretable.

Required components:

- combat HP pool value
- DPS value
- range safety value
- healing throughput value
- mitigation value
- structure defense value
- economy throughput proxy
- hero or elite modifier

Recommended model:

- `player_total_effective_power = army_power + upgrade_power + fortress_defense_power + economy_power_proxy`
- `enemy_wave_effective_power = enemy_hp_power + enemy_dps_power + siege_pressure_power + support_pressure_power + special_pressure_power`

The report must store:

- the final computed values
- the component breakdown
- the weight set version used to compute them

## Canonical Heuristics

To keep reports comparable, the framework must define a few canonical transition heuristics.

### Stabilization Wave

The stabilization wave is the first wave in a 3-wave window where all are true:

- struggle score is below the stabilization threshold
- pressure score is below the stabilization threshold
- wall/core damage is below the stabilization target
- clear time is at or below target

### Snowball Onset Wave

The snowball onset wave is the first wave in a 3-wave window where all are true:

- power ratio is above the snowball threshold
- gold float score is rising
- struggle score is falling
- breach risk effectively disappears

### Meaningful Pressure

A wave has meaningful pressure if any of the following are true:

- pressure score exceeds the configured floor
- breach or core-range threat occurs
- structural damage exceeds configured low-pressure tolerance
- unit loss ratio exceeds low-pressure tolerance

## Output Formats

The reporting system must generate four review-friendly outputs.

## Format 1: Per-Wave Readable Log

Human-readable wave-by-wave breakdown showing:

- economy
- army
- combat
- pressure
- purchases
- derived ratings
- target-vs-actual read

Recommended section order per wave:

1. identity and result
2. economy
3. army state and losses
4. pressure and survivability
5. purchases and reactions
6. derived metrics
7. tuning notes

## Format 2: Match AAR Summary

Compact human-readable report containing:

- totals
- averages
- difficulty ratings by phase
- stabilization wave
- snowball onset wave
- strongest spike sources
- top flags
- likely tuning targets

## Format 3: Balance Diagnosis Section

Short conclusion block with:

- early game too hard / acceptable / too easy
- mid game too hard / acceptable / too easy
- late game too hard / acceptable / too easy
- snowball detected yes or no
- economy overflow yes or no
- likely tuning targets

## Format 4: Trend Dataset

Machine-readable export for repeated comparison over time.

Required exports:

- `balance_wave_rows`
  One row per wave.
- `balance_match_rows`
  One row per match.
- `balance_flag_rows`
  One row per triggered flag.
- `balance_upgrade_spike_rows`
  One row per upgrade purchase event.
- `balance_comparison_rows`
  Aggregated comparison output by config, build, or strategy filter.

Recommended formats:

- JSONL for fidelity
- CSV for spreadsheet work
- SQL view or materialized view for admin analytics

## Machine-Readable Schema Expectations

### `balance_wave_rows`

Must include:

- identity fields
- config and build version fields
- economy fields
- army fields
- combat fields
- pressure fields
- all derived metrics
- flag booleans relevant to the wave

### `balance_match_rows`

Must include:

- match identity
- result
- final wave
- totals and averages
- stabilization wave
- snowball wave
- diagnosis ratings
- strategy classification
- top flags

### `balance_flag_rows`

Must include:

- flag type
- severity
- wave span
- evidence
- config version
- build ids

## Strategy Classification for Comparison Reports

Build comparison needs consistent labels.

Default classification rules:

- `unit_heavy`
  Unit spend share >= 60 percent and upgrade spend share < 25 percent.
- `upgrade_heavy`
  Upgrade spend share >= 40 percent.
- `balanced`
  Neither unit-heavy nor upgrade-heavy and no branch dominates.
- `branch_specialist`
  A single building path receives >= 40 percent of total build plus upgrade spend.
- `economy_heavy`
  Market and economy-path spend crosses the configured economy-focus threshold before stabilization.

The classification used for a match must be stored in the match summary.

## Questions This Spec Must Support

The finished reporting system is acceptable only if a reviewer can answer each of the following directly from the outputs:

- Can we see exactly which wave the player stops struggling?
- Can we see exactly when gold stops being scarce and starts overflowing?
- Can we see whether units are too cheap once the economy ramps?
- Can we see whether starting gold is too low for a fair opener?
- Can we see whether upgrades are creating the real imbalance instead of units?
- Can we see which building trees are overperforming?
- Can we see whether wave scaling is too flat compared to player growth?
- Can we see whether the game has a healthy struggle curve or a hard-to-easy cliff?
- Can we compare multiple matches and see whether tuning changes actually improved balance?

## Acceptance Criteria

The reporting framework is complete when all of the following are true:

- After one match, the readable AAR can identify stabilization, overflow, and likely spike sources without opening raw event logs.
- After several matches, the trend dataset can show average stabilization wave, snowball onset wave, and overflow rate by config version.
- Every high-level diagnosis can be traced back to wave-level evidence.
- Every match is attributable to exact build, content, and config versions.
- The reports are compact enough to persist durably and query later.
- The reports do not require per-tick database writes.

## Final Outcome

This system should let Ransom Forge balance tuning answer, from evidence:

- the correct starting gold range
- the correct base unit cost range
- whether unit costs need scaling or caps
- whether income should ramp slower or faster
- whether upgrades should be weaker, delayed, or more expensive
- whether enemy waves need more HP, more count, more armor, more speed, or better composition
- whether the challenge curve stays tense or collapses into runaway player dominance

The reports must be comprehensive enough that a match review can clearly show whether the game starves the player early, floods the player later, or does both in the same run.
