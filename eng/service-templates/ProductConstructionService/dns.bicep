@description('Virtual network name')
param virtualNetworkName string = 'product-construction-service-vnet-int'

param location string = 'westus2'

@description('private endpoint name')
param privateEndpointName string = 'pcs-storage-account-queue-private-endpoint'

@description('Private Dns Zone name')
param privateDnsZoneName string = 'privatelink.queue.core.windows.net'

@description('Virtual Network Link name')
param virtualNetworkLinkName string = 'product-construction-service-vnet-int-link'

@description('Private Dns Zone Group name')
param privateDnsZoneGroupName string = 'pcs-storage-account-queue-private-endpoint-group'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: virtualNetworkName
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' existing = {
  name: privateEndpointName
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
}

resource virtualNetworkLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: virtualNetworkLinkName
  parent: privateDnsZone
  location: 'global'
  properties: {
    virtualNetwork: {
      id: virtualNetwork.id
    }
    registrationEnabled: false
  }
}

resource productconstructionintARecord 'Microsoft.Network/privateDnsZones/A@2020-06-01' = {
  name: 'productconstructionint'
  parent: privateDnsZone
  properties: {
    ttl: 10
    aRecords: [
      {
        ipv4Address: '10.0.1.3'
      }
    ]
  }
}

resource privateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  name: privateDnsZoneGroupName
  parent: privateEndpoint
  properties: {
    privateDnsZoneConfigs: [
      {
        name: privateDnsZone.name
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}
