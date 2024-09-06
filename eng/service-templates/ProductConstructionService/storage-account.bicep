param location string
param storageAccountName string
param pcsIdentityPrincipalId string
param subscriptionTriggererIdentityPrincipalId string
param storageQueueContrubutorRole string
param blobContributorRole string

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
      name: 'Standard_LRS'
  }
  properties: {
      allowBlobPublicAccess: false
      publicNetworkAccess: 'Enabled'
      allowSharedKeyAccess: false
      networkAcls: {
          defaultAction: 'Deny'
      }
  }
}

// Create the dataprotection container in the storage account
resource storageAccountBlobService 'Microsoft.Storage/storageAccounts/blobServices@2022-09-01' = {
  name: 'default'
  parent: storageAccount
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-09-01' = {
  name: 'dataprotection'
  parent: storageAccountBlobService
}

resource storageAccountQueueService 'Microsoft.Storage/storageAccounts/queueServices@2022-09-01' = {
  name: 'default'
  parent: storageAccount
}

resource storageAccountQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2022-09-01' = {
  name: 'pcs-workitems'
  parent: storageAccountQueueService
}

// allow storage queue access to the identity used for the aca's
resource pcsStorageQueueAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(subscription().id, resourceGroup().id, storageQueueContrubutorRole)
  properties: {
      roleDefinitionId: storageQueueContrubutorRole
      principalType: 'ServicePrincipal'
      principalId: pcsIdentityPrincipalId
  }
}

// allow storage queue access to the identity used for the SubscriptionTriggerer
resource subscriptionTriggererStorageQueueAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(subscription().id, resourceGroup().id, storageQueueContrubutorRole)
  properties: {
      roleDefinitionId: storageQueueContrubutorRole
      principalType: 'ServicePrincipal'
      principalId: subscriptionTriggererIdentityPrincipalId
  }
}

// allow data protection container access to the identity used for the pcs
resource storageAccountContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: dataProtectionContainer
  name: guid(subscription().id, resourceGroup().id, blobContributorRole)
  properties: {
      roleDefinitionId: blobContributorRole
      principalType: 'ServicePrincipal'
      principalId: pcsIdentityPrincipalId
  }
}
