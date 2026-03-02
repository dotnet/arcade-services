param location string
param deploymentIdentityName string
param deploymentIdentityCreate bool
param deploymentIdentityResourceGroupName string
param appIdentityName string
param scheduledJobIdentityName string

var contributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b24988ac-6180-42a0-ab88-20f7382dd24c'
)

resource deploymentIdentityNew 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (deploymentIdentityCreate) {
  name: deploymentIdentityName
  location: location
}

resource existingIdentityResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' existing = if (!deploymentIdentityCreate) {
  scope: subscription()
  name: deploymentIdentityResourceGroupName
}

resource deploymentIdentityExisting 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = if (!deploymentIdentityCreate) {
  name: deploymentIdentityName
  scope: existingIdentityResourceGroup
}

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: appIdentityName
  location: location
}

resource scheduledJobIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: scheduledJobIdentityName
  location: location
}

resource appIdentityContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: appIdentity
  name: guid(subscription().id, resourceGroup().id, 'appIdentity-contributor')
  properties: {
    roleDefinitionId: contributorRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityCreate ? deploymentIdentityNew!.properties.principalId : deploymentIdentityExisting!.properties.principalId
  }
}

output appIdentityPrincipalId string = appIdentity.properties.principalId
output appIdentityId string = appIdentity.id
output deploymentIdentityPrincipalId string = deploymentIdentityCreate ? deploymentIdentityNew!.properties.principalId : deploymentIdentityExisting!.properties.principalId
output deploymentIdentityId string = deploymentIdentityCreate ? deploymentIdentityNew!.id : deploymentIdentityExisting!.id
output scheduledJobIdentityPrincipalId string = scheduledJobIdentity.properties.principalId
output scheduledJobIdentityId string = scheduledJobIdentity.id
