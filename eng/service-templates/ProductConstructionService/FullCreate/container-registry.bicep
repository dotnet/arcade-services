param location string
param containerRegistryName string
param acrPullRole string
param pcsIdentityPrincipalId string
param subscriptionTriggererPricnipalId string
param longestBuildPathUpdaterIdentityPrincipalId string
param feedCleanerIdentityPrincipalId string
param acrPushRole string
param deploymentIdentityPrincipalId string

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

// allow acr pulls to the identity used for the pcs
resource aksAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry 
  name: guid(subscription().id, resourceGroup().id, 'pcsAcrPull')
  properties: {
      roleDefinitionId: acrPullRole
      principalType: 'ServicePrincipal'
      principalId: pcsIdentityPrincipalId
  }
}

// allow acr pulls to the identity used for the subscription triggerer
resource subscriptionTriggererIdentityAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry 
  name: guid(subscription().id, resourceGroup().id, 'subscriptionTriggererAcrPull')
  properties: {
      roleDefinitionId: acrPullRole
      principalType: 'ServicePrincipal'
      principalId: subscriptionTriggererPricnipalId
  }
}

// allow acr pulls to the identity used for the longest build path updater
resource longestBuildPathUpdaterIdentityAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry 
  name: guid(subscription().id, resourceGroup().id, 'longestBuildPathUpdaterAcrPull')
  properties: {
      roleDefinitionId: acrPullRole
      principalType: 'ServicePrincipal'
      principalId: longestBuildPathUpdaterIdentityPrincipalId
  }
}

// allow acr pulls to the identity used for the feed cleaner
resource feedCleanerIdentityAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry 
  name: guid(subscription().id, resourceGroup().id, 'feedCleanerAcrPull')
  properties: {
      roleDefinitionId: acrPullRole
      principalType: 'ServicePrincipal'
      principalId: feedCleanerIdentityPrincipalId
  }
}

// Give the PCS Deployment MI the ACR Push role to be able to push docker images
resource deploymentAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry
  name: guid(subscription().id, resourceGroup().id, 'deploymentAcrPush')
  properties: {
      roleDefinitionId: acrPushRole
      principalType: 'ServicePrincipal'
      principalId: deploymentIdentityPrincipalId
  }
}
