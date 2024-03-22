// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Azure.Identity;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Controllers;

namespace ProductConstructionService.Api.Configuration;

internal static class PcsConfiguration
{
    public const string DatabaseConnectionString = "build-asset-registry-sql-connection-string";
    public const string ManagedIdentityId = "ManagedIdentityClientId";
    public const string KeyVaultName = "KeyVaultName";
    public const string GitHubToken = "BotAccount-dotnet-bot-repo-PAT";
    public const string AzDOToken = "dn-bot-all-orgs-code-r";
    public const string GitHubClientId = "github-oauth-id";
    public const string GitHubClientSecret = "github-oauth-secret";
    public const string KustoConnectionString = "nethelix-engsrv-kusto-connection-string-query";

    public static string GetRequiredValue(this IConfiguration configuration, string key)
        => configuration[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");

    /// <summary>
    /// Registers all necessary services for the Product Construction Service
    /// </summary>
    /// <param name="vmrPath">Path to the VMR on local disk</param>
    /// <param name="tmpPath">Path to the VMR tmp folder</param>
    /// <param name="vmrUri">Uri of the VMR</param>
    /// <param name="credential">Credentials used to authenticate to Azure Resources</param>
    /// <param name="databaseConnectionString">ConnectionString to the BAR database</param>
    /// <param name="initializeService">Run service initialization? Currently this just means cloning the VMR</param>
    /// <param name="addEndpointAuthentication">Add endpoint authentication?</param>
    /// <param name="keyVaultUri">Uri to used KeyVault</param>
    public static void ConfigurePcs(
        this WebApplicationBuilder builder,
        string vmrPath,
        string tmpPath,
        string vmrUri,
        DefaultAzureCredential credential,
        bool initializeService,
        bool addEndpointAuthentication,
        Uri? keyVaultUri = null)
    {
        if (keyVaultUri != null) 
        {
            builder.Configuration.AddAzureKeyVault(keyVaultUri, credential);
        }

        string databaseConnectionString = builder.Configuration.GetRequiredValue(PcsConfiguration.DatabaseConnectionString);

        builder.AddBuildAssetRegistry(databaseConnectionString);
        builder.AddTelemetry();
        builder.AddVmrRegistrations(vmrPath, tmpPath);
        builder.AddGitHubClientFactory();
        builder.AddWorkitemQueues(credential, waitForInitialization: initializeService);

        if (initializeService)
        {
            builder.AddVmrInitialization(vmrUri);
        }

        if (addEndpointAuthentication)
        {
            builder.AddEndpointAuthentication(requirePolicyRole: true);
        }

        builder.AddServiceDefaults();
        builder.Services.AddControllers().EnableInternalControllers();
        builder.ConfigureSwagger();
    }
}
