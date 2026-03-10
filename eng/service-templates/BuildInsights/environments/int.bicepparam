using '../provision.bicep'

var serviceNameKebabCase = 'build-insights'
var serviceNamePascalCase = 'BuildInsights'
var environmentSuffix = 'int'
var environmentPascalCase = 'Int'

// Core deployment
param location = 'westus2'
param environmentName = 'Staging'
param serviceName = '${serviceNameKebabCase}-${environmentSuffix}'

// Container app runtime
param containerEnvironmentName = '${serviceNameKebabCase}-env-${environmentSuffix}'
param containerCpuCoreCount = '1.0'
param containerMemory = '2Gi'
param containerReplicas = 3
param containerRegistryName = '${toLower(replace(serviceNameKebabCase, '-', ''))}${environmentSuffix}'

// Identities
param appIdentityName = '${serviceNamePascalCase}Service${environmentPascalCase}'
param deploymentIdentityCreate = false
param deploymentIdentityName = 'ProductConstructionServiceDeploymentInt'
param deploymentIdentityResourceGroupName = 'product-construction-service'

// Observability
param applicationInsightsName = '${serviceNameKebabCase}-ai-${environmentSuffix}'
param logAnalyticsName = '${serviceNameKebabCase}-log-${environmentSuffix}'

// Data and secrets
param keyVaultName = '${serviceNamePascalCase}${environmentPascalCase}'
param keyVaultCreateMode = 'recover'
param azureCacheRedisName = '${serviceNameKebabCase}-redis-${environmentSuffix}'
param storageAccountName = '${toLower(replace(serviceNameKebabCase, '-', ''))}${environmentSuffix}'

// Networking
param virtualNetworkName = '${serviceNameKebabCase}-vnet-${environmentSuffix}'
param serviceSubnetName = '${serviceNameKebabCase}-subnet'
param networkSecurityGroupName = '${serviceNameKebabCase}-nsg-${environmentSuffix}'
param infrastructureResourceGroupName = '${serviceNameKebabCase}-${environmentSuffix}-ip'
param publicIpAddressName = '${serviceNameKebabCase}-public-ip-${environmentSuffix}'
param publicIpAddressServiceTag = 'DotNet${serviceNamePascalCase}${environmentPascalCase}'
param networkSecurityPerimeterName = '${serviceNameKebabCase}-nsp-${environmentSuffix}'

// Application Gateway
param appGwName = '${serviceNameKebabCase}-agw-${environmentSuffix}'
param appGwIdentityName = '${serviceNamePascalCase}AppGw${environmentPascalCase}'
param certificateName = '${serviceNameKebabCase}-${environmentSuffix}'
param certificateSecretIdShort = 'https://${serviceNamePascalCase}${environmentPascalCase}.vault.azure.net/secrets/${serviceNameKebabCase}-${environmentSuffix}'
param hostName = 'build-insights.int-dot.net'

// SQL
param sqlServerName = '${serviceNameKebabCase}-sql-${environmentSuffix}'
param sqlDatabaseName = '${serviceNamePascalCase}'

// Jobs
param scheduledJobName = 'known-issues-monitor-job-${environmentSuffix}'
