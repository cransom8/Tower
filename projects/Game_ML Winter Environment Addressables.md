# Game_ML Winter Environment Addressables

## Critical Content

- Group: `Remote Environment`
- Address: `environment/game_ml`
- Prefab: `Assets/AddressableContent/Environment/GameEnvironment.prefab`
- Loaded before match start.
- Contains the existing playable board, lane geometry, bridges, forge areas, path markers, and gameplay-required environment shell.

## Optional Content

- Group: `Remote Environment Dressing`
- Address: `environment/game_ml_dressing`
- Prefab: `Assets/AddressableContent/Environment/GameEnvironmentOptional.prefab`
- Loaded asynchronously after the critical environment is instantiated.
- Safe to fail without blocking gameplay startup.

## Optional Dressing Hierarchy

- `OptionalEnvironmentDressing`
- `VisualOuterRing/EdgeCliffs`
- `VisualOuterRing/PineBorders`
- `VisualOuterRing/RockBorders`
- `VisualOuterRing/DecorBorders`
- `VisualOuterRing/SnowOverlayDetails`
- `VisualOuterRing/LandmarkProps`
- `VisualOuterRing/LightingHelpersOptional`
- `Backdrop/MidCliffs`
- `Backdrop/FarMountains`

## Curated Winter Pack Assets Used

- `pine_extra_large`
- `pine_large`
- `pine_medium`
- `pine_small`
- `rock_01`
- `rock_02`
- `rock_03`
- `rock_04`
- `bush_01`
- `bush_02`
- `bush_03`
- `barrel`
- `cottage`
- `sled`
- `snowman_01`
- `snowman_02`
- `fence`
- `fence_seamless`
- `lantern_pole`
- `bench`
- `stacked_wood_01`
- `wheelbarrow`
- `christmas_tree_outside_01`
- `christmas_tree_large`
- `christmas_gift_01`
- `christmas_gift_03`
- `christmas_gift_06`
- `christmas_gift_09`
- `floor_horizon`

## Performance Notes

- Optional content is collider-free to avoid pathing or combat-space changes.
- Backdrop shadows are disabled for lighter WebGL/mobile rendering.
- The extra `LightingHelpersOptional` glow branch is disabled to reduce optional light count for WebGL/mobile.
- Optional lighting is now limited to the four lantern-attached non-shadowed spot lights.
- Dressing relies on repeated props with scale/rotation variation instead of importing the full demo scene into runtime usage.
- The latest visual pass increases tree and rock mass by reusing existing pine/rock variants at larger scales closer to the outer perimeter instead of adding gameplay-adjacent structures.

## Latest Composition Notes

- Lane surfaces stay clean and unchanged in shape; the added density is pushed into `PineBorders` and `RockBorders`.
- `NorthWestVillage` and `SouthEastVillage` remain disabled to avoid houses crowding the board.
- Mid cliffs and selected far-mountain silhouettes stay pulled closer so the board reads more like a carved forest ledge in a snowy mountainside.

## Load Order

- `EnvironmentLoader` instantiates the required map as `CoreMapCritical`.
- `OptionalEnvironmentLoader` waits for `CoreMapCritical`, then requests `environment/game_ml_dressing`.
- If the optional addressable fails or is delayed, the match remains fully playable.
