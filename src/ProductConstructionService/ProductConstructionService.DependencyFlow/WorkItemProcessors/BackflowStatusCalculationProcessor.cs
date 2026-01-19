// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Models;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class BackflowStatusCalculationProcessor : WorkItemProcessor<BackflowStatusCalculationWorkItem>
{
    private const string InternalBranchPrefix = "internal/";

    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly ILogger<BackflowStatusCalculationProcessor> _logger;

    public BackflowStatusCalculationProcessor(
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        IRedisCacheFactory redisCacheFactory,
        IVmrCloneManager vmrCloneManager,
        ILogger<BackflowStatusCalculationProcessor> logger)
    {
        _context = context;
        _remoteFactory = remoteFactory;
        _versionDetailsParser = versionDetailsParser;
        _redisCacheFactory = redisCacheFactory;
        _vmrCloneManager = vmrCloneManager;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        BackflowStatusCalculationWorkItem workItem,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve build ID to a SHA
            Build? build = await _context.Builds
                .FirstOrDefaultAsync(b => b.Id == workItem.VmrBuildId, cancellationToken);

            if (build == null)
            {
                _logger.LogError("Build {buildId} not found", workItem.VmrBuildId);
                return false;
            }

            var branch = build.GetBranch();
            string[] branches = branch.StartsWith(InternalBranchPrefix)
                ? [branch, branch.Substring(InternalBranchPrefix.Length)]
                : [branch];

            _logger.LogInformation("Calculating backflow status for build {buildId} of commit {commit} branches: {branches}",
                build.Id, build.Commit, string.Join(", ", branches));

            ILocalGitRepo vmrClone = await _vmrCloneManager.PrepareVmrAsync(
                [build.GetRepository()],
                branches,
                branch,
                resetToRemote: true,
                cancellationToken);

            // Calculate status for each branch
            var branchStatuses = new Dictionary<string, BranchBackflowStatus>();
            foreach (var sourceBranch in branches)
            {
                var status = await CalculateBranchBackflowStatusAsync(build, sourceBranch, vmrClone, cancellationToken);
                if (status != null)
                {
                    branchStatuses[sourceBranch] = status;
                }
            }

            // Store in Redis cache
            var backflowStatus = new BackflowStatus
            {
                VmrCommitSha = build.Commit,
                ComputationTimestamp = DateTimeOffset.UtcNow,
                BranchStatuses = branchStatuses
            };

            var cache = _redisCacheFactory.Create<BackflowStatus>(build.Commit, includeTypeInKey: true);
            await cache.SetAsync(backflowStatus, TimeSpan.FromDays(60));

            _logger.LogInformation("Successfully computed and cached backflow status for VMR SHA {sha}", build.Commit);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process backflow status calculation for work item");
            return false;
        }
    }

    private async Task<BranchBackflowStatus?> CalculateBranchBackflowStatusAsync(
        Build vmrBuild,
        string branch,
        ILocalGitRepo vmrClone,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the default channel for the VMR on this branch
            var defaultChannel = await _context.DefaultChannels
                .Include(dc => dc.Channel)
                .FirstOrDefaultAsync(
                    dc => dc.Repository == vmrBuild.GetRepository() && dc.Branch == branch,
                    cancellationToken);

            if (defaultChannel == null)
            {
                _logger.LogWarning("No default channel found for VMR branch {branch}", branch);
                return null;
            }

            // Get source-enabled subscriptions from VMR on this channel (backflow subscriptions)
            var subscriptions = await _context.Subscriptions
                .Include(s => s.Channel)
                .Where(s => s.ChannelId == defaultChannel.ChannelId
                    && s.SourceEnabled
                    && s.SourceRepository == vmrBuild.GetRepository())
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {count} backflow subscriptions for branch {branch}",
                subscriptions.Count, branch);

            var subscriptionStatuses = new List<SubscriptionBackflowStatus>();

            foreach (var subscription in subscriptions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var status = await CalculateSubscriptionBackflowStatusAsync(
                        vmrBuild.Commit,
                        subscription,
                        vmrClone,
                        cancellationToken);

                    if (status != null)
                    {
                        subscriptionStatuses.Add(status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to calculate backflow status for subscription {subscriptionId} to {targetRepo}/{targetBranch}",
                        subscription.Id,
                        subscription.TargetRepository,
                        subscription.TargetBranch);
                }
            }

            return new BranchBackflowStatus
            {
                Branch = branch,
                DefaultChannelId = defaultChannel.ChannelId,
                SubscriptionStatuses = subscriptionStatuses
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating branch backflow status for branch {branch}", branch);
            return null;
        }
    }

    private async Task<SubscriptionBackflowStatus?> CalculateSubscriptionBackflowStatusAsync(
        string vmrSha,
        Subscription subscription,
        ILocalGitRepo vmrClone,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the last backflowed VMR commit SHA from Version.Details.xml in the target branch
            var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

            string? lastBackflowedSha;
            try
            {
                var codeflowMetadata = await remote.GetSourceDependencyAsync(
                    subscription.TargetRepository,
                    subscription.TargetBranch);

                lastBackflowedSha = codeflowMetadata?.Sha;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not read Version.Details.xml from {targetRepo}/{targetBranch}",
                    subscription.TargetRepository,
                    subscription.TargetBranch);
                return null;
            }

            if (lastBackflowedSha == null)
            {
                _logger.LogWarning("No backflow for repository {repository} found on branch {branch}",
                    subscription.TargetRepository,
                    subscription.TargetBranch);
                return null;
            }

            // Calculate commit distance using git rev-list
            int commitDistance = 0;
            try
            {
                // TODO: For public branch, we could subtract the amount of commits in the internal branch only.
                //       For that, we'd need to find the merge base.
                var result = await vmrClone.ExecuteGitCommand(
                    ["rev-list", "--count", $"{lastBackflowedSha}..{vmrSha}"],
                    cancellationToken);
                
                if (result.Succeeded && int.TryParse(result.StandardOutput.Trim(), out var distance))
                {
                    commitDistance = distance;
                    _logger.LogDebug(
                        "Subscription {subscriptionId}: Commit distance from {lastSha} to {currentSha} is {distance}",
                        subscription.Id,
                        lastBackflowedSha,
                        vmrSha,
                        commitDistance);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to calculate commit distance for subscription {subscriptionId}: {error}",
                        subscription.Id,
                        result.StandardError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error calculating commit distance for subscription {subscriptionId} between {lastSha} and {currentSha}",
                    subscription.Id,
                    lastBackflowedSha,
                    vmrSha);
            }

            _logger.LogDebug(
                "Last backflowed SHA for subscription {subscriptionId} is {lastSha}",
                subscription.Id,
                lastBackflowedSha);

            return new SubscriptionBackflowStatus
            {
                TargetRepository = subscription.TargetRepository,
                TargetBranch = subscription.TargetBranch,
                LastBackflowedSha = lastBackflowedSha,
                CommitDistance = commitDistance,
                SubscriptionId = subscription.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error calculating subscription backflow status for subscription {subscriptionId}",
                subscription.Id);
            return null;
        }
    }

    protected override Dictionary<string, object> GetLoggingContextData(BackflowStatusCalculationWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["VmrBuildId"] = workItem.VmrBuildId;
        return data;
    }
}
