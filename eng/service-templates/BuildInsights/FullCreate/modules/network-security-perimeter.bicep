@description('Location for the Network Security Perimeter')
param location string

@description('Name of the Network Security Perimeter')
param perimeterName string

@description('Name of the NSP profile')
param profileName string = 'default'

@description('Access mode for resource associations. Use Learning for initial deployment to log violations without blocking, then switch to Enforced once validated.')
@allowed([
    'Learning'
    'Enforced'
    'Audit'
])
param accessMode string = 'Enforced'

// Resource IDs to associate with the perimeter
@description('Resource ID of the Key Vault')
param keyVaultId string

@description('Resource ID of the Storage Account')
param storageAccountId string

@description('Resource ID of the SQL Server')
param sqlServerId string

// Network Security Perimeter
resource perimeter 'Microsoft.Network/networkSecurityPerimeters@2023-08-01-preview' = {
    name: perimeterName
    location: location
}

// Profile – all associated resources share this profile and its access rules
resource profile 'Microsoft.Network/networkSecurityPerimeters/profiles@2023-08-01-preview' = {
    name: profileName
    parent: perimeter
    location: location
}

// Inbound access rules
// ---------------------------------------------------------------------------

// Allow access from Microsoft CorpNet (VPN) and SAW (Secure Admin Workstation)
resource corpNetAccessRule 'Microsoft.Network/networkSecurityPerimeters/profiles/accessRules@2023-08-01-preview' = {
    name: 'AllowMicrosoftCorpNet'
    parent: profile
    location: location
    properties: {
        direction: 'Inbound'
        serviceTags: [
            'CorpNetPublic' // Microsoft corporate VPN
            'CorpNetSAW' // Microsoft Secure Admin Workstations
            'CorpNetSAVM' // Microsoft Secure Admin Virtual Machines
            'CorpNet.DevBox' // Microsoft Dev Boxes
        ]
    }
}

// Allow access from all resources within the same subscription
resource subscriptionAccessRule 'Microsoft.Network/networkSecurityPerimeters/profiles/accessRules@2023-08-01-preview' = {
    name: 'AllowSameSubscription'
    parent: profile
    location: location
    properties: {
        direction: 'Inbound'
        subscriptions: [
            {
                id: '/subscriptions/${subscription().subscriptionId}'
            }
        ]
    }
}

// Resource associations – attach each resource to the perimeter profile
// ---------------------------------------------------------------------------

resource keyVaultAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = {
    name: '${perimeterName}-kv'
    parent: perimeter
    location: location
    properties: {
        accessMode: accessMode
        privateLinkResource: {
            id: keyVaultId
        }
        profile: {
            id: profile.id
        }
    }
}

resource storageAccountAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = {
    name: '${perimeterName}-sa'
    parent: perimeter
    location: location
    properties: {
        accessMode: accessMode
        privateLinkResource: {
            id: storageAccountId
        }
        profile: {
            id: profile.id
        }
    }
}

resource sqlServerAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = {
    name: '${perimeterName}-sql'
    parent: perimeter
    location: location
    properties: {
        accessMode: accessMode
        privateLinkResource: {
            id: sqlServerId
        }
        profile: {
            id: profile.id
        }
    }
}

output perimeterId string = perimeter.id
output profileId string = profile.id
