// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Configuration;
using BuildInsights.BuildAnalysis;
using BuildInsights.Data;
using BuildInsights.Data.Seed;
using BuildInsights.KnownIssues.Models;
using BuildInsights.ServiceDefaults;
using Maestro.Common.Telemetry;
using Microsoft.EntityFrameworkCore;

internal static class BuildInsightsStartup
{
    internal static class ConfigurationKeys
    {
        // Secrets coming from the KeyVault
        public const string AzureDevOpsServiceHookSecret =
            $"{BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultSecretPrefix}azdo-service-hook-secret";

        // Configuration from appsettings.json
        public const string EntraAuthentication = "EntraAuthentication";
        public const string KnownIssues = "KnownIssues";
        public const string KnownIssuesProject = "KnownIssuesProject";
        public const string KnownIssuesCreation = "KnownIssuesCreation";
        public const string KnownIssuesAnalysisLimits = "KnownIssuesAnalysisLimits";
        public const string Kusto = "Kusto";
        public const string QueueInsightsBeta = "QueueInsightsBeta";
        public const string MatrixOfTruth = "MatrixOfTruth";
        public const string BuildConfigurationFile = "BuildConfigurationFile";
        public const string GitHubIssues = "GitHubIssues";
        public const string RelatedBuilds = "RelatedBuilds";
        public const string BuildAnalysisFile = "BuildAnalysisFile";
        public const string AzDoServiceHook = "AzDoServiceHook";
        public const string InternalProject = "InternalProject";
    }

    /// <summary>
    /// Registers all necessary services for the service
    /// </summary>
    /// <param name="addKeyVault">Use KeyVault for secrets?</param>
    internal static async Task ConfigureBuildInsights(
        this WebApplicationBuilder builder,
        bool addKeyVault)
    {
        bool isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.Configure<KnownIssuesProjectOptions>(ConfigurationKeys.KnownIssuesProject, (o, s) => s.Bind(o));
        builder.Services.Configure<AzDoServiceHookSettings>(ConfigurationKeys.AzDoServiceHook, (o, s) =>
        {
            s.Bind(o);
            o.SecretHttpHeaderValue = builder.Configuration[ConfigurationKeys.AzureDevOpsServiceHookSecret];
        });

        await builder.ConfigureBuildInsightsDependencies(addKeyVault);

        builder.Services.AddControllers()
            .AddGitHubWebHooks();
        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddApplicationInsightsTelemetry();
        builder.Services.AddApplicationInsightsTelemetryProcessor<RemoveDefaultPropertiesTelemetryProcessor>();

        builder.Services.AddBuildAnalysis(
            builder.Configuration.GetRequiredSection(ConfigurationKeys.KnownIssues),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.KnownIssuesCreation),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.KnownIssuesAnalysisLimits),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.Kusto),
            builder.Configuration.GetRequiredSection(BuildInsightsCommonConfiguration.ConfigurationKeys.BlobStorage),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.QueueInsightsBeta),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.MatrixOfTruth),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.InternalProject),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.BuildConfigurationFile),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.GitHubIssues),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.RelatedBuilds),
            builder.Configuration.GetRequiredSection(ConfigurationKeys.BuildAnalysisFile));

        if (isDevelopment)
        {
            builder.Services.AddCors();
            builder.Services.AddTransient<IDatabaseSeed, DatabaseSeed>();
        }

        /*
        // Configure auth
        builder.Services.ConfigureAuthServices(builder.Configuration.GetSection(ConfigurationKeys.EntraAuthenticationKey));

        builder.Services.AddRazorPages(
            options =>
            {
                options.Conventions.AuthorizeFolder("/", AuthenticationConfiguration.WebAuthorizationPolicyName);
                options.Conventions.AllowAnonymousToPage("/Error");
            })
            .AddCookieTempDataProvider(
                options =>
                {
                    // Cookie Policy will not send this cookie unless we mark it as Essential
                    // The application will not function without this cookie.
                    options.Cookie.IsEssential = true;
                });
        */
    }

    public static void ConfigureApi(this IEndpointRouteBuilder app, string apiPath, bool allowAnonymous)
    {
        var controllers = app.MapGroup(apiPath).MapControllers();
        if (allowAnonymous)
        {
            controllers.AllowAnonymous();
        }
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

    public static async Task InitializeDatabaseMigrations(this IHost appHost)
    {
        // Apply data migrations automatically in development environment
        using var scope = appHost.Services.CreateScope();

        // Check if there are pending migrations
        var db = scope.ServiceProvider.GetRequiredService<BuildInsightsContext>();
        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            await db.Database.MigrateAsync();

            var databaseSeed = scope.ServiceProvider.GetRequiredService<IDatabaseSeed>();
            await databaseSeed.SeedDataAsync(db);
        }
    }
}
