using '../provision.bicep'

param serviceName = 'build-insights-dev'

param location = 'westus2'
param environmentName = 'Development'
param infrastructureResourceGroupName = 'build-insights-service-ip-dev'

param containerRegistryName = 'buildinsightsdev'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'

param containerReplicas = 3

param applicationInsightsName = 'build-insights-service-ai-dev'

param keyVaultName = 'BuildInsightsDev'

param azureCacheRedisName = 'build-insights-service-redis-dev'

param logAnalyticsName = 'build-insights-service-workspace-dev'

param containerEnvironmentName = 'build-insights-service-env-dev'

param storageAccountName = 'buildinsightsdev'

param appIdentityName = 'BuildInsightsServiceDev'

param deploymentIdentityName = 'BuildInsightsServiceDeploymentDev'

param containerDefaultImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param virtualNetworkName = 'build-insights-service-vnet-dev'

param serviceSubnetName = 'build-insights-service-subnet'

param feedCleanerJobName = 'feed-cleaner-dev'

param scheduledJobIdentityName = 'BuildInsightsScheduledJobDev'

param networkSecurityGroupName = 'build-insights-service-nsg-dev'

param publicIpAddressName = 'build-insights-service-public-ip-dev'

param publicIpAddressServiceTag = 'DotNetBuildInsightsDev'
