#!/bin/bash
# Runs inside the Bicep deploymentScript (Azure CLI image) of grafana_dashboard.bicep.
# Inputs (environment): GRAFANA_NAME, GRAFANA_RESOURCE_GROUP, CELL_RESOURCE_GROUPS (JSON array of
# {cellId, resourceGroupName}), DASHBOARD_JSON (template with __CELL_OPTIONS__ and __DS_UID__).
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
done < <(echo "$CELL_RESOURCE_GROUPS" | python3 -c "import json,sys; [print(c['cellId'], c['resourceGroupName']) for c in json.load(sys.stdin)]")

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
python3 -c "import json; json.load(open('/tmp/dashboard.json'))"

az grafana dashboard create -n "$GRAFANA_NAME" -g "$GRAFANA_RESOURCE_GROUP" \
  --definition /tmp/dashboard.json --overwrite true --only-show-errors -o none
echo "Dashboard imported."
