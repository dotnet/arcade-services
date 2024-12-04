resource containerEnvironment 'Microsoft.App/managedEnvironments@2023-04-01-preview' existing = {
  name: 'product-construction-service-env-int'
}

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' existing = { name: 'product-construction-service-vnet-int' }

module privateDnsZone 'private-dns-zone.bicep' = {
  name: 'privateDnsZone'
  params: {
    privateDnsZoneName: containerEnvironment.properties.defaultDomain
    containerEnvStaticIp: containerEnvironment.properties.staticIp
    virtualNetworkId: virtualNetwork.id
  }
}
