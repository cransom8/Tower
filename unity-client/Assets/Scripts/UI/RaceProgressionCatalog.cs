using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.UI
{
    public enum RaceProgressionLaneLayout
    {
        Linear,
        BuildingStepsToOutcomeCards,
    }

    public enum RaceProgressionLaneSection
    {
        Units,
        Buildings,
    }

    public enum RaceProgressionTab
    {
        Units,
        Buildings,
        Siege,
        Abilities,
    }

    public enum RaceProgressionLaneCategory
    {
        None,
        BaseTower,
        ArcherTower,
    }

    public enum RaceProgressionUnitCardStyle
    {
        Standard,
        UpgradeStep,
        HeroOutcome,
        BuildingTier,
        RequirementStep,
    }

    public sealed class RaceProgressionCardDisplayDefinition
    {
        public RaceProgressionCardDisplayDefinition(
            string buildingType,
            string tierLabel,
            string timeText,
            int cost,
            string imageResourcePath = null)
        {
            BuildingType = buildingType;
            TierLabel = tierLabel;
            TimeText = timeText;
            Cost = Mathf.Max(0, cost);
            ImageResourcePath = imageResourcePath;
        }

        public string BuildingType { get; }
        public string TierLabel { get; }
        public string TimeText { get; }
        public int Cost { get; }
        public string ImageResourcePath { get; }
    }

    public sealed class RaceProgressionRequirementDefinition
    {
        public RaceProgressionRequirementDefinition(
            string buildingType,
            string buildingName,
            int requiredTier,
            string padId = null)
        {
            BuildingType = buildingType;
            BuildingName = buildingName;
            RequiredTier = Mathf.Max(1, requiredTier);
            PadId = padId;
        }

        public string BuildingType { get; }
        public string BuildingName { get; }
        public int RequiredTier { get; }
        public string PadId { get; }

        public string Label => string.IsNullOrWhiteSpace(BuildingName)
            ? $"Tier {RequiredTier}"
            : $"{BuildingName} T{RequiredTier}";
    }

    public sealed class RaceProgressionUnitDefinition
    {
        public RaceProgressionUnitDefinition(
            string id,
            string laneId,
            string displayName,
            string catalogKey,
            string portraitKey,
            bool startsUnlocked,
            RaceProgressionRequirementDefinition unlockRequirement,
            string description,
            string nextUnitId = null,
            string statsSummary = null,
            RaceProgressionUnitCardStyle cardStyle = RaceProgressionUnitCardStyle.Standard,
            string cardTag = null,
            RaceProgressionCardDisplayDefinition cardDisplay = null,
            bool suppressInlineRequirementCard = false,
            string imageResourcePath = null)
        {
            Id = id;
            LaneId = laneId;
            DisplayName = displayName;
            CatalogKey = catalogKey;
            PortraitKey = portraitKey;
            StartsUnlocked = startsUnlocked;
            UnlockRequirement = unlockRequirement;
            Description = description;
            NextUnitId = nextUnitId;
            StatsSummary = statsSummary;
            CardStyle = cardStyle;
            CardTag = cardTag;
            CardDisplay = cardDisplay;
            SuppressInlineRequirementCard = suppressInlineRequirementCard;
            ImageResourcePath = imageResourcePath;
        }

        public string Id { get; }
        public string LaneId { get; }
        public string DisplayName { get; }
        public string CatalogKey { get; }
        public string PortraitKey { get; }
        public bool StartsUnlocked { get; }
        public RaceProgressionRequirementDefinition UnlockRequirement { get; }
        public string Description { get; }
        public string NextUnitId { get; }
        public string StatsSummary { get; }
        public RaceProgressionUnitCardStyle CardStyle { get; }
        public string CardTag { get; }
        public RaceProgressionCardDisplayDefinition CardDisplay { get; }
        public bool SuppressInlineRequirementCard { get; }
        public string ImageResourcePath { get; }

        public string RequirementLabel => UnlockRequirement?.Label;
        public bool IsStartUnit => StartsUnlocked;
    }

    public sealed class RaceProgressionLaneDefinition
    {
        public RaceProgressionLaneDefinition(string id, string label, params RaceProgressionUnitDefinition[] units)
            : this(id, label, RaceProgressionLaneLayout.Linear, null, RaceProgressionLaneSection.Units, RaceProgressionTab.Units, true, null, RaceProgressionLaneCategory.None, false, false, units)
        {
        }

        public RaceProgressionLaneDefinition(
            string id,
            string label,
            RaceProgressionLaneLayout layout,
            RaceProgressionUnitDefinition[] outcomeUnits,
            params RaceProgressionUnitDefinition[] units)
            : this(id, label, layout, outcomeUnits, RaceProgressionLaneSection.Units, RaceProgressionTab.Units, true, null, RaceProgressionLaneCategory.None, false, false, units)
        {
        }

        public RaceProgressionLaneDefinition(
            string id,
            string label,
            RaceProgressionLaneLayout layout,
            RaceProgressionUnitDefinition[] outcomeUnits,
            RaceProgressionLaneSection section = RaceProgressionLaneSection.Units,
            RaceProgressionTab tab = RaceProgressionTab.Units,
            bool showRequirementCards = true,
            string summaryOverride = null,
            params RaceProgressionUnitDefinition[] units)
            : this(id, label, layout, outcomeUnits, section, tab, showRequirementCards, summaryOverride, RaceProgressionLaneCategory.None, false, false, units)
        {
        }

        public RaceProgressionLaneDefinition(
            string id,
            string label,
            RaceProgressionLaneLayout layout,
            RaceProgressionUnitDefinition[] outcomeUnits,
            RaceProgressionLaneSection section,
            RaceProgressionTab tab,
            bool showRequirementCards,
            string summaryOverride,
            RaceProgressionLaneCategory progressionCategory,
            bool requiresLumberMill,
            bool requiresTurretTier3,
            params RaceProgressionUnitDefinition[] units)
        {
            Id = id;
            Label = label;
            Layout = layout;
            Section = section;
            Tab = tab;
            ShowRequirementCards = showRequirementCards;
            SummaryOverride = summaryOverride;
            ProgressionCategory = progressionCategory;
            RequiresLumberMill = requiresLumberMill;
            RequiresTurretTier3 = requiresTurretTier3;
            Units = units ?? Array.Empty<RaceProgressionUnitDefinition>();
            OutcomeUnits = outcomeUnits ?? Array.Empty<RaceProgressionUnitDefinition>();
        }

        public string Id { get; }
        public string Label { get; }
        public RaceProgressionLaneLayout Layout { get; }
        public RaceProgressionLaneSection Section { get; }
        public RaceProgressionTab Tab { get; }
        public bool ShowRequirementCards { get; }
        public string SummaryOverride { get; }
        public RaceProgressionLaneCategory ProgressionCategory { get; }
        public bool RequiresLumberMill { get; }
        public bool RequiresTurretTier3 { get; }
        public RaceProgressionUnitDefinition[] Units { get; }
        public RaceProgressionUnitDefinition[] OutcomeUnits { get; }
    }

    public sealed class RaceProgressionDefinition
    {
        readonly Dictionary<string, RaceProgressionUnitDefinition> _unitsById;
        readonly Dictionary<string, RaceProgressionLaneDefinition> _lanesById;

        public RaceProgressionDefinition(
            string id,
            string displayName,
            string featuredPortraitKey,
            string featuredTitle,
            string summary,
            string[] matchLoadoutKeys,
            params RaceProgressionLaneDefinition[] lanes)
        {
            Id = id;
            DisplayName = displayName;
            FeaturedPortraitKey = featuredPortraitKey;
            FeaturedTitle = featuredTitle;
            Summary = summary;
            MatchLoadoutKeys = matchLoadoutKeys ?? Array.Empty<string>();
            Lanes = lanes ?? Array.Empty<RaceProgressionLaneDefinition>();

            var units = new List<RaceProgressionUnitDefinition>();
            _unitsById = new Dictionary<string, RaceProgressionUnitDefinition>(StringComparer.OrdinalIgnoreCase);
            _lanesById = new Dictionary<string, RaceProgressionLaneDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int laneIndex = 0; laneIndex < Lanes.Length; laneIndex++)
            {
                var lane = Lanes[laneIndex];
                if (lane == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(lane.Id))
                    _lanesById[lane.Id] = lane;

                for (int unitIndex = 0; unitIndex < lane.Units.Length; unitIndex++)
                {
                    var unit = lane.Units[unitIndex];
                    if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
                        continue;

                    units.Add(unit);
                    _unitsById[unit.Id] = unit;
                }

                for (int outcomeIndex = 0; outcomeIndex < lane.OutcomeUnits.Length; outcomeIndex++)
                {
                    var unit = lane.OutcomeUnits[outcomeIndex];
                    if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
                        continue;

                    units.Add(unit);
                    _unitsById[unit.Id] = unit;
                }
            }

            Units = units.ToArray();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string FeaturedPortraitKey { get; }
        public string FeaturedTitle { get; }
        public string Summary { get; }
        public string[] MatchLoadoutKeys { get; }
        public RaceProgressionLaneDefinition[] Lanes { get; }
        public RaceProgressionUnitDefinition[] Units { get; }

        public bool TryGetUnit(string unitId, out RaceProgressionUnitDefinition unit)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                unit = null;
                return false;
            }

            return _unitsById.TryGetValue(unitId.Trim(), out unit);
        }

        public bool TryGetLane(string laneId, out RaceProgressionLaneDefinition lane)
        {
            if (string.IsNullOrWhiteSpace(laneId))
            {
                lane = null;
                return false;
            }

            return _lanesById.TryGetValue(laneId.Trim(), out lane);
        }
    }

    public static class RaceProgressionCatalog
    {
        public const string HumansRaceId = "humans";

        static readonly RaceProgressionDefinition[] _races =
        {
            BuildHumansRace(),
        };

        static readonly Dictionary<string, RaceProgressionDefinition> _racesById =
            BuildRaceLookup(_races);

        static Dictionary<string, RaceProgressionDefinition> BuildRaceLookup(IEnumerable<RaceProgressionDefinition> races)
        {
            var lookup = new Dictionary<string, RaceProgressionDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var race in races)
            {
                if (race == null || string.IsNullOrWhiteSpace(race.Id))
                    continue;

                lookup[race.Id] = race;
            }

            return lookup;
        }

        static RaceProgressionRequirementDefinition Requirement(string buildingType, string buildingName, int requiredTier, string padId)
        {
            return new RaceProgressionRequirementDefinition(buildingType, buildingName, requiredTier, padId);
        }

        static RaceProgressionDefinition BuildHumansRace()
        {
            const string civicLane = "civic";
            const string blacksmithInfantryLane = "blacksmith_infantry";
            const string blacksmithPolearmLane = "blacksmith_polearm";
            const string blacksmithShieldLane = "blacksmith_shield";
            const string templeLane = "temple";
            const string wizardLane = "wizard";
            const string archeryLane = "archery";
            const string marketLane = "market";
            const string stableHorseLane = "stable_horses";
            const string buildingCivicLane = "buildings_civic";
            const string buildingBarracksLane = "buildings_barracks";
            const string buildingBlacksmithLane = "buildings_blacksmith";
            const string buildingTempleLane = "buildings_temple";
            const string buildingWizardLane = "buildings_wizard_tower";
            const string buildingArcheryLane = "buildings_archery_tower";
            const string buildingStableLane = "buildings_stable";
            const string buildingWorkshopLane = "buildings_workshop";
            const string buildingLibraryLane = "buildings_library";
            const string buildingMarketLane = "buildings_market";
            const string buildingLumberMillLane = "buildings_lumber_mill";
            const string buildingWallsLane = "buildings_walls";
            const string buildingBaseTowerLane = "buildings_base_towers";
            const string buildingArcherTowerLane = "buildings_archer_towers";
            const string siegeTier1Lane = "siege_tier1";
            const string siegeTier2Lane = "siege_tier2";
            const string abilityTier1Lane = "abilities_tier1";
            const string abilityTier2Lane = "abilities_tier2";
            const string abilityTier3Lane = "abilities_tier3";
            const string abilitySupportLane = "abilities_support";

            var civicT2 = Requirement("town_core", "Civic", 2, "town_core_pad");
            var civicT3 = Requirement("town_core", "Civic", 3, "town_core_pad");
            var civicT4 = Requirement("town_core", "Civic", 4, "town_core_pad");
            var castleHeroUnlock = Requirement("town_core", "Castle", 4, "town_core_pad");
            var barracksT1 = Requirement("barracks", "Barracks", 1, "barracks_pad");
            var blacksmithT1 = Requirement("blacksmith", "Blacksmith", 1, "blacksmith_pad");
            var blacksmithT2 = Requirement("blacksmith", "Blacksmith", 2, "blacksmith_pad");
            var blacksmithT3 = Requirement("blacksmith", "Blacksmith", 3, "blacksmith_pad");
            var templeT1 = Requirement("temple", "Temple", 1, "temple_pad");
            var templeT2 = Requirement("temple", "Temple", 2, "temple_pad");
            var templeT3 = Requirement("temple", "Temple", 3, "temple_pad");
            var wizardT1 = Requirement("wizard_tower", "Wizard Tower", 1, "wizard_tower_pad");
            var wizardT2 = Requirement("wizard_tower", "Wizard Tower", 2, "wizard_tower_pad");
            var wizardT3 = Requirement("wizard_tower", "Wizard Tower", 3, "wizard_tower_pad");
            var archeryT1 = Requirement("archery_tower", "Archery Tower", 1, "archery_tower_pad");
            var archeryT2 = Requirement("archery_tower", "Archery Tower", 2, "archery_tower_pad");
            var archeryT3 = Requirement("archery_tower", "Archery Tower", 3, "archery_tower_pad");
            var marketT1 = Requirement("market", "Market", 1, null);
            var marketT2 = Requirement("market", "Market", 2, null);
            var marketT3 = Requirement("market", "Market", 3, null);
            var townHallRequirement = Requirement("town_core", "Town Hall", 2, "town_core_pad");
            var keepRequirement = Requirement("town_core", "Keep", 3, "town_core_pad");
            var castleRequirement = Requirement("town_core", "Castle", 4, "town_core_pad");
            var stableT1 = Requirement("stable", "Stable", 1, "stable_pad");
            var stableT2 = Requirement("stable", "Stable", 2, "stable_pad");
            var stableT3 = Requirement("stable", "Stable", 3, "stable_pad");
            var workshopT1 = Requirement("workshop", "Workshop", 1, "workshop_pad");
            var workshopT2 = Requirement("workshop", "Workshop", 2, "workshop_pad");
            var workshopT3 = Requirement("workshop", "Workshop", 3, "workshop_pad");
            var libraryT1 = Requirement("library", "Library", 1, "library_pad");
            var libraryT2 = Requirement("library", "Library", 2, "library_pad");
            var libraryT3 = Requirement("library", "Library", 3, "library_pad");
            var lumberMillT1Requirement = Requirement("lumber_mill", "Lumber Mill", 1, null);
            var lumberMillT2Requirement = Requirement("lumber_mill", "Lumber Mill", 2, null);
            var lumberMillT3Requirement = Requirement("lumber_mill", "Lumber Mill", 3, null);
            var turretT1Requirement = Requirement("turret", "Turret", 1, null);
            var turretT2Requirement = Requirement("turret", "Turret", 2, null);
            var turretT3Requirement = Requirement("turret", "Turret", 3, null);
            var archerTowerT1Requirement = Requirement("tower_archer", "Archer Tower", 1, null);
            var archerTowerT2Requirement = Requirement("tower_archer", "Archer Tower", 2, null);

            const int placeholderBuildingTier1Cost = 60;
            const int placeholderBuildingTier2Cost = 100;
            const int placeholderBuildingTier3Cost = 150;
            const int marketTier1Cost = 60;
            const int marketTier2Cost = 100;
            const int marketTier3Cost = 150;
            const int placeholderLumberMillTier1Cost = 50;
            const int placeholderLumberMillTier2Cost = 80;
            const int placeholderLumberMillTier3Cost = 125;
            const int placeholderWallTier1Cost = 20;
            const int placeholderWallTier2Cost = 40;
            const int placeholderWallTier3Cost = 80;
            const int placeholderTurret1Cost = 40;
            const int placeholderTurret2Cost = 80;
            const int placeholderTurret3Cost = 140;
            const int placeholderArcherTower1Cost = 180;
            const int placeholderArcherTower2Cost = 260;
            const int placeholderArcherTower3Cost = 380;
            const string feetAuraVisualNote = "Visual indicator: a slight colored circle around the unit's feet only. No full-body glow.";

            const string baseTowerDescription =
                "Requires Lumber Mill before construction. Towers are empty structures by default. " +
                "After the Archery Tower building is unlocked, this tower can purchase an Archer to man the tower. " +
                "Archers are purchased per tower and are assigned to the selected tower only.";
            const string archerTowerDescription =
                "Requires Turret Tier 3 before construction. Towers are empty structures by default. " +
                "After the Archery Tower building is unlocked, this tower can purchase an Archer to man the tower. " +
                "Archers are purchased per tower and are assigned to the selected tower only.";

            static string GeneratedBuildingArt(string cardId)
            {
                return string.IsNullOrWhiteSpace(cardId)
                    ? null
                    : $"TechTree/Buildings/{cardId.Trim()}";
            }

            RaceProgressionUnitDefinition BuildingCard(
                string id,
                string laneId,
                string displayName,
                string buildingType,
                string tierLabel,
                string timeText,
                int cost,
                bool startsUnlocked,
                RaceProgressionRequirementDefinition unlockRequirement,
                string description,
                string nextUnitId = null,
                string imageResourcePath = null)
            {
                imageResourcePath ??= GeneratedBuildingArt(id);
                return new RaceProgressionUnitDefinition(
                    id,
                    laneId,
                    displayName,
                    catalogKey: null,
                    portraitKey: null,
                    startsUnlocked: startsUnlocked,
                    unlockRequirement: unlockRequirement,
                    description: description,
                    nextUnitId: nextUnitId,
                    statsSummary: tierLabel,
                    cardStyle: RaceProgressionUnitCardStyle.BuildingTier,
                    cardTag: "Building Upgrade",
                    cardDisplay: new RaceProgressionCardDisplayDefinition(buildingType, tierLabel, timeText, cost, imageResourcePath));
            }

            RaceProgressionUnitDefinition RequirementStepCard(
                string id,
                string laneId,
                string displayName,
                RaceProgressionRequirementDefinition unlockRequirement,
                string description,
                string nextUnitId = null)
            {
                return new RaceProgressionUnitDefinition(
                    id,
                    laneId,
                    displayName,
                    catalogKey: null,
                    portraitKey: null,
                    startsUnlocked: false,
                    unlockRequirement: unlockRequirement,
                    description: description,
                    nextUnitId: nextUnitId,
                    statsSummary: unlockRequirement?.Label,
                    cardStyle: RaceProgressionUnitCardStyle.RequirementStep,
                    cardTag: "Building Requirement");
            }

            RaceProgressionLaneDefinition StandardBuildingLane(
                string laneId,
                string label,
                string buildingType,
                int tier1Cost,
                int tier2Cost,
                int tier3Cost)
            {
                return new RaceProgressionLaneDefinition(
                    laneId,
                    label,
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Buildings,
                    RaceProgressionTab.Buildings,
                    true,
                    "T1 immediate   Town Hall -> T2   Keep -> T3",
                    BuildingCard(
                        $"{laneId}_tier1",
                        laneId,
                        label,
                        buildingType,
                        "Tier 1",
                        "20s",
                        tier1Cost,
                        startsUnlocked: false,
                        unlockRequirement: null,
                        description: $"{label} Tier 1 building card. Tier 1 is available immediately in the Phase 1 tech tree view.",
                        nextUnitId: $"{laneId}_tier2"),
                    BuildingCard(
                        $"{laneId}_tier2",
                        laneId,
                        label,
                        buildingType,
                        "Tier 2",
                        "40s",
                        tier2Cost,
                        startsUnlocked: false,
                        unlockRequirement: townHallRequirement,
                        description: $"{label} Tier 2 upgrade. Requires Town Hall in the Phase 1 building tree.",
                        nextUnitId: $"{laneId}_tier3"),
                    BuildingCard(
                        $"{laneId}_tier3",
                        laneId,
                        label,
                        buildingType,
                        "Tier 3",
                        "80s",
                        tier3Cost,
                        startsUnlocked: false,
                        unlockRequirement: keepRequirement,
                        description: $"{label} Tier 3 upgrade. Requires Keep in the Phase 1 building tree."));
            }

            RaceProgressionLaneDefinition TownCoreGatedBuildingLane(
                string laneId,
                string label,
                string buildingType,
                int tier1Cost,
                int tier2Cost,
                int tier3Cost,
                string tier1Description,
                string tier2Description,
                string tier3Description,
                string summaryOverride)
            {
                return new RaceProgressionLaneDefinition(
                    laneId,
                    label,
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Buildings,
                    RaceProgressionTab.Buildings,
                    true,
                    summaryOverride,
                    BuildingCard(
                        $"{laneId}_tier1",
                        laneId,
                        $"{label} Tier 1",
                        buildingType,
                        "Tier 1",
                        "20s",
                        tier1Cost,
                        startsUnlocked: false,
                        unlockRequirement: townHallRequirement,
                        description: tier1Description,
                        nextUnitId: $"{laneId}_tier2"),
                    BuildingCard(
                        $"{laneId}_tier2",
                        laneId,
                        $"{label} Tier 2",
                        buildingType,
                        "Tier 2",
                        "40s",
                        tier2Cost,
                        startsUnlocked: false,
                        unlockRequirement: keepRequirement,
                        description: tier2Description,
                        nextUnitId: $"{laneId}_tier3"),
                    BuildingCard(
                        $"{laneId}_tier3",
                        laneId,
                        $"{label} Tier 3",
                        buildingType,
                        "Tier 3",
                        "80s",
                        tier3Cost,
                        startsUnlocked: false,
                        unlockRequirement: castleRequirement,
                        description: tier3Description));
            }

            return new RaceProgressionDefinition(
                HumansRaceId,
                "Humans",
                featuredPortraitKey: "tt_king",
                featuredTitle: "King",
                summary: "Human progression is building-based. Civic advances House to Castle for hero access, Barracks unlocks Militia, Blacksmith upgrades unlock the melee branches, Archery Tower tiers unlock the ranged branch, Lumber Mill gates the Base Tower row, Turret Tier 3 opens the Archer Tower row, Temple and Wizard Tower unlock support and arcane branches, and Market tiers unlock economy units.",
                matchLoadoutKeys: new[] { "tt_peasant", "tt_spearman", "tt_archer", "tt_priest", "tt_light_infantry" },
                new RaceProgressionLaneDefinition(
                    civicLane,
                    "Civic",
                    RaceProgressionLaneLayout.BuildingStepsToOutcomeCards,
                    new[]
                    {
                        new RaceProgressionUnitDefinition(
                            "king",
                            civicLane,
                            "King",
                            "tt_king",
                            "tt_king",
                            startsUnlocked: false,
                            unlockRequirement: castleHeroUnlock,
                            description: "Castle hero unlock. Summon the King from a Barracks once the civic branch reaches Castle.",
                            statsSummary: "Hero Tank   Barracks Summon   70g",
                            cardStyle: RaceProgressionUnitCardStyle.HeroOutcome,
                            cardTag: "Castle Hero"),
                        new RaceProgressionUnitDefinition(
                            "paladin",
                            civicLane,
                            "Paladin",
                            "tt_paladin",
                            "tt_paladin",
                            startsUnlocked: false,
                            unlockRequirement: castleHeroUnlock,
                            description: "Castle hero unlock. Summon the Paladin from a Barracks after reaching Castle.",
                            statsSummary: "Hero Frontline   Barracks Summon   60g",
                            cardStyle: RaceProgressionUnitCardStyle.HeroOutcome,
                            cardTag: "Castle Hero"),
                        new RaceProgressionUnitDefinition(
                            "bishop",
                            civicLane,
                            "Bishop",
                            "tt_commander",
                            "tt_commander",
                            startsUnlocked: false,
                            unlockRequirement: castleHeroUnlock,
                            description: "Castle hero unlock. Summon the Bishop from a Barracks for support once Castle is complete.",
                            statsSummary: "Hero Support   Barracks Summon   55g",
                            cardStyle: RaceProgressionUnitCardStyle.HeroOutcome,
                            cardTag: "Castle Hero"),
                    },
                    new RaceProgressionUnitDefinition(
                        "house",
                        civicLane,
                        "House",
                        null,
                        "tt_king",
                        startsUnlocked: true,
                        unlockRequirement: null,
                        description: "Opening civic tier. The city starts here and grows upward toward Castle.",
                        nextUnitId: "town_hall",
                        statsSummary: "Civic Tier 1",
                        cardStyle: RaceProgressionUnitCardStyle.UpgradeStep,
                        cardDisplay: new RaceProgressionCardDisplayDefinition("town_core", "Civic Tier 1", "2:00", 0, GeneratedBuildingArt("building_house"))),
                    new RaceProgressionUnitDefinition(
                        "town_hall",
                        civicLane,
                        "Town Hall",
                        null,
                        "tt_paladin",
                        startsUnlocked: false,
                        unlockRequirement: civicT2,
                        description: "Second civic tier. Pushes the city closer to Keep and Castle.",
                        nextUnitId: "keep",
                        statsSummary: "Civic Tier 2",
                        cardStyle: RaceProgressionUnitCardStyle.UpgradeStep,
                        cardDisplay: new RaceProgressionCardDisplayDefinition("town_core", "Civic Tier 2", "2:30", 70, GeneratedBuildingArt("building_town_hall"))),
                    new RaceProgressionUnitDefinition(
                        "keep",
                        civicLane,
                        "Keep",
                        null,
                        "tt_commander",
                        startsUnlocked: false,
                        unlockRequirement: civicT3,
                        description: "Third civic tier. One more upgrade reaches Castle and hero access.",
                        nextUnitId: "castle",
                        statsSummary: "Civic Tier 3",
                        cardStyle: RaceProgressionUnitCardStyle.UpgradeStep,
                        cardDisplay: new RaceProgressionCardDisplayDefinition("town_core", "Civic Tier 3", "3:00", 110, GeneratedBuildingArt("building_keep"))),
                    new RaceProgressionUnitDefinition(
                        "castle",
                        civicLane,
                        "Castle",
                        null,
                        "tt_king",
                        startsUnlocked: false,
                        unlockRequirement: civicT4,
                        description: "Final civic tier. Reaching Castle unlocks King, Paladin, and Bishop summons in the Barracks.",
                        statsSummary: "Civic Tier 4",
                        cardStyle: RaceProgressionUnitCardStyle.UpgradeStep,
                        cardDisplay: new RaceProgressionCardDisplayDefinition("town_core", "Civic Tier 4", "3:30", 165, GeneratedBuildingArt("building_castle")))
                ),
                new RaceProgressionLaneDefinition(
                    blacksmithInfantryLane,
                    "Infantry",
                    new RaceProgressionUnitDefinition(
                        "militia",
                        blacksmithInfantryLane,
                        "Militia",
                        "tt_peasant",
                        "tt_peasant",
                        startsUnlocked: false,
                        unlockRequirement: barracksT1,
                        description: "Default Barracks infantry. Cheap frontline bodies become available as soon as a Barracks is built.",
                        nextUnitId: "swordsman"),
                    new RaceProgressionUnitDefinition(
                        "swordsman",
                        blacksmithInfantryLane,
                        "Swordsman",
                        "tt_light_infantry",
                        "tt_light_infantry",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT2,
                        description: "Blacksmith Tier 2 infantry. A cleaner frontline upgrade with more damage and staying power.",
                        nextUnitId: "knight"),
                    new RaceProgressionUnitDefinition(
                        "knight",
                        blacksmithInfantryLane,
                        "Knight",
                        "tt_mounted_knight",
                        "tt_mounted_knight",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT3,
                        description: "Blacksmith Tier 3 infantry. Elite cavalry pressure from the melee branch.")
                ),
                new RaceProgressionLaneDefinition(
                    archeryLane,
                    "Archery",
                    RequirementStepCard(
                        "archery_tier1_gate",
                        archeryLane,
                        "Archery Tower",
                        archeryT1,
                        "Archery Tower Tier 1 gate for the archery line. Building it unlocks Archer.",
                        nextUnitId: "archer"),
                    new RaceProgressionUnitDefinition(
                        "archer",
                        archeryLane,
                        "Archer",
                        "tt_archer",
                        "tt_archer",
                        startsUnlocked: false,
                        unlockRequirement: archeryT1,
                        description: "Archery Tower Tier 1 ranged unit. Baseline ranged pressure once the tower is built.",
                        nextUnitId: "crossbowman",
                        suppressInlineRequirementCard: true),
                    new RaceProgressionUnitDefinition(
                        "crossbowman",
                        archeryLane,
                        "Crossbowman",
                        "tt_crossbowman",
                        "tt_crossbowman",
                        startsUnlocked: false,
                        unlockRequirement: archeryT2,
                        description: "Archery Tower Tier 2 ranged unit. Heavier shots and stronger back-line threat.",
                        nextUnitId: "ranger"),
                    new RaceProgressionUnitDefinition(
                        "ranger",
                        archeryLane,
                        "Ranger",
                        "tt_mounted_scout",
                        "tt_mounted_scout",
                        startsUnlocked: false,
                        unlockRequirement: archeryT3,
                        description: "Archery Tower Tier 3 ranged unit. Late-game skirmisher for the ranged branch.")
                ),
                new RaceProgressionLaneDefinition(
                    blacksmithPolearmLane,
                    "Polearm",
                    RequirementStepCard(
                        "polearm_tier1_gate",
                        blacksmithPolearmLane,
                        "Blacksmith",
                        blacksmithT1,
                        "Blacksmith Tier 1 gate for the polearm line. Building it unlocks Spearman.",
                        nextUnitId: "spearman"),
                    new RaceProgressionUnitDefinition(
                        "spearman",
                        blacksmithPolearmLane,
                        "Spearman",
                        "tt_spearman",
                        "tt_spearman",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT1,
                        description: "Blacksmith Tier 1 polearm unit. Reach-focused frontline support.",
                        nextUnitId: "halberdier",
                        suppressInlineRequirementCard: true),
                    new RaceProgressionUnitDefinition(
                        "halberdier",
                        blacksmithPolearmLane,
                        "Halberdier",
                        "tt_halberdier",
                        "tt_halberdier",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT2,
                        description: "Blacksmith Tier 2 polearm unit. Better reach and anti-armor pressure.",
                        nextUnitId: "lancer"),
                    new RaceProgressionUnitDefinition(
                        "lancer",
                        blacksmithPolearmLane,
                        "Lancer",
                        "tt_light_cavalry",
                        "tt_light_cavalry",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT3,
                        description: "Blacksmith Tier 3 polearm cavalry. Fast pressure with long reach.")
                ),
                new RaceProgressionLaneDefinition(
                    blacksmithShieldLane,
                    "Shield",
                    RequirementStepCard(
                        "shield_tier1_gate",
                        blacksmithShieldLane,
                        "Blacksmith",
                        blacksmithT1,
                        "Blacksmith Tier 1 gate for the shield line. Building it unlocks Shieldman.",
                        nextUnitId: "shieldman"),
                    new RaceProgressionUnitDefinition(
                        "shieldman",
                        blacksmithShieldLane,
                        "Shieldman",
                        "tt_heavy_infantry",
                        "tt_heavy_infantry",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT1,
                        description: "Blacksmith Tier 1 shield line. Defensive anchor for the melee branch.",
                        nextUnitId: "shield_guard",
                        suppressInlineRequirementCard: true),
                    new RaceProgressionUnitDefinition(
                        "shield_guard",
                        blacksmithShieldLane,
                        "Shield Guard",
                        "tt_heavy_swordman",
                        "tt_heavy_swordman",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT2,
                        description: "Blacksmith Tier 2 shield line. Heavy infantry that stabilizes the frontline.",
                        nextUnitId: "guardian"),
                    new RaceProgressionUnitDefinition(
                        "guardian",
                        blacksmithShieldLane,
                        "Guardian",
                        "tt_heavy_cavalry",
                        "tt_heavy_cavalry",
                        startsUnlocked: false,
                        unlockRequirement: blacksmithT3,
                        description: "Blacksmith Tier 3 shield line. Premium defensive cavalry for late-game pushes.")
                ),
                new RaceProgressionLaneDefinition(
                    templeLane,
                    "Temple",
                    RequirementStepCard(
                        "temple_tier1_gate",
                        templeLane,
                        "Temple",
                        templeT1,
                        "Temple Tier 1 gate for the support line. Building it unlocks Cleric.",
                        nextUnitId: "cleric"),
                    new RaceProgressionUnitDefinition(
                        "cleric",
                        templeLane,
                        "Cleric",
                        "tt_mounted_priest",
                        "tt_mounted_priest",
                        startsUnlocked: false,
                        unlockRequirement: templeT1,
                        description: "Temple Tier 1 support. The first healing and sustain unlock.",
                        nextUnitId: "priest",
                        suppressInlineRequirementCard: true),
                    new RaceProgressionUnitDefinition(
                        "priest",
                        templeLane,
                        "Priest",
                        "tt_priest",
                        "tt_priest",
                        startsUnlocked: false,
                        unlockRequirement: templeT2,
                        description: "Temple Tier 2 support. More reliable healing and support presence.",
                        nextUnitId: "high_priest"),
                    new RaceProgressionUnitDefinition(
                        "high_priest",
                        templeLane,
                        "High Priest",
                        "tt_high_priest",
                        "tt_high_priest",
                        startsUnlocked: false,
                        unlockRequirement: templeT3,
                        description: "Temple Tier 3 support. Peak sustain for the human army.")
                ),
                new RaceProgressionLaneDefinition(
                    wizardLane,
                    "Wizard Tower",
                    RequirementStepCard(
                        "wizard_tier1_gate",
                        wizardLane,
                        "Wizard Tower",
                        wizardT1,
                        "Wizard Tower Tier 1 gate for the arcane line. Building it unlocks Mage.",
                        nextUnitId: "mage"),
                    new RaceProgressionUnitDefinition(
                        "mage",
                        wizardLane,
                        "Mage",
                        "tt_mage",
                        "tt_mage",
                        startsUnlocked: false,
                        unlockRequirement: wizardT1,
                        description: "Wizard Tower Tier 1 arcane unit. The first ranged spellcaster.",
                        nextUnitId: "wizard",
                        suppressInlineRequirementCard: true),
                    new RaceProgressionUnitDefinition(
                        "wizard",
                        wizardLane,
                        "Wizard",
                        "tt_mounted_mage",
                        "tt_mounted_mage",
                        startsUnlocked: false,
                        unlockRequirement: wizardT2,
                        description: "Wizard Tower Tier 2 arcane unit. Stronger spell output and presence.",
                        nextUnitId: "thaumaturge"),
                    new RaceProgressionUnitDefinition(
                        "thaumaturge",
                        wizardLane,
                        "Thaumaturge",
                        "tt_mounted_king",
                        "tt_mounted_king",
                        startsUnlocked: false,
                        unlockRequirement: wizardT3,
                        description: "Wizard Tower Tier 3 arcane unit. The final human caster upgrade.")
                ),
                new RaceProgressionLaneDefinition(
                    marketLane,
                    "Market",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Units,
                    true,
                    "Economy / trade units unlocked by Market tiers",
                    RequirementStepCard(
                        "market_tier1_gate",
                        marketLane,
                        "Market",
                        marketT1,
                        "Market Tier 1 gate for the economy line. Building it unlocks Peasant purchases.",
                        nextUnitId: "peasant"),
                    new RaceProgressionUnitDefinition(
                        "peasant",
                        marketLane,
                        "Peasant",
                        null,
                        "tt_settler",
                        startsUnlocked: false,
                        unlockRequirement: marketT1,
                        description: "Market Tier 1 economy unit. Carries starter trade goods between the Town Core and Market for 4 gold each completed lap.",
                        nextUnitId: "settler",
                        statsSummary: "4 gold/lap   Town Core <-> Market   Economy unit",
                        cardTag: "Economy Unit",
                        suppressInlineRequirementCard: true),
                    new RaceProgressionUnitDefinition(
                        "settler",
                        marketLane,
                        "Settler",
                        null,
                        "tt_settler",
                        startsUnlocked: false,
                        unlockRequirement: marketT2,
                        description: "Market Tier 2 economy unit. Carries more goods and higher trade value than Peasants, earning 7 gold each completed lap.",
                        nextUnitId: "trader",
                        statsSummary: "7 gold/lap   Town Core <-> Market   More goods carried",
                        cardTag: "Economy Unit"),
                    new RaceProgressionUnitDefinition(
                        "trader",
                        marketLane,
                        "Trader",
                        null,
                        "tt_settler",
                        startsUnlocked: false,
                        unlockRequirement: marketT3,
                        description: "Market Tier 3 economy unit. The top-tier trade runner, carrying the highest-value cargo for 10 gold each completed lap.",
                        statsSummary: "10 gold/lap   Town Core <-> Market   Top-tier trade unit",
                        cardTag: "Economy Unit")
                ),
                new RaceProgressionLaneDefinition(
                    stableHorseLane,
                    "Horses",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Units,
                    true,
                    "Stable Tier 1 -> Colt   Stable Tier 2 -> Stallion   Stable Tier 3 -> Dark Stallion",
                    new RaceProgressionUnitDefinition(
                        "colt",
                        stableHorseLane,
                        "Colt",
                        null,
                        "tt_mounted_scout",
                        startsUnlocked: false,
                        unlockRequirement: stableT1,
                        description: "Stable Tier 1 horse unlock. Light brown mount for the opening stable tier.",
                        nextUnitId: "stallion",
                        statsSummary: "Horse Color: Light Brown",
                        cardTag: "Stable Tier 1"),
                    new RaceProgressionUnitDefinition(
                        "stallion",
                        stableHorseLane,
                        "Stallion",
                        null,
                        "tt_mounted_knight",
                        startsUnlocked: false,
                        unlockRequirement: stableT2,
                        description: "Stable Tier 2 horse unlock. Dark brown warhorse for stronger mounted progression.",
                        nextUnitId: "dark_stallion",
                        statsSummary: "Horse Color: Dark Brown",
                        cardTag: "Stable Tier 2"),
                    new RaceProgressionUnitDefinition(
                        "dark_stallion",
                        stableHorseLane,
                        "Dark Stallion",
                        null,
                        "tt_heavy_cavalry",
                        startsUnlocked: false,
                        unlockRequirement: stableT3,
                        description: "Stable Tier 3 horse unlock. Black late-game warhorse tied to the final Stable tier.",
                        statsSummary: "Horse Color: Black",
                        cardTag: "Stable Tier 3")
                ),
                new RaceProgressionLaneDefinition(
                    buildingCivicLane,
                    "Civic",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Buildings,
                    RaceProgressionTab.Buildings,
                    false,
                    "Core civic progression",
                    BuildingCard(
                        "building_house",
                        buildingCivicLane,
                        "House",
                        "town_core",
                        "Civic Tier 1",
                        "2:00",
                        0,
                        startsUnlocked: true,
                        unlockRequirement: null,
                        description: "House is the opening civic tier. Phase 1 uses a starting-state cost assumption of 0g because the civic core begins at House.",
                        nextUnitId: "building_town_hall"),
                    BuildingCard(
                        "building_town_hall",
                        buildingCivicLane,
                        "Town Hall",
                        "town_core",
                        "Civic Tier 2",
                        "2:30",
                        70,
                        startsUnlocked: false,
                        unlockRequirement: null,
                        description: "Town Hall is the second civic tier in the dedicated building upgrade section.",
                        nextUnitId: "building_keep"),
                    BuildingCard(
                        "building_keep",
                        buildingCivicLane,
                        "Keep",
                        "town_core",
                        "Civic Tier 3",
                        "3:00",
                        110,
                        startsUnlocked: false,
                        unlockRequirement: null,
                        description: "Keep is the third civic tier in the dedicated building upgrade section.",
                        nextUnitId: "building_castle"),
                    BuildingCard(
                        "building_castle",
                        buildingCivicLane,
                        "Castle",
                        "town_core",
                        "Civic Tier 4",
                        "3:30",
                        165,
                        startsUnlocked: false,
                        unlockRequirement: null,
                        description: "Castle is the final civic tier. The card art slot is intentionally ready for Phase 2 civic image insertion.")
                ),
                StandardBuildingLane(buildingBarracksLane, "Barracks", "barracks", 100, 100, 220),
                StandardBuildingLane(buildingBlacksmithLane, "Blacksmith", "blacksmith", 60, 95, 145),
                StandardBuildingLane(buildingTempleLane, "Temple", "temple", 70, 105, 160),
                StandardBuildingLane(buildingWizardLane, "Wizard Tower", "wizard_tower", 75, 115, 170),
                StandardBuildingLane(buildingArcheryLane, "Archery Tower", "archery_tower", 50, 85, 130),
                TownCoreGatedBuildingLane(
                    buildingStableLane,
                    "Stable",
                    "stable",
                    placeholderBuildingTier1Cost,
                    placeholderBuildingTier2Cost,
                    placeholderBuildingTier3Cost,
                    "Unlocks horse units. Stable Tier 1 requires Town Hall.",
                    "Unlocks horse units. Higher Stable tiers unlock stronger horses. Stable Tier 2 requires Keep.",
                    "Unlocks horse units. Higher Stable tiers unlock stronger horses. Stable Tier 3 requires Castle.",
                    "Town Hall -> Stable Tier 1   Keep -> Stable Tier 2   Castle -> Stable Tier 3"),
                TownCoreGatedBuildingLane(
                    buildingWorkshopLane,
                    "Workshop",
                    "workshop",
                    placeholderBuildingTier1Cost,
                    placeholderBuildingTier2Cost,
                    placeholderBuildingTier3Cost,
                    "Allows access to siege equipment. Workshop Tier 1 requires Town Hall.",
                    "Allows access to siege equipment. Higher tiers expand workshop progression. Workshop Tier 2 requires Keep.",
                    "Allows access to siege equipment. Higher tiers expand workshop progression. Workshop Tier 3 requires Castle.",
                    "Town Hall -> Workshop Tier 1   Keep -> Workshop Tier 2   Castle -> Workshop Tier 3"),
                TownCoreGatedBuildingLane(
                    buildingLibraryLane,
                    "Library",
                    "library",
                    placeholderBuildingTier1Cost,
                    placeholderBuildingTier2Cost,
                    placeholderBuildingTier3Cost,
                    "Unlocks hero abilities, auras, and unit upgrades. Library Tier 1 requires Town Hall.",
                    "Unlocks hero abilities, auras, and unit upgrades. Higher library tiers unlock stronger abilities. Library Tier 2 requires Keep.",
                    "Unlocks hero abilities, auras, and unit upgrades. Higher library tiers unlock stronger abilities. Library Tier 3 requires Castle.",
                    "Town Hall -> Library Tier 1   Keep -> Library Tier 2   Castle -> Library Tier 3"),
                StandardBuildingLane(buildingMarketLane, "Market", "market", marketTier1Cost, marketTier2Cost, marketTier3Cost),
                StandardBuildingLane(buildingLumberMillLane, "Lumber Mill", "lumber_mill", placeholderLumberMillTier1Cost, placeholderLumberMillTier2Cost, placeholderLumberMillTier3Cost),
                new RaceProgressionLaneDefinition(
                    buildingWallsLane,
                    "Walls",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Buildings,
                    RaceProgressionTab.Buildings,
                    true,
                    "Lumber Mill T2/T3 gates wall reinforcement",
                    BuildingCard(
                        "wall_tier_1",
                        buildingWallsLane,
                        "Wall 1",
                        "wall",
                        "Tier 1",
                        "20s",
                        placeholderWallTier1Cost,
                        startsUnlocked: false,
                        unlockRequirement: null,
                        description: "Wall 1 represents the starting wall tier in the building upgrade section.",
                        nextUnitId: "wall_tier_2"),
                    BuildingCard(
                        "wall_tier_2",
                        buildingWallsLane,
                        "Wall 2",
                        "wall",
                        "Tier 2",
                        "40s",
                        placeholderWallTier2Cost,
                        startsUnlocked: false,
                        unlockRequirement: lumberMillT2Requirement,
                        description: "Wall 2 represents the stone-wall step and requires Lumber Mill Tier 2.",
                        nextUnitId: "wall_tier_3"),
                    BuildingCard(
                        "wall_tier_3",
                        buildingWallsLane,
                        "Wall 3",
                        "wall",
                        "Tier 3",
                        "80s",
                        placeholderWallTier3Cost,
                        startsUnlocked: false,
                        unlockRequirement: lumberMillT3Requirement,
                        description: "Wall 3 represents the reinforced final wall tier and requires Lumber Mill Tier 3.")
                ),
                new RaceProgressionLaneDefinition(
                    buildingBaseTowerLane,
                    "Base Towers",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Buildings,
                    RaceProgressionTab.Buildings,
                    true,
                    "Lumber Mill -> Turret Tier 1 -> Turret Tier 2 -> Turret Tier 3",
                    RaceProgressionLaneCategory.BaseTower,
                    true,
                    false,
                    BuildingCard(
                        "turret_tier_1",
                        buildingBaseTowerLane,
                        "Turret Tier 1",
                        "turret",
                        "Tier 1",
                        "20s",
                        placeholderTurret1Cost,
                        startsUnlocked: false,
                        unlockRequirement: lumberMillT1Requirement,
                        description: baseTowerDescription,
                        nextUnitId: "turret_tier_2"),
                    BuildingCard(
                        "turret_tier_2",
                        buildingBaseTowerLane,
                        "Turret Tier 2",
                        "turret",
                        "Tier 2",
                        "40s",
                        placeholderTurret2Cost,
                        startsUnlocked: false,
                        unlockRequirement: turretT1Requirement,
                        description: baseTowerDescription,
                        nextUnitId: "turret_tier_3"),
                    BuildingCard(
                        "turret_tier_3",
                        buildingBaseTowerLane,
                        "Turret Tier 3",
                        "turret",
                        "Tier 3",
                        "80s",
                        placeholderTurret3Cost,
                        startsUnlocked: false,
                        unlockRequirement: turretT2Requirement,
                        description: baseTowerDescription,
                        nextUnitId: "archer_tower_tier_1")
                ),
                new RaceProgressionLaneDefinition(
                    buildingArcherTowerLane,
                    "Archer Towers",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Buildings,
                    RaceProgressionTab.Buildings,
                    true,
                    "Turret Tier 3 -> Archer Tower Tier 1 -> Archer Tower Tier 2 -> Archer Tower Tier 3",
                    RaceProgressionLaneCategory.ArcherTower,
                    false,
                    true,
                    BuildingCard(
                        "archer_tower_tier_1",
                        buildingArcherTowerLane,
                        "Archer Tower Tier 1",
                        "tower_archer",
                        "Tier 1",
                        "80s",
                        placeholderArcherTower1Cost,
                        startsUnlocked: false,
                        unlockRequirement: turretT3Requirement,
                        description: archerTowerDescription,
                        nextUnitId: "archer_tower_tier_2"),
                    BuildingCard(
                        "archer_tower_tier_2",
                        buildingArcherTowerLane,
                        "Archer Tower Tier 2",
                        "tower_archer",
                        "Tier 2",
                        "160s",
                        placeholderArcherTower2Cost,
                        startsUnlocked: false,
                        unlockRequirement: archerTowerT1Requirement,
                        description: archerTowerDescription,
                        nextUnitId: "archer_tower_tier_3"),
                    BuildingCard(
                        "archer_tower_tier_3",
                        buildingArcherTowerLane,
                        "Archer Tower Tier 3",
                        "tower_archer",
                        "Tier 3",
                        "320s",
                        placeholderArcherTower3Cost,
                        startsUnlocked: false,
                        unlockRequirement: archerTowerT2Requirement,
                        description: archerTowerDescription)
                ),
                new RaceProgressionLaneDefinition(
                    siegeTier1Lane,
                    "Workshop Tier 1 Siege",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Siege,
                    true,
                    "Display-only siege roster. Workshop purchase wiring is not active yet.",
                    new RaceProgressionUnitDefinition(
                        "ballista_siege",
                        siegeTier1Lane,
                        "Ballista",
                        null,
                        null,
                        startsUnlocked: false,
                        unlockRequirement: workshopT1,
                        description: "Strong against buildings. Long-range siege support shown here for progression planning only.",
                        statsSummary: "Requires: Workshop Tier 1",
                        cardTag: "Long-range siege support",
                        imageResourcePath: "Icons/towers/ballista_icon")
                ),
                new RaceProgressionLaneDefinition(
                    siegeTier2Lane,
                    "Workshop Tier 2 Siege",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Siege,
                    true,
                    "Display-only siege roster. Workshop purchase wiring is not active yet.",
                    new RaceProgressionUnitDefinition(
                        "cannon_siege",
                        siegeTier2Lane,
                        "Cannon",
                        null,
                        null,
                        startsUnlocked: false,
                        unlockRequirement: workshopT2,
                        description: "Heavy structure damage. Strong against buildings and ideal for heavier siege pressure.",
                        statsSummary: "Requires: Workshop Tier 2",
                        cardTag: "Heavy structure damage",
                        imageResourcePath: "Icons/towers/cannon_icon")
                ),
                new RaceProgressionLaneDefinition(
                    abilityTier1Lane,
                    "Library Tier 1 Auras",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Abilities,
                    true,
                    "Aura effects are shown as a slight colored circle around the feet only.",
                    new RaceProgressionUnitDefinition(
                        "march_aura",
                        abilityTier1Lane,
                        "March Aura",
                        null,
                        "tt_mounted_scout",
                        startsUnlocked: false,
                        unlockRequirement: libraryT1,
                        description: $"Increases movement speed of nearby units. Aura color: Green. {feetAuraVisualNote}",
                        nextUnitId: "focus_aura",
                        statsSummary: "Library Tier 1   Movement speed aura",
                        cardTag: "Aura Color: Green"),
                    new RaceProgressionUnitDefinition(
                        "focus_aura",
                        abilityTier1Lane,
                        "Focus Aura",
                        null,
                        "tt_archer",
                        startsUnlocked: false,
                        unlockRequirement: libraryT1,
                        description: $"Improves attack consistency for nearby units. Aura color: Blue. {feetAuraVisualNote}",
                        statsSummary: "Library Tier 1   Attack consistency aura",
                        cardTag: "Aura Color: Blue",
                        suppressInlineRequirementCard: true)
                ),
                new RaceProgressionLaneDefinition(
                    abilityTier2Lane,
                    "Library Tier 2 Auras",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Abilities,
                    true,
                    "Aura effects are shown as a slight colored circle around the feet only.",
                    new RaceProgressionUnitDefinition(
                        "war_command",
                        abilityTier2Lane,
                        "War Command",
                        null,
                        "tt_light_infantry",
                        startsUnlocked: false,
                        unlockRequirement: libraryT2,
                        description: $"Increases attack damage of nearby units. Aura color: Red. {feetAuraVisualNote}",
                        nextUnitId: "iron_will",
                        statsSummary: "Library Tier 2   Attack damage aura",
                        cardTag: "Aura Color: Red"),
                    new RaceProgressionUnitDefinition(
                        "iron_will",
                        abilityTier2Lane,
                        "Iron Will",
                        null,
                        "tt_heavy_infantry",
                        startsUnlocked: false,
                        unlockRequirement: libraryT2,
                        description: $"Reduces incoming damage for nearby units. Aura color: Yellow. {feetAuraVisualNote}",
                        statsSummary: "Library Tier 2   Damage reduction aura",
                        cardTag: "Aura Color: Yellow",
                        suppressInlineRequirementCard: true)
                ),
                new RaceProgressionLaneDefinition(
                    abilityTier3Lane,
                    "Library Tier 3 Auras",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Abilities,
                    true,
                    "Aura effects are shown as a slight colored circle around the feet only.",
                    new RaceProgressionUnitDefinition(
                        "vital_surge",
                        abilityTier3Lane,
                        "Vital Surge",
                        null,
                        "tt_priest",
                        startsUnlocked: false,
                        unlockRequirement: libraryT3,
                        description: $"Regenerates health over time for nearby units. Aura color: Light Green. {feetAuraVisualNote}",
                        nextUnitId: "precision_volley",
                        statsSummary: "Library Tier 3   Health regeneration aura",
                        cardTag: "Aura Color: Light Green"),
                    new RaceProgressionUnitDefinition(
                        "precision_volley",
                        abilityTier3Lane,
                        "Precision Volley",
                        null,
                        "tt_crossbowman",
                        startsUnlocked: false,
                        unlockRequirement: libraryT3,
                        description: $"Increases ranged attack effectiveness for nearby units. Aura color: Purple. {feetAuraVisualNote}",
                        statsSummary: "Library Tier 3   Ranged effectiveness aura",
                        cardTag: "Aura Color: Purple",
                        suppressInlineRequirementCard: true)
                ),
                new RaceProgressionLaneDefinition(
                    abilitySupportLane,
                    "Library Tier 3 Support",
                    RaceProgressionLaneLayout.Linear,
                    null,
                    RaceProgressionLaneSection.Units,
                    RaceProgressionTab.Abilities,
                    true,
                    "Optional higher-tier support aura shown as a feet-circle only.",
                    new RaceProgressionUnitDefinition(
                        "divine_presence",
                        abilitySupportLane,
                        "Divine Presence",
                        null,
                        "tt_high_priest",
                        startsUnlocked: false,
                        unlockRequirement: libraryT3,
                        description: $"Improves healing and support effects for nearby allied units. Aura color: Gold. {feetAuraVisualNote}",
                        statsSummary: "Library Tier 3   Healing and support aura",
                        cardTag: "Aura Color: Gold")
                )
            );
        }

        public static IReadOnlyList<RaceProgressionDefinition> All => _races;

        public static string DefaultRaceId => HumansRaceId;

        public static bool TryGetRace(string raceId, out RaceProgressionDefinition race)
        {
            if (string.IsNullOrWhiteSpace(raceId))
            {
                race = null;
                return false;
            }

            return _racesById.TryGetValue(raceId.Trim(), out race);
        }

        public static RaceProgressionDefinition GetOrDefault(string raceId, string logContext = null)
        {
            if (TryGetRace(raceId, out var race))
                return race;

            if (!string.IsNullOrWhiteSpace(raceId))
                Debug.LogWarning($"[RaceProgression] Unknown race id '{raceId}' in {logContext ?? "catalog lookup"}. Falling back to {DefaultRaceId}.");

            return _races[0];
        }

        public static string ResolveAllowedRaceId(IEnumerable<string> availableRaceIds, string requestedRaceId, string logContext = null)
        {
            var available = new List<string>();
            if (availableRaceIds != null)
            {
                foreach (var raceId in availableRaceIds)
                {
                    if (string.IsNullOrWhiteSpace(raceId))
                        continue;

                    string trimmed = raceId.Trim();
                    if (!_racesById.ContainsKey(trimmed))
                    {
                        Debug.LogWarning($"[RaceProgression] Ignoring unknown race id '{trimmed}' from {logContext ?? "payload"}.");
                        continue;
                    }

                    if (!available.Contains(trimmed))
                        available.Add(trimmed);
                }
            }

            if (available.Count == 0)
                available.Add(DefaultRaceId);

            if (!string.IsNullOrWhiteSpace(requestedRaceId))
            {
                string trimmedRequested = requestedRaceId.Trim();
                for (int i = 0; i < available.Count; i++)
                {
                    if (string.Equals(available[i], trimmedRequested, StringComparison.OrdinalIgnoreCase))
                        return available[i];
                }
            }

            return available[0];
        }

        public static string[] GetPortraitWarmupKeys(IEnumerable<string> raceIds)
        {
            var resolvedKeys = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hadAny = false;

            if (raceIds != null)
            {
                foreach (var raceId in raceIds)
                {
                    hadAny = true;
                    var race = GetOrDefault(raceId, "portrait warmup");
                    CollectPortraitKeys(race, resolvedKeys, seen);
                }
            }

            if (!hadAny)
                CollectPortraitKeys(GetOrDefault(DefaultRaceId), resolvedKeys, seen);

            return resolvedKeys.ToArray();
        }

        static void CollectPortraitKeys(RaceProgressionDefinition race, List<string> output, HashSet<string> seen)
        {
            if (race == null)
                return;

            if (!string.IsNullOrWhiteSpace(race.FeaturedPortraitKey) && seen.Add(race.FeaturedPortraitKey))
                output.Add(race.FeaturedPortraitKey);

            for (int unitIndex = 0; unitIndex < race.Units.Length; unitIndex++)
            {
                var unit = race.Units[unitIndex];
                if (unit == null || string.IsNullOrWhiteSpace(unit.PortraitKey))
                    continue;

                if (seen.Add(unit.PortraitKey))
                    output.Add(unit.PortraitKey);
            }
        }
    }
}
