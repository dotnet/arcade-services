// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Microsoft.AspNetCore.Authentication.OAuth;
using Octokit;

namespace Microsoft.DotNet.Web.Authentication.GitHub
{
    public class GitHubAuthenticationOptions : OAuthOptions
    {
        public GitHubAuthenticationOptions()
        {
            AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            TokenEndpoint = "https://github.com/login/oauth/access_token";
            UserInformationEndpoint = "https://api.github.com/user";
        }
        
        public string OrganizationEndpoint { get; set; } = "https://api.github.com/user/orgs";
        public string TeamsEndpoint { get; set; } = "https://api.github.com/user/teams";
    }
}
