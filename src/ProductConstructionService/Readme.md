# Starting the service locally

To run the Product Construction Service locally, set the `ProductConstructionService.AppHost` as Startup Project, and run with F5.

When running locally:
 - The service will attempt to read secrets from the [`ProductConstructionDev`](https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/resource/subscriptions/cab65fc3-d077-467d-931f-3932eabf36d3/resourceGroups/product-construction-service/providers/Microsoft.KeyVault/vaults/ProductConstructionDev/overview) KeyVault, using your Microsoft credentials. If you're having some authentication double check the following:
    - In VS, go to `Tools -> Options -> Azure Service Authentication -> Account Selection` and make sure your corp account is selected
    - Check your environmental variables, you might have `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` set, and the `DefaultAzureCredential` is attempting to use `EnvironmentalCredentials` for an app that doesn't have access to the dev KV.
 - The service is configured to use the same SQL Express database Maestro uses. To se it up, follow the [instructions](https://github.com/dotnet/arcade-services/blob/main/docs/DevGuide.md)

# Instructions for recreating the Product Construction Service
Run the `provision.ps1` script by giving it the name of the subscription you want to create the service in. Note that keyvault and container registry names have to be unique on Azure, so you'll have to change these, or delete and purge the existing ones.

This will create all of the necessary Azure resources.

The last part is setting up the pipeline:
 - Make sure all of the resources referenced in the yaml have the correct names
 - Make sure the variable group referenced in the yaml points to the new Key Vault

When creating a Container App with a bicep template, we have to give it some kind of boilerplate docker image, since our repository will be empty at the time of creation. Since we have a custom startup health probe, this revision will fail to activate. After the first run of the pipeline (deployment), make sure to deactivate the first, default revision.