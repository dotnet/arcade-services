// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

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
            string repoUrl = GetRepository();
            if (repoUrl == null)
            {
                throw new InvalidOperationException($"Cannot get commit link of build with id {Id} because it does not have a repo URL.");
            }
            
            if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out Uri parsedUri))
            {
                throw new InvalidOperationException($"Failed to construct a commit link for build with id {Id} with repo url {repoUrl}.");
            }

            return parsedUri.Host switch
            {
                "github.com" => $"{repoUrl.TrimEnd('/')}/commit/{Commit}",
                "dev.azure.com" => $"{repoUrl.TrimEnd('/')}?_a=history&version=GC{Commit}",
                var host when host.EndsWith("visualstudio.com") => $"{repoUrl.TrimEnd('/')}?_a=history&version=GC{Commit}",
                _ => throw new InvalidOperationException($"Failed to construct a commit link for build with id {Id} with repo url {repoUrl}.")
            };
        }

        public string GetBranchLink()
        {
            string repoUrl = GetRepository();
            if (repoUrl == null)
            {
                throw new InvalidOperationException($"Cannot get branch link of build with id {Id} because it does not have a repo URL.");
            }
            
            string branch = GetBranch();
            if (string.IsNullOrEmpty(branch))
            {
                throw new InvalidOperationException($"Cannot get branch link of build with id {Id} because it does not have a branch name.");
            }
            
            if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out Uri parsedUri))
            {
                throw new InvalidOperationException($"Failed to construct a branch link for build with id {Id} with repo url {repoUrl}.");
            }

            return parsedUri.Host switch
            {
                "github.com" => $"{repoUrl.TrimEnd('/')}/tree/{branch}",
                "dev.azure.com" => $"{repoUrl.TrimEnd('/')}?version=GB{branch}",
                var host when host.EndsWith("visualstudio.com") => $"{repoUrl.TrimEnd('/')}?version=GB{branch}",
                _ => throw new InvalidOperationException($"Failed to construct a branch link for build with id {Id} with repo url {repoUrl}.")
            };
        }
    }
}
