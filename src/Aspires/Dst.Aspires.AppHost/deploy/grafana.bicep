// Optional Azure Managed Grafana (global, next to the registry and Front Door). Off by default.
// Holds the instance, the identity used by the dashboard import script and the Grafana roles.
// Reader rights on the cells are granted by grafana_access.bicep; the dashboard is imported by grafana_dashboard.bicep.
targetScope = 'resourceGroup'

param name string
param location string

@description('Standard is the generally available tier; Essential is cheaper but has fewer features.')
@allowed([
  'Standard'
  'Essential'
])
param sku string = 'Standard'

@description('Identity that may sign in to Grafana as admin (usually the person running the deployment). Empty = none.')
param adminPrincipalId string = ''

@allowed([
  'ServicePrincipal'
  'User'
])
param adminPrincipalType string = 'User'

var grafanaAdminRoleId = '22926164-76b3-42b3-bc55-97df8dab3e41'
var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'

resource grafana 'Microsoft.Dashboard/grafana@2023-09-01' = {
  name: name
  location: location
  sku: {
    name: sku
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
    apiKey: 'Disabled'
    deterministicOutboundIP: 'Disabled'
  }
}

// Used by the deployment script to import dashboards through the Grafana API.
resource scriptIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'dst-grafana-import-id'
  location: location
}

resource scriptIsAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafana.id, scriptIdentity.id, grafanaAdminRoleId)
  scope: grafana
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', grafanaAdminRoleId)
    principalId: scriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// The az CLI reads the instance through ARM (to find its endpoint) before calling the Grafana API.
resource scriptIsReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(grafana.id, scriptIdentity.id, readerRoleId)
  scope: grafana
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: scriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource adminIsAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(adminPrincipalId)) {
  name: guid(grafana.id, adminPrincipalId, grafanaAdminRoleId)
  scope: grafana
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', grafanaAdminRoleId)
    principalId: adminPrincipalId
    principalType: adminPrincipalType
  }
}

output name string = grafana.name
output endpoint string = grafana.properties.endpoint
output grafanaPrincipalId string = grafana.identity.principalId
output scriptIdentityId string = scriptIdentity.id
output scriptIdentityPrincipalId string = scriptIdentity.properties.principalId
