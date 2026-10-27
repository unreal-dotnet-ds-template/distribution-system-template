#!/usr/bin/env bash
# Runs Azure deployment preparation, provisioning, image pushes, registration, and summary steps.
# Used by the deploy job in .github/workflows/official_release_pipeline.yml.
set -euo pipefail

COMMAND="${1:-}"

case "$COMMAND" in
  prepare)
    CELL_KEYS_INPUT="${2:-}"
    CELL_KEYS=$(echo "$CELL_KEYS_INPUT" | tr -d ' ' | jq -R -c 'split(",") | map(select(length > 0))')
    echo "CELL_KEYS=$CELL_KEYS" >> "${GITHUB_ENV:?GITHUB_ENV must be set}"

    TOKEN=$(az account get-access-token --query accessToken -o tsv)
    PAYLOAD=$(echo "$TOKEN" | cut -d'.' -f2 | tr '_-' '/+')
    PAD=$(( (4 - ${#PAYLOAD} % 4) % 4 ))
    [ "$PAD" -eq 1 ] && PAYLOAD="${PAYLOAD}="
    [ "$PAD" -eq 2 ] && PAYLOAD="${PAYLOAD}=="
    [ "$PAD" -eq 3 ] && PAYLOAD="${PAYLOAD}==="
    OID=$(echo "$PAYLOAD" | base64 -d | jq -r '.oid')
    if [ -z "$OID" ] || [ "$OID" = "null" ]; then
      echo "Could not read the deployer's object ID from the Azure token." >&2
      exit 1
    fi
    echo "::add-mask::$OID"
    echo "DEPLOYER_PRINCIPAL_ID=$OID" >> "$GITHUB_ENV"
    ;;
  provision)
    APP_VERSION="${2:?Usage: bash scripts/deploy.sh provision <app-version> <location> <parameters-file>}"
    LOCATION="${3:?Usage: bash scripts/deploy.sh provision <app-version> <location> <parameters-file>}"
    PARAMETERS_FILE="${4:?Usage: bash scripts/deploy.sh provision <app-version> <location> <parameters-file>}"
    az deployment sub create \
      --name "dst-provision-${APP_VERSION}" \
      --location "$LOCATION" \
      --parameters "$PARAMETERS_FILE" \
      --output table
    ;;
  wait-rbac)
    echo "Waiting 45 seconds for Azure RBAC role assignments to propagate to ACR..."
    sleep 45
    ;;
  push-images)
    APP_VERSION="${2:?Usage: bash scripts/deploy.sh push-images <app-version> [artifact-directory]}"
    ARTIFACT_DIRECTORY="${3:-./deploy-artifacts}"
    ACR=$(az acr list -g dst-global-rg --query "[0].name" -o tsv)
    if [ -z "$ACR" ]; then
      echo "No global registry found. Run with deploy_mode=full first." >&2
      exit 1
    fi
    SERVER=$(az acr show -n "$ACR" --query loginServer -o tsv)

    echo "::group::Loading container images"
    docker load -i "$ARTIFACT_DIRECTORY/images/dst-web-api.tar.gz"
    docker load -i "$ARTIFACT_DIRECTORY/images/dst-web-orleans-silo.tar.gz"
    echo "::endgroup::"

    for IMAGE in dst-web-api dst-web-orleans-silo; do
      if az acr repository show --name "$ACR" --image "$IMAGE:$APP_VERSION" -o none 2>/dev/null; then
        echo "ℹ️ $IMAGE:$APP_VERSION is already registered, skipping."
        continue
      fi

      docker tag "$IMAGE:$APP_VERSION" "$SERVER/$IMAGE:$APP_VERSION"
      PUSHED=false
      echo "::group::Pushing $IMAGE:$APP_VERSION to $SERVER"
      for attempt in 1 2 3 4 5 6; do
        echo "Attempt $attempt of 6: logging in to ACR and pushing..."
        az acr login --name "$ACR" --output none 2>/dev/null || true
        if docker push "$SERVER/$IMAGE:$APP_VERSION"; then
          PUSHED=true
          echo "✅ Successfully pushed $IMAGE:$APP_VERSION"
          break
        fi
        echo "⚠️ Push failed (attempt $attempt). Waiting 15s before retrying..."
        sleep 15
      done
      echo "::endgroup::"

      if [ "$PUSHED" != "true" ]; then
        echo "❌ Error: Failed to push $IMAGE:$APP_VERSION to $SERVER after 6 attempts." >&2
        exit 1
      fi
    done
    ;;
  register)
    APP_VERSION="${2:?Usage: bash scripts/deploy.sh register <app-version> <location> <parameters-file>}"
    LOCATION="${3:?Usage: bash scripts/deploy.sh register <app-version> <location> <parameters-file>}"
    PARAMETERS_FILE="${4:?Usage: bash scripts/deploy.sh register <app-version> <location> <parameters-file>}"
    az deployment sub create \
      --name "dst-apps-${APP_VERSION}" \
      --location "$LOCATION" \
      --parameters "$PARAMETERS_FILE" \
      --output table
    ;;
  summary)
    APP_VERSION="${2:-}"
    DEPLOY_MODE="${3:-}"
    CELL_KEYS="${4:-}"
    SUMMARY_FILE="${GITHUB_STEP_SUMMARY:?GITHUB_STEP_SUMMARY must be set}"
    {
      echo "### 🚀 Azure Deployment Summary"
      echo "| | |"
      echo "|---|---|"
      echo "| **App Version** | \`$APP_VERSION\` |"
      echo "| **Deployment Mode** | \`$DEPLOY_MODE\` |"
      echo "| **Cells** | \`$CELL_KEYS\` |"
    } >> "$SUMMARY_FILE"

    FD_HOST=$(az afd endpoint list -g dst-global-rg --profile-name dst-afd --query "[0].hostName" -o tsv 2>/dev/null || true)
    if [ -n "$FD_HOST" ]; then
      {
        echo "| **Front Door URL** | [https://${FD_HOST}](https://${FD_HOST}) |"
        echo "| **Health Endpoint** | [https://${FD_HOST}/health](https://${FD_HOST}/health) |"
      } >> "$SUMMARY_FILE"
    fi

    GRAFANA_URL=$(az grafana list -g dst-global-rg --query "[0].properties.endpoint" -o tsv 2>/dev/null || true)
    if [ -n "$GRAFANA_URL" ]; then
      echo "| **Grafana Dashboard** | [${GRAFANA_URL}](${GRAFANA_URL}) |" >> "$SUMMARY_FILE"
    fi
    ;;
  *)
    echo "Usage: bash scripts/deploy.sh {prepare|provision|wait-rbac|push-images|register|summary} ..." >&2
    exit 1
    ;;
esac
