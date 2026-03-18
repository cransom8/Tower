# Game_ML Environment Audit

Scene: `Assets/Scenes/Game_ML.unity`
Map Root: `Map`
Generated: `2026-03-18 00:05:14`

## Likely Visual-Only Branches

No clean visual-only branches were detected.

## Review / Keep Local

## Review / Keep Local

### `WinterDecor`

Path: `Map/WinterDecor`
Nodes: `253`
Renderers: `221`
Particles: `1`
Lights: `26`
Missing Scripts: `0`
Likely Visual Only: `False`
Colliders: `BoxCollider, MeshCollider`
Custom Components: `UnityEngine.Rendering.Universal.UniversalAdditionalLightData, WinterChristmasVillage.ChristmasLights`

## Extraction Rule Of Thumb

- Branches with custom gameplay scripts should stay local unless reviewed manually.
- Branches with only renderers, particles, animators, or lights are the safest first extraction candidates.
- Branches with only basic colliders and no custom scripts are good extraction candidates after collider removal is reviewed.
- Waypoints, spawn points, tile grids, triggers, and anything referenced by gameplay code should remain in-scene.
