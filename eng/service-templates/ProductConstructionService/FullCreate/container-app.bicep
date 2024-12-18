param location string
param containerRegistryName string
param containerImageName string
param containerCpuCoreCount string
param containerMemory string
param aspnetcoreEnvironment string
param productConstructionServiceName string
param applicationInsightsConnectionString string
param pcsIdentityId string
param containerEnvironmentId string
param contributorRoleId string
param deploymentIdentityPrincipalId string
param replicaNumber int

// common environment variables used by the app
var containerAppEnv = [
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
]

// container app hosting the Product Construction Service
resource containerApp 'Microsoft.App/containerApps@2023-04-01-preview' = {
  name: productConstructionServiceName
  location: location
  identity: {
      type: 'UserAssigned'
      userAssignedIdentities: { '${pcsIdentityId}' : {}}
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
                  identity: pcsIdentityId
              }
          ]
      }
      template: {
          scale: {
              minReplicas: replicaNumber
              maxReplicas: replicaNumber
          }
          serviceBinds: []
          containers: [ 
              {
                  image: containerImageName
                  name: 'api'
                  env: containerAppEnv
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
}

// Give the PCS Deployment MI the Contributor role in the containerapp to allow it to deploy
resource deploymentSubscriptionTriggererContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerApp
  name: guid(subscription().id, resourceGroup().id, '${productConstructionServiceName}-contributor')
  properties: {
      roleDefinitionId: contributorRoleId
      principalType: 'ServicePrincipal'
      principalId: deploymentIdentityPrincipalId
  }
}
