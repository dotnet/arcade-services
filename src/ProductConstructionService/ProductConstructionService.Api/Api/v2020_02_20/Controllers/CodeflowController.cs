// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Common.Cache;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2020_02_20.Models;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to read codeflow statuses
/// </summary>
[Route("codeflows")]
[ApiVersion("2020-02-20")]
public class CodeflowController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IRedisCacheFactory _cacheFactory;

    public CodeflowController(
        BuildAssetRegistryContext context,
        IRedisCacheFactory cacheFactory)
    {
        _context = context;
        _cacheFactory = cacheFactory;
    }

    /// <summary>
    ///   Gets codeflow statuses for a repository and branch
    /// </summary>
    /// <param name="repositoryUrl">The VMR repository URL</param>
    /// <param name="branch">The VMR branch name</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<CodeflowStatus>), Description = "The list of codeflow statuses")]
    [ValidateModelState]
    public async Task<IActionResult> GetCodeflowStatuses(
        [FromQuery] string repositoryUrl,
        [FromQuery] string branch)
    {
        if (string.IsNullOrEmpty(repositoryUrl))
        {
            return BadRequest("Repository URL is required");
        }

        if (string.IsNullOrEmpty(branch))
        {
            return BadRequest("Branch is required");
        }

        var forwardFlowSubscriptions = await GetForwardFlowSubscriptionsAsync(repositoryUrl, branch);
        var backflowSubscriptions = await GetBackflowSubscriptionsAsync(repositoryUrl, branch);

        var subscriptions = forwardFlowSubscriptions.Concat(backflowSubscriptions).ToList();

        Dictionary<Guid, InProgressPullRequest> activePrsPerSubscriptionId = await GetInProgressPullRequestsAsync(subscriptions);

        Dictionary<Guid, BuildStaleness> buildStalenessPerSubscriptionId = await CalculateBuildStalenessPerSubscription(subscriptions);

        var codeflowStatuses = BuildCodeflowStatuses(
            forwardFlowSubscriptions,
            backflowSubscriptions,
            activePrsPerSubscriptionId,
            buildStalenessPerSubscriptionId);

        return Ok(codeflowStatuses);
    }

    private async Task<List<Maestro.Data.Models.Subscription>> GetForwardFlowSubscriptionsAsync(
        string repositoryUrl,
        string branch)
    {
        return await _context.Subscriptions
            .Include(s => s.Channel)
            .Include(s => s.LastAppliedBuild)
            .Where(s => s.TargetRepository == repositoryUrl
                     && s.TargetBranch == branch
                     && s.SourceEnabled)
            .ToListAsync();
    }

    private async Task<List<Maestro.Data.Models.Subscription>> GetBackflowSubscriptionsAsync(
        string repositoryUrl,
        string branch)
    {
        var defaultChannels = await _context.DefaultChannels
            .Include(dc => dc.Channel)
            .Where(dc => dc.Repository == repositoryUrl && dc.Branch == branch)
            .ToListAsync();

        var channelIds = defaultChannels.Select(dc => dc.ChannelId).ToList();

        return await _context.Subscriptions
            .Include(s => s.Channel)
            .Include(s => s.LastAppliedBuild)
            .Where(s => s.SourceRepository == repositoryUrl
                     && channelIds.Contains(s.ChannelId)
                     && s.SourceEnabled)
            .ToListAsync();
    }

    private async Task<Dictionary<Guid, InProgressPullRequest>> GetInProgressPullRequestsAsync(
        List<Maestro.Data.Models.Subscription> subscriptions)
    {
        var keys = subscriptions
            .Select(s => $"{nameof(InProgressPullRequest)}_{s.Id}")
            .ToList();

        var cache = _cacheFactory.Create<InProgressPullRequest>(string.Empty, includeTypeInKey: false);
        var batchResults = await cache.TryGetStateBatchAsync(keys);

        var result = new Dictionary<Guid, InProgressPullRequest>();
        foreach (var subscription in subscriptions)
        {
            var prKey = $"{nameof(InProgressPullRequest)}_{subscription.Id}";
            if (batchResults.TryGetValue(prKey, out var pr) && pr != null)
            {
                result[subscription.Id] = pr;
            }
        }

        return result;
    }


    private async Task<Dictionary<Guid, BuildStaleness>> CalculateBuildStalenessPerSubscription(
        List<Maestro.Data.Models.Subscription> subscriptions)
    {
        var relevantSubscriptions = subscriptions
            .Where(s => s.LastAppliedBuildId != null && s.LastAppliedBuild != null)
            .ToList();

        if (relevantSubscriptions.Count == 0)
        {
            return [];
        }

        // Group subscriptions by (ChannelId, SourceRepository, LastAppliedBuildId) so subscriptions
        // with the same parameters share a single SQL query. Each query uses BuildId > cutoff to
        // leverage the BuildChannels PK for range seeks, then SQL aggregates COUNT/MAX in-place.
        var subscriptionGroups = relevantSubscriptions
            .GroupBy(s => (s.ChannelId, s.SourceRepository, s.LastAppliedBuildId!.Value, s.LastAppliedBuild!.DateProduced));

        var result = new Dictionary<Guid, BuildStaleness>();
        foreach (var group in subscriptionGroups)
        {
            var (channelId, sourceRepo, buildIdCutoff, dateProducedCutoff) = group.Key;

            var staleness = await _context.BuildChannels
                .Where(bc => bc.ChannelId == channelId && bc.BuildId > buildIdCutoff)
                .Join(_context.Builds,
                    bc => bc.BuildId,
                    b => b.Id,
                    (bc, b) => new { b.GitHubRepository, b.AzureDevOpsRepository, b.DateProduced })
                .Where(x =>
                    x.DateProduced > dateProducedCutoff
                    && (x.GitHubRepository == sourceRepo || x.AzureDevOpsRepository == sourceRepo))
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Newest = g.Max(x => x.DateProduced) })
                .FirstOrDefaultAsync();

            var buildStaleness = staleness != null
                ? new BuildStaleness(staleness.Count, staleness.Newest)
                : new BuildStaleness(0, null);

            foreach (var subscription in group)
            {
                result[subscription.Id] = buildStaleness;
            }
        }

        return result;
    }

    private record BuildStaleness(int NewerBuildsCount, DateTimeOffset? NewestBuildDate);

    #region Helpers

    private static List<CodeflowStatus> BuildCodeflowStatuses(
        List<Maestro.Data.Models.Subscription> forwardFlowSubscriptions,
        List<Maestro.Data.Models.Subscription> backflowSubscriptions,
        Dictionary<Guid, InProgressPullRequest> activePrsPerSubscriptionId,
        Dictionary<Guid, BuildStaleness> buildStalenessPerSubscriptionId)
    {
        List<string> mappings = [.. forwardFlowSubscriptions.Concat(backflowSubscriptions)
            .Select(s => !string.IsNullOrEmpty(s.TargetDirectory) ? s.TargetDirectory : s.SourceDirectory)
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct()];

        List<CodeflowStatus> statuses = [];

        foreach (var mapping in mappings)
        {
            var forwardFlowSubscription = forwardFlowSubscriptions.FirstOrDefault(s => s.TargetDirectory == mapping);
            var backflowSubscription = backflowSubscriptions.FirstOrDefault(s => s.SourceDirectory == mapping);

            var forwardFlowStatus = CreateSubscriptionStatus(forwardFlowSubscription, activePrsPerSubscriptionId, buildStalenessPerSubscriptionId);
            var backflowStatus = CreateSubscriptionStatus(backflowSubscription, activePrsPerSubscriptionId, buildStalenessPerSubscriptionId);

            var repoUrl = forwardFlowSubscription?.SourceRepository ?? backflowSubscription?.TargetRepository;
            var repoBranch = backflowSubscription?.TargetBranch;

            statuses.Add(new CodeflowStatus
            {
                MappingName = mapping,
                RepositoryUrl = repoUrl,
                RepositoryBranch = repoBranch,
                ForwardFlow = forwardFlowStatus,
                Backflow = backflowStatus
            });
        }

        return statuses;
    }

    private static CodeflowSubscriptionStatus? CreateSubscriptionStatus(
        Maestro.Data.Models.Subscription? subscription,
        Dictionary<Guid, InProgressPullRequest> activePrsBySubscriptionIds,
        Dictionary<Guid, BuildStaleness> buildStalenessMap)
    {
        if (subscription == null)
        {
            return null;
        }

        buildStalenessMap.TryGetValue(subscription.Id, out var staleness);
        activePrsBySubscriptionIds.TryGetValue(subscription.Id, out var pr);

        var trackedPullRequest = pr != null
            ? PullRequestController.ToTrackedPullRequest(pr, subscription.Id.ToString(), subscription)
            : null;

        return new CodeflowSubscriptionStatus
        {
            Subscription = new Subscription(subscription),
            ActivePullRequest = trackedPullRequest,
            NewerBuildsAvailable = staleness?.NewerBuildsCount,
            NewestBuildDate = staleness?.NewestBuildDate
        };
    }

    #endregion Helpers
}
