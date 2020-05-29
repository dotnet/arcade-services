// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Pages.Account;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Octokit;

namespace Maestro.Web
{
    public partial class Startup
    {
        public const string GitHubScheme = "GitHub";

        public const string MsftAuthorizationPolicyName = "msft";

        private static string ProductName { get; } = "Maestro";

        private static string ProductVersion { get; } = Assembly.GetEntryAssembly().GetName().Version.ToString();

        private void ConfigureAuthServices(IServiceCollection services)
        {
            services.AddIdentity<ApplicationUser, IdentityRole<int>>(
                    options => { options.Lockout.AllowedForNewUsers = false; })
                .AddEntityFrameworkStores<BuildAssetRegistryContext>();
            
            services.AddAuthentication(options =>
                    {
                        // The "AddIdentity" above messed with these, so we need to re-mess with them.
                        options.DefaultChallengeScheme = options.DefaultAuthenticateScheme =
                            options.DefaultSignInScheme = options.DefaultScheme = "Contextual";
                    })
                .AddPolicyScheme("Contextual","Contextual",
                    policyOptions => { policyOptions.ForwardDefaultSelector = ctx => ctx.Request.Path.StartsWithSegments("/api") ? PersonalAccessTokenDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme; })
                .AddGitHubOAuth(Configuration.GetSection("GitHubAuthentication"), GitHubScheme)
                .AddPersonalAccessToken<ApplicationUser>(
                    options =>
                    {
                        options.Events = new PersonalAccessTokenEvents<ApplicationUser>
                        {
                            OnSetTokenHash = async context =>
                            {
                                var dbContext = context.HttpContext.RequestServices
                                    .GetRequiredService<BuildAssetRegistryContext>();
                                int userId = context.User.Id;
                                var token = new ApplicationUserPersonalAccessToken
                                {
                                    ApplicationUserId = userId,
                                    Name = context.Name,
                                    Hash = context.Hash,
                                    Created = DateTimeOffset.UtcNow
                                };
                                await dbContext.Set<ApplicationUserPersonalAccessToken>().AddAsync(token);
                                await dbContext.SaveChangesAsync();

                                return token.Id;
                            },
                            OnGetTokenHash = async context =>
                            {
                                var dbContext = context.HttpContext.RequestServices
                                    .GetRequiredService<BuildAssetRegistryContext>();
                                ApplicationUserPersonalAccessToken token = await dbContext
                                    .Set<ApplicationUserPersonalAccessToken>()
                                    .Where(t => t.Id == context.TokenId)
                                    .Include(t => t.ApplicationUser)
                                    .FirstOrDefaultAsync();
                                if (token != null)
                                {
                                    context.Success(token.Hash, token.ApplicationUser);
                                }
                            },
                            OnValidatePrincipal = async context =>
                            {
                                ApplicationUser user = context.User;
                                var dbContext = context.HttpContext.RequestServices
                                    .GetRequiredService<BuildAssetRegistryContext>();
                                var userManager = context.HttpContext.RequestServices
                                    .GetRequiredService<UserManager<ApplicationUser>>();
                                var signInManager = context.HttpContext.RequestServices
                                    .GetRequiredService<SignInManager<ApplicationUser>>();
                                var gitHubClaimResolver = context.HttpContext.RequestServices
                                    .GetRequiredService<GitHubClaimResolver>();

                                await UpdateUserIfNeededAsync(user, dbContext, userManager, signInManager, gitHubClaimResolver);

                                ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user);
                                context.ReplacePrincipal(principal);
                            }
                        };
                    });
            services.ConfigureExternalCookie(
                options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                    options.ReturnUrlParameter = "returnUrl";
                    options.LoginPath = "/Account/SignIn";
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
                            ctx.Response.StatusCode = 403;
                            return Task.CompletedTask;
                        },
                    };
                });
            services.ConfigureApplicationCookie(
                options =>
                {
                    options.ExpireTimeSpan = LoginCookieLifetime;
                    options.SlidingExpiration = true;
                    options.Events = new CookieAuthenticationEvents
                    {
                        OnSigningIn = async ctx =>
                        {
                            var dbContext = ctx.HttpContext.RequestServices
                                .GetRequiredService<BuildAssetRegistryContext>();
                            var signInManager = ctx.HttpContext.RequestServices
                                .GetRequiredService<SignInManager<ApplicationUser>>();
                            var userManager = ctx.HttpContext.RequestServices
                                .GetRequiredService<UserManager<ApplicationUser>>();
                            ExternalLoginInfo info = await signInManager.GetExternalLoginInfoAsync();

                            var user = await userManager.GetUserAsync(ctx.Principal);
                            await UpdateUserTokenAsync(dbContext, userManager, user, info);

                            IdentityOptions identityOptions = ctx.HttpContext.RequestServices
                                .GetRequiredService<IOptions<IdentityOptions>>()
                                .Value;

                            // replace the ClaimsPrincipal we are about to serialize to the cookie with a reference
                            Claim claim = ctx.Principal.Claims.First(
                                c => c.Type == identityOptions.ClaimsIdentity.UserIdClaimType);
                            Claim[] claims = {claim};
                            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
                            ctx.Principal = new ClaimsPrincipal(identity);
                        },
                        OnValidatePrincipal = async ctx =>
                        {
                            var dbContext = ctx.HttpContext.RequestServices
                                .GetRequiredService<BuildAssetRegistryContext>();
                            var userManager = ctx.HttpContext.RequestServices
                                .GetRequiredService<UserManager<ApplicationUser>>();
                            var signInManager = ctx.HttpContext.RequestServices
                                .GetRequiredService<SignInManager<ApplicationUser>>();
                            var gitHubClaimResolver = ctx.HttpContext.RequestServices
                                .GetRequiredService<GitHubClaimResolver>();

                            // extract the userId from the ClaimsPrincipal and read the user from the Db
                            ApplicationUser user = await userManager.GetUserAsync(ctx.Principal);
                            if (user == null)
                            {
                                ctx.RejectPrincipal();
                            }
                            else
                            {
                                await UpdateUserIfNeededAsync(user, dbContext, userManager, signInManager, gitHubClaimResolver);

                                ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user);
                                ctx.ReplacePrincipal(principal);
                            }
                        }
                    };
                });

            services.AddAuthorization(
                options =>
                {
                    options.AddPolicy(
                        MsftAuthorizationPolicyName,
                        policy =>
                        {
                            policy.RequireAuthenticatedUser();
                            if (!HostingEnvironment.IsDevelopment())
                            {
                                policy.RequireRole(GitHubClaimResolver.GetTeamRole("dotnet","dnceng"), GitHubClaimResolver.GetTeamRole("dotnet","arcade-contrib"));
                            }
                        });
                });

            services.Configure<MvcOptions>(
                options =>
                {
                    options.Conventions.Add(new DefaultAuthorizeActionModelConvention(MsftAuthorizationPolicyName));
                });
        }

        private static async Task UpdateUserTokenAsync(BuildAssetRegistryContext dbContext,
            UserManager<ApplicationUser> userManager, ApplicationUser user, ExternalLoginInfo info)
        {
            try
            {
                await userManager.SetAuthenticationTokenAsync(
                    user,
                    info.LoginProvider,
                    "access_token",
                    info.AuthenticationTokens.First(t => t.Name == "access_token").Value);
            }
            catch (DbUpdateConcurrencyException)
            {
                // If we have a concurrent modification exception that means another request updated this token, we can abandon our update and reload the data from the DB
                foreach (EntityEntry entry in dbContext.ChangeTracker.Entries())
                {
                    await entry.ReloadAsync();
                }
            }
        }

        private async Task UpdateUserIfNeededAsync(ApplicationUser user,
            BuildAssetRegistryContext dbContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            GitHubClaimResolver gitHubClaimResolver)
        {
            while (true)
            {
                try
                {
                    if (ShouldUpdateUser(user))
                    {
                        await UpdateUserAsync(user, dbContext, userManager, signInManager, gitHubClaimResolver);
                    }

                    break;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // If we have a concurrent modification exception reload the data from the DB and try again
                    foreach (EntityEntry entry in dbContext.ChangeTracker.Entries())
                    {
                        await entry.ReloadAsync();
                    }
                }
            }
        }

        private bool ShouldUpdateUser(ApplicationUser user)
        {
            // If we haven't updated the user in the last 30 minutes
            return DateTimeOffset.UtcNow - user.LastUpdated > new TimeSpan(0, 30, 0);
        }

        private async Task UpdateUserAsync(ApplicationUser user,
            BuildAssetRegistryContext dbContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            GitHubClaimResolver gitHubClaimResolver)
        {
            using (IDbContextTransaction txn = await dbContext.Database.BeginTransactionAsync())
            {
                string token = await userManager.GetAuthenticationTokenAsync(user, GitHubScheme, "access_token");
                var newClaims = (await gitHubClaimResolver.GetUserInformationClaims(token)).Concat(
                    await gitHubClaimResolver.GetMembershipClaims(token)
                ).Where(AccountController.ShouldAddClaimToUser);
                var currentClaims = (await userManager.GetClaimsAsync(user)).ToList();

                // remove old claims
                await userManager.RemoveClaimsAsync(user, currentClaims);

                // add new claims
                await userManager.AddClaimsAsync(user, newClaims);

                user.LastUpdated = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync();
                txn.Commit();
            }
        }
    }
}
