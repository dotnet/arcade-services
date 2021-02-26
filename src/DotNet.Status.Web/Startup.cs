// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Health;
using Microsoft.DotNet.Services.Utility;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace DotNet.Status.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Env = env;
        }

        public IWebHostEnvironment Env { get; }
        public IConfiguration Configuration { get; }

        public const string GitHubScheme = "github";
        public const string MsftAuthorizationPolicyName = "msft";

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(Configuration);
            if (Env.IsDevelopment())
            {
                services.AddDataProtection()
                    .SetDefaultKeyLifetime(TimeSpan.FromDays(14));
            }
            else
            {
                IConfigurationSection dpConfig = Configuration.GetSection("DataProtection");
                var provider = new AzureServiceTokenProvider();
                var kvClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(provider.KeyVaultTokenCallback));
                string vaultUri = Configuration[ConfigurationConstants.KeyVaultUriConfigurationKey];
                string keyVaultKeyIdentifierName = dpConfig["KeyIdentifier"];
                KeyBundle key = kvClient.GetKeyAsync(vaultUri, keyVaultKeyIdentifierName).GetAwaiter().GetResult();
                services.AddDataProtection()
                    .PersistKeysToAzureBlobStorage(new Uri(dpConfig["KeyFileUri"]))
                    .ProtectKeysWithAzureKeyVault(kvClient, key.KeyIdentifier.ToString())
                    .SetDefaultKeyLifetime(TimeSpan.FromDays(14))
                    .SetApplicationName(typeof(Startup).FullName);
            }

            AddServices(services);
            ConfigureConfiguration(services);
        }

        private void ConfigureConfiguration(IServiceCollection services)
        {
            services.Configure<TeamMentionForwardingOptions>(Configuration.GetSection("IssueMentionForwarding"));
            services.Configure<GitHubConnectionOptions>(Configuration.GetSection("GitHub"));
            services.Configure<GrafanaOptions>(Configuration.GetSection("Grafana"));
            services.Configure<GitHubTokenProviderOptions>(Configuration.GetSection("GitHubAppAuth"));
            services.Configure<ZenHubOptions>(Configuration.GetSection("ZenHub"));
            services.Configure<BuildMonitorOptions>(Configuration.GetSection("BuildMonitor"));
            services.Configure<KustoOptions>(Configuration.GetSection("Kusto"));

            services.Configure<SimpleSigninOptions>(o => { o.ChallengeScheme = GitHubScheme; });
            services.ConfigureExternalCookie(options =>
            {
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api"))
                        {
                            ctx.Response.StatusCode = 401;
                            return Task.CompletedTask;
                        }

                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api"))
                        {
                            ctx.Response.StatusCode = 403;
                            return Task.CompletedTask;
                        }
                        ctx.Response.StatusCode = 403;
                        return Task.CompletedTask;
                    },
                };
            });
            services.Configure<GitHubAuthenticationOptions>(GitHubScheme, o => o.SignInScheme = IdentityConstants.ApplicationScheme);
            services.Configure<MvcOptions>(
                options =>
                {
                    options.Conventions.Add(new DefaultAuthorizeActionModelConvention(MsftAuthorizationPolicyName));
                });
            services.Configure<GitHubClientOptions>(o =>
                o.ProductHeader = new ProductHeaderValue("DotNetEngineeringStatus",
                    Assembly.GetEntryAssembly().GetName().Version.ToString()));
            services.Configure<ExponentialRetryOptions>(o => { });
            services.Configure<HttpClientFactoryOptions>(
                o => o.HttpMessageHandlerBuilderActions.Add(EnableCertificateRevocationCheck)
            );
        }

        private static void EnableCertificateRevocationCheck(HttpMessageHandlerBuilder builder)
        {
            if (builder.PrimaryHandler is HttpClientHandler httpHandler)
            {
                httpHandler.CheckCertificateRevocationList = true;
            }
        }

        private Task AddSecurityHeaders(HttpContext context, Func<Task> next)
        {
            if (!context.Response.Headers.ContainsKey("X-XSS-Protection"))
            {
                context.Response.Headers.Add("X-XSS-Protection", "1");
            }

            if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
            {
                context.Response.Headers.Add("X-Frame-Options", "DENY");
            }

            if (!context.Response.Headers.ContainsKey("X-Content-Type-Options"))
            {
                context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            }

            if (!context.Response.Headers.ContainsKey("Referrer-Policy"))
            {
                context.Response.Headers.Add("Referrer-Policy", "no-referrer-when-downgrade");
            }
            return next();
        }

        private void AddServices(IServiceCollection services)
        {
            services.AddRazorPages(o =>
                {
                    o.Conventions
                        .AuthorizeFolder("/", MsftAuthorizationPolicyName)
                        .AllowAnonymousToPage("/Index")
                        .AllowAnonymousToPage("/Status")
                        .AllowAnonymousToPage("/Routes")
                        .AllowAnonymousToPage("/Error");
                    o.RootDirectory = "/Pages";
                });

            services.AddControllers()
                .AddGitHubWebHooks();

            services.AddApplicationInsightsTelemetry(Configuration.GetSection("ApplicationInsights").Bind);
            services.Configure<LoggerFilterOptions>(o =>
            {
                // This handler is added by 'AddApplicationInsightsTelemetry' above and hard limits
                // and reporting below "warning", which basically kills all logging
                // Remove it, we already configured the filters in Program.cs
                o.Rules.Remove(o.Rules.FirstOrDefault(r =>
                    r.ProviderName ==
                    "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider"));

                // These two categories log a lot of noise at "Information", let's raise them to warning
                o.Rules.Add(new LoggerFilterRule(null, "Microsoft.AspNetCore.Mvc.ViewFeatures.Filters.ValidateAntiforgeryTokenAuthorizationFilter", LogLevel.Warning, null));
                o.Rules.Add(new LoggerFilterRule(null, "Microsoft.AspNetCore.Mvc.ViewFeatures.Filters.AutoValidateAntiforgeryTokenAuthorizationFilter", LogLevel.Warning, null));
            });

            services.AddAuthentication("contextual")
                .AddPolicyScheme("contextual", "Contextual Scheme",
                    o => { o.ForwardDefaultSelector = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/api/webhooks"))
                        {
                            return "nothing";
                        }
                        if (context.Request.Path.StartsWithSegments("/api"))
                        {
                            return "github-token";
                        }

                        return IdentityConstants.ApplicationScheme;
                    }; })
                .AddGitHubOAuth(Configuration.GetSection("GitHubAuthentication"), GitHubScheme)
                .AddScheme<NothingOptions, NothingHandler>("nothing", o => { })
                .AddScheme<UserTokenOptions, GitHubUserTokenHandler>("github-token", o => { })
                .AddCookie(IdentityConstants.ApplicationScheme,
                    o =>
                    {
                        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                        o.SlidingExpiration = true;
                        o.Cookie.IsEssential = true;
                        o.LoginPath = "/signin";
                        o.LogoutPath = "/signout";
                        o.ReturnUrlParameter = "r";
                        o.Events = new CookieAuthenticationEvents
                        {
                            OnValidatePrincipal = async ctx =>
                            {
                                GitHubClaimResolver resolver =
                                    ctx.HttpContext.RequestServices.GetRequiredService<GitHubClaimResolver>();
                                ClaimsIdentity identity = ctx.Principal.Identities.FirstOrDefault();
                                identity?.AddClaims(await resolver.GetMembershipClaims(resolver.GetAccessToken(ctx.Principal)));
                            },
                        };
                    })
                .AddExternalCookie()
                ;
            services.AddAzureTableTokenStore(o => Configuration.GetSection("AzureTableTokenStore").Bind(o));
            services.AddAuthorization(
                options =>
                {
                    options.AddPolicy(
                        MsftAuthorizationPolicyName,
                        policy =>
                        {
                            policy.RequireAuthenticatedUser();
                            if (!Env.IsDevelopment())
                            {
                                policy.RequireRole(GitHubClaimResolver.GetTeamRole("dotnet","dnceng"), GitHubClaimResolver.GetTeamRole("dotnet","bots-high"));
                            }
                        });
                });
            services.AddKustoIngest(options => Configuration.GetSection("Kusto").Bind(options));

            services.AddScoped<SimpleSigninMiddleware>();
            services.AddGitHubTokenProvider();
            services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();

            services.AddSingleton<ZenHubClient>();
            services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
            services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            services.AddSingleton<ITimelineIssueTriage, TimelineIssueTriage>();
            services.AddSingleton<ExponentialRetry>();
            services.AddSingleton<ISystemClock, SystemClock>();
            services.AddSingleton<Microsoft.Extensions.Internal.ISystemClock, Microsoft.Extensions.Internal.SystemClock>();
            services.AddHttpClient();
            services.AddHealthReporting(
                b =>
                {
                    b.AddLogging();
                    b.AddAzureTable((o, p) => o.WriteSasUri = p.GetRequiredService<IConfiguration>()["HealthTableUri"]);
                });

            services.AddScoped<ITeamMentionForwarder, TeamMentionForwarder>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseStatusCodePagesWithReExecute("/Status", "?code={0}");

            if (Env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHttpsRedirection();
            }
            app.UseAuthentication();
            app.UseRouting();
            app.UseAuthorization();
            app.Use(AddSecurityHeaders);
            app.UseEndpoints(e =>
            {
                e.MapRazorPages();
                e.MapControllers();
            });
            app.UseStaticFiles();
            app.UseMiddleware<SimpleSigninMiddleware>();
        }
    }

    internal class NothingOptions : AuthenticationSchemeOptions
    {
    }

    internal class NothingHandler : AuthenticationHandler<NothingOptions>
    {
        public NothingHandler(IOptionsMonitor<NothingOptions> options, ILoggerFactory logger, UrlEncoder encoder, Microsoft.AspNetCore.Authentication.ISystemClock clock) : base(options, logger, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
