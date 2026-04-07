# Google Cloud Run Deployment

This repo can be deployed to Google Cloud Run without Railway. The recommended shape for the current backend is:

- Cloud Run service with `min=1` and `max=1`
- `2 vCPU` and `2Gi` memory
- `--no-cpu-throttling`
- Google Cloud Storage for addressables

The single-instance constraint is intentional. The server keeps live room, party, reconnect, and match ownership in in-memory maps under `server/state/runtimeState.js`. Horizontal autoscaling would split players across instances and break lobby or match ownership.

## Files To Prepare

1. Copy `deploy/cloudrun.env.example` to `deploy/cloudrun.env`.
2. Fill in the non-secret runtime values in `deploy/cloudrun.env`.
3. Create the required Google Secret Manager secrets and map them using `deploy/cloudrun.secrets.example.txt` as the reference list.

## PowerShell Deploy

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy-cloud-run.ps1 `
  -Project ransom-forge-game `
  -Secret @(
    "DATABASE_URL=castle-defender-database-url:latest",
    "JWT_SECRET=castle-defender-jwt-secret:latest",
    "SMTP_USER=castle-defender-smtp-user:latest",
    "SMTP_PASS=castle-defender-smtp-pass:latest",
    "ADMIN_SECRET=castle-defender-admin-secret:latest",
    "OPENAI_API_KEY=openai-api-key:latest",
    "ELEVENLABS_API_KEY=elevenlabs-api-key:latest"
  )
```

If the database is moved to Cloud SQL later, add:

```powershell
-CloudSqlInstance "ransom-forge-game:us-central1:castle-defender-db"
```

## Bash Deploy

If you are working in a bash-compatible shell:

```bash
GCS_PROJECT=ransom-forge-game \
CLOUD_RUN_SERVICE=castle-defender \
CLOUD_RUN_ENV_FILE=deploy/cloudrun.env \
CLOUD_RUN_SECRETS="DATABASE_URL=castle-defender-database-url:latest,JWT_SECRET=castle-defender-jwt-secret:latest" \
./scripts/deploy.sh
```

## Cutover Notes

- Keep the public client pointed at `https://app.ransomforge.com`, then move that domain to the Cloud Run service once the service is healthy.
- The Android addressables pipeline already uploads bundles to Google Cloud Storage. That part does not need Railway.
- `tick over budget` in the current incident log looks like the game loop is CPU-starved. Moving to Cloud Run with dedicated CPU and no throttling may help, but it does not make the backend horizontally scalable.
