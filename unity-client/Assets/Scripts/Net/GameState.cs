// GameState.cs — C# mirror of ACTUAL server JSON payloads.
// Matches server/index.js event payloads + sim-multilane.js snapshot structure exactly.
// Field names are camelCase to match server JSON. All classes are [Serializable].

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Net
{
    // ─── ML Lobby ─────────────────────────────────────────────────────────────

    [Serializable]
    public class MLRoomCreatedPayload
    {
        public string code;
        public int    laneIndex;    // always 0 for creator
        public string displayName;
    }

    [Serializable]
    public class MLRoomJoinedPayload
    {
        public string code;
        public int    laneIndex;
        public string displayName;
    }

    [Serializable]
    public class MLLobbyPlayer
    {
        public int    laneIndex;
        public string displayName;
        public bool   ready;
        public bool   isAI;
        public string difficulty;   // "easy"|"medium"|"hard"|null for humans
    }

    [Serializable]
    public class MLLobbyUpdate
    {
        public string          code;
        public MLLobbyPlayer[] players;
        public int             hostLaneIndex;
    }

    // ─── Classic Lobby ────────────────────────────────────────────────────────

    [Serializable]
    public class ClassicRoomCreatedPayload
    {
        public string code;
        public string side;     // "bottom" for creator
    }

    [Serializable]
    public class ClassicRoomJoinedPayload
    {
        public string code;
        public string side;     // "top" for joiner
    }

    // ─── Match Ready ─────────────────────────────────────────────────────────

    [Serializable]
    public class MLMatchReadyPayload
    {
        public string             code;
        public int                playerCount;
        public MLLaneAssignment[] laneAssignments;
        public MLBattlefieldTopology battlefieldTopology;
    }

    [Serializable]
    public class MLLaneAssignment
    {
        public int    laneIndex;
        public string displayName;
        public bool   isAI;
        public string team;
        public string side;
        public string slotKey;
        public string slotColor;
        public string branchId;
        public string branchLabel;
        public string castleSide;
    }

    [Serializable]
    public class MLMatchConfig
    {
        public int          tickHz;
        public int          incomeIntervalTicks;
        public float        startGold;
        public int          livesStart;
        public int          gridW;
        public int          gridH;
        public LoadoutEntry[] loadout;       // 5 unit types for this match
        public string       reconnectToken;  // Phase U8 — store for disconnect recovery
        public bool         ranked;
        public MLBattlefieldTopology battlefieldTopology;
        public MLSlotDefinition[] slotDefinitions;
    }

    [Serializable]
    public class LoadoutEntry
    {
        public string key;          // "runner"|"footman"|etc.
        public string name;         // display name
        public int    send_cost;    // gold cost to send
        public int    hp;
        public float  path_speed;
    }

    [Serializable]
    public class MLSlotDefinition
    {
        public int    laneIndex;
        public string slotKey;
        public string side;
        public string slotColor;
        public string branchId;
        public string branchLabel;
        public string castleSide;
        public string team;
    }

    [Serializable]
    public class MLBattlefieldTopology
    {
        public string             mapType;
        public string             centerIslandId;
        public string[]           sideOrder;
        public MLCastleDef[]      castles;
        public MLMergeZoneDef[]   mergeZones;
        public MLBuildZoneDef[]   buildZones;
        public bool               sharedZonesBuildable;
        public MLSlotDefinition[] slotDefinitions;
    }

    [Serializable]
    public class MLCastleDef
    {
        public string side;
        public string castleId;
        public string bridgeId;
    }

    [Serializable]
    public class MLMergeZoneDef
    {
        public string side;
        public string landmassId;
        public string bridgeId;
    }

    [Serializable]
    public class MLBuildZoneDef
    {
        public string branchId;
        public int    ownerLaneIndex;
        public bool   buildable;
    }

    // ─── Queue Update ─────────────────────────────────────────────────────────
    // Server emits "queue_update" each tick when send queue changes (Phase D).
    // queues is a dynamic { key: count } dict matching whatever unit keys are in
    // the current match (HF creatures, etc.) — not hardcoded classic unit names.

    [Serializable]
    public class QueueUpdatePayload
    {
        public Dictionary<string, int> queues;   // key → count in send queue
        public float                   drainProgress;
        public int                     totalQueued;
        public int                     queueCap;
    }

    [Serializable]
    public class ClassicMatchReadyPayload
    {
        public string code;
    }

    // ─── ML Snapshot ─────────────────────────────────────────────────────────
    // Server emits as "ml_state_snapshot" at 10hz.

    [Serializable]
    public class MLTeamHp
    {
        public int left;
        public int right;
    }

    [Serializable]
    public class MLSnapshot
    {
        public int          tick;
        public string       phase;                  // "playing"|"ended"
        public int          winner;                 // lane index when ended; 0 when null (check phase)
        public int          incomeTicksRemaining;   // global, shared by all lanes
        // ── Forge Wars wave defense ──────────────────────────────────────────
        public string       roundState;             // "build"|"combat"|"transition"
        public int          roundNumber;
        public int          roundStateTicks;
        public int          buildPhaseTotal;
        public int          transitionPhaseTotal;
        public MLTeamHp     teamHp;                 // { left, right }
        public int          teamHpMax;
        // ─────────────────────────────────────────────────────────────────────
        public MLBattlefieldTopology battlefieldTopology;
        public MLLaneSnap[] lanes;
    }

    [Serializable]
    public class MLLaneSnap
    {
        public int            laneIndex;
        public string         team;
        public string         side;
        public string         slotKey;
        public string         slotColor;
        public string         branchId;
        public string         branchLabel;
        public string         castleSide;
        public bool           eliminated;
        public float          gold;
        public float          income;
        public int            lives;
        public int            barracksLevel;
        public MLTowerCell[]  towerCells;      // active defender tiles
        public MLDeadCell[]   deadCells;       // dead defender tiles (inactive until next build phase)
        public MLGridPos[]    path;            // wave path as [{x,y}] array
        public MLUnit[]       units;
        public int            spawnQueueLength;
        public MLProjectile[] projectiles;
    }

    [Serializable]
    public class MLWall
    {
        public int x;
        public int y;
        // Backward/forward compatibility with server payload variants.
        public int gridX;
        public int gridY;
        public int col;
        public int row;

        public int X => (x != 0 || y != 0) ? x
                    : (gridX != 0 || gridY != 0) ? gridX
                    : col;
        public int Y => (x != 0 || y != 0) ? y
                    : (gridX != 0 || gridY != 0) ? gridY
                    : row;
    }

    [Serializable]
    public class MLTowerCell
    {
        public int    x;
        public int    y;
        // Backward/forward compatibility with server payload variants.
        public int    gridX;
        public int    gridY;
        public int    col;
        public int    row;
        public string type;     // unit type key, e.g. "goblin"|"orc"|"cyclops"
        public int    level;
        public bool   debuffed;
        public float  hp;       // current HP (wave defense)
        public float  maxHp;    // max HP (wave defense)

        public int X => (x != 0 || y != 0) ? x
                    : (gridX != 0 || gridY != 0) ? gridX
                    : col;
        public int Y => (x != 0 || y != 0) ? y
                    : (gridX != 0 || gridY != 0) ? gridY
                    : row;
    }

    // Dead defender tile — unit died this round; tile reserved until next build phase.
    [Serializable]
    public class MLDeadCell
    {
        public int    x;
        public int    y;
        public string type;     // unit type key (for rendering the inactive unit sprite)
    }

    [Serializable]
    public class MLGridPos
    {
        public int x;
        public int y;
    }

    [Serializable]
    public class MLUnit
    {
        public string id;
        public int    ownerLane;    // -1 for wave enemies
        public string type;         // unit type key
        public string skinKey;      // null = default skin; otherwise overrides prefab lookup
        public float  pathIdx;
        public int    gridX;
        public int    gridY;
        public float  normProgress; // 0..1 along wave path
        public float  hp;
        public float  maxHp;
        public bool   isWaveUnit;   // true = enemy wave unit; false = player-sent unit
    }

    [Serializable]
    public class MLProjectile
    {
        public string id;
        public int    ownerLane;
        public string sourceKind;       // "tower"
        public string projectileType;   // which tower type fired
        public string damageType;       // "PIERCE"|"NORMAL"|"MAGIC"|"SIEGE"|"SPLASH"
        public bool   isSplash;
        public int    fromX;
        public int    fromY;
        public int    toX;
        public int    toY;
        public float  progress;         // 0=at tower, 1=at target
    }

    // ─── Classic Snapshot ────────────────────────────────────────────────────
    // Server emits as "state_snapshot" at 10hz.

    [Serializable]
    public class ClassicSnapshot
    {
        public int                  tick;
        public string               phase;
        public string               winner;
        public int                  incomeTicksRemaining;
        public ClassicPlayers       players;
        public ClassicUnit[]        units;
        public ClassicProjectile[]  projectiles;
    }

    [Serializable]
    public class ClassicPlayers
    {
        public ClassicPlayerState bottom;
        public ClassicPlayerState top;
    }

    [Serializable]
    public class ClassicPlayerState
    {
        public float gold;
        public float income;
        public int   lives;
        // towers dict: deserialized manually in ClassicGameManager if needed
    }

    [Serializable]
    public class ClassicUnit
    {
        public string id;
        public string side;     // "top"|"bottom"
        public string type;     // "runner"|"footman"|"ironclad"|"warlock"|"golem"
        public float  y;        // 0..1 (0=spawn, 1=enemy castle)
        public float  hp;
        public float  maxHp;
    }

    [Serializable]
    public class ClassicProjectile
    {
        public string id;
        public string side;
        public string slot;
        public string projectileType;
        public string damageType;
        public bool   isSplash;
        public float  progress;
    }

    // ─── Game Over ───────────────────────────────────────────────────────────
    // ML uses event "ml_game_over"; Classic uses "game_over".

    [Serializable]
    public class MLGameOverPayload
    {
        public int    winnerLaneIndex;
        public string winnerName;
    }

    [Serializable]
    public class ClassicGameOverPayload
    {
        public string winner;   // socket id or "draw"
    }

    // ─── Rematch ─────────────────────────────────────────────────────────────
    // Server field is "count" (not "votes").

    [Serializable]
    public class RematchVotePayload
    {
        public int count;
        public int needed;
    }

    // ─── Action Applied ───────────────────────────────────────────────────────

    [Serializable]
    public class ActionAppliedPayload
    {
        public string type;
        public int    laneIndex;
        public int    tick;
        public float  gold;
        public float  income;
    }

    // ─── Eliminated / Spectate ───────────────────────────────────────────────

    [Serializable]
    public class MLPlayerEliminatedPayload
    {
        public int    laneIndex;
        public string displayName;
    }

    [Serializable]
    public class MLSpectatorJoinPayload
    {
        public int laneIndex;
    }

    [Serializable]
    public class MLLaneReassignedPayload
    {
        public int laneIndex;
    }

    // ─── Error ───────────────────────────────────────────────────────────────

    [Serializable]
    public class ErrorPayload
    {
        public string message;
    }

    // ─── Queue & Lobby System (Phase U5) ─────────────────────────────────────

    [Serializable]
    public class QueueStatusPayload
    {
        public string status;       // "queued" | "idle"
        public string mode;         // bucket key e.g. "line_wars:1v1:0"
        public int    elapsed;      // seconds in queue
        public int    queueSize;    // total players in this bucket
    }

    [Serializable]
    public class MatchFoundPayload
    {
        public string   roomCode;
        public int      laneIndex;
        public string   gameType;           // "line_wars" (Forge Wars) | null (legacy)
        public bool     autoStart;
        public string[] teammates;
        public string[] opponents;
        public string   reconnectToken;     // Phase U8 — store for disconnect recovery
        public bool     ranked;
        public string   matchFormat;
    }

    [Serializable]
    public class LobbyMember
    {
        public string socketId;
        public string name;
        public bool   isHost;
        public bool   isReady;
        public string team;         // team color or null
    }

    [Serializable]
    public class LobbyBotSlot
    {
        public string difficulty;
        public int    index;
    }

    [Serializable]
    public class LobbySnapshot
    {
        public string         lobbyId;
        public string         code;
        public string         hostSocketId;
        public string         gameType;       // "line_wars" (Forge Wars)
        public string         matchFormat;    // "1v1" | "2v2" | "ffa"
        public string         pvpMode;        // "teams" | "ffa"
        public LobbyMember[]  members;
        public LobbyBotSlot[] botSlots;
        public string         status;         // "open" | "starting"
    }

    [Serializable]
    public class LobbyCreatedPayload
    {
        public string        lobbyId;
        public string        code;
        public LobbySnapshot lobby;
    }

    [Serializable]
    public class LobbyJoinedPayload
    {
        public string        lobbyId;
        public string        code;
        public LobbySnapshot lobby;
    }

    [Serializable]
    public class LobbyUpdatePayload
    {
        public LobbySnapshot lobby;
    }

    [Serializable]
    public class LobbyLeftPayload
    {
        public string lobbyId;
    }

    [Serializable]
    public class LobbyErrorPayload
    {
        public string message;
    }

    // ─── Catalog (fetched from server on startup via CatalogLoader) ───────────

    [Serializable]
    public class UnitCatalogEntry
    {
        public int    id;           // DB primary key — used for inline loadout selection
        public string key;          // "goblin"|"orc"|etc.
        public string name;         // display name
        public int    send_cost;
        public int    build_cost;   // gold cost to place as a fixed defender / tower
        public float  hp;
        public float  path_speed;
        public bool   enabled;
    }

    [Serializable]
    public class TowerCatalogEntry
    {
        public string key;          // "archer"|"fighter"|etc.
        public string name;         // display name
        public int    build_cost;
        public bool   enabled;
    }

    [Serializable]
    public class BarracksLevelEntry
    {
        public int   level;         // target level (2, 3, 4)
        public int   upgrade_cost;
        public int   req_income;
        public float hp_multiplier;
        public float dmg_multiplier;
    }

    // ─── Rating Update (Phase U8) ─────────────────────────────────────────────
    // Emitted as "rating_update" after a ranked match ends.

    [Serializable]
    public class RatingUpdatePayload
    {
        public float  oldRating;
        public float  newRating;
        public float  delta;        // positive = gain, negative = loss
        public float  newRD;        // new rating deviation (Glicko-2 RD)
        public bool   ranked;
        public string mode;         // e.g. "2v2_ranked"
    }

    // ─── Leaderboard (Phase U8) ───────────────────────────────────────────────
    // Used by LobbyUI for HTTP GET /leaderboard

    [Serializable]
    public class LeaderboardEntry
    {
        public int    rank;
        public string display_name;     // server field
        public float  rating;
        public int    wins;
        public int    losses;
        public string region;
    }

    [Serializable]
    public class LeaderboardSeason
    {
        public int    id;
        public string name;
        public string start_date;
    }

    [Serializable]
    public class LeaderboardResponse
    {
        public LeaderboardSeason  season;
        public LeaderboardEntry[] entries;
        public int                page;
        public int                total;
    }

    // ─── Season (Phase U8) ────────────────────────────────────────────────────
    // Used by LobbyUI for HTTP GET /api/seasons/current

    [Serializable]
    public class SeasonPayload
    {
        public int    id;
        public string name;
        public string end_date;     // ISO date string
        public bool   is_active;
    }
}
