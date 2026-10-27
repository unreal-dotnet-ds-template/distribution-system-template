#!/usr/bin/env bash
# Writes the version, tag, and run ID to the GitHub Actions step summary.
# Used by .github/workflows/official_build_pipeline.yml.
set -euo pipefail

VERSION="${1:-}"
TAG="${2:-}"
RUN_ID="${3:-${GITHUB_RUN_ID:-}}"
SUMMARY_FILE="${GITHUB_STEP_SUMMARY:-}"

if [ -z "$VERSION" ] || [ -z "$TAG" ] || [ -z "$RUN_ID" ] || [ -z "$SUMMARY_FILE" ]; then
  echo "Usage: bash scripts/build-summary.sh <version> <tag> <run-id> (GITHUB_STEP_SUMMARY must be set)" >&2
  exit 1
fi

{
  echo "### ✅ Build complete"
  echo "| | |"
  echo "|---|---|"
  echo "| **Version** | \`$VERSION\` |"
  echo "| **Git tag** | \`$TAG\` |"
  echo "| **Run ID** | \`$RUN_ID\` — use in Official Release Pipeline |"
} >> "$SUMMARY_FILE"
