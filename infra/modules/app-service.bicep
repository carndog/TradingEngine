@description('Name of the App Service Plan.')
param appServicePlanName string

@description('Globally unique name of the Web App.')
param webAppName string

@description('Azure region for the resources.')
param location string

@description('App Service Plan SKU name.')
param appServiceSkuName string

@description('Expiry of the subscription-assigned temporary App Service Plan free offer, preserved so deployments do not remove it.')
param appServicePlanFreeOfferExpirationTime string

@description('Passwordless Azure SQL connection string exposed as the ConnectionStrings__TradingEngine app setting. Empty means no database is configured.')
param tradingEngineConnectionString string = ''

@description('Shared key required in the X-Database-Probe-Key header for the /health/database endpoint, exposed as the Diagnostics__DatabaseProbeKey app setting. Empty disables the endpoint.')
@secure()
param databaseProbeKey string = ''

@description('Enable App Service Easy Auth with Microsoft Entra ID. When true, entraAuthClientId and entraAuthAllowedPrincipalIds are required.')
param configureEntraAuth bool = false

@description('Application (client) ID of the single-tenant Entra app registration backing Easy Auth.')
param entraAuthClientId string = ''

@description('Object IDs of the Entra principals allowed through Easy Auth (the owner allowlist). A successful tenant sign-in alone does not grant access.')
param entraAuthAllowedPrincipalIds array = []

@description('Name of the dedicated user-assigned managed identity used as the Easy Auth federated credential. Created only when configureEntraAuth is true; it must not be assigned to any other resource.')
param easyAuthIdentityName string = ''

@description('Tags applied to the resources.')
param tags object

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSkuName
  }
  properties: {
    reserved: true
    freeOfferExpirationTime: appServicePlanFreeOfferExpirationTime
  }
}

resource easyAuthIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (configureEntraAuth) {
  name: easyAuthIdentityName
  location: location
  tags: tags
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: configureEntraAuth
    ? {
        type: 'SystemAssigned, UserAssigned'
        userAssignedIdentities: {
          '${easyAuthIdentity.id}': {}
        }
      }
    : {
        type: 'SystemAssigned'
      }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      alwaysOn: true
    }
  }
}

resource webAppAppSettings 'Microsoft.Web/sites/config@2023-12-01' = if (tradingEngineConnectionString != '' || configureEntraAuth) {
  parent: webApp
  name: 'appsettings'
  properties: union(
    tradingEngineConnectionString != ''
      ? {
          ConnectionStrings__TradingEngine: tradingEngineConnectionString
          Diagnostics__DatabaseProbeKey: databaseProbeKey
        }
      : {},
    configureEntraAuth
      ? {
          OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID: easyAuthIdentity.?properties.clientId ?? ''
        }
      : {}
  )
}

resource webAppSlotConfigNames 'Microsoft.Web/sites/config@2023-12-01' = if (configureEntraAuth) {
  parent: webApp
  name: 'slotConfigNames'
  properties: {
    appSettingNames: [
      'OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID'
    ]
  }
}

resource webAppAuth 'Microsoft.Web/sites/config@2023-12-01' = if (configureEntraAuth) {
  parent: webApp
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: true
    }
    globalValidation: {
      requireAuthentication: true
      unauthenticatedClientAction: 'Return401'
      redirectToProvider: 'azureactivedirectory'
      excludedPaths: [
        '/health'
        '/health/database'
        '/version'
      ]
    }
    httpSettings: {
      requireHttps: true
      forwardProxy: {
        convention: 'NoProxy'
      }
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: entraAuthClientId
          clientSecretSettingName: 'OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID'
          openIdIssuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
        }
        validation: {
          allowedAudiences: [
            'api://${entraAuthClientId}'
            entraAuthClientId
          ]
          defaultAuthorizationPolicy: {
            allowedPrincipals: {
              identities: entraAuthAllowedPrincipalIds
            }
          }
        }
      }
    }
    login: {
      tokenStore: {
        enabled: true
      }
    }
  }
}

resource ftpPublishingPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: webApp
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmPublishingPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: webApp
  name: 'scm'
  properties: {
    allow: false
  }
}

output webAppName string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
output easyAuthIdentityName string = easyAuthIdentity.?name ?? ''
output easyAuthIdentityPrincipalId string = easyAuthIdentity.?properties.principalId ?? ''
