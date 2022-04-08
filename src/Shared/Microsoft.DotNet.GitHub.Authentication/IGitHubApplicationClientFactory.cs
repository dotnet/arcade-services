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

        /// <param name="name">When using <see href="https://docs.microsoft.com/en-us/dotnet/core/extensions/options#named-options-support-using-iconfigurenamedoptions">named options</see>, 
        /// the name of the <see cref="GitHubTokenProviderOptions"/> to use when creating the client.</param>
        IGitHubClient CreateGitHubAppClient(string name);
    }
}
