param appGwName string = 'product-construction-service-agw-int'
param location string = 'westus2'
param kvName string = 'ProductConstructionInt'
param appGwIdentityName string = 'AppGwIdentityInt'
param certificateName string = 'maestro-int-ag'
// Certificate Secret identifier, without the last part (the version)
param certificateSecretIdShort string = 'https://productconstructionint.vault.azure.net/secrets/maestro-int-ag'
param virtualNetworkName string = 'product-construction-service-vnet-int'
param appGwVirtualNetworkSubnetName string = 'AppGateway'
param nsgName string = 'product-construction-service-nsg-int'
param publicIpAddressName string = 'product-construction-service-public-ip-int'
param frontendIpName string = 'frontendIp'
param httpPortName string = 'httpPort'
param httpsPortName string = 'httpsPort'
param pcsPool string = 'pcs'
param containerAppName string = 'product-construction-int'
param backendHttpSettingName string = 'backendHttpSetting'
param backendHttpsSettingName string = 'backendHttpsSetting'
param pcs80listener string = 'pcs-listener-80'
param pcs443listener string = 'pcs-listener-443'
param pcsRedirection string = 'pcs-redirection'
param pcs80rule string = 'pcs-rule-80'
param pcs443rule string = 'pcs-rule-443'
param pcsSubnetName string = 'product-construction-service-subnet'
param privateLinkServiceName string = 'product-construction-service-pls-int'

resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' existing = {
  name: kvName
}

resource containerApp 'Microsoft.App/containerApps@2023-04-01-preview' existing = { name: containerAppName }

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-04-01' existing = { name: virtualNetworkName }

resource networkSecurityGroup 'Microsoft.Network/networkSecurityGroups@2023-11-01' existing = { name: nsgName }

resource containerEnvironment 'Microsoft.App/managedEnvironments@2023-04-01-preview' existing = {
  name: 'product-construction-service-env-int'
}

resource pcsSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' existing = {
  parent: virtualNetwork
  name: pcsSubnetName
}

resource appGatewaySubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' existing = {name: appGwVirtualNetworkSubnetName}
// subnet for the product application gateway
/*resource appGatewaySubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' = {
  name: appGwVirtualNetworkSubnetName 
  parent: virtualNetwork
  properties: {
      addressPrefix: '10.0.2.0/24'
      networkSecurityGroup: {
          id: networkSecurityGroup.id
      }
      privateLinkServiceNetworkPolicies: 'Disabled'
  }
}*/

resource publicIpAddress 'Microsoft.Network/publicIPAddresses@2022-09-01' existing = {
  name: publicIpAddressName
}

resource appGwIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: appGwIdentityName
}

/*
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
}*/

/*module privateDnsZone 'private-dns-zone.bicep' = {
  name: 'privateDnsZone'
  params: {
    privateDnsZoneName: containerEnvironment.properties.defaultDomain
    containerEnvStaticIp: containerEnvironment.properties.staticIp
    virtualNetworkId: virtualNetwork.id
  }
}*/

// resource applicationGateway 'Microsoft.Network/applicationGateways@2023-04-01' = {
//   name: appGwName
//   location: location
//   identity: {
//     type: 'UserAssigned'
//     userAssignedIdentities: {
//       '${appGwIdentity.id}' : {}
//     }
//   }
//   properties: {
//     sku: {
//       name: 'Standard_v2'
//       tier: 'Standard_v2'
//       capacity: 10
//     }
//     sslCertificates: [
//       {
//         name: certificateName
//         properties: {
//           keyVaultSecretId: certificateSecretIdShort
//         }
//       }
//     ]
//     gatewayIPConfigurations: [
//       {
//         name: 'appGwIpConfigurationName'
//         properties: {
//           subnet: {
//             id: appGatewaySubnet.id
//           }
//         }
//       }
//     ]
//     frontendIPConfigurations: [
//       {
//         name: frontendIpName
//         properties: {
//           publicIPAddress: {
//             id: publicIpAddress.id
//           }
//         }
//       }
//     ]
//     privateLinkConfigurations: [
//       {
//         name: 'privateLinkConfiguration'
//         properties: {
//           ipConfigurations: [
//             {
//               name: 'privateLinkIpConfiguration'
//               properties: {
//                 subnet: {
//                   id: appGatewaySubnet.id
//                 }
//                 primary: true
//                 privateIPAllocationMethod: 'Dynamic'
//               }
//             }
//           ]
//         }
//       }
//     ]
//     frontendPorts: [
//       {
//         name: httpPortName
//         properties: {
//           port: 80
//         }
//       }
//       {
//         name: httpsPortName
//         properties: {
//           port: 443
//         }
//       }
//     ]
//     backendAddressPools: [
//       {
//         name: pcsPool
//         properties: {
//           backendAddresses: [
//             {
//               fqdn: containerApp.properties.configuration.ingress.fqdn
//             }
//           ]
//         }
//       }
//     ]
//     backendHttpSettingsCollection: [
//       {
//         name: backendHttpsSettingName
//         properties: {
//           port: 443
//           protocol: 'Https'
//           cookieBasedAffinity: 'Disabled'
//           pickHostNameFromBackendAddress: true
//           requestTimeout: 60
//         }
//       }
//       {
//         name: backendHttpSettingName
//         properties: {
//           port: 80
//           protocol: 'Http'
//           cookieBasedAffinity: 'Disabled'
//           pickHostNameFromBackendAddress: true
//           requestTimeout: 60
//         }
//       }
//     ]
//     httpListeners: [
//       {
//         name: pcs80listener
//         properties: {
//           frontendIPConfiguration: {
//             id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, frontendIpName)
//           }
//           frontendPort: {
//             id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, httpPortName)
//           }
//           protocol: 'Http'
//         }
//       }
//       {
//         name: pcs443listener
//         properties: {
//           frontendIPConfiguration: {
//             id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, frontendIpName)
//           }
//           frontendPort: {
//             id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, httpsPortName)
//           }
//           protocol: 'Https'
//           sslCertificate: {
//             id: resourceId('Microsoft.Network/applicationGateways/sslCertificates', appGwName, certificateName)
//           }
//         }
//       }
//     ]
//     redirectConfigurations: [
//       {
//         name: pcsRedirection
//         properties: {
//           redirectType: 'Permanent'
//           targetListener: {
//             id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, pcs443listener)
//           }
//           includePath: true
//           includeQueryString: true
//         }
//       }
//     ]
//     requestRoutingRules: [
//       {
//         name: pcs80rule
//         properties: {
//           priority: 1
//           httpListener: {
//             id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, pcs80listener)
//           }
//           redirectConfiguration: {
//             id: resourceId('Microsoft.Network/applicationGateways/redirectConfigurations', appGwName, pcsRedirection)
//           }
//         }
//       }
//       {
//         name: pcs443rule
//         properties: {
//           priority: 2
//           httpListener: {
//             id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, pcs443listener)
//           }
//           backendAddressPool: {
//             id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, pcsPool)
//           }
//           backendHttpSettings: {
//             id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, backendHttpsSettingName)
//           }
//         }
//       }
//     ]
//   }
// }

resource applicationGateway 'Microsoft.Network/applicationGateways@2023-04-01' existing = {
  name: appGwName
}

resource privateLinkService 'Microsoft.Network/privateLinkServices@2023-11-01' = {
  name: privateLinkServiceName
  location: location
  properties: {
    loadBalancerFrontendIpConfigurations: [
      { 
        id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', applicationGateway.name, frontendIpName)
      }     
    ]
    ipConfigurations: [
      {
        name: 'my-agw-private-link-config'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          privateIPAddressVersion: 'IPv4'
          primary: true
          subnet: {
            id: pcsSubnet.id
          }
        }
      }
    ]
  }
  dependsOn: [
    applicationGateway
  ]
}
