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
internal class BatchedPullRequestTarget : IPullRequestTarget
{
    private readonly BatchedPullRequestUpdaterId _id;
    private readonly BuildAssetRegistryContext _context;

    public string UpdaterId => _id.Id;

    public BatchedPullRequestTarget(
        BatchedPullRequestUpdaterId id,
        BuildAssetRegistryContext context)
    {
        _id = id;
        _context = context;
    }

    public Task<(string Repository, string Branch)> GetTargetAsync()
    {
        return Task.FromResult((_id.Repository, _id.Branch));
    }

    public async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitionsAsync()
    {
        RepositoryBranch? repositoryBranch = await _context.RepositoryBranches.FindAsync(_id.Repository, _id.Branch);
        return repositoryBranch?.PolicyObject?.MergePolicies ?? [];
    }

    // For batched subscriptions we don't know which source repo to tag
    public Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        return Task.CompletedTask;
    }

    // For bathed subscriptions we don't know which subscriptions are actually a part of the PR,
    // so we can't tell if all of them have been deleted
    public Task<bool> ShouldContinueProcessingAsync()
    {
        return Task.FromResult(true);
    }
}
