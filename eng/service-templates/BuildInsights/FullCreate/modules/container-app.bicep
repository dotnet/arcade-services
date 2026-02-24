param location string
param containerRegistryName string
param containerCpuCoreCount string
param containerMemory string
param environmentName string
param serviceName string
param applicationInsightsConnectionString string
param appIdentityId string
param containerEnvironmentId string
param deploymentIdentityPrincipalId string
param containerReplicas int

var contributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b24988ac-6180-42a0-ab88-20f7382dd24c'
)

module shared './shared.bicep' = {
  name: 'shared'
}

// common environment variables used by the app
var containerAppEnv = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: environmentName
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
    value: applicationInsightsConnectionString
  }
  {
    name: 'VmrPath'
    value: '/mnt/datadir/vmr'
  }
  {
    name: 'TmpPath'
    value: '/mnt/datadir/tmp'
  }
  {
    name: 'AZURE_TOKEN_CREDENTIALS'
    value: 'prod'
  }
]

// container app hosting the Product Construction Service
resource containerApp 'Microsoft.App/containerApps@2023-04-01-preview' = {
  name: serviceName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${appIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnvironmentId
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
          identity: appIdentityId
        }
      ]
    }
    template: {
      scale: {
        minReplicas: containerReplicas
        maxReplicas: containerReplicas
      }
      serviceBinds: []
      containers: [
        {
          image: shared.outputs.containerDefaultImageName
          name: 'api'
          env: containerAppEnv
          resources: {
            cpu: json(containerCpuCoreCount)
            memory: containerMemory
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
}

// Give the Deployment MI the Contributor role in the containerapp to allow it to deploy
resource deploymentSubscriptionTriggerContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerApp
  name: guid(subscription().id, resourceGroup().id, '${serviceName}-contributor')
  properties: {
    roleDefinitionId: contributorRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityPrincipalId
  }
}
