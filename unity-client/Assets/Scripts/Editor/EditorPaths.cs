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
    }
}
#endif
