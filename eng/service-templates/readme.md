This folder contains scripts for recreating arcade-services Azure resources

# Product Construction Service

The product construction service folder contains the `provision.ps1` script that is used to recreate all of the Product Construction Service Azure resources

# Scale Set extension scripts

The scale set extensions scripts folders contains two scrips that are used to configure a Virtual Machine Scale Set extension that's responsible for setting Application Insights environmental variables, and configuring TLS.

If at some point we need to recreate Maestro, we'll need to upload these scripts to a storage account, and add a Scale Set extension that's calling them with the `AppInsightsConnectionString` parameter set

More information on this extension can been seen at the [wiki](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/1085/Application-insights-overview)