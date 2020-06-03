// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Octokit;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public class GitHubApplicationClientFactory : IGitHubApplicationClientFactory
    {
        private readonly IGitHubClientFactory _clientFactory;
        private readonly IGitHubTokenProvider _tokenProvider;

        public GitHubApplicationClientFactory(IGitHubClientFactory clientFactory, IGitHubTokenProvider tokenProvider)
        {
            _clientFactory = clientFactory;
            _tokenProvider = tokenProvider;
        }

        public async Task<IGitHubClient> CreateGitHubClientAsync(string owner, string repo)
        {
            return _clientFactory.CreateGitHubClient(await _tokenProvider.GetTokenForRepository(owner, repo));
        }
    }
}
