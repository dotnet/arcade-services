// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Models;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class BackflowValidationProcessor : WorkItemProcessor<BackflowValidationWorkItem>
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ILocalGitClient _gitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<BackflowValidationProcessor> _logger;

    public BackflowValidationProcessor(
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        ILocalGitClient gitClient,
        IVersionDetailsParser versionDetailsParser,
        IRedisCacheFactory redisCacheFactory,
        ILogger<BackflowValidationProcessor> logger)
    {
        _context = context;
        _remoteFactory = remoteFactory;
        _gitClient = gitClient;
        _versionDetailsParser = versionDetailsParser;
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        BackflowValidationWorkItem workItem,
        CancellationToken cancellationToken)
    {
        try
        {
            string vmrSha = workItem.VmrCommitSha;

            // If build ID is provided, resolve it to a SHA
            if (workItem.VmrBuildId.HasValue)
            {
                var build = await _context.Builds
                    .FirstOrDefaultAsync(b => b.Id == workItem.VmrBuildId.Value, cancellationToken);

                if (build == null)
                {
                    _logger.LogError("Build {buildId} not found", workItem.VmrBuildId.Value);
                    return false;
                }

                vmrSha = build.Commit;
                _logger.LogInformation("Resolved build {buildId} to VMR SHA {sha}", workItem.VmrBuildId.Value, vmrSha);
            }

            // Detect which branch the SHA is on
            var branches = await DetectBranchesAsync(vmrSha, cancellationToken);
            if (branches.Count == 0)
            {
                _logger.LogWarning("Could not detect branch for VMR SHA {sha}", vmrSha);
                return false;
            }

            _logger.LogInformation("Detected {count} branch(es) for SHA {sha}: {branches}", 
                branches.Count, vmrSha, string.Join(", ", branches));

            // Calculate status for each branch
            var branchStatuses = new Dictionary<string, BranchBackflowStatus>();
            foreach (var branch in branches)
            {
                var status = await CalculateBranchBackflowStatusAsync(vmrSha, branch, cancellationToken);
                if (status != null)
                {
                    branchStatuses[branch] = status;
                }
            }

            // Store in Redis cache
            var backflowStatus = new BackflowStatus
            {
                VmrCommitSha = vmrSha,
                ComputationTimestamp = DateTimeOffset.UtcNow,
                BranchStatuses = branchStatuses
            };

            var cache = _redisCacheFactory.Create<BackflowStatus>(vmrSha, includeTypeInKey: true);
            await cache.SetAsync(backflowStatus, TimeSpan.FromHours(24));

            _logger.LogInformation("Successfully computed and cached backflow status for VMR SHA {sha}", vmrSha);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process backflow validation for work item");
            return false;
        }
    }

    private async Task<List<string>> DetectBranchesAsync(string sha, CancellationToken cancellationToken)
    {
        var branches = new List<string>();
        
        try
        {
            // Get VMR repository URI from constants
            var vmrUri = Constants.DefaultVmrUri;
            var remote = await _remoteFactory.CreateRemoteAsync(vmrUri);

            // Check common branch patterns
            var branchesToCheck = new[] { "main", "internal/main" };
            
            foreach (var branch in branchesToCheck)
            {
                try
                {
                    // Try to get the commit on this branch
                    var commitInfo = await remote.GetCommitAsync(vmrUri, sha);
                    if (commitInfo != null)
                    {
                        branches.Add(branch);
                        
                        // If this is an internal branch, also check the public branch
                        if (branch.StartsWith("internal/"))
                        {
                            var publicBranch = branch.Replace("internal/", "");
                            if (!branches.Contains(publicBranch) && branchesToCheck.Contains(publicBranch))
                            {
                                try
                                {
                                    var publicCommit = await remote.GetCommitAsync(vmrUri, sha);
                                    if (publicCommit != null)
                                    {
                                        branches.Add(publicBranch);
                                    }
                                }
                                catch
                                {
                                    // Public branch doesn't have this commit, which is expected
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "SHA {sha} not found on branch {branch}", sha, branch);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting branches for SHA {sha}", sha);
        }

        return branches;
    }

    private async Task<BranchBackflowStatus?> CalculateBranchBackflowStatusAsync(
        string vmrSha,
        string branch,
        CancellationToken cancellationToken)
    {
        try
        {
            // Branch will be normalized when used in queries against the database
            
            // Get the default channel for the VMR on this branch
            var defaultChannel = await _context.DefaultChannels
                .Include(dc => dc.Channel)
                .FirstOrDefaultAsync(
                    dc => dc.Repository == Constants.DefaultVmrUri && 
                          dc.Branch == branch,
                    cancellationToken);

            if (defaultChannel == null)
            {
                _logger.LogWarning("No default channel found for VMR branch {branch}", branch);
                return null;
            }

            // Get source-enabled subscriptions from VMR on this channel (backflow subscriptions)
            var subscriptions = await _context.Subscriptions
                .Include(s => s.Channel)
                .Where(s => s.ChannelId == defaultChannel.ChannelId &&
                           s.SourceEnabled &&
                           s.SourceRepository == Constants.DefaultVmrUri &&
                           s.Enabled)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {count} backflow subscriptions for branch {branch}", 
                subscriptions.Count, branch);

            var subscriptionStatuses = new List<SubscriptionBackflowStatus>();

            foreach (var subscription in subscriptions)
            {
                try
                {
                    var status = await CalculateSubscriptionBackflowStatusAsync(
                        vmrSha,
                        branch,
                        subscription,
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
        string vmrBranch,
        Maestro.Data.Models.Subscription subscription,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the last backflowed VMR commit SHA from Version.Details.xml in the target branch
            var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
            
            string lastBackflowedSha;
            try
            {
                var versionDetails = await remote.GetFileContentsAsync(
                    VersionFiles.VersionDetailsXml,
                    subscription.TargetRepository,
                    subscription.TargetBranch);

                var dependencies = _versionDetailsParser.ParseVersionDetailsXml(versionDetails, includePinned: false);

                // Find the VMR dependency in the version details
                var vmrDependency = dependencies.Dependencies.FirstOrDefault(d => 
                    d.RepoUri.Equals(Constants.DefaultVmrUri, StringComparison.OrdinalIgnoreCase));

                if (vmrDependency == null)
                {
                    _logger.LogWarning(
                        "No VMR dependency found in Version.Details.xml for {targetRepo}/{targetBranch}",
                        subscription.TargetRepository,
                        subscription.TargetBranch);
                    return null;
                }

                lastBackflowedSha = vmrDependency.Commit;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not read Version.Details.xml from {targetRepo}/{targetBranch}",
                    subscription.TargetRepository,
                    subscription.TargetBranch);
                return null;
            }

            // Calculate commit distance
            // For now, we'll use a simplified approach and set distance to 0
            // A full implementation would need to clone the VMR and use git to calculate the distance
            int commitDistance = 0;

            _logger.LogInformation(
                "Subscription {subscriptionId}: Last backflowed SHA is {lastSha}, current SHA is {currentSha}",
                subscription.Id,
                lastBackflowedSha,
                vmrSha);

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

    protected override Dictionary<string, object> GetLoggingContextData(BackflowValidationWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["VmrCommitSha"] = workItem.VmrCommitSha;
        
        if (workItem.VmrBuildId.HasValue)
        {
            data["VmrBuildId"] = workItem.VmrBuildId.Value;
        }
        
        return data;
    }
}
