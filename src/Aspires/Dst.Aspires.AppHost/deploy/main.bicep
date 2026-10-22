// Entry point of every Azure deployment. Called by the release pipeline:
//   az deployment sub create --location <region> --parameters deploy/main.bicepparam
// The parameter file reads its values from environment variables, so the pipeline can override all of them.
//
// Deployment model
//   global resources : container registry (images, pushed once) + one Front Door profile (+WAF) -> modes full, provision_only
//   cell resources   : everything Aspire generates for one cell (infra/main.bicep) -> modes full, provision_only
//   cell apps        : Web API + Orleans silo container apps of one cell        -> modes full, deploy_only
//
// Images are registered once in the global registry and then enabled per cell (cell apps pull from it).
// provision_only exists because the registry must exist before images can be pushed: the release pipeline runs
// provision_only -> push images (once) -> deploy_only. 'full' does everything in one go and requires the images
// for appVersion to be in the registry already.
targetScope = 'subscription'

@description('full = provision resources + deploy apps; provision_only = resources only; deploy_only = only register the new app version.')
@allowed([
  'full'
  'provision_only'
  'deploy_only'
])
param mode string = 'deploy_only'

@description('Keys of the cells to process (must exist in the cell catalog below).')
param cellKeys array

@description('Version of the apps; also the image tag in the global registry.')
param appVersion string

@description('Object id of the identity running the deployment (granted rights by the Aspire-generated resources).')
param deployerPrincipalId string = ''

@description('Type of the deploying identity: ServicePrincipal (GitHub pipeline) or User (local run with az login).')
@allowed([
  'ServicePrincipal'
  'User'
])
param deployerPrincipalType string = 'ServicePrincipal'

@description('Create the Entra ID app registrations (API + Scalar SPA) with Bicep and use them for auth (forces JwtBearer). Needs Graph permission Application.ReadWrite.OwnedBy for the deployer. When false, the auth* params below are used as given.')
param provisionAuthApp bool = false

@description('Web API authentication mode: None (anonymous) or JwtBearer.')
@allowed([
  'None'
  'JwtBearer'
])
param authMode string = 'None'

@description('JwtBearer: token issuer, e.g. https://login.microsoftonline.com/<tenant-id>/v2.0')
param authAuthority string = ''

@description('JwtBearer: expected audience, e.g. api://<api-app-id>')
param authAudience string = ''

@description('Optional Scalar login: client id of the SPA app registration (empty = no login button).')
param authScalarClientId string = ''

@description('Optional Scalar login: scope requested, e.g. api://<api-app-id>/access_as_user')
param authScalarScope string = ''

@description('Serve the Scalar API docs.')
param docsEnabled string = 'true'

@description('Resource group of the global resources.')
param globalResourceGroupName string = 'dst-global-rg'

@description('Location of the global resource group (Front Door itself is global).')
param globalLocation string = 'westeurope'

@description('Front Door profile name.')
param frontDoorProfileName string = 'dst-afd'

@description('Registry SKU. Basic is the cheap default; Premium geo-replicates images to every cell region (production).')
@allowed([
  'Premium'
  'Basic'
])
param registrySku string = 'Basic'

@description('Front Door SKU. Standard is the cheap default; Premium adds managed WAF rules (production).')
@allowed([
  'Premium_AzureFrontDoor'
  'Standard_AzureFrontDoor'
])
param frontDoorSku string = 'Standard_AzureFrontDoor'

@description('Name of the global container registry (globally unique, 5-50 alphanumeric characters, so no separators).')
param registryName string = 'dst${uniqueString(subscription().id, globalResourceGroupName)}acr'

@description('Create an Azure Managed Grafana instance with the cell dashboard (opt-in: it has a monthly cost).')
param grafanaEnabled bool = false

@description('Managed Grafana tier.')
@allowed([
  'Standard'
  'Essential'
])
param grafanaSku string = 'Standard'

// Cell catalog: key -> cell id + Azure region. A cell lives in resource group dst-<cellId>-<region>-rg.
// Add further cells here, e.g.:
//   cell_2_eastus: { cellId: '2', region: 'eastus' }
//   cell_3_northeurope: { cellId: '3', region: 'northeurope' }
var cellCatalog = {
  cell_1_westus: {
    cellId: '1'
    region: 'westus'
  }
}

var cells = [for key in cellKeys: {
  key: key
  cellId: cellCatalog[key].cellId
  region: cellCatalog[key].region
  resourceGroupName: 'dst-${cellCatalog[key].cellId}-${cellCatalog[key].region}-rg'
}]

var provisionResources = mode == 'full' || mode == 'provision_only'
var deployApps = mode == 'full' || mode == 'deploy_only'

resource globalRg 'Microsoft.Resources/resourceGroups@2023-07-01' = if (provisionResources) {
  name: globalResourceGroupName
  location: globalLocation
}

// 1. Global registry first: cells reference it (AcrPull for their ACA identity).
module globalRegistry 'global_resources.bicep' = if (provisionResources) {
  name: 'global-registry'
  scope: globalRg
  params: {
    profileName: frontDoorProfileName
    registryName: registryName
    registrySku: registrySku
    replicaLocations: [for cell in cells: cell.region]
    pusherPrincipalId: deployerPrincipalId
    pusherPrincipalType: deployerPrincipalType
    includeFrontDoor: false
  }
}

// 2. Cell resources.
module cellResources 'cell_resources.bicep' = [for cell in cells: if (provisionResources) {
  name: 'cell-resources-${cell.key}'
  dependsOn: [
    globalRegistry
  ]
  params: {
    resourceGroupName: cell.resourceGroupName
    location: cell.region
    principalId: deployerPrincipalId
    registryName: registryName
    registryResourceGroup: globalResourceGroupName
  }
}]

// 3. Front Door: needs the host names of the cells.
module globalFrontDoor 'global_resources.bicep' = if (provisionResources) {
  name: 'global-front-door'
  scope: globalRg
  params: {
    profileName: frontDoorProfileName
    registryName: registryName
    registrySku: registrySku
    replicaLocations: [for cell in cells: cell.region]
    pusherPrincipalId: deployerPrincipalId
    pusherPrincipalType: deployerPrincipalType
    includeFrontDoor: true
    frontDoorSku: frontDoorSku
    origins: [for (cell, i) in cells: {
      name: cell.key
      hostName: cellResources[i]!.outputs.webApiHostName
    }]
  }
}

// 3b. Optional: Managed Grafana with the cell dashboard. Needs the cell resource groups (role assignments),
// so it runs after the cell resources; the import script runs after the roles exist.
module grafana 'grafana.bicep' = if (grafanaEnabled && provisionResources) {
  name: 'global-grafana'
  scope: globalRg
  params: {
    name: 'dst-${take(uniqueString(subscription().id, globalResourceGroupName), 8)}-amg'
    location: globalLocation
    sku: grafanaSku
    adminPrincipalId: deployerPrincipalId
    adminPrincipalType: deployerPrincipalType
  }
}

module grafanaAccess 'grafana_access.bicep' = [for (cell, i) in cells: if (grafanaEnabled && provisionResources) {
  name: 'grafana-access-${cell.key}'
  scope: resourceGroup(cell.resourceGroupName)
  dependsOn: [
    cellResources
  ]
  params: {
    grafanaPrincipalId: grafana!.outputs.grafanaPrincipalId
    scriptPrincipalId: grafana!.outputs.scriptIdentityPrincipalId
  }
}]

module grafanaDashboard 'grafana_dashboard.bicep' = if (grafanaEnabled && provisionResources) {
  name: 'global-grafana-dashboard'
  scope: globalRg
  dependsOn: [
    grafanaAccess
  ]
  params: {
    location: globalLocation
    grafanaName: grafana!.outputs.name
    scriptIdentityId: grafana!.outputs.scriptIdentityId
    cells: [for cell in cells: {
      cellId: cell.cellId
      resourceGroupName: cell.resourceGroupName
    }]
  }
}

// 4a. Optional: Entra app registrations (global), after Front Door because of the Scalar redirect URI.
module authApp 'auth_app.bicep' = if (provisionAuthApp && deployApps) {
  name: 'global-auth-app'
  scope: resourceGroup(globalResourceGroupName)
  dependsOn: [
    globalFrontDoor
  ]
  params: {
    frontDoorProfileName: frontDoorProfileName
  }
}

var useProvisionedAuth = provisionAuthApp && deployApps

// 4b. Enable the app version per cell.
module cellApps 'cell_apps.bicep' = [for cell in cells: if (deployApps) {
  name: 'cell-apps-${cell.key}'
  scope: resourceGroup(cell.resourceGroupName)
  dependsOn: [
    cellResources
    globalFrontDoor
  ]
  params: {
    cellId: cell.cellId
    appVersion: appVersion
    registryName: registryName
    globalResourceGroupName: globalResourceGroupName
    frontDoorProfileName: frontDoorProfileName
    authMode: provisionAuthApp ? 'JwtBearer' : authMode
    authAuthority: useProvisionedAuth ? authApp!.outputs.authority : authAuthority
    authAudience: useProvisionedAuth ? authApp!.outputs.audience : authAudience
    authScalarClientId: useProvisionedAuth ? authApp!.outputs.scalarClientId : authScalarClientId
    authScalarScope: useProvisionedAuth ? authApp!.outputs.scalarScope : authScalarScope
    docsEnabled: docsEnabled
  }
}]

output cellResourceGroups array = [for cell in cells: {
  key: cell.key
  resourceGroupName: cell.resourceGroupName
}]

output registryName string = registryName

output grafanaEndpoint string = grafanaEnabled && provisionResources ? grafana!.outputs.endpoint : ''
