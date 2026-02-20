param location string
param azureCacheRedisName string
param appIdentityPrincipalId string
param deploymentIdentityPrincipalId string

resource redisCache 'Microsoft.Cache/redis@2024-03-01' = {
  name: azureCacheRedisName
  location: location
  properties: {
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    sku: {
      capacity: 0
      family: 'C'
      name: 'Basic'
    }
    redisConfiguration: {
      'aad-enabled': 'true'
    }
    disableAccessKeyAuthentication: true
  }
}

// allow redis cache read / write access to the service's identity
resource pcsRedisDataContributorRoleAssignment 'Microsoft.Cache/redis/accessPolicyAssignments@2024-03-01' = {
  name: guid(subscription().id, resourceGroup().id, 'pcsDataContributor')
  parent: redisCache
  properties: {
    accessPolicyName: 'Data Contributor'
    objectId: appIdentityPrincipalId
    objectIdAlias: 'PCS Managed Identity'
  }
}

// allow redis cache read / write access to the service's identity
resource deploymentRedisDataContributorRoleAssignment 'Microsoft.Cache/redis/accessPolicyAssignments@2024-03-01' = {
  name: guid(subscription().id, resourceGroup().id, 'deploymentDataContributor')
  parent: redisCache
  properties: {
    accessPolicyName: 'Data Contributor'
    objectId: deploymentIdentityPrincipalId
    objectIdAlias: 'PCS Managed Identity'
  }
}
