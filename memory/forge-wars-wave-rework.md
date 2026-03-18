# Forge Wars Wave Rework — Phase 1 Implementation Plan
Last updated: 2026-03-09

## Status: APPROVED — EXECUTION IN PROGRESS

---

## What's Already Done (Server-Side)

All core server gameplay for Phase 1 is complete in `server/sim-multilane.js`:
- Round state machine (`roundState: build/combat/transition`, `roundNumber`, `roundStateTicks`)
- Team HP (`teamHp: {left, right}`, -1 HP per leak)
- `place_unit` action with loadout validation, behavior_mode check, build-phase gate
- `spawn_unit` → `pendingSends` accumulation during build, flushed into wave at combat start
- Wave spawning (`_resolveWave`, `_spawnWaveUnit`, DB-backed config via migration 039)
- Two-way combat: defenders attack wave units, wave enemies attack defenders (stop-to-fight)
- Defender death → `dead_tower` tile type
- Defender respawn → `_startBuild` restores all `dead_tower` → `tower` at full HP
- Snapshot: `roundState`, `roundNumber`, `teamHp`, `isWaveUnit`, `deadCells`, tower `hp`/`maxHp`
- `ml_wave_start`, `ml_round_start`, `ml_round_end` events
- Win condition via team HP → 0
- `place_wall` removed from `applyMLAction`
- AI adapted (`BUILD_TOWER` maps to `place_unit` in `ai/actions.js`)
- DB: migration 039 `ml_wave_configs` + `ml_waves`, 10 seeded waves

---

## Locked Design Decisions

### Canonical place_unit payload (NO ALIASES):
```json
{ "type": "place_unit", "data": { "gridX": 3, "gridY": 14, "unitTypeKey": "goblin" } }
```

### Dead tile rules:
- `dead_tower` tiles: server blocks placement (tile.type !== "empty")
- Client must also block tap/picker for dead tiles
- Restore happens automatically on next ml_round_start snapshot

### Wave progression:
- Waves 1–21: authored rows in ml_waves table
- Wave 22+: _resolveWave uses last row (wave 21 = demon_lord) + 10%/wave escalation

### Player side derivation:
- Derive once from `snapshot.lanes[MyLaneIndex].side` ("left"|"right")
- Never from camera/UI layout/colors

---

## Ticket Status

| Ticket | Description | Status |
|--------|-------------|--------|
| P1-S1  | Migration 040 — extend waves to 21 | PENDING |
| P1-S2  | Web client: replace place_wall with place_unit | PENDING |
| P1-S8  | Server: remove bfsPath dead code | PENDING |
| P1-S3  | Unity GameState.cs snapshot types | PENDING |
| P1-S4  | Unity ActionSender.cs PlaceUnit | PENDING |
| P1-S5  | Unity TileGrid + TileMenuUI build flow | PENDING |
| P1-S6  | Unity GameManager HUD | PENDING |
| P1-S7  | Unity LaneRenderer wave/defender visuals | PENDING |
| P1-S9  | Admin wave editor UI | DEFERRED |

---

## P1-S1 — Migration 040 (wave table 11–21)

**File:** `server/migrations/040_ml_wave_21.sql`

All unit keys confirmed in migration 034:

```sql
INSERT INTO ml_waves (config_id, wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
SELECT 1, w.wave_number, w.unit_type, w.spawn_qty, w.hp_mult, w.dmg_mult, w.speed_mult
FROM (VALUES
  (11, 'cyclops',        5, 1.00, 1.00, 1.00),
  (12, 'cyclops',        6, 1.30, 1.20, 1.00),
  (13, 'werewolf',       6, 1.00, 1.00, 1.00),
  (14, 'werewolf',       7, 1.35, 1.25, 1.05),
  (15, 'griffin',        5, 1.00, 1.00, 1.00),
  (16, 'griffin',        6, 1.40, 1.30, 1.05),
  (17, 'manticora',      4, 1.00, 1.00, 1.00),
  (18, 'chimera',        4, 1.00, 1.00, 1.00),
  (19, 'mountain_dragon',3, 1.00, 1.00, 1.00),
  (20, 'mountain_dragon',4, 1.50, 1.40, 1.05),
  (21, 'demon_lord',     3, 1.00, 1.00, 1.00)
) AS w(wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
ON CONFLICT (config_id, wave_number) DO NOTHING;
```

Wave 21 (demon_lord) = terminal wave. Wave 22+ uses existing _resolveWave escalation.

---

## P1-S2 — Web client: replace place_wall (game.js)

**Changes needed:**
1. Add `let mlPlacementUnit = null;` — currently selected loadout unit key for placement
2. Add loadout unit selection in CmdBar (clicking a loadout send button during build also selects it for placement, OR a separate placement mode toggle)
3. `commitDragPreviewWalls()` → sends `place_unit` with `mlPlacementUnit` per tile
4. `handleMLCanvasClick` empty-tile branch:
   - Gate: `roundState === 'build'` AND not deadCell AND not towerCell
   - If `mlPlacementUnit` set: send `place_unit` directly
   - If not set: show unit picker from loadout
5. Remove `canPreviewWallAt`, `showMLWallConvertMenu`, wall-specific gold check, wall feedback messages
6. `isWall` check in click handler → replaced with `deadCell` check

**Key vars to remove/replace:** `mlWallCost`, `mlMaxWalls`, `mlDragPlacing` (rename to `mlDragBuilding`), wall feedback strings

---

## P1-S8 — Server: remove BFS dead code (sim-multilane.js)

**Remove:**
- `MAX_WALLS = null` and `WALL_COST = 2` constants
- `bfsPath(grid)` function (lines 407–454)
- Call `bfsPath(grid)` in `createMLGame` → replace with `straightLinePath()` inline

**Replace with:**
```js
// In createMLGame, replace: const path = bfsPath(grid);
const path = [];
for (let y = 0; y <= CASTLE_YG; y++) path.push({ x: SPAWN_X, y });
```

**Update header comment:** Remove "Wall placement requires BFS validation + combat lock."

---

## P1-S3 — Unity GameState.cs snapshot types

**Add to `MLSnapshot`:**
```csharp
public string roundState;          // "build"|"combat"|"transition"
public int    roundNumber;
public int    roundStateTicks;
public int    buildPhaseTotal;
public int    transitionPhaseTotal;
public SerializableDictionary<string, int> teamHp;  // or float
public int    teamHpMax;
```

**Update `MLTowerCell`:** add `public float hp; public float maxHp;`

**Add `MLDeadCell`:**
```csharp
[Serializable] public class MLDeadCell { public int x; public int y; public string type; }
```

**Update `MLLaneSnap`:** add `public MLDeadCell[] deadCells;`
**Update `MLUnit`:** add `public bool isWaveUnit;`
**Remove from `MLLaneSnap`:** `wallCount`, `walls[]`
**Remove from `MLMatchConfig`:** `wallCost`, `maxWalls`

---

## P1-S4 — Unity ActionSender.cs

**Remove:** `PlaceWall`, `RemoveWall`, `UpgradeWall`

**Add:**
```csharp
public static void PlaceUnit(int col, int row, string unitTypeKey)
    => SendAction("place_unit", new { gridX = col, gridY = row, unitTypeKey });
```

No aliases, no compatibility extras.

---

## P1-S5 — Unity TileGrid + TileMenuUI

**Picker open gate (strict AND):**
1. `roundState == "build"`
2. Tapped tile NOT in `deadCells`
3. Tapped tile NOT in `towerCells`
4. Tapped tile NOT equal to `lane.path[0]` (spawn) or `lane.path[lane.path.length-1]` (castle)

If any fails: no picker, no action. Dead tiles get tooltip "Restores next build phase."

**Dead tile rendering:** unit type at reduced opacity/grey. Restored by next ml_round_start snapshot.

**Note on spawn/castle coords:** Derive from `lane.path[0]` and `lane.path[last]` from snapshot, not hardcoded (5,0)/(5,27), even though those ARE the fixed values.

---

## P1-S6 — Unity GameManager HUD

**Player side:** derive once from `snap.lanes[MyLaneIndex].side`, store as `_playerSide`.
Never infer from camera/UI position.

```csharp
string _playerSide; // set once from first snapshot
if (_playerSide == null && snap.lanes != null) {
    var myLane = Array.Find(snap.lanes, l => l.laneIndex == SnapshotApplier.Instance.MyLaneIndex);
    if (myLane != null) _playerSide = myLane.side;
}
```

**HUD:** Show both `teamHp[_playerSide]` (primary, color-coded) and opponent HP (secondary, muted).
Color: >66% green, 33-66% yellow, <33% red.

---

## P1-S7 — Unity LaneRenderer

- Wave units (`isWaveUnit`) render with distinct enemy tint (no owner color)
- Dead defenders (`deadCells`) render at reduced opacity/grey, no attack anim
- HP bars on all `towerCells` using `hp`/`maxHp`
- Remove wall rendering path

---

## Key File Reference

| File | Role |
|------|------|
| `server/sim-multilane.js` | Core sim — already implements wave rework |
| `server/game/multilaneRuntime.js` | Room/game lifecycle |
| `server/migrations/039_ml_wave_config.sql` | Wave DB schema + 10 waves |
| `server/migrations/040_ml_wave_21.sql` | NEW — waves 11–21 |
| `client/game.js` | Web client — needs place_wall → place_unit |
| `unity-client/Assets/Scripts/Net/GameState.cs` | C# type mirror |
| `unity-client/Assets/Scripts/Game/ActionSender.cs` | Action helpers |
| `unity-client/Assets/Scripts/Game/TileGrid.cs` | Tile rendering + tap |
| `unity-client/Assets/Scripts/UI/TileMenuUI.cs` | Tile tap UI |
| `unity-client/Assets/Scripts/Game/GameManager.cs` | HUD |
| `unity-client/Assets/Scripts/Game/LaneRenderer.cs` | Unit/defender rendering |
