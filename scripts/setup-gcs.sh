#!/usr/bin/env bash
# scripts/setup-gcs.sh
# Run once to create and configure the GCS bucket for addressable assets.
# Usage: GCS_PROJECT=your-project-id GCS_BUCKET=castle-defender-assets ./scripts/setup-gcs.sh

set -euo pipefail

PROJECT="${GCS_PROJECT:?Set GCS_PROJECT to your Google Cloud project ID}"
BUCKET="${GCS_BUCKET:?Set GCS_BUCKET to your desired bucket name (e.g. castle-defender-assets)}"
REGION="${GCS_REGION:-us-central1}"
ALLOWED_ORIGIN="${ALLOWED_ORIGIN:-*}"

echo "==> Creating bucket gs://$BUCKET in project $PROJECT ($REGION)"
gcloud storage buckets create "gs://$BUCKET" \
  --project="$PROJECT" \
  --location="$REGION" \
  --uniform-bucket-level-access

echo "==> Making bucket publicly readable"
gcloud storage buckets add-iam-policy-binding "gs://$BUCKET" \
  --member="allUsers" \
  --role="roles/storage.objectViewer"

echo "==> Setting CORS policy (allows Unity WebGL to fetch bundles)"
cat > /tmp/cors.json <<EOF
[
  {
    "origin": ["$ALLOWED_ORIGIN"],
    "method": ["GET", "HEAD"],
    "responseHeader": ["Content-Type", "Content-Encoding", "Accept-Ranges"],
    "maxAgeSeconds": 86400
  }
]
EOF
gcloud storage buckets update "gs://$BUCKET" --cors-file=/tmp/cors.json
rm /tmp/cors.json

echo ""
echo "==> Done! Bucket URL: https://storage.googleapis.com/$BUCKET"
echo ""
echo "Set these env vars on your server:"
echo "  ADDRESSABLES_CDN_URL=https://storage.googleapis.com/$BUCKET/addressables"
echo ""
echo "Next: run ./scripts/upload-bundles.sh to upload your addressable bundles."
