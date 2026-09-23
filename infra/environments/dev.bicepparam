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

param tags = {
  project: 'trading-engine'
  environment: 'dev'
  owner: 'jason'
}
