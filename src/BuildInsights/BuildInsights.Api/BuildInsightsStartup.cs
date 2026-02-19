// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using BuildInsights.Api.Configuration;
using BuildInsights.Api.Configuration.Models;
using BuildInsights.BuildAnalysis;
using BuildInsights.GitHubGraphQL;
using BuildInsights.KnownIssues.Models;
using BuildInsights.ServiceDefaults;
using BuildInsights.Utilities.AzureDevOps;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductConstructionService.Common;
using ProductConstructionService.Common.Telemetry;
using ProductConstructionService.WorkItems;

internal static class BuildInsightsStartup
{
    private const string DefaultWorkItemType = "Default";

    private static class ConfigurationKeys
    {
        // All secrets loaded from KeyVault will have this prefix
        public const string KeyVaultSecretPrefix = "KeyVaultSecrets:";

        // Secrets coming from the KeyVault
        public const string GitHubAppPrivateKey = $"{KeyVaultSecretPrefix}github-app-private-key";
        public const string GitHubWebHookSecret = $"{KeyVaultSecretPrefix}github-app-webhook-secret";
        public const string AzDoServiceHookSecret = $"{KeyVaultSecretPrefix}azdo-service-hook-secret";

        // Configuration from appsettings.json
        public const string AzureDevOpsConfiguration = "AzureDevOps";
        public const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";
        public const string DependencyFlowSLAs = "DependencyFlowSLAs";
        public const string EntraAuthenticationKey = "EntraAuthentication";
        public const string KeyVaultName = "KeyVaultName";
        public const string ManagedIdentityId = "ManagedIdentityClientId";
        public const string GitHubAppKey = "GitHubApp";
        public const string KnownIssuesProjectKey = "KnownIssuesProject";
        public const string KnownIssuesCreationKey = "KnownIssuesCreation";
        public const string KnownIssuesAnalysisLimitsKey = "KnownIssuesAnalysisLimits";
        public const string KnownIssuesKustoKey = "KnownIssuesKusto";

        public const string WorkItemQueueName = "WorkItemQueueName";
        public const string SpecialWorkItemQueueName = "SpecialWorkItemQueueName";
        public const string WorkItemConsumerCount = "WorkItemConsumerCount";
    }

    /// <summary>
    /// Registers all necessary services for the Product Construction Service
    /// </summary>
    /// <param name="authRedis">Use authenticated connection for Redis?</param>
    internal static async Task ConfigureBuildInsights(
        this WebApplicationBuilder builder,
        bool authRedis)
    {
        bool isDevelopment = builder.Environment.IsDevelopment();

        // Register configuration settings
        string? managedIdentityId = builder.Configuration[ConfigurationKeys.ManagedIdentityId];
        var gitHubAppSettings = builder.Configuration.GetSection(ConfigurationKeys.GitHubAppKey).Get<GitHubAppSettings>()!;
        builder.Services.Configure<AzureDevOpsTokenProviderOptions>(ConfigurationKeys.AzureDevOpsConfiguration, (o, s) => s.Bind(o));
        builder.Services.Configure<KnownIssuesProjectOptions>(ConfigurationKeys.KnownIssuesProjectKey, (o, s) => s.Bind(o));
        builder.Services.Configure<GitHubAppSettings>(ConfigurationKeys.GitHubAppKey, (o, s) => s.Bind(o));

        // Set up Key Vault access for some secrets
        TokenCredential azureCredential = AzureAuthentication.GetServiceCredential(isDevelopment, managedIdentityId);
        Uri keyVaultUri = new($"https://{builder.Configuration.GetRequiredValue(ConfigurationKeys.KeyVaultName)}.vault.azure.net/");
        builder.Configuration.AddAzureKeyVault(
            keyVaultUri,
            azureCredential,
            new KeyVaultSecretsWithPrefix(ConfigurationKeys.KeyVaultSecretPrefix));

        // Set up GitHub and Azure DevOps auth
        builder.Services.AddVssConnection();
        builder.AddGitHubClientFactory(
            gitHubAppSettings.AppId,
            builder.Configuration[ConfigurationKeys.GitHubAppPrivateKey]);
        builder.Services.AddGitHubTokenProvider();
        builder.Services.AddGitHubGraphQL();
        builder.Services.TryAddSingleton<IRemoteTokenProvider>(sp =>
        {
            var azdoTokenProvider = sp.GetRequiredService<IAzureDevOpsTokenProvider>();
            var gitHubTokenProvider = sp.GetRequiredService<IGitHubTokenProvider>();
            return new RemoteTokenProvider(azdoTokenProvider, new Microsoft.DotNet.DarcLib.GitHubTokenProvider(gitHubTokenProvider));
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

        await builder.AddRedisCache(authRedis);

        builder.Services.AddBuildAnalysis(
            ConfigurationKeys.KnownIssuesCreationKey,
            ConfigurationKeys.KnownIssuesAnalysisLimitsKey,
            ConfigurationKeys.KnownIssuesKustoKey);

        // Set up telemetry
        builder.AddServiceDefaults();
        builder.AddDataProtection(azureCredential);
        builder.AddTelemetry();
        builder.Services.AddOperationTracking(_ => { });
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

    public static bool IsGet(this HttpContext context)
    {
        return string.Equals(context.Request.Method, "get", StringComparison.OrdinalIgnoreCase);
    }
}
