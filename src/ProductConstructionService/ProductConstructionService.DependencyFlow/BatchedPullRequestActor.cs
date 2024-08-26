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
internal class BatchedPullRequestActor : PullRequestActor
{
    private readonly BatchedPullRequestActorId _id;
    private readonly BuildAssetRegistryContext _context;

    public BatchedPullRequestActor(
        BatchedPullRequestActorId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IActorFactory actorFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<BatchedPullRequestActor> logger)
        : base(
            id,
            mergePolicyEvaluator,
            remoteFactory,
            actorFactory,
            coherencyUpdateResolver,
            pullRequestBuilder,
            cacheFactory,
            reminderManagerFactory,
            workItemProducerFactory,
            logger)
    {
        _id = id;
        _context = context;
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
}
