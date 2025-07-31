// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public enum GitRepoType
    {
        GitHub,
        AzureDevOps,
        Local,
        None
    }

    public partial class Build
    {
        private static GitRepoType ParseTypeFromUri(string pathOrUri)
        {
            if (!Uri.TryCreate(pathOrUri, UriKind.RelativeOrAbsolute, out Uri parsedUri))
            {
                return GitRepoType.None;
            }

            // Local relative paths are still a local git URI
            if (!parsedUri.IsAbsoluteUri)
            {
                return pathOrUri.IndexOfAny(Path.GetInvalidPathChars()) == -1
                    ? GitRepoType.Local
                    : GitRepoType.None;
            }

            return parsedUri switch
            {
                { IsFile: true } => GitRepoType.Local,
                { Host: "github.com" } => GitRepoType.GitHub,
                { Host: var host } when host is "dev.azure.com" => GitRepoType.AzureDevOps,
                { Host: var host } when host.EndsWith("visualstudio.com") => GitRepoType.AzureDevOps,
                _ => GitRepoType.None,
            };
        }
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

            return ParseTypeFromUri(repoUrl) switch
            {
                GitRepoType.AzureDevOps => $"{repoUrl}?_a=history&version=GC{Commit}",
                GitRepoType.GitHub => $"{repoUrl}/commit/{Commit}",
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

            return ParseTypeFromUri(repoUrl) switch
            {
                GitRepoType.AzureDevOps => $"{repoUrl}?version=GB{branch}",
                GitRepoType.GitHub => $"{repoUrl}/tree/{branch}",
                _ => throw new InvalidOperationException($"Failed to construct a branch link for build with id {Id} with repo url {repoUrl}.")
            };
        }
    }
}
