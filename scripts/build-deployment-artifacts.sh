#!/usr/bin/env bash
# Builds container image archives and Aspire-generated deployment artifacts.
# Used by .github/workflows/official_build_pipeline.yml.
set -euo pipefail

VERSION="${1:-}"
OUTPUT_DIRECTORY="${2:-./deploy-artifacts}"
CONFIGURATION="${3:-Release}"

if [ -z "$VERSION" ]; then
  echo "Usage: bash scripts/build-deployment-artifacts.sh <version> [output-directory] [configuration]" >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIRECTORY/images"

dotnet publish src/Dst.WebApiApp/Dst.WebApiApp.csproj --configuration "$CONFIGURATION" \
  -t:PublishContainer -r linux-x64 --self-contained false \
  -p:ContainerRepository=dst-web-api -p:ContainerImageTags="$VERSION" \
  -p:ContainerArchiveOutputPath="$PWD/$OUTPUT_DIRECTORY/images/dst-web-api.tar.gz"
dotnet publish src/OrleansSilo/Dst.OrleansSilo.WebApp/Dst.OrleansSilo.WebApp.csproj --configuration "$CONFIGURATION" \
  -t:PublishContainer -r linux-x64 --self-contained false \
  -p:ContainerRepository=dst-web-orleans-silo -p:ContainerImageTags="$VERSION" \
  -p:ContainerArchiveOutputPath="$PWD/$OUTPUT_DIRECTORY/images/dst-web-orleans-silo.tar.gz"

aspire publish --apphost src/Aspires/Dst.Aspires.AppHost/Dst.Aspires.AppHost.csproj \
  -o "$OUTPUT_DIRECTORY/infra" -c "$CONFIGURATION"
cp -r src/Aspires/Dst.Aspires.AppHost/deploy "$OUTPUT_DIRECTORY/deploy"
echo "$VERSION" > "$OUTPUT_DIRECTORY/version.txt"
