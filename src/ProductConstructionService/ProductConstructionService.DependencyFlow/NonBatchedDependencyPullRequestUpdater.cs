// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Cache;
using Maestro.Common.Telemetry;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow;

internal class NonBatchedDependencyPullRequestUpdater : DependencyPullRequestUpdater
{
    private readonly IPullRequestCommentBuilder _commentBuilder;
    private readonly NonBatchedPullRequestUpdaterId _id;

    private readonly Lazy<Task<Subscription?>> _subscription;

    public NonBatchedDependencyPullRequestUpdater(
        NonBatchedPullRequestUpdaterId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        ISqlBarClient sqlClient,
        ITelemetryRecorder telemetryRecorder,
        ILogger<DependencyPullRequestUpdater> logger,
        ICommentCollector commentCollector,
        IPullRequestCommenter pullRequestCommenter,
        IPullRequestCommentBuilder commentBuilder)
        : base(id, mergePolicyEvaluator, context, remoteFactory, updaterFactory, coherencyUpdateResolver, pullRequestBuilder, cacheFactory, reminderManagerFactory, sqlClient, telemetryRecorder, logger, commentCollector, pullRequestCommenter)
    {
        _id = id;
        _subscription = new Lazy<Task<Subscription?>>(RetrieveSubscription);
        _commentBuilder = commentBuilder;
    }

    public Guid SubscriptionId => _id.SubscriptionId;

    private async Task<Subscription?> RetrieveSubscription()
    {
        Subscription? subscription = await _context.Subscriptions.FindAsync(SubscriptionId);

        // This can mainly happen during E2E tests where we delete a subscription
        // while some PRs have just been closed and there's a reminder on those still
        if (subscription == null)
        {
            _logger.LogInformation(
                "Failed to find a subscription {SubscriptionId}. " +
                "Possibly it was deleted while an existing PR is still tracked. Untracking PR...",
                SubscriptionId);

            await _pullRequestState.TryDeleteAsync();
            await _pullRequestCheckReminders.UnsetReminderAsync(isCodeFlow: false);
            await _pullRequestUpdateReminders.UnsetReminderAsync(isCodeFlow: false);
            return null;
        }

        return subscription;
    }

    private Task<Subscription?> GetSubscription()
    {
        return _subscription.Value;
    }

    protected override async Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        var comment = await _commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);
        if (!string.IsNullOrEmpty(comment))
        {
            _commentCollector.AddComment(comment, CommentType.Warning);
        }
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

    protected override async Task<bool> CheckInProgressPullRequestAsync(
        InProgressPullRequest pullRequestCheck,
        bool isCodeFlow)
    {
        Subscription? subscription = await GetSubscription();
        if (subscription == null)
        {
            // If the subscription was deleted during tests (a frequent occurrence when we delete subscriptions at the end),
            // we don't want to report this as a failure. For real PRs, it might be good to learn about this. 
            return pullRequestCheck.Url?.Contains("maestro-auth-test") ?? false;
        }

        return await base.CheckInProgressPullRequestAsync(pullRequestCheck, isCodeFlow);
    }
}
