// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Api.Configuration;
using BuildInsights.BuildAnalysis;
using BuildInsights.KnownIssues.Models;
using BuildInsights.ServiceDefaults;
using HandlebarsDotNet;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection.Extensions;

internal static class BuildInsightsStartup
{
    internal static class ConfigurationKeys
    {
        // Configuration from appsettings.json
        public const string EntraAuthentication = "EntraAuthentication";
        public const string KnownIssuesProject = "KnownIssuesProject";
        public const string KnownIssuesCreation = "KnownIssuesCreation";
        public const string KnownIssuesAnalysisLimits = "KnownIssuesAnalysisLimits";
        public const string KnownIssuesKusto = "KnownIssuesKusto";
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

        string? managedIdentityId = builder.Configuration[BuildInsightsCommonConfiguration.ConfigurationKeys.ManagedIdentityId];

        // If we're using a user assigned managed identity, inject it into the Kusto configuration section
        if (!string.IsNullOrEmpty(managedIdentityId))
        {
            string kustoManagedIdentityIdKey = $"{ConfigurationKeys.KnownIssuesKusto}:{nameof(KustoOptions.ManagedIdentityId)}";
            builder.Configuration[kustoManagedIdentityIdKey] = managedIdentityId;
        }

        builder.Services.Configure<KnownIssuesProjectOptions>(ConfigurationKeys.KnownIssuesProject, (o, s) => s.Bind(o));
        builder.Services.Configure<AzDoServiceHookSettings>(ConfigurationKeys.AzDoServiceHook, (o, s) => s.Bind(o));

        await builder.ConfigureBuildInsightsDependencies(addKeyVault);
        builder.AddRedisOutputCache("redis");

        builder.Services.AddControllers();
        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddApplicationInsightsTelemetry();
        builder.Services.AddApplicationInsightsTelemetryProcessor<RemoveDefaultPropertiesTelemetryProcessor>();

        builder.Services.AddBuildAnalysis(
            builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesCreation),
            builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesAnalysisLimits),
            builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesKusto),
            builder.Configuration.GetSection(BuildInsightsCommonConfiguration.ConfigurationKeys.BlobStorage),
            builder.Configuration.GetSection(ConfigurationKeys.QueueInsightsBeta),
            builder.Configuration.GetSection(ConfigurationKeys.MatrixOfTruth),
            builder.Configuration.GetSection(ConfigurationKeys.InternalProject),
            builder.Configuration.GetSection(ConfigurationKeys.BuildConfigurationFile),
            builder.Configuration.GetSection(ConfigurationKeys.GitHubIssues),
            builder.Configuration.GetSection(ConfigurationKeys.RelatedBuilds),
            builder.Configuration.GetSection(ConfigurationKeys.BuildAnalysisFile));

        if (isDevelopment)
        {
            builder.Services.AddCors();
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

    public static void ConfigureApi(this IApplicationBuilder app, string apiPath, bool isDevelopment)
    {
        app.MapWhen(
            ctx => ctx.Request.Path.StartsWithSegments(apiPath),
            a => a.UseEndpoints(e =>
            {
                var controllers = e.MapControllers();
                // TODO - Turn on auth
                // controllers.RequireAuthorization(AuthenticationConfiguration.ApiAuthorizationPolicyName);

                if (isDevelopment)
                {
                    controllers.AllowAnonymous();
                }
            }));
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
