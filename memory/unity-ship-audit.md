# Unity Ship-Quality Audit — Pass 1
Last updated: 2026-03-11
Unity 6000.3.10f1 · URP 17.0.3 · Target: WebGL

---

## STATUS SUMMARY

### CRITICAL — ✅ ALL DONE
| Item | Fix | Status |
|------|-----|--------|
| C1 — Color Space Gamma | `m_ActiveColorSpace: 1` (Linear) in ProjectSettings | ✅ DONE |
| C2 — Survival scene in Build | Removed Game_Survival.unity + ghost Assets/Scenes.unity from EditorBuildSettings | ✅ DONE |
| C3 — Input Handler "Both" | `activeInputHandler: 0` (legacy only) in ProjectSettings | ✅ DONE |
| C4 — Duplicate LVESceneSetup | Deleted stale `Assets/Editor/LVESceneSetup.cs`; fixed LVELobbySetup.cs deps | ✅ DONE |
| C5 — ToonLit ignores shadows | Added shadowCoord to Varyings, GetMainLight(shadowCoord), ApplyShadowBias in ShadowCaster | ✅ DONE |

### HIGH — ✅ ALL DONE (except H8, H11)
| Item | Fix | Status |
|------|-----|--------|
| H1 — No assembly definitions | Created 7 asmdefs (Net, Game, UI, FX, Audio, Utils, Editor); broke Game↔UI cycle with ITileMenu interface; moved GameOverUI.cs to UI/; inverted InfoBar anchor wiring | ✅ DONE |
| H2 — Duplicate GlobalVolumes | Deleted 2 inactive volumes, renamed active to GlobalVolume via CleanupVolumes editor script | ✅ DONE |
| H3 — No anti-aliasing | FXAA High enabled on main camera via UniversalAdditionalCameraData | ✅ DONE |
| H4 — Shadow bias = 1.0 | `m_ShadowDepthBias: 0.1`, `m_ShadowNormalBias: 0.1` in URP asset | ✅ DONE |
| H5 — Soft shadows off | `m_SoftShadowsSupported: 1` in URP asset | ✅ DONE |
| H6 — WebGL canvas 960×600 | `defaultScreenWidthWeb: 1280`, `defaultScreenHeightWeb: 720` | ✅ DONE |
| H7 — LDR color grading | `m_ColorGradingMode: 1` (HDR) in URP asset | ✅ DONE |
| H8 — Unity splash screen | **SKIP** — requires Unity Pro license to remove | ⏭ SKIP |
| H9 — dedicatedServerOptimizations | `dedicatedServerOptimizations: 0` in ProjectSettings | ✅ DONE |
| H10 — Mip stripping off | `mipStripping: 1` in ProjectSettings | ✅ DONE |
| H11 — Duplicate UnitPrefabRegistry | **PENDING** — 4 copies found: Assets/UnitPrefabRegistry.asset (263 lines, 5 refs in Game_ML), Assets/Registry/UnitPrefabRegistry.asset (167 lines, 4 refs), plus 2 in LVE folder. Both main ones are referenced in Game_ML scene. Needs careful consolidation to Assets/Data/ with scene re-wire. | ⏳ PENDING |

### PERFORMANCE
| Item | Fix | Status |
|------|-----|--------|
| P1 — Runtime shader conversion (UpgradeToURP) | Shader.Find + new Material per spawn; affects TileGrid + LaneRenderer | ⏳ PENDING |
| P2 — TileGrid 10Hz GC allocs | Promoted towerMap/deadMap/currentIds to class-level reusable fields; s_attackStates static readonly | ✅ DONE |
| P3 — LaneRenderer GetComponentsInChildren per flash | Added `Renderer[] renderers` cache to UnitView; populated at spawn; ApplyTintToRenderers uses cache | ✅ DONE |
| P4 — Resources.Load for icons | CmdBar + TileMenuUI load icons at runtime from Resources/; move to direct Inspector assignment | ⏳ PENDING |
| P5 — Adaptive Performance on WebGL | `m_UseAdaptivePerformance: 0` in URP asset | ✅ DONE |

### VISUAL QUALITY
| Item | Fix | Status |
|------|-----|--------|
| V1 — ApplyDebuffTint uses r.material.color | Changed to r.material.SetColor("_BaseColor", ...) in ApplyDebuffTint + ApplyDeadTint | ✅ DONE |
| V2 — Icons named after retired units | Art work required — goblin/orc/troll/vampire/wyvern icons needed in Resources/Icons/units/ | ⏳ PENDING (needs art) |
| V3 — Gamma color space distortion | Fixed via C1 | ✅ DONE |
| V4 — ToonLit ShadowCaster non-standard bias | Fixed via C5 — using ApplyShadowBias now | ✅ DONE |
| V5 — Asset folder casing art/ → Art/ | Renamed Assets/art/ → Assets/Art/ (two-step rename) | ✅ DONE |

### ARCHITECTURE
| Item | Fix | Status |
|------|-----|--------|
| A1 — Game_Survival.unity still in project | Scene file already gone from Assets/Scenes/ | ✅ DONE |
| A2 — Scene build order wrong | Reordered: Login(0)→Lobby(1)→Loading(2)→Game_ML(3)→Game_Classic(4) | ✅ DONE |
| A3 — No Bootstrap scene | NetworkManager is DDOL in Game_ML; Login/Lobby may create duplicate | ⏳ PENDING (effort M) |
| A4 — Cinemachine unused | Confirmed not in any scene/prefab; removed from manifest.json | ✅ DONE |
| A5 — No assembly definitions | Fixed via H1 | ✅ DONE |

---

## REMAINING WORK (Pass 1)

### H11 — UnitPrefabRegistry consolidation
- **Files found:**
  - `Assets/UnitPrefabRegistry.asset` (guid: 8380e0ce) — 263 lines, **5 refs in Game_ML.unity** (likely the active one)
  - `Assets/Registry/UnitPrefabRegistry.asset` (guid: d8e76b66) — 167 lines, 4 refs in Game_ML.unity
  - `Assets/NatureManufacture Assets/.../UnitPrefabRegistry.asset` — copy in wrong place
  - `Assets/NatureManufacture Assets/.../UnitPrefabRegistry 1.asset` — duplicate copy
- **Plan:**
  1. Create `Assets/Data/` folder
  2. Move `Assets/UnitPrefabRegistry.asset` + .meta → `Assets/Data/UnitPrefabRegistry.asset` (keep GUID)
  3. Delete the 3 others (Registry/ copy, LVE copies)
  4. Verify all scene refs auto-update via GUID
- **Risk:** M — check scene still works after move

### P1 — Runtime UpgradeToURP
- `TileGrid.cs:UpgradeToURP()` at ~line 620 — Shader.Find + new Material per tower spawn
- `LaneRenderer.cs:CreateUnit()` at ~line 349 — same pattern for unit prefabs
- **Fix:** Pre-convert all HFC prefab materials in Editor. Delete UpgradeToURP methods. Use MaterialPropertyBlock for tinting instead of material instancing.
- **Effort:** M — requires checking all HFC prefabs actually have URP materials

### P4 — Resources.Load icons
- `CmdBar.cs:ApplyIcon()` and `TileMenuUI.cs:ApplyIcon()` — loads icons at runtime
- Icons in `Assets/Resources/Icons/units/` use old classic names (goblin_send_icon etc.) that don't exist → silent null
- **Fix:** Pre-assign Sprite refs in Inspector; or fix icon names to match actual HFC creature assets
- **Note:** Also blocked by V2 (no HFC creature icons exist yet)

### V2 — HFC creature icons needed
- Need: goblin_send_icon, orc_send_icon, troll_send_icon, vampire_send_icon, wyvern_send_icon
- Tower icons: need icons for the current ML defender types
- **Requires art work** — no code fix alone

### A3 — Bootstrap scene
- NetworkManager, AudioManager singletons live in Game_ML
- If any other scene loads first (e.g. Lobby direct), singletons missing
- **Fix:** New Bootstrap.unity (index 0), Login moves to index 1, etc.
- **Effort:** M — need to verify current flow doesn't already handle this

---

---

## Pass 2 — Audit (2026-03-12)
Unity 6000.3.10f1 · URP 17.0.3 · WebGL

### FIXES APPLIED

| ID | Item | Fix | Status |
|----|------|-----|--------|
| 2H3 | Runtime UpgradeToURP (P1) | Executed `Castle Defender → Setup → Convert Prefab Materials to URP` — 0 errors | ✅ DONE |
| 2C1 | QueueUpdatePayload Dictionary null | `NetworkManager.cs:337` changed `JsonUtility.FromJson` → `JsonConvert.DeserializeObject` (Newtonsoft.Json already in project) | ✅ DONE |
| 2C2 | AudioListener audit | 1 per scene (Lobby/Loading/Game_Classic/Game_ML), 0 in Bootstrap/Login — correct | ✅ CLEAN |
| 2C3 | CatalogLoader race condition | Both CmdBar and TileMenuUI check `IsReady` and subscribe `OnCatalogReady` — properly guarded | ✅ CLEAN |
| 2H1 | CanvasScaler portrait refs | Lobby + Loading had 1080×1920 reference; fixed to 1920×1080 (landscape, matches Login). Numerically equivalent at 1280×720 but correct semantic | ✅ DONE |
| 2H2 | Duplicate DDOL singletons in Game_ML | Removed dead NetworkManager (localhost URL) + empty AudioManager GOs from Game_ML.unity; removed from SceneRoots | ✅ DONE |
| 2P1 | Missing HFC unit icons | Copied goblin/orc/troll/vampire/wyvern icons from Art/icons/units/ → Resources/Icons/units/ (were silent null) | ✅ DONE |

### AUDITED — NO ACTION NEEDED

| Item | Finding |
|------|---------|
| URP Renderer Features | `m_RendererFeatures: []` — no custom render passes (minimal/intentional) |
| SnapshotApplier lifecycle | Intentionally NOT DontDestroyOnLoad — per-match lifecycle, rides NetworkManager DDOL when in same scene |
| AudioManager WebGL | DDOL singleton, AudioMixer + dB mapping correct, WebGL AudioContext auto-unlocked in Unity 6 |
| Post-process volumes | PostProcessProfile_Lobby.asset has `components: []`; no scene references LVE demo profiles |
| ToonTile shader | Correct URP tags; missing ShadowCaster pass (tiles don't cast/receive shadows) — acceptable for top-down view |
| Outline shader | Correct inverted-normals technique; object-space width (minor dist variation) |
| TMP Settings | Default LiberationSans SDF; `m_GetFontFeaturesAtRuntime: 1` (minor WebGL perf, not a blocker) |
| Texture import | Icons: Sprite type, no mips, max 256 (DefaultTexturePlatform) ✅; Tower textures: Texture2D with mips (correct for 3D) |
| Prefab discipline | 35 game prefabs (clean); 71 LVE prefabs (third-party package, don't touch) |
| H11 UnitPrefabRegistry | Only `Assets/Registry/UnitPrefabRegistry.asset` (guid d8e76b66) remains; all 9 refs in Game_ML point to it ✅ |
| A3 Bootstrap scene | Bootstrap.unity at build index 0 with Bootstrap GO + BootstrapManager + NetworkManager + AuthManager ✅ |

### REMAINING / DEFERRED

| Item | Notes |
|------|-------|
| 2A1 — Singleton migration to Bootstrap | ✅ DONE (2026-03-12) — CatalogLoader + LoadoutManager added as components to Bootstrap/NetworkManager GO; AudioManager (all clips) added as new GO in Bootstrap. Removed entire "NetworkManager" bundle GO + child "CatalogLoader" label GO + AudioManager GO from Lobby.unity. |
| 2V1 — HFC creature icons art | goblin/orc/troll/vampire/wyvern icons now in Resources (copies of existing Art/ assets) but V2 (new art for current units) still needs art work |
| 2P2 — Sprite Atlas for UI icons | Icons are individual Resources/ sprites; no atlas. Acceptable for current scale; revisit if build size becomes concern |
| 2A2 — Addressables evaluation | Using Resources.Load; acceptable for project size. No action needed unless build times degrade |
| 2A3 — Skinning pipeline design | Design/architecture item; no code exists yet |
| 2V2 — ToonTile ShadowCaster | Low priority for top-down; add if shadows on tiles become visually needed |
