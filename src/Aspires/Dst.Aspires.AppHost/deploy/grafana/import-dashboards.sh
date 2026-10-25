#!/bin/bash
# ==============================================================================
# import-dashboards.sh
#
# WHAT IT DOES:
#   Configures and imports the Cell Overview dashboard into an Azure Managed
#   Grafana instance. It resolves each cell's Log Analytics workspace ID, updates
#   dashboard template variables (__DS_UID__ and __CELL_OPTIONS__), validates
#   the resulting JSON, and pushes it via the Azure CLI Grafana extension.
#
# WHERE IT IS USED:
#   Referenced by: src/Aspires/Dst.Aspires.AppHost/deploy/grafana_dashboard.bicep
#   Execution context: Runs inside an Azure deployment script container
#   (kind: 'AzureCLI', mcr.microsoft.com/azure-cli) during Bicep deployment.
#
# DEPENDENCIES:
#   - bash
#   - Azure CLI (az) with 'amg' (Azure Managed Grafana) extension
#   - jq (for JSON parsing and validation; pre-installed in Azure CLI container)
#   - sed
#
# ENVIRONMENT VARIABLES REQUIRED:
#   - GRAFANA_NAME: Name of the Azure Managed Grafana instance
#   - GRAFANA_RESOURCE_GROUP: Resource group of the Grafana instance
#   - CELL_RESOURCE_GROUPS: JSON array string of [{cellId, resourceGroupName}]
#   - DASHBOARD_JSON: Raw dashboard JSON template with __CELL_OPTIONS__ and __DS_UID__
# ==============================================================================
set -euo pipefail

az extension add --name amg --only-show-errors

# Each cell has its own Log Analytics workspace; the dashboard's "Cell" variable switches between them.
cell_options=""
while read -r cell_id rg; do
  workspace_id=$(az resource list -g "$rg" --resource-type Microsoft.OperationalInsights/workspaces --query "[0].id" -o tsv)
  if [ -z "$workspace_id" ]; then
    echo "No Log Analytics workspace in $rg, skipping cell $cell_id"
    continue
  fi
  cell_options+="${cell_options:+,}$cell_id : $workspace_id"
done < <(echo "$CELL_RESOURCE_GROUPS" | jq -r '.[] | "\(.cellId) \(.resourceGroupName)"')

if [ -z "$cell_options" ]; then
  echo "No cell workspace found, nothing to import."
  exit 1
fi

# The built-in Azure Monitor data source of Managed Grafana (uses the instance's managed identity).
ds_uid=$(az grafana data-source list -n "$GRAFANA_NAME" -g "$GRAFANA_RESOURCE_GROUP" \
  --query "[?type=='grafana-azure-monitor-datasource'] | [0].uid" -o tsv)
if [ -z "$ds_uid" ]; then
  echo "Azure Monitor data source not found in $GRAFANA_NAME"
  exit 1
fi

printf '%s' "$DASHBOARD_JSON" | sed -e "s|__DS_UID__|$ds_uid|g" -e "s|__CELL_OPTIONS__|$cell_options|g" > /tmp/dashboard.json

# Validate dashboard JSON syntax using jq
jq empty /tmp/dashboard.json

az grafana dashboard create -n "$GRAFANA_NAME" -g "$GRAFANA_RESOURCE_GROUP" \
  --definition /tmp/dashboard.json --overwrite true --only-show-errors -o none
echo "Dashboard imported."
