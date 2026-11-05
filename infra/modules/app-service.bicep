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

@description('Application Insights connection string resolved from the monitoring resources during deployment, exposed as the APPLICATIONINSIGHTS_CONNECTION_STRING app setting. Empty disables Azure Monitor telemetry export.')
@secure()
param applicationInsightsConnectionString string = ''

@description('Enable the timer-triggered price collection hosted service, exposed as the PriceCollection__Enabled app setting.')
param priceCollectionEnabled bool = false

@description('eToro market-data API key, exposed as the EToro__MarketData__ApiKey app setting. Supplied at deploy time from environment secrets; empty omits the setting. Never commit a live value.')
@secure()
param etoroMarketDataApiKey string = ''

@description('eToro market-data user key, exposed as the EToro__MarketData__UserKey app setting. Supplied at deploy time from environment secrets; empty omits the setting. Never commit a live value.')
@secure()
param etoroMarketDataUserKey string = ''

@description('Map of instrument exchange identifier to the provider exchange names accepted for it, flattened into indexed EToro__MarketData__ExchangeMap__* app settings. Supplied as a JSON object of string arrays at deploy time; an empty object emits no settings.')
param marketDataExchangeMap object = {}

@description('Map of instrument exchange identifier and symbol to the provider native currency, flattened into EToro__MarketData__QuoteCurrencyMap__{exchange}__{symbol} app settings. Supplied as a JSON object of objects at deploy time; an empty object emits no settings.')
param marketDataQuoteCurrencyMap object = {}

@description('Exchange session definitions keyed by exchange identifier, flattened into PriceCollection__ExchangeSessions__* app settings. Each value supports timeZone, alwaysOpen and windows[{days[], open, close}]. Supplied as a JSON object at deploy time; an empty object emits no settings.')
param priceCollectionExchangeSessions object = {}

@description('Tags applied to the resources.')
param tags object

var exchangeMapSettings = reduce(
  items(marketDataExchangeMap),
  {},
  (accumulated, exchange) => union(
    accumulated,
    reduce(
      range(0, length(exchange.value)),
      {},
      (indexAccumulated, valueIndex) => union(
        indexAccumulated,
        {
          'EToro__MarketData__ExchangeMap__${exchange.key}__${valueIndex}': string(exchange.value[valueIndex])
        }))))

var quoteCurrencyMapSettings = reduce(
  items(marketDataQuoteCurrencyMap),
  {},
  (accumulated, exchange) => union(
    accumulated,
    reduce(
      items(exchange.value),
      {},
      (symbolAccumulated, symbol) => union(
        symbolAccumulated,
        {
          'EToro__MarketData__QuoteCurrencyMap__${exchange.key}__${symbol.key}': string(symbol.value)
        }))))

var exchangeSessionSettings = reduce(
  items(priceCollectionExchangeSessions),
  {},
  (accumulated, exchange) => union(
    accumulated,
    {
      'PriceCollection__ExchangeSessions__${exchange.key}__TimeZone': exchange.value.?timeZone ?? ''
      'PriceCollection__ExchangeSessions__${exchange.key}__AlwaysOpen': string(exchange.value.?alwaysOpen ?? false)
    },
    reduce(
      range(0, length(exchange.value.?windows ?? [])),
      {},
      (windowAccumulated, windowIndex) => union(
        windowAccumulated,
        {
          'PriceCollection__ExchangeSessions__${exchange.key}__Windows__${windowIndex}__Open': exchange.value.windows[windowIndex].?open ?? ''
          'PriceCollection__ExchangeSessions__${exchange.key}__Windows__${windowIndex}__Close': exchange.value.windows[windowIndex].?close ?? ''
        },
        reduce(
          range(0, length(exchange.value.windows[windowIndex].?days ?? [])),
          {},
          (dayAccumulated, dayIndex) => union(
            dayAccumulated,
            {
              'PriceCollection__ExchangeSessions__${exchange.key}__Windows__${windowIndex}__Days__${dayIndex}': exchange.value.windows[windowIndex].days[dayIndex]
            }))))))

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
      appCommandLine: 'dotnet TradingEngine.Api.dll'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      alwaysOn: true
    }
  }
}

resource webAppAppSettings 'Microsoft.Web/sites/config@2023-12-01' = if (tradingEngineConnectionString != '' || configureEntraAuth || applicationInsightsConnectionString != '' || priceCollectionEnabled || etoroMarketDataApiKey != '' || etoroMarketDataUserKey != '' || !empty(marketDataExchangeMap) || !empty(marketDataQuoteCurrencyMap) || !empty(priceCollectionExchangeSessions)) {
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
          Authentication__EasyAuth__TrustPlatformHeaders: 'true'
        }
      : {},
    applicationInsightsConnectionString != ''
      ? {
          APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsightsConnectionString
        }
      : {},
    etoroMarketDataApiKey != ''
      ? {
          EToro__MarketData__ApiKey: etoroMarketDataApiKey
        }
      : {},
    etoroMarketDataUserKey != ''
      ? {
          EToro__MarketData__UserKey: etoroMarketDataUserKey
        }
      : {},
    exchangeMapSettings,
    quoteCurrencyMapSettings,
    exchangeSessionSettings,
    priceCollectionEnabled
      ? {
          PriceCollection__Enabled: 'true'
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
        '/signin.html'
        '/css/app.css'
        '/favicon.ico'
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

resource webAppLogs 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: webApp
  name: 'logs'
  properties: {
    applicationLogs: {
      fileSystem: {
        level: 'Information'
      }
    }
    httpLogs: {
      fileSystem: {
        enabled: true
        retentionInDays: 7
        retentionInMb: 35
      }
    }
    detailedErrorMessages: {
      enabled: true
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
