@minLength(1)
@description('Primary location for all resources')
param location string

@minLength(5)
@maxLength(50)
@description('Name of the Azure Container Registry resource into which container images will be published')
param containerRegistryName string

@description('CPU cores allocated to a single container instance')
param containerCpuCoreCount string

@description('Memory allocated to a single container instance')
param containerMemory string

@description('aspnetcore environment')
@allowed([
    'Development'
    'Staging'
    'Production'
])
param aspnetcoreEnvironment string

@description('Name of the application insights resource')
param applicationInsightsName string

@description('Key Vault name')
param keyVaultName string

@description('Dev Key Vault name')
param devKeyVaultName string = ''

@description('Azure Cache for Redis name')
param azureCacheRedisName string

@description('Log analytics workspace name')
param logAnalyticsName string

@description('Name of the container apps environment')
param containerEnvironmentName string

@description('Product construction service API name')
param productConstructionServiceName string

@description('Storage account name')
param storageAccountName string

@description('Name of the MI used for the PCS container app')
param pcsIdentityName string

@description('Name of the identity used for the PCS deployment')
param deploymentIdentityName string

@description('Bicep requires an image when creating a containerapp. Using a dummy image for that.')
param containerImageName string

@description('Virtual network name')
param virtualNetworkName string

@description('Product construction service subnet name')
param productConstructionServiceSubnetName string

@description('Subscription Triggerer Identity name')
param subscriptionTriggererIdentityName string

@description('Subscription Triggerer Weekly Job name')
param subscriptionTriggererWeeklyJobName string

@description('Subscription Triggerer Twice Daily Job name')
param subscriptionTriggererTwiceDailyJobName string

@description('Subscription Triggerer Daily Job name')
param subscriptionTriggererDailyJobName string

@description('Longest Build Path Updater Identity Name')
param longestBuildPathUpdaterIdentityName string

@description('Longest Build Path Updater Job Name')
param longestBuildPathUpdaterJobName string

@description('Feed Cleaner Job name')
param feedCleanerJobName string

@description('Feed Cleaner Identity name')
param feedCleanerIdentityName string

@description('Network security group name')
param networkSecurityGroupName string

@description('Resource group where PCS IP resources will be created')
param infrastructureResourceGroupName string

// azure system role for setting up acr pull access
var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
// azure system role for granting push access
var acrPushRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
// azure system role for setting secret access
var kvSecretUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
// azure system role for setting storage queue access
var storageQueueContrubutorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
// azure system role for setting contributor access
var contributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
// storage account blob contributor
var blobContributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
// Key Vault Crypto User role
var kvCryptoUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
// Reader role
var readerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
// Container Apps ManagedEnvironments Contributor Role
var containerAppsManagedEnvironmentsContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '57cc5028-e6a7-4284-868d-0611c5923f8d')

module networkSecurityGroupModule 'nsg.bicep' = {
    name: 'networkSecurityGroupModule'
    params: {
        networkSecurityGroupName: networkSecurityGroupName
        location: location
    }
}

module virtualNetworkModule 'virtual-network.bicep' = {
    name: 'virtualNetworkModule'
    params: {
        location: location
        virtualNetworkName: virtualNetworkName
        networkSecurityGroupId: networkSecurityGroupModule.outputs.networkSecurityGroupId
        productConstructionServiceSubnetName: productConstructionServiceSubnetName
    }
}

module containerEnvironmentModule 'container-environment.bicep' = {
    name: 'containerEnvironmentModule'
    params: {
        location: location
        logAnalyticsName: logAnalyticsName
        containerEnvironmentName: containerEnvironmentName
        productConstructionServiceSubnetId: virtualNetworkModule.outputs.productConstructionServiceSubnetId
        infrastructureResourceGroupName: infrastructureResourceGroupName
        applicationInsightsName: applicationInsightsName
        containerAppsManagedEnvironmentsContributor: containerAppsManagedEnvironmentsContributor
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module managedIdentitiesModule 'managed-identities.bicep' = {
    name: 'managedIdentitiesModule'
    params: {
        location: location
        deploymentIdentityName: deploymentIdentityName
        pcsIdentityName: pcsIdentityName
        subscriptionTriggererIdentityName: subscriptionTriggererIdentityName
        longestBuildPathUpdaterIdentityName: longestBuildPathUpdaterIdentityName
        feedCleanerIdentityName: feedCleanerIdentityName
        contributorRole: contributorRole
    }
}

module containerRegistryModule 'container-registry.bicep' = {
    name: 'containerRegistryModule'
    params: {
        location: location
        containerRegistryName: containerRegistryName
        acrPullRole: acrPullRole
        pcsIdentityPrincipalId: managedIdentitiesModule.outputs.pcsIdentityPrincipalId
        subscriptionTriggererPricnipalId: managedIdentitiesModule.outputs.subscriptionTriggererIdentityPrincipalId
        longestBuildPathUpdaterIdentityPrincipalId: managedIdentitiesModule.outputs.longestBuildPathUpdaterIdentityPrincipalId
        feedCleanerIdentityPrincipalId: managedIdentitiesModule.outputs.feedCleanerIdentityPrincipalId
        acrPushRole: acrPushRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module containerAppModule 'container-app.bicep' = {
    name: 'containerAppModule'
    params: {
        location: location
        containerEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerImageName: containerImageName
        containerRegistryName: containerRegistryName
        containerCpuCoreCount: containerCpuCoreCount
        containerMemory: containerMemory
        productConstructionServiceName: productConstructionServiceName
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        aspnetcoreEnvironment: aspnetcoreEnvironment
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
        pcsIdentityId: managedIdentitiesModule.outputs.pcsIdentityId
    }
    dependsOn: [
        containerRegistryModule
    ]
}

module subscriptionTriggererTwiceDaily 'scheduledContainerJob.bicep' = {
    name: 'subscriptionTriggererTwiceDaily'
    params: {
        jobName: subscriptionTriggererTwiceDailyJobName
        location: location
        aspnetcoreEnvironment: aspnetcoreEnvironment
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.subscriptionTriggererIdentityId
        cronSchedule: '0 5,19 * * *'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerImageName: containerImageName
        command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll twicedaily'
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
    dependsOn: [
        containerRegistryModule
    ]
}

module subscriptionTriggererDaily 'scheduledContainerJob.bicep' = {
    name: 'subscriptionTriggererDaily'
    params: {
        jobName: subscriptionTriggererDailyJobName
        location: location
        aspnetcoreEnvironment: aspnetcoreEnvironment
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.subscriptionTriggererIdentityId
        cronSchedule: '0 5 * * *'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerImageName: containerImageName
        command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll daily'
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
    dependsOn: [
        subscriptionTriggererTwiceDaily
    ]
}

module subscriptionTriggererWeekly 'scheduledContainerJob.bicep' = {
    name: 'subscriptionTriggererWeekly'
    params: {
        jobName: subscriptionTriggererWeeklyJobName
        location: location
        aspnetcoreEnvironment: aspnetcoreEnvironment
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.subscriptionTriggererIdentityId
        cronSchedule: '0 5 * * MON'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerImageName: containerImageName
        command: 'cd /app/SubscriptionTriggerer && dotnet ProductConstructionService.SubscriptionTriggerer.dll weekly'
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
    dependsOn: [
        subscriptionTriggererDaily
    ]
}

module longestBuildPathUpdater 'scheduledContainerJob.bicep' = {
    name: 'longestBuildPathUpdater'
    params: {
        jobName: longestBuildPathUpdaterJobName
        location: location
        aspnetcoreEnvironment: aspnetcoreEnvironment
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.longestBuildPathUpdaterIdentityId
        cronSchedule: '0 5 * * MON'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerImageName: containerImageName
        command: 'cd /app/LongestBuildPathUpdater && dotnet ProductConstructionService.LongestBuildPathUpdater.dll'
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
    dependsOn: [
        subscriptionTriggererWeekly
    ]
}

module feedCleaner 'scheduledContainerJob.bicep' = {
    name: 'feedCleaner'
    params: {
        jobName: feedCleanerJobName
        location: location
        aspnetcoreEnvironment: aspnetcoreEnvironment
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.feedCleanerIdentityId
        cronSchedule: '0 2 * * *'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerImageName: containerImageName
        command: 'cd /app/FeedCleaner && dotnet ProductConstructionService.FeedCleaner.dll'
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
    dependsOn: [
        longestBuildPathUpdater
    ]
}

module keyVaultsModule 'key-vaults.bicep' = {
    name: 'keyVaultsModule'
    params: {
        location: location
        keyVaultName: keyVaultName
        devKeyVaultName: devKeyVaultName
        aspnetcoreEnvironment: aspnetcoreEnvironment
        kvSecretUserRole: kvSecretUserRole
        kvCryptoUserRole: kvCryptoUserRole
        pcsIdentityPrincipalId: managedIdentitiesModule.outputs.pcsIdentityPrincipalId
    }
}

module redisModule 'redis.bicep' = {
    name: 'redisModule'
    params: {
        location: location
        azureCacheRedisName: azureCacheRedisName
        pcsIdentityPrincipalId: managedIdentitiesModule.outputs.pcsIdentityPrincipalId
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module storageAccountModule 'storage-account.bicep' = {
    name: 'storageAccountModule'
    params: {
        location: location
        storageAccountName: storageAccountName
        pcsIdentityPrincipalId: managedIdentitiesModule.outputs.pcsIdentityPrincipalId
        subscriptionTriggererIdentityPrincipalId: managedIdentitiesModule.outputs.subscriptionTriggererIdentityPrincipalId
        blobContributorRole: blobContributorRole
        storageQueueContrubutorRole: storageQueueContrubutorRole
    }
}
