// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net;
using Maestro.Common.Cache;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<CodeflowController> _logger;

    public CodeflowController(
        BuildAssetRegistryContext context,
        IRedisCacheFactory cacheFactory,
        ILogger<CodeflowController> logger)
    {
        _context = context;
        _cacheFactory = cacheFactory;
        _logger = logger;
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

        var totalStopwatch = Stopwatch.StartNew();
        var stepStopwatch = Stopwatch.StartNew();

        var forwardFlowSubscriptions = await GetForwardFlowSubscriptionsAsync(repositoryUrl, branch);
        _logger.LogInformation("GetForwardFlowSubscriptionsAsync completed in {ElapsedMs}ms (repo: {RepositoryUrl}, branch: {Branch})",
            stepStopwatch.ElapsedMilliseconds, repositoryUrl, branch);

        stepStopwatch.Restart();
        var backflowSubscriptions = await GetBackflowSubscriptionsAsync(repositoryUrl, branch);
        _logger.LogInformation("GetBackflowSubscriptionsAsync completed in {ElapsedMs}ms (repo: {RepositoryUrl}, branch: {Branch})",
            stepStopwatch.ElapsedMilliseconds, repositoryUrl, branch);

        var subscriptions = forwardFlowSubscriptions.Concat(backflowSubscriptions).ToList();

        stepStopwatch.Restart();
        Dictionary<Guid, InProgressPullRequest> activePrsPerSubscriptionId = await GetInProgressPullRequestsAsync(subscriptions);
        _logger.LogInformation("GetInProgressPullRequestsAsync completed in {ElapsedMs}ms for {SubscriptionCount} subscriptions",
            stepStopwatch.ElapsedMilliseconds, subscriptions.Count);

        stepStopwatch.Restart();
        Dictionary<Guid, int> newerBuildsPerSubscriptionId = await CalculateBuildStalenessPerSubscription(subscriptions);
        _logger.LogInformation("CalculateBuildStalenessPerSubscription completed in {ElapsedMs}ms for {SubscriptionCount} subscriptions",
            stepStopwatch.ElapsedMilliseconds, subscriptions.Count);

        stepStopwatch.Restart();
        var codeflowStatuses = BuildCodeflowStatuses(
            forwardFlowSubscriptions,
            backflowSubscriptions,
            activePrsPerSubscriptionId,
            newerBuildsPerSubscriptionId);
        _logger.LogInformation("BuildCodeflowStatuses completed in {ElapsedMs}ms for {StatusCount} statuses",
            stepStopwatch.ElapsedMilliseconds, codeflowStatuses.Count);

        _logger.LogInformation("GetCodeflowStatuses total completed in {TotalElapsedMs}ms (repo: {RepositoryUrl}, branch: {Branch})",
            totalStopwatch.ElapsedMilliseconds, repositoryUrl, branch);

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

    private async Task<Dictionary<Guid, int>> CalculateBuildStalenessPerSubscription(
        List<Maestro.Data.Models.Subscription> subscriptions)
    {
        var subscriptionIds = subscriptions
            .Where(s => s.LastAppliedBuildId != null)
            .Select(s => s.Id)
            .ToList();

        if (subscriptionIds.Count == 0)
        {
            return [];
        }

        var newerBuildsPerSubscriptionId = await _context.Subscriptions
            .Where(s => subscriptionIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                Staleness = _context.BuildChannels
                    .Where(bc => bc.ChannelId == s.ChannelId)
                    .Where(bc => bc.Build.GitHubRepository == s.SourceRepository
                              || bc.Build.AzureDevOpsRepository == s.SourceRepository)
                    .Where(bc => bc.Build.DateProduced > s.LastAppliedBuild.DateProduced)
                    .Count()
            })
            .ToDictionaryAsync(x => x.Id, x => x.Staleness);

        return newerBuildsPerSubscriptionId;
    }

    #region Helpers

    private static List<CodeflowStatus> BuildCodeflowStatuses(
        List<Maestro.Data.Models.Subscription> forwardFlowSubscriptions,
        List<Maestro.Data.Models.Subscription> backflowSubscriptions,
        Dictionary<Guid, InProgressPullRequest> activePrsPerSubscriptionId,
        Dictionary<Guid, int> newerBuildsPerSubscriptionId)
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

            var forwardFlowStatus = CreateSubscriptionStatus(forwardFlowSubscription, activePrsPerSubscriptionId, newerBuildsPerSubscriptionId);
            var backflowStatus = CreateSubscriptionStatus(backflowSubscription, activePrsPerSubscriptionId, newerBuildsPerSubscriptionId);

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
        Dictionary<Guid, int> stalenessMap)
    {
        if (subscription == null)
        {
            return null;
        }

        int? staleness = stalenessMap.TryGetValue(subscription.Id, out var value) ? value : null;
        activePrsBySubscriptionIds.TryGetValue(subscription.Id, out var pr);

        var trackedPullRequest = pr != null
            ? PullRequestController.ToTrackedPullRequest(pr, subscription.Id.ToString(), subscription)
            : null;

        return new CodeflowSubscriptionStatus
        {
            Subscription = new Subscription(subscription),
            ActivePullRequest = trackedPullRequest,
            NewerBuildsAvailable = staleness
        };
    }

    #endregion Helpers
}
