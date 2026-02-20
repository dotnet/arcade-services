using '../provision.bicep'

// Core deployment
param location = 'westus2'
param environmentName = 'Production'
param serviceName = 'build-insights'
param infrastructureResourceGroupName = 'build-insights-service-ip'

// Container app runtime
param containerEnvironmentName = 'build-insights-service-env'
param containerDefaultImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'
param containerReplicas = 3
param containerRegistryName = 'buildinsights'

// Identities
param appIdentityName = 'BuildInsightsServiceProd'
param deploymentIdentityName = 'BuildInsightsServiceDeploymentProd'
param scheduledJobIdentityName = 'BuildInsightsScheduledJobProd'

// Observability
param applicationInsightsName = 'build-insights-service-ai'
param logAnalyticsName = 'build-insights-service-workspace'

// Data and secrets
param keyVaultName = 'BuildInsightsProd'
param azureCacheRedisName = 'build-insights-service-redis'
param storageAccountName = 'buildinsightsprod'

// Networking
param virtualNetworkName = 'build-insights-service-vnet'
param serviceSubnetName = 'build-insights-service-subnet'
param networkSecurityGroupName = 'build-insights-service-nsg'
param publicIpAddressName = 'build-insights-service-public-ip'
param publicIpAddressServiceTag = 'DotNetBuildInsightsProd'

// Jobs
param feedCleanerJobName = 'feed-cleaner-prod'
