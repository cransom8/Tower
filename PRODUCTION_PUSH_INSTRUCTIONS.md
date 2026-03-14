# Production Push Instructions

Use this runbook when the request is: "read push instructions and do a push".

## Scope

This project has two connected repos/folders:

- Unity project: `C:\Users\Crans\RansomForge\CastleDefenderClient`
- Production server repo: `C:\Users\Crans\RansomForge\castle-defender`

The live Unity WebGL client for `app.ransomforge.com` is served by the server repo from:

- `C:\Users\Crans\RansomForge\castle-defender\server\client`

## Required access

This workflow assumes Codex has Unity MCP server access to the running Unity editor.

Expected capability:

- Read Unity editor state
- Target the active Unity instance
- Trigger the production WebGL build from the Unity editor menu
- Validate that Unity is responsive after the build

If Unity MCP access is missing, disconnected, stale, or unusable, stop and flag the user before attempting the production push.

## How production gets the Unity build

The Unity editor menu item below runs the production WebGL build pipeline:

- `Castle Defender/Build/Build WebGL Release`

That menu maps to:

- [BuildWebGL.cs](C:/Users/Crans/RansomForge/CastleDefenderClient/Assets/Scripts/Editor/BuildWebGL.cs)

What that script does:

1. Switches Unity to `WebGL`.
2. Uses Brotli compression.
3. Builds the enabled scenes, or falls back to:
   - `Assets/Scenes/Bootstrap.unity`
   - `Assets/Scenes/Login.unity`
   - `Assets/Scenes/Lobby.unity`
   - `Assets/Scenes/Loadout.unity`
   - `Assets/Scenes/Game_ML.unity`
   - `Assets/Scenes/PostGame.unity`
4. Builds into a temporary Unity folder named `WebGLBuild_Auto`.
5. Copies the finished build directly into:
   - `castle-defender/server/client`
6. Removes any nested wrapper folder Unity may have produced.

This means the production web client is not deployed from the Unity repo directly. It becomes production by being copied into the server repo and then pushed from the server repo.

## How the server serves the Unity client

The production server resolves its Unity client from:

- [index.js](C:/Users/Crans/RansomForge/castle-defender/server/index.js)

Relevant behavior:

- It prefers `server/client` as the Unity client directory.
- It serves `/`, `/Build/*`, `/TemplateData/*`, and `/client/*` from that Unity build output.
- It sets the correct headers for Unity Brotli WebGL files like:
  - `.framework.js.unityweb`
  - `.wasm.unityweb`
  - `.data.unityweb`

## Standard push flow

1. Confirm Unity code changes are done in `CastleDefenderClient`.
2. Run the Unity production build from the Unity editor:
   - `Castle Defender/Build/Build WebGL Release`
3. Wait for the build to finish.
4. Verify the server repo's Unity client output changed:
   - `C:\Users\Crans\RansomForge\castle-defender\server\client`
   - Especially `index.html`, `Build`, and `TemplateData`
5. In `C:\Users\Crans\RansomForge\castle-defender`, review git status.
6. Commit the server repo changes, including refreshed `server/client` assets.
7. Push `main` to `origin`.
8. Railway redeploys the app from the pushed server repo.
9. Verify on `https://app.ransomforge.com`.

## Commands to use after build completes

Run these from:

- `C:\Users\Crans\RansomForge\castle-defender`

Check what changed:

```powershell
git status --short
```

Stage everything intended for the release:

```powershell
git add -A
```

Commit:

```powershell
git commit -m "Deploy WebGL production build"
```

Push:

```powershell
git push origin main
```

## Exact push behavior Codex should follow

When asked to "read push instructions and do a push", do this:

1. Open this file.
2. Confirm Unity MCP server access is available and the correct Unity instance is reachable.
3. If MCP access is unavailable or unhealthy, flag the user and stop before the build/push.
4. Confirm whether the Unity build is still running or has finished.
5. If it is still running, wait and verify output timestamps in `castle-defender/server/client`.
6. Once finished, inspect `git status` in `castle-defender`.
7. Do not revert unrelated changes.
8. Commit only when the user wants the deployment pushed.
9. Push from the `castle-defender` repo, not from `CastleDefenderClient`.

## Notes from the 3D unit portrait fix push

The production issue investigated here affected shared runtime portrait rendering used by:

- Loadout phase
- CMD bar
- Build menu
- Tile/unit builder

The code fix was applied in the Unity project here:

- [RuntimePortraitStudio.cs](C:/Users/Crans/RansomForge/CastleDefenderClient/Assets/Scripts/UI/RuntimePortraitStudio.cs)
- [UnitPortraitCamera.cs](C:/Users/Crans/RansomForge/CastleDefenderClient/Assets/Scripts/UI/UnitPortraitCamera.cs)

The expectation for this push is:

- Build from Unity
- Copy into `castle-defender/server/client`
- Commit and push the server repo
- Validate on production
