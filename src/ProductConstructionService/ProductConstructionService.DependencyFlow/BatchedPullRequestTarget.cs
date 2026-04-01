// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Pull request target for batched updaters.
///     Target repository and branch come from the <see cref="BatchedPullRequestUpdaterId"/>;
///     merge policies come from the <see cref="RepositoryBranch"/> entity in the database.
/// </summary>
internal class BatchedPullRequestTarget : BatchedPullRequestUpdaterId, IPullRequestTarget
{
    private readonly BuildAssetRegistryContext _context;

    public string UpdaterId => Id;

    public BatchedPullRequestTarget(
        BatchedPullRequestUpdaterId id,
        BuildAssetRegistryContext context)
        : base(id.Repository, id.Branch, id.IsCodeFlow)
    {
        _context = context;
    }

    public Task<(string Repository, string Branch)> GetTargetAsync()
    {
        return Task.FromResult((Repository, Branch));
    }

    public async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitionsAsync()
    {
        RepositoryBranch? repositoryBranch = await _context.RepositoryBranches.FindAsync(Repository, Branch);
        return repositoryBranch?.PolicyObject?.MergePolicies ?? [];
    }

    // For batced subscriptions we don't know which source repo to tag
    public Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        return Task.CompletedTask;
    }

    public Task<bool> ShouldContinueProcessingAsync()
    {
        return Task.FromResult(true);
    }
}
