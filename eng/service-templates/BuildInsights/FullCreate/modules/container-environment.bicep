param location string
param logAnalyticsName string
param containerEnvironmentName string
param productConstructionServiceSubnetId string
param infrastructureResourceGroupName string
param applicationInsightsName string
param deploymentIdentityPrincipalId string

var containerAppsManagedEnvironmentsContributor = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '57cc5028-e6a7-4284-868d-0611c5923f8d'
)

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-12-01-preview' = {
  name: logAnalyticsName
  location: location
  properties: any({
    retentionInDays: 30
    features: {
      searchVersion: 1
    }
    sku: {
      name: 'PerGB2018'
    }
  })
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2023-04-01-preview' = {
  name: containerEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    vnetConfiguration: {
      infrastructureSubnetId: productConstructionServiceSubnetId
    }
    infrastructureResourceGroup: infrastructureResourceGroupName
  }
}

// application insights for service logging
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    RetentionInDays: 120
    WorkspaceResourceId: logAnalytics.id
  }
}

resource deploymentSubscriptionTriggerContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerEnvironment
  name: guid(subscription().id, resourceGroup().id, 'containerEnvironment-contributor')
  properties: {
    roleDefinitionId: containerAppsManagedEnvironmentsContributor
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityPrincipalId
  }
}

output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
output containerEnvironmentId string = containerEnvironment.id
