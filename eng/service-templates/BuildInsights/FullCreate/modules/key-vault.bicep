param location string
param keyVaultName string
param appIdentityPrincipalId string
param serviceSubnetId string

module roles './roles.bicep' = {
  name: 'roles'
}

resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      name: 'standard'
      family: 'A'
    }
    tenantId: subscription().tenantId
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    accessPolicies: []
    enableRbacAuthorization: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: [
        {
          id: serviceSubnetId
        }
      ]
    }
  }
}

// allow secret access to the identity used for the aca's
resource secretAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(subscription().id, resourceGroup().id, 'keyVault-secret-access')
  properties: {
    roleDefinitionId: roles.outputs.kvSecretUserRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}

// allow crypto access to the identity used for the aca's
resource cryptoAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(subscription().id, resourceGroup().id, 'keyVault-crypto-access')
  properties: {
    roleDefinitionId: roles.outputs.kvCryptoUserRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}

resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  name: 'data-protection-encryption-key'
  parent: keyVault
  properties: {
    attributes: {
      enabled: true
      exportable: false
    }
    keyOps: [
      'sign'
      'verify'
      'wrapKey'
      'unwrapKey'
      'encrypt'
      'decrypt'
    ]
    keySize: 2048
    kty: 'RSA'
    rotationPolicy: {
      attributes: {
        expiryTime: 'P540D'
      }
      lifetimeActions: [
        {
          action: {
            type: 'rotate'
          }
          trigger: {
            timeBeforeExpiry: 'P30D'
          }
        }
      ]
    }
  }
}

output keyVaultId string = keyVault.id
