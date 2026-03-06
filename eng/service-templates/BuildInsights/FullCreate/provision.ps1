param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('prod', 'int', 'dev')]
    [string]$environment
)

# Load environment configuration from shared config file
$configFile = Join-Path -Path $PSScriptRoot -ChildPath 'environment-config.json'
$allConfig = Get-Content $configFile -Raw | ConvertFrom-Json
$environmentConfig = $allConfig.$environment
if (-not $environmentConfig) {
    Write-Host "No configuration found for environment '$environment' in $configFile" -ForegroundColor Red
    exit 1
}

# switch to the target subscription
Write-Host "Switching to subscription: $($environmentConfig.subscriptionId)"
$subscriptionId = $environmentConfig.subscriptionId
$resourceGroupName = $environmentConfig.resourceGroupName
$currentSubscriptionId = az account show --query id --output tsv
if (-not $currentSubscriptionId.Equals($subscriptionId, [System.StringComparison]::OrdinalIgnoreCase)) {
    az account set --subscription $subscriptionId
}

# check if the resource group exists, if not create it
Write-Host "Checking if resource group '$resourceGroupName' exists"
$resourceGroupExists = az group exists --name $resourceGroupName
if ($resourceGroupExists -eq 'false') {
    Write-Host "Creating resource group '$resourceGroupName'"
    az group create --name $resourceGroupName --location 'West US 2'
}

# deploy the bicep template with the parameters from the environment config
$paramFile = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath 'environments') -ChildPath $environmentConfig.bicepParamFileName
Write-Host "Using parameter file: $paramFile"
az deployment group create --resource-group $resourceGroupName --parameters $paramFile --name deploy

if ($LASTEXITCODE -ne 0) {
    Write-Host "Deployment failed. Skipping SQL access assignment." -ForegroundColor Red
    exit 1
}

$sqlAccessScript = Join-Path -Path $PSScriptRoot -ChildPath 'assign-managed-identity-sql-access.ps1'
Write-Host "Assigning managed identity SQL access..."
& $sqlAccessScript -Environment $environment
