#!/usr/bin/env bash
# scripts/deploy.sh
# Deploy the server to Google Cloud Run from source.
# Usage:
#   GCS_PROJECT=ransom-forge-game \
#   CLOUD_RUN_ENV_FILE=deploy/cloudrun.env \
#   CLOUD_RUN_SECRETS="DATABASE_URL=castle-defender-database-url:latest,JWT_SECRET=castle-defender-jwt-secret:latest" \
#   ./scripts/deploy.sh

set -euo pipefail

PROJECT="${GCS_PROJECT:-${GOOGLE_CLOUD_PROJECT:-}}"
SERVICE="${CLOUD_RUN_SERVICE:-castle-defender}"
REGION="${GCS_REGION:-us-central1}"
SOURCE="${CLOUD_RUN_SOURCE:-.}"
ENV_FILE="${CLOUD_RUN_ENV_FILE:-deploy/cloudrun.env}"
CPU="${CLOUD_RUN_CPU:-2}"
MEMORY="${CLOUD_RUN_MEMORY:-2Gi}"
MIN_INSTANCES="${CLOUD_RUN_MIN:-1}"
MAX_INSTANCES="${CLOUD_RUN_MAX:-1}"
CONCURRENCY="${CLOUD_RUN_CONCURRENCY:-250}"
TIMEOUT="${CLOUD_RUN_TIMEOUT:-3600s}"
SECRETS="${CLOUD_RUN_SECRETS:-}"
CLOUDSQL_INSTANCE="${CLOUD_RUN_CLOUDSQL_INSTANCE:-}"
SERVICE_ACCOUNT="${CLOUD_RUN_SERVICE_ACCOUNT:-}"

if [[ -z "${PROJECT}" ]]; then
  PROJECT="$(gcloud config get-value core/project 2>/dev/null || true)"
fi

if [[ -z "${PROJECT}" ]]; then
  echo "Set GCS_PROJECT or GOOGLE_CLOUD_PROJECT, or configure a default project with gcloud." >&2
  exit 1
fi

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Cloud Run env file not found: ${ENV_FILE}" >&2
  echo "Copy deploy/cloudrun.env.example to deploy/cloudrun.env and fill in the non-secret values." >&2
  exit 1
fi

args=(
  run deploy "${SERVICE}"
  "--source=${SOURCE}"
  "--project=${PROJECT}"
  "--region=${REGION}"
  "--platform=managed"
  "--allow-unauthenticated"
  "--execution-environment=gen2"
  "--port=8080"
  "--cpu=${CPU}"
  "--memory=${MEMORY}"
  "--concurrency=${CONCURRENCY}"
  "--min=${MIN_INSTANCES}"
  "--max=${MAX_INSTANCES}"
  "--timeout=${TIMEOUT}"
  "--cpu-boost"
  "--no-cpu-throttling"
  "--env-vars-file=${ENV_FILE}"
  "--startup-probe=httpGet.path=/health,httpGet.port=8080,timeoutSeconds=5,periodSeconds=10,failureThreshold=6"
)

if [[ -n "${SECRETS}" ]]; then
  args+=("--update-secrets=${SECRETS}")
fi

if [[ -n "${CLOUDSQL_INSTANCE}" ]]; then
  args+=("--set-cloudsql-instances=${CLOUDSQL_INSTANCE}")
fi

if [[ -n "${SERVICE_ACCOUNT}" ]]; then
  args+=("--service-account=${SERVICE_ACCOUNT}")
fi

echo "==> Deploying Cloud Run service ${SERVICE}"
echo "    project: ${PROJECT}"
echo "    region: ${REGION}"
echo "    env file: ${ENV_FILE}"
echo "    cpu/memory: ${CPU}/${MEMORY}"
echo "    scaling: min=${MIN_INSTANCES} max=${MAX_INSTANCES}"
echo "    concurrency: ${CONCURRENCY}"
echo ""
echo "This backend owns live matches in process memory."
echo "Keep Cloud Run pinned to one instance until runtime state is externalized."
echo ""

gcloud "${args[@]}"

echo ""
echo "==> Deploy complete. Service URL:"
gcloud run services describe "${SERVICE}" \
  --project="${PROJECT}" \
  --region="${REGION}" \
  --format="value(status.url)"
