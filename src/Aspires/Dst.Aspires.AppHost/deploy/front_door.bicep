// Azure Front Door (Standard by default, Premium for managed WAF rules) in front of the Web API of every cell. Called by global_resources.bicep.
targetScope = 'resourceGroup'

param profileName string

@description('One origin per cell: { name, hostName }.')
param origins array

@description('WAF mode. Use Detection to tune managed rules before blocking.')
@allowed([
  'Prevention'
  'Detection'
])
param wafMode string = 'Prevention'

@description('Premium adds the managed WAF rule sets (OWASP, bot protection) and Private Link origins; Standard is ~10x cheaper.')
@allowed([
  'Premium_AzureFrontDoor'
  'Standard_AzureFrontDoor'
])
param sku string = 'Standard_AzureFrontDoor'

var isPremium = sku == 'Premium_AzureFrontDoor'
resource profile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: profileName
  location: 'global'
  sku: {
    name: sku
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: profile
  name: 'dst-api-ep'
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'dst-web-api-og'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    healthProbeSettings: {
      probePath: '/health'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 30
    }
    sessionAffinityState: 'Disabled'
  }
}

resource originEntries 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = [for origin in origins: {
  parent: originGroup
  // Front Door origin names allow only letters, digits and hyphens (cell keys contain underscores)
  name: '${replace(origin.name, '_', '-')}-origin'
  properties: {
    hostName: origin.hostName
    originHostHeader: origin.hostName
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}]

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'dst-default-route'
  dependsOn: [
    originEntries
  ]
  properties: {
    originGroup: {
      id: originGroup.id
    }
    supportedProtocols: [
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
  }
}

resource waf 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: 'dstwaf'
  location: 'global'
  sku: {
    name: sku
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: wafMode
    }
    managedRules: isPremium ? {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
          ruleSetAction: 'Block'
        }
        {
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.1'
        }
      ]
    } : {
      // Standard SKU has no managed rule sets, but the property itself is required.
      managedRuleSets: []
    }
  }
}

resource security 'Microsoft.Cdn/profiles/securityPolicies@2024-02-01' = {
  parent: profile
  name: 'dst-waf-sp'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: waf.id
      }
      associations: [
        {
          domains: [
            {
              id: endpoint.id
            }
          ]
          patternsToMatch: [
            '/*'
          ]
        }
      ]
    }
  }
}

output frontDoorId string = profile.properties.frontDoorId

output endpointHostName string = endpoint.properties.hostName
