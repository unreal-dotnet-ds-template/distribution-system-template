#!/usr/bin/env bash
# Runs the shared restore, build, test, and Redis-image-pull commands.
# Used by .github/workflows/official_build_pipeline.yml and pr_build_pipeline.yml.
set -euo pipefail

COMMAND="${1:-}"
CONFIGURATION="${2:-Release}"

case "$COMMAND" in
  restore)
    dotnet restore
    ;;
  build)
    dotnet build --no-restore --configuration "$CONFIGURATION"
    ;;
  test)
    RESULTS_DIRECTORY="${3:-./test-results}"
    dotnet test --no-build --configuration "$CONFIGURATION" \
      --report-xunit-trx \
      --results-directory "$RESULTS_DIRECTORY"
    ;;
  pull-redis)
    docker pull "${2:-redis:latest}"
    ;;
  *)
    echo "Usage: bash scripts/dotnet-ci.sh {restore|build|test|pull-redis} [configuration-or-image] [results-directory]" >&2
    exit 1
    ;;
esac
