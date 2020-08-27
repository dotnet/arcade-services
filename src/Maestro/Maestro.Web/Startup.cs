// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggers;
using FluentValidation.AspNetCore;
using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Swagger;

namespace Maestro.Web
{
    public partial class Startup : StartupBase
    {
        // https://github.com/dotnet/core-eng/issues/6819
        // TODO: Remove once the repo in this list is ready to onboard to yaml publishing.
        private static readonly HashSet<string> ReposWithoutAssetLocationAllowList =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https://github.com/aspnet/AspNetCore" };

        static Startup()
        {
            Triggers<BuildChannel>.Inserted += entry =>
            {
                BuildAssetRegistryContext context = entry.Context as BuildAssetRegistryContext;
                ILogger<Startup> logger = context.GetService<ILogger<Startup>>();
                BuildChannel entity = entry.Entity;

                Build build = context.Builds
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
                        var queue = context.GetService<IBackgroundQueue>();
                        queue.Post<StartDependencyUpdate>(StartDependencyUpdate.CreateArgs(entity));
                    }
                    else
                    {
                        logger.LogInformation($"Skipping Dependency update for Build {entity.BuildId} because it contains no assets in valid locations");
                    }
                }
            };
        }

        private class StartDependencyUpdate : IBackgroundWorkItem
        {
            private readonly IDependencyUpdater _updater;

            public StartDependencyUpdate(IDependencyUpdater updater)
            {
                _updater = updater;
            }

            public Task ProcessAsync(JToken argumentToken)
            {
                var argVal = argumentToken.ToObject<Arguments>();
                return _updater.StartUpdateDependenciesAsync(argVal.BuildId, argVal.ChannelId);
            }

            public static JToken CreateArgs(BuildChannel channel)
            {
                return JToken.FromObject(new Arguments {BuildId = channel.BuildId, ChannelId = channel.ChannelId});
            }

            private struct Arguments
            {
                public int BuildId;
                public int ChannelId;
            }
        }

        public Startup(IConfiguration configuration, IHostEnvironment env)
        {
            HostingEnvironment = env;
            Configuration = configuration;
        }

        public static readonly TimeSpan LoginCookieLifetime = new TimeSpan(days: 120, hours: 0, minutes: 0, seconds: 0);

        public IHostEnvironment HostingEnvironment { get; }
        public IConfiguration Configuration { get; }

        public override void ConfigureServices(IServiceCollection services)
        {
            if (HostingEnvironment.IsDevelopment())
            {
                services.AddDataProtection()
                    .SetDefaultKeyLifetime(LoginCookieLifetime * 2);
            }
            else
            {
                IConfigurationSection dpConfig = Configuration.GetSection("DataProtection");

                string vaultUri = Configuration["KeyVaultUri"];
                string keyVaultKeyIdentifierName = dpConfig["KeyIdentifier"];
                var provider = new AzureServiceTokenProvider();
                var kvClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(provider.KeyVaultTokenCallback));
                KeyBundle key = kvClient.GetKeyAsync(vaultUri, keyVaultKeyIdentifierName).GetAwaiter().GetResult();


                services.AddDataProtection()
                    .PersistKeysToAzureBlobStorage(new Uri(dpConfig["KeyFileUri"]))
                    .ProtectKeysWithAzureKeyVault(kvClient, key.KeyIdentifier.ToString())
                    .SetDefaultKeyLifetime(LoginCookieLifetime * 2)
                    .SetApplicationName(typeof(Startup).FullName);
            }

            ConfigureApiServices(services);

            services.Configure<CookiePolicyOptions>(
                options =>
                {
                    options.CheckConsentNeeded = context => true;
                    options.MinimumSameSitePolicy = SameSiteMode.Lax;

                    options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;

                    if (HostingEnvironment.IsDevelopment())
                    {
                        options.Secure = CookieSecurePolicy.SameAsRequest;
                    }
                    else
                    {
                        options.Secure = CookieSecurePolicy.Always;
                    }
                });

            services.AddBuildAssetRegistry(
                options =>
                {
                    options.UseSqlServer(Configuration.GetSection("BuildAssetRegistry")["ConnectionString"]);
                });

            services.AddRazorPages(options =>
                {
                    options.Conventions.AuthorizeFolder("/", MsftAuthorizationPolicyName);
                    options.Conventions.AllowAnonymousToPage("/Index");
                    options.Conventions.AllowAnonymousToPage("/Error");
                    options.Conventions.AllowAnonymousToPage("/SwaggerUi");
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                .AddFluentValidation(options => options.RegisterValidatorsFromAssemblyContaining<Startup>())
                .AddGitHubWebHooks()
                .AddApiPagination()
                .AddCookieTempDataProvider(
                    options =>
                    {
                        // Cookie Policy will not send this cookie unless we mark it as Essential
                        // The application will not function without this cookie.
                        options.Cookie.IsEssential = true;
                    });

            services.AddControllers()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    options.SerializerSettings.Converters.Add(new StringEnumConverter
                        {NamingStrategy = new CamelCaseNamingStrategy()});
                    options.SerializerSettings.Converters.Add(
                        new IsoDateTimeConverter
                        {
                            DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ",
                            DateTimeStyles = DateTimeStyles.AdjustToUniversal
                        });
                });

            services.AddSingleton(Configuration);

            ConfigureAuthServices(services);
            
            services.AddSingleton<BackgroundQueue>();
            services.AddSingleton<IBackgroundQueue>(provider => provider.GetRequiredService<BackgroundQueue>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BackgroundQueue>());

            services.AddServiceFabricService<IDependencyUpdater>("fabric:/MaestroApplication/DependencyUpdater");

            services.AddGitHubTokenProvider();
            services.Configure<GitHubClientOptions>(o =>
            {
                o.ProductHeader = new Octokit.ProductHeaderValue("Maestro",
                    Assembly.GetEntryAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion);
            });
            services.Configure<GitHubTokenProviderOptions>(
                (options, provider) =>
                {
                    IConfigurationSection section = Configuration.GetSection("GitHub");
                    section.Bind(options);
                });
            services.AddAzureDevOpsTokenProvider();
            services.Configure<AzureDevOpsTokenProviderOptions>(
                (options, provider) =>
                {
                    var config = provider.GetRequiredService<IConfiguration>();
                    var tokenMap = config.GetSection("AzureDevOps:Tokens").GetChildren();
                    foreach (IConfigurationSection token in tokenMap)
                    {
                        options.Tokens.Add(token.GetValue<string>("Account"), token.GetValue<string>("Token"));
                    }
                });
            services.AddKustoClientProvider(
                options =>
                {
                    IConfigurationSection section = Configuration.GetSection("Kusto");
                    section.Bind(options);
                });

            // We do not use AddMemoryCache here. We use our own cache because we wish to
            // use a sized cache and some components, such as EFCore, do not implement their caching
            // in such a way that will work with sizing.
            services.AddSingleton<DarcRemoteMemoryCache>();

            services.AddScoped<IRemoteFactory, DarcRemoteFactory>();
            services.AddSingleton(typeof(IActorProxyFactory<>), typeof(ActorProxyFactory<>));

            services.EnableLazy();

            services.AddMergePolicies();
            services.Configure<SwaggerOptions>(options =>
            {
                options.SerializeAsV2 = false;
                options.RouteTemplate = "api/{documentName}/swagger.json";
                options.PreSerializeFilters.Add(
                    (doc, req) =>
                    {
                        bool http = HostingEnvironment.IsDevelopment() && !ServiceFabricHelpers.RunningInServiceFabric();
                        doc.Servers = new List<OpenApiServer>
                        {
                            new OpenApiServer
                            {
                                Url = $"{(http ? "http" : "https")}://{req.Host.Value}/",
                            },
                        };

                        req.HttpContext.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    });
            });

            services.AddSingleton<ISystemClock, SystemClock>();
        }

        private void ConfigureApiExceptions(IApplicationBuilder app)
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

        private bool DoApiRedirect => !string.IsNullOrEmpty(ApiRedirectTarget);
        private string ApiRedirectTarget => Configuration.GetSection("ApiRedirect")["uri"];
        private string ApiRedirectToken => Configuration.GetSection("ApiRedirect")["token"];

        private async Task ApiRedirectHandler(HttpContext ctx)
        {
            var logger = ctx.RequestServices.GetRequiredService<ILogger<Startup>>();
            logger.LogInformation("Preparing for redirect: enabled: '{redirectEnabled}', uri: '{redirectUrl}'", DoApiRedirect, ApiRedirectTarget);
            if (ctx.User == null)
            {
                logger.LogInformation("Rejecting redirect because of missing authentication");
                ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            var authService = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
            AuthorizationResult result = await authService.AuthorizeAsync(ctx.User, MsftAuthorizationPolicyName);
            if (!result.Succeeded)
            {
                logger.LogInformation("Rejecting redirect because authorization failed");
                ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }


            using (var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
            {
                logger.LogInformation("Preparing proxy request to {proxyPath}", ctx.Request.Path);
                var uri = new UriBuilder(ApiRedirectTarget)
                {
                    Path = ctx.Request.Path,
                    Query = ctx.Request.QueryString.ToUriComponent(),
                };

                string absoluteUri = uri.Uri.AbsoluteUri;
                logger.LogInformation("Service proxied request to {url}", absoluteUri);
                await ctx.ProxyRequestAsync(client, absoluteUri,
                    req =>
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiRedirectToken);
                    });
            }
        }

        private void ConfigureApi(IApplicationBuilder app)
        {
            app.UseExceptionHandler(ConfigureApiExceptions);

            var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();

            logger.LogInformation(
                "Configuring API, env: '{env}', isDev: {isDev}, inSF: {inSF}, forceLocal: '{forceLocal}'",
                HostingEnvironment.EnvironmentName,
                HostingEnvironment.IsDevelopment(),
                ServiceFabricHelpers.RunningInServiceFabric(),
                Configuration["ForceLocalApi"]
            );

            if (HostingEnvironment.IsDevelopment() &&
                !ServiceFabricHelpers.RunningInServiceFabric() &&
                !string.Equals(
                    Configuration["ForceLocalApi"],
                    true.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Redirect api requests to prod when running locally outside of service fabric
                // This is for the `ng serve` local debugging case for the website
                app.MapWhen(
                    ctx => IsGet(ctx) &&
                        ctx.Request.Path.StartsWithSegments("/api") &&
                        !ctx.Request.Path.Value.EndsWith("swagger.json"),
                    a => { a.Run(ApiRedirectHandler); });
            }

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
            
            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(e =>
            {
                e.MapRazorPages();
                e.MapControllers();
            });
        }

        // The whole api, only allowing GET requests, with all urls prefixed with _
        private void ConfigureCookieAuthedApi(IApplicationBuilder app)
        {
            app.UseExceptionHandler(ConfigureApiExceptions);

            if (DoApiRedirect)
            {
                app.MapWhen(ctx => !ctx.Request.Cookies.TryGetValue("Skip-Api-Redirect", out _),
                    a =>
                    {
                        a.UseAuthentication();
                        a.UseRewriter(new RewriteOptions().AddRewrite("^_/(.*)", "$1", true));
                        a.UseRouting();
                        a.UseAuthorization();
                        a.Run(ApiRedirectHandler);
                    });
            }
            
            app.UseAuthentication();
            app.UseRewriter(new RewriteOptions().AddRewrite("^_/(.*)", "$1", true));
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(e =>
            {
                e.MapControllers();
            });
        }

        private static bool IsGet(HttpContext context)
        {
            return string.Equals(context.Request.Method, "get", StringComparison.OrdinalIgnoreCase);
        }

        public override void Configure(IApplicationBuilder app)
        {
            if (HostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            // Add security headers
            app.Use(
                (ctx, next) =>
                {
                    ctx.Response.OnStarting(() =>
                    {
                        if (!ctx.Response.Headers.ContainsKey("X-XSS-Protection"))
                        {
                            ctx.Response.Headers.Add("X-XSS-Protection", "1");
                        }

                        if (!ctx.Response.Headers.ContainsKey("X-Frame-Options"))
                        {
                            ctx.Response.Headers.Add("X-Frame-Options", "DENY");
                        }

                        if (!ctx.Response.Headers.ContainsKey("X-Content-Type-Options"))
                        {
                            ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                        }

                        if (!ctx.Response.Headers.ContainsKey("Referrer-Policy"))
                        {
                            ctx.Response.Headers.Add("Referrer-Policy", "no-referrer-when-downgrade");
                        }

                        return Task.CompletedTask;
                    });

                    return next();
                });

            if (HostingEnvironment.IsDevelopment() && !ServiceFabricHelpers.RunningInServiceFabric())
            {
                // In local dev with the `ng serve` scenario, just redirect /_/api to /api
                app.UseRewriter(new RewriteOptions().AddRewrite("^_/(.*)", "$1", true));
            }

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), ConfigureApi);
            if (ServiceFabricHelpers.RunningInServiceFabric())
            {
                app.MapWhen(
                    ctx => ctx.Request.Path.StartsWithSegments("/_/api") && IsGet(ctx),
                    ConfigureCookieAuthedApi);
            }

            app.UseRewriter(new RewriteOptions().AddRedirect("^swagger(/ui)?/?$", "/swagger/ui/index.html"));
            app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
            app.UseCookiePolicy();
            app.UseStaticFiles();
            
            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(e =>
                {
                    e.MapRazorPages();
                    e.MapControllers();
                }
            );
            app.MapWhen(IsGet, AngularIndexHtmlRedirect);
        }

        private static void AngularIndexHtmlRedirect(IApplicationBuilder app)
        {
            app.UseRewriter(new RewriteOptions().AddRewrite(".*", "Index", true));
            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(e => { e.MapRazorPages(); });
        }
    }
}
