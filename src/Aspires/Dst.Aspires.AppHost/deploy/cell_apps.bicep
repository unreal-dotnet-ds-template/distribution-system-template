// Cell apps: Web API + Orleans silo of one cell, deployed into the cell's resource group (created by cell_resources.bicep).
// Everything else (ACA environment, storage, identities, Application Insights) comes from the
// Aspire-generated ../infra/main.bicep. Resource names below mirror Aspire's deterministic naming
// (<name>-uniqueString(resourceGroup().id)); if the AppHost resource names change, update them here.
targetScope = 'resourceGroup'

@description('Cell id, e.g. "1". The resource group is dst-<cellId>-<region>.')
param cellId string

@description('App version: reported as BUILD_NUMBER and used as the image tag in the cell registry.')
param appVersion string

@description('Resource group of the global Front Door profile.')
param globalResourceGroupName string

param frontDoorProfileName string = 'dst-frontdoor'

@description('Global registry holding the images.')
param authMode string = 'None'
param authAuthority string = ''
param authAudience string = ''
param authScalarClientId string = ''
param authScalarScope string = ''
param docsEnabled string = 'true'

param registryName string


@description('Orleans cluster id. One cell = one cluster.')
param orleansClusterId string = 'dst-${cellId}'

@description('Orleans service id.')
param orleansServiceId string = 'dst'

@description('Container port exposed by the Web API image.')
param webApiPort string = '8080'

@description('Container port exposed by the silo image.')
param siloPort string = '8080'

var suffix = uniqueString(resourceGroup().id)

// Front Door lives in the global resource group; its id restricts the Web API to Front Door traffic.
resource frontDoor 'Microsoft.Cdn/profiles@2024-02-01' existing = {
  name: frontDoorProfileName
  scope: resourceGroup(globalResourceGroupName)
}

var webApiImage = '${acr.properties.loginServer}/dst-web-api:${appVersion}'
var siloImage = '${acr.properties.loginServer}/dst-web-orleans-silo:${appVersion}'

resource aca 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: take('aca${suffix}', 24)
}

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: registryName
  scope: resourceGroup(globalResourceGroupName)
}

resource acrIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: take('aca_mi-${suffix}', 128)
}

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: take('dststorage${suffix}', 24)
}

resource logs 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: take('Dstlogs-${suffix}', 63)
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: take('Dst_web_api_identity-${suffix}', 128)
}

resource siloIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: take('Dst_web_orleans_silo_identity-${suffix}', 128)
}

// Same definition as the Aspire-generated module, plus Entra-only ingestion (no instrumentation keys).
resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: take('Dst_insights-${suffix}', 260)
  kind: 'web'
  location: resourceGroup().location
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: true
  }
  tags: {
    'aspire-resource-name': 'Dst-insights'
  }
}

var monitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb'

resource apiTelemetryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(insights.id, apiIdentity.id, monitoringMetricsPublisher)
  scope: insights
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisher)
  }
}

resource siloTelemetryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(insights.id, siloIdentity.id, monitoringMetricsPublisher)
  scope: insights
  properties: {
    principalId: siloIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisher)
  }
}

module silo '../infra/Dst-web-orleans-silo/Dst-web-orleans-silo.bicep' = {
  name: 'Dst-web-orleans-silo'
  dependsOn: [
    siloTelemetryRole
  ]
  params: {
    aca_outputs_azure_container_apps_environment_default_domain: aca.properties.defaultDomain
    aca_outputs_azure_container_apps_environment_id: aca.id
    aca_outputs_azure_container_registry_endpoint: acr.properties.loginServer
    aca_outputs_azure_container_registry_managed_identity_id: acrIdentity.id
    dst_web_orleans_silo_containerimage: siloImage
    dst_web_orleans_silo_containerport: siloPort
    dst_web_orleans_silo_identity_outputs_id: siloIdentity.id
    dst_web_orleans_silo_identity_outputs_clientid: siloIdentity.properties.clientId
    dst_storage_outputs_tableendpoint: storage.properties.primaryEndpoints.table
    dst_storage_outputs_blobendpoint: storage.properties.primaryEndpoints.blob
    dst_insights_outputs_appinsightsconnectionstring: insights.properties.ConnectionString
    dst_orleanscluster_cluster_id_value: orleansClusterId
    dst_orleanscluster_service_id_value: orleansServiceId
    cellid_value: cellId
    buildnumber_value: appVersion
  }
}

module webApi '../infra/Dst-web-api/Dst-web-api.bicep' = {
  name: 'Dst-web-api'
  dependsOn: [
    apiTelemetryRole
    silo
  ]
  params: {
    aca_outputs_azure_container_apps_environment_default_domain: aca.properties.defaultDomain
    aca_outputs_azure_container_apps_environment_id: aca.id
    aca_outputs_azure_container_registry_endpoint: acr.properties.loginServer
    aca_outputs_azure_container_registry_managed_identity_id: acrIdentity.id
    dst_web_api_containerimage: webApiImage
    dst_web_api_containerport: webApiPort
    dst_web_api_identity_outputs_id: apiIdentity.id
    dst_web_api_identity_outputs_clientid: apiIdentity.properties.clientId
    frontdoorid_value: frontDoor.properties.frontDoorId
    authmode_value: authMode
    authauthority_value: authAuthority
    authaudience_value: authAudience
    authscalarclientid_value: authScalarClientId
    authscalarscope_value: authScalarScope
    docsenabled_value: docsEnabled
    dst_storage_outputs_tableendpoint: storage.properties.primaryEndpoints.table
    dst_insights_outputs_appinsightsconnectionstring: insights.properties.ConnectionString
    dst_orleanscluster_cluster_id_value: orleansClusterId
    dst_orleanscluster_service_id_value: orleansServiceId
    cellid_value: cellId
    buildnumber_value: appVersion
  }
}

output webApiFqdn string = 'dst-web-api.${aca.properties.defaultDomain}'
