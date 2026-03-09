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

@description('Whether to create a new deployment identity or use an existing one')
param deploymentIdentityCreate bool

@description('Resource group of the existing deployment identity (used when deploymentIdentityCreate is false)')
param deploymentIdentityResourceGroupName string = resourceGroup().name

@description('Virtual network name')
param virtualNetworkName string

@description('Feed Cleaner Job name')
param scheduledJobName string

@description('Network security group name')
param networkSecurityGroupName string

@description('Azure SQL Server name')
param sqlServerName string

@description('Azure SQL Database name')
param sqlDatabaseName string

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

@description('Network Security Perimeter name')
param networkSecurityPerimeterName string

// Shared environment variables for all container apps and jobs
var sharedEnvVars = [
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
    value: containerEnvironmentModule.outputs.applicationInsightsConnectionString
  }
  {
    name: 'ConnectionStrings__bi-mssql'
    value: 'Server=tcp:${sqlDatabaseModule.outputs.sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseModule.outputs.sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${managedIdentitiesModule.outputs.appIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30'
  }
  {
    name: 'ConnectionStrings__bi-redis'
    value: '${redisModule.outputs.redisCacheHostName}:6380,ssl=True,abortConnect=False'
  }
  {
    name: 'ConnectionStrings__bi-blobs'
    value: storageAccountModule.outputs.storageAccountBlobEndpoint
  }
  {
    name: 'ConnectionStrings__bi-queues'
    value: storageAccountModule.outputs.storageAccountQueueEndpoint
  }
  {
    name: 'ManagedIdentityClientId'
    value: managedIdentitiesModule.outputs.appIdentityClientId
  }
  {
    name: 'KeyVaultName'
    value: keyVaultName
  }
]

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
    deploymentIdentityCreate: deploymentIdentityCreate
    deploymentIdentityResourceGroupName: deploymentIdentityResourceGroupName
    appIdentityName: appIdentityName
  }
}

module containerRegistryModule 'modules/container-registry.bicep' = {
  name: 'containerRegistryModule'
  params: {
    location: location
    containerRegistryName: containerRegistryName
    appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
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
    environmentName: environmentName
    deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    appIdentityId: managedIdentitiesModule.outputs.appIdentityId
    containerReplicas: containerReplicas
    sharedEnvVars: sharedEnvVars
  }
  dependsOn: [
    containerRegistryModule
  ]
}

module scheduledJob 'modules/container-job.bicep' = {
  name: 'scheduledJob'
  params: {
    jobName: scheduledJobName
    location: location
    environmentName: environmentName
    userAssignedIdentityId: managedIdentitiesModule.outputs.appIdentityId
    cronSchedule: '0 * * * *'
    containerRegistryName: containerRegistryName
    containerAppsEnvironmentId: containerEnvironmentModule.outputs.containerEnvironmentId
    command: 'cd /app/BuildInsights.KnownIssuesMonitor && dotnet BuildInsights.KnownIssuesMonitor.dll'
    deploymentIdentityPrincipalId: managedIdentitiesModule.outputs.deploymentIdentityPrincipalId
    sharedEnvVars: sharedEnvVars
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

module sqlDatabaseModule 'modules/sql-database.bicep' = {
  name: 'sqlDatabaseModule'
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    appIdentityPrincipalId: managedIdentitiesModule.outputs.appIdentityPrincipalId
    serviceSubnetId: virtualNetworkModule.outputs.productConstructionServiceSubnetId
  }
}

module networkSecurityPerimeterModule 'modules/network-security-perimeter.bicep' = {
  name: 'networkSecurityPerimeterModule'
  params: {
    location: location
    perimeterName: networkSecurityPerimeterName
    keyVaultId: keyVaultsModule.outputs.keyVaultId
    storageAccountId: storageAccountModule.outputs.storageAccountId
    sqlServerId: sqlDatabaseModule.outputs.sqlServerId
  }
}

// Outputs used by the post-deployment SQL access provisioning script
output sqlServerName string = sqlServerName
output sqlServerFqdn string = sqlDatabaseModule.outputs.sqlServerFqdn
output sqlDatabaseName string = sqlDatabaseModule.outputs.sqlDatabaseName
output appIdentityName string = appIdentityName
output appIdentityPrincipalId string = managedIdentitiesModule.outputs.appIdentityPrincipalId
output appIdentityClientId string = managedIdentitiesModule.outputs.appIdentityClientId
output deploymentIdentityName string = deploymentIdentityName
