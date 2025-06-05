param location string
param virtualNetworkName string
param nsgName string
param productConstructionServiceSubnetName string
param logAnalyticsName string
param containerEnvironmentName string
param infrastructureResourceGroupName string
param applicationInsightsName string
param containerRegistryName string
param containerImageName string
param containerCpuCoreCount string
param containerMemory string
param aspnetcoreEnvironment string
param productConstructionServiceName string
param pcsIdentityName string
param deploymentIdentityName string
param subscriptionTriggererIdentityName string
param feedCleanerIdentityName string
param subscriptionTriggererTwiceDailyJobName string
param subscriptionTriggererDailyJobName string
param subscriptionTriggererWeeklyJobName string
param subscriptionTriggererEveryTwoWeeksJobName string
param subscriptionTriggererEveryMonthJobName string
param feedCleanerJobName string
param replicaNumber int

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {name:applicationInsightsName}

resource pcsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: pcsIdentityName
}

var contributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
var containerAppsManagedEnvironmentsContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '57cc5028-e6a7-4284-868d-0611c5923f8d')

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: deploymentIdentityName
}

resource subscriptionTriggererIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: subscriptionTriggererIdentityName
}

resource feedCleanerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: feedCleanerIdentityName
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' existing = {
  name: nsgName
}

module virtualNetwork '../FullCreate/virtual-network.bicep' = {
  name: 'virtualNetwork'
  params: {
    virtualNetworkName: virtualNetworkName
    location: location
    productConstructionServiceSubnetName: productConstructionServiceSubnetName
    networkSecurityGroupId: nsg.id
  }
}

module containerEnvironment './container-environment-recreate.bicep' = {
  name: 'containerEnvironment'
  params: {
    location: location
    logAnalyticsName: logAnalyticsName
    containerEnvironmentName: containerEnvironmentName
    productConstructionServiceSubnetId: virtualNetwork.outputs.productConstructionServiceSubnetId
    infrastructureResourceGroupName: infrastructureResourceGroupName
    containerAppsManagedEnvironmentsContributor: containerAppsManagedEnvironmentsContributor
    deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
}

module pcs '../FullCreate/container-app.bicep' = {
  name: 'pcs'
  params: {
    location: location
    containerRegistryName: containerRegistryName
    containerImageName: containerImageName
    containerCpuCoreCount: containerCpuCoreCount
    containerMemory: containerMemory
    aspnetcoreEnvironment: aspnetcoreEnvironment
    productConstructionServiceName: productConstructionServiceName
    applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
    pcsIdentityId: pcsIdentity.id
    containerEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
    contributorRoleId: contributorRole
    deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
    replicaNumber: replicaNumber
  }
}

module subscriptionTriggererTwiceDaily '../FullCreate/scheduledContainerJob.bicep' = {
  name: 'subscriptionTriggererTwiceDaily'
  params: {
      jobName: subscriptionTriggererTwiceDailyJobName
      location: location
      aspnetcoreEnvironment: aspnetcoreEnvironment
      applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
      userAssignedIdentityId: subscriptionTriggererIdentity.id
      cronSchedule: '0 5,19 * * *'
      containerRegistryName: containerRegistryName
      containerAppsEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
      containerImageName: containerImageName
      command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll twicedaily'
      contributorRoleId: contributorRole
      deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
}

module subscriptionTriggererDaily '../FullCreate/scheduledContainerJob.bicep' = {
  name: 'subscriptionTriggererDaily'
  params: {
      jobName: subscriptionTriggererDailyJobName
      location: location
      aspnetcoreEnvironment: aspnetcoreEnvironment
      applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
      userAssignedIdentityId: subscriptionTriggererIdentity.id
      cronSchedule: '0 2 * * *'
      containerRegistryName: containerRegistryName
      containerAppsEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
      containerImageName: containerImageName
      command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll daily'
      contributorRoleId: contributorRole
      deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
  dependsOn: [
      subscriptionTriggererTwiceDaily
  ]
}

module subscriptionTriggererWeekly '../FullCreate/scheduledContainerJob.bicep' = {
  name: 'subscriptionTriggererWeekly'
  params: {
      jobName: subscriptionTriggererWeeklyJobName
      location: location
      aspnetcoreEnvironment: aspnetcoreEnvironment
      applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
      userAssignedIdentityId: subscriptionTriggererIdentity.id
      cronSchedule: '0 4 * * MON'
      containerRegistryName: containerRegistryName
      containerAppsEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
      containerImageName: containerImageName
      command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll weekly'
      contributorRoleId: contributorRole
      deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
  dependsOn: [
      subscriptionTriggererDaily
  ]
}

module subscriptionTriggererEveryTwoWeeks '../FullCreate/scheduledContainerJob.bicep' = {
  name: 'subscriptionTriggererEveryTwoWeeks'
  params: {
    jobName: subscriptionTriggererEveryTwoWeeksJobName
    location: location
    aspnetcoreEnvironment: aspnetcoreEnvironment
    applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
    userAssignedIdentityId: subscriptionTriggererIdentity.id
    cronSchedule: '0 5 1,14 * *'
    containerRegistryName: containerRegistryName
    containerAppsEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
    containerImageName: containerImageName
    command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll everytwoweeks'
    contributorRoleId: contributorRole
    deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
  dependsOn: [
    subscriptionTriggererWeekly
  ]
}

module subscriptionTriggererEveryMonth '../FullCreate/scheduledContainerJob.bicep' = {
  name: 'subscriptionTriggererEveryMonth'
  params: {
    jobName: subscriptionTriggererEveryMonthJobName
    location: location
    aspnetcoreEnvironment: aspnetcoreEnvironment
    applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
    userAssignedIdentityId: subscriptionTriggererIdentity.id
    cronSchedule: '0 5 1 * *'
    containerRegistryName: containerRegistryName
    containerAppsEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
    containerImageName: containerImageName
    command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll everymonth'
    contributorRoleId: contributorRole
    deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
  dependsOn: [
    subscriptionTriggererEveryTwoWeeks
  ]
}

module feedCleaner '../FullCreate/scheduledContainerJob.bicep' = {
  name: 'feedCleaner'
  params: {
      jobName: feedCleanerJobName
      location: location
      aspnetcoreEnvironment: aspnetcoreEnvironment
      applicationInsightsConnectionString: applicationInsights.properties.ConnectionString
      userAssignedIdentityId: feedCleanerIdentity.id
      cronSchedule: '0 2 * * *'
      containerRegistryName: containerRegistryName
      containerAppsEnvironmentId: containerEnvironment.outputs.containerEnvironmentId
      containerImageName: containerImageName
      command: 'cd /app/FeedCleaner && dotnet ProductConstructionService.FeedCleaner.dll'
      contributorRoleId: contributorRole
      deploymentIdentityPrincipalId: deploymentIdentity.properties.principalId
  }
  dependsOn: [
      subscriptionTriggererEveryMonth
  ]
}
