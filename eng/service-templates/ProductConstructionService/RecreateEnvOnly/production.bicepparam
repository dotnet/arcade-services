using './recreate.bicep'

param location = 'westus2'

param nsgName = 'product-construction-service-nsg-prod'

param containerRegistryName = 'productconstructionprod'

param containerCpuCoreCount = '4.0'

param containerMemory = '8Gi'

param replicaNumber = 10

param aspnetcoreEnvironment = 'Production'

param applicationInsightsName = 'product-construction-service-ai-prod'

param logAnalyticsName = 'product-construction-service-workspace-prod'

param containerEnvironmentName = 'product-construction-service-env-prod'

param productConstructionServiceName = 'product-construction-prod'

param pcsIdentityName = 'ProductConstructionServiceProd'

param deploymentIdentityName = 'ProductConstructionServiceDeploymentProd'

param containerImageName = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param virtualNetworkName = 'product-construction-service-vnet-prod'

param productConstructionServiceSubnetName = 'product-construction-service-subnet'

param subscriptionTriggererIdentityName = 'SubscriptionTriggererProd'

param subscriptionTriggererWeeklyJobName = 'sub-triggerer-weekly-prod'

param subscriptionTriggererTwiceDailyJobName = 'sub-triggerer-twicedaily-prod'

param subscriptionTriggererDailyJobName = 'sub-triggerer-daily-prod'

param longestBuildPathUpdaterIdentityName = 'LongestBuildPathUpdaterProd'

param longestBuildPathUpdaterJobName = 'longest-path-updater-job-prod'

param feedCleanerJobName = 'feed-cleaner-prod'

param feedCleanerIdentityName = 'FeedCleanerProd'

param infrastructureResourceGroupName = 'product-construction-service-ip-prod'
