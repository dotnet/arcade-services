# Gettings started developing

## Getting started
1. Install Visual Studio 2019 with the '.NET Core', 'Desktop Development with C++'. and 'Azure Development' workloads.
1. Install Azure Service Fabric SDK: https://www.microsoft.com/web/handlers/webpi.ashx?command=getinstallerredirect&appid=MicrosoftAzure-ServiceFabric-CoreSDK
1. Install SQL Server Express: https://www.microsoft.com/en-us/sql-server/sql-server-downloads
1. Install Node.js LTS. When asked, at the end of installation, also opt-in for all necessary tools.
1. Acquire the required secrets from Azure key vault. This can be done by running [/src/Maestro/bootstrap.ps1](../src/Maestro/bootstrap.ps1) from an admin powershell window (note: the Powershell ISE may have problems running this script). This script will do three things:
    - Download a secret required for using the `Microsoft.Azure.Services.AppAuthentication` package from the service fabric local dev cluster
    - Download and install the SSL cert used for local development from key vault
    - Configure the SQL Server LocalDB instance for use from the local Service Fabric cluster
1. Make sure you have installed Entity Framework Core CLI by running `dotnet tool install --global dotnet-ef`
1. From the `src\Maestro\Maestro.Data` project directory, run `dotnet ef --msbuildprojectextensionspath <full path to obj dir for Maestro repo (e.g. "C:\arcade-services\artifacts\obj\Maestro.Data\")> database update`.
    - Note that the generated files are in the root artifacts folder, not the artifacts folder within the Maestro.Data project folder
1. Join the @maestro-auth-test org in GitHub (you will need to ask someone to manually add you to the org).
1. In SQL Server Object Explorer in Visual Studio, find the local SQLExpress database for the build asset registry and populate the Repositories table with the following rows:

  ```sql
  INSERT INTO [Repositories] (RepositoryName, InstallationId) VALUES
      ('https://github.com/maestro-auth-test/maestro-test', 289474),
      ('https://github.com/maestro-auth-test/maestro-test2', 289474),
      ('https://github.com/maestro-auth-test/maestro-test3', 289474),
      ('https://github.com/maestro-auth-test/dnceng-vmr', 289474);
  ```

1. Run `.\Build.cmd -pack` at the root of the repo
1. Install ngrok from  https://ngrok.com/ or `choco install ngrok`
1. (optional - when darc is used) Run `ngrok http 8080` and then use the reported ngrok url for the --bar-uri darc argument

After successfully running `bootstrap.ps1` running the `MaestroApplication` project via F5 in VS (launch as elevated) will run the application on `http://localhost:8080`.

It seems that calling `bootstrap.ps1` is not a one-time operation and needs to be called every time you restart your machine.

### Local developer workflow

The guaranteed way (some steps might be extraneous but assured to work) to successfully (re-)deploy Maestro locally after you're iterating on the code is to:
- Make sure you have run `bootstrap.ps1` after the last reboot
- Reset the local SF cluster (`Service Fabric Local Cluster Manager` -> `Reset Local Cluster`)
- Start the VS in Administrator mode
- Start the `MaestroApplication` project in VS

In case you need to also run PCS locally,
- either run `dotnet run` in `src/ProductConstructionService/ProductConstructionService.AppHost`,
- or open another instance of VS and F5 the `ProductConstructionService.AppHost` project.

#### How to tell it's done
- The Build log (in VS) shows
  ```
  3>Finished executing script 'Deploy-FabricApplication.ps1'
  ```
- The Service Fabric Tools log shows
  ```
  Something is taking too long, the application is still not ready.
  Finished executing script 'Get-ServiceFabricApplicationStatus'.
  Time elapsed: 00:00:41.8014175
  The URL for the launch target is not set or is not an HTTP/HTTPS URL so the browser will not be opened.
  ```
- You can open `http://127.0.0.1:8088/swagger`

## Azure AppConfiguration

Maestro.Web uses Azure AppConfiguration (AAC) to dynamically enable/disable automatic build to channel assignment. AAC works basically as a KeyVault, however it doesn't need to necessarily store secrets. We use Azure Managed Service Identity (AMSI) to authenticate to AAC. 

##### Useful resources about AAC:

- https://docs.microsoft.com/en-us/azure/azure-app-configuration/overview
- https://zimmergren.net/introduction-azure-app-configuration-store-csharp-dotnetcore/
- https://docs.microsoft.com/en-us/azure/azure-app-configuration/howto-integrate-azure-managed-service-identity

## Running scenario tests against a local cluster

If you want to run the C# scenario tests (make sure that you followed the getting started steps before), you will need to set some environment variables:

   1. GITHUB_TOKEN : Get a github PAT from https://github.com/settings/tokens
   1. AZDO_TOKEN Get a Azure DevOps PAT from https://dnceng.visualstudio.com/_usersSettings/tokens (or appropriately matching project name if not dnceng)
   1. MAESTRO_TOKEN : Get a maestro bearer token (you can create one after running maestro app)
   1. DARC_PACKAGE_SOURCE : Get the path to the darc nuget package (which would be in `arcade-services\artifacts\packages\Debug\NonShipping\`, see below for getting this built)
   1. MAESTRO_BASEURIS : Run ngrok and get the https url

Generally this is easiest if you want to debug both sides to simply have two instances of Visual Studio 2019 running. See below if you do not have the file paths mentioned for DARC_PACKAGE_SOURCE

Since Visual Studio's Debug Environment variable settings do not apply to debugging tests, you'll need to set them, preferably from a command prompt:

1. Start command prompt
1. set AZDO_TOKEN=...
1. set GITHUB_TOKEN=...
1. set MAESTRO_TOKEN=...
1. set DARC_PACKAGE_SOURCE=...
1. set MAESTRO_BASEURIS=https://blahblahblah.ngrok.io
1. "C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\Common7\IDE\devenv.com" (adjusted for your VS version / drive)

When debugging the tests, you can check this via the Immediate window, e.g. by running `System.Environment.GetEnvironmentVariable("GITHUB_TOKEN")`

## Changing the database model

In case you need to change the database model (e.g. add a column to a table), follow the usual [EF Core code-first migration steps](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli#evolving-your-model).
In practice this means the following:
1. Make the change to the model classes in the `Maestro.Data` project
1. Install the [EF Core CLI](https://docs.microsoft.com/en-us/ef/core/cli/dotnet)
  ```ps1
  dotnet tool install --global dotnet-ef
  ```
1. Go to the `src\Maestro\Maestro.Data` project directory and build the project
  ```ps1
  cd src\Maestro\Maestro.Data
  dotnet build
  ```
1. Run the following command to create a new migration
  ```ps1
  dotnet ef --msbuildprojectextensionspath <full path to obj dir for Maestro repo (e.g. "C:\arcade-services\artifacts\obj\Maestro.Data")> migrations add <migration name>
  ```

The steps above will produce a new migration file which will later be processed by the CI pipeline on the real database.  
Test this migration locally by running:
```ps1
dotnet ef --msbuildprojectextensionspath <full path to obj dir for Maestro repo (e.g. "C:\arcade-services\artifacts\obj\Maestro.Data")> database update
```
You should see something like:
```
Build started...
Build succeeded.
Applying migration '20240201144006_<migration name>'.
Done.
```

If your change of the model changes the public API of the service, update also the `Maestro.Client`.

## Changing the API / Updating `Maestro.Client`

`Maestro.Client` is an auto-generated client library for the Maestro API. It is generated using Swagger Codegen which parses the `swagger.json` file.
The `swagger.json` file is accessible at `/api/swagger.json` when the Maestro application is running.
If you changed the API (e.g. changed an endpoint, model coming from the API, etc.), you need to regenerate the `Maestro.Client` library.

If you need to update the client library, follow these steps:

1. Change the model/endpoint/..
1. Change `src\Maestro\Client\src\Microsoft.DotNet.Maestro.Client.csproj` and point the `SwaggerDocumentUri` to `http://127.0.0.1:8088/api/swagger.json`.
1. Start the Maestro application locally, verify you can access the swagger.json file. You can now stop debugging, the local SF cluster will keep running.
1. Run `src\Maestro\Client\src\generate-client.cmd` which will regenerate the C# classes.
1. You might see code-style changes in the C# classes as the SDK of the repo has now been updated. You can quickly use the Visual Studio's refactorings to fix those and minimize the code changes in this project.

## Troubleshooting

Things to try:
- If Visual Studio hangs, edit the properties of MaestroApplication and change `Application Debug Mode` to `Remove Application`
- If Visual Studio still hangs, under Debug->Options, uncheck "Enable Diagnostic Tools while debugging"
- Clean your repository before building/running. (e.g. cd to repo, `git clean -xdf`)
- Ensure the ASP.NET Workload is installed for Visual Studio.
- If you are using your own PATs for debugging tests, your account needs to have permissions to the repositories involved and include appropriate scopes (default is just 'public')
- To build the packages folder starting from a clean repository, run **desktop** `nuget restore arcade-services.sln` then **desktop** `msbuild arcade-services.sln /t:Restore,Build,Pack`.  Non-desktop versions of build, including dotnet.exe, will fail building the .sfproj projects and then not pack the nupkgs.
- Seeing errors like
    ```
    EXEC : gyp verb `which` failed error : not found: python2 [E:\gh\chcosta\arcade-services\src\Maestro\maestro-angular\maestro-angular.proj]
    EXEC : gyp verb `which` failed  python2 error : not found: python2 [E:\gh\chcosta\arcade-services\src\Maestro\maestro-angular\maestro-angular.proj]
    ```
    Make sure python 2.7 is installed and on your path.  You may have to copy `python27\python.exe` to `python27\python2.exe`
- if DNS Service is enabled for the cluster, Service Fabric will automaticaly edit the DNS settings for your machine and add SF as first DNS server which sometimes causes issues with DNS resolution and you may not be able to access any webpages. There is information about the issue in https://github.com/microsoft/service-fabric/issues/124

## Configuring a dev cluster
The script that sets up a local Service Fabric cluster is `C:\Program Files\Microsoft SDKs\Service Fabric\ClusterSetup\DevClusterSetup.ps1`
Configuration files are in the folders `Secure\OneNode`, `Secure\FiveNode`, `NonSecure\OneNode`, `NonSecure\FiveNode` depending if the cluster is secure/nonsecure and the number of nodes. By default the local cluster is not secure. Once the cluster is set up all the configuration is stored in `C:\SFDevCluster\Data`, you can see logs in `C:\SFDevCluster\Log`
You can disable the DNS Service by deleting `DnsService` from the add-on features in `ClusterManifestTemplate.json`
```
    "addOnFeatures": [
      "EventStoreService",
      "DnsService"
    ]
```
If you change any settings in `ClusterManifestTemplate.json` run `Reset Local Cluster` from Service Fabric Local Cluster Manager to recreate the cluster configuration using the new settings
