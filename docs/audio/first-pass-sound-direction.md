# First-Pass Sound Direction

Date: 2026-04-01

This is the first review pass for Castle Defender sound identity before we replace any live clips.

## Direction Pillars

- RTS readability first
- Fantasy-medieval tone second
- Winter atmosphere always underneath
- Inspired by classic RTS impact and clarity, without cloning any specific game's exact sounds, melodies, or mix

## Reference Feel

- `Warcraft 3`: chunky melee readability, satisfying frontline impacts, strong command-game feel
- `StarCraft`: clean, disciplined UI feedback and mix hierarchy
- `Lord of the Rings`: noble high-fantasy orchestral color and windswept scale
- `Winter battlefield`: cold air, slight openness, restrained wind, no cozy tavern tone

## Guardrails

- Do not copy any recognizable Warcraft, StarCraft, or LOTR melody, motif, rhythm, or exact timbre signature
- Do not go full Hollywood trailer
- Do not go hyper-real foley-only
- Do not sound sci-fi, glossy-mobile, or arcade-cartoon
- Keep sounds short and readable in a busy RTS battlefield

## Brief 01: Richer Melee Clash

Working name: `Winter Steel Clash 01`

### Goal

Create a richer frontline melee impact family for infantry and shield units that feels heavy, cold, and readable at RTS scale.

### Target Feel

- Steel on steel first
- Shield rim or shield-face contact second
- Armor/body thunk underneath
- Slight cold-air openness
- Assertive without being huge or boomy

### Prompt-Ready Brief

Create a short stylized fantasy RTS melee sound family for a snowy battlefield. Focus on sword-on-sword clashes, shield-edge impacts, brief armor thunks, and a clean metallic ring that cuts through battlefield noise. The feel should be cold, martial, readable, and satisfying, like a classic fantasy RTS frontline clash. Keep it short, punchy, and gameplay-first rather than cinematic. Avoid movie trailer whooshes, exaggerated anime swipes, sci-fi energy, or overly realistic foley that disappears in the mix. Add only a light sense of open winter air, not a cavern reverb. No voices, no footsteps, no music, no monster sounds.

### Variation Targets

- Variation A: medium sword clash with shield edge
- Variation B: heavier armored clash with lower thunk
- Variation C: brighter steel ring for lighter infantry
- Variation D: shield-dominant hit with less ring

### Technical Shape

- One-shot
- Ideal length: `0.18s` to `0.45s`
- Fast transient
- Strong midrange presence
- Tail should clear quickly so repeated hits stay readable

### Unity Mapping

- First pass target slot: `fighterSlash`
- Future optional split slots:
- `shieldBlock`
- `armorHit`
- `heroMeleeHeavy`

### Review Questions

- Does it read instantly as melee in a crowded RTS mix?
- Does it feel medieval and physical rather than generic action-library?
- Does it sound cold enough for the winter setting?
- Is it satisfying without becoming muddy or too long?

## Brief 02: Shared Building Hammer

Working name: `Fortress Hammer Build 01`

### Goal

Create one shared construction sound family we can use across buildings for now, built around practical medieval hammer-and-brace work.

### Target Feel

- Hammer on wood beam
- Hammer on iron bracket
- Tiny debris settle
- Slight forge-adjacent metal brightness
- Feels like fortress labor, not carpentry in a modern workshop

### Prompt-Ready Brief

Create a short fantasy-medieval RTS building sound family for fortress construction in a cold winter setting. Focus on hammer strikes on timber and iron braces, subtle structural settling, and grounded medieval construction energy. It should feel practical, sturdy, and hand-built, like workers reinforcing a fortress wall or raising a defensive structure. Keep it short and readable for repeated game actions. Avoid modern tools, saws, nail guns, power-tool textures, bright magic chimes, or exaggerated rubble crashes. No voices, no carts, no crowd, no music. A faint hint of open cold air is okay, but keep the sound mostly dry and direct.

### Variation Targets

- Variation A: wood-first hammer strike
- Variation B: iron-brace hammer strike
- Variation C: sturdier double-hit with tiny settle
- Variation D: slightly brighter "construction confirmed" version

### Technical Shape

- One-shot
- Ideal length: `0.22s` to `0.60s`
- Short enough for repeated placement or upgrade actions
- Keep low end controlled

### Unity Mapping

- Primary shared slot target: `buildTower`
- Can also inform future replacements for:
- `placeWall`
- `upgradeTower`
- `upgradeBarracks`

### Review Questions

- Does it sound like fortress construction, not generic hammer Foley?
- Can we reuse this family across multiple structures without it feeling wrong?
- Does it stay readable if clicked repeatedly?
- Does it feel grounded enough beside the melee family?

## Music Direction For Later

Not generating this in the first pass, but this is the lane:

- `Menu loop`: noble winter fortress, calm wind, low strings, soft choir or horn color, strategic not sleepy
- `Battle loop`: still fantasy, but more drums and low brass, disciplined energy, not full trailer bombast

## OpenAI Note

The repo already has an OpenAI voice workflow for spoken unit lines in [scripts/voice/render-unit-voices.mjs](/C:/Users/Crans/RansomForge/castle-defender/scripts/voice/render-unit-voices.mjs). As of 2026-04-01, the official OpenAI docs I checked document text-to-speech and audio input/output workflows, but I did not find a documented first-party OpenAI API path specifically for non-speech SFX or music generation. So this file is a creative direction brief for review first, not a claim that these two non-voice assets are ready to generate through the current OpenAI script.
