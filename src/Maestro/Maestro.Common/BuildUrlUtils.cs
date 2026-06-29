// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;

namespace Maestro.Common;

public partial class BuildUrlUtils
{
    public static string ParseOrganizationFromBuildUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        string[] match = AzureDevOpsUrlRegex().Match(url).Value.Split("/");

        if (match != null && match.Length > 1)
        {
            return match[1];
        }

        return string.Empty;
    }

    public static string GetBuildUrl(string organization, string project, int buildId)
    {
        if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project))
        {
            return string.Empty;
        }

        return $"https://dev.azure.com/{organization}/{project}/_build/results?buildId={buildId}";
    }

    public static string GetBuildJobUrl(string organization, string project, int buildId, Guid? jobId)
    {
        return $"{GetBuildUrl(organization, project, buildId)}&view=logs&j={jobId:D}";
    }

    [GeneratedRegex("(dev.azure.com/[a-z]+-?[a-z]+)")]
    private static partial Regex AzureDevOpsUrlRegex();
}
