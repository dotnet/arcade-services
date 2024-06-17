@minLength(1)
@description('Primary location for all resources')
param location string = 'westus2'

@minLength(5)
@maxLength(50)
@description('Name of the Azure Container Registry resource into which container images will be published')
param containerRegistryName string = 'productconstructionint'

@description('CPU cores allocated to a single container instance')
param containerCpuCoreCount string = '1.0'

@description('Memory allocated to a single container instance')
param containerMemory string = '2Gi'

@description('aspnetcore environment')
@allowed([
    'Development'
    'Staging'
    'Production'
])
param aspnetcoreEnvironment string = 'Staging'

@description('Name of the application insights resource')
param applicationInsightsName string = 'product-construction-service-ai-int'

@description('Key Vault name')
param keyVaultName string = 'ProductConstructionInt'

@description('Dev Key Vault name')
param devKeyVaultName string = 'ProductConstructionDev'

@description('Log analytics workspace name')
param logAnalyticsName string = 'product-construction-service-workspace-int'

@description('Name of the container apps environment')
param containerAppsEnvironmentName string = 'product-construction-service-env-int'

@description('Product construction service API name')
param productConstructionServiceName string = 'product-construction-int'

@description('Storage account name')
param storageAccountName string = 'productconstructionint'

@description('Name of the MI used for the PCS container app')
param pcsIdentityName string = 'ProductConstructionServiceInt'

@description('Name of the identity used for the PCS deployment')
param deploymentIdentityName string = 'ProductConstructionServiceDeploymentInt'

@description('Bicep requires an image when creating a containerapp. Using a dummy image for that.')
var containerImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Virtual network name')
param virtualNetworkName string = 'product-construction-service-vnet-int'

@description('Product construction service subnet name')
param productConstructionServiceSubnetName string = 'product-construction-service-subnet'

@description('Name of the subnet used for private endpoints')
param privateEndpointsSubnetName string = 'private-endpoints-subnet'

@description('Storage account queue private endpoint name')
param storageAccountQueuePrivateEndpointName string = 'pcs-storage-account-queue-private-endpoint'

@description('Storage account network interface name')
param storageAccountQueueNetworkInterfaceName string = 'pcs-storage-account-network-interface'

@description('Private Dns Zone name')
param queuePrivateDnsZoneName string = 'privatelink.queue.core.windows.net'

@description('Virtual Network Link name')
param queueVirtualNetworkLinkName string = 'queue-vnet-link'

@description('Private Dns Zone Group name')
param queuePrivateDnsZoneGroupName string = 'pcs-storage-account-queue-private-endpoint-group'

@description('Build Asset Registry subscription id')
var buildAssetRegistrySubscriptionId = 'cab65fc3-d077-467d-931f-3932eabf36d3'

@description('Build Asset Registry resource group name')
var buildAssetRegistryResourceGroupName = 'maestro'

@description('BAR Sql server name')
var buildAssetRegistryServerName = 'maestro-int-server'

@description('Build Asset Registry private endpoint name')
var buildAssetRegistryPrivateEndpointName = 'pcs-bar-private-endpoint'

@description('Build Asset Registry Network Interface name')
var buildAssetRegistryNetworkInterfaceName = 'pcs-bar-network-interface'

@description('Private Dns Zone name')
param barPrivateDnsZoneName string = 'privatelink.database.windows.net'

@description('Virtual Network Link name')
param barVirtualNetworkLinkName string = 'bar-vnet-link'

@description('Private Dns Zone Group name')
param barPrivateDnsZoneGroupName string = 'pcs-bar-private-endpoint-group'

@description('Network security group name')
var networkSecurityGroupName = 'product-construction-service-nsg-int'

@description('Resource group where PCS IP resources will be created')
var infrastructureResourceGroupName = 'product-construction-service-ip-int'

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
        ]
    }
}

// log analytics
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-12-01-preview' = {
    name: logAnalyticsName
    location: location
    properties: any({
        retentionInDays: 30
        features: {
            searchVersion: 1
        }
        sku: {
            name: 'PerGB2018'
        }
    })
}

// virtual network
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
}

// subnet for the product construction service
resource productConstructionServiceSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' = {
    name: productConstructionServiceSubnetName
    parent: virtualNetwork
    properties: {
        addressPrefix: '10.0.0.0/24'
        delegations: [
            {
                name: 'Microsoft.App/environments'
                properties: {
                    serviceName: 'Microsoft.App/environments'
                }
            }
        ]
        networkSecurityGroup: {
            id: networkSecurityGroup.id
        }
    }
}

// subnet for the storage account private endpoints
resource privateEndpointsSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' = {
    name: privateEndpointsSubnetName
    parent: virtualNetwork
    properties: {
        addressPrefix: '10.0.1.0/24'
        networkSecurityGroup: {
            id: networkSecurityGroup.id
        }
    }
}

// the container apps environment
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-04-01-preview' = {
  name: containerAppsEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
        destination: 'log-analytics'
        logAnalyticsConfiguration: {
            customerId: logAnalytics.properties.customerId
            sharedKey: logAnalytics.listKeys().primarySharedKey
        }
    }
    workloadProfiles: [
        {
            name: 'Consumption'
            workloadProfileType: 'Consumption'
        }
    ]
    vnetConfiguration: {
        infrastructureSubnetId: productConstructionServiceSubnet.id
    }
    infrastructureResourceGroup: infrastructureResourceGroupName
  }
}

// the container registry
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2022-02-01-preview' = {
    name: containerRegistryName
    location: location
    sku: {
        name: 'Premium'
    }
    properties: {
        adminUserEnabled: true
        anonymousPullEnabled: false
        dataEndpointEnabled: false
        encryption: {
            status: 'disabled'
        }
        networkRuleBypassOptions: 'AzureServices'
        publicNetworkAccess: 'Enabled'
        zoneRedundancy: 'Disabled'
        policies: {
            retentionPolicy: {
                days: 60
                status: 'enabled'
            }
        }
    }
}


resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deploymentIdentityName
  location: location
}

resource pcsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: pcsIdentityName
  location: location
}

// allow acr pulls to the identity used for the aca's
resource aksAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    scope: containerRegistry // Use when specifying a scope that is different than the deployment scope
    name: guid(subscription().id, resourceGroup().id, acrPullRole)
    properties: {
        roleDefinitionId: acrPullRole
        principalType: 'ServicePrincipal'
        principalId: pcsIdentity.properties.principalId
    }
}

// azure system role for setting up acr pull access
var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
// azure system role for setting secret access
var kvSecretUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
// azure system role for setting storage queue access
var storageQueueContrubutorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
// azure system role for setting controbutor access
var contributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
// azure system role Key Vault Reader
var keyVaultReaderRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '21090545-7ca7-4776-b22c-e363652d74d2')

// application insights for service logging
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
    name: applicationInsightsName
    location: location
    kind: 'web'
    properties: {
        Application_Type: 'web'
        publicNetworkAccessForIngestion: 'Enabled'
        publicNetworkAccessForQuery: 'Enabled'
        RetentionInDays: 120
        WorkspaceResourceId: logAnalytics.id
    }
}

// common environment variables used by each of the apps
var env = [
    {
        name: 'ASPNETCORE_ENVIRONMENT'
        value: aspnetcoreEnvironment
    }
    {
        name: 'Logging__Console__FormatterName'
        value: 'simple'
    }
    {
        name: 'Logging__Console__FormatterOptions__SingleLine'
        value: 'true'
    }
    {
        name: 'Logging__Console__FormatterOptions__IncludeScopes'
        value: 'true'
    }
    {
        name: 'ASPNETCORE_LOGGING__CONSOLE__DISABLECOLORS'
        value: 'true'
    }
    {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: applicationInsights.properties.ConnectionString
    }
    {
        name: 'VmrPath'
        value: '/mnt/datadir/vmr'
    }
    {
        name: 'TmpPath'
        value: '/mnt/datadir/tmp'
    }
]

// container app hosting the Product Construction Service
resource containerapp 'Microsoft.App/containerApps@2023-04-01-preview' = {
    name: productConstructionServiceName
    location: location
    identity: {
        type: 'UserAssigned'
        userAssignedIdentities: { '${pcsIdentity.id}' : {}}
    }
    properties: {
        managedEnvironmentId: containerAppsEnvironment.id
        configuration: {
            activeRevisionsMode: 'Multiple'
            maxInactiveRevisions: 5
            ingress: {
                external: true
                targetPort: 8080
                transport: 'http'
            }
            dapr: { enabled: false }
            registries: [
                {
                    server: '${containerRegistryName}.azurecr.io'
                    identity: pcsIdentity.id
                }
            ]
        }
        template: {
            scale: {
                minReplicas: 1
                maxReplicas: 1
            }
            serviceBinds: []
            containers: [ 
                {
                    image: containerImageName
                    name: 'api'
                    env: env
                    resources: {
                        cpu: json(containerCpuCoreCount)
                        memory: containerMemory
                        ephemeralStorage: '50Gi'
                    }
                    volumeMounts: [
                        {
                            volumeName: 'data'
                            mountPath: '/mnt/datadir'
                        }
                    ]
                    probes: [
                        {
                            httpGet: {
                                path: '/alive'
                                port: 8080
                                scheme: 'HTTP'
                            }
                            initialDelaySeconds: 5
                            periodSeconds: 10
                            successThreshold: 1
                            failureThreshold: 3
                            type: 'Startup'
                        }
                        {
                            httpGet: {
                                path: '/health'
                                port: 8080
                                scheme: 'HTTP'
                            }
                            initialDelaySeconds: 60
                            failureThreshold: 10
                            successThreshold: 1
                            periodSeconds: 30
                            type: 'Readiness'
                        }
                    ]
                } 
            ]
            volumes: [
                {
                    name: 'data'
                    storageType: 'EmptyDir'
                }
            ]
        }
    }
    dependsOn: [
        aksAcrPull
    ]
}

resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
    name: keyVaultName
    location: location
    properties: {
        sku: {
            name: 'standard'
            family: 'A'
        }
        tenantId: subscription().tenantId
        enableSoftDelete: true
        softDeleteRetentionInDays: 90
        accessPolicies: []
        enableRbacAuthorization: true
    }
}

resource containerRegistryUsernameSecret 'Microsoft.KeyVault/vaults/secrets@2022-07-01' = {
    parent: keyVault
    name: 'container-registry-username'
    properties: {
        value: containerRegistry.listCredentials().username
    }
}

resource containerRegistryPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2022-07-01' = {
    parent: keyVault
    name: 'container-registry-password'
    properties: {
        value: containerRegistry.listCredentials().passwords[0].value
    }
}

// If we're creating the staging environment, also create a dev key vault
resource devKeyVault 'Microsoft.KeyVault/vaults@2022-07-01' = if (aspnetcoreEnvironment == 'Staging') {
    name: devKeyVaultName
    location: location
    properties: {
        sku: {
            name: 'standard'
            family: 'A'
        }
        tenantId: subscription().tenantId
        enableSoftDelete: true
        softDeleteRetentionInDays: 90
        accessPolicies: []
        enableRbacAuthorization: true
    }
}

// allow secret access to the identity used for the aca's
resource secretAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault // Use when specifying a scope that is different than the deployment scope
  name: guid(subscription().id, resourceGroup().id, kvSecretUserRole)
  properties: {
      roleDefinitionId: kvSecretUserRole
      principalType: 'ServicePrincipal'
      principalId: pcsIdentity.properties.principalId
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
    name: storageAccountName
    location: location
    kind: 'StorageV2'
    sku: {
        name: 'Standard_LRS'
    }
    properties: {
        allowBlobPublicAccess: false
        publicNetworkAccess: 'Enabled'
        allowSharedKeyAccess: false
        networkAcls: {
            defaultAction: 'Deny'
        }
    }
}

resource storageAccountQueueService 'Microsoft.Storage/storageAccounts/queueServices@2022-09-01' = {
    name: 'default'
    parent: storageAccount
}

resource storageAccountQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2022-09-01' = {
    name: 'pcs-jobs'
    parent: storageAccountQueueService
}

// allow storage queue access to the identity used for the aca's
resource storageQueueAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount // Use when specifying a scope that is different than the deployment scope
  name: guid(subscription().id, resourceGroup().id, storageQueueContrubutorRole)
  properties: {
      roleDefinitionId: storageQueueContrubutorRole
      principalType: 'ServicePrincipal'
      principalId: pcsIdentity.properties.principalId
  }
}

resource storageAccountQueuePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
    name: storageAccountQueuePrivateEndpointName
    location: location
    properties: {
        privateLinkServiceConnections: [
            {
                name: 'storage-account-queue-endpoint'
                properties: {
                    groupIds: [
                        'queue'
                    ]
                    privateLinkServiceId: storageAccount.id
                }
            }
        ]
        subnet: {
            id: privateEndpointsSubnet.id
        }
        customNetworkInterfaceName: storageAccountQueueNetworkInterfaceName
    }
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
  
resource queuePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
    name: queuePrivateDnsZoneGroupName
    parent: storageAccountQueuePrivateEndpoint
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

resource buildAssetRegistry 'Microsoft.Sql/servers@2023-05-01-preview' existing = {
    name: buildAssetRegistryServerName
    scope: resourceGroup(buildAssetRegistrySubscriptionId, buildAssetRegistryResourceGroupName  )
}

resource buildAssetRegistryPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
    name: buildAssetRegistryPrivateEndpointName
    location: location
    properties: {
        privateLinkServiceConnections: [
            {
                name: 'pcs-private-endpoint'
                properties: {
                    groupIds: [
                        'sqlServer'
                    ]
                    privateLinkServiceId: buildAssetRegistry.id
                }
            }
        ]
        subnet: {
            id: privateEndpointsSubnet.id
        }
        customNetworkInterfaceName: buildAssetRegistryNetworkInterfaceName
    }
    // Wait for the previous endpoint to be created before creating this one
    dependsOn: [
        storageAccountQueuePrivateEndpoint
    ]
}

resource barPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
    name: barPrivateDnsZoneName
    location: 'global'
}
  
resource barVirtualNetworkLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
    name: barVirtualNetworkLinkName
    parent: barPrivateDnsZone
    location: 'global'
    properties: {
      virtualNetwork: {
        id: virtualNetwork.id
      }
      registrationEnabled: false
    }
}
  
resource barPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
    name: barPrivateDnsZoneGroupName
    parent: buildAssetRegistryPrivateEndpoint
    properties: {
      privateDnsZoneConfigs: [
        {
          name: barPrivateDnsZone.name
          properties: {
            privateDnsZoneId: barPrivateDnsZone.id
          }
        }
      ]
    }
}

// Give the PCS Deployment MI the Contributor role in the containerapp to allow it to deploy
resource deploymentContainerappContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    scope: containerapp // Use when specifying a scope that is different than the deployment scope
    name: guid(subscription().id, resourceGroup().id, contributorRole)
    properties: {
        roleDefinitionId: contributorRole
        principalType: 'ServicePrincipal'
        principalId: deploymentIdentity.properties.principalId
    }
}

// Give the PCS Deployment MI the Key Vault Reader role to be able to read secrets during the deployment
resource deploymentKeyVaultReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    scope: keyVault // Use when specifying a scope that is different than the deployment scope
    name: guid(subscription().id, resourceGroup().id, keyVaultReaderRole)
    properties: {
        roleDefinitionId: keyVaultReaderRole
        principalType: 'ServicePrincipal'
        principalId: deploymentIdentity.properties.principalId
    }
    dependsOn: [
        deploymentContainerappContributor
    ]
}

// Give the PCS Deploymeny MI the Key Vault User role to be able to read secrets during the deployment
resource deploymentKeyVaultUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    scope: keyVault // Use when specifying a scope that is different than the deployment scope
    name: guid(subscription().id, resourceGroup().id, 'deploymentKeyVaultUser')
    properties: {
        roleDefinitionId: kvSecretUserRole
        principalType: 'ServicePrincipal'
        principalId: deploymentIdentity.properties.principalId
    }
    dependsOn: [
        deploymentKeyVaultReader
    ]
}
