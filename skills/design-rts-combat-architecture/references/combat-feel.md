# Combat Feel

## Core Thesis

Real-feeling RTS combat comes from timing, readability, and consequence. The player should understand who is attacking, what kind of damage is happening, whether it landed, and why the frontline is shifting.

## Build Feel In Layers

1. Anticipation
   - Show wind-up, aim, or charge before damage resolves.
2. Travel
   - Make projectile or attack travel readable in arc, speed, and damage type.
3. Impact
   - Pair hit flash, sound, health change, and short-lived impact FX.
4. Aftermath
   - Leave a brief trace: recoil, death beat, stagger, debris, or status marker.
5. Information
   - Keep health, threat, and target ownership readable during dense fights.

## Rules For Server-Authoritative Combat

- Keep combat truth on the server.
- Use client-side presentation to sell the result, not to invent it.
- Do not fake kills, leaks, or lane ownership changes on the client.
- Use interpolation, wind-up, trails, flashes, audio, and impact effects to make snapshot-driven combat feel immediate.

## What Usually Matters More Than Extra Effects

- Clear attack cadence for each unit or tower archetype.
- A distinct signature per damage family such as physical, magic, siege, or splash.
- Strong frontline and backline separation.
- Visible unit spacing so crowds do not dissolve into noise.
- Obvious objective damage when a castle, bridge, or lane leaks.

## Repo-Specific Notes

- `unity-client/Assets/Scripts/Game/ProjectileSystem.cs` already maps projectile type and damage type to color, scale, and impact behavior. Extend that vocabulary before inventing a parallel feedback system.
- `unity-client/Assets/Scripts/Game/TileGrid.cs` and the shared path mapping decide where combat reads from the camera. Feel problems can be positioning problems, not only VFX problems.
- `unity-client/Assets/Scripts/Net/SnapshotApplier.cs` and `NetworkManager.cs` already give you the authoritative lane, team, and snapshot context for branch-local or shared-funnel feedback.
- `server/sim-multilane.js` is the place to change cadence, ability timing, spacing, and actual combat logic.

## Recommendations By Goal

- To make melee feel heavier:
  - emphasize wind-up and recovery
  - keep the hit window readable
  - use concise contact FX instead of large lingering particles
- To make ranged feel sharper:
  - improve launch timing
  - make projectile travel consistent
  - differentiate impact signatures by ammo and damage type
- To make large fights readable:
  - reduce simultaneous noisy particles
  - keep health bars and lane markers clean
  - let the strongest hit moments stand out
- To make objectives feel important:
  - reserve your biggest audio and FX response for castle and leak events
  - use screen-space feedback sparingly and only for truly important events

## Validation

- Test feel at the real gameplay camera, not a debug close-up.
- Watch for whether a player can answer these questions in under a second:
  - who is winning this fight
  - what is hitting me
  - where is the lane pressure coming from
  - what unit or tower should I react to
- If the answer is no, improve readability before increasing spectacle.
