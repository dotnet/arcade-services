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
            string repoUrl = GetRepository().TrimEnd('/');
            if (repoUrl.Contains("github.com"))
            {
                return $"{repoUrl}/commit/{Commit}";
            }
            else if (repoUrl.Contains("dev.azure.com"))
            {
                return $"{repoUrl}?_a=history&version=GC{Commit}";
            }
            return null;
        }
    }
}
