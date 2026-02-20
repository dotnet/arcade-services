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
  }
  tags: {
    'ms.inv.v0.networkUsage': 'mixedTraffic'
  }
}

// subnet for the product construction service
resource productConstructionServiceSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' = {
  name: serviceSubnetName
  parent: virtualNetwork
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
    networkSecurityGroup: {
      id: networkSecurityGroupId
    }
  }
}

output productConstructionServiceSubnetId string = productConstructionServiceSubnet.id
