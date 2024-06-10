// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Azure.Identity;
using Microsoft.AspNetCore.HttpLogging;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Controllers;
using Microsoft.DotNet.Maestro.Client;

namespace ProductConstructionService.Api.Configuration;

internal static class PcsConfiguration
{
    public const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";
    public const string ManagedIdentityId = "ManagedIdentityClientId";
    public const string KeyVaultName = "KeyVaultName";
    public const string GitHubToken = "BotAccount-dotnet-bot-repo-PAT";
    public const string AzDOToken = "dn-bot-all-orgs-code-r";
    public const string GitHubClientId = "github-oauth-id";
    public const string GitHubClientSecret = "github-oauth-secret";

    public const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";

    public static string GetRequiredValue(this IConfiguration configuration, string key)
        => configuration[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");

    /// <summary>
    /// Registers all necessary services for the Product Construction Service
    /// </summary>
    /// <param name="vmrPath">Path to the VMR on local disk</param>
    /// <param name="tmpPath">Path to the VMR tmp folder</param>
    /// <param name="vmrUri">Uri of the VMR</param>
    /// <param name="azureCredential">Credentials used to authenticate to Azure Resources</param>
    /// <param name="databaseConnectionString">ConnectionString to the BAR database</param>
    /// <param name="initializeService">Run service initialization? Currently this just means cloning the VMR</param>
    /// <param name="addEndpointAuthentication">Add endpoint authentication?</param>
    /// <param name="keyVaultUri">Uri to used KeyVault</param>
    public static void ConfigurePcs(
        this WebApplicationBuilder builder,
        string vmrPath,
        string tmpPath,
        string vmrUri,
        DefaultAzureCredential azureCredential,
        bool initializeService,
        bool addEndpointAuthentication,
        bool addSwagger,
        Uri? keyVaultUri = null)
    {
        if (keyVaultUri != null) 
        {
            builder.Configuration.AddAzureKeyVault(keyVaultUri, azureCredential);
        }

        string databaseConnectionString = builder.Configuration.GetRequiredValue(DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, builder.Configuration[ManagedIdentityId]);

        builder.AddBuildAssetRegistry(databaseConnectionString);
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields =
                HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.ResponseStatusCode;
            options.CombineLogs = true;
        });

        builder.AddTelemetry();
        builder.AddVmrRegistrations(vmrPath, tmpPath);
        builder.AddGitHubClientFactory();
        builder.AddWorkitemQueues(azureCredential, waitForInitialization: initializeService);

        builder.Services.AddScoped<IMaestroApi>(s =>
        {
            var config = s.GetRequiredService<IConfiguration>();
            var uri = config.GetValue<string>("Maestro:Uri")
                ?? throw new Exception("Missing configuration key Maestro.Uri");

            var noAuth = config.GetValue<bool>("Maestro:NoAuth");
            if (noAuth)
            {
                return MaestroApiFactory.GetAnonymous(uri);
            }

            var managedIdentityId = config.GetValue<string>("ManagedIdentityClientId");

            return MaestroApiFactory.GetAuthenticated(
                uri,
                accessToken: null,
                managedIdentityId: managedIdentityId,
                federatedToken: null,
                disableInteractiveAuth: true);
        });

        if (initializeService)
        {
            builder.AddVmrInitialization(vmrUri);
        }
        else
        {
            // This is expected in local flows and it's useful to learn about this early
            if (!Directory.Exists(vmrPath))
            {
                throw new InvalidOperationException($"VMR not found at {vmrPath}. " +
                    $"Either run the service in initialization mode or clone {vmrUri} into {vmrPath}.");
            }
        }

        if (addEndpointAuthentication)
        {
            builder.AddEndpointAuthentication(requirePolicyRole: true);
        }

        builder.AddServiceDefaults();
        builder.Services.AddControllers().EnableInternalControllers();

        if (addSwagger)
        {
            builder.ConfigureSwagger();
        }
    }
}
