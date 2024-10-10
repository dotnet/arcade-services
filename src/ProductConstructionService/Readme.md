# Getting started with local development

1. Install the latest Preview VS.
  - Be sure to install the `Azure Development => .NET Aspire SDK (Preview)` optional workload in the VS installer.
  - If you're building the project using the command line, run `dotnet workload install aspire` or `dotnet workload update` to install/update the aspire workload.
1. Install SQL Server Express: https://www.microsoft.com/en-us/sql-server/sql-server-downloads
1. Install Node.js LTS. When asked, at the end of installation, also opt-in for all necessary tools.
1. Install Entity Framework Core CLI by running `dotnet tool install --global dotnet-ef`
1. From the `src\Maestro\Maestro.Data` project directory, run `dotnet ef --msbuildprojectextensionspath <full path to obj dir for Maestro repo (e.g. "C:\arcade-services\artifacts\obj\Maestro.Data\")> database update`.
    - Note that the generated files are in the root artifacts folder, not the artifacts folder within the Maestro.Data project folder
1. Join the `maestro-auth-test` org in GitHub (you will need to ask someone to manually add you to the org).
1. Make sure you can read the `ProductConstructionDev` keyvault. If you can't, ask someone to add you to the keyvault.
1. In SQL Server Object Explorer in Visual Studio, find the local SQLExpress database for the build asset registry and populate the Repositories table with the following rows:

  ```sql
  INSERT INTO [Repositories] (RepositoryName, InstallationId) VALUES
      ('https://github.com/maestro-auth-test/maestro-test', 289474),
      ('https://github.com/maestro-auth-test/maestro-test2', 289474),
      ('https://github.com/maestro-auth-test/maestro-test3', 289474),
      ('https://github.com/maestro-auth-test/arcade', 289474),
      ('https://github.com/maestro-auth-test/dnceng-vmr', 289474);
  ```
1. Install Docker Desktop: https://www.docker.com/products/docker-desktop

# Configuring the service for local runs

When running locally:
 - The service will attempt to read secrets from the [`ProductConstructionDev`](https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/resource/subscriptions/cab65fc3-d077-467d-931f-3932eabf36d3/resourceGroups/product-construction-service/providers/Microsoft.KeyVault/vaults/ProductConstructionDev/overview) KeyVault, using your Microsoft credentials. If you're having some authentication double check the following:
    - In VS, go to `Tools -> Options -> Azure Service Authentication -> Account Selection` and make sure your corp account is selected
    - Check your environmental variables, you might have `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` set, and the `DefaultAzureCredential` is attempting to use `EnvironmentalCredentials` for an app that doesn't have access to the dev KV.
 - The service is configured to use the same SQL Express database Maestro uses. To se it up, follow the [instructions](https://github.com/dotnet/arcade-services/blob/main/docs/DevGuide.md)
 - Configure the `ProductConstructionService.AppHost/Properties/launchSettings`.json file:
   - `VmrUri`: URI of the VMR that will be targeted by the service.
   - `VmrPath`: path to the cloned [VMR](https://github.com/dotnet/dotnet) on your machine.
   - `TmpPath`: path to the TMP folder that the service will use to clone other repos (like runtime). If you've already worked with the VMR and have the TMP VMR folder on your machine, you can point the service there and it will reuse the cloned repos you already have.
   - AppHost's `launchSettings.json` config should look something like this (fill in the VMR paths):
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
                    "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL": "http://localhost:19265",
                    "DOTNET_RESOURCE_SERVICE_ENDPOINT_URL": "http://localhost:20130"
                }
            }
        }
    }
    ```
    - Modify the `ProductConstructionService.Api/Properties/launchSettings.json` config should look something like this (fill in the VMR paths):
    ```json
    {
        "$schema": "http://json.schemastore.org/launchsettings.json",
        "profiles": {
            "ProductConstructionService.Api": {
                "commandName": "Project",
                "launchBrowser": true,
                "applicationUrl": "https://localhost:53180",
                "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "Development",
                    "VmrPath": "D:\\tmp\\dnceng-vmr",
                    "TmpPath": "D:\\tmp\\",
                    "VmrUri": "https://github.com/maestro-auth-test/dnceng-vmr"
                }
            }
        }
    }

# Running the service locally

To run the Product Construction Service locally:
1. Start Docker Desktop.
2. Publish the `ProductConstructionService.BarViz` project to a local folder (use the `BarVizLocalPublish` profile).
    - Right click the project
    - Publish...
    - Publish button on the profile
3. Set the `ProductConstructionService.AppHost` as Startup Project, and run with F5.

# Debugging the front-end locally

In order to debug the Blazor project, you need to run the server (the `ProductConstructionService.AppHost` project) and the front-end separately. The front-end will be served from a different port but will still be able to communicate with the local server.

- Start Docker
- Run the `ProductConstructionService.AppHost` project (without debugging)
- Debug the `ProductConstructionService.BarViz` project

It is also recommended to turn on the API redirection (in `src\ProductConstructionService\ProductConstructionService.Api\appsettings.Development.json`) to point to the production so that the front-end has data to work with:

```json
{
  "ApiRedirect": {
    "Uri": "https://maestro.dot.net/"
  }
}
```

# Running the Scenario Tests locally

After you completed the steps to run the service locally, you can run the scenario tests against your local instance too:
- Right-click the `ProductConstructionService.ScenarioTests` project and select `Manage User Secrets` and add the following content:
    ```json
    {
    "PCS_BASEURI": "https://localhost:53180/",
    "GITHUB_TOKEN": "[FILL SAME TOKEN AS YOU WOULD FOR DARC]",
    "DARC_PACKAGE_SOURCE": "[full path to your arcade-services]\\artifacts\\packages\\Debug\\NonShipping",
    "DARC_VERSION": "0.0.99-dev"
    }
    ```
- Build the Darc tool locally (it is run by the scenario tests):
    ```ps
    cd src\Microsoft.DotNet.Darc\Darc
    dotnet pack -c Debug
    ```
- Open two Visual Studio instances.
- In the first instance, run the PCS service (instructions above).
- In the second instance, run any of the `ProductConstructionService.ScenarioTests` tests.
- After you have run the tests or the service locally, your local git credential manager might populate with the `dotnet-maestro-bot` account. You can log out of it by running `git credential-manager github logout dotnet-maestro-bot`.

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
 - Add this identity as a user to AzDO so it can get AzDO tokens (you'll need a saw for this). You might have to remove the old user identity before doing this
   - It needs to be able to manage code / pull requests and manage feeds (this is done in the artifact section).
 - Update the `ProductConstructionServiceDeploymentProd` (or `ProductConstructionServiceDeploymentInt`) Service Connection with the new MI information (you'll also have to create a Federated Credential in the MI)
 - Update the default PCS URI in `ProductConstructionServiceApiOptions`.

We're not able to configure a few Kusto things in bicep:
 - Give the PCS Managed Identity the permissions it needs:
    - Go to the Kusto Cluster, and select the database you want the MI to have access to
    - Go to permissions -> Add -> Viewer and select the newly created PCS Managed Identity

Give the Deployment Identity Reader role for the whole subscription.

The last part is setting up the pipeline:
 - Make sure all of the resources referenced in the yaml have the correct names
 - Make sure the variable group referenced in the yaml points to the new Key Vault

When creating a Container App with a bicep template, we have to give it some kind of boilerplate docker image, since our repository will be empty at the time of creation. Since we have a custom startup health probe, this revision will fail to activate. After the first run of the pipeline (deployment), make sure to deactivate the first, default revision.

# General deployment notes

The Product Construction Service uses the [Blue-Green](https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment?pivots=bicep) deployment approach, implemented in the [ProductConstructionService.Deployment](https://github.com/dotnet/arcade-services/tree/main/src/ProductConstructionService/ProductConstructionService.Deployment) script. The script does the following:
 - Figures out the label that should be assigned to the new revision and removes it from the old, inactive revision.
 - Tells the currently active revision to stop processing new jobs and waits for the new one to finish.
 - Deploys the new revision.
 - Waits for the Health Probes to be successful. We currently have two health probes:
   - A Startup probe, that is run after the service is started. This probe just tests if the service is responsive.
   - A Readiness probe that waits for the service to fully initialize. Currently, this is probe just waits for the VMR to be cloned on the containerapp disk.
 - Assigns the correct label to the new revision, and switches all traffic to it.
 - Starts the JobProcessor once the service is ready.
 - If there are any failures during the deployment, the old revision is started, and the deployment is cleaned up.

# Debugging

## Getting container logs (when service does not start)

When the service does not start and you can't see the logs in the usual Application Insights place, you can still get the container app logs for the given revision. You can get the (short) SHA of the commit that you tried to deploy and find the logs with this query:

```kql
ContainerAppConsoleLogs_CL
| where RevisionName_s contains "4c7e5db50e"
```

## Exploring the container images

You can explore or locally run the container images that are being deployed to the Azure Container App.

1. Find the image that you want to explore by checking the revisions in the Azure Container App.
2. Run the following command to pull the image locally (replace the image tag):

```ps
az acr login --name productconstructionint
docker run --rm --entrypoint "/bin/sh" -it productconstructionint.azurecr.io/product-construction-service.api:2024081411-1-87a5bcb35f-dev
```
