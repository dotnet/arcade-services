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

@description('Name of the MI used for the service container app')
param appIdentityName string

@description('Name of the identity used for the service deployment')
param deploymentIdentityName string

@description('Virtual network name')
param virtualNetworkName string

@description('Feed Cleaner Job name')
param scheduledJobName string

@description('Feed Cleaner Identity name')
param scheduledJobIdentityName string

@description('Network security group name')
param networkSecurityGroupName string

@description('Service subnet name')
param serviceSubnetName string

@description('Resource group where service IP resources will be created')
param infrastructureResourceGroupName string

@description('Number of replicas for the container app')
param containerReplicas int

@description('Public IP address name')
param publicIpAddressName string

@description('Public IP address service tag')
param publicIpAddressServiceTag string

@description('Enable creation of public IP address resources')
param enablePublicIpAddress bool = (environmentName == 'Production')

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
    }
}

module containerRegistryModule 'modules/container-registry.bicep' = {
    name: 'containerRegistryModule'
    params: {
        location: location
        containerRegistryName: containerRegistryName
        appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
        scheduledJobIdentityPrincipalId: managedIdentitiesModule.outputs.scheduledJobIdentityPrincipalId
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module containerAppModule 'modules/container-app.bicep' = {
    name: 'containerAppModule'
    params: {
        location: location
        containerEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        containerRegistryName: containerRegistryName
        containerCpuCoreCount: containerCpuCoreCount
        containerMemory: containerMemory
        serviceName: serviceName
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        environmentName: environmentName
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
        appIdentityId: managedIdentitiesModule.outputs.appIdentityId
        containerReplicas: containerReplicas
    }
    dependsOn: [
        containerRegistryModule
    ]
}

module scheduledJob 'modules/scheduledContainerJob.bicep' = {
    name: 'scheduledJob'
    params: {
        jobName: scheduledJobName
        location: location
        environmentName: environmentName
        applicationInsightsConnectionString: containerEnvironmentModule.outputs.applicationInsightsConnectionString
        userAssignedIdentityId: managedIdentitiesModule.outputs.scheduledJobIdentityId
        cronSchedule: '0 2 * * *'
        containerRegistryName: containerRegistryName
        containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
        command: 'cd /app/FeedCleaner && dotnet ProductConstructionService.FeedCleaner.dll'
        deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    }
}

module keyVaultsModule 'modules/key-vault.bicep' = {
    name: 'keyVaultModule'
    params: {
        location: location
        keyVaultName: keyVaultName
        appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
        serviceSubnetId: virtualNetworkModule.outputs.productConstructionServiceSubnetId
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
        serviceSubnetId: virtualNetworkModule.outputs.productConstructionServiceSubnetId
    }
}

module ipAddressModule 'modules/public-ip-address.bicep' = if (enablePublicIpAddress) {
    name: 'ipAddressModule'
    params: {
        location: location
        publicIpAddressName: publicIpAddressName
        publicIpAddressServiceTag: publicIpAddressServiceTag
    }
}
