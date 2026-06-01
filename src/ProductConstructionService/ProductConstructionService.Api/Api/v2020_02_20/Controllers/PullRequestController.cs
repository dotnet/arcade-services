// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Common;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Configuration;
using ProductConstructionService.Api.v2020_02_20.Models;
using Maestro.Services.Common.Cache;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("pull-requests")]
[ApiVersion("2020-02-20")]
public partial class PullRequestController : ControllerBase
{
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
    [SwaggerApiResponse(HttpStatusCode.OK,
        Type = typeof(List<TrackedPullRequest>),
        Description = "The list of currently tracked pull requests by the service")]
    [ValidateModelState]
    public async Task<IActionResult> GetTrackedPullRequests()
    {
        var keyPrefix = nameof(InProgressPullRequest) + "_";
        var cache = _cacheFactory.Create(keyPrefix);

        var prs = new List<TrackedPullRequest>();
        await foreach (var key in cache.GetKeysAsync(keyPrefix + "*"))
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
                if (GitRepoUrlUtils.ParseTypeFromUri(sampleSub.TargetRepository) == GitRepoType.AzureDevOps)
                {
                    try
                    {
                        (repoName, org) = GitRepoUrlUtils.GetRepoNameAndOwner(sampleSub.TargetRepository);
                    }
                    catch
                    {
                        // Repos which do not conform to usual naming will not be handled
                    }
                }
            }

            var updates = subscriptions
                .Select(update => new PullRequestUpdate(
                    GitRepoUrlUtils.TurnApiUrlToWebsite(update.SourceRepository, org, repoName),
                    pr.ContainedSubscriptions.First(s => s.SubscriptionId == update.Id).SubscriptionId,
                    pr.ContainedSubscriptions.First(s => s.SubscriptionId == update.Id).BuildId))
                .ToList();

            var trackedPr = ToTrackedPullRequest(
                pr,
                key.Replace(keyPrefix, null, StringComparison.InvariantCultureIgnoreCase),
                sampleSub);

            prs.Add(trackedPr);
        }

        return Ok(prs.AsQueryable());
    }

    /// <summary>
    /// Retrieves the tracked codeflow pull request associated with the specified subscription identifier.
    /// </summary>
    /// <param name="subscriptionId">The id of the subscription for which to retrieve the tracked pull request.</param>
    /// <returns>An <see cref="IActionResult"/> containing the tracked pull request details if found; otherwise, a <see
    /// cref="NotFoundResult"/> if no pull request is associated with the specified subscription.</returns>
    [HttpGet("tracked/subscription/{subscriptionId}")]
    [SwaggerApiResponse(HttpStatusCode.OK,
    Type = typeof(TrackedPullRequest),
        Description = "Get a PR that is tracked by the service, by its corresponding subscription id")]
    [SwaggerApiResponse(HttpStatusCode.BadRequest, Description = "Wrong attributes provided")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "The corresponding subscription or pull request cannot be found")]
    [ValidateModelState]
    public async Task<IActionResult> GetTrackedPullRequestBySubscriptionId(string subscriptionId)
    {
        if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
        {
            return BadRequest("A valid subscription GUID must be provided.");
        }

        var subscription = await _context.Subscriptions
            .Include(s => s.Channel)
            .SingleOrDefaultAsync(s => s.Id == subscriptionGuid);

        if (subscription == null)
        {
            return NotFound();
        }

        string prRedisKey = PullRequestUpdaterId.CreateUpdaterId(subscription).Id;

        var cache = _cacheFactory.Create<InProgressPullRequest>(prRedisKey);
        var pr = await cache.TryGetStateAsync();

        if (pr == null)
        {
            return NotFound();
        }

        var trackedPullRequest = ToTrackedPullRequest(
            pr,
            subscription.Id.ToString(),
            subscription);

        return Ok(trackedPullRequest);
    }

    [HttpDelete("tracked/{id}")]
    [Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(void), Description = "The pull request was successfully untracked")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Type = typeof(void), Description = "The pull request was not found in the list of tracked pull requests")]
    [ValidateModelState]
    public async Task<IActionResult> UntrackPullRequest(string id)
    {
        var cache = _cacheFactory.Create<InProgressPullRequest>($"{nameof(InProgressPullRequest)}_{id}", includeTypeInKey: false);
        return await cache.TryDeleteAsync() == null ? NotFound() : Ok();
    }

    internal static TrackedPullRequest ToTrackedPullRequest(
        InProgressPullRequest pr,
        string subscriptionId,
        Maestro.Data.Models.Subscription? subscription)
    {
        string? org = null;
        string? repoName = null;
        // Only extract org/repoName for Azure DevOps PRs
        if (subscription != null && GitRepoUrlUtils.IsAzdoApiPrUrl(pr.Url))
        {
            try
            {
                (repoName, org) = GitRepoUrlUtils.GetRepoNameAndOwner(subscription.TargetRepository);
            }
            catch
            {
                // Repos which do not conform to usual naming will not be handled
            }
        }

        return new TrackedPullRequest(
            subscriptionId,
            GitRepoUrlUtils.TurnApiUrlToWebsite(pr.Url, org, repoName),
            subscription?.Channel != null ? new Channel(subscription.Channel) : null,
            subscription?.TargetBranch,
            subscription?.SourceEnabled ?? false,
            pr.LastUpdate,
            pr.LastCheck,
            pr.NextCheck,
            [.. pr.ContainedSubscriptions
            .Select(csu => new PullRequestUpdate(
                csu.SourceRepo,
                csu.SubscriptionId,
                csu.BuildId))],
            pr.HeadBranch,
            pr.NextBuildsToProcess,
            pr.CreationDate);
    }

    public record TrackedPullRequest(
        string Id,
        string Url,
        Channel? Channel,
        string? TargetBranch,
        bool SourceEnabled,
        DateTime LastUpdate,
        DateTime LastCheck,
        DateTime? NextCheck,
        List<PullRequestUpdate> Updates,
        string HeadBranch,
        Dictionary<Guid, int> NextBuildsToApply,
        DateTime CreationDate);

    public record PullRequestUpdate(
        string SourceRepository,
        Guid SubscriptionId,
        int BuildId);
}
