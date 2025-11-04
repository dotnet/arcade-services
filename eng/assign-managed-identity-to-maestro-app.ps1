<#
.SYNOPSIS
    Assigns a given Managed Identity (MI) a role in the Maestro application.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $true)]
    [string]$ManagedIdentityName,
    
    [Parameter(Mandatory = $false)]
    [string]$AppServicePrincipal = "caf36d9b-2940-4270-9a1d-c494eda6ea18", # Maestro application object ID
    
    [Parameter(Mandatory = $false)]
    [ValidateSet("user", "admin")]
    [string]$Role = "user"
)

# Set app role ID based on role parameter
$appRoleId = switch ($Role) {
    "user"  { "108187e7-df11-4592-b306-2a2a0b15d8f0" } # User role ID
    "admin" { "8b5767ed-0675-4e95-9858-f9851b884345" } # Admin role ID
}

Write-Host "Using role: $Role (ID: $appRoleId)"

az login

$resourceIdWithManagedIdentity = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$ManagedIdentityName"
$principalId = (az identity show --resource-group $ResourceGroupName --name $ManagedIdentityName --subscription $SubscriptionId --query 'principalId' -o tsv)
Write-Host "Managed identity principal ID: $($principalId)"

$body = "{'principalId': '$principalId', 'resourceId': '$($appServicePrincipal)', 'appRoleId': '$($appRoleId)'}"
Write-Host "Body: $body"

az rest -m POST -u https://graph.microsoft.com/v1.0/servicePrincipals/$principalId/appRoleAssignments -b $body
