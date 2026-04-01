// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Pull request target for non-batched (subscription-based) updaters.
///     All information is loaded lazily from the <see cref="Subscription"/> entity.
///     Handles the case where a subscription has been deleted while a PR is still being tracked.
/// </summary>
internal class NonBatchedPullRequestTarget : NonBatchedPullRequestUpdaterId, IPullRequestTarget
{
    private readonly BuildAssetRegistryContext _context;
    private readonly ICommentCollector _commentCollector;
    private readonly IPullRequestCommentBuilder _commentBuilder;
    private readonly ILogger<NonBatchedPullRequestTarget> _logger;

    private readonly Lazy<Task<Subscription?>> _subscription;

    public string UpdaterId => Id;

    public NonBatchedPullRequestTarget(
        NonBatchedPullRequestUpdaterId id,
        BuildAssetRegistryContext context,
        ICommentCollector commentCollector,
        IPullRequestCommentBuilder commentBuilder,
        ILogger<NonBatchedPullRequestTarget> logger)
        : base(id.SubscriptionId, id.IsCodeFlow)
    {
        _context = context;
        _commentCollector = commentCollector;
        _commentBuilder = commentBuilder;
        _logger = logger;
        _subscription = new Lazy<Task<Subscription?>>(RetrieveSubscriptionAsync);
    }

    public async Task<(string Repository, string Branch)> GetTargetAsync()
    {
        Subscription subscription = await GetSubscriptionAsync()
            ?? throw new SubscriptionException($"Subscription '{SubscriptionId}' was not found...");
        return (subscription.TargetRepository, subscription.TargetBranch);
    }

    public async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitionsAsync()
    {
        Subscription? subscription = await GetSubscriptionAsync();
        return subscription?.PolicyObject?.MergePolicies ?? [];
    }

    public async Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        var comment = await _commentBuilder.BuildTagSourceRepositoryGitHubContactsCommentAsync(pr);
        if (!string.IsNullOrEmpty(comment))
        {
            _commentCollector.AddComment(comment, CommentType.Warning);
        }
    }

    public async Task<bool> ShouldContinueProcessingAsync()
    {
        return await GetSubscriptionAsync() != null;
    }

    private Task<Subscription?> GetSubscriptionAsync()
    {
        return _subscription.Value;
    }

    private async Task<Subscription?> RetrieveSubscriptionAsync()
    {
        Subscription? subscription = await _context.Subscriptions.FindAsync(SubscriptionId);

        if (subscription == null)
        {
            _logger.LogInformation(
                "Failed to find subscription {subscriptionId}. " +
                "Possibly it was deleted while an existing PR is still tracked",
                SubscriptionId);
        }

        return subscription;
    }
}
