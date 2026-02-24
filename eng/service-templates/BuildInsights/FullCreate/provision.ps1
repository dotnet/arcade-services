param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('prod', 'stage', 'dev')]
    [string]$environment
)

$environmentConfig = switch ($environment) {
    'prod' {
        @{
            bicepParamFileName = 'prod.bicepparam';
            subscriptionId     = '00000000-0000-0000-0000-000000000001';
            resourceGroupName  = 'build-insights-rg'
        }
    }
    'stage' {
        @{
            bicepParamFileName = 'stage.bicepparam';
            subscriptionId     = 'e6b5f9f5-0ca4-4351-879b-014d78400ec2'; # .NET Product Construction Services - Staging
            resourceGroupName  = 'build-insights-stage-rg'
        }
    }
    'dev' {
        @{
            bicepParamFileName = 'dev.bicepparam';
            subscriptionId     = '3fd7c137-8faa-4309-9822-de5701a6dd7a'; # .NET Release Infrastructure - Dev
            resourceGroupName  = 'build-insights-dev-rg'
        }
    }
}

# switch to the target subscription
Write-Host "Switching to subscription: $($environmentConfig.subscriptionId)"
$subscriptionId = $environmentConfig.subscriptionId
$currentSubscriptionId = az account show --query id --output tsv
if (-not $currentSubscriptionId.Equals($subscriptionId, [System.StringComparison]::OrdinalIgnoreCase)) {
    az account set --subscription $subscriptionId
}

# check if the resource group exists, if not create it
Write-Host "Checking if resource group '$($environmentConfig.resourceGroupName)' exists"
$resourceGroupExists = az group exists --name $environmentConfig.resourceGroupName
if ($resourceGroupExists -eq 'false') {
    Write-Host "Creating resource group '$($environmentConfig.resourceGroupName)'"
    az group create --name $environmentConfig.resourceGroupName --location 'West US 2'
}

# deploy the bicep template with the parameters from the environment config
$paramFile = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath 'environments') -ChildPath $environmentConfig.bicepParamFileName
Write-Host "Using parameter file: $paramFile"
az deployment group create --resource-group $environmentConfig.resourceGroupName --parameters $paramFile --name deploy
