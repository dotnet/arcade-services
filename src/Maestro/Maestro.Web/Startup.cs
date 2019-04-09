// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Autofac;
using EntityFrameworkCore.Triggers;
using FluentValidation.AspNetCore;
using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.GitHub;
using Maestro.MergePolicies;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Rewrite.Internal;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;

namespace Maestro.Web
{
    public partial class Startup
    {
        static Startup()
        {
            Triggers<BuildChannel>.Inserting += entry =>
            {
                BuildAssetRegistryContext context = entry.Context as BuildAssetRegistryContext;
                BuildChannel entity = entry.Entity;
                Build build = context.Builds.Find(entity.BuildId);

                if (build == null)
                {
                    ILogger<Startup> logger = context.GetService<ILogger<Startup>>();
                    logger.LogError($"Could not find build with id {entity.BuildId} in BAR. Skipping pipeline triggering.");
                }
                else
                {
                    if (build.PublishUsingPipelines && ChannelHasAssociatedReleasePipeline(entity.ChannelId, context))
                    {
                        entry.Cancel = true;
                        var queue = context.GetService<BackgroundQueue>();
                        var releasePipelineRunner = context.GetService<IReleasePipelineRunner>();
                        queue.Post(() => releasePipelineRunner.StartAssociatedReleasePipelinesAsync(entity.BuildId, entity.ChannelId));
                    }
                }
            };

            Triggers<BuildChannel>.Inserted += entry =>
            {
                DbContext context = entry.Context;
                BuildChannel entity = entry.Entity;

                var queue = context.GetService<BackgroundQueue>();
                var dependencyUpdater = context.GetService<IDependencyUpdater>();
                queue.Post(() => dependencyUpdater.StartUpdateDependenciesAsync(entity.BuildId, entity.ChannelId));
            };
        }

        private static bool ChannelHasAssociatedReleasePipeline(int channelId, BuildAssetRegistryContext context)
        {
            return context.Channels
                .Where(ch => ch.Id == channelId)
                .Include(ch => ch.ChannelReleasePipelines)
                .ThenInclude(crp => crp.ReleasePipeline)
                .FirstOrDefault(c => c.ChannelReleasePipelines.Count > 0) != null;
        }

        public Startup(IHostingEnvironment env)
        {
            HostingEnvironment = env;
            Configuration = ServiceHostConfiguration.Get(env);
        }

        public static readonly TimeSpan LoginCookieLifetime = new TimeSpan(days: 120, hours: 0, minutes: 0, seconds: 0);

        public IHostingEnvironment HostingEnvironment { get; set; }
        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
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
                KeyVaultClient kvClient = ServiceHostConfiguration.GetKeyVaultClient(HostingEnvironment);
                KeyBundle key = kvClient.GetKeyAsync(vaultUri, keyVaultKeyIdentifierName).GetAwaiter().GetResult();
                services.AddDataProtection()
                    .PersistKeysToAzureBlobStorage(new Uri(dpConfig["KeyFileUri"]))
                    .ProtectKeysWithAzureKeyVault(kvClient, key.KeyIdentifier.ToString())
                    .SetDefaultKeyLifetime(LoginCookieLifetime * 2);
            }

            ConfigureApiServices(services);

            services.Configure<CookiePolicyOptions>(
                options =>
                {
                    options.CheckConsentNeeded = context => true;
                    options.MinimumSameSitePolicy = SameSiteMode.None;
                });

            services.AddDbContext<BuildAssetRegistryContext>(
                options =>
                {
                    options.UseSqlServer(Configuration.GetSection("BuildAssetRegistry")["ConnectionString"]);
                });

            services.AddMvc()
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1)
                .AddFluentValidation(options => options.RegisterValidatorsFromAssemblyContaining<Startup>())
                .AddRazorPagesOptions(
                    options =>
                    {
                        options.Conventions.AuthorizeFolder("/", MsftAuthorizationPolicyName);
                        options.Conventions.AllowAnonymousToPage("/Index");
                        options.Conventions.AllowAnonymousToPage("/SwaggerUi");
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

            services.AddSingleton<IConfiguration>(Configuration);

            ConfigureAuthServices(services);

            services.AddSingleton<BackgroundQueue>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BackgroundQueue>());

            services.AddServiceFabricService<IDependencyUpdater>("fabric:/MaestroApplication/DependencyUpdater");
            services.AddServiceFabricService<IReleasePipelineRunner>("fabric:/MaestroApplication/ReleasePipelineRunner");

            services.AddGitHubTokenProvider();
            services.Configure<GitHubTokenProviderOptions>(
                (options, provider) =>
                {
                    IConfigurationSection section = Configuration.GetSection("GitHub");
                    section.Bind(options);
                    options.ApplicationName = "Maestro";
                    options.ApplicationVersion = Assembly.GetEntryAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion;
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

            services.AddMergePolicies();
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.AddServiceFabricActor<ISubscriptionActor>();
            builder.AddServiceFabricActor<IPullRequestActor>();
        }

        private void ConfigureApiExceptions(IApplicationBuilder app)
        {
            app.Run(
                async ctx =>
                {
                    var result = new ApiError("An error occured.");
                    MvcJsonOptions jsonOptions =
                        ctx.RequestServices.GetRequiredService<IOptions<MvcJsonOptions>>().Value;
                    string output = JsonConvert.SerializeObject(result, jsonOptions.SerializerSettings);
                    ctx.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    await ctx.Response.WriteAsync(output, Encoding.UTF8);
                });
        }

        private bool DoApiRedirect => !string.IsNullOrEmpty(Configuration.GetSection("ApiRedirect")["uri"]);
        private string ApiRedirectTarget => Configuration.GetSection("ApiRedirect")["uri"];
        private string ApiRedirectToken => Configuration.GetSection("ApiRedirect")["token"];

        private async Task ApiRedirectHandler(HttpContext ctx)
        {
            if (ctx.User == null)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            var authService = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
            AuthorizationResult result = await authService.AuthorizeAsync(ctx.User, MsftAuthorizationPolicyName);
            if (!result.Succeeded)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }


            using (var client = new HttpClient())
            {
                var uri = new UriBuilder(ApiRedirectTarget) {Path = ctx.Request.Path, Query = ctx.Request.QueryString.ToUriComponent(),};
                await ctx.ProxyRequestAsync(client, uri.Uri.AbsoluteUri,
                    req =>
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiRedirectToken);
                    });
            }
        }

        private void ConfigureApi(IApplicationBuilder app)
        {
            app.UseExceptionHandler(ConfigureApiExceptions);

            app.UseAuthentication();

            if (HostingEnvironment.IsDevelopment() && !Program.RunningInServiceFabric())
            {
                // Redirect api requests to prod when running locally outside of service fabric
                // This is for the `ng serve` local debugging case for the website
                app.MapWhen(
                    ctx => IsGet(ctx) && ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Path != "/api/swagger.json",
                    a =>
                    {
                        a.Run(ApiRedirectHandler);
                    });
            }

            app.UseMvc();

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

            app.UseSwagger(
                options =>
                {
                    options.RouteTemplate = "api/{documentName}/swagger.json";
                    options.PreSerializeFilters.Add(
                        (doc, req) =>
                        {
                            doc.Host = req.Host.Value;
                            if (HostingEnvironment.IsDevelopment() && !Program.RunningInServiceFabric())
                            {
                                doc.Schemes = new List<string> {"http"};
                            }
                            else
                            {
                                doc.Schemes = new List<string> {"https"};
                            }

                            req.HttpContext.Response.Headers["Access-Control-Allow-Origin"] = "*";
                        });
                });
        }

        // The whole api, only allowing GET requests, with all urls prefixed with _
        private void ConfigureCookieAuthedApi(IApplicationBuilder app)
        {
            app.UseExceptionHandler(ConfigureApiExceptions);
            app.UseAuthentication();

            app.UseRewriter(new RewriteOptions
            {
                Rules =
                {
                    new RewriteRule("^_/(.*)", "$1", true),
                },
            });

            // Redirect the entire cookie-authed api if it is in settings.
            if (DoApiRedirect)
            {
                app.Run(ApiRedirectHandler);
            }
            else
            {
                app.UseMvc();
            }

        }

        private static bool IsGet(HttpContext context)
        {
            return string.Equals(context.Request.Method, "get", StringComparison.OrdinalIgnoreCase);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            if (env.IsDevelopment() && !Program.RunningInServiceFabric())
            {
                // In local dev with the `ng serve` scenario, just redirect /_/api to /api
                app.UseRewriter(new RewriteOptions
                {
                    Rules =
                    {
                        new RewriteRule("^_/(.*)", "$1", true),
                    },
                });
            }

            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), ConfigureApi);
            if (Program.RunningInServiceFabric())
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

            app.UseMvc();
            app.MapWhen(IsGet, AngularIndexHtmlRedirect);
        }

        private static void AngularIndexHtmlRedirect(IApplicationBuilder app)
        {
            app.UseRewriter(new RewriteOptions
            {
                Rules =
                {
                    new RewriteRule(".*", "Index", true),
                },
            });
            app.UseMvc();
        }
    }
}
