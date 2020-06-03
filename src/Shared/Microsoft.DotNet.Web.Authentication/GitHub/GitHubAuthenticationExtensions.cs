// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Octokit;

namespace Microsoft.DotNet.Web.Authentication.GitHub
{
    public static class GitHubAuthenticationExtensions
    {
        public static AuthenticationBuilder AddGitHubOAuth(this AuthenticationBuilder auth, IConfigurationSection section, string scheme)
        {
            auth.Services.Configure<GitHubAuthenticationOptions>(scheme, section.Bind);
            auth.Services.AddSingleton<GitHubClaimResolver>();
            auth.Services.TryAddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            return auth.AddOAuth<GitHubAuthenticationOptions, GitHubAuthenticationHandler>(
                scheme,
                options =>
                {

                    options.Events = new OAuthEvents
                    {
                        OnCreatingTicket = async context =>
                        {
                            GitHubClaimResolver resolver = context.HttpContext.RequestServices.GetRequiredService<GitHubClaimResolver>();
                            IEnumerable<Claim> memberClaims = await resolver.GetMembershipClaims(
                                context.AccessToken,
                                context.HttpContext.RequestAborted
                            );
                            context.Identity.AddClaims(memberClaims);
                        },
                        OnRemoteFailure = context =>
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILogger<GitHubAuthenticationHandler>>();
                            logger.LogError(context.Failure, "Github authentication failed.");
                            var res = context.HttpContext.Response;
                            res.StatusCode = (int) HttpStatusCode.Forbidden;
                            context.HandleResponse();
                            context.HttpContext.Items["ErrorMessage"] = "Authentication failed.";
                            return Task.CompletedTask;
                        },
                    };
                });
        }
    }
}
