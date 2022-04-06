param clusterName string
param applicationTypeName string
param applicationTypeVersion string
param applicationPackageUrl string
param applicationName string

param location string = resourceGroup().location

param appTypeExists bool = true
param monitoredUpgrade bool = true
param parameters object

resource appType 'Microsoft.ServiceFabric/clusters/applicationTypes@2021-06-01' = if (!appTypeExists) {
  name: '${clusterName}/${applicationTypeName}'
  location: location
}

resource appVersion 'Microsoft.ServiceFabric/clusters/applicationTypes/versions@2021-06-01' = {
  name: '${clusterName}/${applicationTypeName}/${applicationTypeVersion}'
  location: location
  dependsOn: [
    appType
  ]
  properties: {
    appPackageUrl: applicationPackageUrl
  }
}

var upgradePolicy = monitoredUpgrade ? {
  upgradeMode: 'Monitored'
  upgradeReplicaSetCheckTimeout: '00:02:00.0'
  forceRestart: false
  rollingUpgradeMonitoringPolicy: {
    healthCheckWaitDuration: '00:00:00.0'
    healthCheckStableDuration: '00:01:00.0'
    healthCheckRetryTimeout: '00:10:00.0'
    upgradeTimeout: '01:00:00.0'
    upgradeDomainTimeout: '00:11:00.0'
  }
  applicationHealthPolicy: {
    considerWarningAsError: false
    maxPercentUnhealthyDeployedApplications: 0
    defaultServiceTypeHealthPolicy: {
      maxPercentUnhealthyServices: 0
      maxPercentUnhealthyPartitionsPerService: 0
      maxPercentUnhealthyReplicasPerPartition: 0
    }
  }
} : {
  upgradeMode: 'UnmonitoredAuto'
  forceRestart: false
  upgradeReplicaSetCheckTimeout: '00:02:00.0'
}

resource app 'Microsoft.ServiceFabric/clusters/applications@2021-06-01' = {
  name: '${clusterName}/${applicationName}'
  location: location
  dependsOn: [
    appVersion
  ]
  properties: {
    typeName: applicationTypeName
    typeVersion: applicationTypeVersion
    parameters: parameters
    upgradePolicy: upgradePolicy
  }
}

