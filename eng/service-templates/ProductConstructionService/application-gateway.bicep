param appGwName string
param location string
param kvName string
param appGwIdentityName string
param certificateName string
// Certificate Secret identifier, without the last part (the version)
param certificateSecretIdShort string = 'https://productconstructionint.vault.azure.net/secrets/maestro-int-ag'
param virtualNetworkName string = 'product-construction-service-vnet-int'
param appGwVirtualNetworkSubnetName string = 'application-gateway-subnet'
param nsgName string = 'product-construction-service-nsg-int'
param publicIpAddressName string = 'product-construction-service-public-ip-int'
param publicIpAddressDnsName string = ''

resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' existing = {
  name: kvName
}

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' existing = { name: virtualNetworkName }

resource networkSecurityGroup 'Microsoft.Network/networkSecurityGroups@2023-11-01' existing = { name: nsgName }

// subnet for the product construction service
resource productConstructionServiceSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' = {
  name: appGwVirtualNetworkSubnetName 
  parent: virtualNetwork
  properties: {
      addressPrefix: '10.0.2.0/24'
      networkSecurityGroup: {
          id: networkSecurityGroup.id
      }
  }
}

resource publicIpAddress 'Microsoft.Network/publicIPAddresses@2022-09-01' = {
  name: publicIpAddressName
  location: location
  properties: {
    publicIPAllocationMethod: 'static'
    publicIPAddressVersion: 'IPv4'

    dnsSettings: {
      domainNameLabel: publicIpAddressDnsName
    }
  }
  sku: {
    name: 'Standard'
  }
}

resource appGwIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: appGwIdentityName
  location: location
}

var certificateUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'db79e9a7-68ee-4b58-9aeb-b90e7c24fcba')
var kvSecretUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource appGwCertificateUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(subscription().id, resourceGroup().id, 'appGwCertUser')
  properties: {
      roleDefinitionId: certificateUserRole
      principalType: 'ServicePrincipal'
      principalId: appGwIdentity.properties.principalId
  }
}

resource appGwSecretUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(subscription().id, resourceGroup().id, 'appGwSecretUser')
  properties: {
      roleDefinitionId: kvSecretUser
      principalType: 'ServicePrincipal'
      principalId: appGwIdentity.properties.principalId
  }
}

resource applicationGateway 'Microsoft.Network/applicationGateways@2023-04-01' = {
  name: appGwName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appGwIdentity.id}' : {}
    }
  }
  properties: {
    sku: {
      name: 'Standard_v2'
      tier: 'Standard_v2'
      capacity: 10
    }
    sslCertificates: [
      {
        name: certificateName
        properties: {
          keyVaultSecretId: certificateSecretIdShort
        }
      }
    ]
    gatewayIPConfigurations: [
      {
        name: 'appGwIpConfigurationName'
        properties: {
          subnet: {
            id: productConstructionServiceSubnet.id
          }
        }
      }
    ]
  }
}
