// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

        public string GetCommitLink()
        {
            var repository = GetRepository();
            if (string.IsNullOrEmpty(repository))
            {
                throw new InvalidOperationException($"Cannot get commit link of build with id {Id} because it does not have a repo URL.");
            }

            return GitRepoUrlUtils.GetCommitUri(repository, Commit);
        }

        public string GetBranchLink()
        {
            var repository = GetRepository();
            if (string.IsNullOrEmpty(repository))
            {
                throw new InvalidOperationException($"Cannot get branch link of build with id {Id} because it does not have a repo URL.");
            }

            var branch = GetBranch();
            if (string.IsNullOrEmpty(branch))
            {
                throw new InvalidOperationException($"Cannot get branch link of build with id {Id} because it does not have a branch name.");
            }

            return GitRepoUrlUtils.GetRepoAtBranchUri(repository, branch);
        }
    }
}
