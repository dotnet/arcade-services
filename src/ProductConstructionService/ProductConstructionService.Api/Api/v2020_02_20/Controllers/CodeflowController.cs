// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Maestro.Services.Common.Cache;
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
    /// <param name="includeConflictStatuses">Whether to include conflict statuses for subscriptions with existing PRs in the response</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<CodeflowStatus>), Description = "The list of codeflow statuses")]
    [ValidateModelState]
    public async Task<IActionResult> GetCodeflowStatuses(
        [FromQuery] string repositoryUrl,
        [FromQuery] string branch,
        [FromQuery] bool includeConflictStatuses = false)
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

        List<string> updaterIds = [.. subscriptions.Select(s => PullRequestUpdaterId.CreateUpdaterId(s).Id)];

        Dictionary<string, InProgressPullRequest> activePrsPerSubscriptionId = await GetInProgressPullRequestsAsync(updaterIds);

        Dictionary<string, NewestBuildInfo> newestBuildInfoPerSubscription = await CalculateNewestBuildInfo(subscriptions);

        Dictionary<string, SubscriptionTriggerOutcome> latestOutcomePerSubscription = await GetLatestOutcomesAsync(subscriptions);

        Dictionary<string, bool> conflictStatusesPerSubscription = includeConflictStatuses
            ? await GetCodeflowPrConflictStatusesAsync(updaterIds)
            : [];

        var codeflowStatuses = BuildCodeflowStatuses(
            forwardFlowSubscriptions,
            backflowSubscriptions,
            activePrsPerSubscriptionId,
            newestBuildInfoPerSubscription,
            latestOutcomePerSubscription,
            conflictStatusesPerSubscription);

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

    private async Task<Dictionary<string, InProgressPullRequest>> GetInProgressPullRequestsAsync(List<string> updaterIds)
    {
        var cacheKeys = updaterIds
            .Select(id => $"{nameof(InProgressPullRequest)}_{id}")
            .ToList();

        var cache = _cacheFactory.Create<InProgressPullRequest>(string.Empty, includeTypeInKey: false);
        var batchResults = await cache.TryGetStateBatchAsync(cacheKeys);

        var result = new Dictionary<string, InProgressPullRequest>();
        foreach (var id in updaterIds)
        {
            var prKey = $"{nameof(InProgressPullRequest)}_{id}";
            if (batchResults.TryGetValue(prKey, out var pr) && pr != null)
            {
                result[id] = pr;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, NewestBuildInfo>> CalculateNewestBuildInfo(
        List<Maestro.Data.Models.Subscription> subscriptions)
    {
        var subscriptionsWithBuilds = subscriptions
            .Where(s => s.LastAppliedBuild != null)
            .ToList();

        if (subscriptionsWithBuilds.Count == 0)
        {
            return [];
        }

        // Group subscriptions by (ChannelId, SourceRepository) so we query once per unique pair
        // instead of once per subscription (correlated subquery).
        // For each group, use the earliest LastAppliedBuild date as the cutoff so we fetch
        // all potentially relevant builds in a single query per group.
        var groups = subscriptionsWithBuilds
            .GroupBy(s => (s.ChannelId, s.SourceRepository))
            .ToList();

        var channelIds = groups.Select(g => g.Key.ChannelId).Distinct().ToList();
        var earliestCutoff = subscriptionsWithBuilds.Min(s => s.LastAppliedBuild.DateProduced);

        var newerBuildsQuery = _context.BuildChannels
            .Where(bc => channelIds.Contains(bc.ChannelId))
            .Where(bc => bc.Build.DateProduced > earliestCutoff)
            .Select(bc => new
            {
                bc.ChannelId,
                bc.BuildId,
                bc.Build.GitHubRepository,
                bc.Build.AzureDevOpsRepository,
                bc.Build.DateProduced,
            });

        var newerBuilds = await newerBuildsQuery.ToListAsync();

        var result = new Dictionary<string, NewestBuildInfo>();
        foreach (var subscription in subscriptionsWithBuilds)
        {
            var lastAppliedDate = subscription.LastAppliedBuild.DateProduced;
            var matchingBuilds = newerBuilds
                .Where(b =>
                    b.ChannelId == subscription.ChannelId
                    && b.DateProduced > lastAppliedDate
                    && (b.GitHubRepository == subscription.SourceRepository
                        || b.AzureDevOpsRepository == subscription.SourceRepository))
                .ToList();

            int? newestBuildId = null;
            DateTimeOffset? newestDate = null;

            if (matchingBuilds.Count > 0)
            {
                var newestBuild = matchingBuilds.MaxBy(b => b.DateProduced) ?? matchingBuilds.First(); 
                newestBuildId = newestBuild.BuildId;
                newestDate = newestBuild.DateProduced;
            }

            result[PullRequestUpdaterId.CreateUpdaterId(subscription).Id] = new NewestBuildInfo(matchingBuilds.Count, newestBuildId, newestDate);
        }

        return result;
    }

    private async Task<Dictionary<string, bool>> GetCodeflowPrConflictStatusesAsync(List<string> updaterIds)
    {
        var cacheKeys = updaterIds
            .Select(id => $"{nameof(MergePolicyEvaluationResult)}_{id}")
            .ToList();

        var cache = _cacheFactory.Create<MergePolicyEvaluationResults>(string.Empty, includeTypeInKey: false);
        var batchResults = await cache.TryGetStateBatchAsync(cacheKeys);

        var result = new Dictionary<string, bool>();
        foreach (var id in updaterIds)
        {
            var checkResultsKey = $"{nameof(MergePolicyEvaluationResults)}_{id}";
            if (batchResults.TryGetValue(checkResultsKey, out var checkResults) && checkResults != null)
            {
                bool hasConflict = checkResults.Results
                    .Any(checkResult => checkResult.Message.Contains(CodeFlowMergePolicy.BarIdMismatchErrorMarker));

                result[id] = hasConflict;
            }
        }
        return result;
    }

    private record NewestBuildInfo(int NewerBuildsCount, int? NewestBuildId, DateTimeOffset? NewestBuildDate);

    private async Task<Dictionary<string, SubscriptionTriggerOutcome>> GetLatestOutcomesAsync(
        List<Maestro.Data.Models.Subscription> subscriptions)
    {
        var subscriptionIds = subscriptions.Select(s => s.Id).Distinct().ToList();
        if (subscriptionIds.Count == 0)
        {
            return [];
        }

        var latestOutcomes = await _context.SubscriptionOutcomes
            .AsNoTracking()
            .Where(o => subscriptionIds.Contains(o.SubscriptionId)
                && o.Date == _context.SubscriptionOutcomes
                    .Where(o2 => o2.SubscriptionId == o.SubscriptionId)
                    .Max(o2 => o2.Date))
            .ToListAsync();

        return latestOutcomes.ToDictionary(
            o => o.SubscriptionId.ToString(),
            o => new SubscriptionTriggerOutcome(o));
    }

    #region Helpers

    private static List<CodeflowStatus> BuildCodeflowStatuses(
        List<Maestro.Data.Models.Subscription> forwardFlowSubscriptions,
        List<Maestro.Data.Models.Subscription> backflowSubscriptions,
        Dictionary<string, InProgressPullRequest> activePrsPerSubscriptionId,
        Dictionary<string, NewestBuildInfo> newestBuildInfoPerSubscription,
        Dictionary<string, SubscriptionTriggerOutcome> latestOutcomePerSubscription,
        Dictionary<string, bool> conflictStatuses)
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

            var forwardFlowStatus = CreateSubscriptionStatus(
                forwardFlowSubscription,
                activePrsPerSubscriptionId,
                newestBuildInfoPerSubscription,
                latestOutcomePerSubscription,
                conflictStatuses);

            var backflowStatus = CreateSubscriptionStatus(
                backflowSubscription,
                activePrsPerSubscriptionId,
                newestBuildInfoPerSubscription,
                latestOutcomePerSubscription,
                conflictStatuses);

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
        Dictionary<string, InProgressPullRequest> activePrsBySubscriptionIds,
        Dictionary<string, NewestBuildInfo> newestBuildInfos,
        Dictionary<string, SubscriptionTriggerOutcome> latestOutcomes,
        Dictionary<string, bool> conflictStatuses)
    {
        if (subscription == null)
        {
            return null;
        }

        newestBuildInfos.TryGetValue(PullRequestUpdaterId.CreateUpdaterId(subscription).Id, out var newestBuildInfo);
        activePrsBySubscriptionIds.TryGetValue(PullRequestUpdaterId.CreateUpdaterId(subscription).Id, out var pr);
        latestOutcomes.TryGetValue(PullRequestUpdaterId.CreateUpdaterId(subscription).Id, out var latestOutcome);
        conflictStatuses.TryGetValue(PullRequestUpdaterId.CreateUpdaterId(subscription).Id, out var hasConflict);

        hasConflict = hasConflict && pr != null;

        if (subscription.LastAppliedBuild != null && newestBuildInfo != null)
        {
            subscription.LastAppliedBuild.Staleness = newestBuildInfo.NewerBuildsCount;
        }

        var trackedPullRequest = pr != null
            ? PullRequestController.ToTrackedPullRequest(pr, subscription.Id.ToString(), subscription)
            : null;

        return new CodeflowSubscriptionStatus
        {
            Subscription = new Subscription(subscription),
            ActivePullRequest = trackedPullRequest,
            NewestBuildId = newestBuildInfo?.NewestBuildId,
            NewestBuildDate = newestBuildInfo?.NewestBuildDate,
            LatestOutcome = latestOutcome,
            HasCodeflowConflict = hasConflict,
        };
    }

    #endregion Helpers
    }
