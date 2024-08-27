// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using ProductConstructionService.DependencyFlow.WorkItems;

using Asset = Maestro.Contracts.Asset;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A service fabric actor implementation that is responsible for creating and updating pull requests for dependency
///     updates.
/// </summary>
internal abstract class PullRequestActor : IPullRequestActor
{
    private readonly PullRequestActorId _id;

    /// <summary>
    ///     Creates a new PullRequestActor
    /// </summary>
    public PullRequestActor(
        PullRequestActorId id)
    {
        _id = id;
    }

    protected abstract Task<(string repository, string branch)> GetTargetAsync();

    protected abstract Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions();

    /// <summary>
    ///     Process any pending pull request updates.
    /// </summary>
    /// <returns>
    ///     True if updates have been applied; <see langword="false" /> otherwise.
    /// </returns>
    public Task<bool> ProcessPendingUpdatesAsync(SubscriptionUpdateWorkItem update)
    {
        throw new NotImplementedException();
    }

    protected virtual Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        // Only do actual stuff in the non-batched implementation
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Synchronizes an in progress pull request.
    ///     This will update current state if the pull request has been manually closed or merged.
    ///     This will evaluate merge policies on an in progress pull request and merge the pull request if policies allow.
    /// </summary>
    /// <returns>
    ///     A <see cref="ValueTuple{InProgressPullRequest, bool}" /> containing:
    ///     The current open pull request if one exists, and
    ///     <see langword="true" /> if the open pull request can be updated; <see langword="false" /> otherwise.
    /// </returns>
    public virtual Task<(InProgressPullRequest? pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync(InProgressPullRequest pullRequestCheck)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Applies or queues asset updates for the target repository and branch from the given build and list of assets.
    /// </summary>
    /// <param name="subscriptionId">The id of the subscription the update comes from</param>
    /// <param name="buildId">The build that the updated assets came from</param>
    /// <param name="sourceSha">The commit hash that built the assets</param>
    /// <param name="assets">The list of assets</param>
    /// <remarks>
    ///     This function will queue updates if there is a pull request and it is currently not-updateable.
    ///     A pull request is considered "not-updateable" based on merge policies.
    ///     If at least one merge policy calls <see cref="IMergePolicyEvaluationContext.Pending" /> and
    ///     no merge policy calls <see cref="IMergePolicyEvaluationContext.Fail" /> then the pull request is considered
    ///     not-updateable.
    ///
    ///     PRs are marked as non-updateable so that we can allow pull request checks to complete on a PR prior
    ///     to pushing additional commits.
    /// </remarks>
    public Task<bool> UpdateAssetsAsync(
        Guid subscriptionId,
        SubscriptionType type,
        int buildId,
        string sourceRepo,
        string sourceSha,
        List<Asset> assets)
    {
        throw new NotImplementedException();
    }
}
