1. **Objective**

Reduce the login-to-lobby remote download to only the gameplay-critical content required before the first match, while preserving strict runtime safety so missing remote content never degrades into silent null-prefab failures or broken gameplay visuals.

2. **Non-Negotiable Rules**

- Runtime safety comes before optimization. No preload reduction is allowed if it creates a chance of missing-prefab, missing-material, broken-animator, or unreadable-gameplay failures.
- A remote asset is `critical` only if gameplay can fail, visually break, or become unreadable without it before the first match begins.
- `Critical` must never mean “convenient to preload.”
- Every remote asset must be classified into exactly one bucket:
  - `Required before lobby`
  - `Required before first match`
  - `Optional and deferred`
  - `Deprecated or removable`
- `Required before lobby` should be near-empty. Use it only for content needed to safely render the immediate post-login flow.
- `Required before first match` is the main critical gameplay preload set.
- `Optional and deferred` includes skins, cosmetics, previews, and any nonessential content.
- `Deprecated or removable` must not remain in active preload groups.
- `Gameplay Core Units` must contain only:
  - units that can actually appear in the first playable match/session
  - the exact dependencies required for those units to render and animate correctly
  - no cosmetics unless they are strictly required for gameplay readability
- `Gameplay Core Units` must not become a replacement junk drawer.
- `Shared Gameplay Dependencies` must contain only assets truly shared across multiple critical gameplay units.
- `Shared Gameplay Dependencies` must not contain:
  - optional skins/cosmetics
  - test/dev leftovers
  - “just in case” content
  - miscellaneous assets that are merely hard to classify
- Critical gameplay units must still be preloaded in this phase.
- On-demand loading in this phase is limited to optional skins/cosmetics and clearly noncritical content.
- No wave-time, spawn-time, or first-combat-time loading risk is allowed for core gameplay units during this cleanup pass.
- Keep same-origin `/addressables` delivery for this phase.
- Do not introduce a more complex runtime catalog/content URL system until grouping and preload scope are fixed.
- If metadata fields remain in the model, each must be labeled as either:
  - `runtime-active`
  - `admin-only`
- Every release must produce an auditable `critical_content` list, with change visibility and justification for each newly critical item.
- Size budgets are owned, explicit, and enforced. No exception by default.

3. **Revised Workstreams**

- **Workstream A: Missing-Prefab Safety**
  - Make missing remote prefab resolution impossible to ignore.
  - `GetPrefab` and `GetPrefabForSkin` paths must never silently return `null`.
  - Implement a real fallback strategy:
    - valid `fallbackPrefab`, or
    - explicit placeholder resolution that is tested and visible
  - Add tests and runtime assertions proving:
    - missing remote prefab returns fallback/placeholder
    - missing critical prefab never reaches gameplay as null
  - Preserve blocking before lobby when critical preload is incomplete.

- **Workstream B: Retry UI and Failure States**
  - Add explicit retry UI/state for:
    - manifest failure
    - catalog failure
    - bundle download failure
    - prefab load failure
  - Keep current blocking behavior for incomplete critical content.
  - Surface failure reason clearly to the player and logs.
  - Test retry without requiring a full app restart or re-login where possible.

- **Workstream C: Addressables Group Split**
  - Split remote content into strict groups with ownership:
    - `Gameplay Core Units`
    - `Shared Gameplay Dependencies`
    - `Optional Skins`
    - `Deprecated / Pending Removal` if needed temporarily
  - `Gameplay Core Units`:
    - first-match-capable units only
    - no optional skins/cosmetics
    - no “nice to have” units
  - `Shared Gameplay Dependencies`:
    - only assets referenced by multiple critical core units
    - must be dependency-audited after build
  - `Optional Skins`:
    - all skins/cosmetics moved out of critical preload
  - Ban catch-all groups for gameplay preload.

- **Workstream D: Shared Dependency Cleanup**
  - Audit what actually lands in `Shared Gameplay Dependencies`.
  - Remove any asset that is:
    - only used by optional skins
    - only used by deprecated content
    - only used by one unit and should live with that unit instead
  - Verify shared dependencies are not swallowing most of the project.
  - If shared dependencies are still oversized, split further by real usage pattern rather than convenience.

- **Workstream E: Critical Preload Policy**
  - Define and publish the classification list:
    - `Required before lobby`
    - `Required before first match`
    - `Optional and deferred`
    - `Deprecated or removable`
  - `Required before lobby`:
    - only content that must exist before the player can safely sit in lobby
  - `Required before first match`:
    - all units and unit dependencies that can appear in the first playable match/session
  - `Optional and deferred`:
    - skins, cosmetic variants, optional previews, anything not needed to safely begin first match
  - `Deprecated or removable`:
    - old mappings, dead units, dead skins, stale variants

- **Workstream F: Manifest and Seed Discipline**
  - Clean manifest generation so `critical_content` is intentional, not inherited from convenience defaults.
  - Remove “all units are critical by default” behavior from seed/import flow.
  - Require each critical item to carry:
    - asset key
    - category
    - reason it is critical
  - For every release:
    - export current `critical_content`
    - diff it against previous release
    - review and approve newly added critical items

- **Workstream G: Bundle Output Verification**
  - After regrouping and manifest cleanup, rebuild and inspect actual outputs.
  - Verify:
    - no giant convenience bundle remains
    - core preload no longer drags optional skins/cosmetics
    - shared dependency bundle is not oversized
    - optional bundles do not accidentally force core-size downloads
  - Treat actual built bundle output as source of truth, not group names.

- **Workstream H: Optional Skins On-Demand Loading**
  - Add on-demand loading only for optional skins/cosmetics and clearly noncritical content.
  - Do not move core gameplay units to on-demand loading in this phase.
  - Ensure optional content failure does not break gameplay readability.
  - Use placeholders or default visuals when optional content is unavailable.

- **Workstream I: Config and Version Simplification**
  - Keep same-origin `/addressables`.
  - Simplify runtime path assumptions.
  - Remove, ignore, or explicitly mark as admin-only any metadata not consumed by runtime.
  - Do not implement multi-URL or per-entry catalog routing in this phase.
  - Document exactly which metadata fields are runtime-active vs admin-only.

- **Workstream J: WebGL Delivery Tuning**
  - Tune server/CDN behavior for `/addressables`:
    - compression
    - caching
    - cache reuse validation
  - Validate first-session and repeat-session behavior with real WebGL runs.
  - Ensure bundle size strategy fits WebGL/network constraints.

- **Workstream K: Automation and Pipeline Hardening**
  - Automate:
    - addressables sync
    - critical manifest export
    - previous-vs-current manifest diff
    - size-budget checks
    - bundle output verification
  - Fail the pipeline when:
    - critical preload grows past budget
    - a new giant bundle appears
    - optional content enters critical preload without approval
    - critical items are added without justification

4. **Anti-Bloat Controls**

- **Critical Set Controls**
  - Every item in `critical_content` must have a one-line justification.
  - Newly added critical items must be reviewed against prior release.
  - “Needed eventually” is not valid justification.
  - “Convenient to preload” is not valid justification.

- **Group Discipline Controls**
  - `Gameplay Core Units` owner must verify every included asset can appear before or during the first match.
  - `Shared Gameplay Dependencies` owner must prove each asset is used by multiple critical core units.
  - No optional skin or cosmetic asset may enter core/shared critical groups.
  - No deprecated asset may remain in critical groups.

- **Build Output Controls**
  - After every regroup/build:
    - inspect actual bundle count
    - inspect bundle sizes
    - inspect which assets land in each bundle
  - Required proof:
    - core preload no longer pulls optional skins/cosmetics
    - optional bundles do not drag core bundles unnecessarily
    - shared dependencies are not oversized
    - the login-to-lobby preload is derived from an intentional asset set, not broad group-wide convenience

- **Manifest Discipline Controls**
  - Every release must publish:
    - current `critical_content` list
    - diff from previous release
    - short reason for each newly critical item
  - Critical manifest changes without review are blocked.

- **Size-Budget Gates**
  - Proposed hard gates for this phase:
    - Base WebGL build compressed: `<= 18 MB`
    - Login-to-lobby critical remote download on fresh cache: `<= 35 MB`
    - Maximum single remote bundle size: `<= 16 MB`
  - If the team wants different numbers, they must still be explicit, owned, and enforced before work begins.
  - Any breach requires an explicit review and signoff, not silent acceptance.

5. **Revised Execution Order**

1. Fallback + missing-prefab safety  
2. Retry UI + clearer failure states  
3. Addressables group split  
4. Shared dependency cleanup  
5. Define critical preload policy  
6. Manifest/seed cleanup  
7. Rebuild and verify actual bundle outputs  
8. Optional skins on-demand loading  
9. Config/version simplification  
10. WebGL delivery tuning  
11. Automation / pipeline hardening  
12. Acceptance matrix validation

6. **Acceptance Criteria**

- Critical login-to-lobby preload includes only gameplay-critical content required before the first match.
- Optional skins/cosmetics are not part of required preload.
- Missing critical content fails clearly and safely before lobby or before match start, according to policy.
- No silent null prefab cases remain in runtime unit/skin resolution.
- No giant convenience bundle remains after regrouping.
- `Gameplay Core Units` contains only first-match-capable gameplay units and their exact rendering/animation dependencies.
- `Shared Gameplay Dependencies` is narrowly scoped and does not absorb miscellaneous content.
- Core preload no longer pulls optional skins/cosmetics.
- Optional content does not accidentally drag core bundles unnecessarily.
- Repeat sessions reuse cache appropriately and do not redownload unchanged critical content unnecessarily.
- First match has:
  - no missing models
  - no pink materials
  - no broken animators
  - no runtime critical downloads
- `critical_content` is reviewable for each release.
- Every newly critical item has a visible justification.
- Size budgets are met:
  - base build budget
  - critical remote preload budget
  - maximum single bundle budget

7. **Risks / Watchouts**

- Splitting groups without auditing real dependencies can recreate the same problem in smaller names but not smaller downloads.
- `Shared Gameplay Dependencies` is the highest-risk bloat sink; it must be treated as suspicious by default.
- On-demand loading can become an excuse to under-preload core units. Do not allow that in this phase.
- Leaving unused metadata fields undocumented will recreate false confidence about environment/version safety.
- A clean Addressables group layout does not guarantee a clean runtime download shape; only built bundle inspection does.
- If first-match-capable unit lists are vague, `critical_content` will bloat again through “temporary” exceptions.
- Size budgets that are proposed but not pipeline-enforced will drift almost immediately.
