# Wave Defense (Forge Wars Phase 1) — Test Handoff

## What Was Built

The `game_ml` mode has been converted from a wall-based PvP tower defense into a **Legion TD–style wave defense game**. Players place permanent defenders on their lane grid during a build phase, then enemy waves attack during the combat phase.

This document covers everything needed to test the implementation end-to-end from a fresh terminal.

---

## Architecture Summary

### Server (Node.js — `server/`)
The server-side wave defense is **fully implemented** in `server/sim-multilane.js`:
- Round FSM: `build` (30s) → `combat` → `transition` (10s) → repeat
- `place_unit` action: places a loadout defender on the player's grid tile
- `sell_tower` action: sells a placed defender back for gold
- Wave enemies spawn on combat start (from DB config + +10%/wave escalation after wave 21)
- Two-way combat: wave enemies attack defenders, defenders attack wave enemies
- Dead defenders auto-respawn at build phase start
- `teamHp { left, right }` — one HP pool per team side, loses 1 per leaked enemy
- `pendingSends` — `spawn_unit` during build phase queues pressure units into the opponent's next wave

Wave config is in DB (migration 039 + 040):
- `ml_wave_configs` table — one "Standard" default config
- `ml_waves` table — 21 authored waves (waves 1–21), then +10%/wave auto-escalation

### Unity Client (`unity-client/Assets/Scripts/`)
All scripts compile clean (zero errors). Key changes:

| Script | What Changed |
|--------|-------------|
| `Game/TileGrid.cs` | Click opens unit picker (not wall drag); gated on `roundState == "build"`; dead tiles blocked; path endpoints blocked; HP bars on towers |
| `UI/TileMenuUI.cs` | Shows unit placement picker for empty tiles; upgrade/sell for towers |
| `Game/ActionSender.cs` | `PlaceUnit(col, row, key)` and `SellTower(col, row)` added; wall methods removed |
| `Game/GameManager.cs` | Wave HUD: round number, phase badge, countdown, both team HP values |
| `Game/LaneRenderer.cs` | Wave enemies render red-orange tint; player units use team colors |
| `UI/CmdBar.cs` | Send buttons disabled during combat/transition; wall button hidden; drain bar repurposed as build-phase countdown |
| `Net/GameState.cs` | New fields: `roundState`, `roundNumber`, `roundStateTicks`, `buildPhaseTotal`, `teamHp` (MLTeamHp), `deadCells` (MLDeadCell[]), `hp`/`maxHp` on MLTowerCell, `isWaveUnit` on MLUnit |

### Scene Wiring (Game_ML.unity — already done, saved)
- `GameManager` → TxtRound, TxtPhase, TxtCountdown, TxtTeamHpLeft, TxtTeamHpRight all wired to `Canvas/WaveHUD` TMP_Text labels
- `LaneRenderer.HpBarPrefab` → `Assets/Prefabs/UI/HpBar.prefab`
- All 4 `TileGrid` components → `HpBarPrefab` assigned

---

## Test Environment Setup

### 1. Start the server
```bash
cd C:\Users\Crans\castle-defender
node server/index.js
```
Server runs on port 3000 (or `PORT` env var). DB migrations run automatically on startup — confirm `040_ml_wave_21` runs clean in the log.

### 2. Open Unity
- Project: `C:\Users\Crans\CastleDefenderClient`
- Unity 6 (6000.3.10f1)
- Open scene: `Assets/Scenes/Game_ML.unity`
- Verify zero compile errors in Console

### 3. Connection
Unity connects to the server via `NetworkManager`. The server URL is configured in the `NetworkManager` component in the scene (or in `NetworkManager.cs`). Make sure it points to `http://localhost:3000` for local testing.

---

## Test Cases

### TC-1: Compilation & Scene Integrity
**Steps:**
1. Open Unity, let it compile
2. Open `Game_ML.unity`
3. Check Console for errors

**Expected:**
- Zero compile errors
- One known non-error warning: `Cannot add menu item 'Castle Defender/Setup/Build LVE Background'...` (duplicate editor menu — harmless)
- `GameManager` inspector shows all 5 wave HUD TMP_Text fields assigned (TxtRound, TxtPhase, TxtCountdown, TxtTeamHpLeft, TxtTeamHpRight)
- `LaneRenderer` inspector shows `HpBarPrefab` assigned
- All 4 TileGrid components show `HpBarPrefab` assigned

---

### TC-2: Match Start — Build Phase
**Steps:**
1. Press Play in Unity
2. Log in (or continue as guest)
3. Navigate Lobby → Line Wars → create/join a match
4. Match starts in Game_ML scene

**Expected:**
- WaveHUD panel visible at top of screen showing:
  - "Wave 1"
  - "BUILD" (green)
  - Countdown timer ticking down from ~30s
  - "♥ 20" for both teams (gold color for own team, dim white for opponent)
- CmdBar send buttons are **active** (white, clickable)
- Build-phase countdown bar fills the QueueDrainBar slot
- Wall button is **hidden** (not visible in CmdBar)

---

### TC-3: Unit Placement
**Steps:**
1. During build phase, tap/click an empty floor tile on your lane

**Expected:**
- `TileMenuUI` opens showing:
  - "Place unit (col, row)" header
  - Unit picker buttons (HLayoutTowerButtons visible) with loadout units and their costs
  - No upgrade or sell button
  - Close button
- Tap a unit button
- Menu closes
- A defender model appears on that tile
- Gold decreases by the unit's `build_cost`
- HP bar appears above the defender
- Clicking the unit again during build phase (it's now a "tower" tile) shows:
  - Unit name + level header
  - Upgrade button + cost
  - Sell/Remove button
  - No unit picker

**Failure signals:**
- Menu doesn't open → check `TileGrid.HandleTileClick` roundState gate, check scene is in build phase
- Menu opens but buttons don't send → check `ActionSender.PlaceUnit` in console
- Tile doesn't update → check server `place_unit` action response in server log

---

### TC-4: Can't Place on Blocked Tiles
**Steps:**
1. During build phase, click the spawn tile (top of lane, row 0, col 5)
2. Click the castle tile (bottom of lane, row 27, col 5)
3. Click a tile that already has a defender

**Expected:**
- Nothing happens on spawn/castle tiles (menu does NOT open)
- Tower tile opens upgrade/sell menu, not placement picker

---

### TC-5: Combat Phase Starts
**Steps:**
1. Wait for build timer to expire (or wait 30s)

**Expected:**
- WaveHUD phase badge changes to "COMBAT" (or "NEXT WAVE" during transition)
- CmdBar send buttons go **grey and non-interactive** (ColorPhaseOff = 0.35, 0.35, 0.35, 0.50)
- Build countdown bar hides
- Wave enemy units spawn and move toward castle — rendered in **red-orange** tint (distinct from player team colors)
- Defenders attack wave units (server-driven, visible as units losing HP / dying)
- HP bars on tower tiles drain as defenders take damage

---

### TC-6: Enemy Leaks → Team HP
**Steps:**
1. Let wave enemies reach the castle (don't place enough defenders, or wait for a late wave)

**Expected:**
- `♥ N` for the affected team decreases by 1 per enemy that completes the path
- When team HP hits 0, the game ends (GameOverUI shows)

---

### TC-7: Transition Phase — Dead Defender Respawn
**Steps:**
1. Let some defenders die during combat
2. Wait for transition phase

**Expected:**
- Dead defenders render as greyed-out models during combat (grey/transparent tint)
- Clicking a dead tile during build phase does NOT open the picker
- At the start of the next build phase, greyed-out defenders turn back to normal color (they respawned)
- Those tiles block placement again (now "tower" type, not "dead_tower")

---

### TC-8: Send Units (Add Wave Pressure)
**Steps:**
1. During build phase, click a send button in CmdBar for a unit you can afford

**Expected:**
- Unit is queued as `pendingSend` (server-side)
- At combat start, that unit type is added to the opponent's wave

**Note:** There is no queue count badge visible for this in the current build (the server doesn't emit `queue_update` events in wave defense mode). The send is fire-and-forget until combat starts.

---

### TC-9: Sell a Defender
**Steps:**
1. During build phase, click a placed defender tile
2. Click the Sell/Remove button

**Expected:**
- `sell_tower` action sent to server
- Tile clears back to empty floor
- Gold refunded (server-determined sell value)

---

### TC-10: Round Progression
**Steps:**
1. Survive multiple rounds

**Expected:**
- "Wave N" in HUD increments each round
- Waves get progressively harder (more enemies, higher HP/damage after wave 21 it scales +10%/wave)
- Dead defenders always respawn at build start

---

## Known Limitations (Not Bugs)

1. **No queue count badge during sends** — the old `queue_update` event is no longer emitted by the server in wave defense mode. The send buttons work (server queues the unit), but there's no "×N" counter visible. This is expected for now.

2. **HP bar shrinks from center** — The HpBar prefab uses localScale.x to fill, so bars shrink symmetrically from both sides rather than draining left-to-right. Functional but not perfectly polished.

3. **WaveHUD panel** — newly created panel in Canvas at top. May overlap with other UI in some aspect ratios. If needed, adjust `Canvas/WaveHUD` RectTransform position in the inspector.

4. **Wall button in CmdBar** — The `BtnWall` GameObject is hidden at runtime (`SetActive(false)` in `Start()`). It is still in the hierarchy. `CmdBar.WallModeActive` remains accessible (used by `CameraController`) but always false.

5. **Survival mode** — `Game_Survival` scene is unaffected. `SurvivalManager.cs` was updated to remove references to `wallCount`/`walls` (removed from MLLaneSnap), but survival gameplay itself is unchanged.

---

## Key File Locations

```
server/
  sim-multilane.js          — Round FSM, place_unit, wave spawning, combat, teamHp
  game/multilaneRuntime.js  — Wave config loading from DB, _pendingEvents drain
  migrations/
    039_ml_wave_config.sql  — ml_wave_configs + ml_waves tables
    040_ml_wave_21.sql      — 21 authored waves seeded

unity-client/Assets/Scripts/
  Game/
    TileGrid.cs             — Tile click handling, HP bars, dead cell rendering
    GameManager.cs          — Wave HUD subscription and update
    LaneRenderer.cs         — Wave enemy red-orange tint
    ActionSender.cs         — PlaceUnit(), SellTower()
  UI/
    TileMenuUI.cs           — Unit picker (empty tiles) + upgrade/sell (tower tiles)
    CmdBar.cs               — Phase-gated send buttons, build countdown bar
  Net/
    GameState.cs            — MLSnapshot, MLTeamHp, MLDeadCell, MLLaneSnap

Assets/
  Prefabs/UI/HpBar.prefab   — World-space HP bar (Background + FillAnchor/Fill)
  Scenes/Game_ML.unity      — Saved with all wiring complete
```

---

## Server Log Signals to Watch

| Log message | Meaning |
|-------------|---------|
| `[ml-game] wave config loaded: 21 waves` | DB wave config loaded successfully |
| `[ml-game] round 1 combat start` | Build phase ended, combat begins |
| `[ml-game] place_unit ok` | Defender placed successfully |
| `[ml-game] sell_tower ok` | Defender sold successfully |
| `[ml-game] round 1 transition` | All enemies cleared, transition phase |
| `[ml-game] place_unit rejected: not build phase` | Client sent placement outside build window |

---

## If Something Breaks

**Unity won't connect:** Check `NetworkManager` URL points to running server. Check server started without DB errors.

**`place_unit` silently fails:** Open browser DevTools (or Unity console `[ActionSender]` log) to confirm the socket event fires. Check server log for rejection reason. Verify the unit type key is in the player's loadout.

**Build phase never ends:** Check server console — the FSM tick may not be running. Confirm `multilaneRuntime.js` is starting the game tick.

**Wave enemies don't appear:** Confirm server has wave config loaded (see log above). Check `ml_waves` table has rows for `config_id = 1`.

**Dead cells not rendering grey:** Confirm `deadCells` array is present in the snapshot payload (add `console.log` in `createMLSnapshot` in sim-multilane.js). Check `TileGrid.UpdateTiles` dead cell branch.
