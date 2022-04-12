// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Octokit;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public interface IGitHubApplicationClientFactory
    { 
        Task<IGitHubClient> CreateGitHubClientAsync(string owner, string repo);
        IGitHubClient CreateGitHubAppClient();

        /// <summary>
        /// Creates a GitHub App client configured the configuration that corresponds to the logical name specified by <paramref name="name"/>.
        /// </summary>
        IGitHubClient CreateGitHubAppClient(string name);
    }
}
