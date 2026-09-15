targetScope = 'subscription'

@description('Short environment name, for example dev.')
param environmentName string

@description('Azure region for all resources.')
param location string

@description('Naming prefix applied to generated resource names.')
param namingPrefix string

@description('App Service Plan SKU name. B1 is the low-cost development default.')
param appServiceSkuName string = 'B1'

@description('Common tags applied to all resources.')
param tags object

var resourceGroupName = 'rg-${namingPrefix}-${environmentName}'
var appServicePlanName = 'asp-${namingPrefix}-${environmentName}'
var webAppName = 'app-${namingPrefix}-${environmentName}-${uniqueString(subscription().subscriptionId, resourceGroupName)}'

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
    tags: tags
  }
  dependsOn: [
    resourceGroup
  ]
}

output resourceGroupName string = resourceGroup.outputs.resourceGroupName
output webAppName string = appService.outputs.webAppName
output defaultHostName string = appService.outputs.defaultHostName
output managedIdentityPrincipalId string = appService.outputs.principalId
