// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionTriggerProcessor : WorkItemProcessor<SubscriptionTriggerWorkItem>
{
    private readonly BuildAssetRegistryContext _context;
    private readonly OperationManager _operations;
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly ILogger<SubscriptionTriggerProcessor> _logger;

    public SubscriptionTriggerProcessor(
        BuildAssetRegistryContext context,
        OperationManager operationManager,
        IPullRequestUpdaterFactory updaterFactory,
        ILogger<SubscriptionTriggerProcessor> logger)
    {
        _context = context;
        _operations = operationManager;
        _updaterFactory = updaterFactory;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        SubscriptionTriggerWorkItem workItem,
        CancellationToken cancellationToken)
    {
        if (workItem.BuildId.HasValue && workItem.BuildId.Value > 0)
        {
            return await StartSubscriptionUpdateForSpecificBuildAsync(
                workItem.SubscriptionId,
                workItem.BuildId.Value,
                workItem.Force);
        }

        return await StartSubscriptionUpdateAsync(workItem.SubscriptionId, workItem.Force);
    }

    protected override string? GetSynchronizationKey(SubscriptionTriggerWorkItem workItem) => "SubscriptionTrigger_" + workItem.SubscriptionId;

    /// <summary>
    ///     Run a single subscription, only accept the build Id specified 
    /// </summary>
    /// <param name="subscriptionId">Subscription to run the update for.</param>
    /// <param name="buildId">BAR build id to run the update for</param>
    /// <param name="force">Force update even for PRs with pending or successful checks</param>
    private async Task<bool> StartSubscriptionUpdateForSpecificBuildAsync(Guid subscriptionId, int buildId, bool force)
    {
        var subscriptionToUpdate =
            (from sub in _context.Subscriptions
             where sub.Id == subscriptionId
             where sub.Enabled
             let specificBuild =
                 sub.Channel.BuildChannels.Select(bc => bc.Build)
                     .Where(b => sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository)
                     .Where(b => b.Id == buildId)
                     .FirstOrDefault()
             where specificBuild != null
             select new
             {
                 subscription = sub.Id,
                 specificBuild = specificBuild.Id
             }).SingleOrDefault();

        if (subscriptionToUpdate == null)
        {
            return true;
        }

        return await UpdateSubscriptionAsync(subscriptionToUpdate.subscription, subscriptionToUpdate.specificBuild, force);
    }

    /// <summary>
    ///     Run a single subscription, adopting the latest build's id
    /// </summary>
    /// <param name="subscriptionId">Subscription to run the update for.</param>
    /// <param name="force">Force update even for PRs with pending or successful checks</param>
    private async Task<bool> StartSubscriptionUpdateAsync(Guid subscriptionId, bool force)
    {
        var subscriptionToUpdate =
            (from sub in _context.Subscriptions
             where sub.Id == subscriptionId
             where sub.Enabled
             let latestBuild =
                 sub.Channel.BuildChannels.Select(bc => bc.Build)
                     .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                     .OrderByDescending(b => b.DateProduced)
                     .FirstOrDefault()
             where latestBuild != null
             select new
             {
                 subscription = sub.Id,
                 latestBuild = latestBuild.Id
             }).SingleOrDefault();

        if (subscriptionToUpdate == null)
        {
            return true;
        }

        return await UpdateSubscriptionAsync(subscriptionToUpdate.subscription, subscriptionToUpdate.latestBuild, force);
    }

    private async Task<bool> UpdateSubscriptionAsync(Guid subscriptionId, int buildId, bool force)
    {
        using (_operations.BeginOperation("Updating subscription '{subscriptionId}' with build '{buildId}'", subscriptionId, buildId))
        {
            try
            {
                ISubscriptionTriggerer triggerer = _updaterFactory.CreateSubscriptionTrigerrer(subscriptionId);
                await triggerer.UpdateSubscriptionAsync(buildId, force);
                return true;
            }
            catch (Exception)
            {
                _logger.LogError("Failed to update subscription '{subscriptionId}' with build '{buildId}'", subscriptionId, buildId);
                throw;
            }
        }
    }

    protected override Dictionary<string, object> GetLoggingContextData(SubscriptionTriggerWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["SubscriptionId"] = workItem.SubscriptionId;

        if (workItem.BuildId.HasValue)
        {
            data["BuildId"] = workItem.BuildId.Value;
        }

        return data;
    }
}
