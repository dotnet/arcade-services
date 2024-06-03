param(
    [Parameter(Mandatory=$true)][string]$subscriptionName
)

az account set --subscription $subscriptionName

# creates a resource group `product-construction-service` in West US 2
az group create --name product-construction-service --location "West US 2"

$provisionFilePath = Join-Path -Path $PSScriptRoot -ChildPath "provision.bicep"
az deployment group create --resource-group product-construction-service --template-file $provisionFilePath --name deploy