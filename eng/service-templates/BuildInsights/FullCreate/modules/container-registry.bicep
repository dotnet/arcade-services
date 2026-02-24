param location string
param containerRegistryName string
param appIdentityPrincipalId string
param scheduledJobIdentityPrincipalId string
param deploymentIdentityPrincipalId string

var acrPullRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

var acrPushRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '8311e382-0749-4cb8-b61a-304f252e45ec'
)

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2022-02-01-preview' = {
  name: containerRegistryName
  location: location
  sku: {
    name: 'Premium'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    dataEndpointEnabled: false
    encryption: {
      status: 'disabled'
    }
    networkRuleBypassOptions: 'AzureServices'
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
    policies: {
      retentionPolicy: {
        days: 60
        status: 'enabled'
      }
    }
  }
}

// allow acr pulls to the identity used for the application
resource aksAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(subscription().id, resourceGroup().id, 'acaAcrPull')
  properties: {
    roleDefinitionId: acrPullRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}

// allow acr pulls to the identity used for the scheduled job
resource scheduledJobIdentityAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(subscription().id, resourceGroup().id, 'scheduledJobAcrPull')
  properties: {
    roleDefinitionId: acrPullRole
    principalType: 'ServicePrincipal'
    principalId: scheduledJobIdentityPrincipalId
  }
}

// Give the Deployment MI the ACR Push role to be able to push docker images
resource deploymentAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(subscription().id, resourceGroup().id, 'deploymentAcrPush')
  properties: {
    roleDefinitionId: acrPushRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityPrincipalId
  }
}

output containerRegistryId string = containerRegistry.id
