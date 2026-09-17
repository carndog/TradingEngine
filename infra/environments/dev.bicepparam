using '../main.bicep'

param environmentName = 'dev'
param location = 'ukwest'
param namingPrefix = 'tradingengine'
param appServiceSkuName = 'B1'
param appServicePlanFreeOfferExpirationTime = '2026-10-15T17:51:34.82'
param tags = {
  project: 'trading-engine'
  environment: 'dev'
  owner: 'jason'
}
