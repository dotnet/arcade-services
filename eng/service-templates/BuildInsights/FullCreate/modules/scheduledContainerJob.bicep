param environmentName string
param applicationInsightsConnectionString string
param jobName string
param location string
param userAssignedIdentityId string
param cronSchedule string
param containerRegistryName string
param containerAppsEnvironmentId string
param command string
param deploymentIdentityPrincipalId string

module roles './roles.bicep' = {
  name: 'roles'
}

module shared './shared.bicep' = {
  name: 'shared'
}

var env = [
  {
    name: 'DOTNET_ENVIRONMENT'
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
          image: shared.outputs.containerDefaultImageName
          name: 'job'
          env: env
          command: [
            '/bin/sh'
            '-c'
            command
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

resource deploymentSubscriptionTriggerContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containerJob
  name: guid(subscription().id, resourceGroup().id, '${jobName}-contributor')
  properties: {
    roleDefinitionId: roles.outputs.contributorRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityPrincipalId
  }
}
