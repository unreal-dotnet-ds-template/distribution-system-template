#!/usr/bin/env bash
# Installs the Aspire CLI and adds it to the GitHub Actions runner PATH.
# Used by .github/workflows/official_build_pipeline.yml.
set -euo pipefail

if [ -z "${GITHUB_PATH:-}" ]; then
  echo "GITHUB_PATH must be set by the GitHub Actions runner." >&2
  exit 1
fi

curl -sSL https://aspire.dev/install.sh | bash
echo "$HOME/.aspire/bin" >> "$GITHUB_PATH"
