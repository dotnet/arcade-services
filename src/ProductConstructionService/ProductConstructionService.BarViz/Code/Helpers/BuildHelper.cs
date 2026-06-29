// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Code.Helpers;


public static class BuildHelper
{
    public static string GetBuildUrl(this Build build)
    {
        return $"https://dev.azure.com/dnceng/internal/_build/results?view=results&buildId={build.AzureDevOpsBuildId}";
    }

    public static string GetBuildStalenessText(this Build build)
    {
        int stalenes = build.Staleness;
        if (stalenes == 0)
        {
            return "latest";
        }
        else
        {
            return $"{stalenes} behind";
        }
    }

    public static int GetBuildAgeDays(this Build build)
    {
        return (int)Math.Round(DateTimeOffset.UtcNow.Subtract(build.DateProduced).TotalDays);
    }

    public static string GetRepoName(this Build build)
    {
        string repoUrl = build.GetRepoUrl();
        if (repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            repoUrl = repoUrl.Substring(8);
        }
        return repoUrl;
    }

    public static string GetRepoUrl(this Build build)
    {
        return build.GitHubRepository ?? build.AzureDevOpsRepository;
    }

    public static string? GetLinkToBuildDetails(this Build build, int channelId)
    {
        return GetLinkToBuildDetails(build.GetRepoUrl(), channelId, build.Id);
    }

    public static string? GetLinkToBuildDetails(string repoUrl, int channelId, int buildId)
    {
        string? repoSlug = RepoUrlConverter.RepoUrlToSlug(repoUrl);
        return repoSlug != null ? $"/channel/{channelId}/{repoSlug}/build/{buildId}" : null;
    }

    public static string? GetCommitLink(this Build build)
    {
        return GetCommitUri(build.GetRepoUrl(), build.Commit);
    }

    public static string? GetCommitUri(string repoUrl, string commitSha)
    {
        string trimmedUrl = repoUrl.TrimEnd('/');
        if (trimmedUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmedUrl}/commit/{commitSha}";
        }
        else if (trimmedUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmedUrl}?_a=history&version=GC{commitSha}";
        }
        return null;
    }

    public static string GetShortRepository(this Build build)
    {
        var repository = build.GetRepoUrl();

        if (repository.StartsWith("https://dev.azure.com"))
        {
            return repository.Substring(repository.LastIndexOf('/'));
        }
        if (repository.StartsWith("https://github.com"))
        {
            // skip https://github.com/
            return repository.Substring(19);
        }

        return string.Empty;
    }
}
