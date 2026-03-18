#!/usr/bin/env bash
# scripts/upload-bundles.sh
# Upload Unity addressable bundles and catalogs to GCS after each build.
# Usage: GCS_BUCKET=castle-defender-assets ./scripts/upload-bundles.sh
#
# Run this after every Unity Addressables build (when ServerData/WebGL/ changes).

set -euo pipefail

BUCKET="${GCS_BUCKET:?Set GCS_BUCKET to your bucket name (e.g. castle-defender-assets)}"
SERVERDATA_DIR="${SERVERDATA_DIR:-unity-client/ServerData/WebGL}"
GCS_PREFIX="addressables"

if [ ! -d "$SERVERDATA_DIR" ]; then
  echo "ERROR: ServerData dir not found: $SERVERDATA_DIR"
  echo "Run from the repo root or set SERVERDATA_DIR."
  exit 1
fi

echo "==> Uploading addressable content to gs://$BUCKET/$GCS_PREFIX/"
echo "    Source: $SERVERDATA_DIR"
echo ""

# Upload everything — gcloud storage rsync handles adds, updates, and deletes.
# --delete-unmatched-destination-objects removes old bundles no longer in source.
gcloud storage rsync "$SERVERDATA_DIR" "gs://$BUCKET/$GCS_PREFIX" \
  --recursive \
  --delete-unmatched-destination-objects \
  --project="${GCS_PROJECT:-}"

echo ""
echo "==> Setting cache headers"

# Bundles are content-addressed (hash in filename) — cache forever.
gcloud storage objects update "gs://$BUCKET/$GCS_PREFIX/**/*.bundle" \
  --cache-control="public, max-age=31536000, immutable" 2>/dev/null || true

# Catalogs and settings change with every build — must not be cached.
for f in catalog.bin catalog.hash catalog_1.0.bin catalog_1.0.hash settings.json; do
  gcloud storage objects update "gs://$BUCKET/$GCS_PREFIX/WebGL/$f" \
    --cache-control="public, max-age=0, must-revalidate" 2>/dev/null || true
  gcloud storage objects update "gs://$BUCKET/$GCS_PREFIX/$f" \
    --cache-control="public, max-age=0, must-revalidate" 2>/dev/null || true
done

echo ""
echo "==> Upload complete!"
echo "    CDN URL: https://storage.googleapis.com/$BUCKET/$GCS_PREFIX"
