param location string
param deploymentIdentityName string
param pcsIdentityName string
param subscriptionTriggererIdentityName string
param longestBuildPathUpdaterIdentityName string
param feedCleanerIdentityName string
param contributorRole string

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deploymentIdentityName
  location: location
}

resource pcsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: pcsIdentityName
  location: location
}

resource subscriptionTriggererIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: subscriptionTriggererIdentityName
  location: location
}

resource longestBuildPathUpdaterIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: longestBuildPathUpdaterIdentityName
  location: location
}

resource feedCleanerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: feedCleanerIdentityName
  location: location
}

resource pcsIdentityContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: pcsIdentity
  name: guid(subscription().id, resourceGroup().id, contributorRole)
  properties: {
      roleDefinitionId: contributorRole
      principalType: 'ServicePrincipal'
      principalId: deploymentIdentity.properties.principalId
  }
}

output pcsIdentityPrincipalId string = pcsIdentity.properties.principalId
output pcsIdentityId string = pcsIdentity.id
output deploymentIdentityPrincipalId string = deploymentIdentity.properties.principalId
output deploymentIdentityId string = deploymentIdentity.id
output subscriptionTriggererIdentityPrincipalId string = subscriptionTriggererIdentity.properties.principalId
output subscriptionTriggererIdentityId string = subscriptionTriggererIdentity.id
output longestBuildPathUpdaterIdentityPrincipalId string = longestBuildPathUpdaterIdentity.properties.principalId
output longestBuildPathUpdaterIdentityId string = longestBuildPathUpdaterIdentity.id
output feedCleanerIdentityPrincipalId string = feedCleanerIdentity.properties.principalId
output feedCleanerIdentityId string = feedCleanerIdentity.id
