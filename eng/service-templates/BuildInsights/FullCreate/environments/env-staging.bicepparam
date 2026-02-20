using '../provision.bicep'

param serviceName = 'build-insights-stage'

param location = 'westus2'
param environmentName = 'Staging'
param infrastructureResourceGroupName = 'build-insights-service-ip-stage'

param containerRegistryName = 'buildinsightsstage'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'

param containerReplicas = 3

param applicationInsightsName = 'build-insights-service-ai-stage'

param keyVaultName = 'BuildInsightsStage'

param azureCacheRedisName = 'build-insights-service-redis-stage'

param logAnalyticsName = 'build-insights-service-workspace-stage'

param containerEnvironmentName = 'build-insights-service-env-stage'

param storageAccountName = 'buildinsightsstage'

param appIdentityName = 'BuildInsightsServiceStage'

param deploymentIdentityName = 'BuildInsightsServiceDeploymentStage'

param containerDefaultImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param virtualNetworkName = 'build-insights-service-vnet-stage'

param serviceSubnetName = 'build-insights-service-subnet'

param feedCleanerJobName = 'feed-cleaner-stage'

param scheduledJobIdentityName = 'BuildInsightsScheduledJobStage'

param networkSecurityGroupName = 'build-insights-service-nsg-stage'

param publicIpAddressName = 'build-insights-service-public-ip-stage'

param publicIpAddressServiceTag = 'DotNetBuildInsightsStage'
