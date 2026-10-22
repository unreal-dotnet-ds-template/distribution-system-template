using 'main.bicep'

// Every value can be overridden by the pipeline through environment variables.
param mode = readEnvironmentVariable('DEPLOY_MODE', 'deploy_only')
param cellKeys = json(readEnvironmentVariable('CELL_KEYS', '["cell_1_westus"]'))
param appVersion = readEnvironmentVariable('APP_VERSION', 'dev')
param deployerPrincipalId = readEnvironmentVariable('DEPLOYER_PRINCIPAL_ID', '')

// Web API auth (None = anonymous API) and docs. Set from GitHub environment variables by the release pipeline.
param provisionAuthApp = bool(readEnvironmentVariable('PROVISION_AUTH_APP', 'false'))
param authMode = readEnvironmentVariable('AUTH_MODE', 'None')
param authAuthority = readEnvironmentVariable('AUTH_AUTHORITY', '')
param authAudience = readEnvironmentVariable('AUTH_AUDIENCE', '')
param authScalarClientId = readEnvironmentVariable('AUTH_SCALAR_CLIENT_ID', '')
param authScalarScope = readEnvironmentVariable('AUTH_SCALAR_SCOPE', '')
param docsEnabled = readEnvironmentVariable('DOCS_ENABLED', 'true')
param registrySku = readEnvironmentVariable('REGISTRY_SKU', 'Basic')
param frontDoorSku = readEnvironmentVariable('FRONT_DOOR_SKU', 'Standard_AzureFrontDoor')
