# Google Play Console Release Guide

This guide is for shipping the Unity client in this repo to Google Play.

## Current project state

As of this repo snapshot:

- Unity version: `6000.3.10f1`
- Product name: `Castle Defender`
- Android package name: not set yet
- App version: `1.0`
- Android version code: `1`
- Min SDK: `25`
- Target SDK: automatic/highest installed
- Architectures: `ARM64` only
- Custom Android keystore: not configured
- Google Play API upload script: `scripts/publish-google-play.js`

Source of truth:

- `unity-client/ProjectSettings/ProjectVersion.txt`
- `unity-client/ProjectSettings/ProjectSettings.asset`

## First-time Play release checklist

1. Choose a permanent Android package name.
   Example format: `com.ransomforge.castledefender`
   Play package names are unique and cannot be reused later, so do not change this casually.
2. Create or import a release keystore.
   Use a dedicated upload key and store the keystore/passwords outside git.
3. Set Android player settings in Unity.
   - Build Target: `Android`
   - Build App Bundle: `On`
   - Package Name: your permanent package name
   - Version: bump from `1.0` as needed
   - Bundle Version Code: increment every upload
   - Target API Level: set this explicitly to the latest Play-supported stable API installed in Unity
4. Build and test a signed `.aab`.
5. Create the app in Play Console.
6. Complete the App content declarations.
   - Privacy policy
   - Data safety
   - Ads declaration
   - Content rating
   - Target audience
   - App access instructions if review requires login
7. Fill out the store listing.
8. Upload the `.aab` to an internal or closed testing track first.
9. Fix all pre-launch report and review issues.
10. Promote the tested release to production.

Important:

- The API automation in this repo helps upload a signed `.aab` to an existing Play app.
- It does not remove the need to create the Play app, configure app content, or finish first-release console setup.

## Unity settings to change in this repo

Open `unity-client` in Unity and update:

- `Edit > Project Settings > Player > Android`
  - Set `Package Name`
  - Set `Version`
  - Set `Bundle Version Code`
  - Enable `Custom Keystore`
  - Select your keystore and key alias
  - Set `Target API Level` explicitly instead of leaving it on automatic
- `File > Build Profiles` or `Build Settings`
  - Platform: `Android`
  - Build System: keep Unity default unless you have a Gradle customization reason
  - Build App Bundle (`.aab`): enabled

Notes for this project:

- The repo was missing an Android package name and keystore configuration, so those must be supplied before the first Play upload.
- The build settings scene list had a broken `PostGame` scene path; that has been corrected in this repo so Android builds use the real scene asset.

## Recommended repo-specific values

- App name in Play Console: `RansomForge`
- Unity product name: `RansomForge`
- Android launcher icon source: `unity-client/Assets/Branding/RansomForgeAppIcon.png`
- Google Play listing icon source: `unity-client/Assets/Branding/RansomForgePlayStoreIcon.png`
- Support website: `https://app.ransomforge.com`
- Privacy policy: use the hosted version of `PRIVACY_POLICY.md`
- Support email: `support@ransomforge.com`

The installed Android app label now comes from Unity product name `RansomForge`, and the Android build step reapplies the committed launcher icons before building.

## Build order for this project

This project uses remote Addressables content, so release order matters:

1. Rebuild and publish production Addressables content.
2. Verify the live manifest and remote bundles.
3. Build the Android player.
4. Test the Android build against the same production or staging content endpoints you intend to use.
5. Upload the tested `.aab` to Play Console.
6. Upload the matching native debug symbols zip for the same version code if Unity produced one.

See also:

- `docs/remote-content-launch-checklist.md`

## Play Console flow

1. Open Play Console and create the app as a `Game`.
2. Use the permanent package name that matches the Unity Android package name.
3. Accept Play App Signing during first release setup.
4. Upload the signed `.aab` to `Internal testing`.
5. Upload the matching native debug symbols zip if Play shows the native-symbol warning.
   - For this repo's recent builds, Unity generated files like `builds/android/forge-wars-1.0-v6-IL2CPP.symbols.zip`.
   - Match the zip to the same Android version code as the uploaded bundle.
6. Add testers and verify:
   - install works from Play
   - login works
   - remote content downloads successfully
   - gameplay scenes load
   - no missing materials, prefabs or portraits
7. Complete store listing assets:
   - app icon
   - phone screenshots
   - short description
   - full description
   - category/tags
8. Complete all App content tasks until nothing critical is blocked.
9. Roll out to production, ideally with managed publishing if launch timing matters.

## Release gates before production

- Android package name is final and matches Play Console exactly
- Keystore is backed up securely
- Version code is higher than every previous upload
- Signed `.aab` installs from Play internal testing
- Privacy policy URL is live and matches actual data practices
- Data safety answers match the app and backend
- Reviewer test credentials or access instructions are provided if login is required
- Remote Addressables content is already published for the build being reviewed

## Likely blockers right now

- No Android package name is configured
- No release keystore is configured
- Target API is on automatic rather than pinned explicitly
- Store listing assets for Play are not tracked in this repo yet
- App content declarations still need to be completed in Play Console

## Google Play API automation in this repo

This repo now includes a Play upload script:

- Command: `npm run play:upload -- --dry-run`
- Script: `scripts/publish-google-play.js`

The script can:

- authenticate with a Google service account
- create a Play edit
- upload a signed Android App Bundle (`.aab`)
- assign it to a track such as `internal`, `beta`, or `production`
- commit the edit

### Required inputs

Supply these with CLI flags or environment variables:

- `GOOGLE_PLAY_PACKAGE_NAME`
- `GOOGLE_PLAY_AAB_PATH`
- authentication, using one of these modes:
  - `adc` / workload identity / application default credentials
  - `oauth` refresh token
  - `service-account` legacy JSON key fallback

Optional:

- `GOOGLE_PLAY_AUTH_MODE` default auto-detects, preferred `adc`
- `GOOGLE_PLAY_TRACK` default `internal`
- `GOOGLE_PLAY_RELEASE_NAME`
- `GOOGLE_PLAY_RELEASE_STATUS` default `completed`
- `GOOGLE_PLAY_RELEASE_NOTES_FILE`
- `GOOGLE_PLAY_RELEASE_NOTES_LANGUAGE` default `en-US`
- `GOOGLE_PLAY_RELEASE_NOTES_TEXT`
- `GOOGLE_PLAY_USER_FRACTION` for staged rollout
- `GOOGLE_PLAY_IN_APP_UPDATE_PRIORITY` from `0` to `5`
- `GOOGLE_PLAY_CHANGES_NOT_SENT_FOR_REVIEW`

Android build note:

- Release AAB builds can auto-increment the Play version code when `ANDROID_AUTO_INCREMENT_VERSION_CODE=true` is present in `.local-secrets/forge-wars-upload.env`.
- `ANDROID_BUNDLE_VERSION_CODE` acts as the floor for that increment, and archived files in `builds/android/releases` are also considered.
- Example: if the env file says `5` and the latest archived bundle is `forge-wars-v1.0-code5.aab`, the next release build will use version code `6`.
- Local APK builds keep their current/manual version code behavior.
- Unity IL2CPP builds in this repo also emit a native debug symbols zip next to the AAB, for example `builds/android/forge-wars-1.0-v6-IL2CPP.symbols.zip`.
- Google Play's "missing deobfuscation file" warning can be ignored when Android minification is off. In this repo, `AndroidMinifyRelease` is currently disabled in `unity-client/ProjectSettings/ProjectSettings.asset`, so there is no ProGuard/R8 mapping file to upload for the current build.

### Example

PowerShell:

```powershell
$env:GOOGLE_PLAY_PACKAGE_NAME = "com.ransomforge.castledefender"
$env:GOOGLE_PLAY_AAB_PATH = "C:\builds\castle-defender.aab"
$env:GOOGLE_PLAY_AUTH_MODE = "adc"
$env:GOOGLE_PLAY_TRACK = "internal"
$env:GOOGLE_PLAY_RELEASE_NAME = "Internal test build 1"
npm run play:upload
```

Dry-run validation:

```powershell
npm run play:upload -- --dry-run
```

Release notes:

- Plain text file: `GOOGLE_PLAY_RELEASE_NOTES_FILE=release-notes.txt`
- JSON file: `GOOGLE_PLAY_RELEASE_NOTES_FILE=release-notes.json`
- JSON object format example:

```json
{
  "en-US": "Initial internal test build.",
  "es-ES": "Compilacion inicial para pruebas internas."
}
```

### Service account setup

Before the script can publish, make sure:

1. The app already exists in Play Console with the exact final package name.
2. The identity you use has Play Console access for the app with release/upload permissions.
4. The uploaded `.aab` is signed with the upload keystore tied to the Play app.

### Preferred auth modes

`adc` / workload identity / federated credentials:

- Best for automation because it avoids long-lived private keys.
- The script now supports Application Default Credentials through `google-auth-library`.
- That means it can use:
  - `GOOGLE_APPLICATION_CREDENTIALS` pointing to a workload identity federation credential config
  - or a local ADC login from `gcloud auth application-default login`

`oauth` refresh token:

- Good for manual operator-driven uploads from a specific Google account.
- Provide:
  - `GOOGLE_PLAY_OAUTH_CLIENT_ID`
  - `GOOGLE_PLAY_OAUTH_CLIENT_SECRET`
  - `GOOGLE_PLAY_OAUTH_REFRESH_TOKEN`
- This repo includes a helper to mint the refresh token:
  - `npm run play:oauth-token -- --client-id YOUR_CLIENT_ID --client-secret YOUR_CLIENT_SECRET --login-hint you@example.com`

`service-account`:

- Still supported as a fallback for existing setups.
- Prefer not to use it for new automation unless you specifically want key-based auth.

### OAuth refresh token setup

1. In Google Cloud Console, create OAuth credentials for a `Desktop app`.
2. Add your Google account to the Play Console app with the release permissions you need.
3. Run the helper from this repo:

```powershell
npm run play:oauth-token -- --client-id YOUR_CLIENT_ID --client-secret YOUR_CLIENT_SECRET --login-hint you@example.com
```

4. Open the printed URL in a browser while signed into the Google account you want to use.
5. Approve access and let Google redirect back to the local callback.
6. Copy the printed values into your shell:

```powershell
$env:GOOGLE_PLAY_AUTH_MODE = "oauth"
$env:GOOGLE_PLAY_OAUTH_CLIENT_ID = "..."
$env:GOOGLE_PLAY_OAUTH_CLIENT_SECRET = "..."
$env:GOOGLE_PLAY_OAUTH_REFRESH_TOKEN = "..."
```

7. Run the uploader:

```powershell
npm run play:upload
```

### Suggested first automated path

1. Finish Unity Android settings and keystore setup.
2. Build a signed `.aab`.
3. Create the Play app in the console.
4. Grant the service account Play access.
5. Run `npm run play:upload -- --dry-run`.
6. Run `npm run play:upload` to push to `internal`.
7. Verify install, login, and remote content.

## Good first release path

1. Decide the final package name.
2. Generate the upload keystore.
3. Configure Android player settings in Unity.
4. Build an internal-test `.aab`.
5. Create the Play app and complete App content.
6. Upload to internal testing.
7. Test install/login/gameplay/remote content.
8. Promote to closed test or production.
