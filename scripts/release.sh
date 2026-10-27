#!/usr/bin/env bash
# Resolves build artifacts and creates a draft GitHub Release.
# Used by the release job in .github/workflows/official_release_pipeline.yml.
set -euo pipefail

COMMAND="${1:-}"

case "$COMMAND" in
  resolve-run)
    BUILD_RUN_ID="${2:-}"
    OUTPUT_FILE="${3:-${GITHUB_OUTPUT:-}}"
    if [ -z "$OUTPUT_FILE" ]; then
      echo "Usage: bash scripts/release.sh resolve-run [build-run-id] <github-output-file>" >&2
      exit 1
    fi

    if [ -n "$BUILD_RUN_ID" ]; then
      RUN_ID="$BUILD_RUN_ID"
      echo "Using provided run ID: $RUN_ID"
    else
      RUN_ID=$(gh run list \
        --workflow=official_build_pipeline.yml \
        --status=success \
        --limit=1 \
        --json databaseId \
        --jq '.[0].databaseId')
      if [ -z "$RUN_ID" ] || [ "$RUN_ID" = "null" ]; then
        echo "No successful Official Build Pipeline run was found." >&2
        exit 1
      fi
      echo "Resolved latest successful build run ID: $RUN_ID"
    fi
    echo "run_id=$RUN_ID" >> "$OUTPUT_FILE"
    ;;
  read-version)
    VERSION_FILE="${2:-}"
    OUTPUT_FILE="${3:-${GITHUB_OUTPUT:-}}"
    if [ -z "$VERSION_FILE" ] || [ -z "$OUTPUT_FILE" ]; then
      echo "Usage: bash scripts/release.sh read-version <version-file> <github-output-file>" >&2
      exit 1
    fi
    VERSION=$(<"$VERSION_FILE")
    if [ -z "$VERSION" ]; then
      echo "Version file is empty: $VERSION_FILE" >&2
      exit 1
    fi
    echo "version=$VERSION" >> "$OUTPUT_FILE"
    echo "Releasing version: $VERSION"
    ;;
  create)
    VERSION="${2:-}"
    RUN_ID="${3:-}"
    SERVER_URL="${4:-${GITHUB_SERVER_URL:-}}"
    REPOSITORY="${5:-${GITHUB_REPOSITORY:-}}"
    ARTIFACT_DIRECTORY="${6:-./artifacts}"
    if [ -z "$VERSION" ] || [ -z "$RUN_ID" ] || [ -z "$SERVER_URL" ] || [ -z "$REPOSITORY" ]; then
      echo "Usage: bash scripts/release.sh create <version> <run-id> <server-url> <repository> [artifact-directory]" >&2
      exit 1
    fi

    TAG="v$VERSION"
    ARCHIVE="dst-binaries-${TAG}.zip"
    (cd "$ARTIFACT_DIRECTORY" && zip -r "../$ARCHIVE" .)
    gh release create "$TAG" "$ARCHIVE" \
      --verify-tag \
      --title "Release $TAG" \
      --notes "Released from build run [$RUN_ID]($SERVER_URL/$REPOSITORY/actions/runs/$RUN_ID)" \
      --draft
    ;;
  *)
    echo "Usage: bash scripts/release.sh {resolve-run|read-version|create} ..." >&2
    exit 1
    ;;
esac
