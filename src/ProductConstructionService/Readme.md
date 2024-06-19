# Starting the service locally

If you're running the service from VS, install the latest Preview VS. Be sure to install the `Azure Development => .NET Aspire SDK (Preview)` optional workload in the VS installer.

If you're building the project using the command line, run `dotnet workload install aspire` or `dotnet workload update` to install/update the aspire workload.

To run the Product Construction Service locally, set the `ProductConstructionService.AppHost` as Startup Project, and run with F5.

When running locally:
 - The service will attempt to read secrets from the [`ProductConstructionDev`](https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/resource/subscriptions/cab65fc3-d077-467d-931f-3932eabf36d3/resourceGroups/product-construction-service/providers/Microsoft.KeyVault/vaults/ProductConstructionDev/overview) KeyVault, using your Microsoft credentials. If you're having some authentication double check the following:
    - In VS, go to `Tools -> Options -> Azure Service Authentication -> Account Selection` and make sure your corp account is selected
    - Check your environmental variables, you might have `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` set, and the `DefaultAzureCredential` is attempting to use `EnvironmentalCredentials` for an app that doesn't have access to the dev KV.
 - The service is configured to use the same SQL Express database Maestro uses. To se it up, follow the [instructions](https://github.com/dotnet/arcade-services/blob/main/docs/DevGuide.md)
 - Configure the `ProductConstructionService.AppHost` launchSettings.json file:
   - `VmrUri`: URI of the VMR that will be targeted by the service.
   - `VmrPath`: path to the cloned [VMR](https://github.com/dotnet/dotnet) on your machine.
   - `TmpPath`: path to the TMP folder that the service will use to clone other repos (like runtime). If you've already worked with the VMR and have the TMP VMR folder on your machine, you can point the service there and it will reuse the cloned repos you already have.
   - Set the `ASPIRE_ALLOW_UNSECURED_TRANSPORT` environmental variable to `true` to allow the service to run without HTTPS. This is useful when running locally, but should not be used in production.
   - The local config should look something like this:
    ```json
    {
        "$schema": "http://json.schemastore.org/launchsettings.json",
        "profiles": {
            "PCS (local)": {
                "commandName": "Project",
                "dotnetRunMessages": true,
                "launchBrowser": true,
                "applicationUrl": "http://localhost:18848",
                "environmentVariables": {
                    "VmrPath": "D:\\tmp\\vmr",
                    "TmpPath": "D:\\tmp\\",
                    "VmrUri": "https://github.com/maestro-auth-test/dnceng-vmr",
                    "ASPIRE_ALLOW_UNSECURED_TRANSPORT": "true",
                    "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL": "http://localhost:19265",
                    "DOTNET_RESOURCE_SERVICE_ENDPOINT_URL": "http://localhost:20130"
                }
            }
        }
    }
    ```

# Instructions for recreating the Product Construction Service
Run the `provision.ps1` script by giving it the name of the subscription you want to create the service in. Note that keyvault and container registry names have to be unique on Azure, so you'll have to change these, or delete and purge the existing ones.

This will create all of the necessary Azure resources.

We're using a Managed Identity to authenticate PCS to BAR. You'll need to run the following SQL queries to enable this (we can't run SQL from bicep):
 - CREATE USER [`ManagedIdentityName`] FROM EXTERNAL PROVIDER
 - ALTER ROLE db_datareader ADD MEMBER [`ManagedIdentityName`]
 - ALTER ROLE db_datawriter ADD MEMBER [`ManagedIdentityName`]
 
If the service is being recreated and the same Managed Identity name is reused, you will have to drop the old MI from the BAR, and then run the SQL queries above

Once the resources are created and configured:
 - Go to the newly created User Assigned Managed Identity (the one that's assigned to the container app, not the deployment one)
 - Copy the Client ID, and paste it in the correct appconfig.json, under `ManagedIdentityClientId`
 - Add this identity as a user to AzDo so it can get AzDo tokens (you'll need a saw for this). You might have to remove the old user identity before doing this
 - Update the `ProductConstructionServiceDeploymentProd` (or `ProductConstructionServiceDeploymentInt`) Service Connection with the new MI information (you'll also have to create a Federated Credential in the MI)
 - Update the default PCS URI in `ProductConstructionServiceApiOptions`.

We're not able to configure a few Kusto things in bicep:
 - Give the PCS Managed Identity the permissions it needs:
    - Go to the Kusto Cluster, and select the database you want the MI to have access to
    - Go to permissions -> Add -> Viewer and select the newly created PCS Managed Identity
 - Create a private endpoint between the Kusto cluster and PCS
    - Go to the Kusto cluster -> Networking -> Private endpoint connections -> + Private endpoint
    - Select the appropriate subscription and resource group. Name the private endpoint something meaningful, like `pcs-kusto-private-connection`
    - On the Resource page, set the `Target sub-resource` to `cluster`
    - On the Virtual Network page, select the product-construction-service-vntet-int/prod, and the private-endpoints-subnet, leave the rest as default
    - leave the rest of the settings as default

The last part is setting up the pipeline:
 - Make sure all of the resources referenced in the yaml have the correct names
 - Make sure the variable group referenced in the yaml points to the new Key Vault

When creating a Container App with a bicep template, we have to give it some kind of boilerplate docker image, since our repository will be empty at the time of creation. Since we have a custom startup health probe, this revision will fail to activate. After the first run of the pipeline (deployment), make sure to deactivate the first, default revision.

# General deployment notes

The Product Construction Service uses the [Blue-Green](https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment?pivots=bicep) deployment approach, implemented in the [product-construction-service-deploy.ps1](https://github.com/dotnet/arcade-services/blob/main/eng/deployment/product-construction-service-deploy.ps1) script. The script does the following:
 - Figures out the label that should be assigned to the new revision and removes it from the old, inactive revision.
 - Tells the currently active revision to stop processing new jobs and waits for the new one to finish.
 - Deploys the new revision.
 - Waits for the Health Probes to be successful. We currently have two health probes:
   - A Startup probe, that is run after the service is started. This probe just tests if the service is responsive.
   - A Readiness probe that waits for the service to fully initialize. Currently, this is probe just waits for the VMR to be cloned on the containerapp disk.
 - Assigns the correct label to the new revision, and switches all traffic to it.
 - Starts the JobProcessor once the service is ready.
 - If there are any failures during the deployment, the old revision is started, and the deployment is cleaned up.