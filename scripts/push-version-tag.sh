#!/usr/bin/env bash
# Configures the GitHub Actions identity and pushes the computed version tag.
# Used by .github/workflows/official_build_pipeline.yml.
set -euo pipefail

TAG="${1:-}"
if [ -z "$TAG" ]; then
  echo "Usage: bash scripts/push-version-tag.sh <tag>" >&2
  exit 1
fi

git config user.name "github-actions[bot]"
git config user.email "github-actions[bot]@users.noreply.github.com"
git tag "$TAG"
git push origin "$TAG"
