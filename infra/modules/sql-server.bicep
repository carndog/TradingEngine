@description('Globally unique name of the Azure SQL logical server.')
param sqlServerName string

@description('Azure region for the server.')
param location string

@description('Login/display name of the Microsoft Entra administrator principal.')
param entraAdminLogin string

@description('Object ID of the Microsoft Entra administrator principal.')
param entraAdminObjectId string

@description('Principal type of the Microsoft Entra administrator.')
@allowed([
  'User'
  'Group'
  'Application'
])
param entraAdminPrincipalType string

@description('Tags applied to the server.')
param tags object

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: entraAdminLogin
      sid: entraAdminObjectId
      tenantId: tenant().tenantId
      principalType: entraAdminPrincipalType
    }
  }
}

resource allowAzureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output sqlServerName string = sqlServer.name
output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
