// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Azure.Core;
using Azure.Identity;
using EntityFrameworkCore.Triggers;
using FluentValidation.AspNetCore;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.Pages.DependencyFlow;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.VirtualMonoRepo;
using Swashbuckle.AspNetCore.Swagger;

namespace ProductConstructionService.Api.Configuration;

internal static class PcsStartup
{
    public const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";
    public const string ManagedIdentityId = "ManagedIdentityClientId";
    public const string KeyVaultName = "KeyVaultName";
    public const string GitHubConfiguration = "GitHub";
    public const string AzureDevOpsConfiguration = "AzureDevOps";
    public const string ApiRedirectionConfiguration = "ApiRedirect";
    public const string ApiRedirectionTarget = "Uri";
    public const string ApiRedirectionToken = "Token";
    public const string GitHubToken = "BotAccount-dotnet-bot-repo-PAT";
    public const string GitHubClientId = "github-oauth-id";
    public const string GitHubClientSecret = "github-oauth-secret";
    public const string MaestroUri = "Maestro:Uri";
    public const string MaestroNoAuth = "Maestro:NoAuth";
    public const string DependencyFlowSLAs = "DependencyFlowSLAs";

    public const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";

    internal static int LocalHttpsPort { get; }
    internal static string? SourceVersion { get; }
    internal static string? SourceBranch { get; }

    // https://github.com/dotnet/core-eng/issues/6819
    // TODO: Remove once the repo in this list is ready to onboard to yaml publishing.
    private static readonly HashSet<string> ReposWithoutAssetLocationAllowList =
        new(StringComparer.OrdinalIgnoreCase) { "https://github.com/aspnet/AspNetCore" };

    private static readonly TimeSpan DataProtectionKeyLifetime = new(days: 240, hours: 0, minutes: 0, seconds: 0);

    /// <summary>
    /// Path to the compiled static files for the Angular app.
    /// This is required when running Maestro.Web locally, outside of Service Fabric.
    /// </summary>
    internal static string LocalCompiledStaticFilesPath => Path.Combine(Environment.CurrentDirectory, "..", "..", "Maestro", "maestro-angular", "dist", "maestro-angular");

    static PcsStartup()
    {
        LocalHttpsPort = int.Parse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT") ?? "443");

        var metadata = typeof(Program).Assembly
            .GetCustomAttributes()
            .OfType<AssemblyMetadataAttribute>()
            .ToDictionary(m => m.Key, m => m.Value);
        SourceVersion = metadata.TryGetValue("Build.SourceVersion", out var sourceVer) ? sourceVer : null;
        SourceBranch = metadata.TryGetValue("Build.SourceBranch", out var sourceBranch) ? sourceBranch : null;

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
    /// <param name="addDataProtection">Turn on data protection</param>
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
        bool addDataProtection = false,
        string? apiRedirectionTarget = null)
    {
        builder.ConfigureDataProtection(addDataProtection);

        if (keyVaultUri != null)
        {
            builder.Configuration.AddAzureKeyVault(keyVaultUri, azureCredential);
        }

        builder.ConfigureApiServices();

        builder.Services.Configure<CookiePolicyOptions>(
            options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = Microsoft.AspNetCore.Http.SameSiteMode.Lax;

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

        string databaseConnectionString = builder.Configuration.GetRequiredValue(DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, builder.Configuration[ManagedIdentityId]);

        builder.AddBuildAssetRegistry(databaseConnectionString);
        builder.AddWorkitemQueues(azureCredential, waitForInitialization: initializeService);
        builder.AddTelemetry();
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields =
                HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.ResponseStatusCode;
            options.CombineLogs = true;
        });

        builder.Services.Configure<AzureDevOpsTokenProviderOptions>(AzureDevOpsConfiguration, (o, s) => s.Bind(o));
        builder.AddVmrRegistrations(vmrPath, tmpPath);
        builder.AddGitHubClientFactory(builder.Configuration.GetSection(GitHubConfiguration));

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

        // We do not use AddMemoryCache here. We use our own cache because we wish to
        // use a sized cache and some components, such as EFCore, do not implement their caching
        // in such a way that will work with sizing.
        builder.Services.AddSingleton<DarcRemoteMemoryCache>();
        builder.Services.EnableLazy();
        builder.Services.AddMergePolicies();
        builder.Services.Configure<SlaOptions>(builder.Configuration.GetSection(DependencyFlowSLAs));

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

        if (apiRedirectionTarget != null)
        {
            var apiRedirectSection = builder.Configuration.GetSection(ApiRedirectionConfiguration);
            var token = apiRedirectSection[ApiRedirectionToken];
            var managedIdentityId = apiRedirectSection[ManagedIdentityId];

            builder.Services.AddKeyedSingleton<IMaestroApi>(apiRedirectionTarget, MaestroApiFactory.GetAuthenticated(
                apiRedirectionTarget,
                accessToken: token,
                managedIdentityId: managedIdentityId,
                federatedToken: null,
                disableInteractiveAuth: !builder.Environment.IsDevelopment()));
        }

        builder.AddServiceDefaults();

        builder.Services
            .AddControllers()
            .EnableInternalControllers()
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
                options.Conventions.AuthorizeFolder("/", Maestro.Authentication.AuthenticationConfiguration.MsftAuthorizationPolicyName);
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
            builder.Services.Configure<SwaggerOptions>(options =>
            {
                options.SerializeAsV2 = false;
                options.RouteTemplate = "api/{documentName}/swagger.json";
                options.PreSerializeFilters.Add(
                    (doc, req) =>
                    {
                        bool http = builder.Environment.IsDevelopment();
                        doc.Servers =
                        [
                            new()
                            {
                                Url = $"{(http ? "http" : "https")}://{req.Host.Value}/",
                            },
                        ];

                        req.HttpContext.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    });
            });
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
            IConfigurationSection dpConfig = builder.Configuration.GetSection("DataProtection");

            var keyBlobUri = new Uri(dpConfig["KeyBlobUri"] ?? throw new Exception("DataProtection:KeyBlobUri is missing"));
            var dataProtectionKeyUri = new Uri(dpConfig["DataProtectionKeyUri"] ?? throw new Exception("DataProtection:DataProtectionKeyUri is missing"));

            builder.Services.AddDataProtection()
                .PersistKeysToAzureBlobStorage(keyBlobUri, new DefaultAzureCredential())
                .ProtectKeysWithAzureKeyVault(dataProtectionKeyUri, new DefaultAzureCredential())
                .SetDefaultKeyLifetime(DataProtectionKeyLifetime)
                .SetApplicationName(typeof(PcsStartup).FullName!);
        }
    }

    private static void ConfigureApiServices(this WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;
        services.AddApiVersioning(options => options.VersionByQuery("api-version"));
        services.AddSwaggerApiVersioning(
            (version, info) =>
            {
                info.Description =
                    "The Web API enabling access to the .NET Product Constructions Service that supports the [.NET Core Dependency Flow infrastructure](https://github.com/dotnet/arcade/blob/main/Documentation/DependenciesFlowPlan.md).";
                info.Contact = new OpenApiContact
                {
                    Name = ".NET Product Constructions Services",
                    Email = "dotnetprodconsvcs@microsoft.com",
                };
            });

        services.AddSwaggerGen(
            options =>
            {
                // If you get an exception saying 'Identical schemaIds detected for types Maestro.Web.Api.<version>.Models.<something> and Maestro.Web.Api.<different-version>.Models.<something>'
                // Then you should NEVER add the following like the StackOverflow answers will suggest.
                // options.CustomSchemaIds(x => x.FullName);

                // This exception means that you have added something to one of the versions of the api that results in a conflict, because one version of the api cannot have 2 models for the same object
                // e.g. If you add a new api, or just a new model to an existing version (with new or modified properties), every method in the API that can return this type,
                // even nested (e.g. you changed Build, and Subscription contains a Build object), must be updated to return the new type.
                // It could also mean that you forgot to apply [ApiRemoved] to an inherited method that shouldn't be included in the new version

                options.FilterOperations(
                    (op, ctx) =>
                    {
                        var errorSchema = ctx.SchemaGenerator.GenerateSchema(typeof(ApiError), ctx.SchemaRepository);
                        op.Responses["default"] = new OpenApiResponse
                        {
                            Description = "Error",
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = errorSchema,
                                },
                            },
                        };
                        // Replace the large list generated by WepApi with just application/json
                        // We can accept more than just application/json, but the swagger spec defines what we prefer
                        op.OperationId = $"{op.Tags.First().Name}_{op.OperationId}";
                    });

                options.FilterOperations(
                    (op, ctx) =>
                    {
                        var paginated = ctx.MethodInfo.GetCustomAttribute<PaginatedAttribute>();
                        if (paginated != null)
                        {
                            // Add an extension that tells the client generator that this operation is paged with first,prev,next,last urls in the Link header.
                            op.Extensions["x-ms-paginated"] = new PaginatedExtension
                            {
                                PageParameterName = paginated.PageParameterName,
                                PageSizeParameterName = paginated.PageSizeParameterName
                            };
                        }
                    });

                options.RequestBodyFilter<NameRequestBodyFilter>();

                options.FilterSchemas(
                    (schema, ctx) =>
                    {
                        // Types that are not-nullable in C# should be required
                        if (schema.Type == "object")
                        {
                            var required = schema.Required == null
                                ? []
                                : new HashSet<string>(schema.Required.Select(ToCamelCase));
                            schema.Properties =
                                schema.Properties.ToDictionary(
                                    p => ToCamelCase(p.Key),
                                    p => p.Value);
                            foreach (var property in schema.Properties.Keys)
                            {
                                var propertyInfo = ctx.Type.GetRuntimeProperties().FirstOrDefault(p =>
                                    string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase));
                                if (propertyInfo != null)
                                {
                                    var propertyType = propertyInfo.PropertyType;
                                    var shouldBeRequired =
                                        propertyType.IsValueType &&
                                        !(propertyType.IsConstructedGenericType &&
                                          propertyType.GetGenericTypeDefinition() == typeof(Nullable<>));
                                    if (shouldBeRequired)
                                    {
                                        required.Add(property);
                                    }
                                }
                            }

                            schema.Required = required;
                        }
                    });

                options.MapType<TimeSpan>(
                    () => new OpenApiSchema
                    {
                        Type = "string",
                        Format = "duration"
                    });
                options.MapType<TimeSpan?>(
                    () => new OpenApiSchema
                    {
                        Type = "string",
                        Format = "duration"
                    });
                options.MapType<JToken>(() => new OpenApiSchema());

                options.DescribeAllParametersInCamelCase();

                string xmlPath;
                if (builder.Environment.IsDevelopment())
                {
                    xmlPath = Path.GetDirectoryName(typeof(PcsStartup).Assembly.Location)!;
                }
                else
                {
                    xmlPath = builder.Environment.ContentRootPath;
                }

                string xmlFile = Path.Combine(xmlPath, "Maestro.Web.xml");
                if (File.Exists(xmlFile))
                {
                    options.IncludeXmlComments(xmlFile);
                }

                options.AddSecurityDefinition(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Scheme = "bearer",
                        Name = HeaderNames.Authorization
                    });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Id = "Bearer", Type = ReferenceType.SecurityScheme
                            }
                        },
                        []},
                });
            });
    }

    private static async Task ApiRedirectHandler(HttpContext ctx, string apiRedirectionTarget)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<IApplicationBuilder>>();
        logger.LogDebug("Preparing for redirect to '{redirectUrl}'", apiRedirectionTarget);

        var authTasks = Maestro.Authentication.AuthenticationConfiguration.AuthenticationSchemes.Select(ctx.AuthenticateAsync);
        var authResults = await Task.WhenAll(authTasks);
        var success = authResults.FirstOrDefault(t => t.Succeeded);

        if (ctx.User == null || success == null)
        {
            logger.LogInformation("Rejecting redirect because of missing authentication");
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        var authService = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        AuthorizationResult result = await authService.AuthorizeAsync(success.Ticket!.Principal, Maestro.Authentication.AuthenticationConfiguration.MsftAuthorizationPolicyName);
        if (!result.Succeeded)
        {
            logger.LogInformation("Rejecting redirect because authorization failed");
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        using (var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
        {
            logger.LogInformation("Preparing proxy request to {proxyPath}", ctx.Request.Path);
            var uri = new UriBuilder(apiRedirectionTarget)
            {
                Path = ctx.Request.Path,
                Query = ctx.Request.QueryString.ToUriComponent(),
            };

            string absoluteUri = uri.Uri.AbsoluteUri;
            logger.LogInformation("Service proxied request to {url}", absoluteUri);
            await ctx.ProxyRequestAsync(client, absoluteUri,
                async req =>
                {
                    var maestroApi = ctx.RequestServices.GetRequiredKeyedService<IMaestroApi>(apiRedirectionTarget);
                    AccessToken token = await maestroApi.Options.Credentials.GetTokenAsync(new(), CancellationToken.None);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                });
        }
    }

    private static string ToCamelCase(string value)
    {
        return string.Concat(value.Substring(0, 1).ToLowerInvariant(), value.AsSpan(1));
    }

    public static void ConfigureApiExceptions(IApplicationBuilder app)
    {
        app.Run(
            async ctx =>
            {
                var result = new ApiError("An error occured.");
                MvcNewtonsoftJsonOptions jsonOptions =
                    ctx.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value;
                string output = JsonConvert.SerializeObject(result, jsonOptions.SerializerSettings);
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await ctx.Response.WriteAsync(output, Encoding.UTF8);
            });
    }

    public static void ConfigureApi(this IApplicationBuilder app, bool isDevelopment, bool useSwagger, string? apiRedirectionTarget)
    {
        app.UseExceptionHandler(ConfigureApiExceptions);

        var logger = app.ApplicationServices.GetRequiredService<ILogger<IApplicationBuilder>>();

        if (isDevelopment && apiRedirectionTarget != null)
        {
            // Redirect api requests to prod when running locally
            // This is for the `ng serve` local debugging case for the website
            app.MapWhen(
                ctx => ctx.IsGet() &&
                       ctx.Request.Path.StartsWithSegments("/api") &&
                       (!ctx.Request.Path.Value?.EndsWith("swagger.json") ?? false),
                a => a.Run(b => ApiRedirectHandler(b, apiRedirectionTarget)));
        }

        if (useSwagger)
        {
            app.Use(
                (ctx, next) =>
                {
                    if (ctx.Request.Path == "/api/swagger.json")
                    {
                        var vcp = ctx.RequestServices.GetRequiredService<VersionedControllerProvider>();
                        string highestVersion = vcp.Versions.Keys.OrderByDescending(n => n).First();
                        ctx.Request.Path = $"/api/{highestVersion}/swagger.json";
                    }

                    return next();
                });
            app.UseSwagger();
        }

        app.UseAuthentication();
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(e =>
        {
            e.MapRazorPages();

            if (isDevelopment)
            {
                e.MapControllers().AllowAnonymous();
            }
            else
            {
                e.MapControllers();
            }
        });
    }

    // The whole api, only allowing GET requests, with all urls prefixed with _
    public static void ConfigureCookieAuthedApi(this IApplicationBuilder app, bool isDevelopment, string? apiRedirectionTarget)
    {
        app.UseExceptionHandler(ConfigureApiExceptions);

        if (apiRedirectionTarget != null)
        {
            app.MapWhen(ctx => !ctx.Request.Cookies.TryGetValue("Skip-Api-Redirect", out _),
                a =>
                {
                    a.UseAuthentication();
                    a.UseRewriter(new RewriteOptions().AddRewrite("^_/(.*)", "$1", true));
                    a.UseRouting();
                    a.UseAuthorization();
                    a.Run(ctx => ApiRedirectHandler(ctx, apiRedirectionTarget));
                });
        }

        app.UseAuthentication();
        app.UseRewriter(new RewriteOptions().AddRewrite("^_/(.*)", "$1", true));
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(e =>
        {
            if (isDevelopment)
            {
                e.MapControllers().AllowAnonymous();
            }
            else
            {
                e.MapControllers();
            }
        });
    }

    public static void AngularIndexHtmlRedirect(IApplicationBuilder app)
    {
        app.UseRewriter(new RewriteOptions().AddRewrite(".*", "Index", true));
        app.UseAuthentication();
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(e => e.MapRazorPages());
    }

    public static bool IsGet(this HttpContext context)
    {
        return string.Equals(context.Request.Method, "get", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<IActionResult> ProxyRequestAsync(this HttpContext context, HttpClient client, string targetUrl, Action<HttpRequestMessage> configureRequest)
    {
        using (var req = new HttpRequestMessage(HttpMethod.Get, targetUrl))
        {
            foreach (var (key, values) in context.Request.Headers)
            {
                switch (key.ToLower())
                {
                    // We shouldn't copy any of these request headers
                    case "host":
                    case "authorization":
                    case "cookie":
                    case "content-length":
                    case "content-type":
                        continue;
                    default:
                        try
                        {
                            req.Headers.Add(key, values.ToArray());
                        }
                        catch
                        {
                            // Some headers set by the client might be invalid (e.g. contain :)
                        }
                        break;
                }
            }

            configureRequest(req);

            HttpResponseMessage res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            context.Response.RegisterForDispose(res);

            foreach (var (key, values) in res.Headers)
            {
                switch (key.ToLower())
                {
                    // Remove headers that the response doesn't need
                    case "set-cookie":
                    case "x-powered-by":
                    case "x-aspnet-version":
                    case "server":
                    case "transfer-encoding":
                    case "access-control-expose-headers":
                    case "access-control-allow-origin":
                        continue;
                    default:
                        if (!context.Response.Headers.ContainsKey(key))
                        {
                            context.Response.Headers.Append(key, values.ToArray());
                        }

                        break;
                }
            }


            context.Response.StatusCode = (int)res.StatusCode;
            if (res.Content != null)
            {
                foreach (var (key, values) in res.Content.Headers)
                {
                    if (!context.Response.Headers.ContainsKey(key))
                    {
                        context.Response.Headers.Append(key, values.ToArray());
                    }
                }

                using (var data = await res.Content.ReadAsStreamAsync())
                {
                    await data.CopyToAsync(context.Response.Body);
                }
            }

            return new EmptyResult();
        }
    }
}
