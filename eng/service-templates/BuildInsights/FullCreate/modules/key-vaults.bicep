param location string
param keyVaultName string
param kvSecretUserRole string
param kvCryptoUserRole string
param appIdentityPrincipalId string

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
  }
}

// allow secret access to the identity used for the aca's
resource secretAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(subscription().id, resourceGroup().id, kvSecretUserRole)
  properties: {
    roleDefinitionId: kvSecretUserRole
    principalType: 'ServicePrincipal'
    principalId: appIdentityPrincipalId
  }
}

// allow crypto access to the identity used for the aca's
resource cryptoAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(subscription().id, resourceGroup().id, kvCryptoUserRole)
  properties: {
    roleDefinitionId: kvCryptoUserRole
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
