@description('Virtual network name')
param virtualNetworkName string = 'product-construction-service-vnet-int'

param location string = 'westus2'

@description('private endpoint name')
param privateEndpointName string = 'pcs-storage-account-queue-private-endpoint'

@description('Private Dns Zone name')
param queuePrivateDnsZoneName string = 'privatelink.queue.core.windows.net'

@description('Virtual Network Link name')
param queueVirtualNetworkLinkName string = 'product-construction-service-vnet-int-link'

@description('Private Dns Zone Group name')
param privateDnsZoneGroupName string = 'pcs-storage-account-queue-private-endpoint-group'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: virtualNetworkName
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' existing = {
  name: privateEndpointName
}

resource queuePrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: queuePrivateDnsZoneName
  location: 'global'
}

resource queueVirtualNetworkLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: queueVirtualNetworkLinkName
  parent: queuePrivateDnsZone
  location: 'global'
  properties: {
    virtualNetwork: {
      id: virtualNetwork.id
    }
    registrationEnabled: false
  }
}

resource queueARecord 'Microsoft.Network/privateDnsZones/A@2020-06-01' = {
  name: 'productconstructionint'
  parent: queuePrivateDnsZone
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
        name: queuePrivateDnsZone.name
        properties: {
          privateDnsZoneId: queuePrivateDnsZone.id
        }
      }
    ]
  }
}
