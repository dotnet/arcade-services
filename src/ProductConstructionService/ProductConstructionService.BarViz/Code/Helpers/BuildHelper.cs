// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Client.Models;

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
        string? repoSlug = RepoUrlConverter.RepoUrlToSlug(build.GetRepoUrl());
        if (repoSlug != null)
        {
            return $"/channel/{channelId}/{repoSlug}/build/{build.Id}";
        }
        return null;
    }

    public static string? GetCommitLink(this Build build)
    {
        string repoUrl = build.GetRepoUrl().TrimEnd('/');
        if (repoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{repoUrl}/commit/{build.Commit}";
        }
        else if (repoUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"{repoUrl}?_a=history&version=GC{build.Commit}";
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
