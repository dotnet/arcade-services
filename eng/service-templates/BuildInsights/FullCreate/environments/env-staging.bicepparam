using '../provision.bicep'

// Core deployment
param location = 'westus2'
param environmentName = 'Staging'
param serviceName = 'build-insights-stage'

// Container app runtime
param containerEnvironmentName = 'build-insights-service-env-stage'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'
param containerReplicas = 3
param containerRegistryName = 'buildinsightsstage'

// Identities
param appIdentityName = 'BuildInsightsServiceStage'
param deploymentIdentityName = 'BuildInsightsServiceDeploymentStage'
param scheduledJobIdentityName = 'BuildInsightsScheduledJobStage'

// Observability
param applicationInsightsName = 'build-insights-service-ai-stage'
param logAnalyticsName = 'build-insights-service-workspace-stage'

// Data and secrets
param keyVaultName = 'BuildInsightsStage'
param azureCacheRedisName = 'build-insights-service-redis-stage'
param storageAccountName = 'buildinsightsstage'

// Networking
param virtualNetworkName = 'build-insights-service-vnet-stage'
param serviceSubnetName = 'build-insights-service-subnet'
param networkSecurityGroupName = 'build-insights-service-nsg-stage'
param infrastructureResourceGroupName = 'build-insights-service-ip-stage'
param publicIpAddressName = 'build-insights-service-public-ip-stage'
param publicIpAddressServiceTag = 'DotNetBuildInsightsStage'

// Jobs
param feedCleanerJobName = 'feed-cleaner-stage'
