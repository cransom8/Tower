#!/usr/bin/env bash
# scripts/deploy.sh
# Build and deploy the server to Google Cloud Run.
# Usage: GCS_PROJECT=your-project-id ./scripts/deploy.sh

set -euo pipefail

PROJECT="${GCS_PROJECT:?Set GCS_PROJECT to your Google Cloud project ID}"
SERVICE="${CLOUD_RUN_SERVICE:-castle-defender}"
REGION="${GCS_REGION:-us-central1}"
IMAGE="gcr.io/$PROJECT/$SERVICE"

echo "==> Building Docker image: $IMAGE"
docker build -t "$IMAGE" .

echo "==> Pushing image to Google Container Registry"
docker push "$IMAGE"

echo "==> Deploying to Cloud Run ($SERVICE in $REGION)"
gcloud run deploy "$SERVICE" \
  --image="$IMAGE" \
  --platform=managed \
  --region="$REGION" \
  --project="$PROJECT" \
  --port=8080 \
  --min-instances=1 \
  --max-instances=10 \
  --memory=512Mi \
  --cpu=1 \
  --timeout=3600 \
  --session-affinity \
  --allow-unauthenticated \
  --set-env-vars="NODE_ENV=production"

echo ""
echo "==> Deploy complete! Service URL:"
gcloud run services describe "$SERVICE" \
  --platform=managed \
  --region="$REGION" \
  --project="$PROJECT" \
  --format="value(status.url)"
echo ""
echo "Remember to set env vars on the service (DATABASE_URL, JWT_SECRET, etc.):"
echo "  gcloud run services update $SERVICE --region=$REGION --set-env-vars=KEY=VALUE"
