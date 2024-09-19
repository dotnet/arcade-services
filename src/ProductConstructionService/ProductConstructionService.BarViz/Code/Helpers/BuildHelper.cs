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

    public static string GetBuildStaleness(this Build build)
    {
        int days = build.Staleness;
        if (days == 0)
        {
            return "same day";
        }
        else if (days > 1)
        {
            return $"{Math.Abs(days)} ahead";
        }
        else
        {
            return $"{Math.Abs(days)} behind";
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
}
