using '../main.bicep'

param environmentName = 'dev'
param location = 'ukwest'
param namingPrefix = 'tradingengine'
param appServiceSkuName = 'B1'
param appServicePlanFreeOfferExpirationTime = '2026-10-15T17:51:00'

// Azure SQL is enabled for the development environment (issue #35). The Entra
// administrator parameters are intentionally absent: they are supplied at
// deploy time from GitHub environment secrets and must never be committed to
// this repository.
param provisionAzureSql = true
param useBasicSqlDatabase = true

// Easy Auth (issue #38) is enabled for the development environment. The app
// registration client ID and the owner principal allowlist are intentionally
// absent: they are supplied at deploy time from GitHub environment secrets and
// must never be committed to this repository.
param configureEntraAuth = true

// Application Insights and its Log Analytics workspace for the development
// environment (issue #36). Retention is the supported minimum. The daily
// ingestion cap is a safeguard against ingestion spikes, not a cost bound:
// collection cannot stop at exactly the cap, the overshoot is billed, and
// collection resumes at a workspace-specific reset hour that is not configurable.
param applicationInsightsRetentionDays = 30
param logAnalyticsDailyDataCapGb = '0.1'

// Timer-triggered price collection (issue #9) runs as a hosted service inside
// the API. It stays disabled until the exchange session app settings for the
// monitored instruments have been reviewed; see docs/price-collection.md.
param enablePriceCollection = false

// The eToro market-data keys, the exchange/quote-currency maps and the exchange
// session definitions are intentionally absent: they are supplied at deploy
// time from GitHub environment secrets (etoroMarketDataApiKey,
// etoroMarketDataUserKey, marketDataExchangeMap, marketDataQuoteCurrencyMap and
// priceCollectionExchangeSessions) and must never be committed to this
// repository. The appsettings deployment replaces the whole app-settings
// collection, so these parameters are what keep the values in place across
// infrastructure redeployments.

param tags = {
  project: 'trading-engine'
  environment: 'dev'
  owner: 'jason'
}
