param environmentName string
param jobName string
param location string
param userAssignedIdentityId string
param cronSchedule string
param containerRegistryName string
param containerAppsEnvironmentId string
param command string
param deploymentIdentityPrincipalId string

@description('Shared environment variables (logging, App Insights, resource connection strings, managed identity)')
param sharedEnvVars array

var contributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b24988ac-6180-42a0-ab88-20f7382dd24c'
)

module shared './shared.bicep' = {
  name: 'shared'
}

// environment variables specific to this scheduled job
var jobSpecificEnv = [
  {
    name: 'APP_ROLE'
    value: jobName
  }
  {
    name: 'DOTNET_ENVIRONMENT'
    value: environmentName
  }
]

// combined environment variables: job-specific + shared
var env = concat(jobSpecificEnv, sharedEnvVars)

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
    roleDefinitionId: contributorRole
    principalType: 'ServicePrincipal'
    principalId: deploymentIdentityPrincipalId
  }
}
