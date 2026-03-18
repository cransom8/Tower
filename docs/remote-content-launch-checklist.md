# Remote Content Launch Checklist

## Must Have Before Launch

- Publish a production Addressables catalog and remote bundles for all launch-critical unit prefabs.
- Mark every launch-critical unit prefab as Addressable and verify its address matches the admin `prefab_address`.
- Add admin metadata for every launch-critical unit:
  - `content_key`
  - `addressables_label`
  - `prefab_address`
  - `dependency_keys`
  - `version_tag`
  - `catalog_url` or `content_url` if used in your pipeline
- Add admin metadata for any launch-critical skins that must render correctly in-match.
- Make shared dependencies Addressable too:
  - materials
  - textures
  - animator controllers
  - VFX
  - any shared prefabs loaded at spawn time
- Run the Unity audit tool:
  - `Castle Defender/Remote Content/Audit Registry Readiness`
- Resolve every audit warning for launch-critical entries.
- Test at least one unit with its local registry prefab removed or nulled so it proves remote loading works without fallback.
- Test at least one skin with its local registry skin prefab removed or nulled so it proves remote skin loading works without fallback.
- Verify login flow blocks before Lobby when critical preload fails.
- Add final user-facing copy for preload failure and retry states.
- Verify `/api/content-manifest` in the target environment returns the exact live keys used by the Addressables build.
- Confirm the server is restarted in production with the new metadata/manifests live.

## Must Pass In QA

- Fresh install:
  - login downloads required critical content
  - enters Lobby only after preload completes
- Repeat session:
  - cached content is reused
  - no unnecessary redownload of unchanged packs
- Content update:
  - increment `version_tag` or content hash
  - confirm client fetches updated content cleanly
- Offline or bad network:
  - manifest fetch failure blocks correctly
  - content download failure blocks correctly
  - retry path works
- Gameplay spawn validation:
  - wave units spawn with no missing meshes/materials/animators
  - skinned units render correctly
  - no partial-load visuals mid-wave
- Portrait and preview validation:
  - loadout portraits still render for remote-loaded units
  - skin previews work for remote-loaded skins
- WebGL performance:
  - acceptable login-to-lobby preload time
  - acceptable memory footprint after preload
  - no major hitching on first combat wave
- Cache behavior:
  - browser refresh
  - returning player session
  - cleared cache / hard reload

## Strongly Recommended Before Launch

- Add analytics/logging around:
  - manifest fetch success/failure
  - preload duration
  - per-key download failures
  - retry counts
- Add an admin or script-driven validation pass that compares:
  - DB metadata keys
  - Unity Addressables entries
  - published catalog contents
- Separate launch-critical content from optional cosmetics in metadata.
- Set up a staging catalog/storage location that mirrors production.
- Write a rollback procedure for bad remote-content publishes.

## Post-Launch Cleanup

- Remove direct local prefab references for units that are fully remoteized.
- Reduce WebGL base build by stripping gameplay-critical prefabs that are now remote-only.
- Move from preload-all-critical-at-login to a smaller policy if desired:
  - core gameplay set after login
  - match-specific delta before game
- Add background download support for optional cosmetics only.
- Add automated content-pipeline tooling so admin metadata and Unity Addressables stay in sync.

## Launch Go/No-Go

Launch is a `go` when all of these are true:

- Every launch-critical unit has valid admin metadata.
- Every required prefab and dependency is in the published Addressables catalog.
- Unity preload succeeds on a fresh client.
- At least one remote-only unit and one remote-only skin have passed end-to-end testing.
- Failure and retry behavior is clear and safe.
- WebGL load time and memory are acceptable for release.
