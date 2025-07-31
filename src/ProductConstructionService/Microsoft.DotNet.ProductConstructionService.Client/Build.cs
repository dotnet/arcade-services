// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;

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

        public string GetBuildDefinitionLink()
        {
            if (!string.IsNullOrEmpty(AzureDevOpsAccount) &&
                !string.IsNullOrEmpty(AzureDevOpsProject) &&
                AzureDevOpsBuildDefinitionId.HasValue)
            {
                return $"https://dev.azure.com/{AzureDevOpsAccount}/{AzureDevOpsProject}/_build?definitionId={AzureDevOpsBuildDefinitionId.Value}";
            }
            return null;
        }

        public string GetCommitLink() => GitRepoUrlUtils.GetCommitUri(GetRepository(), Commit);

        public string GetBranchLink() => GitRepoUrlUtils.GetRepoAtBranchUri(GetRepository(), GetBranch());
    }
}
