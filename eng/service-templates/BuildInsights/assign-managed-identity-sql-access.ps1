#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Assigns SQL access permissions to the managed identity for the BuildInsights database.

.DESCRIPTION
    This script fetches deployment outputs from an Azure resource group deployment, sets the
    currently logged-in Azure user as the SQL server Entra administrator, and creates SQL
    contained database users for:
      - The deployment identity (db_owner - can modify schema for migrations)
      - The app managed identity (db_datareader + db_datawriter - data access only)

    A temporary firewall rule is added for the caller's public IP and removed afterwards.

.PARAMETER Environment
    The target environment: dev, int, or prod.

.PARAMETER DeploymentName
    The name of the Azure resource group deployment to read outputs from. Default is 'deploy'.

.EXAMPLE
    .\assign-managed-identity-sql-access.ps1 -Environment dev

.EXAMPLE
    .\assign-managed-identity-sql-access.ps1 -Environment prod -DeploymentName deploy
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('prod', 'int', 'dev')]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$DeploymentName = 'deploy'
)

# Error handling
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Main execution
try {
    Write-Host "BuildInsights SQL Managed Identity Access Assignment" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""

    # Load environment configuration from shared config file
    $configFile = Join-Path -Path $PSScriptRoot -ChildPath 'environment-config.json'
    $allConfig = Get-Content $configFile -Raw | ConvertFrom-Json
    $envConfig = $allConfig.$Environment
    if (-not $envConfig) {
        Write-Host "No configuration found for environment '$Environment' in $configFile" -ForegroundColor Red
        exit 1
    }

    $ResourceGroupName = $envConfig.resourceGroupName
    $subscriptionId = $envConfig.subscriptionId

    Write-Host "Environment:     $Environment" -ForegroundColor Gray
    Write-Host "Subscription:    $subscriptionId" -ForegroundColor Gray
    Write-Host "Resource Group:  $ResourceGroupName" -ForegroundColor Gray
    Write-Host ""

    # Switch to the target subscription
    $currentSubscriptionId = az account show --query id --output tsv
    if (-not $currentSubscriptionId.Equals($subscriptionId, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Switching to subscription: $subscriptionId" -ForegroundColor Yellow
        az account set --subscription $subscriptionId
    }

    # Fetch deployment outputs from the Azure resource group deployment
    Write-Host "Fetching deployment outputs from resource group '$ResourceGroupName', deployment '$DeploymentName'..." -ForegroundColor Yellow
    try {
        $outputsRaw = az deployment group show `
            --resource-group $ResourceGroupName `
            --name $DeploymentName `
            --query properties.outputs `
            --output json 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Host "Failed to fetch deployment outputs. Is the deployment name correct?" -ForegroundColor Red
            Write-Host $outputsRaw -ForegroundColor Red
            exit 1
        }

        $outputsJson = $outputsRaw | ConvertFrom-Json
        Write-Host "Deployment outputs loaded successfully" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to fetch or parse deployment outputs: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    # Extract required values from outputs
    try {
        $sqlServerName = $outputsJson.sqlServerName.value
        $sqlServerFqdn = $outputsJson.sqlServerFqdn.value
        $sqlDatabaseName = $outputsJson.sqlDatabaseName.value
        $appIdentityName = $outputsJson.appIdentityName.value
        $appIdentityPrincipalId = $outputsJson.appIdentityPrincipalId.value
        $appIdentityClientId = $outputsJson.appIdentityClientId.value
        $deploymentIdentityName = $outputsJson.deploymentIdentityName.value

        Write-Host ""
        Write-Host "Configuration:" -ForegroundColor Yellow
        Write-Host "  SQL Server:              $sqlServerFqdn ($sqlServerName)" -ForegroundColor Gray
        Write-Host "  SQL Database:            $sqlDatabaseName" -ForegroundColor Gray
        Write-Host "  Resource Group:          $ResourceGroupName" -ForegroundColor Gray
        Write-Host "  App Identity Name:       $appIdentityName" -ForegroundColor Gray
        Write-Host "  App Identity Client:     $appIdentityClientId" -ForegroundColor Gray
        Write-Host "  App Identity Principal:  $appIdentityPrincipalId" -ForegroundColor Gray
        Write-Host "  Deployment Identity:     $deploymentIdentityName" -ForegroundColor Gray
    }
    catch {
        Write-Host "Failed to extract required values from deployment outputs: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Expected outputs: sqlServerFqdn, sqlDatabaseName, appIdentityName, appIdentityClientId, deploymentIdentityName" -ForegroundColor Yellow
        exit 1
    }

    # Check Azure CLI
    Write-Host ""
    Write-Host "Checking Azure CLI..." -ForegroundColor Yellow
    try {
        $azVersion = az version --output json 2>$null | ConvertFrom-Json
        if (-not $azVersion) {
            Write-Host "Azure CLI is not installed or not accessible" -ForegroundColor Red
            Write-Host "Please install Azure CLI from: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli" -ForegroundColor Yellow
            exit 1
        }
        Write-Host "Azure CLI version $($azVersion.'azure-cli') is installed" -ForegroundColor Green
    }
    catch {
        Write-Host "Azure CLI is not installed or not accessible" -ForegroundColor Red
        Write-Host "Please install Azure CLI from: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli" -ForegroundColor Yellow
        exit 1
    }

    # Check Azure login and get current user details
    Write-Host "Checking Azure login..." -ForegroundColor Yellow
    try {
        $account = az account show --output json 2>$null | ConvertFrom-Json
        if (-not $account) {
            Write-Host "Not logged in to Azure" -ForegroundColor Red
            Write-Host "Please run: az login" -ForegroundColor Yellow
            exit 1
        }
        Write-Host "Logged in as: $($account.user.name)" -ForegroundColor Green
    }
    catch {
        Write-Host "Not logged in to Azure" -ForegroundColor Red
        Write-Host "Please run: az login" -ForegroundColor Yellow
        exit 1
    }

    # Get the signed-in user's object ID and display name for SQL admin assignment
    Write-Host ""
    Write-Host "Resolving current user identity..." -ForegroundColor Yellow
    $currentUser = az ad signed-in-user show --output json 2>$null | ConvertFrom-Json
    if (-not $currentUser) {
        Write-Host "Failed to resolve signed-in user from Azure AD" -ForegroundColor Red
        exit 1
    }
    $currentUserObjectId = $currentUser.id
    $currentUserDisplayName = $currentUser.displayName
    Write-Host "Current user: $currentUserDisplayName ($currentUserObjectId)" -ForegroundColor Green

    # Set the current logged-in user as the SQL server AD administrator
    # This is required so we can run CREATE USER ... FROM EXTERNAL PROVIDER
    Write-Host ""
    Write-Host "Setting current user as SQL server AD administrator..." -ForegroundColor Yellow
    az sql server ad-admin create `
        --resource-group $ResourceGroupName `
        --server-name $sqlServerName `
        --display-name $currentUserDisplayName `
        --object-id $currentUserObjectId `
        --output none

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to set current user as SQL server AD admin" -ForegroundColor Red
        exit 1
    }
    Write-Host "Current user set as SQL server AD administrator" -ForegroundColor Green

    # Add a temporary firewall rule so the current user can connect from their local machine
    Write-Host ""
    Write-Host "Detecting public IP address..." -ForegroundColor Yellow
    $myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=text' -TimeoutSec 10).Trim()
    Write-Host "Public IP: $myIp" -ForegroundColor Green

    $firewallRuleName = 'AllowProvisioningScript'
    Write-Host "Adding temporary firewall rule '$firewallRuleName' for $myIp..." -ForegroundColor Yellow
    az sql server firewall-rule create `
        --resource-group $ResourceGroupName `
        --server $sqlServerName `
        --name $firewallRuleName `
        --start-ip-address $myIp `
        --end-ip-address $myIp `
        --output none

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to add firewall rule" -ForegroundColor Red
        exit 1
    }
    Write-Host "Temporary firewall rule added" -ForegroundColor Green

    # Wait a moment for the AD admin and firewall changes to propagate
    Write-Host "Waiting 30 seconds for admin and firewall changes to propagate..." -ForegroundColor Yellow
    Start-Sleep -Seconds 30

    # Function to create SQL user for managed identity (with retry logic for AD propagation)
    function Add-SqlManagedIdentityUser {
        param(
            [string]$ServerFqdn,
            [string]$DatabaseName,
            [string]$IdentityName,
            [string[]]$Roles
        )

        Write-Host "    Creating user '$IdentityName' in database: $DatabaseName" -ForegroundColor Gray
        Write-Host "    Roles: $($Roles -join ', ')" -ForegroundColor Gray

        # Build SQL commands: create user and assign requested roles
        $sqlLines = @("CREATE USER [$IdentityName] FROM EXTERNAL PROVIDER;")
        foreach ($role in $Roles) {
            $sqlLines += "ALTER ROLE $role ADD MEMBER [$IdentityName];"
        }
        $sqlCommands = $sqlLines -join "`n"

        # Retry loop — AD admin propagation can take longer than the initial wait
        $maxRetries = 5
        $retryDelaySec = 15

        for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
            try {
                # Get Azure AD access token for SQL
                $accessToken = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
                if (-not $accessToken) {
                    throw "Failed to acquire Azure AD access token for SQL."
                }

                $connectionString = "Server=$ServerFqdn;Database=$DatabaseName;Encrypt=True;TrustServerCertificate=False;"

                # Try Microsoft.Data.SqlClient first, fall back to System.Data.SqlClient
                $connection = $null
                try {
                    Add-Type -AssemblyName "Microsoft.Data.SqlClient" -ErrorAction Stop
                    $connection = New-Object Microsoft.Data.SqlClient.SqlConnection $connectionString
                }
                catch {
                    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
                }

                $connection.AccessToken = $accessToken
                $connection.Open()

                $commands = $sqlCommands -split ";\s*" | Where-Object { $_.Trim() -ne "" }
                foreach ($cmd in $commands) {
                    $sqlCmd = $connection.CreateCommand()
                    $sqlCmd.CommandText = $cmd
                    try {
                        $sqlCmd.ExecuteNonQuery() | Out-Null
                    }
                    catch {
                        # Ignore errors for already-existing users or role memberships
                        if ($_.Exception.Message -notmatch 'already exists|is already a member') {
                            throw $_
                        }
                        Write-Host "    (Skipped: $($_.Exception.Message))" -ForegroundColor DarkGray
                    }
                }

                $connection.Close()
                Write-Host "    User created successfully in $DatabaseName" -ForegroundColor Green
                return
            }
            catch {
                if ($attempt -lt $maxRetries -and $_.Exception.Message -match 'Login failed') {
                    Write-Host "    Attempt $attempt/$maxRetries failed (AD propagation likely). Retrying in ${retryDelaySec}s..." -ForegroundColor Yellow
                    Start-Sleep -Seconds $retryDelaySec
                }
                else {
                    Write-Host "    Warning: Failed to create user in ${DatabaseName}: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
        }
    }

    # Create SQL user for the deployment identity (db_owner for schema changes / migrations)
    Write-Host ""
    Write-Host "Creating SQL user for deployment identity (db_owner)..." -ForegroundColor Yellow

    Add-SqlManagedIdentityUser `
        -ServerFqdn $sqlServerFqdn `
        -DatabaseName $sqlDatabaseName `
        -IdentityName $deploymentIdentityName `
        -Roles @('db_owner')

    # Create SQL user for the app managed identity (data access only)
    Write-Host ""
    Write-Host "Creating SQL user for app managed identity (data reader/writer)..." -ForegroundColor Yellow

    Add-SqlManagedIdentityUser `
        -ServerFqdn $sqlServerFqdn `
        -DatabaseName $sqlDatabaseName `
        -IdentityName $appIdentityName `
        -Roles @('db_datareader', 'db_datawriter')

    # Remove the temporary firewall rule
    Write-Host ""
    Write-Host "Removing temporary firewall rule '$firewallRuleName'..." -ForegroundColor Yellow
    az sql server firewall-rule delete `
        --resource-group $ResourceGroupName `
        --server $sqlServerName `
        --name $firewallRuleName `
        --output none

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: Failed to remove temporary firewall rule '$firewallRuleName'. Please remove it manually." -ForegroundColor Yellow
    }
    else {
        Write-Host "Temporary firewall rule removed" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "SQL managed identity access assignment completed!" -ForegroundColor Green
    Write-Host "Current user ($currentUserDisplayName) remains as SQL server Entra administrator." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "SQL user creation failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please check the error details above and try again." -ForegroundColor Yellow
    exit 1
}
