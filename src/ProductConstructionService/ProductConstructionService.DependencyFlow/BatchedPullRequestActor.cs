// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A <see cref="PullRequestActorImplementation" /> for batched subscriptions that reads its Target and Merge Policies
///     from the configuration for a repository
/// </summary>
internal class BatchedPullRequestActor : PullRequestActor
{
    private readonly BatchedPullRequestActorId _id;
    private readonly BuildAssetRegistryContext _context;

    /// <param name="id">
    ///     The actor id for this actor.
    ///     If it is a <see cref="Guid" /> actor id, then it is required to be the id of a non-batched subscription in the
    ///     database
    ///     If it is a <see cref="string" /> actor id, then it MUST be an actor id created with
    ///     <see cref="PullRequestActorId.Create(string, string)" /> for use with all subscriptions targeting the specified
    ///     repository and branch.
    /// </param>
    public BatchedPullRequestActor(
        BatchedPullRequestActorId id,
        IReminderManager reminders,
        IStateManager stateManager,
        IMergePolicyEvaluator mergePolicyEvaluator,
        ICoherencyUpdateResolver updateResolver,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IActorFactory actorFactory,
        IPullRequestBuilder pullRequestBuilder,
        IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier,
        IReminderManager reminderManager,
        ILogger<BatchedPullRequestActor> logger)
        : base(
            id,
            reminders,
            stateManager,
            mergePolicyEvaluator,
            context,
            remoteFactory,
            actorFactory,
            updateResolver,
            pullRequestBuilder,
            pullRequestPolicyFailureNotifier,
            reminderManager,
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
