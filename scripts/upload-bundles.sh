#!/usr/bin/env bash
# scripts/upload-bundles.sh
# Upload Unity addressable bundles and catalogs to GCS after each build.
# Usage: GCS_BUCKET=castle-defender-assets GCS_PROJECT=ransom-forge-game ./scripts/upload-bundles.sh
#
# Run this after every Unity Addressables build (when ServerData/WebGL/ changes).
#
# GCS layout:
#   addressables/catalog.bin, catalog.hash, settings.json  (catalogs — no-cache)
#   addressables/WebGL/*.bundle                            (bundles — Unity appends /WebGL/ to RemoteLoadPath)

set -euo pipefail

BUCKET="${GCS_BUCKET:?Set GCS_BUCKET to your bucket name (e.g. castle-defender-assets)}"
SERVERDATA_DIR="${SERVERDATA_DIR:-unity-client/ServerData/WebGL}"
PROJECT="${GCS_PROJECT:-}"

if [ ! -d "$SERVERDATA_DIR" ]; then
  echo "ERROR: ServerData dir not found: $SERVERDATA_DIR"
  echo "Run from the repo root or set SERVERDATA_DIR."
  exit 1
fi

GSUTIL_CMD="${GSUTIL_CMD:-gsutil}"

echo "==> Uploading catalog and settings to gs://$BUCKET/addressables/"
echo "    Source: $SERVERDATA_DIR/{catalog.*,settings.json}"
echo ""

# Upload catalogs and settings to the root of addressables/ (no WebGL/ prefix).
for f in catalog.bin catalog.hash catalog_1.0.bin catalog_1.0.hash settings.json; do
  src="$SERVERDATA_DIR/$f"
  if [ -f "$src" ]; then
    "$GSUTIL_CMD" cp "$src" "gs://$BUCKET/addressables/$f"
    "$GSUTIL_CMD" setmeta -h "Cache-Control:public, max-age=0, must-revalidate" "gs://$BUCKET/addressables/$f" 2>/dev/null || true
  fi
done

echo ""
echo "==> Uploading bundles to gs://$BUCKET/addressables/WebGL/"
echo "    Source: $SERVERDATA_DIR/*.bundle"
echo ""

# Upload bundles to addressables/WebGL/ — Unity appends the platform name (WebGL)
# to ADDRESSABLES_CDN_URL at runtime, so this is where it looks for them.
"$GSUTIL_CMD" -m cp "$SERVERDATA_DIR"/*.bundle "gs://$BUCKET/addressables/WebGL/"

echo ""
echo "==> Upload complete!"
echo "    Catalogs : https://storage.googleapis.com/$BUCKET/addressables/"
echo "    Bundles  : https://storage.googleapis.com/$BUCKET/addressables/WebGL/"
