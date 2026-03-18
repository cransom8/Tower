# WebGL Player Size Audit

Date: 2026-03-18 00:20:57

## Build Output

- Build output path: `C:\Users\Crans\RansomForge\castle-defender\server\client`
- Current build output size: `47.25 MB`
- Local `StreamingAssets/aa` size inside player: `0.38 MB`

## Build Scenes

- `Assets/Scenes/Bootstrap.unity`
- `Assets/Scenes/Login.unity`
- `Assets/Scenes/Lobby.unity`
- `Assets/Scenes/Loading.unity`
- `Assets/Scenes/Loadout.unity`
- `Assets/Scenes/Game_ML.unity`

## Dependency Totals

- Raw scene dependency source assets: `4959.64 MB` across `1564` files
- Scene dependency source assets after registry-strip approximation: `57.30 MB` across `52` files
- Resources source assets: `0.08 MB` across `13` files

## Top Scene Dependency Folders

- `Assets/Dimensional-3D-Design`: `52.95 MB` across `7` files
- `Assets/TextMesh Pro`: `2.51 MB` across `4` files
- `Assets/Scenes`: `1.19 MB` across `6` files
- `Assets/Prefabs`: `0.63 MB` across `10` files
- `Assets/Audio`: `0.02 MB` across `24` files
- `Assets/Settings`: `0.00 MB` across `1` files

## Top Resources Folders

- `Assets/Resources`: `0.05 MB` across `5` files
- `Assets/TextMesh Pro`: `0.03 MB` across `7` files
- `Assets/MCPForUnity`: `0.00 MB` across `1` files

## Largest Scene Dependency Assets

- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Textures/Props/Village_Exterior_Normal.png`: `16.08 MB`
- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Textures/Props/Village_Floor_Normal.png`: `15.56 MB`
- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Textures/Props/Village_Exterior_Albedo.png`: `12.52 MB`
- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Textures/Props/Village_Floor_Albedo.png`: `8.72 MB`
- `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset`: `2.16 MB`
- `Assets/Scenes/Game_ML.unity`: `0.80 MB`
- `Assets/Prefabs/FX/CannonSplash.prefab`: `0.36 MB`
- `Assets/TextMesh Pro/Fonts/LiberationSans.ttf`: `0.33 MB`
- `Assets/Prefabs/FX/HitEffect.prefab`: `0.24 MB`
- `Assets/Scenes/Lobby.unity`: `0.21 MB`
- `Assets/Scenes/Login.unity`: `0.13 MB`
- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Textures/Props/Village_Exterior_Emission.png`: `0.05 MB`
- `Assets/Scenes/Loading.unity`: `0.03 MB`
- `Assets/Scenes/Loadout.unity`: `0.01 MB`
- `Assets/Scenes/Bootstrap.unity`: `0.01 MB`
- `Assets/Prefabs/FX/FloatingText.prefab`: `0.01 MB`
- `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`: `0.01 MB`
- `Assets/Prefabs/FX/GoldPop.prefab`: `0.01 MB`
- `Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader`: `0.01 MB`
- `Assets/Prefabs/UI/HpBar.prefab`: `0.01 MB`
- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Materials/W&CP_VillageExteriorAtlas_MAT.mat`: `0.00 MB`
- `Assets/Audio/GameMixer.mixer`: `0.00 MB`
- `Assets/Dimensional-3D-Design/Winter & Christmas Pack/Materials/W&CP_Floor_MAT.mat`: `0.00 MB`
- `Assets/Prefabs/Tiles/FloorTile.prefab`: `0.00 MB`
- `Assets/Prefabs/Tiles/WallTile.prefab`: `0.00 MB`

## Largest Resources Assets

- `Assets/TextMesh Pro/Resources/Sprite Assets/EmojiOne.asset`: `0.01 MB`
- `Assets/Resources/Icons/towers/archer_icon.png`: `0.01 MB`
- `Assets/Resources/Icons/towers/cannon_icon.png`: `0.01 MB`
- `Assets/Resources/Icons/towers/ballista_icon.png`: `0.01 MB`
- `Assets/Resources/Icons/towers/fighter_icon.png`: `0.01 MB`
- `Assets/Resources/Icons/towers/mage_icon.png`: `0.01 MB`
- `Assets/TextMesh Pro/Resources/Style Sheets/Default Style Sheet.asset`: `0.01 MB`
- `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Drop Shadow.mat`: `0.00 MB`
- `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Outline.mat`: `0.00 MB`
- `Assets/TextMesh Pro/Resources/TMP Settings.asset`: `0.00 MB`
- `Assets/MCPForUnity/Editor/Windows/Components/Resources/McpResourcesSection.uxml`: `0.00 MB`
- `Assets/TextMesh Pro/Resources/LineBreaking Following Characters.txt`: `0.00 MB`
- `Assets/TextMesh Pro/Resources/LineBreaking Leading Characters.txt`: `0.00 MB`

## Remote-Only Leak Candidates

- No scene/resource dependency paths currently match the known remote-only folders.

## Notes

- This audit ranks source asset file sizes, which is directional rather than a perfect one-to-one build-size measurement.
- Scene dependency totals are reported both raw and with `UnitPrefabRegistry` dependencies removed, because WebGL builds strip that registry before build.
- The goal is to identify what is still being pulled into the player build, especially assets that should now be remote-only or deferred.
