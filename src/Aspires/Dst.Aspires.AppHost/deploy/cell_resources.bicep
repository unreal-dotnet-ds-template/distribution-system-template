// Cell resources: wraps the Aspire-generated infra/main.bicep (resource group, VNet, ACA environment,
// storage + private endpoints, Log Analytics, Application Insights, identities, role assignments).
// The container registry is not part of a cell: images come from the global registry (global_resources.bicep).
// Nothing is hand-written here, so the topology always follows the AppHost.
targetScope = 'subscription'

param resourceGroupName string
param location string
param principalId string

@description('Global registry the cell pulls images from.')
param registryName string
param registryResourceGroup string

module cell '../infra/main.bicep' = {
  name: 'aspire-${resourceGroupName}'
  params: {
    resourceGroupName: resourceGroupName
    location: location
    principalId: principalId
    registryName: registryName
    registryResourceGroup: registryResourceGroup
  }
}

// Public host name of the Web API, used as the Front Door origin.
output webApiHostName string = 'dst-web-api.${cell.outputs.aca_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN}'
