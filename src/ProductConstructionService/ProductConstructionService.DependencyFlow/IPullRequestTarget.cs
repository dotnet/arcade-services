// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Resolves target repository, branch, merge policies, and other subscription/target-specific
///     information needed by <see cref="PullRequestUpdater"/>.
///     Different implementations handle batched (repository-level) vs subscription-level targets.
/// </summary>
internal interface IPullRequestTarget
{
    /// <summary>
    ///     Returns the target repository and branch for the pull request.
    /// </summary>
    Task<(string Repository, string Branch)> GetTargetAsync();

    /// <summary>
    ///     Returns the merge policy definitions applicable to this target.
    /// </summary>
    Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitionsAsync();

    /// <summary>
    ///     Tags the source repository's GitHub contacts when merge policies fail.
    /// </summary>
    Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr);

    /// <summary>
    ///     Checks whether the underlying target (e.g. subscription) still exists.
    ///     Returns false when the subscription has been deleted, signalling the caller to clean up state.
    /// </summary>
    Task<bool> ShouldContinueProcessingAsync();

    string UpdaterId { get; }
}
