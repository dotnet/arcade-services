param virtualNetworkName string
param location string
param serviceSubnetName string
param networkSecurityGroupId string

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' = {
  name: virtualNetworkName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: serviceSubnetName
        properties: {
          addressPrefix: '10.0.0.0/23'
          delegations: [
            {
              name: 'Microsoft.App/environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
          serviceEndpoints: [
            {
              service: 'Microsoft.Storage'
            }
            {
              service: 'Microsoft.KeyVault'
            }
          ]
          networkSecurityGroup: {
            id: networkSecurityGroupId
          }
        }
      }
    ]
  }
  tags: {
    'ms.inv.v0.networkUsage': 'mixedTraffic'
  }
}

output productConstructionServiceSubnetId string = virtualNetwork.properties.subnets[0].id
output virtualNetworkId string = virtualNetwork.id
