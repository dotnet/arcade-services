// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.DotNet.Configuration.Extensions;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Octokit;

namespace DotNet.Status.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostingEnvironment env)
        {
            Configuration = KeyVaultMappedJsonConfigurationExtensions.CreateConfiguration(configuration, env, new AppTokenVaultProvider(), "appsettings{0}.json");
            Env = env;
        }
        
        public IHostingEnvironment Env { get; }
        public IConfiguration Configuration { get; }

        public const string GitHubScheme = "github";
        public const string MsftAuthorizationPolicyName = "msft";

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IKeyVaultProvider, AppTokenVaultProvider>();
            
            if (Env.IsDevelopment())
            {
                services.AddDataProtection()
                    .SetDefaultKeyLifetime(TimeSpan.FromDays(14));
            }
            else
            {
                IConfigurationSection dpConfig = Configuration.GetSection("DataProtection");
                AppTokenVaultProvider keyVaultProvider = new AppTokenVaultProvider();
                KeyVaultClient kvClient = keyVaultProvider.CreateKeyVaultClient();
                string vaultUri = Configuration["KeyVaultUri"];
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
            services.Configure<GitHubConnectionOptions>(Configuration.GetSection("GitHub").Bind);
            services.Configure<GitHubTokenProviderOptions>(Configuration.GetSection("GitHubAppAuth").Bind);

            services.Configure<SimpleSigninOptions>(o => { o.ChallengeScheme = GitHubScheme; });
            services.ConfigureExternalCookie(options =>
            {
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
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
        }

        private void AddServices(IServiceCollection services)
        {
            services.AddMvc().WithRazorPagesRoot("/Pages").AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/", MsftAuthorizationPolicyName).AllowAnonymousToPage("/Index"));
            services.AddApplicationInsightsTelemetry(Configuration.GetSection("ApplicationInsights").Bind);
            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddGitHubOAuth(Configuration.GetSection("GitHubAuthentication"), GitHubScheme)
                .AddScheme<UserTokenOptions, GitHubUserTokenHandler>("github-token", o => { })
                .AddCookie(IdentityConstants.ApplicationScheme,
                    o =>
                    {
                        o.ExpireTimeSpan = TimeSpan.FromDays(7);
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
                            }
                        };
                    })
                ;
            services.AddContextAwareAuthenticationScheme(o =>
            {
                o.SelectScheme = p => p.StartsWithSegments("/api") ? "github-token" : IdentityConstants.ApplicationScheme;
            });
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
                                policy.RequireRole("github:team:dotnet/dnceng");
                            }
                        });
                });

            services.AddScoped<SimpleSigninMiddleware>();
            services.AddGitHubTokenProvider();
            services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            if (Env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHttpsRedirection();
            }

            app.UseAuthentication();
            app.UseMvc();
            app.UseMiddleware<SimpleSigninMiddleware>();
        }
    }
}
