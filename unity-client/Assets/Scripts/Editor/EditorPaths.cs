// EditorPaths.cs — Single source of truth for all asset paths used by Castle Defender editor tools.
//
// ALL editor scripts must reference constants from this file.
// Never hardcode an Assets/ path string anywhere else in the Editor folder.
//
// Sections:
//   Project structure  — registry, scenes, tile prefabs
//   LVE environment    — NatureManufacture L.V.E asset paths
//   Creature pack      — Heroic Fantasy Creatures prefab root

#if UNITY_EDITOR
namespace CastleDefender.Editor
{
    public static class EditorPaths
    {
        // ── Project structure ─────────────────────────────────────────────────

        public const string REGISTRY      = "Assets/Registry/UnitPrefabRegistry.asset";

        public const string SCENE_ML       = "Assets/Scenes/Game_ML.unity";
        public const string SCENE_LOGIN    = "Assets/Scenes/Login.unity";

        public const string TILE_FLOOR  = "Assets/Prefabs/Tiles/FloorTile.prefab";
        public const string TILE_WALL   = "Assets/Prefabs/Tiles/WallTile.prefab";
        public const string TILE_CASTLE = "Assets/Prefabs/Tiles/CastleTile.prefab";

        // ── LVE — NatureManufacture Lava and Volcano Environment ─────────────
        // All LVE shaders are URP Shader Graph ("RenderPipeline"="UniversalPipeline").
        // Do NOT run conversion or replace materials — they are already URP-compatible.

        public const string LVE_ROOT      = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment";

        // Prefabs — always use Vertex Paint Prefabs (URP Shader Graph).
        // NEVER use "Rocks/Standard prefabs" — those are Built-in RP and will appear pink in URP.
        public const string LVE_ROCKS     = LVE_ROOT + "/Rocks/Vertex Paint Prefabs";
        public const string LVE_CLIFFS    = LVE_ROOT + "/Cliffs/Vertex Paint Prefabs/Prefabs V2";
        public const string LVE_PARTICLES = LVE_ROOT + "/Particles";

        // Materials
        public const string LVE_GROUND_MAT  = LVE_ROOT + "/Ground and River Textures/Ground Material/Ground_01.mat";
        public const string LVE_LAVA_LAKE_MAT = LVE_ROOT + "/Lava Materials/Lava_2_flow Lake.mat";
        public const string LVE_SKYBOX      = LVE_ROOT + "/Demo Scenes/Vulcano Sky.mat";
        public const string LVE_SKYBOX_ALT  = LVE_ROOT + "/Demo Scenes/Skybox.mat";
        public const string LVE_POSTPROCESS = LVE_ROOT + "/Demo Scenes/PostProcessVolumeProfile Lava.asset";

        // ── Heroic Fantasy Creatures Full Pack Vol 1 ──────────────────────────
        public const string HFC_ROOT = "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1";

        public const string TT_RTS_ROOT = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard";
        public const string TT_BUILDINGS_ROOT = TT_RTS_ROOT + "/models/buildings";
        public const string TT_BUILDING_CONSTRUCTION_ROOT = TT_BUILDINGS_ROOT + "/construction";
        public const string TT_EXTRAS_ROOT = TT_RTS_ROOT + "/models/extras";
        public const string TT_BANNERS_ROOT = TT_RTS_ROOT + "/prefabs/banners";
        public const string TT_FX_PREFABS_ROOT = TT_RTS_ROOT + "/FX/FX_prefabs";
        public const string TT_BUILDING_MATERIAL = TT_RTS_ROOT + "/models/materials/TT_RTS_Buildings.mat";
        public const string TT_BUILDING_WHITE_MATERIAL = TT_RTS_ROOT + "/models/materials/color/Buildings/TT_RTS_buildings_white.mat";

        public const string TT_BUILDING_HOUSE = TT_BUILDINGS_ROOT + "/House.FBX";
        public const string TT_BUILDING_TOWN_HALL = TT_BUILDINGS_ROOT + "/TownHall.FBX";
        public const string TT_BUILDING_KEEP = TT_BUILDINGS_ROOT + "/Keep.FBX";
        public const string TT_BUILDING_CASTLE = TT_BUILDINGS_ROOT + "/Castle.FBX";
        public const string TT_BUILDING_BARRACKS = TT_BUILDINGS_ROOT + "/Barracks.FBX";
        public const string TT_BUILDING_BLACKSMITH = TT_BUILDINGS_ROOT + "/Blacksmith.FBX";
        public const string TT_BUILDING_TEMPLE = TT_BUILDINGS_ROOT + "/Temple.FBX";
        public const string TT_BUILDING_WIZARD_TOWER = TT_BUILDINGS_ROOT + "/MageTower.FBX";
        public const string TT_BUILDING_ARCHERY = TT_BUILDINGS_ROOT + "/Archery.FBX";
        public const string TT_BUILDING_STABLE = TT_BUILDINGS_ROOT + "/Stables.FBX";
        public const string TT_BUILDING_WORKSHOP = TT_BUILDINGS_ROOT + "/Workshop.FBX";
        public const string TT_BUILDING_LIBRARY = TT_BUILDINGS_ROOT + "/Library.FBX";
        public const string TT_BUILDING_MARKET = TT_BUILDINGS_ROOT + "/Market.FBX";
        public const string TT_BUILDING_LUMBER_MILL = TT_BUILDINGS_ROOT + "/LumberMill.FBX";
        public const string TT_BUILDING_WALL_A = TT_BUILDINGS_ROOT + "/Wall_A_wall.FBX";
        public const string TT_BUILDING_WALL_B = TT_BUILDINGS_ROOT + "/Wall_B_wall.FBX";
        public const string TT_BUILDING_GATE_A = TT_BUILDINGS_ROOT + "/Wall_A_gate.FBX";
        public const string TT_BUILDING_GATE_B = TT_BUILDINGS_ROOT + "/Wall_B_gate.FBX";
        public const string TT_BUILDING_CORNER_A = TT_BUILDINGS_ROOT + "/Wall_A_corner.FBX";
        public const string TT_BUILDING_CORNER_B = TT_BUILDINGS_ROOT + "/Wall_B_corner.FBX";
        public const string TT_BUILDING_TOWER_A = TT_BUILDINGS_ROOT + "/Tower_A.FBX";
        public const string TT_BUILDING_TOWER_B = TT_BUILDINGS_ROOT + "/Tower_B.FBX";
        public const string TT_BUILDING_TOWER_C = TT_BUILDINGS_ROOT + "/Tower_C.FBX";
        public const string TT_CONSTRUCTION_BARRACKS_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Barracks_0.FBX";
        public const string TT_CONSTRUCTION_BARRACKS_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Barracks_1.FBX";
        public const string TT_CONSTRUCTION_BLACKSMITH_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Blacksmith_0.FBX";
        public const string TT_CONSTRUCTION_BLACKSMITH_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Blacksmith_1.FBX";
        public const string TT_CONSTRUCTION_TEMPLE_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Temple_0.FBX";
        public const string TT_CONSTRUCTION_TEMPLE_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Temple_1.FBX";
        public const string TT_CONSTRUCTION_MAGE_TOWER_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/MageTower_0.FBX";
        public const string TT_CONSTRUCTION_MAGE_TOWER_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/MageTower_1.FBX";
        public const string TT_CONSTRUCTION_ARCHERY_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Archery_0.FBX";
        public const string TT_CONSTRUCTION_ARCHERY_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Archery_1.FBX";
        public const string TT_CONSTRUCTION_STABLES_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Stables_0.FBX";
        public const string TT_CONSTRUCTION_STABLES_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Stables_1.FBX";
        public const string TT_CONSTRUCTION_WORKSHOP_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Workshop_0.FBX";
        public const string TT_CONSTRUCTION_WORKSHOP_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Workshop_1.FBX";
        public const string TT_CONSTRUCTION_LIBRARY_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Library_0.FBX";
        public const string TT_CONSTRUCTION_LIBRARY_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Library_1.FBX";
        public const string TT_CONSTRUCTION_MARKET_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Market_0.FBX";
        public const string TT_CONSTRUCTION_MARKET_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Market_1.FBX";
        public const string TT_CONSTRUCTION_LUMBER_MILL_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/LumberMill_0.FBX";
        public const string TT_CONSTRUCTION_LUMBER_MILL_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/LumberMill_1.FBX";
        public const string TT_CONSTRUCTION_WALL_A_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Wall_A_wall_0.FBX";
        public const string TT_CONSTRUCTION_WALL_A_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Wall_A_wall_1.FBX";
        public const string TT_CONSTRUCTION_GATE_A_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Wall_A_gate_0.FBX";
        public const string TT_CONSTRUCTION_GATE_A_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Wall_A_gate_1.FBX";
        public const string TT_CONSTRUCTION_CORNER_A_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Wall_A_corner_0.FBX";
        public const string TT_CONSTRUCTION_CORNER_A_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Wall_A_corner_1.FBX";
        public const string TT_CONSTRUCTION_TOWER_A_0 = TT_BUILDING_CONSTRUCTION_ROOT + "/Tower_A_0.FBX";
        public const string TT_CONSTRUCTION_TOWER_A_1 = TT_BUILDING_CONSTRUCTION_ROOT + "/Tower_A_1.FBX";

        public const string TT_BANNER_BLUE = TT_BANNERS_ROOT + "/TT_Banner_Blue_A.prefab";
        public const string TT_BANNER_RED = TT_BANNERS_ROOT + "/TT_Banner_Red.prefab";
        public const string TT_BANNER_YELLOW = TT_BANNERS_ROOT + "/TT_Banner_Yellow.prefab";
        public const string TT_BANNER_GREEN = TT_BANNERS_ROOT + "/TT_Banner_Green_A.prefab";
        public const string TT_BANNER_MATERIAL_BLUE = TT_RTS_ROOT + "/models/extras/banners/materials/colors/TT_Banner_blue_A.mat";
        public const string TT_BANNER_MATERIAL_RED = TT_RTS_ROOT + "/models/extras/banners/materials/colors/TT_Banner_red.mat";
        public const string TT_BANNER_MATERIAL_YELLOW = TT_RTS_ROOT + "/models/extras/banners/materials/colors/TT_Banner_yellow.mat";
        public const string TT_BANNER_MATERIAL_GREEN = TT_RTS_ROOT + "/models/extras/banners/materials/colors/TT_Banner_green_A.mat";
        public const string TT_SHIELD = TT_EXTRAS_ROOT + "/weapons/shield_20.FBX";
        public const string TT_HAMMER = TT_EXTRAS_ROOT + "/weapons/w_hammer.FBX";
        public const string TT_BUILDING_BURNING_SMALL_FX = TT_FX_PREFABS_ROOT + "/FX_Building_burning_small.prefab";
        public const string TT_BUILDING_BURNING_FX = TT_FX_PREFABS_ROOT + "/FX_Building_burning.prefab";
        public const string TT_BUILDING_DESTROYED_FX = TT_FX_PREFABS_ROOT + "/FX_Building_Destroyed_mid.prefab";

        public const string GENERATED_BUILDING_PREFABS_ROOT = "Assets/Prefabs/Buildings/Generated";
        public const string GENERATED_BUILDING_MATERIALS_ROOT = "Assets/Materials/TT/Generated";
        public const string GENERATED_BUILDING_PORTRAITS_ROOT = "Assets/Resources/TechTree/Buildings";
        public const string GENERATED_BUILDING_CATALOG_ASSET = "Assets/Resources/Generated/BuildingVisualCatalog.asset";
        public const string GAME_ENVIRONMENT_PREFAB = "Assets/AddressableContent/Environment/GameEnvironment.prefab";
    }
}
#endif
