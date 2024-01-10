param[
    [Parameter(Mandatory=$true)][string]$subscriptionName
]

az account set --subscription $subscriptionName

# creates a resource group `product-construction-service` in North Central US
az group create --name product-construction-service --location "North Central US"

az deployment group create --resource-group product-construction-service --template-file ./provision.bicep