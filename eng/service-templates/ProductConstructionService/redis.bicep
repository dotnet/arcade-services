param location string
param azureCacheRedisName string
param pcsIdentityPrincipalId string
param deploymentIdentityPrincipalId string

// Azure Managed Redis (Microsoft.Cache/redisEnterprise). The classic
// Microsoft.Cache/redis Basic/Standard/Premium tiers are retiring: no new
// classic instances can be created after 1 Oct 2026, and all remaining
// instances are disabled on 30 Sep 2028. See AzDO #8469.
resource redisCache 'Microsoft.Cache/redisEnterprise@2025-05-01-preview' = {
  name: azureCacheRedisName
  location: location
  sku: {
      name: 'Balanced_B0'
  }
  properties: {
      minimumTlsVersion: '1.2'
      highAvailability: 'Disabled'
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2025-05-01-preview' = {
  name: 'default'
  parent: redisCache
  properties: {
      clientProtocol: 'Encrypted'
      port: 10000
      clusteringPolicy: 'OSSCluster'
      evictionPolicy: 'NoEviction'
      accessKeysAuthentication: 'Disabled'
  }
}

// allow redis cache read / write access to the service's identity
resource pcsRedisDataContributorRoleAssignment 'Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments@2025-05-01-preview' = {
  name: 'pcsDataContributor'
  parent: redisDatabase
  properties: {
      accessPolicyName: 'default'
      user: {
          objectId: pcsIdentityPrincipalId
      }
  }
}

// allow redis cache read / write access to the deployment's identity
resource deploymentRedisDataContributorRoleAssignment 'Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments@2025-05-01-preview' = {
  name: 'deploymentDataContributor'
  parent: redisDatabase
  properties: {
      accessPolicyName: 'default'
      user: {
          objectId: deploymentIdentityPrincipalId
      }
  }
}

output redisCacheHostName string = redisCache.properties.hostName
output redisCachePort int = redisDatabase.properties.port
