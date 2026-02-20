param networkSecurityGroupName string
param location string

resource networkSecurityGroup 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: networkSecurityGroupName
  location: location
  properties: {
    securityRules: [
      // These are required by a corp policy
      {
        name: 'NRMS-Rule-101'
        properties: {
          priority: 101
          protocol: 'Tcp'
          sourcePortRange: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '443'
          destinationAddressPrefix: '*'
          access: 'Allow'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-103'
        properties: {
          priority: 103
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'CorpNetPublic'
          destinationPortRange: '*'
          destinationAddressPrefix: '*'
          access: 'Allow'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-104'
        properties: {
          priority: 104
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'CorpNetSaw'
          destinationPortRange: '*'
          destinationAddressPrefix: '*'
          access: 'Allow'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-105'
        properties: {
          priority: 105
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationPortRanges: [
            '1434'
            '1433'
            '3306'
            '4333'
            '5432'
            '6379'
            '7000'
            '7001'
            '7199'
            '9042'
            '9160'
            '9300'
            '16379'
            '26379'
            '27017'
          ]
          destinationAddressPrefix: '*'
          access: 'Deny'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-106'
        properties: {
          priority: 106
          protocol: 'Tcp'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationPortRanges: [
            '22'
            '3389'
          ]
          destinationAddressPrefix: '*'
          access: 'Deny'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-107'
        properties: {
          priority: 107
          protocol: 'Tcp'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationPortRanges: [
            '23'
            '135'
            '445'
            '5985'
            '5986'
          ]
          destinationAddressPrefix: '*'
          access: 'Deny'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-108'
        properties: {
          priority: 108
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationPortRanges: [
            '13'
            '17'
            '19'
            '53'
            '69'
            '111'
            '123'
            '512'
            '514'
            '593'
            '873'
            '1900'
            '5353'
            '11211'
          ]
          destinationAddressPrefix: '*'
          access: 'Deny'
          direction: 'Inbound'
        }
      }
      {
        name: 'NRMS-Rule-109'
        properties: {
          priority: 109
          protocol: '*'
          sourcePortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationPortRanges: [
            '119'
            '137'
            '138'
            '139'
            '161'
            '162'
            '389'
            '636'
            '2049'
            '2301'
            '2381'
            '3268'
            '5800'
            '5900'
          ]
          destinationAddressPrefix: '*'
          access: 'Deny'
          direction: 'Inbound'
        }
      }
      {
        name: 'AppGatewayRule'
        properties: {
          priority: 119
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '65200-65535'
          sourceAddressPrefix: 'GatewayManager'
          destinationAddressPrefix: '*'
          access: 'Allow'
          direction: 'Inbound'
        }
      }
    ]
  }
}

output networkSecurityGroupId string = networkSecurityGroup.id
