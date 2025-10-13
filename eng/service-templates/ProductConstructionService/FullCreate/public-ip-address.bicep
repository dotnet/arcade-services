param publicIpAddressName string
param location string
param publicIpAddressServiceTag string

resource publicIpAddress 'Microsoft.Network/publicIPAddresses@2022-09-01' = {
  name: publicIpAddressName
  location: location
  properties: {
    publicIPAllocationMethod: 'static'
    publicIPAddressVersion: 'IPv4'
    ipTags: [
      {
        ipTagType: 'FirstPartyUsage'
        tag: '/${publicIpAddressServiceTag}'
      }
    ]
  }
  sku: {
    name: 'Standard'
  }
}
