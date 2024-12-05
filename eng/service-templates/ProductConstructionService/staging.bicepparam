using 'recrete.bicep'

param location = 'westus2'

param nsgName = 'product-construction-service-nsg-int'

param containerRegistryName = 'productconstructionint'

param containerCpuCoreCount = '1.0'

param containerMemory = '2Gi'

param aspnetcoreEnvironment = 'Staging'

param applicationInsightsName = 'product-construction-service-ai-int'

//param keyVaultName = 'ProductConstructionInt'

//param devKeyVaultName = 'ProductConstructionDev'

//param azureCacheRedisName = 'product-construction-service-redis-int'

param logAnalyticsName = 'product-construction-service-workspace-int'

param containerEnvironmentName = '1product-construction-service-env-int'

param productConstructionServiceName = 'a1product-construction-int'

//param storageAccountName = 'productconstructionint'

param pcsIdentityName = 'ProductConstructionServiceInt'

param deploymentIdentityName = 'ProductConstructionServiceDeploymentInt'

param containerImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param virtualNetworkName = '2product-construction-service-vnet-int'

param productConstructionServiceSubnetName = 'product-construction-service-subnet'

param subscriptionTriggererIdentityName = 'SubscriptionTriggererInt'

param subscriptionTriggererWeeklyJobName = 'a1sub-triggerer-weekly-int'

param subscriptionTriggererTwiceDailyJobName = 'a1sub-triggerer-twicedaily-int'

param subscriptionTriggererDailyJobName = 'a1sub-triggerer-daily-int'

param longestBuildPathUpdaterIdentityName = 'LongestBuildPathUpdaterInt'

param longestBuildPathUpdaterJobName = 'a1longest-path-updater-job-int'

param feedCleanerJobName = 'a1feed-cleaner-int'

param feedCleanerIdentityName = 'FeedCleanerInt'

//param networkSecurityGroupName = 'product-construction-service-nsg-int'

param infrastructureResourceGroupName = '1product-construction-service-ip-int'
