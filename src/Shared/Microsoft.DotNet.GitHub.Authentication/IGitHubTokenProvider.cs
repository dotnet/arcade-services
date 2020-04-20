// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public interface IGitHubTokenProvider
    {
        Task<string> GetTokenForInstallationAsync(long installationId);
        Task<string> GetTokenForRepository(string repositoryUrl);
    }

    public static class GitHubTokenProviderExtensions
    {
        public static Task<string> GetTokenForRepository(this IGitHubTokenProvider provider, string organization, string repository)
        {
            return provider.GetTokenForRepository(GitHubHelper.GetRepositoryUrl(organization, repository));
        }
    }
}
