// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;

namespace ProductConstructionService.BarViz.Code.Helpers;


public static class RepoUrlConverter
{
    public static string? SlugToRepoUrl(string? slug)
    {
        if (slug == null)
        {
            return null;
        }
        var parts = slug.Split(":");
        if (parts.Length < 3)
        {
            return null;
        }
        var repoType = parts[0].ToLowerInvariant();
        if (parts.Length == 4 && repoType is "ado" or "azdo" or "azuredevops")
        {
            return $"https://dev.azure.com/{parts[1]}/{parts[2]}/_git/{parts[3]}";
        }
        if (parts.Length == 3 && repoType is "github")
        {
            return $"https://github.com/{parts[1]}/{parts[2]}";
        }
        return null;
    }

    private static readonly Regex _repoUrlGitHubRegex = new(@"^https:\/\/github\.com\/(?<org>[^\/]+)\/(?<repoPath>.*)$");
    private static readonly Regex _repoUrlAzureDevOpsRegex = new(@"^https:\/\/dev\.azure\.com\/(?<org>[^\/]+)\/(?<project>[^\/]+)\/_git\/(?<repoPath>.*)$");

    public static string? RepoUrlToSlug(string? repoUrl)
    {
        if (repoUrl == null)
        {
            return null;
        }

        var mGitHub = _repoUrlGitHubRegex.Match(repoUrl);
        if (mGitHub.Success)
        {
            string org = mGitHub.Groups["org"].Value;
            string repoPath = mGitHub.Groups["repoPath"].Value;
            return $"github:{org}:{repoPath}";
        }

        var mAzdo = _repoUrlAzureDevOpsRegex.Match(repoUrl);
        if (mAzdo.Success)
        {
            string org = mAzdo.Groups["org"].Value;
            string project = mAzdo.Groups["project"].Value;
            string repoPath = mAzdo.Groups["repoPath"].Value;
            return $"azdo:{org}:{project}:{repoPath}";
        }

        return null;
    }
}
