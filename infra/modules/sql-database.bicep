@description('Name of the Azure SQL logical server that hosts the database.')
param sqlServerName string

@description('Name of the database.')
param databaseName string

@description('Azure region for the database.')
param location string

@description('Maximum vCore capacity of the serverless database.')
param maxVcores int = 1

@description('Minimum vCore capacity of the serverless database, expressed as a decimal string (for example \'0.5\').')
param minVcores string = '0.5'

@description('Minutes of inactivity before the serverless database auto-pauses. 60 is the shortest supported period.')
param autoPauseDelayMinutes int = 60

@description('Maximum database size in bytes. 34359738368 is 32 GB, the free-offer storage limit.')
param maxSizeBytes int = 34359738368

@description('Tags applied to the database.')
param tags object

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' existing = {
  name: sqlServerName
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: maxVcores
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: maxSizeBytes
    autoPauseDelay: autoPauseDelayMinutes
    minCapacity: json(minVcores)
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
    readScale: 'Disabled'
    isLedgerOn: false
  }
}

output databaseName string = sqlDatabase.name
