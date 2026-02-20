using '../provision.bicep'

// Core deployment
param location = 'westus2'
param environmentName = 'Development'
param serviceName = 'build-insights-dev'

// Container app runtime
param containerEnvironmentName = 'build-insights-service-env-dev'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'
param containerReplicas = 3
param containerRegistryName = 'buildinsightsdev'

// Identities
param appIdentityName = 'BuildInsightsServiceDev'
param deploymentIdentityName = 'BuildInsightsServiceDeploymentDev'
param scheduledJobIdentityName = 'BuildInsightsScheduledJobDev'

// Observability
param applicationInsightsName = 'build-insights-service-ai-dev'
param logAnalyticsName = 'build-insights-service-workspace-dev'

// Data and secrets
param keyVaultName = 'BuildInsightsDev'
param azureCacheRedisName = 'build-insights-service-redis-dev'
param storageAccountName = 'buildinsightsdev'

// Networking
param virtualNetworkName = 'build-insights-service-vnet-dev'
param serviceSubnetName = 'build-insights-service-subnet'
param networkSecurityGroupName = 'build-insights-service-nsg-dev'
param infrastructureResourceGroupName = 'build-insights-service-ip-dev'
param publicIpAddressName = 'build-insights-service-public-ip-dev'
param publicIpAddressServiceTag = 'DotNetBuildInsightsDev'

// Jobs
param feedCleanerJobName = 'feed-cleaner-dev'
