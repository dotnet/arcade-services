param(
    [Parameter(Mandatory=$true)][string]$subscriptionName,
    [Parameter(Mandatory=$true)][string]$bicepparamFileName
)

az account set --subscription $subscriptionName

# creates a resource group `product-construction-service` in West US 2
az group create --name product-construction-service --location "West US 2"

$paramFile = Join-Path -Path $PSScriptRoot -ChildPath $bicepparamFileName
az deployment group create --resource-group product-construction-service --parameters $paramFile --name deploy