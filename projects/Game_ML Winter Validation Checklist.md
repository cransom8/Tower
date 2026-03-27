# Game_ML Winter Validation Checklist

- [x] Validate critical startup path wiring.
- [x] Validate optional async dressing path wiring.
- [x] Validate optional fallback behavior in code.
- [x] Validate Addressables packaging and bundle separation.
- [x] Run runtime startup validation in Unity and confirm `CoreMapCritical` appears before match start.
- [x] Run runtime optional-load validation and confirm `OptionalEnvironmentDressing` appears later without hitching.
- [x] Run optional-failure validation and confirm gameplay remains fully playable without dressing.
- [ ] Review gameplay readability from the live camera and adjust sparse or noisy clusters.
- [ ] Review WebGL/mobile performance impact and trim optional props/lights if needed.
- [ ] Rebuild final Addressables content after any visual polish.

## Current Findings

- Critical preload still points only to `environment/game_ml`.
- Optional dressing is isolated to `environment/game_ml_dressing`.
- Scene wiring uses `CoreMapCritical` for required content and `OptionalEnvironmentDressing` for deferred visuals.
- Built Addressables content includes a separate `Remote Environment Dressing` group and bundle.
- Live runtime validation confirmed `Map/CoreMapCritical` and `Map/OptionalEnvironmentDressing` both appear in `Game_ML` under the normal flow.
- Live fallback validation confirmed `Game_ML` still becomes active with `CoreMapCritical` present when the optional dressing address is intentionally invalid, and `OptionalEnvironmentDressing` stays absent.
- Prefab-asset mutation warnings during environment warmup are resolved after guarding editor-only visual rebuilds against prefab-asset execution.
- Runtime revalidation still confirms `Game_ML` reaches the active state with both critical and optional environment roots present after the fix.
- Optional environment light budget was reduced by disabling the `LightingHelpersOptional` branch in the non-critical dressing prefab; runtime streaming still succeeds afterward.
- Optional forest density was increased by pulling the pine and rock border clusters inward and scaling them up around the outer corners/sides, keeping the lanes and bridge approaches clear.
- The latest Addressables rebuild completed successfully after the forest-density pass.
- Remaining console noise is now unrelated to environment prefab mutation:
  - URP additional-lights array-size errors (`_AdditionalLights... exceeds previous array size`) no longer reproduced after a clean Unity restart and fresh `Bootstrap -> Game_ML` validation pass.
  - A TMP glyph warning for `\u2699` still appears in UI text but does not affect environment startup.
