// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using EntityFrameworkCore.Triggers;
using FluentValidation.AspNetCore;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;

namespace ProductConstructionService.Api.Configuration;

internal static class PcsStartup
{
    // https://github.com/dotnet/core-eng/issues/6819
    // TODO: Remove once the repo in this list is ready to onboard to yaml publishing.
    private static readonly HashSet<string> ReposWithoutAssetLocationAllowList =
        new(StringComparer.OrdinalIgnoreCase) { "https://github.com/aspnet/AspNetCore" };

    private static readonly TimeSpan DataProtectionKeyLifetime = new(days: 240, hours: 0, minutes: 0, seconds: 0);

    public const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";
    public const string ManagedIdentityId = "ManagedIdentityClientId";
    public const string KeyVaultName = "KeyVaultName";
    public const string GitHubToken = "BotAccount-dotnet-bot-repo-PAT";
    public const string GitHubClientId = "github-oauth-id";
    public const string GitHubClientSecret = "github-oauth-secret";
    public const string MaestroUri = "Maestro:Uri";
    public const string MaestroNoAuth = "Maestro:NoAuth";

    public const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";

    static PcsStartup()
    {
        Triggers<BuildChannel>.Inserted += entry =>
        {
            var context = (BuildAssetRegistryContext)entry.Context;
            ILogger<BuildAssetRegistryContext> logger = context.GetService<ILogger<BuildAssetRegistryContext>>();
            BuildChannel entity = entry.Entity;

            Build? build = context.Builds
                .Include(b => b.Assets)
                .ThenInclude(a => a.Locations)
                .FirstOrDefault(b => b.Id == entity.BuildId);

            if (build == null)
            {
                logger.LogError($"Could not find build with id {entity.BuildId} in BAR. Skipping dependency update.");
            }
            else
            {
                bool hasAssetsWithPublishedLocations = build.Assets
                    .Any(a => a.Locations.Any(al => al.Type != LocationType.None && !al.Location.EndsWith("/artifacts")));

                if (hasAssetsWithPublishedLocations || ReposWithoutAssetLocationAllowList.Contains(build.GitHubRepository))
                {
                    // TODO: Only activate this when we want the service to do things
                    // TODO (https://github.com/dotnet/arcade-services/issues/3814): var queue = context.GetService<IBackgroundQueue>();
                    // queue.Post<StartDependencyUpdate>(StartDependencyUpdate.CreateArgs(entity));
                }
                else
                {
                    logger.LogInformation($"Skipping Dependency update for Build {entity.BuildId} because it contains no assets in valid locations");
                }
            }
        };
    }

    public static string GetRequiredValue(this IConfiguration configuration, string key)
        => configuration[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");

    /// <summary>
    /// Registers all necessary services for the Product Construction Service
    /// </summary>
    /// <param name="vmrPath">Path to the VMR on local disk</param>
    /// <param name="tmpPath">Path to the VMR tmp folder</param>
    /// <param name="vmrUri">Uri of the VMR</param>
    /// <param name="azureCredential">Credentials used to authenticate to Azure Resources</param>
    /// <param name="initializeService">Run service initialization? Currently this just means cloning the VMR</param>
    /// <param name="addEndpointAuthentication">Add endpoint authentication?</param>
    /// <param name="addSwagger">Add Swagger UI?</param>
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
        Uri? keyVaultUri = null,
        bool addDataProtection = false)
    {
        if (!addDataProtection)
        {
            builder.Services.AddDataProtection()
                .SetDefaultKeyLifetime(DataProtectionKeyLifetime);
        }
        else
        {
            IConfigurationSection dpConfig = builder.Configuration.GetSection("DataProtection");

            var keyBlobUri = new Uri(dpConfig["KeyBlobUri"] ?? throw new Exception("DataProtection:KeyBlobUri is missing"));
            var dataProtectionKeyUri = new Uri(dpConfig["DataProtectionKeyUri"] ?? throw new Exception("DataProtection:DataProtectionKeyUri is missing"));

            builder.Services.AddDataProtection()
                .PersistKeysToAzureBlobStorage(keyBlobUri, new DefaultAzureCredential())
                .ProtectKeysWithAzureKeyVault(dataProtectionKeyUri, new DefaultAzureCredential())
                .SetDefaultKeyLifetime(DataProtectionKeyLifetime)
                .SetApplicationName(typeof(PcsStartup).FullName!);
        }

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
        builder.Services.Configure<AzureDevOpsTokenProviderOptions>("AzureDevOps", (o, s) => s.Bind(o));
        builder.AddVmrRegistrations(vmrPath, tmpPath);
        builder.AddGitHubClientFactory();
        builder.AddWorkitemQueues(azureCredential, waitForInitialization: initializeService);

        // TODO (https://github.com/dotnet/arcade-services/issues/3807): Won't be needed but keeping here to make PCS happy for now
        builder.Services.AddScoped<IMaestroApi>(s =>
        {
            var uri = builder.Configuration[MaestroUri]
                ?? throw new Exception($"Missing configuration key {MaestroUri}");

            var noAuth = builder.Configuration.GetValue<bool>(MaestroNoAuth);
            if (noAuth)
            {
                return MaestroApiFactory.GetAnonymous(uri);
            }

            var managedIdentityId = builder.Configuration[ManagedIdentityId];

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
            builder.AddEndpointAuthentication();
        }

        builder.AddServiceDefaults();
        builder.Services.AddControllers().EnableInternalControllers();

        if (addSwagger)
        {
            builder.ConfigureSwagger();
        }


        builder.Services.AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/", Maestro.Authentication.AuthenticationConfiguration.MsftAuthorizationPolicyName);
                options.Conventions.AllowAnonymousToPage("/Index");
                options.Conventions.AllowAnonymousToPage("/Error");
                options.Conventions.AllowAnonymousToPage("/SwaggerUi");
            })
            .AddFluentValidation(options => options.RegisterValidatorsFromAssemblyContaining<AccountController>())
            .AddGitHubWebHooks()
            .AddApiPagination()
            .AddCookieTempDataProvider(
                options =>
                {
                    // Cookie Policy will not send this cookie unless we mark it as Essential
                    // The application will not function without this cookie.
                    options.Cookie.IsEssential = true;
                });
    }
}
