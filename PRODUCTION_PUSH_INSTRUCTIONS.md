# Production Push Instructions

Use this runbook when the request is: "read push instructions and do a push".

## Scope

The active production repo is:

- `C:\Users\Crans\RansomForge\castle-defender`

The Unity project now lives inside that repo:

- `C:\Users\Crans\RansomForge\castle-defender\unity-client`

WebGL release pushes are retired. The deployment path going forward is Android only.

## Primary entry point

Run the Unity editor menu item:

- `Castle Defender/Deploy Android`

That pipeline now handles the Android release flow in one place:

1. Builds Addressables for `Android`.
2. Writes refreshed remote content into:
   - `unity-client/ServerData/Android`
3. Builds the signed Android App Bundle:
   - stable path: `builds/android/forge-wars.aab`
   - archived release copy: `builds/android/releases/...`
4. Uploads Android Addressables to Google Cloud Storage.
5. Stages the Railway-facing Android catalog/hash/settings files in git so they can be committed and pushed.

## Supporting scripts

The menu pipeline calls this uploader:

- `scripts/upload-addressables.ps1`

You can also run it manually from the repo root if needed:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\upload-addressables.ps1 -Platform Android -StageRailwayMetadata
```

That uploads:

- `unity-client/ServerData/Android/catalog*.bin`
- `unity-client/ServerData/Android/catalog*.hash`
- `unity-client/ServerData/Android/settings.json`
- all `unity-client/ServerData/Android/*.bundle`

to:

- `gs://castle-defender-assets/addressables/Android/`

and stages the tracked metadata files for Railway.

## Standard release flow

1. Finish the Unity and server changes intended for the release.
2. Run `Castle Defender/Deploy Android`.
3. Wait for the menu pipeline to finish successfully.
4. Verify the output `.aab` exists under:
   - `builds/android/forge-wars.aab`
   - `builds/android/releases`
5. Review the staged Railway metadata and any other release changes:

```powershell
git status --short
```

6. Commit the intended release changes:

```powershell
git commit -m "Deploy Android release"
```

7. Push `main`:

```powershell
git push origin main
```

8. Railway redeploys from the pushed repo.

## Exact push behavior Codex should follow

When asked to "read push instructions and do a push", do this:

1. Open this file.
2. Confirm Unity MCP access is available and the correct Unity instance is reachable.
3. If MCP access is unavailable or unhealthy, stop and flag the user before building.
4. Run `Castle Defender/Deploy Android` unless the user explicitly says the pipeline already finished.
5. Inspect `git status` in `castle-defender`.
6. Do not revert unrelated changes.
7. Commit only what the user wants in the release.
8. Push from the `castle-defender` repo.
