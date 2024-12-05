param location string
param logAnalyticsName string
param containerEnvironmentName string
param productConstructionServiceSubnetId string
param infrastructureResourceGroupName string
param containerAppsManagedEnvironmentsContributor string
param deploymentIdentityPrincipalId string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-12-01-preview' existing = {
  name: logAnalyticsName
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

resource deploymentSubscriptionTriggererContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    scope: containerEnvironment
    name: guid(subscription().id, resourceGroup().id, containerAppsManagedEnvironmentsContributor)
    properties: {
        roleDefinitionId: containerAppsManagedEnvironmentsContributor
        principalType: 'ServicePrincipal'
        principalId: deploymentIdentityPrincipalId
    }
  }

output containerEnvironmentId string = containerEnvironment.id
