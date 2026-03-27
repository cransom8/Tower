# Unity Visual Performance

## Core Thesis

For a top-down RTS or tower-defense game, "looks good" means readable motion, strong silhouettes, clear path ownership, and a few memorable impact moments. Do not trade clarity for expensive atmosphere unless the frame budget is already healthy.

## Budget First

- Set a target frame rate before recommending polish.
- Use rough envelopes:
  - desktop baseline: 16.6 ms frame budget for 60 FPS
  - WebGL or lower-end hardware: 33.3 ms frame budget for 30 FPS
- Split the budget into rendering, gameplay, UI, and spikes.
- Require a stated expected unit and projectile count. A beautiful 40-unit fight and a beautiful 400-unit fight need different advice.

## High-Value Visual Investments

- Improve silhouettes before shader complexity.
- Make walkable paths, lane ownership, and danger zones readable from the gameplay camera.
- Use slot colors and team accents to reinforce battlefield ownership without hiding unit identity.
- Spend VFX budget on:
  - projectile travel readability
  - hit confirmation
  - castle and objective damage
  - elite or rare unit signatures
- Use lighting and post-processing to support mood, not to carry gameplay readability.

## Cost-Effective Techniques

- Prefer shared materials, atlases, and instancing over per-unit material variants.
- Pool particles, impact effects, floating text, and transient combat objects.
- Keep one strong directional light and be selective about additional realtime lights.
- Use shadows only where they add tactical value or strong depth cues.
- Use emissive pulses, vertex color variation, decals, or simple UV animation before adding heavy full-screen effects.
- Add quality tiers for expensive effects rather than forcing a single max setting.

## Common Visual Traps In RTS Scale

- Too many transparent particles causing overdraw in the center of combat.
- Unique materials or property blocks on every unit when a shared atlas would do.
- Realtime lights on every projectile or unit attack.
- Heavy depth of field, motion blur, or cinematic camera shake that hurts readability.
- Screen-space effects that obscure lane state, health, or path progress.

## Repo-Specific Notes

- `unity-client/Assets/Scripts/Game/TileGrid.cs` already defines battlefield-aware coordinate mapping. Keep visual terrain, props, and path readback aligned with those coordinates.
- `unity-client/Assets/Scripts/Game/ProjectileSystem.cs` is snapshot-driven. Favor persistent projectile archetypes and pooled impact effects keyed by projectile type rather than bespoke per-frame logic.
- `unity-client/Assets/Scripts/Net/SnapshotApplier.cs` already exposes slot colors and topology helpers. Derive branch and team presentation from payloads whenever possible.
- Use `BATTLEFIELD_REMODEL_HANDOFF.md` when readability depends on the transition from private lanes to shared funnel space.

## URP Guidance

- Use bloom lightly and only where it supports high-value impacts or emissive landmarks.
- Avoid defaulting to depth of field or motion blur in a strategy camera.
- Prefer material and lighting hierarchy over stacking renderer features.
- When adding a renderer feature or post volume, explain the visual benefit, the likely GPU cost, and the fallback tier.

## Validation

- Check draw calls, material count, and overdraw before and after major art changes.
- Measure with the camera positioned where real play happens, not in a staged close-up.
- Test the busiest expected combat state, not an empty lane.
- Verify that new art still preserves:
  - target readability
  - team readability
  - path readability
  - low-health readability
