// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public static class GitRepoUrlParser
{
    public static GitRepoType ParseTypeFromUri(string pathOrUri)
    {
        if (!Uri.TryCreate(pathOrUri, UriKind.RelativeOrAbsolute, out Uri? parsedUri))
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

    /// <summary>
    /// Sorts so that we go Local -> GitHub -> AzDO.
    /// (in other words local, public, internal)
    /// </summary>
    public static int OrderByLocalPublicOther(GitRepoType first, GitRepoType second)
    {
        if (first == second)
        {
            return 0;
        }

        if (first == GitRepoType.Local)
        {
            return -1;
        }

        if (second == GitRepoType.Local)
        {
            return 1;
        }

        if (first == GitRepoType.GitHub)
        {
            return -1;
        }

        return 1;
    }

    public static (string RepoName, string Org) GetRepoNameAndOwner(string uri)
    {
        var repoType = ParseTypeFromUri(uri);

        if (repoType == GitRepoType.AzureDevOps)
        {
            string[] repoParts = uri.Substring(uri.LastIndexOf('/') + 1).Split('-', 2);

            if (repoParts.Length != 2)
            {
                throw new Exception($"Invalid URI in source manifest. Repo '{uri}' does not end with the expected <GH organization>-<GH repo> format.");
            }

            string org = repoParts[0];
            string repo = repoParts[1];

            // The internal Nuget.Client repo has suffix which needs to be accounted for.
            const string trustedSuffix = "-Trusted";
            if (uri.EndsWith(trustedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                repo = repo.Substring(0, repo.Length - trustedSuffix.Length);
            }

            return new(repo, org);
        }

        if (repoType == GitRepoType.GitHub)
        {
            string[] repoParts = uri.Substring(Constants.GitHubUrlPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (repoParts.Length != 2)
            {
                throw new Exception($"Invalid URI in source manifest. Repo '{uri}' does not end with the expected <GH organization>/<GH repo> format.");
            }

            return new(repoParts[1], repoParts[0]);
        }

        throw new Exception("Unsupported format of repository url " + uri);
    }

    public static string ConvertInternalUriToPublic(string uri)
    {
        if (!uri.StartsWith(Constants.AzureDevOpsUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var (repo, org) = GetRepoNameAndOwner(uri);
        return $"{Constants.GitHubUrlPrefix}{org}/{repo}";
    }
}
