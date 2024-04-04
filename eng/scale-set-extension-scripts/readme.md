These two scripts are used to configure a Virtual Machine Scale Set extension that's responsible for setting Application Insights environmental variables, and configuring TLS.

If at some point we need to recreate Maestro, we'll need to upload these scripts to a storage account, and add a Scale Set extension that's calling them with the `AppInsightsConnectionString` parameter set