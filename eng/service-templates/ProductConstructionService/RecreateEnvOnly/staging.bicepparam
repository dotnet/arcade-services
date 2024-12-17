using 'recreate.bicep'

param location = 'westus2'

param nsgName = 'product-construction-service-nsg-int'

param containerRegistryName = 'productconstructionint'

param containerCpuCoreCount = '1.0'

param containerMemory = '2Gi'

param aspnetcoreEnvironment = 'Staging'

param applicationInsightsName = 'product-construction-service-ai-int'

param logAnalyticsName = 'product-construction-service-workspace-int'

param containerEnvironmentName = 'product-construction-service-env-int'

param productConstructionServiceName = 'product-construction-int'

param pcsIdentityName = 'ProductConstructionServiceInt'

param deploymentIdentityName = 'ProductConstructionServiceDeploymentInt'

param containerImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param virtualNetworkName = 'product-construction-service-vnet-int'

param productConstructionServiceSubnetName = 'product-construction-service-subnet'

param subscriptionTriggererIdentityName = 'SubscriptionTriggererInt'

param subscriptionTriggererWeeklyJobName = 'sub-triggerer-weekly-int'

param subscriptionTriggererTwiceDailyJobName = 'sub-triggerer-twicedaily-int'

param subscriptionTriggererDailyJobName = 'sub-triggerer-daily-int'

param longestBuildPathUpdaterIdentityName = 'LongestBuildPathUpdaterInt'

param longestBuildPathUpdaterJobName = 'longest-path-updater-job-int'

param feedCleanerJobName = 'feed-cleaner-int'

param feedCleanerIdentityName = 'FeedCleanerInt'

param infrastructureResourceGroupName = 'product-construction-service-ip-int'

param replicaNumber = 3
