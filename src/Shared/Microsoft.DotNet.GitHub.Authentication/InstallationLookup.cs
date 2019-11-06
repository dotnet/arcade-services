// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;

namespace Microsoft.Dotnet.GitHub.Authentication
{
    public interface IInstallationLookup
    {
        Task<long> GetInstallationId(string repositoryUrl);
    }

    public static class InstallationLookup
    {
        public static Task<long> GetInstallationId(this IInstallationLookup lookup, string organization, string repository)
        {
            return lookup.GetInstallationId(GitHubHelper.GetRepositoryUrl(organization, repository));
        }
    }
}
