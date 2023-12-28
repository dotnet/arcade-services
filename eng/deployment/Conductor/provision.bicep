@minLength(1)
@maxLength(64)
@description('Name of the resource group that will contain all the resources')
param resourceGroupName string = 'dkurepa-containers2'

@minLength(1)
@description('Primary location for all resources')
param location string = 'northeurope'

@minLength(5)
@maxLength(50)
@description('Name of the Azure Container Registry resource into which container images will be published')
param containerRegistryName string = 'dkurepaacr'

@minLength(1)
@maxLength(64)
@description('Name of the identity used by the apps to access Azure Container Registry')
param identityName string = 'dkurepa-c'

@description('CPU cores allocated to a single container instance, e.g., 0.5')
param containerCpuCoreCount string = '0.25'

@description('Memory allocated to a single container instance, e.g., 1Gi')
param containerMemory string = '0.5Gi'

var resourceToken = toLower(uniqueString(subscription().id, resourceGroupName, location))
var helloWorldContainerImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

// common environment variables used by each of the apps
var env = [
    {
        name: 'ASPNETCORE_ENVIRONMENT'
        value: 'Development'
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
]

// log analytics
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-12-01-preview' = {
  name: 'logs${resourceToken}'
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
  name: 'acae${resourceToken}'
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

// apiservice - the app's back-end
resource apiservice 'Microsoft.App/containerApps@2023-04-01-preview' = {
  name: 'apiservice'
  location: location
  identity: {
      type: 'UserAssigned'
      userAssignedIdentities: { '${identity.id}' : {}}
  }
  properties: {
      managedEnvironmentId: containerAppsEnvironment.id
      configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
          external: true
          targetPort: 80
          transport: 'http'
      }
      dapr: { enabled: false }
      registries: [ {
          server: '${containerRegistryName}.azurecr.io'
          identity: identity.id
          } ]
      }
      template: {
          scale: {
              minReplicas: 1
              maxReplicas: 1
          }
          serviceBinds: []
          containers: [ {
              image: helloWorldContainerImage
              name: 'apiservice'
              env: env
              resources: {
                  cpu: json(containerCpuCoreCount)
                  memory: containerMemory
              }
          } ]
      }
  }
}
