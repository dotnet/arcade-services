// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Options;
using Octokit;

namespace DotNet.Status.Web.Controllers
{
    public class GitHubClientFactory
    {
        private readonly IOptions<GitHubClientOptions> _githubClientOptions;
        private readonly IGitHubTokenProvider _tokenProvider;

        public GitHubClientFactory(IOptions<GitHubClientOptions> githubClientOptions, IGitHubTokenProvider tokenProvider)
        {
            _githubClientOptions = githubClientOptions;
            _tokenProvider = tokenProvider;
        }

        public virtual async Task<IGitHubClient> CreateGitHubClientAsync(string owner, string repo)
        {
            return new GitHubClient(_githubClientOptions.Value.ProductHeader)
            {
                Credentials = new Credentials(await _tokenProvider.GetTokenForRepository(owner, repo))
            };
        }
    }
}
