// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.PullRequestUpdaters;

internal class NonBatchedDependencyPullRequestUpdater : DependencyPullRequestUpdater
{
    private readonly Lazy<Task<Subscription?>> _lazySubscription;
    private readonly NonBatchedPullRequestUpdaterId _id;
    private readonly BuildAssetRegistryContext _context;
    private readonly ILogger<NonBatchedDependencyPullRequestUpdater> _logger;
    private readonly ICommentCollector _commentCollector;
    private readonly IPullRequestCommentBuilder _commentBuilder;

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
        ICommentCollector commentCollector,
        IPullRequestCommenter pullRequestCommenter,
        IPullRequestCommentBuilder commentBuilder,
        IFeatureFlagService featureFlagService,
        ILogger<NonBatchedDependencyPullRequestUpdater> logger)
        : base(
            id,
            mergePolicyEvaluator,
            context,
            remoteFactory,
            updaterFactory,
            coherencyUpdateResolver,
            pullRequestBuilder,
            cacheFactory,
            reminderManagerFactory,
            sqlClient,
            logger,
            pullRequestCommenter,
            featureFlagService)
    {
        _lazySubscription = new Lazy<Task<Subscription?>>(RetrieveSubscription);
        _id = id;
        _context = context;
        _logger = logger;
        _commentCollector = commentCollector;
        _commentBuilder = commentBuilder;
    }

    private async Task<Subscription?> RetrieveSubscription()
    {
        Subscription? subscription = await _context.Subscriptions.FindAsync(_id.SubscriptionId);

        // This can mainly happen during E2E tests where we delete a subscription
        // while some PRs have just been closed and there's a reminder on those still
        if (subscription == null)
        {
            _logger.LogWarning(
                "Failed to find a subscription {subscriptionId}. " +
                "Possibly it was deleted while an existing PR is still tracked. Untracking PR...",
                _id.SubscriptionId);

            await ClearAllStateAsync(clearPendingUpdates: true);
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
        var comment = await _commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);
        if (!string.IsNullOrEmpty(comment))
        {
            _commentCollector.AddComment(comment, CommentType.Warning);
        }
    }

    protected override async Task<(string repository, string branch)> GetTargetAsync()
    {
        Subscription subscription = await GetSubscription()
            ?? throw new SubscriptionException($"Subscription '{_id.SubscriptionId}' was not found...");
        return (subscription.TargetRepository, subscription.TargetBranch);
    }

    protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
    {
        Subscription? subscription = await GetSubscription();
        return subscription?.PolicyObject?.MergePolicies ?? [];
    }
}
