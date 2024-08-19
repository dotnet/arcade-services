resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: 'product-construction-service-ai-int'
}

resource subscriptionTriggererIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'SubscriptionTriggererInt'
}

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'ProductConstructionServiceDeploymentInt'
}
var contributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-04-01-preview' existing = {name:'product-construction-service-env-int'}

module ctr 'scheduledContainerJob.bicep' = {
  name: 'scheduledContainerJob'
  params: {
    aspnetcoreEnvironment: 'string'
    applicationInsightsConnectionString: appInsights.properties.ConnectionString
    jobName: 'longest-path-updater-job-int'
    location: 'westus2'
    userAssignedIdentityId: subscriptionTriggererIdentity.id
    cronSchedule: '0 0 * * *'
    containerRegistryName: 'productconstructionint'
    containerAppsEnvironmentId: containerAppsEnvironment.id
    containerImageName:'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
    dllFullPath: '/app/LongestBuildPathUpdater/LongestBuildPathUpdater.dll'
    argument: ''
    contributorRoleId: contributorRole
    deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
}
