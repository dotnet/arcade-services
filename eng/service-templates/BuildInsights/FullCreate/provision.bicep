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
param environmentName string

@description('Name of the application insights resource')
param applicationInsightsName string

@description('Key Vault name')
param keyVaultName string

@description('Azure Cache for Redis name')
param azureCacheRedisName string

@description('Log analytics workspace name')
param logAnalyticsName string

@description('Name of the container apps environment')
param containerEnvironmentName string

@description('Product construction service API name')
param serviceName string

@description('Storage account name')
param storageAccountName string

@description('Name of the MI used for the PCS container app')
param appIdentityName string

@description('Name of the identity used for the PCS deployment')
param deploymentIdentityName string

@description('Bicep requires an image when creating a containerapp. Using a dummy image for that.')
param containerDefaultImageName string

@description('Virtual network name')
param virtualNetworkName string

@description('Feed Cleaner Job name')
param feedCleanerJobName string

@description('Feed Cleaner Identity name')
param scheduledJobIdentityName string

@description('Network security group name')
param networkSecurityGroupName string

@description('Service subnet name')
param serviceSubnetName string

@description('Resource group where PCS IP resources will be created')
param infrastructureResourceGroupName string

@description('Number of replicas for the container app')
param containerReplicas int

@description('Public IP address name')
param publicIpAddressName string

@description('Public IP address service tag')
param publicIpAddressServiceTag string

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

module networkSecurityGroupModule 'modules/nsg.bicep' = {
    name: 'networkSecurityGroupModule'
    params: {
        networkSecurityGroupName: networkSecurityGroupName
        location: location
    }
}

module virtualNetworkModule 'modules/virtual-network.bicep' = {
    name: 'virtualNetworkModule'
    params: {
        location: location
        virtualNetworkName: virtualNetworkName
        networkSecurityGroupId: networkSecurityGroupModule.outputs.networkSecurityGroupId
        serviceSubnetName: serviceSubnetName
    }
}

module containerEnvironmentModule 'modules/container-environment.bicep' = {
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

module managedIdentitiesModule 'modules/managed-identities.bicep' = {
    name: 'managedIdentitiesModule'
    params: {
        location: location
        deploymentIdentityName: deploymentIdentityName
        appIdentityName: appIdentityName
        scheduledJobIdentityName: scheduledJobIdentityName
        contributorRole: contributorRole
    }
}

module containerRegistryModule 'modules/container-registry.bicep' = {
    name: 'containerRegistryModule'
    params: {
        location: location
        containerRegistryName: containerRegistryName
        acrPullRole: acrPullRole
        appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
        scheduledJobIdentityPrincipalId: managedIdentitiesModule.outputs.scheduledJobIdentityPrincipalId
        acrPushRole: acrPushRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module containerAppModule 'modules/container-app.bicep' = {
    name: 'containerAppModule'
    params: {
        location: location
        containerEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerDefaultImageName: containerDefaultImageName
        containerRegistryName: containerRegistryName
        containerCpuCoreCount: containerCpuCoreCount
        containerMemory: containerMemory
        serviceName: serviceName
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        environmentName: environmentName
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
        appIdentityId: managedIdentitiesModule.outputs.appIdentityId
        containerReplicas: containerReplicas
    }
    dependsOn: [
        containerRegistryModule
    ]
}

module feedCleaner 'modules/scheduledContainerJob.bicep' = {
    name: 'feedCleaner'
    params: {
        jobName: feedCleanerJobName
        location: location
        environmentName: environmentName
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.scheduledJobIdentityId
        cronSchedule: '0 2 * * *'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerDefaultImageName: containerDefaultImageName
        command: 'cd /app/FeedCleaner && dotnet ProductConstructionService.FeedCleaner.dll'
        contributorRoleId: contributorRole
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module keyVaultsModule 'modules/key-vaults.bicep' = {
    name: 'keyVaultsModule'
    params: {
        location: location
        keyVaultName: keyVaultName
        kvSecretUserRole: kvSecretUserRole
        kvCryptoUserRole: kvCryptoUserRole
        appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
    }
}

module redisModule 'modules/redis.bicep' = {
    name: 'redisModule'
    params: {
        location: location
        azureCacheRedisName: azureCacheRedisName
        appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module storageAccountModule 'modules/storage-account.bicep' = {
    name: 'storageAccountModule'
    params: {
        location: location
        storageAccountName: storageAccountName
        appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
        blobContributorRole: blobContributorRole
        storageQueueContrubutorRole: storageQueueContrubutorRole
    }
}

module ipAddressModule 'modules/public-ip-address.bicep' = {
    name: 'ipAddressModule'
    params: {
        location: location
        publicIpAddressName: publicIpAddressName
        publicIpAddressServiceTag: publicIpAddressServiceTag
    }
}
