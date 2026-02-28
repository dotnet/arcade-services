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
    [HttpGet("status")]
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
        var mappings = ExtractMappings(subscriptions);
        var inProgressCodeflowPrs = await GetInProgressPullRequestsAsync(subscriptions);

        var codeflowStatuses = await BuildCodeflowStatusesAsync(
            mappings,
            forwardFlowSubscriptions,
            backflowSubscriptions,
            inProgressCodeflowPrs);

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

    private async Task<List<CodeflowStatus>> BuildCodeflowStatusesAsync(
        List<string> mappings,
        List<Maestro.Data.Models.Subscription> forwardFlowSubscriptions,
        List<Maestro.Data.Models.Subscription> backflowSubscriptions,
        Dictionary<Guid, InProgressPullRequest> allPrs)
    {
        var stalenessMap = await CalculateBuildStalenessAsync(
            [.. forwardFlowSubscriptions, .. backflowSubscriptions]);

        var codeflowStatuses = new List<CodeflowStatus>();

        foreach (var mapping in mappings)
        {
            var forwardflowSubscription = forwardFlowSubscriptions.FirstOrDefault(s => s.TargetDirectory == mapping);
            var backflowSubscription = backflowSubscriptions.FirstOrDefault(s => s.SourceDirectory == mapping);

            var forwardFlowStatus = CreateSubscriptionStatus(forwardflowSubscription, allPrs, stalenessMap);
            var backflowStatus = CreateSubscriptionStatus(backflowSubscription, allPrs, stalenessMap);

            var repositoryUrlForMapping = forwardflowSubscription?.SourceRepository ?? backflowSubscription?.TargetRepository;
            var repositoryBranchForMapping = backflowSubscription?.TargetBranch;

            codeflowStatuses.Add(new CodeflowStatus
            {
                MappingName = mapping,
                RepositoryUrl = repositoryUrlForMapping,
                RepositoryBranch = repositoryBranchForMapping,
                ForwardFlow = forwardFlowStatus,
                Backflow = backflowStatus
            });
        }

        return codeflowStatuses;
    }

    private async Task<Dictionary<Guid, int>> CalculateBuildStalenessAsync(
        List<Maestro.Data.Models.Subscription> subscriptions)
    {
        var subscriptionsWithBuilds = subscriptions
            .Where(s => s.LastAppliedBuildId != null)
            .ToList();

        if (subscriptionsWithBuilds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var groupedBySourceAndChannel = subscriptionsWithBuilds
            .GroupBy(s => new { SourceRepo = s.SourceRepository, ChannelId = s.ChannelId })
            .ToList();

        var stalenessMap = new Dictionary<Guid, int>();

        foreach (var group in groupedBySourceAndChannel)
        {
            var sourceRepo = group.Key.SourceRepo;
            var channelId = group.Key.ChannelId;
            var subscriptionsInGroup = group.ToList();

            var allBuilds = await _context.Builds
                .Where(b => b.GitHubRepository == sourceRepo || b.AzureDevOpsRepository == sourceRepo)
                .Where(b => b.BuildChannels.Any(bc => bc.ChannelId == channelId))
                .OrderBy(b => b.DateProduced)
                .Select(b => new { b.Id, b.DateProduced })
                .ToListAsync();

            foreach (var subscription in subscriptionsInGroup)
            {
                var lastAppliedBuildDate = subscription.LastAppliedBuild.DateProduced;
                var newerBuildsCount = allBuilds.Count(b => b.DateProduced > lastAppliedBuildDate);
                stalenessMap[subscription.Id] = newerBuildsCount;
            }
        }

        return stalenessMap;
    }

    #region Helpers

    private static CodeflowSubscriptionStatus? CreateSubscriptionStatus(
        Maestro.Data.Models.Subscription? subscription,
        Dictionary<Guid, InProgressPullRequest> allPrs,
        Dictionary<Guid, int> stalenessMap)
    {
        if (subscription == null)
        {
            return null;
        }

        var staleness = stalenessMap.GetValueOrDefault(subscription.Id, 0);
        allPrs.TryGetValue(subscription.Id, out var pr);

        return new CodeflowSubscriptionStatus
        {
            Subscription = new Subscription(subscription),
            InProgressPullRequestUrl = pr?.Url,
            Staleness = staleness
        };
    }

    private static List<string> ExtractMappings(List<Maestro.Data.Models.Subscription> subscriptions)
        => [.. subscriptions
            .Select(s => !string.IsNullOrEmpty(s.TargetDirectory) ? s.TargetDirectory : s.SourceDirectory)
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct()];

    #endregion Helpers
}
