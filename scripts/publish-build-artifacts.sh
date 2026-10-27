#!/usr/bin/env bash
# Publishes the solution and writes its resolved version alongside the artifacts.
# Used by .github/workflows/official_build_pipeline.yml.
set -euo pipefail

VERSION="${1:-}"
OUTPUT_DIRECTORY="${2:-./publish}"
CONFIGURATION="${3:-Release}"

if [ -z "$VERSION" ]; then
  echo "Usage: bash scripts/publish-build-artifacts.sh <version> [output-directory] [configuration]" >&2
  exit 1
fi

dotnet publish --no-build --configuration "$CONFIGURATION" --output "$OUTPUT_DIRECTORY"
echo "$VERSION" > "$OUTPUT_DIRECTORY/version.txt"
