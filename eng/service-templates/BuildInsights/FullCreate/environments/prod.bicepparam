using '../provision.bicep'

var serviceNameKebabCase = 'build-insights'
var serviceNamePascalCase = 'BuildInsights'
var environmentSuffix = 'prod'
var environmentPascalCase = 'Prod'
var optionalEnvironmentSuffix = ''

// Core deployment
param location = 'westus2'
param environmentName = 'Production'
param serviceName = '${serviceNameKebabCase}${optionalEnvironmentSuffix}'

// Container app runtime
param containerEnvironmentName = '${serviceNameKebabCase}-env${optionalEnvironmentSuffix}'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'
param containerReplicas = 3
param containerRegistryName = '${toLower(replace(serviceNameKebabCase, '-', ''))}${optionalEnvironmentSuffix}'

// Identities
param appIdentityName = '${serviceNamePascalCase}Service${environmentPascalCase}'
param deploymentIdentityName = '${serviceNamePascalCase}ServiceDeployment${environmentPascalCase}'
param scheduledJobIdentityName = '${serviceNamePascalCase}ScheduledJob${environmentPascalCase}'

// Observability
param applicationInsightsName = '${serviceNameKebabCase}-ai${optionalEnvironmentSuffix}'
param logAnalyticsName = '${serviceNameKebabCase}-log${optionalEnvironmentSuffix}'

// Data and secrets
param keyVaultName = '${serviceNamePascalCase}${environmentPascalCase}'
param azureCacheRedisName = '${serviceNameKebabCase}-redis${optionalEnvironmentSuffix}'
param storageAccountName = '${toLower(replace(serviceNameKebabCase, '-', ''))}${environmentSuffix}'

// Networking
param virtualNetworkName = '${serviceNameKebabCase}-vnet${optionalEnvironmentSuffix}'
param serviceSubnetName = '${serviceNameKebabCase}-subnet'
param networkSecurityGroupName = '${serviceNameKebabCase}-nsg${optionalEnvironmentSuffix}'
param infrastructureResourceGroupName = '${serviceNameKebabCase}${optionalEnvironmentSuffix}-ip'
param publicIpAddressName = '${serviceNameKebabCase}-public-ip${optionalEnvironmentSuffix}'
param publicIpAddressServiceTag = 'DotNet${serviceNamePascalCase}${environmentPascalCase}'
param enablePublicIpAddress = true
param networkSecurityPerimeterName = '${serviceNameKebabCase}-nsp-${environmentSuffix}'

// SQL
param sqlServerName = '${serviceNameKebabCase}-sql${optionalEnvironmentSuffix}'
param sqlDatabaseName = '${serviceNamePascalCase}'

// Jobs
param scheduledJobName = '${serviceNameKebabCase}-scheduled-job-${environmentSuffix}'
