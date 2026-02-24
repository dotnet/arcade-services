param location string
param containerRegistryName string
param appIdentityPrincipalId string
param scheduledJobIdentityPrincipalId string
param deploymentIdentityPrincipalId string

module roles './roles.bicep' = {
  name: 'roles'
}

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
    roleDefinitionId: roles.outputs.acrPullRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}

// allow acr pulls to the identity used for the scheduled job
resource scheduledJobIdentityAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(subscription().id, resourceGroup().id, 'scheduledJobAcrPull')
  properties: {
    roleDefinitionId: roles.outputs.acrPullRole
    principalType: 'ServicePrincipal'
    principalId: scheduledJobIdentityPrincipalId
  }
}

// Give the Deployment MI the ACR Push role to be able to push docker images
resource deploymentAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(subscription().id, resourceGroup().id, 'deploymentAcrPush')
  properties: {
    roleDefinitionId: roles.outputs.acrPushRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityPrincipalId
  }
}
