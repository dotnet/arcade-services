// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Build
    {
        public string GetRepository() => GitHubRepository ?? AzureDevOpsRepository;

        public string GetBranch() => GitHubBranch ?? AzureDevOpsBranch;

        public string GetBuildLink()
        {
            if (!string.IsNullOrEmpty(AzureDevOpsAccount) &&
                !string.IsNullOrEmpty(AzureDevOpsProject) &&
                AzureDevOpsBuildId.HasValue)
            {
                return $"https://dev.azure.com/{AzureDevOpsAccount}/{AzureDevOpsProject}/_build/results?buildId={AzureDevOpsBuildId.Value}";
            }
            return null;
        }

        public string GetCommitLink()
        {
            string repoUrl = GetRepository();
            if (repoUrl == null)
            {
                throw new InvalidOperationException($"Cannot get commit link of build with id {Id} because it does not have a repo URL.");
            }
            string baseUrl = repoUrl.TrimEnd('/');
            if (baseUrl.Contains("github.com"))
            {
                return $"{baseUrl}/commit/{Commit}";
            }
            else if (baseUrl.Contains("dev.azure.com"))
            {
                return $"{baseUrl}?_a=history&version=GC{Commit}";
            }
            throw new InvalidOperationException($"Failed to construct a commit link for build with id {Id} with repo url {repoUrl}.");
        }
    }
}
