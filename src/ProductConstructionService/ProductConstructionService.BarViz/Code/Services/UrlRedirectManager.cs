// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.RegularExpressions;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Code.Services;


public class UrlRedirectManager
{
    private static readonly Regex _repoUrlGitHubRegex = new(@"^\/(?<channelId>[^\/]+)\/(?<repoUrl>[^\/]+)\/(?<buildId>[^\/]+)\/graph$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string? ApplyLocatinoRedirects(string location)
    {
        Uri uri = new Uri(location);

        var path = uri.PathAndQuery;

        Match m = _repoUrlGitHubRegex.Match(path);
        if (m.Success)
        {
            string repoUrl = WebUtility.UrlDecode(m.Groups["repoUrl"].Value);
            string? repoSlug = RepoUrlConverter.RepoUrlToSlug(repoUrl);
            if (repoSlug != null)
            {
                string channelId = m.Groups["channelId"].Value;
                string buildId = m.Groups["buildId"].Value;
                return $"/channel/{channelId}/{repoSlug}/build/{buildId}";
            }
        }
        return null;
    }
}
