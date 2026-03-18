# Mobile Match HUD UI Update

## Goal

Replace the current floating top HUD and ad-hoc side UI with a cleaner mobile-first match HUD built around:

- a persistent top ribbon for moment-to-moment match context
- a right-side rail of independently collapsible stat cards
- a recommended build indicator inspired by Legion TD 2
- clearer grouping, contrast, and hierarchy

## Design Principles

- Use separate cards/boxes instead of loose text over the game world.
- Keep only the most important information always visible.
- Make secondary information independently collapsible.
- Prioritize mobile readability and touch targets.
- Use safe-area-aware layout for phones.
- Favor icon-plus-number presentation over repeated text labels.

## HUD Zones

### 1. Top Ribbon

Always visible. Anchored to the top safe area.

Contains three modules:

1. Wave Block
   - Current wave number
   - Current phase: `BUILD`, `COMBAT`, `NEXT WAVE`
   - Countdown timer

2. Recommended Build Block
   - Current build value
   - Recommended build value
   - Delta (`+24 over`, `-20 under`)
   - Color state:
     - green = healthy
     - yellow = borderline
     - red = underbuilt

3. Next Wave Block
   - Next wave unit icons
   - Damage type
   - Armor type
   - Special trait icons/text
   - Tap target to expand the `Wave Intel` card if collapsed

### 2. Right Rail

Vertical stack of cards anchored to the right safe area.

Each card:

- collapses independently from right to left
- keeps its own header visible as a slim tab when collapsed
- uses a consistent visual treatment and spacing

Cards:

1. `My Stats`
2. `Team Stats`
3. `Player Stats`
4. `Wave Intel`

## Card Contents

### My Stats

Default open on phone and tablet.

- Build Value
- Income
- Leak %
- Send Pressure / Mythium-equivalent
- Optional later: workers or eco stat

### Team Stats

Default collapsed on phone, open on tablet.

- Left Team Total Build
- Left Team Total Income
- Left Team Leak
- Right Team Total Build
- Right Team Total Income
- Right Team Leak

### Player Stats

Default collapsed.

Rows:

- Player A
- Player B
- Player C
- Player D

Columns:

- Build
- Income
- Leak
- Send

Notes:

- Highlight the local player row.
- `Units alive` is intentionally not included in the first version.
- Keep rows compact and aligned in columns for easy comparison.

### Wave Intel

Default collapsed.

- Current wave summary
- Next wave summary
- Wave unit icons
- Damage type
- Armor type
- Special abilities / traits
- Recommended build explanation

## Recommended Build Logic

## Version 1

Use a wave threat score rather than raw unit gold cost alone.

Recommended build value per wave:

`sum(unitThreatScore * unitCount)`

Initial unit threat score can start from cost, then be modified:

- tanky unit: `x1.15`
- ranged threat: `x1.10`
- splash or siege: `x1.20`
- special ability: `x1.10` to `x1.30`
- boss: manual override

This gives a practical first pass while leaving room for tuning later.

## Status Thresholds

- Green: `build >= 100%` of recommended
- Yellow: `build >= 90% and < 100%`
- Red: `build < 90%`

Display format:

- `210 / 230`
- `-20 under`

## Mobile Defaults

### Default Open

- Top Ribbon
- `My Stats`

### Default Collapsed

- `Team Stats`
- `Player Stats`
- `Wave Intel`

### Tablet Defaults

- `My Stats` open
- `Team Stats` open
- `Player Stats` collapsed
- `Wave Intel` collapsed

## Visual Direction

- Dark translucent cards with strong contrast against the playfield
- Clear borders or glows for active/high-priority blocks
- Short labels and icon-first stat rows
- One accent color per stat family
- Stronger emphasis for the local player row
- Team totals larger than player-row details

## Implementation Order

1. Add reusable independent collapsible-card UI behavior
2. Rebuild top ribbon into modular cards
3. Add `My Stats` card on the right rail
4. Add `Team Stats` card
5. Add `Player Stats` card
6. Add `Wave Intel` card
7. Add recommended-build calculation and display

## Current Notes

- `CmdBar` already has independent collapse behavior that can inform the right-rail interaction.
- `InfoBar` currently owns top HUD text updates and should likely be evolved into a more modular match HUD controller.
- Portrait contamination issue was caused by multiple portrait studios sharing the same world region; this has already been corrected.
