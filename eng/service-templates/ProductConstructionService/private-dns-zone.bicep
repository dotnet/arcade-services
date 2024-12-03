param privateDnsZoneName string = 'product-construction-service-dns-zone-int'
param containerEnvStaticIp string
param virtualNetworkId string

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
}

resource starRecordSet 'Microsoft.Network/privateDnsZones/A@2020-06-01' = {
  name: '*'
  parent: privateDnsZone
  properties: {
    ttl: 3600
    aRecords: [
      { 
        ipv4Address: containerEnvStaticIp
      }
    ]
  }
}

resource atRecordSet 'Microsoft.Network/privateDnsZones/A@2020-06-01' = {
  name: '@'
  parent: privateDnsZone
  properties: {
    ttl: 3600
    aRecords: [
      { 
        ipv4Address: containerEnvStaticIp
      }
    ]
  }
}

resource symbolicname 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  name: 'pcs-pdns-link'
  parent: privateDnsZone
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetworkId
    }
  }
}
