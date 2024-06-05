@description('Virtual network name')
param virtualNetworkName string = 'product-construction-service-vnet-int'

param location string = 'westus2'

@description('private endpoint name')
var kustoClusterPrivateEndpointName = 'pcs-kusto-cluster-private-endpoint'

@description('Private Dns Zone name')
param kustoPrivateDnsZoneName string = 'privatelink.kusto.windows.net'

@description('Virtual Network Link name')
param kustoVirtualNetworkLinkName string = 'kusto-vnet-link'

@description('Private Dns Zone Group name')
param kustoPrivateDnsZoneGroupName string = 'pcs-kusto-private-endpoint-group'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: virtualNetworkName
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' existing = {
  name: kustoClusterPrivateEndpointName
}

resource kustoPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: kustoPrivateDnsZoneName
  location: 'global'
}

output asd string = privateEndpoint.properties.customDnsConfigs[0].fqdn

resource kustoVirtualNetworkLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  name: kustoVirtualNetworkLinkName
  parent: kustoPrivateDnsZone
  location: 'global'
  properties: {
    virtualNetwork: {
      id: virtualNetwork.id
    }
    registrationEnabled: false
  }
}

resource kustoARecord 'Microsoft.Network/privateDnsZones/A@2020-06-01' = {
  name: 'engdata'
  parent: kustoPrivateDnsZone
  properties: {
    ttl: 10
    aRecords: [
      {
        ipv4Address: '10.0.1.6'
      }
    ]
  }
}

resource privateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  name: kustoPrivateDnsZoneGroupName
  parent: privateEndpoint
  properties: {
    privateDnsZoneConfigs: [
      {
        name: kustoPrivateDnsZone.name
        properties: {
          privateDnsZoneId: kustoPrivateDnsZone.id
        }
      }
    ]
  }
}
