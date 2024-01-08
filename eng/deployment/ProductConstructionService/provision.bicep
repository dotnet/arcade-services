@minLength(1)
@description('Primary location for all resources')
param location string = 'northcentralusstage'

@minLength(5)
@maxLength(50)
@description('Name of the Azure Container Registry resource into which container images will be published')
param containerRegistryName string = 'productconstructionint'

@minLength(1)
@maxLength(64)
@description('Name of the identity used by the apps to access Azure Container Registry')
param identityName string = 'ProductConstructionServiceIntIdentity'

@description('CPU cores allocated to a single container instance, e.g., 0.5')
param containerCpuCoreCount string = '0.25'

@description('Memory allocated to a single container instance, e.g., 1Gi')
param containerMemory string = '0.5Gi'

@description('aspnetcore environment')
@allowed([
    'Development'
    'Staging'
    'Production'
])
param aspnetcoreEnvironment string = 'Staging'

@description('Name of the application insights resource')
param applicationInsightsName string = 'product-construction-service-int-ai'

@description('Key Vault name')
param keyVaultName string = 'ProductConstructionInt'

@description('Log analytics workspace name')
param logAnalyticsName string = 'product-construction-service-int-workspace'

@description('Name of the container apps environment')
param containerAppsEnvironmentName string = 'product-construction-service-int-env'

@description('Product construction service API name')
param productConstructionServiceApiName string = 'product-construction-int-api'

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
  }
}

// the container registry
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2022-02-01-preview' = {
  name: containerRegistryName
  location: location
  sku: {
      name: 'Basic'
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
  }
}

// identity for the container apps
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

var principalId = identity.properties.principalId

// azure system role for setting up acr pull access
var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

// allow acr pulls to the identity used for the aca's
resource aksAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerRegistry // Use when specifying a scope that is different than the deployment scope
  name: guid(subscription().id, resourceGroup().id, acrPullRole)
  properties: {
      roleDefinitionId: acrPullRole
      principalType: 'ServicePrincipal'
      principalId: principalId
  }
}

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
]

// apiservice - the app's back-end
resource apiservice 'Microsoft.App/containerApps@2023-04-01-preview' = {
  name: productConstructionServiceApiName
  location: location
  identity: {
      type: 'UserAssigned'
      userAssignedIdentities: { '${identity.id}' : {}}
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
                identity: identity.id
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
                name: 'apiservice'
                env: env
                resources: {
                    cpu: json(containerCpuCoreCount)
                    memory: containerMemory
                }
                probes: [
                    {
                        httpGet: {
                            path: '/status/startup'
                            port: 8080
                            scheme: 'HTTP'
                        }
                        initialDelaySeconds: 5
                        periodSeconds: 10
                        successThreshold: 1
                        failureThreshold: 3
                        type: 'Startup'
                    }
                ]
            } 
        ]
      }
  }
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
