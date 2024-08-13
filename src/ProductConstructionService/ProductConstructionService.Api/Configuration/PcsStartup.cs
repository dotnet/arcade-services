// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Azure.Identity;
using EntityFrameworkCore.Triggers;
using FluentValidation.AspNetCore;
using Maestro.Authentication;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.Pages.DependencyFlow;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;

namespace ProductConstructionService.Api.Configuration;

internal static class PcsStartup
{
    public const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";

    internal static int LocalHttpsPort { get; }

    private static readonly TimeSpan DataProtectionKeyLifetime = new(days: 240, hours: 0, minutes: 0, seconds: 0);

    /// <summary>
    /// Path to the compiled static files for the Angular app.
    /// This is required when running PCS locally when Angular is not published.
    /// </summary>
    internal static string LocalCompiledStaticFilesPath => Path.Combine(Environment.CurrentDirectory, "..", "..", "Maestro", "maestro-angular", "dist", "maestro-angular");

    static PcsStartup()
    {
        LocalHttpsPort = int.Parse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT") ?? "443");

        var metadata = typeof(Program).Assembly
            .GetCustomAttributes()
            .OfType<AssemblyMetadataAttribute>()
            .ToDictionary(m => m.Key, m => m.Value);

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

                if (hasAssetsWithPublishedLocations)
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
    /// <param name="builder"></param>
    /// <param name="vmrPath">Path to the VMR on local disk</param>
    /// <param name="tmpPath">Path to the VMR tmp folder</param>
    /// <param name="vmrUri">Uri of the VMR</param>
    /// <param name="azureCredential">Credentials used to authenticate to Azure Resources</param>
    /// <param name="initializeService">Run service initialization? Currently this just means cloning the VMR</param>
    /// <param name="addSwagger">Add Swagger UI?</param>
    /// <param name="keyVaultUri">Uri to used KeyVault</param>
    /// <param name="addDataProtection">Turn on data protection</param>
    internal static void ConfigurePcs(
        this WebApplicationBuilder builder,
        string vmrPath,
        string tmpPath,
        string vmrUri,
        DefaultAzureCredential azureCredential,
        bool initializeService,
        bool addSwagger,
        Uri? keyVaultUri = null,
        bool addDataProtection = false)
    {
        builder.ConfigureDataProtection(addDataProtection);
        builder.AddTelemetry();

        if (keyVaultUri != null)
        {
            builder.Configuration.AddAzureKeyVault(keyVaultUri, azureCredential);
        }

        builder.Services.AddApiVersioning(options => options.VersionByQuery("api-version"));

        builder.Services.Configure<CookiePolicyOptions>(
            options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.Lax;

                options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;

                if (builder.Environment.IsDevelopment())
                {
                    options.Secure = CookieSecurePolicy.SameAsRequest;
                }
                else
                {
                    options.Secure = CookieSecurePolicy.Always;
                }
            });

        string databaseConnectionString = builder.Configuration.GetRequiredValue(ConfigurationKeys.DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, builder.Configuration[ConfigurationKeys.ManagedIdentityId]);

        builder.AddBuildAssetRegistry(databaseConnectionString);
        builder.AddWorkitemQueues(azureCredential, waitForInitialization: initializeService);
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields =
                HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.ResponseStatusCode;
            options.CombineLogs = true;
        });
        builder.Services.AddOperationTracking(_ => { });

        builder.Services.Configure<AzureDevOpsTokenProviderOptions>(ConfigurationKeys.AzureDevOpsConfiguration, (o, s) => s.Bind(o));
        builder.AddVmrRegistrations(vmrPath, tmpPath);
        builder.AddGitHubClientFactory(builder.Configuration.GetSection(ConfigurationKeys.GitHubConfiguration));
        builder.Services.AddScoped<IRemoteFactory, DarcRemoteFactory>();
        builder.Services.AddGitHubTokenProvider();
        builder.Services.AddSingleton<Microsoft.Extensions.Internal.ISystemClock, Microsoft.Extensions.Internal.SystemClock>();
        builder.Services.AddSingleton<ExponentialRetry>();
        builder.Services.Configure<ExponentialRetryOptions>(_ => { });
        builder.Services.AddMemoryCache();

        // TODO (https://github.com/dotnet/arcade-services/issues/3807): Won't be needed but keeping here to make PCS happy for now
        builder.Services.AddScoped<IMaestroApi>(s =>
        {
            var uri = builder.Configuration[ConfigurationKeys.MaestroUri]
                ?? throw new Exception($"Missing configuration key {ConfigurationKeys.MaestroUri}");

            var noAuth = builder.Configuration.GetValue<bool>(ConfigurationKeys.MaestroNoAuth);
            if (noAuth)
            {
                return MaestroApiFactory.GetAnonymous(uri);
            }

            var managedIdentityId = builder.Configuration[ConfigurationKeys.ManagedIdentityId];

            return MaestroApiFactory.GetAuthenticated(
                uri,
                accessToken: null,
                managedIdentityId: managedIdentityId,
                disableInteractiveAuth: true);
        });

        // We do not use AddMemoryCache here. We use our own cache because we wish to
        // use a sized cache and some components, such as EFCore, do not implement their caching
        // in such a way that will work with sizing.
        builder.Services.AddSingleton<DarcRemoteMemoryCache>();
        builder.Services.EnableLazy();
        builder.Services.AddMergePolicies();
        builder.Services.Configure<SlaOptions>(builder.Configuration.GetSection(ConfigurationKeys.DependencyFlowSLAs));

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

        builder.Services.ConfigureAuthServices(builder.Configuration.GetSection(ConfigurationKeys.EntraAuthenticationKey));

        builder.ConfigureApiRedirection();

        builder.AddServiceDefaults();

        builder.Services
            .AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                options.SerializerSettings.Converters.Add(new StringEnumConverter
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                });
                options.SerializerSettings.Converters.Add(
                    new IsoDateTimeConverter
                    {
                        DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ",
                        DateTimeStyles = DateTimeStyles.AdjustToUniversal
                    });
            });

        builder.Services.AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/", AuthenticationConfiguration.MsftAuthorizationPolicyName);
                options.Conventions.AllowAnonymousToPage("/Index");
                options.Conventions.AllowAnonymousToPage("/Error");
                options.Conventions.AllowAnonymousToPage("/SwaggerUi");
            })
            .AddFluentValidation(options => options.RegisterValidatorsFromAssemblyContaining<StatusController>())
            .AddGitHubWebHooks()
            .AddApiPagination()
            .AddCookieTempDataProvider(
                options =>
                {
                    // Cookie Policy will not send this cookie unless we mark it as Essential
                    // The application will not function without this cookie.
                    options.Cookie.IsEssential = true;
                });

        builder.Services.AddSingleton(builder.Configuration);

        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddHttpsRedirection(options =>
            {
                options.HttpsPort = LocalHttpsPort;
            });
        }

        if (addSwagger)
        {
            builder.ConfigureSwagger();
        }
    }

    private static void ConfigureDataProtection(this WebApplicationBuilder builder, bool addDataProtection)
    {
        if (!addDataProtection)
        {
            builder.Services.AddDataProtection()
                .SetDefaultKeyLifetime(DataProtectionKeyLifetime);
        }
        else
        {
            var keyBlobUri = new Uri(builder.Configuration[ConfigurationKeys.DataProtectionKeyBlobUri]
                ?? throw new Exception($"{ConfigurationKeys.DataProtectionKeyBlobUri} configuration key is missing"));

            var dataProtectionKeyUri = new Uri(builder.Configuration[ConfigurationKeys.DataProtectionKeyUri]
                ?? throw new Exception($"{ConfigurationKeys.DataProtectionKeyUri} configuration key is missing"));

            builder.Services.AddDataProtection()
                .PersistKeysToAzureBlobStorage(keyBlobUri, new DefaultAzureCredential())
                .ProtectKeysWithAzureKeyVault(dataProtectionKeyUri, new DefaultAzureCredential())
                .SetDefaultKeyLifetime(DataProtectionKeyLifetime)
                .SetApplicationName(typeof(PcsStartup).FullName!);
        }
    }

    public static void ConfigureApiExceptions(IApplicationBuilder app)
    {
        app.Run(async ctx =>
        {
            var result = new ApiError("An error occured.");
            MvcNewtonsoftJsonOptions jsonOptions =
                ctx.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value;
            string output = JsonConvert.SerializeObject(result, jsonOptions.SerializerSettings);
            ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await ctx.Response.WriteAsync(output, Encoding.UTF8);
        });
    }

    public static void ConfigureApi(this IApplicationBuilder app, bool isDevelopment)
    {
        app.UseExceptionHandler(ConfigureApiExceptions);

        var logger = app.ApplicationServices.GetRequiredService<ILogger<IApplicationBuilder>>();

        app.UseApiRedirection();

        app.UseEndpoints(e =>
        {
            var controllers = e.MapControllers();
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
