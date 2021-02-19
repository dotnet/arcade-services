# Gettings started developing

## Getting started
1. Install Visual Studio 2019 with the '.NET Core' and 'Azure Development' workloads.
1. Install Azure Service Fabric SDK: https://www.microsoft.com/web/handlers/webpi.ashx?command=getinstallerredirect&appid=MicrosoftAzure-ServiceFabric-CoreSDK
1. Install SQL Server Express: https://www.microsoft.com/en-us/sql-server/sql-server-editions-express
1. Install Node.js LTS. When asked, at the end of installation, also opt-in for all necessary tools.
1. Acquire the required secrets from Azure key vault. This can be done by running [/src/Maestro/bootstrap.ps1](../src/Maestro/bootstrap.ps1) from an admin powershell window (note: the Powershell ISE may have problems running this script). This script will do three things:
    - Download a secret required for using the `Microsoft.Azure.Services.AppAuthentication` package from the service fabric local dev cluster
    - Download and install the SSL cert used for local development from key vault
    - Configure the SQL Server LocalDB instance for use from the local Service Fabric cluster
1. Make sure you have installed Entity Framework Core CLI by running `dotnet tool install --global dotnet-ef`
1. From the Maestro.Data project directory, run `dotnet ef --msbuildprojectextensionspath <full path to obj dir for Maestro repo (e.g. "C:\arcade-services\artifacts\obj\Maestro.Data\")> database update`.
    - Note that the generated files are in the root artifacts folder, not the artifacts folder within the Maestro.Data project folder
1. Join the @maestro-auth-test org in GitHub (you will need to ask someone to manually add you to the org).
1. In SQL Server Object Explorer in Visual Studio, find the local SQLExpress database for the build asset registry and populate the Repositories table with the following rows:

    1. 
        - Repository: https://github.com/maestro-auth-test/maestro-test
        - Installation Id: 289474
    1.  
        - Repository: https://github.com/maestro-auth-test/maestro-test2
        - Installation Id: 289474
    1. 
        - Repository: https://github.com/maestro-auth-test/maestro-test3
        - Installation Id: 289474
1. Run `.\Build.cmd -pack` at the root of the repo
1. Install ngrok from  https://ngrok.com/ or `choco install ngrok`
1. (optional - when darc is used) Run `ngrok http 8080` and then use the reported ngrok url for the --bar-uri darc argument

After successfully running `bootstrap.ps1` running the `MaestroApplication` project via F5 in VS (launch as elevated) will run the application on `http://localhost:8080`.

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
   1. MAESTRO_BASEURI : Run ngrok and get the https url

Generally this is easiest if you want to debug both sides to simply have two instances of Visual Studio 2019 running. See below if you do not have the file paths mentioned for DARC_PACKAGE_SOURCE

Since Visual Studio's Debug Environment variable settings do not apply to debugging tests, you'll need to set them, preferably from a command prompt:

1. Start command prompt
1. set AZDO_TOKEN=...
1. set GITHUB_TOKEN=...
1. set MAESTRO_TOKEN=...
1. set DARC_PACKAGE_SOURCE=...
1. set MAESTRO_BASEURI=https://blahblahblah.ngrok.io
1. "C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\Common7\IDE\devenv.com" (adjusted for your VS version / drive)

When debugging the tests, you can check this via the Immediate window, e.g. by running `System.Environment.GetEnvironmentVariable("GITHUB_TOKEN")`


## Troubleshooting

Things to try:
- If Visual Studio hangs, edit the properties of MaestroApplication and change `Application Debug Mode` to `Remove Application`
- If Visual Studio still hangs, under Debug->Options, uncheck "Enable Diagnostic Tools while debugging"
- Clean your repository before building/running. (e.g. cd to repo, `git clean -xdf`)
- Ensure the ASP.NET Workload is installed for Visual Studio.
- If you are using your own PATs for debugging tests, your account needs to have permissions to the repositories involved and include appropriate scopes (default is just 'public')
- To build the packages folder starting from a clean repository, run **desktop** `nuget restore arcade-services.sln` then **desktop** `msbuild arcade-services.sln /t:Restore,Build,Pack`.  Non-desktop versions of build, including dotnet.exe, will fail building the .sfproj projects and then not pack the nupkgs.
