// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.StateModel;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A <see cref="PullRequestActorImplementation" /> that reads its Merge Policies and Target information from a
///     non-batched subscription object
/// </summary>
internal class NonBatchedPullRequestActor : PullRequestActor
{
    private readonly Lazy<Task<Subscription?>> _lazySubscription;
    private readonly NonBatchedPullRequestActorId _id;
    private readonly BuildAssetRegistryContext _context;
    private readonly IPullRequestPolicyFailureNotifier _pullRequestPolicyFailureNotifier;

    /// <param name="id">
    ///     The actor id for this actor.
    ///     If it is a <see cref="Guid" /> actor id, then it is required to be the id of a non-batched subscription in the
    ///     database
    ///     If it is a <see cref="string" /> actor id, then it MUST be an actor id created with
    ///     <see cref="PullRequestActorId.Create(string, string)" /> for use with all subscriptions targeting the specified
    ///     repository and branch.
    /// </param>
    public NonBatchedPullRequestActor(
        NonBatchedPullRequestActorId id,
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
        ILogger<NonBatchedPullRequestActor> logger)
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
        _lazySubscription = new Lazy<Task<Subscription?>>(RetrieveSubscription);
        _id = id;
        _context = context;
        _pullRequestPolicyFailureNotifier = pullRequestPolicyFailureNotifier;
    }

    public Guid SubscriptionId => _id.SubscriptionId;

    private async Task<Subscription?> RetrieveSubscription()
    {
        Subscription? subscription = await _context.Subscriptions.FindAsync(SubscriptionId);
        if (subscription == null)
        {
            await _pullRequestCheckState.UnsetReminderAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();
            await _pullRequestState.RemoveStateAsync();

            return null;
        }

        return subscription;
    }

    private Task<Subscription?> GetSubscription()
    {
        return _lazySubscription.Value;
    }

    protected override async Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        await _pullRequestPolicyFailureNotifier.TagSourceRepositoryGitHubContactsAsync(pr);
    }

    protected override async Task<(string repository, string branch)> GetTargetAsync()
    {
        Subscription subscription = await GetSubscription()
            ?? throw new SubscriptionException($"Subscription '{SubscriptionId}' was not found...");
        return (subscription.TargetRepository, subscription.TargetBranch);
    }

    protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
    {
        Subscription? subscription = await GetSubscription();
        return subscription?.PolicyObject?.MergePolicies ?? [];
    }

    public override async Task<(InProgressPullRequest? pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync()
    {
        Subscription? subscription = await GetSubscription();
        if (subscription == null)
        {
            return (null, false);
        }

        return await base.SynchronizeInProgressPullRequestAsync();
    }
}
