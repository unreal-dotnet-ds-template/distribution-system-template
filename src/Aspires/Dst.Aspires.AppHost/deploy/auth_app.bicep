// Entra ID app registrations for the Web API (global: tokens come from the tenant, all cells share them):
//   - "API" app: exposes the access_as_user scope, the audience of the access tokens
//   - "Scalar" SPA app: public client (authorization code + PKCE, no secret) behind the login button in Scalar
// Needs the deployer to have the Microsoft Graph application permission Application.ReadWrite.OwnedBy (admin consent).
// Re-running is idempotent: apps are matched by uniqueName.
targetScope = 'resourceGroup'
extension microsoftGraphV1

@description('Front Door profile and its endpoint (<profileName>-api) already deployed in this resource group.')
param frontDoorProfileName string

@description('Prefix of the app registration names/unique names.')
param namePrefix string = 'dst'

// Stable ids so that the scope can be referenced by the SPA app.
var scopeId = guid(tenant().tenantId, namePrefix, 'access_as_user')
var audience = 'api://${tenant().tenantId}/${namePrefix}-api'

resource frontDoor 'Microsoft.Cdn/profiles@2024-02-01' existing = {
  name: frontDoorProfileName

  resource endpoint 'afdEndpoints' existing = {
    name: '${frontDoorProfileName}-api'
  }
}

resource apiApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: '${namePrefix}-api'
  displayName: '${namePrefix}-api'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    audience
  ]
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: scopeId
        value: 'access_as_user'
        type: 'User'
        isEnabled: true
        adminConsentDisplayName: 'Access the DST API'
        adminConsentDescription: 'Allows the app to call the DST API on behalf of the signed-in user.'
        userConsentDisplayName: 'Access the DST API'
        userConsentDescription: 'Allows the app to call the DST API on your behalf.'
      }
    ]
  }
}

resource scalarApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: '${namePrefix}-scalar'
  displayName: '${namePrefix}-scalar'
  signInAudience: 'AzureADMyOrg'
  spa: {
    redirectUris: [
      'https://${frontDoor::endpoint.properties.hostName}/scalar/'
      'https://${frontDoor::endpoint.properties.hostName}/scalar'
    ]
  }
  requiredResourceAccess: [
    {
      resourceAppId: apiApp.appId
      resourceAccess: [
        {
          id: scopeId
          type: 'Scope'
        }
      ]
    }
  ]
}

output authority string = '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
output audience string = audience
output scalarClientId string = scalarApp.appId
output scalarScope string = '${audience}/access_as_user'
