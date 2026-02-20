using '../provision.bicep'

param serviceName = 'build-insights'

param location = 'westus2'
param environmentName = 'Production'
param infrastructureResourceGroupName = 'build-insights-service-ip'

param containerRegistryName = 'buildinsights'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'

param containerReplicas = 3

param applicationInsightsName = 'build-insights-service-ai'

param keyVaultName = 'BuildInsightsProd'

param azureCacheRedisName = 'build-insights-service-redis'

param logAnalyticsName = 'build-insights-service-workspace'

param containerEnvironmentName = 'build-insights-service-env'

param storageAccountName = 'buildinsightsprod'

param appIdentityName = 'BuildInsightsServiceProd'

param deploymentIdentityName = 'BuildInsightsServiceDeploymentProd'

param containerDefaultImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param virtualNetworkName = 'build-insights-service-vnet'

param serviceSubnetName = 'build-insights-service-subnet'

param feedCleanerJobName = 'feed-cleaner-prod'

param scheduledJobIdentityName = 'BuildInsightsScheduledJobProd'

param networkSecurityGroupName = 'build-insights-service-nsg'

param publicIpAddressName = 'build-insights-service-public-ip'

param publicIpAddressServiceTag = 'DotNetBuildInsightsProd'
