// GameState.cs — C# mirror of ACTUAL server JSON payloads.
// Matches server/index.js event payloads + sim-multilane.js snapshot structure exactly.
// Field names are camelCase to match server JSON. All classes are [Serializable].

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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
        public float        startIncome;
        public int          livesStart;
        public int          teamHpStart;
        public int          buildPhaseTicks;
        public int          transitionPhaseTicks;
        public int          gridW;
        public int          gridH;
        public string       raceId;
        public LoadoutEntry[] loadout;       // 5 unit types for this match
        public string       reconnectToken;  // Phase U8 — store for disconnect recovery
        public bool         ranked;
        public MLBattlefieldTopology battlefieldTopology;
        public MLSlotDefinition[] slotDefinitions;
        public MLFortressBuildingConfig[] fortressBuildingConfigs;
        public MLFortressPadConfig[] fortressPadConfigs;
        public MLBarracksSiteConfig[] barracksSiteConfigs;
        public MLBarracksRosterConfig[] barracksRosterConfigs;
        public MLHeroRosterConfig[] heroRosterConfigs;
        public MLMarketRosterConfig[] marketRosterConfigs;
        public int          barracksRosterRefundPct;
        public int          barracksSendTimerTicks;
        public int          waveTimerTicks;
        public MLMovementTuning movementTuning;
    }

    [Serializable]
    public class MLMovementTuning
    {
        public float baseCombatPathSpeed;
        public float barracksLevelOneSpeedMultiplier;
        public float barracksSpeedUpgradeStep;
        public float waveSpeedUpgradeStep;
        public float serverPathSpeedToUnityMoveSpeedScale;
    }

    [Serializable]
    public class LoadoutEntry
    {
        public int    id;           // DB primary key — required by ml_loadout_confirm
        public string key;          // "runner"|"footman"|etc.
        public string name;         // display name
        public int    send_cost;    // gold cost to send
        public int    build_cost;   // gold cost to place as a defender / tower
        public int    hp;
        public float  path_speed;
        public float  income;        // gold-per-wave income bonus
        public float  attack_damage;
    }

    // ─── Loadout Selection Phase ──────────────────────────────────────────────

    [Serializable]
    public class MLLoadoutPhaseStartPayload
    {
        public string         code;
        public int            timeoutSeconds;   // countdown (25)
        public string         selectionMode;    // "manual" | "random"
        public string         defaultRaceId;
        public string         selectedRaceId;
        public string[]       availableRaceIds;
        public LoadoutEntry[] availableUnits;   // full sendable catalog
    }

    [Serializable]
    public class MLLoadoutPhaseEndPayload
    {
        public string code;
        public string reason;   // "all_confirmed" | "timeout"
    }

    [Serializable]
    public class MLWaveReadyStatePayload
    {
        public int   upcomingWaveNumber;
        public int   requiredReadyCount;
        public int[] eligibleLaneIndices;
        public int[] readyLaneIndices;
        public int   remainingWaveMobCount;
        public bool  currentWaveComplete;
        public bool  allReady;
    }

    [Serializable]
    public class MLWaveStartPayload
    {
        public int roundNumber;
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

    [Serializable]
    public class MLFortressBuildingConfig
    {
        public string buildingType;
        public string displayName;
        public string branchKey;
        public string branchLabel;
        public string progressionCategory;
        public int    maxTier;
        public string[] tierDisplayNames;
        public bool   startsBuilt;
        public int    requiredTownCoreTier;
        public bool   requiresLumberMill;
        public bool   requiresTurretTier3;
        public int    baseMaxHp;
        public int    buildCost;
    }

    [Serializable]
    public class MLFortressPadConfig
    {
        public string padId;
        public string buildingType;
        public string displayName;
        public string branchKey;
        public string branchLabel;
        public int    gridX;
        public int    gridY;
    }

    [Serializable]
    public class MLBarracksRosterConfig
    {
        public string rosterKey;
        public string displayName;
        public string role;
        public string roleLabel;
        public int    sortIndex;
        public string archetypeKey;
        public string presentationKey;
        public string unitTypeKey;
        public string catalogUnitKey;
        public string skinKey;
        public string portraitKey;
        public string branchKey;
        public string branchLabel;
        public string productionBuildingType;
        public string productionBuildingName;
        public int    tier;
        public int    buyCost;
        public int    sellRefund;
        public string unlockBuildingType;
        public string unlockBuildingName;
        public string unlockBuildingTierName;
        public int    requiredBuildingTier;
        public string lockedReason;
    }

    [Serializable]
    public class MLHeroRosterConfig
    {
        public string heroKey;
        public string displayName;
        public string role;
        public string roleLabel;
        public int    sortIndex;
        public string archetypeKey;
        public string presentationKey;
        public string unitTypeKey;
        public string catalogUnitKey;
        public string skinKey;
        public string portraitKey;
        public string branchKey;
        public string branchLabel;
        public string unlockBuildingType;
        public string unlockBuildingName;
        public string unlockBuildingTierName;
        public int    requiredBuildingTier;
        public string summonSourceBuildingType;
        public string summonSourceBuildingName;
        public int    summonCost;
        public int    cooldownTicks;
        public int    activeLimit;
        public string heroVisualStyleKey;
        public string lockedReason;
    }

    [Serializable]
    public class MLMarketRosterConfig
    {
        public string unitKey;
        public string displayName;
        public string role;
        public string roleLabel;
        public int    sortIndex;
        public string archetypeKey;
        public string presentationKey;
        public string skinKey;
        public string portraitKey;
        public string branchKey;
        public string branchLabel;
        public string productionBuildingType;
        public string productionBuildingName;
        public int    tier;
        public string unlockBuildingType;
        public string unlockBuildingName;
        public string unlockBuildingTierName;
        public int    requiredBuildingTier;
        public int    economyLapGold;
        public string routeStartBuildingType;
        public string routeStartBuildingName;
        public string routeEndBuildingType;
        public string routeEndBuildingName;
        public string nextUnitKey;
        public string description;
    }

    [Serializable]
    public class MLBarracksSiteConfig
    {
        public string barracksId;
        public string displayName;
        public string slot;
        public int    sortIndex;
        public int    requiredBarracksTier;
        public bool   startsBuilt;
        public int    buildCost;
        public int    maxLevel;
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
        public string       matchState;             // "active_survival"|"final_game_over"
        public int          officialWinnerLane;
        public bool         continuedIntoSurvival;
        public int          survivalDurationTicks;
        public int          incomeTicksRemaining;   // global, shared by all lanes
        // ── Forge Wars wave defense ──────────────────────────────────────────
        public string       roundState;             // legacy compatibility only
        public int          roundNumber;
        public int          roundStateTicks;
        public int          buildPhaseTotal;
        public int          transitionPhaseTotal;
        public int          waveTimerTicksRemaining;
        public int          waveTimerTotalTicks;
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
        public string         allegianceKey;
        public string         side;
        public string         slotKey;
        public string         slotColor;
        public string         branchId;
        public string         branchLabel;
        public string         castleSide;
        public bool           eliminated;
        public string         commandState;
        public int            commandTargetLaneIndex;
        public float          commandAnchorProgress;
        public MLGridPos      insideGateAnchor;
        public MLGridPos      outsideGateAnchor;
        public MLGridPos      enemyCoreAnchor;
        public MLGridPos      formationAnchor;
        public MLGridPos      formationFacing;
        public MLLaneFormationSlot[] formationSlots;
        public string[]       assignedUnits;
        public MLLanePacketSnap[] packets;
        public float          engagementRadius;
        public bool           combatEnabled;
        public float          gold;
        public float          income;
        public float          buildValue;
        public int            lives; // legacy field; mirrors current Town Core HP
        public int            barracksLevel;
        public MLFortressPad[] fortressPads;
        public MLBarracksSite[] barracksSites;
        public MLBarracksRosterEntry[] barracksRoster;
        public MLHeroRosterEntry[] heroRoster;
        public MLUpcomingWave upcomingWave;
        public MLUpcomingWave[] upcomingWaveQueue;
        public int            barracksSendTimerTicksRemaining;
        public int            barracksSendTimerTotalTicks;
        public MLGridPos[]    path;            // wave path as [{x,y}] array
        public int            fullPathLength;
        public MLUnit[]       units;
        public MLUnit[]       spawnQueueUnits;
        public int            spawnQueueLength;
        public MLProjectile[] projectiles;
    }

    [Serializable]
    public class MLLaneFormationSlot
    {
        public int    slotIndex;
        public string unitId;
        public float  x;
        public float  y;
    }

    [Serializable]
    public class MLLanePacketSnap
    {
        public string                     groupId;
        public int                        laneId;
        public int                        sourceLaneIndex;
        public string                     sourceBarracksId;
        public string                     stance;
        public MLWaypointTarget           currentWaypointTarget;
        public MLGridPos                  groupCenter;
        public float                      cohesionRadius;
        public string                     movementMode;
        public int                        packetIndex;
        public string[]                   assignedUnits;
        public MLLanePacketFormationSlot[] formationSlots;
    }

    [Serializable]
    public class MLWaypointTarget
    {
        public string kind;
        public int    laneIndex;
        public float  x;
        public float  y;
    }

    [Serializable]
    public class MLLanePacketFormationSlot
    {
        public int    slotIndex;
        public string unitId;
        public string band;
        public float  x;
        public float  y;
    }

    [Serializable]
    public class MLUpcomingWave
    {
        public int waveNumber;
        public int totalUnits;
        public MLUpcomingWaveEntry[] entries;
    }

    [Serializable]
    public class MLUpcomingWaveEntry
    {
        public string source;
        public string unitType;
        public string archetypeKey;
        public string presentationKey;
        public string skinKey;
        public int    count;
        public float  hpMult;
        public float  dmgMult;
        public float  speedMult;
        public int    sourceLaneIndex;
        public string sourceBarracksId;
        public bool   isHero;
        public string heroKey;
        public string heroVisualStyleKey;
    }

    [Serializable]
    public class MLFortressPad
    {
        public string padId;
        public string allegianceKey;
        public int    ownerLaneIndex;
        public int    gridX;
        public int    gridY;
        public string buildingType;
        public string buildingName;
        public string displayName;
        public string branchKey;
        public string branchLabel;
        public string buildState;
        public int    tier;
        public int    maxTier;
        public string currentTierName;
        public int    nextTier;
        public string nextTierName;
        public bool   isBuilt;
        public bool   canBuild;
        public bool   canUpgrade;
        public int    buildCost;
        public int    upgradeCost;
        public int    requiredTownCoreTier;
        public string requiredTownCoreTierName;
        public float  hp;
        public float  maxHp;
        public string lockedReason;
    }

    [Serializable]
    public class MLBarracksRosterEntry
    {
        public string rosterKey;
        public string displayName;
        public string role;
        public string roleLabel;
        public int    sortIndex;
        public string archetypeKey;
        public string presentationKey;
        public string unitTypeKey;
        public string catalogUnitKey;
        public string skinKey;
        public string portraitKey;
        public string branchKey;
        public string branchLabel;
        public string productionBuildingType;
        public string productionBuildingName;
        public int    tier;
        public int    ownedCount;
        public int    buyCost;
        public int    sellRefund;
        public bool   unlocked;
        public string unlockBuildingType;
        public string unlockBuildingName;
        public string unlockBuildingTierName;
        public int    requiredBuildingTier;
        public string unlockPadId;
        public string barracksId;
        public string lockedReason;
    }

    [Serializable]
    public class MLBarracksSite
    {
        public string barracksId;
        public string allegianceKey;
        public int    ownerLaneIndex;
        public string displayName;
        public string slot;
        public int    sortIndex;
        public int    requiredBarracksTier;
        public bool   available;
        public bool   isBuilt;
        public int    level;
        public int    maxLevel;
        public string buildState;
        public bool   canBuild;
        public bool   canUpgrade;
        public int    buildCost;
        public int    upgradeCost;
        public int    requiredTownCoreTier;
        public string requiredTownCoreTierName;
        public int    sendIntervalTicks;
        public int    sendTimerTicksRemaining;
        public int    sendTimerTotalTicks;
        public string lockedReason;
        public float  hp;
        public float  maxHp;
        public MLBarracksRosterEntry[] roster;
    }

    [Serializable]
    public class MLHeroRosterEntry
    {
        public string heroKey;
        public string displayName;
        public string role;
        public string roleLabel;
        public int    sortIndex;
        public string archetypeKey;
        public string presentationKey;
        public string unitTypeKey;
        public string catalogUnitKey;
        public string skinKey;
        public string portraitKey;
        public string branchKey;
        public string branchLabel;
        public bool   isHero;
        public bool   unlocked;
        public string unlockBuildingType;
        public string unlockBuildingName;
        public string unlockBuildingTierName;
        public int    requiredBuildingTier;
        public string summonSourceBuildingType;
        public string summonSourceBuildingName;
        public int    summonCost;
        public int    cooldownTicks;
        public int    cooldownReadyTick;
        public int    cooldownTicksRemaining;
        public int    activeLimit;
        public int    activeCount;
        public int    builtBarracksCount;
        public string state;
        public bool   canSummon;
        public string heroVisualStyleKey;
        public string lockedReason;
        public string disabledReason;
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
    public class MLGridPos
    {
        public float x;
        public float y;

        public int XRounded => Mathf.RoundToInt(x);
        public int YRounded => Mathf.RoundToInt(y);
    }

    [Serializable]
    public class MLUnit
    {
        public string id;
        public string unitId;
        public int    laneId;
        public int    ownerLaneIndex;
        public int    targetLaneIndex;
        public int    objectiveLaneIndex;
        public string unitTypeKey;
        public string allegianceKey;
        public string pathContractType;
        public int    ownerLane;    // -1 for wave enemies
        public int    sourceLaneIndex; // -1 when this unit came from the ambient wave instead of a player lane
        public string sourceTeam;
        public string barracksId;
        public string sourceBarracksKey;
        public string sourceBarracksId;
        public string spawnSourceType;
        public string type;         // unit type key
        public string archetypeKey;
        public string presentationKey;
        public string catalogUnitKey;
        public string skinKey;      // null = default skin; otherwise overrides prefab lookup
        public bool   isHero;
        public string heroKey;
        public string heroVisualStyleKey;
        public string groupId;
        public string combatRole;
        public string preferredBand;
        public float  pathIdx;
        public float  gridX;        // float: 2D tile X for defenders; path-derived for wave units
        public float  gridY;        // float: 2D tile Y for defenders; path-derived for wave units
        public float  normProgress; // 0..1 along wave path
        public string routeType;
        public string routeStartNode;
        public string routeTargetNode;
        public string pathId;
        public int    currentWaypointIndex;
        public string nextWaypoint;
        public string currentSegment;
        public float  segmentProgress;
        public string stance;
        public string commandState;
        public string movementMode;
        public string movementState;
        public string state;
        public string presentationPhase;
        public string presentationIntent;
        public bool   blockedByStructure;
        public string blockedByStructureId;
        public float  routeWorldX;
        public float  routeWorldY;
        public int    currentSlotIndex;
        public float  anchorTargetX;
        public float  anchorTargetY;
        public float  anchorTargetProgress;
        public float  groupCenterX;
        public float  groupCenterY;
        public float  cohesionRadius;
        public float  leashFromGroupCenter;
        public float  currentWaypointTargetX;
        public float  currentWaypointTargetY;
        public string currentWaypointTargetKind;
        public float  combatLeashRadius;
        public bool   canEngage;
        public float  hp;
        public float  maxHp;
        public float  moveSpeed;    // authoritative server path speed for combat visuals
        public bool   isWaveUnit;   // true = enemy wave unit; false = player-sent unit
        public bool   isAttacking;  // true when unit has a combat target (stops advancing)
        public string combatTargetKind;
        public string combatTargetId; // unit id or fortress pad id
        public string currentTargetId;
        public bool   combatContact;
        public int    regroupTicksRemaining;
        public int    combatLockTicksRemaining;
        public int    attackPulse;  // increments on each real strike in the sim
        public int    level;        // barracks level (1–4 for player units, 1 for wave units)
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
        public float  fromX;
        public float  fromY;
        public float  toX;
        public float  toY;
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
        // towers dict: legacy classic payload field, deserialized manually if needed
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
    public class MLFinalLaneStat
    {
        public int    laneIndex;
        public string displayName;
        public string team;
        public string side;
        public float  income;
        public float  buildValue;
        public int    gold;
        public float  totalSendSpend;
        public int    totalSendCount;
        public float  totalBuildSpend;
        public int    totalLeaksTaken;
        public int    biggestLeakTaken;
        public int    wavesHeld;
        public int    wavesLeaked;
        public int    longestHoldStreak;
        public int    lives; // legacy field; mirrors current Town Core HP
        public int    teamHp;
        public bool   eliminated;
    }

    [Serializable]
    public class MLWaveLaneStat
    {
        public int   laneIndex;
        public float income;
        public float buildValue;
        public int   gold;
        public int   leaksTaken;
        public int   leakDamage;
        public float sendSpend;
        public int   sendCount;
        public float buildSpend;
        public int   lives; // legacy field; mirrors current Town Core HP
        public int   teamHp;
        public bool  eliminated;
        public string holdResult;
    }

    [Serializable]
    public class MLWaveSnapshot
    {
        public int              round;
        public bool             terminal;
        public int              elapsedSeconds;
        public MLWaveLaneStat[] lanes;
    }

    [Serializable]
    public class MLGameOverPayload
    {
        public int    winnerLaneIndex;       // -1 when survival ends without an explicit winner
        public string winnerName;
        public string winningTeam;
        public string winningSide;
        public string losingTeam;
        public string losingSide;
        public int    finalRound;
        public string matchState;
        // Phase 1 additions
        public int    gameDuration;
        public string causeLoss;
        public MLFinalLaneStat[] finalStats;
        // Phase 2 additions
        public MLWaveSnapshot[]  waveSnapshots;
        public bool   continuedIntoSurvival;
        public int    survivalDuration;
        public int    survivalExtraRounds;
        public int    pvpWinnerLaneIndex;
    }

    [Serializable]
    public class MLPvPResolvedPayload
    {
        public int    winnerLaneIndex;
        public string winnerName;
        public string winningTeam;
        public string winningSide;
        public string losingTeam;
        public string losingSide;
        public int    finalRound;
        public string matchState;
        public int    gameDuration;
        public string causeLoss;
        public MLFinalLaneStat[] finalStats;
        public int[]  winnerLaneIndices;
        public bool   waitingForDecision;
    }

    [Serializable]
    public class MLSurvivalContinuationStartedPayload
    {
        public int    winnerLaneIndex;
        public string winningTeam;
        public string winningSide;
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

    [Serializable]
    public class RematchStatusPayload
    {
        public int      count;
        public int      needed;
        public int[]    acceptedLaneIndices;
        public string[] acceptedDisplayNames;
        public bool     allAccepted;
    }

    [Serializable]
    public class RematchStartingPayload
    {
        public int countdownSeconds;
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
    public class MLAllChatMessagePayload
    {
        public int laneIndex;
        public string displayName;
        public string message;
        public string timestampUtc;
        public string team;
    }

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
        public string   matchFormat;         // "ffa"
    }

    [Serializable]
    public class LobbyMember
    {
        public string socketId;
        public string name;
        public bool   isHost;
        public bool   isReady;
        public string team;         // lane color / identity or null
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
        public string         matchFormat;    // "ffa"
        public string         pvpMode;        // "ffa"
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
        public string description;
        public int    send_cost;
        public int    build_cost;   // gold cost to place as a fixed defender / tower
        public float  income;
        public float  hp;
        public float  attack_damage;
        public float  attack_speed;
        public float  range;
        public float  path_speed;
        public string damage_type;
        public string armor_type;
        public float  damage_reduction_pct;
        public bool   enabled;
        public string canonical_skin_key;
        public string canonical_unit_type;
        public string proj_behavior;
        public JToken proj_behavior_params;
        public JToken special_props;
        public UnitAbilityAssignmentEntry[] abilities;
        public RemoteContentEntry remote_content;
    }

    [Serializable]
    public class UnitAbilityAssignmentEntry
    {
        public string ability_key;
        public JToken @params;
    }

    [Serializable]
    public class RemoteContentEntry
    {
        public int      id;
        public string   content_key;
        public string   addressables_label;
        public string   prefab_address;
        public string   placeholder_key;
        public string   catalog_url;
        public string   content_url;
        public string   version_tag;
        public string   content_hash;
        public string[] dependency_keys;
        public bool     is_critical;
        public bool     enabled;
        public string   tier;
        public string   preload_reason;
    }

    [Serializable]
    public class ContentManifestEntry
    {
        public string             key;
        public string             name;
        public bool               enabled;
        public string             usage_scope;
        public string             unit_type;
        public string             portrait_key;
        public string             prefab_key;
        public string             content_kind;
        public RemoteContentEntry remote_content;
    }

    [Serializable]
    public class ContentManifestSkinEntry
    {
        public string             skin_key;
        public string             unit_type;
        public string             name;
        public string             usage_scope;
        public RemoteContentEntry remote_content;
    }

    [Serializable]
    public class CriticalContentEntry
    {
        public string kind;
        public string key;
        public string content_key;
        public string tier;
        public string address;
        public string reason;
    }

    [Serializable]
    public class ContentManifestResponse
    {
        public int                        manifest_version;
        public string                     generated_at;
        public ContentManifestEntry[]     units;
        public ContentManifestSkinEntry[] skins;
        public CriticalContentEntry[]     t0_content;
        public CriticalContentEntry[]     t1_content;
        public CriticalContentEntry[]     critical_content;
        public CriticalContentEntry[]     loadout_critical_content;
        public CriticalContentEntry[]     wave_critical_content;
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
        public float multiplier;
        public string notes;
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

    // ─── Phase-based readiness protocol ──────────────────────────────────────

    [Serializable]
    public class MLPlayerPreparationState
    {
        public int    laneIndex;
        public string displayName;
        public bool   loadoutReady;
        public bool   gameplayReady;
        public float  contentPercent;   // 0–1
        public string contentState;     // human-readable status, e.g. "Downloading battlefield"
    }

    [Serializable]
    public class MLMatchPreparationStatePayload
    {
        public MLPlayerPreparationState[] players;
    }

    [Serializable]
    public class MLMatchCancelledPayload
    {
        public string code;
        public string reason;
        public string message;
    }
}
