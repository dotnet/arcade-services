// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A <see cref="PullRequestActorImplementation" /> for batched subscriptions that reads its Target and Merge Policies
///     from the configuration for a repository
/// </summary>
internal class BatchedPullRequestUpdater : PullRequestUpdater
{
    private readonly BatchedPullRequestUpdaterId _id;
    private readonly BuildAssetRegistryContext _context;
    private readonly IRedisCache _batchedSubscriptionMutex;

    private const int MutexWakeUpTimeSeconds = 10;

    public BatchedPullRequestUpdater(
        BatchedPullRequestUpdaterId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<BatchedPullRequestUpdater> logger)
        : base(
            id,
            mergePolicyEvaluator,
            remoteFactory,
            updaterFactory,
            coherencyUpdateResolver,
            pullRequestBuilder,
            cacheFactory,
            reminderManagerFactory,
            workItemProducerFactory,
            logger)
    {
        _id = id;
        _context = context;
        _batchedSubscriptionMutex = cacheFactory.Create($"{id}_mutex");
    }

    protected override Task<(string repository, string branch)> GetTargetAsync()
    {
        return Task.FromResult((_id.Repository, _id.Branch));
    }

    protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
    {
        RepositoryBranch? repositoryBranch = await _context.RepositoryBranches.FindAsync(_id.Repository, _id.Branch);
        return repositoryBranch?.PolicyObject?.MergePolicies ?? [];
    }

    public override async Task<bool> UpdateAssetsAsync(Guid subscriptionId, SubscriptionType type, int buildId, string sourceRepo, string sourceSha, List<Maestro.Contracts.Asset> assets)
    {
        try
        {
            // Check if a different replica is processing a subscription that belongs to the same batch
            string? state;
            do
            {
                state = await _batchedSubscriptionMutex.GetAsync();
            } while (await Utility.SleepIfTrue(
                () => !string.IsNullOrEmpty(state),
                MutexWakeUpTimeSeconds));
            // Updating assets should never take more than an hour. If it does, it's possible something
            // bad happened, so reset the mutex
            await _batchedSubscriptionMutex.SetAsync("busy", TimeSpan.FromHours(1));

            return await base.UpdateAssetsAsync(subscriptionId, type, buildId, sourceRepo, sourceSha, assets);
        }
        finally
        {
            // if something happens, we don' want this subscription to be blocked forever
            await _batchedSubscriptionMutex.TryDeleteAsync();
        }
    }
}
