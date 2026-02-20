param location string
param deploymentIdentityName string
param appIdentityName string
param scheduledJobIdentityName string
param contributorRole string

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deploymentIdentityName
  location: location
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
  name: guid(subscription().id, resourceGroup().id, contributorRole)
  properties: {
    roleDefinitionId: contributorRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentity.properties.principalId
  }
}

output appIdentityPrincipalId string = appIdentity.properties.principalId
output appIdentityId string = appIdentity.id
output deploymentIdentityPrincipalId string = deploymentIdentity.properties.principalId
output deploymentIdentityId string = deploymentIdentity.id
output scheduledJobIdentityPrincipalId string = scheduledJobIdentity.properties.principalId
output scheduledJobIdentityId string = scheduledJobIdentity.id
