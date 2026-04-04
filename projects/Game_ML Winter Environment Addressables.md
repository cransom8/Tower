# Game_ML Winter Environment Addressables

Generated: `2026-04-04 01:44:49`

## Critical Content

- Group: `Remote Environment`
- Address: `environment/game_ml`
- Prefab: `Assets/AddressableContent/Environment/GameEnvironment.prefab`
- Loaded before match start.
- Keeps the current gameplay-required board, bridges, forge areas, lane geometry, path markers, and map shell intact.

## Optional Content

- Group: `Remote Environment Dressing`
- Address: `environment/game_ml_dressing`
- Prefab: `Assets/AddressableContent/Environment/GameEnvironmentOptional.prefab`
- Streams after the critical environment is present.
- Contains only collider-free visual dressing: edge cliffs, pines, rock borders, snow details, landmark props, backdrop silhouettes, and optional warm accent lights.

## Shared Content

- Group: `Remote Environment Shared`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Textures/Seamless Terrain/Snow_Dirt_Seamless_Texture_Albedo.png` -> `environment/shared/snow_dirt_albedo`

## Optional Winter Pack Assets Used

- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_extra_large.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_large.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_medium.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_small.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_01.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_02.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_03.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_04.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/bush_01.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/bush_02.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/bush_03.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/barrel.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/cottage.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/sled.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/snowman_01.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/snowman_02.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/fence.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/fence_seamless.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/lantern_pole.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/bench.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/stacked_wood_01.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/wheelbarrow.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_tree_outside_01.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_tree_large.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_01.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_03.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_06.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_09.prefab`
- `Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Terrain/floor_horizon.prefab`

## Performance Notes

- Optional props are collider-free so they do not affect gameplay pathing or collision.
- Backdrop renderers have shadows disabled to keep the streamed dressing lighter for WebGL/mobile.
- Reused a small curated subset of winter prefabs with scale and rotation variation instead of pulling the full demo scene.
- Optional warm lights are limited to four non-shadowed point lights near focal landmarks.

## Load Order

- `EnvironmentLoader` loads the required environment and instantiates it as `CoreMapCritical`.
- `OptionalEnvironmentLoader` waits for `CoreMapCritical`, then downloads and instantiates `OptionalEnvironmentDressing`.
- If optional dressing fails, gameplay still starts and continues with the critical map only.
