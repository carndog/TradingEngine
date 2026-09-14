using '../main.bicep'

param environmentName = 'dev'
param location = 'uksouth'
param namingPrefix = 'tradingengine'
param appServiceSkuName = 'B1'
param tags = {
  project: 'trading-engine'
  environment: 'dev'
  owner: 'jason'
}
