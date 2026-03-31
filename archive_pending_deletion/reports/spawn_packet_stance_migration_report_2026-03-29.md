# Spawn Packet + Stance Anchor Migration Report

Date: 2026-03-29

## Scope

Reviewed the active lane formation, stance, and combat coordination model across:

- `server/sim-multilane.js`
- `server/sim-multilane-serialization.js`
- `server/tests/route_graph_routing.test.js`
- `server/tests/town_core_combat.test.js`
- `unity-client/Assets/Scripts/Net/GameState.cs`
- `unity-client/Assets/Scripts/UI/CmdBar.cs`

Goal of this migration:

- replace lane-wide shared formation control with spawn packets
- replace freeform stance progress sampling with explicit lane stance anchors
- replace rigid slot-preserving combat with loose role-band combat
- preserve server authority and determinism while reducing jitter and re-slot churn

## Archived Model

The following behavior is now considered retired and should not be extended further:

- lane-wide tactical ownership through one shared `formationUnitOrder`
- full-lane re-slotting as the primary way to keep units organized during combat
- full-lane combat target propagation across all barracks units from the same owner lane
- `DEFEND` publishing as `HOLD` instead of a real defend stance
- treating newly spawned barracks sends as candidates for immediate merge into one giant lane formation

These assumptions were the main source of:

- constant reordering
- collision jitter
- position oscillation
- hard-to-debug combat regroup logic

## Replacement Model

Active authoritative model after this migration:

- each barracks send or hero deploy becomes its own packet via `groupId`
- each lane exposes explicit `insideGateAnchor`, `outsideGateAnchor`, and `enemyCoreAnchor`
- packet controllers publish packet-local centers, cohesion radii, waypoint targets, and movement mode
- units keep packet identity, loose combat role metadata, and leash distance from packet center
- combat target sharing is packet-local first, with only short-range nearby packet support
- march order is preserved for readability, but combat no longer performs exact whole-packet slot correction every tick

## Files Moved

No files were physically moved during this migration.

Reason:

- the retired lane-wide behavior lived mostly in active simulation logic rather than in isolated obsolete files
- Unity UI still consumes legacy compatibility fields derived from the new packet model
- removing those compatibility fields today would create an unnecessary client regression

## Compatibility Fields Kept Temporarily

These fields still exist, but now act as compatibility projections instead of the true tactical source of authority:

- `lane.formationAnchor`
- `lane.formationSlots`
- `lane.assignedUnits`

Current policy for those fields:

- they may be read by legacy client code
- they must not be treated as the canonical tactical model
- new server behavior should flow through packets and stance anchors first

## Follow-Up Archive Candidates

Once the client no longer depends on the legacy lane-wide compatibility projection, the next archive pass should remove or deprecate:

1. lane-level consumers that assume one shared formation anchor for all barracks units
2. lane-level consumers that assume `assignedUnits` is the canonical grouping primitive
3. any remaining server helpers whose only purpose is maintaining legacy lane-wide formation compatibility

## Summary

- Active model: spawn packets + explicit stance anchors + loose role combat
- Retired model: lane-wide shared formation as the primary combat/movement authority
- Files moved: `0`
- Safe archive status: conceptual retirement complete, compatibility cleanup still pending on the last legacy projection seam
