targetScope = 'subscription'

@description('Short environment name, for example dev.')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Naming prefix applied to generated resource names.')
param namingPrefix string

@description('App Service Plan SKU name. B1 is the low-cost development default.')
param appServiceSkuName string = 'B1'

@description('Expiry of the subscription-assigned temporary App Service Plan free offer, preserved so deployments do not remove it.')
param appServicePlanFreeOfferExpirationTime string

@description('Common tags applied to all resources.')
param tags object

@description('Provision the Azure SQL logical server and database. Disabled by default; enabling is a deliberate issue-35 decision.')
param provisionAzureSql bool = false

@description('Login/display name of the Microsoft Entra administrator for the SQL logical server. Required only when provisionAzureSql is true; never commit a live value.')
param sqlEntraAdminLogin string = ''

@description('Object ID of the Microsoft Entra administrator for the SQL logical server. Required only when provisionAzureSql is true; never commit a live value.')
param sqlEntraAdminObjectId string = ''

@description('Principal type of the Microsoft Entra administrator for the SQL logical server. Required only when provisionAzureSql is true.')
param sqlEntraAdminPrincipalType string = ''

@description('Shared key required in the X-Database-Probe-Key header for the /health/database endpoint. Required only when provisionAzureSql is true; never commit a live value.')
@secure()
param databaseProbeKey string = ''

@description('Enable App Service Easy Auth with Microsoft Entra ID on the Web App. When true, entraAuthClientId and entraAuthAllowedPrincipalIds are required at deploy time.')
param configureEntraAuth bool = false

@description('Application (client) ID of the single-tenant Entra app registration backing Easy Auth. Required when configureEntraAuth is true; never commit a live value.')
param entraAuthClientId string = ''

@description('Object IDs of the Entra principals allowed through Easy Auth (the owner allowlist). Required when configureEntraAuth is true; never commit live values.')
param entraAuthAllowedPrincipalIds array = []

var resourceGroupName = 'rg-${namingPrefix}-${environmentName}'
var appServicePlanName = 'asp-${namingPrefix}-${environmentName}'
var webAppName = 'app-${namingPrefix}-${environmentName}-${uniqueString(subscription().subscriptionId, resourceGroupName)}'
var sqlServerName = 'sql-${namingPrefix}-${environmentName}-${uniqueString(subscription().subscriptionId, resourceGroupName)}'
var sqlDatabaseName = 'sqldb-${namingPrefix}-${environmentName}'
var sqlServerFullyQualifiedDomainName = '${sqlServerName}${environment().suffixes.sqlServerHostname}'
var easyAuthIdentityName = 'id-${namingPrefix}-easyauth-${environmentName}'
var tradingEngineConnectionString = provisionAzureSql
  ? 'Server=tcp:${sqlServerFullyQualifiedDomainName},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;'
  : ''

module resourceGroup 'modules/resource-group.bicep' = {
  name: 'resourceGroup-${environmentName}'
  params: {
    resourceGroupName: resourceGroupName
    location: location
    tags: tags
  }
}

module appService 'modules/app-service.bicep' = {
  name: 'appService-${environmentName}'
  scope: az.resourceGroup(resourceGroupName)
  params: {
    appServicePlanName: appServicePlanName
    webAppName: webAppName
    location: location
    appServiceSkuName: appServiceSkuName
    appServicePlanFreeOfferExpirationTime: appServicePlanFreeOfferExpirationTime
    tradingEngineConnectionString: tradingEngineConnectionString
    databaseProbeKey: databaseProbeKey
    configureEntraAuth: configureEntraAuth
    entraAuthClientId: entraAuthClientId
    entraAuthAllowedPrincipalIds: entraAuthAllowedPrincipalIds
    easyAuthIdentityName: easyAuthIdentityName
    tags: tags
  }
  dependsOn: [
    resourceGroup
  ]
}

module sqlServer 'modules/sql-server.bicep' = if (provisionAzureSql) {
  name: 'sqlServer-${environmentName}'
  scope: az.resourceGroup(resourceGroupName)
  params: {
    sqlServerName: sqlServerName
    location: location
    entraAdminLogin: sqlEntraAdminLogin
    entraAdminObjectId: sqlEntraAdminObjectId
    entraAdminPrincipalType: sqlEntraAdminPrincipalType
    tags: tags
  }
  dependsOn: [
    resourceGroup
  ]
}

module sqlDatabase 'modules/sql-database.bicep' = if (provisionAzureSql) {
  name: 'sqlDatabase-${environmentName}'
  scope: az.resourceGroup(resourceGroupName)
  params: {
    sqlServerName: sqlServerName
    databaseName: sqlDatabaseName
    location: location
    tags: tags
  }
  dependsOn: [
    sqlServer
  ]
}

output resourceGroupName string = resourceGroup.outputs.resourceGroupName
output webAppName string = appService.outputs.webAppName
output defaultHostName string = appService.outputs.defaultHostName
output managedIdentityPrincipalId string = appService.outputs.principalId
output easyAuthIdentityName string = appService.outputs.easyAuthIdentityName
output easyAuthIdentityPrincipalId string = appService.outputs.easyAuthIdentityPrincipalId
output sqlServerName string = sqlServer.?outputs.sqlServerName ?? ''
output sqlServerFullyQualifiedDomainName string = sqlServer.?outputs.fullyQualifiedDomainName ?? ''
output sqlDatabaseName string = sqlDatabase.?outputs.databaseName ?? ''
