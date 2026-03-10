param location string
param sqlServerName string
param sqlDatabaseName string
param appIdentityPrincipalId string
param serviceSubnetId string

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: 'app-identity'
      sid: appIdentityPrincipalId
      principalType: 'Application'
      tenantId: subscription().tenantId
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'S4'
    tier: 'Standard'
    capacity: 200
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// Allow Azure services to access the SQL server
resource allowAzureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Allow access from the service subnet
resource vnetRule 'Microsoft.Sql/servers/virtualNetworkRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'serviceSubnetRule'
  properties: {
    virtualNetworkSubnetId: serviceSubnetId
    ignoreMissingVnetServiceEndpoint: false
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output sqlServerId string = sqlServer.id
