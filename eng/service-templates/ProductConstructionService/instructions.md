# Instructions for recreating the Product Construction Service
Using Azure CLI, run the following lines:
 - `az account set --subscription <subscription name/id>`
 - `az group create --location northcentralus --name product-construction-service`
 - `az deployment group create --template-file provision.bicep --resource-group product-construction-service`

This will create all of the necessary Azure resources.

The last part is setting up the pipeline:
 - Make sure all of the resources referenced in the yaml have the correct names
 - Make sure the variable group referenced in the yaml points to the new Key Vault

When creating a Container App with a bicep template, we have to give it some kind of boilerplate docker image, since our repository will be empty at the time of creation. Since we have a custom startup health probe, this revision will fail to activate. After the first run of the pipeline (deployment), make sure to deactivate the first, default revision.