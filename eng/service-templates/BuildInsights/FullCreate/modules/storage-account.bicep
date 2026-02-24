param location string
param storageAccountName string
param appIdentityPrincipalId string

module roles './roles.bicep' = {
  name: 'roles'
}

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

// Create the data protection container in the storage account
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
  name: 'app-workitems'
  parent: storageAccountQueueService
}

// allow storage queue access to the identity used for the aca's
resource acaStorageQueueAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(subscription().id, resourceGroup().id, 'aca-queue-access')
  properties: {
    roleDefinitionId: roles.outputs.storageQueueContributorRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}

// allow data protection container access to the identity used for the service
resource storageAccountContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: dataProtectionContainer
  name: guid(subscription().id, resourceGroup().id, 'storage-blob-contributor')
  properties: {
    roleDefinitionId: roles.outputs.blobContributorRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}
