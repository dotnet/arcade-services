// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using ProductConstructionService.Client;
using SubscriptionActorService.StateModel;

namespace SubscriptionActorService;

/// <summary>
///     A <see cref="PullRequestActorImplementation" /> that reads its Merge Policies and Target information from a
///     non-batched subscription object
/// </summary>
internal class NonBatchedPullRequestActorImplementation : PullRequestActorImplementation
{
    private readonly Lazy<Task<Subscription>> _lazySubscription;
    private readonly ActorId _id;
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
    public NonBatchedPullRequestActorImplementation(
        ActorId id,
        IReminderManager reminders,
        IActorStateManager stateManager,
        IMergePolicyEvaluator mergePolicyEvaluator,
        ICoherencyUpdateResolver updateResolver,
        BuildAssetRegistryContext context,
        IRemoteFactory darcFactory,
        IProductConstructionServiceApi pcsClient,
        IPullRequestBuilder pullRequestBuilder,
        ILoggerFactory loggerFactory,
        IActionRunner actionRunner,
        IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory,
        IPullRequestPolicyFailureNotifier pullRequestPolicyFailureNotifier)
        : base(
            reminders,
            stateManager,
            mergePolicyEvaluator,
            updateResolver,
            context,
            darcFactory,
            pcsClient,
            pullRequestBuilder,
            loggerFactory,
            actionRunner,
            subscriptionActorFactory)
    {
        _lazySubscription = new Lazy<Task<Subscription>>(RetrieveSubscription);
        _id = id;
        _context = context;
        _pullRequestPolicyFailureNotifier = pullRequestPolicyFailureNotifier;
    }

    public Guid SubscriptionId => _id.GetGuidId();

    private async Task<Subscription> RetrieveSubscription()
    {
        Subscription subscription = await _context.Subscriptions.FindAsync(SubscriptionId);
        if (subscription == null)
        {
            await _pullRequestCheckState.UnsetReminderAsync();
            await _pullRequestUpdateState.UnsetReminderAsync();
            await _pullRequestState.RemoveStateAsync();

            throw new SubscriptionException($"Subscription '{SubscriptionId}' was not found...");
        }

        return subscription;
    }

    private Task<Subscription> GetSubscription()
    {
        return _lazySubscription.Value;
    }
    protected override async Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        await _pullRequestPolicyFailureNotifier.TagSourceRepositoryGitHubContactsAsync(pr);
    }

    protected override async Task<(string repository, string branch)> GetTargetAsync()
    {
        Subscription subscription = await GetSubscription();
        return (subscription.TargetRepository, subscription.TargetBranch);
    }

    protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
    {
        Subscription subscription = await GetSubscription();
        return (IReadOnlyList<MergePolicyDefinition>) subscription.PolicyObject.MergePolicies ??
               Array.Empty<MergePolicyDefinition>();
    }

    public override async Task<(InProgressPullRequest pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync()
    {
        Subscription subscription = await GetSubscription();
        if (subscription == null)
        {
            return (null, false);
        }

        return await base.SynchronizeInProgressPullRequestAsync();
    }
}
