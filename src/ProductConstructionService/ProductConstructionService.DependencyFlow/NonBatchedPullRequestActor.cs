// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

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

    public NonBatchedPullRequestActor(
        NonBatchedPullRequestActorId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IActorFactory actorFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<NonBatchedPullRequestActor> logger)
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
            await _pullRequestCheckReminders.UnsetReminderAsync();
            await _pullRequestUpdateReminders.UnsetReminderAsync();
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

    public override async Task<bool> SynchronizeInProgressPullRequestAsync(
        InProgressPullRequest pullRequestCheck)
    {
        Subscription? subscription = await GetSubscription();
        if (subscription == null)
        {
            return false;
        }

        return await base.SynchronizeInProgressPullRequestAsync(pullRequestCheck);
    }
}
