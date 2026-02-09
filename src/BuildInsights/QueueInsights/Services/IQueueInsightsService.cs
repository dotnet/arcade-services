// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace QueueInsights.Services;

/// <summary>
///     Provides insight and visibility into the current state of Helix queues by posting a Check Run.
/// </summary>
public interface IQueueInsightsService
{
    /// <summary>
    ///     Creates the queue insights for a specific repo and its commit hash.
    /// </summary>
    /// <param name="repo">The repository to create the queue insights for.</param>
    /// <param name="commitHash">The SHA hash of the git commit.</param>
    /// <param name="pullRequest">The pull request number.</param>
    /// <param name="pipelineIds">The IDs of pipelines the PR uses.</param>
    /// <param name="targetBranch">The target branch the PR is merging into.</param>
    /// <param name="criticalIssues"><c>true</c> if there are any critical infrastructure issues.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id of the new check run created in the GitHub pull request.</returns>
    public Task<long> CreateQueueInsightsAsync(string repo, string commitHash, string pullRequest,
        IImmutableSet<int> pipelineIds, string targetBranch, bool criticalIssues, CancellationToken cancellationToken);
}
