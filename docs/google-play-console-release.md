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

- App name in Play Console: `Ransom Forge`
- Unity product name: keep as-is unless you want the installed app label to change from `Castle Defender`
- Support website: `https://app.ransomforge.com`
- Privacy policy: use the hosted version of `PRIVACY_POLICY.md`
- Support email: `support@ransomforge.com`

If you want the installed Android app label to say `Ransom Forge`, also update the Unity product name before building.

## Build order for this project

This project uses remote Addressables content, so release order matters:

1. Rebuild and publish production Addressables content.
2. Verify the live manifest and remote bundles.
3. Build the Android player.
4. Test the Android build against the same production or staging content endpoints you intend to use.
5. Upload the tested `.aab` to Play Console.

See also:

- `docs/remote-content-launch-checklist.md`

## Play Console flow

1. Open Play Console and create the app as a `Game`.
2. Use the permanent package name that matches the Unity Android package name.
3. Accept Play App Signing during first release setup.
4. Upload the signed `.aab` to `Internal testing`.
5. Add testers and verify:
   - install works from Play
   - login works
   - remote content downloads successfully
   - gameplay scenes load
   - no missing materials, prefabs or portraits
6. Complete store listing assets:
   - app icon
   - phone screenshots
   - short description
   - full description
   - category/tags
7. Complete all App content tasks until nothing critical is blocked.
8. Roll out to production, ideally with managed publishing if launch timing matters.

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

## Good first release path

1. Decide the final package name.
2. Generate the upload keystore.
3. Configure Android player settings in Unity.
4. Build an internal-test `.aab`.
5. Create the Play app and complete App content.
6. Upload to internal testing.
7. Test install/login/gameplay/remote content.
8. Promote to closed test or production.
