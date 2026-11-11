@description('Name of the Log Analytics workspace backing Application Insights.')
param logAnalyticsWorkspaceName string

@description('Name of the Application Insights resource.')
param applicationInsightsName string

@description('Azure region for the resources.')
param location string

@description('Retention in days applied to the workspace and Application Insights data. 30 is the minimum supported value and bounds development storage cost.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Daily ingestion cap in GB applied to the Log Analytics workspace, expressed as a string so fractional values are supported. A safeguard against ingestion spikes, not a cost bound: collection cannot stop at exactly the cap, excess data is billed, and collection resumes at a workspace-specific reset hour that cannot be configured.')
param dailyDataCapGb string = '0.1'

@description('Enable scheduled-query alert rules for repeated price-collection and notification dispatch failures.')
param enablePipelineFailureAlerts bool = false

@description('Failure count inside the evaluation window that fires each pipeline alert.')
param pipelineFailureAlertThreshold int = 3

@description('Tags applied to the resources.')
param tags object

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    workspaceCapping: {
      dailyQuotaGb: json(dailyDataCapGb)
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    Request_Source: 'rest'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: retentionInDays
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource priceCollectionFailureAlert 'Microsoft.Insights/scheduledQueryRules@2021-08-01' = if (enablePipelineFailureAlerts) {
  name: 'alert-price-collection-failures'
  location: location
  tags: tags
  properties: {
    displayName: 'TradingEngine repeated price-collection failures'
    description: 'Fires when ${pipelineFailureAlertThreshold} or more price-collection failure increments are reported inside the evaluation window.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    scopes: [
      applicationInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "tradingengine.price_collection.failures" | summarize TotalFailures = sum(value) | where TotalFailures >= ${pipelineFailureAlertThreshold}'
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: 1
        }
      ]
    }
    actions: {
      actionGroups: []
    }
  }
}

resource notificationFailureAlert 'Microsoft.Insights/scheduledQueryRules@2021-08-01' = if (enablePipelineFailureAlerts) {
  name: 'alert-notification-failures'
  location: location
  tags: tags
  properties: {
    displayName: 'TradingEngine repeated notification dispatch failures'
    description: 'Fires when ${pipelineFailureAlertThreshold} or more notification dispatch failure increments are reported inside the evaluation window.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    scopes: [
      applicationInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "tradingengine.notifications.failures" | summarize TotalFailures = sum(value) | where TotalFailures >= ${pipelineFailureAlertThreshold}'
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: 1
        }
      ]
    }
    actions: {
      actionGroups: []
    }
  }
}

output applicationInsightsName string = applicationInsights.name
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name

@secure()
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
