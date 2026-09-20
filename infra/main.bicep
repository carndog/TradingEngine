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

var resourceGroupName = 'rg-${namingPrefix}-${environmentName}'
var appServicePlanName = 'asp-${namingPrefix}-${environmentName}'
var webAppName = 'app-${namingPrefix}-${environmentName}-${uniqueString(subscription().subscriptionId, resourceGroupName)}'
var sqlServerName = 'sql-${namingPrefix}-${environmentName}-${uniqueString(subscription().subscriptionId, resourceGroupName)}'
var sqlDatabaseName = 'sqldb-${namingPrefix}-${environmentName}'

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
output sqlServerName string = sqlServer.?outputs.sqlServerName ?? ''
output sqlServerFullyQualifiedDomainName string = sqlServer.?outputs.fullyQualifiedDomainName ?? ''
output sqlDatabaseName string = sqlDatabase.?outputs.databaseName ?? ''
