// Global resources:
//  - one container registry holding the images of all cells (pushed once, pulled by every cell),
//  - Azure Front Door (see front_door.bicep) in front of the Web API of every cell.
// Deployed twice by main.bicep: first registry only (cells need it), then again with Front Door (needs cell host names).
targetScope = 'resourceGroup'

param profileName string

@description('Globally unique registry name (alphanumeric).')
param registryName string

@description('Premium geo-replicates the registry to every cell region (local pulls, regional resilience); Basic is the cheap option.')
@allowed([
  'Premium'
  'Basic'
])
param registrySku string = 'Basic'

@description('Regions of the cells; the registry is replicated to each of them (Premium only).')
param replicaLocations array = []

@description('Identity allowed to push images (the release pipeline).')
param pusherPrincipalId string = ''

@description('Type of the pusher: ServicePrincipal (pipeline) or User (running the deployment from your machine).')
@allowed([
  'ServicePrincipal'
  'User'
])
param pusherPrincipalType string = 'ServicePrincipal'

@description('false = registry only; true = also Front Door.')
param includeFrontDoor bool = false

@description('One origin per cell: { name, hostName }. Used when includeFrontDoor is true.')
param origins array = []

@description('WAF mode.')
param wafMode string = 'Prevention'

@description('Front Door SKU.')
param frontDoorSku string = 'Standard_AzureFrontDoor'

resource registry 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: registryName
  location: resourceGroup().location
  sku: {
    name: registrySku
  }
  properties: {
    adminUserEnabled: false
  }
}

// The home region is already served by the registry itself.
resource replicas 'Microsoft.ContainerRegistry/registries/replications@2025-04-01' = [for location in replicaLocations: if (registrySku == 'Premium' && location != resourceGroup().location) {
  parent: registry
  name: location
  location: location
}]

var acrPushRole = '8311e382-0749-4cb8-b61a-304f252e45ec'

resource pusherRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(pusherPrincipalId)) {
  name: guid(registry.id, pusherPrincipalId, acrPushRole)
  scope: registry
  properties: {
    principalId: pusherPrincipalId
    principalType: pusherPrincipalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPushRole)
  }
}

module frontDoor 'front_door.bicep' = if (includeFrontDoor) {
  name: 'front-door'
  params: {
    profileName: profileName
    origins: origins
    wafMode: wafMode
    sku: frontDoorSku
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output frontDoorId string = includeFrontDoor ? frontDoor!.outputs.frontDoorId : ''
output frontDoorEndpointHostName string = includeFrontDoor ? frontDoor!.outputs.endpointHostName : ''
