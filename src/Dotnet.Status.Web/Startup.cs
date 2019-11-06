// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
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
            Configuration = KeyVaultMappedJsonConfigurationExtensions.CreateConfiguration(env, new AppTokenVaultProvider(), "appsettings{0}.json");
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

            services.AddMvc().WithRazorPagesRoot("/Pages");
            services.AddApplicationInsightsTelemetry();
            services.Configure<GitHubConnectionOptions>(o => { });
            services.Configure<SimpleSigninOptions>(o => { o.ChallengeScheme = GitHubScheme; });
            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddGitHubOAuth(Configuration.GetSection("GitHubAuthentication"), GitHubScheme)
                .AddScheme<UserTokenOptions, GitHubUserTokenHandler>("github-token", o => {})
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
                                GitHubClaimResolver resolver = ctx.HttpContext.RequestServices.GetRequiredService<GitHubClaimResolver>();
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

            services.Configure<GitHubAuthenticationOptions>(GitHubScheme, o => o.SignInScheme = IdentityConstants.ApplicationScheme);

            services.AddAzureTableTokenStore(o => Configuration.GetSection("AzureTableTokenStore").Bind(o));
            
            services.ConfigureExternalCookie(options =>
            {
                options.LoginPath = "/signin";
                options.LogoutPath = "/signout";
            });

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

            services.Configure<MvcOptions>(
                options =>
                {
                    options.Conventions.Add(new DefaultAuthorizeActionModelConvention(MsftAuthorizationPolicyName));
                });

            services.AddScoped<SimpleSigningMiddleware>();
            services.AddGitHubTokenProvider();
            services.Configure<GitHubClientOptions>(o => o.ProductHeader = new ProductHeaderValue(""));
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
            app.UseMiddleware<SimpleSigningMiddleware>();
        }
    }

    public class EmptyUserFactory : IUserFactory<EmptyUser>
    {
        public Task<EmptyUser> CreateAsync(ExternalLoginInfo info)
        {
            return Task.FromResult(new EmptyUser());
        }
    }

    public class GitHubConnectionOptions
    {
        public string Organization { get; set; }
        public string Repository { get; set; }
        public string NotificationTarget { get; set; }
        public ImmutableArray<string> AlertLabels { get; set; }
    }
}
