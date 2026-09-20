using '../main.bicep'

param environmentName = 'dev'
param location = 'ukwest'
param namingPrefix = 'tradingengine'
param appServiceSkuName = 'B1'
param appServicePlanFreeOfferExpirationTime = '2026-10-15T17:51:34.82'

// Azure SQL is defined but deliberately disabled. Enabling it is an issue-35
// decision after cost approval. The Entra administrator parameters are
// intentionally absent: they are supplied at deploy time and must never be
// committed to this repository.
param provisionAzureSql = false

param tags = {
  project: 'trading-engine'
  environment: 'dev'
  owner: 'jason'
}
