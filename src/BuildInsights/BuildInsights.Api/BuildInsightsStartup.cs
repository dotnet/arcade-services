// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
using BuildInsights.Api.Configuration;
using BuildInsights.Api.Configuration.Models;
using BuildInsights.BuildAnalysis;
using BuildInsights.Data;
using BuildInsights.GitHub;
using BuildInsights.GitHubGraphQL;
using BuildInsights.KnownIssues.Models;
using BuildInsights.ServiceDefaults;
using BuildInsights.Utilities.AzureDevOps;
using HandlebarsDotNet;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Common.Cache;
using Maestro.Common.Telemetry;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Helix.Client;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

internal static class BuildInsightsStartup
{
    private const string DefaultWorkItemType = "Default";

    internal static class ConfigurationKeys
    {
        // All secrets loaded from KeyVault will have this prefix
        public const string KeyVaultSecretPrefix = "KeyVaultSecrets:";

        // Secrets coming from the KeyVault
        public const string GitHubAppPrivateKey = $"{KeyVaultSecretPrefix}github-app-private-key";
        public const string GitHubWebHookSecret = $"{KeyVaultSecretPrefix}github-app-webhook-secret";
        public const string AzDoServiceHookSecret = $"{KeyVaultSecretPrefix}azdo-service-hook-secret";

        // Configuration from appsettings.json
        public const string DatabaseConnectionString = "ConnectionStrings:sql";
        public const string RedisConnectionString = "ConnectionStrings:redis";
        public const string AzureDevOpsConfiguration = "AzureDevOps";
        public const string DependencyFlowSLAs = "DependencyFlowSLAs";
        public const string EntraAuthentication = "EntraAuthentication";
        public const string KeyVaultName = "KeyVaultName";
        public const string ManagedIdentityId = "ManagedIdentityClientId";
        public const string GitHubApp = "GitHubApp";
        public const string KnownIssuesProject = "KnownIssuesProject";
        public const string KnownIssuesCreation = "KnownIssuesCreation";
        public const string KnownIssuesAnalysisLimits = "KnownIssuesAnalysisLimits";
        public const string KnownIssuesKusto = "KnownIssuesKusto";
        public const string BlobStorage = "BlobStorage";
        public const string QueueInsightsBeta = "QueueInsightsBeta";
        public const string MatrixOfTruth = "MatrixOfTruth";
        public const string InternalProject = "InternalProject";
        public const string BuildConfigurationFile = "BuildConfigurationFile";
        public const string GitHubIssues = "GitHubIssues";
        public const string RelatedBuilds = "RelatedBuilds";
        public const string BuildAnalysisFile = "BuildAnalysisFile";
        public const string Helix = "Helix";

        public const string WorkItemQueueName = "WorkItemQueueName";
        public const string SpecialWorkItemQueueName = "SpecialWorkItemQueueName";
        public const string WorkItemConsumerCount = "WorkItemConsumerCount";
    }

    /// <summary>
    /// Registers all necessary services for the Product Construction Service
    /// </summary>
    /// <param name="addKeyVault">Use KeyVault for secrets?</param>
    internal static async Task ConfigureBuildInsights(
        this WebApplicationBuilder builder,
        bool addKeyVault)
    {
        bool isDevelopment = builder.Environment.IsDevelopment();

        string? managedIdentityId = builder.Configuration[ConfigurationKeys.ManagedIdentityId];

        // If we're using a user assigned managed identity, inject it into the Kusto configuration section
        if (!string.IsNullOrEmpty(managedIdentityId))
        {
            string kustoManagedIdentityIdKey = $"{ConfigurationKeys.KnownIssuesKusto}:{nameof(KustoOptions.ManagedIdentityId)}";
            builder.Configuration[kustoManagedIdentityIdKey] = managedIdentityId;
        }

        var gitHubAppSettings = builder.Configuration.GetSection(ConfigurationKeys.GitHubApp).Get<GitHubAppSettings>()!;
        builder.Services.Configure<AzureDevOpsTokenProviderOptions>(ConfigurationKeys.AzureDevOpsConfiguration, (o, s) => s.Bind(o));
        builder.Services.Configure<KnownIssuesProjectOptions>(ConfigurationKeys.KnownIssuesProject, (o, s) => s.Bind(o));
        builder.Services.Configure<GitHubAppSettings>(ConfigurationKeys.GitHubApp, (o, s) => s.Bind(o));
        builder.Services.Configure<HelixSettings>(ConfigurationKeys.Helix, (o, s) => s.Bind(o));

        // Set up Key Vault access for some secrets
        TokenCredential azureCredential = isDevelopment
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityId));

        if (addKeyVault)
        {
            Uri keyVaultUri = new($"https://{builder.Configuration.GetRequiredValue(ConfigurationKeys.KeyVaultName)}.vault.azure.net/");
            builder.Configuration.AddAzureKeyVault(
                keyVaultUri,
                azureCredential,
                new KeyVaultSecretsWithPrefix(ConfigurationKeys.KeyVaultSecretPrefix));
        }

        // Set up GitHub and Azure DevOps auth
        builder.Services.AddVssConnection();
        builder.AddGitHubClientFactory(
            gitHubAppSettings.AppId,
            builder.Configuration[ConfigurationKeys.GitHubAppPrivateKey]);
        builder.Services.AddGitHubTokenProvider();
        builder.Services.AddGitHub();
        builder.Services.AddGitHubGraphQL();
        builder.Services.TryAddSingleton<IRemoteTokenProvider>(sp =>
        {
            var azdoTokenProvider = sp.GetRequiredService<IAzureDevOpsTokenProvider>();
            var gitHubTokenProvider = sp.GetRequiredService<IGitHubTokenProvider>();
            return new RemoteTokenProvider(azdoTokenProvider, new BuildInsights.Api.Configuration.GitHubTokenProvider(gitHubTokenProvider));
        });

        // Set up SQL database
        string databaseConnectionString = builder.Configuration.GetRequiredValue(ConfigurationKeys.DatabaseConnectionString);
        builder.AddSqlDatabase<BuildInsightsContext>(databaseConnectionString, managedIdentityId);

        // Set up Kusto client provider
        builder.Services.AddKustoClientProvider("Kusto");

        // Set up Helix API
        builder.Services.AddScoped<IHelixApi>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<HelixSettings>>();
            return new HelixApi(new HelixApiOptions(
                new Uri(options.Value.Endpoint),
                new HelixApiTokenCredential(options.Value.Token)));
        });

        // Set up background queue processing
        var workItemQueueName = builder.Configuration.GetRequiredValue(ConfigurationKeys.WorkItemQueueName);
        var specialWorkItemQueueName = builder.Configuration.GetRequiredValue(ConfigurationKeys.SpecialWorkItemQueueName);
        builder.AddWorkItemQueues(azureCredential, waitForInitialization: false, new()
        {
            { workItemQueueName, (int.Parse(builder.Configuration.GetRequiredValue(ConfigurationKeys.WorkItemConsumerCount)), DefaultWorkItemType) },
        });
        builder.AddWorkItemProducerFactory(azureCredential, workItemQueueName, specialWorkItemQueueName);

        // Set up DI
        builder.Services.AddSingleton<Microsoft.Extensions.Internal.ISystemClock, Microsoft.Extensions.Internal.SystemClock>();
        builder.Services.AddSingleton<ExponentialRetry>();
        builder.Services.Configure<ExponentialRetryOptions>(_ => { });
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(builder.Configuration);

        // Set up Redis
        var redisConnectionString = builder.Configuration[ConfigurationKeys.RedisConnectionString]!;
        await builder.AddRedisCache(redisConnectionString, managedIdentityId);

        builder.Services.AddBuildAnalysis(
            builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesCreation),
            builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesAnalysisLimits),
            builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesKusto),
            builder.Configuration.GetSection(ConfigurationKeys.BlobStorage),
            builder.Configuration.GetSection(ConfigurationKeys.QueueInsightsBeta),
            builder.Configuration.GetSection(ConfigurationKeys.MatrixOfTruth),
            builder.Configuration.GetSection(ConfigurationKeys.InternalProject),
            builder.Configuration.GetSection(ConfigurationKeys.BuildConfigurationFile),
            builder.Configuration.GetSection(ConfigurationKeys.GitHubIssues),
            builder.Configuration.GetSection(ConfigurationKeys.RelatedBuilds),
            builder.Configuration.GetSection(ConfigurationKeys.BuildAnalysisFile));

        // Set up telemetry
        builder.AddServiceDefaults();
        builder.AddDataProtection(azureCredential);
        builder.Services.AddTelemetry();
        builder.Services.AddApplicationInsightsTelemetry();
        builder.Services.AddApplicationInsightsTelemetryProcessor<RemoveDefaultPropertiesTelemetryProcessor>();
        //builder.RegisterLogging(); // TODO
        builder.Services.AddOperationTracking(_ => { });
        builder.Services.AddSingleton<IMetricRecorder, MetricRecorder>();
        builder.Services.AddHttpLogging(
            options =>
            {
                options.LoggingFields =
                    HttpLoggingFields.RequestPath
                    | HttpLoggingFields.RequestQuery
                    | HttpLoggingFields.ResponseStatusCode;
                options.CombineLogs = true;
            });

        builder.Services
            .AddControllers();
        //.AddNewtonsoftJson(
        //    options =>
        //    {
        //        options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
        //        options.SerializerSettings.Converters.Add(new StringEnumConverter
        //        {
        //            NamingStrategy = new CamelCaseNamingStrategy()
        //        });
        //        options.SerializerSettings.Converters.Add(
        //            new IsoDateTimeConverter
        //            {
        //                DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ",
        //                DateTimeStyles = DateTimeStyles.AdjustToUniversal
        //            });
        //    });

        // TODO: Use OpenAPI instead of Swagger?
        //if (isDevelopment)
        //{
        //    builder.ConfigureSwagger();
        //}

        if (isDevelopment)
        {
            builder.Services.AddCors(policy =>
            {
                //policy.AddDefaultPolicy(p =>
                //    // These come from BarViz project's launchsettings.json
                //    p.WithOrigins("https://localhost:7287", "http://localhost:5015")
                //      .AllowAnyHeader()
                //      .AllowAnyMethod());
            });
        }

        /*

        // Configure API
        builder.Services.ConfigureAuthServices(builder.Configuration.GetSection(ConfigurationKeys.EntraAuthenticationKey));

        builder.Services.AddRazorPages(
            options =>
            {
                options.Conventions.AuthorizeFolder("/", AuthenticationConfiguration.WebAuthorizationPolicyName);
                options.Conventions.AllowAnonymousToPage("/Error");
            })
            .AddGitHubWebHooks()
            .AddApiPagination()
            .AddCookieTempDataProvider(
                options =>
                {
                    // Cookie Policy will not send this cookie unless we mark it as Essential
                    // The application will not function without this cookie.
                    options.Cookie.IsEssential = true;
                });

        builder.Services.AddTransient<WebhookEventProcessor, GitHubWebhookEventProcessor>();
        */
    }

    public static void ConfigureApi(this IApplicationBuilder app, bool isDevelopment)
    {
        //app.UseApiRedirection(requireAuth: !isDevelopment);
        //app.UseExceptionHandler(a =>
        //    a.Run(async ctx =>
        //    {
        //        var result = new ApiError("An error occured.");
        //        MvcNewtonsoftJsonOptions jsonOptions =
        //            ctx.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value;
        //        string output = JsonConvert.SerializeObject(result, jsonOptions.SerializerSettings);
        //        ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        //        await ctx.Response.WriteAsync(output, Encoding.UTF8);
        //    }));
        app.UseEndpoints(e =>
        {
            var controllers = e.MapControllers();
            // controllers.RequireAuthorization(AuthenticationConfiguration.ApiAuthorizationPolicyName);

            if (isDevelopment)
            {
                controllers.AllowAnonymous();
            }
        });
    }

    public static void ConfigureSecurityHeaders(this WebApplication app)
    {
        app.Use((ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                ctx.Response.Headers.TryAdd("X-XSS-Protection", "1");
                ctx.Response.Headers.TryAdd("X-Frame-Options", "DENY");
                ctx.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                ctx.Response.Headers.TryAdd("Referrer-Policy", "no-referrer-when-downgrade");
                // Allow DependencyFlow pages to be rendered on Azure DevOps dashboard
                ctx.Response.Headers.Append("Content-Security-Policy", "frame-ancestors https://dev.azure.com");
                return Task.CompletedTask;
            });

            return next();
        });
    }
}
