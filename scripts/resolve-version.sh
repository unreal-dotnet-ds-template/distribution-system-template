#!/usr/bin/env bash
# Selects the semantic-version bump from commits since the last version tag.
# Used by .github/workflows/official_build_pipeline.yml.
set -euo pipefail

OUTPUT_FILE="${1:-${GITHUB_OUTPUT:-}}"
if [ -z "$OUTPUT_FILE" ]; then
  echo "Usage: bash scripts/resolve-version.sh <github-output-file>" >&2
  exit 1
fi

if LAST_TAG=$(git describe --tags --abbrev=0 --match="v[0-9]*" 2>/dev/null); then
  echo "Last tag: $LAST_TAG"
  COMMITS=$(git log "${LAST_TAG}..HEAD" --format="%s%n%b")
else
  echo "No existing tag found. Inspecting all commits up to HEAD."
  COMMITS=$(git log HEAD --format="%s%n%b")
  LAST_TAG="v0.0.0"
fi

if echo "$COMMITS" | grep -qE "(BREAKING CHANGE|feat!|fix!)"; then
  BUMP_TYPE=major
elif echo "$COMMITS" | grep -qE "^feat(\([^)]+\))?:"; then
  BUMP_TYPE=minor
else
  BUMP_TYPE=patch
fi

VERSION="${LAST_TAG#v}"
BASE_VERSION="${VERSION%%-*}"
IFS=. read -r MAJOR MINOR PATCH <<< "$BASE_VERSION"

MAJOR=${MAJOR:-0}
MINOR=${MINOR:-0}
PATCH=${PATCH:-0}

case "$BUMP_TYPE" in
  major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
  minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
  patch) PATCH=$((PATCH + 1)) ;;
esac

VERSION="$MAJOR.$MINOR.$PATCH"
{
  echo "type=$BUMP_TYPE"
  echo "version=$VERSION"
  echo "tag=v$VERSION"
} >> "$OUTPUT_FILE"

echo "Bump type: $BUMP_TYPE"
echo "Next version: $VERSION"
