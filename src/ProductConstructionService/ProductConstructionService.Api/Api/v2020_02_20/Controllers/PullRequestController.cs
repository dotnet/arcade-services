// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.RegularExpressions;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
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

            var updates = subscriptions
                .Select(update => new PullRequestUpdate(
                    update.SourceRepository,
                    pr.ContainedSubscriptions.First(s => s.SubscriptionId == update.Id).BuildId))
                .ToList();

            var sampleSub = subscriptions.FirstOrDefault();

            prs.Add(new TrackedPullRequest(
                TurnApiUrlToWebsite(pr.Url),
                sampleSub?.Channel?.Name ?? "N/A",
                sampleSub?.TargetBranch ?? "N/A",
                updates));
        }

        return Ok(prs.AsQueryable());
    }

    private static string TurnApiUrlToWebsite(string url)
    {
        var match = GitHubApiPrUrlRegex().Match(url);
        if (match.Success)
        {
            return $"https://github.com/{match.Groups["org"]}/{match.Groups["repo"]}/pull/{match.Groups["id"]}";
        }

        match = AzdoApiPrUrlRegex().Match(url);
        if (match.Success)
        {
            return $"https://dev.azure.com/{match.Groups["org"]}/{match.Groups["project"]}/_git/{match.Groups["repo"]}/pullrequest/{match.Groups["id"]}";
        }

        return url;
    }

    private record TrackedPullRequest(
        string Url,
        string Channel,
        string TargetBranch,
        List<PullRequestUpdate> Updates);

    private record PullRequestUpdate(
        string SourceRepository,
        int BuildId);
}
