using '../provision.bicep'

var serviceNameKebabCase = 'build-insights'
var serviceNamePascalCase = 'BuildInsights'
var environmentSuffix = 'dev'
var environmentPascalCase = 'Dev'

// Core deployment
param location = 'westus2'
param environmentName = 'Development'
param serviceName = '${serviceNameKebabCase}-${environmentSuffix}'

// Container app runtime
param containerEnvironmentName = '${serviceNameKebabCase}-env-${environmentSuffix}'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'
param containerReplicas = 3
param containerRegistryName = '${toLower(replace(serviceNameKebabCase, '-', ''))}${environmentSuffix}'

// Identities
param appIdentityName = '${serviceNamePascalCase}Service${environmentPascalCase}'
param deploymentIdentityName = '${serviceNamePascalCase}ServiceDeployment${environmentPascalCase}'
param scheduledJobIdentityName = '${serviceNamePascalCase}ScheduledJob${environmentPascalCase}'

// Observability
param applicationInsightsName = '${serviceNameKebabCase}-ai-${environmentSuffix}'
param logAnalyticsName = '${serviceNameKebabCase}-workspace-${environmentSuffix}'

// Data and secrets
param keyVaultName = '${serviceNamePascalCase}${environmentPascalCase}'
param azureCacheRedisName = '${serviceNameKebabCase}-redis-${environmentSuffix}'
param storageAccountName = '${toLower(replace(serviceNameKebabCase, '-', ''))}${environmentSuffix}'

// Networking
param virtualNetworkName = '${serviceNameKebabCase}-vnet-${environmentSuffix}'
param serviceSubnetName = '${serviceNameKebabCase}-subnet'
param networkSecurityGroupName = '${serviceNameKebabCase}-nsg-${environmentSuffix}'
param infrastructureResourceGroupName = '${serviceNameKebabCase}-ip-${environmentSuffix}'
param publicIpAddressName = '${serviceNameKebabCase}-public-ip-${environmentSuffix}'
param publicIpAddressServiceTag = 'DotNet${serviceNamePascalCase}${environmentPascalCase}'
param enablePublicIpAddress = false
param networkSecurityPerimeterName = '${serviceNameKebabCase}-nsp-${environmentSuffix}'

// SQL
param sqlServerName = '${serviceNameKebabCase}-sql-${environmentSuffix}'
param sqlDatabaseName = '${serviceNamePascalCase}'

// Jobs
param scheduledJobName = '${serviceNameKebabCase}-scheduled-job-${environmentSuffix}'
