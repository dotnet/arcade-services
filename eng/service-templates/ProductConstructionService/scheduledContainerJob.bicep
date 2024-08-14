param aspnetcoreEnvironment string
param applicationInsightsConnectionString string
param jobName string
param location string
param userAssignedIdentityId string
param cronSchedule string
param containerRegistryName string
param containerAppsEnvironmentId string
param containerImageName string
param dllName string
param argument string

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
      value: applicationInsightsConnectionString
  }
]

resource containerJob 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  identity: {
      type: 'UserAssigned'
      userAssignedIdentities: {
          '${userAssignedIdentityId}': {}
      }
  }
  properties: {
      configuration: {
          eventTriggerConfig: null
          manualTriggerConfig: null
          scheduleTriggerConfig: {
          cronExpression: cronSchedule
          parallelism: 1
          replicaCompletionCount: 1
          }
          triggerType: 'Schedule'
          registries: [
              {
                  identity: userAssignedIdentityId
                  server: '${containerRegistryName}.azurecr.io'
              }
          ]
          replicaRetryLimit: 1
          replicaTimeout: 300
      }
      environmentId: containerAppsEnvironmentId
      template: {
          containers: [
              {
                  image: containerImageName
                  name: 'job'
                  env: env
                  command: [
                      'dotnet'
                      dllName
                  ]
                  args: [
                      argument
                  ]
                  resources: {
                      cpu: json('1.0')
                      memory: '2Gi'
                  }
              }
          ]
      }
  }
}
