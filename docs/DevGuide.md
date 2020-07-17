# Gettings started developing

## Getting started
1. Install Visual Studio 2019 with the .NET Core and Azure Devlopment workloads.
1. Install Azure Service Fabric SDK: https://www.microsoft.com/web/handlers/webpi.ashx?command=getinstallerredirect&appid=MicrosoftAzure-ServiceFabric-CoreSDK
1. Install SQL Server Express: https://www.microsoft.com/en-us/sql-server/sql-server-editions-express
1. Install Node.js LTS. When asked, at the end of installation, also option-in for all necessary tools.
1. Acquire the required secrets from azure key vault. This can be done by running [/src/Maestro/bootstrap.ps1](../src/Maestro/bootstrap.ps1) from an admin powershell window (note: the Powershell ISE may have problems running this script). This script will do 3 things:
    - Download a secret required for using the `Microsoft.Azure.Services.AppAuthentication` package from the service fabric local dev cluster
    - Download and install the SSL cert used for local development from key vault
    - Configure the SQL Server LocalDB instance for use from the local service fabric cluster
1. Make sure you have installed Entity Framework Core CLI by running `dotnet tool install --global dotnet-ef`
1. From the Maestro.Data project directory, run `dotnet ef --msbuildprojectextensionspath <full path to obj dir for Maestro repo (e.g. "C:\arcade-services\artifacts\obj\Maestro.Data\")> database update`.
    - Note that the generated files are in the root artifacts folder, not the artifacts folder within the Maestro.Data project folder
1. Join the @maestro-auth-test org in GitHub (you will need to ask someone to manually add you to the org).
1. In SQL Server Object Explorer in Visual Studio, find the local SQLExpression database for the build asset registry and populate the Repositories table with the following rows:

    1. 
        - Repository: https://github.com/maestro-auth-test/maestro-test
        - Installation Id: 289474
    1.  
        - Repository: https://github.com/maestro-auth-test/maestro-test2
        - Installation Id: 289474
    1. 
        - Repository: https://github.com/maestro-auth-test/maestro-test3
        - Installation Id: 289474

After successfully running `bootstrap.ps1` running the `MaestroApplication` project via F5 in VS (launch as elevated) will run the application on `http://localhost:8080`

## Azure AppConfiguration

Maestro.Web uses Azure AppConfiguration (AAC) to dynamically enable/disable automatic build to channel assignment. AAC works basically as a KeyVault, however it doesn't need to necessarily store secrets. We use Azure Managed Service Identity (AMSI) to authenticate to AAC. 

##### Useful resources about AAC: 

- https://docs.microsoft.com/en-us/azure/azure-app-configuration/overview
- https://zimmergren.net/introduction-azure-app-configuration-store-csharp-dotnetcore/
- https://docs.microsoft.com/en-us/azure/azure-app-configuration/howto-integrate-azure-managed-service-identity

## Troubleshooting

Things to try:
- If Visual Studio hangs, edit properties of MastroApplication and change `Aplication Debug Mode` to `Remove Application`
- If Visual Studio still hungs, under Debug->Options, uncheck "Enable Diagnostic Tools while debugging"
- Clean your repo before building/running.
- Ensure the ASP.NET Workload is installed for Visual Studio.
- Search the web for the error you are seeing.
