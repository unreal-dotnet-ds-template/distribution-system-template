// Imports the Azure dashboard into Managed Grafana. Dashboards are not ARM resources (they live inside Grafana),
// so a deployment script calls the Grafana API. Runs after the roles of grafana_access.bicep exist.
targetScope = 'resourceGroup'

param location string
param grafanaName string
param scriptIdentityId string

@description('[{ cellId, resourceGroupName }] of the cells shown in the dashboard.')
param cells array

@description('Changes on every deployment so the import runs again (dashboard changes are picked up).')
param runTag string = utcNow()

resource importDashboards 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'dst-grafana-import-ds'
  location: location
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${scriptIdentityId}': {}
    }
  }
  properties: {
    azCliVersion: '2.61.0'
    forceUpdateTag: runTag
    retentionInterval: 'PT1H'
    timeout: 'PT20M'
    cleanupPreference: 'OnSuccess'
    environmentVariables: [
      {
        name: 'GRAFANA_NAME'
        value: grafanaName
      }
      {
        name: 'GRAFANA_RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'CELL_RESOURCE_GROUPS'
        value: string(cells)
      }
      {
        name: 'DASHBOARD_JSON'
        value: loadTextContent('grafana/cell-overview.azure.json')
      }
    ]
    scriptContent: replace(loadTextContent('grafana/import-dashboards.sh'), '\r', '')
  }
}
