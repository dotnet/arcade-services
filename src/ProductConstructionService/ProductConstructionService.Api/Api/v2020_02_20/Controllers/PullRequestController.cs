// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.RegularExpressions;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("pull-requests")]
[ApiVersion("2020-02-20")]
public partial class PullRequestController : ControllerBase
{
    [GeneratedRegex(@"https://api.github.com/repos/(?<org>[^/]+)/(?<repo>[^/]+)/pulls/(?<id>[0-9]+)/?")]
    private static partial Regex GitHubApiPrUrlRegex();

    [GeneratedRegex(@"https://dev.azure.com/(?<org>[^/]+)/(?<project>[^/]+)/_apis/git/repositories/(?<repo>[^/]+)/pullRequests/(?<id>[0-9]+)/?")]
    private static partial Regex AzdoApiPrUrlRegex();

    private static readonly Dictionary<string, string> WellKnownIds = new()
    {
        ["7ea9116e-9fac-403d-b258-b31fcf1bb293"] = "internal", // https://dev.azure.com/dnceng/internal
        ["0bdbc590-a062-4c3f-b0f6-9383f67865ee"] = "DevDiv", // https://dev.azure.com/devdiv/DevDiv
        ["55e8140e-57ac-4e5f-8f9c-c7c15b51929d"] = "ProjectReunion", // https://dev.azure.com/microsoft/ProjectReunion
    };

    private static string ResolveWellKnownIds(string str)
    {
        foreach (var pair in WellKnownIds)
        {
            str = str.Replace(pair.Key, pair.Value);
        }

        return str;
    }

    private readonly IRedisCacheFactory _cacheFactory;
    private readonly BuildAssetRegistryContext _context;

    public PullRequestController(
        IRedisCacheFactory cacheFactory,
        BuildAssetRegistryContext context)
    {
        _cacheFactory = cacheFactory;
        _context = context;
    }

    [HttpGet("tracked")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<TrackedPullRequest>), Description = "The list of currently tracked pull requests by the service")]
    [ValidateModelState]
    public async Task<IActionResult> GetTrackedPullRequests()
    {
        var cache = _cacheFactory.Create(nameof(InProgressPullRequest) + "_");

        var prs = new List<TrackedPullRequest>();
        await foreach (var key in cache.GetKeysAsync(nameof(InProgressPullRequest) + "_*"))
        {
            var pr = await _cacheFactory
                .Create<InProgressPullRequest>(key, includeTypeInKey: false)
                .TryGetStateAsync();

            if (pr == null)
            {
                continue;
            }

            var subscriptionIds = pr.ContainedSubscriptions.Select(s => s.SubscriptionId).ToList();
            var subscriptions = await _context.Subscriptions
                .Where(s => subscriptionIds.Contains(s.Id))
                .Include(s => s.Channel)
                .ToListAsync();

            var sampleSub = subscriptions.FirstOrDefault();

            string? org = null;
            string? repoName = null;
            if (sampleSub != null)
            {
                if (GitRepoUrlParser.ParseTypeFromUri(sampleSub.TargetRepository) == GitRepoType.AzureDevOps)
                {
                    try
                    {
                        (repoName, org) = GitRepoUrlParser.GetRepoNameAndOwner(sampleSub.TargetRepository);
                    }
                    catch
                    {
                        // Repos which do not conform to usual naming will not be handled
                    }
                }
            }

            var updates = subscriptions
                .Select(update => new PullRequestUpdate(
                    TurnApiUrlToWebsite(update.SourceRepository, org, repoName),
                    pr.ContainedSubscriptions.First(s => s.SubscriptionId == update.Id).BuildId))
                .ToList();

            prs.Add(new TrackedPullRequest(
                TurnApiUrlToWebsite(pr.Url, org, repoName),
                sampleSub?.Channel?.Name,
                sampleSub?.TargetBranch,
                updates));
        }

        return Ok(prs.AsQueryable());
    }

    private static string TurnApiUrlToWebsite(string url, string? orgName, string? repoName)
    {
        var match = GitHubApiPrUrlRegex().Match(url);
        if (match.Success)
        {
            return $"https://github.com/{match.Groups["org"]}/{match.Groups["repo"]}/pull/{match.Groups["id"]}";
        }

        match = AzdoApiPrUrlRegex().Match(url);
        if (match.Success)
        {
            // If we have the repo name, use it to replace the repo GUID in the URL
            if (repoName != null)
            {
                WellKnownIds[match.Groups["repo"].Value] = orgName + "-" +repoName;
            }

            var org = ResolveWellKnownIds(match.Groups["org"].Value);
            var project = ResolveWellKnownIds(match.Groups["project"].Value);
            var repo = ResolveWellKnownIds(match.Groups["repo"].Value);
            return $"https://dev.azure.com/{org}/{project}/_git/{repo}/pullrequest/{match.Groups["id"]}";
        }

        return url;
    }

    private record TrackedPullRequest(
        string Url,
        string? Channel,
        string? TargetBranch,
        List<PullRequestUpdate> Updates);

    private record PullRequestUpdate(
        string SourceRepository,
        int BuildId);
}
